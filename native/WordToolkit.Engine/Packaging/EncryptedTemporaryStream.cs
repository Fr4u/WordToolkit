using System.Buffers.Binary;
using System.Security.Cryptography;

namespace WordToolkit.Engine.Packaging;

/// <summary>
/// A bounded, seekable spool that writes only AES-GCM ciphertext to its
/// delete-on-close backing file.
/// </summary>
internal sealed class EncryptedTemporaryStream : Stream
{
    private const int BlockSize = 1024 * 1024;
    private const int HeaderSize = sizeof(int);
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int BufferBytes = 64 * 1024;

    private readonly FileStream _file;
    private readonly AesGcm _aes;
    private readonly byte[] _key;
    private readonly byte[] _noncePrefix;
    private readonly byte[] _writeBuffer;
    private readonly byte[] _readBuffer;
    private readonly byte[] _cipherBuffer;
    private readonly byte[] _tagBuffer;
    private readonly long _maxLength;

    private long _position;
    private long _length;
    private long _cachedReadBlock = -1;
    private int _writeCount;
    private bool _complete;
    private bool _faulted;
    private bool _disposed;

    internal EncryptedTemporaryStream(long maxBytes)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        _maxLength = maxBytes;
        BackingPath = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-secure-{Guid.NewGuid():N}.tmp"
        );

        FileStream? file = null;
        AesGcm? aes = null;
        byte[]? key = null;
        byte[]? noncePrefix = null;
        byte[]? writeBuffer = null;
        byte[]? readBuffer = null;
        byte[]? cipherBuffer = null;
        byte[]? tagBuffer = null;
        try
        {
            file = new FileStream(
                BackingPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete,
                BufferBytes,
                FileOptions.DeleteOnClose | FileOptions.RandomAccess
            );
            key = RandomNumberGenerator.GetBytes(KeySize);
            noncePrefix = RandomNumberGenerator.GetBytes(
                NonceSize - sizeof(ulong)
            );
            writeBuffer = GC.AllocateUninitializedArray<byte>(BlockSize);
            readBuffer = GC.AllocateUninitializedArray<byte>(BlockSize);
            cipherBuffer = GC.AllocateUninitializedArray<byte>(BlockSize);
            tagBuffer = GC.AllocateUninitializedArray<byte>(TagSize);
            aes = new AesGcm(key, TagSize);

            _file = file;
            _key = key;
            _noncePrefix = noncePrefix;
            _writeBuffer = writeBuffer;
            _readBuffer = readBuffer;
            _cipherBuffer = cipherBuffer;
            _tagBuffer = tagBuffer;
            _aes = aes;
        }
        catch
        {
            aes?.Dispose();
            ZeroIfPresent(key);
            ZeroIfPresent(noncePrefix);
            ZeroIfPresent(writeBuffer);
            ZeroIfPresent(readBuffer);
            ZeroIfPresent(cipherBuffer);
            ZeroIfPresent(tagBuffer);
            try
            {
                file?.Dispose();
            }
            finally
            {
                TryDeleteBackingFile();
            }
            throw;
        }
    }

    internal string BackingPath { get; }

    public override bool CanRead => !_disposed && _complete && !_faulted;

    public override bool CanSeek => !_disposed && _complete && !_faulted;

    public override bool CanWrite => !_disposed && !_complete && !_faulted;

    public override long Length
    {
        get
        {
            EnsureUsable();
            return _length;
        }
    }

    public override long Position
    {
        get
        {
            EnsureUsable();
            return _position;
        }
        set
        {
            EnsureReadable();
            if (value < 0 || value > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _position = value;
        }
    }

    internal void CompleteWriting()
    {
        EnsureUsable();
        if (_complete)
        {
            return;
        }

        try
        {
            if (_writeCount > 0)
            {
                WriteBlock(_length / BlockSize, _writeCount);
                _writeCount = 0;
            }

            _file.Flush(flushToDisk: false);
            _complete = true;
            _position = 0;
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureWritable();
        if (buffer.Length > _maxLength - _length)
        {
            throw new IOException(
                $"Encrypted spool exceeds its {_maxLength}-byte plaintext limit."
            );
        }

        while (!buffer.IsEmpty)
        {
            var take = Math.Min(buffer.Length, BlockSize - _writeCount);
            buffer[..take].CopyTo(_writeBuffer.AsSpan(_writeCount));
            _writeCount += take;
            _length += take;
            _position = _length;
            buffer = buffer[take..];

            if (_writeCount != BlockSize)
            {
                continue;
            }

            try
            {
                WriteBlock((_length - 1) / BlockSize, BlockSize);
                _writeCount = 0;
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        EnsureReadable();
        var available = checked((int)Math.Min(buffer.Length, _length - _position));
        var copied = 0;
        while (copied < available)
        {
            var block = _position / BlockSize;
            var inside = checked((int)(_position % BlockSize));
            var take = Math.Min(available - copied, BlockSize - inside);
            ReadBlock(block);
            _readBuffer
                .AsSpan(inside, take)
                .CopyTo(buffer.Slice(copied, take));
            copied += take;
            _position += take;
        }

        return copied;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        EnsureReadable();
        long next;
        try
        {
            next = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(_length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
        }
        catch (OverflowException exception)
        {
            throw new IOException("Seek is outside the encrypted spool.", exception);
        }

        if (next < 0 || next > _length)
        {
            throw new IOException("Seek is outside the encrypted spool.");
        }

        _position = next;
        return next;
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Flush()
    {
        EnsureUsable();
        _file.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (disposing)
            {
                try
                {
                    _aes.Dispose();
                }
                finally
                {
                    _file.Dispose();
                }
            }
        }
        finally
        {
            if (disposing)
            {
                CryptographicOperations.ZeroMemory(_key);
                CryptographicOperations.ZeroMemory(_noncePrefix);
                CryptographicOperations.ZeroMemory(_writeBuffer);
                CryptographicOperations.ZeroMemory(_readBuffer);
                CryptographicOperations.ZeroMemory(_cipherBuffer);
                CryptographicOperations.ZeroMemory(_tagBuffer);
                TryDeleteBackingFile();
            }
            base.Dispose(disposing);
        }
    }

    private void WriteBlock(long block, int plaintextLength)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, plaintextLength);
        Span<byte> nonce = stackalloc byte[NonceSize];
        BuildNonce(block, nonce);

        if (plaintextLength < BlockSize)
        {
            _writeBuffer.AsSpan(plaintextLength).Clear();
        }

        try
        {
            _aes.Encrypt(
                nonce,
                _writeBuffer,
                _cipherBuffer,
                _tagBuffer,
                header
            );
            _file.Write(header);
            _file.Write(_cipherBuffer);
            _file.Write(_tagBuffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(_writeBuffer);
            CryptographicOperations.ZeroMemory(_cipherBuffer);
            CryptographicOperations.ZeroMemory(_tagBuffer);
        }
    }

    private void ReadBlock(long block)
    {
        if (_cachedReadBlock == block)
        {
            return;
        }

        _cachedReadBlock = -1;
        CryptographicOperations.ZeroMemory(_readBuffer);
        var recordOffset = checked(
            (HeaderSize + (long)BlockSize + TagSize) * block
        );
        _file.Position = recordOffset;
        Span<byte> header = stackalloc byte[HeaderSize];
        _file.ReadExactly(header);
        var plaintextLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        var expectedLength = checked(
            (int)Math.Min(BlockSize, _length - (block * BlockSize))
        );
        if (plaintextLength != expectedLength)
        {
            throw new InvalidDataException("Encrypted spool record is invalid.");
        }

        Span<byte> nonce = stackalloc byte[NonceSize];
        BuildNonce(block, nonce);
        try
        {
            _file.ReadExactly(_cipherBuffer);
            _file.ReadExactly(_tagBuffer);
            _aes.Decrypt(
                nonce,
                _cipherBuffer,
                _tagBuffer,
                _readBuffer,
                header
            );
            _cachedReadBlock = block;
        }
        catch
        {
            _faulted = true;
            CryptographicOperations.ZeroMemory(_readBuffer);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(_cipherBuffer);
            CryptographicOperations.ZeroMemory(_tagBuffer);
        }
    }

    private void BuildNonce(long block, Span<byte> nonce)
    {
        if (block < 0)
        {
            throw new InvalidDataException("Encrypted spool block index is invalid.");
        }

        _noncePrefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(
            nonce[(NonceSize - sizeof(ulong))..],
            checked((ulong)block)
        );
    }

    private void EnsureReadable()
    {
        EnsureUsable();
        if (!_complete)
        {
            throw new InvalidOperationException(
                "CompleteWriting must succeed before the encrypted spool can be read."
            );
        }
    }

    private void EnsureWritable()
    {
        EnsureUsable();
        if (_complete)
        {
            throw new InvalidOperationException("Writing is already complete.");
        }
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted)
        {
            throw new IOException("The encrypted spool is in a faulted state.");
        }
    }

    private void TryDeleteBackingFile()
    {
        try
        {
            File.Delete(BackingPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ZeroIfPresent(byte[]? buffer)
    {
        if (buffer is not null)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}
