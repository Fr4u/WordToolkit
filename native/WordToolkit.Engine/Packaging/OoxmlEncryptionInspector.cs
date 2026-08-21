using System.Buffers.Binary;
using System.Text;

namespace WordToolkit.Engine.Packaging;

public sealed record OoxmlEncryptionInspectionLimits
{
    public static OoxmlEncryptionInspectionLimits Default { get; } = new();

    public long MaxFileBytes { get; init; } = 576L * 1024 * 1024;

    public int MaxDirectoryEntries { get; init; } = 65_536;

    public int MaxChainSectors { get; init; } = 1_200_000;

    internal void Validate()
    {
        if (MaxFileBytes < 512)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFileBytes));
        }
        if (MaxDirectoryEntries is < 4 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDirectoryEntries));
        }
        if (MaxChainSectors is < 4 or > 2_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChainSectors));
        }
    }
}

public sealed class OoxmlEncryptionInspectionLimitException : IOException
{
    public OoxmlEncryptionInspectionLimitException(string message)
        : base(message)
    {
    }
}

internal sealed record OoxmlEncryptionProbe(
    string ContainerKind,
    string EncryptionState,
    bool IsEncryptedOoxml,
    bool CompleteEncryptionContainer,
    bool HasEncryptionInfoStream,
    bool HasEncryptedPackageStream,
    bool HasDataSpacesStorage,
    string EncryptionInfoVariant,
    int? EncryptionInfoMajor,
    int? EncryptionInfoMinor,
    int? CompoundFileMajorVersion,
    int? SectorSize,
    int DirectoryEntryCount,
    int RootChildCount,
    IReadOnlyList<string> IssueCodes
);

/// <summary>
/// Performs a bounded, read-only probe for the ECMA-376 encrypted-package container.
/// It reads only Compound File Binary metadata and at most eight bytes from the
/// EncryptionInfo stream. It never accepts a password or decrypts package content.
/// </summary>
internal sealed class OoxmlEncryptionInspector
{
    private static ReadOnlySpan<byte> CompoundFileSignature =>
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;
    private const uint NoStream = 0xFFFFFFFF;
    private const uint MaximumRegularSector = 0xFFFFFFFA;
    private const uint MaximumSurplusFatSectors = 109;
    private const string EncryptionInfoName = "EncryptionInfo";
    private const string EncryptedPackageName = "EncryptedPackage";
    private const string DataSpacesName = "\u0006DataSpaces";

    private readonly OoxmlEncryptionInspectionLimits _limits;

    public OoxmlEncryptionInspector(OoxmlEncryptionInspectionLimits? limits = null)
    {
        _limits = limits ?? OoxmlEncryptionInspectionLimits.Default;
        _limits.Validate();
    }

