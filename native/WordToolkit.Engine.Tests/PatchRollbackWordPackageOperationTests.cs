using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Tests;

public sealed class PatchRollbackWordPackageOperationTests
{
    [Fact]
    public void DirectSdkPlanAndApplyRestoreExactPackageAndKeepRedoBackup()
    {
        using var fixture = Fixture.Create("before", "after");
        var operation = new PatchRollbackWordPackageOperation(new PassingValidator());
        var plan = operation.Plan(fixture.PlanRequest());

        Assert.Equal(PatchRollbackWordPackageContract.PlanContract, plan.OperationContract);
        Assert.StartsWith("wtrollback_", plan.RollbackPlanId);
        Assert.NotEqual(fixture.PatchId, plan.ReversePatchId);
        Assert.Equal(fixture.AfterFingerprint, plan.CurrentPackageFingerprint);
        Assert.Equal(fixture.BeforeFingerprint, plan.RestoredPackageFingerprint);
        Assert.True(plan.DefaultPolicy.CanRollback);
        Assert.Empty(plan.HardBlockCodes);
        Assert.False(plan.MutationPerformed);
        Assert.False(plan.WordOpened);

        var result = operation.Apply(fixture.ApplyRequest(plan.RollbackPlanId));

        Assert.Equal(PatchRollbackWordPackageContract.ApplyContract, result.OperationContract);
        Assert.True(result.RolledBack);
        Assert.False(result.NoOp);
        Assert.Equal(fixture.BeforeFingerprint, result.PackageFingerprint);
        Assert.Equal(
            fixture.BeforeFingerprint,
            new OpcPackageReader().Read(fixture.CurrentPath).Fingerprint
        );
        Assert.NotNull(result.BackupPath);
        Assert.Equal(
            fixture.AfterFingerprint,
            new OpcPackageReader().Read(result.BackupPath!).Fingerprint
        );
        fixture.Track(result.BackupPath!);
    }

    [Fact]
    public void PlanIsDestinationBoundAndRejectsStaleCurrentFingerprint()
    {
        using var fixture = Fixture.Create("before", "after");
        var operation = new PatchRollbackWordPackageOperation(new PassingValidator());
        var plan = operation.Plan(fixture.PlanRequest());
        var copy = fixture.NewPath("other.docx");
        File.Copy(fixture.CurrentPath, copy);

        var wrongPath = Assert.Throws<WordToolkitOperationException>(() =>
            operation.Apply(fixture.ApplyRequest(plan.RollbackPlanId) with
            {
                LocalPath = copy,
            })
        );
        Assert.Equal("PLAN_MISMATCH", wrongPath.Code);

        var stale = Assert.Throws<WordToolkitOperationException>(() =>
            operation.Plan(fixture.PlanRequest() with
            {
                ExpectedPackageFingerprint = fixture.BeforeFingerprint,
            })
        );
        Assert.Equal("VERSION_CONFLICT", stale.Code);
        Assert.Equal(
            fixture.AfterFingerprint,
            new OpcPackageReader().Read(fixture.CurrentPath).Fingerprint
        );
    }

    [Fact]
    public void DestinationBindingPreservesCaseOnCaseSensitivePlatforms()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = Fixture.Create("before", "after");
        var operation = new PatchRollbackWordPackageOperation(new PassingValidator());
        var lowerPath = fixture.NewPath("case.docx");
        var upperPath = fixture.NewPath("CASE.docx");
        File.Copy(fixture.CurrentPath, lowerPath);
        File.Copy(fixture.CurrentPath, upperPath);

        var lowerPlan = operation.Plan(fixture.PlanRequest() with
        {
            LocalPath = lowerPath,
        });
        var upperPlan = operation.Plan(fixture.PlanRequest() with
        {
            LocalPath = upperPath,
        });

