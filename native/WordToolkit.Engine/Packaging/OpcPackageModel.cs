using System.Collections.ObjectModel;

namespace WordToolkit.Engine.Packaging;

public enum OpcDiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Fatal,
}

public sealed record OpcDiagnostic(
    string Code,
    OpcDiagnosticSeverity Severity,
    string Message,
    string? PartUri = null,
    string? RelationshipId = null
);

public enum OpcRelationshipTargetMode
{
    Internal,
    External,
    Invalid,
}

public sealed record OpcRelationship(
    string SourcePartUri,
    string RelationshipPartUri,
    string Id,
    string Type,
    string Target,
    OpcRelationshipTargetMode TargetMode,
    string? ResolvedTargetPartUri
);

public sealed record OpcPackageEntry(
    string Name,
    string? PartUri,
    long CompressedLength,
    long UncompressedLength,
    string Sha256,
    DateTimeOffset LastWriteTime,
    int ExternalAttributes,
    bool IsDirectory,
    bool IsInfrastructure,
    ReadOnlyMemory<byte> Content
);

public sealed record OpcPart(
    string Uri,
    string? ContentType,
    OpcPackageEntry Entry
);

public sealed class OpcContentTypes
{
    internal OpcContentTypes(
        IDictionary<string, string> defaults,
        IDictionary<string, string> overrides
    )
    {
        Defaults = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase)
        );
        Overrides = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(overrides, StringComparer.Ordinal)
        );
    }

    public IReadOnlyDictionary<string, string> Defaults { get; }

    public IReadOnlyDictionary<string, string> Overrides { get; }

    public string? Resolve(string partUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partUri);

        if (Overrides.TryGetValue(partUri, out var contentType))
        {
            return contentType;
        }

        var lastSlash = partUri.LastIndexOf('/');
        var lastDot = partUri.LastIndexOf('.');
        if (lastDot <= lastSlash || lastDot == partUri.Length - 1)
        {
            return null;
        }

        var extension = partUri[(lastDot + 1)..];
        return Defaults.TryGetValue(extension, out contentType) ? contentType : null;
    }
}

public sealed class OpcPackageSnapshot
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<OpcRelationship>>
        _relationshipsBySource;

    internal OpcPackageSnapshot(
        IReadOnlyList<OpcPackageEntry> entries,
        IReadOnlyDictionary<string, OpcPart> parts,
        OpcContentTypes contentTypes,
        IReadOnlyList<OpcRelationship> relationships,
        IReadOnlyList<OpcDiagnostic> diagnostics,
        string fingerprint
    )
    {
        Entries = entries;
        Parts = parts;
        ContentTypes = contentTypes;
        Relationships = relationships;
        Diagnostics = diagnostics;
        Fingerprint = fingerprint;
        _relationshipsBySource = relationships
            .GroupBy(relationship => relationship.SourcePartUri, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<OpcRelationship>)group.ToArray(),
                StringComparer.Ordinal
            );
    }

    public IReadOnlyList<OpcPackageEntry> Entries { get; }

    public IReadOnlyDictionary<string, OpcPart> Parts { get; }

    public OpcContentTypes ContentTypes { get; }

    public IReadOnlyList<OpcRelationship> Relationships { get; }

    public IReadOnlyList<OpcDiagnostic> Diagnostics { get; }

    public string Fingerprint { get; }

    public bool IsStructurallyValid => Diagnostics.All(
        diagnostic => diagnostic.Severity is not OpcDiagnosticSeverity.Error
            and not OpcDiagnosticSeverity.Fatal
    );

    public IReadOnlyList<OpcRelationship> RelationshipsFrom(string sourcePartUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePartUri);
        return _relationshipsBySource.TryGetValue(sourcePartUri, out var relationships)
            ? relationships
            : Array.Empty<OpcRelationship>();
    }
}
