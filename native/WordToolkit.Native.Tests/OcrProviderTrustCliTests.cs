using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class OcrProviderTrustCliTests
{
    [Fact]
    public void IssueAndVerifyCreateTokenFreeHostTrustArtifacts()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-ocr-trust-cli-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "tesseract.exe");
            var models = Path.Combine(directory, "models");
            var trustDirectory = Path.Combine(directory, "trust");
            var privateKey = Path.Combine(trustDirectory, "publisher-private.pem");
            var generatedTrustStore = Path.Combine(trustDirectory, "generated-trust-store.json");
            var manifest = Path.Combine(trustDirectory, "provider-manifest.json");
            var trustStore = Path.Combine(trustDirectory, "trust-store.json");
            Directory.CreateDirectory(models);
            Directory.CreateDirectory(trustDirectory);
            File.WriteAllBytes(executable, [1, 2, 3, 4]);
            File.WriteAllBytes(Path.Combine(models, "eng.traineddata"), [5, 6, 7, 8]);
            var output = new StringWriter();
            var error = new StringWriter();
            var keygenRequest = JsonSerializer.Serialize(new
            {
                publisher_id = "wordtoolkit.project",
                key_id = "release-test",
                private_key_output_path = privateKey,
                trust_store_output_path = generatedTrustStore,
            }, JsonDefaults.Compact);
            var exitCode = OcrProviderTrustCli.Run(
                ["--mode", "keygen", "--request", "-", "--format", "json"],
                new StringReader(keygenRequest),
                output,
                error
            );
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(privateKey));
            Assert.True(File.Exists(generatedTrustStore));
            Assert.DoesNotContain("PRIVATE KEY", output.ToString(), StringComparison.Ordinal);
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            var now = DateTimeOffset.UtcNow;
            var issueRequest = JsonSerializer.Serialize(new
            {
                executable_path = executable,
                model_directory = models,
                languages = new[] { "eng" },
                publisher_id = "wordtoolkit.project",
                key_id = "release-test",
                provider_version = "5.5.0-test",
                private_key_pkcs8_pem_path = privateKey,
                manifest_output_path = manifest,
                trust_store_output_path = trustStore,
                issued_at_utc = now.AddMinutes(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                expires_at_utc = now.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            }, JsonDefaults.Compact);
            exitCode = OcrProviderTrustCli.Run(
                ["--mode", "issue", "--request", "-", "--format", "json"],
                new StringReader(issueRequest),
                output,
                error
            );

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.True(File.Exists(manifest));
            Assert.True(File.Exists(trustStore));
            using var issueJson = JsonDocument.Parse(output.ToString());
            Assert.True(issueJson.RootElement.GetProperty("signature_verified").GetBoolean());
            Assert.True(issueJson.RootElement.GetProperty("resource_hashes_verified").GetBoolean());
            Assert.False(issueJson.RootElement.GetProperty("paths_returned").GetBoolean());
            Assert.False(issueJson.RootElement.GetProperty("private_key_returned").GetBoolean());
            Assert.DoesNotContain(directory, output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE KEY", output.ToString(), StringComparison.Ordinal);

            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            var verifyRequest = JsonSerializer.Serialize(new
            {
                executable_path = executable,
                model_directory = models,
                languages = new[] { "eng" },
                manifest_path = manifest,
                trust_store_path = trustStore,
            }, JsonDefaults.Compact);
            exitCode = OcrProviderTrustCli.Run(
                ["--mode", "verify", "--request", "-", "--format", "json"],
                new StringReader(verifyRequest),
                output,
                error
            );

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            using var verifyJson = JsonDocument.Parse(output.ToString());
            Assert.Equal("verify", verifyJson.RootElement.GetProperty("mode").GetString());
            Assert.Equal(64, verifyJson.RootElement.GetProperty("manifest_sha256").GetString()!.Length);
            Assert.DoesNotContain(directory, output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StrictCliRejectsUnknownArgumentsAndRequestFields()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = OcrProviderTrustCli.Run(
            ["--unknown", "value"],
            new StringReader("{}"),
            output,
            error
        );
        Assert.Equal(64, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("OCR_PROVIDER_TRUST_INVALID", error.ToString(), StringComparison.Ordinal);

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        exitCode = OcrProviderTrustCli.Run(
            ["--mode", "verify", "--request", "-"],
            new StringReader("{\"unexpected\":true}"),
            output,
            error
        );
        Assert.Equal(64, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("unknown or duplicate", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
