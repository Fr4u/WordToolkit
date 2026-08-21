using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class DocumentAnalysisPackageCliTests
{
    [Fact]
    public void CliUsesTheStrictEngineContractWithoutReturningDocumentContent()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var request = JsonSerializer.Serialize(new { local_path = path });
            var output = new StringWriter();
            var error = new StringWriter();

            var exit = DocumentAnalysisPackageCli.Run(
                ["--request", "-", "--format", "json"],
                new StringReader(request),
                output,
                error
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var json = JsonDocument.Parse(output.ToString());
            var root = json.RootElement;
            Assert.Equal(
                DocumentAnalysisWordPackageContract.Contract,
                root.GetProperty("operation_contract").GetString()
            );
            Assert.Equal(1, root.GetProperty("safety")
                .GetProperty("external_relationship_count").GetInt32());
            Assert.False(root.GetProperty("disclosure")
                .GetProperty("document_text_returned").GetBoolean());
            Assert.False(root.GetProperty("disclosure")
                .GetProperty("word_opened").GetBoolean());
            Assert.DoesNotContain(
                "Native secret document text",
                output.ToString(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task McpEnvelopeConformsToClosedSchemaWithoutComAndStaysTokenLean()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "analyze_ooxml_document",
                    ["arguments"] = new JsonObject
                    {
                        ["local_path"] = path,
                    },
                },
            };
            var output = new StringWriter();
            var host = new NoInvokeHost();
            var catalog = ToolCatalog.LoadNativeWordTools();
            var server = new McpServer(
                new StringReader(request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
                output,
                catalog,
                new WordLiveService(host)
            );

            await server.RunAsync();

            var response = JsonNode.Parse(output.ToString().Trim())!.AsObject();
            var structured = response["result"]!["structuredContent"]!;
            var schema = catalog.InspectAction("analyze_ooxml_document")["tool"]![
                "outputSchema"
            ]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            Assert.True(structured["ok"]!.GetValue<bool>());
            var data = structured["data"]!.AsObject();
            Assert.Equal(
                DocumentAnalysisWordPackageContract.Contract,
                data["operation_contract"]!.GetValue<string>()
            );
            Assert.False(data["disclosure"]!["document_text_returned"]!.GetValue<bool>());
            Assert.False(data["disclosure"]!["word_opened"]!.GetValue<bool>());
            Assert.Equal(0, host.InvocationCount);
            Assert.DoesNotContain(
                "Native secret document text",
                structured.ToJsonString(JsonDefaults.Compact),
                StringComparison.Ordinal
            );
            Assert.True(
                structured.ToJsonString(JsonDefaults.Compact).Length < 7_500,
                $"Default document-analysis response is too large: {structured.ToJsonString(JsonDefaults.Compact).Length}"
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CliRejectsUnknownFieldsBeforeFilesystemAccess()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = DocumentAnalysisPackageCli.Run(
            ["--request", "-"],
            new StringReader(
                "{\"local_path\":\"Z:\\\\missing.docx\",\"include_text\":true}"
            ),
            output,
            error
        );

        Assert.Equal(64, exit);
        using var json = JsonDocument.Parse(error.ToString());
        Assert.Equal(
            "INVALID_INPUT",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"wordtoolkit-document-analysis-{Guid.NewGuid():N}.docx"
    );

    private static byte[] BuildPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/></Types>
                """
            );
            Add(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/><Relationship Id="rCore" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/></Relationships>
                """
            );
            Add(
                archive,
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:pPr><w:pStyle w:val="HeadingOne"/></w:pPr><w:r><w:t>Native secret document text</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl></w:body></w:document>
                """
            );
            Add(
                archive,
                "word/_rels/document.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.invalid/private" TargetMode="External"/></Relationships>
                """
            );
            Add(
                archive,
                "word/styles.xml",
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:default="1" w:styleId="Normal"/><w:style w:type="paragraph" w:styleId="HeadingOne"><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style></w:styles>
                """
            );
            Add(
                archive,
                "docProps/core.xml",
                """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title/></cp:coreProperties>
                """
            );
        }
        return stream.ToArray();
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
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
                "Saved-package document analysis must not invoke Word COM."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
