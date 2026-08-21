using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordRelationshipRepairAcceptanceTests
{
    [Fact]
    public async Task RepairedRelationshipPackageOpensReadOnlyInWordWithoutResave()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_RELATIONSHIP_REPAIR_TEST"
            ),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-relationship-repair-oracle-{Guid.NewGuid():N}.docx"
        );
        CreatePackage(path);
        try
        {
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var operation = new RelationshipRepairWordPackageOperation(
                NativeExtensionHost.CandidateValidator
            );
            var inspection = operation.Inspect(new RelationshipInspectionRequest(
                path,
                before.Fingerprint
            ));
            var dead = Assert.Single(inspection.Relationships);
            var orphan = Assert.Single(inspection.OrphanRelationshipParts);
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
                before.Fingerprint,
                commands
            ));
            var applied = operation.Apply(new RelationshipRepairApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                commands,
                AllowExternalRelationshipRemoval: true,
                KeepBackup: false
            ));
            Assert.True(applied.Applied);
            Assert.Equal(plan.ResultPackageFingerprint, applied.PackageFingerprint);

            using (var document = WordprocessingDocument.Open(path, false))
            {
                Assert.Empty(
                    new OpenXmlValidator(FileFormatVersions.Microsoft365)
                        .Validate(document)
                );
            }
            var after = reader.Read(path);
            Assert.DoesNotContain(after.Relationships, relationship =>
                relationship.Id == "rIdDeadLink"
            );
            Assert.DoesNotContain(after.Entries, entry =>
                entry.Name == "word/_rels/missing.xml.rels"
            );
            var repairedHash = SHA256.HashData(File.ReadAllBytes(path));

            await using var host = new WordComHost();
            var wordVersion = string.Empty;
            var wordBuild = string.Empty;
            var text = string.Empty;
            var hyperlinkCount = -1;
            await host.InvokeAsync(
                application =>
                {
                    wordVersion = Convert.ToString(application.Version) ?? string.Empty;
                    wordBuild = Convert.ToString(application.Build) ?? string.Empty;
                    dynamic document = application.Documents.Open(
                        FileName: path,
                        ConfirmConversions: false,
                        ReadOnly: true,
                        AddToRecentFiles: false,
                        Visible: false,
                        OpenAndRepair: false
                    );
                    try
                    {
                        text = ((string)document.Content.Text).TrimEnd('\r', '\a');
                        hyperlinkCount = Convert.ToInt32(document.Hyperlinks.Count);
                    }
                    finally
                    {
                        document.Close(0);
                    }
                    return true;
                },
                launchIfMissing: true
            );

            Assert.Equal("Relacja naprawiona.", text);
            Assert.Equal(0, hyperlinkCount);
            Assert.Equal(repairedHash, SHA256.HashData(File.ReadAllBytes(path)));
            Console.WriteLine($"Qualified Word {wordVersion} build {wordBuild}.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreatePackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """);
        WriteEntry(archive, "_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """);
        WriteEntry(archive, "word/document.xml", """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Relacja naprawiona.</w:t></w:r></w:p><w:sectPr/></w:body></w:document>
            """);
        WriteEntry(archive, "word/_rels/document.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdDeadLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.invalid/dead" TargetMode="External"/></Relationships>
            """);
        WriteEntry(archive, "word/_rels/missing.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
            """);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }
}
