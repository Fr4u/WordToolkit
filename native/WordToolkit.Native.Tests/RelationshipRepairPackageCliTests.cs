using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RelationshipRepairPackageCliTests
{
    [Fact]
    public void CliInspectsPlansAndAppliesTheSameStrictRelationshipContract()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-relationship-repair-cli-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
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
                include_details = true,
            });

            var exit = RelationshipRepairPackageCli.Run(
                ["--mode", "inspect", "--request", "-", "--format", "json"],
                new StringReader(inspectRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var inspectJson = JsonDocument.Parse(output.ToString());
            var relationship = inspectJson.RootElement.GetProperty("relationships")[0];
            var orphan = inspectJson.RootElement.GetProperty("orphan_relationship_parts")[0];
            Assert.False(inspectJson.RootElement.GetProperty("external_targets_returned")
                .GetBoolean());
            Assert.False(relationship.TryGetProperty("target", out _));
            var commands = new object[]
            {
                new
                {
                    kind = "remove_unreferenced_relationship",
                    source_part_uri = relationship.GetProperty("source_part_uri").GetString(),
                    relationship_id = relationship.GetProperty("relationship_id").GetString(),
                    expected_relationship_fingerprint = relationship.GetProperty("fingerprint").GetString(),
                },
                new
                {
                    kind = "remove_orphan_relationship_part",
                    relationship_part_uri = orphan.GetProperty("relationship_part_uri").GetString(),
                    expected_entry_sha256 = orphan.GetProperty("entry_sha256").GetString(),
                },
            };
            var planRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands,
            });
            output.GetStringBuilder().Clear();
            exit = RelationshipRepairPackageCli.Run(
                ["--mode", "plan", "--request", "-", "--format", "json"],
                new StringReader(planRequest),
                output,
                error
            );
            Assert.Equal(0, exit);
            using var planJson = JsonDocument.Parse(output.ToString());
            var planId = planJson.RootElement.GetProperty("plan_id").GetString()!;
            var resultFingerprint = planJson.RootElement
                .GetProperty("result_package_fingerprint").GetString();
            Assert.True(planJson.RootElement
                .GetProperty("requires_external_relationship_authorization").GetBoolean());
            Assert.False(planJson.RootElement.TryGetProperty("actions", out _));

            var applyRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands,
                allow_external_relationship_removal = true,
                keep_backup = false,
            });
            output.GetStringBuilder().Clear();
            exit = RelationshipRepairPackageCli.Run(
                ["--mode", "apply", "--request", "-", "--format", "json"],
                new StringReader(applyRequest),
                output,
                error
            );
            Assert.Equal(0, exit);
            using var appliedJson = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                resultFingerprint,
                appliedJson.RootElement.GetProperty("package_fingerprint").GetString()
            );
            Assert.False(appliedJson.RootElement.GetProperty("external_targets_returned")
                .GetBoolean());
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task McpInspectionEnvelopeConformsToPublishedClosedOutputSchemaWithoutCom()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-relationship-repair-mcp-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "inspect_ooxml_relationships",
                    ["arguments"] = new JsonObject
                    {
                        ["local_path"] = path,
                        ["expected_package_fingerprint"] = package.Fingerprint,
                        ["include_all"] = true,
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
            var schema = catalog.InspectAction("inspect_ooxml_relationships")["tool"]![
                "outputSchema"
            ]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            Assert.True(structured["ok"]!.GetValue<bool>());
            Assert.False(structured["data"]!["external_targets_returned"]!.GetValue<bool>());
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
        var exit = RelationshipRepairPackageCli.Run(
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
        using var json = JsonDocument.Parse(error.ToString());
        Assert.Equal(
            "INVALID_INPUT",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    private static byte[] BuildPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            Add(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Add(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body><w:p><w:r><w:drawing r:embed="rIdImage"/></w:r><w:r><w:t>tekst</w:t></w:r></w:p></w:body></w:document>
                """);
            Add(archive, "word/media/image1.png", "not really png");
            Add(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/><Relationship Id="rIdDeadLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://secret.example.invalid/private" TargetMode="External"/></Relationships>
                """);
            Add(archive, "word/_rels/missing.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdOrphan" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/></Relationships>
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
                "Saved-package relationship operations must not invoke Word COM."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
