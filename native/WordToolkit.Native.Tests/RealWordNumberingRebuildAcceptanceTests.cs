using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordNumberingRebuildAcceptanceTests
{
    [Fact]
    public async Task ReconstructedMultilevelListMatchesRealWordAndRendersWithoutResave()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_NUMBERING_REBUILD_TEST"
            ),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-numbering-rebuild-word-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "reconstructed.docx");
        var pdfPath = Path.Combine(directory, "reconstructed.pdf");
        var artifactDirectory = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_REAL_WORD_NUMBERING_REBUILD_ARTIFACT_DIR"
        );
        CreatePackage(path);
        try
        {
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var paragraphs = semantic.Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ToArray();
            var operation = new NumberingRebuildWordPackageOperation(
                NativeExtensionHost.CandidateValidator
            );
            var candidates = operation.Inspect(new NumberingRebuildInspectRequest(
                path,
                before.Fingerprint,
                paragraphs.Select(item => item.Id.Value).ToArray()
            )).Candidates;
            var levels = new[] { 0, 1, 1, 0 };
            var commands = new[]
            {
                new WordNumberingRebuildCommand(
                    "word-outline",
                    WordNumberingRebuildMultiLevelKind.Multilevel,
                    RestartAfterSectionBreak: true,
                    Levels:
                    [
                        new WordNumberingRebuildLevel(
                            0,
                            1,
                            WordNumberingRebuildFormat.Decimal,
                            "%1."
                        ),
                        new WordNumberingRebuildLevel(
                            1,
                            1,
                            WordNumberingRebuildFormat.LowerLetter,
                            "%1.%2)"
                        ),
                    ],
                    Targets: paragraphs.Select((paragraph, index) =>
                        new WordNumberingRebuildTarget(
                            paragraph.Id,
                            candidates[index].CandidateFingerprint,
                            levels[index]
                        )
                    ).ToArray()
                ),
            };
            var plan = operation.Plan(new NumberingRebuildPlanRequest(
                path,
                before.Fingerprint,
                commands
            ));
            Assert.True(plan.CanApply);
            Assert.True(plan.NumberingPartCreated);
            var applied = operation.Apply(new NumberingRebuildApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                commands,
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
            var appliedHash = SHA256.HashData(File.ReadAllBytes(path));
            var after = reader.Read(path);
            var afterSemantic = new WordSemanticProjector().Project(after);
            var styles = new WordStyleGraphBuilder().Build(after, afterSemantic);
            var numbering = new WordNumberingGraphBuilder().Build(
                after,
                afterSemantic,
                styles
            );
            var sequences = new WordListSequenceGraphBuilder().Build(
                after,
                afterSemantic,
                styles,
                numbering
            );
            var engineLabels = sequences.Items.Select(item => item.Label!).ToArray();
            Assert.Equal(new[] { "1.", "1.a)", "1.b)", "2." }, engineLabels);

            await using var host = new WordComHost();
            var wordLabels = new List<string>();
            var wordVersion = string.Empty;
            var wordBuild = string.Empty;
            await host.InvokeAsync(
                application =>
                {
                    var previousSecurity = Convert.ToInt32(application.AutomationSecurity);
                    var previousAlerts = Convert.ToInt32(application.DisplayAlerts);
                    dynamic? document = null;
                    try
                    {
                        wordVersion = Convert.ToString(application.Version) ?? string.Empty;
                        wordBuild = Convert.ToString(application.Build) ?? string.Empty;
                        application.AutomationSecurity = 3;
                        application.DisplayAlerts = 0;
                        document = application.Documents.Open(
                            FileName: path,
                            ConfirmConversions: false,
                            ReadOnly: true,
                            AddToRecentFiles: false,
                            Visible: false,
                            OpenAndRepair: false
                        );
                        document.Repaginate();
                        for (var index = 1; index <= (int)document.Paragraphs.Count; index++)
                        {
                            dynamic paragraph = document.Paragraphs[index];
                            if (Convert.ToInt32(paragraph.Range.ListFormat.ListType) != 0)
                            {
                                wordLabels.Add((string)paragraph.Range.ListFormat.ListString);
                            }
                        }
                        document.ExportAsFixedFormat(
                            OutputFileName: pdfPath,
                            ExportFormat: 17,
                            OpenAfterExport: false,
                            OptimizeFor: 0,
                            Range: 0,
                            Item: 0,
                            IncludeDocProps: true,
                            KeepIRM: true,
                            CreateBookmarks: 1,
                            DocStructureTags: true,
                            BitmapMissingFonts: true,
                            UseISO19005_1: false
                        );
                    }
                    finally
                    {
                        if (document is not null)
                        {
                            document.Close(0);
                        }
                        application.DisplayAlerts = previousAlerts;
                        application.AutomationSecurity = previousSecurity;
                    }
                    return true;
                },
                launchIfMissing: true
            );

            Assert.Equal(engineLabels, wordLabels);
            Assert.Equal(appliedHash, SHA256.HashData(File.ReadAllBytes(path)));
            var pdf = File.ReadAllBytes(pdfPath);
            Assert.True(pdf.Length > 1_000);
            Assert.True(pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8));
            if (!string.IsNullOrWhiteSpace(artifactDirectory))
            {
                var fullArtifactDirectory = Path.GetFullPath(artifactDirectory);
                Directory.CreateDirectory(fullArtifactDirectory);
                var docxArtifact = Path.Combine(
                    fullArtifactDirectory,
                    "numbering-rebuild-word-proof.docx"
                );
                var pdfArtifact = Path.Combine(
                    fullArtifactDirectory,
                    "numbering-rebuild-word-proof.pdf"
                );
                File.Copy(path, docxArtifact, overwrite: false);
                File.Copy(pdfPath, pdfArtifact, overwrite: false);
                File.WriteAllText(
                    Path.Combine(
                        fullArtifactDirectory,
                        "numbering-rebuild-word-proof.json"
                    ),
                    JsonSerializer.Serialize(
                        new
                        {
                            word_version = wordVersion,
                            word_build = wordBuild,
                            package_fingerprint = after.Fingerprint,
                            docx_sha256 = Convert.ToHexString(appliedHash).ToLowerInvariant(),
                            pdf_sha256 = Convert.ToHexString(SHA256.HashData(pdf))
                                .ToLowerInvariant(),
                            engine_labels = engineLabels,
                            word_labels = wordLabels,
                            microsoft_open_xml_valid = true,
                            source_unchanged_after_word_render = true,
                        },
                        new JsonSerializerOptions { WriteIndented = true }
                    ),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                );
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml",
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        WriteEntry(archive, "_rels/.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        WriteEntry(archive, "word/_rels/document.xml.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");
        WriteEntry(archive, "word/document.xml",
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Chapter</w:t></w:r></w:p><w:p><w:r><w:t>Child A</w:t></w:r></w:p><w:p><w:r><w:t>Child B</w:t></w:r></w:p><w:p><w:r><w:t>Next chapter</w:t></w:r></w:p><w:sectPr/></w:body></w:document>");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }
}
