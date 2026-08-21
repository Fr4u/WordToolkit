using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class SemanticRolePackageCliTests
{
    [Fact]
    public void CliUsesStrictEngineContractWithoutReturningParagraphText()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var request = JsonSerializer.Serialize(new { local_path = path });
            var output = new StringWriter();
            var error = new StringWriter();

            var exit = SemanticRolePackageCli.Run(
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
                SemanticRoleWordPackageContract.Contract,
                root.GetProperty("operation_contract").GetString()
            );
            Assert.Equal(3, root.GetProperty("returned_item_count").GetInt32());
            Assert.False(
                root.GetProperty("disclosure").GetProperty("text_returned").GetBoolean()
            );
            Assert.DoesNotContain("Secret", output.ToString(), StringComparison.Ordinal);
            Assert.False(
                root.GetProperty("disclosure").GetProperty("word_opened").GetBoolean()
            );
            Assert.False(root.GetProperty("semantic_completeness_claimed").GetBoolean());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task McpEnvelopeConformsToPublishedClosedSchemaWithoutCom()
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
                    ["name"] = "execute_wordtoolkit_action",
                    ["arguments"] = new JsonObject
                    {
                        ["action"] = "inspect_ooxml_semantic_roles",
                        ["arguments"] = new JsonObject { ["local_path"] = path },
                        ["response_mode"] = "full",
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
            var schema = catalog.InspectAction("inspect_ooxml_semantic_roles")["tool"]![
                "outputSchema"
            ]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            Assert.True(structured["ok"]!.GetValue<bool>());
            var data = structured["data"]!.AsObject();
            Assert.Equal(3, data["returned_item_count"]!.GetValue<int>());
            Assert.False(data["disclosure"]!["text_returned"]!.GetValue<bool>());
            Assert.False(data["semantic_completeness_claimed"]!.GetValue<bool>());
            Assert.Equal("dotnet-native", data["runtime"]!.GetValue<string>());
            Assert.False(data["python_used"]!.GetValue<bool>());
            Assert.Equal(0, host.InvocationCount);
            Assert.True(
                structured.ToJsonString(JsonDefaults.Compact).Length < 6_000,
                $"Default semantic-role response is too large: {structured.ToJsonString(JsonDefaults.Compact).Length}"
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

        var exit = SemanticRolePackageCli.Run(
            ["--request", "-"],
            new StringReader(
                "{\"local_path\":\"Z:\\\\missing.docx\",\"infer_everything\":true}"
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
        $"wordtoolkit-semantic-role-{Guid.NewGuid():N}.docx"
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
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>
                """
            );
            Add(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """
            );
            Add(
                archive,
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                <w:p><w:r><w:t>Theorem 1. Secret lexical body</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Theorem"/></w:pPr><w:r><w:t>Secret styled body</w:t></w:r></w:p>
                <w:sdt><w:sdtPr><w:tag w:val="wordtoolkit:role=theorem"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Secret declared body</w:t></w:r></w:p></w:sdtContent></w:sdt>
                <w:p><w:r><w:t>Ordinary body</w:t></w:r></w:p>
                </w:body></w:document>
                """
            );
            Add(
                archive,
                "word/_rels/document.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """
            );
            Add(
                archive,
                "word/styles.xml",
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Theorem"><w:name w:val="Twierdzenie"/></w:style></w:styles>
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
                "Saved-package semantic-role inspection must not invoke Word COM."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
