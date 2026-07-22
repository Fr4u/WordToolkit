using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordToolkit.Engine.Packaging;

public enum OpcPackagePatchOperationKind
{
    Add,
    Replace,
    Delete,
}

public sealed record OpcPackagePatchLimits
{
    public static OpcPackagePatchLimits Default { get; } = new();

    public int MaxOperations { get; init; } = 20_000;

    public int MaxPayloads { get; init; } = 40_000;

    public long MaxPayloadBytes { get; init; } = 512L * 1024 * 1024;

    public long MaxPayloadBytesPerBlob { get; init; } = 128L * 1024 * 1024;

    public long MaxManifestBytes { get; init; } = 16L * 1024 * 1024;

    public double MaxCompressionRatio { get; init; } = 1_000;

    internal void Validate()
    {
        if (MaxOperations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOperations));
        }
        if (MaxPayloads <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPayloads));
        }
        if (MaxPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPayloadBytes));
        }
        if (MaxPayloadBytesPerBlob <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPayloadBytesPerBlob));
        }
        if (MaxManifestBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxManifestBytes));
        }
        if (MaxCompressionRatio <= 0 || double.IsNaN(MaxCompressionRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCompressionRatio));
        }
    }
}

public sealed record OpcPackagePatchOperation(
    string OperationId,
    OpcPackagePatchOperationKind Kind,
    string EntryName,
    string? PartUri,
    string? BeforeContentType,
    string? AfterContentType,
    string? BeforeSha256,
    long? BeforeBytes,
    string? AfterSha256,
    long? AfterBytes,
    bool IsInfrastructure
);

public sealed class OpcPackagePatch
{
    public const string Format = "wordtoolkit-opc-patch";

    public const int FormatVersion = 1;

    private readonly IReadOnlyDictionary<string, byte[]> _payloads;

    internal OpcPackagePatch(
        string patchId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<OpcPackagePatchOperation> operations,
        IReadOnlyDictionary<string, byte[]> payloads
    )
    {
        PatchId = patchId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        Operations = new ReadOnlyCollection<OpcPackagePatchOperation>(
            operations.ToArray()
        );
        _payloads = new ReadOnlyDictionary<string, byte[]>(
            new Dictionary<string, byte[]>(payloads, StringComparer.Ordinal)
        );
        PayloadBytes = _payloads.Values.Sum(payload => (long)payload.Length);
    }

    public string PatchId { get; }

    public string BasePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public IReadOnlyList<OpcPackagePatchOperation> Operations { get; }

    public int OperationCount => Operations.Count;

    public int AddedEntryCount => Operations.Count(operation =>
        operation.Kind == OpcPackagePatchOperationKind.Add
    );

    public int ReplacedEntryCount => Operations.Count(operation =>
        operation.Kind == OpcPackagePatchOperationKind.Replace
    );

    public int DeletedEntryCount => Operations.Count(operation =>
        operation.Kind == OpcPackagePatchOperationKind.Delete
    );

    public int PayloadCount => _payloads.Count;

    public long PayloadBytes { get; }

    public bool IsNoOp => Operations.Count == 0;

