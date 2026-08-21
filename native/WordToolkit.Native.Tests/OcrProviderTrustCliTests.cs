using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Ocr;

namespace WordToolkit.Native.Tests;

public sealed class OcrProviderTrustCliTests
{
    [Fact]
    public void ValidCommittedJournalIsCryptoValidatedAndCleared()
    {
        using var f = Fixture();
        var secondary = Encoding.UTF8.GetBytes("owned-secondary");
        var primary = Encoding.UTF8.GetBytes("manifest"); File.WriteAllBytes(f.Manifest, primary); File.WriteAllBytes(f.Store, secondary);
        OcrProviderTrustPairCoordinator.WriteJournal(f.Manifest, f.Store, secondary, "crashed", "pair", primary);
        OcrProviderTrustPairCoordinator.Recover(f.Manifest, f.Store, (_, _) => true);
        Assert.True(File.Exists(f.Manifest)); Assert.True(File.Exists(f.Store));
        Assert.False(File.Exists(OcrProviderTrustPairCoordinator.JournalPath(f.Manifest, f.Store)));
    }

    [Fact]
    public void MismatchedSecondaryJournalFailsClosedAndPreservesExternalBytes()
    {
        using var f = Fixture();
        var owned = Encoding.UTF8.GetBytes("owned"); var primary = Encoding.UTF8.GetBytes("manifest"); File.WriteAllBytes(f.Manifest, primary); File.WriteAllBytes(f.Store, owned);
        OcrProviderTrustPairCoordinator.WriteJournal(f.Manifest, f.Store, owned, "crashed", "pair", primary);
        var external = Encoding.UTF8.GetBytes("external"); File.WriteAllBytes(f.Store, external);
        var journal = OcrProviderTrustPairCoordinator.JournalPath(f.Manifest, f.Store);
        var error = Assert.Throws<IOException>(() => OcrProviderTrustPairCoordinator.Recover(f.Manifest, f.Store));
        Assert.Contains("hashes", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(external, File.ReadAllBytes(f.Store)); Assert.True(File.Exists(journal));
    }

    [Fact]
    public void MaliciousPathJournalFailsClosedAndRemains()
    {
        using var f = Fixture();
        var journal = OcrProviderTrustPairCoordinator.JournalPath(f.Manifest, f.Store);
        File.WriteAllText(journal, JsonSerializer.Serialize(new { primary_path = f.Manifest, secondary_path = Path.Combine(f.Root, "other.json"), secondary_sha256 = "00", transaction_id = "evil" }));
        var error = Assert.Throws<IOException>(() => OcrProviderTrustPairCoordinator.Recover(f.Manifest, f.Store));
        Assert.Contains("does not match the pair", error.Message, StringComparison.OrdinalIgnoreCase); Assert.True(File.Exists(journal));
    }

    [Fact]
    public void CommittedPairWithLeftoverJournalIsReadableAndJournalIsRemoved()
    {
        using var f = Fixture();
        var output = new StringWriter(); var error = new StringWriter();
        Assert.Equal(0, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), output, error));
        var bytes = File.ReadAllBytes(f.Store); OcrProviderTrustPairCoordinator.WriteJournal(f.Manifest, f.Store, bytes, "leftover");
        using var snapshot = new OcrProviderTrustPolicy(f.Manifest, f.Store).Authorize(f.Executable, f.Models, ["eng"], CancellationToken.None);
        Assert.Equal("wordtoolkit.project", snapshot.Binding.PublisherId); Assert.False(File.Exists(OcrProviderTrustPairCoordinator.JournalPath(f.Manifest, f.Store)));
    }

    [Fact]
    public void PolicyAuthorizeRejectsNetworkDriveThroughInjectedResolver()
    {
        using var f = Fixture();
            var error = Assert.ThrowsAny<Exception>(() => new OcrProviderTrustPolicy(f.Manifest, f.Store, driveTypeResolver: _ => DriveType.Network)
                .Authorize(f.Executable, f.Models, ["eng"], CancellationToken.None));
            Assert.Contains("not local", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrimaryOnlyJournalFailsClosedAndPreservesPrimaryAndJournal()
    {
        using var f = Fixture();
        var secondary = Encoding.UTF8.GetBytes("owned-secondary");
        File.WriteAllBytes(f.Manifest, [9, 8, 7]);
        OcrProviderTrustPairCoordinator.WriteJournal(f.Manifest, f.Store, secondary, "primary-only", "pair", [9, 8, 7]);
        var journal = OcrProviderTrustPairCoordinator.JournalPath(f.Manifest, f.Store);
        var error = Assert.Throws<IOException>(() => OcrProviderTrustPairCoordinator.Recover(f.Manifest, f.Store));
        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(f.Manifest)); Assert.True(File.Exists(journal));
    }

    [Fact]
    public void NetworkDriveResolverRejectsInputsAndOutputsWithExistingMessages()
    {
        using var f = Fixture();
            var output = new StringWriter(); var error = new StringWriter();
            Assert.Equal(64, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), output, error,
                new OcrProviderTrustCli.RunOptions(null, _ => DriveType.Network)));
            Assert.Contains("not local", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JournalIsDurableBeforeSecondaryPublicationAndCrashRecovers()
    {
        using var f = Fixture();
        var journal = OcrProviderTrustPairCoordinator.JournalPath(f.Manifest, f.Store);
        var hook = new OcrProviderTrustPairHooks(
            BeforeSecondaryPublish: () =>
            {
                Assert.True(File.Exists(journal));
                Assert.False(File.Exists(f.Store));
                throw new IOException("injected crash before secondary publication");
            });
        var o = new StringWriter(); var e = new StringWriter();
        Assert.Equal(2, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), o, e, hook));
        Assert.False(File.Exists(f.Manifest)); Assert.False(File.Exists(f.Store)); Assert.False(File.Exists(journal));
        o.GetStringBuilder().Clear(); e.GetStringBuilder().Clear();
        Assert.Equal(0, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), o, e));
        Assert.True(File.Exists(f.Manifest)); Assert.True(File.Exists(f.Store));
    }

    [Fact]
    public void SymlinkedOutputDirectoryAndInputAreRejectedWithoutTouchingTarget()
    {
        using var f = Fixture();
        var external = Path.Combine(f.Root, "external"); Directory.CreateDirectory(external);
        var link = Path.Combine(f.Root, "linked");
        try { Directory.CreateSymbolicLink(link, external); }
        catch (PlatformNotSupportedException) { return; }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException ex) when (ex.Message.Contains("uprawnie", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase)) { return; }
        var target = Path.Combine(external, "manifest.json");
        var o = new StringWriter(); var e = new StringWriter();
        var req = IssueRequest(f, Path.Combine(link, "manifest.json"), Path.Combine(link, "store.json"));
        var outputExitCode = OcrProviderTrustCli.Run(
            ["--mode", "issue", "--request", "-"],
            new StringReader(req),
            o,
            e
        );
        Assert.True(
            outputExitCode == 64,
            $"Expected unsafe output path to return 64, got {outputExitCode}. stderr: {e}"
        );
        Assert.Equal("OCR_PROVIDER_TRUST_INVALID: OCR provider trust paths cannot contain reparse points.\r\n", e.ToString());
        Assert.False(File.Exists(target)); Assert.Empty(Directory.GetFiles(external));
        var inputLink = Path.Combine(f.Root, "input-link");
        try { File.CreateSymbolicLink(inputLink, f.Executable); }
        catch (PlatformNotSupportedException) { return; }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException ex) when (ex.Message.Contains("uprawnie", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase)) { return; }
        o.GetStringBuilder().Clear(); e.GetStringBuilder().Clear();
        var bad = IssueRequest(f, f.Manifest, f.Store).Replace(f.Executable, inputLink, StringComparison.Ordinal);
        var inputExitCode = OcrProviderTrustCli.Run(
            ["--mode", "issue", "--request", "-"],
            new StringReader(bad),
            o,
            e
        );
        Assert.True(
            inputExitCode is 2 or 64,
            $"Expected unsafe input path to fail closed with path or identity rejection, got {inputExitCode}. stderr: {e}"
        );
        Assert.True(
            e.ToString().Contains("OCR_PROVIDER_TRUST_INVALID", StringComparison.Ordinal)
            || e.ToString().Contains("OCR_PROVIDER_IDENTITY_MISMATCH", StringComparison.Ordinal),
            $"Unexpected unsafe-input failure: {e}"
        );
        Assert.False(File.Exists(f.Manifest)); Assert.False(File.Exists(f.Store));
    }

    [Fact]
    public void ReaderRejectsSymlinkedTrustParentBeforeCreatingLockOrJournal()
    {
        using var f = Fixture();
        var external = Path.Combine(f.Root, "reader-external"); Directory.CreateDirectory(external);
        var link = Path.Combine(f.Root, "reader-linked");
        try { Directory.CreateSymbolicLink(link, external); }
        catch (PlatformNotSupportedException) { return; }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException ex) when (ex.Message.Contains("uprawnie", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase)) { return; }
        var before = Directory.GetFileSystemEntries(external).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var manifest = Path.Combine(link, "manifest.json"); var store = Path.Combine(link, "store.json");
        var error = Assert.ThrowsAny<Exception>(() => new OcrProviderTrustPolicy(manifest, store).Authorize(f.Executable, f.Models, ["eng"], CancellationToken.None));
        Assert.Contains("reparse", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Directory.GetFileSystemEntries(external).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }
    [Fact]
    public void SecondaryPublishFailureLeavesStoreOnlyAndAuthorizationFailsClosed()
    {
        using var f = Fixture();
        var output = new StringWriter(); var error = new StringWriter();
        var request = IssueRequest(f, f.Manifest, f.Store);
        var code = OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-", "--format", "json"], new StringReader(request), output, error,
            new OcrProviderTrustPairHooks(() => throw new IOException("injected")));
        Assert.Equal(2, code); Assert.False(File.Exists(f.Manifest)); Assert.True(File.Exists(f.Store));
        var storeBytes = File.ReadAllBytes(f.Store);
        Assert.Single(Directory.GetFiles(f.Trust, "*.journal.json"));
        Assert.Empty(Directory.GetFiles(f.Trust, "*.tmp"));
        output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
        var verify = OcrProviderTrustCli.Run(["--mode", "verify", "--request", "-", "--format", "json"], new StringReader(VerifyRequest(f)), output, error);
        Assert.Equal(2, verify);
        Assert.Equal(storeBytes, File.ReadAllBytes(f.Store)); Assert.Single(Directory.GetFiles(f.Trust, "*.journal.json"));
        output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
        Assert.NotEqual(0, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), output, error));
        Assert.Equal(storeBytes, File.ReadAllBytes(f.Store)); Assert.Single(Directory.GetFiles(f.Trust, "*.journal.json"));
    }

    [Fact]
    public void JournalWriteFailureLeavesNeitherOutput()
    {
        using var f = Fixture();
        var o = new StringWriter(); var e = new StringWriter();
        var hook = new OcrProviderTrustPairHooks(BeforeJournalWrite: () => throw new IOException("journal unavailable"));
        Assert.Equal(2, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), o, e, hook));
        Assert.False(File.Exists(f.Manifest)); Assert.False(File.Exists(f.Store));
        Assert.Empty(Directory.GetFiles(f.Trust, "*.journal.json")); Assert.Empty(Directory.GetFiles(f.Trust, "*.tmp"));
    }

    [Fact]
    public void ExistingPairIsPreservedAndIssueReportsExists()
    {
        using var f = Fixture();
        var output = new StringWriter(); var error = new StringWriter();
        Assert.Equal(0, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), output, error));
        var manifest = File.ReadAllBytes(f.Manifest); var store = File.ReadAllBytes(f.Store);
        output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
        Assert.Equal(2, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), output, error));
        Assert.Contains("exists", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(manifest, File.ReadAllBytes(f.Manifest)); Assert.Equal(store, File.ReadAllBytes(f.Store));
    }

    [Fact]
    public async Task ConcurrentIssueSamePairPublishesOneValidPair()
    {
        using var f = Fixture(); var gate = new Barrier(2);
        var requests = Enumerable.Range(0, 2).Select(_ => Task.Run(() => { gate.SignalAndWait(); var o = new StringWriter(); var e = new StringWriter(); return OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), o, e); })).ToArray();
        var results = (await Task.WhenAll(requests)).OrderBy(x => x).ToArray(); Assert.Equal([0, 2], results);
        var o2 = new StringWriter(); var e2 = new StringWriter(); Assert.Equal(0, OcrProviderTrustCli.Run(["--mode", "verify", "--request", "-"], new StringReader(VerifyRequest(f)), o2, e2));
    }

    [Fact]
    public async Task ReaderConcurrentWithWriterNeverObservesMixedPair()
    {
        using var f = Fixture();
        Task<OcrProviderTrustSnapshot>? reader = null;
        var o2 = new StringWriter(); var e2 = new StringWriter();
        var hooks = new OcrProviderTrustPairHooks(() =>
        {
            var policy = new OcrProviderTrustPolicy(f.Manifest, f.Store);
            reader = Task.Run(() => policy.Authorize(f.Executable, f.Models, ["eng"], CancellationToken.None));
            Assert.False(reader.Wait(TimeSpan.FromMilliseconds(100)), "reader bypassed the writer pair lock");
        });
        Assert.Equal(0, OcrProviderTrustCli.Run(["--mode", "issue", "--request", "-"], new StringReader(IssueRequest(f, f.Manifest, f.Store)), o2, e2, hooks));
        using var snapshot = await reader!;
        Assert.Equal("wordtoolkit.project", snapshot.Binding.PublisherId);
        Assert.Equal("release-test", snapshot.Binding.PublisherKeyId);
    }

    private sealed record FixtureData(string Root, string Trust, string Executable, string Models, string PrivateKey, string Manifest, string Store) : IDisposable { public void Dispose() { try { Directory.Delete(Root, true); } catch { } } }
    private static FixtureData Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "wt-ocr-" + Guid.NewGuid().ToString("N")); var trust = Path.Combine(root, "trust"); var models = Path.Combine(root, "models"); Directory.CreateDirectory(trust); Directory.CreateDirectory(models); var exe = Path.Combine(root, "tesseract.exe"); File.WriteAllBytes(exe, [1,2,3]); File.WriteAllBytes(Path.Combine(models, "eng.traineddata"), [4,5,6]); var pk = Path.Combine(trust, "key.pem"); var gen = Path.Combine(trust, "gen.json"); var o = new StringWriter(); var e = new StringWriter(); var req = JsonSerializer.Serialize(new { publisher_id="wordtoolkit.project", key_id="release-test", private_key_output_path=pk, trust_store_output_path=gen }, JsonDefaults.Compact); Assert.Equal(0, OcrProviderTrustCli.Run(["--mode","keygen","--request","-"], new StringReader(req), o,e)); return new(root, trust, exe, models, pk, Path.Combine(trust,"manifest.json"), Path.Combine(trust,"store.json"));
    }
    private static string IssueRequest(FixtureData f,string m,string s)=>JsonSerializer.Serialize(new { executable_path=f.Executable, model_directory=f.Models, languages=new[]{"eng"}, publisher_id="wordtoolkit.project", key_id="release-test", provider_version="5.5.0", private_key_pkcs8_pem_path=f.PrivateKey, manifest_output_path=m, trust_store_output_path=s, issued_at_utc=DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), expires_at_utc=DateTimeOffset.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }, JsonDefaults.Compact);
    private static string VerifyRequest(FixtureData f)=>JsonSerializer.Serialize(new { executable_path=f.Executable, model_directory=f.Models, languages=new[]{"eng"}, manifest_path=f.Manifest, trust_store_path=f.Store }, JsonDefaults.Compact);
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
