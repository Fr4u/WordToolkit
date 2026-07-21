using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WordToolkit.Engine.Packaging;

public sealed class OpcPackageReader
{
    private const string ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string RelationshipsContentType =
        "application/vnd.openxmlformats-package.relationships+xml";

    private readonly OpcPackageLimits _limits;

    public OpcPackageReader(OpcPackageLimits? limits = null)
    {
        _limits = limits ?? OpcPackageLimits.Default;
        _limits.Validate();
    }

    public OpcPackageSnapshot Read(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        return Read(stream, cancellationToken);
    }

    public OpcPackageSnapshot Read(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("Package stream must be readable.", nameof(stream));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<OpcDiagnostic>();
        var entries = ReadEntries(stream, diagnostics, cancellationToken);
        AuditDuplicateNames(entries, diagnostics);

        var contentTypes = ReadContentTypes(entries, diagnostics);
        var parts = BuildParts(entries, contentTypes, diagnostics);
        var relationships = ReadRelationships(entries, parts, contentTypes, diagnostics);
        AuditRelationshipTargets(relationships, parts, diagnostics);
        AuditReachability(relationships, parts, diagnostics);

        var orderedDiagnostics = diagnostics
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.PartUri, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.RelationshipId, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

        return new OpcPackageSnapshot(
            entries.AsReadOnly(),
            new ReadOnlyDictionary<string, OpcPart>(parts),
            contentTypes,
            relationships.AsReadOnly(),
            Array.AsReadOnly(orderedDiagnostics),
            ComputeFingerprint(entries)
        );
    }

    private List<OpcPackageEntry> ReadEntries(
        Stream stream,
        ICollection<OpcDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > _limits.MaxEntries)
        {
            throw new OpcPackageLimitException(
                $"Package has {archive.Entries.Count} entries; limit is {_limits.MaxEntries}."
            );
        }

        var result = new List<OpcPackageEntry>(archive.Entries.Count);
        long totalLength = 0;
        foreach (var zipEntry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (zipEntry.Length > _limits.MaxEntryUncompressedBytes)
            {
                throw new OpcPackageLimitException(
                    $"Entry '{zipEntry.FullName}' expands to {zipEntry.Length} bytes; "
                        + $"per-entry limit is {_limits.MaxEntryUncompressedBytes}."
                );
            }

            checked
            {
                totalLength += zipEntry.Length;
            }

            if (totalLength > _limits.MaxTotalUncompressedBytes)
            {
                throw new OpcPackageLimitException(
                    $"Package expands to more than {_limits.MaxTotalUncompressedBytes} bytes."
                );
            }

            var ratio = zipEntry.Length == 0
                ? 0
                : (double)zipEntry.Length / Math.Max(1, zipEntry.CompressedLength);
            if (ratio > _limits.MaxCompressionRatio)
            {
                throw new OpcPackageLimitException(
                    $"Entry '{zipEntry.FullName}' has compression ratio {ratio:F1}; "
                        + $"limit is {_limits.MaxCompressionRatio:F1}."
                );
            }

            var isDirectory = zipEntry.FullName.EndsWith("/", StringComparison.Ordinal);
            string? partUri = null;
            if (!OpcPartUri.TryFromEntryName(zipEntry.FullName, out partUri, out var error))
            {
                diagnostics.Add(
                    new OpcDiagnostic(
                        "OPC012",
                        OpcDiagnosticSeverity.Error,
                        error ?? "Entry name is not a valid OPC part name.",
                        zipEntry.FullName
                    )
                );
            }

            if (zipEntry.Length > int.MaxValue)
            {
                throw new OpcPackageLimitException(
                    $"Entry '{zipEntry.FullName}' is too large for the in-memory snapshot."
                );
            }

            var content = GC.AllocateUninitializedArray<byte>((int)zipEntry.Length);
            using (var entryStream = zipEntry.Open())
            {
                var offset = 0;
                while (offset < content.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = entryStream.Read(content, offset, content.Length - offset);
                    if (read == 0)
                    {
                        break;
                    }

                    offset += read;
                }

                if (offset != content.Length || entryStream.ReadByte() != -1)
                {
                    throw new InvalidDataException(
                        $"Entry '{zipEntry.FullName}' length changed while reading."
                    );
                }
            }

            var isInfrastructure = string.Equals(
                    zipEntry.FullName,
                    OpcPartUri.ContentTypesEntryName,
                    StringComparison.Ordinal
                )
                || OpcPartUri.TryRelationshipSource(zipEntry.FullName, out _);
            result.Add(
                new OpcPackageEntry(
                    zipEntry.FullName,
                    partUri,
                    zipEntry.CompressedLength,
                    zipEntry.Length,
                    Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                    zipEntry.LastWriteTime,
                    zipEntry.ExternalAttributes,
                    isDirectory,
                    isInfrastructure,
                    content
                )
            );
        }

