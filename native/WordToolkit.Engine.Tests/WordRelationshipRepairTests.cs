using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordRelationshipRepairTests
{
    [Fact]
    public void PlansBatchRemovesOnlyReviewedRelationshipDataAndRoundTripsExactly()
    {
        using var bytes = BuildPackage();
        var reader = new OpcPackageReader();
        var baseline = reader.Read(bytes);
        var usage = new WordRelationshipUsageGraphBuilder().Build(baseline);
        var deadLink = Find(usage, "/word/document.xml", "rIdDeadLink");
        var orphan = Assert.Single(usage.OrphanRelationshipParts);

        var plan = new WordRelationshipRepairPlanner().Plan(
            baseline,
            [
                new RemoveUnreferencedRelationshipCommand(
                    deadLink.SourcePartUri,
                    deadLink.RelationshipId,
                    deadLink.Fingerprint
                ),
                new RemoveOrphanRelationshipPartCommand(
                    orphan.RelationshipPartUri,
                    orphan.EntrySha256
                ),
            ]
        );

        Assert.StartsWith("wrrplan_", plan.PlanId, StringComparison.Ordinal);
        Assert.True(plan.Validation.Passed);
        Assert.True(plan.Validation.SemanticProjectionPreserved);
        Assert.True(plan.Validation.UnplannedEntriesPreserved);
        Assert.True(plan.Validation.ExactInverseVerified);
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal(2, plan.ChangedEntries.Count);
        Assert.DoesNotContain(
            plan.ChangedEntries,
            entry => entry.EntryName == "word/media/image1.png"
        );
        Assert.Contains(
            "relationship_deletion_never_deletes_target_part",
            plan.SafetyRules
        );

        using var appliedBytes = new MemoryStream();
        new OpcPackageSerializer().Write(appliedBytes, plan.CreateMutation(baseline));
        appliedBytes.Position = 0;
        var applied = reader.Read(appliedBytes);
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        Assert.DoesNotContain(applied.Relationships, relationship =>
            relationship.SourcePartUri == "/word/document.xml"
            && relationship.Id == "rIdDeadLink"
        );
        Assert.DoesNotContain(applied.Entries, entry =>
            entry.Name == "word/_rels/missing.xml.rels"
        );
        Assert.Contains(applied.Entries, entry => entry.Name == "word/media/image1.png");

        using var restoredBytes = new MemoryStream();
        new OpcPackageSerializer().Write(
            restoredBytes,
            plan.CreateInverseMutation(applied)
        );
        restoredBytes.Position = 0;
        Assert.Equal(baseline.Fingerprint, reader.Read(restoredBytes).Fingerprint);
    }

    [Fact]
    public void RejectsRemovingReferencedImplicitUnknownAndRootRelationships()
    {
        using var bytes = BuildPackage();
        var baseline = new OpcPackageReader().Read(bytes);
        var graph = new WordRelationshipUsageGraphBuilder().Build(baseline);
        var planner = new WordRelationshipRepairPlanner();

        foreach (var usage in new[]
        {
            Find(graph, "/word/document.xml", "rIdImage"),
            Find(graph, "/word/document.xml", "rIdStyles"),
            Find(graph, "/word/document.xml", "rIdUnknown"),
        })
        {
            Assert.Throws<WordSemanticEditException>(() => planner.Plan(
                baseline,
                [new RemoveUnreferencedRelationshipCommand(
                    usage.SourcePartUri,
                    usage.RelationshipId,
                    usage.Fingerprint
                )]
            ));
        }

        var root = Find(graph, "/", "rIdRoot");
        Assert.Throws<WordSemanticEditException>(() => planner.Plan(
            baseline,
            [new RemoveUnreferencedRelationshipCommand(
                root.SourcePartUri,
                root.RelationshipId,
                root.Fingerprint
            )]
        ));
    }

    [Fact]
    public void RejectsFingerprintDriftDuplicatesAndCommandLimit()
    {
        using var bytes = BuildPackage();
        var baseline = new OpcPackageReader().Read(bytes);
        var graph = new WordRelationshipUsageGraphBuilder().Build(baseline);
        var dead = Find(graph, "/word/document.xml", "rIdDeadLink");
        var command = new RemoveUnreferencedRelationshipCommand(
            dead.SourcePartUri,
            dead.RelationshipId,
            dead.Fingerprint
        );

        Assert.Throws<WordSemanticPreconditionException>(() =>
            new WordRelationshipRepairPlanner().Plan(
                baseline,
                [command with { ExpectedRelationshipFingerprint = new string('0', 64) }]
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new WordRelationshipRepairPlanner().Plan(baseline, [command, command])
        );
        Assert.Throws<WordSemanticTransactionLimitException>(() =>
            new WordRelationshipRepairPlanner(
                new WordRelationshipRepairOptions { MaxCommands = 1 }
            ).Plan(baseline, [command, command with { RelationshipId = "other" }])
        );
    }

    [Fact]
    public void RefusesRemovalThatWouldCreateANewUnreachableTargetPart()
    {
        using var bytes = BuildPackage(
            documentXml: """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>tekst</w:t></w:r></w:p></w:body></w:document>
            """,
            includeReferencedImageRelationship: false,
            includeUnusedInternalImageRelationship: true
        );
        var baseline = new OpcPackageReader().Read(bytes);
        var graph = new WordRelationshipUsageGraphBuilder().Build(baseline);
        var unused = Find(graph, "/word/document.xml", "rIdUnusedImage");
        Assert.True(unused.MarkupRemovalCandidate);

        var exception = Assert.Throws<WordSemanticEditException>(() =>
            new WordRelationshipRepairPlanner().Plan(
                baseline,
                [new RemoveUnreferencedRelationshipCommand(
                    unused.SourcePartUri,
                    unused.RelationshipId,
                    unused.Fingerprint
                )]
            )
        );
        Assert.Contains("candidate failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HonorsCancellationBeforePlanning()
    {
        using var bytes = BuildPackage();
        var baseline = new OpcPackageReader().Read(bytes);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new WordRelationshipRepairPlanner().Plan(
                baseline,
                [new RemoveOrphanRelationshipPartCommand("/x/_rels/y.rels", "hash")],
                cancellation.Token
            )
        );
    }

    private static WordRelationshipUsage Find(
        WordRelationshipUsageGraph graph,
        string source,
        string id
    ) => graph.Relationships.Single(item =>
        item.SourcePartUri == source && item.RelationshipId == id
    );

    private static MemoryStream BuildPackage(
        string? documentXml = null,
        bool includeReferencedImageRelationship = true,
        bool includeUnusedInternalImageRelationship = false
    )
    {
        documentXml ??= """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body><w:p><w:r><w:drawing r:embed="rIdImage"/></w:r><w:r><w:t>tekst</w:t></w:r></w:p></w:body></w:document>
            """;
        var relationships = new StringBuilder();
        relationships.Append("""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdDeadLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.invalid/dead" TargetMode="External"/>
              <Relationship Id="rIdUnknown" Type="urn:wordtoolkit:unknown" Target="https://example.invalid/unknown" TargetMode="External"/>
            """);
        if (includeReferencedImageRelationship)
        {
            relationships.Append("""
              <Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
            """);
        }
        if (includeUnusedInternalImageRelationship)
        {
            relationships.Append("""
              <Relationship Id="rIdUnusedImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
            """);
        }
        relationships.Append("</Relationships>");

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
            Write(archive, "word/_rels/document.xml.rels", relationships.ToString());
            Write(archive, "word/_rels/missing.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdOrphan" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/></Relationships>
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
