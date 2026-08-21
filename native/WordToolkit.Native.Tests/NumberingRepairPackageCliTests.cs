using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class NumberingRepairPackageCliTests
{
    [Fact]
    public void CliPlansAndAppliesTheSameStrictNumberingContract()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-repair-cli-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var before = new OpcPackageReader().Read(path);
            var target = new WordSemanticProjector().Project(before).Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ElementAt(1);
            var planRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                target_paragraph_node_id = target.Id.Value,
                expected_number_id = 5,
                expected_level_index = 0,
                start_value = 3,
                include_details = false,
            });
            var output = new StringWriter();
            var error = new StringWriter();

            var exit = NumberingRepairPackageCli.Run(
                ["--mode", "plan", "--request", "-", "--format", "json"],
                new StringReader(planRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var planJson = JsonDocument.Parse(output.ToString());
            var plan = planJson.RootElement;
            var planId = plan.GetProperty("plan_id").GetString()!;
            Assert.Equal(
                "wordtoolkit.plan_ooxml_numbering_repair/1.0",
                plan.GetProperty("operation_contract").GetString()
            );
            Assert.False(plan.GetProperty("paragraph_text_returned").GetBoolean());
            Assert.False(plan.TryGetProperty("affected_paragraphs", out _));

            var applyRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                target_paragraph_node_id = target.Id.Value,
                expected_number_id = 5,
                expected_level_index = 0,
                start_value = 3,
                keep_backup = false,
            });
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            exit = NumberingRepairPackageCli.Run(
                ["--mode", "apply", "--request", "-", "--format", "json"],
                new StringReader(applyRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var applyJson = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                plan.GetProperty("result_package_fingerprint").GetString(),
                applyJson.RootElement.GetProperty("package_fingerprint").GetString()
            );
            Assert.False(applyJson.RootElement.GetProperty("paragraph_text_returned")
                .GetBoolean());
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliRejectsUnknownJsonFieldsBeforeFilesystemAccess()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = NumberingRepairPackageCli.Run(
            ["--mode", "plan", "--request", "-"],
            new StringReader(
                """
                {"local_path":"Z:\\missing.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","target_paragraph_node_id":"wdn_abcde","expected_number_id":5,"expected_level_index":0,"start_value":1,"unknown":true}
                """
            ),
            output,
            error
        );

        Assert.Equal(64, exit);
        Assert.Equal(string.Empty, output.ToString());
        using var json = JsonDocument.Parse(error.ToString());
        Assert.Equal("INVALID_INPUT", json.RootElement.GetProperty("error")
            .GetProperty("code").GetString());
    }

    [Fact]
    public async Task McpPlanEnvelopeConformsToItsPublishedClosedOutputSchema()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-repair-mcp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var target = new WordSemanticProjector().Project(package).Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ElementAt(1);
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "plan_ooxml_numbering_repair",
                    ["arguments"] = new JsonObject
                    {
                        ["local_path"] = path,
                        ["expected_package_fingerprint"] = package.Fingerprint,
                        ["target_paragraph_node_id"] = target.Id.Value,
                        ["expected_number_id"] = 5,
                        ["expected_level_index"] = 0,
                        ["start_value"] = 3,
                        ["include_details"] = true,
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
            var schema = catalog.InspectAction("plan_ooxml_numbering_repair")["tool"]![
                "outputSchema"
            ]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(
                structured,
                schema,
                schema
            );
            Assert.True(structured["ok"]!.GetValue<bool>());
            Assert.True(
                structured["data"]!["affected_paragraph_details_truncated"] is JsonValue
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] BuildPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/></Types>
                """);
            Add(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Add(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>one</w:t></w:r></w:p>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>two</w:t></w:r></w:p>
                </w:body></w:document>
                """);
            Add(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/></Relationships>
                """);
            Add(archive, "word/numbering.xml", """
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum><w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num></w:numbering>
                """);
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
                "Saved-package numbering repairs must not invoke the Word COM host."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
