using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class PrePublicationDriftTests
{
    [Fact]
    public void IgnoresRawWordXmlDriftWhenSemanticStateIsIdentical()
    {
        var baseline = Snapshot(rawSuffix: "before");
        var observed = Snapshot(rawSuffix: "after");

        var differences = WordLiveService.PrePublicationDifferences(
            baseline,
            observed
        );

        Assert.Empty(differences);
    }

    [Fact]
    public void RejectsSemanticPackageDriftEvenWhenVisibleTextIsIdentical()
    {
        var baseline = Snapshot(rawSuffix: "before");
        var observed = Snapshot(
            rawSuffix: "after",
            semanticDocumentHash: "semantic-after"
        );

        var differences = WordLiveService.PrePublicationDifferences(
            baseline,
            observed
        );

        Assert.Equal(
            ["document_semantic_word_open_xml_sha256"],
            differences
        );
    }

    [Fact]
    public void RejectsStructuralDriftWithoutDependingOnRawXmlHashes()
    {
        var baseline = Snapshot(rawSuffix: "before");
        var observed = Snapshot(rawSuffix: "after", equationCount: 1);

        var differences = WordLiveService.PrePublicationDifferences(
            baseline,
            observed
        );

        Assert.Equal(["equation_count"], differences);
    }

    private static LiveRollbackSnapshot Snapshot(
        string rawSuffix,
        string semanticDocumentHash = "semantic",
        int equationCount = 0
    ) =>
        new(
            LiveVersion: 7,
            Saved: true,
            ContentStart: 0,
            ContentEnd: 1,
            TargetStart: 0,
            TargetEnd: 0,
            ContextStart: 0,
            ContextEnd: 1,
            ParagraphCount: 1,
            EquationCount: equationCount,
            TableCount: 0,
            FieldCount: 0,
            BookmarkCount: 0,
            InlineShapeCount: 0,
            ShapeCount: 0,
            CommentCount: 0,
            FootnoteCount: 0,
            EndnoteCount: 0,
            SectionCount: 1,
            DocumentWordOpenXmlSha256: "document-" + rawSuffix,
            DocumentSemanticWordOpenXmlSha256: semanticDocumentHash,
            ContentTextSha256: "content-text",
            ContentWordOpenXmlSha256: "content-" + rawSuffix,
            TargetTextSha256: "target-text",
            TargetWordOpenXmlSha256: "target-" + rawSuffix,
            ContextTextSha256: "context-text",
            ContextWordOpenXmlSha256: "context-" + rawSuffix,
            StoryDigest: new RollbackStoryDigest(1, "story-" + rawSuffix)
        );
}
