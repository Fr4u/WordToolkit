using System.Security.Cryptography;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Tests;

public sealed class EncryptedTemporaryStreamTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData((1024 * 1024) - 1)]
    [InlineData(1024 * 1024)]
    [InlineData((1024 * 1024) + 1)]
    public void BoundaryLengthsRoundTrip(int length)
    {
        using var stream = new EncryptedTemporaryStream(
            maxBytes: (1024 * 1024) + 1
        );
        var expected = RandomNumberGenerator.GetBytes(length);
        stream.Write(expected);
        stream.CompleteWriting();
        var actual = new byte[length];

        stream.ReadExactly(actual);

        Assert.Equal(expected, actual);
        Assert.Equal(length, stream.Length);
    }

    [Fact]
    public void RandomSeekAcrossEncryptedBlocksRoundTrips()
    {
        const int blockBytes = 1024 * 1024;
        using var stream = new EncryptedTemporaryStream(maxBytes: 3 * blockBytes);
        var expected = RandomNumberGenerator.GetBytes((2 * blockBytes) + 96 * 1024);
        stream.Write(expected);
        stream.CompleteWriting();

        var random = new Random(17);
        var buffer = new byte[4096];
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var offset = random.Next(0, expected.Length - buffer.Length);
            stream.Seek(offset, SeekOrigin.Begin);
            var read = stream.Read(buffer);

            Assert.Equal(buffer.Length, read);
            Assert.Equal(expected.AsSpan(offset, buffer.Length).ToArray(), buffer);
        }

        foreach (var offset in new[] { blockBytes - 2048, (2 * blockBytes) - 2048 })
        {
            stream.Position = offset;
            var read = stream.Read(buffer);

            Assert.Equal(buffer.Length, read);
            Assert.Equal(expected.AsSpan(offset, buffer.Length).ToArray(), buffer);
        }
    }

    [Fact]
    public void LimitIsRejectedBeforeTheBackingFileGrowsPastIt()
    {
        using var stream = new EncryptedTemporaryStream(maxBytes: 32);
        stream.Write(new byte[32]);
        var backingLength = new FileInfo(stream.BackingPath).Length;

        Assert.Throws<IOException>(() => stream.Write(new byte[] { 1 }));
        Assert.Equal(backingLength, new FileInfo(stream.BackingPath).Length);
        Assert.Equal(32, stream.Length);
    }

    [Fact]
    public void BackingBytesAreEncryptedAndFileIsRemovedOnDispose()
    {
        const string marker = "WORDTOOLKIT-PLAINTEXT-MARKER";
        var plaintext = System.Text.Encoding.UTF8.GetBytes(marker);
        string backingPath;

        using (var stream = new EncryptedTemporaryStream(maxBytes: 4096))
        {
            stream.Write(plaintext);
            stream.CompleteWriting();
            backingPath = stream.BackingPath;

            using var backing = new FileStream(
                backingPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            var backingBytes = new byte[backing.Length];
            backing.ReadExactly(backingBytes);
            Assert.True(backingBytes.AsSpan().IndexOf(plaintext) < 0);
            Assert.True(File.Exists(backingPath));
        }

        Assert.False(File.Exists(backingPath));
    }

    [Fact]
    public void DisposedStreamRejectsFurtherUse()
    {
        var stream = new EncryptedTemporaryStream(maxBytes: 1024);
        stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.Write(new byte[] { 1 }));
        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[1]));
    }

    [Fact]
    public void DisposingAnIncompleteSpoolDiscardsTheBufferAndDeletesTheBackingFile()
    {
        var stream = new EncryptedTemporaryStream(maxBytes: 1024);
        stream.Write(RandomNumberGenerator.GetBytes(1024));
        var backingPath = stream.BackingPath;

        stream.Dispose();

        Assert.False(File.Exists(backingPath));
    }
}
