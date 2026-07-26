using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class TemplateStyleAlignmentPackageCliTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void CliInspectsPlansAndAppliesReviewedStyleClosure()
    {
        var directory = TemporaryDirectory();
        var targetPath = Path.Combine(directory, "target.docx");
        var templatePath = Path.Combine(directory, "template.docx");
        try
        {
            File.WriteAllBytes(targetPath, BuildPackage(
                Style("Normal", string.Empty),
                Style("Base", "<w:rPr><w:b w:val=\"0\"/></w:rPr>"),
                Style("Heading", "<w:basedOn w:val=\"Base\"/><w:rPr><w:i/></w:rPr>")
            ));
            File.WriteAllBytes(templatePath, BuildPackage(
                Style("Normal", string.Empty),
                Style("Base", "<w:rPr><w:b/></w:rPr>"),
                Style("Heading", "<w:basedOn w:val=\"Base\"/><w:rPr><w:i/></w:rPr>")
            ));
            var reader = new OpcPackageReader();
            var target = reader.Read(targetPath);
            var template = reader.Read(templatePath);
            var templateBytes = File.ReadAllBytes(templatePath);
            var output = new StringWriter();
            var error = new StringWriter();
            var inspectRequest = JsonSerializer.Serialize(new
            {
                target_path = targetPath,
                template_path = templatePath,
                expected_target_package_fingerprint = target.Fingerprint,
                expected_template_package_fingerprint = template.Fingerprint,
                include_dependencies = true,
            });

            var exit = TemplateStyleAlignmentPackageCli.Run(
                ["--mode", "inspect", "--request", "-", "--format", "json"],
                new StringReader(inspectRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var inspectJson = JsonDocument.Parse(output.ToString());
            var candidate = inspectJson.RootElement.GetProperty("candidates")
                .EnumerateArray().Single(item =>
                    item.GetProperty("style_id").GetString() == "Heading"
                );
            var commands = new[]
            {
                new
                {
                    candidate_id = candidate.GetProperty("id").GetString(),
                    expected_candidate_fingerprint = candidate.GetProperty("fingerprint")
                        .GetString(),
                },
            };
            Assert.Contains("Base", candidate.GetProperty("dependency_style_ids")
                .EnumerateArray().Select(item => item.GetString()));

            var planRequest = JsonSerializer.Serialize(new
            {
                target_path = targetPath,
                template_path = templatePath,
                expected_target_package_fingerprint = target.Fingerprint,
                expected_template_package_fingerprint = template.Fingerprint,
                commands,
                include_details = true,
            });
            output.GetStringBuilder().Clear();
            exit = TemplateStyleAlignmentPackageCli.Run(
                ["--mode", "plan", "--request", "-", "--format", "json"],
                new StringReader(planRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            using var planJson = JsonDocument.Parse(output.ToString());
            Assert.True(planJson.RootElement.GetProperty("can_apply").GetBoolean());
            Assert.True(planJson.RootElement.GetProperty("candidate_validation")
                .GetProperty("no_new_errors").GetBoolean());
            var planId = planJson.RootElement.GetProperty("plan_id").GetString();
            var resultFingerprint = planJson.RootElement
                .GetProperty("result_package_fingerprint").GetString();

            var applyRequest = JsonSerializer.Serialize(new
            {
                target_path = targetPath,
                template_path = templatePath,
                expected_target_package_fingerprint = target.Fingerprint,
                expected_template_package_fingerprint = template.Fingerprint,
                expected_plan_id = planId,
                commands,
                keep_backup = true,
            });
            output.GetStringBuilder().Clear();
            exit = TemplateStyleAlignmentPackageCli.Run(
                ["--mode", "apply", "--request", "-", "--format", "json"],
                new StringReader(applyRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            using var applyJson = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                resultFingerprint,
                applyJson.RootElement.GetProperty("package_fingerprint").GetString()
            );
            var backupPath = applyJson.RootElement.GetProperty("backup_path").GetString();
            Assert.NotNull(backupPath);
            Assert.Equal(target.Fingerprint, reader.Read(backupPath!).Fingerprint);
            Assert.Equal(templateBytes, File.ReadAllBytes(templatePath));
            Assert.False(applyJson.RootElement.GetProperty("template_attached").GetBoolean());
            Assert.False(applyJson.RootElement.GetProperty("template_mutation_performed")
                .GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(TemplateStyleAlignmentWordPackageContract.InspectOperationName)]
    [InlineData(TemplateStyleAlignmentWordPackageContract.PlanOperationName)]
    [InlineData(TemplateStyleAlignmentWordPackageContract.ApplyOperationName)]
    public async Task McpEnvelopeConformsToPublishedClosedSchemaWithoutCom(string action)
    {
        var directory = TemporaryDirectory();
        var targetPath = Path.Combine(directory, "target.docx");
        var templatePath = Path.Combine(directory, "template.docx");
        try
        {
            File.WriteAllBytes(targetPath, BuildPackage(
                Style("Normal", string.Empty),
                Style("Focus", "<w:rPr><w:b w:val=\"0\"/></w:rPr>")
            ));
            File.WriteAllBytes(templatePath, BuildPackage(
                Style("Normal", string.Empty),
                Style("Focus", "<w:rPr><w:b/></w:rPr>")
            ));
            var reader = new OpcPackageReader();
            var target = reader.Read(targetPath);
            var template = reader.Read(templatePath);
            var operation = new TemplateStyleAlignmentWordPackageOperation(
                NativeExtensionHost.CandidateValidator
            );
            var inspected = operation.Inspect(new TemplateStyleAlignmentInspectRequest(
                targetPath,
                templatePath,
                target.Fingerprint,
                template.Fingerprint,
                IncludeDependencies: true
            ));
            var candidate = Assert.Single(inspected.Candidates, item =>
                item.StyleId == "Focus"
            );
            var command = new TemplateStyleAlignmentCommandRequest(
                candidate.Id,
                candidate.Fingerprint
            );
            var arguments = new JsonObject
            {
                ["target_path"] = targetPath,
                ["template_path"] = templatePath,
                ["expected_target_package_fingerprint"] = target.Fingerprint,
                ["expected_template_package_fingerprint"] = template.Fingerprint,
            };
            if (action == TemplateStyleAlignmentWordPackageContract.InspectOperationName)
            {
                arguments["include_dependencies"] = true;
            }
            else
            {
                arguments["commands"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["candidate_id"] = command.CandidateId,
                        ["expected_candidate_fingerprint"] =
                            command.ExpectedCandidateFingerprint,
                    },
                };
            }
            if (action == TemplateStyleAlignmentWordPackageContract.PlanOperationName)
            {
                arguments["include_details"] = true;
            }
            if (action == TemplateStyleAlignmentWordPackageContract.ApplyOperationName)
            {
                var plan = operation.Plan(new TemplateStyleAlignmentPlanRequest(
                    targetPath,
                    templatePath,
                    target.Fingerprint,
                    template.Fingerprint,
                    [command]
                ));
                arguments["expected_plan_id"] = plan.PlanId;
                arguments["keep_backup"] = false;
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
                new StringReader(
                    request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine
                ),
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

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-template-style-cli-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Style(string id, string body) =>
        $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{id}\"/>{body}</w:style>";

    private static byte[] BuildPackage(params string[] styles)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
            Add(archive, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rDoc\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(archive, "word/_rels/document.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add(archive, "word/document.xml", $"<w:document xmlns:w=\"{WordNamespace}\"><w:body><w:p><w:pPr><w:pStyle w:val=\"Normal\"/></w:pPr><w:r><w:t>content</w:t></w:r></w:p><w:sectPr/></w:body></w:document>");
            Add(archive, "word/styles.xml", $"<w:styles xmlns:w=\"{WordNamespace}\">{string.Concat(styles)}</w:styles>");
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
                "Offline template style alignment must not invoke Word COM."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
