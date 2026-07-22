using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Tests;

public sealed class DocumentCorpusSmokeTests
{
    [Fact]
    public void ParsesEveryXmlPartInBundledMultiProducerDocxCorpusWithoutRewritingBytes()
    {
        var root = FindRepositoryRoot();
        var documentPaths = new[]
        {
            Path.Combine(root, "examples"),
            Path.Combine(root, "tests", "upstream", "fixtures"),
            Path.Combine(root, "tests", "upstream", "fuzz", "corpus"),
        }
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.docx", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(
            documentPaths.Length >= 40,
            $"Expected at least 40 corpus documents, found {documentPaths.Length}."
        );

        var reader = new OpcPackageReader();
        var parsedXmlParts = 0;
        var projectedKinds = new HashSet<WordSemanticNodeKind>();
        foreach (var path in documentPaths)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            projectedKinds.UnionWith(semantic.Nodes.Select(node => node.Kind));
            foreach (var part in package.Parts.Values.Where(part => IsXml(part.ContentType)))
            {
                var source = LosslessXmlDocument.Parse(part.Entry.Content);
                Assert.Equal(
                    part.Entry.Content.ToArray(),
                    source.ApplyPatches(Array.Empty<XmlSourcePatch>())
                );
                parsedXmlParts++;
            }
        }

        Assert.True(
            parsedXmlParts >= 200,
            $"Expected at least 200 XML parts, parsed {parsedXmlParts}."
        );
        foreach (
            var requiredKind in new[]
            {
                WordSemanticNodeKind.Header,
                WordSemanticNodeKind.Footer,
                WordSemanticNodeKind.Footnote,
                WordSemanticNodeKind.Endnote,
                WordSemanticNodeKind.Comment,
                WordSemanticNodeKind.TextBox,
            }
        )
        {
            Assert.Contains(requiredKind, projectedKinds);
        }
    }

    [Fact]
    public void BuildsClosedDependencyGraphsForBundledMultiProducerDocxCorpus()
    {
        var root = FindRepositoryRoot();
        var documentPaths = new[]
        {
            Path.Combine(root, "examples"),
            Path.Combine(root, "tests", "upstream", "fixtures"),
            Path.Combine(root, "tests", "upstream", "fuzz", "corpus"),
        }
            .Where(Directory.Exists)
            .SelectMany(path =>
                Directory.EnumerateFiles(path, "*.docx", SearchOption.AllDirectories)
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(
            documentPaths.Length >= 40,
            $"Expected at least 40 corpus documents, found {documentPaths.Length}."
        );

        var reader = new OpcPackageReader();
        var nodeKinds = new HashSet<WordDependencyNodeKind>();
        var edgeKinds = new HashSet<WordDependencyEdgeKind>();
        foreach (var path in documentPaths)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var graph = new WordDependencyGraphBuilder().Build(package, semantic);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            var nodeIds = graph.Nodes.Select(node => node.Id)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(graph.Nodes.Count, nodeIds.Count);
            Assert.Equal(
                graph.Edges.Count,
                graph.Edges.Select(edge => edge.Id)
                    .Distinct(StringComparer.Ordinal)
                    .Count()
            );
            Assert.All(
                graph.Edges,
                edge =>
                {
                    Assert.Contains(edge.SourceNodeId, nodeIds);
                    Assert.Contains(edge.TargetNodeId, nodeIds);
                }
            );
            nodeKinds.UnionWith(graph.Nodes.Select(node => node.Kind));
            edgeKinds.UnionWith(graph.Edges.Select(edge => edge.Kind));
        }

        Assert.Contains(WordDependencyNodeKind.Part, nodeKinds);
        Assert.Contains(WordDependencyNodeKind.SemanticNode, nodeKinds);
        Assert.Contains(WordDependencyNodeKind.Style, nodeKinds);
        Assert.Contains(WordDependencyNodeKind.NumberingInstance, nodeKinds);
        Assert.Contains(WordDependencyNodeKind.Bookmark, nodeKinds);
        Assert.Contains(WordDependencyNodeKind.Section, nodeKinds);
        Assert.Contains(WordDependencyEdgeKind.PackageRelationship, edgeKinds);
        Assert.Contains(WordDependencyEdgeKind.UsesStyle, edgeKinds);
        Assert.Contains(WordDependencyEdgeKind.UsesNumbering, edgeKinds);
        Assert.Contains(WordDependencyEdgeKind.FieldReference, edgeKinds);
        Assert.Contains(WordDependencyEdgeKind.SectionBindsStory, edgeKinds);
    }

    private static bool IsXml(string? contentType) =>
        contentType is not null
        && (
            contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase)
        );

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
