using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

public enum WordOcrIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordOcrMediaFamily
{
    Raster,
    Vector,
    Unknown,
}

public sealed record WordOcrIssue(
    string Code,
    WordOcrIssueSeverity Severity,
    string Message,
    string? CandidateId,
    string? SourcePartUri
);

public sealed record WordOcrCandidate(
    string Id,
    string TargetPartUri,
    string? DeclaredContentType,
    string? DetectedContentType,
    WordOcrMediaFamily MediaFamily,
    long ByteLength,
    string Sha256,
    bool SignatureValid,
    bool Eligible,
    string? RejectionCode,
    IReadOnlyList<string> FigureIds,
    IReadOnlyList<string> ResourceIds,
    IReadOnlyList<WordStoryKind> StoryKinds
);

public sealed class WordOcrGraph
{
    private readonly IReadOnlyDictionary<string, WordOcrCandidate> _candidatesById;

    internal WordOcrGraph(
        string packageFingerprint,
        IReadOnlyList<WordOcrCandidate> candidates,
        IReadOnlyList<WordOcrIssue> issues,
        bool issuesTruncated,
        bool sourceProjectionCoverageComplete
    )
    {
        PackageFingerprint = packageFingerprint;
        Candidates = new ReadOnlyCollection<WordOcrCandidate>(candidates.ToArray());
        Issues = new ReadOnlyCollection<WordOcrIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        SourceProjectionCoverageComplete = sourceProjectionCoverageComplete;
        _candidatesById = new ReadOnlyDictionary<string, WordOcrCandidate>(
            candidates.ToDictionary(item => item.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordOcrCandidate> Candidates { get; }

    public IReadOnlyList<WordOcrIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public bool SourceProjectionCoverageComplete { get; }

    public bool CandidateCoverageComplete =>
        !IssuesTruncated && SourceProjectionCoverageComplete;

    public bool TryGetCandidate(string id, out WordOcrCandidate? candidate) =>
        _candidatesById.TryGetValue(id, out candidate);
}

public sealed record WordOcrGraphOptions
{
    public static WordOcrGraphOptions Default { get; } = new();

    public int MaxCandidates { get; init; } = 100_000;

    public int MaxIssues { get; init; } = 10_000;

    public long MaxCandidateBytes { get; init; } = 32L * 1024 * 1024;

    internal void Validate()
    {
        if (MaxCandidates <= 0 || MaxIssues <= 0 || MaxCandidateBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WordOcrGraphOptions),
                "OCR graph limits must be positive."
            );
        }
    }
}

public sealed class WordOcrGraphBuilder
{
    private const int MaximumPublicPartUriCharacters = 2_048;
    private const int MaximumPublicContentTypeCharacters = 256;
    private static readonly IReadOnlyDictionary<string, WordOcrMediaFamily>
        DeclaredFamilies = new Dictionary<string, WordOcrMediaFamily>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["image/png"] = WordOcrMediaFamily.Raster,
            ["image/jpeg"] = WordOcrMediaFamily.Raster,
            ["image/jpg"] = WordOcrMediaFamily.Raster,
            ["image/gif"] = WordOcrMediaFamily.Raster,
            ["image/bmp"] = WordOcrMediaFamily.Raster,
            ["image/x-ms-bmp"] = WordOcrMediaFamily.Raster,
            ["image/tiff"] = WordOcrMediaFamily.Raster,
            ["image/webp"] = WordOcrMediaFamily.Raster,
            ["image/svg+xml"] = WordOcrMediaFamily.Vector,
            ["image/x-emf"] = WordOcrMediaFamily.Vector,
            ["image/emf"] = WordOcrMediaFamily.Vector,
            ["image/x-wmf"] = WordOcrMediaFamily.Vector,
            ["image/wmf"] = WordOcrMediaFamily.Vector,
        };

    private readonly WordOcrGraphOptions _options;
    private readonly WordFigureCaptionGraphBuilder _figureBuilder;

    public WordOcrGraphBuilder(
        WordOcrGraphOptions? options = null,
        WordFigureCaptionGraphOptions? figureOptions = null
    )
    {
        _options = options ?? WordOcrGraphOptions.Default;
        _options.Validate();
        _figureBuilder = new WordFigureCaptionGraphBuilder(figureOptions);
    }

