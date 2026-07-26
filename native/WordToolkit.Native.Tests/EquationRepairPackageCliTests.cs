using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class EquationRepairPackageCliTests
{
    [Fact]
    public void InstalledValidatorInspectsPlansAndAppliesExactDuplicateRepair()
    {
        var directory = CreateTemporaryDirectory();
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
                include_source = true,
                max_items = 20,
            });

            var exit = EquationRepairPackageCli.Run(
                ["--mode", "inspect", "--request", "-", "--format", "json"],
                new StringReader(inspectRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var inspectJson = JsonDocument.Parse(output.ToString());
            var candidates = inspectJson.RootElement.GetProperty("candidates")
                .EnumerateArray()
                .Select(candidate => new
                {
                    repair_kind = candidate.GetProperty("repair_kind").GetString(),
                    candidate_id = candidate.GetProperty("id").GetString(),
                    expected_candidate_fingerprint = candidate.GetProperty("fingerprint")
                        .GetString(),
                })
                .ToArray();
            Assert.Equal(2, candidates.Length);
            Assert.False(inspectJson.RootElement.GetProperty("raw_omml_returned").GetBoolean());

            var planRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = candidates,
                include_details = true,
            });
            output.GetStringBuilder().Clear();
            exit = EquationRepairPackageCli.Run(
                ["--mode", "plan", "--request", "-", "--format", "json"],
                new StringReader(planRequest),
                output,
                error
            );

            Assert.Equal(0, exit);
            using var planJson = JsonDocument.Parse(output.ToString());
            Assert.True(planJson.RootElement.GetProperty("can_apply").GetBoolean());
            Assert.True(planJson.RootElement.GetProperty("microsoft_schema_errors_reduced")
                .GetBoolean());
            var planId = planJson.RootElement.GetProperty("plan_id").GetString()!;
            var resultFingerprint = planJson.RootElement
                .GetProperty("result_package_fingerprint").GetString();

            var applyRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands = candidates,
                keep_backup = true,
            });
            output.GetStringBuilder().Clear();
            exit = EquationRepairPackageCli.Run(
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
            Assert.True(applyJson.RootElement.GetProperty("mutation_performed").GetBoolean());
            var backupPath = applyJson.RootElement.GetProperty("backup_path").GetString();
            Assert.NotNull(backupPath);
            Assert.Equal(before.Fingerprint, new OpcPackageReader().Read(backupPath!).Fingerprint);
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
        var exit = EquationRepairPackageCli.Run(
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
    [InlineData("inspect_ooxml_equation_repairs")]
    [InlineData("plan_ooxml_equation_repair")]
    [InlineData("apply_ooxml_equation_repair")]
    public async Task McpEnvelopeConformsToPublishedClosedOutputSchema(string action)
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "mcp.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var operation = new EquationRepairWordPackageOperation(
                NativeExtensionHost.CandidateValidator
            );
            var inspected = operation.Inspect(new EquationRepairInspectionRequest(
                path,
                package.Fingerprint,
                MaxItems: 20
            ));
            var commands = inspected.Candidates.Select(candidate =>
                new EquationRepairCommandRequest(
                    candidate.RepairKind,
                    candidate.Id,
                    candidate.Fingerprint
                )
            ).ToArray();
            var arguments = new JsonObject
            {
                ["local_path"] = path,
                ["expected_package_fingerprint"] = package.Fingerprint,
            };
            if (action != "inspect_ooxml_equation_repairs")
            {
                arguments["commands"] = new JsonArray(commands.Select(command =>
                    (JsonNode)new JsonObject
                    {
                        ["repair_kind"] = command.RepairKind,
                        ["candidate_id"] = command.CandidateId,
                        ["expected_candidate_fingerprint"] =
                            command.ExpectedCandidateFingerprint,
                    }
                ).ToArray());
            }
            if (action == "plan_ooxml_equation_repair")
            {
                arguments["include_details"] = true;
            }
            if (action == "apply_ooxml_equation_repair")
            {
                var plan = operation.Plan(new EquationRepairPlanRequest(
                    path,
                    package.Fingerprint,
                    commands
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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-equation-repair-cli-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] BuildPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """
            );
            Add(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """
            );
            Add(
                archive,
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
                  <w:body><w:p><m:oMath><m:f>
                    <m:fPr><m:type m:val="bar"/></m:fPr>
                    <m:fPr><m:type m:val="bar"/></m:fPr>
                    <m:num><m:r><m:rPr><m:sty m:val="b"/><m:sty m:val="b"/></m:rPr><m:t>x</m:t></m:r></m:num>
                    <m:den><m:r><m:t>2</m:t></m:r></m:den>
                  </m:f></m:oMath></w:p></w:body>
                </w:document>
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
                "Saved-package OfficeMath repair must not invoke Word COM."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
