using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordChartGraphTests
{
    public static TheoryData<string> ClassicPlotTypes => new()
    {
        "area3DChart",
        "areaChart",
        "bar3DChart",
        "barChart",
        "bubbleChart",
        "doughnutChart",
        "line3DChart",
        "lineChart",
        "ofPieChart",
        "pie3DChart",
        "pieChart",
        "radarChart",
        "scatterChart",
        "stockChart",
        "surface3DChart",
        "surfaceChart",
    };

    [Fact]
    public void ProjectsLibreOfficeChartSeriesAxesCachesAndEmbeddedWorkbook()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures",
            "lo_chart.docx"
        );
        var package = new OpcPackageReader().Read(path);

        var graph = new WordChartGraphBuilder().Build(package);

        Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
        var reference = Assert.Single(graph.References);
        Assert.True(reference.IsResolved);
        var chart = Assert.Single(graph.Charts);
        Assert.Equal("/word/charts/chart1.xml", chart.PartUri);
        Assert.True(chart.IsPackageReachable);
        Assert.Equal(1, chart.IncomingReferenceCount);
        Assert.True(chart.HasTitle);
        Assert.Equal(3, chart.Series.Count);
        Assert.Equal(2, chart.Axes.Count);
        var plot = Assert.Single(chart.Plots);
        Assert.Equal("barChart", plot.Type);
        Assert.Equal("col", plot.BarDirection);
        Assert.Equal("clustered", plot.Grouping);
        Assert.Equal(3, plot.SeriesIds.Count);
        Assert.Equal(2, plot.AxisIds.Count);
        Assert.All(chart.Series, series =>
        {
            Assert.Equal(new[] { "tx", "cat", "val" }, series.DataSources.Select(item => item.Role));
            Assert.All(series.DataSources, source =>
            {
                Assert.True(source.CachePresent);
                Assert.True(source.DeclaredCountMatches);
                Assert.False(source.HasDuplicatePointIndexes);
            });
        });
        var externalData = Assert.Single(chart.ExternalData);
        Assert.True(externalData.IsResolved);
        Assert.Equal("rId3", externalData.RelationshipId);
        Assert.Equal(
            "/word/embeddings/Microsoft_Excel_Worksheet1.xlsx",
            externalData.TargetPartUri
        );
        Assert.Contains(
            chart.RelatedParts,
            part => part.Kind == WordChartRelatedPartKind.EmbeddedPackage
                && part.IsResolved
        );
        Assert.Contains(chart.RelatedParts, part => part.Kind == WordChartRelatedPartKind.Style);
        Assert.Contains(
            chart.RelatedParts,
            part => part.Kind == WordChartRelatedPartKind.ColorStyle
        );
        Assert.Empty(graph.Issues);
        Assert.Empty(graph.UnsupportedExtendedChartPartUris);

        var semantic = new WordSemanticProjector().Project(package);
        var dependencies = new WordDependencyGraphBuilder().Build(package, semantic);
        Assert.True(dependencies.Coverage.Charts);
        Assert.Single(dependencies.Nodes, node => node.Kind == WordDependencyNodeKind.Chart);
        Assert.Equal(
            3,
            dependencies.Nodes.Count(node =>
                node.Kind == WordDependencyNodeKind.ChartSeries
            )
        );
        Assert.Equal(
            2,
            dependencies.Nodes.Count(node => node.Kind == WordDependencyNodeKind.ChartAxis)
        );
        Assert.Equal(
            3,
            dependencies.Edges.Count(edge =>
                edge.Kind == WordDependencyEdgeKind.ChartUsesPart
            )
        );
    }

    [Fact]
    public void ProjectsStrictChartNamespaceAndRelationship()
    {
        using var bytes = BuildPackage(
            ChartXml(strict: true),
            strict: true,
            includeWorkbook: true
        );
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordChartGraphBuilder().Build(package);

        var chart = Assert.Single(graph.Charts);
        Assert.Equal("http://purl.oclc.org/ooxml/drawingml/chart", chart.NamespaceUri);
        var series = Assert.Single(chart.Series);
        Assert.Equal(7, series.Index);
        Assert.Equal(3, series.Order);
        Assert.Equal(3, series.DataSources.Count);
        Assert.Equal(
            "Sheet1!$B$2:$B$3",
            series.DataSources.Single(source => source.Role == "val").Formula
        );
        Assert.True(Assert.Single(chart.ExternalData).IsResolved);
        Assert.Empty(graph.Issues);
    }

    [Theory]
    [MemberData(nameof(ClassicPlotTypes))]
    public void RecognizesEveryClassicPlotFamily(string plotType)
    {
        var chartXml = ChartXml(strict: false)
            .Replace("<c:lineChart>", $"<c:{plotType}>", StringComparison.Ordinal)
            .Replace("</c:lineChart>", $"</c:{plotType}>", StringComparison.Ordinal);
        using var bytes = BuildPackage(chartXml);
        var package = new OpcPackageReader().Read(bytes);

        var plot = Assert.Single(Assert.Single(
            new WordChartGraphBuilder().Build(package).Charts
        ).Plots);

        Assert.Equal(plotType, plot.Type);
        Assert.Single(plot.SeriesIds);
    }

    [Fact]
    public void ReportsCacheAndAxisCorruptionWithoutReturningPointValues()
    {
        var chartXml = ChartXml(strict: false)
            .Replace("<c:ptCount val=\"2\"/>", "<c:ptCount val=\"9\"/>", StringComparison.Ordinal)
            .Replace("<c:pt idx=\"1\"><c:v>Beta</c:v></c:pt>", "<c:pt idx=\"0\"><c:v>SECRET</c:v></c:pt>", StringComparison.Ordinal)
            .Replace("<c:crossAx val=\"20\"/>", "<c:crossAx val=\"999\"/>", StringComparison.Ordinal);
        using var bytes = BuildPackage(chartXml, includeWorkbook: true);
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordChartGraphBuilder().Build(package);

        Assert.Contains(graph.Issues, issue => issue.Code == "CHART_CACHE_COUNT_MISMATCH");
        Assert.Contains(graph.Issues, issue => issue.Code == "CHART_CACHE_DUPLICATE_INDEX");
        Assert.Contains(graph.Issues, issue => issue.Code == "CHART_CROSS_AXIS_UNRESOLVED");
        var category = Assert.Single(graph.Charts).Series.Single().DataSources
            .Single(source => source.Role == "cat");
        Assert.Equal(2, category.ActualPointCount);
        Assert.Equal(1, category.DistinctPointIndexCount);
        Assert.DoesNotContain(
            typeof(WordChartDataSourceDefinition).GetProperties(),
            property => property.Name.Contains("Value", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void KeepsUnresolvedExternalDataExplicitAndNeverFetchesIt()
    {
        using var bytes = BuildPackage(
            ChartXml(strict: false),
            includeWorkbook: false,
            externalWorkbookTarget: true
        );
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordChartGraphBuilder().Build(package);

        var externalData = Assert.Single(Assert.Single(graph.Charts).ExternalData);
        Assert.False(externalData.IsResolved);
        Assert.Equal(OpcRelationshipTargetMode.External, externalData.TargetMode);
        Assert.Null(externalData.TargetPartUri);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "CHART_EXTERNAL_DATA_UNRESOLVED"
        );
    }

    [Fact]
    public void ProjectsLiteralAndMultiLevelCachesWithoutCrossLevelDuplicateNoise()
    {
        var chartXml = ChartXml(strict: false)
            .Replace(
                "<c:cat><c:strRef><c:f>Sheet1!$A$2:$A$3</c:f><c:strCache><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>Alpha</c:v></c:pt><c:pt idx=\"1\"><c:v>Beta</c:v></c:pt></c:strCache></c:strRef></c:cat>",
                "<c:cat><c:multiLvlStrRef><c:f>Sheet1!$A$2:$A$3</c:f><c:multiLvlStrCache><c:ptCount val=\"2\"/><c:lvl><c:pt idx=\"0\"><c:v>North</c:v></c:pt><c:pt idx=\"1\"><c:v>South</c:v></c:pt></c:lvl><c:lvl><c:pt idx=\"0\"><c:v>Alpha</c:v></c:pt><c:pt idx=\"1\"><c:v>Beta</c:v></c:pt></c:lvl></c:multiLvlStrCache></c:multiLvlStrRef></c:cat>",
                StringComparison.Ordinal
            )
            .Replace(
                "<c:val><c:numRef><c:f>Sheet1!$B$2:$B$3</c:f><c:numCache><c:formatCode>0.00</c:formatCode><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>10</c:v></c:pt><c:pt idx=\"1\"><c:v>20</c:v></c:pt></c:numCache></c:numRef></c:val>",
                "<c:val><c:numLit><c:formatCode>0.00</c:formatCode><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>10</c:v></c:pt><c:pt idx=\"1\"><c:v>20</c:v></c:pt></c:numLit></c:val>",
                StringComparison.Ordinal
            );
        using var bytes = BuildPackage(chartXml);
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordChartGraphBuilder().Build(package);

        var sources = Assert.Single(graph.Charts).Series.Single().DataSources;
        var category = sources.Single(source => source.Role == "cat");
        Assert.Equal(WordChartDataSourceKind.MultiLevelStringReference, category.Kind);
        Assert.Equal(2, category.CacheLevelCount);
        Assert.Equal(4, category.ActualPointCount);
        Assert.Equal(2, category.DistinctPointIndexCount);
        Assert.True(category.DeclaredCountMatches);
        Assert.False(category.HasDuplicatePointIndexes);
        var values = sources.Single(source => source.Role == "val");
        Assert.Equal(WordChartDataSourceKind.NumberLiteral, values.Kind);
        Assert.True(values.CachePresent);
        Assert.Equal(1, values.CacheLevelCount);
        Assert.Equal(2, values.ActualPointCount);
        Assert.True(values.DeclaredCountMatches);
        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Code is "CHART_CACHE_COUNT_MISMATCH"
                or "CHART_CACHE_DUPLICATE_INDEX"
        );
    }

    [Fact]
    public void ReportsUnreferencedAndExtendedChartParts()
    {
        using var bytes = BuildPackage(
            ChartXml(strict: false),
            includeChartRelationship: false,
            includeExtendedChart: true
        );
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordChartGraphBuilder().Build(package);

        Assert.Contains(graph.Issues, issue => issue.Code == "CHART_PART_UNREFERENCED");
        Assert.Contains(graph.Issues, issue => issue.Code == "CHART_EXTENDED_UNMODELED");
        Assert.Equal("/word/charts/chartEx1.xml", Assert.Single(graph.UnsupportedExtendedChartPartUris));
    }

    [Fact]
    public void RejectsSeriesAndFormulaResourceBombs()
    {
        var twoSeries = ChartXml(strict: false).Replace(
            "</c:lineChart>",
            "<c:ser><c:idx val=\"8\"/><c:order val=\"4\"/></c:ser></c:lineChart>",
            StringComparison.Ordinal
        );
        using var seriesBytes = BuildPackage(twoSeries);
        var seriesPackage = new OpcPackageReader().Read(seriesBytes);
        Assert.Throws<WordChartLimitException>(() =>
            new WordChartGraphBuilder(
                new WordChartGraphOptions { MaxSeriesPerChart = 1 }
            ).Build(seriesPackage)
        );

        var longFormula = ChartXml(strict: false).Replace(
            "Sheet1!$B$2:$B$3",
            new string('A', 33),
            StringComparison.Ordinal
        );
        using var formulaBytes = BuildPackage(longFormula);
        var formulaPackage = new OpcPackageReader().Read(formulaBytes);
        Assert.Throws<WordChartLimitException>(() =>
            new WordChartGraphBuilder(
                new WordChartGraphOptions { MaxFormulaCharacters = 32 }
            ).Build(formulaPackage)
        );

        using var cacheBytes = BuildPackage(ChartXml(strict: false));
        var cachePackage = new OpcPackageReader().Read(cacheBytes);
        Assert.Throws<WordChartLimitException>(() =>
            new WordChartGraphBuilder(
                new WordChartGraphOptions { MaxCachedPointsPerDataSource = 1 }
            ).Build(cachePackage)
        );
    }

    [Fact]
    public void RejectsWrongChartRoot()
    {
        using var bytes = BuildPackage(
            ChartXml(strict: false).Replace("chartSpace", "notChartSpace", StringComparison.Ordinal)
        );
        var package = new OpcPackageReader().Read(bytes);

        Assert.Throws<WordChartProjectionException>(() =>
            new WordChartGraphBuilder().Build(package)
        );
    }

    [Fact]
    public void ConvertsAmbiguousSingletonsIntoTypedProjectionFailures()
    {
        var chartXml = ChartXml(strict: false).Replace(
            "</c:chartSpace>",
            "<c:chart/></c:chartSpace>",
            StringComparison.Ordinal
        );
        using var bytes = BuildPackage(chartXml);
        var package = new OpcPackageReader().Read(bytes);

        var exception = Assert.Throws<WordChartProjectionException>(() =>
            new WordChartGraphBuilder().Build(package)
        );
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static string ChartXml(bool strict)
    {
        var c = strict
            ? "http://purl.oclc.org/ooxml/drawingml/chart"
            : "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var a = strict
            ? "http://purl.oclc.org/ooxml/drawingml/main"
            : "http://schemas.openxmlformats.org/drawingml/2006/main";
        var r = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <c:chartSpace xmlns:c="{{c}}" xmlns:a="{{a}}" xmlns:r="{{r}}">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Quarterly result</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:autoTitleDeleted val="0"/>
                <c:plotArea>
                  <c:lineChart>
                    <c:grouping val="standard"/><c:varyColors val="0"/>
                    <c:ser>
                      <c:idx val="7"/><c:order val="3"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>Revenue</c:v></c:pt></c:strCache></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$3</c:f><c:strCache><c:ptCount val="2"/><c:pt idx="0"><c:v>Alpha</c:v></c:pt><c:pt idx="1"><c:v>Beta</c:v></c:pt></c:strCache></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$3</c:f><c:numCache><c:formatCode>0.00</c:formatCode><c:ptCount val="2"/><c:pt idx="0"><c:v>10</c:v></c:pt><c:pt idx="1"><c:v>20</c:v></c:pt></c:numCache></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="10"/><c:axId val="20"/>
                  </c:lineChart>
                  <c:catAx><c:axId val="10"/><c:axPos val="b"/><c:crossAx val="20"/></c:catAx>
                  <c:valAx><c:axId val="20"/><c:axPos val="l"/><c:crossAx val="10"/></c:valAx>
                </c:plotArea>
                <c:plotVisOnly val="1"/><c:dispBlanksAs val="gap"/>
              </c:chart>
              <c:externalData r:id="rIdWorkbook"><c:autoUpdate val="0"/></c:externalData>
            </c:chartSpace>
            """;
    }

    private static MemoryStream BuildPackage(
        string chartXml,
        bool strict = false,
        bool includeWorkbook = false,
        bool externalWorkbookTarget = false,
        bool includeChartRelationship = true,
        bool includeExtendedChart = false
    )
    {
        var word = strict
            ? "http://purl.oclc.org/ooxml/wordprocessingml/main"
            : "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var officeRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
        var chartRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/chart"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        var packageRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/package"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";
        var overrides = includeExtendedChart
            ? "<Override PartName=\"/word/charts/chartEx1.xml\" ContentType=\"application/vnd.ms-office.chartex+xml\"/>"
            : string.Empty;
        var contentTypes = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="xlsx" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
              {{overrides}}
            </Types>
            """;
        var documentRelationships = includeChartRelationship
            ? $"<Relationship Id=\"rIdChart\" Type=\"{chartRelationship}\" Target=\"charts/chart1.xml\"/>"
            : string.Empty;
        var workbookRelationship = externalWorkbookTarget
            ? $"<Relationship Id=\"rIdWorkbook\" Type=\"{packageRelationship}\" Target=\"https://example.invalid/secret.xlsx\" TargetMode=\"External\"/>"
            : $"<Relationship Id=\"rIdWorkbook\" Type=\"{packageRelationship}\" Target=\"../embeddings/book.xlsx\"/>";
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", contentTypes);
            AddEntry(
                archive,
                "_rels/.rels",
                $$"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{{officeRelationship}}" Target="word/document.xml"/>
                </Relationships>
                """
            );
            AddEntry(
                archive,
                "word/document.xml",
                $"<w:document xmlns:w=\"{word}\"><w:body><w:p/></w:body></w:document>"
            );
            AddEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{documentRelationships}</Relationships>"
            );
            AddEntry(archive, "word/charts/chart1.xml", chartXml);
            AddEntry(
                archive,
                "word/charts/_rels/chart1.xml.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{workbookRelationship}</Relationships>"
            );
            if (includeWorkbook)
            {
                AddEntry(archive, "word/embeddings/book.xlsx", "not-opened-by-chart-graph");
            }
            if (includeExtendedChart)
            {
                AddEntry(
                    archive,
                    "word/charts/chartEx1.xml",
                    "<cx:chartSpace xmlns:cx=\"http://schemas.microsoft.com/office/drawing/2014/chartex\"/>"
                );
            }
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
        throw new DirectoryNotFoundException("Could not locate the WordToolkit repository root.");
    }
}
