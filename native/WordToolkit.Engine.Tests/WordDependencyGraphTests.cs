using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordDependencyGraphTests
{
    [Fact]
    public void BuildsOneStableSourceLinkedGraphAcrossSixDocumentDomains()
    {
        using var bytes = BuildPackage(
            documentBody: """
            <w:p>
              <w:pPr><w:pStyle w:val="Heading1"/><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr>
              <w:bookmarkStart w:id="7" w:name="Target"/>
              <w:r><w:rPr><w:rStyle w:val="Heading1Char"/></w:rPr><w:t>Heading</w:t></w:r>
              <w:bookmarkEnd w:id="7"/>
            </w:p>
            <w:p>
              <w:r><w:fldChar w:fldCharType="begin"/></w:r>
              <w:r><w:instrText xml:space="preserve"> REF Target \h </w:instrText></w:r>
              <w:r><w:fldChar w:fldCharType="separate"/></w:r>
              <w:r><w:t>Heading</w:t></w:r>
              <w:r><w:fldChar w:fldCharType="end"/></w:r>
            </w:p>
            <w:tbl>
              <w:tblPr><w:tblStyle w:val="TableGrid"/></w:tblPr>
              <w:tr><w:tc><w:p><w:r><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            """,
            stylesXml: """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
            <w:style w:type="paragraph" w:styleId="Heading1">
              <w:name w:val="Heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:link w:val="Heading1Char"/>
            </w:style>
            <w:style w:type="character" w:styleId="Heading1Char"><w:link w:val="Heading1"/></w:style>
            <w:style w:type="table" w:default="1" w:styleId="TableGrid"><w:name w:val="Table Grid"/></w:style>
            <w:style w:type="paragraph" w:styleId="Numbered"><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr></w:style>
            """,
            numberingXml: """
            <w:abstractNum w:abstractNumId="10">
              <w:multiLevelType w:val="singleLevel"/>
              <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:pStyle w:val="Heading1"/><w:lvlText w:val="%1."/></w:lvl>
            </w:abstractNum>
            <w:num w:numId="5"><w:abstractNumId w:val="10"/></w:num>
            """,
            headerXml: "<w:p><w:r><w:t>Header</w:t></w:r></w:p>"
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var first = new WordDependencyGraphBuilder().Build(package, semantic);
        var second = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.Equal(
            first.Nodes.Select(node => node.Id),
            second.Nodes.Select(node => node.Id)
        );
        Assert.Equal(
            first.Edges.Select(edge => edge.Id),
            second.Edges.Select(edge => edge.Id)
        );
        Assert.All(
            first.Edges,
            edge =>
            {
                Assert.True(first.TryGetNode(edge.SourceNodeId, out _));
                Assert.True(first.TryGetNode(edge.TargetNodeId, out _));
            }
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.PackageRelationship
                && edge.IsResolved
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.SemanticContainment
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.UsesStyle
                && edge.Qualifier == "paragraph"
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.UsesStyle
                && edge.Qualifier == "run"
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.UsesStyle
                && edge.Qualifier == "table"
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.UsesNumbering
                && edge.IsResolved
                && edge.Qualifier == "0"
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.StyleUsesNumbering
                && edge.IsResolved
                && edge.PartUri == "/word/styles.xml"
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.FieldReference
                && edge.IsResolved
        );
        Assert.Contains(
            first.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.SectionBindsStory
                && edge.IsResolved
        );
        Assert.Contains(
            first.Nodes,
            node => node.Kind == WordDependencyNodeKind.Part
                && node.Key == "/word/header1.xml"
                && node.IsPackageReachable
        );
        Assert.True(first.Coverage.PackageRelationships);
        Assert.True(first.Coverage.SemanticContainment);
        Assert.True(first.Coverage.Styles);
        Assert.True(first.Coverage.Numbering);
        Assert.True(first.Coverage.References);
        Assert.True(first.Coverage.Sections);
        Assert.True(first.Coverage.Charts);
        Assert.True(first.Coverage.SmartArtDiagrams);
        Assert.True(first.Coverage.ContentControlsAndCustomXml);
        Assert.True(first.Coverage.TablesAndCellTopology);
        Assert.DoesNotContain("smartart_diagrams", first.Coverage.ExplicitlyUnmodeledDomains);
        Assert.DoesNotContain(
            "content_control_custom_xml_bindings",
            first.Coverage.ExplicitlyUnmodeledDomains
        );
        Assert.DoesNotContain(
            "charts_smartart_diagrams",
            first.Coverage.ExplicitlyUnmodeledDomains
        );
        Assert.Empty(first.Issues);

        var paragraph = semantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
            && node.Properties.ContainsKey("numbering_id")
        );
        Assert.Equal("5", paragraph.Properties["numbering_id"]);
        Assert.Equal("0", paragraph.Properties["numbering_level"]);
        Assert.Equal(
            "TableGrid",
            semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Table)
                .Properties["style_id"]
        );
    }

    [Fact]
    public void JoinsSmartArtPartsPointsAndConnectionsIntoTheSharedGraph()
    {
        using var bytes = WordDiagramGraphTests.BuildPackage();
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.True(graph.Coverage.SmartArtDiagrams);
        Assert.Equal(0, graph.DiagramIssueCount);
        Assert.Single(graph.Nodes, node => node.Kind == WordDependencyNodeKind.Diagram);
        Assert.Equal(
            3,
            graph.Nodes.Count(node => node.Kind == WordDependencyNodeKind.DiagramPoint)
        );
        Assert.Single(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.DefinesDiagram && edge.IsResolved
        );
        Assert.Equal(
            3,
            graph.Edges.Count(edge =>
                edge.Kind == WordDependencyEdgeKind.DiagramContainsPoint
                && edge.IsResolved
            )
        );
        Assert.Equal(
            2,
            graph.Edges.Count(edge =>
                edge.Kind == WordDependencyEdgeKind.DiagramConnectsPoints
                && edge.IsResolved
            )
        );
        Assert.Equal(
            5,
            graph.Edges.Count(edge =>
                edge.Kind == WordDependencyEdgeKind.DiagramUsesPart
                && edge.IsResolved
            )
        );
        Assert.DoesNotContain("smartart_diagrams", graph.Coverage.ExplicitlyUnmodeledDomains);
    }

    [Fact]
    public void JoinsRealContentControlsCustomXmlStoresAndBindingTargets()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "examples",
            "advanced",
            "WordToolkit-advanced-torture-test.docx"
        );
        var package = new OpcPackageReader().Read(path);
        var semantic = new WordSemanticProjector().Project(package);
        var contentControls = new WordContentControlBindingGraphBuilder().Build(
            package,
            semantic
        );
        var tables = new WordTableGraphBuilder().Build(package, semantic);

        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.True(graph.Coverage.ContentControlsAndCustomXml);
        Assert.True(graph.Coverage.TablesAndCellTopology);
        Assert.Equal(contentControls.Issues.Count, graph.ContentControlIssueCount);
        Assert.Equal(tables.Issues.Count, graph.TableIssueCount);
        Assert.Contains(
            graph.Nodes,
            node => node.Kind == WordDependencyNodeKind.ContentControl
                && node.SemanticKind == WordSemanticNodeKind.ContentControl
                && node.SemanticNodeId is not null
        );
        Assert.Contains(
            graph.Nodes,
            node => node.Kind == WordDependencyNodeKind.CustomXmlStore
                && node.IsResolved
        );
        Assert.Contains(
            graph.Nodes,
            node => node.Kind == WordDependencyNodeKind.CustomXmlBindingTarget
                && node.IsResolved
        );
        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.DefinesContentControl
        );
        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.DefinesCustomXmlStore
        );
        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.ContentControlUsesStore
                && edge.IsResolved
        );
        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.CustomXmlStoreContainsTarget
        );
        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.ContentControlBindsTarget
                && edge.IsResolved
        );
        Assert.All(
            graph.Edges,
            edge =>
            {
                Assert.True(graph.TryGetNode(edge.SourceNodeId, out _));
                Assert.True(graph.TryGetNode(edge.TargetNodeId, out _));
            }
        );
    }

    [Fact]
    public void LinksNestedTablesAndVerticalMergeContinuationsWithoutDuplicateNodes()
    {
        using var bytes = BuildPackage(
            documentBody: """
            <w:tbl>
              <w:tblPr/>
              <w:tblGrid><w:gridCol/><w:gridCol/></w:tblGrid>
              <w:tr>
                <w:tc><w:tcPr><w:vMerge w:val="restart"/></w:tcPr><w:p/></w:tc>
                <w:tc><w:p/></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc>
                <w:tc>
                  <w:p/>
                  <w:tbl><w:tblPr/><w:tblGrid><w:gridCol/></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>
                </w:tc>
              </w:tr>
            </w:tbl>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.TableNestsTable
                && edge.IsResolved
        );
        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.TableCellContinuesVerticalMerge
                && edge.IsResolved
        );
        Assert.Equal(
            semantic.NodeCount,
            graph.Nodes.Count(node => node.Kind == WordDependencyNodeKind.SemanticNode)
        );
        Assert.All(
            graph.Edges,
            edge =>
            {
                Assert.True(graph.TryGetNode(edge.SourceNodeId, out _));
                Assert.True(graph.TryGetNode(edge.TargetNodeId, out _));
            }
        );
    }

    [Fact]
    public void PreservesTypedTableDamageAsSourceLinkedDependencyEvidence()
    {
        using var bytes = BuildPackage(
            documentBody: """
            <w:tbl>
              <w:tblPr/><w:tblGrid><w:gridCol/></w:tblGrid>
              <w:tr><w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc></w:tr>
            </w:tbl>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        var issue = Assert.Single(
            graph.Issues,
            issue => issue.Code
                == "WDG060_TABLE_VERTICAL_MERGE_ORPHAN_CONTINUATION"
        );
        Assert.Equal("/word/document.xml", issue.PartUri);
        Assert.NotNull(issue.SourceElementOrdinal);
        Assert.NotNull(issue.NodeId);
        Assert.True(graph.TryGetNode(issue.NodeId!, out var node));
        Assert.Equal(WordSemanticNodeKind.TableCell, node!.SemanticKind);
    }

    [Fact]
    public void PreservesUnresolvedTargetsAndPackageOrphansAsEvidence()
    {
        using var bytes = BuildPackage(
            documentBody: """
            <w:p>
              <w:pPr><w:pStyle w:val="MissingStyle"/><w:numPr><w:numId w:val="99"/></w:numPr></w:pPr>
              <w:r><w:fldChar w:fldCharType="begin"/></w:r>
              <w:r><w:instrText xml:space="preserve"> REF MissingBookmark </w:instrText></w:r>
              <w:r><w:fldChar w:fldCharType="end"/></w:r>
            </w:p>
            """,
            stylesXml: """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
            """,
            numberingXml: """
            <w:num w:numId="5"><w:abstractNumId w:val="42"/></w:num>
            """,
            includeMissingRelationship: true,
            includeExternalRelationship: true,
            includeOrphanPart: true
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.Contains(graph.Issues, issue => issue.Code == "WDG001");
        Assert.Contains(graph.Issues, issue => issue.Code == "WDG003");
        Assert.Contains(graph.Issues, issue => issue.Code == "WDG010");
        Assert.Contains(graph.Issues, issue => issue.Code == "WDG020");
        Assert.Contains(graph.Issues, issue => issue.Code == "WDG022");
        Assert.Contains(graph.Issues, issue => issue.Code == "WDG030");
        Assert.Contains(
            graph.Nodes,
            node => node.Kind == WordDependencyNodeKind.ExternalTarget
                && node.IsExternal
                && !node.IsResolved
        );
        Assert.Contains(
            graph.Nodes,
            node => node.Kind == WordDependencyNodeKind.Part
                && node.Key == "/custom/orphan.xml"
                && !node.IsPackageReachable
        );
        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.FieldReference
                && !edge.IsResolved
        );
        Assert.All(
            graph.Edges,
            edge =>
            {
                Assert.True(graph.TryGetNode(edge.SourceNodeId, out _));
                Assert.True(graph.TryGetNode(edge.TargetNodeId, out _));
            }
        );
    }

    [Fact]
    public void ResolvesAuthorityEntriesToAnAllCategoriesTableOfAuthoritiesField()
    {
        using var bytes = BuildPackage(
            documentBody: """
            <w:p><w:fldSimple w:instr=" TA \l &quot;Alpha&quot; \c 1 "><w:r><w:t/></w:r></w:fldSimple></w:p>
            <w:p><w:fldSimple w:instr=" TA \l &quot;Beta&quot; \c 2 "><w:r><w:t/></w:r></w:fldSimple></w:p>
            <w:p><w:fldSimple w:instr=" TOA \c &quot;0&quot; "><w:r><w:t>Alpha 1 Beta 2</w:t></w:r></w:fldSimple></w:p>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        var authorityEdges = graph.Edges
            .Where(edge =>
                edge.Kind == WordDependencyEdgeKind.FieldReference
                && edge.Qualifier is not null
                && edge.Qualifier.Contains("IndexEntry", StringComparison.Ordinal)
            )
            .ToArray();
        Assert.Equal(2, authorityEdges.Length);
        Assert.All(authorityEdges, edge =>
        {
            Assert.True(edge.IsResolved);
            Assert.True(graph.TryGetNode(edge.TargetNodeId, out var target));
            Assert.Equal(WordDependencyNodeKind.Field, target!.Kind);
            Assert.Contains("category=", edge.Qualifier, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(graph.Issues, issue => issue.Code == "WDG030");
    }

    [Fact]
    public void EnforcesNodeAndFingerprintBoundaries()
    {
        using var bytes = BuildPackage(documentBody: "<w:p/>");
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        Assert.Throws<WordDependencyLimitException>(() =>
            new WordDependencyGraphBuilder(
                new WordDependencyGraphOptions { MaxNodes = 1 }
            ).Build(package, semantic)
        );

        using var otherBytes = BuildPackage(documentBody: "<w:p><w:r><w:t>Other</w:t></w:r></w:p>");
        var otherPackage = new OpcPackageReader().Read(otherBytes);
        var otherSemantic = new WordSemanticProjector().Project(otherPackage);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var numbering = new WordNumberingGraphBuilder().Build(package, semantic, styles);
        var references = new WordReferenceGraphBuilder().Build(package, semantic);
        var sections = new WordSectionGraphBuilder().Build(package, semantic);
        var charts = new WordChartGraphBuilder().Build(package);
        var otherContentControls = new WordContentControlBindingGraphBuilder().Build(
            otherPackage,
            otherSemantic
        );

        Assert.Throws<WordDependencyProjectionException>(() =>
            new WordDependencyGraphBuilder().Build(
                package,
                otherSemantic,
                styles,
                numbering,
                references,
                sections
            )
        );
        Assert.Throws<WordDependencyProjectionException>(() =>
            new WordDependencyGraphBuilder().Build(
                package,
                semantic,
                styles,
                numbering,
                references,
                sections,
                charts,
                otherContentControls
            )
        );
    }

    [Fact]
    public void EnforcesDeterministicAccountedByteBudgetAndReportsCompactAdjacency()
    {
        using var bytes = BuildPackage(
            documentBody: "<w:p><w:r><w:t>Bounded</w:t></w:r></w:p>",
            stylesXml: """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
            """,
            headerXml: "<w:p><w:r><w:t>Header</w:t></w:r></w:p>"
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var first = new WordDependencyGraphBuilder().Build(package, semantic);
        var second = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.Equal("dependency_graph_accounted_v1", first.ResourceUsage.AccountingModel);
        Assert.Equal(first.ResourceUsage.AccountedBytes, second.ResourceUsage.AccountedBytes);
        Assert.Equal(first.Nodes.Count, first.ResourceUsage.NodeCount);
        Assert.Equal(first.Edges.Count, first.ResourceUsage.EdgeCount);
        Assert.Equal(first.Issues.Count, first.ResourceUsage.IssueCount);
        Assert.Equal(
            ((long)first.Nodes.Count + 1L) * 2L * sizeof(int)
                + (long)first.Edges.Count * 2L * sizeof(int),
            first.ResourceUsage.AdjacencyIndexBytes
        );
        Assert.InRange(
            first.ResourceUsage.AccountedBytes,
            first.ResourceUsage.AdjacencyIndexBytes + 1,
            first.ResourceUsage.MaximumAccountedBytes
        );

        foreach (var node in first.Nodes)
        {
            Assert.Equal(
                first.Edges.Where(edge => edge.TargetNodeId == node.Id)
                    .OrderBy(edge => edge.Kind)
                    .ThenBy(edge => edge.Id, StringComparer.Ordinal)
                    .Select(edge => edge.Id),
                first.IncomingView(node.Id).Select(edge => edge.Id)
            );
            Assert.Equal(
                first.Edges.Where(edge => edge.SourceNodeId == node.Id)
                    .OrderBy(edge => edge.Kind)
                    .ThenBy(edge => edge.Id, StringComparer.Ordinal)
                    .Select(edge => edge.Id),
                first.OutgoingView(node.Id).Select(edge => edge.Id)
            );
        }
        Assert.Empty(first.IncomingView("wddn_missing"));
        Assert.Empty(first.OutgoingView("wddn_missing"));
        var enumerator = first.Edges.Count > 0
            ? first.OutgoingView(first.Edges[0].SourceNodeId).GetEnumerator()
            : default;
        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
        while (enumerator.MoveNext()) { }
        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);

        var allocationView = first.OutgoingView(first.Edges[0].SourceNodeId);
        var observed = 0;
        foreach (var edge in allocationView)
        {
            observed += edge.IsResolved ? 1 : 0;
        }
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            foreach (var edge in allocationView)
            {
                observed += edge.IsResolved ? 1 : 0;
            }
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        GC.KeepAlive(observed);

        var exception = Assert.Throws<WordDependencyLimitException>(() =>
            new WordDependencyGraphBuilder(
                new WordDependencyGraphOptions
                {
                    MaxAccountedBytes = first.ResourceUsage.AccountedBytes - 1,
                }
            ).Build(package, semantic)
        );
        Assert.Contains("accounted budget", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WordDependencyGraphBuilder(
                new WordDependencyGraphOptions { MaxAccountedBytes = 0 }
            )
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WordDependencyGraphBuilder(
                new WordDependencyGraphOptions { MaxMetadataCharacters = 0 }
            )
        );
        Assert.Throws<WordDependencyLimitException>(() =>
            new WordDependencyGraphBuilder(
                new WordDependencyGraphOptions { MaxMetadataCharacters = 8 }
            ).Build(package, semantic)
        );
    }

    [Fact]
    public void HighDegreeAdjacencyRetainsDeterministicOrdering()
    {
        var body = string.Concat(
            Enumerable.Range(0, 4_100)
                .Select(index =>
                    $"<w:p><w:r><w:t>Paragraph {index}</w:t></w:r></w:p>"
                )
        );
        using var bytes = BuildPackage(documentBody: body);
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var graph = new WordDependencyGraphBuilder().Build(package, semantic);
        var highDegreeNode = Assert.Single(
            graph.Nodes,
            node => graph.OutgoingView(node.Id).Count > 4_096
        );

        Assert.Equal(
            graph.Edges.Where(edge => edge.SourceNodeId == highDegreeNode.Id)
                .OrderBy(edge => edge.Kind)
                .ThenBy(edge => edge.Id, StringComparer.Ordinal)
                .Select(edge => edge.Id),
            graph.OutgoingView(highDegreeNode.Id).Select(edge => edge.Id)
        );
    }

    [Fact]
    public void SharesOneDeterministicOperationLeaseAcrossTheInspectionPipeline()
    {
        using var bytes = BuildPackage(
            documentBody: "<w:p><w:r><w:t>bounded</w:t></w:r></w:p>"
        );
        var sourceHash = SHA256.HashData(bytes.ToArray());

        static (WordDependencyGraph Graph, WordOperationResourceUsage Usage) Build(
            Stream source,
            long maximum = WordOperationResourceLease.DefaultMaximumAccountedBytes
        )
        {
            source.Position = 0;
            var lease = new WordOperationResourceLease(maximum);
            var package = new OpcPackageReader(OpcPackageLimits.Default, lease).Read(source);
            var semantic = new WordSemanticProjector(null, lease).Project(package);
            var graph = new WordDependencyGraphBuilder(null, lease).Build(
                package,
                semantic
            );
            return (
                graph,
                graph.OperationResourceUsage
                    ?? throw new InvalidOperationException("Operation usage is missing.")
            );
        }

        var first = Build(bytes);
        var second = Build(bytes);
        Assert.Equal(first.Usage.AccountingModel, second.Usage.AccountingModel);
        Assert.Equal(first.Usage.AccountedBytes, second.Usage.AccountedBytes);
        Assert.Equal(
            first.Usage.MaximumAccountedBytes,
            second.Usage.MaximumAccountedBytes
        );
        Assert.Equal(first.Usage.Stages, second.Usage.Stages);
        Assert.True(first.Usage.AccountedBytes > first.Graph.ResourceUsage.AccountedBytes);
        Assert.Equal(
            WordOperationResourceLease.AccountingModel,
            first.Usage.AccountingModel
        );
        Assert.Equal(
            Enum.GetValues<WordOperationResourceStage>()
                .Except([WordOperationResourceStage.Operation])
                .Order(),
            first.Usage.Stages.Select(item => item.Stage)
                .Except([WordOperationResourceStage.Operation])
                .Order()
        );
        Assert.Equal(sourceHash, SHA256.HashData(bytes.ToArray()));

        var exception = Assert.Throws<WordOperationResourceLimitException>(() =>
            Build(bytes, first.Usage.AccountedBytes - 1)
        );
        Assert.True(exception.AttemptedBytes > 0);
        Assert.InRange(
            exception.AccountedBytes,
            1,
            exception.MaximumAccountedBytes
        );
        Assert.Equal(sourceHash, SHA256.HashData(bytes.ToArray()));
    }

    private static MemoryStream BuildPackage(
        string documentBody,
        string? stylesXml = null,
        string? numberingXml = null,
        string? headerXml = null,
        bool includeMissingRelationship = false,
        bool includeExternalRelationship = false,
        bool includeOrphanPart = false
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {(stylesXml is null ? "" : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>")}
                  {(numberingXml is null ? "" : "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>")}
                  {(headerXml is null ? "" : "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>")}
                </Types>
                """
            );
            AddEntry(
                archive,
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """
            );
            AddEntry(
                archive,
                "word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    {documentBody}
                    <w:sectPr>{(headerXml is null ? "" : "<w:headerReference w:type=\"default\" r:id=\"rId3\"/>")}</w:sectPr>
                  </w:body>
                </w:document>
                """
            );
            var documentRelationships = new StringBuilder();
            documentRelationships.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            documentRelationships.AppendLine("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            if (stylesXml is not null)
            {
                documentRelationships.AppendLine("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            }
            if (numberingXml is not null)
            {
                documentRelationships.AppendLine("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>");
            }
            if (headerXml is not null)
            {
                documentRelationships.AppendLine("<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>");
            }
            if (includeMissingRelationship)
            {
                documentRelationships.AppendLine("<Relationship Id=\"rId98\" Type=\"urn:wordtoolkit:test:missing\" Target=\"missing.xml\"/>");
            }
            if (includeExternalRelationship)
            {
                documentRelationships.AppendLine("<Relationship Id=\"rId99\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.invalid/private\" TargetMode=\"External\"/>");
            }
            documentRelationships.AppendLine("</Relationships>");
            AddEntry(
                archive,
                "word/_rels/document.xml.rels",
                documentRelationships.ToString()
            );
            if (stylesXml is not null)
            {
                AddEntry(
                    archive,
                    "word/styles.xml",
                    $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{stylesXml}</w:styles>
                    """
                );
            }
            if (numberingXml is not null)
            {
                AddEntry(
                    archive,
                    "word/numbering.xml",
                    $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{numberingXml}</w:numbering>
                    """
                );
            }
            if (headerXml is not null)
            {
                AddEntry(
                    archive,
                    "word/header1.xml",
                    $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{headerXml}</w:hdr>
                    """
                );
            }
            if (includeOrphanPart)
            {
                AddEntry(archive, "custom/orphan.xml", "<orphan/>");
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
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
        throw new DirectoryNotFoundException(
            "Could not locate the WordToolkit repository root."
        );
    }
}
