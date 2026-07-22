using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordTableGraphTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    [Fact]
    public void BuildsASourceLinkedLogicalGridWithNestedTablesAndVerticalMerges()
    {
        using var bytes = BuildPackage(
            Document(
                """
                <w:tbl>
                  <w:tblPr>
                    <w:tblStyle w:val="TableGrid"/>
                    <w:tblW w:w="5000" w:type="pct"/>
                    <w:tblLayout w:type="fixed"/>
                    <w:jc w:val="center"/>
                    <w:tblCaption w:val="Sensitive caption"/>
                    <w:tblDescription w:val="Sensitive description"/>
                  </w:tblPr>
                  <w:tblGrid>
                    <w:gridCol w:w="1200"/><w:gridCol w:w="1400"/><w:gridCol w:w="1600"/>
                  </w:tblGrid>
                  <w:tr>
                    <w:trPr><w:tblHeader/><w:cantSplit/></w:trPr>
                    <w:tc><w:tcPr><w:gridSpan w:val="2"/><w:vMerge w:val="restart"/></w:tcPr><w:p/></w:tc>
                    <w:tc><w:tcPr><w:vAlign w:val="center"/></w:tcPr><w:p/></w:tc>
                  </w:tr>
                  <w:tr>
                    <w:tc><w:tcPr><w:gridSpan w:val="2"/><w:vMerge/></w:tcPr><w:p/></w:tc>
                    <w:tc>
                      <w:p/>
                      <w:tbl>
                        <w:tblPr><w:tblW w:w="0" w:type="auto"/></w:tblPr>
                        <w:tblGrid><w:gridCol w:w="900"/></w:tblGrid>
                        <w:tr><w:tc><w:p/></w:tc></w:tr>
                      </w:tbl>
                    </w:tc>
                  </w:tr>
                </w:tbl>
                """
            )
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var first = new WordTableGraphBuilder().Build(package, semantic);
        var second = new WordTableGraphBuilder().Build(package, semantic);

        Assert.Equal(first.Tables.Select(table => table.Id), second.Tables.Select(table => table.Id));
        Assert.Equal(2, first.Tables.Count);
        var outer = Assert.Single(first.Tables, table => table.Depth == 0);
        var nested = Assert.Single(first.Tables, table => table.Depth == 1);
        Assert.Equal(outer.Id, nested.ParentTableId);
        Assert.Equal([nested.Id], outer.NestedTableIds);
        Assert.Equal("TableGrid", outer.StyleId);
        Assert.Equal(3, outer.DeclaredGridColumnCount);
        Assert.Equal(3, outer.LogicalColumnCount);
        Assert.Equal(2, outer.RowCount);
        Assert.Equal(4, outer.CellCount);
        Assert.Equal(WordTableWidthKind.Percent, outer.Width.Kind);
        Assert.Equal(100m, outer.Width.Percent);
        Assert.Equal(WordTableLayoutKind.Fixed, outer.Layout);
        Assert.Equal(WordTableJustification.Center, outer.Justification);
        Assert.Equal("Sensitive caption", outer.Caption);
        Assert.Equal("Sensitive description", outer.Description);

        var rows = first.Rows.Where(row => row.TableId == outer.Id).ToArray();
        Assert.True(rows[0].HeaderDeclared);
        Assert.True(rows[0].HeaderEffective);
        Assert.True(rows[0].CannotSplit);
        Assert.False(rows[1].HeaderDeclared);
        Assert.All(rows, row => Assert.Equal(3, row.LogicalColumnCount));

        var mergedCells = first.Cells
            .Where(cell => cell.TableId == outer.Id && cell.LogicalColumnStart == 0)
            .ToArray();
        Assert.Equal([WordTableMergeState.Restart, WordTableMergeState.Continue], mergedCells.Select(cell => cell.VerticalMerge));
        Assert.All(mergedCells, cell => Assert.Equal(2, cell.GridSpan));
        Assert.Equal(mergedCells[0].Id, mergedCells[1].VerticalMergeRootCellId);
        var merge = Assert.Single(first.VerticalMerges);
        Assert.Equal(2, merge.RowSpan);
        Assert.Equal(2, merge.GridSpan);
        Assert.Equal(mergedCells.Select(cell => cell.Id), merge.CellIds);
        Assert.DoesNotContain(
            first.Issues,
            issue => issue.Severity == WordTableIssueSeverity.Error
        );
        Assert.Equal(package.Fingerprint, first.PackageFingerprint);
        Assert.Equal(semantic.Nodes.Count(node => node.Kind == WordSemanticNodeKind.TableCell), first.Cells.Count);
    }

    [Fact]
    public void DiagnosesBrokenSpansGridSkipsMergeContinuationsAndHeaderContiguity()
    {
        using var bytes = BuildPackage(
            Document(
                """
                <w:tbl>
                  <w:tblPr/>
                  <w:tblGrid><w:gridCol/><w:gridCol/></w:tblGrid>
                  <w:tr>
                    <w:tc><w:tcPr><w:gridSpan w:val="2"/><w:vMerge w:val="restart"/></w:tcPr><w:p/></w:tc>
                  </w:tr>
                  <w:tr>
                    <w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc>
                    <w:tc><w:tcPr><w:gridSpan w:val="0"/></w:tcPr><w:p/></w:tc>
                  </w:tr>
                  <w:tr>
                    <w:trPr><w:gridBefore w:val="3"/><w:tblHeader/></w:trPr>
                    <w:tc><w:tcPr><w:hMerge w:val="restart"/></w:tcPr><w:p/></w:tc>
                  </w:tr>
                </w:tbl>
                """
            )
        );
        var graph = new WordTableGraphBuilder().Build(new OpcPackageReader().Read(bytes));
        var codes = graph.Issues.Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TABLE_VERTICAL_MERGE_SPAN_MISMATCH", codes);
        Assert.Contains("TABLE_GRID_SPAN_INVALID", codes);
        Assert.Contains("TABLE_HEADER_NOT_CONTIGUOUS", codes);
        Assert.Contains("TABLE_ROW_GRID_OVERFLOW", codes);
        Assert.Contains("TABLE_ROW_GRID_SKIP_OUT_OF_RANGE", codes);
        Assert.Contains("TABLE_LEGACY_HORIZONTAL_MERGE", codes);
        Assert.Contains(
            graph.Cells,
            cell => cell.LegacyHorizontalMerge == WordTableMergeState.Restart
        );
        Assert.Contains(
            graph.Rows,
            row => row.HeaderDeclared && !row.HeaderEffective
        );
    }

    [Fact]
    public void AppliesWordFloatingDefaultsAndMarksTextboxPositioningAsIgnored()
    {
        using var bytes = BuildPackage(
            Document(
                """
                <w:tbl>
                  <w:tblPr><w:tblpPr w:tblpX="23" w:tblpY="45" w:leftFromText="10"/></w:tblPr>
                  <w:tblGrid><w:gridCol/></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr>
                </w:tbl>
                <w:txbxContent>
                  <w:tbl>
                    <w:tblPr><w:tblpPr w:horzAnchor="page" w:tblpX="12"/></w:tblPr>
                    <w:tblGrid><w:gridCol/></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr>
                  </w:tbl>
                </w:txbxContent>
                """
            )
        );
        var graph = new WordTableGraphBuilder().Build(new OpcPackageReader().Read(bytes));
        var main = Assert.Single(
            graph.Tables,
            table => table.StoryKind == WordStoryKind.Main
        );
        var textBox = Assert.Single(
            graph.Tables,
            table => table.StoryKind == WordStoryKind.TextBox
        );

        Assert.True(main.FloatingPosition.Declared);
        Assert.True(main.FloatingPosition.IsEffectiveInWord);
        Assert.Equal(WordTableAnchor.Text, main.FloatingPosition.HorizontalAnchor);
        Assert.Equal(WordTableAnchor.Margin, main.FloatingPosition.VerticalAnchor);
        Assert.Equal(23, main.FloatingPosition.HorizontalPositionTwips);
        Assert.Equal(45, main.FloatingPosition.VerticalPositionTwips);
        Assert.False(textBox.FloatingPosition.IsEffectiveInWord);
        Assert.Equal("textbox_story", textBox.FloatingPosition.IgnoredReason);
    }

    [Fact]
    public void SupportsStrictWordprocessingMlAndRowPropertyExceptions()
    {
        using var bytes = BuildPackage(
            Document(
                """
                <w:tbl>
                  <w:tblPr><w:tblW w:w="2400" w:type="dxa"/></w:tblPr>
                  <w:tblGrid><w:gridCol w:w="2400"/></w:tblGrid>
                  <w:tr>
                    <w:tblPrEx><w:tblW w:w="1200" w:type="dxa"/><w:jc w:val="right"/></w:tblPrEx>
                    <w:tc><w:tcPr><w:tcW w:w="1200" w:type="dxa"/><w:noWrap/></w:tcPr><w:p/></w:tc>
                  </w:tr>
                </w:tbl>
                """,
                StrictWordNamespace
            )
        );
        var graph = new WordTableGraphBuilder().Build(new OpcPackageReader().Read(bytes));

        var table = Assert.Single(graph.Tables);
        var row = Assert.Single(graph.Rows);
        var cell = Assert.Single(graph.Cells);
        Assert.Equal(WordTableWidthKind.Twips, table.Width.Kind);
        Assert.Equal(2400, table.Width.Value);
        Assert.True(row.PropertyOverrides.Declared);
        Assert.Equal(1200, row.PropertyOverrides.Width.Value);
        Assert.Equal(WordTableJustification.Right, row.PropertyOverrides.Justification);
        Assert.True(cell.NoWrap);
    }

    [Fact]
    public void GroupsBothSidesOfAnAdjacentSameStyleTableContinuation()
    {
        var first = "<w:tbl><w:tblPr><w:tblStyle w:val='Grid'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol/></w:tblGrid><w:tr><w:tc><w:p/>"
            + "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol/></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>"
            + "</w:tc></w:tr></w:tbl>";
        var second = "<w:tbl><w:tblPr><w:tblStyle w:val='Grid'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol/></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>";
        var separated = second;
        using var bytes = BuildPackage(
            Document(first + second + "<w:p/>" + separated)
        );
        var graph = new WordTableGraphBuilder().Build(new OpcPackageReader().Read(bytes));
        var topLevel = graph.Tables.Where(table => table.Depth == 0).ToArray();

        Assert.Equal(3, topLevel.Length);
        Assert.NotNull(topLevel[0].VisualContinuationGroupId);
        Assert.Equal(
            topLevel[0].VisualContinuationGroupId,
            topLevel[1].VisualContinuationGroupId
        );
        Assert.Null(topLevel[2].VisualContinuationGroupId);
        Assert.Null(Assert.Single(graph.Tables, table => table.Depth == 1).VisualContinuationGroupId);
    }

    [Fact]
    public void EnforcesFingerprintCancellationAndResourceLimits()
    {
        using var firstBytes = BuildPackage(Document(SimpleTable()));
        using var secondBytes = BuildPackage(Document(SimpleTable() + SimpleTable()));
        var reader = new OpcPackageReader();
        var first = reader.Read(firstBytes);
        var second = reader.Read(secondBytes);
        var semantic = new WordSemanticProjector().Project(first);

        Assert.Throws<WordTableProjectionException>(() =>
            new WordTableGraphBuilder().Build(second, semantic)
        );
        Assert.Throws<WordTableLimitException>(() =>
            new WordTableGraphBuilder(new WordTableGraphOptions { MaxTables = 1 })
                .Build(second)
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new WordTableGraphBuilder().Build(first, semantic, cancellation.Token)
        );
    }

    [Fact]
    public void ProjectsTheTrackedAdvancedWordCorpusWithoutLosingTableCells()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "examples",
            "advanced",
            "WordToolkit-advanced-torture-test.docx"
        );
        using var stream = File.OpenRead(path);
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var graph = new WordTableGraphBuilder().Build(package, semantic);

        Assert.NotEmpty(graph.Tables);
        Assert.Equal(
            semantic.Nodes.Count(node => node.Kind == WordSemanticNodeKind.Table),
            graph.Tables.Count
        );
        Assert.Equal(
            semantic.Nodes.Count(node => node.Kind == WordSemanticNodeKind.TableCell),
            graph.Cells.Count
        );
        Assert.All(graph.Tables, table => Assert.True(table.LogicalColumnCount > 0));
    }

    private static string SimpleTable() =>
        "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol/></w:tblGrid>"
        + "<w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>";

    private static string Document(string body, string wordNamespace = WordNamespace) => $$"""
        <w:document xmlns:w="{{wordNamespace}}">
          <w:body>{{body}}<w:sectPr/></w:body>
        </w:document>
        """;

    private static MemoryStream BuildPackage(string documentXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(
                archive,
                "[Content_Types].xml",
                "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                    + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                    + "<Default Extension='xml' ContentType='application/xml'/>"
                    + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
                    + "</Types>"
            );
            AddEntry(
                archive,
                "_rels/.rels",
                "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                    + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
                    + "</Relationships>"
            );
            AddEntry(archive, "word/document.xml", documentXml);
        }
        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
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
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
