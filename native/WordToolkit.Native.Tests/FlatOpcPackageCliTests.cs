using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class FlatOpcPackageCliTests
{
    [Fact]
    public async Task EngineCliAndMcpReturnCanonicalParityWithoutOpeningWord()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "input.docx");
            CreatePackage(input);
            var engineDirectory = Directory.CreateDirectory(
                Path.Combine(directory, "engine")
            ).FullName;
            var cliDirectory = Directory.CreateDirectory(
                Path.Combine(directory, "cli")
            ).FullName;
            var mcpDirectory = Directory.CreateDirectory(
                Path.Combine(directory, "mcp")
            ).FullName;
            var engineOutput = Path.Combine(engineDirectory, "result.xml");
            var cliOutput = Path.Combine(cliDirectory, "result.xml");
            var mcpOutput = Path.Combine(mcpDirectory, "result.xml");

            var direct = new FlatOpcWordPackageOperation().Execute(
                new FlatOpcWordPackageRequest(
                    input,
                    engineOutput,
                    FlatOpcConversionDirection.ToFlatOpc
                )
            );
            var directJson = WordToolkitOperationJson.Serialize(direct);

            var cliStdout = new StringWriter();
            var cliStderr = new StringWriter();
            var cliExit = FlatOpcPackageCli.Run(
                [input, cliOutput, "--direction", "to_flat_opc", "--format", "json"],
                cliStdout,
                cliStderr
            );
            var cliJson = JsonNode.Parse(cliStdout.ToString())!
                .ToJsonString(JsonDefaults.Compact);

            var host = new NoInvokeHost();
            var mcpResponse = await CallMcpAsync(
                host,
                new JsonObject
                {
                    ["local_path"] = input,
                    ["output_path"] = mcpOutput,
                    ["direction"] = "to_flat_opc",
                }
            );
            var structured = mcpResponse
                .GetProperty("result")
                .GetProperty("structuredContent");
            var data = JsonNode.Parse(structured.GetProperty("data").GetRawText())!
                .AsObject();
            data.Remove("runtime");
            data.Remove("python_used");
            data.Remove("performance");
            var mcpJson = data.ToJsonString(JsonDefaults.Compact);

            Assert.Equal(0, cliExit);
            Assert.Equal(string.Empty, cliStderr.ToString());
            Assert.Equal(directJson, cliJson);
            Assert.Equal(directJson, mcpJson);
            Assert.True(structured.GetProperty("ok").GetBoolean());
            Assert.False(mcpResponse.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
            Assert.Equal(File.ReadAllBytes(engineOutput), File.ReadAllBytes(cliOutput));
            Assert.Equal(File.ReadAllBytes(engineOutput), File.ReadAllBytes(mcpOutput));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliImportsFlatOpcAndReturnsMachineReadableFailures()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "input.docx");
            var flat = Path.Combine(directory, "input.xml");
            var output = Path.Combine(directory, "output.docx");
            CreatePackage(input);
            _ = new FlatOpcWordPackageOperation().Execute(
                new FlatOpcWordPackageRequest(
                    input,
                    flat,
                    FlatOpcConversionDirection.ToFlatOpc
                )
            );
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exit = FlatOpcPackageCli.Run(
                [flat, output, "--direction", "from_flat_opc"],
                stdout,
                stderr
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, stderr.ToString());
            using var success = JsonDocument.Parse(stdout.ToString());
            Assert.Equal(
                "from_flat_opc",
                success.RootElement.GetProperty("direction").GetString()
            );
            Assert.True(new OpcPackageReader().Read(output).IsStructurallyValid);

            stdout = new StringWriter();
            stderr = new StringWriter();
            exit = FlatOpcPackageCli.Run(
                [flat, output, "--direction", "from_flat_opc"],
                stdout,
                stderr
            );
            Assert.Equal(73, exit);
            using var failure = JsonDocument.Parse(stderr.ToString());
            Assert.Equal(
                "VERSION_CONFLICT",
                failure.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CatalogKeepsFlatOpcLazyAndPublishesCompleteClosedMetadata()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var inspected = catalog.InspectAction("convert_ooxml_flat_opc");
        var tool = inspected["tool"]!.AsObject();

        Assert.Equal(151, catalog.ActionCount);
        Assert.Equal(15, catalog.Tools.Count);
        Assert.DoesNotContain(
            catalog.Tools,
            node => node!["name"]!.GetValue<string>() == "convert_ooxml_flat_opc"
        );
        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.NotNull(tool["outputSchema"]);
        Assert.NotNull(tool["permissions"]);
        Assert.NotNull(tool["reversibility"]);
        Assert.False(
            tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>()
        );
        Assert.False(
            tool["outputSchema"]!["additionalProperties"]!.GetValue<bool>()
        );
        Assert.Equal(
            "wordtoolkit.convert_ooxml_flat_opc/1.0",
            tool["outputSchema"]!["properties"]!["data"]!["properties"]![
                "operation_contract"
            ]!["const"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task McpSchemaRejectsUnknownFieldsBeforeExecution()
    {
        var host = new NoInvokeHost();
        var response = await CallMcpAsync(
            host,
            new JsonObject
            {
                ["local_path"] = "missing.docx",
                ["output_path"] = "result.xml",
                ["direction"] = "to_flat_opc",
                ["raw_xml"] = true,
            }
        );
        var structured = response.GetProperty("result").GetProperty("structuredContent");

        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(
            "INVALID_INPUT",
            structured.GetProperty("error").GetProperty("code").GetString()
        );
        Assert.Equal(0, host.InvocationCount);
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
                ["name"] = "convert_ooxml_flat_opc",
                ["arguments"] = arguments,
            },
        };
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new WordLiveService(host)
        );
        await server.RunAsync();
        using var document = JsonDocument.Parse(output.ToString().Trim());
        return document.RootElement.Clone();
    }

    private static void CreatePackage(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(
            archive,
            "[Content_Types].xml",
            "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
                + "</Types>"
        );
        Write(
            archive,
            "_rels/.rels",
            $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                + $"<Relationship Id='rId1' Type='{WordPackageConformance.TransitionalOfficeDocumentRelationship}' Target='word/document.xml'/>"
                + "</Relationships>"
        );
        Write(
            archive,
            "word/document.xml",
            "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
                + "<w:body><w:p><w:r><w:t>Flat OPC parity</w:t></w:r></w:p></w:body>"
                + "</w:document>"
        );
    }

    private static void Write(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(
            1980,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero
        );
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(value));
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-flatopc-cli-tests",
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
                "Flat OPC package conversion must not invoke or launch Microsoft Word."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
