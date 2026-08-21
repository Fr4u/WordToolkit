using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSemanticQueryTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void FindsTextNodesWithBoundedOptionalProjection()
    {
        var document = Project(TestDocumentXml());

        var result = new WordSemanticQueryEngine().Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Text],
                Text = "BETA",
                CaseSensitive = false,
                Limit = 10,
                TextPreviewCharacters = 3,
                IncludeSource = true,
                IncludeProperties = true,
            }
        );

        var match = Assert.Single(result.Matches);
        Assert.Equal(1, result.MatchedNodeCount);
        Assert.Equal(1, result.ReturnedNodeCount);
        Assert.Equal(WordSemanticNodeKind.Text, match.Kind);
        Assert.Equal("bet", match.TextPreview);
        Assert.True(match.TextPreviewTruncated);
        Assert.Equal("/word/document.xml", match.SourcePartUri);
        Assert.StartsWith("/w:document[1]", match.SourcePath, StringComparison.Ordinal);
        Assert.True(match.SourceElementOrdinal >= 0);
        Assert.NotNull(match.Properties);
    }

    [Fact]
    public void MatchesParagraphTextAcrossRunAndTabBoundariesWithoutFlatteningDocument()
    {
        var document = Project(TestDocumentXml());
        var engine = new WordSemanticQueryEngine();

        var acrossRuns = engine.Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Paragraph],
                Text = "ha beta",
                TextScope = WordSemanticTextScope.Subtree,
                TextMatch = WordSemanticTextMatchMode.Contains,
            }
        );
        var acrossTab = engine.Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Paragraph],
                Text = "beta\tg",
                TextScope = WordSemanticTextScope.Subtree,
                TextMatch = WordSemanticTextMatchMode.Contains,
            }
        );
        var nodeOnly = engine.Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Paragraph],
                Text = "alpha",
                TextScope = WordSemanticTextScope.Node,
            }
        );

        Assert.Single(acrossRuns.Matches);
        Assert.Single(acrossTab.Matches);
        Assert.Empty(nodeOnly.Matches);
    }

    [Theory]
    [InlineData(WordSemanticTextMatchMode.Equals, "alpha beta\tgamma", true)]
    [InlineData(WordSemanticTextMatchMode.Equals, "alpha beta", false)]
    [InlineData(WordSemanticTextMatchMode.StartsWith, "alpha be", true)]
    [InlineData(WordSemanticTextMatchMode.StartsWith, "beta", false)]
    [InlineData(WordSemanticTextMatchMode.EndsWith, "ta\tgamma", true)]
    [InlineData(WordSemanticTextMatchMode.EndsWith, "alpha", false)]
    public void SupportsStreamingTextMatchModesAcrossSegments(
        WordSemanticTextMatchMode mode,
        string pattern,
        bool expected
    )
    {
        var document = Project(TestDocumentXml());
        var result = new WordSemanticQueryEngine().Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Paragraph],
                Text = pattern,
                TextScope = WordSemanticTextScope.Subtree,
                TextMatch = mode,
                CaseSensitive = true,
            }
        );

        Assert.Equal(expected ? 1 : 0, result.MatchedNodeCount);
    }

    [Fact]
    public void ParagraphBoundariesDoNotCreateFalseCrossParagraphPhrases()
    {
        var document = Project(TestDocumentXml());
        var body = document.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Body);
        var engine = new WordSemanticQueryEngine();

        var falseJoin = engine.Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Body],
                Text = "gammadelta",
                TextScope = WordSemanticTextScope.Subtree,
            }
        );
        var explicitBoundary = engine.Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Body],
                Text = "gamma\ndelta",
                TextScope = WordSemanticTextScope.Subtree,
            }
        );

        Assert.Empty(falseJoin.Matches);
        Assert.Single(explicitBoundary.Matches);
        Assert.Contains("gamma\ndelta", body.TextPreview(), StringComparison.Ordinal);
    }

    [Fact]
    public void FiltersByPropertyAndSemanticSubtree()
    {
        var document = Project(TestDocumentXml());
        var body = document.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Body);
        var firstParagraph = document.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .OrderBy(node => node.SourceOrder)
            .First();
        var engine = new WordSemanticQueryEngine();

        var styled = engine.Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Paragraph],
                PropertyEquals = new Dictionary<string, string>
                {
                    ["style_id"] = "Definition",
                },
            }
        );
        var withinFirst = engine.Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Text],
                WithinNodeId = firstParagraph.Id,
                Limit = 20,
            }
        );
        var withinBody = engine.Query(
            document,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Paragraph],
                WithinNodeId = body.Id,
            }
        );

        Assert.Single(styled.Matches);
        Assert.Equal(3, withinFirst.MatchedNodeCount);
        Assert.Equal(2, withinBody.MatchedNodeCount);
        Assert.True(withinFirst.ScannedNodeCount < document.NodeCount);
    }

    [Fact]
    public void PaginatesInStableSourceOrderWithoutReturningOptionalFields()
    {
        var document = Project(TestDocumentXml());
        var query = new WordSemanticQuery
        {
            Kinds = [WordSemanticNodeKind.Text],
            Offset = 1,
            Limit = 2,
            TextPreviewCharacters = 0,
        };

        var result = new WordSemanticQueryEngine().Query(document, query);

        Assert.True(result.MatchedNodeCount >= 4);
        Assert.Equal(2, result.ReturnedNodeCount);
        Assert.Equal(3, result.NextOffset);
        Assert.True(result.Matches[0].SourceOrder < result.Matches[1].SourceOrder);
        Assert.All(result.Matches, match =>
        {
            Assert.Null(match.TextPreview);
            Assert.Null(match.Properties);
            Assert.Null(match.SourcePartUri);
            Assert.Null(match.SourcePath);
            Assert.Null(match.SourceElementOrdinal);
        });
    }

    [Fact]
    public void SelectsParagraphContainingEquationAndEquationInsideStyledTable()
    {
        var document = Project(RelationDocumentXml());
        var engine = new WordSemanticQueryEngine();
        var paragraphContainingEquation = new WordSemanticQuery
        {
            Kinds = [WordSemanticNodeKind.Paragraph],
            Ancestor = new WordSemanticRelatedNodePredicate
            {
                Kinds = [WordSemanticNodeKind.Table],
                PropertyEquals = new Dictionary<string, string>
                {
                    ["style_id"] = "EquationGrid",
                },
            },
            Descendant = new WordSemanticRelatedNodePredicate
            {
                Kinds = [WordSemanticNodeKind.Equation],
            },
        };
        var equationInsideTable = new WordSemanticQuery
        {
            Kinds = [WordSemanticNodeKind.Equation],
            Ancestor = new WordSemanticRelatedNodePredicate
            {
                Kinds = [WordSemanticNodeKind.Table],
                PropertyEquals = new Dictionary<string, string>
                {
                    ["style_id"] = "EquationGrid",
                },
            },
        };

        var paragraph = Assert.Single(
            engine.Query(document, paragraphContainingEquation).Matches
        );
        var equation = Assert.Single(engine.Query(document, equationInsideTable).Matches);

        Assert.Equal(WordSemanticNodeKind.Paragraph, paragraph.Kind);
        Assert.Contains("inside equation", paragraph.TextPreview, StringComparison.Ordinal);
        Assert.Equal(WordSemanticNodeKind.Equation, equation.Kind);
    }

    [Fact]
    public void IndexedStructuralJoinMatchesLinearResultAndNarrowsCandidates()
    {
        var document = Project(RelationDocumentXml());
        var index = WordSemanticIndex.Build(document);
        var query = new WordSemanticQuery
        {
            Descendant = new WordSemanticRelatedNodePredicate
            {
                Kinds = [WordSemanticNodeKind.Equation],
            },
            TextPreviewCharacters = 0,
        };
        var engine = new WordSemanticQueryEngine();

        var linear = engine.Query(document, query);
        var indexed = engine.Query(index, query);

        Assert.Equal(
            linear.Matches.Select(match => match.NodeId),
            indexed.Matches.Select(match => match.NodeId)
        );
        Assert.Equal("descendant_relation", indexed.CandidateSeed);
        Assert.True(indexed.ScannedNodeCount < indexed.TotalNodeCount);
        Assert.DoesNotContain(
            indexed.Matches,
            match => match.Kind == WordSemanticNodeKind.Equation
        );
    }

    [Fact]
    public void RejectsInvalidQueriesAndUnknownScopeNode()
    {
        var document = Project(TestDocumentXml());
        var engine = new WordSemanticQueryEngine();

        Assert.Throws<ArgumentException>(() =>
            engine.Query(document, new WordSemanticQuery { Kinds = [] })
        );
        Assert.Throws<ArgumentException>(() =>
            engine.Query(document, new WordSemanticQuery { Text = string.Empty })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            engine.Query(document, new WordSemanticQuery { Limit = 201 })
        );
        Assert.Throws<ArgumentException>(() =>
            engine.Query(
                document,
                new WordSemanticQuery
                {
                    Ancestor = new WordSemanticRelatedNodePredicate(),
                }
            )
        );
        Assert.Throws<KeyNotFoundException>(() =>
            engine.Query(
                document,
                new WordSemanticQuery
                {
                    WithinNodeId = new SemanticNodeId("wdn_missing"),
                }
            )
        );
    }

    [Fact]
    public void CancellationStopsQueryBeforeScanningNodes()
    {
        var document = Project(TestDocumentXml());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new WordSemanticQueryEngine().Query(
                document,
                new WordSemanticQuery(),
                cancellation.Token
            )
        );
    }

    private static WordSemanticDocument Project(string documentXml)
    {
        using var package = BuildPackage(documentXml);
        return new WordSemanticProjector().Project(new OpcPackageReader().Read(package));
    }

    private static string TestDocumentXml() => $"""
        <w:document xmlns:w="{WordNamespace}">
          <w:body>
            <w:p><w:pPr><w:pStyle w:val="Definition"/></w:pPr><w:r><w:t>alpha </w:t></w:r><w:r><w:t>beta</w:t><w:tab/><w:t>gamma</w:t></w:r></w:p>
            <w:p><w:r><w:t>delta</w:t></w:r></w:p>
          </w:body>
        </w:document>
        """;

    private static string RelationDocumentXml() => $"""
        <w:document xmlns:w="{WordNamespace}" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
          <w:body>
            <w:p><w:r><w:t>outside paragraph</w:t></w:r></w:p>
            <w:tbl>
              <w:tblPr><w:tblStyle w:val="EquationGrid"/></w:tblPr>
              <w:tr>
                <w:tc>
                  <w:p>
                    <w:r><w:t>inside equation </w:t></w:r>
                    <m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>
                  </w:p>
                </w:tc>
              </w:tr>
            </w:tbl>
          </w:body>
        </w:document>
        """;

    private static MemoryStream BuildPackage(string documentXml)
    {
        var entries = new[]
        {
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                  <Default Extension="xml" ContentType="application/xml" />
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
                </Types>
                """),
            ("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
                </Relationships>
                """),
            ("word/document.xml", documentXml),
        };
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        stream.Position = 0;
        return stream;
    }
}
