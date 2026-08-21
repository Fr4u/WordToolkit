using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordNumberingGraphTests
{
    [Fact]
    public void BuildsTypedGraphAndResolvesInstanceOverrides()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="4">
                  <w:nsid w:val="FFFFFF89"/><w:multiLevelType w:val="multilevel"/><w:name w:val="Legal"/><w:tmpl w:val="D9842532"/>
                  <w:lvl w:ilvl="0" w:tplc="0409000F">
                    <w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:suff w:val="tab"/><w:lvlJc w:val="left"/>
                    <w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr><w:rPr><w:b/></w:rPr>
                  </w:lvl>
                  <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%2)"/></w:lvl>
                </w:abstractNum>
                <w:num w:numId="6">
                  <w:abstractNumId w:val="4"/>
                  <w:lvlOverride w:ilvl="0"><w:startOverride w:val="7"/></w:lvlOverride>
                  <w:lvlOverride w:ilvl="1"><w:lvl w:ilvl="1"><w:start w:val="3"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="%2."/></w:lvl></w:lvlOverride>
                </w:num>
                <w:numIdMacAtCleanup w:val="9"/>
                """
            )
        );
        var (package, semantic, styles) = ReadSnapshots(bytes);

        var graph = new WordNumberingGraphBuilder().Build(package, semantic, styles);

        Assert.Equal("/word/numbering.xml", graph.NumberingPartUri);
        Assert.Equal(9, graph.LastAssignedNumberId);
        var definition = Assert.Single(graph.AbstractDefinitions);
        Assert.Equal(4, definition.AbstractNumberId);
        Assert.Equal("FFFFFF89", definition.NamespaceId);
        Assert.Equal("multilevel", definition.MultiLevelType);
        Assert.Equal("Legal", definition.Name);
        Assert.Equal(2, definition.Levels.Count);
        Assert.Equal("720", definition.Levels[0].ParagraphProperties.Values["indent_left_twips"]);
        Assert.Equal("true", definition.Levels[0].RunProperties.Values["bold"]);
        Assert.Empty(graph.Issues);

        var first = graph.ResolveLevel(6, 0);
        Assert.Equal(4, first.EffectiveAbstractNumberId);
        Assert.Equal(7, first.EffectiveStart);
        Assert.Equal(WordNumberingStartSourceKind.InstanceStartOverride, first.StartSourceKind);
        Assert.Equal(WordNumberingLevelSourceKind.AbstractDefinition, first.LevelSourceKind);
        Assert.Equal("decimal", first.Level.NumberFormat);

        var second = graph.ResolveLevel(6, 1);
        Assert.Equal(3, second.EffectiveStart);
        Assert.Equal(WordNumberingLevelSourceKind.InstanceOverride, second.LevelSourceKind);
        Assert.Equal("upperRoman", second.Level.NumberFormat);
    }

    [Fact]
    public void ResolvesNumberingStyleIndirectionWithoutFlatteningTheChain()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="0"><w:numStyleLink w:val="ListStyle"/></w:abstractNum>
                <w:abstractNum w:abstractNumId="5"><w:styleLink w:val="ListStyle"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1)"/></w:lvl></w:abstractNum>
                <w:num w:numId="6"><w:abstractNumId w:val="0"/></w:num>
                <w:num w:numId="9"><w:abstractNumId w:val="5"/></w:num>
                """
            ),
            StylesXml(
                """
                <w:style w:type="numbering" w:styleId="ListStyle"><w:name w:val="List Style"/><w:pPr><w:numPr><w:numId w:val="9"/></w:numPr></w:pPr></w:style>
                """
            )
        );
        var (package, semantic, styles) = ReadSnapshots(bytes);

        var graph = new WordNumberingGraphBuilder().Build(package, semantic, styles);
        var resolution = graph.ResolveLevel(6, 0);

        Assert.Equal(0, resolution.RequestedAbstractNumberId);
        Assert.Equal(5, resolution.EffectiveAbstractNumberId);
        Assert.Equal([0, 5], resolution.AbstractNumberChain);
        Assert.Equal(["ListStyle"], resolution.NumberingStyleChain);
        Assert.Equal("%1)", resolution.Level.LevelText);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void ReportsBrokenReferencesAndUnsafeLevelShapes()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:numPicBullet w:numPicBulletId="8" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:pict><v:shape><v:imagedata r:id="rIdMissing"/></v:shape></w:pict></w:numPicBullet>
                <w:abstractNum w:abstractNumId="1">
                  <w:lvl w:ilvl="9"><w:pStyle w:val="MissingParagraph"/><w:lvlPicBulletId w:val="77"/></w:lvl>
                </w:abstractNum>
                <w:num w:numId="2"><w:abstractNumId w:val="99"/></w:num>
                <w:num w:numId="3"><w:abstractNumId w:val="1"/><w:lvlOverride w:ilvl="0"><w:lvl w:ilvl="1"/></w:lvlOverride></w:num>
                """
            )
        );
        var (package, semantic, styles) = ReadSnapshots(bytes);

        var graph = new WordNumberingGraphBuilder().Build(package, semantic, styles);

        Assert.Contains(graph.Issues, issue => issue.Code == "NUMBERING_LEVEL_OUT_OF_RANGE");
        Assert.Contains(graph.Issues, issue => issue.Code == "NUMBERING_LEVEL_STYLE_MISSING");
        Assert.Contains(graph.Issues, issue => issue.Code == "NUMBERING_PICTURE_BULLET_MISSING");
        Assert.Contains(graph.Issues, issue => issue.Code == "NUMBERING_PICTURE_RELATIONSHIP_MISSING");
        Assert.Contains(graph.Issues, issue => issue.Code == "NUMBERING_ABSTRACT_MISSING");
        Assert.Contains(graph.Issues, issue => issue.Code == "NUMBERING_OVERRIDE_LEVEL_MISMATCH");
        Assert.Throws<WordNumberingResolutionException>(() => graph.ResolveLevel(2, 0));
        Assert.Throws<WordNumberingResolutionException>(() => graph.ResolveLevel(3, 0));
    }

    [Fact]
    public void DetectsCircularNumberingStyleLinks()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:numStyleLink w:val="StyleA"/></w:abstractNum>
                <w:abstractNum w:abstractNumId="2"><w:numStyleLink w:val="StyleB"/></w:abstractNum>
                <w:num w:numId="11"><w:abstractNumId w:val="2"/></w:num>
                <w:num w:numId="12"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            StylesXml(
                """
                <w:style w:type="numbering" w:styleId="StyleA"><w:pPr><w:numPr><w:numId w:val="11"/></w:numPr></w:pPr></w:style>
                <w:style w:type="numbering" w:styleId="StyleB"><w:pPr><w:numPr><w:numId w:val="12"/></w:numPr></w:pPr></w:style>
                """
            )
        );
        var (package, semantic, styles) = ReadSnapshots(bytes);

        var graph = new WordNumberingGraphBuilder().Build(package, semantic, styles);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "NUMBERING_ABSTRACT_LINK_UNRESOLVED"
                && issue.Message.Contains("Circular", StringComparison.Ordinal)
        );
        Assert.Throws<WordNumberingResolutionException>(() => graph.ResolveLevel(11, 0));
    }

    [Fact]
    public void AppliesNumberingLevelBetweenParagraphStyleAndDirectFormatting()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="4"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr><w:rPr><w:b/></w:rPr></w:lvl></w:abstractNum>
                <w:num w:numId="6"><w:abstractNumId w:val="4"/></w:num>
                """
            ),
            documentBody:
                """
                <w:body><w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="6"/></w:numPr><w:ind w:left="1000"/></w:pPr><w:r><w:rPr><w:b w:val="0"/></w:rPr><w:t>Numbered</w:t></w:r></w:p></w:body>
                """
        );
        var (package, semantic, styles) = ReadSnapshots(bytes);
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            semantic,
            styles
        );
        var run = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Run);

        var formatting = new WordEffectiveFormattingResolver().Resolve(
            package,
            semantic,
            styles,
            numbering,
            run.Id
        );

        Assert.NotNull(formatting.Numbering);
        Assert.Equal(6, formatting.Numbering.NumberId);
        Assert.Equal("1000", formatting.ParagraphProperties["indent_left_twips"].Value);
        Assert.Equal("false", formatting.RunProperties["bold"].Value);
        Assert.Contains(
            formatting.ParagraphProperties["indent_left_twips"].Contributions,
            contribution => contribution.SourceKind == WordFormattingSourceKind.NumberingLevel
                && contribution.NumberId == 6
                && contribution.AbstractNumberId == 4
        );
        Assert.DoesNotContain("numbering_level_properties", formatting.CoverageOmissions);
    }

    [Fact]
    public void InfersNumberingLevelFromParagraphStyleMappingWhenIlvlIsAbsent()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="4">
                  <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:pStyle w:val="Heading1"/><w:lvlText w:val="%1"/><w:pPr><w:ind w:left="720"/></w:pPr></w:lvl>
                  <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:pStyle w:val="Heading2"/><w:lvlText w:val="%1.%2"/><w:pPr><w:ind w:left="1440"/></w:pPr></w:lvl>
                </w:abstractNum>
                <w:num w:numId="6"><w:abstractNumId w:val="4"/></w:num>
                """
            ),
            StylesXml(
                """
                <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/></w:style>
                <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:pPr><w:numPr><w:numId w:val="6"/></w:numPr></w:pPr></w:style>
                """
            ),
            """
            <w:body><w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Mapped heading</w:t></w:r></w:p></w:body>
            """
        );
        var (package, semantic, styles) = ReadSnapshots(bytes);
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            semantic,
            styles
        );
        var run = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Run);

        var formatting = new WordEffectiveFormattingResolver().Resolve(
            package,
            semantic,
            styles,
            numbering,
            run.Id
        );

        Assert.NotNull(formatting.Numbering);
        Assert.Equal(1, formatting.Numbering.LevelIndex);
        Assert.Equal("%1.%2", formatting.Numbering.Level.LevelText);
        Assert.Equal("1440", formatting.ParagraphProperties["indent_left_twips"].Value);
        Assert.Empty(numbering.Issues);
    }

    [Fact]
    public void MissingNumberingPartIsAValidEmptyGraph()
    {
        using var bytes = BuildPackage(numberingXml: null);
        var (package, semantic, styles) = ReadSnapshots(bytes);

        var graph = new WordNumberingGraphBuilder().Build(package, semantic, styles);

        Assert.False(graph.HasNumberingPart);
        Assert.Empty(graph.AbstractDefinitions);
        Assert.Empty(graph.Instances);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void AcceptsStrictNumberingRelationshipAndNamespace()
    {
        using var bytes = BuildPackage(
            """
            <w:numbering xmlns:w="http://purl.oclc.org/ooxml/wordprocessingml/main"><w:abstractNum w:abstractNumId="2"><w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum><w:num w:numId="3"><w:abstractNumId w:val="2"/></w:num></w:numbering>
            """,
            strictNumberingRelationship: true
        );
        var (package, semantic, styles) = ReadSnapshots(bytes);

        var graph = new WordNumberingGraphBuilder().Build(package, semantic, styles);
        var resolved = graph.ResolveLevel(3, 0);

        Assert.Equal(2, resolved.EffectiveAbstractNumberId);
        Assert.Equal("decimal", resolved.Level.NumberFormat);
    }

    [Fact]
    public void RejectsDuplicateIdsAndConfiguredLimits()
    {
        using var duplicateBytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"/><w:abstractNum w:abstractNumId="1"/>
                """
            )
        );
        var duplicateSnapshots = ReadSnapshots(duplicateBytes);
        Assert.Throws<WordNumberingProjectionException>(() =>
            new WordNumberingGraphBuilder().Build(
                duplicateSnapshots.Package,
                duplicateSnapshots.Semantic,
                duplicateSnapshots.Styles
            )
        );

        using var limitedBytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"/><w:abstractNum w:abstractNumId="2"/>
                """
            )
        );
        var limitedSnapshots = ReadSnapshots(limitedBytes);
        Assert.Throws<WordNumberingLimitException>(() =>
            new WordNumberingGraphBuilder(
                new WordNumberingGraphOptions { MaxAbstractDefinitions = 1 }
            ).Build(
                limitedSnapshots.Package,
                limitedSnapshots.Semantic,
                limitedSnapshots.Styles
            )
        );
    }

    [Fact]
    public void BuildsGraphsForEveryBundledDocxNumberingPart()
    {
        var fixtureDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures"
        );
        var paths = Directory.EnumerateFiles(fixtureDirectory, "*.docx").ToArray();
        Assert.NotEmpty(paths);
        var reader = new OpcPackageReader();
        var numberingParts = 0;
        foreach (var path in paths)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var styles = new WordStyleGraphBuilder().Build(package, semantic);
            var graph = new WordNumberingGraphBuilder().Build(package, semantic, styles);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            if (graph.HasNumberingPart)
            {
                numberingParts++;
            }
        }

        Assert.True(numberingParts >= 3);
    }

    private static (
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic,
        WordStyleGraph Styles
    ) ReadSnapshots(Stream bytes)
    {
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        return (package, semantic, styles);
    }

    private static MemoryStream BuildPackage(
        string? numberingXml,
        string? stylesXml = null,
        string? documentBody = null,
        bool strictNumberingRelationship = false
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var numberingOverride = numberingXml is null
                ? string.Empty
                : "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>";
            var stylesOverride = stylesXml is null
                ? string.Empty
                : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>";
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {numberingOverride}{stylesOverride}
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
                $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{documentBody ?? "<w:body><w:p><w:r><w:t>List item</w:t></w:r></w:p></w:body>"}</w:document>
                """
            );
            var relationships = new StringBuilder();
            if (numberingXml is not null)
            {
                var numberingRelationshipType = strictNumberingRelationship
                    ? "http://purl.oclc.org/ooxml/officeDocument/relationships/numbering"
                    : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
                relationships.Append(
                    $"<Relationship Id=\"rIdNumbering\" Type=\"{numberingRelationshipType}\" Target=\"numbering.xml\"/>"
                );
                WriteEntry(archive, "word/numbering.xml", numberingXml);
            }

            if (stylesXml is not null)
            {
                relationships.Append(
                    "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                );
                WriteEntry(archive, "word/styles.xml", stylesXml);
            }

            if (relationships.Length != 0)
            {
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{relationships}</Relationships>
                    """
                );
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string NumberingXml(string content) => $"""
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{content}</w:numbering>
        """;

    private static string StylesXml(string content) => $"""
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{content}</w:styles>
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
