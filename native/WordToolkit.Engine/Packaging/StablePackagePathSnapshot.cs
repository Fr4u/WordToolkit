using System.Buffers;
using System.Security.Cryptography;

namespace WordToolkit.Engine.Packaging;

public sealed class OpcPackageSourceChangedException : IOException
{
    public OpcPackageSourceChangedException()
        : base("Package changed while a stable snapshot was being captured.")
    {
    }
}

internal sealed class ZeroingMemoryStream : MemoryStream
{
    protected override void Dispose(bool disposing)
    {
        if (disposing && TryGetBuffer(out var buffer))
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan());
        }
        base.Dispose(disposing);
    }
}

internal static class StablePackagePathSnapshot
{
    private const int Attempts = 2;
    private const int BufferBytes = 80 * 1024;

    internal static ZeroingMemoryStream Capture(
        string path,
        long maxBytes,
        CancellationToken cancellationToken,
        Action<int>? afterCopy = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new ZeroingMemoryStream();
            SnapshotHash? copied = null;
            SnapshotHash? verified = null;
            try
            {
                using var source = OpenSharedSource(path);
                copied = CopyAndHash(
                    source,
                    snapshot,
                    maxBytes,
                    cancellationToken
                );
                afterCopy?.Invoke(attempt);
                source.Position = 0;
                verified = CopyAndHash(
                    source,
                    Stream.Null,
                    maxBytes,
                    cancellationToken
                );
                var stable = copied.Bytes == verified.Bytes
                    && CryptographicOperations.FixedTimeEquals(
                        copied.Sha256,
                        verified.Sha256
                    );
                if (stable)
                {
                    snapshot.Position = 0;
                    return snapshot;
                }
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
            finally
            {
                if (copied is not null)
                {
                    CryptographicOperations.ZeroMemory(copied.Sha256);
                }
                if (verified is not null)
                {
                    CryptographicOperations.ZeroMemory(verified.Sha256);
                }
            }

            snapshot.Dispose();
        }

        throw new OpcPackageSourceChangedException();
    }

    private static FileStream OpenSharedSource(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferBytes,
            FileOptions.SequentialScan
        );

    private static SnapshotHash CopyAndHash(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken
    )
    {
        if (source.Length > maxBytes)
        {
            throw new OpcPackageLimitException(
                $"Package stream has {source.Length} bytes; limit is {maxBytes}."
            );
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long totalBytes = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }
                if (totalBytes > maxBytes - read)
                {
                    throw new OpcPackageLimitException(
                        $"Package stream exceeds {maxBytes} bytes."
                    );
                }
                destination.Write(buffer, 0, read);
                hash.AppendData(buffer, 0, read);
                totalBytes += read;
            }
            return new SnapshotHash(totalBytes, hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private sealed record SnapshotHash(long Bytes, byte[] Sha256);
}
