using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordOutlineGraphTests
{
    [Fact]
    public void ResolvesDirectAndInheritedLevelsAndBuildsARealHierarchy()
    {
        using var bytes = BuildPackage(
            documentBody:
                """
                <w:p><w:pPr><w:pStyle w:val="Child"/></w:pPr><w:r><w:t>Root</w:t></w:r></w:p>
                <w:p><w:pPr><w:outlineLvl w:val="2"/></w:pPr><w:r><w:t>Skipped level</w:t></w:r></w:p>
                <w:p><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>Middle</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Child"/><w:outlineLvl w:val="9"/></w:pPr><w:r><w:t>Body override</w:t></w:r></w:p>
                <w:p><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:r><w:t xml:space="preserve">   </w:t></w:r></w:p>
                <w:p><w:r><w:t>Body</w:t></w:r></w:p>
                """,
            stylesXml:
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                  <w:style w:type="paragraph" w:styleId="Base"><w:name w:val="Dowolna nazwa"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:styleId="Child"><w:name w:val="Nie Heading 1"/><w:basedOn w:val="Base"/></w:style>
                </w:styles>
                """
        );

        var graph = BuildGraph(bytes);

        Assert.Equal(6, graph.ExaminedParagraphCount);
        Assert.Equal(4, graph.HeadingCount);
        Assert.Equal(2, graph.BodyTextParagraphCount);
        Assert.Equal(0, graph.UnresolvedParagraphCount);
        Assert.True(graph.OutlineCoverageComplete);
        Assert.Equal(2, graph.RootHeadingCount);
        Assert.Equal(
            new[] { 1, 3, 2, 1 },
            graph.Headings.Select(heading => heading.Level).ToArray()
        );

        var root = graph.Headings[0];
        var skipped = graph.Headings[1];
        var middle = graph.Headings[2];
        Assert.Equal(WordOutlineLevelSourceKind.ParagraphStyle, root.LevelSourceKind);
        Assert.Equal("Child", root.ParagraphStyleId);
        Assert.Equal("Base", root.LevelSourceStyleId);
        Assert.Equal(WordOutlineLevelSourceKind.DirectParagraph, skipped.LevelSourceKind);
        Assert.Equal(root.ParagraphNodeId, skipped.ParentHeadingParagraphNodeId);
        Assert.Equal(root.ParagraphNodeId, middle.ParentHeadingParagraphNodeId);
        Assert.Equal(2, root.DescendantHeadingCount);
        Assert.Contains(graph.Issues, issue => issue.Code == "OUTLINE_LEVEL_SKIPPED");
        Assert.Contains(graph.Issues, issue => issue.Code == "OUTLINE_EMPTY_HEADING");

        var bodyOverride = graph.Paragraphs[3];
        Assert.Equal(WordOutlineResolutionStatus.BodyText, bodyOverride.Status);
        Assert.Equal(WordOutlineLevelSourceKind.DirectParagraph, bodyOverride.LevelSourceKind);
        Assert.Null(bodyOverride.Level);
        var implicitBody = graph.Paragraphs[5];
        Assert.Equal(WordOutlineResolutionStatus.BodyText, implicitBody.Status);
        Assert.Null(implicitBody.LevelSourceKind);
        Assert.All(graph.Paragraphs, paragraph => Assert.False(string.IsNullOrWhiteSpace(paragraph.SourcePartUri)));
    }

    [Fact]
    public void UsesTheUnambiguousDefaultParagraphStyleWithoutGuessingItsName()
    {
        using var bytes = BuildPackage(
            documentBody: "<w:p><w:r><w:t>Default heading</w:t></w:r></w:p>",
            stylesXml:
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:default="1" w:styleId="RandomLocalized"><w:name w:val="Zwykły"/><w:pPr><w:outlineLvl w:val="4"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="Heading 1"/></w:style>
                </w:styles>
                """
        );

        var heading = Assert.Single(BuildGraph(bytes).Headings);

        Assert.Equal(5, heading.Level);
        Assert.Equal("RandomLocalized", heading.ParagraphStyleId);
        Assert.Equal("RandomLocalized", heading.LevelSourceStyleId);
    }

    [Fact]
    public void AValidDirectLevelRemainsAuthoritativeWhenTheStyleReferenceIsBroken()
    {
        using var bytes = BuildPackage(
            documentBody:
                """
                <w:p><w:pPr><w:pStyle w:val="Missing"/><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>Exact direct</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Missing"/></w:pPr><w:r><w:t>Unresolved</w:t></w:r></w:p>
                """
        );

        var graph = BuildGraph(bytes);

        Assert.Single(graph.Headings);
        Assert.Equal(2, graph.Headings[0].Level);
        Assert.Equal(1, graph.UnresolvedParagraphCount);
        Assert.Equal(WordOutlineResolutionStatus.Unresolved, graph.Paragraphs[1].Status);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "OUTLINE_LEVEL_UNRESOLVED"
            && issue.Message.Contains("does not exist", StringComparison.Ordinal)
        );
        Assert.False(graph.OutlineCoverageComplete);
    }

    [Fact]
    public void RefusesInvalidHigherPrecedenceMarkupInsteadOfFallingBackToAStyle()
    {
        using var bytes = BuildPackage(
            documentBody:
                """
                <w:p><w:pPr><w:pStyle w:val="Head"/><w:outlineLvl w:val="12"/></w:pPr><w:r><w:t>Invalid</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Head"/><w:outlineLvl w:val="0"/><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>Duplicate</w:t></w:r></w:p>
                """,
            stylesXml:
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:styleId="Head"><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
                </w:styles>
                """
        );

        var graph = BuildGraph(bytes);

        Assert.Empty(graph.Headings);
        Assert.Equal(2, graph.UnresolvedParagraphCount);
        Assert.All(graph.Paragraphs, paragraph =>
            Assert.Equal(WordOutlineResolutionStatus.Unresolved, paragraph.Status)
        );
        Assert.Contains(graph.Issues, issue => issue.Message.Contains("outside 0 through 9", StringComparison.Ordinal));
        Assert.Contains(graph.Issues, issue => issue.Message.Contains("duplicate 'outlineLvl'", StringComparison.Ordinal));
    }

    [Fact]
    public void UsesOnlyTheHighestPrecedenceDeclaredStyleLevelAndCachesItsResult()
    {
        using var bytes = BuildPackage(
            documentBody:
                """
                <w:p><w:pPr><w:pStyle w:val="Child"/></w:pPr><w:r><w:t>One</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Child"/></w:pPr><w:r><w:t>Two</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="BrokenChild"/></w:pPr><w:r><w:t>Broken</w:t></w:r></w:p>
                """,
            stylesXml:
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:docDefaults><w:pPrDefault><w:pPr><w:outlineLvl w:val="also-invalid"/></w:pPr></w:pPrDefault></w:docDefaults>
                  <w:style w:type="paragraph" w:styleId="Base"><w:pPr><w:outlineLvl w:val="invalid"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:styleId="Child"><w:basedOn w:val="Base"/><w:pPr><w:outlineLvl w:val="2"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:styleId="BrokenChild"><w:basedOn w:val="Child"/><w:pPr><w:outlineLvl w:val="bad"/></w:pPr></w:style>
                </w:styles>
                """
        );

        var graph = BuildGraph(bytes);

        Assert.Equal(new[] { 3, 3 }, graph.Headings.Select(heading => heading.Level));
        Assert.All(graph.Headings, heading =>
            Assert.Equal("Child", heading.LevelSourceStyleId)
        );
        Assert.Equal(1, graph.UnresolvedParagraphCount);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "OUTLINE_LEVEL_UNRESOLVED"
            && issue.Message.Contains("'bad'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void KeepsRevisionAndTextBoxFlowsOutOfTheMainHierarchy()
    {
        using var bytes = BuildPackage(
            documentBody:
                """
                <w:p><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:r><w:t>Main</w:t></w:r></w:p>
                <w:ins w:id="1" w:author="A"><w:p><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>Revision</w:t></w:r></w:p></w:ins>
                <w:p><w:r><w:pict><w:txbxContent><w:p><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:r><w:t>Text box</w:t></w:r></w:p></w:txbxContent></w:pict></w:r></w:p>
                """
        );

        var graph = BuildGraph(bytes);

        Assert.Equal(4, graph.ExaminedParagraphCount);
        Assert.Equal(3, graph.HeadingCount);
        Assert.Equal(2, graph.HierarchyHeadingCount);
        var revision = graph.Headings.Single(heading => heading.ViewAmbiguous);
        Assert.False(revision.HierarchyEligible);
        Assert.Null(revision.ParentHeadingParagraphNodeId);
        var textBox = graph.Headings.Single(heading => heading.StoryKind == WordStoryKind.TextBox);
        Assert.True(textBox.HierarchyEligible);
        Assert.Null(textBox.ParentHeadingParagraphNodeId);
        Assert.Equal(1, graph.SkippedHeadingCount);
        Assert.Contains(graph.Issues, issue => issue.Code == "OUTLINE_VIEW_AMBIGUOUS");
        Assert.False(graph.OutlineCoverageComplete);
    }

    [Fact]
    public void SupportsStrictWordprocessingMlWithoutLocalizedStyleHeuristics()
    {
        using var bytes = BuildPackage(
            documentBody: "<w:p><w:pPr><w:pStyle w:val=\"X\"/></w:pPr><w:r><w:t>Strict</w:t></w:r></w:p>",
            stylesXml:
                """
                <w:styles xmlns:w="http://purl.oclc.org/ooxml/wordprocessingml/main"><w:style w:type="paragraph" w:styleId="X"><w:name w:val="Anything"/><w:pPr><w:outlineLvl w:val="8"/></w:pPr></w:style></w:styles>
                """,
            strict: true
        );

        var heading = Assert.Single(BuildGraph(bytes).Headings);

        Assert.Equal(9, heading.Level);
        Assert.Equal(WordOutlineLevelSourceKind.ParagraphStyle, heading.LevelSourceKind);
    }

    [Fact]
    public void EnforcesParagraphHeadingAndIssueLimitsAndCancellation()
    {
        using var bytes = BuildPackage(
            documentBody:
                """
                <w:p><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:r><w:t>One</w:t></w:r></w:p>
                <w:p><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>Two</w:t></w:r></w:p>
                """
        );
        var snapshots = ReadSnapshots(bytes);

        Assert.Throws<WordOutlineLimitException>(() =>
            new WordOutlineGraphBuilder(new WordOutlineGraphOptions { MaxParagraphs = 1 })
                .Build(snapshots.Package, snapshots.Semantic, snapshots.Styles)
        );
        Assert.Throws<WordOutlineLimitException>(() =>
            new WordOutlineGraphBuilder(new WordOutlineGraphOptions { MaxHeadings = 1 })
                .Build(snapshots.Package, snapshots.Semantic, snapshots.Styles)
        );
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new WordOutlineGraphBuilder().Build(
                snapshots.Package,
                snapshots.Semantic,
                snapshots.Styles,
                cancelled.Token
            )
        );
    }

    [Fact]
    public void ProducesStableParagraphIdentityAndOneResolutionPerParagraph()
    {
        using var bytes = BuildPackage(
            documentBody:
                """
                <w:p><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:r><w:t>One</w:t></w:r></w:p>
                <w:p><w:r><w:t>Body</w:t></w:r></w:p>
                """
        );
        var snapshots = ReadSnapshots(bytes);

        var first = new WordOutlineGraphBuilder().Build(
            snapshots.Package,
            snapshots.Semantic,
            snapshots.Styles
        );
        var second = new WordOutlineGraphBuilder().Build(
            snapshots.Package,
            snapshots.Semantic,
            snapshots.Styles
        );

        Assert.Equal(
            first.Paragraphs.Select(item => item.ParagraphNodeId),
            second.Paragraphs.Select(item => item.ParagraphNodeId)
        );
        Assert.Equal(first.ExaminedParagraphCount, first.Paragraphs.Count);
        Assert.Equal(first.Headings[0].ParagraphNodeId, first.Paragraphs[0].ParagraphNodeId);
    }

    private static WordOutlineGraph BuildGraph(Stream bytes)
    {
        var snapshots = ReadSnapshots(bytes);
        return new WordOutlineGraphBuilder().Build(
            snapshots.Package,
            snapshots.Semantic,
            snapshots.Styles
        );
    }

    private static (
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic,
        WordStyleGraph Styles
    ) ReadSnapshots(Stream bytes)
    {
        bytes.Position = 0;
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        return (package, semantic, styles);
    }

    private static MemoryStream BuildPackage(
        string documentBody,
        string? stylesXml = null,
        bool strict = false
    )
    {
        var wordNamespace = strict
            ? "http://purl.oclc.org/ooxml/wordprocessingml/main"
            : "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var officeRelationships = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        stylesXml ??= $"<w:styles xmlns:w=\"{wordNamespace}\"/>";
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{officeRelationships}/officeDocument\" Target=\"word/document.xml\"/></Relationships>"
            );
            WriteEntry(
                archive,
                "word/document.xml",
                $"<w:document xmlns:w=\"{wordNamespace}\"><w:body>{documentBody}</w:body></w:document>"
            );
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdStyles\" Type=\"{officeRelationships}/styles\" Target=\"styles.xml\"/></Relationships>"
            );
            WriteEntry(archive, "word/styles.xml", stylesXml);
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
