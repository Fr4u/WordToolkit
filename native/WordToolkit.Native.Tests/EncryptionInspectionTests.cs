using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class EncryptionInspectionTests
{
    [Fact]
    public void CatalogKeepsEncryptionInspectionLazyAndPublishesClosedMetadata()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.Equal(130, catalog.ActionCount);
        Assert.DoesNotContain(
            catalog.Tools,
            tool => tool!["name"]!.GetValue<string>()
                == InspectOoxmlEncryptionContract.OperationName
        );
        var tool = catalog.InspectAction(
            InspectOoxmlEncryptionContract.OperationName
        )["tool"]!.AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.NotNull(tool["permissions"]);
        Assert.NotNull(tool["reversibility"]);
        Assert.False(tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(tool["outputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            InspectOoxmlEncryptionContract.Contract,
            tool["outputSchema"]!["properties"]!["data"]!["properties"]![
                "operation_contract"
            ]!["const"]!.GetValue<string>()
        );
        Assert.False(
            tool["outputSchema"]!["properties"]!["data"]!["properties"]!["security"]![
                "properties"
            ]!["decrypts_content"]!["const"]!.GetValue<bool>()
        );
    }

    [Fact]
    public void CliInspectsWithoutReturningTheLocalPath()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "plain.docx");
            File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4]);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = InspectEncryptionCli.Run(
                [path, "--format", "json"],
                output,
                error
            );

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.DoesNotContain(directory, output.ToString(), StringComparison.OrdinalIgnoreCase);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                InspectOoxmlEncryptionContract.Contract,
                json.RootElement.GetProperty("operation_contract").GetString()
            );
            Assert.Equal(
                "opc_zip_candidate",
                json.RootElement.GetProperty("container_kind").GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NativeServiceIsReadOnlyClosedAndDoesNotInvokeWord()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "plain.docx");
            File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4]);
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var result = await service.CallAsync(
                InspectOoxmlEncryptionContract.OperationName,
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );

            Assert.Equal(0, host.InvocationCount);
            Assert.Equal("dotnet-native", json.RootElement.GetProperty("runtime").GetString());
            Assert.False(json.RootElement.GetProperty("python_used").GetBoolean());
            Assert.False(
                json.RootElement
                    .GetProperty("security")
                    .GetProperty("returns_paths")
                    .GetBoolean()
            );

            using var invalid = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, password = "secret" })
            );
            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    InspectOoxmlEncryptionContract.OperationName,
                    invalid.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", exception.ErrorCode);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LazyMcpExecutionReturnsTheVersionedSuccessEnvelope()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "plain.docx");
            File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4]);
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "execute_wordtoolkit_action",
                    ["arguments"] = new JsonObject
                    {
                        ["action"] = InspectOoxmlEncryptionContract.OperationName,
                        ["arguments"] = new JsonObject { ["local_path"] = path },
                        ["response_mode"] = "full",
                    },
                },
            };
            var output = new StringWriter();
            var host = new NoInvokeHost();
            var server = new McpServer(
                new StringReader(request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
                output,
                ToolCatalog.LoadNativeWordTools(),
                new WordLiveService(host)
            );

            await server.RunAsync();

            using var response = JsonDocument.Parse(output.ToString().Trim());
            var structured = response.RootElement
                .GetProperty("result")
                .GetProperty("structuredContent");
            Assert.True(structured.GetProperty("ok").GetBoolean());
            Assert.Equal(
                InspectOoxmlEncryptionContract.Contract,
                structured.GetProperty("data").GetProperty("operation_contract").GetString()
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-encryption-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
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
                "OOXML encryption inspection must not invoke Microsoft Word."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
