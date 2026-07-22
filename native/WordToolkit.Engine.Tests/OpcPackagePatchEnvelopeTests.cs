using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Tests;

public sealed class OpcPackagePatchEnvelopeTests
{
    [Fact]
    public void UnsignedPlaintextEnvelopeRoundTripsAValidatedPatch()
    {
        var patch = CreatePatch();
        var codec = new OpcPackagePatchEnvelopeCodec();
        using var envelope = new MemoryStream();

        var written = codec.Write(envelope, patch);
        envelope.Position = 0;
        var read = codec.Read(envelope);

        Assert.False(written.Encrypted);
        Assert.False(written.Signed);
        Assert.Equal(patch.PatchId, read.Patch.PatchId);
        Assert.Equal(patch.PayloadBytes, read.Patch.PayloadBytes);
        Assert.Equal(written, read.Envelope);
    }

    [Fact]
    public void AesGcmAndEcdsaEnvelopeAuthenticatesBeforeReturningThePatch()
    {
        var patch = CreatePatch();
        var key = RandomNumberGenerator.GetBytes(32);
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = ECDsa.Create(signer.ExportParameters(false));
        var codec = new OpcPackagePatchEnvelopeCodec();
        using var envelope = new MemoryStream();

        var written = codec.Write(
            envelope,
            patch,
            key,
            signer,
            "release-key-2026"
        );
        envelope.Position = 0;
        var read = codec.Read(
            envelope,
            key,
            verifier,
            "release-key-2026"
        );

        Assert.True(written.Encrypted);
        Assert.True(written.Signed);
        Assert.Equal("release-key-2026", read.Envelope.SignerKeyId);
        Assert.Equal(patch.PatchId, read.Patch.PatchId);
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void WrongDecryptionKeyFailsClosed()
    {
        var patch = CreatePatch();
        var key = RandomNumberGenerator.GetBytes(32);
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        var codec = new OpcPackagePatchEnvelopeCodec();
        using var envelope = new MemoryStream();
        codec.Write(envelope, patch, key);
        envelope.Position = 0;

        Assert.Throws<OpcPackagePatchEnvelopeAuthenticationException>(() =>
            codec.Read(envelope, wrongKey)
        );
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(wrongKey);
    }

    [Fact]
    public void SignedEnvelopeRequiresVerifierAndExpectedIdentity()
    {
        var patch = CreatePatch();
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = ECDsa.Create(signer.ExportParameters(false));
        var codec = new OpcPackagePatchEnvelopeCodec();
        using var envelope = new MemoryStream();
        codec.Write(envelope, patch, signer: signer, signerKeyId: "trusted-key");

        envelope.Position = 0;
        Assert.Throws<OpcPackagePatchEnvelopeAuthenticationException>(() =>
            codec.Read(envelope)
        );
        envelope.Position = 0;
        Assert.Throws<OpcPackagePatchEnvelopeAuthenticationException>(() =>
            codec.Read(
                envelope,
                signatureVerifier: verifier,
                expectedSignerKeyId: "other-key"
            )
        );
    }

    [Fact]
    public void PayloadTamperingBreaksTheSignature()
    {
        var patch = CreatePatch();
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = ECDsa.Create(signer.ExportParameters(false));
        var codec = new OpcPackagePatchEnvelopeCodec();
        using var envelope = new MemoryStream();
        codec.Write(envelope, patch, signer: signer, signerKeyId: "trusted-key");
        TamperPayload(envelope);
        envelope.Position = 0;

        Assert.Throws<OpcPackagePatchEnvelopeAuthenticationException>(() =>
            codec.Read(
                envelope,
                signatureVerifier: verifier,
                expectedSignerKeyId: "trusted-key"
            )
        );
    }

    [Fact]
    public void ProtectionInputsRejectWeakOrUnboundKeys()
    {
        var codec = new OpcPackagePatchEnvelopeCodec();
        var patch = CreatePatch();
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<ArgumentException>(() =>
            codec.Write(new MemoryStream(), patch, new byte[16])
        );
        Assert.Throws<ArgumentException>(() =>
            codec.Write(new MemoryStream(), patch, signerKeyId: "orphan")
        );
        Assert.Throws<ArgumentException>(() =>
            codec.Write(new MemoryStream(), patch, signer: signer, signerKeyId: "bad key")
        );
    }

    private static OpcPackagePatch CreatePatch()
    {
        using var beforeStream = BuildPackage("before");
        using var afterStream = BuildPackage("after");
        var reader = new OpcPackageReader();
        var before = reader.Read(beforeStream);
        var after = reader.Read(afterStream);
        return new OpcPackagePatchBuilder().Create(before, after);
    }

    private static MemoryStream BuildPackage(string text)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                    + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                    + "<Default Extension='xml' ContentType='application/xml'/>"
                    + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
                    + "</Types>"
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                    + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
                    + "</Relationships>"
            );
            WriteEntry(
                archive,
                "word/document.xml",
                "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
                    + $"<w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>"
            );
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    private static void TamperPayload(MemoryStream envelope)
    {
        envelope.Position = 0;
        using var archive = new ZipArchive(envelope, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("payload.bin")!;
        byte[] bytes;
        using (var input = entry.Open())
        using (var copy = new MemoryStream())
        {
            input.CopyTo(copy);
            bytes = copy.ToArray();
        }
        bytes[bytes.Length / 2] ^= 0x5a;
        entry.Delete();
        var replacement = archive.CreateEntry("payload.bin", CompressionLevel.NoCompression);
        using var output = replacement.Open();
        output.Write(bytes);
    }
}
