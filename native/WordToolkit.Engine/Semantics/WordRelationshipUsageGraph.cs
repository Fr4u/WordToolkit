using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordRelationshipUsageStatus
{
    PackageRelationship,
    OwnerMissing,
    OwnerNonXml,
    OwnerXmlUnparseable,
    DuplicateRelationshipId,
    ReferencedByMarkup,
    ImplicitByRelationshipType,
    UnreferencedExplicitRelationship,
    UnknownUnreferencedRelationship,
}

public sealed record WordRelationshipMarkupReference(
    int ElementOrdinal,
    string AttributeName
);

public sealed class WordRelationshipUsage
{
    internal WordRelationshipUsage(
        string id,
        string fingerprint,
        OpcRelationship relationship,
        WordRelationshipUsageStatus status,
        IReadOnlyList<WordRelationshipMarkupReference> markupReferences,
        bool referencesTruncated
    )
    {
        Id = id;
        Fingerprint = fingerprint;
        SourcePartUri = relationship.SourcePartUri;
        RelationshipPartUri = relationship.RelationshipPartUri;
        RelationshipId = relationship.Id;
        RelationshipType = relationship.Type;
        Target = relationship.Target;
        TargetMode = relationship.TargetMode;
        ResolvedTargetPartUri = relationship.ResolvedTargetPartUri;
        TargetFragment = relationship.TargetFragment;
        Status = status;
        MarkupReferences = new ReadOnlyCollection<WordRelationshipMarkupReference>(
            markupReferences.ToArray()
        );
        MarkupReferencesTruncated = referencesTruncated;
    }

    public string Id { get; }

    public string Fingerprint { get; }

    public string SourcePartUri { get; }

    public string RelationshipPartUri { get; }

    public string RelationshipId { get; }

    public string RelationshipType { get; }

    public string Target { get; }

    public OpcRelationshipTargetMode TargetMode { get; }

    public string? ResolvedTargetPartUri { get; }

    public string? TargetFragment { get; }

    public WordRelationshipUsageStatus Status { get; }

    public IReadOnlyList<WordRelationshipMarkupReference> MarkupReferences { get; }

    public bool MarkupReferencesTruncated { get; }

    public int MarkupReferenceCount { get; internal init; }

    public bool MarkupRemovalCandidate =>
        Status == WordRelationshipUsageStatus.UnreferencedExplicitRelationship
        && MarkupReferenceCount == 0
        && !MarkupReferencesTruncated;
}

public sealed record WordOrphanRelationshipPart(
    string Id,
    string RelationshipPartUri,
    string EntryName,
    string SourcePartUri,
    string EntrySha256,
    int ParsedRelationshipCount
);

public sealed class WordRelationshipUsageGraph
{
    internal WordRelationshipUsageGraph(
        string packageFingerprint,
        IReadOnlyList<WordRelationshipUsage> relationships,
        IReadOnlyList<WordOrphanRelationshipPart> orphanRelationshipParts,
        int parsedOwnerPartCount
    )
    {
        PackageFingerprint = packageFingerprint;
        Relationships = new ReadOnlyCollection<WordRelationshipUsage>(
            relationships.ToArray()
        );
        OrphanRelationshipParts = new ReadOnlyCollection<WordOrphanRelationshipPart>(
            orphanRelationshipParts.ToArray()
        );
        ParsedOwnerPartCount = parsedOwnerPartCount;
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordRelationshipUsage> Relationships { get; }

    public IReadOnlyList<WordOrphanRelationshipPart> OrphanRelationshipParts { get; }

    public int ParsedOwnerPartCount { get; }

    public int MarkupRemovalCandidateCount => Relationships.Count(item =>
        item.MarkupRemovalCandidate
    );

    public bool TryGetRelationship(
        string sourcePartUri,
        string relationshipId,
        out WordRelationshipUsage? usage
    )
    {
        var matches = Relationships.Where(item =>
            string.Equals(item.SourcePartUri, sourcePartUri, StringComparison.Ordinal)
            && string.Equals(item.RelationshipId, relationshipId, StringComparison.Ordinal)
        ).Take(2).ToArray();
        usage = matches.Length == 1 ? matches[0] : null;
        return matches.Length == 1;
    }
}

public sealed record WordRelationshipUsageGraphOptions
{
    public static WordRelationshipUsageGraphOptions Default { get; } = new();

