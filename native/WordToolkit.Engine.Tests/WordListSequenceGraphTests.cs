using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordListSequenceGraphTests
{
    [Fact]
    public void ExecutesNestedCountersWithWordRestartAndLegalLabelRules()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1">
                  <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperLetter"/><w:lvlText w:val="%1."/></w:lvl>
                  <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%1.%2"/></w:lvl>
                  <w:lvl w:ilvl="2"><w:start w:val="1"/><w:numFmt w:val="lowerRoman"/><w:lvlRestart w:val="0"/><w:isLgl/><w:lvlText w:val="%1.%2.%3"/></w:lvl>
                </w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            documentBody: string.Concat(
                Paragraph(5, 0, "one"),
                Paragraph(5, 1, "two"),
                Paragraph(5, 1, "three"),
                Paragraph(5, 2, "four"),
                Paragraph(5, 2, "five"),
                Paragraph(5, 0, "six"),
                Paragraph(5, 1, "seven"),
                Paragraph(5, 2, "eight")
            )
        );

        var graph = BuildGraph(bytes);

        Assert.Equal(
            new string?[] { "A.", "A.a", "A.b", "1.2.1", "1.2.2", "B.", "B.a", "2.1.3" },
            graph.Items.Select(item => item.Label).ToArray()
        );
        Assert.All(graph.Items, item => Assert.True(item.CounterExact));
        Assert.All(graph.Items, item => Assert.True(item.LabelExact));
        Assert.Equal(
            WordListContinuationKind.RestartedByHigherLevel,
            graph.Items[6].ContinuationKind
        );
        Assert.Equal(WordListContinuationKind.Continued, graph.Items[7].ContinuationKind);
        Assert.True(graph.CounterCoverageComplete);
        Assert.True(graph.LabelCoverageComplete);
    }

    [Fact]
    public void UsesReplacementLevelStartButIgnoresItsRestartRuleForWord()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1">
                  <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl>
                  <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%2."/></w:lvl>
                </w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/><w:lvlOverride w:ilvl="1"><w:lvl w:ilvl="1"><w:start w:val="9"/><w:lvlRestart w:val="0"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%2."/></w:lvl></w:lvlOverride></w:num>
                <w:num w:numId="6"><w:abstractNumId w:val="1"/><w:lvlOverride w:ilvl="1"><w:startOverride w:val="5"/><w:lvl w:ilvl="1"><w:start w:val="9"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%2."/></w:lvl></w:lvlOverride></w:num>
                """
            ),
            documentBody: string.Concat(
                Paragraph(5, 0, "a"),
                Paragraph(5, 1, "b"),
                Paragraph(5, 1, "c"),
                Paragraph(5, 0, "d"),
                Paragraph(5, 1, "e"),
                Paragraph(6, 0, "f"),
                Paragraph(6, 1, "g")
            )
        );

        var graph = BuildGraph(bytes);

        Assert.Equal(new string?[] { "9.", "10.", "9." }, graph.Items.Where(item => item.NumberId == 5 && item.LevelIndex == 1).Select(item => item.Label).ToArray());
        Assert.Equal("9.", graph.Items.Single(item => item.NumberId == 6 && item.LevelIndex == 1).Label);
        Assert.Contains(
            "word_uses_start_inside_level_override",
            graph.Items[1].CompatibilityWarnings
        );
        Assert.Contains(
            "word_prefers_level_override_start_over_start_override",
            graph.Items.Single(item => item.NumberId == 6 && item.LevelIndex == 1)
                .CompatibilityWarnings
        );
        Assert.Contains(
            "word_ignores_restart_inside_level_override",
            graph.Items[1].CompatibilityWarnings
        );
    }

    [Fact]
    public void RestartsAnOptedInDefinitionAfterASectionBoundary()
    {
        using var bytes = BuildPackage(
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" mc:Ignorable="w15">
              <w:abstractNum w:abstractNumId="1" w15:restartNumberingAfterBreak="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
              <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
            </w:numbering>
            """,
            documentBody: string.Concat(
                Paragraph(5, 0, "one"),
                Paragraph(5, 0, "two"),
                "<w:p><w:pPr><w:sectPr/></w:pPr><w:r><w:t>boundary</w:t></w:r></w:p>",
                Paragraph(5, 0, "three")
            )
        );

        var (package, semantic, styles, numbering) = ReadSnapshots(bytes);
        Assert.True(numbering.AbstractDefinitions.Single().RestartNumberingAfterBreak);
        var graph = new WordListSequenceGraphBuilder().Build(
            package,
            semantic,
            styles,
            numbering
        );

        Assert.Equal(new string?[] { "1.", "2.", "1." }, graph.Items.Select(item => item.Label).ToArray());
        Assert.Equal(
            WordListContinuationKind.RestartedAfterSectionBreak,
            graph.Items[2].ContinuationKind
        );
    }

    [Fact]
    public void ResolvesNumberingInheritedFromAParagraphStyleAndHonorsDirectRemoval()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:pStyle w:val="ListParagraph"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            stylesXml:
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:pPr><w:numPr><w:numId w:val="5"/></w:numPr></w:pPr></w:style></w:styles>
                """,
            documentBody:
                """
                <w:p><w:pPr><w:pStyle w:val="ListParagraph"/></w:pPr><w:r><w:t>one</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="ListParagraph"/><w:numPr><w:numId w:val="0"/></w:numPr></w:pPr><w:r><w:t>plain</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="ListParagraph"/></w:pPr><w:r><w:t>two</w:t></w:r></w:p>
                """
        );

        var graph = BuildGraph(bytes);

        Assert.Equal(2, graph.Items.Count);
        Assert.Equal(new string?[] { "1.", "2." }, graph.Items.Select(item => item.Label).ToArray());
    }

    [Fact]
    public void KeepsExactCounterEvidenceWhenLabelFormatIsUnsupported()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="japaneseCounting"/><w:lvlText w:val="%1"/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            documentBody: Paragraph(5, 0, "one")
        );

        var item = Assert.Single(BuildGraph(bytes).Items);

        Assert.Equal(1, item.CounterValue);
        Assert.True(item.CounterExact);
        Assert.Null(item.Label);
        Assert.Equal(WordListLabelStatus.UnsupportedNumberFormat, item.LabelStatus);
    }

    [Fact]
    public void RefusesToGuessInvalidWordLevelTextAndUnspecifiedStart()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%2"/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            documentBody: Paragraph(5, 0, "one")
        );

        var item = Assert.Single(BuildGraph(bytes).Items);

        Assert.Equal(WordListCounterStatus.UnresolvedStart, item.CounterStatus);
        Assert.Equal(WordListLabelStatus.InvalidLevelText, item.LabelStatus);
        Assert.False(item.CounterExact);
        Assert.False(item.LabelExact);
    }

    [Fact]
    public void SkipsARevisionWrappedNumberedParagraphInsteadOfSelectingAView()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            documentBody:
                """
                <w:ins w:id="1" w:author="A"><w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>one</w:t></w:r></w:p></w:ins>
                """
        );

        var graph = BuildGraph(bytes);

        Assert.Empty(graph.Items);
        Assert.Equal(1, graph.NumberedParagraphCount);
        Assert.Equal(1, graph.SkippedNumberedParagraphCount);
        Assert.Contains(graph.Issues, issue => issue.Code == "LIST_PARAGRAPH_VIEW_AMBIGUOUS");
        Assert.False(graph.CounterCoverageComplete);
    }

    [Fact]
    public void EnforcesParagraphAndItemLimits()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            documentBody: Paragraph(5, 0, "one") + Paragraph(5, 0, "two")
        );
        var snapshots = ReadSnapshots(bytes);

        Assert.Throws<WordListSequenceLimitException>(() =>
            new WordListSequenceGraphBuilder(
                new WordListSequenceGraphOptions { MaxParagraphs = 1 }
            ).Build(
                snapshots.Package,
                snapshots.Semantic,
                snapshots.Styles,
                snapshots.Numbering
            )
        );
        Assert.Throws<WordListSequenceLimitException>(() =>
            new WordListSequenceGraphBuilder(
                new WordListSequenceGraphOptions { MaxItems = 1 }
            ).Build(
                snapshots.Package,
                snapshots.Semantic,
                snapshots.Styles,
                snapshots.Numbering
            )
        );
    }

    [Fact]
    public void SharesStoryParsesAndAccountsItsProjectionWithinOneOperationLease()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            documentBody: Paragraph(5, 0, "one")
        );
        var lease = new WordOperationResourceLease();
        bytes.Position = 0;
        var package = new OpcPackageReader(null, lease).Read(bytes);
        var semantic = new WordSemanticProjector(null, lease).Project(package);
        var styles = new WordStyleGraphBuilder(null, lease).Build(package, semantic);
        var numbering = new WordNumberingGraphBuilder(null, lease).Build(
            package,
            semantic,
            styles
        );
        var before = lease.SnapshotXmlParseCache();

        var graph = new WordListSequenceGraphBuilder(null, lease).Build(
            package,
            semantic,
            styles,
            numbering
        );
        var after = lease.SnapshotXmlParseCache();

        Assert.Single(graph.Items);
        Assert.True(after.Requests > before.Requests);
        Assert.True(after.CacheHits > before.CacheHits);
        Assert.True(after.AvoidedAccountedBytes > before.AvoidedAccountedBytes);
        Assert.Contains(
            lease.Snapshot().Stages,
            stage => stage.Stage == WordOperationResourceStage.ListSequences
                && stage.AccountedBytes > 0
        );
    }

    private static WordListSequenceGraph BuildGraph(Stream bytes)
    {
        var snapshots = ReadSnapshots(bytes);
        return new WordListSequenceGraphBuilder().Build(
            snapshots.Package,
            snapshots.Semantic,
            snapshots.Styles,
            snapshots.Numbering
        );
    }

    private static (
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic,
        WordStyleGraph Styles,
        WordNumberingGraph Numbering
    ) ReadSnapshots(Stream bytes)
    {
        bytes.Position = 0;
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var numbering = new WordNumberingGraphBuilder().Build(package, semantic, styles);
        return (package, semantic, styles, numbering);
    }

    private static MemoryStream BuildPackage(
        string numberingXml,
        string? stylesXml = null,
        string? documentBody = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var stylesOverride = stylesXml is null
                ? string.Empty
                : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>";
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>{stylesOverride}</Types>
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
                "word/document.xml",
                $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{documentBody ?? "<w:p/>"}</w:body></w:document>
                """
            );
            var styleRelationship = stylesXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>";
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>{styleRelationship}</Relationships>
                """
            );
            WriteEntry(archive, "word/numbering.xml", numberingXml);
            if (stylesXml is not null)
            {
                WriteEntry(archive, "word/styles.xml", stylesXml);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string NumberingXml(string content) => $"""
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{content}</w:numbering>
        """;

    private static string Paragraph(int numberId, int levelIndex, string text) => $"""
        <w:p><w:pPr><w:numPr><w:ilvl w:val="{levelIndex}"/><w:numId w:val="{numberId}"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>
        """;

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
