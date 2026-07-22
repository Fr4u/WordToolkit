using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordLintRepairKind
{
    SetDocumentTitle,
}

public sealed record WordLintRepairPlannerOptions
{
    public static WordLintRepairPlannerOptions Default { get; } = new();

    public int MaxDocumentTitleCharacters { get; init; } = 255;

    public int MaxSourceXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxDocumentTitleCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDocumentTitleCharacters));
        }
        if (MaxSourceXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSourceXmlPartBytes));
        }
    }
}

public sealed record WordLintRepairPartChange(
    string PartUri,
    string EntryName,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed record WordLintRepairValidation(
    bool BasePackageStructurallyValid,
    bool CandidatePackageStructurallyValid,
    bool ChangedOnlyExpectedPart,
    bool TargetFindingResolved,
    int CandidateAccessibilityFindingCount,
    int CandidatePackageErrorCount
)
{
    public bool Passed =>
        BasePackageStructurallyValid
        && CandidatePackageStructurallyValid
        && ChangedOnlyExpectedPart
        && TargetFindingResolved
        && CandidatePackageErrorCount == 0;
}

public sealed class WordLintRepairPlan
{
    private readonly WordPackageTransactionCore _transaction;

    internal WordLintRepairPlan(
        string planId,
        WordLintRepairKind repairKind,
        string findingId,
        string ruleId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        string sourcePartUri,
        int sourceElementOrdinal,
        int beforeCharacters,
        int afterCharacters,
        string beforeValueFingerprint,
        string afterValueFingerprint,
        WordLintRepairValidation validation,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts
    )
    {
        PlanId = planId;
        RepairKind = repairKind;
        FindingId = findingId;
        RuleId = ruleId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        SourcePartUri = sourcePartUri;
        SourceElementOrdinal = sourceElementOrdinal;
        BeforeCharacters = beforeCharacters;
        AfterCharacters = afterCharacters;
        BeforeValueFingerprint = beforeValueFingerprint;
        AfterValueFingerprint = afterValueFingerprint;
        Validation = validation;
        _transaction = new WordPackageTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            parts
        );
        ChangedParts = new ReadOnlyCollection<WordLintRepairPartChange>(
            _transaction.Parts
                .OrderBy(part => part.PartUri, StringComparer.Ordinal)
                .Select(part => new WordLintRepairPartChange(
                    part.PartUri,
                    part.EntryName,
                    part.BeforeSha256,
                    part.AfterSha256,
                    part.BeforeContent.Length,
                    part.AfterContent.Length
                ))
                .ToArray()
        );
    }

    public string PlanId { get; }

    public WordLintRepairKind RepairKind { get; }

    public string FindingId { get; }

    public string RuleId { get; }

    public string BasePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public string SourcePartUri { get; }

    public int SourceElementOrdinal { get; }

    public int BeforeCharacters { get; }

    public int AfterCharacters { get; }

    public string BeforeValueFingerprint { get; }

    public string AfterValueFingerprint { get; }

    public WordLintRepairValidation Validation { get; }

    public IReadOnlyList<WordLintRepairPartChange> ChangedParts { get; }

    public bool HasChanges => _transaction.HasChanges;

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot) =>
        _transaction.CreateMutation(currentSnapshot);

    public OpcPackageMutationBuilder CreateInverseMutation(
        OpcPackageSnapshot appliedSnapshot
    ) => _transaction.CreateInverseMutation(appliedSnapshot);
}

public sealed class WordLintRepairPlanner
{
    public const string DocumentTitleRuleId = "WTL_ACCESSIBILITY_DOCUMENT_TITLE";

    private const string CorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";
    private const string CorePropertiesNamespace =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private readonly WordLintRepairPlannerOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;

    public WordLintRepairPlanner(
        WordLintRepairPlannerOptions? options = null,
        LosslessXmlOptions? xmlOptions = null
    )
    {
        _options = options ?? WordLintRepairPlannerOptions.Default;
        _options.Validate();
        _xmlOptions = xmlOptions ?? new LosslessXmlOptions
        {
            MaxSourceBytes = _options.MaxSourceXmlPartBytes,
        };
        _xmlOptions.Validate();
    }

    public WordLintRepairPlan PlanSetDocumentTitle(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        string expectedPackageFingerprint,
        string findingId,
        string newTitle,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPackageFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId);
        ArgumentNullException.ThrowIfNull(newTitle);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTitle(newTitle);
        EnsureFingerprint(package, semanticDocument, expectedPackageFingerprint);
        if (!package.IsStructurallyValid)
        {
            throw new WordLintRepairPreconditionException(
                "Lint repair requires a structurally valid base package."
            );
        }

