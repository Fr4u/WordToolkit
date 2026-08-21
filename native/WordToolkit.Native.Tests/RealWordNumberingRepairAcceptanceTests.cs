using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordNumberingRepairAcceptanceTests
{
    [Fact]
    public async Task RestartedTailMatchesRealWordAndReadOnlyOracleDoesNotResave()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_NUMBERING_REPAIR_TEST"
            ),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-numbering-repair-oracle-{Guid.NewGuid():N}.docx"
        );
        CreatePackage(path);
        try
        {
            var originalHash = SHA256.HashData(File.ReadAllBytes(path));
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var beforeSemantic = new WordSemanticProjector().Project(before);
            var target = beforeSemantic.Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).ElementAt(1);
            var operation = new NumberingRepairWordPackageOperation(
                NativeExtensionHost.CandidateValidator
            );
            var plan = operation.Plan(
                new NumberingRepairPlanRequest(
                    path,
                    before.Fingerprint,
                    target.Id.Value,
                    ExpectedNumberId: 5,
                    ExpectedLevelIndex: 0,
                    StartValue: 7
                )
            );

            Assert.True(plan.CanApply);
            Assert.Equal(3, plan.AffectedParagraphCount);
            var applied = operation.Apply(
                new NumberingRepairApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    target.Id.Value,
                    5,
                    0,
                    7,
                    KeepBackup: false
                )
            );
            Assert.True(applied.Applied);
            Assert.Equal(plan.ResultPackageFingerprint, applied.PackageFingerprint);
            var repairedHash = SHA256.HashData(File.ReadAllBytes(path));
            Assert.NotEqual(originalHash, repairedHash);

            using (var document = WordprocessingDocument.Open(path, false))
            {
                Assert.Empty(
                    new OpenXmlValidator(FileFormatVersions.Microsoft365)
                        .Validate(document)
                );
            }
            var after = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(after);
            var styles = new WordStyleGraphBuilder().Build(after, semantic);
            var numbering = new WordNumberingGraphBuilder().Build(after, semantic, styles);
            var sequence = new WordListSequenceGraphBuilder().Build(
                after,
                semantic,
                styles,
                numbering
            );
            var engineValues = sequence.Items.Select(item => item.CounterValue).ToArray();
            var engineLabels = sequence.Items.Select(item => item.Label).ToArray();

            await using var host = new WordComHost();
            var wordValues = new List<long>();
            var wordLabels = new List<string>();
            await host.InvokeAsync(
                application =>
                {
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
                        for (var index = 1; index <= (int)document.Paragraphs.Count; index++)
                        {
                            dynamic paragraph = document.Paragraphs[index];
                            if (Convert.ToInt32(paragraph.Range.ListFormat.ListType) == 0)
                            {
                                continue;
                            }
                            wordValues.Add(Convert.ToInt64(paragraph.Range.ListFormat.ListValue));
                            wordLabels.Add((string)paragraph.Range.ListFormat.ListString);
                        }
                    }
                    finally
                    {
                        document.Close(0);
                    }
                    return true;
                },
                launchIfMissing: true
            );

            Assert.Equal(new long?[] { 1, 7, 8, 9 }, engineValues);
            Assert.Equal(new[] { "1.", "7.", "8.", "9." }, engineLabels);
            Assert.Equal(engineValues.Select(value => value!.Value), wordValues);
            Assert.Equal(engineLabels.Select(value => value!), wordLabels);
            Assert.Equal(repairedHash, SHA256.HashData(File.ReadAllBytes(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreatePackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/></Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/numbering.xml",
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
              <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
            </w:numbering>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
              <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>one</w:t></w:r></w:p>
              <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>two</w:t></w:r></w:p>
              <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>three</w:t></w:r></w:p>
              <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>four</w:t></w:r></w:p>
              <w:sectPr/>
            </w:body></w:document>
            """
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }
}