    public int MaxRelationships { get; init; } = 500_000;

    public int MaxOwnerXmlParts { get; init; } = 10_000;

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxReferencesPerRelationship { get; init; } = 64;

    internal void Validate()
    {
        if (MaxRelationships <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRelationships));
        }
        if (MaxOwnerXmlParts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOwnerXmlParts));
        }
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
        if (MaxReferencesPerRelationship <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReferencesPerRelationship));
        }
    }
}

public sealed class WordRelationshipUsageGraphBuilder
{
    private static readonly IReadOnlyList<string> KnownRelationshipTypeRoots =
    [
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/",
        "http://purl.oclc.org/ooxml/officeDocument/relationships/",
        "http://schemas.microsoft.com/office/2006/relationships/",
        "http://schemas.microsoft.com/office/2007/relationships/",
        "http://schemas.microsoft.com/office/2011/relationships/",
        "http://schemas.microsoft.com/office/2016/09/relationships/",
        "http://schemas.microsoft.com/office/2016/11/relationships/",
    ];

    private static readonly IReadOnlySet<string> KnownPackageImplicitRelationshipTypes =
        new HashSet<string>(
            [
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail",
            ],
            StringComparer.Ordinal
        );

    private static readonly IReadOnlySet<string> ExplicitRelationshipTypeNames =
        new HashSet<string>(
            [
                "aFChunk",
                "attachedTemplate",
                "audio",
                "chart",
                "chartUserShapes",
                "control",
                "diagramColors",
                "diagramData",
                "diagramLayout",
                "diagramQuickStyle",
                "footer",
                "font",
                "header",
                "hyperlink",
                "image",
                "mailMergeSource",
                "media",
                "oleObject",
                "package",
                "printerSettings",
                "recipientData",
                "video",
            ],
            StringComparer.Ordinal
        );

    private static readonly IReadOnlySet<string> ImplicitRelationshipTypeNames =
        new HashSet<string>(
            [
                "comments",
                "commentsExtended",
                "commentsExtensible",
                "commentsIds",
                "core-properties",
                "custom-properties",
                "customXml",
                "customXmlProps",
                "endnotes",
                "extended-properties",
                "fontTable",
                "footnotes",
                "glossaryDocument",
                "numbering",
                "officeDocument",
                "people",
                "settings",
                "styles",
                "stylesWithEffects",
                "theme",
                "themeOverride",
                "thumbnail",
                "vbaProject",
                "vbaProjectSignature",
                "webSettings",
            ],
            StringComparer.Ordinal
        );

    private readonly WordRelationshipUsageGraphOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;