        var linter = CreateAccessibilityLinter();
        var report = linter.Analyze(package, semanticDocument, cancellationToken);
        if (!report.Coverage.ExecutionComplete || report.FindingsTruncated)
        {
            throw new WordLintRepairPreconditionException(
                "The accessibility lint pass was incomplete; repair evidence is not trustworthy."
            );
        }
        var finding = report.Findings.SingleOrDefault(item =>
            string.Equals(item.Id, findingId, StringComparison.Ordinal)
        ) ?? throw new WordLintRepairPreconditionException(
            "The requested lint finding does not exist in the current package."
        );
        if (
            !string.Equals(finding.RuleId, DocumentTitleRuleId, StringComparison.Ordinal)
            || !string.Equals(finding.Fix.Kind, "set_document_title", StringComparison.Ordinal)
        )
        {
            throw new WordLintRepairPreconditionException(
                "The requested finding is not an empty-document-title finding."
            );
        }

        var coreRelationship = ResolveCorePropertiesRelationship(package);
        var partUri = coreRelationship.ResolvedTargetPartUri
            ?? throw new WordLintRepairPreconditionException(
                "The core-properties relationship has no internal target."
            );
        if (!package.Parts.TryGetValue(partUri, out var part))
        {
            throw new WordLintRepairPreconditionException(
                "The core-properties relationship target is missing."
            );
        }
        if (!string.Equals(
            part.ContentType,
            CorePropertiesContentType,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            throw new WordLintRepairPreconditionException(
                "The core-properties part has the wrong content type."
            );
        }

        LosslessXmlDocument source;
        try
        {
            source = LosslessXmlDocument.Parse(
                part.Entry.Content,
                _xmlOptions,
                cancellationToken
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordLintRepairException(
                "The core-properties part cannot be edited losslessly.",
                exception
            );
        }

        if (
            source.Root.LocalName != "coreProperties"
            || source.Root.NamespaceUri != CorePropertiesNamespace
        )
        {
            throw new WordLintRepairPreconditionException(
                "The core-properties part has the wrong root element."
            );
        }
        var titles = source.Elements.Where(element =>
            element.LocalName == "title" && element.NamespaceUri == DublinCoreNamespace
        ).ToArray();
        if (titles.Length != 1)
        {
            throw new WordLintRepairPreconditionException(
                "Safe title repair requires exactly one existing dc:title element."
            );
        }
        var title = titles[0];
        if (title.ParentOrdinal != source.Root.Ordinal)
        {
            throw new WordLintRepairPreconditionException(
                "Safe title repair requires dc:title to be a direct core-properties child."
            );
        }
        if (!string.IsNullOrWhiteSpace(title.Value))
        {
            throw new WordLintRepairPreconditionException(
                "The existing dc:title element is not empty."
            );
        }
        if (
            finding.Source.SourceElementOrdinal != title.Ordinal
            || !string.Equals(finding.Source.PartUri, part.Uri, StringComparison.Ordinal)
        )
        {
            throw new WordLintRepairPreconditionException(
                "The lint finding is not bound to the current title source element."
            );
        }

        byte[] changed;
        try
        {
            changed = source.ReplaceElementText(
                title.Ordinal,
                newTitle,
                title.Value,
                part.Entry.Sha256,
                preserveBoundaryWhitespace: false,
                cancellationToken
            );
        }
        catch (LosslessXmlPreconditionException exception)
        {
            throw new WordLintRepairPreconditionException(exception.Message, exception);
        }
        catch (LosslessXmlException exception)
        {
            throw new WordLintRepairException(
                "The title cannot be replaced without destroying lexical XML structure.",
                exception
            );
        }

        if (changed.AsSpan().SequenceEqual(part.Entry.Content.Span))
        {
            throw new WordLintRepairPreconditionException(
                "The requested title repair produced no package change."
            );
        }
        var payload = new WordPackagePartPayload(
            part.Uri,
            part.Entry.Name,
            part.Entry.Content.ToArray(),
            changed
        );
        var parts = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal)
        {
            [part.Uri] = payload,
        };
        var projectedEntries = new Dictionary<string, ReadOnlyMemory<byte>>(
            StringComparer.Ordinal
        )
        {
            [part.Entry.Name] = changed,
        };
        var resultFingerprint = OpcPackageFingerprint.ComputeProjected(
            package,
            projectedEntries
        );
        var transaction = new WordPackageTransactionCore(
            package.Fingerprint,
            resultFingerprint,
            parts
        );
        var validation = ValidateCandidate(
            package,
            transaction,
            part.Uri,
            resultFingerprint,
            linter,
            cancellationToken
        );
        if (!validation.Passed)
        {
            throw new WordLintRepairValidationException(
                "The repaired candidate did not pass structural and targeted lint validation.",
                validation
            );
        }

        return new WordLintRepairPlan(
            CreatePlanId(package.Fingerprint, finding.Id, newTitle),
            WordLintRepairKind.SetDocumentTitle,
            finding.Id,
            finding.RuleId,
            package.Fingerprint,
            resultFingerprint,
            part.Uri,
            title.Ordinal,
            title.Value.Length,
            newTitle.Length,
            FingerprintValue(title.Value),
            FingerprintValue(newTitle),
            validation,
            parts
        );
    }