    public WordOcrGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        return Build(
            package,
            _figureBuilder.Build(package, cancellationToken),
            cancellationToken
        );
    }

    public WordOcrGraph Build(
        OpcPackageSnapshot package,
        WordFigureCaptionGraph figures,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(figures);
        if (!string.Equals(
            package.Fingerprint,
            figures.PackageFingerprint,
            StringComparison.Ordinal
        ))
        {
            throw new WordOcrProjectionException(
                "The figure graph belongs to another package fingerprint."
            );
        }

        var groups = new Dictionary<string, CandidateAccumulator>(StringComparer.Ordinal);
        var issues = new List<WordOcrIssue>();
        var issuesTruncated = false;
        var sourceProjectionCoverageComplete = !figures.IssuesTruncated
            && !figures.Issues.Any(IsSourceCoverageIssue);
        if (figures.IssuesTruncated)
        {
            AddIssue(
                issues,
                ref issuesTruncated,
                new WordOcrIssue(
                    "OCR_SOURCE_PROJECTION_ISSUES_TRUNCATED",
                    WordOcrIssueSeverity.Warning,
                    "The source figure projection truncated diagnostics, so complete OCR candidate coverage cannot be proved.",
                    null,
                    null
                )
            );
        }
        if (figures.Issues.Any(IsSourceCoverageIssue))
        {
            AddIssue(
                issues,
                ref issuesTruncated,
                new WordOcrIssue(
                    "OCR_SOURCE_PROJECTION_INCOMPLETE",
                    WordOcrIssueSeverity.Warning,
                    "The source figure projection contains unresolved or unmodeled image evidence, so complete OCR candidate coverage cannot be proved.",
                    null,
                    null
                )
            );
        }
        foreach (var figure in figures.Figures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireBoundedPartUri(figure.PartUri);
            foreach (var resource in figure.Resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsImageResource(resource))
                {
                    continue;
                }
                if (resource.IsExternal)
                {
                    AddIssue(
                        issues,
                        ref issuesTruncated,
                        new WordOcrIssue(
                            "OCR_EXTERNAL_IMAGE_NOT_FETCHED",
                            WordOcrIssueSeverity.Info,
                            "An external image is declared but OCR never follows external relationships.",
                            null,
                            figure.PartUri
                        )
                    );
                    continue;
                }
                if (!resource.IsResolved || resource.TargetPartUri is null)
                {
                    sourceProjectionCoverageComplete = false;
                    AddIssue(
                        issues,
                        ref issuesTruncated,
                        new WordOcrIssue(
                            "OCR_IMAGE_RESOURCE_UNRESOLVED",
                            WordOcrIssueSeverity.Warning,
                            "An embedded image declaration does not resolve to one package part.",
                            null,
                            figure.PartUri
                        )
                    );
                    continue;
                }
                RequireBoundedPartUri(resource.TargetPartUri);
                if (!package.Parts.TryGetValue(resource.TargetPartUri, out var part))
                {
                    throw new WordOcrProjectionException(
                        "A resolved figure image lost its package part."
                    );
                }
                if (!groups.TryGetValue(part.Uri, out var accumulator))
                {
                    if (groups.Count >= _options.MaxCandidates)
                    {
                        throw new WordOcrLimitException(
                            $"OCR candidate count exceeds {_options.MaxCandidates}."
                        );
                    }
                    accumulator = new CandidateAccumulator(part);
                    groups.Add(part.Uri, accumulator);
                }
                accumulator.FigureIds.Add(figure.Id);
                accumulator.ResourceIds.Add(resource.Id);
                accumulator.StoryKinds.Add(figure.StoryKind);
            }
        }

        var candidates = new List<WordOcrCandidate>(groups.Count);
        foreach (var accumulator in groups.Values.OrderBy(
            item => item.Part.Uri,
            StringComparer.Ordinal
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = accumulator.Part.Entry.Content.Span;
            var detected = DetectContentType(bytes);
            var declared = accumulator.Part.ContentType;
            if (declared?.Length > MaximumPublicContentTypeCharacters)
            {
                throw new WordOcrLimitException(
                    $"OCR candidate content type exceeds {MaximumPublicContentTypeCharacters} characters."
                );
            }
            var family = DeclaredFamily(declared, detected);
            var signatureValid = detected is not null
                && DeclaredMatchesDetected(declared, detected);
            var candidateId = StableId(
                package.Fingerprint,
                accumulator.Part.Uri,
                accumulator.Part.Entry.Sha256
            );
            string? rejectionCode = null;
            if (family != WordOcrMediaFamily.Raster)
            {
                rejectionCode = family == WordOcrMediaFamily.Vector
                    ? "OCR_VECTOR_IMAGE_REQUIRES_RASTERIZATION"
                    : "OCR_IMAGE_FORMAT_UNSUPPORTED";
            }
            else if (!signatureValid)
            {
                rejectionCode = "OCR_IMAGE_SIGNATURE_MISMATCH";
            }
            else if (accumulator.Part.Entry.UncompressedLength > _options.MaxCandidateBytes)
            {
                rejectionCode = "OCR_IMAGE_TOO_LARGE";
            }

            var candidate = new WordOcrCandidate(
                candidateId,
                accumulator.Part.Uri,
                declared,
                detected,
                family,
                accumulator.Part.Entry.UncompressedLength,
                accumulator.Part.Entry.Sha256,
                signatureValid,
                rejectionCode is null,
                rejectionCode,
                accumulator.FigureIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                accumulator.ResourceIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                accumulator.StoryKinds.OrderBy(kind => kind).ToArray()
            );
            candidates.Add(candidate);
            if (rejectionCode is not null)
            {
                AddIssue(
                    issues,
                    ref issuesTruncated,
                    new WordOcrIssue(
                        rejectionCode,
                        WordOcrIssueSeverity.Warning,
                        PublicMessage(rejectionCode),
                        candidateId,
                        accumulator.Part.Uri
                    )
                );
            }
        }

        return new WordOcrGraph(
            package.Fingerprint,
            candidates,
            issues,
            issuesTruncated,
            sourceProjectionCoverageComplete
        );
    }

    private void AddIssue(
        List<WordOcrIssue> issues,
        ref bool truncated,
        WordOcrIssue issue
    )
    {
        if (issues.Count < _options.MaxIssues)
        {
            issues.Add(issue);
        }
        else
        {
            truncated = true;
        }
    }

    private static bool IsImageResource(WordFigureResourceDefinition resource) =>
        resource.Role is WordFigureResourceRole.ImageEmbed
            or WordFigureResourceRole.ImageLink
            or WordFigureResourceRole.VmlImage
        || (resource.TargetContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ?? false);

    private static bool IsSourceCoverageIssue(WordFigureIssue issue) => issue.Code is
        "FIGURE_PAYLOAD_UNMODELED"
        or "FIGURE_RELATIONSHIP_MISSING"
        or "FIGURE_RELATIONSHIP_AMBIGUOUS"
        or "FIGURE_RESOURCE_UNRESOLVED"
        or "FIGURE_ALTERNATE_REPRESENTATION_LOCATION_MISMATCH";

    private static void RequireBoundedPartUri(string partUri)
    {
        if (partUri.Length > MaximumPublicPartUriCharacters)
        {
            throw new WordOcrLimitException(
                $"OCR source part URI exceeds {MaximumPublicPartUriCharacters} characters."
            );
        }
    }

    private static WordOcrMediaFamily DeclaredFamily(
        string? declared,
        string? detected
    )
    {
        if (declared is not null && DeclaredFamilies.TryGetValue(declared, out var family))
        {
            return family;
        }
        return detected is null ? WordOcrMediaFamily.Unknown : WordOcrMediaFamily.Raster;
    }

    private static bool DeclaredMatchesDetected(string? declared, string detected)
    {
        if (declared is null)
        {
            return false;
        }
        if (string.Equals(declared, detected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return detected switch
        {
            "image/jpeg" => declared.Equals("image/jpg", StringComparison.OrdinalIgnoreCase),
            "image/bmp" => declared.Equals("image/x-ms-bmp", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return "image/png";
        }
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
        {
            return "image/jpeg";
        }
        if (bytes.Length >= 6
            && (bytes[..6].SequenceEqual("GIF87a"u8)
                || bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return "image/gif";
        }
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return "image/bmp";
        }
        if (bytes.Length >= 4
            && (bytes[..4].SequenceEqual(new byte[] { 0x49, 0x49, 0x2a, 0x00 })
                || bytes[..4].SequenceEqual(new byte[] { 0x4d, 0x4d, 0x00, 0x2a })))
        {
            return "image/tiff";
        }
        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }
        return null;
    }

    private static string StableId(
        string packageFingerprint,
        string partUri,
        string partSha256
    )
    {
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{packageFingerprint.Length}:{packageFingerprint}{partUri.Length}:{partUri}{partSha256.Length}:{partSha256}"
        );
        return "wocr_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
        )[..24].ToLowerInvariant();
    }

    private static string PublicMessage(string code) => code switch
    {
        "OCR_VECTOR_IMAGE_REQUIRES_RASTERIZATION" =>
            "The embedded image is vector content and requires an explicit rasterization provider before OCR.",
        "OCR_IMAGE_SIGNATURE_MISMATCH" =>
            "The declared image content type does not match the embedded payload signature.",
        "OCR_IMAGE_TOO_LARGE" =>
            "The embedded image exceeds the configured per-candidate OCR byte limit.",
        _ => "The embedded image format is not supported by the current OCR candidate profile.",
    };

    private sealed class CandidateAccumulator(OpcPart part)
    {
        public OpcPart Part { get; } = part;

        public HashSet<string> FigureIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ResourceIds { get; } = new(StringComparer.Ordinal);

        public HashSet<WordStoryKind> StoryKinds { get; } = [];
    }
}

public class WordOcrException : Exception
{
    public WordOcrException(string message)
        : base(message)
    { }
}

public sealed class WordOcrProjectionException : WordOcrException
{
    public WordOcrProjectionException(string message)
        : base(message)
    { }
}

public sealed class WordOcrLimitException : WordOcrException
{
    public WordOcrLimitException(string message)
        : base(message)
    { }
}
