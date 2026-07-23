using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Engine.Tests;

public sealed class StyleWordPackageOperationTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void PublicPlanAndApplyPreserveUnknownPartsAndProvideRecoveryBackup()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "style.docx");
            var opaque = SHA256.HashData(Encoding.UTF8.GetBytes("opaque-style-sentinel"));
            CreatePackage(path, opaque);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var beforeBytes = File.ReadAllBytes(path);
            var paragraph = new WordSemanticProjector().Project(before).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            );
            var commands = new StyleEditCommand[]
            {
                new CloneStyleEditCommand("Definition", "DefinitionClone", "Definition clone"),
                new SetStyleEditCommand(
                    paragraph.Id.Value,
                    "DefinitionClone",
                    ExpectedStyleId: "OldPara"
                ),
            };
            var operation = new StyleWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );

            var plan = operation.Plan(
                new StyleEditPlanRequest(
                    path,
                    before.Fingerprint,
                    commands,
                    IncludeDetails: true
                )
            );

            Assert.Equal(StyleWordPackageContract.PlanContract, plan.OperationContract);
            Assert.StartsWith("wseplan_", plan.PlanId, StringComparison.Ordinal);
            Assert.True(plan.CanApply);
            Assert.True(plan.CandidateValidation.Performed);
            Assert.True(plan.CandidateValidation.NoNewErrors);
            Assert.Equal(2, plan.OperationCount);
            Assert.Equal(2, plan.ChangedPartCount);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.False(plan.RawXmlReturned);
            Assert.False(plan.WordOpened);

            var applied = operation.Apply(
                new StyleEditApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    commands,
                    KeepBackup: true
                )
            );

            Assert.Equal(StyleWordPackageContract.ApplyContract, applied.OperationContract);
            Assert.True(applied.Applied);
            Assert.False(applied.NoOp);
            Assert.NotNull(applied.BackupPath);
            Assert.True(File.Exists(applied.BackupPath));
            Assert.Equal(plan.ResultPackageFingerprint, applied.PackageFingerprint);
            Assert.Equal(
                ["word/document.xml", "word/styles.xml"],
                applied.ChangedEntryNames.Order(StringComparer.Ordinal).ToArray()
            );
            var after = reader.Read(path);
            Assert.Equal(opaque, after.Entries.Single(entry =>
                entry.Name == "custom/opaque.bin"
            ).Content.ToArray());
            var changedParagraph = new WordSemanticProjector().Project(after).Nodes.Single(
                node => node.Kind == WordSemanticNodeKind.Paragraph
            );
            Assert.Equal("DefinitionClone", changedParagraph.Properties["style_id"]);

            File.Copy(applied.BackupPath!, path, overwrite: true);
            Assert.Equal(before.Fingerprint, reader.Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApplyFailsClosedWithoutValidatorAndBindsExactCommandIntent()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "closed.docx");
            CreatePackage(path);
            var before = new OpcPackageReader().Read(path);
            var beforeBytes = File.ReadAllBytes(path);
            var commands = new StyleEditCommand[]
            {
                new RenameStyleEditCommand("Definition", "Definition renamed"),
            };
            var noValidator = new StyleWordPackageOperation();
            var blocked = noValidator.Plan(
                new StyleEditPlanRequest(path, before.Fingerprint, commands)
            );
            Assert.False(blocked.CanApply);
            Assert.Contains("schema_validator_unavailable", blocked.ApplyBlockedReasons);
            var missing = Assert.Throws<WordToolkitOperationException>(() =>
                noValidator.Apply(
                    new StyleEditApplyRequest(
                        path,
                        before.Fingerprint,
                        blocked.PlanId,
                        commands
                    )
                )
            );
            Assert.Equal("VALIDATOR_REQUIRED", missing.Code);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));

            var operation = new StyleWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var reviewed = operation.Plan(
                new StyleEditPlanRequest(path, before.Fingerprint, commands)
            );
            var drift = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(
                    new StyleEditApplyRequest(
                        path,
                        before.Fingerprint,
                        reviewed.PlanId,
                        [new RenameStyleEditCommand("Definition", "Different intent")]
                    )
                )
            );
            Assert.Equal("PLAN_MISMATCH", drift.Code);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));

            var schemaFailure = new StyleWordPackageOperation(
                new RejectedCandidateValidator()
            );
            var rejected = schemaFailure.Plan(
                new StyleEditPlanRequest(path, before.Fingerprint, commands)
            );
            Assert.False(rejected.CanApply);
            Assert.True(rejected.CandidateValidation.ErrorsTruncated);
            Assert.Empty(rejected.CandidateValidation.Issues);
            var validation = Assert.Throws<WordToolkitOperationException>(() =>
                schemaFailure.Apply(
                    new StyleEditApplyRequest(
                        path,
                        before.Fingerprint,
                        rejected.PlanId,
                        commands
                    )
                )
            );
            Assert.Equal("OOXML_SCHEMA_INVALID", validation.Code);
            var validationDetails = Assert.IsType<
                WordPackageValidationFailureDetails
            >(validation.Details);
            Assert.Equal(1, validationDetails.ErrorCount);
            Assert.Equal(1, validationDetails.CandidateErrorCount);
            Assert.Single(validationDetails.Issues);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));

            var dishonest = Assert.Throws<WordToolkitOperationException>(() =>
                new StyleWordPackageOperation(
                    new InconsistentCandidateValidator()
                ).Plan(new StyleEditPlanRequest(path, before.Fingerprint, commands))
            );
            Assert.Equal("VALIDATION_FAILED", dishonest.Code);
            Assert.Null(dishonest.Reason);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));

            var signedPath = Path.Combine(directory, "signed.docx");
            CreatePackage(signedPath);
            using (var signedArchive = ZipFile.Open(signedPath, ZipArchiveMode.Update))
            {
                WriteEntry(
                    signedArchive,
                    "_xmlsignatures/sig1.xml",
                    "<Signature/>"
                );
            }
            var signedBefore = new OpcPackageReader().Read(signedPath);
            var signedBytes = File.ReadAllBytes(signedPath);
            var signedCommands = new StyleEditCommand[]
            {
                new RenameStyleEditCommand("Definition", "Signed rename"),
            };
            var signedOperation = new StyleWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var signedPlan = signedOperation.Plan(
                new StyleEditPlanRequest(
                    signedPath,
                    signedBefore.Fingerprint,
                    signedCommands
                )
            );
            Assert.False(signedPlan.CanApply);
            Assert.Contains("digital_signature_present", signedPlan.ApplyBlockedReasons);
            var signed = Assert.Throws<WordToolkitOperationException>(() =>
                signedOperation.Apply(
                    new StyleEditApplyRequest(
                        signedPath,
                        signedBefore.Fingerprint,
                        signedPlan.PlanId,
                        signedCommands
                    )
                )
            );
            Assert.Equal("SIGNED_PACKAGE", signed.Code);
            Assert.Equal(signedBytes, File.ReadAllBytes(signedPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SelectorAndStrictJsonShareOneBoundedContract()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "selector.docx");
            CreatePackage(path);
            var before = new OpcPackageReader().Read(path);
            var json = $$"""
                {
                  "local_path": {{System.Text.Json.JsonSerializer.Serialize(path)}},
                  "expected_package_fingerprint": "{{before.Fingerprint}}",
                  "commands": [
                    {
                      "type": "set_style_where",
                      "selector": {
                        "kind": "paragraph",
                        "text": "Alpha",
                        "text_match": "contains",
                        "text_scope": "subtree"
                      },
                      "style_id": "Definition",
                      "expected_style_id": "OldPara",
                      "max_matches": 1
                    }
                  ],
                  "include_details": true
                }
                """;
            var request = StyleEditOperationJson.ParsePlanRequest(json);
            var result = new StyleWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            ).Plan(request);
            Assert.Equal(1, result.SelectorCommandCount);
            Assert.Equal(1, result.SelectorMatchCount);
            Assert.Equal("all_nodes", Assert.Single(
                result.SelectorResolutions!
            ).CandidateSeed);

            var unknownRoot = Assert.Throws<WordToolkitOperationException>(() =>
                StyleEditOperationJson.ParsePlanRequest(
                    json.Replace(
                        "\"include_details\": true",
                        "\"include_details\": true, \"mystery\": 1",
                        StringComparison.Ordinal
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", unknownRoot.Code);
            Assert.Throws<WordToolkitOperationException>(() =>
                StyleEditOperationJson.ParsePlanRequest(
                    json.Replace(
                        "\"max_matches\": 1",
                        "\"max_matches\": 1, \"node_id\": \"wdn_bad\"",
                        StringComparison.Ordinal
                    )
                )
            );
            Assert.Throws<WordToolkitOperationException>(() =>
                StyleEditOperationJson.ParsePlanRequest(
                    json.Replace("\"paragraph\"", "\"Paragraph\"", StringComparison.Ordinal)
                )
            );
            Assert.Throws<WordToolkitOperationException>(() =>
                StyleEditOperationJson.ParsePlanRequest(
                    json.Replace(
                        "\"text\": \"Alpha\"",
                        "\"text\": \"Alpha\", \"text\": \"Beta\"",
                        StringComparison.Ordinal
                    )
                )
            );
            var oversized = Assert.Throws<WordToolkitOperationException>(() =>
                StyleEditOperationJson.ParsePlanRequest(
                    "{}" + new string(' ', StyleWordPackageContract.MaximumRequestJsonCharacters)
                )
            );
            Assert.Equal("INVALID_INPUT", oversized.Code);
            var invalidEnum = Assert.Throws<WordToolkitOperationException>(() =>
                new StyleWordPackageOperation(
                    new MicrosoftOpenXmlPackageValidator()
                ).Plan(
                    new StyleEditPlanRequest(
                        path,
                        before.Fingerprint,
                        [
                            new SetStyleWhereEditCommand(
                                new StyleEditSelector
                                {
                                    Kind = WordSemanticNodeKind.Paragraph,
                                    Text = "Alpha",
                                    TextMatch = (WordSemanticTextMatchMode)999,
                                },
                                "Definition",
                                MaxMatches: 1
                            ),
                        ]
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", invalidEnum.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MicrosoftValidatorReportsOnlyNewCandidateErrors()
    {
        var directory = TemporaryDirectory();
        try
        {
            var baselinePath = Path.Combine(directory, "baseline.docx");
            var candidatePath = Path.Combine(directory, "candidate.docx");
            CreatePackage(baselinePath);
            CreatePackage(candidatePath, documentBody: "<w:bogus/>");
            using var baseline = File.OpenRead(baselinePath);
            using var candidate = File.OpenRead(candidatePath);
            var result = new MicrosoftOpenXmlPackageValidator().Validate(
                baseline,
                candidate
            );
            Assert.True(result.Performed);
            Assert.False(result.NoNewErrors);
            Assert.True(result.ErrorCount > 0);
            Assert.NotEmpty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PublicBoundaryRejectsUntrustedValidatorMessagesAndBoundsChangedParts()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "privacy.docx");
            CreatePackage(path);
            var before = new OpcPackageReader().Read(path);
            var commands = new StyleEditCommand[]
            {
                new RenameStyleEditCommand("Definition", "Definition renamed"),
            };
            var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, path);
            var rejectedMessage = Assert.Throws<WordToolkitOperationException>(() =>
                new StyleWordPackageOperation(
                    new ThrowingCandidateValidator(
                        new InvalidDataException(
                            $"document text SECRET at {Path.GetFullPath(relativePath)}"
                        )
                    )
                ).Plan(
                    new StyleEditPlanRequest(
                        relativePath,
                        before.Fingerprint,
                        commands
                    )
                )
            );
            Assert.Equal("VALIDATION_FAILED", rejectedMessage.Code);
            Assert.Null(rejectedMessage.Reason);
            Assert.Null(rejectedMessage.Details);
            var publicError = WordToolkitOperationJson.Serialize(
                WordToolkitOperationError.FromException(rejectedMessage)
            );
            Assert.DoesNotContain("SECRET", publicError, StringComparison.Ordinal);
            Assert.DoesNotContain(path, publicError, StringComparison.OrdinalIgnoreCase);

            var ioFailure = Assert.Throws<WordToolkitOperationException>(() =>
                new StyleWordPackageOperation(
                    new ThrowingCandidateValidator(
                        new IOException($"Cannot reopen {path}")
                    )
                ).Plan(new StyleEditPlanRequest(path, before.Fingerprint, commands))
            );
            Assert.Equal("VALIDATION_FAILED", ioFailure.Code);
            Assert.Null(ioFailure.Reason);

            var manyPartsPath = Path.Combine(directory, "many-parts.docx");
            CreatePackageWithHeaders(
                manyPartsPath,
                StyleWordPackageContract.MaximumChangedParts
            );
            var manyParts = new OpcPackageReader().Read(manyPartsPath);
            var bounded = Assert.Throws<WordToolkitOperationException>(() =>
                new StyleWordPackageOperation().Plan(
                    new StyleEditPlanRequest(
                        manyPartsPath,
                        manyParts.Fingerprint,
                        [new ConsolidateStyleEditCommand("OldPara", "Definition")]
                    )
                )
            );
            Assert.Equal("PACKAGE_LIMIT", bounded.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecoveryDetailsExposeOnlyExistingOpaqueSiblingNames()
    {
        var directory = TemporaryDirectory();
        try
        {
            var existing = Path.Combine(
                directory,
                ".wordtoolkit-0123456789abcdef0123456789abcdef.conflict"
            );
            var missing = Path.Combine(
                directory,
                ".wordtoolkit-fedcba9876543210fedcba9876543210.bak"
            );
            File.WriteAllText(existing, "recovery bytes");
            var recovery = new OpcPackageRecoveryException(
                "recovery failed",
                [existing, missing],
                new IOException("commit failed")
            );

            var details = StyleWordPackageOperation.BuildRecoveryDetails(recovery);

            Assert.NotNull(details);
            Assert.Equal(
                [Path.GetFileName(existing)],
                details.RecoveryArtifactNames
            );
            var json = WordToolkitOperationJson.Serialize(details);
            Assert.DoesNotContain(directory, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recovery bytes", json, StringComparison.Ordinal);

            File.Delete(existing);
            Assert.Null(StyleWordPackageOperation.BuildRecoveryDetails(recovery));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApplyDoesNotOverwriteAConcurrentPackageChange()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "concurrent.docx");
            CreatePackage(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var commands = new StyleEditCommand[]
            {
                new RenameStyleEditCommand("Definition", "Definition renamed"),
            };
            var reviewed = new StyleWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            ).Plan(new StyleEditPlanRequest(path, before.Fingerprint, commands));

            var conflict = Assert.Throws<WordToolkitOperationException>(() =>
                new StyleWordPackageOperation(
                    new ConcurrentMutationValidator(path)
                ).Apply(
                    new StyleEditApplyRequest(
                        path,
                        before.Fingerprint,
                        reviewed.PlanId,
                        commands
                    )
                )
            );

            Assert.Equal("VERSION_CONFLICT", conflict.Code);
            Assert.True(conflict.Retryable);
            var after = reader.Read(path);
            Assert.NotEqual(before.Fingerprint, after.Fingerprint);
            Assert.Contains(after.Entries, entry =>
                entry.Name == "custom/concurrent.xml"
            );
            Assert.Contains(
                "w:name w:val=\"Definition\"",
                Encoding.UTF8.GetString(
                    after.Parts["/word/styles.xml"].Entry.Content.Span
                ),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(
        string path,
        byte[]? opaque = null,
        string? documentBody = null
    )
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            $$"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              {{(opaque is null ? string.Empty : "<Default Extension=\"bin\" ContentType=\"application/octet-stream\"/>")}}
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
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
            <w:document xmlns:w="{WordNamespace}"><w:body>{documentBody ?? "<w:p><w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr><w:r><w:t>Alpha definition</w:t></w:r></w:p>"}</w:body></w:document>
            """
        );
        WriteEntry(
            archive,
            "word/styles.xml",
            $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:customStyle="1" w:styleId="OldPara"><w:name w:val="Old paragraph"/></w:style>
              <w:style w:type="paragraph" w:customStyle="1" w:styleId="Definition"><w:name w:val="Definition"/><w:basedOn w:val="Normal"/><w:qFormat/></w:style>
            </w:styles>
            """
        );
        WriteEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """
        );
        if (opaque is not null)
        {
            WriteEntry(archive, "custom/opaque.bin", opaque);
        }
    }

    private static void CreatePackageWithHeaders(string path, int headerCount)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var overrides = string.Join(
            string.Empty,
            Enumerable.Range(1, headerCount).Select(index =>
                $"<Override PartName=\"/word/header{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>"
            )
        );
        WriteEntry(
            archive,
            "[Content_Types].xml",
            $$"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              {{overrides}}
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
            $"<w:document xmlns:w=\"{WordNamespace}\"><w:body><w:p/></w:body></w:document>"
        );
        WriteEntry(
            archive,
            "word/styles.xml",
            $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:customStyle="1" w:styleId="OldPara"><w:name w:val="Old paragraph"/></w:style>
              <w:style w:type="paragraph" w:customStyle="1" w:styleId="Definition"><w:name w:val="Definition"/></w:style>
            </w:styles>
            """
        );
        var relationships = string.Join(
            string.Empty,
            Enumerable.Range(1, headerCount).Select(index =>
                $"<Relationship Id=\"rIdHeader{index}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header{index}.xml\"/>"
            )
        );
        WriteEntry(
            archive,
            "word/_rels/document.xml.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              {relationships}
            </Relationships>
            """
        );
        foreach (var index in Enumerable.Range(1, headerCount))
        {
            WriteEntry(
                archive,
                $"word/header{index}.xml",
                $"<w:hdr xmlns:w=\"{WordNamespace}\"><w:p><w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr></w:p></w:hdr>"
            );
        }
    }

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
            "wordtoolkit-style-operation-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RejectedCandidateValidator : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        ) => new(
            Performed: true,
            CandidateValid: false,
            NoNewErrors: false,
            ErrorCount: 1,
            BaselineErrorCount: 0,
            CandidateErrorCount: 1,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues:
            [
                new WordPackageValidationIssue(
                    "TEST_NEW_ERROR",
                    "Schema",
                    "/word/styles.xml",
                    "/w:styles[1]",
                    "style"
                ),
            ]
        );
    }

    private sealed class ThrowingCandidateValidator(Exception exception)
        : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        ) => throw exception;
    }

    private sealed class InconsistentCandidateValidator
        : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        ) => new(
            Performed: true,
            CandidateValid: true,
            NoNewErrors: true,
            ErrorCount: 1,
            BaselineErrorCount: 0,
            CandidateErrorCount: 1,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues:
            [
                new WordPackageValidationIssue(
                    "CONTRADICTORY",
                    "Schema",
                    null,
                    null,
                    null
                ),
            ]
        );
    }

    private sealed class ConcurrentMutationValidator(string path)
        : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        )
        {
            var result = new MicrosoftOpenXmlPackageValidator().Validate(
                baselinePackage,
                candidatePackage,
                cancellationToken
            );
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            WriteEntry(archive, "custom/concurrent.xml", "<concurrent/>");
            return result;
        }
    }
}
