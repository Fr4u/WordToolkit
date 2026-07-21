using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WordToolkit.Engine.Packaging;

internal static class OpcPackageFingerprint
{
    public static string Compute(IEnumerable<OpcPackageEntry> entries) =>
        Compute(entries.Select(entry =>
            new FingerprintEntry(entry.Name, entry.Sha256, entry.UncompressedLength)
        ));

    public static string ComputeProjected(
        OpcPackageSnapshot snapshot,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> replacements
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(replacements);
        return Compute(snapshot.Entries.Select(entry =>
        {
            if (!replacements.TryGetValue(entry.Name, out var replacement))
            {
                return new FingerprintEntry(
                    entry.Name,
                    entry.Sha256,
                    entry.UncompressedLength
                );
            }

            return new FingerprintEntry(
                entry.Name,
                Convert.ToHexString(SHA256.HashData(replacement.Span)).ToLowerInvariant(),
                replacement.Length
            );
        }));
    }

    private static string Compute(IEnumerable<FingerprintEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (
            var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Sha256, StringComparer.Ordinal)
        )
        {
            AppendHashField(hash, entry.Name);
            AppendHashField(hash, entry.Sha256);
            AppendHashField(
                hash,
                entry.UncompressedLength.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                )
            );
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

    private sealed record FingerprintEntry(
        string Name,
        string Sha256,
        long UncompressedLength
    );
}
