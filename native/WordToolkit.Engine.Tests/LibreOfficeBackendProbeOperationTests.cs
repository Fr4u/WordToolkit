using WordToolkit.Engine.Operations;

namespace WordToolkit.Engine.Tests;

public sealed class LibreOfficeBackendProbeOperationTests
{
    [Fact]
    public async Task ReturnsOnlyVersionProbeEvidenceAndClosedLimitations()
    {
        var provider = new RecordingProvider(SuccessObservation());
        var expected = new string('A', 64);
        var result = await new InspectLibreOfficeBackendOperation(provider).ExecuteAsync(
            new InspectLibreOfficeBackendRequest(
                Path.Combine(Path.GetTempPath(), "soffice-test"),
                expected,
                2_500
            )
        );

        Assert.True(result.Available);
        Assert.Equal(LibreOfficeBackendProbeContract.Contract, result.OperationContract);
        Assert.True(result.Capabilities.VersionProbeVerified);
        Assert.False(result.Capabilities.UnoConnectionVerified);
        Assert.False(result.Capabilities.RenderingVerified);
        Assert.False(result.Capabilities.WordFidelityClaimed);
        Assert.False(result.Security.ReadsDocument);
        Assert.False(result.Security.PathSearchUsed);
        Assert.False(result.Security.NetworkIsolationEnforced);
        Assert.True(result.Security.StdinClosed);
        Assert.True(result.Security.ArgumentsFixed);
        Assert.Contains("not_a_process_sandbox", result.Limitations);
        Assert.Contains("no_macro_execution_policy_proof", result.Limitations);
        Assert.Contains("no_vendor_signature_or_authenticity_proof", result.Limitations);
        Assert.Contains("no_atomic_executable_handle_binding", result.Limitations);
        Assert.Equal(expected.ToLowerInvariant(), provider.LastRequest!.ExpectedExecutableSha256);
        Assert.Equal(2_500, provider.LastRequest.TimeoutMilliseconds);
        Assert.Equal(
            LibreOfficeBackendProbeContract.MaximumExecutableBytes,
            provider.LastRequest.MaximumExecutableBytes
        );
    }

    [Theory]
    [InlineData("relative-soffice", null, 10000)]
    [InlineData("", null, 10000)]
    [InlineData("C:\\soffice.exe", "xyz", 10000)]
    [InlineData("C:\\soffice.exe", null, 999)]
    [InlineData("C:\\soffice.exe", null, 30001)]
    public async Task RejectsInvalidRequestsBeforeInvokingProvider(
        string path,
        string? hash,
        int timeout
    )
    {
        if (!OperatingSystem.IsWindows() && path.StartsWith("C:\\", StringComparison.Ordinal))
        {
            path = Path.Combine(Path.GetTempPath(), "soffice");
        }
        var provider = new RecordingProvider(SuccessObservation());
        var exception = await Assert.ThrowsAsync<WordToolkitOperationException>(() =>
            new InspectLibreOfficeBackendOperation(provider).ExecuteAsync(
                new InspectLibreOfficeBackendRequest(path, hash, timeout)
            )
        );

        Assert.Equal("INVALID_INPUT", exception.Code);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public void StrictJsonRejectsUnknownDuplicateAndWrongTypes()
    {
        var unknown = Assert.Throws<WordToolkitOperationException>(() =>
            LibreOfficeBackendProbeOperationJson.ParseRequest(
                """{"executable_path":"C:\\soffice.exe","search_path":true}"""
            )
        );
        var duplicate = Assert.Throws<WordToolkitOperationException>(() =>
            LibreOfficeBackendProbeOperationJson.ParseRequest(
                """{"executable_path":"C:\\a.exe","executable_path":"C:\\b.exe"}"""
            )
        );
        var wrongType = Assert.Throws<WordToolkitOperationException>(() =>
            LibreOfficeBackendProbeOperationJson.ParseRequest(
                """{"executable_path":12}"""
            )
        );

        Assert.Equal("INVALID_INPUT", unknown.Code);
        Assert.Equal("INVALID_INPUT", duplicate.Code);
        Assert.Equal("INVALID_INPUT", wrongType.Code);
    }

    [Fact]
    public async Task RejectsProviderIdentityThatCannotProveHashStability()
    {
        var provider = new RecordingProvider(SuccessObservation() with
        {
            ExecutableHashStable = false,
        });
        var exception = await Assert.ThrowsAsync<WordToolkitOperationException>(() =>
            new InspectLibreOfficeBackendOperation(provider).ExecuteAsync(
                new InspectLibreOfficeBackendRequest(
                    Path.Combine(Path.GetTempPath(), "soffice-test")
                )
            )
        );

        Assert.Equal("INVALID_BACKEND", exception.Code);
    }

    private static LibreOfficeBackendProbeObservation SuccessObservation() => new(
        "LibreOffice",
        "24.2.7.2",
        "LibreOffice 24.2.7.2 420(Build:2)",
        "soffice.exe",
        128,
        new string('a', 64),
        true,
        "windows",
        "x64",
        "x64"
    );

    private sealed class RecordingProvider(
        LibreOfficeBackendProbeObservation observation
    ) : ILibreOfficeBackendProbeProvider
    {
        public LibreOfficeBackendProbeProviderRequest? LastRequest { get; private set; }

        public Task<LibreOfficeBackendProbeObservation> ProbeAsync(
            LibreOfficeBackendProbeProviderRequest request,
            CancellationToken cancellationToken = default
        )
        {
            LastRequest = request;
            return Task.FromResult(observation);
        }
    }
}
