using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Engine.Tests;

public sealed class EquationParagraphRewriteWordPackageOperationTests
{
    private const string Word =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Word2010 =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string Math =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";

    [Fact]
    public void InspectPlanAndApplyRewriteOnlyTextSlotsAroundExactOfficeMath()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "equation-paragraph.docx");
            CreatePackage(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var catalog = new WordEquationParagraphRewriteCatalogBuilder().Build(
                before,
                semantic
            );
            var target = catalog.Candidates.Single(candidate =>
                candidate.TextSlots.Any(slot => slot.Text.Contains(
                    "Before",
                    StringComparison.Ordinal
                ))
            );
            Assert.True(target.CanRewrite);
            Assert.Equal(2, target.TextSlotCount);
            Assert.Single(target.EquationAnchors);
            var equationHash = target.EquationAnchors[0].ExactXmlSha256;
            var operation = new EquationParagraphRewriteWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );

            var inspection = operation.Inspect(
                new EquationParagraphRewriteInspectRequest(
                    path,
                    before.Fingerprint,
                    target.ParagraphNodeId.Value,
                    IncludeText: true
                )
            );

            Assert.Equal(
                EquationParagraphRewriteWordPackageContract.InspectContract,
                inspection.OperationContract
            );
            Assert.True(inspection.TextIncluded);
            var inspected = Assert.Single(inspection.Candidates);
            Assert.Equal(target.Id, inspected.CandidateId);
            Assert.Equal(["Before ", " after."], inspected.TextSlots!.Select(slot =>
                slot.Text
            ));
            Assert.False(inspection.RawXmlReturned);
            Assert.False(inspection.MutationPerformed);
            Assert.False(inspection.WordOpened);

            var command = new RewriteEquationParagraphTextCommand(
                target.Id,
                target.Fingerprint,
                [" Rewritten ", " remains. "]
            );
            var plan = operation.Plan(new EquationParagraphRewritePlanRequest(
                path,
                before.Fingerprint,
                [command],
                IncludeDetails: true
            ));

            Assert.Equal(
                EquationParagraphRewriteWordPackageContract.PlanContract,
                plan.OperationContract
            );
            Assert.StartsWith("weprplan_", plan.PlanId, StringComparison.Ordinal);
            Assert.True(plan.CanApply);
            Assert.True(plan.ExactEquationBytesPreserved);
            Assert.True(plan.ParagraphStructurePreserved);
            Assert.True(plan.ExactInverseVerified);
            Assert.True(plan.CandidateValidation.NoNewErrors);
            Assert.Equal(1, plan.ParagraphCount);
            Assert.Equal(1, plan.EquationAnchorCount);
            Assert.Equal(2, plan.ChangedTextSlotCount);
            Assert.Equal(2, plan.TextNodeOperationCount);
            Assert.Equal(1, plan.ChangedPartCount);
            Assert.False(plan.RawTextReturned);
            Assert.False(plan.RawXmlReturned);
            Assert.Equal(before.Fingerprint, reader.Read(path).Fingerprint);
            Assert.DoesNotContain("Rewritten", WordToolkitOperationJson.Serialize(plan));

            var repeated = operation.Plan(new EquationParagraphRewritePlanRequest(
                path,
                before.Fingerprint,
                [command],
                IncludeDetails: true
            ));
            Assert.Equal(plan.PlanId, repeated.PlanId);
            Assert.Equal(plan.ResultPackageFingerprint, repeated.ResultPackageFingerprint);

            var applied = operation.Apply(new EquationParagraphRewriteApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                [command],
                KeepBackup: true
            ));

            Assert.True(applied.Applied);
            Assert.False(applied.NoOp);
            Assert.NotNull(applied.BackupPath);
            Assert.True(File.Exists(applied.BackupPath));
            Assert.Equal(["word/document.xml"], applied.ChangedEntryNames);
            Assert.True(applied.ExactEquationBytesPreserved);
            Assert.True(applied.ParagraphStructurePreserved);
            Assert.True(applied.ExactInverseVerified);
            var after = reader.Read(path);
            Assert.Equal(plan.ResultPackageFingerprint, after.Fingerprint);
            var afterSemantic = new WordSemanticProjector().Project(after);
            var afterCatalog = new WordEquationParagraphRewriteCatalogBuilder().Build(
                after,
                afterSemantic
            );
            var afterTarget = afterCatalog.Candidates.Single(candidate =>
                candidate.SourceElementOrdinal == target.SourceElementOrdinal
            );
            Assert.Equal([" Rewritten ", " remains. "], afterTarget.TextSlots.Select(slot =>
                slot.Text
            ));
            Assert.Equal(equationHash, afterTarget.EquationAnchors[0].ExactXmlSha256);
            Assert.Contains("xml:space=\"preserve\"", DocumentXml(after));
            var untouchedBefore = catalog.Candidates.Single(candidate => candidate != target);
            var untouchedAfter = afterCatalog.Candidates.Single(candidate =>
                candidate.SourceElementOrdinal == untouchedBefore.SourceElementOrdinal
            );
            Assert.Equal(untouchedBefore.Fingerprint, untouchedAfter.Fingerprint);
            foreach (var entry in before.Entries.Where(entry =>
                entry.Name != "word/document.xml"
            ))
            {
                Assert.Equal(
                    entry.Content.ToArray(),
                    after.Entries.Single(candidate => candidate.Name == entry.Name)
                        .Content.ToArray()
                );
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RichInlineStructuresAndEmptySlotsFailClosed()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "unsafe.docx");
            CreatePackage(
                path,
                firstParagraph:
                    "<w:p w14:paraId=\"A0000001\"><w:r><w:t>Before</w:t>"
                    + "<w:fldChar w:fldCharType=\"begin\"/></w:r>"
                    + InlineEquation("x")
                    + "<w:r><w:t>After</w:t></w:r></w:p>"
            );
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var candidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                package,
                semantic
            ).Candidates.Single(item => item.SourceElementOrdinal
                == semantic.Nodes.Single(node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                    && node.Properties.TryGetValue("paragraph_id", out var id)
                    && id == "A0000001"
                ).SourceElementOrdinal
            );
            Assert.False(candidate.CanRewrite);
            Assert.Contains("unsupported_run_content", candidate.BlockedReasons);
            var operation = new EquationParagraphRewriteWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var error = Assert.Throws<WordToolkitOperationException>(() => operation.Plan(
                new EquationParagraphRewritePlanRequest(
                    path,
                    package.Fingerprint,
                    [new RewriteEquationParagraphTextCommand(
                        candidate.Id,
                        candidate.Fingerprint,
                        ["x", "y"]
                    )]
                )
            ));
            Assert.Equal("UNSAFE_EDIT", error.Code);
            Assert.Equal(package.Fingerprint, new OpcPackageReader().Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsDriftUnknownJsonAndMissingValidator()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "closed.docx");
            CreatePackage(path);
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var candidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                package,
                semantic
            ).Candidates.First();
            var command = new RewriteEquationParagraphTextCommand(
                candidate.Id,
                candidate.Fingerprint,
                ["New before", "new after"]
            );
            var withoutValidator = new EquationParagraphRewriteWordPackageOperation();
            var blocked = withoutValidator.Plan(new EquationParagraphRewritePlanRequest(
                path,
                package.Fingerprint,
                [command]
            ));
            Assert.False(blocked.CanApply);
            Assert.Contains("schema_validator_unavailable", blocked.ApplyBlockedReasons);
            var validatorError = Assert.Throws<WordToolkitOperationException>(() =>
                withoutValidator.Apply(new EquationParagraphRewriteApplyRequest(
                    path,
                    package.Fingerprint,
                    blocked.PlanId,
                    [command]
                ))
            );
            Assert.Equal("VALIDATOR_REQUIRED", validatorError.Code);

            var operation = new EquationParagraphRewriteWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var plan = operation.Plan(new EquationParagraphRewritePlanRequest(
                path,
                package.Fingerprint,
                [command]
            ));
            var mismatch = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new EquationParagraphRewriteApplyRequest(
                    path,
                    package.Fingerprint,
                    plan.PlanId,
                    [command with { ReplacementTextSlots = ["different", "new after"] }]
                ))
            );
            Assert.Equal("PLAN_MISMATCH", mismatch.Code);

            var json = $$"""
                {
                  "local_path": {{System.Text.Json.JsonSerializer.Serialize(path)}},
                  "expected_package_fingerprint": "{{package.Fingerprint}}",
                  "commands": [{
                    "type": "rewrite_equation_paragraph_text",
                    "candidate_id": "{{candidate.Id}}",
                    "expected_candidate_fingerprint": "{{candidate.Fingerprint}}",
                    "replacement_text_slots": ["new", "text"]
                  }]
                }
                """;
            Assert.Single(
                EquationParagraphRewriteOperationJson.ParsePlanRequest(json).Commands
            );
            var unknown = Assert.Throws<WordToolkitOperationException>(() =>
                EquationParagraphRewriteOperationJson.ParsePlanRequest(
                    json.Replace(
                        "\"replacement_text_slots\": [\"new\", \"text\"]",
                        "\"replacement_text_slots\": [\"new\", \"text\"], \"raw_xml\": true",
                        StringComparison.Ordinal
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", unknown.Code);
            Assert.Equal(package.Fingerprint, new OpcPackageReader().Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
        "http://schemas.openxmlformats.org/officeDocument/2006/math",
        WordPackageConformance.TransitionalOfficeDocumentRelationship
    )]
    [InlineData(
        "http://purl.oclc.org/ooxml/wordprocessingml/main",
        "http://purl.oclc.org/ooxml/officeDocument/math",
        WordPackageConformance.StrictOfficeDocumentRelationship
    )]
    public void ModelsMultipleInlineAndDisplayMathAnchorsInStrictAndTransitionalPackages(
        string wordNamespace,
        string mathNamespace,
        string officeRelationship
    )
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "multi.docx");
            CreateMultiMathPackage(
                path,
                wordNamespace,
                mathNamespace,
                officeRelationship
            );
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var candidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                package,
                semantic
            ).Candidates.Single();
            Assert.True(candidate.CanRewrite);
            Assert.Equal(3, candidate.EquationAnchorCount);
            Assert.Equal(4, candidate.TextSlotCount);
            Assert.Equal(["inline_math", "inline_math", "display_math"],
                candidate.EquationAnchors.Select(anchor => anchor.Kind));
            Assert.Equal(2, candidate.EquationAnchors[2].ContainedEquationCount);
            Assert.False(candidate.TextSlots[1].CanRewrite);
            Assert.Equal(string.Empty, candidate.TextSlots[1].Text);
            var operation = new EquationParagraphRewriteWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var command = new RewriteEquationParagraphTextCommand(
                candidate.Id,
                candidate.Fingerprint,
                ["A", "", "B", "C"]
            );
            var plan = operation.Plan(new EquationParagraphRewritePlanRequest(
                path,
                package.Fingerprint,
                [command]
            ));
            Assert.True(plan.CanApply);
            Assert.True(plan.ExactEquationBytesPreserved);
            Assert.True(plan.ExactInverseVerified);
            var illegalGap = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Plan(new EquationParagraphRewritePlanRequest(
                    path,
                    package.Fingerprint,
                    [command with { ReplacementTextSlots = ["A", "inserted", "B", "C"] }]
                ))
            );
            Assert.Equal("UNSAFE_EDIT", illegalGap.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DeterministicReplacementCorpusEscapesXmlAndNeverChangesOfficeMath()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "corpus.docx");
            CreatePackage(path);
            var reader = new OpcPackageReader();
            var operation = new EquationParagraphRewriteWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            string? equationHash = null;
            var random = new Random(0x4D415448);
            const string alphabet = "abc XYZ<&>\"'ąćΩ中\t\n";
            for (var iteration = 0; iteration < 32; iteration++)
            {
                var package = reader.Read(path);
                var semantic = new WordSemanticProjector().Project(package);
                var candidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                    package,
                    semantic
                ).Candidates.First();
                equationHash ??= candidate.EquationAnchors[0].ExactXmlSha256;
                var replacements = new[]
                {
                    RandomText(random, alphabet, iteration % 17, leadingSpace: iteration % 2 == 0),
                    RandomText(random, alphabet, 23 - iteration % 19, leadingSpace: false)
                        + (iteration % 3 == 0 ? " " : string.Empty),
                };
                var command = new RewriteEquationParagraphTextCommand(
                    candidate.Id,
                    candidate.Fingerprint,
                    replacements
                );
                var plan = operation.Plan(new EquationParagraphRewritePlanRequest(
                    path,
                    package.Fingerprint,
                    [command]
                ));
                Assert.True(plan.CanApply);
                Assert.True(plan.ExactEquationBytesPreserved);
                Assert.True(plan.ExactInverseVerified);
                operation.Apply(new EquationParagraphRewriteApplyRequest(
                    path,
                    package.Fingerprint,
                    plan.PlanId,
                    [command],
                    KeepBackup: false
                ));
                var after = reader.Read(path);
                var afterCandidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                    after,
                    new WordSemanticProjector().Project(after)
                ).Candidates.First();
                Assert.Equal(replacements, afterCandidate.TextSlots.Select(slot => slot.Text));
                Assert.Equal(equationHash, afterCandidate.EquationAnchors[0].ExactXmlSha256);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProtectedRewriteRequiresPlanBoundAuthorizationAndMalformedProtectionIsByteExactNoOp()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "protected.docx");
            CreateProtectedPackage(path, "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\"/>");
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var candidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                before, new WordSemanticProjector().Project(before)).Candidates.First();
            var command = new RewriteEquationParagraphTextCommand(candidate.Id, candidate.Fingerprint, ["changed", " after"]);
            var operation = new EquationParagraphRewriteWordPackageOperation(new MicrosoftOpenXmlPackageValidator());
            var plan = operation.Plan(new EquationParagraphRewritePlanRequest(path, before.Fingerprint, [command]));
            Assert.True(plan.Protection.AuthorizationRequired);
            Assert.Equal(plan.PlanId, plan.ProtectionAuthorizationId);
            var denied = Assert.Throws<WordToolkitOperationException>(() => operation.Apply(
                new EquationParagraphRewriteApplyRequest(path, before.Fingerprint, plan.PlanId, [command], false, "wrong")));
            Assert.Equal("EDIT_POLICY_BLOCKED", denied.Code);
            Assert.Equal(before.Fingerprint, reader.Read(path).Fingerprint);
            var applied = operation.Apply(new EquationParagraphRewriteApplyRequest(
                path, before.Fingerprint, plan.PlanId, [command], false, plan.ProtectionAuthorizationId));
            Assert.True(applied.Applied);
            Assert.Equal(["protected_edit_authorization"], applied.ExplicitAuthorizations);

            var malformed = Path.Combine(directory, "malformed.docx");
            CreateProtectedPackage(malformed, "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\" w:bogus=\"x\"/>");
            var malformedBefore = File.ReadAllBytes(malformed);
            var malformedPackage = reader.Read(malformed);
            var malformedCandidate = new WordEquationParagraphRewriteCatalogBuilder().Build(
                malformedPackage, new WordSemanticProjector().Project(malformedPackage)).Candidates.First();
            var malformedCommand = new RewriteEquationParagraphTextCommand(malformedCandidate.Id, malformedCandidate.Fingerprint, ["x", "y"]);
            var malformedPlan = operation.Plan(new EquationParagraphRewritePlanRequest(malformed, malformedPackage.Fingerprint, [malformedCommand]));
            Assert.True(malformedPlan.Protection.HasMalformedProtectionMetadata);
            var malformedError = Assert.Throws<WordToolkitOperationException>(() => operation.Apply(
                new EquationParagraphRewriteApplyRequest(malformed, malformedPackage.Fingerprint, malformedPlan.PlanId, [malformedCommand], false, malformedPlan.ProtectionAuthorizationId)));
            Assert.Equal("EDIT_POLICY_BLOCKED", malformedError.Code);
            Assert.Equal(malformedBefore, File.ReadAllBytes(malformed));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ProtectedNoOpDoesNotRequireAuthorizationOrWriteBackup()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "noop.docx");
            CreateProtectedPackage(path, "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\"/>");
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var candidate = new WordEquationParagraphRewriteCatalogBuilder().Build(before, new WordSemanticProjector().Project(before)).Candidates.First();
            var command = new RewriteEquationParagraphTextCommand(candidate.Id, candidate.Fingerprint, candidate.TextSlots.Select(slot => slot.Text).ToArray());
            var operation = new EquationParagraphRewriteWordPackageOperation(new MicrosoftOpenXmlPackageValidator());
            var plan = operation.Plan(new EquationParagraphRewritePlanRequest(path, before.Fingerprint, [command]));
            Assert.False(plan.HasChanges);
            var result = operation.Apply(new EquationParagraphRewriteApplyRequest(path, before.Fingerprint, plan.PlanId, [command], true));
            Assert.True(result.NoOp);
            Assert.Null(result.BackupPath);
            Assert.Equal(before.Fingerprint, reader.Read(path).Fingerprint);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void CreatePackage(string path, string? firstParagraph = null)
    {
        firstParagraph ??=
            "<w:p w14:paraId=\"A0000001\">"
            + "<w:r><w:rPr><w:i/></w:rPr><w:t xml:space=\"preserve\">Before </w:t></w:r>"
            + InlineEquation("x+1")
            + "<w:r><w:t xml:space=\"preserve\"> after.</w:t></w:r></w:p>";
        var secondParagraph =
            "<w:p w14:paraId=\"A0000002\"><w:r><w:t>Untouched</w:t></w:r>"
            + InlineEquation("y=2")
            + "<w:r><w:t>tail</w:t></w:r></w:p>";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="bin" ContentType="application/octet-stream"/>
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
            <w:document xmlns:w="{Word}" xmlns:w14="{Word2010}" xmlns:m="{Math}">
              <w:body>{firstParagraph}{secondParagraph}</w:body>
            </w:document>
            """
        );
        WriteEntry(archive, "custom/opaque.bin", Encoding.UTF8.GetBytes("opaque"));
    }

    private static void CreateProtectedPackage(string path, string protection)
    {
        CreatePackage(path);
        var temp = path + ".tmp";
        using (var source = ZipFile.OpenRead(path))
        using (var target = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                var output = target.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var memory = new MemoryStream();
                input.CopyTo(memory);
                if (entry.FullName == "[Content_Types].xml")
                {
                    var xml = Encoding.UTF8.GetString(memory.ToArray());
                    xml = xml.Replace(
                        "</Types>",
                        "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/></Types>",
                        StringComparison.Ordinal
                    );
                    using var writer = new StreamWriter(output.Open(), new UTF8Encoding(false), leaveOpen: false);
                    writer.Write(xml);
                }
                else
                {
                    using var destination = output.Open();
                    memory.Position = 0;
                    memory.CopyTo(destination);
                }
            }
            WriteEntry(
                target,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSettings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
                </Relationships>
                """
            );
            WriteEntry(
                target,
                "word/settings.xml",
                $"""<w:settings xmlns:w="{Word}">{protection}</w:settings>"""
            );
        }
        File.Move(temp, path, true);
    }

    private static void CreateMultiMathPackage(
        string path,
        string wordNamespace,
        string mathNamespace,
        string officeRelationship
    )
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
              <Relationship Id="rId1" Type="{officeRelationship}" Target="word/document.xml"/>
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            $"""
            <w:document xmlns:w="{wordNamespace}" xmlns:m="{mathNamespace}"><w:body>
              <w:p><w:r><w:t>left</w:t></w:r>
                <m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>
                <m:oMath><m:r><m:t>y</m:t></m:r></m:oMath>
                <w:r><w:t>middle</w:t></w:r>
                <m:oMathPara><m:oMath><m:r><m:t>a</m:t></m:r></m:oMath><m:oMath><m:r><m:t>b</m:t></m:r></m:oMath></m:oMathPara>
                <w:r><w:t>right</w:t></w:r>
              </w:p>
            </w:body></w:document>
            """
        );
    }

    private static string InlineEquation(string text) =>
        $"<m:oMath><m:r><m:t>{text}</m:t></m:r></m:oMath>";

    private static string RandomText(
        Random random,
        string alphabet,
        int length,
        bool leadingSpace
    )
    {
        var builder = new StringBuilder(length + 1);
        if (leadingSpace)
        {
            builder.Append(' ');
        }
        for (var index = 0; index < length; index++)
        {
            builder.Append(alphabet[random.Next(alphabet.Length)]);
        }
        return builder.ToString();
    }

    private static string DocumentXml(OpcPackageSnapshot package) => Encoding.UTF8.GetString(
        package.Parts["/word/document.xml"].Entry.Content.Span
    );

    private static void WriteEntry(ZipArchive archive, string name, string content) =>
        WriteEntry(archive, name, Encoding.UTF8.GetBytes(content));

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(content);
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-equation-paragraph-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }
}