    public WordRelationshipUsageGraphBuilder(
        WordRelationshipUsageGraphOptions? options = null
    )
    {
        _options = options ?? WordRelationshipUsageGraphOptions.Default;
        _options.Validate();
        _xmlOptions = new LosslessXmlOptions
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
            MaxTextCharacters = _options.MaxXmlPartBytes,
        };
    }

    public WordRelationshipUsageGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        if (package.Relationships.Count > _options.MaxRelationships)
        {
            throw new WordRelationshipUsageLimitException(
                $"Package relationships exceed {_options.MaxRelationships}."
            );
        }

        var relationships = new List<WordRelationshipUsage>(
            package.Relationships.Count
        );
        var relationshipIdCounts = package.Relationships
            .GroupBy(
                item => item.SourcePartUri + "\0" + item.Id,
                StringComparer.Ordinal
            )
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var ownerCache = new Dictionary<string, OwnerProjection>(StringComparer.Ordinal);
        foreach (var relationship in package.Relationships
            .OrderBy(item => item.SourcePartUri, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projection = ResolveOwnerProjection(
                package,
                relationship.SourcePartUri,
                ownerCache,
                cancellationToken
            );
            var referenceCount = 0;
            var referencesTruncated = false;
            var references = projection.Document is null
                ? Array.Empty<WordRelationshipMarkupReference>()
                : FindReferences(
                    projection.Document,
                    relationship.Id,
                    cancellationToken,
                    out referenceCount,
                    out referencesTruncated
                );
            if (projection.Document is null)
            {
                referenceCount = 0;
                referencesTruncated = false;
            }
            var status = Classify(
                relationship,
                projection,
                referenceCount,
                relationshipIdCounts[relationship.SourcePartUri + "\0" + relationship.Id] > 1
            );
            relationships.Add(
                new WordRelationshipUsage(
                    StableId(
                        "wdrel_",
                        package.Fingerprint,
                        relationship.SourcePartUri,
                        relationship.Id
                    ),
                    RelationshipFingerprint(relationship),
                    relationship,
                    status,
                    references,
                    referencesTruncated
                )
                {
                    MarkupReferenceCount = referenceCount,
                }
            );
        }

        var countsByRelationshipPart = package.Relationships
            .GroupBy(item => item.RelationshipPartUri, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var orphanParts = package.Entries
            .Where(entry =>
                !entry.IsDirectory
                && OpcPartUri.TryRelationshipSource(entry.Name, out var sourcePartUri)
                && sourcePartUri is not null
                && sourcePartUri != OpcPartUri.PackageRoot
                && !package.Parts.ContainsKey(sourcePartUri)
            )
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry =>
            {
                _ = OpcPartUri.TryRelationshipSource(entry.Name, out var sourcePartUri);
                var partUri = entry.PartUri ?? "/" + entry.Name;
                return new WordOrphanRelationshipPart(
                    StableId("wdrelp_", package.Fingerprint, partUri),
                    partUri,
                    entry.Name,
                    sourcePartUri!,
                    entry.Sha256,
                    countsByRelationshipPart.GetValueOrDefault(partUri)
                );
            })
            .ToArray();
        return new WordRelationshipUsageGraph(
            package.Fingerprint,
            relationships,
            orphanParts,
            ownerCache.Values.Count(item => item.Document is not null)
        );
    }

    private OwnerProjection ResolveOwnerProjection(
        OpcPackageSnapshot package,
        string sourcePartUri,
        IDictionary<string, OwnerProjection> cache,
        CancellationToken cancellationToken
    )
    {
        if (sourcePartUri == OpcPartUri.PackageRoot)
        {
            return OwnerProjection.Package;
        }
        if (cache.TryGetValue(sourcePartUri, out var cached))
        {
            return cached;
        }
        if (!package.Parts.TryGetValue(sourcePartUri, out var part))
        {
            return cache[sourcePartUri] = OwnerProjection.Missing;
        }
        if (!IsXmlContentType(part.ContentType))
        {
            return cache[sourcePartUri] = OwnerProjection.NonXml;
        }
        if (cache.Values.Count(item =>
                item.Kind is OwnerProjectionKind.Xml or OwnerProjectionKind.Unparseable
            ) >= _options.MaxOwnerXmlParts)
        {
            throw new WordRelationshipUsageLimitException(
                $"Relationship owner XML parts exceed {_options.MaxOwnerXmlParts}."
            );
        }
        if (part.Entry.Content.Length > _options.MaxXmlPartBytes)
        {
            throw new WordRelationshipUsageLimitException(
                $"Relationship owner XML part '{sourcePartUri}' exceeds {_options.MaxXmlPartBytes} bytes."
            );
        }
        try
        {
            var document = LosslessXmlDocument.Parse(
                part.Entry.Content,
                _xmlOptions,
                cancellationToken
            );
            return cache[sourcePartUri] = new OwnerProjection(
                OwnerProjectionKind.Xml,
                document
            );
        }
        catch (Exception exception) when (
            exception is LosslessXmlParseException
                or LosslessXmlEncodingException
                or LosslessXmlLimitException
        )
        {
            return cache[sourcePartUri] = OwnerProjection.Unparseable;
        }
    }

    private WordRelationshipMarkupReference[] FindReferences(
        LosslessXmlDocument document,
        string relationshipId,
        CancellationToken cancellationToken,
        out int referenceCount,
        out bool truncated
    )
    {
        var result = new List<WordRelationshipMarkupReference>();
        referenceCount = 0;
        foreach (var element in document.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in element.Attributes)
            {
                if (!string.Equals(
                    attribute.Value,
                    relationshipId,
                    StringComparison.Ordinal
                ))
                {
                    continue;
                }
                referenceCount++;
                if (result.Count < _options.MaxReferencesPerRelationship)
                {
                    result.Add(
                        new WordRelationshipMarkupReference(
                            element.Ordinal,
                            attribute.QualifiedName
                        )
                    );
                }
            }
        }
        truncated = referenceCount > result.Count;
        return result.ToArray();
    }

    private static WordRelationshipUsageStatus Classify(
        OpcRelationship relationship,
        OwnerProjection owner,
        int referenceCount,
        bool relationshipIdDuplicated
    )
    {
        if (relationshipIdDuplicated)
        {
            return WordRelationshipUsageStatus.DuplicateRelationshipId;
        }
        if (relationship.SourcePartUri == OpcPartUri.PackageRoot)
        {
            return WordRelationshipUsageStatus.PackageRelationship;
        }
        if (owner.Kind == OwnerProjectionKind.Missing)
        {
            return WordRelationshipUsageStatus.OwnerMissing;
        }
        if (owner.Kind == OwnerProjectionKind.NonXml)
        {
            return WordRelationshipUsageStatus.OwnerNonXml;
        }
        if (owner.Kind == OwnerProjectionKind.Unparseable)
        {
            return WordRelationshipUsageStatus.OwnerXmlUnparseable;
        }
        if (referenceCount != 0)
        {
            return WordRelationshipUsageStatus.ReferencedByMarkup;
        }
        if (KnownPackageImplicitRelationshipTypes.Contains(relationship.Type))
        {
            return WordRelationshipUsageStatus.ImplicitByRelationshipType;
        }
        if (!TryKnownRelationshipTypeName(relationship.Type, out var typeName))
        {
            return WordRelationshipUsageStatus.UnknownUnreferencedRelationship;
        }
        if (ExplicitRelationshipTypeNames.Contains(typeName))
        {
            return WordRelationshipUsageStatus.UnreferencedExplicitRelationship;
        }
        if (ImplicitRelationshipTypeNames.Contains(typeName))
        {
            return WordRelationshipUsageStatus.ImplicitByRelationshipType;
        }
        return WordRelationshipUsageStatus.UnknownUnreferencedRelationship;
    }

    private static bool TryKnownRelationshipTypeName(
        string type,
        out string typeName
    )
    {
        foreach (var root in KnownRelationshipTypeRoots)
        {
            if (!type.StartsWith(root, StringComparison.Ordinal))
            {
                continue;
            }
            typeName = type[root.Length..];
            return typeName.Length != 0
                && !typeName.Contains('/', StringComparison.Ordinal);
        }
        typeName = string.Empty;
        return false;
    }

    private static bool IsXmlContentType(string? contentType) =>
        contentType is not null
        && (
            contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
        );

    private static string RelationshipFingerprint(OpcRelationship relationship) =>
        HashHex(
            relationship.SourcePartUri,
            relationship.RelationshipPartUri,
            relationship.Id,
            relationship.Type,
            relationship.Target,
            relationship.TargetMode.ToString(),
            relationship.ResolvedTargetPartUri ?? string.Empty,
            relationship.TargetFragment ?? string.Empty
        );

    private static string StableId(string prefix, params string[] values)
    {
        var bytes = HashBytes(values);
        return prefix + Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string HashHex(params string[] values) =>
        Convert.ToHexString(HashBytes(values)).ToLowerInvariant();

    private static byte[] HashBytes(params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        return hash.GetHashAndReset();
    }

    private enum OwnerProjectionKind
    {
        Package,
        Missing,
        NonXml,
        Unparseable,
        Xml,
    }

    private sealed record OwnerProjection(
        OwnerProjectionKind Kind,
        LosslessXmlDocument? Document
    )
    {
        internal static OwnerProjection Package { get; } = new(
            OwnerProjectionKind.Package,
            null
        );
        internal static OwnerProjection Missing { get; } = new(
            OwnerProjectionKind.Missing,
            null
        );
        internal static OwnerProjection NonXml { get; } = new(
            OwnerProjectionKind.NonXml,
            null
        );
        internal static OwnerProjection Unparseable { get; } = new(
            OwnerProjectionKind.Unparseable,
            null
        );
    }
}

public class WordRelationshipUsageException : IOException
{
    public WordRelationshipUsageException(string message)
        : base(message) { }
}

public sealed class WordRelationshipUsageLimitException : WordRelationshipUsageException
{
    public WordRelationshipUsageLimitException(string message)
        : base(message) { }
}