    private WordDocumentLinter CreateAccessibilityLinter() => new(
        new WordDocumentLinterOptions
        {
            EnabledRulePacks = [WordLintRulePack.Accessibility],
            MaxSourceXmlPartBytes = _options.MaxSourceXmlPartBytes,
        }
    );

    private static OpcRelationship ResolveCorePropertiesRelationship(
        OpcPackageSnapshot package
    )
    {
        var relationships = package.Relationships.Where(item =>
            item.SourcePartUri == "/"
            && item.TargetMode == OpcRelationshipTargetMode.Internal
            && item.Type.EndsWith("/metadata/core-properties", StringComparison.Ordinal)
        ).ToArray();
        return relationships.Length == 1
            ? relationships[0]
            : throw new WordLintRepairPreconditionException(
                "Safe title repair requires exactly one internal core-properties relationship."
            );
    }

    private static WordLintRepairValidation ValidateCandidate(
        OpcPackageSnapshot basePackage,
        WordPackageTransactionCore transaction,
        string expectedPartUri,
        string expectedResultFingerprint,
        WordDocumentLinter linter,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        new OpcPackageSerializer().Write(
            stream,
            transaction.CreateMutation(basePackage),
            OpcSerializationMode.Preserve
        );
        stream.Position = 0;
        var candidate = new OpcPackageReader().Read(stream);
        if (!string.Equals(
            candidate.Fingerprint,
            expectedResultFingerprint,
            StringComparison.Ordinal
        ))
        {
            throw new WordLintRepairException(
                "Candidate package fingerprint differs from the planned result."
            );
        }
        var changedParts = basePackage.Parts.Keys
            .Union(candidate.Parts.Keys, StringComparer.Ordinal)
            .Where(uri =>
                !basePackage.Parts.TryGetValue(uri, out var before)
                || !candidate.Parts.TryGetValue(uri, out var after)
                || !string.Equals(
                    before.Entry.Sha256,
                    after.Entry.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToArray();
        var candidateErrors = candidate.Diagnostics.Count(diagnostic =>
            diagnostic.Severity is OpcDiagnosticSeverity.Error
                or OpcDiagnosticSeverity.Fatal
        );
        var semantic = new WordSemanticProjector().Project(candidate, cancellationToken);
        var report = linter.Analyze(candidate, semantic, cancellationToken);
        var targetResolved = report.Coverage.ExecutionComplete
            && !report.FindingsTruncated
            && report.Findings.All(item =>
                !string.Equals(item.RuleId, DocumentTitleRuleId, StringComparison.Ordinal)
            );
        return new WordLintRepairValidation(
            basePackage.IsStructurallyValid,
            candidate.IsStructurallyValid,
            changedParts.Length == 1
                && string.Equals(changedParts[0], expectedPartUri, StringComparison.Ordinal),
            targetResolved,
            report.VisibleFindingCount,
            candidateErrors
        );
    }

    private void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Document title must not be blank.", nameof(title));
        }
        if (!string.Equals(title, title.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Document title must not have boundary whitespace.",
                nameof(title)
            );
        }
        if (title.Length > _options.MaxDocumentTitleCharacters)
        {
            throw new WordLintRepairLimitException(
                $"Document title exceeds {_options.MaxDocumentTitleCharacters} characters."
            );
        }
    }

    private static void EnsureFingerprint(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        string expectedPackageFingerprint
    )
    {
        if (
            !string.Equals(
                package.Fingerprint,
                expectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordLintRepairPreconditionException(
                "Package, semantic projection, and expected fingerprint do not match."
            );
        }
    }

    private static string CreatePlanId(
        string packageFingerprint,
        string findingId,
        string newTitle
    ) => "wlrplan_" + HashFields(15, packageFingerprint, findingId, newTitle);

    private static string FingerprintValue(string value) =>
        "sha256:" + HashFields(16, value);

    private static string HashFields(int bytes, params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                length,
                encoded.Length
            );
            hash.AppendData(length);
            hash.AppendData(encoded);
        }
        return Convert.ToHexString(hash.GetHashAndReset().AsSpan(0, bytes))
            .ToLowerInvariant();
    }
}

public class WordLintRepairException : InvalidOperationException
{
    public WordLintRepairException(string message)
        : base(message)
    {
    }

    public WordLintRepairException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordLintRepairPreconditionException : WordLintRepairException
{
    public WordLintRepairPreconditionException(string message)
        : base(message)
    {
    }

    public WordLintRepairPreconditionException(
        string message,
        Exception innerException
    )
        : base(message, innerException)
    {
    }
}

public sealed class WordLintRepairLimitException : WordLintRepairException
{
    public WordLintRepairLimitException(string message)
        : base(message)
    {
    }
}

public sealed class WordLintRepairValidationException : WordLintRepairException
{
    public WordLintRepairValidationException(
        string message,
        WordLintRepairValidation validation
    )
        : base(message)
    {
        Validation = validation;
    }

    public WordLintRepairValidation Validation { get; }
}
