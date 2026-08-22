using System.IO.Compression;
using System.Text.Json;

namespace WordToolkit.Engine.Packaging;

public sealed record OpcPackagePatchFileReadResult(
    OpcPackagePatch Patch,
    long SerializedBytes,
    string SerializedSha256
);

public sealed class OpcPackagePatchCodec
{
    private const int MaximumDeflateOverheadBytesPerEntry = 22;
    private const int MaximumArchiveOverheadBytesPerEntry = 512;
    private const int MaximumArchiveFixedOverheadBytes = 64 * 1024;
    private const string ManifestEntryName = "manifest.json";
    private const string PayloadPrefix = "payloads/";
    private const string PayloadSuffix = ".bin";

    private readonly OpcPackagePatchLimits _limits;

    public OpcPackagePatchCodec(OpcPackagePatchLimits? limits = null)
    {
        _limits = limits ?? OpcPackagePatchLimits.Default;
        _limits.Validate();
    }

    public void Write(
        Stream destination,
        OpcPackagePatch patch,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(patch);
        cancellationToken.ThrowIfCancellationRequested();
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "Patch destination stream must be writable.",
                nameof(destination)
            );
        }
        if (destination.CanSeek && (destination.Position != 0 || destination.Length != 0))
        {
            throw new ArgumentException(
                "Patch destination must be empty and positioned at zero.",
                nameof(destination)
            );
        }
        ValidatePatchLimits(patch);
        var manifest = WriteManifest(patch);
        if (manifest.LongLength > _limits.MaxManifestBytes)
        {
            throw new OpcPackagePatchLimitException(
                "Patch manifest exceeds its configured byte limit."
            );
        }

        using var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true
        );
        WriteEntry(archive, ManifestEntryName, manifest);
        foreach (var payload in patch.Payloads.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteEntry(
                archive,
                PayloadEntryName(payload.Key),
                payload.Value
            );
        }
    }

    public OpcPackagePatch Read(
        Stream source,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "Patch source stream must be readable.",
                nameof(source)
            );
        }
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var maximumArchiveEntries = (long)_limits.MaxPayloads + 1;
        if ((long)archive.Entries.Count > maximumArchiveEntries)
        {
            throw new OpcPackagePatchLimitException(
                "Patch archive contains too many entries."
            );
        }
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var maximumExpandedBytes = MaximumExpandedArchiveBytes();
        long totalExpandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateArchiveEntry(entry);
            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw new OpcPackagePatchFormatException(
                    $"Patch archive contains duplicate entry '{entry.FullName}'."
                );
            }
            if (entry.Length > maximumExpandedBytes - totalExpandedBytes)
            {
                throw new OpcPackagePatchLimitException(
                    "Patch archive expands beyond its configured total limit."
                );
            }
            totalExpandedBytes += entry.Length;
        }
        if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry))
        {
            throw new OpcPackagePatchFormatException(
                "Patch archive has no manifest.json entry."
            );
        }
        if (manifestEntry.Length > _limits.MaxManifestBytes)
        {
            throw new OpcPackagePatchLimitException(
                "Patch manifest exceeds its configured byte limit."
            );
        }

        var manifestBytes = ReadEntry(
            manifestEntry,
            _limits.MaxManifestBytes,
            cancellationToken
        );
        var parsed = ParseManifest(manifestBytes);
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var hash in ReferencedPayloads(parsed.Operations))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = PayloadEntryName(hash);
            if (!entries.TryGetValue(entryName, out var payloadEntry))
            {
                throw new OpcPackagePatchFormatException(
                    $"Patch payload '{hash}' is missing."
                );
            }
            var payload = ReadEntry(
                payloadEntry,
                _limits.MaxPayloadBytesPerBlob,
                cancellationToken
            );
            if (!string.Equals(
                    OpcPackagePatch.Hash(payload),
                    hash,
                    StringComparison.Ordinal
                ))
            {
                throw new OpcPackagePatchFormatException(
                    $"Patch payload '{hash}' failed SHA-256 verification."
                );
            }
            payloads.Add(hash, payload);
        }
        var expectedEntries = payloads.Keys.Select(PayloadEntryName)
            .Append(ManifestEntryName)
            .ToHashSet(StringComparer.Ordinal);
        var extra = entries.Keys.FirstOrDefault(name => !expectedEntries.Contains(name));
        if (extra is not null)
        {
            throw new OpcPackagePatchFormatException(
                $"Patch archive contains unreferenced entry '{extra}'."
            );
        }
        if (payloads.Count != parsed.PayloadCount)
        {
            throw new OpcPackagePatchFormatException(
                "Patch payload count does not match its manifest."
            );
        }
        var payloadBytes = payloads.Values.Sum(payload => (long)payload.Length);
        if (payloadBytes != parsed.PayloadBytes)
        {
            throw new OpcPackagePatchFormatException(
                "Patch payload byte count does not match its manifest."
            );
        }
        var patch = OpcPackagePatchBuilder.FinalizePatch(
            parsed.BasePackageFingerprint,
            parsed.ResultPackageFingerprint,
            parsed.Operations,
            payloads
        );
        ValidatePayloadReferences(patch);
        if (!string.Equals(patch.PatchId, parsed.PatchId, StringComparison.Ordinal))
        {
            throw new OpcPackagePatchFormatException(
                "Patch ID does not match the canonical manifest content."
            );
        }
        for (var index = 0; index < patch.Operations.Count; index++)
        {
            if (!string.Equals(
                    patch.Operations[index].OperationId,
                    parsed.OperationIds[index],
                    StringComparison.Ordinal
                ))
            {
                throw new OpcPackagePatchFormatException(
                    "A patch operation ID does not match its canonical content."
                );
            }
        }
        ValidatePatchLimits(patch);
        return patch;
    }

    public OpcPackagePatch ReadFromPath(
        string path,
        CancellationToken cancellationToken = default
    ) => ReadFileFromPath(path, cancellationToken).Patch;

    public OpcPackagePatchFileReadResult ReadFileFromPath(
        string path,
        CancellationToken cancellationToken = default
    ) => ReadPath(path, cancellationToken, afterCopy: null);

    internal OpcPackagePatchFileReadResult ReadPath(
        string path,
        CancellationToken cancellationToken,
        Action<int>? afterCopy
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EncryptedTemporaryStream snapshot;
        long serializedBytes;
        string serializedSha256;
        try
        {
            snapshot = StablePackagePathSnapshot.CaptureWithMetadata(
                path,
                MaximumSerializedArchiveBytes(),
                cancellationToken,
                out serializedBytes,
                out serializedSha256,
                afterCopy
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new OpcPackagePatchLimitException(
                "Patch artifact exceeds its serialized safety limit.",
                exception
            );
        }
        using (snapshot)
        {
            return new OpcPackagePatchFileReadResult(
                Read(snapshot, cancellationToken),
                serializedBytes,
                serializedSha256
            );
        }
    }

    internal long MaximumSerializedArchiveBytes()
    {
        var entryCount = (long)_limits.MaxPayloads + 1;
        var expandedBytes = MaximumExpandedArchiveBytes();

        // System.IO.Compression emits a raw DEFLATE stream for each compressed
        // ZIP entry. This conservative fixed-block bound covers nine-bit
        // literals plus block/wrapper slack for every entry, including empty
        // entries. It deliberately scales with input size instead of assuming
        // that incompressible data never expands.
        var compressedBytes = SaturatingAdd(
            expandedBytes,
            expandedBytes >> 3
        );
        compressedBytes = SaturatingAdd(
            compressedBytes,
            expandedBytes >> 8
        );
        compressedBytes = SaturatingAdd(
            compressedBytes,
            expandedBytes >> 9
        );
        compressedBytes = SaturatingAdd(
            compressedBytes,
            SaturatingMultiply(entryCount, MaximumDeflateOverheadBytesPerEntry)
        );

        var archiveStructureBytes = SaturatingAdd(
            SaturatingMultiply(entryCount, MaximumArchiveOverheadBytesPerEntry),
            MaximumArchiveFixedOverheadBytes
        );
        return SaturatingAdd(compressedBytes, archiveStructureBytes);
    }

    private long MaximumExpandedArchiveBytes() => SaturatingAdd(
        _limits.MaxPayloadBytes,
        _limits.MaxManifestBytes
    );

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static long SaturatingMultiply(long left, long right) =>
        left == 0 || right == 0
            ? 0
            : left > long.MaxValue / right
                ? long.MaxValue
                : left * right;

    private void ValidatePatchLimits(OpcPackagePatch patch)
    {
        if (patch.OperationCount > _limits.MaxOperations)
        {
            throw new OpcPackagePatchLimitException(
                "Patch operation count exceeds its configured limit."
            );
        }
        if (patch.PayloadCount > _limits.MaxPayloads)
        {
            throw new OpcPackagePatchLimitException(
                "Patch payload count exceeds its configured limit."
            );
        }
        if (patch.PayloadBytes > _limits.MaxPayloadBytes)
        {
            throw new OpcPackagePatchLimitException(
                "Patch payload bytes exceed their configured limit."
            );
        }
        if (patch.Payloads.Values.Any(payload =>
                payload.LongLength > _limits.MaxPayloadBytesPerBlob
            ))
        {
            throw new OpcPackagePatchLimitException(
                "A patch payload exceeds its configured per-blob limit."
            );
        }
    }

    private static byte[] WriteManifest(OpcPackagePatch patch)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", OpcPackagePatch.Format);
            writer.WriteNumber("format_version", OpcPackagePatch.FormatVersion);
            writer.WriteString("patch_id", patch.PatchId);
            writer.WriteString(
                "base_package_fingerprint",
                patch.BasePackageFingerprint
            );
            writer.WriteString(
                "result_package_fingerprint",
                patch.ResultPackageFingerprint
            );
            writer.WriteNumber("operation_count", patch.OperationCount);
            writer.WriteNumber("payload_count", patch.PayloadCount);
            writer.WriteNumber("payload_bytes", patch.PayloadBytes);
            writer.WriteStartArray("operations");
            foreach (var operation in patch.Operations)
            {
                writer.WriteStartObject();
                writer.WriteString("operation_id", operation.OperationId);
                writer.WriteString("kind", OperationKind(operation.Kind));
                writer.WriteString("entry_name", operation.EntryName);
                WriteNullableString(writer, "part_uri", operation.PartUri);
                WriteNullableString(
                    writer,
                    "before_content_type",
                    operation.BeforeContentType
                );
                WriteNullableString(
                    writer,
                    "after_content_type",
                    operation.AfterContentType
                );
                WriteNullableString(writer, "before_sha256", operation.BeforeSha256);
                WriteNullableInt64(writer, "before_bytes", operation.BeforeBytes);
                WriteNullableString(writer, "after_sha256", operation.AfterSha256);
                WriteNullableInt64(writer, "after_bytes", operation.AfterBytes);
                writer.WriteBoolean("is_infrastructure", operation.IsInfrastructure);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private ParsedManifest ParseManifest(ReadOnlyMemory<byte> manifestBytes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        }
        catch (JsonException exception)
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest is not valid bounded JSON: {exception.Message}"
            );
        }
        using (document)
        {
            var root = document.RootElement;
            RequireObject(root, "patch manifest");
            RequireExactProperties(root, RootProperties, "patch manifest");
            if (!string.Equals(
                    RequiredString(root, "format", 64),
                    OpcPackagePatch.Format,
                    StringComparison.Ordinal
                ))
            {
                throw new OpcPackagePatchFormatException(
                    "Patch manifest has an unsupported format."
                );
            }
            if (RequiredInt32(root, "format_version") != OpcPackagePatch.FormatVersion)
            {
                throw new OpcPackagePatchFormatException(
                    "Patch manifest has an unsupported format version."
                );
            }
            var patchId = RequiredIdentifier(root, "patch_id", "wtpatch_", 128);
            var baseFingerprint = RequiredSha256(root, "base_package_fingerprint");
            var resultFingerprint = RequiredSha256(root, "result_package_fingerprint");
            var operationCount = RequiredInt32(root, "operation_count");
            var payloadCount = RequiredInt32(root, "payload_count");
            var payloadBytes = RequiredInt64(root, "payload_bytes");
            if (operationCount is < 0 || operationCount > _limits.MaxOperations)
            {
                throw new OpcPackagePatchLimitException(
                    "Patch manifest operation count exceeds its configured limit."
                );
            }
            if (payloadCount is < 0 || payloadCount > _limits.MaxPayloads)
            {
                throw new OpcPackagePatchLimitException(
                    "Patch manifest payload count exceeds its configured limit."
                );
            }
            if (payloadBytes is < 0 || payloadBytes > _limits.MaxPayloadBytes)
            {
                throw new OpcPackagePatchLimitException(
                    "Patch manifest payload bytes exceed their configured limit."
                );
            }
            var operationsNode = root.GetProperty("operations");
            if (operationsNode.ValueKind != JsonValueKind.Array)
            {
                throw new OpcPackagePatchFormatException(
                    "Patch manifest operations must be an array."
                );
            }
            if (operationsNode.GetArrayLength() != operationCount)
            {
                throw new OpcPackagePatchFormatException(
                    "Patch operation count does not match its manifest array."
                );
            }
            var operations = new List<OpcPackagePatchOperation>(operationCount);
            var operationIds = new List<string>(operationCount);
            string? previousEntryName = null;
            var seenEntries = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in operationsNode.EnumerateArray())
            {
                RequireObject(node, "patch operation");
                RequireExactProperties(node, OperationProperties, "patch operation");
                var operationId = RequiredIdentifier(
                    node,
                    "operation_id",
                    "wtpo_",
                    128
                );
                var kind = ParseOperationKind(RequiredString(node, "kind", 16));
                var entryName = RequiredString(node, "entry_name", 2_048);
                if (!OpcPartUri.TryFromEntryName(entryName, out var derivedPartUri, out var error))
                {
                    throw new OpcPackagePatchFormatException(
                        error ?? "Patch operation has an invalid OPC entry name."
                    );
                }
                if (!seenEntries.Add(entryName))
                {
                    throw new OpcPackagePatchFormatException(
                        $"Patch contains duplicate operation entry '{entryName}'."
                    );
                }
                if (
                    previousEntryName is not null
                    && StringComparer.Ordinal.Compare(previousEntryName, entryName) >= 0
                )
                {
                    throw new OpcPackagePatchFormatException(
                        "Patch operations are not in canonical entry-name order."
                    );
                }
                previousEntryName = entryName;
                var partUri = NullableString(node, "part_uri", 2_049);
                if (
                    partUri is not null
                    && !string.Equals(partUri, derivedPartUri, StringComparison.Ordinal)
                )
                {
                    throw new OpcPackagePatchFormatException(
                        "Patch part URI does not match its OPC entry name."
                    );
                }
                var operation = new OpcPackagePatchOperation(
                    string.Empty,
                    kind,
                    entryName,
                    partUri,
                    NullableString(node, "before_content_type", 1_024),
                    NullableString(node, "after_content_type", 1_024),
                    NullableSha256(node, "before_sha256"),
                    NullableInt64(node, "before_bytes"),
                    NullableSha256(node, "after_sha256"),
                    NullableInt64(node, "after_bytes"),
                    RequiredBoolean(node, "is_infrastructure")
                );
                ValidateOperationShape(operation);
                operations.Add(operation);
                operationIds.Add(operationId);
            }
            return new ParsedManifest(
                patchId,
                baseFingerprint,
                resultFingerprint,
                payloadCount,
                payloadBytes,
                operations,
                operationIds
            );
        }
    }

    private void ValidateArchiveEntry(ZipArchiveEntry entry)
    {
        if (
            string.IsNullOrEmpty(entry.FullName)
            || entry.FullName.EndsWith("/", StringComparison.Ordinal)
            || entry.FullName.Contains("\\", StringComparison.Ordinal)
            || entry.FullName.StartsWith("/", StringComparison.Ordinal)
            || entry.FullName.Contains('\0')
            || entry.FullName.Split('/').Any(segment => segment is "" or "." or "..")
        )
        {
            throw new OpcPackagePatchFormatException(
                "Patch archive contains an unsafe entry name."
            );
        }
        if (
            !string.Equals(entry.FullName, ManifestEntryName, StringComparison.Ordinal)
            && !IsPayloadEntryName(entry.FullName)
        )
        {
            throw new OpcPackagePatchFormatException(
                $"Patch archive entry '{entry.FullName}' is not part of the format."
            );
        }
        var maximum = string.Equals(
            entry.FullName,
            ManifestEntryName,
            StringComparison.Ordinal
        )
            ? _limits.MaxManifestBytes
            : _limits.MaxPayloadBytesPerBlob;
        if (entry.Length > maximum)
        {
            throw new OpcPackagePatchLimitException(
                $"Patch archive entry '{entry.FullName}' exceeds its size limit."
            );
        }
        var ratio = entry.Length == 0
            ? 0
            : (double)entry.Length / Math.Max(1, entry.CompressedLength);
        if (ratio > _limits.MaxCompressionRatio)
        {
            throw new OpcPackagePatchLimitException(
                $"Patch archive entry '{entry.FullName}' exceeds the compression-ratio limit."
            );
        }
    }

    private static byte[] ReadEntry(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        if (entry.Length > maximumBytes || entry.Length > int.MaxValue)
        {
            throw new OpcPackagePatchLimitException(
                $"Patch archive entry '{entry.FullName}' is too large."
            );
        }
        var bytes = GC.AllocateUninitializedArray<byte>((int)entry.Length);
        using var stream = entry.Open();
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                break;
            }
            offset += read;
        }
        if (offset != bytes.Length || stream.ReadByte() != -1)
        {
            throw new OpcPackagePatchFormatException(
                $"Patch archive entry '{entry.FullName}' changed length while reading."
            );
        }
        return bytes;
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        ReadOnlySpan<byte> content
    )
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = OpcPackageSerializer.DeterministicTimestamp;
        entry.ExternalAttributes = 0;
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static void ValidatePayloadReferences(OpcPackagePatch patch)
    {
        foreach (var operation in patch.Operations)
        {
            ValidatePayloadReference(
                patch,
                operation.BeforeSha256,
                operation.BeforeBytes
            );
            ValidatePayloadReference(
                patch,
                operation.AfterSha256,
                operation.AfterBytes
            );
        }
    }

    private static void ValidatePayloadReference(
        OpcPackagePatch patch,
        string? sha256,
        long? bytes
    )
    {
        if (sha256 is null)
        {
            if (bytes is not null)
            {
                throw new OpcPackagePatchFormatException(
                    "Patch payload length exists without a payload hash."
                );
            }
            return;
        }
        if (
            bytes is null
            || !patch.Payloads.TryGetValue(sha256, out var payload)
            || payload.LongLength != bytes.Value
        )
        {
            throw new OpcPackagePatchFormatException(
                "Patch payload length does not match its operation."
            );
        }
    }

    private static IEnumerable<string> ReferencedPayloads(
        IReadOnlyList<OpcPackagePatchOperation> operations
    ) => operations.SelectMany(operation => new[]
        {
            operation.BeforeSha256,
            operation.AfterSha256,
        })
        .Where(hash => hash is not null)
        .Select(hash => hash!)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal);

    private static void ValidateOperationShape(OpcPackagePatchOperation operation)
    {
        var hasBefore = operation.BeforeSha256 is not null
            && operation.BeforeBytes is not null;
        var hasAfter = operation.AfterSha256 is not null
            && operation.AfterBytes is not null;
        if (operation.BeforeBytes is < 0 || operation.AfterBytes is < 0)
        {
            throw new OpcPackagePatchFormatException(
                "Patch operation contains a negative payload length."
            );
        }
        var valid = operation.Kind switch
        {
            OpcPackagePatchOperationKind.Add => !hasBefore && hasAfter
                && operation.BeforeSha256 is null
                && operation.BeforeBytes is null,
            OpcPackagePatchOperationKind.Replace => hasBefore && hasAfter,
            OpcPackagePatchOperationKind.Delete => hasBefore && !hasAfter
                && operation.AfterSha256 is null
                && operation.AfterBytes is null,
            _ => false,
        };
        if (!valid)
        {
            throw new OpcPackagePatchFormatException(
                "Patch operation before/after payload shape is invalid for its kind."
            );
        }
    }

    private static string OperationKind(OpcPackagePatchOperationKind kind) => kind switch
    {
        OpcPackagePatchOperationKind.Add => "add",
        OpcPackagePatchOperationKind.Replace => "replace",
        OpcPackagePatchOperationKind.Delete => "delete",
        _ => throw new OpcPackagePatchFormatException(
            $"Unsupported patch operation '{kind}'."
        ),
    };

    private static OpcPackagePatchOperationKind ParseOperationKind(string value) => value switch
    {
        "add" => OpcPackagePatchOperationKind.Add,
        "replace" => OpcPackagePatchOperationKind.Replace,
        "delete" => OpcPackagePatchOperationKind.Delete,
        _ => throw new OpcPackagePatchFormatException(
            $"Unsupported patch operation kind '{value}'."
        ),
    };

    private static string PayloadEntryName(string hash) => PayloadPrefix + hash + PayloadSuffix;

    private static bool IsPayloadEntryName(string name)
    {
        if (
            !name.StartsWith(PayloadPrefix, StringComparison.Ordinal)
            || !name.EndsWith(PayloadSuffix, StringComparison.Ordinal)
        )
        {
            return false;
        }
        var hash = name[PayloadPrefix.Length..^PayloadSuffix.Length];
        return hash.Length == 64
            && hash.All(character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string name,
        string? value
    )
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNullableInt64(
        Utf8JsonWriter writer,
        string name,
        long? value
    )
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void RequireObject(JsonElement node, string description)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            throw new OpcPackagePatchFormatException(
                $"The {description} must be a JSON object."
            );
        }
    }

    private static void RequireExactProperties(
        JsonElement node,
        IReadOnlySet<string> expected,
        string description
    )
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw new OpcPackagePatchFormatException(
                    $"The {description} contains duplicate property '{property.Name}'."
                );
            }
            if (!expected.Contains(property.Name))
            {
                throw new OpcPackagePatchFormatException(
                    $"The {description} contains unknown property '{property.Name}'."
                );
            }
        }
        var missing = expected.FirstOrDefault(name => !actual.Contains(name));
        if (missing is not null)
        {
            throw new OpcPackagePatchFormatException(
                $"The {description} is missing property '{missing}'."
            );
        }
    }

    private static string RequiredString(
        JsonElement node,
        string name,
        int maximumLength
    )
    {
        var value = node.GetProperty(name);
        if (
            value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || text.Length > maximumLength
        )
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not a bounded string."
            );
        }
        return text;
    }

    private static string? NullableString(
        JsonElement node,
        string name,
        int maximumLength
    )
    {
        var value = node.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (
            value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || text.Length > maximumLength
        )
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not null or a bounded string."
            );
        }
        return text;
    }

    private static int RequiredInt32(JsonElement node, string name)
    {
        var value = node.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not an integer."
            );
        }
        return result;
    }

    private static long RequiredInt64(JsonElement node, string name)
    {
        var value = node.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not an integer."
            );
        }
        return result;
    }

    private static long? NullableInt64(JsonElement node, string name)
    {
        var value = node.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not null or an integer."
            );
        }
        return result;
    }

    private static bool RequiredBoolean(JsonElement node, string name)
    {
        var value = node.GetProperty(name);
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not a Boolean."
            );
        }
        return value.GetBoolean();
    }

    private static string RequiredSha256(JsonElement node, string name)
    {
        var value = RequiredString(node, name, 64);
        if (!IsLowerSha256(value))
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not canonical lowercase SHA-256."
            );
        }
        return value;
    }

    private static string? NullableSha256(JsonElement node, string name)
    {
        var value = NullableString(node, name, 64);
        if (value is not null && !IsLowerSha256(value))
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not canonical lowercase SHA-256."
            );
        }
        return value;
    }

    private static bool IsLowerSha256(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private static string RequiredIdentifier(
        JsonElement node,
        string name,
        string prefix,
        int maximumLength
    )
    {
        var value = RequiredString(node, name, maximumLength);
        if (
            !value.StartsWith(prefix, StringComparison.Ordinal)
            || value.Length <= prefix.Length
            || value[prefix.Length..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new OpcPackagePatchFormatException(
                $"Patch manifest property '{name}' is not a canonical identifier."
            );
        }
        return value;
    }

    private static readonly IReadOnlySet<string> RootProperties = new HashSet<string>(
        [
            "format",
            "format_version",
            "patch_id",
            "base_package_fingerprint",
            "result_package_fingerprint",
            "operation_count",
            "payload_count",
            "payload_bytes",
            "operations",
        ],
        StringComparer.Ordinal
    );

    private static readonly IReadOnlySet<string> OperationProperties = new HashSet<string>(
        [
            "operation_id",
            "kind",
            "entry_name",
            "part_uri",
            "before_content_type",
            "after_content_type",
            "before_sha256",
            "before_bytes",
            "after_sha256",
            "after_bytes",
            "is_infrastructure",
        ],
        StringComparer.Ordinal
    );

    private sealed record ParsedManifest(
        string PatchId,
        string BasePackageFingerprint,
        string ResultPackageFingerprint,
        int PayloadCount,
        long PayloadBytes,
        IReadOnlyList<OpcPackagePatchOperation> Operations,
        IReadOnlyList<string> OperationIds
    );
}
