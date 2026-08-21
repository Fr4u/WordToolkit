using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Rendering;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class SemanticSvgAdapterTests
{
    [Fact]
    public async Task EngineCliAndMcpProduceTheSameExactTargetSvg()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "source.docx");
            var engineOutput = Path.Combine(directory, "engine.svg");
            var cliOutput = Path.Combine(directory, "cli.svg");
            var mcpOutput = Path.Combine(directory, "mcp.svg");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var target = Assert.Single(
                new WordSemanticProjector().Project(package).Nodes,
                node => node.Kind == WordSemanticNodeKind.Paragraph
            );
            var engine = new SemanticSvgWordPackageOperation().Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    engineOutput,
                    package.Fingerprint,
                    target.Id.Value,
                    ViewportWidthPx: 900
                )
            );

            var cliRequest = JsonSerializer.Serialize(new
            {
                local_path = input,
                output_path = cliOutput,
                expected_package_fingerprint = package.Fingerprint,
                target_node_id = target.Id.Value,
                viewport_width_px = 900,
            });
            var cliStdout = new StringWriter();
            var cliStderr = new StringWriter();
            var cliExit = RenderPackageCli.Run(
                ["--request", "-", "--backend", "semantic-svg", "--format", "json"],
                new StringReader(cliRequest),
                cliStdout,
                cliStderr
            );
            using var cliJson = JsonDocument.Parse(cliStdout.ToString());

            var host = new NoInvokeHost();
            var structured = await CallMcpAsync(
                host,
                new JsonObject
                {
                    ["local_path"] = input,
                    ["output_path"] = mcpOutput,
                    ["expected_package_fingerprint"] = package.Fingerprint,
                    ["target_node_id"] = target.Id.Value,
                    ["viewport_width_px"] = 900,
                }
            );
            var data = structured.GetProperty("data");

            Assert.Equal(0, cliExit);
            Assert.Equal(string.Empty, cliStderr.ToString());
            Assert.Equal(engine.ArtifactSha256, cliJson.RootElement.GetProperty("artifact_sha256").GetString());
            Assert.Equal(engine.ArtifactSha256, data.GetProperty("artifact_sha256").GetString());
            Assert.Equal(File.ReadAllBytes(engineOutput), File.ReadAllBytes(cliOutput));
            Assert.Equal(File.ReadAllBytes(engineOutput), File.ReadAllBytes(mcpOutput));
            Assert.Equal("wordtoolkit-semantic-svg", data.GetProperty("backend").GetString());
            Assert.Equal("image/svg+xml", data.GetProperty("artifact_media_type").GetString());
            Assert.Equal(target.Id.Value, data.GetProperty("target_node_id").GetString());
            Assert.False(data.GetProperty("pixel_equivalence_claimed").GetBoolean());
            Assert.False(data.TryGetProperty("runtime", out _));
            Assert.False(data.TryGetProperty("python_used", out _));
            Assert.False(data.TryGetProperty("performance", out _));
            AssertRequiredAndClosed(structured);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CatalogKeepsSvgLazyAndPublishesHonestClosedMetadata()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.True(catalog.IsAction(SemanticSvgWordPackageContract.OperationName));
        Assert.DoesNotContain(catalog.Tools, node =>
            node?["name"]?.GetValue<string>() == SemanticSvgWordPackageContract.OperationName
        );
        var tool = catalog.InspectAction(SemanticSvgWordPackageContract.OperationName)[
            "tool"
        ]!.AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.False(tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(tool["outputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            "read_input_package_and_create_new_svg_output",
            tool["permissions"]!["filesystem"]!.GetValue<string>()
        );
        Assert.Equal(
            SemanticSvgWordPackageContract.MaximumTextLineCount,
            tool["limits"]!["generated_text_lines"]!.GetValue<int>()
        );
        Assert.Equal(
            SemanticSvgWordPackageContract.MaximumSvgElementCount,
            tool["limits"]!["generated_svg_elements"]!.GetValue<int>()
        );
        Assert.Contains(
            "pixel equivalence are explicitly not claimed",
            tool["description"]!.GetValue<string>(),
            StringComparison.Ordinal
        );
        Assert.False(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(tool["annotations"]!["destructiveHint"]!.GetValue<bool>());
    }

    [Fact]
    public void CliRejectsUnknownBackendWithoutReadingRequest()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = RenderPackageCli.Run(
            ["--request", "missing.json", "--backend", "word-magic"],
            new StringReader(string.Empty),
            stdout,
            stderr
        );

        Assert.Equal(64, exit);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Contains("INVALID_INPUT", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("NOT_FOUND", stderr.ToString(), StringComparison.Ordinal);
    }

    private static async Task<JsonElement> CallMcpAsync(
        NoInvokeHost host,
        JsonObject arguments
    )
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = SemanticSvgWordPackageContract.OperationName,
                ["arguments"] = arguments,
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
        return document.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .Clone();
    }

    private static void AssertRequiredAndClosed(JsonElement structured)
    {
        var schema = ToolCatalog
            .LoadNativeWordTools()
            .InspectAction(SemanticSvgWordPackageContract.OperationName)["tool"]![
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
              <w:body><w:p><w:r><w:t>Adapter SVG parity</w:t></w:r></w:p></w:body>
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
            "wordtoolkit-semantic-svg-adapter-tests",
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
                "Saved-package semantic SVG rendering must not invoke Word COM."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