    public OoxmlEncryptionProbe Inspect(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Stream must be readable and seekable.", nameof(stream));
        }
        if (stream.Length > _limits.MaxFileBytes)
        {
            throw new OoxmlEncryptionInspectionLimitException(
                "The file exceeds the bounded encryption-inspection byte limit."
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (stream.Length < 8)
        {
            return Unknown("file_too_short");
        }

        Span<byte> prefix = stackalloc byte[8];
        ReadExactlyAt(stream, 0, prefix);
        if (IsZipSignature(prefix))
        {
            return new OoxmlEncryptionProbe(
                "opc_zip_candidate",
                "not_encrypted",
                false,
                false,
                false,
                false,
                false,
                "not_applicable",
                null,
                null,
                null,
                null,
                0,
                0,
                Array.Empty<string>()
            );
        }
        if (!prefix.SequenceEqual(CompoundFileSignature))
        {
            return Unknown("unrecognized_container_signature");
        }

        try
        {
            return InspectCompoundFile(stream, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OoxmlEncryptionInspectionLimitException)
        {
            throw;
        }
        catch (CompoundFileFormatException exception)
        {
            return new OoxmlEncryptionProbe(
                "malformed_compound_file",
                "indeterminate",
                false,
                false,
                false,
                false,
                false,
                "unknown",
                null,
                null,
                exception.MajorVersion,
                exception.SectorSize,
                exception.DirectoryEntryCount,
                exception.RootChildCount,
                Array.AsReadOnly(new[] { exception.Code })
            );
        }
        catch (OverflowException)
        {
            return new OoxmlEncryptionProbe(
                "malformed_compound_file",
                "indeterminate",
                false,
                false,
                false,
                false,
                false,
                "unknown",
                null,
                null,
                null,
                null,
                0,
                0,
                Array.AsReadOnly(new[] { "cfb_numeric_bounds_invalid" })
            );
        }
    }

    private OoxmlEncryptionProbe InspectCompoundFile(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        if (stream.Length < 512)
        {
            throw Invalid("cfb_header_truncated");
        }
        var header = new byte[512];
        ReadExactlyAt(stream, 0, header);
        if (!header.AsSpan(0, 8).SequenceEqual(CompoundFileSignature))
        {
            throw Invalid("cfb_signature_invalid");
        }
        if (!header.AsSpan(8, 16).SequenceEqual(new byte[16]))
        {
            throw Invalid("cfb_header_clsid_invalid");
        }

        var majorVersion = ReadUInt16(header, 26);
        var byteOrder = ReadUInt16(header, 28);
        var sectorShift = ReadUInt16(header, 30);
        var miniSectorShift = ReadUInt16(header, 32);
        if (byteOrder != 0xFFFE)
        {
            throw Invalid("cfb_byte_order_invalid", majorVersion);
        }
        if (
            (majorVersion == 3 && sectorShift != 9)
            || (majorVersion == 4 && sectorShift != 12)
            || majorVersion is not (3 or 4)
        )
        {
            throw Invalid("cfb_version_or_sector_shift_invalid", majorVersion);
        }
        if (miniSectorShift != 6 || header.AsSpan(34, 6).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw Invalid("cfb_header_reserved_fields_invalid", majorVersion, 1 << sectorShift);
        }

        var sectorSize = 1 << sectorShift;
        if (stream.Length < checked(2L * sectorSize) || stream.Length % sectorSize != 0)
        {
            throw Invalid("cfb_file_size_invalid", majorVersion, sectorSize);
        }
        var sectorCount64 = stream.Length / sectorSize - 1;
        if (sectorCount64 > _limits.MaxChainSectors || sectorCount64 > MaximumRegularSector)
        {
            throw new OoxmlEncryptionInspectionLimitException(
                "The compound file exceeds the bounded sector-count limit."
            );
        }
        var sectorCount = checked((int)sectorCount64);
        var directorySectorCount = ReadUInt32(header, 40);
        var fatSectorCount = ReadUInt32(header, 44);
        var firstDirectorySector = ReadUInt32(header, 48);
        var miniStreamCutoff = ReadUInt32(header, 56);
        var firstMiniFatSector = ReadUInt32(header, 60);
        var miniFatSectorCount = ReadUInt32(header, 64);
        var firstDifatSector = ReadUInt32(header, 68);
        var difatSectorCount = ReadUInt32(header, 72);
        if ((majorVersion == 3 && directorySectorCount != 0) || miniStreamCutoff != 4096)
        {
            throw Invalid("cfb_header_counts_invalid", majorVersion, sectorSize);
        }
        var minimumFatSectorCount = checked(
            (uint)((sectorCount + (sectorSize / 4) - 1) / (sectorSize / 4))
        );
        if (
            fatSectorCount < minimumFatSectorCount
            || fatSectorCount > sectorCount
            || fatSectorCount > minimumFatSectorCount + MaximumSurplusFatSectors
        )
        {
            throw Invalid("cfb_fat_count_invalid", majorVersion, sectorSize);
        }
        if (difatSectorCount > sectorCount || miniFatSectorCount > sectorCount)
        {
            throw Invalid("cfb_auxiliary_sector_count_invalid", majorVersion, sectorSize);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var difat = ReadDifat(
            stream,
            header,
            sectorSize,
            sectorCount,
            fatSectorCount,
            firstDifatSector,
            difatSectorCount,
            cancellationToken
        );
        var fat = ReadFat(
            stream,
            sectorSize,
            sectorCount,
            difat.FatSectorIds,
            cancellationToken
        );
        foreach (var difatSectorId in difat.DifatSectorIds)
        {
            if (fat[difatSectorId] != DifatSector)
            {
                throw Invalid("cfb_difat_self_marker_invalid", majorVersion, sectorSize);
            }
        }
        var entriesPerDirectorySector = sectorSize / 128;
        var maximumDirectorySectors = checked(
            (_limits.MaxDirectoryEntries + entriesPerDirectorySector - 1)
                / entriesPerDirectorySector
        );
        var directoryChain = FollowChain(
            firstDirectorySector,
            fat,
            sectorCount,
            expectedSectors: majorVersion == 4 ? directorySectorCount : null,
            "cfb_directory_chain_invalid",
            cancellationToken,
            maximumDirectorySectors
        );
        if (directoryChain.Count == 0)
        {
            throw Invalid("cfb_directory_missing", majorVersion, sectorSize);
        }
        var possibleEntries = checked(directoryChain.Count * entriesPerDirectorySector);
        if (possibleEntries > _limits.MaxDirectoryEntries)
        {
            throw new OoxmlEncryptionInspectionLimitException(
                "The compound file exceeds the bounded directory-entry limit."
            );
        }
        var entries = ReadDirectory(
            stream,
            directoryChain,
            sectorSize,
            majorVersion,
            cancellationToken
        );
        if (
            entries.Count == 0
            || entries[0].Type != 5
            || !string.Equals(entries[0].Name, "Root Entry", StringComparison.Ordinal)
            || entries[0].LeftSiblingId != NoStream
            || entries[0].RightSiblingId != NoStream
        )
        {
            throw Invalid("cfb_root_entry_invalid", majorVersion, sectorSize, entries.Count);
        }

        var rootChildren = ReadRootChildren(entries, majorVersion, sectorSize);
        var encryptionInfoEntries = rootChildren
            .Where(entry => string.Equals(entry.Name, EncryptionInfoName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var encryptedPackageEntries = rootChildren
            .Where(entry => string.Equals(entry.Name, EncryptedPackageName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var dataSpacesEntries = rootChildren
            .Where(entry => string.Equals(entry.Name, DataSpacesName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasEncryptionInfo = encryptionInfoEntries.Length == 1 && encryptionInfoEntries[0].Type == 2;
        var hasEncryptedPackage = encryptedPackageEntries.Length == 1 && encryptedPackageEntries[0].Type == 2;
        var hasDataSpaces = dataSpacesEntries.Length == 1 && dataSpacesEntries[0].Type == 1;
        var markerSeen = encryptionInfoEntries.Length != 0
            || encryptedPackageEntries.Length != 0
            || dataSpacesEntries.Length != 0;
        var issues = new List<string>();
        if (markerSeen)
        {
            AddMarkerIssue(issues, encryptionInfoEntries, 2, "encryption_info");
            AddMarkerIssue(issues, encryptedPackageEntries, 2, "encrypted_package");
            AddMarkerIssue(issues, dataSpacesEntries, 1, "data_spaces");
        }

        byte[]? encryptionInfoPrefix = null;
        if (hasEncryptionInfo)
        {
            var infoEntry = encryptionInfoEntries[0];
            if (infoEntry.StreamSize < 8 || infoEntry.StreamSize > 1_048_576)
            {
                issues.Add("encryption_info_size_invalid");
            }
            else
            {
                encryptionInfoPrefix = ReadStreamPrefix(
                    stream,
                    infoEntry,
                    entries[0],
                    fat,
                    sectorSize,
                    sectorCount,
                    firstMiniFatSector,
                    miniFatSectorCount,
                    miniStreamCutoff,
                    cancellationToken
                );
            }
        }
        if (hasEncryptedPackage)
        {
            var packageEntry = encryptedPackageEntries[0];
            if (packageEntry.StreamSize < 8)
            {
                issues.Add("encrypted_package_size_invalid");
            }
            else
            {
                ValidateStreamChain(
                    packageEntry,
                    entries[0],
                    stream,
                    fat,
                    sectorSize,
                    sectorCount,
                    firstMiniFatSector,
                    miniFatSectorCount,
                    miniStreamCutoff,
                    cancellationToken
                );
            }
        }

        int? encryptionMajor = null;
        int? encryptionMinor = null;
        var variant = markerSeen ? "unknown" : "not_applicable";
        if (encryptionInfoPrefix is { Length: >= 4 })
        {
            encryptionMajor = BinaryPrimitives.ReadUInt16LittleEndian(encryptionInfoPrefix);
            encryptionMinor = BinaryPrimitives.ReadUInt16LittleEndian(encryptionInfoPrefix.AsSpan(2));
            variant = ClassifyEncryptionInfo(encryptionMajor.Value, encryptionMinor.Value);
            if (variant == "unknown")
            {
                issues.Add("encryption_info_version_unknown");
            }
            else if (!HasValidEncryptionInfoHeader(variant, encryptionInfoPrefix))
            {
                issues.Add("encryption_info_header_invalid");
            }
        }

        var hasStructuralIssue = issues.Any(code =>
            code != "encryption_info_version_unknown"
        );
        var complete = hasEncryptionInfo
            && hasEncryptedPackage
            && hasDataSpaces
            && !hasStructuralIssue;
        var state = complete
            ? "encrypted"
            : markerSeen
                ? "malformed_encryption_container"
                : "not_encrypted";
        return new OoxmlEncryptionProbe(
            complete ? "encrypted_ooxml_compound_file" : "compound_file",
            state,
            complete,
            complete,
            hasEncryptionInfo,
            hasEncryptedPackage,
            hasDataSpaces,
            variant,
            encryptionMajor,
            encryptionMinor,
            majorVersion,
            sectorSize,
            entries.Count,
            rootChildren.Count,
            Array.AsReadOnly(
                issues
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(code => code, StringComparer.Ordinal)
                    .ToArray()
            )
        );
    }

    private static DifatResult ReadDifat(
        Stream stream,
        byte[] header,
        int sectorSize,
        int sectorCount,
        uint fatSectorCount,
        uint firstDifatSector,
        uint difatSectorCount,
        CancellationToken cancellationToken
    )
    {
        var result = new List<uint>(checked((int)fatSectorCount));
        var seen = new HashSet<uint>();
        var difatSectors = new List<uint>(checked((int)difatSectorCount));
        for (var index = 0; index < 109; index++)
        {
            AddFatSectorOrRequireFree(ReadUInt32(header, 76 + index * 4));
        }
        var current = firstDifatSector;
        var entriesPerDifatSector = sectorSize / 4 - 1;
        for (var index = 0U; index < difatSectorCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireRegularSector(current, sectorCount, "cfb_difat_chain_invalid");
            if (!seen.Add(current))
            {
                throw Invalid("cfb_difat_cycle");
            }
            difatSectors.Add(current);
            var bytes = ReadSector(stream, current, sectorSize);
            for (var item = 0; item < entriesPerDifatSector; item++)
            {
                AddFatSectorOrRequireFree(ReadUInt32(bytes, item * 4));
            }
            current = ReadUInt32(bytes, sectorSize - 4);
        }
        if (difatSectorCount == 0 && firstDifatSector != EndOfChain)
        {
            throw Invalid("cfb_difat_header_invalid");
        }
        if (difatSectorCount != 0 && current != EndOfChain)
        {
            throw Invalid("cfb_difat_chain_length_invalid");
        }
        if (result.Count != fatSectorCount)
        {
            throw Invalid("cfb_fat_sector_list_incomplete");
        }
        return new DifatResult(result, difatSectors);

        void AddFatSectorOrRequireFree(uint sector)
        {
            if (result.Count < fatSectorCount)
            {
                AddFatSector(sector);
            }
            else if (sector != FreeSector)
            {
                throw Invalid("cfb_difat_unused_entry_invalid");
            }
        }

        void AddFatSector(uint sector)
        {
            if (sector == FreeSector)
            {
                return;
            }
            RequireRegularSector(sector, sectorCount, "cfb_fat_sector_invalid");
            if (result.Contains(sector))
            {
                throw Invalid("cfb_fat_sector_duplicate");
            }
            result.Add(sector);
        }
    }

    private static uint[] ReadFat(
        Stream stream,
        int sectorSize,
        int sectorCount,
        IReadOnlyList<uint> fatSectorIds,
        CancellationToken cancellationToken
    )
    {
        var entriesPerSector = sectorSize / 4;
        var fat = new uint[checked(fatSectorIds.Count * entriesPerSector)];
        for (var index = 0; index < fatSectorIds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = ReadSector(stream, fatSectorIds[index], sectorSize);
            for (var item = 0; item < entriesPerSector; item++)
            {
                fat[index * entriesPerSector + item] = ReadUInt32(bytes, item * 4);
            }
        }
        if (fat.Length < sectorCount)
        {
            throw Invalid("cfb_fat_table_incomplete");
        }
        foreach (var fatSectorId in fatSectorIds)
        {
            if (fat[fatSectorId] != FatSector)
            {
                throw Invalid("cfb_fat_self_marker_invalid");
            }
        }
        return fat;
    }

    private IReadOnlyList<uint> FollowChain(
        uint first,
        IReadOnlyList<uint> allocationTable,
        int sectorCount,
        uint? expectedSectors,
        string issueCode,
        CancellationToken cancellationToken,
        int? maximumSectors = null
    )
    {
        if (first == EndOfChain)
        {
            if (expectedSectors is null or 0)
            {
                return Array.Empty<uint>();
            }
            throw Invalid(issueCode);
        }
        var result = new List<uint>();
        var seen = new HashSet<uint>();
        var current = first;
        while (current != EndOfChain)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireRegularSector(current, sectorCount, issueCode);
            if (current >= allocationTable.Count || !seen.Add(current))
            {
                throw Invalid(issueCode);
            }
            result.Add(current);
            if (result.Count > (maximumSectors ?? _limits.MaxChainSectors))
            {
                throw new OoxmlEncryptionInspectionLimitException(
                    "A compound-file sector chain exceeds the bounded inspection limit."
                );
            }
            current = allocationTable[checked((int)current)];
            if (current is FatSector or DifatSector or FreeSector || current > MaximumRegularSector && current != EndOfChain)
            {
                throw Invalid(issueCode);
            }
        }
        if (expectedSectors.HasValue && result.Count != expectedSectors.Value)
        {
            throw Invalid(issueCode);
        }
        return result;
    }

    private static IReadOnlyList<DirectoryEntry> ReadDirectory(
        Stream stream,
        IReadOnlyList<uint> sectors,
        int sectorSize,
        int majorVersion,
        CancellationToken cancellationToken
    )
    {
        var result = new List<DirectoryEntry>(sectors.Count * (sectorSize / 128));
        foreach (var sector in sectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = ReadSector(stream, sector, sectorSize);
            for (var offset = 0; offset < sectorSize; offset += 128)
            {
                var type = bytes[offset + 66];
                var color = bytes[offset + 67];
                if (type > 5 || type is 3 or 4)
                {
                    throw Invalid("cfb_directory_entry_type_invalid", majorVersion, sectorSize, result.Count);
                }
                if (type != 0 && color > 1)
                {
                    throw Invalid("cfb_directory_color_invalid", majorVersion, sectorSize, result.Count);
                }
                var nameLength = ReadUInt16(bytes, offset + 64);
                string? name = null;
                if (type != 0)
                {
                    if (nameLength is < 2 or > 64 || nameLength % 2 != 0)
                    {
                        throw Invalid("cfb_directory_name_invalid", majorVersion, sectorSize, result.Count);
                    }
                    var nameBytes = bytes.AsSpan(offset, nameLength - 2);
                    if (ReadUInt16(bytes, offset + nameLength - 2) != 0)
                    {
                        throw Invalid("cfb_directory_name_invalid", majorVersion, sectorSize, result.Count);
                    }
                    name = Encoding.Unicode.GetString(nameBytes);
                }
                var streamSize = ReadUInt64(bytes, offset + 120);
                if (majorVersion == 3)
                {
                    streamSize &= uint.MaxValue;
                }
                result.Add(
                    new DirectoryEntry(
                        result.Count,
                        name,
                        type,
                        ReadUInt32(bytes, offset + 68),
                        ReadUInt32(bytes, offset + 72),
                        ReadUInt32(bytes, offset + 76),
                        ReadUInt32(bytes, offset + 116),
                        streamSize
                    )
                );
            }
        }
        return result;
    }

    private static IReadOnlyList<DirectoryEntry> ReadRootChildren(
        IReadOnlyList<DirectoryEntry> entries,
        int majorVersion,
        int sectorSize
    )
    {
        var result = new List<DirectoryEntry>();
        var seen = new HashSet<uint>();
        var pending = new Stack<uint>();
        if (entries[0].ChildId != NoStream)
        {
            pending.Push(entries[0].ChildId);
        }
        while (pending.Count != 0)
        {
            var id = pending.Pop();
            if (id >= entries.Count || !seen.Add(id))
            {
                throw Invalid("cfb_directory_tree_invalid", majorVersion, sectorSize, entries.Count, result.Count);
            }
            var entry = entries[checked((int)id)];
            if (entry.Type is not (1 or 2))
            {
                throw Invalid("cfb_root_child_type_invalid", majorVersion, sectorSize, entries.Count, result.Count);
            }
            if (entry.Type == 2 && entry.ChildId != NoStream)
            {
                throw Invalid("cfb_stream_child_invalid", majorVersion, sectorSize, entries.Count, result.Count);
            }
            result.Add(entry);
            if (entry.LeftSiblingId != NoStream)
            {
                pending.Push(entry.LeftSiblingId);
            }
            if (entry.RightSiblingId != NoStream)
            {
                pending.Push(entry.RightSiblingId);
            }
        }
        return result;
    }

    private byte[] ReadStreamPrefix(
        Stream stream,
        DirectoryEntry entry,
        DirectoryEntry root,
        IReadOnlyList<uint> fat,
        int sectorSize,
        int sectorCount,
        uint firstMiniFatSector,
        uint miniFatSectorCount,
        uint miniStreamCutoff,
        CancellationToken cancellationToken
    )
    {
        var wanted = checked((int)Math.Min(entry.StreamSize, 8));
        if (entry.StreamSize >= miniStreamCutoff)
        {
            var chain = ValidateRegularStream(entry, fat, sectorSize, sectorCount, cancellationToken);
            var bytes = ReadSector(stream, chain[0], sectorSize);
            return bytes[..wanted];
        }

        var miniFat = ReadMiniFat(
            stream,
            fat,
            sectorSize,
            sectorCount,
            firstMiniFatSector,
            miniFatSectorCount,
            cancellationToken
        );
        var expectedMiniSectors = checked((uint)((entry.StreamSize + 63) / 64));
        var miniChain = FollowChain(
            entry.StartSector,
            miniFat,
            miniFat.Length,
            expectedMiniSectors,
            "cfb_mini_stream_chain_invalid",
            cancellationToken
        );
        var rootChain = ValidateRegularStream(root, fat, sectorSize, sectorCount, cancellationToken);
        var miniOffset = checked((long)miniChain[0] * 64);
        if (miniOffset + wanted > checked((long)root.StreamSize))
        {
            throw Invalid("cfb_mini_stream_bounds_invalid");
        }
        var regularIndex = checked((int)(miniOffset / sectorSize));
        var withinSector = checked((int)(miniOffset % sectorSize));
        if (regularIndex >= rootChain.Count || withinSector + wanted > sectorSize)
        {
            throw Invalid("cfb_mini_stream_bounds_invalid");
        }
        var rootSector = ReadSector(stream, rootChain[regularIndex], sectorSize);
        return rootSector.AsSpan(withinSector, wanted).ToArray();
    }

    private void ValidateStreamChain(
        DirectoryEntry entry,
        DirectoryEntry root,
        Stream stream,
        IReadOnlyList<uint> fat,
        int sectorSize,
        int sectorCount,
        uint firstMiniFatSector,
        uint miniFatSectorCount,
        uint miniStreamCutoff,
        CancellationToken cancellationToken
    )
    {
        if (entry.StreamSize >= miniStreamCutoff)
        {
            _ = ValidateRegularStream(entry, fat, sectorSize, sectorCount, cancellationToken);
            return;
        }
        var miniFat = ReadMiniFat(
            stream,
            fat,
            sectorSize,
            sectorCount,
            firstMiniFatSector,
            miniFatSectorCount,
            cancellationToken
        );
        var expectedMiniSectors = checked((uint)((entry.StreamSize + 63) / 64));
        var miniChain = FollowChain(
            entry.StartSector,
            miniFat,
            miniFat.Length,
            expectedMiniSectors,
            "cfb_mini_stream_chain_invalid",
            cancellationToken
        );
        var rootChain = ValidateRegularStream(root, fat, sectorSize, sectorCount, cancellationToken);
        var lastMiniOffset = checked((long)miniChain[^1] * 64 + 64);
        if (lastMiniOffset > checked((long)root.StreamSize))
        {
            throw Invalid("cfb_mini_stream_bounds_invalid");
        }
        _ = rootChain;
    }

    private IReadOnlyList<uint> ValidateRegularStream(
        DirectoryEntry entry,
        IReadOnlyList<uint> fat,
        int sectorSize,
        int sectorCount,
        CancellationToken cancellationToken
    )
    {
        if (entry.StreamSize == 0)
        {
            if (entry.StartSector != EndOfChain)
            {
                throw Invalid("cfb_empty_stream_chain_invalid");
            }
            return Array.Empty<uint>();
        }
        if (entry.StreamSize > checked((ulong)sectorCount * (ulong)sectorSize))
        {
            throw Invalid("cfb_stream_size_invalid");
        }
        var expected = checked((uint)((entry.StreamSize + (ulong)sectorSize - 1) / (ulong)sectorSize));
        return FollowChain(
            entry.StartSector,
            fat,
            sectorCount,
            expected,
            "cfb_stream_chain_invalid",
            cancellationToken
        );
    }

    private uint[] ReadMiniFat(
        Stream stream,
        IReadOnlyList<uint> fat,
        int sectorSize,
        int sectorCount,
        uint firstMiniFatSector,
        uint miniFatSectorCount,
        CancellationToken cancellationToken
    )
    {
        if (miniFatSectorCount == 0 || firstMiniFatSector == EndOfChain)
        {
            throw Invalid("cfb_mini_fat_missing");
        }
        var chain = FollowChain(
            firstMiniFatSector,
            fat,
            sectorCount,
            miniFatSectorCount,
            "cfb_mini_fat_chain_invalid",
            cancellationToken
        );
        var entriesPerSector = sectorSize / 4;
        var result = new uint[checked(chain.Count * entriesPerSector)];
        for (var index = 0; index < chain.Count; index++)
        {
            var bytes = ReadSector(stream, chain[index], sectorSize);
            for (var item = 0; item < entriesPerSector; item++)
            {
                result[index * entriesPerSector + item] = ReadUInt32(bytes, item * 4);
            }
        }
        return result;
    }

    private static void AddMarkerIssue(
        ICollection<string> issues,
        IReadOnlyCollection<DirectoryEntry> entries,
        byte expectedType,
        string prefix
    )
    {
        if (entries.Count == 0)
        {
            issues.Add(prefix + "_missing");
        }
        else if (entries.Count > 1)
        {
            issues.Add(prefix + "_duplicate");
        }
        else if (entries.Single().Type != expectedType)
        {
            issues.Add(prefix + "_type_invalid");
        }
    }

    private static string ClassifyEncryptionInfo(int major, int minor) =>
        (major, minor) switch
        {
            (2, 2) or (3, 2) or (4, 2) => "standard",
            (3, 3) or (4, 3) => "extensible",
            (4, 4) => "agile",
            _ => "unknown",
        };

    private static bool HasValidEncryptionInfoHeader(
        string variant,
        ReadOnlySpan<byte> prefix
    )
    {
        if (prefix.Length < 8)
        {
            return false;
        }
        var flagsOrReserved = BinaryPrimitives.ReadUInt32LittleEndian(prefix[4..8]);
        return variant switch
        {
            "standard" =>
                (flagsOrReserved & 0x24) == 0x24
                && (flagsOrReserved & 0x18) == 0,
            "extensible" => flagsOrReserved == 0x10,
            "agile" => flagsOrReserved == 0x40,
            _ => false,
        };
    }

    private static bool IsZipSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4
        && bytes[0] == (byte)'P'
        && bytes[1] == (byte)'K'
        && (
            (bytes[2] == 3 && bytes[3] == 4)
            || (bytes[2] == 5 && bytes[3] == 6)
            || (bytes[2] == 7 && bytes[3] == 8)
        );

    private static OoxmlEncryptionProbe Unknown(string issueCode) => new(
        "unknown",
        "indeterminate",
        false,
        false,
        false,
        false,
        false,
        "not_applicable",
        null,
        null,
        null,
        null,
        0,
        0,
        Array.AsReadOnly(new[] { issueCode })
    );

    private static byte[] ReadSector(Stream stream, uint sector, int sectorSize)
    {
        var result = new byte[sectorSize];
        ReadExactlyAt(stream, checked(((long)sector + 1) * sectorSize), result);
        return result;
    }

    private static void ReadExactlyAt(Stream stream, long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > stream.Length - destination.Length)
        {
            throw Invalid("cfb_read_bounds_invalid");
        }
        stream.Position = offset;
        stream.ReadExactly(destination);
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static ulong ReadUInt64(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));

    private static void RequireRegularSector(uint sector, int sectorCount, string issueCode)
    {
        if (sector > MaximumRegularSector || sector >= sectorCount)
        {
            throw Invalid(issueCode);
        }
    }

    private static CompoundFileFormatException Invalid(
        string code,
        int? majorVersion = null,
        int? sectorSize = null,
        int directoryEntryCount = 0,
        int rootChildCount = 0
    ) => new(code, majorVersion, sectorSize, directoryEntryCount, rootChildCount);

    private sealed record DirectoryEntry(
        int Id,
        string? Name,
        byte Type,
        uint LeftSiblingId,
        uint RightSiblingId,
        uint ChildId,
        uint StartSector,
        ulong StreamSize
    );

    private sealed record DifatResult(
        IReadOnlyList<uint> FatSectorIds,
        IReadOnlyList<uint> DifatSectorIds
    );

    private sealed class CompoundFileFormatException : IOException
    {
        public string Code { get; }
        public int? MajorVersion { get; }
        public int? SectorSize { get; }
        public int DirectoryEntryCount { get; }
        public int RootChildCount { get; }

        public CompoundFileFormatException(
            string code,
            int? majorVersion,
            int? sectorSize,
            int directoryEntryCount,
            int rootChildCount
        )
            : base("The compound file is malformed.")
        {
            Code = code;
            MajorVersion = majorVersion;
            SectorSize = sectorSize;
            DirectoryEntryCount = directoryEntryCount;
            RootChildCount = rootChildCount;
        }
    }
}
