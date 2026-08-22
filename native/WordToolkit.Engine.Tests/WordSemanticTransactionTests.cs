using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSemanticTransactionTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void PlansTwoTextEditsAsOnePartMutationAndExactInverse()
    {
        const string documentXml = """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body data-x="keep"><!--opaque--><w:p><w:r><w:t a="1">first &amp; old</w:t></w:r><w:r><w:t /></w:r></w:p></w:body></w:document>
            """;
        using var packageStream = BuildPackage(documentXml, [1, 3, 3, 7]);
        var reader = new OpcPackageReader();
        var package = reader.Read(packageStream);
        var semantic = new WordSemanticProjector().Project(package);
        var textNodes = semantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Text)
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        Assert.Equal(2, textNodes.Length);
        var commands = new[]
        {
            new WordTextReplacementCommand(
                textNodes[0].Id,
                "first < new",
                "first & old"
            ),
            new WordTextReplacementCommand(textNodes[1].Id, " second ", string.Empty),
        };
        var planner = new WordSemanticTransactionPlanner();

        var plan = planner.PlanTextReplacements(package, semantic, commands);
        var repeated = planner.PlanTextReplacements(package, semantic, commands);

        Assert.StartsWith("wplan_", plan.PlanId, StringComparison.Ordinal);
        Assert.Equal(plan.PlanId, repeated.PlanId);
        Assert.Equal(2, plan.OperationCount);
        Assert.Equal(2, plan.ChangedOperationCount);
        Assert.Equal(1, plan.ChangedPartCount);
        Assert.True(plan.HasChanges);
        Assert.NotEqual(package.Fingerprint, plan.ResultPackageFingerprint);
        Assert.All(plan.Operations, operation =>
        {
            Assert.Equal("replace_text", operation.Kind);
            Assert.True(operation.HasChange);
        });

        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        var appliedXml = Encoding.UTF8.GetString(
            applied.Parts["/word/document.xml"].Entry.Content.Span
        );
        Assert.Equal(
            documentXml
                .Replace(
                    "<w:t a=\"1\">first &amp; old</w:t>",
                    "<w:t a=\"1\">first &lt; new</w:t>",
                    StringComparison.Ordinal
                )
                .Replace(
                    "<w:t />",
                    "<w:t  xml:space=\"preserve\"> second </w:t>",
                    StringComparison.Ordinal
                ),
            appliedXml
        );
        Assert.Equal(
            package.Parts["/custom/opaque.bin"].Entry.Sha256,
            applied.Parts["/custom/opaque.bin"].Entry.Sha256
        );

        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
        Assert.Equal(
            package.Parts["/word/document.xml"].Entry.Content.ToArray(),
            reverted.Parts["/word/document.xml"].Entry.Content.ToArray()
        );
        Assert.Equal(
            package.Parts["/custom/opaque.bin"].Entry.Content.ToArray(),
            reverted.Parts["/custom/opaque.bin"].Entry.Content.ToArray()
        );
    }

    [Fact]
    public void NoOpPlanHasNoPartPayloadAndCreatesEmptyForwardAndInverseMutations()
    {
        using var stream = BuildPackage(DocumentXml("same"));
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var text = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);

        var plan = new WordSemanticTransactionPlanner().PlanTextReplacements(
            package,
            semantic,
            [new WordTextReplacementCommand(text.Id, "same", "same")]
        );

        Assert.False(plan.HasChanges);
        Assert.Equal(0, plan.ChangedPartCount);
        Assert.Equal(0, plan.ChangedOperationCount);
        Assert.Equal(package.Fingerprint, plan.ResultPackageFingerprint);
        Assert.False(plan.CreateMutation(package).HasChanges);
        Assert.False(plan.CreateInverseMutation(package).HasChanges);
    }

    [Fact]
    public void RejectsDuplicateTargetAndEmptyCommandSet()
    {
        using var stream = BuildPackage(DocumentXml("one"));
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var text = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);
        var planner = new WordSemanticTransactionPlanner();

        Assert.Throws<ArgumentException>(() =>
            planner.PlanTextReplacements(
                package,
                semantic,
                Array.Empty<WordTextReplacementCommand>()
            )
        );
        Assert.Throws<WordSemanticEditException>(() =>
            planner.PlanTextReplacements(
                package,
                semantic,
                [
                    new WordTextReplacementCommand(text.Id, "two"),
                    new WordTextReplacementCommand(text.Id, "three"),
                ]
            )
        );
    }

    [Theory]
    [InlineData("w", "ins", "t")]
    [InlineData("w", "del", "delText")]
    [InlineData("w", "moveFrom", "delText")]
    [InlineData("w", "moveTo", "t")]
    [InlineData("w14", "conflictIns", "t")]
    [InlineData("w14", "conflictDel", "delText")]
    public void RejectsPlainTextEditsInsideTrackedRevisionsWithoutChangingPackage(
        string revisionPrefix,
        string revisionElement,
        string textElement
    )
    {
        var documentXml = $"""
            <w:document xmlns:w="{WordNamespace}" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"><w:body><w:p><{revisionPrefix}:{revisionElement} w:id="1" w:author="Author"><w:r><w:{textElement}>old</w:{textElement}></w:r></{revisionPrefix}:{revisionElement}></w:p></w:body></w:document>
            """;
        using var stream = BuildPackage(documentXml);
        var package = new OpcPackageReader().Read(stream);
        var originalFingerprint = package.Fingerprint;
        var originalDocumentBytes = package.Parts["/word/document.xml"].Entry.Content.ToArray();
        var semantic = new WordSemanticProjector().Project(package);
        if (revisionPrefix == "w")
        {
            Assert.Contains(
                semantic.Nodes,
                node => node.Kind == WordSemanticNodeKind.Revision
            );
        }
        var text = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);

        var error = Assert.Throws<WordSemanticEditException>(() =>
            new WordSemanticTransactionPlanner().PlanTextReplacements(
                package,
                semantic,
                [new WordTextReplacementCommand(text.Id, "new", "old")]
            )
        );

        Assert.Contains("inside tracked revision markup", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalFingerprint, package.Fingerprint);
        Assert.Equal(
            originalDocumentBytes,
            package.Parts["/word/document.xml"].Entry.Content.ToArray()
        );
    }

    [Fact]
    public void EnforcesCommandAndReplacementCharacterLimits()
    {
        using var stream = BuildPackage(DocumentXml("one", "two"));
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var texts = semantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Text)
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        var commands = new[]
        {
            new WordTextReplacementCommand(texts[0].Id, "a"),
            new WordTextReplacementCommand(texts[1].Id, "b"),
        };

        Assert.Throws<WordSemanticTransactionLimitException>(() =>
            new WordSemanticTransactionPlanner(
                new WordSemanticTransactionOptions { MaxCommands = 1 }
            ).PlanTextReplacements(package, semantic, commands)
        );
        Assert.Throws<WordSemanticTransactionLimitException>(() =>
            new WordSemanticTransactionPlanner(
                new WordSemanticTransactionOptions
                {
                    MaxTotalReplacementCharacters = 1,
                }
            ).PlanTextReplacements(package, semantic, commands)
        );
    }

    [Fact]
    public void ForwardMutationRejectsDifferentBaseSnapshot()
    {
        using var firstStream = BuildPackage(DocumentXml("one"));
        var reader = new OpcPackageReader();
        var first = reader.Read(firstStream);
        var semantic = new WordSemanticProjector().Project(first);
        var text = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);
        var plan = new WordSemanticTransactionPlanner().PlanTextReplacements(
            first,
            semantic,
            [new WordTextReplacementCommand(text.Id, "changed")]
        );
        using var secondStream = BuildPackage(DocumentXml("other"));
        var second = reader.Read(secondStream);

        Assert.Throws<WordSemanticPreconditionException>(() =>
            plan.CreateMutation(second)
        );
    }

    [Fact]
    public void InverseRejectsResultThatWasChangedAfterApply()
    {
        using var originalStream = BuildPackage(DocumentXml("one"));
        var reader = new OpcPackageReader();
        var original = reader.Read(originalStream);
        var semantic = new WordSemanticProjector().Project(original);
        var text = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);
        var plan = new WordSemanticTransactionPlanner().PlanTextReplacements(
            original,
            semantic,
            [new WordTextReplacementCommand(text.Id, "two")]
        );
        using var appliedStream = Serialize(plan.CreateMutation(original));
        var applied = reader.Read(appliedStream);
        var tamperedXml = Encoding.UTF8.GetString(
            applied.Parts["/word/document.xml"].Entry.Content.Span
        ).Replace("two", "tampered", StringComparison.Ordinal);
        var tamperedMutation = new OpcPackageMutationBuilder(applied).ReplacePart(
            "/word/document.xml",
            Encoding.UTF8.GetBytes(tamperedXml),
            applied.Parts["/word/document.xml"].Entry.Sha256
        );
        using var tamperedStream = Serialize(tamperedMutation);
        var tampered = reader.Read(tamperedStream);

        Assert.Throws<WordSemanticPreconditionException>(() =>
            plan.CreateInverseMutation(tampered)
        );
    }

    [Fact]
    public void CancellationStopsPlanningBeforeSourceParsing()
    {
        using var stream = BuildPackage(DocumentXml("one"));
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var text = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new WordSemanticTransactionPlanner().PlanTextReplacements(
                package,
                semantic,
                [new WordTextReplacementCommand(text.Id, "two")],
                cancellation.Token
            )
        );
    }

    private static string DocumentXml(string first, string? second = null)
    {
        var secondRun = second is null
            ? string.Empty
            : $"<w:r><w:t>{second}</w:t></w:r>";
        return $"""
            <w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:r><w:t>{first}</w:t></w:r>{secondRun}</w:p></w:body></w:document>
            """;
    }

    private static MemoryStream BuildPackage(
        string documentXml,
        byte[]? opaque = null
    )
    {
        var entries = new List<(string Name, byte[] Content)>
        {
            ("[Content_Types].xml", Encoding.UTF8.GetBytes(ContentTypes(opaque is not null))),
            ("_rels/.rels", Encoding.UTF8.GetBytes(RootRelationships())),
            ("word/document.xml", Encoding.UTF8.GetBytes(documentXml)),
        };
        if (opaque is not null)
        {
            entries.Add(("custom/opaque.bin", opaque));
        }

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Serialize(OpcPackageMutationBuilder mutation)
    {
        var stream = new MemoryStream();
        new OpcPackageSerializer().Write(stream, mutation);
        stream.Position = 0;
        return stream;
    }

    private static string ContentTypes(bool includeOpaque) => $"""
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
          <Default Extension="xml" ContentType="application/xml" />
          {(includeOpaque ? "<Default Extension=\"bin\" ContentType=\"application/octet-stream\" />" : string.Empty)}
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
        </Types>
        """;

    private static string RootRelationships() => """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
        </Relationships>
        """;
}
