using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordReviewTransactionTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    [Theory]
    [InlineData(WordNamespace)]
    [InlineData(StrictWordNamespace)]
    public void AcceptsAndRejectsRunInsertionsAndDeletionsWithExactInverse(
        string wordNamespace
    )
    {
        var xml = DocumentXml(
            "<w:p><w:ins w:id='1' w:author='A'><w:r><w:t>new</w:t></w:r></w:ins>"
                + "<w:del w:id='2' w:author='B'><w:r><w:delText>old</w:delText></w:r></w:del></w:p>",
            wordNamespace
        );
        using var packageStream = BuildPackage(xml, [1, 2, 3, 4]);
        var reader = new OpcPackageReader();
        var package = reader.Read(packageStream);
        var graph = BuildGraph(package);
        var insertion = graph.Revisions.Single(revision =>
            revision.Kind == WordRevisionKind.Insertion
        );
        var deletion = graph.Revisions.Single(revision =>
            revision.Kind == WordRevisionKind.Deletion
        );
        var planner = new WordReviewMutationPlanner();

        var accept = planner.Plan(
            package,
            graph,
            [
                new WordReviewDecisionCommand(insertion.Id, WordReviewDecision.Accept),
                new WordReviewDecisionCommand(deletion.Id, WordReviewDecision.Accept),
            ]
        );
        var reject = planner.Plan(
            package,
            graph,
            [
                new WordReviewDecisionCommand(insertion.Id, WordReviewDecision.Reject),
                new WordReviewDecisionCommand(deletion.Id, WordReviewDecision.Reject),
            ]
        );

        Assert.True(accept.CanApply);
        Assert.True(reject.CanApply);
        Assert.StartsWith("wrplan_", accept.PlanId, StringComparison.Ordinal);
        Assert.Equal(2, accept.ChangedOperationCount);
        using var acceptedStream = Serialize(accept.CreateMutation(package));
        var accepted = reader.Read(acceptedStream);
        Assert.Equal(
            DocumentXml("<w:p><w:r><w:t>new</w:t></w:r></w:p>", wordNamespace),
            PartXml(accepted)
        );
        using var rejectedStream = Serialize(reject.CreateMutation(package));
        var rejected = reader.Read(rejectedStream);
        Assert.Equal(
            DocumentXml("<w:p><w:r><w:t>old</w:t></w:r></w:p>", wordNamespace),
            PartXml(rejected)
        );
        using var revertedStream = Serialize(accept.CreateInverseMutation(accepted));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
        Assert.Equal(
            package.Parts["/custom/opaque.bin"].Entry.Content.ToArray(),
            reverted.Parts["/custom/opaque.bin"].Entry.Content.ToArray()
        );
    }

    [Fact]
    public void RequiresCompleteMovePairAndRemovesAllRangeMarkers()
    {
        var xml = DocumentXml(
            "<w:p>"
                + "<w:moveFromRangeStart w:id='10' w:name='move-1'/>"
                + "<w:moveFrom w:id='11' w:author='A'><w:r><w:t>source</w:t></w:r></w:moveFrom>"
                + "<w:moveFromRangeEnd w:id='10'/>"
                + "<w:moveToRangeStart w:id='20' w:name='move-1'/>"
                + "<w:moveTo w:id='21' w:author='A'><w:r><w:t>destination</w:t></w:r></w:moveTo>"
                + "<w:moveToRangeEnd w:id='20'/>"
                + "</w:p>"
        );
        using var stream = BuildPackage(xml);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var graph = BuildGraph(package);
        var source = graph.Revisions.Single(revision =>
            revision.Kind == WordRevisionKind.MoveFrom
        );
        var destination = graph.Revisions.Single(revision =>
            revision.Kind == WordRevisionKind.MoveTo
        );
        var planner = new WordReviewMutationPlanner();

        var incomplete = planner.Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(source.Id, WordReviewDecision.Accept)]
        );
        var complete = planner.Plan(
            package,
            graph,
            [
                new WordReviewDecisionCommand(source.Id, WordReviewDecision.Accept),
                new WordReviewDecisionCommand(destination.Id, WordReviewDecision.Accept),
            ]
        );

        Assert.False(incomplete.CanApply);
        Assert.Contains(
            incomplete.Blocks,
            block => block.Code == "move_pair_not_selected"
        );
        Assert.Throws<WordReviewDecisionBlockedException>(() =>
            incomplete.CreateMutation(package)
        );
        Assert.True(complete.CanApply);
        Assert.Equal(4, complete.RemovedMoveMarkerCount);
        using var appliedStream = Serialize(complete.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        Assert.Equal(
            DocumentXml("<w:p><w:r><w:t>destination</w:t></w:r></w:p>"),
            PartXml(applied)
        );
    }

    [Fact]
    public void CascadesOnlyWhenExplicitlyEnabledAndBlocksConflictingNestedDecision()
    {
        var xml = DocumentXml(
            "<w:p><w:ins w:id='1' w:author='A'><w:ins w:id='2' w:author='B'>"
                + "<w:r><w:t>nested</w:t></w:r></w:ins></w:ins></w:p>"
        );
        using var stream = BuildPackage(xml);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var graph = BuildGraph(package);
        var outer = graph.Revisions.Single(revision => revision.ParentRevisionId is null);
        var inner = graph.Revisions.Single(revision => revision.ParentRevisionId is not null);

        var exact = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(outer.Id, WordReviewDecision.Reject)]
        );
        var cascading = new WordReviewMutationPlanner(
            new WordReviewTransactionOptions { AllowCascadingRevisions = true }
        ).Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(outer.Id, WordReviewDecision.Reject)]
        );
        var conflicting = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            [
                new WordReviewDecisionCommand(outer.Id, WordReviewDecision.Reject),
                new WordReviewDecisionCommand(inner.Id, WordReviewDecision.Accept),
            ]
        );

        Assert.False(exact.CanApply);
        Assert.Contains(exact.Blocks, block => block.Code == "unselected_nested_revision");
        Assert.True(cascading.CanApply);
        Assert.Equal(1, cascading.CascadeCount);
        Assert.Contains(cascading.Operations, operation =>
            operation.RevisionId == inner.Id
            && operation.IsImplicit
            && operation.IsAbsorbed
        );
        using var appliedStream = Serialize(cascading.CreateMutation(package));
        Assert.Equal(DocumentXml("<w:p></w:p>"), PartXml(reader.Read(appliedStream)));
        Assert.False(conflicting.CanApply);
        Assert.Contains(
            conflicting.Blocks,
            block => block.Code == "conflicting_nested_decision"
        );
    }

    [Fact]
    public void AcceptsAndRejectsWord2010ConflictWrappers()
    {
        var xml = $"<w:document xmlns:w='{WordNamespace}' "
            + "xmlns:w14='http://schemas.microsoft.com/office/word/2010/wordml'>"
            + "<w:body><w:p><w14:conflictIns w:id='11' w:author='A'>"
            + "<w:r><w:t>new</w:t></w:r></w14:conflictIns>"
            + "<w14:conflictDel w:id='12' w:author='A'>"
            + "<w:r><w:delText>old</w:delText></w:r></w14:conflictDel>"
            + "</w:p></w:body></w:document>";
        using var stream = BuildPackage(xml);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var graph = BuildGraph(package);
        Assert.Equal(2, graph.Revisions.Count);

        var accept = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            graph.Revisions.Select(revision =>
                new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Accept)
            )
        );
        Assert.True(accept.CanApply);
        using var acceptedStream = Serialize(accept.CreateMutation(package));
        var acceptedXml = PartXml(reader.Read(acceptedStream));
        Assert.Contains(">new<", acceptedXml, StringComparison.Ordinal);
        Assert.DoesNotContain(">old<", acceptedXml, StringComparison.Ordinal);
        Assert.DoesNotContain("conflict", acceptedXml, StringComparison.Ordinal);

        var reject = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            graph.Revisions.Select(revision =>
                new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Reject)
            )
        );
        Assert.True(reject.CanApply);
        using var rejectedStream = Serialize(reject.CreateMutation(package));
        var rejectedXml = PartXml(reader.Read(rejectedStream));
        Assert.DoesNotContain(">new<", rejectedXml, StringComparison.Ordinal);
        Assert.Contains("<w:t>old</w:t>", rejectedXml, StringComparison.Ordinal);
        Assert.DoesNotContain("conflict", rejectedXml, StringComparison.Ordinal);
        Assert.DoesNotContain("delText", rejectedXml, StringComparison.Ordinal);
    }

    [Fact]
    public void CascadingNestedMoveExpandsItsPairAndAvoidsOverlappingMarkerPatches()
    {
        var xml = DocumentXml(
            "<w:p><w:ins w:id='1' w:author='A'>"
                + "<w:moveFromRangeStart w:id='10' w:name='move-1'/>"
                + "<w:moveFrom w:id='11' w:author='A'><w:r><w:t>source</w:t></w:r></w:moveFrom>"
                + "<w:moveFromRangeEnd w:id='10'/></w:ins>"
                + "<w:moveToRangeStart w:id='20' w:name='move-1'/>"
                + "<w:moveTo w:id='21' w:author='A'><w:r><w:t>destination</w:t></w:r></w:moveTo>"
                + "<w:moveToRangeEnd w:id='20'/></w:p>"
        );
        using var stream = BuildPackage(xml);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var graph = BuildGraph(package);
        var outer = graph.Revisions.Single(revision =>
            revision.Kind == WordRevisionKind.Insertion
        );

        var plan = new WordReviewMutationPlanner(
            new WordReviewTransactionOptions { AllowCascadingRevisions = true }
        ).Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(outer.Id, WordReviewDecision.Reject)]
        );

        Assert.True(
            plan.CanApply,
            string.Join(" | ", plan.Blocks.Select(block => block.Code))
        );
        Assert.Equal(2, plan.CascadeCount);
        Assert.Equal(4, plan.RemovedMoveMarkerCount);
        using var appliedStream = Serialize(plan.CreateMutation(package));
        Assert.Equal(DocumentXml("<w:p></w:p>"), PartXml(reader.Read(appliedStream)));
    }

    [Fact]
    public void RestoresPropertySnapshotAndPreservesCurrentParagraphMarkProperties()
    {
        var xml = DocumentXml(
            "<w:p><w:pPr><w:spacing w:after='200'/><w:rPr><w:b/></w:rPr>"
                + "<w:pPrChange w:id='7' w:author='A'><w:pPr><w:spacing w:after='100'/></w:pPr>"
                + "</w:pPrChange></w:pPr><w:r><w:t>x</w:t></w:r></w:p>"
        );
        using var stream = BuildPackage(xml);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var graph = BuildGraph(package);
        var revision = Assert.Single(graph.Revisions);
        var plan = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Reject)]
        );

        Assert.True(plan.CanApply);
        using var appliedStream = Serialize(plan.CreateMutation(package));
        Assert.Equal(
            DocumentXml(
                "<w:p><w:pPr><w:spacing w:after='100'/><w:rPr><w:b/></w:rPr></w:pPr>"
                    + "<w:r><w:t>x</w:t></w:r></w:p>"
            ),
            PartXml(reader.Read(appliedStream))
        );
    }

    [Fact]
    public void AcceptsAndRejectsEveryModeledPropertySnapshotFamily()
    {
        var cases = new[]
        {
            (
                Body: "<w:p><w:r><w:rPr><w:b/><w:rPrChange w:id='1' w:author='A'>"
                    + "<w:rPr><w:i/></w:rPr></w:rPrChange></w:rPr><w:t>x</w:t></w:r></w:p>",
                Current: "<w:b/>",
                Previous: "<w:i/>"
            ),
            (
                Body: "<w:tbl><w:tblPr><w:tblW w:w='2000' w:type='dxa'/>"
                    + "<w:tblPrChange w:id='2' w:author='A'><w:tblPr>"
                    + "<w:tblW w:w='1000' w:type='dxa'/></w:tblPr></w:tblPrChange>"
                    + "</w:tblPr><w:tblGrid/><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>",
                Current: "w:w='2000'",
                Previous: "w:w='1000'"
            ),
            (
                Body: "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w='2000'/>"
                    + "<w:tblGridChange w:id='3' w:author='A'><w:tblGrid>"
                    + "<w:gridCol w:w='1000'/></w:tblGrid></w:tblGridChange></w:tblGrid>"
                    + "<w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>",
                Current: "w:w='2000'",
                Previous: "w:w='1000'"
            ),
            (
                Body: "<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:trPr><w:cantSplit/>"
                    + "<w:trPrChange w:id='4' w:author='A'><w:trPr><w:tblHeader/>"
                    + "</w:trPr></w:trPrChange></w:trPr><w:tc><w:p/></w:tc></w:tr></w:tbl>",
                Current: "<w:cantSplit/>",
                Previous: "<w:tblHeader/>"
            ),
            (
                Body: "<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:tc><w:tcPr>"
                    + "<w:tcW w:w='2000' w:type='dxa'/><w:tcPrChange w:id='5' w:author='A'>"
                    + "<w:tcPr><w:tcW w:w='1000' w:type='dxa'/></w:tcPr>"
                    + "</w:tcPrChange></w:tcPr><w:p/></w:tc></w:tr></w:tbl>",
                Current: "w:w='2000'",
                Previous: "w:w='1000'"
            ),
            (
                Body: "<w:p/><w:sectPr><w:pgSz w:w='10000' w:h='16000'/>"
                    + "<w:sectPrChange w:id='6' w:author='A'><w:sectPr>"
                    + "<w:pgSz w:w='12000' w:h='16000'/></w:sectPr></w:sectPrChange>"
                    + "</w:sectPr>",
                Current: "w:w='10000'",
                Previous: "w:w='12000'"
            ),
            (
                Body: "<w:p><w:pPr><w:numPr><w:numId w:val='2'/>"
                    + "<w:numPrChange w:id='7' w:author='A'><w:numPr>"
                    + "<w:numId w:val='1'/></w:numPr></w:numPrChange></w:numPr>"
                    + "</w:pPr><w:r><w:t>x</w:t></w:r></w:p>",
                Current: "w:val='2'",
                Previous: "w:val='1'"
            ),
            (
                Body: "<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:tblPrEx>"
                    + "<w:tblCellSpacing w:w='200' w:type='dxa'/>"
                    + "<w:tblPrExChange w:id='8' w:author='A'><w:tblPrEx>"
                    + "<w:tblCellSpacing w:w='100' w:type='dxa'/></w:tblPrEx>"
                    + "</w:tblPrExChange></w:tblPrEx><w:tc><w:p/></w:tc></w:tr></w:tbl>",
                Current: "w:w='200'",
                Previous: "w:w='100'"
            ),
        };

        foreach (var item in cases)
        {
            using var stream = BuildPackage(DocumentXml(item.Body));
            var reader = new OpcPackageReader();
            var package = reader.Read(stream);
            var graph = BuildGraph(package);
            var revision = Assert.Single(graph.Revisions);

            var accept = new WordReviewMutationPlanner().Plan(
                package,
                graph,
                [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Accept)]
            );
            Assert.True(accept.CanApply);
            using var acceptedStream = Serialize(accept.CreateMutation(package));
            var acceptedXml = PartXml(reader.Read(acceptedStream));
            Assert.Contains(item.Current, acceptedXml, StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"<{revision.SourceName}",
                acceptedXml,
                StringComparison.Ordinal
            );

            var reject = new WordReviewMutationPlanner().Plan(
                package,
                graph,
                [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Reject)]
            );
            Assert.True(
                reject.CanApply,
                string.Join(" | ", reject.Blocks.Select(block => block.Code))
            );
            using var rejectedStream = Serialize(reject.CreateMutation(package));
            var rejectedXml = PartXml(reader.Read(rejectedStream));
            Assert.Contains(item.Previous, rejectedXml, StringComparison.Ordinal);
            Assert.DoesNotContain(item.Current, rejectedXml, StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"<{revision.SourceName}",
                rejectedXml,
                StringComparison.Ordinal
            );
        }
    }

    [Fact]
    public void BlocksDuplicateOrCompetingParagraphMarkPropertySnapshots()
    {
        var propertyShapes = new[]
        {
            "<w:rPr><w:b/></w:rPr><w:rPr><w:i/></w:rPr>"
                + "<w:pPrChange w:id='7' w:author='A'><w:pPr/></w:pPrChange>",
            "<w:rPr><w:b/></w:rPr><w:pPrChange w:id='7' w:author='A'>"
                + "<w:pPr><w:rPr><w:i/></w:rPr></w:pPr></w:pPrChange>",
            "<w:pPrChange w:id='7' w:author='A'><w:pPr>"
                + "<w:rPr><w:b/></w:rPr><w:rPr><w:i/></w:rPr>"
                + "</w:pPr></w:pPrChange>",
        };

        foreach (var propertyShape in propertyShapes)
        {
            using var stream = BuildPackage(
                DocumentXml($"<w:p><w:pPr>{propertyShape}</w:pPr></w:p>")
            );
            var package = new OpcPackageReader().Read(stream);
            var revision = Assert.Single(BuildGraph(package).Revisions);

            var plan = new WordReviewMutationPlanner().Plan(
                package,
                BuildGraph(package),
                [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Reject)]
            );

            Assert.False(plan.CanApply);
            Assert.Contains(
                plan.Blocks,
                block => block.Code == "paragraph_mark_properties_ambiguous"
            );
        }
    }

    [Fact]
    public void MergesFollowingParagraphForProvenParagraphMarkDecisionsAndRevertsExactly()
    {
        var cases = new[]
        {
            (Marker: "del", Decision: WordReviewDecision.Accept),
            (Marker: "ins", Decision: WordReviewDecision.Reject),
        };
        foreach (var item in cases)
        {
            var xml = DocumentXml(
                $"<w:p><w:pPr><w:rPr><w:{item.Marker} w:id='9' w:author='A'/>"
                    + "</w:rPr></w:pPr><w:r><w:t>x </w:t></w:r></w:p>"
                    + "<w:p><w:r><w:t>y</w:t></w:r></w:p>"
            );
            using var stream = BuildPackage(xml);
            var reader = new OpcPackageReader();
            var package = reader.Read(stream);
            var graph = BuildGraph(package);
            var revision = Assert.Single(graph.Revisions);

            var plan = new WordReviewMutationPlanner().Plan(
                package,
                graph,
                [new WordReviewDecisionCommand(revision.Id, item.Decision)]
            );

            Assert.True(plan.CanApply);
            using var appliedStream = Serialize(plan.CreateMutation(package));
            var applied = reader.Read(appliedStream);
            Assert.Equal(
                DocumentXml(
                    "<w:p><w:pPr><w:rPr></w:rPr></w:pPr>"
                        + "<w:r><w:t>x </w:t></w:r><w:r><w:t>y</w:t></w:r></w:p>"
                ),
                PartXml(applied)
            );
            using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
            Assert.Equal(package.Fingerprint, reader.Read(revertedStream).Fingerprint);
        }
    }

    [Fact]
    public void BlocksParagraphMarkMergeWithoutSafeImmediateTarget()
    {
        var shapes = new[]
        {
            "<w:p><w:pPr><w:rPr><w:del w:id='9' w:author='A'/></w:rPr>"
                + "</w:pPr><w:r><w:t>x</w:t></w:r></w:p>",
            "<w:p><w:pPr><w:rPr><w:del w:id='9' w:author='A'/></w:rPr>"
                + "</w:pPr><w:r><w:t>x</w:t></w:r></w:p>"
                + "<w:p><w:pPr><w:keepNext/></w:pPr><w:r><w:t>y</w:t></w:r></w:p>",
        };
        foreach (var shape in shapes)
        {
            using var stream = BuildPackage(DocumentXml(shape));
            var package = new OpcPackageReader().Read(stream);
            var revision = Assert.Single(BuildGraph(package).Revisions);
            var plan = new WordReviewMutationPlanner().Plan(
                package,
                BuildGraph(package),
                [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Accept)]
            );
            Assert.False(plan.CanApply);
            Assert.Contains(
                plan.Blocks,
                block => block.Code is "paragraph_merge_target_missing"
                    or "paragraph_merge_properties_ambiguous"
            );
        }
    }

    [Fact]
    public void AcceptsInsertedCellMarkerButBlocksGridBlindRejection()
    {
        var xml = DocumentXml(
            "<w:tbl><w:tblGrid><w:gridCol w:w='1000'/><w:gridCol w:w='1000'/>"
                + "</w:tblGrid><w:tr><w:tc><w:p/></w:tc><w:tc><w:tcPr>"
                + "<w:cellIns w:id='5' w:author='A'/></w:tcPr><w:p/></w:tc>"
                + "</w:tr></w:tbl>"
        );
        using var stream = BuildPackage(xml);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var graph = BuildGraph(package);
        var revision = Assert.Single(graph.Revisions);

        var accept = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Accept)]
        );
        Assert.True(accept.CanApply);
        using var acceptedStream = Serialize(accept.CreateMutation(package));
        var acceptedXml = PartXml(reader.Read(acceptedStream));
        Assert.DoesNotContain("cellIns", acceptedXml, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(acceptedXml, "<w:tc>").Count);

        var reject = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Reject)]
        );
        Assert.False(reject.CanApply);
        Assert.Contains(
            reject.Blocks,
            block => block.Code == "unsupported_table_revision"
        );
    }

    [Fact]
    public void AcceptsNumberingChangeMarkerButBlocksFakeRejection()
    {
        var xml = DocumentXml(
            "<w:p><w:pPr><w:numPr><w:ilvl w:val='0'/><w:numId w:val='1'/>"
                + "<w:numberingChange w:id='6' w:author='A' w:original='%1:1:0:.'/></w:numPr>"
                + "</w:pPr><w:r><w:t>x</w:t></w:r></w:p>"
        );
        using var stream = BuildPackage(xml);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var graph = BuildGraph(package);
        var revision = Assert.Single(graph.Revisions);

        var accept = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Accept)]
        );
        Assert.True(accept.CanApply);
        using var acceptedStream = Serialize(accept.CreateMutation(package));
        Assert.DoesNotContain(
            "numberingChange",
            PartXml(reader.Read(acceptedStream)),
            StringComparison.Ordinal
        );

        var reject = new WordReviewMutationPlanner().Plan(
            package,
            graph,
            [new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Reject)]
        );
        Assert.False(reject.CanApply);
        Assert.Contains(
            reject.Blocks,
            block => block.Code == "unsupported_numbering_revision"
        );
    }

    [Fact]
    public void BlocksBothCellMergeDecisionsUntilVerticalStateIsRestored()
    {
        var xml = DocumentXml(
            "<w:tbl><w:tblGrid><w:gridCol w:w='1000'/></w:tblGrid><w:tr><w:tc>"
                + "<w:tcPr><w:cellMerge w:id='8' w:author='A' w:vMerge='cont' "
                + "w:vMergeOrig='rest'/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>"
        );
        using var stream = BuildPackage(xml);
        var package = new OpcPackageReader().Read(stream);
        var graph = BuildGraph(package);
        var revision = Assert.Single(graph.Revisions);

        foreach (var decision in Enum.GetValues<WordReviewDecision>())
        {
            var plan = new WordReviewMutationPlanner().Plan(
                package,
                graph,
                [new WordReviewDecisionCommand(revision.Id, decision)]
            );
            Assert.False(plan.CanApply);
            Assert.Contains(
                plan.Blocks,
                block => block.Code == "unsupported_table_revision"
            );
        }
    }

    [Theory]
    [InlineData("real_tracked_changes.docx", 0)]
    [InlineData("poi_tracked_changes_delins.docx", 2)]
    [InlineData("pandoc_track_move.docx", 0)]
    public void AcceptsCoveredRealCorpusRevisionsAndCanRestoreExactPackage(
        string name,
        int expectedRemainingRevisions
    )
    {
        var reader = new OpcPackageReader();
        var package = reader.Read(Fixture(name));
        var graph = BuildGraph(package);
        Assert.NotEmpty(graph.Revisions);
        var commands = graph.Revisions
            .Where(revision =>
                revision.Kind != WordRevisionKind.Deletion
                || revision.ContentElementCount != 0
            )
            .Select(revision =>
                new WordReviewDecisionCommand(revision.Id, WordReviewDecision.Accept)
            )
            .ToArray();

        var plan = new WordReviewMutationPlanner().Plan(package, graph, commands);

        Assert.True(
            plan.CanApply,
            string.Join(" | ", plan.Blocks.Select(block => block.Code + ":" + block.Message))
        );
        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        var appliedGraph = BuildGraph(applied);
        Assert.Equal(expectedRemainingRevisions, appliedGraph.Revisions.Count);
        Assert.Empty(appliedGraph.MoveRanges);
        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
    }

    [Fact]
    public void RejectsUndefinedDecisionEnumInsteadOfTreatingItAsReject()
    {
        using var stream = BuildPackage(
            DocumentXml(
                "<w:p><w:ins w:id='1' w:author='A'><w:r><w:t>x</w:t></w:r>"
                    + "</w:ins></w:p>"
            )
        );
        var package = new OpcPackageReader().Read(stream);
        var graph = BuildGraph(package);
        var revision = Assert.Single(graph.Revisions);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WordReviewMutationPlanner().Plan(
                package,
                graph,
                [new WordReviewDecisionCommand(revision.Id, (WordReviewDecision)99)]
            )
        );

        Assert.Equal("commands", exception.ParamName);
    }

    private static string DocumentXml(
        string body,
        string wordNamespace = WordNamespace
    ) => $"<w:document xmlns:w='{wordNamespace}'><w:body>{body}</w:body></w:document>";

    private static string PartXml(OpcPackageSnapshot package) => Encoding.UTF8.GetString(
        package.Parts["/word/document.xml"].Entry.Content.Span
    );

    private static WordReviewGraph BuildGraph(OpcPackageSnapshot package) =>
        new WordReviewGraphBuilder().Build(
            package,
            new WordSemanticProjector().Project(package)
        );

    private static MemoryStream BuildPackage(string documentXml, byte[]? opaque = null)
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

    private static string Fixture(string name) => Path.Combine(
        FindRepositoryRoot(),
        "tests",
        "upstream",
        "fixtures",
        name
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
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
