using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordNumberingSequenceAcceptanceTests
{
    [Fact]
    public async Task SequenceExecutorMatchesRealWordListValuesAndLabels()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_NUMBERING_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-numbering-oracle-{Guid.NewGuid():N}.docx"
        );
        CreateNumberingPackage(path);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        try
        {
            using (var document = WordprocessingDocument.Open(path, false))
            {
                Assert.Empty(
                    new OpenXmlValidator(FileFormatVersions.Microsoft365)
                        .Validate(document)
                );
            }
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var styles = new WordStyleGraphBuilder().Build(package, semantic);
            var numbering = new WordNumberingGraphBuilder().Build(
                package,
                semantic,
                styles
            );
            var sequence = new WordListSequenceGraphBuilder().Build(
                package,
                semantic,
                styles,
                numbering
            );
            var engineValues = sequence.Items.Select(item => item.CounterValue).ToArray();
            var engineLabels = sequence.Items.Select(item => item.Label).ToArray();

            await using var host = new WordComHost();
            var wordValues = new List<long>();
            var wordLabels = new List<string>();
            var wordVersion = string.Empty;
            var wordBuild = string.Empty;
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

            Assert.Equal(new long?[] { 1, 9, 10, 2, 9, 1, 9 }, engineValues);
            Assert.Equal(
                new[] { "1.", "1.i", "1.j", "2.", "2.i", "1.", "1.i" },
                engineLabels
            );
            Assert.Equal(engineValues.Length, wordValues.Count);
            Assert.Equal(engineLabels.Select(value => value!), wordLabels);
            Assert.Equal(engineValues.Select(value => value!.Value), wordValues);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Console.WriteLine($"Qualified Word {wordVersion} build {wordBuild}.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateNumberingPackage(string path)
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
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/numbering.xml",
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" mc:Ignorable="w15">
              <w:abstractNum w:abstractNumId="1" w15:restartNumberingAfterBreak="1">
                <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl>
                <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%1.%2"/></w:lvl>
              </w:abstractNum>
              <w:num w:numId="5">
                <w:abstractNumId w:val="1"/>
                <w:lvlOverride w:ilvl="1">
                  <w:startOverride w:val="3"/>
                  <w:lvl w:ilvl="1"><w:start w:val="9"/><w:numFmt w:val="lowerLetter"/><w:lvlRestart w:val="0"/><w:lvlText w:val="%1.%2"/></w:lvl>
                </w:lvlOverride>
              </w:num>
            </w:numbering>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>level zero one</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>level one three</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>level one four</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>level zero two</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>level one restarted</w:t></w:r></w:p>
                <w:p><w:pPr><w:sectPr/></w:pPr><w:r><w:t>section boundary</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>level zero after section</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>level one after section</w:t></w:r></w:p>
                <w:sectPr/>
              </w:body>
            </w:document>
            """
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
