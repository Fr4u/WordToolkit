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

public sealed class TransformPackageCliTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public async Task EngineCliAndMcpReturnTheSameCanonicalTransformResultWithoutWord()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "input.docx");
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p><w:r><w:t>Payment due in thirty days</w:t></w:r></w:p>"
                )
            );
            var engineDirectory = Directory.CreateDirectory(
                Path.Combine(directory, "engine")
            ).FullName;
            var cliDirectory = Directory.CreateDirectory(Path.Combine(directory, "cli"))
                .FullName;
            var mcpDirectory = Directory.CreateDirectory(Path.Combine(directory, "mcp"))
                .FullName;
            var engineOutput = Path.Combine(engineDirectory, "result.docx");
            var cliOutput = Path.Combine(cliDirectory, "result.docx");
            var mcpOutput = Path.Combine(mcpDirectory, "result.docx");

            var engineResult = new TransformWordPackageOperation().Execute(
                new TransformWordPackageRequest(
                    input,
                    engineOutput,
                    WordPackageTransformKind.ReplaceFirstTextOccurrence,
                    "thirty",
                    "sixty"
                )
            );
            var engineJson = WordToolkitOperationJson.Serialize(engineResult);

            var cliStdout = new StringWriter();
            var cliStderr = new StringWriter();
            var cliExit = TransformPackageCli.Run(
                [
                    input,
                    cliOutput,
                    "--operation",
                    "replace_first_text_occurrence",
                    "--find-text",
                    "thirty",
                    "--replace-text",
                    "sixty",
                    "--format",
                    "json",
                ],
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
                    ["operation"] = "replace_first_text_occurrence",
                    ["find_text"] = "thirty",
                    ["replace_text"] = "sixty",
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
            Assert.Equal(engineJson, cliJson);
            Assert.Equal(engineJson, mcpJson);
            Assert.True(structured.GetProperty("ok").GetBoolean());
            Assert.False(mcpResponse.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
            Assert.Equal(
                new OpcPackageReader().Read(engineOutput).Fingerprint,
                new OpcPackageReader().Read(cliOutput).Fingerprint
            );
            Assert.Equal(
                new OpcPackageReader().Read(engineOutput).Fingerprint,
                new OpcPackageReader().Read(mcpOutput).Fingerprint
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PlatformAdapterImplementsSuccessUnsupportedAndVersionMismatchCodes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "input.docx");
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p><w:r><w:t>Alpha-tar</w:t></w:r>"
                        + "<w:r><w:t>get-tail</w:t></w:r></w:p>"
                )
            );
            var operation = Path.Combine(directory, "operation.json");
            File.WriteAllText(
                operation,
                """
                {"operationName":"replaceFirstTextOccurrence","findText":"target","replaceText":"clause"}
                """
            );
            var outputPath = Path.Combine(directory, "output.docx");
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = DocxPlatformTestAdapterCli.Run(
                [
                    "--protocol-version",
                    "1",
                    "--operation",
                    operation,
                    "--input",
                    input,
                    "--output",
                    outputPath,
                ],
                stdout,
                stderr
            );
            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, stdout.ToString());
            Assert.Equal(string.Empty, stderr.ToString());
            Assert.True(File.Exists(outputPath));

            var unknown = Path.Combine(directory, "unknown.json");
            File.WriteAllText(unknown, "{\"operationName\":\"composeUnknown\"}");
            stdout = new StringWriter();
            stderr = new StringWriter();
            exit = DocxPlatformTestAdapterCli.Run(
                [
                    "--protocol-version", "1", "--operation", unknown,
                    "--input", input, "--output", Path.Combine(directory, "unknown.docx"),
                ],
                stdout,
                stderr
            );
            Assert.Equal(2, exit);
            Assert.StartsWith("unsupported operation:", stdout.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr.ToString());

            stdout = new StringWriter();
            stderr = new StringWriter();
            exit = DocxPlatformTestAdapterCli.Run(
                [
                    "--protocol-version", "2", "--operation", operation,
                    "--input", input, "--output", Path.Combine(directory, "v2.docx"),
                ],
                stdout,
                stderr
            );
            Assert.Equal(3, exit);
            Assert.Equal("unsupported protocol version" + Environment.NewLine, stdout.ToString());
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PlatformAdapterDeclinesUnsafeReviewDocumentWithoutOutput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "unsafe.docx");
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p><w:pPr><w:rPr><w:del w:id='9' w:author='A'/>"
                        + "</w:rPr></w:pPr><w:r><w:t>x</w:t></w:r></w:p>"
                )
            );
            var operation = Path.Combine(directory, "accept.json");
            File.WriteAllText(operation, "{\"operationName\":\"acceptAllTrackedChanges\"}");
            var outputPath = Path.Combine(directory, "unsafe-output.docx");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exit = DocxPlatformTestAdapterCli.Run(
                [
                    "--protocol-version", "1", "--operation", operation,
                    "--input", input, "--output", outputPath,
                ],
                stdout,
                stderr
            );

            Assert.Equal(2, exit);
            Assert.StartsWith("unsupported input:", stdout.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr.ToString());
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task McpSchemaRejectsReviewTextArgumentsBeforeExecution()
    {
        var host = new NoInvokeHost();
        var response = await CallMcpAsync(
            host,
            new JsonObject
            {
                ["local_path"] = "missing.docx",
                ["output_path"] = "result.docx",
                ["operation"] = "accept_all_tracked_changes",
                ["find_text"] = "forbidden",
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

    [Fact]
    public void CatalogPublishesClosedTransformResultAndProtectionEvidence()
    {
        var tool = ToolCatalog.LoadNativeWordTools()
            .InspectAction(TransformWordPackageContract.OperationName)["tool"]!
            .AsObject();
        var output = tool["outputSchema"]!.AsObject();

        Assert.False(output["additionalProperties"]!.GetValue<bool>());
        Assert.Contains(
            "protection",
            output["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.Contains(
            "changed_entry_names",
            output["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.Equal(
            "boolean",
            output["properties"]!["protection"]!["properties"]!["authorization_required"]!["type"]!
                .GetValue<string>()
        );
        Assert.False(
            output["properties"]!["protection"]!["additionalProperties"]!
                .GetValue<bool>()
        );
        var protection = output["properties"]!["protection"]!["properties"]!;
        Assert.Contains(
            "none",
            protection["base_document_protection_edit_mode"]!["enum"]!
                .AsArray()
                .Select(item => item!.GetValue<string>())
        );
        Assert.Equal(
            250_000,
            protection["base_permission_range_count"]!["maximum"]!.GetValue<int>()
        );
        Assert.Equal(
            500_000,
            protection["malformed_permission_range_count"]!["maximum"]!.GetValue<int>()
        );
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
                ["name"] = "transform_ooxml_package",
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

    private static void CreatePackage(string path, string documentXml)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
              <Default Extension="xml" ContentType="application/xml" />
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{WordPackageConformance.TransitionalOfficeDocumentRelationship}" Target="word/document.xml" />
            </Relationships>
            """
        );
        WriteEntry(archive, "word/document.xml", documentXml);
    }

    private static string DocumentXml(string body) =>
        $"<w:document xmlns:w='{WordNamespace}'><w:body>{body}</w:body></w:document>";

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-transform-cli-tests",
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
                "Package transforms must not invoke or launch Microsoft Word."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