        return result;
    }

    private static void AuditDuplicateNames(
        IReadOnlyList<OpcPackageEntry> entries,
        ICollection<OpcDiagnostic> diagnostics
    )
    {
        foreach (
            var duplicate in entries
                .GroupBy(entry => entry.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            diagnostics.Add(
                new OpcDiagnostic(
                    "OPC010",
                    OpcDiagnosticSeverity.Fatal,
                    $"ZIP contains {duplicate.Count()} entries named '{duplicate.Key}'.",
                    duplicate.Key
                )
            );
        }

        foreach (
            var collision in entries
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(entry => entry.Name).Distinct().Count() > 1)
        )
        {
            diagnostics.Add(
                new OpcDiagnostic(
                    "OPC011",
                    OpcDiagnosticSeverity.Error,
                    "ZIP entry names collide under case-insensitive comparison: "
                        + string.Join(
                            ", ",
                            collision.Select(entry => $"'{entry.Name}'").Distinct()
                        ),
                    collision.Key
                )
            );
        }
    }

    private OpcContentTypes ReadContentTypes(
        IReadOnlyList<OpcPackageEntry> entries,
        ICollection<OpcDiagnostic> diagnostics
    )
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var manifest = entries.FirstOrDefault(entry => string.Equals(
            entry.Name,
            OpcPartUri.ContentTypesEntryName,
            StringComparison.Ordinal
        ));
        if (manifest is null)
        {
            diagnostics.Add(
                new OpcDiagnostic(
                    "OPC020",
                    OpcDiagnosticSeverity.Fatal,
                    "Package is missing [Content_Types].xml."
                )
            );
            return new OpcContentTypes(defaults, overrides);
        }

        try
        {
            var document = LoadMetadataXml(manifest.Content);
            var root = document.Root;
            if (root?.Name != XName.Get("Types", ContentTypesNamespace))
            {
                throw new XmlException("Root element is not the OPC Types element.");
            }

            foreach (var element in root.Elements())
            {
                if (element.Name == XName.Get("Default", ContentTypesNamespace))
                {
                    var extension = ((string?)element.Attribute("Extension"))?.Trim();
                    var contentType = ((string?)element.Attribute("ContentType"))?.Trim();
                    if (string.IsNullOrEmpty(extension) || string.IsNullOrEmpty(contentType))
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC021",
                                OpcDiagnosticSeverity.Error,
                                "Content type Default is missing Extension or ContentType.",
                                "/[Content_Types].xml"
                            )
                        );
                        continue;
                    }

                    if (!defaults.TryAdd(extension, contentType))
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC022",
                                OpcDiagnosticSeverity.Error,
                                $"Duplicate default content type for extension '{extension}'.",
                                "/[Content_Types].xml"
                            )
                        );
                    }
                }
                else if (element.Name == XName.Get("Override", ContentTypesNamespace))
                {
                    var rawPartName = ((string?)element.Attribute("PartName"))?.Trim();
                    var contentType = ((string?)element.Attribute("ContentType"))?.Trim();
                    if (
                        string.IsNullOrEmpty(rawPartName)
                        || string.IsNullOrEmpty(contentType)
                        || !TryCanonicalOverride(rawPartName, out var partName)
                    )
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC021",
                                OpcDiagnosticSeverity.Error,
                                "Content type Override has an invalid PartName or ContentType.",
                                "/[Content_Types].xml"
                            )
                        );
                        continue;
                    }

                    if (!overrides.TryAdd(partName!, contentType))
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC023",
                                OpcDiagnosticSeverity.Error,
                                $"Duplicate override content type for part '{partName}'.",
                                "/[Content_Types].xml"
                            )
                        );
                    }
                }
            }
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            diagnostics.Add(
                new OpcDiagnostic(
                    "OPC021",
                    OpcDiagnosticSeverity.Fatal,
                    $"Cannot parse [Content_Types].xml safely: {exception.Message}",
                    "/[Content_Types].xml"
                )
            );
        }

        return new OpcContentTypes(defaults, overrides);
    }

    private static bool TryCanonicalOverride(string rawPartName, out string? partName)
    {
        partName = null;
        if (!rawPartName.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        return OpcPartUri.TryFromEntryName(rawPartName[1..], out partName, out _);
    }

    private static Dictionary<string, OpcPart> BuildParts(
        IReadOnlyList<OpcPackageEntry> entries,
        OpcContentTypes contentTypes,
        ICollection<OpcDiagnostic> diagnostics
    )
    {
        var parts = new Dictionary<string, OpcPart>(StringComparer.Ordinal);
        foreach (
            var entry in entries.Where(entry =>
                !entry.IsDirectory && !entry.IsInfrastructure && entry.PartUri is not null
            )
        )
        {
            var contentType = contentTypes.Resolve(entry.PartUri!);
            if (contentType is null)
            {
                diagnostics.Add(
                    new OpcDiagnostic(
                        "OPC024",
                        OpcDiagnosticSeverity.Error,
                        "Part has no matching content type Default or Override.",
                        entry.PartUri
                    )
                );
            }

            if (!parts.TryAdd(entry.PartUri!, new OpcPart(entry.PartUri!, contentType, entry)))
            {
                diagnostics.Add(
                    new OpcDiagnostic(
                        "OPC013",
                        OpcDiagnosticSeverity.Fatal,
                        "Multiple ZIP entries resolve to the same canonical part URI.",
                        entry.PartUri
                    )
                );
            }
        }

        return parts;
    }

    private List<OpcRelationship> ReadRelationships(
        IReadOnlyList<OpcPackageEntry> entries,
        IReadOnlyDictionary<string, OpcPart> parts,
        OpcContentTypes contentTypes,
        ICollection<OpcDiagnostic> diagnostics
    )
    {
        var relationshipEntries = entries
            .Where(entry =>
                !entry.IsDirectory
                && OpcPartUri.TryRelationshipSource(entry.Name, out _)
            )
            .ToArray();
        if (!relationshipEntries.Any(entry => string.Equals(
            entry.Name,
            OpcPartUri.RootRelationshipsEntryName,
            StringComparison.Ordinal
        )))
        {
            diagnostics.Add(
                new OpcDiagnostic(
                    "OPC030",
                    OpcDiagnosticSeverity.Error,
                    "Package is missing the root relationship part _rels/.rels."
                )
            );
        }

        var result = new List<OpcRelationship>();
        foreach (var entry in relationshipEntries)
        {
            _ = OpcPartUri.TryRelationshipSource(entry.Name, out var sourcePartUri);
            if (sourcePartUri is null)
            {
                continue;
            }

            if (
                entry.PartUri is null
                || !string.Equals(
                    contentTypes.Resolve(entry.PartUri),
                    RelationshipsContentType,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                diagnostics.Add(
                    new OpcDiagnostic(
                        "OPC041",
                        OpcDiagnosticSeverity.Error,
                        "Relationship part does not have the required OPC relationships content type.",
                        entry.PartUri ?? "/" + entry.Name
                    )
                );
            }

            if (
                sourcePartUri != OpcPartUri.PackageRoot
                && OpcPartUri.IsRelationshipPartUri(sourcePartUri)
            )
            {
                diagnostics.Add(
                    new OpcDiagnostic(
                        "OPC042",
                        OpcDiagnosticSeverity.Error,
                        "A relationship part cannot itself own relationships.",
                        entry.PartUri ?? "/" + entry.Name
                    )
                );
            }

            if (sourcePartUri != OpcPartUri.PackageRoot && !parts.ContainsKey(sourcePartUri))
            {
                diagnostics.Add(
                    new OpcDiagnostic(
                        "OPC031",
                        OpcDiagnosticSeverity.Error,
                        "Relationship part belongs to a source part that does not exist.",
                        sourcePartUri
                    )
                );
            }

            try
            {
                var document = LoadMetadataXml(entry.Content);
                var root = document.Root;
                if (root?.Name != XName.Get("Relationships", RelationshipsNamespace))
                {
                    throw new XmlException(
                        "Root element is not the OPC Relationships element."
                    );
                }

                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (
                    var element in root.Elements(
                        XName.Get("Relationship", RelationshipsNamespace)
                    )
                )
                {
                    var id = ((string?)element.Attribute("Id"))?.Trim();
                    var type = ((string?)element.Attribute("Type"))?.Trim();
                    var target = ((string?)element.Attribute("Target"))?.Trim();
                    var rawMode = ((string?)element.Attribute("TargetMode"))?.Trim();
                    if (
                        string.IsNullOrEmpty(id)
                        || string.IsNullOrEmpty(type)
                        || string.IsNullOrEmpty(target)
                    )
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC031",
                                OpcDiagnosticSeverity.Error,
                                "Relationship is missing Id, Type, or Target.",
                                sourcePartUri
                            )
                        );
                        continue;
                    }

                    if (!ids.Add(id))
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC032",
                                OpcDiagnosticSeverity.Error,
                                $"Relationship ID '{id}' is duplicated for its source part.",
                                sourcePartUri,
                                id
                            )
                        );
                    }


                    try
                    {
                        _ = XmlConvert.VerifyNCName(id);
                    }
                    catch (XmlException)
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC037",
                                OpcDiagnosticSeverity.Error,
                                "Relationship Id is not a valid XML ID.",
                                sourcePartUri,
                                id
                            )
                        );
                    }

                    if (!OpcPartUri.TryValidateRelationshipType(type, out var typeError))
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC038",
                                OpcDiagnosticSeverity.Error,
                                typeError ?? "Relationship Type is not a valid URI.",
                                sourcePartUri,
                                id
                            )
                        );
                    }

                    var mode = rawMode switch
                    {
                        null or "" => OpcRelationshipTargetMode.Internal,
                        _ when string.Equals(
                            rawMode,
                            "Internal",
                            StringComparison.OrdinalIgnoreCase
                        ) => OpcRelationshipTargetMode.Internal,
                        _ when string.Equals(
                            rawMode,
                            "External",
                            StringComparison.OrdinalIgnoreCase
                        ) => OpcRelationshipTargetMode.External,
                        _ => OpcRelationshipTargetMode.Invalid,
                    };
                    string? resolvedTarget = null;
                    string? targetFragment = null;
                    if (mode == OpcRelationshipTargetMode.Invalid)
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC036",
                                OpcDiagnosticSeverity.Error,
                                $"Relationship TargetMode '{rawMode}' is invalid.",
                                sourcePartUri,
                                id
                            )
                        );
                    }
                    else if (mode == OpcRelationshipTargetMode.External)
                    {
                        if (
                            !OpcPartUri.TryValidateExternalRelationshipTarget(
                                target,
                                out var targetError
                            )
                        )
                        {
                            diagnostics.Add(
                                new OpcDiagnostic(
                                    "OPC039",
                                    OpcDiagnosticSeverity.Error,
                                    targetError
                                        ?? "External relationship target is not a valid URI reference.",
                                    sourcePartUri,
                                    id
                                )
                            );
                        }

                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC035",
                                OpcDiagnosticSeverity.Info,
                                "External relationship was recorded but not dereferenced.",
                                sourcePartUri,
                                id
                            )
                        );
                    }
                    else if (
                        !OpcPartUri.TryResolveRelationshipTarget(
                            sourcePartUri,
                            target,
                            out resolvedTarget,
                            out targetFragment,
                            out var error
                        )
                    )
                    {
                        diagnostics.Add(
                            new OpcDiagnostic(
                                "OPC033",
                                OpcDiagnosticSeverity.Error,
                                error ?? "Internal relationship target is invalid.",
                                sourcePartUri,
                                id
                            )
                        );
                    }

                    result.Add(
                        new OpcRelationship(
                            sourcePartUri,
                            entry.PartUri ?? "/" + entry.Name,
                            id,
                            type,
                            target,
                            mode,
                            resolvedTarget,
                            targetFragment
                        )
                    );
                }
            }
            catch (Exception exception) when (exception is XmlException or InvalidOperationException)
            {
                diagnostics.Add(
                    new OpcDiagnostic(
                        "OPC031",
                        OpcDiagnosticSeverity.Fatal,
                        $"Cannot parse relationship part safely: {exception.Message}",
                        entry.PartUri ?? "/" + entry.Name
                    )
                );
            }
        }

        return result;
    }

    private static void AuditRelationshipTargets(
        IEnumerable<OpcRelationship> relationships,
        IReadOnlyDictionary<string, OpcPart> parts,
        ICollection<OpcDiagnostic> diagnostics
    )
    {
        foreach (var relationship in relationships)
        {
            if (
                relationship.TargetMode != OpcRelationshipTargetMode.Internal
                || relationship.ResolvedTargetPartUri is null
            )
            {
                continue;
            }

            if (OpcPartUri.IsPackageInfrastructureUri(relationship.ResolvedTargetPartUri))
            {
                diagnostics.Add(
                    new OpcDiagnostic(
                        "OPC043",
                        OpcDiagnosticSeverity.Error,
                        "An internal relationship cannot target package infrastructure.",
                        relationship.SourcePartUri,
                        relationship.Id
                    )
                );
                continue;
            }

            if (parts.ContainsKey(relationship.ResolvedTargetPartUri))
            {
                continue;
            }

            diagnostics.Add(
                new OpcDiagnostic(
                    "OPC034",
                    OpcDiagnosticSeverity.Error,
                    $"Internal relationship target '{relationship.ResolvedTargetPartUri}' does not exist.",
                    relationship.SourcePartUri,
                    relationship.Id
                )
            );
        }
    }

    private static void AuditReachability(
        IReadOnlyList<OpcRelationship> relationships,
        IReadOnlyDictionary<string, OpcPart> parts,
        ICollection<OpcDiagnostic> diagnostics
    )
    {
        var bySource = relationships
            .Where(relationship =>
                relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.ResolvedTargetPartUri is not null
            )
            .GroupBy(relationship => relationship.SourcePartUri, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(relationship => relationship.ResolvedTargetPartUri!).ToArray(),
                StringComparer.Ordinal
            );
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(OpcPartUri.PackageRoot);
        while (queue.TryDequeue(out var source))
        {
            if (!bySource.TryGetValue(source, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (parts.ContainsKey(target) && reachable.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        foreach (var orphan in parts.Keys.Where(partUri => !reachable.Contains(partUri)))
        {
            diagnostics.Add(
                new OpcDiagnostic(
                    "OPC040",
                    OpcDiagnosticSeverity.Warning,
                    "Part is not reachable from any package-level relationship.",
                    orphan
                )
            );
        }
    }

    private XDocument LoadMetadataXml(ReadOnlyMemory<byte> content)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = _limits.MaxMetadataXmlCharacters,
                IgnoreComments = false,
                IgnoreWhitespace = false,
            }
        );
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    private static string ComputeFingerprint(IEnumerable<OpcPackageEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (
            var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Sha256, StringComparer.Ordinal)
        )
        {
            AppendHashField(hash, entry.Name);
            AppendHashField(hash, entry.Sha256);
            AppendHashField(hash, entry.UncompressedLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
