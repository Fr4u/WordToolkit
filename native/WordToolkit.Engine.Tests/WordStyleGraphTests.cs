using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordStyleGraphTests
{
    [Fact]
    public void BuildsTypedStyleAndLatentStyleGraphs()
    {
        using var bytes = BuildPackage(
            StylesXml(
                """
                <w:docDefaults>
                  <w:rPrDefault><w:rPr><w:rFonts w:asciiTheme="minorHAnsi"/><w:sz w:val="22"/></w:rPr></w:rPrDefault>
                  <w:pPrDefault><w:pPr><w:jc w:val="left"/><w:spacing w:after="160"/></w:pPr></w:pPrDefault>
                </w:docDefaults>
                <w:latentStyles w:defLockedState="0" w:defUIPriority="99" w:count="2">
                  <w:lsdException w:name="Normal" w:qFormat="1" w:uiPriority="0"/>
                </w:latentStyles>
                <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                  <w:name w:val="Normal"/>
                  <w:pPr><w:spacing w:after="120"/><w:widowControl/></w:pPr>
                  <w:rPr><w:b w:val="0"/><w:sz w:val="20"/></w:rPr>
                </w:style>
                <w:style w:type="paragraph" w:customStyle="1" w:styleId="Heading1">
                  <w:name w:val="Heading 1"/><w:aliases w:val="H1, Major heading"/>
                  <w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:link w:val="Heading1Char"/>
                  <w:qFormat/><w:uiPriority w:val="9"/>
                  <w:pPr><w:outlineLvl w:val="0"/></w:pPr>
                  <w:rPr><w:b/><w:color w:val="123456"/></w:rPr>
                </w:style>
                <w:style w:type="character" w:default="1" w:styleId="DefaultParagraphFont">
                  <w:name w:val="Default Paragraph Font"/>
                </w:style>
                <w:style w:type="character" w:styleId="Heading1Char">
                  <w:name w:val="Heading 1 Char"/><w:basedOn w:val="DefaultParagraphFont"/><w:link w:val="Heading1"/>
                  <w:rPr><w:b/><w:sz w:val="28"/></w:rPr>
                </w:style>
                <w:style w:type="table" w:default="1" w:styleId="TableNormal">
                  <w:name w:val="Normal Table"/><w:tblPr><w:tblInd w:w="0" w:type="dxa"/></w:tblPr>
                </w:style>
                """
            ),
            includeEffectsPart: true
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordStyleGraphBuilder().Build(package, semantic);

        Assert.Equal("/word/styles.xml", graph.StylesPartUri);
        Assert.Equal("/word/stylesWithEffects.xml", graph.StylesWithEffectsPartUri);
        Assert.Equal(5, graph.Styles.Count);
        Assert.Equal("left", graph.DefaultParagraphProperties.Values["alignment"]);
        Assert.Equal("22", graph.DefaultRunProperties.Values["size_half_points"]);
        Assert.Equal("minorHAnsi", graph.DefaultRunProperties.Values["font_ascii_theme"]);
        Assert.Equal("Normal", graph.DefaultStyleIds[WordStyleType.Paragraph]);
        Assert.Equal(
            "DefaultParagraphFont",
            graph.DefaultStyleIds[WordStyleType.Character]
        );
        Assert.Equal("TableNormal", graph.DefaultStyleIds[WordStyleType.Table]);
        var latent = Assert.IsType<WordLatentStyles>(graph.LatentStyles);
        Assert.Equal(2, latent.DeclaredCount);
        Assert.False(latent.DefaultLocked);
        Assert.Equal(99, latent.DefaultUiPriority);
        Assert.True(Assert.Single(latent.Exceptions).QuickFormat);

        Assert.True(graph.TryGetStyle("Heading1", out var heading));
        Assert.NotNull(heading);
        Assert.Equal(WordStyleType.Paragraph, heading.Type);
        Assert.True(heading.IsCustom);
        Assert.True(heading.QuickFormat);
        Assert.Equal(9, heading.UiPriority);
        Assert.Equal(["H1", "Major heading"], heading.Aliases);
        Assert.Equal(["Normal", "Heading1"], heading.InheritanceChainStyleIds);
        Assert.True(heading.InheritanceResolvable);
        Assert.Equal("0", heading.ParagraphProperties.Values["outline_level"]);
        Assert.Equal("true", heading.RunProperties.Values["bold"]);
        Assert.Equal("123456", heading.RunProperties.Values["color_value"]);
        Assert.Empty(graph.Issues);

        Assert.True(graph.TryGetStyle("TableNormal", out var table));
        Assert.False(table!.TableProperties.IsFullyModeled);
        Assert.Contains("tblInd", table.TableProperties.UnmodeledElements);
    }

    [Fact]
    public void MissingStylesPartIsValidAndProducesAnEmptyGraph()
    {
        using var bytes = BuildPackage(stylesXml: null);
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordStyleGraphBuilder().Build(package, semantic);

        Assert.False(graph.HasStylesPart);
        Assert.Empty(graph.Styles);
        Assert.Empty(graph.DefaultStyleIds);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void ReportsUnresolvableInheritanceWithoutLosingTheStyleInventory()
    {
        using var bytes = BuildPackage(
            StylesXml(
                """
                <w:style w:type="paragraph" w:styleId="CycleA"><w:basedOn w:val="CycleB"/></w:style>
                <w:style w:type="paragraph" w:styleId="CycleB"><w:basedOn w:val="CycleA"/></w:style>
                <w:style w:type="table" w:styleId="Missing"><w:basedOn w:val="TableNormal"/></w:style>
                <w:style w:type="character" w:styleId="WrongType"><w:basedOn w:val="CycleA"/></w:style>
                <w:style w:type="paragraph" w:styleId="BadNext"><w:next w:val="MissingNext"/></w:style>
                """
            )
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordStyleGraphBuilder().Build(package, semantic);

        Assert.Equal(5, graph.Styles.Count);
        Assert.All(
            graph.Styles.Where(style => style.StyleId != "BadNext"),
            style => Assert.False(style.InheritanceResolvable)
        );
        Assert.True(graph.Styles.Single(style => style.StyleId == "BadNext").InheritanceResolvable);
        Assert.Equal(
            4,
            graph.Issues.Count(issue => issue.Code == "STYLE_INHERITANCE_UNRESOLVED")
        );
        Assert.Contains(graph.Issues, issue => issue.Code == "STYLE_NEXT_MISSING");
    }

    [Fact]
    public void RejectsDuplicateStyleIdsAndStyleLimits()
    {
        using var duplicateBytes = BuildPackage(
            StylesXml(
                """
                <w:style w:type="paragraph" w:styleId="Same"/>
                <w:style w:type="paragraph" w:styleId="Same"/>
                """
            )
        );
        var reader = new OpcPackageReader();
        var duplicatePackage = reader.Read(duplicateBytes);
        var duplicateSemantic = new WordSemanticProjector().Project(duplicatePackage);
        Assert.Throws<WordStyleProjectionException>(() =>
            new WordStyleGraphBuilder().Build(duplicatePackage, duplicateSemantic)
        );

        using var limitedBytes = BuildPackage(
            StylesXml(
                """
                <w:style w:type="paragraph" w:styleId="A"/>
                <w:style w:type="paragraph" w:styleId="B"/>
                """
            )
        );
        var limitedPackage = reader.Read(limitedBytes);
        var limitedSemantic = new WordSemanticProjector().Project(limitedPackage);
        Assert.Throws<WordStyleLimitException>(() =>
            new WordStyleGraphBuilder(
                new WordStyleGraphOptions { MaxStyles = 1 }
            ).Build(limitedPackage, limitedSemantic)
        );
    }

    [Fact]
    public void BuildsGraphsForEveryBundledDocxStylePart()
    {
        var fixtureDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures"
        );
        var files = Directory.EnumerateFiles(fixtureDirectory, "*.docx").ToArray();
        Assert.NotEmpty(files);
        var reader = new OpcPackageReader();
        foreach (var path in files)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var graph = new WordStyleGraphBuilder().Build(package, semantic);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            Assert.Equal(semantic.MainPartUri, graph.MainPartUri);
        }
    }

    private static MemoryStream BuildPackage(
        string? stylesXml,
        bool includeEffectsPart = false
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var styleOverrides = stylesXml is null
                ? string.Empty
                : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>";
            var effectsOverride = includeEffectsPart
                ? "<Override PartName=\"/word/stylesWithEffects.xml\" ContentType=\"application/vnd.ms-word.stylesWithEffects+xml\"/>"
                : string.Empty;
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {styleOverrides}{effectsOverride}
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
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>Styled text</w:t></w:r></w:p></w:body>
                </w:document>
                """
            );
            if (stylesXml is not null)
            {
                var effectsRelationship = includeEffectsPart
                    ? "<Relationship Id=\"rIdEffects\" Type=\"http://schemas.microsoft.com/office/2007/relationships/stylesWithEffects\" Target=\"stylesWithEffects.xml\"/>"
                    : string.Empty;
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                      {effectsRelationship}
                    </Relationships>
                    """
                );
                WriteEntry(archive, "word/styles.xml", stylesXml);
                if (includeEffectsPart)
                {
                    WriteEntry(archive, "word/stylesWithEffects.xml", StylesXml(string.Empty));
                }
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string StylesXml(string content) => $"""
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          {content}
        </w:styles>
        """;

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
