using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class ExtensionCatalogTests
{
    [Fact]
    public void CliMatchesDirectEngineOperationAndRejectsBadInput()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = ExtensionCatalogCli.Run(
            ["--query", "openxml", "--limit", "4", "--format", "json"],
            output,
            error
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("", error.ToString());
        var direct = new InspectExtensionCatalogOperation(
            NativeExtensionHost.Registry
        ).Execute(new InspectExtensionCatalogRequest("openxml", 0, 4));
        Assert.Equal(
            WordToolkitOperationJson.Serialize(direct),
            JsonNode.Parse(output.ToString())!.ToJsonString(JsonDefaults.Compact)
        );
        Assert.Single(direct.Items);
        Assert.Equal(
            NativeExtensionHost.OpenXmlValidatorCapabilityId,
            direct.Items[0].CapabilityId
        );
        Assert.Equal(["read_document_content", "read_package"], direct.Items[0].Permissions);
        Assert.False(direct.Security.ReturnsDocumentContent);
        Assert.False(direct.Security.LoadsAssemblies);

        var ocr = new InspectExtensionCatalogOperation(
            NativeExtensionHost.Registry
        ).Execute(new InspectExtensionCatalogRequest("ocr", 0, 4));
        var ocrItem = Assert.Single(ocr.Items);
        Assert.Equal(
            NativeExtensionHost.TesseractOcrCapabilityId,
            ocrItem.CapabilityId
        );
        Assert.Equal(
            WordToolkit.Engine.Extensions.WordToolkitExtensionKind.OcrProvider,
            ocrItem.Kind
        );
        Assert.Equal(
            ["filesystem_read", "read_document_content", "spawn_process"],
            ocrItem.Permissions
        );
        Assert.True(ocrItem.CapabilityReturnsDocumentContent);
        Assert.DoesNotContain("network", ocrItem.Permissions);

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        exitCode = ExtensionCatalogCli.Run(["--limit", "33"], output, error);
        Assert.Equal(64, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Contains("INVALID_INPUT", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogIsLazyAndHasCompleteClosedMetadata()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.Equal(148, catalog.ActionCount);
        Assert.DoesNotContain(
            catalog.Tools,
            tool => tool!["name"]!.GetValue<string>()
                == InspectExtensionCatalogContract.OperationName
        );

        var inspected = catalog.InspectAction(
            InspectExtensionCatalogContract.OperationName
        )["tool"]!.AsObject();
        Assert.Equal("1.0", inspected["operationVersion"]!.GetValue<string>());
        Assert.NotNull(inspected["permissions"]);
        Assert.NotNull(inspected["reversibility"]);
        Assert.NotNull(inspected["outputSchema"]);
        Assert.True(
            inspected["annotations"]!["readOnlyHint"]!.GetValue<bool>()
        );
        Assert.False(
            inspected["annotations"]!["openWorldHint"]!.GetValue<bool>()
        );
        Assert.False(
            inspected["inputSchema"]!["additionalProperties"]!.GetValue<bool>()
        );
    }

    [Fact]
    public async Task HandlerAndLazyMcpReturnTheSameContentFreeCatalogWithoutWord()
    {
        await using var host = new NoInvokeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse("""{"limit":4}""");
        var handlerResult = await service.CallAsync(
            InspectExtensionCatalogContract.OperationName,
            arguments.RootElement,
            CancellationToken.None
        );
        var direct = new InspectExtensionCatalogOperation(
            NativeExtensionHost.Registry
        ).Execute(new InspectExtensionCatalogRequest(Limit: 4));
        Assert.Equal(
            WordToolkitOperationJson.Serialize(direct),
            JsonSerializer.Serialize(handlerResult, JsonDefaults.Compact)
        );
        Assert.Equal(0, host.InvocationCount);

        const string request =
            """{"jsonrpc":"2.0","id":91,"method":"tools/call","params":{"name":"execute_wordtoolkit_action","arguments":{"action":"inspect_wordtoolkit_extensions","arguments":{"limit":4},"response_mode":"full"}}}"""
            + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(request),
            output,
            ToolCatalog.LoadNativeWordTools(),
            service
        );
        await server.RunAsync();

        using var response = JsonDocument.Parse(output.ToString());
        var structured = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        Assert.True(structured.GetProperty("ok").GetBoolean());
        Assert.Equal(
            direct.CatalogSha256,
            structured.GetProperty("data").GetProperty("catalog_sha256").GetString()
        );
        Assert.False(
            structured.GetProperty("data")
                .GetProperty("security")
                .GetProperty("loads_assemblies")
                .GetBoolean()
        );
        Assert.Equal(0, host.InvocationCount);
    }

    [Fact]
    public async Task HandlerRejectsUnknownAndMalformedArgumentsBeforeWord()
    {
        await using var host = new NoInvokeHost();
        var service = new WordLiveService(host);
        using var unknown = JsonDocument.Parse("""{"local_path":"secret.docx"}""");
        using var malformed = JsonDocument.Parse("""{"offset":"zero"}""");

        Assert.Equal(
            "INVALID_INPUT",
            (await Assert.ThrowsAsync<NativeToolException>(() => service.CallAsync(
                InspectExtensionCatalogContract.OperationName,
                unknown.RootElement,
                CancellationToken.None
            ))).ErrorCode
        );
        Assert.Equal(
            "INVALID_INPUT",
            (await Assert.ThrowsAsync<NativeToolException>(() => service.CallAsync(
                InspectExtensionCatalogContract.OperationName,
                malformed.RootElement,
                CancellationToken.None
            ))).ErrorCode
        );
        Assert.Equal(0, host.InvocationCount);
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
                "Extension catalog inspection must not invoke Microsoft Word."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
