using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WordToolkit.Engine.Packaging;

public sealed record OpcPackagePatchEnvelopeLimits
{
    public static OpcPackagePatchEnvelopeLimits Default { get; } = new();

    public long MaxSerializedPatchBytes { get; init; } = 140L * 1024 * 1024;

    public int MaxMetadataBytes { get; init; } = 16 * 1024;

    public int MaxSignatureBytes { get; init; } = 32 * 1024;

    public double MaxCompressionRatio { get; init; } = 100;

    internal void Validate()
    {
        if (MaxSerializedPatchBytes <= 0 || MaxSerializedPatchBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSerializedPatchBytes));
        }
        if (MaxMetadataBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMetadataBytes));
        }
        if (MaxSignatureBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSignatureBytes));
        }
        if (MaxCompressionRatio <= 0 || double.IsNaN(MaxCompressionRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCompressionRatio));
        }
    }
}

public sealed record OpcPackagePatchEnvelopeInfo(
    bool Encrypted,
    bool Signed,
    string? SignerKeyId,
    long SerializedPatchBytes,
    string PlaintextSha256
);

public sealed record OpcPackagePatchEnvelopeReadResult(
    OpcPackagePatch Patch,
    OpcPackagePatchEnvelopeInfo Envelope
);

public sealed class OpcPackagePatchEnvelopeCodec
{
    public const string Format = "wordtoolkit-opc-patch-envelope";

    public const int FormatVersion = 1;

    private const string MetadataEntryName = "envelope.json";
    private const string PayloadEntryName = "payload.bin";
    private const string TagEntryName = "authentication-tag.bin";
    private const string SignatureEntryName = "signature.bin";
    private const string NoEncryption = "none";
    private const string AesEncryption = "aes-256-gcm";
    private const string NoSignature = "none";
    private const string EcdsaSignature = "ecdsa-sha256";
    private const int AesKeyBytes = 32;
    private const int AesNonceBytes = 12;
    private const int AesTagBytes = 16;

    private readonly OpcPackagePatchLimits _patchLimits;
    private readonly OpcPackagePatchEnvelopeLimits _envelopeLimits;

    public OpcPackagePatchEnvelopeCodec(
        OpcPackagePatchLimits? patchLimits = null,
        OpcPackagePatchEnvelopeLimits? envelopeLimits = null
    )
    {
        _patchLimits = patchLimits ?? OpcPackagePatchLimits.Default;
        _envelopeLimits = envelopeLimits ?? OpcPackagePatchEnvelopeLimits.Default;
        _patchLimits.Validate();
        _envelopeLimits.Validate();
    }

