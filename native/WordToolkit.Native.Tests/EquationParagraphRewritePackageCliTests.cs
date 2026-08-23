using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class EquationParagraphRewritePackageCliTests
{
    private const string Word =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Math =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";

    [Fact]
    public void CliSharesInspectPlanApplyContractWithNativeOperation()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "cli.docx");
            CreatePackage(path);
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var candidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                package,
                semantic
            ).Candidates.Single();

            var inspectRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                paragraph_node_id = candidate.ParagraphNodeId.Value,
                include_text = true,
            });
            var inspectOutput = new StringWriter();
            Assert.Equal(0, EquationParagraphRewritePackageCli.Run(
                ["--mode", "inspect", "--request", "-", "--format", "json"],
                new StringReader(inspectRequest),
                inspectOutput,
                new StringWriter()
            ));
            using (var inspection = JsonDocument.Parse(inspectOutput.ToString()))
            {
                Assert.Equal(
                    EquationParagraphRewriteWordPackageContract.InspectContract,
                    inspection.RootElement.GetProperty("operation_contract").GetString()
                );
                Assert.True(inspection.RootElement.GetProperty("text_included").GetBoolean());
            }

            var command = new
            {
                type = "rewrite_equation_paragraph_text",
                candidate_id = candidate.Id,
                expected_candidate_fingerprint = candidate.Fingerprint,
                replacement_text_slots = new[] { "Changed ", " tail" },
            };
            var planRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = new[] { command },
            });
            var planOutput = new StringWriter();
            Assert.Equal(0, EquationParagraphRewritePackageCli.Run(
                ["--mode", "plan", "--request", "-"],
                new StringReader(planRequest),
                planOutput,
                new StringWriter()
            ));
            string planId;
            using (var plan = JsonDocument.Parse(planOutput.ToString()))
            {
                Assert.Equal(
                    EquationParagraphRewriteWordPackageContract.PlanContract,
                    plan.RootElement.GetProperty("operation_contract").GetString()
                );
                Assert.True(plan.RootElement.GetProperty("can_apply").GetBoolean());
                planId = plan.RootElement.GetProperty("plan_id").GetString()!;
            }

            var applyRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = planId,
                commands = new[] { command },
                keep_backup = true,
            });
            var applyOutput = new StringWriter();
            Assert.Equal(0, EquationParagraphRewritePackageCli.Run(
                ["--mode", "apply", "--request", "-"],
                new StringReader(applyRequest),
                applyOutput,
                new StringWriter()
            ));
            using var applied = JsonDocument.Parse(applyOutput.ToString());
            Assert.Equal(
                EquationParagraphRewriteWordPackageContract.ApplyContract,
                applied.RootElement.GetProperty("operation_contract").GetString()
            );
            Assert.True(applied.RootElement.GetProperty("applied").GetBoolean());
            Assert.True(applied.RootElement.GetProperty(
                "exact_equation_bytes_preserved"
            ).GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CatalogKeepsEquationParagraphActionsLazyVersionedAndBounded()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.True(catalog.IsAction(
            EquationParagraphRewriteWordPackageContract.InspectOperationName
        ));
        Assert.True(catalog.IsAction(
            EquationParagraphRewriteWordPackageContract.PlanOperationName
        ));
        Assert.True(catalog.IsAction(
            EquationParagraphRewriteWordPackageContract.ApplyOperationName
        ));
        Assert.DoesNotContain(catalog.Tools, tool => tool!["name"]!.GetValue<string>()
            is "inspect_ooxml_equation_paragraph_rewrites"
                or "plan_ooxml_equation_paragraph_rewrites"
                or "apply_ooxml_equation_paragraph_rewrites");
        Assert.Equal(
            EquationParagraphRewriteWordPackageContract.InspectContract,
            catalog.InspectAction(
                EquationParagraphRewriteWordPackageContract.InspectOperationName
            )["tool"]!["outputSchema"]!["properties"]!["data"]!["properties"]![
                "operation_contract"
            ]!["const"]!.GetValue<string>()
        );
        var planSchema = catalog.InspectAction(
            EquationParagraphRewriteWordPackageContract.PlanOperationName
        ).ToJsonString();
        Assert.True(planSchema.Length < 12_000, planSchema.Length.ToString());
        Assert.DoesNotContain("raw_xml", catalog.InspectAction(
            EquationParagraphRewriteWordPackageContract.PlanOperationName
        )["tool"]!["inputSchema"]!.ToJsonString(), StringComparison.Ordinal);
        var applySchema = catalog.InspectAction(
            EquationParagraphRewriteWordPackageContract.ApplyOperationName
        )["tool"]!;
        Assert.Equal(
            "^weprplan_[A-Za-z0-9_-]+$",
            applySchema["inputSchema"]!["properties"]!["protected_edit_authorization"]!["pattern"]!.GetValue<string>()
        );
        Assert.Contains(
            "explicit_authorizations",
            applySchema["outputSchema"]!["properties"]!["data"]!["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
    }

    [Fact]
    public void CatalogPublishesClosedProtectionContractsForTypedPackageMutators()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var actions = new[]
        {
            (NumberingRepairWordPackageContract.PlanOperationName, NumberingRepairWordPackageContract.ApplyOperationName, "^wnrplan_[A-Za-z0-9_-]+$"),
            (NumberingRebuildWordPackageContract.PlanOperationName, NumberingRebuildWordPackageContract.ApplyOperationName, "^wnrbplan_[A-Za-z0-9_-]+$"),
            (StyleWordPackageContract.PlanOperationName, StyleWordPackageContract.ApplyOperationName, "^wseplan_[A-Za-z0-9_-]+$"),
            (TemplateStyleAlignmentWordPackageContract.PlanOperationName, TemplateStyleAlignmentWordPackageContract.ApplyOperationName, "^wtsaplan_[A-Za-z0-9_-]+$"),
            (EquationParagraphRewriteWordPackageContract.PlanOperationName, EquationParagraphRewriteWordPackageContract.ApplyOperationName, "^weprplan_[A-Za-z0-9_-]+$"),
        };

        foreach (var (planName, applyName, planPattern) in actions)
        {
            var planOutput = catalog.InspectAction(planName)["tool"]!["outputSchema"]!;
            var planData = planOutput["properties"]!["data"]!;
            var required = planData["required"]!.AsArray()
                .Select(item => item!.GetValue<string>())
                .ToArray();
            Assert.Contains("protection", required);
            Assert.Contains("required_authorizations", required);
            Assert.DoesNotContain("protection_authorization_id", required);
            Assert.Equal(
                planPattern,
                planData["properties"]!["protection_authorization_id"]!["pattern"]!
                    .GetValue<string>()
            );
            var protection = planData["properties"]!["protection"]!;
            if (protection["$ref"] is not null)
            {
                protection = planOutput["$defs"]!["protection"]!;
            }
            Assert.False(protection["additionalProperties"]!.GetValue<bool>());
            Assert.Equal(
                250_000,
                protection["properties"]!["base_permission_range_count"]!["maximum"]!
                    .GetValue<int>()
            );
            Assert.Equal(
                500_000,
                protection["properties"]!["malformed_permission_range_count"]!["maximum"]!
                    .GetValue<int>()
            );
            Assert.Contains(
                "readOnly",
                protection["properties"]!["base_document_protection_edit_mode"]!["enum"]!
                    .AsArray()
                    .Select(item => item!.GetValue<string>())
            );

            var apply = catalog.InspectAction(applyName)["tool"]!;
            Assert.Equal(
                planPattern,
                apply["inputSchema"]!["properties"]!["protected_edit_authorization"]![
                    "pattern"
                ]!.GetValue<string>()
            );
            Assert.Contains(
                "explicit_authorizations",
                apply["outputSchema"]!["properties"]!["data"]!["required"]!
                    .AsArray()
                    .Select(item => item!.GetValue<string>())
            );
        }
    }

    [Fact]
    public async Task McpAdapterUsesTheSameSavedPackageEngineWithoutInvokingWord()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mcp.docx");
            CreatePackage(path);
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var candidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                package,
                semantic
            ).Candidates.Single();
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = new[]
                {
                    new
                    {
                        type = "rewrite_equation_paragraph_text",
                        candidate_id = candidate.Id,
                        expected_candidate_fingerprint = candidate.Fingerprint,
                        replacement_text_slots = new[] { "MCP before ", " MCP after" },
                    },
                },
            }));
            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                EquationParagraphRewriteWordPackageContract.PlanOperationName,
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(
                result,
                JsonDefaults.Compact
            ));
            Assert.Equal(
                EquationParagraphRewriteWordPackageContract.PlanContract,
                json.RootElement.GetProperty("operation_contract").GetString()
            );
            Assert.Equal("dotnet-native", json.RootElement.GetProperty("runtime").GetString());
            Assert.False(json.RootElement.GetProperty("python_used").GetBoolean());
            Assert.True(json.RootElement.GetProperty(
                "exact_equation_bytes_preserved"
            ).GetBoolean());
            Assert.False(json.RootElement.GetProperty("raw_text_returned").GetBoolean());
            Assert.False(json.RootElement.GetProperty("mutation_performed").GetBoolean());
            Assert.Equal(package.Fingerprint, new OpcPackageReader().Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
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
        WriteEntry(
            archive,
            "_rels/.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{WordPackageConformance.TransitionalOfficeDocumentRelationship}" Target="word/document.xml"/>
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            $"""
            <w:document xmlns:w="{Word}" xmlns:m="{Math}"><w:body>
              <w:p><w:r><w:t xml:space="preserve">Before </w:t></w:r><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath><w:r><w:t>after</w:t></w:r></w:p>
            </w:body></w:document>
            """
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-equation-paragraph-cli-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        ) => throw new Xunit.Sdk.XunitException(
            "Saved-package equation paragraph rewrites must not invoke Word COM."
        );

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
