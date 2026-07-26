using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Observability;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Resources;
using WordToolkit.LibreOffice;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LibreOfficeBackendProbeTests
{
    [Fact]
    public void CatalogKeepsProbeLazyAndPublishesClosedMetadata()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.Equal(144, catalog.ActionCount);
        Assert.DoesNotContain(
            catalog.Tools,
            tool => tool!["name"]!.GetValue<string>()
                == LibreOfficeBackendProbeContract.OperationName
        );
        var tool = catalog.InspectAction(
            LibreOfficeBackendProbeContract.OperationName
        )["tool"]!.AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.NotNull(tool["permissions"]);
        Assert.NotNull(tool["reversibility"]);
        Assert.False(tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(tool["outputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            LibreOfficeBackendProbeContract.Contract,
            tool["outputSchema"]!["properties"]!["data"]!["properties"]![
                "operation_contract"
            ]!["const"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task ProviderRunsOnlyFixedVersionArgumentAndReturnsBoundIdentity()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, OperatingSystem.IsWindows() ? "soffice.exe" : "soffice");
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
            Assert.Equal(Path.GetFileName(path), observation.ExecutableFileName);
            Assert.True(observation.ExecutableHashStable);
            var process = Assert.Single(runner.Requests);
            Assert.Equal(path, process.ExecutablePath);
            Assert.Equal(["--version"], process.Arguments);
            Assert.Equal(8_192, process.MaximumOutputCharacters);
            var json = WordToolkitOperationJson.Serialize(observation);
            Assert.DoesNotContain(directory, json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HashMismatchFailsBeforeStartingProcess()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, OperatingSystem.IsWindows() ? "soffice.exe" : "soffice");
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
    public async Task ProviderFailsClosedForUnqualifiedProcessEvidence(
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
            var path = Path.Combine(directory, OperatingSystem.IsWindows() ? "soffice.exe" : "soffice");
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
    public async Task CliAndMcpShareTheOperationWithoutInvokingWord()
    {
        var path = Path.Combine(Path.GetTempPath(), OperatingSystem.IsWindows() ? "soffice.exe" : "soffice");
        var provider = new StaticProvider();
        var requestJson = JsonSerializer.Serialize(new { executable_path = path });
        var cliOutput = new StringWriter();
        var cliError = new StringWriter();

        var exit = await LibreOfficeBackendProbeCli.RunAsync(
            ["--request", "-", "--format", "json"],
            new StringReader(requestJson),
            cliOutput,
            cliError,
            provider
        );

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, cliError.ToString());
        using var cli = JsonDocument.Parse(cliOutput.ToString());
        Assert.Equal(
            LibreOfficeBackendProbeContract.Contract,
            cli.RootElement.GetProperty("operation_contract").GetString()
        );

        var host = new NoInvokeHost();
        var service = new WordLiveService(
            host,
            () => new WordOperationResourceLease(),
            WordOperationObservability.Disabled,
            provider
        );
        using var arguments = JsonDocument.Parse(requestJson);
        var result = await service.CallAsync(
            LibreOfficeBackendProbeContract.OperationName,
            arguments.RootElement,
            CancellationToken.None
        );
        using var mcp = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));

        Assert.Equal(0, host.InvocationCount);
        Assert.Equal(
            LibreOfficeBackendProbeContract.Contract,
            mcp.RootElement.GetProperty("operation_contract").GetString()
        );
        Assert.False(
            mcp.RootElement.GetProperty("capabilities").GetProperty("rendering_verified").GetBoolean()
        );
        Assert.False(
            mcp.RootElement.GetProperty("security").GetProperty("network_isolation_enforced").GetBoolean()
        );
        Assert.False(
            mcp.RootElement.GetProperty("security").GetProperty("profile_created_by_word_toolkit").GetBoolean()
        );
        Assert.True(
            mcp.RootElement.GetProperty("performance").TryGetProperty("total_milliseconds", out _)
        );
    }

    [Fact]
    public async Task LazyMcpGatewayReturnsTheVersionedClosedEnvelope()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            OperatingSystem.IsWindows() ? "soffice.exe" : "soffice"
        );
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 71,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "execute_wordtoolkit_action",
                ["arguments"] = new JsonObject
                {
                    ["action"] = LibreOfficeBackendProbeContract.OperationName,
                    ["arguments"] = new JsonObject { ["executable_path"] = path },
                    ["response_mode"] = "full",
                },
            },
        };
        var output = new StringWriter();
        var host = new NoInvokeHost();
        var service = new WordLiveService(
            host,
            () => new WordOperationResourceLease(),
            WordOperationObservability.Disabled,
            new StaticProvider()
        );
        var server = new McpServer(
            new StringReader(request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
            output,
            ToolCatalog.LoadNativeWordTools(),
            service
        );

        await server.RunAsync();

        using var response = JsonDocument.Parse(output.ToString().Trim());
        var structured = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        Assert.True(structured.GetProperty("ok").GetBoolean());
        var data = structured.GetProperty("data");
        Assert.Equal(
            LibreOfficeBackendProbeContract.Contract,
            data.GetProperty("operation_contract").GetString()
        );
        Assert.False(data.GetProperty("capabilities").GetProperty("rendering_verified").GetBoolean());
        Assert.False(data.GetProperty("security").GetProperty("executable_path_returned").GetBoolean());
        Assert.Equal(0, host.InvocationCount);
    }

    [Fact]
    public async Task RealLibreOfficeProbeRunsOnlyWhenExplicitlyConfigured()
    {
        var path = Environment.GetEnvironmentVariable("WORDTOOLKIT_TEST_LIBREOFFICE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        var expectedHash = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_SHA256"
        );

        var result = await new InspectLibreOfficeBackendOperation(
            new LibreOfficeBackendProbeProvider()
        ).ExecuteAsync(new InspectLibreOfficeBackendRequest(path, expectedHash));

        Assert.True(result.Available);
        Assert.StartsWith("LibreOffice", result.Identity.Product, StringComparison.Ordinal);
        Assert.True(result.Capabilities.VersionProbeVerified);
        Assert.False(result.Capabilities.RenderingVerified);
        if (expectedHash is not null)
        {
            Assert.Equal(expectedHash, result.Identity.ExecutableSha256);
            Assert.True(result.Identity.ExpectedExecutableHashEnforced);
        }
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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-libreoffice-probe-" + Guid.NewGuid().ToString("N")
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

    private sealed class StaticProvider : ILibreOfficeBackendProbeProvider
    {
        public Task<LibreOfficeBackendProbeObservation> ProbeAsync(
            LibreOfficeBackendProbeProviderRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(
            new LibreOfficeBackendProbeObservation(
                "LibreOffice",
                "24.2.7.2",
                "LibreOffice 24.2.7.2",
                "soffice.exe",
                128,
                new string('a', 64),
                true,
                "windows",
                "x64",
                "x64"
            )
        );
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public int InvocationCount { get; private set; }

        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        )
        {
            InvocationCount++;
            throw new Xunit.Sdk.XunitException(
                "LibreOffice backend probe must not invoke Microsoft Word."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
