using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Tests;

public sealed class EquationRepairWordPackageOperationTests
{
    [Fact]
    public void InspectsBoundedCandidatesWithoutReturningEquationContent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "equations.docx");
            File.WriteAllBytes(path, BuildPackage());
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;

            var result = new EquationRepairWordPackageOperation().Inspect(
                new EquationRepairInspectionRequest(
                    path,
                    fingerprint,
                    IncludeSource: false,
                    IncludeIssues: true,
                    MaxItems: 1
                )
            );

            Assert.Equal(
                EquationRepairWordPackageContract.InspectContract,
                result.OperationContract
            );
            Assert.Equal(2, result.CandidateCount);
            Assert.Single(result.Candidates);
            Assert.True(result.CandidatesTruncated);
            Assert.Null(result.Candidates[0].PartUri);
            Assert.Null(result.Candidates[0].ParentElementOrdinal);
            Assert.NotEmpty(result.Issues!);
            Assert.False(result.SensitiveEquationTextReturned);
            Assert.False(result.RawOmmlReturned);
            Assert.False(result.MutationPerformed);
            Assert.False(result.WordOpened);
            Assert.DoesNotContain(
                "confidential",
                WordToolkitOperationJson.Serialize(result),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PlansAndAtomicallyAppliesOnlyAfterSchemaErrorsDecrease()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "equations.docx");
            File.WriteAllBytes(path, BuildPackage());
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var operation = new EquationRepairWordPackageOperation(
                new ImprovingValidator()
            );
            var inspected = operation.Inspect(new EquationRepairInspectionRequest(
                path,
                before.Fingerprint,
                IncludeSource: true,
                MaxItems: 20
            ));
            var commands = inspected.Candidates.Select(candidate =>
                new EquationRepairCommandRequest(
                    candidate.RepairKind,
                    candidate.Id,
                    candidate.Fingerprint
                )
            ).ToArray();

            var plan = operation.Plan(new EquationRepairPlanRequest(
                path,
                before.Fingerprint,
                commands,
                IncludeDetails: true
            ));
            var applied = operation.Apply(new EquationRepairApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                commands,
                KeepBackup: true
            ));

            Assert.True(plan.CanApply);
            Assert.True(plan.MicrosoftSchemaErrorsReduced);
            Assert.True(plan.EngineValidation.Passed);
            Assert.NotNull(plan.Candidates);
            Assert.NotNull(plan.ChangedParts);
            Assert.True(applied.Applied);
            Assert.True(applied.MicrosoftSchemaErrorsReduced);
            Assert.NotNull(applied.BackupPath);
            Assert.True(File.Exists(applied.BackupPath));
            Assert.Equal(before.Fingerprint, reader.Read(applied.BackupPath!).Fingerprint);
            Assert.Equal(plan.ResultPackageFingerprint, reader.Read(path).Fingerprint);
            Assert.Empty(operation.Inspect(new EquationRepairInspectionRequest(
                path,
                plan.ResultPackageFingerprint
            )).Candidates);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BlocksApplyWithoutValidatorOrWithoutSchemaImprovement()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "equations.docx");
            File.WriteAllBytes(path, BuildPackage());
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;
            var noValidator = new EquationRepairWordPackageOperation();
            var candidate = Assert.Single(
                noValidator.Inspect(new EquationRepairInspectionRequest(
                    path,
                    fingerprint,
                    MaxItems: 1
                )).Candidates
            );
            var commands = new[]
            {
                new EquationRepairCommandRequest(
                    candidate.RepairKind,
                    candidate.Id,
                    candidate.Fingerprint
                ),
            };
            var unavailablePlan = noValidator.Plan(new EquationRepairPlanRequest(
                path,
                fingerprint,
                commands
            ));
            Assert.Contains(
                "schema_validator_unavailable",
                unavailablePlan.ApplyBlockedReasons
            );
            var unavailable = Assert.Throws<WordToolkitOperationException>(() =>
                noValidator.Apply(new EquationRepairApplyRequest(
                    path,
                    fingerprint,
                    unavailablePlan.PlanId,
                    commands
                ))
            );
            Assert.Equal("VALIDATOR_REQUIRED", unavailable.Code);

            var noImprovement = new EquationRepairWordPackageOperation(
                new NoImprovementValidator()
            );
            var blockedPlan = noImprovement.Plan(new EquationRepairPlanRequest(
                path,
                fingerprint,
                commands
            ));
            Assert.Contains(
                "microsoft_schema_errors_not_reduced",
                blockedPlan.ApplyBlockedReasons
            );
            var blocked = Assert.Throws<WordToolkitOperationException>(() =>
                noImprovement.Apply(new EquationRepairApplyRequest(
                    path,
                    fingerprint,
                    blockedPlan.PlanId,
                    commands
                ))
            );
            Assert.Equal("OOXML_SCHEMA_INVALID", blocked.Code);
            Assert.Equal(fingerprint, new OpcPackageReader().Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProtectedRepairRequiresExactPlanAuthorizationAndDenialIsByteExact()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "protected-equations.docx");
            File.WriteAllBytes(
                path,
                BuildPackage(
                    "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\"/>"
                )
            );
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var operation = new EquationRepairWordPackageOperation(
                new ImprovingValidator()
            );
            var candidate = Assert.Single(
                operation.Inspect(new EquationRepairInspectionRequest(
                    path,
                    before.Fingerprint,
                    MaxItems: 1
                )).Candidates
            );
            var commands = new[]
            {
                new EquationRepairCommandRequest(
                    candidate.RepairKind,
                    candidate.Id,
                    candidate.Fingerprint
                ),
            };
            var plan = operation.Plan(new EquationRepairPlanRequest(
                path,
                before.Fingerprint,
                commands
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
                operation.Apply(new EquationRepairApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    commands,
                    KeepBackup: true,
                    ProtectedEditAuthorization: "werplan_wrong"
                ))
            );

            Assert.Equal("EDIT_POLICY_BLOCKED", denied.Code);
            var details = Assert.IsType<EquationRepairEditPolicyBlockDetails>(
                denied.Details
            );
            Assert.Equal(plan.PlanId, details.PlanId);
            Assert.Equal(
                ["protected_document_edit_not_authorized"],
                details.BlockCodes
            );
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));

            var applied = operation.Apply(new EquationRepairApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                commands,
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
    public void MalformedProtectionCannotBeOverriddenAndDoesNotLeakAuthorization()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "malformed-protection.docx");
            File.WriteAllBytes(
                path,
                BuildPackage(
                    "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\" w:bogus=\"x\"/>"
                )
            );
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var operation = new EquationRepairWordPackageOperation(
                new ImprovingValidator()
            );
            var candidate = Assert.Single(
                operation.Inspect(new EquationRepairInspectionRequest(
                    path,
                    before.Fingerprint,
                    MaxItems: 1
                )).Candidates
            );
            var commands = new[]
            {
                new EquationRepairCommandRequest(
                    candidate.RepairKind,
                    candidate.Id,
                    candidate.Fingerprint
                ),
            };
            var plan = operation.Plan(new EquationRepairPlanRequest(
                path,
                before.Fingerprint,
                commands
            ));

            Assert.True(plan.Protection.HasMalformedProtectionMetadata);
            Assert.Null(plan.ProtectionAuthorizationId);
            Assert.Empty(plan.RequiredAuthorizations);
            Assert.Contains("protection_metadata_malformed", plan.ApplyBlockedReasons);
            var beforeBytes = File.ReadAllBytes(path);

            var denied = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new EquationRepairApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    commands,
                    ProtectedEditAuthorization: plan.PlanId
                ))
            );

            Assert.Equal("EDIT_POLICY_BLOCKED", denied.Code);
            var details = Assert.IsType<EquationRepairEditPolicyBlockDetails>(
                denied.Details
            );
            Assert.Equal(["protection_metadata_malformed"], details.BlockCodes);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StrictJsonRejectsUnknownAndDuplicateFieldsBeforeFilesystemAccess()
    {
        var unknown = Assert.Throws<WordToolkitOperationException>(() =>
            EquationRepairOperationJson.ParseInspectRequest(
                """
                {"local_path":"Z:\\missing.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","unknown":true}
                """
            )
        );
        Assert.Equal("INVALID_INPUT", unknown.Code);
        var duplicate = Assert.Throws<WordToolkitOperationException>(() =>
            EquationRepairOperationJson.ParsePlanRequest(
                """
                {"local_path":"x.docx","local_path":"y.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","commands":[]}
                """
            )
        );
        Assert.Equal("INVALID_INPUT", duplicate.Code);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-equation-repair-operation-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] BuildPackage(string? protectionXml = null)
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
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
                  <w:body><w:p><m:oMath><m:f>
                    <m:fPr><m:type m:val="bar"/></m:fPr>
                    <m:fPr><m:type m:val="bar"/></m:fPr>
                    <m:num><m:r><m:rPr><m:sty m:val="b"/><m:sty m:val="b"/></m:rPr><m:t>confidential</m:t></m:r></m:num>
                    <m:den><m:r><m:t>2</m:t></m:r></m:den>
                  </m:f></m:oMath></w:p></w:body>
                </w:document>
                """
            );
            if (protectionXml is not null)
            {
                Add(
                    archive,
                    "word/_rels/document.xml.rels",
                    """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdSettings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
                    </Relationships>
                    """
                );
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

    private sealed class ImprovingValidator : IWordPackageCandidateValidator
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
            BaselineErrorCount: 2,
            CandidateErrorCount: 0,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues: Array.Empty<WordPackageValidationIssue>()
        );
    }

    private sealed class NoImprovementValidator : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        ) => new(
            Performed: true,
            CandidateValid: false,
            NoNewErrors: true,
            ErrorCount: 0,
            BaselineErrorCount: 2,
            CandidateErrorCount: 2,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues: Array.Empty<WordPackageValidationIssue>()
        );
    }
}
