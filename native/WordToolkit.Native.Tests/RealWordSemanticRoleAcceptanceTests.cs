using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordSemanticRoleAcceptanceTests
{
    [Fact]
    public async Task ConservativeRoleEvidenceSurvivesARealWordSave()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_SEMANTIC_ROLE_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-semantic-role-word-{Guid.NewGuid():N}.docx"
        );
        CreatePackage(path);
        try
        {
            AssertSchemaValid(path);
            var wordVersion = string.Empty;
            var wordBuild = string.Empty;
            await using (var host = new WordComHost())
            {
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
                                ReadOnly: false,
                                AddToRecentFiles: false,
                                Visible: false,
                                OpenAndRepair: false
                            );
                            try
                            {
                                document.Save();
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
            }

            AssertSchemaValid(path);
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var styles = new WordStyleGraphBuilder().Build(package, semantic);
            var controls = new WordContentControlBindingGraphBuilder().Build(
                package,
                semantic
            );
            var graph = new WordSemanticRoleGraphBuilder().Build(
                package,
                semantic,
                styles,
                controls
            );
            var evidence = graph.Candidates.ToDictionary(
                candidate => ParagraphText(semantic, candidate.ParagraphNodeId),
                candidate => candidate,
                StringComparer.Ordinal
            );

            Assert.Equal(3, evidence.Count);
            Assert.Equal(
                WordSemanticRoleClassification.LexicalCandidate,
                evidence["Theorem 1. WORD-LEXICAL"].Classification
            );
            Assert.Equal(
                WordSemanticRoleClassification.StyleConvention,
                evidence["WORD-STYLED"].Classification
            );
            Assert.Equal(
                WordSemanticRoleClassification.Declared,
                evidence["WORD-DECLARED"].Classification
            );
            Assert.DoesNotContain("Ordinary WORD-INLINE", evidence.Keys);
            Assert.All(evidence.Values, candidate =>
            {
                Assert.Equal(WordSemanticRoleKind.Theorem, candidate.Role);
                Assert.True(candidate.UsableAsSemanticRole);
            });
            Console.WriteLine(
                $"Qualified semantic-role evidence after Word {wordVersion} build {wordBuild} save."
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ParagraphText(
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

    private static void AssertSchemaValid(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        Assert.Empty(
            new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document)
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
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/styles.xml",
            """
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Theorem"><w:name w:val="Twierdzenie"/><w:basedOn w:val="Normal"/></w:style>
            </w:styles>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>Theorem 1. WORD-LEXICAL</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Theorem"/></w:pPr><w:r><w:t>WORD-STYLED</w:t></w:r></w:p>
                <w:sdt><w:sdtPr><w:tag w:val="wordtoolkit:role=theorem"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>WORD-DECLARED</w:t></w:r></w:p></w:sdtContent></w:sdt>
                <w:p><w:r><w:t>Ordinary </w:t></w:r><w:sdt><w:sdtPr><w:tag w:val="wordtoolkit:role=theorem"/></w:sdtPr><w:sdtContent><w:r><w:t>WORD-INLINE</w:t></w:r></w:sdtContent></w:sdt></w:p>
                <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
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
