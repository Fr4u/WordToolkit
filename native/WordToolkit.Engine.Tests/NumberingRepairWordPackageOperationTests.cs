using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Tests;

public sealed class NumberingRepairWordPackageOperationTests
{
    [Fact]
    public void PlansAndAppliesOneReviewedAtomicNumberingRepair()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-repair-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var target = semantic.Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ElementAt(1);
            var operation = new NumberingRepairWordPackageOperation(
                new PassingValidator()
            );
            var request = new NumberingRepairPlanRequest(
                path,
                package.Fingerprint,
                target.Id.Value,
                ExpectedNumberId: 5,
                ExpectedLevelIndex: 0,
                StartValue: 4,
                IncludeDetails: true
            );

            var plan = operation.Plan(request);

            Assert.True(plan.CanApply);
            Assert.False(plan.ApplyBlocked);
            Assert.Equal(2, plan.AffectedParagraphCount);
            Assert.False(plan.AffectedParagraphDetailsTruncated);
            Assert.Equal(6, plan.NewNumberId);
            Assert.Equal(4, plan.TargetCounterAfter);
            Assert.NotNull(plan.AffectedParagraphs);
            Assert.NotNull(plan.ChangedParts);
            Assert.False(plan.ParagraphTextReturned);
            Assert.False(plan.RawXmlReturned);

            var applied = operation.Apply(
                new NumberingRepairApplyRequest(
                    path,
                    package.Fingerprint,
                    plan.PlanId,
                    target.Id.Value,
                    5,
                    0,
                    4,
                    KeepBackup: true
                )
            );

            Assert.True(applied.Applied);
            Assert.Equal(plan.ResultPackageFingerprint, applied.PackageFingerprint);
            Assert.NotNull(applied.BackupPath);
            Assert.True(File.Exists(applied.BackupPath));
            Assert.False(applied.ParagraphTextReturned);
            Assert.False(applied.RawXmlReturned);
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
    public void JsonAndApplyFailClosedOnUnknownFieldsMissingValidatorAndPlanDrift()
    {
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                NumberingRepairOperationJson.ParsePlanRequest(
                    """
                    {"local_path":"a.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","target_paragraph_node_id":"wdn_abc","expected_number_id":5,"expected_level_index":0,"start_value":1,"surprise":true}
                    """
                )
            ).Code
        );

        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-repair-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var target = semantic.Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ElementAt(1);
            var noValidator = new NumberingRepairWordPackageOperation();
            var plan = noValidator.Plan(
                new NumberingRepairPlanRequest(
                    path,
                    package.Fingerprint,
                    target.Id.Value,
                    5,
                    0,
                    4
                )
            );
            Assert.True(plan.ApplyBlocked);
            Assert.Contains("schema_validator_unavailable", plan.ApplyBlockedReasons);
            Assert.Equal(
                "VALIDATOR_REQUIRED",
                Assert.Throws<WordToolkitOperationException>(() =>
                    noValidator.Apply(
                        new NumberingRepairApplyRequest(
                            path,
                            package.Fingerprint,
                            plan.PlanId,
                            target.Id.Value,
                            5,
                            0,
                            4
                        )
                    )
                ).Code
            );

            var passing = new NumberingRepairWordPackageOperation(new PassingValidator());
            Assert.Equal(
                "PLAN_MISMATCH",
                Assert.Throws<WordToolkitOperationException>(() =>
                    passing.Apply(
                        new NumberingRepairApplyRequest(
                            path,
                            package.Fingerprint,
                            plan.PlanId,
                            target.Id.Value,
                            5,
                            0,
                            9
                        )
                    )
                ).Code
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProtectedRepairRequiresExactAuthorizationAndMalformedIsByteExact()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "wt-num-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var malformed in new[] { false, true })
            {
                var path = Path.Combine(dir, malformed ? "bad.docx" : "good.docx");
                File.WriteAllBytes(
                    path,
                    BuildPackage(
                        malformed
                            ? "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\" w:bogus=\"x\"/>"
                            : "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\"/>"
                    )
                );
                var reader = new OpcPackageReader();
                var before = reader.Read(path);
                var semantic = new WordSemanticProjector().Project(before);
                var target = semantic.Nodes.Where(node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                ).ElementAt(1);
                var operation = new NumberingRepairWordPackageOperation(
                    new PassingValidator()
                );
                var plan = operation.Plan(new NumberingRepairPlanRequest(
                    path,
                    before.Fingerprint,
                    target.Id.Value,
                    5,
                    0,
                    4
                ));
                var bytes = File.ReadAllBytes(path);
                var denied = Assert.Throws<WordToolkitOperationException>(() =>
                    operation.Apply(new NumberingRepairApplyRequest(
                        path,
                        before.Fingerprint,
                        plan.PlanId,
                        target.Id.Value,
                        5,
                        0,
                        4,
                        ProtectedEditAuthorization: "wrong"
                    ))
                );
                Assert.Equal("EDIT_POLICY_BLOCKED", denied.Code);
                Assert.Equal(bytes, File.ReadAllBytes(path));

                if (malformed)
                {
                    Assert.Contains(
                        "protection_metadata_malformed",
                        plan.ApplyBlockedReasons
                    );
                    continue;
                }

                Assert.Equal(plan.PlanId, plan.ProtectionAuthorizationId);
                var applied = operation.Apply(new NumberingRepairApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    target.Id.Value,
                    5,
                    0,
                    4,
                    KeepBackup: false,
                    ProtectedEditAuthorization: plan.PlanId
                ));
                Assert.True(applied.Applied);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static byte[] BuildPackage(string? protectionXml = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/><Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/></Types>
                """
            );
            Add(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """
            );
            Add(
                archive,
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>before</w:t></w:r></w:p>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>target</w:t></w:r></w:p>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>after</w:t></w:r></w:p>
                </w:body></w:document>
                """
            );
            Add(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>{(protectionXml is null ? string.Empty : "<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>")}</Relationships>
                """
            );
            Add(
                archive,
                "word/numbering.xml",
                """
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum><w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num></w:numbering>
                """
            );
            if (protectionXml is not null)
            {
                Add(archive, "word/settings.xml", $"<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">{protectionXml}</w:settings>");
            }
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
