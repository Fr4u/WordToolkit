using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Rendering;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RenderPackageCliTests
{
    [Fact]
    public async Task EngineCliAndMcpUseOneRendererWithoutInvokingWord()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "source.docx");
            var engineOutput = Path.Combine(directory, "engine.html");
            var cliOutputPath = Path.Combine(directory, "cli.html");
            var mcpOutputPath = Path.Combine(directory, "mcp.html");
            var mcpFullOutputPath = Path.Combine(directory, "mcp-full.html");
            CreatePackage(input);

            var engine = new SemanticHtmlWordPackageOperation().Execute(
                new SemanticHtmlWordPackageRequest(input, engineOutput)
            );
            var cliRequest = JsonSerializer.Serialize(new
            {
                local_path = input,
                output_path = cliOutputPath,
                story_scope = "main_document",
                language = "pl-PL",
            });
            var cliOutput = new StringWriter();
            var cliError = new StringWriter();
            var cliExit = RenderPackageCli.Run(
                ["--request", "-", "--format", "json"],
                new StringReader(cliRequest),
                cliOutput,
                cliError
            );
            using var cliJson = JsonDocument.Parse(cliOutput.ToString());

            var host = new NoInvokeHost();
            var mcpResponse = await CallMcpAsync(
                host,
                new JsonObject
                {
                    ["local_path"] = input,
                    ["output_path"] = mcpOutputPath,
                    ["story_scope"] = "main_document",
                    ["language"] = "pl-PL",
                }
            );
            var structured = mcpResponse
                .GetProperty("result")
                .GetProperty("structuredContent");
            var mcpData = structured.GetProperty("data");
            var mcpFullResponse = await CallMcpAsync(
                host,
                new JsonObject
                {
                    ["local_path"] = input,
                    ["output_path"] = mcpFullOutputPath,
                    ["story_scope"] = "main_document",
                    ["language"] = "pl-PL",
                },
                fullResponse: true
            );
            var mcpFullStructured = mcpFullResponse
                .GetProperty("result")
                .GetProperty("structuredContent");
            var mcpFullData = mcpFullStructured.GetProperty("data");

            Assert.Equal(0, cliExit);
            Assert.Equal(string.Empty, cliError.ToString());
            Assert.Equal(engine.PackageFingerprint, cliJson.RootElement.GetProperty("package_fingerprint").GetString());
            Assert.True(structured.GetProperty("ok").GetBoolean());
            Assert.Equal(engine.PackageFingerprint, mcpData.GetProperty("package_fingerprint").GetString());
            Assert.Equal(
                cliJson.RootElement.GetProperty("artifact_sha256").GetString(),
                mcpData.GetProperty("artifact_sha256").GetString()
            );
            Assert.Equal(File.ReadAllBytes(cliOutputPath), File.ReadAllBytes(mcpOutputPath));
            Assert.Equal(File.ReadAllBytes(cliOutputPath), File.ReadAllBytes(mcpFullOutputPath));
            Assert.False(mcpData.TryGetProperty("runtime", out _));
            Assert.False(mcpData.TryGetProperty("python_used", out _));
            Assert.False(mcpData.TryGetProperty("performance", out _));
            Assert.Equal("dotnet-native", mcpFullData.GetProperty("runtime").GetString());
            Assert.False(mcpFullData.GetProperty("python_used").GetBoolean());
            Assert.True(mcpFullData.GetProperty("performance").GetProperty("total_ms").GetDouble() >= 0);
            AssertMatchesPublishedClosedShape(structured);
            AssertMatchesPublishedClosedShape(mcpFullStructured);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliAndMcpParserRejectUnknownArguments()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = RenderPackageCli.Run(
            ["--request", "-"],
            new StringReader(
                "{\"local_path\":\"a.docx\",\"output_path\":\"a.html\",\"execute_magic\":true}"
            ),
            output,
            error
        );

        Assert.Equal(64, exit);
        Assert.Equal(string.Empty, output.ToString());
        using var json = JsonDocument.Parse(error.ToString());
        Assert.Equal(
            "INVALID_INPUT",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
        Assert.Throws<NativeToolException>(() =>
        {
            using var arguments = JsonDocument.Parse(
                "{\"local_path\":\"a.docx\",\"output_path\":\"a.html\",\"execute_magic\":true}"
            );
            _ = new WordLiveService(new NoInvokeHost()).CallAsync(
                SemanticHtmlWordPackageContract.OperationName,
                arguments.RootElement,
                CancellationToken.None
            );
        });
    }

    [Fact]
    public void CatalogKeepsRendererLazyAndPublishesCompleteHonestMetadata()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.True(catalog.IsAction(SemanticHtmlWordPackageContract.OperationName));
        Assert.DoesNotContain(catalog.Tools, node =>
            node?["name"]?.GetValue<string>()
                == SemanticHtmlWordPackageContract.OperationName
        );
        var tool = catalog.InspectAction(SemanticHtmlWordPackageContract.OperationName)[
            "tool"
        ]!.AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.False(tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(tool["outputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            "read_input_package_and_create_new_html_output",
            tool["permissions"]!["filesystem"]!.GetValue<string>()
        );
        Assert.True(tool["reversibility"]!["applicable"]!.GetValue<bool>());
        Assert.False(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(tool["annotations"]!["destructiveHint"]!.GetValue<bool>());
        Assert.Contains(
            "does not claim Microsoft Word layout",
            tool["description"]!.GetValue<string>(),
            StringComparison.Ordinal
        );
        Assert.InRange(
            tool.ToJsonString(JsonDefaults.Compact).Length,
            1,
            10_000
        );
    }

    private static async Task<JsonElement> CallMcpAsync(
        NoInvokeHost host,
        JsonObject arguments,
        bool fullResponse = false
    )
    {
        JsonObject callArguments = arguments.DeepClone().AsObject();
        var toolName = SemanticHtmlWordPackageContract.OperationName;
        if (fullResponse)
        {
            toolName = "execute_wordtoolkit_action";
            callArguments = new JsonObject
            {
                ["action"] = SemanticHtmlWordPackageContract.OperationName,
                ["arguments"] = arguments.DeepClone(),
                ["response_mode"] = "full",
            };
        }
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = callArguments,
            },
        };
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(
                request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine
            ),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new WordLiveService(host)
        );
        await server.RunAsync();
        using var document = JsonDocument.Parse(output.ToString().Trim());
        return document.RootElement.Clone();
    }

    private static void AssertMatchesPublishedClosedShape(JsonElement structured)
    {
        var schema = ToolCatalog
            .LoadNativeWordTools()
            .InspectAction(SemanticHtmlWordPackageContract.OperationName)["tool"]![
                "outputSchema"
            ]!.AsObject();
        AssertRequiredAndClosed(structured, schema);
        AssertRequiredAndClosed(
            structured.GetProperty("data"),
            schema["properties"]!["data"]!.AsObject()
        );
    }

    private static void AssertRequiredAndClosed(JsonElement actual, JsonObject schema)
    {
        var allowed = schema["properties"]!.AsObject().Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var required in schema["required"]!.AsArray())
        {
            Assert.True(actual.TryGetProperty(required!.GetValue<string>(), out _));
        }
        foreach (var property in actual.EnumerateObject())
        {
            Assert.Contains(property.Name, allowed);
        }
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    private static void CreatePackage(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{WordPackageConformance.TransitionalOfficeDocumentRelationship}" Target="word/document.xml"/>
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p><w:r><w:t>Adapter parity</w:t></w:r></w:p></w:body>
            </w:document>
            """
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-render-cli-tests",
            Guid.NewGuid().ToString("N")
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
                "Saved-package semantic rendering must not invoke the Word COM host."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
