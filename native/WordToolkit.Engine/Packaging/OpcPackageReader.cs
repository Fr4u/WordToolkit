using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

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
    private readonly WordOperationResourceLease? _resourceLease;

    public OpcPackageReader(OpcPackageLimits? limits = null)
    {
        _limits = limits ?? OpcPackageLimits.Default;
        _limits.Validate();
    }

    public OpcPackageReader(
        OpcPackageLimits? limits,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _limits = limits ?? OpcPackageLimits.Default;
        _resourceLease = resourceLease;
        _limits.Validate();
    }

    public OpcPackageSnapshot Read(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = StablePackagePathSnapshot.Capture(
            Path.GetFullPath(path), _limits.MaxArchiveBytes, cancellationToken);
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
        if (!stream.CanSeek)
        {
            using var seekable = SpoolToBoundedSeekableStream(
                stream,
                cancellationToken
            );
            return Read(seekable, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.OpcPackage
        );
        var diagnostics = new BoundedDiagnosticCollection(
            _limits.MaxDiagnostics,
            _resourceLease
        );
        var entries = ReadEntries(stream, diagnostics, cancellationToken);
        AuditDuplicateNames(entries, diagnostics, cancellationToken);

        var contentTypes = ReadContentTypes(entries, diagnostics, cancellationToken);
        var parts = BuildParts(
            entries,
            contentTypes,
            diagnostics,
            cancellationToken
        );
        var relationships = ReadRelationships(
            entries,
            parts,
            contentTypes,
            diagnostics,
            cancellationToken
        );
        AuditRelationshipTargets(
            relationships,
            parts,
            diagnostics,
            cancellationToken
        );
        AuditReachability(relationships, parts, diagnostics, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

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
            OpcPackageFingerprint.Compute(entries),
            cancellationToken
        );
    }

    private List<OpcPackageEntry> ReadEntries(
        Stream stream,
        ICollection<OpcDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        var preflight = stream.CanSeek
            ? PreflightZipDirectory(stream, cancellationToken)
            : null;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var archiveEntries = archive.Entries;
        if (preflight is not null && archiveEntries.Count != preflight.EntryCount)
        {
            throw new InvalidDataException(
                "ZIP central-directory entry count changed after preflight."
            );
        }
        if (archiveEntries.Count > _limits.MaxEntries)
        {
            throw new OpcPackageLimitException(
                $"Package has {archiveEntries.Count} entries; limit is {_limits.MaxEntries}."
            );
        }

        var result = new List<OpcPackageEntry>(archiveEntries.Count);
        long totalLength = 0;
        foreach (var zipEntry in archiveEntries)
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

            WordOperationResourceAccounting.ChargePackageEntry(
                _resourceLease,
                zipEntry.FullName,
                zipEntry.Length
            );
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

    private EncryptedTemporaryStream SpoolToBoundedSeekableStream(
        Stream source,
        CancellationToken cancellationToken
    )
    {
        const int bufferBytes = 80 * 1024;
        WordOperationResourceAccounting.ChargeZipPreflightBuffer(
            _resourceLease,
            bufferBytes
        );
        var buffer = GC.AllocateUninitializedArray<byte>(bufferBytes);
        var target = new EncryptedTemporaryStream(_limits.MaxArchiveBytes);
        try
        {
            long totalBytes = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }
                if (totalBytes > _limits.MaxArchiveBytes - read)
                {
                    throw new OpcPackageLimitException(
                        $"Package stream exceeds {_limits.MaxArchiveBytes} bytes."
                    );
                }
                target.Write(buffer, 0, read);
                totalBytes += read;
            }
            target.CompleteWriting();
            target.Position = 0;
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private ZipDirectoryPreflight PreflightZipDirectory(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        const uint endOfCentralDirectorySignature = 0x06054b50;
        const uint zip64LocatorSignature = 0x07064b50;
        const uint zip64EndOfCentralDirectorySignature = 0x06064b50;
        const int minimumEndRecordBytes = 22;
        const int maximumCommentBytes = ushort.MaxValue;

        var archiveStart = stream.Position;
        var archiveLength = stream.Length - archiveStart;
        if (archiveLength > _limits.MaxArchiveBytes)
        {
            throw new OpcPackageLimitException(
                $"Package stream has {archiveLength} bytes; limit is {_limits.MaxArchiveBytes}."
            );
        }
        if (archiveLength < minimumEndRecordBytes)
        {
            throw new InvalidDataException("ZIP end-of-central-directory record is missing.");
        }

        var tailLength = checked(
            (int)Math.Min(
                archiveLength,
                minimumEndRecordBytes + maximumCommentBytes
            )
        );
        WordOperationResourceAccounting.ChargeZipPreflightBuffer(
            _resourceLease,
            tailLength
        );
        var tail = GC.AllocateUninitializedArray<byte>(tailLength);
        var tailStart = stream.Length - tailLength;
        try
        {
            ReadExactlyAt(stream, tailStart, tail, cancellationToken);
            var recordOffset = -1;
            for (var index = tail.Length - minimumEndRecordBytes; index >= 0; index--)
            {
                if (
                    BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index, 4))
                    != endOfCentralDirectorySignature
                )
                {
                    continue;
                }
                var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                    tail.AsSpan(index + 20, 2)
                );
                if (index + minimumEndRecordBytes + commentLength == tail.Length)
                {
                    recordOffset = index;
                    break;
                }
            }
            if (recordOffset < 0)
            {
                throw new InvalidDataException(
                    "ZIP end-of-central-directory record is missing or malformed."
                );
            }

            var record = tail.AsSpan(recordOffset, minimumEndRecordBytes);
            var diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(record[4..6]);
            var directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..8]);
            var entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..10]);
            var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..12]);
            long directoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(record[12..16]);
            long directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..20]);
            long entryCount = totalEntries;
            var endRecordPosition = checked(tailStart + recordOffset);

            var requiresZip64 = entriesOnDisk == ushort.MaxValue
                || totalEntries == ushort.MaxValue
                || directoryBytes == uint.MaxValue
                || directoryOffset == uint.MaxValue;
            if (requiresZip64)
            {
                var locatorPosition = endRecordPosition - 20;
                if (locatorPosition < archiveStart)
                {
                    throw new InvalidDataException("ZIP64 locator is missing.");
                }
                Span<byte> locator = stackalloc byte[20];
                ReadExactlyAt(stream, locatorPosition, locator, cancellationToken);
                if (
                    BinaryPrimitives.ReadUInt32LittleEndian(locator[0..4])
                    != zip64LocatorSignature
                )
                {
                    throw new InvalidDataException("ZIP64 locator is missing or malformed.");
                }
                if (
                    BinaryPrimitives.ReadUInt32LittleEndian(locator[4..8]) != 0
                    || BinaryPrimitives.ReadUInt32LittleEndian(locator[16..20]) != 1
                )
                {
                    throw new InvalidDataException("Multi-disk ZIP packages are not supported.");
                }
                var zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..16]);
                if (zip64Offset > long.MaxValue)
                {
                    throw new InvalidDataException("ZIP64 directory offset is too large.");
                }
                if ((long)zip64Offset > archiveLength)
                {
                    throw new InvalidDataException(
                        "ZIP64 end-of-central-directory offset is outside the archive."
                    );
                }
                var zip64Position = archiveStart + (long)zip64Offset;
                Span<byte> zip64Record = stackalloc byte[56];
                ReadExactlyAt(stream, zip64Position, zip64Record, cancellationToken);
                if (
                    BinaryPrimitives.ReadUInt32LittleEndian(zip64Record[0..4])
                    != zip64EndOfCentralDirectorySignature
                    || BinaryPrimitives.ReadUInt64LittleEndian(zip64Record[4..12]) < 44
                )
                {
                    throw new InvalidDataException(
                        "ZIP64 end-of-central-directory record is malformed."
                    );
                }
                if (
                    BinaryPrimitives.ReadUInt32LittleEndian(zip64Record[16..20]) != 0
                    || BinaryPrimitives.ReadUInt32LittleEndian(zip64Record[20..24]) != 0
                )
                {
                    throw new InvalidDataException("Multi-disk ZIP packages are not supported.");
                }
                var zip64EntriesOnDisk = BinaryPrimitives.ReadUInt64LittleEndian(
                    zip64Record[24..32]
                );
                var zip64TotalEntries = BinaryPrimitives.ReadUInt64LittleEndian(
                    zip64Record[32..40]
                );
                var zip64DirectoryBytes = BinaryPrimitives.ReadUInt64LittleEndian(
                    zip64Record[40..48]
                );
                var zip64DirectoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(
                    zip64Record[48..56]
                );
                if (
                    zip64EntriesOnDisk != zip64TotalEntries
                    || zip64TotalEntries > long.MaxValue
                    || zip64DirectoryBytes > long.MaxValue
                    || zip64DirectoryOffset > long.MaxValue
                )
                {
                    throw new InvalidDataException("ZIP64 directory metadata is unsupported.");
                }
                entryCount = (long)zip64TotalEntries;
                directoryBytes = (long)zip64DirectoryBytes;
                directoryOffset = (long)zip64DirectoryOffset;
            }
            else if (
                diskNumber != 0
                || directoryDisk != 0
                || entriesOnDisk != totalEntries
            )
            {
                throw new InvalidDataException("Multi-disk ZIP packages are not supported.");
            }

            if (entryCount > _limits.MaxEntries)
            {
                throw new OpcPackageLimitException(
                    $"Package has {entryCount} entries; limit is {_limits.MaxEntries}."
                );
            }
            if (directoryBytes > _limits.MaxCentralDirectoryBytes)
            {
                throw new OpcPackageLimitException(
                    $"ZIP central directory has {directoryBytes} bytes; limit is {_limits.MaxCentralDirectoryBytes}."
                );
            }
            if (
                directoryOffset < 0
                || directoryBytes < 0
                || directoryOffset > archiveLength
                || directoryBytes > archiveLength - directoryOffset
                || directoryOffset > endRecordPosition - archiveStart
                || directoryBytes
                    > endRecordPosition - archiveStart - directoryOffset
            )
            {
                throw new InvalidDataException("ZIP central-directory bounds are invalid.");
            }
            WordOperationResourceAccounting.ChargeZipCentralDirectory(
                _resourceLease,
                directoryBytes,
                entryCount
            );
            return new ZipDirectoryPreflight(checked((int)entryCount));
        }
        finally
        {
            stream.Position = archiveStart;
        }
    }

    private static void ReadExactlyAt(
        Stream stream,
        long position,
        Span<byte> buffer,
        CancellationToken cancellationToken
    )
    {
        stream.Position = position;
        var offset = 0;
        while (offset < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                throw new InvalidDataException("ZIP metadata ended unexpectedly.");
            }
            offset += read;
        }
    }

    private sealed record ZipDirectoryPreflight(int EntryCount);

    private void AuditDuplicateNames(
        IReadOnlyList<OpcPackageEntry> entries,
        ICollection<OpcDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var duplicate in entries
                .GroupBy(entry => entry.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            var spellingCount = collision
                .Select(entry => entry.Name)
                .Distinct(StringComparer.Ordinal)
                .Count();
            diagnostics.Add(
                new OpcDiagnostic(
                    "OPC011",
                    OpcDiagnosticSeverity.Error,
                    $"ZIP entry names have {spellingCount} spellings that collide under case-insensitive comparison.",
                    collision.Key
                )
            );
        }
    }

    private OpcContentTypes ReadContentTypes(
        IReadOnlyList<OpcPackageEntry> entries,
        ICollection<OpcDiagnostic> diagnostics,
        CancellationToken cancellationToken
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
            var document = LoadMetadataXml(manifest.Content, cancellationToken);
            var root = document.Root;
            if (root?.Name != XName.Get("Types", ContentTypesNamespace))
            {
                throw new XmlException("Root element is not the OPC Types element.");
            }

            var declarationCount = 0;
            foreach (var element in root.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++declarationCount > _limits.MaxContentTypeDeclarations)
                {
                    throw new OpcPackageLimitException(
                        $"Content-type declarations exceed {_limits.MaxContentTypeDeclarations}."
                    );
                }
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

                    WordOperationResourceAccounting.ChargePackageContentType(
                        _resourceLease,
                        extension,
                        contentType
                    );
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

                    WordOperationResourceAccounting.ChargePackageContentType(
                        _resourceLease,
                        partName!,
                        contentType
                    );
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
        catch (LosslessXmlLimitException exception)
        {
            throw new OpcPackageLimitException(
                $"Content-types metadata exceeds a bounded XML limit: {exception.Message}"
            );
        }
        catch (Exception exception) when (
            exception is XmlException
                or InvalidOperationException
                or LosslessXmlParseException
                or LosslessXmlEncodingException
        )
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

    private Dictionary<string, OpcPart> BuildParts(
        IReadOnlyList<OpcPackageEntry> entries,
        OpcContentTypes contentTypes,
        ICollection<OpcDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        var parts = new Dictionary<string, OpcPart>(StringComparer.Ordinal);
        foreach (
            var entry in entries.Where(entry =>
                !entry.IsDirectory && !entry.IsInfrastructure && entry.PartUri is not null
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            WordOperationResourceAccounting.ChargePackagePart(
                _resourceLease,
                entry.PartUri!,
                contentType
            );
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
        ICollection<OpcDiagnostic> diagnostics,
        CancellationToken cancellationToken
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
        var relationshipCount = 0;
        foreach (var entry in relationshipEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                var document = LoadMetadataXml(entry.Content, cancellationToken);
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
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++relationshipCount > _limits.MaxRelationships)
                    {
                        throw new OpcPackageLimitException(
                            $"Package relationships exceed {_limits.MaxRelationships}."
                        );
                    }
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

                    var relationshipPartUri = entry.PartUri ?? "/" + entry.Name;
                    WordOperationResourceAccounting.ChargePackageRelationship(
                        _resourceLease,
                        sourcePartUri,
                        relationshipPartUri,
                        id,
                        type,
                        target,
                        resolvedTarget,
                        targetFragment
                    );
                    result.Add(
                        new OpcRelationship(
                            sourcePartUri,
                            relationshipPartUri,
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
            catch (LosslessXmlLimitException exception)
            {
                throw new OpcPackageLimitException(
                    $"Relationship metadata exceeds a bounded XML limit: {exception.Message}"
                );
            }
            catch (Exception exception) when (
                exception is XmlException
                    or InvalidOperationException
                    or LosslessXmlParseException
                    or LosslessXmlEncodingException
            )
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

    private void AuditRelationshipTargets(
        IEnumerable<OpcRelationship> relationships,
        IReadOnlyDictionary<string, OpcPart> parts,
        ICollection<OpcDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        foreach (var relationship in relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private void AuditReachability(
        IReadOnlyList<OpcRelationship> relationships,
        IReadOnlyDictionary<string, OpcPart> parts,
        ICollection<OpcDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        var bySource = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var relationship in relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                relationship.TargetMode != OpcRelationshipTargetMode.Internal
                || relationship.ResolvedTargetPartUri is null
            )
            {
                continue;
            }
            if (!bySource.TryGetValue(relationship.SourcePartUri, out var targets))
            {
                targets = [];
                bySource.Add(relationship.SourcePartUri, targets);
            }
            targets.Add(relationship.ResolvedTargetPartUri);
        }
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(OpcPartUri.PackageRoot);
        while (queue.TryDequeue(out var source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!bySource.TryGetValue(source, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (parts.ContainsKey(target) && reachable.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        foreach (var orphan in parts.Keys.Where(partUri => !reachable.Contains(partUri)))
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private XDocument LoadMetadataXml(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken
    )
    {
        var options = new LosslessXmlOptions
        {
            MaxSourceBytes = checked(
                (int)Math.Min(int.MaxValue, _limits.MaxEntryUncompressedBytes)
            ),
            MaxXmlCharacters = _limits.MaxMetadataXmlCharacters,
            MaxXmlElements = _limits.MaxMetadataXmlElements,
            MaxXmlDepth = 256,
            MaxTextCharacters = _limits.MaxMetadataXmlCharacters,
        };
        var source = _resourceLease is null
            ? LosslessXmlDocument.Parse(content, options, cancellationToken)
            : LosslessXmlDocument.Parse(
                content,
                options,
                _resourceLease,
                WordOperationResourceStage.OpcPackage,
                cancellationToken
            );
        return source.ParsedDocument;
    }

    private sealed class BoundedDiagnosticCollection : ICollection<OpcDiagnostic>
    {
        private readonly List<OpcDiagnostic> _items = [];
        private readonly int _maximum;
        private readonly WordOperationResourceLease? _resourceLease;

        public BoundedDiagnosticCollection(
            int maximum,
            WordOperationResourceLease? resourceLease
        )
        {
            _maximum = maximum;
            _resourceLease = resourceLease;
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(OpcDiagnostic item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (_items.Count >= _maximum)
            {
                throw new OpcPackageLimitException(
                    $"Package diagnostics exceed {_maximum}."
                );
            }
            WordOperationResourceAccounting.ChargePackageDiagnostic(
                _resourceLease,
                item.Code,
                item.Message,
                item.PartUri,
                item.RelationshipId
            );
            _items.Add(item);
        }

        public void Clear() => _items.Clear();

        public bool Contains(OpcDiagnostic item) => _items.Contains(item);

        public void CopyTo(OpcDiagnostic[] array, int arrayIndex) =>
            _items.CopyTo(array, arrayIndex);

        public bool Remove(OpcDiagnostic item) => _items.Remove(item);

        public IEnumerator<OpcDiagnostic> GetEnumerator() => _items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

}
