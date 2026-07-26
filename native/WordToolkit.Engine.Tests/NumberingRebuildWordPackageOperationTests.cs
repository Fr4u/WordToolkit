using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Engine.Tests;

public sealed class NumberingRebuildWordPackageOperationTests
{
    [Fact]
    public void InspectsPlansAndAtomicallyAppliesAReviewedReconstruction()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-rebuild-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var paragraphs = semantic.Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ToArray();
            var operation = new NumberingRebuildWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var inspection = operation.Inspect(new NumberingRebuildInspectRequest(
                path,
                package.Fingerprint,
                paragraphs.Select(paragraph => paragraph.Id.Value).ToArray()
            ));
            Assert.All(inspection.Candidates, candidate => Assert.True(candidate.CanRebuild));
            Assert.False(inspection.ParagraphTextReturned);
            Assert.False(inspection.RawXmlReturned);

            var commands = new[]
            {
                new WordNumberingRebuildCommand(
                    "api-list",
                    WordNumberingRebuildMultiLevelKind.SingleLevel,
                    true,
                    [new WordNumberingRebuildLevel(
                        0,
                        7,
                        WordNumberingRebuildFormat.Decimal,
                        "%1."
                    )],
                    paragraphs.Select((paragraph, index) =>
                        new WordNumberingRebuildTarget(
                            paragraph.Id,
                            inspection.Candidates[index].CandidateFingerprint,
                            0
                        )
                    ).ToArray()
                ),
            };
            var plan = operation.Plan(new NumberingRebuildPlanRequest(
                path,
                package.Fingerprint,
                commands,
                IncludeDetails: true
            ));
            Assert.True(
                plan.CanApply,
                JsonSerializer.Serialize(plan.CandidateValidation)
            );
            Assert.True(plan.NumberingPartCreated);
            Assert.Equal(2, plan.TargetCount);
            Assert.NotNull(plan.Commands.Single().Targets);
            Assert.NotNull(plan.ChangedEntries);

            var applied = operation.Apply(new NumberingRebuildApplyRequest(
                path,
                package.Fingerprint,
                plan.PlanId,
                commands,
                KeepBackup: true
            ));
            Assert.True(applied.Applied);
            Assert.Equal(plan.ResultPackageFingerprint, applied.PackageFingerprint);
            Assert.True(File.Exists(applied.BackupPath));
            Assert.Equal(
                plan.ResultPackageFingerprint,
                new OpcPackageReader().Read(path).Fingerprint
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JsonIsStrictAndApplyFailsClosedWithoutAValidator()
    {
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                NumberingRebuildOperationJson.ParsePlanRequest(
                    """
                    {"local_path":"a.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","commands":[],"surprise":true}
                    """
                )
            ).Code
        );
        var parsed = NumberingRebuildOperationJson.ParsePlanRequest(
            """
            {"local_path":"a.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","commands":[{"command_id":"x","multi_level_kind":"single_level","restart_after_section_break":false,"levels":[{"level_index":0,"start_value":1,"number_format":"decimal","level_text":"%1."}],"targets":[{"paragraph_node_id":"wdn_abc","expected_candidate_fingerprint":"wnrb_x","level_index":0}]}]}
            """
        );
        Assert.Equal(WordNumberingRebuildFormat.Decimal, parsed.Commands[0].Levels[0].NumberFormat);
        Assert.Equal(WordNumberingRebuildSuffix.Tab, parsed.Commands[0].Levels[0].Suffix);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-rebuild-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var paragraph = semantic.Nodes.First(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            );
            var operation = new NumberingRebuildWordPackageOperation();
            var inspected = operation.Inspect(new NumberingRebuildInspectRequest(
                path,
                package.Fingerprint,
                [paragraph.Id.Value]
            )).Candidates.Single();
            var command = new WordNumberingRebuildCommand(
                "no-validator",
                WordNumberingRebuildMultiLevelKind.SingleLevel,
                false,
                [new WordNumberingRebuildLevel(
                    0,
                    1,
                    WordNumberingRebuildFormat.Decimal,
                    "%1."
                )],
                [new WordNumberingRebuildTarget(
                    paragraph.Id,
                    inspected.CandidateFingerprint,
                    0
                )]
            );
            var plan = operation.Plan(new NumberingRebuildPlanRequest(
                path,
                package.Fingerprint,
                [command]
            ));
            Assert.True(plan.ApplyBlocked);
            Assert.Contains("schema_validator_unavailable", plan.ApplyBlockedReasons);
            Assert.Equal(
                "VALIDATOR_REQUIRED",
                Assert.Throws<WordToolkitOperationException>(() => operation.Apply(
                    new NumberingRebuildApplyRequest(
                        path,
                        package.Fingerprint,
                        plan.PlanId,
                        [command]
                    )
                )).Code
            );

            var passing = new NumberingRebuildWordPackageOperation(
                new PassingValidator()
            );
            var changedIntent = command with
            {
                Levels = [command.Levels[0] with { StartValue = 9 }],
            };
            Assert.Equal(
                "PLAN_MISMATCH",
                Assert.Throws<WordToolkitOperationException>(() => passing.Apply(
                    new NumberingRebuildApplyRequest(
                        path,
                        package.Fingerprint,
                        plan.PlanId,
                        [changedIntent]
                    )
                )).Code
            );
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
            Add(archive, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(archive, "_rels/.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(archive, "word/document.xml",
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>one</w:t></w:r></w:p><w:p><w:r><w:t>two</w:t></w:r></w:p></w:body></w:document>");
            Add(archive, "word/_rels/document.xml.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");
        }
        return stream.ToArray();
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }

    private sealed class PassingValidator : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        ) => new(
            Performed: true,
            CandidateValid: true,
            NoNewErrors: true,
            ErrorCount: 0,
            BaselineErrorCount: 0,
            CandidateErrorCount: 0,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues: Array.Empty<WordPackageValidationIssue>()
        );
    }
}