    public OpcPackageMutationBuilder CreateMutation(
        OpcPackageSnapshot currentSnapshot,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(currentSnapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                currentSnapshot.Fingerprint,
                BasePackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new OpcPackagePatchPreconditionException(
                "The current package does not match the patch base fingerprint."
            );
        }

        var mutation = new OpcPackageMutationBuilder(currentSnapshot);
        for (var index = 0; index < Operations.Count; index++)
        {
            if ((index & 0xff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var operation = Operations[index];
            switch (operation.Kind)
            {
                case OpcPackagePatchOperationKind.Add:
                    mutation.AddEntry(
                        operation.EntryName,
                        Payload(operation.AfterSha256)
                    );
                    break;
                case OpcPackagePatchOperationKind.Replace:
                    mutation.ReplaceEntry(
                        operation.EntryName,
                        Payload(operation.AfterSha256),
                        operation.BeforeSha256
                    );
                    break;
                case OpcPackagePatchOperationKind.Delete:
                    mutation.DeleteEntry(
                        operation.EntryName,
                        operation.BeforeSha256
                    );
                    break;
                default:
                    throw new OpcPackagePatchFormatException(
                        $"Unsupported patch operation '{operation.Kind}'."
                    );
            }
        }
        return mutation;
    }

    public OpcPackageSnapshot MaterializeCandidate(
        OpcPackageSnapshot currentSnapshot,
        OpcPackageReader? reader = null,
        OpcPackageSerializer? serializer = null,
        CancellationToken cancellationToken = default
    )
    {
        var mutation = CreateMutation(currentSnapshot, cancellationToken);
        using var stream = new MemoryStream();
        (serializer ?? new OpcPackageSerializer()).Write(stream, mutation);
        stream.Position = 0;
        var candidate = (reader ?? new OpcPackageReader()).Read(
            stream,
            cancellationToken
        );
        if (!string.Equals(
                candidate.Fingerprint,
                ResultPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new OpcPackagePatchResultMismatchException(
                "The materialized package does not match the patch result fingerprint."
            );
        }
        return candidate;
    }

    public OpcPackagePatch Reverse()
    {
        var reversed = Operations.Select(operation =>
        {
            var kind = operation.Kind switch
            {
                OpcPackagePatchOperationKind.Add => OpcPackagePatchOperationKind.Delete,
                OpcPackagePatchOperationKind.Delete => OpcPackagePatchOperationKind.Add,
                _ => OpcPackagePatchOperationKind.Replace,
            };
            return new OpcPackagePatchOperation(
                string.Empty,
                kind,
                operation.EntryName,
                operation.PartUri,
                operation.AfterContentType,
                operation.BeforeContentType,
                operation.AfterSha256,
                operation.AfterBytes,
                operation.BeforeSha256,
                operation.BeforeBytes,
                operation.IsInfrastructure
            );
        }).ToArray();
        return OpcPackagePatchBuilder.FinalizePatch(
            ResultPackageFingerprint,
            BasePackageFingerprint,
            reversed,
            _payloads
        );
    }

    internal IReadOnlyDictionary<string, byte[]> Payloads => _payloads;

    internal static string ComputePatchId(
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<OpcPackagePatchOperation> operations
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "wordtoolkit-opc-patch-v1");
        AppendHashField(hash, basePackageFingerprint);
        AppendHashField(hash, resultPackageFingerprint);
        foreach (var operation in operations)
        {
            AppendHashField(hash, operation.OperationId);
            AppendHashField(hash, operation.Kind.ToString());
            AppendHashField(hash, operation.EntryName);
            AppendHashField(hash, operation.PartUri ?? string.Empty);
            AppendHashField(hash, operation.BeforeContentType ?? string.Empty);
            AppendHashField(hash, operation.AfterContentType ?? string.Empty);
            AppendHashField(hash, operation.BeforeSha256 ?? string.Empty);
            AppendHashField(hash, Invariant(operation.BeforeBytes));
            AppendHashField(hash, operation.AfterSha256 ?? string.Empty);
            AppendHashField(hash, Invariant(operation.AfterBytes));
            AppendHashField(hash, operation.IsInfrastructure ? "1" : "0");
        }
        return "wtpatch_" + Base64Id(hash.GetHashAndReset(), 18);
    }

    internal static string ComputeOperationId(
        string basePackageFingerprint,
        string resultPackageFingerprint,
        OpcPackagePatchOperation operation
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "wordtoolkit-opc-patch-operation-v1");
        AppendHashField(hash, basePackageFingerprint);
        AppendHashField(hash, resultPackageFingerprint);
        AppendHashField(hash, operation.Kind.ToString());
        AppendHashField(hash, operation.EntryName);
        AppendHashField(hash, operation.BeforeSha256 ?? string.Empty);
        AppendHashField(hash, Invariant(operation.BeforeBytes));
        AppendHashField(hash, operation.AfterSha256 ?? string.Empty);
        AppendHashField(hash, Invariant(operation.AfterBytes));
        return "wtpo_" + Base64Id(hash.GetHashAndReset(), 15);
    }

    internal static string Hash(ReadOnlySpan<byte> content) => Convert.ToHexString(
        SHA256.HashData(content)
    ).ToLowerInvariant();

    private ReadOnlyMemory<byte> Payload(string? sha256)
    {
        if (sha256 is null || !_payloads.TryGetValue(sha256, out var payload))
        {
            throw new OpcPackagePatchFormatException(
                "A patch operation references a missing payload."
            );
        }
        return payload;
    }

    private static string Invariant(long? value) => value?.ToString(
        CultureInfo.InvariantCulture
    ) ?? string.Empty;

    private static string Base64Id(byte[] digest, int bytes) => Convert.ToBase64String(
        digest.AsSpan(0, bytes)
    ).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void AppendHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed class OpcPackagePatchBuilder
{
    private readonly OpcPackagePatchLimits _limits;

    public OpcPackagePatchBuilder(OpcPackagePatchLimits? limits = null)
    {
        _limits = limits ?? OpcPackagePatchLimits.Default;
        _limits.Validate();
    }

    public OpcPackagePatch Create(
        OpcPackageSnapshot before,
        OpcPackageSnapshot after,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        cancellationToken.ThrowIfCancellationRequested();
        var beforeEntries = UniqueEntries(before, "base");
        var afterEntries = UniqueEntries(after, "result");
        var names = beforeEntries.Keys.Concat(afterEntries.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var operations = new List<OpcPackagePatchOperation>();
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalPayloadBytes = 0;
        for (var nameIndex = 0; nameIndex < names.Length; nameIndex++)
        {
            if ((nameIndex & 0xff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var name = names[nameIndex];
            beforeEntries.TryGetValue(name, out var beforeEntry);
            afterEntries.TryGetValue(name, out var afterEntry);
            if (
                beforeEntry is not null
                && afterEntry is not null
                && beforeEntry.UncompressedLength == afterEntry.UncompressedLength
                && string.Equals(
                    beforeEntry.Sha256,
                    afterEntry.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }
            if (operations.Count >= _limits.MaxOperations)
            {
                throw new OpcPackagePatchLimitException(
                    $"Patch contains more than {_limits.MaxOperations} operations."
                );
            }
            var kind = beforeEntry is null
                ? OpcPackagePatchOperationKind.Add
                : afterEntry is null
                    ? OpcPackagePatchOperationKind.Delete
                    : OpcPackagePatchOperationKind.Replace;
            AddPayload(
                payloads,
                beforeEntry,
                _limits,
                ref totalPayloadBytes
            );
            AddPayload(
                payloads,
                afterEntry,
                _limits,
                ref totalPayloadBytes
            );
            var partUri = afterEntry?.PartUri ?? beforeEntry?.PartUri;
            operations.Add(new OpcPackagePatchOperation(
                string.Empty,
                kind,
                name,
                partUri,
                ContentType(before, beforeEntry),
                ContentType(after, afterEntry),
                beforeEntry?.Sha256,
                beforeEntry?.UncompressedLength,
                afterEntry?.Sha256,
                afterEntry?.UncompressedLength,
                beforeEntry?.IsInfrastructure == true
                    || afterEntry?.IsInfrastructure == true
            ));
        }
        return FinalizePatch(
            before.Fingerprint,
            after.Fingerprint,
            operations,
            payloads
        );
    }

    internal static OpcPackagePatch FinalizePatch(
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<OpcPackagePatchOperation> operations,
        IReadOnlyDictionary<string, byte[]> payloads
    )
    {
        var ordered = operations.OrderBy(operation => operation.EntryName, StringComparer.Ordinal)
            .Select(operation => operation with
            {
                OperationId = OpcPackagePatch.ComputeOperationId(
                    basePackageFingerprint,
                    resultPackageFingerprint,
                    operation
                ),
            })
            .ToArray();
        var patchId = OpcPackagePatch.ComputePatchId(
            basePackageFingerprint,
            resultPackageFingerprint,
            ordered
        );
        return new OpcPackagePatch(
            patchId,
            basePackageFingerprint,
            resultPackageFingerprint,
            ordered,
            payloads
        );
    }

    private static IReadOnlyDictionary<string, OpcPackageEntry> UniqueEntries(
        OpcPackageSnapshot snapshot,
        string side
    )
    {
        var duplicate = snapshot.Entries.GroupBy(
            entry => entry.Name,
            StringComparer.Ordinal
        ).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new OpcPackagePatchPreconditionException(
                $"The {side} package contains duplicate entry '{duplicate.Key}'."
            );
        }
        return snapshot.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal
        );
    }

    private static string? ContentType(
        OpcPackageSnapshot package,
        OpcPackageEntry? entry
    ) => entry?.PartUri is { } partUri
        && package.Parts.TryGetValue(partUri, out var part)
            ? part.ContentType
            : null;

    private static void AddPayload(
        IDictionary<string, byte[]> payloads,
        OpcPackageEntry? entry,
        OpcPackagePatchLimits limits,
        ref long totalPayloadBytes
    )
    {
        if (entry is null)
        {
            return;
        }
        if (entry.UncompressedLength > limits.MaxPayloadBytesPerBlob)
        {
            throw new OpcPackagePatchLimitException(
                $"Entry '{entry.Name}' exceeds the per-payload patch limit."
            );
        }
        if (payloads.TryGetValue(entry.Sha256, out var existing))
        {
            if (!existing.AsSpan().SequenceEqual(entry.Content.Span))
            {
                throw new OpcPackagePatchCollisionException(
                    "Two distinct payloads have the same SHA-256 digest."
                );
            }
            return;
        }
        if (payloads.Count >= limits.MaxPayloads)
        {
            throw new OpcPackagePatchLimitException(
                $"Patch contains more than {limits.MaxPayloads} payloads."
            );
        }
        var projectedTotal = checked(
            totalPayloadBytes + entry.UncompressedLength
        );
        if (projectedTotal > limits.MaxPayloadBytes)
        {
            throw new OpcPackagePatchLimitException(
                $"Patch payloads exceed {limits.MaxPayloadBytes} bytes."
            );
        }
        var bytes = entry.Content.ToArray();
        payloads.Add(entry.Sha256, bytes);
        totalPayloadBytes = projectedTotal;
    }
}

public class OpcPackagePatchException : InvalidOperationException
{
    public OpcPackagePatchException(string message)
        : base(message)
    {
    }
}

public sealed class OpcPackagePatchPreconditionException : OpcPackagePatchException
{
    public OpcPackagePatchPreconditionException(string message)
        : base(message)
    {
    }
}

public sealed class OpcPackagePatchFormatException : OpcPackagePatchException
{
    public OpcPackagePatchFormatException(string message)
        : base(message)
    {
    }
}

public sealed class OpcPackagePatchLimitException : OpcPackagePatchException
{
    public OpcPackagePatchLimitException(string message)
        : base(message)
    {
    }
}

public sealed class OpcPackagePatchCollisionException : OpcPackagePatchException
{
    public OpcPackagePatchCollisionException(string message)
        : base(message)
    {
    }
}

public sealed class OpcPackagePatchResultMismatchException : OpcPackagePatchException
{
    public OpcPackagePatchResultMismatchException(string message)
        : base(message)
    {
    }
}
