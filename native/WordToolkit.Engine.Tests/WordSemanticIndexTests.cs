using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSemanticIndexTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void BuildsDeterministicBoundedPostingsWithoutChangingTheDocument()
    {
        var document = Project(TestDocumentXml());

        var first = WordSemanticIndex.Build(document);
        var second = WordSemanticIndex.Build(document);

        Assert.Equal(document.PackageFingerprint, first.PackageFingerprint);
        Assert.Equal(first.IndexFingerprint, second.IndexFingerprint);
        Assert.Equal(document.NodeCount, first.NodeCount);
        Assert.Equal(
            document.Nodes.Count(node => node.Kind == WordSemanticNodeKind.Paragraph),
            first.KindCounts[WordSemanticNodeKind.Paragraph]
        );
        Assert.Equal(document.NodeCount, first.PartCounts["/word/document.xml"]);
        Assert.Contains("style_id", first.IndexedPropertyNames);
        Assert.True(first.PropertyOccurrenceCount >= 2);
        Assert.True(first.DistinctPropertyValueCount >= 2);
    }

    [Fact]
    public void IndexedQueryMatchesLinearQueryAndUsesSmallestPosting()
    {
        var document = Project(TestDocumentXml());
        var index = WordSemanticIndex.Build(document);
        var query = new WordSemanticQuery
        {
            Kinds = [WordSemanticNodeKind.Paragraph],
            PropertyEquals = new Dictionary<string, string>
            {
                ["style_id"] = "Definition",
            },
            Text = "beta",
            TextScope = WordSemanticTextScope.Subtree,
            IncludeSource = true,
        };
        var engine = new WordSemanticQueryEngine();

        var linear = engine.Query(document, query);
        var indexed = engine.Query(index, query);

        Assert.Equal(
            linear.Matches.Select(match => match.NodeId),
            indexed.Matches.Select(match => match.NodeId)
        );
        Assert.True(indexed.SemanticIndexUsed);
        Assert.Equal(index.IndexFingerprint, indexed.SemanticIndexFingerprint);
        Assert.Equal("property:style_id", indexed.CandidateSeed);
        Assert.Equal(1, indexed.ScannedNodeCount);
        Assert.True(indexed.ScannedNodeCount < indexed.TotalNodeCount);
    }

    [Fact]
    public void EmptyPostingProducesAProvenEmptyResultWithoutScanningEveryNode()
    {
        var document = Project(TestDocumentXml());
        var index = WordSemanticIndex.Build(document);

        var result = new WordSemanticQueryEngine().Query(
            index,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Paragraph],
                PropertyEquals = new Dictionary<string, string>
                {
                    ["style_id"] = "Missing",
                },
            }
        );

        Assert.Empty(result.Matches);
        Assert.Equal(0, result.ScannedNodeCount);
        Assert.Equal("property:style_id", result.CandidateSeed);
    }

    [Fact]
    public void KindUnionAndSubtreeQueriesRemainInStableSourceOrder()
    {
        var document = Project(TestDocumentXml());
        var body = document.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Body);
        var index = WordSemanticIndex.Build(document);

        var result = new WordSemanticQueryEngine().Query(
            index,
            new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Text, WordSemanticNodeKind.Paragraph],
                WithinNodeId = body.Id,
                Limit = 200,
            }
        );

        Assert.NotEmpty(result.Matches);
        Assert.Equal(
            result.Matches.Select(match => match.SourceOrder).Order(),
            result.Matches.Select(match => match.SourceOrder)
        );
        Assert.True(result.ScannedNodeCount < result.TotalNodeCount);
    }

    [Fact]
    public void RefusesNodeAndPropertyBudgetsInsteadOfAllocatingWithoutBound()
    {
        var document = Project(TestDocumentXml());

        var nodeException = Assert.Throws<WordSemanticIndexLimitException>(() =>
            WordSemanticIndex.Build(
                document,
                new WordSemanticIndexOptions
                {
                    MaxNodeCount = 1,
                    MaxPropertyOccurrences = 100,
                }
            )
        );
        var propertyException = Assert.Throws<WordSemanticIndexLimitException>(() =>
            WordSemanticIndex.Build(
                document,
                new WordSemanticIndexOptions
                {
                    MaxNodeCount = 100,
                    MaxPropertyOccurrences = 1,
                }
            )
        );

        Assert.Contains("node", nodeException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "property",
            propertyException.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void CancellationStopsIndexConstruction()
    {
        var document = Project(TestDocumentXml());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            WordSemanticIndex.Build(document, cancellationToken: cancellation.Token)
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
            <w:p><w:pPr><w:pStyle w:val="Definition"/></w:pPr><w:r><w:t>alpha </w:t></w:r><w:r><w:t>beta</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="BodyText"/></w:pPr><w:r><w:t>delta</w:t></w:r></w:p>
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
