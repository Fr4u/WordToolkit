using System.Security.Cryptography;
using WordToolkit.Engine.Operations;
using Xunit;

namespace WordToolkit.LibreOffice.Tests;

public sealed class LibreOfficeBackendProbeProviderTests
{
    [Fact]
    public async Task RunsOnlyFixedVersionArgumentAndBindsPrePostHash()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = ExecutablePath(directory);
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6 };
            File.WriteAllBytes(path, bytes);
            var runner = new RecordingRunner(
                new LibreOfficeProcessResult(
                    0,
                    "LibreOffice 24.2.7.2 420(Build:2)\n",
                    string.Empty
                )
            );
            var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            var observation = await new LibreOfficeBackendProbeProvider(runner).ProbeAsync(
                Request(path, expected)
            );

            Assert.Equal("LibreOffice", observation.Product);
            Assert.Equal("24.2.7.2", observation.Version);
            Assert.Equal(expected, observation.ExecutableSha256);
            Assert.True(observation.ExecutableHashStable);
            var process = Assert.Single(runner.Requests);
            Assert.Equal(path, process.ExecutablePath);
            Assert.Equal(["--version"], process.Arguments);
            Assert.Equal(TimeSpan.FromSeconds(5), process.Timeout);
            Assert.Equal(8_192, process.MaximumOutputCharacters);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExpectedHashMismatchPreventsProcessStart()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = ExecutablePath(directory);
            File.WriteAllBytes(path, [1, 2, 3]);
            var runner = new RecordingRunner(
                new LibreOfficeProcessResult(0, "LibreOffice 24.2.7.2", string.Empty)
            );

            var exception = await Assert.ThrowsAsync<WordToolkitOperationException>(() =>
                new LibreOfficeBackendProbeProvider(runner).ProbeAsync(
                    Request(path, new string('f', 64))
                )
            );

            Assert.Equal("EXECUTABLE_MISMATCH", exception.Code);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "not libreoffice", false, false, "INVALID_BACKEND")]
    [InlineData(1, "LibreOffice 24.2.7.2", false, false, "BACKEND_UNAVAILABLE")]
    [InlineData(0, "LibreOffice 24.2.7.2", true, false, "OUTPUT_LIMIT")]
    [InlineData(0, "LibreOffice 24.2.7.2", false, true, "BACKEND_TIMEOUT")]
    public async Task FailsClosedForUnqualifiedProcessEvidence(
        int exitCode,
        string stdout,
        bool truncated,
        bool timedOut,
        string expectedCode
    )
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = ExecutablePath(directory);
            File.WriteAllBytes(path, [1, 2, 3]);
            var runner = new RecordingRunner(
                new LibreOfficeProcessResult(
                    exitCode,
                    stdout,
                    string.Empty,
                    StandardOutputTruncated: truncated,
                    TimedOut: timedOut
                )
            );

            var exception = await Assert.ThrowsAsync<WordToolkitOperationException>(() =>
                new LibreOfficeBackendProbeProvider(runner).ProbeAsync(Request(path))
            );

            Assert.Equal(expectedCode, exception.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsSymbolicLinkBeforeStartingProcess()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var target = ExecutablePath(directory, "target");
            var link = ExecutablePath(directory, "link");
            File.WriteAllBytes(target, [1, 2, 3]);
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception linkException) when (
                linkException is UnauthorizedAccessException
                    or IOException
                    or PlatformNotSupportedException
            )
            {
                return;
            }
            var runner = new RecordingRunner(
                new LibreOfficeProcessResult(0, "LibreOffice 24.2.7.2", string.Empty)
            );

            var exception = await Assert.ThrowsAsync<WordToolkitOperationException>(() =>
                new LibreOfficeBackendProbeProvider(runner).ProbeAsync(Request(link))
            );

            Assert.Equal("INVALID_INPUT", exception.Code);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealLibreOfficeProbeRunsOnlyWhenExplicitlyConfigured()
    {
        var path = Environment.GetEnvironmentVariable("WORDTOOLKIT_TEST_LIBREOFFICE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var result = await new InspectLibreOfficeBackendOperation(
            new LibreOfficeBackendProbeProvider()
        ).ExecuteAsync(new InspectLibreOfficeBackendRequest(path));

        Assert.True(result.Available);
        Assert.StartsWith("LibreOffice", result.Identity.Product, StringComparison.Ordinal);
        Assert.True(result.Capabilities.VersionProbeVerified);
        Assert.False(result.Capabilities.RenderingVerified);
        Assert.False(result.Security.NetworkIsolationEnforced);
    }

    private static LibreOfficeBackendProbeProviderRequest Request(
        string path,
        string? expectedHash = null
    ) => new(
        path,
        expectedHash,
        5_000,
        LibreOfficeBackendProbeContract.MaximumExecutableBytes,
        LibreOfficeBackendProbeContract.MaximumProcessOutputCharacters
    );

    private static string ExecutablePath(string directory, string stem = "soffice") =>
        Path.Combine(directory, OperatingSystem.IsWindows() ? stem + ".exe" : stem);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-libreoffice-provider-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingRunner(LibreOfficeProcessResult result)
        : ILibreOfficeProcessRunner
    {
        public List<LibreOfficeProcessRequest> Requests { get; } = [];

        public Task<LibreOfficeProcessResult> RunAsync(
            LibreOfficeProcessRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }
}
