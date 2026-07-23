using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordFigureCaptionGraphTests
{
    [Fact]
    public void ProjectsSourceLinkedPictureCaptionAccessibilityAndResource()
    {
        using var bytes = BuildPackage(
            PictureParagraph("rIdImage", 7, "Quarterly chart", "Revenue by quarter")
                + CaptionParagraph("Figure", "1", " — Secret caption")
        );
        var package = new OpcPackageReader().Read(bytes);

        var first = new WordFigureCaptionGraphBuilder().Build(package);
        var second = new WordFigureCaptionGraphBuilder().Build(package);

        var figure = Assert.Single(first.Figures);
        Assert.Equal(figure.Id, Assert.Single(second.Figures).Id);
        Assert.Equal(WordFigureObjectKind.Picture, figure.ObjectKind);
        Assert.Equal(WordFigureRepresentationKind.DrawingInline, Assert.Single(figure.Representations).Kind);
        Assert.Equal(914400, figure.Representations[0].Placement.WidthEmu);
        Assert.Equal(457200, figure.Representations[0].Placement.HeightEmu);
        Assert.Equal((ulong)7, figure.Representations[0].NonVisualDrawingId);
        Assert.Equal("Quarterly chart", figure.Representations[0].Accessibility.Title);
        Assert.Equal("Revenue by quarter", figure.Representations[0].Accessibility.Description);
        Assert.True(figure.Representations[0].Accessibility.HasAlternativeText);
        var resource = Assert.Single(figure.Resources);
        Assert.Equal(WordFigureResourceRole.ImageEmbed, resource.Role);
        Assert.True(resource.IsResolved);
        Assert.False(resource.IsExternal);
        Assert.Equal("/word/media/image1.png", resource.TargetPartUri);
        Assert.Equal("image/png", resource.TargetContentType);
        Assert.NotNull(resource.TargetSha256);

        var caption = Assert.Single(first.Captions);
        Assert.Equal(WordCaptionKind.Figure, caption.Kind);
        Assert.Equal("Figure", caption.PrimaryLabel);
        Assert.True(caption.HasCaptionStyleEvidence);
        Assert.Equal("1 — Secret caption", caption.Text);
        Assert.Equal("1", caption.SequenceResultText);
        Assert.Single(caption.SequenceFieldIds);
        var association = Assert.Single(first.Associations);
        Assert.Equal(WordFigureCaptionAssociationStatus.Selected, association.Status);
        Assert.Equal(WordFigureCaptionDirection.CaptionAfterFigure, association.Direction);
        Assert.Equal(1, association.ParagraphDistance);
        Assert.Equal(caption.Id, figure.SelectedCaptionId);
        Assert.Equal(figure.Id, caption.SelectedFigureId);
        Assert.DoesNotContain(first.Issues, issue =>
            issue.Code is "FIGURE_ALT_TEXT_MISSING" or "FIGURE_CAPTION_NOT_RESOLVED"
        );

        var semantic = new WordSemanticProjector().Project(package);
        var dependencies = new WordDependencyGraphBuilder().Build(package, semantic);
        Assert.True(dependencies.Coverage.FiguresAndCaptions);
        Assert.Single(dependencies.Nodes, node => node.Kind == WordDependencyNodeKind.Figure);
        Assert.Single(dependencies.Nodes, node =>
            node.Kind == WordDependencyNodeKind.FigureRepresentation
        );
        Assert.Single(dependencies.Nodes, node =>
            node.Kind == WordDependencyNodeKind.FigureResource
        );
        Assert.Single(dependencies.Nodes, node => node.Kind == WordDependencyNodeKind.Caption);
        Assert.Single(dependencies.Edges, edge =>
            edge.Kind == WordDependencyEdgeKind.FigureCaptionAssociation
            && edge.IsResolved
        );
        Assert.Single(dependencies.Edges, edge =>
            edge.Kind == WordDependencyEdgeKind.FigureResourceTargetsPart
            && edge.IsResolved
        );
    }

    [Fact]
    public void CollapsesDrawingChoiceAndVmlFallbackIntoOneLogicalFigure()
    {
        var body = $$"""
            <w:p>
              <w:r>
                <mc:AlternateContent>
                  <mc:Choice Requires="w14">
                    {{PictureDrawing("rIdImage", 11, "Modern", "Modern description")}}
                  </mc:Choice>
                  <mc:Fallback>
                    <w:pict>
                      <v:shape id="legacy" alt="Legacy description" style="width:72pt;height:36pt">
                        <v:imagedata o:relid="rIdImage"/>
                      </v:shape>
                    </w:pict>
                  </mc:Fallback>
                </mc:AlternateContent>
              </w:r>
            </w:p>
            {{CaptionParagraph("Figure", "2", " fallback")}}
            """;
        using var bytes = BuildPackage(body);
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordFigureCaptionGraphBuilder().Build(package);

        var figure = Assert.Single(graph.Figures);
        Assert.NotNull(figure.AlternateContentGroupId);
        Assert.Equal(2, figure.Representations.Count);
        Assert.Equal(
            WordFigureRepresentationSelectionBasis.AlternateContentChoicePresentNotEvaluated,
            figure.RepresentationSelectionBasis
        );
        Assert.Null(figure.PrimaryRepresentationId);
        Assert.Equal(WordFigureObjectKind.Unknown, figure.ObjectKind);
        Assert.Equal("Choice", figure.Representations[0].AlternateContentBranch);
        Assert.Equal("Fallback", figure.Representations[1].AlternateContentBranch);
        Assert.Equal(WordFigureRepresentationKind.DrawingInline, figure.Representations[0].Kind);
        Assert.Equal(WordFigureRepresentationKind.VmlPicture, figure.Representations[1].Kind);
        Assert.Equal(2, figure.Resources.Count);
        Assert.Equal(figure.Id, Assert.Single(graph.Captions).SelectedFigureId);

        var dependencies = new WordDependencyGraphBuilder().Build(
            package,
            new WordSemanticProjector().Project(package)
        );
        Assert.Equal(
            2,
            dependencies.Edges.Count(item =>
                item.Kind == WordDependencyEdgeKind.DefinesFigure
            )
        );
    }

    [Fact]
    public void RecordsVmlRelationshipAndDirectSourceIndependently()
    {
        const string body = """
            <w:p><w:r><w:pict>
              <v:shape id="legacy" alt="Legacy">
                <v:imagedata o:relid="rIdImage" src="https://example.invalid/direct.png"/>
              </v:shape>
            </w:pict></w:r></w:p>
            """;
        using var bytes = BuildPackage(body);

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var resources = Assert.Single(graph.Figures).Resources;
        Assert.Equal(2, resources.Count);
        Assert.Contains(resources, item =>
            item.RelationshipId == "rIdImage" && item.IsResolved && !item.IsExternal
        );
        Assert.Contains(resources, item =>
            item.RelationshipId is null
            && item.Target == "https://example.invalid/direct.png"
            && item.IsExternal
            && !item.IsResolved
        );
        Assert.Contains(graph.Issues, item =>
            item.Code == "FIGURE_VML_DIRECT_SOURCE_DECLARED"
        );
    }

    [Fact]
    public void IgnoresForeignNamespaceElementsThatImitateDrawingMl()
    {
        const string body = """
            <w:p><w:r><w:drawing xmlns:evil="urn:evil">
              <evil:inline>
                <evil:docPr id="999" title="forged" descr="forged"/>
                <evil:blip r:link="rIdImage" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
              </evil:inline>
            </w:drawing></w:r></w:p>
            """;
        using var bytes = BuildPackage(body);

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var representation = Assert.Single(Assert.Single(graph.Figures).Representations);
        Assert.Equal(WordFigureRepresentationKind.Unknown, representation.Kind);
        Assert.Equal(WordFigureObjectKind.Unknown, representation.ObjectKind);
        Assert.Null(representation.NonVisualDrawingId);
        Assert.False(representation.Accessibility.HasAlternativeText);
        Assert.Empty(representation.Resources);
        Assert.Contains("{urn:evil}inline", representation.UnmodeledPayloadElements);
        Assert.Contains("{urn:evil}docPr", representation.UnmodeledPayloadElements);
        Assert.Contains("{urn:evil}blip", representation.UnmodeledPayloadElements);
    }

    [Fact]
    public void BoundsUnmodeledQNameInventoryBeforeSortingOrRetainingAttackerInput()
    {
        var foreign = string.Concat(
            Enumerable.Range(0, 1_000).Select(index => $"<e:x{index}/>")
        );
        var body = $"""
            <w:p><w:r><w:drawing xmlns:e="urn:many">{foreign}</w:drawing></w:r></w:p>
            """;
        using var bytes = BuildPackage(body);

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var names = Assert.Single(Assert.Single(graph.Figures).Representations)
            .UnmodeledPayloadElements;
        Assert.Equal(64, names.Count);
        Assert.Equal("{urn:many}x0", names[0]);
        Assert.Equal("{urn:many}x63", names[^1]);
    }

    [Fact]
    public void KeepsEqualNearbyFiguresAmbiguousInsteadOfInventingALink()
    {
        var body = $$"""
            <w:p>
              <w:r>{{PictureDrawing("rIdImage", 1, null, null)}}</w:r>
              <w:r>{{PictureDrawing("rIdImage", 2, null, null)}}</w:r>
            </w:p>
            {{CaptionParagraph("Figure", "3", " ambiguous")}}
            """;
        using var bytes = BuildPackage(body);
        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        Assert.Equal(2, graph.Figures.Count);
        var caption = Assert.Single(graph.Captions);
        Assert.Null(caption.SelectedFigureId);
        Assert.All(graph.Figures, figure => Assert.Null(figure.SelectedCaptionId));
        Assert.Equal(2, graph.Associations.Count);
        Assert.All(graph.Associations, association =>
            Assert.Equal(WordFigureCaptionAssociationStatus.Ambiguous, association.Status)
        );
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "FIGURE_CAPTION_ASSOCIATION_AMBIGUOUS"
        );
    }

    [Fact]
    public void MarksOnlyTopScoringTiesAmbiguousAndKeepsWeakerEvidenceCandidate()
    {
        var body = $$"""
            <w:p>
              <w:r>{{PictureDrawing("rIdImage", 1, null, null)}}</w:r>
              <w:r>{{PictureDrawing("rIdImage", 2, null, null)}}</w:r>
            </w:p>
            {{CaptionParagraph("Figure", "3", " ambiguous")}}
            {{PictureParagraph("rIdImage", 3, null, null)}}
            """;
        using var bytes = BuildPackage(body);

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        Assert.Equal(3, graph.Associations.Count);
        Assert.Equal(
            2,
            graph.Associations.Count(item =>
                item.Status == WordFigureCaptionAssociationStatus.Ambiguous
            )
        );
        var weaker = Assert.Single(graph.Associations, item =>
            item.Status == WordFigureCaptionAssociationStatus.Candidate
        );
        Assert.True(weaker.Score < graph.Associations.Max(item => item.Score));
        Assert.Null(Assert.Single(graph.Captions).SelectedFigureId);
    }

    [Fact]
    public void RecordsButNeverResolvesOrFollowsExternalImageLinks()
    {
        using var bytes = BuildPackage(
            PictureParagraph("rIdExternal", 1, "Linked", "External image"),
            externalImage: true
        );

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var resource = Assert.Single(Assert.Single(graph.Figures).Resources);
        Assert.Equal(WordFigureResourceRole.ImageLink, resource.Role);
        Assert.Equal(OpcRelationshipTargetMode.External, resource.TargetMode);
        Assert.Equal("https://example.invalid/never-fetch.png", resource.Target);
        Assert.True(resource.IsExternal);
        Assert.False(resource.IsResolved);
        Assert.Null(resource.TargetPartUri);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "FIGURE_EXTERNAL_RESOURCE_DECLARED"
        );
    }

    [Fact]
    public void ReportsMissingRelationshipsDuplicateDrawingIdsAndInvalidExtents()
    {
        var invalidExtent = PictureDrawing("rIdMissing", 7, "Title", "Description")
            .Replace("cx=\"914400\"", "cx=\"broken\"", StringComparison.Ordinal);
        var body = $"<w:p><w:r>{invalidExtent}</w:r></w:p>"
            + PictureParagraph("rIdImage", 7, "Other", "Other description");
        using var bytes = BuildPackage(body);

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var missing = Assert.Single(graph.Figures[0].Resources);
        Assert.Equal("rIdMissing", missing.RelationshipId);
        Assert.False(missing.IsResolved);
        Assert.Null(missing.TargetPartUri);
        Assert.Contains(graph.Issues, item => item.Code == "FIGURE_RELATIONSHIP_MISSING");
        Assert.Contains(graph.Issues, item => item.Code == "FIGURE_EXTENT_INVALID");
        Assert.Equal(
            2,
            graph.Issues.Count(item => item.Code == "FIGURE_DOC_PROPERTIES_ID_DUPLICATE")
        );
    }

    [Fact]
    public void BoundsSensitiveAccessibilityMetadataWithoutLosingDeclaredLength()
    {
        var title = new string('x', 9_000);
        using var bytes = BuildPackage(
            PictureParagraph("rIdImage", 1, title, "bounded description")
        );

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var accessibility = Assert.Single(graph.Figures).Representations[0].Accessibility;
        Assert.Equal(9_000, accessibility.TitleCharacterCount);
        Assert.Equal(8_192, accessibility.Title!.Length);
        Assert.True(accessibility.TitleTruncated);
    }

    [Fact]
    public void StreamsCaptionTextIntoBoundedStorageAndCountsFullLength()
    {
        var suffix = new string('s', 20_000);
        using var bytes = BuildPackage(
            PictureParagraph("rIdImage", 1, "Title", "Description")
                + CaptionParagraph("Figure", "1", suffix)
        );
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordFigureCaptionGraphBuilder(
            new WordFigureCaptionGraphOptions
            {
                MaxTextCharacters = 64,
                MaxMetadataCharacters = 100_000,
            }
        ).Build(package);

        var caption = Assert.Single(graph.Captions);
        Assert.Equal(20_001, caption.TextCharacterCount);
        Assert.Equal(64, caption.Text!.Length);
        Assert.True(caption.TextTruncated);
        Assert.Throws<WordFigureLimitException>(() =>
            new WordFigureCaptionGraphBuilder(
                new WordFigureCaptionGraphOptions
                {
                    MaxTextCharacters = 64,
                    MaxMetadataCharacters = 100,
                }
            ).Build(package)
        );
    }

    [Fact]
    public void ProjectsStrictWordprocessingAndDrawingNamespaces()
    {
        using var bytes = BuildPackage(
            PictureParagraph("rIdImage", 5, "Strict title", "Strict description", strict: true)
                + CaptionParagraph("Figure", "4", " strict", strict: true),
            strict: true
        );

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var figure = Assert.Single(graph.Figures);
        Assert.Equal(WordFigureObjectKind.Picture, figure.ObjectKind);
        Assert.True(Assert.Single(figure.Resources).IsResolved);
        Assert.Equal(
            WordFigureCaptionAssociationStatus.Selected,
            Assert.Single(graph.Associations).Status
        );
    }

    [Fact]
    public void TreatsCaptionStyleWithoutSequenceAsWeakerDeclaredEvidence()
    {
        using var bytes = BuildPackage(
            PictureParagraph("rIdImage", 1, "Title", "Description")
                + "<w:p><w:pPr><w:pStyle w:val=\"Caption\"/></w:pPr><w:r><w:t>Unnumbered caption</w:t></w:r></w:p>"
        );

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var caption = Assert.Single(graph.Captions);
        Assert.Empty(caption.SequenceFieldIds);
        var association = Assert.Single(graph.Associations);
        Assert.Equal(WordFigureCaptionAssociationStatus.Selected, association.Status);
        Assert.Equal(WordFigureCaptionConfidence.Moderate, association.Confidence);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "CAPTION_SEQUENCE_FIELD_MISSING"
        );
    }

    [Fact]
    public void PreservesButDoesNotSelectDeletedCaptionEvidence()
    {
        var deletedCaption = """
            <w:p><w:pPr><w:pStyle w:val="Caption"/></w:pPr>
              <w:del w:author="Reviewer" w:date="2026-01-01T00:00:00Z">
                <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                <w:r><w:instrText xml:space="preserve"> SEQ Figure \* ARABIC </w:instrText></w:r>
                <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                <w:r><w:delText>9</w:delText></w:r>
                <w:r><w:fldChar w:fldCharType="end"/></w:r>
              </w:del>
            </w:p>
            """;
        using var bytes = BuildPackage(
            PictureParagraph("rIdImage", 1, "Title", "Description")
                + deletedCaption
        );

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var caption = Assert.Single(graph.Captions);
        Assert.True(caption.IsInDeletedContent);
        Assert.Empty(graph.Associations);
        Assert.Null(Assert.Single(graph.Figures).SelectedCaptionId);
    }

    [Fact]
    public void UsesLiveSequenceWhenSameCaptionParagraphAlsoContainsDeletedHistory()
    {
        var mixedCaption = """
            <w:p><w:pPr><w:pStyle w:val="Caption"/></w:pPr>
              <w:del w:author="Reviewer" w:date="2026-01-01T00:00:00Z">
                <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                <w:r><w:instrText> SEQ Table </w:instrText></w:r>
                <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                <w:r><w:delText>8</w:delText></w:r>
                <w:r><w:fldChar w:fldCharType="end"/></w:r>
              </w:del>
              <w:r><w:fldChar w:fldCharType="begin"/></w:r>
              <w:r><w:instrText> SEQ Figure </w:instrText></w:r>
              <w:r><w:fldChar w:fldCharType="separate"/></w:r>
              <w:r><w:t>3</w:t></w:r>
              <w:r><w:fldChar w:fldCharType="end"/></w:r>
              <w:r><w:t> live</w:t></w:r>
            </w:p>
            """;
        using var bytes = BuildPackage(
            PictureParagraph("rIdImage", 1, "Title", "Description") + mixedCaption
        );

        var graph = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var caption = Assert.Single(graph.Captions);
        Assert.False(caption.IsInDeletedContent);
        Assert.Equal("Figure", caption.PrimaryLabel);
        Assert.Single(caption.SequenceFieldIds);
        Assert.Equal("3", caption.SequenceResultText);
        Assert.Equal(
            WordFigureCaptionAssociationStatus.Selected,
            Assert.Single(graph.Associations).Status
        );
    }

    [Fact]
    public void StableIdsChangeWhenPackageFingerprintChanges()
    {
        using var firstBytes = BuildPackage(
            PictureParagraph("rIdImage", 1, "Title", "Description")
                + CaptionParagraph("Figure", "1", " first")
        );
        using var secondBytes = BuildPackage(
            PictureParagraph("rIdImage", 1, "Title", "Description")
                + CaptionParagraph("Figure", "1", " changed")
        );

        var first = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(firstBytes)
        );
        var second = new WordFigureCaptionGraphBuilder().Build(
            new OpcPackageReader().Read(secondBytes)
        );

        Assert.NotEqual(first.PackageFingerprint, second.PackageFingerprint);
        Assert.NotEqual(Assert.Single(first.Figures).Id, Assert.Single(second.Figures).Id);
        Assert.NotEqual(Assert.Single(first.Captions).Id, Assert.Single(second.Captions).Id);
    }

    [Fact]
    public void EnforcesRepresentationAndMetadataSafetyLimits()
    {
        var body = PictureParagraph("rIdImage", 1, null, null)
            + PictureParagraph("rIdImage", 2, null, null);
        using var representationBytes = BuildPackage(body);
        var package = new OpcPackageReader().Read(representationBytes);
        Assert.Throws<WordFigureLimitException>(() =>
            new WordFigureCaptionGraphBuilder(
                new WordFigureCaptionGraphOptions { MaxRepresentations = 1 }
            ).Build(package)
        );

        using var metadataBytes = BuildPackage(
            PictureParagraph("rIdImage", 1, new string('x', 40), null)
        );
        var metadataPackage = new OpcPackageReader().Read(metadataBytes);
        Assert.Throws<WordFigureLimitException>(() =>
            new WordFigureCaptionGraphBuilder(
                new WordFigureCaptionGraphOptions { MaxMetadataCharacters = 8 }
            ).Build(metadataPackage)
        );
    }

    [Fact]
    public void RejectsOversizedRelationshipIdentifiersBeforeRetainingThem()
    {
        var oversizedId = new string('r', 4_097);
        using var bytes = BuildPackage(
            PictureParagraph(oversizedId, 1, "Title", "Description")
        );
        var package = new OpcPackageReader().Read(bytes);

        var exception = Assert.Throws<WordFigureLimitException>(() =>
            new WordFigureCaptionGraphBuilder().Build(package)
        );

        Assert.Contains("relationship identifier", exception.Message);
        Assert.DoesNotContain(oversizedId, exception.Message);
    }

    [Fact]
    public void RejectsGraphsFromAnotherPackageSnapshot()
    {
        using var firstBytes = BuildPackage(PictureParagraph("rIdImage", 1, null, null));
        using var secondBytes = BuildPackage(PictureParagraph("rIdImage", 2, null, null));
        var first = new OpcPackageReader().Read(firstBytes);
        var second = new OpcPackageReader().Read(secondBytes);
        var semantic = new WordSemanticProjector().Project(first);
        var references = new WordReferenceGraphBuilder().Build(first, semantic);
        var styles = new WordStyleGraphBuilder().Build(first, semantic);

        Assert.Throws<WordFigureProjectionException>(() =>
            new WordFigureCaptionGraphBuilder().Build(
                second,
                semantic,
                references,
                styles
            )
        );
    }

    private static string PictureParagraph(
        string relationshipId,
        int drawingId,
        string? title,
        string? description,
        bool strict = false
    ) => $"<w:p><w:r>{PictureDrawing(relationshipId, drawingId, title, description, strict)}</w:r></w:p>";

    private static string PictureDrawing(
        string relationshipId,
        int drawingId,
        string? title,
        string? description,
        bool strict = false
    )
    {
        var wp = strict
            ? "http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing"
            : "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        var a = strict
            ? "http://purl.oclc.org/ooxml/drawingml/main"
            : "http://schemas.openxmlformats.org/drawingml/2006/main";
        var pic = strict
            ? "http://purl.oclc.org/ooxml/drawingml/picture"
            : "http://schemas.openxmlformats.org/drawingml/2006/picture";
        var r = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var linkAttribute = relationshipId == "rIdExternal" ? "link" : "embed";
        var titleAttribute = title is null ? string.Empty : $" title=\"{title}\"";
        var descriptionAttribute = description is null ? string.Empty : $" descr=\"{description}\"";
        return $$"""
            <w:drawing>
              <wp:inline xmlns:wp="{{wp}}" xmlns:a="{{a}}" xmlns:pic="{{pic}}" xmlns:r="{{r}}">
                <wp:extent cx="914400" cy="457200"/>
                <wp:docPr id="{{drawingId}}" name="Picture {{drawingId}}"{{titleAttribute}}{{descriptionAttribute}}/>
                <a:graphic>
                  <a:graphicData uri="{{pic}}">
                    <pic:pic>
                      <pic:nvPicPr><pic:cNvPr id="0" name="image.png"/><pic:cNvPicPr/></pic:nvPicPr>
                      <pic:blipFill><a:blip r:{{linkAttribute}}="{{relationshipId}}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                      <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="457200"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                    </pic:pic>
                  </a:graphicData>
                </a:graphic>
              </wp:inline>
            </w:drawing>
            """;
    }

    private static string CaptionParagraph(
        string label,
        string result,
        string suffix,
        bool strict = false
    ) => $$"""
        <w:p>
          <w:pPr><w:pStyle w:val="Caption"/></w:pPr>
          <w:r><w:fldChar w:fldCharType="begin"/></w:r>
          <w:r><w:instrText xml:space="preserve"> SEQ {{label}} \* ARABIC </w:instrText></w:r>
          <w:r><w:fldChar w:fldCharType="separate"/></w:r>
          <w:r><w:t>{{result}}</w:t></w:r>
          <w:r><w:fldChar w:fldCharType="end"/></w:r>
          <w:r><w:t>{{suffix}}</w:t></w:r>
        </w:p>
        """;

    private static MemoryStream BuildPackage(
        string body,
        bool strict = false,
        bool externalImage = false
    )
    {
        var w = strict
            ? "http://purl.oclc.org/ooxml/wordprocessingml/main"
            : "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var officeDocumentRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
        var imageRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/image"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
        var image = externalImage
            ? $"<Relationship Id=\"rIdExternal\" Type=\"{imageRelationship}\" Target=\"https://example.invalid/never-fetch.png\" TargetMode=\"External\"/>"
            : $"<Relationship Id=\"rIdImage\" Type=\"{imageRelationship}\" Target=\"media/image1.png\"/>";
        var contentTypes = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="png" ContentType="image/png"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """;
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", contentTypes);
            AddEntry(
                archive,
                "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{officeDocumentRelationship}\" Target=\"word/document.xml\"/></Relationships>"
            );
            AddEntry(
                archive,
                "word/document.xml",
                $$"""
                <w:document xmlns:w="{{w}}"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
                  xmlns:v="urn:schemas-microsoft-com:vml"
                  xmlns:o="urn:schemas-microsoft-com:office:office">
                  <w:body>{{body}}<w:sectPr/></w:body>
                </w:document>
                """
            );
            AddEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{image}</Relationships>"
            );
            if (!externalImage)
            {
                AddEntry(archive, "word/media/image1.png", "not-decoded-by-figure-graph");
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