    public OpcPackagePatchEnvelopeInfo Write(
        Stream destination,
        OpcPackagePatch patch,
        byte[]? encryptionKey = null,
        ECDsa? signer = null,
        string? signerKeyId = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(patch);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDestination(destination);
        ValidateProtectionArguments(encryptionKey, signer, signerKeyId);

        byte[] plaintext;
        using (var serialized = new MemoryStream())
        {
            try
            {
                new OpcPackagePatchCodec(_patchLimits).Write(
                    serialized,
                    patch,
                    cancellationToken
                );
                if (serialized.Length > _envelopeLimits.MaxSerializedPatchBytes)
                {
                    throw new OpcPackagePatchEnvelopeLimitException(
                        "Serialized patch exceeds the envelope byte limit."
                    );
                }
                plaintext = serialized.ToArray();
            }
            finally
            {
                if (serialized.TryGetBuffer(out var buffer))
                {
                    CryptographicOperations.ZeroMemory(
                        buffer.AsSpan(0, checked((int)serialized.Length))
                    );
                }
            }
        }

        byte[]? payload = null;
        byte[]? nonce = null;
        byte[]? tag = null;
        byte[]? signature = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plaintextSha256 = Convert.ToHexString(SHA256.HashData(plaintext))
                .ToLowerInvariant();
            var encrypted = encryptionKey is not null;
            var signed = signer is not null;
            if (encrypted)
            {
                nonce = RandomNumberGenerator.GetBytes(AesNonceBytes);
            }
            var metadata = WriteMetadata(
                encrypted,
                signed,
                signerKeyId,
                plaintext.LongLength,
                nonce
            );
            if (metadata.LongLength > _envelopeLimits.MaxMetadataBytes)
            {
                throw new OpcPackagePatchEnvelopeLimitException(
                    "Patch envelope metadata exceeds its byte limit."
                );
            }

            if (encrypted)
            {
                payload = new byte[plaintext.Length];
                tag = new byte[AesTagBytes];
                using var aes = new AesGcm(encryptionKey!, AesTagBytes);
                aes.Encrypt(nonce!, plaintext, payload, tag, metadata);
            }
            else
            {
                payload = plaintext.ToArray();
                tag = [];
            }

            if (signed)
            {
                signature = signer!.SignHash(HashEnvelope(metadata, tag, payload));
                if (signature.Length > _envelopeLimits.MaxSignatureBytes)
                {
                    throw new OpcPackagePatchEnvelopeLimitException(
                        "Patch envelope signature exceeds its byte limit."
                    );
                }
            }

            using var archive = new ZipArchive(
                destination,
                ZipArchiveMode.Create,
                leaveOpen: true
            );
            WriteEntry(archive, MetadataEntryName, metadata);
            WriteEntry(archive, PayloadEntryName, payload);
            if (encrypted)
            {
                WriteEntry(archive, TagEntryName, tag);
            }
            if (signed)
            {
                WriteEntry(archive, SignatureEntryName, signature!);
            }
            return new OpcPackagePatchEnvelopeInfo(
                encrypted,
                signed,
                signerKeyId,
                plaintext.LongLength,
                plaintextSha256
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (payload is not null && !ReferenceEquals(payload, plaintext))
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    public OpcPackagePatchEnvelopeReadResult Read(
        Stream source,
        byte[]? decryptionKey = null,
        ECDsa? signatureVerifier = null,
        string? expectedSignerKeyId = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!source.CanRead)
        {
            throw new ArgumentException("Patch envelope source must be readable.", nameof(source));
        }

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entries = ReadEntryMap(archive);
        var metadataBytes = ReadEntry(
            RequiredEntry(entries, MetadataEntryName),
            _envelopeLimits.MaxMetadataBytes,
            cancellationToken
        );
        var metadata = ParseMetadata(metadataBytes);
        ValidateReadProtectionArguments(
            metadata,
            decryptionKey,
            signatureVerifier,
            expectedSignerKeyId
        );
        var payload = ReadEntry(
            RequiredEntry(entries, PayloadEntryName),
            _envelopeLimits.MaxSerializedPatchBytes,
            cancellationToken
        );
        byte[] tag = [];
        byte[]? signature = null;
        byte[]? plaintext = null;
        try
        {
            if (payload.LongLength != metadata.SerializedPatchBytes)
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope payload length does not match metadata."
                );
            }
            if (metadata.Encrypted)
            {
                tag = ReadEntry(
                    RequiredEntry(entries, TagEntryName),
                    AesTagBytes,
                    cancellationToken
                );
                if (tag.Length != AesTagBytes)
                {
                    throw new OpcPackagePatchEnvelopeFormatException(
                        "Patch envelope authentication tag has the wrong length."
                    );
                }
            }
            if (metadata.Signed)
            {
                signature = ReadEntry(
                    RequiredEntry(entries, SignatureEntryName),
                    _envelopeLimits.MaxSignatureBytes,
                    cancellationToken
                );
                if (
                    !signatureVerifier!.VerifyHash(
                        HashEnvelope(metadataBytes, tag, payload),
                        signature
                    )
                )
                {
                    throw new OpcPackagePatchEnvelopeAuthenticationException(
                        "Patch envelope signature verification failed."
                    );
                }
            }
            ValidateExpectedEntries(entries, metadata);
            cancellationToken.ThrowIfCancellationRequested();

            if (metadata.Encrypted)
            {
                plaintext = new byte[payload.Length];
                try
                {
                    using var aes = new AesGcm(decryptionKey!, AesTagBytes);
                    aes.Decrypt(
                        metadata.Nonce!,
                        payload,
                        tag,
                        plaintext,
                        metadataBytes
                    );
                }
                catch (AuthenticationTagMismatchException exception)
                {
                    throw new OpcPackagePatchEnvelopeAuthenticationException(
                        "Patch envelope decryption authentication failed.",
                        exception
                    );
                }
            }
            else
            {
                plaintext = payload.ToArray();
            }

            var actualSha256 = Convert.ToHexString(SHA256.HashData(plaintext))
                .ToLowerInvariant();
            using var serialized = new MemoryStream(plaintext, writable: false);
            var patch = new OpcPackagePatchCodec(_patchLimits).Read(
                serialized,
                cancellationToken
            );
            return new OpcPackagePatchEnvelopeReadResult(
                patch,
                new OpcPackagePatchEnvelopeInfo(
                    metadata.Encrypted,
                    metadata.Signed,
                    metadata.SignerKeyId,
                    metadata.SerializedPatchBytes,
                    actualSha256
                )
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static void ValidateDestination(Stream destination)
    {
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "Patch envelope destination must be writable.",
                nameof(destination)
            );
        }
        if (destination.CanSeek && (destination.Position != 0 || destination.Length != 0))
        {
            throw new ArgumentException(
                "Patch envelope destination must be empty and positioned at zero.",
                nameof(destination)
            );
        }
    }

