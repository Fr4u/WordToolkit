using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordReviewGraphTests
{
    private const string Word =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrict =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string Word2010 =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string Word2012 =
        "http://schemas.microsoft.com/office/word/2012/wordml";
    private const string Word2016Cid =
        "http://schemas.microsoft.com/office/word/2016/wordml/cid";
    private const string Word2018Cex =
        "http://schemas.microsoft.com/office/word/2018/wordml/cex";
    private const string Math =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrict =
        "http://purl.oclc.org/ooxml/officeDocument/math";

    [Fact]
    public void LinksCommentsThreadsPeopleRevisionsMovesAndPermissions()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}" xmlns:w14="{Word2010}">
              <w:body>
                <w:p w14:paraId="A0000001">
                  <w:commentRangeStart w:id="0"/>
                  <w:permStart w:id="7" w:edGrp="everyone" w:colFirst="1" w:colLast="2"/>
                  <w:ins w:id="1" w:author="Alice" w:date="2026-07-20T12:00:00Z"><w:r><w:t>new</w:t></w:r></w:ins>
                  <w:del w:id="2" w:author="Alice" w:date="2026-07-20T12:01:00Z"><w:r><w:delText>old</w:delText></w:r></w:del>
                  <w:permEnd w:id="7"/>
                  <w:commentRangeEnd w:id="0"/>
                  <w:r><w:commentReference w:id="0"/></w:r>
                  <w:r><w:commentReference w:id="1"/></w:r>
                </w:p>
                <w:p w14:paraId="A0000002"><w:moveToRangeStart w:id="10" w:name="moveA" w:author="Bob" w:date="2026-07-20T13:00:00Z"/></w:p>
                <w:p w14:paraId="A0000003"><w:moveTo w:id="3" w:author="Bob" w:date="2026-07-20T13:00:00Z"><w:r><w:t>moved</w:t></w:r></w:moveTo></w:p>
                <w:p w14:paraId="A0000004"><w:moveToRangeEnd w:id="10"/></w:p>
                <w:p w14:paraId="A0000005"><w:moveFromRangeStart w:id="11" w:name="moveA" w:author="Bob" w:date="2026-07-20T13:00:00Z"/></w:p>
                <w:p w14:paraId="A0000006"><w:moveFrom w:id="4" w:author="Bob" w:date="2026-07-20T13:00:00Z"><w:r><w:t>moved</w:t></w:r></w:moveFrom></w:p>
                <w:p w14:paraId="A0000007"><w:moveFromRangeEnd w:id="11"/></w:p>
                <w:p w14:paraId="A0000008"><w:pPr><w:pPrChange w:id="5" w:author="Alice" w:date="2026-07-20T14:00:00Z"><w:pPr/></w:pPrChange></w:pPr><w:r><w:t>end</w:t></w:r></w:p>
              </w:body>
            </w:document>
            """,
            commentsXml: $"""
            <w:comments xmlns:w="{Word}" xmlns:w14="{Word2010}">
              <w:comment w:id="0" w:author="Alice" w:initials="A" w:date="2026-07-20T12:00:00Z"><w:p w14:paraId="C0000001"><w:r><w:t>Root comment</w:t></w:r></w:p></w:comment>
              <w:comment w:id="1" w:author="Bob" w:initials="B" w:date="2026-07-20T12:02:00Z"><w:p w14:paraId="C0000002"><w:r><w:t>Reply comment</w:t></w:r></w:p></w:comment>
            </w:comments>
            """,
            commentsExtendedXml: $"""
            <w15:commentsEx xmlns:w15="{Word2012}">
              <w15:commentEx w15:paraId="C0000001" w15:done="1"/>
              <w15:commentEx w15:paraId="C0000002" w15:paraIdParent="C0000001" w15:done="0"/>
            </w15:commentsEx>
            """,
            commentsIdsXml: $"""
            <w16cid:commentsIds xmlns:w16cid="{Word2016Cid}">
              <w16cid:commentId w16cid:paraId="C0000001" w16cid:durableId="00000001"/>
              <w16cid:commentId w16cid:paraId="C0000002" w16cid:durableId="00000002"/>
            </w16cid:commentsIds>
            """,
            commentsExtensibleXml: $$"""
            <w16cex:commentsExtensible xmlns:w16cex="{{Word2018Cex}}" xmlns:w16="http://schemas.microsoft.com/office/word/2018/wordml" xmlns:or="urn:test-reactions">
              <w16cex:commentExtensible w16cex:durableId="00000001" w16cex:dateUtc="2026-07-20T12:00:00Z"><w16:extLst><w16:ext uri="{CE6994B0-6A32-4C9F-8C6B-6E91EDA988CE}"><or:reactions><or:reaction/></or:reactions></w16:ext></w16:extLst></w16cex:commentExtensible>
              <w16cex:commentExtensible w16cex:durableId="00000002" w16cex:dateUtc="2026-07-20T12:02:00Z"/>
            </w16cex:commentsExtensible>
            """,
            peopleXml: $"""
            <w15:people xmlns:w15="{Word2012}">
              <w15:person w15:author="Alice"><w15:presenceInfo w15:providerId="tenant" w15:userId="alice-id"/></w15:person>
              <w15:person w15:author="Bob"><w15:presenceInfo w15:providerId="tenant" w15:userId="bob-id"/></w15:person>
            </w15:people>
            """,
            settingsXml: $"""
            <w:settings xmlns:w="{Word}"><w:trackRevisions/><w:doNotTrackMoves w:val="0"/><w:doNotTrackFormatting/></w:settings>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReviewGraphBuilder().Build(package, semantic);

        Assert.Equal(2, graph.Comments.Count);
        Assert.Equal(2, graph.Anchors.Count);
        Assert.Equal(1, graph.ReplyCount);
        Assert.Equal(1, graph.ResolvedCommentCount);
        Assert.Equal(1, graph.ThreadCount);
        var root = graph.Comments.Single(comment => comment.OoxmlId == "0");
        var reply = graph.Comments.Single(comment => comment.OoxmlId == "1");
        Assert.True(root.HasReactions);
        Assert.Equal(1, root.ExtensionCount);
        Assert.Equal("00000001", root.DurableId);
        Assert.Equal(root.Id, reply.ParentCommentId);
        Assert.Equal(root.Id, reply.ThreadRootCommentId);
        Assert.Equal(1, reply.ThreadDepth);
        Assert.NotNull(root.PersonId);
        Assert.NotNull(reply.PersonId);
        Assert.Equal(WordCommentAnchorStatus.Complete, graph.Anchors[0].Status);
        Assert.Equal(WordCommentAnchorStatus.PointReference, graph.Anchors[1].Status);
        Assert.Contains("newold", graph.Anchors[0].Text, StringComparison.Ordinal);

        Assert.Equal(5, graph.Revisions.Count);
        Assert.Contains(graph.Revisions, revision =>
            revision.Kind == WordRevisionKind.Insertion && revision.Text == "new"
        );
        Assert.Contains(graph.Revisions, revision =>
            revision.Kind == WordRevisionKind.Deletion && revision.IsInDeletedContent
        );
        Assert.Contains(graph.Revisions, revision =>
            revision.Kind == WordRevisionKind.ParagraphPropertiesChange
        );
        Assert.All(graph.Revisions, revision => Assert.NotNull(revision.PersonId));

        var move = Assert.Single(graph.Moves);
        Assert.Equal(WordMovePairStatus.Complete, move.Status);
        var sourceRange = graph.MoveRanges.Single(range =>
            range.Kind == WordMoveRangeKind.Source
        );
        var destinationRange = graph.MoveRanges.Single(range =>
            range.Kind == WordMoveRangeKind.Destination
        );
        Assert.Single(sourceRange.RevisionIds);
        Assert.Single(destinationRange.RevisionIds);
        var permission = Assert.Single(graph.Permissions);
        Assert.Equal(WordReviewRangeStatus.Complete, permission.Status);
        Assert.Equal("everyone", permission.EditorGroup);
        Assert.Equal(1, permission.ColumnFirst);
        Assert.Equal(2, permission.ColumnLast);
        Assert.NotNull(graph.Settings);
        Assert.True(graph.Settings.TrackRevisions);
        Assert.False(graph.Settings.DoNotTrackMoves);
        Assert.True(graph.Settings.DoNotTrackFormatting);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void DiagnosesBrokenAnchorsThreadsMovesPermissionsAndDurableIds()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}" xmlns:w14="{Word2010}"><w:body><w:p w14:paraId="A0000001">
              <w:commentRangeStart w:id="9"/>
              <w:moveFromRangeStart w:id="4"/>
              <w:permStart w:id="5" w:colFirst="1"/>
              <w:ins w:author="Nobody"><w:r><w:t>x</w:t></w:r></w:ins>
            </w:p></w:body></w:document>
            """,
            commentsXml: $"""
            <w:comments xmlns:w="{Word}" xmlns:w14="{Word2010}">
              <w:comment w:id="0" w:author="Nobody"><w:p w14:paraId="C0000001"><w:r><w:t>a</w:t></w:r></w:p></w:comment>
              <w:comment w:id="0" w:author="Nobody"><w:p w14:paraId="C0000002"><w:r><w:t>b</w:t></w:r></w:p></w:comment>
            </w:comments>
            """,
            commentsExtendedXml: $"""
            <w15:commentsEx xmlns:w15="{Word2012}"><w15:commentEx w15:paraId="C0000001" w15:paraIdParent="DEADBEEF"/></w15:commentsEx>
            """,
            commentsIdsXml: $"""
            <w16cid:commentsIds xmlns:w16cid="{Word2016Cid}"><w16cid:commentId w16cid:paraId="C0000001" w16cid:durableId="00000000"/></w16cid:commentsIds>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReviewGraphBuilder().Build(package, semantic);

        var codes = graph.Issues.Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("COMMENT_ID_DUPLICATE", codes);
        Assert.Contains("COMMENT_DEFINITION_MISSING", codes);
        Assert.Contains("COMMENT_ANCHOR_INCOMPLETE", codes);
        Assert.Contains("COMMENT_THREAD_PARENT_MISSING", codes);
        Assert.Contains("COMMENT_DURABLE_ID_INVALID", codes);
        Assert.Contains("MOVE_RANGE_INCOMPLETE", codes);
        Assert.Contains("MOVE_NAME_MISSING", codes);
        Assert.Contains("PERMISSION_RANGE_INCOMPLETE", codes);
        Assert.Contains("PERMISSION_COLUMN_RANGE_INCOMPLETE", codes);
        Assert.Contains("REVISION_ID_MISSING", codes);
        Assert.True(graph.Issues.Count >= 10);
    }

    [Fact]
    public void DiagnosesInvalidPermissionAttributeValues()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}" xmlns:w14="{Word2010}"><w:body><w:p w14:paraId="A0000001">
              <w:permStart w:id="invalid" w:edGrp="invalid-group" w:colFirst="invalid" w:colLast="2" w:displacedByCustomXml="invalid"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="invalid"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);
        var permission = Assert.Single(graph.Permissions);
        var codes = graph.Issues.Select(issue => issue.Code).ToArray();

        Assert.Equal(WordReviewRangeStatus.Complete, permission.Status);
        Assert.Null(permission.ColumnFirst);
        Assert.Equal(2, permission.ColumnLast);
        Assert.Contains("PERMISSION_RANGE_ID_INVALID", codes);
        Assert.Contains("PERMISSION_COLUMN_RANGE_INVALID", codes);
        Assert.Contains("PERMISSION_EDITOR_GROUP_INVALID", codes);
        Assert.Contains("PERMISSION_DISPLACEMENT_INVALID", codes);
    }

    [Fact]
    public void DiagnosesReversedPermissionColumnBounds()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}" xmlns:w14="{Word2010}"><w:body><w:p w14:paraId="A0000001">
              <w:permStart w:id="7" w:edGrp="everyone" w:colFirst="2" w:colLast="1"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="7"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);
        var permission = Assert.Single(graph.Permissions);

        Assert.Equal(WordReviewRangeStatus.Complete, permission.Status);
        Assert.Equal(2, permission.ColumnFirst);
        Assert.Equal(1, permission.ColumnLast);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_COLUMN_RANGE_INVALID"
        );
    }

    [Fact]
    public void DiagnosesNegativePermissionColumnBounds()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}" xmlns:w14="{Word2010}"><w:body><w:p w14:paraId="A0000001">
              <w:permStart w:id="7" w:edGrp="everyone" w:colFirst="-1" w:colLast="2"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="7"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);
        var permission = Assert.Single(graph.Permissions);

        Assert.Equal(WordReviewRangeStatus.Complete, permission.Status);
        Assert.Equal(-1, permission.ColumnFirst);
        Assert.Equal(2, permission.ColumnLast);
        Assert.Contains(
            graph.Issues,
            issue =>
                issue.Code == "PERMISSION_COLUMN_RANGE_INVALID"
                && issue.Message.Contains("non-negative", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void DiagnosesMixedPermissionAttributeNamespaces()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}" xmlns:ws="{WordStrict}" xmlns:w14="{Word2010}"><w:body><w:p w14:paraId="A0000001">
              <w:permStart ws:id="7" ws:edGrp="everyone" ws:colFirst="0" ws:colLast="2" ws:displacedByCustomXml="next"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd ws:id="7"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID"
        );
        Assert.DoesNotContain(graph.Permissions, permission =>
            permission.Status == WordReviewRangeStatus.Complete
        );
    }

    [Fact]
    public void DiagnosesStrictMarkersWithTransitionalPermissionAttributes()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <ws:document xmlns:w="{Word}" xmlns:ws="{WordStrict}"><ws:body><ws:p>
              <ws:permStart w:id="7" w:ed="user@example.test"/>
              <ws:r><ws:t>x</ws:t></ws:r>
              <ws:permEnd w:id="7"/>
            </ws:p></ws:body></ws:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID"
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagnosesPermissionMarkersFromTheOtherStoryNamespace(bool strictStory)
    {
        var storyPrefix = strictStory ? "ws" : "w";
        var markerPrefix = strictStory ? "w" : "ws";
        using var bytes = BuildPackage(
            documentXml: $"""
            <{storyPrefix}:document xmlns:w="{Word}" xmlns:ws="{WordStrict}"><{storyPrefix}:body><{storyPrefix}:p>
              <{markerPrefix}:permStart {markerPrefix}:id="7" {markerPrefix}:edGrp="everyone"/>
              <{storyPrefix}:r><{storyPrefix}:t>x</{storyPrefix}:t></{storyPrefix}:r>
              <{markerPrefix}:permEnd {markerPrefix}:id="7"/>
            </{storyPrefix}:p></{storyPrefix}:body></{storyPrefix}:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Equal(
            WordReviewRangeStatus.Complete,
            Assert.Single(graph.Permissions).Status
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_NAMESPACE_INVALID"
        );
    }

    [Fact]
    public void DiagnosesStartOnlyPermissionAttributesOnEndMarkers()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p>
              <w:permStart w:id="7"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="7" w:ed="user@example.test" w:edGrp="everyone" w:colFirst="0" w:colLast="2"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Equal(
            WordReviewRangeStatus.Complete,
            Assert.Single(graph.Permissions).Status
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_ATTRIBUTE_PLACEMENT_INVALID"
        );
    }

    [Fact]
    public void DiagnosesUnknownWordPermissionAttributes()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p>
              <w:permStart w:id="7" w:bogus="x"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="7"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_ATTRIBUTE_UNKNOWN"
        );
    }

    [Fact]
    public void DiagnosesUnqualifiedUnknownPermissionAttributes()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p>
              <w:permStart w:id="7" bogus="x"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="7"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_ATTRIBUTE_UNKNOWN"
        );
    }

    [Fact]
    public void DiagnosesUnqualifiedKnownPermissionAttributes()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p>
              <w:permStart id="7" edGrp="everyone"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd id="7"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID"
        );
        Assert.DoesNotContain(graph.Permissions, permission =>
            permission.Status == WordReviewRangeStatus.Complete
        );
    }

    [Fact]
    public void DiagnosesForeignNamespacePermissionAttributes()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}" xmlns:x="urn:test"><w:body><w:p>
              <w:permStart w:id="7" x:bogus="value"/>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="7"/>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID"
        );
    }

    [Theory]
    [InlineData("<w:r/>", "")]
    [InlineData("", "text")]
    [InlineData("<![CDATA[]]>", "")]
    public void DiagnosesNonemptyPermissionMarkers(
        string startContent,
        string endContent
    )
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p>
              <w:permStart w:id="7">{startContent}</w:permStart>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="7">{endContent}</w:permEnd>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_CONTENT_INVALID"
        );
    }

    [Fact]
    public void PermissionMarkerCommentsAndProcessingInstructionsRemainNonContent()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p>
              <w:permStart w:id="7"><!-- preserve --></w:permStart>
              <w:r><w:t>x</w:t></w:r>
              <w:permEnd w:id="7"><?wordtoolkit preserve?></w:permEnd>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_CONTENT_INVALID"
        );
        Assert.Equal(
            WordReviewRangeStatus.Complete,
            Assert.Single(graph.Permissions).Status
        );
    }

    [Fact]
    public void DiagnosesPermissionMarkersInInvalidParents()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p><w:r><w:t>
              <w:permStart w:id="7"/><w:permEnd w:id="7"/>
            </w:t></w:r></w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_PARENT_INVALID"
        );
        Assert.Equal(
            WordReviewRangeStatus.Complete,
            Assert.Single(graph.Permissions).Status
        );
    }

    [Theory]
    [InlineData(Math, "bogus")]
    [InlineData("urn:test", "conflictIns")]
    public void PermissionParentAllowListRequiresNamespaceAndLocalName(
        string parentNamespace,
        string parentLocalName
    )
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}" xmlns:x="{parentNamespace}"><w:body><w:p>
              <x:{parentLocalName}><w:permStart w:id="7"/><w:permEnd w:id="7"/></x:{parentLocalName}>
            </w:p></w:body></w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_PARENT_INVALID"
        );
    }

    [Theory]
    [InlineData(Word, "w", WordStrict, "ws")]
    [InlineData(WordStrict, "ws", Word, "w")]
    public void PermissionWordParentsMustMatchTheStoryConformanceNamespace(
        string storyNamespace,
        string storyPrefix,
        string parentNamespace,
        string parentPrefix
    )
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <{storyPrefix}:document xmlns:{storyPrefix}="{storyNamespace}" xmlns:{parentPrefix}="{parentNamespace}">
              <{storyPrefix}:body><{parentPrefix}:p>
                <{storyPrefix}:permStart {storyPrefix}:id="7"/>
                <{storyPrefix}:permEnd {storyPrefix}:id="7"/>
              </{parentPrefix}:p></{storyPrefix}:body>
            </{storyPrefix}:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_NAMESPACE_INVALID"
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_PARENT_INVALID"
        );
        Assert.Equal(
            WordReviewRangeStatus.Complete,
            Assert.Single(graph.Permissions).Status
        );
    }

    [Theory]
    [InlineData(Word, "w", MathStrict)]
    [InlineData(WordStrict, "ws", Math)]
    public void PermissionMathParentsMustMatchTheStoryConformanceNamespace(
        string storyNamespace,
        string storyPrefix,
        string mathNamespace
    )
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <{storyPrefix}:document xmlns:{storyPrefix}="{storyNamespace}" xmlns:m="{mathNamespace}">
              <{storyPrefix}:body><{storyPrefix}:p><m:oMath>
                <{storyPrefix}:permStart {storyPrefix}:id="7"/>
                <{storyPrefix}:permEnd {storyPrefix}:id="7"/>
              </m:oMath></{storyPrefix}:p></{storyPrefix}:body>
            </{storyPrefix}:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_PARENT_INVALID"
        );
    }

    [Theory]
    [InlineData(Word, "w", Math)]
    [InlineData(WordStrict, "ws", MathStrict)]
    public void PermissionMathParentsAcceptTheStoryConformanceNamespace(
        string storyNamespace,
        string storyPrefix,
        string mathNamespace
    )
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <{storyPrefix}:document xmlns:{storyPrefix}="{storyNamespace}" xmlns:m="{mathNamespace}">
              <{storyPrefix}:body><{storyPrefix}:p><m:oMath>
                <{storyPrefix}:permStart {storyPrefix}:id="7"/>
                <{storyPrefix}:permEnd {storyPrefix}:id="7"/>
              </m:oMath></{storyPrefix}:p></{storyPrefix}:body>
            </{storyPrefix}:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);
        var document = new WordSemanticProjector().Project(package);
        var graph = new WordReviewGraphBuilder().Build(package, document);

        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Code == "PERMISSION_MARKER_PARENT_INVALID"
        );
        Assert.Equal(
            WordReviewRangeStatus.Complete,
            Assert.Single(graph.Permissions).Status
        );
    }

    [Fact]
    public void EnforcesReviewLimitsAndCapsIssueFlood()
    {
        using var bytes = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p><w:ins w:id="1"><w:r><w:t>a</w:t></w:r></w:ins><w:ins w:id="2"><w:r><w:t>b</w:t></w:r></w:ins></w:p></w:body></w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);
        Assert.Throws<WordReviewLimitException>(() =>
            new WordReviewGraphBuilder(
                new WordReviewGraphOptions { MaxRevisions = 1 }
            ).Build(package, semantic)
        );

        using var broken = BuildPackage(
            documentXml: $"""
            <w:document xmlns:w="{Word}"><w:body><w:p><w:commentRangeStart/><w:commentRangeStart/><w:permStart/></w:p></w:body></w:document>
            """
        );
        var (brokenPackage, brokenSemantic) = ReadSnapshots(broken);
        var graph = new WordReviewGraphBuilder(
            new WordReviewGraphOptions { MaxIssues = 1 }
        ).Build(brokenPackage, brokenSemantic);
        Assert.Single(graph.Issues);
        Assert.True(graph.IssuesTruncated);
    }

    [Fact]
    public void ParsesEveryBundledDocumentContainingReviewMarkup()
    {
        var root = FindRepositoryRoot();
        var paths = new[]
        {
            "examples/advanced/WordToolkit-advanced-torture-test.docx",
            "tests/upstream/fixtures/mammoth_comments.docx",
            "tests/upstream/fixtures/pandoc_comments.docx",
            "tests/upstream/fixtures/pandoc_track_move.docx",
            "tests/upstream/fixtures/poi_tracked_changes_delins.docx",
            "tests/upstream/fixtures/real_hyperlinks_footnotes.docx",
            "tests/upstream/fixtures/real_tracked_changes.docx",
        }.Select(path => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))
            .ToArray();
        var reader = new OpcPackageReader();
        var comments = 0;
        var revisions = 0;
        foreach (var path in paths)
        {
            Assert.True(File.Exists(path), path);
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var graph = new WordReviewGraphBuilder().Build(package, semantic);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            comments += graph.Comments.Count;
            revisions += graph.Revisions.Count;
        }
        Assert.True(comments >= 10, $"Expected at least 10 comments, found {comments}.");
        Assert.True(revisions >= 100, $"Expected at least 100 revisions, found {revisions}.");
    }

    private static (
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic
    ) ReadSnapshots(Stream bytes)
    {
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        return (package, semantic);
    }

    private static MemoryStream BuildPackage(
        string documentXml,
        string? commentsXml = null,
        string? commentsExtendedXml = null,
        string? commentsIdsXml = null,
        string? commentsExtensibleXml = null,
        string? peopleXml = null,
        string? settingsXml = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var overrides = new List<string>
            {
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>",
            };
            var relationships = new List<string>();
            AddOptionalPart(
                archive,
                commentsXml,
                "word/comments.xml",
                "/word/comments.xml",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml",
                "rIdComments",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
                overrides,
                relationships
            );
            AddOptionalPart(
                archive,
                commentsExtendedXml,
                "word/commentsExtended.xml",
                "/word/commentsExtended.xml",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml",
                "rIdCommentsEx",
                "http://schemas.microsoft.com/office/2011/relationships/commentsExtended",
                overrides,
                relationships
            );
            AddOptionalPart(
                archive,
                commentsIdsXml,
                "word/commentsIds.xml",
                "/word/commentsIds.xml",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsIds+xml",
                "rIdCommentsIds",
                "http://schemas.microsoft.com/office/2016/09/relationships/commentsIds",
                overrides,
                relationships
            );
            AddOptionalPart(
                archive,
                commentsExtensibleXml,
                "word/commentsExtensible.xml",
                "/word/commentsExtensible.xml",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtensible+xml",
                "rIdCommentsExtensible",
                "http://schemas.microsoft.com/office/2018/08/relationships/commentsExtensible",
                overrides,
                relationships
            );
            AddOptionalPart(
                archive,
                peopleXml,
                "word/people.xml",
                "/word/people.xml",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml",
                "rIdPeople",
                "http://schemas.microsoft.com/office/2011/relationships/people",
                overrides,
                relationships
            );
            AddOptionalPart(
                archive,
                settingsXml,
                "word/settings.xml",
                "/word/settings.xml",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml",
                "rIdSettings",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings",
                overrides,
                relationships
            );
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  {string.Join("", overrides)}
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """
            );
            WriteEntry(archive, "word/document.xml", documentXml);
            if (relationships.Count > 0)
            {
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{string.Join("", relationships)}</Relationships>"
                );
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void AddOptionalPart(
        ZipArchive archive,
        string? content,
        string entryName,
        string partName,
        string contentType,
        string relationshipId,
        string relationshipType,
        ICollection<string> overrides,
        ICollection<string> relationships
    )
    {
        if (content is null) return;
        WriteEntry(archive, entryName, content);
        overrides.Add(
            $"<Override PartName=\"{partName}\" ContentType=\"{contentType}\"/>"
        );
        relationships.Add(
            $"<Relationship Id=\"{relationshipId}\" Type=\"{relationshipType}\" Target=\"{Path.GetFileName(entryName)}\"/>"
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
