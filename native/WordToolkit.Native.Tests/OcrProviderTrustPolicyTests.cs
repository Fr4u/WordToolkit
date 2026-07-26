using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Extensions;
using WordToolkit.Native.Ocr;

namespace WordToolkit.Native.Tests;

public sealed class OcrProviderTrustPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SignedManifestAuthorizesExactProviderAndModelsWithoutRequestTrustMaterial()
    {
        using var fixture = new TrustFixture(Now);

        using var snapshot = fixture.Policy.Authorize(
            fixture.ExecutablePath,
            fixture.ModelDirectory,
            ["eng"],
            CancellationToken.None
        );

        Assert.Equal(OcrProviderTrustPolicy.BindingContract, snapshot.Binding.Contract);
        Assert.Equal(TesseractCliOcrProvider.ExtensionId, snapshot.Binding.ProviderId);
        Assert.Equal("wordtoolkit.project", snapshot.Binding.PublisherId);
        Assert.Equal("release-2026", snapshot.Binding.PublisherKeyId);
        Assert.Equal(fixture.ExecutableSha256, snapshot.Binding.ExecutableSha256);
        var model = Assert.Single(snapshot.Binding.Models);
        Assert.Equal("eng", model.Language);
        Assert.Equal(fixture.ModelSha256, model.Sha256);
        Assert.Equal(64, snapshot.Binding.ManifestSha256.Length);
        Assert.Equal(64, snapshot.Binding.TrustStoreSha256.Length);

        fixture.Policy.Revalidate(
            snapshot,
            fixture.ExecutablePath,
            fixture.ModelDirectory,
            ["eng"],
            CancellationToken.None
        );
    }

    [Fact]
    public void ProviderOrModelTamperingFailsBeforeProcessLaunchAndLeaksNoPath()
    {
        using var fixture = new TrustFixture(Now);
        OcrProviderTrustBinding binding;
        using (var snapshot = fixture.Authorize())
        {
            binding = snapshot.Binding;
        }
        File.AppendAllText(fixture.ExecutablePath, "tampered", Encoding.UTF8);

        var exception = Assert.Throws<WordToolkitExtensionException>(() =>
            new TesseractCliOcrProvider(binding).Recognize(Request(fixture))
        );

        Assert.Equal("OCR_PROVIDER_IDENTITY_MISMATCH", exception.Code);
        Assert.DoesNotContain(fixture.DirectoryPath, exception.ToString(), StringComparison.Ordinal);

        fixture.RestoreExecutable();
        File.AppendAllText(fixture.ModelPath, "tampered", Encoding.UTF8);
        exception = Assert.Throws<WordToolkitExtensionException>(() =>
            new TesseractCliOcrProvider(binding).Recognize(Request(fixture))
        );
        Assert.Equal("OCR_PROVIDER_IDENTITY_MISMATCH", exception.Code);
    }

    private static WordOcrProviderRequest Request(TrustFixture fixture)
    {
        byte[] image = [1, 2, 3, 4];
        return new WordOcrProviderRequest(
            image,
            "image/png",
            Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(),
            ["eng"],
            WordOcrLayoutHint.Automatic,
            2000,
            1024,
            new WordOcrProviderConfiguration(
                fixture.ExecutablePath,
                fixture.ModelDirectory
            )
        );
    }

    [Fact]
    public void ManifestTamperingUnknownFieldsAndDuplicateFieldsFailClosed()
    {
        using var fixture = new TrustFixture(Now);
        var original = File.ReadAllText(fixture.ManifestPath, Encoding.UTF8);
        File.WriteAllText(
            fixture.ManifestPath,
            original.Replace("\"provider_version\": \"5.5.0\"", "\"provider_version\": \"5.5.1\"", StringComparison.Ordinal),
            new UTF8Encoding(false)
        );
        Assert.Equal(
            "OCR_PROVIDER_SIGNATURE_INVALID",
            Assert.Throws<WordToolkitExtensionException>(() => fixture.Authorize()).Code
        );

        File.WriteAllText(
            fixture.ManifestPath,
            original.TrimEnd()[..^1] + ",\"unexpected\":true}",
            new UTF8Encoding(false)
        );
        Assert.Equal(
            "OCR_PROVIDER_TRUST_INVALID",
            Assert.Throws<WordToolkitExtensionException>(() => fixture.Authorize()).Code
        );

        File.WriteAllText(
            fixture.ManifestPath,
            original.Replace(
                "\"schema\": \"wordtoolkit.ocr-provider-manifest/1.0\"",
                "\"schema\": \"wordtoolkit.ocr-provider-manifest/1.0\",\"schema\":\"wordtoolkit.ocr-provider-manifest/1.0\"",
                StringComparison.Ordinal
            ),
            new UTF8Encoding(false)
        );
        Assert.Equal(
            "OCR_PROVIDER_TRUST_INVALID",
            Assert.Throws<WordToolkitExtensionException>(() => fixture.Authorize()).Code
        );
    }

    [Fact]
    public void UntrustedPublisherAndExpiredManifestFailClosed()
    {
        using var fixture = new TrustFixture(Now);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        fixture.WriteTrustStore(other);

        Assert.Equal(
            "OCR_PROVIDER_SIGNATURE_INVALID",
            Assert.Throws<WordToolkitExtensionException>(() => fixture.Authorize()).Code
        );

        using var expired = new TrustFixture(Now, issuedAt: Now.AddDays(-3), expiresAt: Now.AddDays(-2));
        Assert.Equal(
            "OCR_PROVIDER_MANIFEST_EXPIRED",
            Assert.Throws<WordToolkitExtensionException>(() => expired.Authorize()).Code
        );
    }

    [Fact]
    public void UnlistedLanguageAndTrustDriftAreRejected()
    {
        using var fixture = new TrustFixture(Now);
        Assert.Equal(
            "OCR_PROVIDER_MODEL_NOT_TRUSTED",
            Assert.Throws<WordToolkitExtensionException>(() =>
                fixture.Policy.Authorize(
                    fixture.ExecutablePath,
                    fixture.ModelDirectory,
                    ["pol"],
                    CancellationToken.None
                )
            ).Code
        );

        using var snapshot = fixture.Authorize();
        using var replacementKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        fixture.WriteTrustStore(replacementKey);
        Assert.Equal(
            "OCR_PROVIDER_SIGNATURE_INVALID",
            Assert.Throws<WordToolkitExtensionException>(() =>
                fixture.Policy.Revalidate(
                    snapshot,
                    fixture.ExecutablePath,
                    fixture.ModelDirectory,
                    ["eng"],
                    CancellationToken.None
                )
            ).Code
        );
    }

    [Fact]
    public void BoundProviderRejectsAHashMismatchBeforeExecutingTheConfiguredBinary()
    {
        using var fixture = new TrustFixture(Now);
        using var snapshot = fixture.Authorize();
        var binding = snapshot.Binding with { ExecutableSha256 = new string('f', 64) };
        byte[] image = [1, 2, 3, 4];
        var request = new WordOcrProviderRequest(
            image,
            "image/png",
            Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(),
            ["eng"],
            WordOcrLayoutHint.Automatic,
            2000,
            1024,
            new WordOcrProviderConfiguration(fixture.ExecutablePath, fixture.ModelDirectory)
        );

        var exception = Assert.Throws<WordToolkitExtensionException>(() =>
            new TesseractCliOcrProvider(binding).Recognize(request)
        );

        Assert.Equal("EXTENSION_PROTOCOL_VIOLATION", exception.Code);
    }

    [Fact]
    public void ResourceLeaseBlocksMutationAndRejectsAnUnlistedRuntimeFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var fixture = new TrustFixture(Now);
        OcrProviderTrustBinding binding;
        using (var snapshot = fixture.Authorize())
        {
            binding = snapshot.Binding;
        }

        var unsignedRuntimePath = Path.Combine(fixture.DirectoryPath, "unsigned.dll");
        using (var lease = OcrProviderResourceLease.Acquire(
            fixture.ExecutablePath,
            fixture.ModelDirectory,
            binding
        ))
        {
            Assert.Throws<IOException>(() => new FileStream(
                fixture.ExecutablePath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite
            ));
            Assert.Throws<IOException>(() => new FileStream(
                fixture.ModelPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite
            ));
            File.WriteAllBytes(unsignedRuntimePath, [9, 9, 9]);
            var exception = Assert.Throws<WordToolkitExtensionException>(
                lease.VerifyDirectorySet
            );
            Assert.Equal("OCR_PROVIDER_IDENTITY_MISMATCH", exception.Code);
            Assert.DoesNotContain(
                fixture.DirectoryPath,
                exception.ToString(),
                StringComparison.Ordinal
            );
            File.Delete(unsignedRuntimePath);
        }

        using (var writable = new FileStream(
            fixture.ExecutablePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite
        ))
        {
            Assert.True(writable.CanWrite);
        }
        File.WriteAllBytes(unsignedRuntimePath, [9, 9, 9]);
        Assert.Equal(
            "OCR_PROVIDER_IDENTITY_MISMATCH",
            Assert.Throws<WordToolkitExtensionException>(() =>
                OcrProviderResourceLease.Acquire(
                    fixture.ExecutablePath,
                    fixture.ModelDirectory,
                    binding
                )
            ).Code
        );
    }

    [Fact]
    public void MissingHostTrustConfigurationFailsWithoutInspectingAProvider()
    {
        var manifest = Environment.GetEnvironmentVariable(
            OcrProviderTrustPolicy.ManifestEnvironmentVariable
        );
        var store = Environment.GetEnvironmentVariable(
            OcrProviderTrustPolicy.TrustStoreEnvironmentVariable
        );
        try
        {
            Environment.SetEnvironmentVariable(
                OcrProviderTrustPolicy.ManifestEnvironmentVariable,
                null
            );
            Environment.SetEnvironmentVariable(
                OcrProviderTrustPolicy.TrustStoreEnvironmentVariable,
                null
            );

            var exception = Assert.Throws<WordToolkitExtensionException>(
                OcrProviderTrustPolicy.FromEnvironment
            );

            Assert.Equal("OCR_PROVIDER_TRUST_NOT_CONFIGURED", exception.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                OcrProviderTrustPolicy.ManifestEnvironmentVariable,
                manifest
            );
            Environment.SetEnvironmentVariable(
                OcrProviderTrustPolicy.TrustStoreEnvironmentVariable,
                store
            );
        }
    }

    private sealed class TrustFixture : IDisposable
    {
        private readonly byte[] _executableBytes = [1, 3, 3, 7, 9];
        private readonly ECDsa _signer;
        private readonly DateTimeOffset _issuedAt;
        private readonly DateTimeOffset _expiresAt;

        internal TrustFixture(
            DateTimeOffset now,
            DateTimeOffset? issuedAt = null,
            DateTimeOffset? expiresAt = null
        )
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "wordtoolkit-ocr-trust-test-" + Guid.NewGuid().ToString("N")
            );
            ModelDirectory = Path.Combine(DirectoryPath, "models");
            var trustDirectory = Path.Combine(DirectoryPath, "trust");
            Directory.CreateDirectory(ModelDirectory);
            Directory.CreateDirectory(trustDirectory);
            ExecutablePath = Path.Combine(DirectoryPath, "tesseract.exe");
            ModelPath = Path.Combine(ModelDirectory, "eng.traineddata");
            ManifestPath = Path.Combine(trustDirectory, "provider.json");
            TrustStorePath = Path.Combine(trustDirectory, "trust-store.json");
            File.WriteAllBytes(ExecutablePath, _executableBytes);
            File.WriteAllBytes(ModelPath, [2, 4, 6, 8, 10]);
            ExecutableSha256 = HashFile(ExecutablePath);
            ModelSha256 = HashFile(ModelPath);
            _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _issuedAt = issuedAt ?? now.AddMinutes(-1);
            _expiresAt = expiresAt ?? now.AddDays(30);
            WriteManifest();
            WriteTrustStore(_signer);
            Policy = new OcrProviderTrustPolicy(
                ManifestPath,
                TrustStorePath,
                () => now
            );
        }

        internal string DirectoryPath { get; }
        internal string ExecutablePath { get; }
        internal string ModelDirectory { get; }
        internal string ModelPath { get; }
        internal string ManifestPath { get; }
        internal string TrustStorePath { get; }
        internal string ExecutableSha256 { get; }
        internal string ModelSha256 { get; }
        internal OcrProviderTrustPolicy Policy { get; }

        internal OcrProviderTrustSnapshot Authorize() => Policy.Authorize(
            ExecutablePath,
            ModelDirectory,
            ["eng"],
            CancellationToken.None
        );

        internal void RestoreExecutable() => File.WriteAllBytes(ExecutablePath, _executableBytes);

        internal void WriteTrustStore(ECDsa key)
        {
            var store = new OcrProviderTrustStore(
                OcrProviderTrustPolicy.TrustStoreContract,
                [
                    new OcrProviderTrustedKey(
                        "wordtoolkit.project",
                        "release-2026",
                        OcrProviderTrustPolicy.SignatureAlgorithm,
                        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
                    ),
                ]
            );
            File.WriteAllBytes(
                TrustStorePath,
                OcrProviderTrustPolicy.SerializeTrustStore(store)
            );
        }

        public void Dispose()
        {
            _signer.Dispose();
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }

        private void WriteManifest()
        {
            var manifest = OcrProviderTrustPolicy.CreateSignedManifest(
                "wordtoolkit.project",
                "release-2026",
                "5.5.0",
                Path.GetFileName(ExecutablePath),
                ExecutableSha256,
                OcrProviderTrustPolicy.HashRuntimeFiles(
                    ExecutablePath,
                    CancellationToken.None
                ).Select(item => new OcrProviderManifestRuntimeFile(
                    item.FileName,
                    item.Sha256
                )).ToArray(),
                [new OcrProviderManifestModel("eng", "eng.traineddata", ModelSha256)],
                _issuedAt,
                _expiresAt,
                _signer
            );
            File.WriteAllBytes(
                ManifestPath,
                OcrProviderTrustPolicy.SerializeManifest(manifest)
            );
        }

        private static string HashFile(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}