    private static void ValidateProtectionArguments(
        byte[]? encryptionKey,
        ECDsa? signer,
        string? signerKeyId
    )
    {
        if (encryptionKey is not null && encryptionKey.Length != AesKeyBytes)
        {
            throw new ArgumentException(
                "AES-256-GCM requires a 32-byte key.",
                nameof(encryptionKey)
            );
        }
        if (signer is null && signerKeyId is not null)
        {
            throw new ArgumentException(
                "A signer key id cannot be supplied without a signer.",
                nameof(signerKeyId)
            );
        }
        if (signer is not null && !ValidKeyId(signerKeyId))
        {
            throw new ArgumentException(
                "Signed envelopes require a 1-128 character signer key id.",
                nameof(signerKeyId)
            );
        }
    }

    private static void ValidateReadProtectionArguments(
        ParsedMetadata metadata,
        byte[]? decryptionKey,
        ECDsa? signatureVerifier,
        string? expectedSignerKeyId
    )
    {
        if (metadata.Encrypted && decryptionKey?.Length != AesKeyBytes)
        {
            throw new OpcPackagePatchEnvelopeAuthenticationException(
                "A 32-byte decryption key is required for this patch envelope."
            );
        }
        if (!metadata.Encrypted && decryptionKey is not null)
        {
            throw new OpcPackagePatchEnvelopeAuthenticationException(
                "A decryption key was supplied for an unencrypted patch envelope."
            );
        }
        if (metadata.Signed && signatureVerifier is null)
        {
            throw new OpcPackagePatchEnvelopeAuthenticationException(
                "A signature verifier is required for this patch envelope."
            );
        }
        if (!metadata.Signed && (signatureVerifier is not null || expectedSignerKeyId is not null))
        {
            throw new OpcPackagePatchEnvelopeAuthenticationException(
                "Signature verification was requested for an unsigned patch envelope."
            );
        }
        if (
            expectedSignerKeyId is not null
            && !string.Equals(
                expectedSignerKeyId,
                metadata.SignerKeyId,
                StringComparison.Ordinal
            )
        )
        {
            throw new OpcPackagePatchEnvelopeAuthenticationException(
                "Patch envelope signer key id does not match the expected identity."
            );
        }
    }