        Assert.NotEqual(lowerPlan.RollbackPlanId, upperPlan.RollbackPlanId);
    }

    [Fact]
    public void ActiveContentRollbackNeedsOnlyItsExplicitAuthorization()
    {
        using var fixture = Fixture.Create(
            "same",
            "same",
            beforeMacro: [1, 2, 3],
            afterMacro: [1, 2, 4]
        );
        var operation = new PatchRollbackWordPackageOperation(new PassingValidator());
        var plan = operation.Plan(fixture.PlanRequest());

        Assert.False(plan.DefaultPolicy.CanRollback);
        Assert.Contains("allow_active_content_changes", plan.RequiredAuthorizations);
        var blocked = Assert.Throws<WordToolkitOperationException>(() =>
            operation.Apply(fixture.ApplyRequest(plan.RollbackPlanId))
        );
        Assert.Equal("PATCH_POLICY_BLOCKED", blocked.Code);

        var applied = operation.Apply(fixture.ApplyRequest(plan.RollbackPlanId) with
        {
            AllowActiveContentChanges = true,
            KeepBackup = false,
        });
        Assert.True(applied.RolledBack);
        Assert.Equal(fixture.BeforeFingerprint, applied.PackageFingerprint);
    }

    [Fact]
    public void NoOpRollbackWritesNothingAndNeedsNoValidator()
    {
        using var fixture = Fixture.Create("same", "same");
        var operation = new PatchRollbackWordPackageOperation();
        var plan = operation.Plan(fixture.PlanRequest());
        var beforeWriteTime = File.GetLastWriteTimeUtc(fixture.CurrentPath);

        Assert.True(plan.NoOp);
        Assert.True(plan.DefaultPolicy.CanRollback);
        Assert.Equal("no_changes", plan.OpenxmlSchemaValidation.NotPerformedReason);
        var result = operation.Apply(fixture.ApplyRequest(plan.RollbackPlanId));

        Assert.False(result.RolledBack);
        Assert.True(result.NoOp);
        Assert.False(result.MutationPerformed);
        Assert.Null(result.BackupPath);
        Assert.Equal(beforeWriteTime, File.GetLastWriteTimeUtc(fixture.CurrentPath));
    }

    [Fact]
    public void ChangedRollbackWithoutValidatorFailsClosed()
    {
        using var fixture = Fixture.Create("before", "after");
        var operation = new PatchRollbackWordPackageOperation();
        var plan = operation.Plan(fixture.PlanRequest());

        Assert.False(plan.DefaultPolicy.CanRollback);
        Assert.Contains("openxml_validation_not_performed", plan.HardBlockCodes);
        var blocked = Assert.Throws<WordToolkitOperationException>(() =>
            operation.Apply(fixture.ApplyRequest(plan.RollbackPlanId))
        );
        Assert.Equal("PATCH_POLICY_BLOCKED", blocked.Code);
        Assert.Equal(
            fixture.AfterFingerprint,
            new OpcPackageReader().Read(fixture.CurrentPath).Fingerprint
        );
    }

    [Fact]
    public void StrictJsonRejectsUnknownFieldsAndUsesSnakeCaseViews()
    {
        var operation = new PatchRollbackWordPackageOperation();
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                operation.Plan(null!)
            ).Code
        );
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(null!)
            ).Code
        );
        var request = PatchRollbackOperationJson.ParsePlanRequest(
            """
            {
              "local_path":"a.docx",
              "patch_path":"a.wtpatch",
              "expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "expected_patch_id":"wtpatch_abcdefghijklmnop",
              "view":"schema_errors"
            }
            """
        );
        Assert.Equal(PatchRollbackView.SchemaErrors, request.View);

        var error = Assert.Throws<WordToolkitOperationException>(() =>
            PatchRollbackOperationJson.ParsePlanRequest(
                """
                {
                  "local_path":"a.docx",
                  "patch_path":"a.wtpatch",
                  "expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "expected_patch_id":"wtpatch_abcdefghijklmnop",
                  "force":true
                }
                """
            )
        );
        Assert.Equal("INVALID_INPUT", error.Code);
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

    private sealed class Fixture : IDisposable
    {
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

        private Fixture(
            string stem,
            string currentPath,
            string patchPath,
            string beforeFingerprint,
            string afterFingerprint,
            string patchId
        )
        {
            Stem = stem;
            CurrentPath = currentPath;
            PatchPath = patchPath;
            BeforeFingerprint = beforeFingerprint;
            AfterFingerprint = afterFingerprint;
            PatchId = patchId;
            Track(currentPath);
            Track(patchPath);
        }

        public string Stem { get; }

        public string CurrentPath { get; }

        public string PatchPath { get; }

        public string BeforeFingerprint { get; }

        public string AfterFingerprint { get; }

        public string PatchId { get; }

        public static Fixture Create(
            string beforeText,
            string afterText,
            byte[]? beforeMacro = null,
            byte[]? afterMacro = null
        )
        {
            var stem = Path.Combine(
                Path.GetTempPath(),
                $"wordtoolkit-engine-rollback-{Guid.NewGuid():N}"
            );
            var extension = beforeMacro is null && afterMacro is null
                ? ".docx"
                : ".docm";
            var beforePath = stem + "-before" + extension;
            var currentPath = stem + "-current" + extension;
            var patchPath = stem + ".wtpatch";
            WriteDocument(beforePath, beforeText, beforeMacro);
            WriteDocument(currentPath, afterText, afterMacro);
            var reader = new OpcPackageReader();
            var projector = new WordSemanticProjector();
            var before = reader.Read(beforePath);
            var after = reader.Read(currentPath);
            var patch = new WordPackagePatchPlanner().Plan(
                before,
                projector.Project(before),
                after,
                projector.Project(after)
            ).Patch;
            using (var stream = File.Create(patchPath))
            {
                new OpcPackagePatchCodec().Write(stream, patch);
            }
            File.Delete(beforePath);
            return new Fixture(
                stem,
                currentPath,
                patchPath,
                before.Fingerprint,
                after.Fingerprint,
                patch.PatchId
            );
        }

        public PatchRollbackPlanRequest PlanRequest() => new(
            CurrentPath,
            PatchPath,
            AfterFingerprint,
            PatchId
        );

        public PatchRollbackApplyRequest ApplyRequest(string planId) => new(
            CurrentPath,
            PatchPath,
            AfterFingerprint,
            PatchId,
            planId
        );

        public string NewPath(string suffix)
        {
            var path = Stem + "-" + suffix;
            Track(path);
            return path;
        }

        public void Track(string path) => _paths.Add(path);

        public void Dispose()
        {
            foreach (var path in _paths)
            {
                try
                {
                    File.Delete(path);
                    File.Delete(path + ".wordtoolkit.lock");
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static void WriteDocument(
            string path,
            string text,
            byte[]? macro
        )
        {
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite
            );
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            Write(archive, "[Content_Types].xml", ContentTypes(macro is not null));
            Write(archive, "_rels/.rels", RootRelationships());
            Write(archive, "word/document.xml", DocumentXml(text));
            if (macro is not null)
            {
                Write(archive, "word/_rels/document.xml.rels", DocumentRelationships());
                Write(archive, "word/vbaProject.bin", macro);
            }
        }

        private static string ContentTypes(bool macro) =>
            "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
            + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
            + "<Default Extension='xml' ContentType='application/xml'/>"
            + (macro
                ? "<Default Extension='bin' ContentType='application/octet-stream'/>"
                    + "<Override PartName='/word/document.xml' ContentType='application/vnd.ms-word.document.macroEnabled.main+xml'/>"
                    + "<Override PartName='/word/vbaProject.bin' ContentType='application/vnd.ms-office.vbaProject'/>"
                : "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>")
            + "</Types>";

        private static string DocumentXml(string text) =>
            "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + $"<w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p><w:sectPr/></w:body>"
            + "</w:document>";

        private static string RootRelationships() =>
            "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
            + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
            + "</Relationships>";

        private static string DocumentRelationships() =>
            "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
            + "<Relationship Id='rIdVba' Type='http://schemas.microsoft.com/office/2006/relationships/vbaProject' Target='vbaProject.bin'/>"
            + "</Relationships>";

        private static void Write(ZipArchive archive, string name, string value) =>
            Write(archive, name, Encoding.UTF8.GetBytes(value));

        private static void Write(ZipArchive archive, string name, byte[] value)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using var target = entry.Open();
            target.Write(value);
        }
    }
}
