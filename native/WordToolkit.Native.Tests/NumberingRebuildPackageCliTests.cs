using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class NumberingRebuildPackageCliTests
{
    [Fact]
    public void CliInspectsPlansAndAppliesTheSameStrictRebuildContract()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-rebuild-cli-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var before = new OpcPackageReader().Read(path);
            var paragraphs = new WordSemanticProjector().Project(before).Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ToArray();
            var inspectRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                paragraph_node_ids = paragraphs.Select(item => item.Id.Value).ToArray(),
            });
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = NumberingRebuildPackageCli.Run(
                ["--mode", "inspect", "--request", "-", "--format", "json"],
                new StringReader(inspectRequest),
                output,
                error
            );
            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var inspectJson = JsonDocument.Parse(output.ToString());
            var candidates = inspectJson.RootElement.GetProperty("candidates")
                .EnumerateArray().ToArray();
            Assert.Equal(2, candidates.Length);
            Assert.All(candidates, item => Assert.True(item.GetProperty("can_rebuild").GetBoolean()));

            var commands = new[]
            {
                new
                {
                    command_id = "cli-list",
                    multi_level_kind = "single_level",
                    restart_after_section_break = true,
                    levels = new[]
                    {
                        new
                        {
                            level_index = 0,
                            start_value = 4,
                            number_format = "decimal",
                            level_text = "%1.",
                        },
                    },
                    targets = paragraphs.Select((paragraph, index) => new
                    {
                        paragraph_node_id = paragraph.Id.Value,
                        expected_candidate_fingerprint = candidates[index]
                            .GetProperty("candidate_fingerprint").GetString(),
                        level_index = 0,
                    }).ToArray(),
                },
            };
            var planRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands,
                include_details = true,
            });
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            exit = NumberingRebuildPackageCli.Run(
                ["--mode", "plan", "--request", "-", "--format", "json"],
                new StringReader(planRequest),
                output,
                error
            );
            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var planJson = JsonDocument.Parse(output.ToString());
            var planId = planJson.RootElement.GetProperty("plan_id").GetString()!;
            Assert.True(planJson.RootElement.GetProperty("can_apply").GetBoolean());
            Assert.True(planJson.RootElement.GetProperty("numbering_part_created").GetBoolean());
            Assert.False(planJson.RootElement.GetProperty("paragraph_text_returned").GetBoolean());

            var applyRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands,
                keep_backup = false,
            });
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            exit = NumberingRebuildPackageCli.Run(
                ["--mode", "apply", "--request", "-", "--format", "json"],
                new StringReader(applyRequest),
                output,
                error
            );
            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var applyJson = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                planJson.RootElement.GetProperty("result_package_fingerprint").GetString(),
                applyJson.RootElement.GetProperty("package_fingerprint").GetString()
            );
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task McpPlanEnvelopeConformsToPublishedClosedSchemaWithoutWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-rebuild-mcp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var paragraphs = new WordSemanticProjector().Project(package).Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ToArray();
            var operation = new WordToolkit.Engine.Operations.NumberingRebuildWordPackageOperation();
            var candidates = operation.Inspect(
                new WordToolkit.Engine.Operations.NumberingRebuildInspectRequest(
                    path,
                    package.Fingerprint,
                    paragraphs.Select(item => item.Id.Value).ToArray()
                )
            ).Candidates;
            var command = new JsonObject
            {
                ["command_id"] = "mcp-list",
                ["multi_level_kind"] = "single_level",
                ["restart_after_section_break"] = false,
                ["levels"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["level_index"] = 0,
                        ["start_value"] = 1,
                        ["number_format"] = "decimal",
                        ["level_text"] = "%1.",
                    },
                },
                ["targets"] = new JsonArray(paragraphs.Select((paragraph, index) =>
                    (JsonNode)new JsonObject
                    {
                        ["paragraph_node_id"] = paragraph.Id.Value,
                        ["expected_candidate_fingerprint"] = candidates[index]
                            .CandidateFingerprint,
                        ["level_index"] = 0,
                    }
                ).ToArray()),
            };
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "plan_ooxml_numbering_rebuild",
                    ["arguments"] = new JsonObject
                    {
                        ["local_path"] = path,
                        ["expected_package_fingerprint"] = package.Fingerprint,
                        ["commands"] = new JsonArray(command),
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
            var schema = catalog.InspectAction("plan_ooxml_numbering_rebuild")["tool"]![
                "outputSchema"
            ]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            Assert.True(structured["ok"]!.GetValue<bool>());
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliRejectsUnknownFieldsBeforeFilesystemAccess()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = NumberingRebuildPackageCli.Run(
            ["--mode", "plan", "--request", "-"],
            new StringReader(
                """
                {"local_path":"Z:\\missing.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","commands":[],"unknown":true}
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

    private static byte[] BuildPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(archive, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(archive, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>one</w:t></w:r></w:p><w:p><w:r><w:t>two</w:t></w:r></w:p></w:body></w:document>");
            Add(archive, "word/_rels/document.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");
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
                "Saved-package numbering reconstruction must not invoke Word COM."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
