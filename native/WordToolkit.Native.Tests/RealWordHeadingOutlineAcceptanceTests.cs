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

public sealed class RealWordHeadingOutlineAcceptanceTests
{
    [Fact]
    public async Task EngineOutlineLevelsMatchRealWordAcrossMainAndHeaderStories()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_HEADING_OUTLINE_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-heading-outline-oracle-{Guid.NewGuid():N}.docx"
        );
        CreatePackage(path);
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
            var outline = new WordOutlineGraphBuilder().Build(
                package,
                semantic,
                styles
            );
            var engineLevels = outline.Paragraphs.ToDictionary(
                paragraph => ParagraphMarker(semantic, paragraph.ParagraphNodeId),
                paragraph => paragraph.Status == WordOutlineResolutionStatus.Heading
                    ? paragraph.Level!.Value
                    : 10,
                StringComparer.Ordinal
            );
            Assert.Equal(2, outline.StoryCount);
            var expectedLevels = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["MAIN-H1"] = 1,
                ["MAIN-H2"] = 2,
                ["MAIN-H9"] = 9,
                ["MAIN-BODY-DIRECT"] = 10,
                ["MAIN-BODY-IMPLICIT"] = 10,
                ["HEADER-H1"] = 1,
            };
            Assert.Equal(expectedLevels.Count, engineLevels.Count);
            Assert.All(expectedLevels, item =>
                Assert.Equal(item.Value, engineLevels[item.Key])
            );

            await using var host = new WordComHost();
            var wordLevels = new Dictionary<string, int>(StringComparer.Ordinal);
            var wordVersion = string.Empty;
            var wordBuild = string.Empty;
            await host.InvokeAsync(
                application =>
                {
                    wordVersion = Convert.ToString(application.Version) ?? string.Empty;
                    wordBuild = Convert.ToString(application.Build) ?? string.Empty;
                    var originalAutomationSecurity = Convert.ToInt32(
                        application.AutomationSecurity
                    );
                    application.AutomationSecurity = 3;
                    try
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
                            foreach (var storyType in new[] { 1, 7 })
                            {
                                dynamic? story = document.StoryRanges.Item(storyType);
                                while (story is not null)
                                {
                                    for (
                                        var index = 1;
                                        index <= Convert.ToInt32(story.Paragraphs.Count);
                                        index++
                                    )
                                    {
                                        dynamic paragraph = story.Paragraphs[index];
                                        var marker = Convert.ToString(paragraph.Range.Text)?
                                            .Trim('\r', '\a');
                                        if (
                                            marker is not null
                                            && engineLevels.ContainsKey(marker)
                                        )
                                        {
                                            wordLevels.Add(
                                                marker,
                                                Convert.ToInt32(paragraph.OutlineLevel)
                                            );
                                        }
                                    }
                                    story = story.NextStoryRange;
                                }
                            }
                        }
                        finally
                        {
                            document.Close(0);
                        }
                    }
                    finally
                    {
                        application.AutomationSecurity = originalAutomationSecurity;
                    }
                    return true;
                },
                launchIfMissing: true
            );

            Assert.Equal(engineLevels.Count, wordLevels.Count);
            Assert.All(engineLevels, item =>
                Assert.Equal(item.Value, wordLevels[item.Key])
            );
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Console.WriteLine(
                $"Qualified heading outline against Word {wordVersion} build {wordBuild}."
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ParagraphMarker(
        WordSemanticDocument semantic,
        SemanticNodeId paragraphId
    )
    {
        Assert.True(semantic.TryGetNode(paragraphId, out var paragraph));
        Assert.NotNull(paragraph);
        return string.Concat(
            paragraph.DescendantsAndSelf()
                .Where(node => node.Kind == WordSemanticNodeKind.Text)
                .Select(node => node.Text)
        );
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
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
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
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/styles.xml",
            """
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="H1"><w:name w:val="Nazwa bez heurystyki"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
              <w:style w:type="paragraph" w:styleId="H2"><w:name w:val="Dziedziczony"/><w:basedOn w:val="H1"/><w:pPr><w:outlineLvl w:val="1"/></w:pPr></w:style>
            </w:styles>
            """
        );
        WriteEntry(
            archive,
            "word/header1.xml",
            """
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:p><w:pPr><w:pStyle w:val="H1"/></w:pPr><w:r><w:t>HEADER-H1</w:t></w:r></w:p>
            </w:hdr>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p><w:pPr><w:pStyle w:val="H1"/></w:pPr><w:r><w:t>MAIN-H1</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="H2"/></w:pPr><w:r><w:t>MAIN-H2</w:t></w:r></w:p>
                <w:p><w:pPr><w:outlineLvl w:val="8"/></w:pPr><w:r><w:t>MAIN-H9</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="H1"/><w:outlineLvl w:val="9"/></w:pPr><w:r><w:t>MAIN-BODY-DIRECT</w:t></w:r></w:p>
                <w:p><w:r><w:t>MAIN-BODY-IMPLICIT</w:t></w:r></w:p>
                <w:sectPr><w:headerReference w:type="default" r:id="rIdHeader"/><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
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
