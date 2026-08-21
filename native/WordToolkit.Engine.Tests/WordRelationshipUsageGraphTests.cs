using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordRelationshipUsageGraphTests
{
    [Fact]
    public void ClassifiesReferencedImplicitUnusedUnknownBinaryAndOrphanRelationships()
    {
        using var bytes = BuildPackage();
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordRelationshipUsageGraphBuilder(
            new WordRelationshipUsageGraphOptions
            {
                MaxReferencesPerRelationship = 1,
            }
        ).Build(package);

        Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
        Assert.Equal(1, graph.ParsedOwnerPartCount);
        Assert.Equal(
            WordRelationshipUsageStatus.PackageRelationship,
            Find(graph, "/", "rIdRoot").Status
        );
        var image = Find(graph, "/word/document.xml", "rIdImage");
        Assert.Equal(WordRelationshipUsageStatus.ReferencedByMarkup, image.Status);
        Assert.Equal(2, image.MarkupReferenceCount);
        Assert.True(image.MarkupReferencesTruncated);
        Assert.Equal(
            WordRelationshipUsageStatus.ImplicitByRelationshipType,
            Find(graph, "/word/document.xml", "rIdStyles").Status
        );
        Assert.True(
            Find(graph, "/word/document.xml", "rIdDeadLink")
                .MarkupRemovalCandidate
        );
        Assert.Equal(
            WordRelationshipUsageStatus.UnknownUnreferencedRelationship,
            Find(graph, "/word/document.xml", "rIdUnknown").Status
        );
        Assert.Equal(
            WordRelationshipUsageStatus.UnknownUnreferencedRelationship,
            Find(graph, "/word/document.xml", "rIdLookalike").Status
        );
        Assert.Equal(
            WordRelationshipUsageStatus.OwnerNonXml,
            Find(graph, "/word/media/image1.png", "rIdBinary").Status
        );
        Assert.Equal(
            WordRelationshipUsageStatus.OwnerMissing,
            Find(graph, "/word/missing.xml", "rIdOrphan").Status
        );
        var orphan = Assert.Single(graph.OrphanRelationshipParts);
        Assert.Equal("/word/missing.xml", orphan.SourcePartUri);
        Assert.Equal(1, orphan.ParsedRelationshipCount);
        Assert.Equal(1, graph.MarkupRemovalCandidateCount);
    }

    [Fact]
    public void CountsReferencesInsideEveryMceBranchAndNeverSelectsAView()
    {
        using var bytes = BuildPackage(
            documentXml: """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"><w:body><mc:AlternateContent><mc:Choice Requires="w"><w:p r:id="rIdDeadLink"/></mc:Choice><mc:Fallback><w:p/></mc:Fallback></mc:AlternateContent></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);

        var usage = Find(
            new WordRelationshipUsageGraphBuilder().Build(package),
            "/word/document.xml",
            "rIdDeadLink"
        );

        Assert.Equal(WordRelationshipUsageStatus.ReferencedByMarkup, usage.Status);
        Assert.Equal(1, usage.MarkupReferenceCount);
        Assert.False(usage.MarkupRemovalCandidate);
    }

    [Fact]
    public void MakesMalformedOwnerXmlAndLimitsExplicit()
    {
        using var bytes = BuildPackage(documentXml: "<broken");
        var package = new OpcPackageReader().Read(bytes);
        var graph = new WordRelationshipUsageGraphBuilder().Build(package);
        Assert.Equal(
            WordRelationshipUsageStatus.OwnerXmlUnparseable,
            Find(graph, "/word/document.xml", "rIdDeadLink").Status
        );

        Assert.Throws<WordRelationshipUsageLimitException>(() =>
            new WordRelationshipUsageGraphBuilder(
                new WordRelationshipUsageGraphOptions { MaxRelationships = 1 }
            ).Build(package)
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new WordRelationshipUsageGraphBuilder().Build(
                package,
                cancellation.Token
            )
        );
    }

    [Fact]
    public void DuplicateRelationshipIdsAreAmbiguousAndNeverRemovalCandidates()
    {
        using var bytes = BuildDuplicateRelationshipPackage();
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordRelationshipUsageGraphBuilder().Build(package);

        var duplicates = graph.Relationships.Where(item =>
            item.SourcePartUri == "/word/document.xml" && item.RelationshipId == "rIdSame"
        ).ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.All(duplicates, item =>
        {
            Assert.Equal(WordRelationshipUsageStatus.DuplicateRelationshipId, item.Status);
            Assert.False(item.MarkupRemovalCandidate);
        });
        Assert.False(graph.TryGetRelationship(
            "/word/document.xml",
            "rIdSame",
            out _
        ));
    }

    private static WordRelationshipUsage Find(
        WordRelationshipUsageGraph graph,
        string source,
        string id
    ) => graph.Relationships.Single(item =>
        item.SourcePartUri == source && item.RelationshipId == id
    );

    private static MemoryStream BuildPackage(string? documentXml = null)
    {
        documentXml ??= """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body><w:p r:embed="rIdImage" custom="rIdImage"/></w:body></w:document>
            """;
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>
                """);
            Write(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Write(archive, "word/document.xml", documentXml);
            Write(archive, "word/styles.xml", """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"/>
                """);
            Write(archive, "word/media/image1.png", "not really png");
            Write(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
                  <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                  <Relationship Id="rIdDeadLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.invalid/dead" TargetMode="External"/>
                  <Relationship Id="rIdUnknown" Type="urn:wordtoolkit:unknown" Target="https://example.invalid/unknown" TargetMode="External"/>
                  <Relationship Id="rIdLookalike" Type="https://attacker.invalid/hyperlink" Target="https://example.invalid/lookalike" TargetMode="External"/>
                </Relationships>
                """);
            Write(archive, "word/media/_rels/image1.png.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdBinary" Type="urn:wordtoolkit:binary" Target="https://example.invalid/binary" TargetMode="External"/></Relationships>
                """);
            Write(archive, "word/_rels/missing.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdOrphan" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/></Relationships>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildDuplicateRelationshipPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            Write(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Write(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body/></w:document>
                """);
            Write(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdSame" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://one.invalid" TargetMode="External"/><Relationship Id="rIdSame" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://two.invalid" TargetMode="External"/></Relationships>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var target = entry.Open();
        target.Write(Encoding.UTF8.GetBytes(content));
    }
}