    private Dictionary<string, ZipArchiveEntry> ReadEntryMap(ZipArchive archive)
    {
        if (archive.Entries.Count is < 2 or > 4)
        {
            throw new OpcPackagePatchEnvelopeFormatException(
                "Patch envelope contains an invalid number of entries."
            );
        }
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (
                entry.FullName.Length == 0
                || entry.FullName.Contains('\\', StringComparison.Ordinal)
                || entry.FullName.StartsWith("/", StringComparison.Ordinal)
                || entry.FullName.Contains("..", StringComparison.Ordinal)
            )
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope contains an unsafe entry name."
                );
            }
            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope contains a duplicate entry."
                );
            }
            var maximum = entry.FullName switch
            {
                MetadataEntryName => _envelopeLimits.MaxMetadataBytes,
                PayloadEntryName => _envelopeLimits.MaxSerializedPatchBytes,
                TagEntryName => AesTagBytes,
                SignatureEntryName => _envelopeLimits.MaxSignatureBytes,
                _ => throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope contains an unknown entry."
                ),
            };
            if (entry.Length > maximum)
            {
                throw new OpcPackagePatchEnvelopeLimitException(
                    "Patch envelope entry exceeds its byte limit."
                );
            }
            var ratio = entry.Length / (double)Math.Max(1, entry.CompressedLength);
            if (ratio > _envelopeLimits.MaxCompressionRatio)
            {
                throw new OpcPackagePatchEnvelopeLimitException(
                    "Patch envelope entry exceeds its compression-ratio limit."
                );
            }
        }
        return entries;
    }

    private static ZipArchiveEntry RequiredEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string name
    )
    {
        return entries.TryGetValue(name, out var entry)
            ? entry
            : throw new OpcPackagePatchEnvelopeFormatException(
                $"Patch envelope is missing '{name}'."
            );
    }

    private static void ValidateExpectedEntries(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        ParsedMetadata metadata
    )
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            MetadataEntryName,
            PayloadEntryName,
        };
        if (metadata.Encrypted)
        {
            expected.Add(TagEntryName);
        }
        if (metadata.Signed)
        {
            expected.Add(SignatureEntryName);
        }
        if (entries.Keys.Any(name => !expected.Contains(name)) || entries.Count != expected.Count)
        {
            throw new OpcPackagePatchEnvelopeFormatException(
                "Patch envelope entries do not match its protection metadata."
            );
        }
    }

    private static byte[] WriteMetadata(
        bool encrypted,
        bool signed,
        string? signerKeyId,
        long serializedPatchBytes,
        byte[]? nonce
    )
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteNumber("version", FormatVersion);
            writer.WriteNumber("serialized_patch_bytes", serializedPatchBytes);
            writer.WriteString("encryption", encrypted ? AesEncryption : NoEncryption);
            if (encrypted)
            {
                writer.WriteString("nonce", Convert.ToBase64String(nonce!));
            }
            else
            {
                writer.WriteNull("nonce");
            }
            writer.WriteString("signature", signed ? EcdsaSignature : NoSignature);
            if (signed)
            {
                writer.WriteString("signer_key_id", signerKeyId);
            }
            else
            {
                writer.WriteNull("signer_key_id");
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private ParsedMetadata ParseMetadata(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope metadata must be an object."
                );
            }
            var expectedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "format",
                "version",
                "serialized_patch_bytes",
                "encryption",
                "nonce",
                "signature",
                "signer_key_id",
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!expectedNames.Contains(property.Name) || !seen.Add(property.Name))
                {
                    throw new OpcPackagePatchEnvelopeFormatException(
                        "Patch envelope metadata contains an unknown or duplicate field."
                    );
                }
            }
            if (seen.Count != expectedNames.Count)
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope metadata is incomplete."
                );
            }
            if (RequiredString(root, "format") != Format)
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope format identifier is unsupported."
                );
            }
            if (RequiredInt32(root, "version") != FormatVersion)
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope version is unsupported."
                );
            }
            var serializedPatchBytes = RequiredInt64(root, "serialized_patch_bytes");
            if (
                serializedPatchBytes < 0
                || serializedPatchBytes > _envelopeLimits.MaxSerializedPatchBytes
            )
            {
                throw new OpcPackagePatchEnvelopeLimitException(
                    "Patch envelope serialized size exceeds its limit."
                );
            }
            var encryption = RequiredString(root, "encryption");
            var encrypted = encryption switch
            {
                AesEncryption => true,
                NoEncryption => false,
                _ => throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope encryption algorithm is unsupported."
                ),
            };
            var nonce = OptionalBase64(root, "nonce");
            if (encrypted ? nonce?.Length != AesNonceBytes : nonce is not null)
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope nonce does not match its encryption mode."
                );
            }
            var signature = RequiredString(root, "signature");
            var signed = signature switch
            {
                EcdsaSignature => true,
                NoSignature => false,
                _ => throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope signature algorithm is unsupported."
                ),
            };
            var signerKeyId = OptionalString(root, "signer_key_id");
            if (signed ? !ValidKeyId(signerKeyId) : signerKeyId is not null)
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope signer identity does not match its signature mode."
                );
            }
            return new ParsedMetadata(
                encrypted,
                signed,
                signerKeyId,
                serializedPatchBytes,
                nonce
            );
        }
        catch (JsonException exception)
        {
            throw new OpcPackagePatchEnvelopeFormatException(
                "Patch envelope metadata is invalid JSON.",
                exception
            );
        }
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
        {
            throw new OpcPackagePatchEnvelopeFormatException(
                $"Patch envelope field '{name}' must be a string."
            );
        }
        return text;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new OpcPackagePatchEnvelopeFormatException(
                $"Patch envelope field '{name}' must be a string or null."
            ),
        };
    }

    private static byte[]? OptionalBase64(JsonElement root, string name)
    {
        var value = OptionalString(root, name);
        if (value is null)
        {
            return null;
        }
        try
        {
            var decoded = Convert.FromBase64String(value);
            if (!string.Equals(Convert.ToBase64String(decoded), value, StringComparison.Ordinal))
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    $"Patch envelope field '{name}' is not canonical base64."
                );
            }
            return decoded;
        }
        catch (OpcPackagePatchEnvelopeFormatException)
        {
            throw;
        }
        catch (FormatException exception)
        {
            throw new OpcPackagePatchEnvelopeFormatException(
                $"Patch envelope field '{name}' is not canonical base64.",
                exception
            );
        }
    }

    private static int RequiredInt32(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (!value.TryGetInt32(out var number))
        {
            throw new OpcPackagePatchEnvelopeFormatException(
                $"Patch envelope field '{name}' must be a 32-bit integer."
            );
        }
        return number;
    }

    private static long RequiredInt64(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (!value.TryGetInt64(out var number))
        {
            throw new OpcPackagePatchEnvelopeFormatException(
                $"Patch envelope field '{name}' must be a 64-bit integer."
            );
        }
        return number;
    }

    private static bool ValidKeyId(string? value)
    {
        return value is { Length: >= 1 and <= 128 }
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or ':'
            );
    }

    private static byte[] HashEnvelope(
        ReadOnlySpan<byte> metadata,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> payload
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(metadata);
        hash.AppendData(tag);
        hash.AppendData(payload);
        return hash.GetHashAndReset();
    }

    private static byte[] ReadEntry(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        if (entry.Length > maximumBytes || entry.Length > int.MaxValue)
        {
            throw new OpcPackagePatchEnvelopeLimitException(
                "Patch envelope entry exceeds its configured byte limit."
            );
        }
        var bytes = new byte[(int)entry.Length];
        using var stream = entry.Open();
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                throw new OpcPackagePatchEnvelopeFormatException(
                    "Patch envelope entry ended before its declared length."
                );
            }
            offset += read;
        }
        if (stream.ReadByte() != -1)
        {
            throw new OpcPackagePatchEnvelopeFormatException(
                "Patch envelope entry exceeds its declared length."
            );
        }
        return bytes;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private sealed record ParsedMetadata(
        bool Encrypted,
        bool Signed,
        string? SignerKeyId,
        long SerializedPatchBytes,
        byte[]? Nonce
    );
}

public class OpcPackagePatchEnvelopeException : IOException
{
    public OpcPackagePatchEnvelopeException(string message)
        : base(message)
    {
    }

    public OpcPackagePatchEnvelopeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class OpcPackagePatchEnvelopeFormatException
    : OpcPackagePatchEnvelopeException
{
    public OpcPackagePatchEnvelopeFormatException(string message)
        : base(message)
    {
    }

    public OpcPackagePatchEnvelopeFormatException(
        string message,
        Exception innerException
    )
        : base(message, innerException)
    {
    }
}

public sealed class OpcPackagePatchEnvelopeLimitException
    : OpcPackagePatchEnvelopeException
{
    public OpcPackagePatchEnvelopeLimitException(string message)
        : base(message)
    {
    }
}

public sealed class OpcPackagePatchEnvelopeAuthenticationException
    : OpcPackagePatchEnvelopeException
{
    public OpcPackagePatchEnvelopeAuthenticationException(string message)
        : base(message)
    {
    }

    public OpcPackagePatchEnvelopeAuthenticationException(
        string message,
        Exception innerException
    )
        : base(message, innerException)
    {
    }
}
