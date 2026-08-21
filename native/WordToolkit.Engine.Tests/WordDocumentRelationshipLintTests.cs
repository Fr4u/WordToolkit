using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordDocumentRelationshipLintTests
{
    [Fact]
    public void ReportsTypedUnusedAndOrphanRelationshipFindingsWithoutTargets()
    {
        using var bytes = BuildPackage();
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var report = new WordDocumentLinter().Analyze(package, semantic);

        var unused = Assert.Single(report.Findings, item =>
            item.RuleId == "WTL_RELATIONSHIP_UNUSED_EXPLICIT"
        );
        Assert.Equal("rIdDead", unused.Source.RelationshipId);
        Assert.Equal("remove_unreferenced_relationship", unused.Fix.Kind);
        Assert.False(unused.Fix.IsImplemented);
        Assert.DoesNotContain("secret.example", unused.Message, StringComparison.Ordinal);
        var orphan = Assert.Single(report.Findings, item =>
            item.RuleId == "WTL_RELATIONSHIP_ORPHAN_PART"
        );
        Assert.Equal("/word/_rels/missing.xml.rels", orphan.Source.PartUri);
        Assert.Equal("remove_orphan_relationship_part", orphan.Fix.Kind);
        Assert.False(orphan.Fix.IsImplemented);
    }

    private static MemoryStream BuildPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            Add(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Add(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>tekst</w:t></w:r></w:p></w:body></w:document>
                """);
            Add(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdDead" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://secret.example.invalid/private" TargetMode="External"/></Relationships>
                """);
            Add(archive, "word/_rels/missing.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }
}
