using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordEffectiveFormattingTests
{
    [Fact]
    public void ResolvesModeledHierarchyAndDirectFormattingWithProvenance()
    {
        using var bytes = BuildPackage(
            """
            <w:body>
              <w:p>
                <w:pPr><w:pStyle w:val="Heading1"/><w:jc w:val="right"/></w:pPr>
                <w:r>
                  <w:rPr><w:rStyle w:val="Emphasis"/><w:b w:val="0"/><w:sz w:val="24"/></w:rPr>
                  <w:t>Resolved</w:t>
                </w:r>
              </w:p>
            </w:body>
            """,
            """
            <w:docDefaults>
              <w:rPrDefault><w:rPr><w:b w:val="0"/><w:sz w:val="22"/></w:rPr></w:rPrDefault>
              <w:pPrDefault><w:pPr><w:spacing w:after="200"/></w:pPr></w:pPrDefault>
            </w:docDefaults>
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
              <w:pPr><w:jc w:val="left"/></w:pPr>
              <w:rPr><w:b/><w:rFonts w:ascii="Calibri"/></w:rPr>
            </w:style>
            <w:style w:type="paragraph" w:styleId="Heading1">
              <w:basedOn w:val="Normal"/>
              <w:pPr><w:spacing w:after="100"/></w:pPr>
              <w:rPr><w:b/><w:sz w:val="32"/></w:rPr>
            </w:style>
            <w:style w:type="character" w:default="1" w:styleId="DefaultParagraphFont"/>
            <w:style w:type="character" w:styleId="Emphasis">
              <w:basedOn w:val="DefaultParagraphFont"/>
              <w:rPr><w:i/><w:color w:val="C00000"/></w:rPr>
            </w:style>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var run = semantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Run
        );

        var result = new WordEffectiveFormattingResolver().Resolve(
            package,
            semantic,
            styles,
            run.Id
        );

        Assert.Equal("Heading1", result.ParagraphStyleId);
        Assert.Equal("Emphasis", result.CharacterStyleId);
        Assert.Equal("right", result.ParagraphProperties["alignment"].Value);
        Assert.Equal("100", result.ParagraphProperties["spacing_after_twips"].Value);
        Assert.Equal("false", result.RunProperties["bold"].Value);
        Assert.Equal("24", result.RunProperties["size_half_points"].Value);
        Assert.Equal("Calibri", result.RunProperties["font_ascii"].Value);
        Assert.Equal("true", result.RunProperties["italic"].Value);
        Assert.Equal("C00000", result.RunProperties["color_value"].Value);
        Assert.Equal(
            ["false", "true", "false", "false"],
            result.RunProperties["bold"].Contributions
                .Select(item => item.ResultingValue)
                .ToArray()
        );
        Assert.Equal(
            WordFormattingSourceKind.DirectRunFormatting,
            result.RunProperties["bold"].Contributions[^1].SourceKind
        );
        Assert.Empty(result.UnmodeledElements);
        Assert.Equal(
            ["application_defaults_for_unspecified_properties"],
            result.CoverageOmissions
        );
        Assert.Empty(result.CompatibilityWarnings);
        Assert.False(result.IsFullyResolved);
    }

    [Fact]
    public void UsesDefaultParagraphStyleWhenParagraphHasNoExplicitStyle()
    {
        using var bytes = BuildPackage(
            "<w:body><w:p><w:r><w:t>Default style</w:t></w:r></w:p></w:body>",
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
              <w:pPr><w:keepNext/></w:pPr><w:rPr><w:sz w:val="21"/></w:rPr>
            </w:style>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var paragraph = semantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
        );

        var result = new WordEffectiveFormattingResolver().Resolve(
            package,
            semantic,
            styles,
            paragraph.Id
        );

        Assert.Equal("Normal", result.ParagraphStyleId);
        Assert.Null(result.CharacterStyleId);
        Assert.Equal("true", result.ParagraphProperties["keep_with_next"].Value);
        Assert.Equal("21", result.RunProperties["size_half_points"].Value);
    }

    [Fact]
    public void ReportsMicrosoftDefaultTrueToggleCompatibilityBoundary()
    {
        using var bytes = BuildPackage(
            """
            <w:body><w:p><w:pPr><w:pStyle w:val="Child"/></w:pPr><w:r><w:t>Toggle</w:t></w:r></w:p></w:body>
            """,
            """
            <w:docDefaults><w:rPrDefault><w:rPr><w:b/></w:rPr></w:rPrDefault></w:docDefaults>
            <w:style w:type="paragraph" w:styleId="Base"><w:rPr><w:b/></w:rPr></w:style>
            <w:style w:type="paragraph" w:styleId="Child"><w:basedOn w:val="Base"/><w:rPr><w:b/></w:rPr></w:style>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var run = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Run);

        var result = new WordEffectiveFormattingResolver().Resolve(
            package,
            semantic,
            styles,
            run.Id
        );

        Assert.Single(result.CompatibilityWarnings);
        Assert.Contains(
            "word_default_true_toggle_compatibility",
            result.CoverageOmissions
        );
    }

    [Fact]
    public void MarksTableNumberingThemeAndUnmodeledLayersAsIncomplete()
    {
        using var bytes = BuildPackage(
            """
            <w:body><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
              <w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="7"/></w:numPr><w:pBdr><w:bottom w:val="single"/></w:pBdr></w:pPr>
              <w:rPr><w:rFonts w:asciiTheme="minorHAnsi"/></w:rPr>
            </w:style>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var run = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Run);

        var result = new WordEffectiveFormattingResolver().Resolve(
            package,
            semantic,
            styles,
            run.Id
        );

        Assert.Equal("7", result.ParagraphProperties["numbering_id"].Value);
        Assert.Equal("minorHAnsi", result.RunProperties["font_ascii_theme"].Value);
        Assert.Contains("conditional_table_style_properties", result.CoverageOmissions);
        Assert.Contains("numbering_level_properties", result.CoverageOmissions);
        Assert.Contains("theme_value_resolution", result.CoverageOmissions);
        Assert.Contains("unmodeled_property_elements", result.CoverageOmissions);
        Assert.Contains(result.UnmodeledElements, item => item.EndsWith(":pBdr"));
    }

    [Fact]
    public void RejectsMissingOrUnresolvableReferencedStyle()
    {
        using var missingBytes = BuildPackage(
            """<w:body><w:p><w:pPr><w:pStyle w:val="Missing"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p></w:body>""",
            """<w:style w:type="paragraph" w:styleId="Normal"/>"""
        );
        var reader = new OpcPackageReader();
        var missingPackage = reader.Read(missingBytes);
        var missingSemantic = new WordSemanticProjector().Project(missingPackage);
        var missingStyles = new WordStyleGraphBuilder().Build(
            missingPackage,
            missingSemantic
        );
        var missingRun = missingSemantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Run
        );
        Assert.Throws<WordFormattingResolutionException>(() =>
            new WordEffectiveFormattingResolver().Resolve(
                missingPackage,
                missingSemantic,
                missingStyles,
                missingRun.Id
            )
        );

        using var cycleBytes = BuildPackage(
            """<w:body><w:p><w:pPr><w:pStyle w:val="A"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p></w:body>""",
            """
            <w:style w:type="paragraph" w:styleId="A"><w:basedOn w:val="B"/></w:style>
            <w:style w:type="paragraph" w:styleId="B"><w:basedOn w:val="A"/></w:style>
            """
        );
        var cyclePackage = reader.Read(cycleBytes);
        var cycleSemantic = new WordSemanticProjector().Project(cyclePackage);
        var cycleStyles = new WordStyleGraphBuilder().Build(cyclePackage, cycleSemantic);
        var cycleRun = cycleSemantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Run
        );
        Assert.Throws<WordFormattingResolutionException>(() =>
            new WordEffectiveFormattingResolver().Resolve(
                cyclePackage,
                cycleSemantic,
                cycleStyles,
                cycleRun.Id
            )
        );
    }

    [Fact]
    public void ResolvesAHeaderRunAgainstItsOwnSourcePart()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures",
            "poi_diff_header_footer.docx"
        );
        var package = new OpcPackageReader().Read(path);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var run = semantic.Nodes.First(node =>
            node.Kind == WordSemanticNodeKind.Run
            && node.SourcePartUri == "/word/header1.xml"
        );

        var result = new WordEffectiveFormattingResolver().Resolve(
            package,
            semantic,
            styles,
            run.Id
        );

        Assert.Equal("/word/header1.xml", result.SourcePartUri);
        Assert.Equal(run.Id, result.NodeId);
        Assert.NotEqual(run.Id, result.ParagraphNodeId);
    }

    private static MemoryStream BuildPackage(string bodyXml, string stylesXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
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
                "word/document.xml",
                $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  {bodyXml}
                </w:document>
                """
            );
            WriteEntry(
                archive,
                "word/styles.xml",
                $"""
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  {stylesXml}
                </w:styles>
                """
            );
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the WordToolkit repository root."
        );
    }
}
