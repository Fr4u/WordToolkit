using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Tests;

public sealed class RelationshipRepairWordPackageOperationTests
{
    [Fact]
    public void InspectsPlansAndAtomicallyAppliesReviewedBatchWithoutTargetDisclosure()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-relationship-repair-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var operation = new RelationshipRepairWordPackageOperation(
                new PassingValidator()
            );
            var inspection = operation.Inspect(new RelationshipInspectionRequest(
                path,
                package.Fingerprint,
                IncludeDetails: true
            ));
            var dead = Assert.Single(inspection.Relationships);
            var orphan = Assert.Single(inspection.OrphanRelationshipParts);
            Assert.Equal("rIdDeadLink", dead.RelationshipId);
            Assert.True(dead.MarkupRemovalCandidate);
            Assert.False(inspection.ExternalTargetsReturned);
            Assert.False(inspection.RawXmlReturned);
            Assert.Null(dead.ResolvedTargetPartUri);

            var commands = new RelationshipRepairCommandRequest[]
            {
                new(
                    "remove_unreferenced_relationship",
                    dead.SourcePartUri,
                    dead.RelationshipId,
                    dead.Fingerprint,
                    null,
                    null
                ),
                new(
                    "remove_orphan_relationship_part",
                    null,
                    null,
                    null,
                    orphan.RelationshipPartUri,
                    orphan.EntrySha256
                ),
            };
            var plan = operation.Plan(new RelationshipRepairPlanRequest(
                path,
                package.Fingerprint,
                commands,
                IncludeDetails: true
            ));
            Assert.True(plan.EngineValidation.Passed);
            Assert.True(plan.RequiresExternalRelationshipAuthorization);
            Assert.True(plan.ApplyBlocked);
            Assert.Contains(
                "external_relationship_removal_requires_apply_authorization",
                plan.ApplyBlockedReasons
            );
            Assert.Equal(2, plan.CommandCount);
            Assert.Equal(2, plan.RemovedRelationshipCount);
            Assert.NotNull(plan.Actions);
            Assert.NotNull(plan.ChangedEntries);

            Assert.Equal(
                "EXTERNAL_RELATIONSHIP_AUTHORIZATION_REQUIRED",
                Assert.Throws<WordToolkitOperationException>(() => operation.Apply(
                    new RelationshipRepairApplyRequest(
                        path,
                        package.Fingerprint,
                        plan.PlanId,
                        commands
                    )
                )).Code
            );
            var applied = operation.Apply(new RelationshipRepairApplyRequest(
                path,
                package.Fingerprint,
                plan.PlanId,
                commands,
                AllowExternalRelationshipRemoval: true,
                KeepBackup: true
            ));
            Assert.True(applied.Applied);
            Assert.Equal(plan.ResultPackageFingerprint, applied.PackageFingerprint);
            Assert.NotNull(applied.BackupPath);
            Assert.True(File.Exists(applied.BackupPath));
            Assert.False(applied.ExternalTargetsReturned);
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
    public void JsonAndOperationFailClosedOnUnknownCrossKindAndMissingValidator()
    {
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                RelationshipRepairOperationJson.ParsePlanRequest(
                    """
                    {"local_path":"a.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","commands":[{"kind":"remove_orphan_relationship_part","relationship_part_uri":"/word/_rels/missing.xml.rels","expected_entry_sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}],"surprise":true}
                    """
                )
            ).Code
        );

        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-relationship-repair-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var package = new OpcPackageReader().Read(path);
            var noValidator = new RelationshipRepairWordPackageOperation();
            var inspection = noValidator.Inspect(new RelationshipInspectionRequest(
                path,
                package.Fingerprint
            ));
            var dead = Assert.Single(inspection.Relationships);
            var commands = new[]
            {
                new RelationshipRepairCommandRequest(
                    "remove_unreferenced_relationship",
                    dead.SourcePartUri,
                    dead.RelationshipId,
                    dead.Fingerprint,
                    null,
                    null
                ),
            };
            var plan = noValidator.Plan(new RelationshipRepairPlanRequest(
                path,
                package.Fingerprint,
                commands
            ));
            Assert.Contains("schema_validator_unavailable", plan.ApplyBlockedReasons);
            Assert.Equal(
                "VALIDATOR_REQUIRED",
                Assert.Throws<WordToolkitOperationException>(() => noValidator.Apply(
                    new RelationshipRepairApplyRequest(
                        path,
                        package.Fingerprint,
                        plan.PlanId,
                        commands,
                        AllowExternalRelationshipRemoval: true
                    )
                )).Code
            );

            var invalid = commands[0] with
            {
                RelationshipPartUri = "/word/_rels/document.xml.rels",
            };
            Assert.Equal(
                "INVALID_INPUT",
                Assert.Throws<WordToolkitOperationException>(() => noValidator.Plan(
                    new RelationshipRepairPlanRequest(
                        path,
                        package.Fingerprint,
                        [invalid]
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
            Add(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            Add(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Add(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body><w:p><w:r><w:drawing r:embed="rIdImage"/></w:r><w:r><w:t>tekst</w:t></w:r></w:p></w:body></w:document>
                """);
            Add(archive, "word/media/image1.png", "not really png");
            Add(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/><Relationship Id="rIdDeadLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://secret.example.invalid/path?token=never-return" TargetMode="External"/></Relationships>
                """);
            Add(archive, "word/_rels/missing.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdOrphan" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/></Relationships>
                """);
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
            BaselineErrorCount: 1,
            CandidateErrorCount: 0,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues: Array.Empty<WordPackageValidationIssue>()
        );
    }
}
