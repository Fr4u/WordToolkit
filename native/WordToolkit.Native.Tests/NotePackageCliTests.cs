using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class NotePackageCliTests
{
    [Fact]
    public void CliInspectsPlansAndAppliesTheSameStrictNoteContract()
    {
        var directory = CreateTemporaryDirectory("cli");
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var before = new OpcPackageReader().Read(path);
            var output = new StringWriter();
            var error = new StringWriter();
            var inspectRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
            });

            var exit = NotePackageCli.Run(
                ["--mode", "inspect", "--request", "-", "--format", "json"],
                new StringReader(inspectRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var inspectJson = JsonDocument.Parse(output.ToString());
            var definition = inspectJson.RootElement.GetProperty("definitions")[0];
            var definitionId = definition.GetProperty("id").GetString()!;
            var definitionFingerprint = definition.GetProperty("fingerprint").GetString()!;
            Assert.False(inspectJson.RootElement.GetProperty("raw_xml_returned").GetBoolean());

            var planRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                repair_kind = "remove_empty_orphan_definition",
                definition_id = definitionId,
                expected_definition_fingerprint = definitionFingerprint,
                include_details = true,
            });
            output.GetStringBuilder().Clear();
            exit = NotePackageCli.Run(
                ["--mode", "plan", "--request", "-", "--format", "json"],
                new StringReader(planRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            using var planJson = JsonDocument.Parse(output.ToString());
            var planId = planJson.RootElement.GetProperty("plan_id").GetString()!;
            Assert.True(planJson.RootElement.GetProperty("can_apply").GetBoolean());
            Assert.True(planJson.RootElement.GetProperty("engine_validation")
                .GetProperty("passed").GetBoolean());

            var applyRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                repair_kind = "remove_empty_orphan_definition",
                definition_id = definitionId,
                expected_definition_fingerprint = definitionFingerprint,
                keep_backup = false,
            });
            output.GetStringBuilder().Clear();
            exit = NotePackageCli.Run(
                ["--mode", "apply", "--request", "-", "--format", "json"],
                new StringReader(applyRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            using var applyJson = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                planJson.RootElement.GetProperty("result_package_fingerprint").GetString(),
                applyJson.RootElement.GetProperty("package_fingerprint").GetString()
            );
            Assert.True(applyJson.RootElement.GetProperty("mutation_performed").GetBoolean());
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
        var exit = NotePackageCli.Run(
            ["--mode", "inspect", "--request", "-"],
            new StringReader(
                """
                {"local_path":"Z:\\missing.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","unknown":true}
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

    [Theory]
    [InlineData("inspect_ooxml_notes")]
    [InlineData("plan_ooxml_note_repair")]
    public async Task McpEnvelopeConformsToPublishedClosedOutputSchema(string action)
    {
        var directory = CreateTemporaryDirectory("mcp");
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var operation = new WordToolkit.Engine.Operations.NoteWordPackageOperation();
            var definition = Assert.Single(operation.Inspect(
                new WordToolkit.Engine.Operations.NoteInspectionRequest(
                    path,
                    package.Fingerprint
                )
            ).Definitions);
            var arguments = new JsonObject
            {
                ["local_path"] = path,
                ["expected_package_fingerprint"] = package.Fingerprint,
            };
            if (action == "plan_ooxml_note_repair")
            {
                arguments["repair_kind"] = "remove_empty_orphan_definition";
                arguments["definition_id"] = definition.Id;
                arguments["expected_definition_fingerprint"] = definition.Fingerprint;
                arguments["include_details"] = true;
            }
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = action,
                    ["arguments"] = arguments,
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
            var schema = catalog.InspectAction(action)["tool"]!["outputSchema"]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            Assert.True(structured["ok"]!.GetValue<bool>());
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory(string suffix)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-note-{suffix}-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] BuildPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/></Types>
                """);
            Add(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Add(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Main</w:t></w:r></w:p></w:body></w:document>
                """);
            Add(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdFootnotes" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes" Target="footnotes.xml"/></Relationships>
                """);
            Add(archive, "word/footnotes.xml", """
                <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:footnote w:id="4"><w:p><w:r><w:footnoteRef/></w:r></w:p></w:footnote></w:footnotes>
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
                "Saved-package note operations must not invoke the Word COM host."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
