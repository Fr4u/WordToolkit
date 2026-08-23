using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Tests;

public sealed class NoteWordPackageOperationTests
{
    [Fact]
    public void InspectsCandidatesWithoutReturningRawXml()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "notes.docx");
            File.WriteAllBytes(path, BuildPackage(includeContentfulOrphan: true));
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;

            var result = new NoteWordPackageOperation().Inspect(
                new NoteInspectionRequest(
                    path,
                    fingerprint,
                    IncludeAll: true,
                    IncludeDetails: true,
                    MaxItems: 1
                )
            );

            Assert.Equal(NoteWordPackageContract.InspectContract, result.OperationContract);
            Assert.Equal(2, result.DefinitionCount);
            Assert.Equal(1, result.EmptyOrphanRemovalCandidateCount);
            Assert.Equal(1, result.ReturnedDefinitionCount);
            Assert.True(result.DefinitionsTruncated);
            Assert.True(Assert.Single(result.Definitions).EmptyOrphanRemovalCandidate);
            Assert.False(result.ReferenceDetailsTruncated);
            Assert.False(result.SpecialReferenceDetailsTruncated);
            Assert.False(result.NumberingPolicyDetailsTruncated);
            Assert.False(result.RawXmlReturned);
            Assert.False(result.MutationPerformed);
            Assert.False(result.WordOpened);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PlansAndAtomicallyAppliesSchemaValidatedRemoval()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "notes.docx");
            File.WriteAllBytes(path, BuildPackage(includeContentfulOrphan: false));
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var operation = new NoteWordPackageOperation(new PassingValidator());
            var inspected = operation.Inspect(
                new NoteInspectionRequest(path, before.Fingerprint)
            );
            var definition = Assert.Single(inspected.Definitions);
            var planRequest = new NoteRepairPlanRequest(
                path,
                before.Fingerprint,
                "remove_empty_orphan_definition",
                definition.Id,
                definition.Fingerprint,
                IncludeDetails: true
            );

            var plan = operation.Plan(planRequest);
            var applied = operation.Apply(new NoteRepairApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                plan.RepairKind,
                definition.Id,
                definition.Fingerprint,
                KeepBackup: true
            ));

            Assert.True(plan.CanApply);
            Assert.False(plan.ApplyBlocked);
            Assert.True(plan.EngineValidation.Passed);
            Assert.True(plan.CandidateValidation.NoNewErrors);
            Assert.Single(plan.ChangedParts!);
            Assert.True(applied.Applied);
            Assert.True(applied.MutationPerformed);
            Assert.NotNull(applied.BackupPath);
            Assert.True(File.Exists(applied.BackupPath));
            var after = reader.Read(path);
            Assert.Equal(plan.ResultPackageFingerprint, after.Fingerprint);
            Assert.Empty(new WordNoteGraphBuilder().Build(after).Definitions);
            Assert.Equal(before.Fingerprint, reader.Read(applied.BackupPath!).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BlocksApplyWhenSchemaValidatorIsUnavailable()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "notes.docx");
            File.WriteAllBytes(path, BuildPackage(includeContentfulOrphan: false));
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;
            var operation = new NoteWordPackageOperation();
            var definition = Assert.Single(operation.Inspect(
                new NoteInspectionRequest(path, fingerprint)
            ).Definitions);
            var plan = operation.Plan(new NoteRepairPlanRequest(
                path,
                fingerprint,
                "remove_empty_orphan_definition",
                definition.Id,
                definition.Fingerprint
            ));

            Assert.True(plan.ApplyBlocked);
            Assert.Contains("schema_validator_unavailable", plan.ApplyBlockedReasons);
            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new NoteRepairApplyRequest(
                    path,
                    fingerprint,
                    plan.PlanId,
                    plan.RepairKind,
                    definition.Id,
                    definition.Fingerprint
                ))
            );
            Assert.Equal("VALIDATOR_REQUIRED", exception.Code);
            Assert.Equal(fingerprint, new OpcPackageReader().Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsStalePackageAndMismatchedPlan()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "notes.docx");
            File.WriteAllBytes(path, BuildPackage(includeContentfulOrphan: false));
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;
            var operation = new NoteWordPackageOperation(new PassingValidator());
            var definition = Assert.Single(operation.Inspect(
                new NoteInspectionRequest(path, fingerprint)
            ).Definitions);
            var plan = operation.Plan(new NoteRepairPlanRequest(
                path,
                fingerprint,
                "remove_empty_orphan_definition",
                definition.Id,
                definition.Fingerprint
            ));

            var mismatch = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new NoteRepairApplyRequest(
                    path,
                    fingerprint,
                    "wnrplan_AAAAAAAAAAAAAAAA",
                    plan.RepairKind,
                    definition.Id,
                    definition.Fingerprint
                ))
            );
            Assert.Equal("PLAN_MISMATCH", mismatch.Code);
            var stale = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Inspect(new NoteInspectionRequest(path, new string('0', 64)))
            );
            Assert.Equal("VERSION_CONFLICT", stale.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProtectedNoteRepairRequiresExactTokenAndDenialIsByteExact()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "protected-notes.docx");
            File.WriteAllBytes(
                path,
                BuildPackage(
                    includeContentfulOrphan: false,
                    protectionXml: "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\"/>"
                )
            );
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var operation = new NoteWordPackageOperation(new PassingValidator());
            var definition = Assert.Single(operation.Inspect(
                new NoteInspectionRequest(path, before.Fingerprint)
            ).Definitions);
            var plan = operation.Plan(new NoteRepairPlanRequest(
                path,
                before.Fingerprint,
                "remove_empty_orphan_definition",
                definition.Id,
                definition.Fingerprint
            ));

            Assert.False(plan.CanApply);
            Assert.True(plan.Protection.AuthorizationRequired);
            Assert.False(plan.Protection.HasMalformedProtectionMetadata);
            Assert.Equal(plan.PlanId, plan.ProtectionAuthorizationId);
            Assert.Equal(["protected_edit_authorization"], plan.RequiredAuthorizations);
            Assert.Contains(
                "protected_document_edit_not_authorized",
                plan.ApplyBlockedReasons
            );
            var beforeBytes = File.ReadAllBytes(path);

            var denied = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new NoteRepairApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    plan.RepairKind,
                    definition.Id,
                    definition.Fingerprint,
                    KeepBackup: true,
                    ProtectedEditAuthorization: "wnrplan_wrong"
                ))
            );

            Assert.Equal("EDIT_POLICY_BLOCKED", denied.Code);
            var details = Assert.IsType<NoteRepairEditPolicyBlockDetails>(denied.Details);
            Assert.Equal(plan.PlanId, details.PlanId);
            Assert.Equal(
                ["protected_document_edit_not_authorized"],
                details.BlockCodes
            );
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));

            var applied = operation.Apply(new NoteRepairApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                plan.RepairKind,
                definition.Id,
                definition.Fingerprint,
                KeepBackup: false,
                ProtectedEditAuthorization: plan.ProtectionAuthorizationId
            ));

            Assert.True(applied.Applied);
            Assert.Equal(["protected_edit_authorization"], applied.ExplicitAuthorizations);
            Assert.Equal(plan.ResultPackageFingerprint, reader.Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MalformedNoteProtectionCannotBeOverriddenOrExposeToken()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "malformed-notes.docx");
            File.WriteAllBytes(
                path,
                BuildPackage(
                    includeContentfulOrphan: false,
                    protectionXml: "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\" w:bogus=\"x\"/>"
                )
            );
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var operation = new NoteWordPackageOperation(new PassingValidator());
            var definition = Assert.Single(operation.Inspect(
                new NoteInspectionRequest(path, before.Fingerprint)
            ).Definitions);
            var plan = operation.Plan(new NoteRepairPlanRequest(
                path,
                before.Fingerprint,
                "remove_empty_orphan_definition",
                definition.Id,
                definition.Fingerprint
            ));

            Assert.True(plan.Protection.HasMalformedProtectionMetadata);
            Assert.Null(plan.ProtectionAuthorizationId);
            Assert.Empty(plan.RequiredAuthorizations);
            Assert.Contains("protection_metadata_malformed", plan.ApplyBlockedReasons);
            var beforeBytes = File.ReadAllBytes(path);

            var denied = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new NoteRepairApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    plan.RepairKind,
                    definition.Id,
                    definition.Fingerprint,
                    KeepBackup: true,
                    ProtectedEditAuthorization: plan.PlanId
                ))
            );

            Assert.Equal("EDIT_POLICY_BLOCKED", denied.Code);
            var details = Assert.IsType<NoteRepairEditPolicyBlockDetails>(denied.Details);
            Assert.Equal(["protection_metadata_malformed"], details.BlockCodes);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));
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
            "wordtoolkit-note-operation-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] BuildPackage(
        bool includeContentfulOrphan,
        string? protectionXml = null
    )
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/>
                  {(protectionXml is null ? string.Empty : "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>")}
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
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Main</w:t></w:r></w:p></w:body></w:document>
                """
            );
            var settingsRelationship = protectionXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>";
            Add(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdFootnotes" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes" Target="footnotes.xml"/>
                  {settingsRelationship}
                </Relationships>
                """
            );
            var contentful = includeContentfulOrphan
                ? "<w:footnote w:id=\"5\"><w:p><w:r><w:footnoteRef/><w:t>Keep</w:t></w:r></w:p></w:footnote>"
                : string.Empty;
            Add(
                archive,
                "word/footnotes.xml",
                $"<w:footnotes xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:footnote w:id=\"4\"><w:p><w:r><w:footnoteRef/></w:r></w:p></w:footnote>{contentful}</w:footnotes>"
            );
            if (protectionXml is not null)
            {
                Add(
                    archive,
                    "word/settings.xml",
                    $"<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">{protectionXml}</w:settings>"
                );
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
