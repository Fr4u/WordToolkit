using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class OcrPackageCliTests
{
    [Fact]
    public async Task EngineCliAndLazyMcpInspectTheSameCandidatesWithoutWord()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, BuildPackage(TestPng));
            var request = JsonSerializer.Serialize(new { local_path = path });
            var direct = new OcrWordPackageOperation(
                NativeExtensionHost.Registry
            ).Inspect(OcrOperationJson.ParseInspectRequest(request));

            var cliOutput = new StringWriter();
            var cliError = new StringWriter();
            var exit = OcrPackageCli.Run(
                ["--mode", "inspect", "--request", "-", "--format", "json"],
                new StringReader(request),
                cliOutput,
                cliError
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, cliError.ToString());
            Assert.Equal(
                WordToolkitOperationJson.Serialize(direct),
                JsonNode.Parse(cliOutput.ToString())!.ToJsonString(JsonDefaults.Compact)
            );

            var host = new NoInvokeHost();
            var catalog = ToolCatalog.LoadNativeWordTools();
            var rpc = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "execute_wordtoolkit_action",
                    ["arguments"] = new JsonObject
                    {
                        ["action"] = OcrWordPackageContract.InspectOperationName,
                        ["arguments"] = new JsonObject { ["local_path"] = path },
                        ["response_mode"] = "full",
                    },
                },
            };
            var mcpOutput = new StringWriter();
            var server = new McpServer(
                new StringReader(rpc.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
                mcpOutput,
                catalog,
                new WordLiveService(host)
            );

            await server.RunAsync();

            var response = JsonNode.Parse(mcpOutput.ToString().Trim())!.AsObject();
            var structured = response["result"]!["structuredContent"]!;
            var schema = catalog.InspectAction(
                OcrWordPackageContract.InspectOperationName
            )["tool"]!["outputSchema"]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            var data = structured["data"]!.AsObject();
            Assert.Equal(direct.PackageFingerprint, data["package_fingerprint"]!.GetValue<string>());
            Assert.Equal(1, data["candidate_count"]!.GetValue<int>());
            Assert.Equal(1, data["eligible_candidate_count"]!.GetValue<int>());
            Assert.True(data["candidate_coverage_complete"]!.GetValue<bool>());
            Assert.Equal("dotnet-native", data["runtime"]!.GetValue<string>());
            Assert.False(data["python_used"]!.GetValue<bool>());
            Assert.False(data["disclosure"]!["provider_invoked"]!.GetValue<bool>());
            Assert.False(data["disclosure"]!["image_hashes_returned"]!.GetValue<bool>());
            Assert.False(data["disclosure"]!["source_returned"]!.GetValue<bool>());
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CliRejectsUnknownFieldsAndMalformedModesBeforeFilesystemAccess()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = OcrPackageCli.Run(
            ["--mode", "inspect", "--request", "-"],
            new StringReader(
                "{\"local_path\":\"Z:\\\\missing.docx\",\"image_bytes\":true}"
            ),
            output,
            error
        );

        Assert.Equal(64, exit);
        Assert.Equal(string.Empty, output.ToString());
        using var failure = JsonDocument.Parse(error.ToString());
        Assert.Equal(
            "INVALID_INPUT",
            failure.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        exit = OcrPackageCli.Run(
            ["--mode", "magic", "--request", "-"],
            new StringReader("{}"),
            output,
            error
        );
        Assert.Equal(64, exit);
        Assert.Contains("INVALID_INPUT", error.ToString(), StringComparison.Ordinal);
    }

    internal static byte[] BuildPackage(byte[] imageBytes)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Add(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                  xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                  xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body><w:p><w:r><w:drawing><wp:inline>
                    <wp:extent cx="914400" cy="457200"/>
                    <wp:docPr id="1" name="OCR source" descr="Embedded test scan"/>
                    <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                      <pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="ocr.png"/><pic:cNvPicPr/></pic:nvPicPr>
                        <pic:blipFill><a:blip r:embed="rIdImage"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                        <pic:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                      </pic:pic>
                    </a:graphicData></a:graphic>
                  </wp:inline></w:drawing></w:r></w:p><w:sectPr/></w:body>
                </w:document>
                """);
            Add(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
                </Relationships>
                """);
            Add(archive, "word/media/image1.png", imageBytes);
        }
        return stream.ToArray();
    }

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"wordtoolkit-native-ocr-cli-{Guid.NewGuid():N}.docx"
    );

    private static readonly byte[] TestPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9WlSAAAAAASUVORK5CYII="
    );

    private static void Add(ZipArchive archive, string name, string content) =>
        Add(archive, name, Encoding.UTF8.GetBytes(content));

    private static void Add(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var destination = entry.Open();
        destination.Write(content);
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
            throw new Xunit.Sdk.XunitException("Offline OCR must not invoke Microsoft Word.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
