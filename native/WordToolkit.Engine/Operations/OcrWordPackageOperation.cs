using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Extensions;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public sealed class OcrWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly WordOcrGraphBuilder _graphBuilder;
    private readonly WordToolkitExtensionRegistry _extensions;

    public OcrWordPackageOperation(
        WordToolkitExtensionRegistry extensions,
        OpcPackageLimits? packageLimits = null,
        WordOcrGraphOptions? ocrOptions = null,
        WordFigureCaptionGraphOptions? figureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(extensions);
        _extensions = extensions;
        _reader = new OpcPackageReader(packageLimits);
        _graphBuilder = new WordOcrGraphBuilder(ocrOptions, figureOptions);
    }

    public OcrCandidateInspectionResult Inspect(
        OcrCandidateInspectionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw Invalid("OCR candidate inspection request is required");
        }
        Validate(request);
        var path = ResolvePath(request.LocalPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var package = _reader.Read(stream, cancellationToken);
            return InspectPackage(
                package,
                Path.GetFileName(path),
                request,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception, request.LocalPath);
        }
    }

    public OcrRecognitionResult Recognize(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw Invalid("OCR recognition request is required");
        }
        Validate(request);
        var path = ResolvePath(request.LocalPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFileHash = HashFile(path, cancellationToken);
            OpcPackageSnapshot package;
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            ))
            {
                package = _reader.Read(stream, cancellationToken);
            }
            if (!string.Equals(
                package.Fingerprint,
                request.ExpectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                throw new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The Word package changed after the OCR candidates were inspected"
                );
            }
            var graph = _graphBuilder.Build(package, cancellationToken);
            var capability = ResolveProviderCapability(request.ProviderCapabilityId);
            AuthorizePrivacy(capability.Capability, request.PrivacyMode);
            var selected = SelectCandidates(graph, request);
            var results = new List<OcrRecognitionItem>(selected.Count);
            foreach (var candidate in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!candidate.Eligible)
                {
                    throw new WordToolkitOperationException(
                        "OCR_CANDIDATE_INELIGIBLE",
                        "A selected OCR candidate is not an eligible verified raster image",
                        reason: candidate.RejectionCode
                    );
                }
                if (!package.Parts.TryGetValue(candidate.TargetPartUri, out var part))
                {
                    throw new WordToolkitOperationException(
                        "INVALID_WORD_PACKAGE",
                        "An OCR candidate lost its bound package part"
                    );
                }
                var providerRequest = new WordOcrProviderRequest(
                    part.Entry.Content,
                    candidate.DetectedContentType!,
                    candidate.Sha256,
                    request.Languages ?? ["eng"],
                    request.LayoutHint,
                    request.TimeoutMilliseconds,
                    request.ProviderOutputCharacters,
                    new WordOcrProviderConfiguration(
                        request.ProviderExecutablePath,
                        request.ProviderModelDirectory
                    )
                );
                WordOcrProviderResult providerResult;
                try
                {
                    providerResult = _extensions.Invoke<
                        IWordOcrProvider,
                        WordOcrProviderResult
                    >(
                        request.ProviderCapabilityId,
                        part.Entry.Content.Length,
                        (provider, token) => provider.Recognize(providerRequest, token),
                        MeasureProviderOutput,
                        cancellationToken
                    );
                }
                catch (WordToolkitExtensionException exception)
                {
                    throw new WordToolkitOperationException(
                        exception.Code,
                        exception.Message,
                        retryable: exception.Retryable,
                        innerException: exception
                    );
                }
                ValidateProviderResult(providerResult, providerRequest, capability.Capability);
                results.Add(Result(
                    candidate,
                    providerResult,
                    capability.Extension,
                    request
                ));
            }

            var reverifiedHash = HashFile(path, cancellationToken);
            if (!string.Equals(sourceFileHash, reverifiedHash, StringComparison.Ordinal))
            {
                throw new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The source Word package changed while OCR was running"
                );
            }
            var networkUsed = results.Any(item => item.Provenance.NetworkUsed);
            return new OcrRecognitionResult(
                OcrWordPackageContract.RecognizeContract,
                Path.GetFileName(path),
                package.Fingerprint,
                request.Detail,
                selected.Count,
                results.Count,
                results.Sum(item => item.LineCount),
                results.Sum(item => item.WordCount),
                results,
                new OcrRecognitionDisclosure(
                    SourceFingerprintVerified: true,
                    SourceFileHashReverified: true,
                    ImageBytesReturned: false,
                    TextReturned: results.Any(item => item.Text is not null
                        || item.Lines.Any(line => line.Text is not null
                            || line.Words.Any(word => word.Text is not null))),
                    GeometryReturned: results.Any(item => item.Lines.Count > 0),
                    RawProviderOutputReturned: false,
                    RawXmlReturned: false,
                    ExternalRelationshipsFollowed: false,
                    NetworkUsed: networkUsed,
                    MutationPerformed: false,
                    WordOpened: false,
                    DocumentContentIsUntrusted: true
                )
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception, request.LocalPath);
        }
    }

    private OcrCandidateInspectionResult InspectPackage(
        OpcPackageSnapshot package,
        string fileName,
        OcrCandidateInspectionRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.ExpectedPackageFingerprint is not null
            && !string.Equals(
                package.Fingerprint,
                request.ExpectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "The Word package changed after the inspected fingerprint was issued"
            );
        }
        var graph = _graphBuilder.Build(package, cancellationToken);
        var matched = graph.Candidates.Where(candidate =>
            request.CandidateId is null
            || string.Equals(candidate.Id, request.CandidateId, StringComparison.Ordinal)
        ).ToArray();
        var page = request.View == "candidates"
            ? matched.Skip(request.Offset).Take(request.MaxItems).ToArray()
            : Array.Empty<WordOcrCandidate>();
        var items = page.Select(candidate => new OcrCandidateInspectionItem(
            candidate.Id,
            candidate.DeclaredContentType,
            candidate.DetectedContentType,
            SnakeCase(candidate.MediaFamily),
            candidate.ByteLength,
            candidate.SignatureValid,
            candidate.Eligible,
            candidate.RejectionCode,
            candidate.FigureIds.Count,
            candidate.ResourceIds.Count,
            candidate.StoryKinds.Select(SnakeCase).ToArray(),
            request.IncludeHashes ? candidate.Sha256 : null,
            request.IncludeSource ? candidate.TargetPartUri : null
        )).ToArray();
        var issuePage = request.View == "issues"
            ? graph.Issues.Skip(request.Offset).Take(request.MaxItems).Select(issue =>
                new OcrCandidateInspectionIssue(
                    issue.Code,
                    SnakeCase(issue.Severity),
                    issue.Message,
                    issue.CandidateId,
                    request.IncludeSource ? issue.SourcePartUri : null
                )
            ).ToArray()
            : [];
        var nextOffset = request.View switch
        {
            "candidates" when request.Offset + page.Length < matched.Length =>
                request.Offset + page.Length,
            "issues" when request.Offset + issuePage.Length < graph.Issues.Count =>
                request.Offset + issuePage.Length,
            _ => (int?)null,
        };
        return new OcrCandidateInspectionResult(
            OcrWordPackageContract.InspectContract,
            fileName,
            package.Fingerprint,
            request.View,
            graph.Candidates.Count,
            graph.Candidates.Count(item => item.Eligible),
            graph.Candidates.Count(item => item.MediaFamily == WordOcrMediaFamily.Raster),
            graph.Candidates.Count(item => item.MediaFamily == WordOcrMediaFamily.Vector),
            graph.Candidates.Count(item => item.MediaFamily == WordOcrMediaFamily.Unknown),
            graph.Issues.Count,
            AnalysisExecutionComplete: true,
            CandidateCoverageComplete: graph.CandidateCoverageComplete,
            matched.Length,
            request.Offset,
            items.Length,
            nextOffset,
            items,
            issuePage.Length,
            graph.IssuesTruncated
                || (request.View == "issues"
                    && request.Offset + issuePage.Length < graph.Issues.Count),
            issuePage,
            new OcrCandidateInspectionDisclosure(
                ImageBytesReturned: false,
                ImageHashesReturned: request.IncludeHashes && items.Length > 0,
                SourceReturned: request.IncludeSource
                    && (items.Length > 0 || issuePage.Length > 0),
                ExternalRelationshipsFollowed: false,
                ProviderInvoked: false,
                NetworkUsed: false,
                MutationPerformed: false,
                WordOpened: false,
                DocumentContentIsUntrusted: true
            )
        );
    }

    private static IReadOnlyList<WordOcrCandidate> SelectCandidates(
        WordOcrGraph graph,
        OcrRecognitionRequest request
    )
    {
        WordOcrCandidate[] selected;
        if (request.SelectAllEligible)
        {
            selected = graph.Candidates.Where(item => item.Eligible).ToArray();
        }
        else
        {
            var result = new List<WordOcrCandidate>(request.CandidateIds.Count);
            foreach (var id in request.CandidateIds)
            {
                if (!graph.TryGetCandidate(id, out var candidate) || candidate is null)
                {
                    throw new WordToolkitOperationException(
                        "OCR_CANDIDATE_NOT_FOUND",
                        "A selected OCR candidate does not exist in the bound package"
                    );
                }
                result.Add(candidate);
            }
            selected = result.ToArray();
        }
        if (selected.Length == 0)
        {
            throw new WordToolkitOperationException(
                "OCR_CANDIDATE_NOT_FOUND",
                "No eligible OCR candidates matched the explicit selection"
            );
        }
        if (selected.Length > OcrWordPackageContract.MaximumSelectedCandidates)
        {
            throw new WordToolkitOperationException(
                "OCR_CANDIDATE_LIMIT",
                $"One OCR request cannot select more than {OcrWordPackageContract.MaximumSelectedCandidates} images"
            );
        }
        return selected;
    }

    private (WordToolkitExtensionDescriptor Extension, WordToolkitExtensionCapabilityDescriptor Capability)
        ResolveProviderCapability(string capabilityId)
    {
        foreach (var extension in _extensions.Extensions)
        {
            var capability = extension.Capabilities.FirstOrDefault(item =>
                string.Equals(item.CapabilityId, capabilityId, StringComparison.Ordinal)
            );
            if (capability is null)
            {
                continue;
            }
            if (capability.Kind != WordToolkitExtensionKind.OcrProvider
                || !string.Equals(
                    capability.InterfaceContract,
                    WordOcrProviderContract.InterfaceContract,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    capability.InterfaceVersion,
                    WordOcrProviderContract.InterfaceVersion,
                    StringComparison.Ordinal
                ))
            {
                throw new WordToolkitOperationException(
                    "EXTENSION_CONTRACT_MISMATCH",
                    "The selected capability is not a compatible OCR provider"
                );
            }
            if (!capability.ReturnsDocumentContent)
            {
                throw new WordToolkitOperationException(
                    "EXTENSION_CONTRACT_MISMATCH",
                    "An OCR provider must declare that it returns document content"
                );
            }
            return (extension.Extension, capability);
        }
        throw new WordToolkitOperationException(
            "EXTENSION_NOT_FOUND",
            "The selected OCR provider capability is not registered"
        );
    }

    private static void AuthorizePrivacy(
        WordToolkitExtensionCapabilityDescriptor capability,
        string privacyMode
    )
    {
        if (privacyMode == "local_only"
            && (capability.Permissions
                & (WordToolkitExtensionPermission.Network
                    | WordToolkitExtensionPermission.Credentials)) != 0)
        {
            throw new WordToolkitOperationException(
                "OCR_PRIVACY_POLICY_DENIED",
                "The selected OCR provider is not allowed by local-only privacy mode"
            );
        }
    }

    private static void ValidateProviderResult(
        WordOcrProviderResult result,
        WordOcrProviderRequest request,
        WordToolkitExtensionCapabilityDescriptor capability
    )
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ImageWidthPixels is < 1 or > 1_000_000
            || result.ImageHeightPixels is < 1 or > 1_000_000)
        {
            throw ProviderViolation("OCR provider returned invalid image dimensions.");
        }
        if (result.Text.Length > request.MaximumOutputCharacters
            || ContainsUnsafeText(result.Text)
            || result.Lines.Count > 20_000)
        {
            throw ProviderViolation("OCR provider returned unsafe or oversized text.");
        }
        var wordCount = 0;
        foreach (var line in result.Lines)
        {
            if (line.Text.Length > request.MaximumOutputCharacters
                || ContainsUnsafeText(line.Text)
                || !ValidBox(line.Bounds, result.ImageWidthPixels, result.ImageHeightPixels)
                || !ValidConfidence(line.Confidence))
            {
                throw ProviderViolation("OCR provider returned an invalid line.");
            }
            wordCount = checked(wordCount + line.Words.Count);
            if (wordCount > 100_000)
            {
                throw ProviderViolation("OCR provider returned too many words.");
            }
            foreach (var word in line.Words)
            {
                if (word.Text.Length is < 1 or > 8_192
                    || ContainsUnsafeText(word.Text)
                    || !ValidBox(word.Bounds, result.ImageWidthPixels, result.ImageHeightPixels)
                    || !ValidConfidence(word.Confidence))
                {
                    throw ProviderViolation("OCR provider returned an invalid word.");
                }
            }
        }
        if (!string.Equals(
            result.Text,
            string.Join('\n', result.Lines.Select(item => item.Text)),
            StringComparison.Ordinal
        ))
        {
            throw ProviderViolation("OCR provider text and line projections disagree.");
        }
        var provenance = result.Provenance;
        if (string.IsNullOrWhiteSpace(provenance.ProviderName)
            || provenance.ProviderName.Length > 128
            || string.IsNullOrWhiteSpace(provenance.ProviderVersion)
            || provenance.ProviderVersion.Length > 128
            || !IsCanonicalSha256(provenance.ProviderBinarySha256)
            || !IsCanonicalSha256(provenance.ModelSetSha256)
            || provenance.EffectiveLanguages.Count != request.Languages.Count
            || !provenance.EffectiveLanguages.SequenceEqual(
                request.Languages,
                StringComparer.Ordinal
            )
            || string.IsNullOrWhiteSpace(provenance.ConfidenceScale)
            || provenance.ConfidenceScale.Length > 128)
        {
            throw ProviderViolation("OCR provider provenance is incomplete or invalid.");
        }
        if (provenance.NetworkUsed
            && (capability.Permissions & WordToolkitExtensionPermission.Network) == 0)
        {
            throw ProviderViolation("OCR provider used undeclared network access.");
        }
        if (result.Warnings.Count > 32
            || result.Warnings.Any(item => item.Length is < 1 or > 128
                || ContainsUnsafeText(item)))
        {
            throw ProviderViolation("OCR provider warnings are invalid.");
        }
    }

    private static OcrRecognitionItem Result(
        WordOcrCandidate candidate,
        WordOcrProviderResult provider,
        WordToolkitExtensionDescriptor extension,
        OcrRecognitionRequest request
    )
    {
        var words = provider.Lines.SelectMany(item => item.Words).ToArray();
        var confidenceValues = words
            .Where(item => item.Confidence.HasValue)
            .Select(item => item.Confidence!.Value)
            .ToArray();
        var mean = confidenceValues.Length == 0 ? (double?)null : confidenceValues.Average();
        var meets = request.MinimumMeanConfidence is null
            || (mean.HasValue && mean.Value >= request.MinimumMeanConfidence.Value);
        if (!meets)
        {
            throw new WordToolkitOperationException(
                "OCR_CONFIDENCE_TOO_LOW",
                "OCR mean confidence did not meet the requested minimum"
            );
        }
        var textTruncated = false;
        var returnedText = request.IncludeText
            ? Truncate(
                provider.Text,
                request.MaxReturnedTextCharacters,
                out textTruncated
            )
            : null;
        var providerLines = request.Detail == "summary"
            ? []
            : provider.Lines.Take(request.MaxReturnedLines).ToArray();
        var lines = providerLines.Select(line =>
        {
            var returnedWords = request.Detail == "words"
                ? line.Words.Take(request.MaxReturnedWordsPerLine).ToArray()
                : [];
            var lineTextTruncated = false;
            var returnedLineText = request.IncludeText
                ? Truncate(
                    line.Text,
                    Math.Min(request.MaxReturnedTextCharacters, 8_192),
                    out lineTextTruncated
                )
                : null;
            return new OcrRecognitionLine(
                returnedLineText,
                line.Text.Length,
                lineTextTruncated,
                line.Confidence,
                Box(line.Bounds),
                returnedWords.Select(word => new OcrRecognitionWord(
                        request.IncludeText ? word.Text : null,
                        word.Text.Length,
                        word.Confidence,
                        Box(word.Bounds)
                    )).ToArray(),
                returnedWords.Length,
                request.Detail == "words" && returnedWords.Length < line.Words.Count
            );
        }).ToArray();
        var textHash = Sha256(Encoding.UTF8.GetBytes(provider.Text));
        var resultId = StableResultId(
            candidate.Id,
            extension.ExtensionId,
            extension.ExtensionVersion,
            request.ProviderCapabilityId,
            SnakeCase(request.LayoutHint),
            request.PrivacyMode,
            provider.Provenance.ProviderBinarySha256,
            provider.Provenance.ModelSetSha256,
            textHash
        );
        return new OcrRecognitionItem(
            resultId,
            candidate.Id,
            request.IncludeHashes ? candidate.Sha256 : null,
            provider.ImageWidthPixels,
            provider.ImageHeightPixels,
            provider.Text.Length,
            returnedText,
            textTruncated,
            request.IncludeHashes ? textHash : null,
            provider.Lines.Count,
            words.Length,
            lines.Length,
            request.Detail != "summary" && lines.Length < provider.Lines.Count,
            new OcrConfidenceSummary(
                "normalized_0_to_1",
                confidenceValues.Length == 0 ? null : confidenceValues.Min(),
                mean,
                confidenceValues.Length == 0 ? null : confidenceValues.Max(),
                request.MinimumMeanConfidence,
                meets
            ),
            provider.Warnings,
            lines,
            new OcrRecognitionProvenance(
                request.ProviderCapabilityId,
                extension.ExtensionId,
                extension.ExtensionVersion,
                provider.Provenance.ProviderName,
                provider.Provenance.ProviderVersion,
                provider.Provenance.ProviderBinarySha256,
                provider.Provenance.ModelSetSha256,
                provider.Provenance.EffectiveLanguages,
                SnakeCase(request.LayoutHint),
                request.PrivacyMode,
                provider.Provenance.NetworkUsed,
                provider.Provenance.DeterministicForBoundInputs
            )
        );
    }

    private static OcrPixelBox Box(WordOcrPixelBox box) => new(
        box.Left,
        box.Top,
        box.Width,
        box.Height
    );

    private static bool ValidBox(WordOcrPixelBox box, int width, int height) =>
        box.Left >= 0 && box.Top >= 0 && box.Width > 0 && box.Height > 0
        && (long)box.Left + box.Width <= width
        && (long)box.Top + box.Height <= height;

    private static bool ValidConfidence(double? confidence) => confidence is null
        || (double.IsFinite(confidence.Value)
            && confidence.Value is >= 0 and <= 1);

    private static long MeasureProviderOutput(WordOcrProviderResult result)
    {
        long characters = result.Text.Length;
        characters = checked(characters + result.Provenance.ProviderName.Length);
        characters = checked(characters + result.Provenance.ProviderVersion.Length);
        characters = checked(characters + result.Provenance.ProviderBinarySha256.Length);
        characters = checked(characters + result.Provenance.ModelSetSha256.Length);
        foreach (var line in result.Lines)
        {
            characters = checked(characters + line.Text.Length + 64);
            foreach (var word in line.Words)
            {
                characters = checked(characters + word.Text.Length + 64);
            }
        }
        foreach (var warning in result.Warnings)
        {
            characters = checked(characters + warning.Length);
        }
        return checked(characters * sizeof(char));
    }

    private static void Validate(OcrCandidateInspectionRequest request)
    {
        ValidatePath(request.LocalPath);
        if (request.View is not "summary" and not "candidates" and not "issues")
        {
            throw Invalid("view must be summary, candidates, or issues");
        }
        if (request.ExpectedPackageFingerprint is not null
            && !IsSha256(request.ExpectedPackageFingerprint))
        {
            throw Invalid("expected_package_fingerprint must be exactly 64 hexadecimal characters");
        }
        if (request.CandidateId is not null && !IsCandidateId(request.CandidateId))
        {
            throw Invalid("candidate_id is invalid");
        }
        if (request.Offset < 0)
        {
            throw Invalid("offset must be non-negative");
        }
        if (request.Offset > 0 && request.ExpectedPackageFingerprint is null)
        {
            throw Invalid("expected_package_fingerprint is required when offset is positive");
        }
        if (request.MaxItems is < 1 or > OcrWordPackageContract.MaximumMaxItems)
        {
            throw Invalid(
                $"max_items must be between 1 and {OcrWordPackageContract.MaximumMaxItems}"
            );
        }
    }

    private static void Validate(OcrRecognitionRequest request)
    {
        ValidatePath(request.LocalPath);
        if (!IsSha256(request.ExpectedPackageFingerprint))
        {
            throw Invalid("expected_package_fingerprint must be exactly 64 hexadecimal characters");
        }
        if (request.CandidateIds is null)
        {
            throw Invalid("candidate_ids cannot be null");
        }
        var hasIds = request.CandidateIds.Count > 0;
        if (hasIds == request.SelectAllEligible)
        {
            throw Invalid(
                "Select exactly one of candidate_ids or select_all_eligible=true"
            );
        }
        if (request.CandidateIds.Count > OcrWordPackageContract.MaximumSelectedCandidates
            || request.CandidateIds.Any(id => !IsCandidateId(id))
            || request.CandidateIds.Distinct(StringComparer.Ordinal).Count()
                != request.CandidateIds.Count)
        {
            throw Invalid("candidate_ids must contain unique bounded OCR candidate IDs");
        }
        if (string.IsNullOrWhiteSpace(request.ProviderCapabilityId)
            || request.ProviderCapabilityId.Length > 128)
        {
            throw Invalid("provider_capability_id is invalid");
        }
        if (request.PrivacyMode is not "local_only" and not "network_allowed")
        {
            throw Invalid("privacy_mode must be local_only or network_allowed");
        }
        var languages = request.Languages ?? ["eng"];
        if (languages.Count is < 1 or > 4
            || languages.Any(language => language is null
                || language.Length is < 2 or > 32
                || language.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '_' and not '-'))
            || languages.Distinct(StringComparer.Ordinal).Count() != languages.Count)
        {
            throw Invalid("languages must contain one to four unique safe identifiers");
        }
        if ((request.ProviderExecutablePath is not null
                && (string.IsNullOrWhiteSpace(request.ProviderExecutablePath)
                    || request.ProviderExecutablePath.Length
                        > OcrWordPackageContract.MaximumLocalPathCharacters))
            || (request.ProviderModelDirectory is not null
                && (string.IsNullOrWhiteSpace(request.ProviderModelDirectory)
                    || request.ProviderModelDirectory.Length
                        > OcrWordPackageContract.MaximumLocalPathCharacters)))
        {
            throw Invalid("OCR provider paths must be non-empty and bounded when supplied");
        }
        if (request.TimeoutMilliseconds is < 1_000
            or > OcrWordPackageContract.MaximumTimeoutMilliseconds)
        {
            throw Invalid(
                $"timeout_milliseconds must be between 1000 and {OcrWordPackageContract.MaximumTimeoutMilliseconds}"
            );
        }
        if (request.ProviderOutputCharacters is < 1_024
            or > OcrWordPackageContract.MaximumProviderOutputCharacters)
        {
            throw Invalid(
                $"provider_output_characters must be between 1024 and {OcrWordPackageContract.MaximumProviderOutputCharacters}"
            );
        }
        if (request.Detail is not "summary" and not "lines" and not "words")
        {
            throw Invalid("detail must be summary, lines, or words");
        }
        if (request.MaxReturnedTextCharacters is < 1
            or > OcrWordPackageContract.MaximumReturnedTextCharacters)
        {
            throw Invalid(
                $"max_returned_text_characters must be between 1 and {OcrWordPackageContract.MaximumReturnedTextCharacters}"
            );
        }
        if (request.MaxReturnedLines is < 1
            or > OcrWordPackageContract.MaximumReturnedLines)
        {
            throw Invalid(
                $"max_returned_lines must be between 1 and {OcrWordPackageContract.MaximumReturnedLines}"
            );
        }
        if (request.MaxReturnedWordsPerLine is < 1
            or > OcrWordPackageContract.MaximumReturnedWordsPerLine)
        {
            throw Invalid(
                $"max_returned_words_per_line must be between 1 and {OcrWordPackageContract.MaximumReturnedWordsPerLine}"
            );
        }
        if (request.MinimumMeanConfidence is < 0 or > 1)
        {
            throw Invalid("minimum_mean_confidence must be between 0 and 1");
        }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > OcrWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(path))
        {
            throw Invalid("local_path must use .docx, .docm, .dotx, or .dotm");
        }
    }

    private static string ResolvePath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    "The requested Word package does not exist"
                );
            }
            return fullPath;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw Invalid("local_path is invalid", exception);
        }
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Truncate(string value, int maximum, out bool truncated)
    {
        truncated = value.Length > maximum;
        if (!truncated)
        {
            return value;
        }
        var length = maximum;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }
        return value[..length];
    }

    private static string StableResultId(
        string candidateId,
        string extensionId,
        string extensionVersion,
        string capabilityId,
        string layoutHint,
        string privacyMode,
        string providerHash,
        string modelHash,
        string textHash
    )
    {
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{candidateId.Length}:{candidateId}{extensionId.Length}:{extensionId}{extensionVersion.Length}:{extensionVersion}{capabilityId.Length}:{capabilityId}{layoutHint.Length}:{layoutHint}{privacyMode.Length}:{privacyMode}{providerHash}{modelHash}{textHash}"
        );
        return "wocrres_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
        )[..24].ToLowerInvariant();
    }

    private static string Sha256(ReadOnlySpan<byte> value) => Convert.ToHexString(
        SHA256.HashData(value)
    ).ToLowerInvariant();

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(char.IsAsciiHexDigit);

    private static bool IsCanonicalSha256(string? value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character)
            || character is >= 'a' and <= 'f');

    private static bool IsCandidateId(string? value) => value is { Length: 29 }
        && value.StartsWith("wocr_", StringComparison.Ordinal)
        && value.AsSpan(5).ToArray().All(character => char.IsAsciiDigit(character)
            || character is >= 'a' and <= 'f');

    private static bool ContainsUnsafeText(string value) => value.Any(character =>
        character == '\0'
        || (char.IsControl(character) && character is not '\t' and not '\r' and not '\n')
    );

    private static string SnakeCase<TEnum>(TEnum value)
        where TEnum : struct, Enum => System.Text.Json.JsonNamingPolicy.SnakeCaseLower
            .ConvertName(value.ToString());

    private static WordToolkitOperationException ProviderViolation(string message) => new(
        "OCR_PROVIDER_CONTRACT_VIOLATION",
        message
    );

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath
    ) => exception switch
    {
        WordOcrLimitException => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            "The Word package exceeded an OCR projection limit",
            innerException: exception
        ),
        WordOcrException => new WordToolkitOperationException(
            "INVALID_WORD_PACKAGE",
            "The Word package could not be projected into OCR candidates",
            innerException: exception
        ),
        OpcPackageLimitException => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            "The Word package exceeded a package safety limit",
            innerException: exception
        ),
        InvalidDataException => new WordToolkitOperationException(
            "INVALID_WORD_PACKAGE",
            "The requested file is not a valid supported Word package",
            innerException: exception
        ),
        UnauthorizedAccessException => new WordToolkitOperationException(
            "ACCESS_DENIED",
            "The Word package cannot be read",
            innerException: exception
        ),
        IOException => new WordToolkitOperationException(
            "IO_ERROR",
            "The Word package could not be read",
            retryable: true,
            innerException: exception
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            localPath is null
                ? "The OCR package operation failed"
                : "The OCR package operation failed without exposing local path details",
            innerException: exception
        ),
    };
}
