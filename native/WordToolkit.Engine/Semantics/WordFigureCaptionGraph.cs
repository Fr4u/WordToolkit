using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordFigureIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordFigureObjectKind
{
    Picture,
    Chart,
    Diagram,
    Shape,
    Ink,
    ContentPart,
    EmbeddedObject,
    Unknown,
}

public enum WordFigureRepresentationKind
{
    DrawingInline,
    DrawingAnchor,
    VmlPicture,
    LegacyObject,
    Unknown,
}

public enum WordFigureRepresentationSelectionBasis
{
    SingleDeclaredRepresentation,
    AlternateContentChoicePresentNotEvaluated,
    AlternateContentFallbackOnly,
    AlternateContentUnclassified,
}

public enum WordFigureResourceRole
{
    ImageEmbed,
    ImageLink,
    Chart,
    DiagramData,
    DiagramLayout,
    DiagramQuickStyle,
    DiagramColors,
    ContentPart,
    EmbeddedObject,
    Hyperlink,
    VmlImage,
    Other,
}

public enum WordCaptionKind
{
    Figure,
    Table,
    Equation,
    Custom,
    Unknown,
}

public enum WordFigureCaptionAssociationStatus
{
    Selected,
    Candidate,
    Ambiguous,
}

public enum WordFigureCaptionConfidence
{
    Weak,
    Moderate,
    Strong,
    VeryStrong,
}

public enum WordFigureCaptionDirection
{
    SameParagraph,
    CaptionBeforeFigure,
    CaptionAfterFigure,
}

public sealed record WordFigureIssue(
    string Code,
    WordFigureIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? FigureId = null,
    string? CaptionId = null,
    string? RelationshipId = null
);

public sealed record WordFigurePositionDefinition(
    string? RelativeFrom,
    string? Alignment,
    long? OffsetEmu
);

public sealed record WordFigurePlacementDefinition(
    WordFigureRepresentationKind Kind,
    long? WidthEmu,
    long? HeightEmu,
    long? DistanceTopEmu,
    long? DistanceBottomEmu,
    long? DistanceLeftEmu,
    long? DistanceRightEmu,
    long? RelativeHeight,
    bool? BehindDocument,
    bool? LayoutInCell,
    bool? AllowOverlap,
    string? WrapKind,
    WordFigurePositionDefinition? HorizontalPosition,
    WordFigurePositionDefinition? VerticalPosition,
    string? LegacyStyle
);

public sealed record WordFigureAccessibilityDefinition(
    string? Name,
    string? Title,
    int TitleCharacterCount,
    bool TitleTruncated,
    string? Description,
    int DescriptionCharacterCount,
    bool DescriptionTruncated,
    bool? Hidden,
    bool Decorative,
    bool HasAlternativeText
);

public sealed record WordFigureResourceDefinition(
    string Id,
    WordFigureResourceRole Role,
    string? RelationshipId,
    string? RelationshipType,
    OpcRelationshipTargetMode? TargetMode,
    string? Target,
    string? TargetPartUri,
    string? TargetContentType,
    long? TargetByteLength,
    string? TargetSha256,
    bool IsResolved,
    bool IsExternal,
    bool TargetTruncated,
    int SourceElementOrdinal
);

public sealed record WordFigureRepresentationDefinition(
    string Id,
    SemanticNodeId SemanticNodeId,
    string PartUri,
    int SourceElementOrdinal,
    string SourcePath,
    WordStoryKind StoryKind,
    SemanticNodeId StoryRootNodeId,
    SemanticNodeId ParagraphNodeId,
    SemanticNodeId ContainerNodeId,
    bool IsInDeletedContent,
    WordFigureRepresentationKind Kind,
    WordFigureObjectKind ObjectKind,
    string? GraphicDataUri,
    string? AlternateContentGroupId,
    string? AlternateContentBranch,
    ulong? NonVisualDrawingId,
    WordFigurePlacementDefinition Placement,
    WordFigureAccessibilityDefinition Accessibility,
    IReadOnlyList<WordFigureResourceDefinition> Resources,
    IReadOnlyList<string> UnmodeledPayloadElements
);

public sealed record WordFigureDefinition(
    string Id,
    WordFigureObjectKind ObjectKind,
    WordStoryKind StoryKind,
    SemanticNodeId StoryRootNodeId,
    SemanticNodeId ParagraphNodeId,
    SemanticNodeId ContainerNodeId,
    string PartUri,
    int SourceElementOrdinal,
    bool IsInDeletedContent,
    string? PrimaryRepresentationId,
    WordFigureRepresentationSelectionBasis RepresentationSelectionBasis,
    string? AlternateContentGroupId,
    IReadOnlyList<WordFigureRepresentationDefinition> Representations,
    IReadOnlyList<WordFigureResourceDefinition> Resources,
    string? SelectedCaptionId
);

public sealed record WordCaptionDefinition(
    string Id,
    SemanticNodeId ParagraphNodeId,
    string PartUri,
    int SourceElementOrdinal,
    string SourcePath,
    WordStoryKind StoryKind,
    SemanticNodeId StoryRootNodeId,
    SemanticNodeId ContainerNodeId,
    bool IsInDeletedContent,
    string? ParagraphStyleId,
    string? ParagraphStyleName,
    bool HasCaptionStyleEvidence,
    IReadOnlyList<string> SequenceFieldIds,
    IReadOnlyList<string> SequenceLabels,
    string? PrimaryLabel,
    WordCaptionKind Kind,
    string? Text,
    int TextCharacterCount,
    bool TextTruncated,
    string? SequenceResultText,
    int SequenceResultCharacterCount,
    bool SequenceResultTruncated,
    string? SelectedFigureId
);

public sealed record WordFigureCaptionAssociation(
    string Id,
    string FigureId,
    string CaptionId,
    WordFigureCaptionAssociationStatus Status,
    WordFigureCaptionConfidence Confidence,
    WordFigureCaptionDirection Direction,
    int ParagraphDistance,
    int Score,
    bool SameContainer,
    bool HasSequenceEvidence,
    bool HasCaptionStyleEvidence,
    bool LabelCompatible
);

public sealed class WordFigureCaptionGraph
{
    private readonly IReadOnlyDictionary<string, WordFigureDefinition> _figuresById;
    private readonly IReadOnlyDictionary<string, WordCaptionDefinition> _captionsById;

    internal WordFigureCaptionGraph(
        string packageFingerprint,
        string mainPartUri,
        IReadOnlyList<WordFigureDefinition> figures,
        IReadOnlyList<WordCaptionDefinition> captions,
        IReadOnlyList<WordFigureCaptionAssociation> associations,
        IReadOnlyList<WordFigureIssue> issues,
        bool issuesTruncated,
        long parsedXmlBytes,
        int parsedXmlElements
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        Figures = new ReadOnlyCollection<WordFigureDefinition>(figures.ToArray());
        Captions = new ReadOnlyCollection<WordCaptionDefinition>(captions.ToArray());
        Associations = new ReadOnlyCollection<WordFigureCaptionAssociation>(
            associations.ToArray()
        );
        Issues = new ReadOnlyCollection<WordFigureIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        ParsedXmlBytes = parsedXmlBytes;
        ParsedXmlElements = parsedXmlElements;
        _figuresById = new ReadOnlyDictionary<string, WordFigureDefinition>(
            figures.ToDictionary(figure => figure.Id, StringComparer.Ordinal)
        );
        _captionsById = new ReadOnlyDictionary<string, WordCaptionDefinition>(
            captions.ToDictionary(caption => caption.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public IReadOnlyList<WordFigureDefinition> Figures { get; }

    public IReadOnlyList<WordCaptionDefinition> Captions { get; }

    public IReadOnlyList<WordFigureCaptionAssociation> Associations { get; }

    public IReadOnlyList<WordFigureIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public long ParsedXmlBytes { get; }

    public int ParsedXmlElements { get; }

    public bool TryGetFigure(string id, out WordFigureDefinition? figure) =>
        _figuresById.TryGetValue(id, out figure);

    public bool TryGetCaption(string id, out WordCaptionDefinition? caption) =>
        _captionsById.TryGetValue(id, out caption);
}

public sealed record WordFigureCaptionGraphOptions
{
    public static WordFigureCaptionGraphOptions Default { get; } = new();

    public int MaxStoryParts { get; init; } = 256;

    public int MaxFigures { get; init; } = 100_000;

    public int MaxRepresentations { get; init; } = 200_000;

    public int MaxCaptions { get; init; } = 100_000;

    public int MaxResources { get; init; } = 500_000;

    public int MaxAssociations { get; init; } = 500_000;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxPartBytes { get; init; } = 128 * 1024 * 1024;

    public long MaxAggregateXmlBytes { get; init; } = 512L * 1024 * 1024;

    public int MaxElementsPerPart { get; init; } = 2_000_000;

    public int MaxAggregateElements { get; init; } = 5_000_000;

    public int MaxTextCharacters { get; init; } = 8_192;

    public long MaxMetadataCharacters { get; init; } = 32L * 1024 * 1024;

    public int MaxCaptionParagraphDistance { get; init; } = 2;

    internal void Validate()
    {
        if (
            MaxStoryParts <= 0
            || MaxFigures <= 0
            || MaxRepresentations <= 0
            || MaxCaptions <= 0
            || MaxResources <= 0
            || MaxAssociations <= 0
            || MaxIssues <= 0
            || MaxPartBytes <= 0
            || MaxAggregateXmlBytes <= 0
            || MaxElementsPerPart <= 0
            || MaxAggregateElements <= 0
            || MaxTextCharacters <= 0
            || MaxMetadataCharacters <= 0
            || MaxCaptionParagraphDistance < 0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(WordFigureCaptionGraphOptions),
                "All figure/caption graph limits must be positive, except paragraph distance which may be zero."
            );
        }
        if (MaxPartBytes > MaxAggregateXmlBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPartBytes));
        }
        if (MaxElementsPerPart > MaxAggregateElements)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxElementsPerPart));
        }
    }
}

public sealed class WordFigureCaptionGraphBuilder
{
    private const string TransitionalWordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string TransitionalRelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string StrictRelationshipsNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string MarkupCompatibilityNamespace =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string TransitionalWordprocessingDrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string StrictWordprocessingDrawingNamespace =
        "http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing";
    private const string TransitionalDrawingMainNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string StrictDrawingMainNamespace =
        "http://purl.oclc.org/ooxml/drawingml/main";
    private const string TransitionalPictureNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string StrictPictureNamespace =
        "http://purl.oclc.org/ooxml/drawingml/picture";
    private const string TransitionalChartNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string StrictChartNamespace =
        "http://purl.oclc.org/ooxml/drawingml/chart";
    private const string TransitionalDiagramNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private const string StrictDiagramNamespace =
        "http://purl.oclc.org/ooxml/drawingml/diagram";
    private const string Word2010Namespace =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string WordprocessingShapeNamespace =
        "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string DecorativeNamespace =
        "http://schemas.microsoft.com/office/drawing/2017/decorative";
    private const string VmlNamespace = "urn:schemas-microsoft-com:vml";
    private const string OfficeNamespace = "urn:schemas-microsoft-com:office:office";

    private readonly WordFigureCaptionGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordFigureCaptionGraphBuilder(WordFigureCaptionGraphOptions? options = null)
    {
        _options = options ?? WordFigureCaptionGraphOptions.Default;
        _options.Validate();
    }

    public WordFigureCaptionGraphBuilder(
        WordFigureCaptionGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordFigureCaptionGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordFigureCaptionGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        var semantic = _resourceLease is null
            ? new WordSemanticProjector().Project(package, cancellationToken)
            : new WordSemanticProjector(null, _resourceLease).Project(
                package,
                cancellationToken
            );
        var references = (_resourceLease is null
            ? new WordReferenceGraphBuilder()
            : new WordReferenceGraphBuilder(null, _resourceLease)).Build(
            package,
            semantic,
            cancellationToken
        );
        var styles = (_resourceLease is null
            ? new WordStyleGraphBuilder()
            : new WordStyleGraphBuilder(null, _resourceLease)).Build(
            package,
            semantic,
            cancellationToken
        );
        return Build(package, semantic, references, styles, cancellationToken);
    }

    public WordFigureCaptionGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordReferenceGraph referenceGraph,
        WordStyleGraph styleGraph,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(referenceGraph);
        ArgumentNullException.ThrowIfNull(styleGraph);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.FiguresAndCaptions
        );
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.FiguresAndCaptions,
            semanticDocument.NodeCount,
            128
        );
        RequireSamePackage(package, semanticDocument, referenceGraph, styleGraph);
        if (semanticDocument.ProjectedPartCount > _options.MaxStoryParts)
        {
            throw new WordFigureLimitException(
                $"Projected story count exceeds {_options.MaxStoryParts}."
            );
        }

        var state = new BuildState(
            _options,
            package,
            semanticDocument,
            referenceGraph,
            styleGraph
        );
        foreach (var partUri in semanticDocument.ProjectedPartUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordFigureProjectionException(
                    $"Projected story part '{partUri}' is missing from the package."
                );
            }
            var source = ParsePart(part, state, cancellationToken);
            ParseStoryPart(partUri, source, state, cancellationToken);
        }

        state.GroupRepresentations();
        state.BuildAssociations();
        state.AddCompletenessIssues();
        return state.Freeze(package.Fingerprint, semanticDocument.MainPartUri);
    }

    private LosslessXmlDocument ParsePart(
        OpcPart part,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        if (part.Entry.Content.Length > _options.MaxPartBytes)
        {
            throw new WordFigureLimitException(
                $"Story part '{part.Uri}' exceeds {_options.MaxPartBytes} bytes."
            );
        }
        try
        {
            state.EnsureCanParse(part.Entry.Content.Length);
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxPartBytes,
                MaxXmlCharacters = _options.MaxPartBytes,
                MaxXmlElements = _options.MaxElementsPerPart,
                MaxXmlDepth = 256,
                MaxTextCharacters = _options.MaxPartBytes,
            };
            var source = _resourceLease is null
                ? LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    cancellationToken
                )
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.FiguresAndCaptions,
                    cancellationToken
                );
            state.AddParsedPart(part.Entry.Content.Length, source.Elements.Count);
            return source;
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordFigureLimitException(
                $"Part '{part.Uri}' exceeds an XML safety limit: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordFigureProjectionException(
                $"Part '{part.Uri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private void ParseStoryPart(
        string partUri,
        LosslessXmlDocument source,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var root = source.ParsedDocument.Root
            ?? throw new WordFigureProjectionException(
                $"Projected story part '{partUri}' has no root element."
            );
        var representationElements = root.DescendantsAndSelf()
            .Where(IsRepresentationElement)
            .Where(element => !element.Ancestors().Any(IsRepresentationElement))
            .OrderBy(source.GetElementOrdinal)
            .ToArray();
        if (state.Representations.Count + representationElements.Length > _options.MaxRepresentations)
        {
            throw new WordFigureLimitException(
                $"Figure representation count exceeds {_options.MaxRepresentations}."
            );
        }
        foreach (var element in representationElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Representations.Add(
                ParseRepresentation(partUri, element, source, state, cancellationToken)
            );
        }

        var paragraphElements = root.DescendantsAndSelf()
            .Where(element => IsWordElement(element, "p"))
            .OrderBy(source.GetElementOrdinal)
            .ToArray();
        var fieldsByParagraph = IndexFieldsByParagraph(
            partUri,
            source,
            state.ReferenceGraph.Fields
        );
        foreach (var paragraph in paragraphElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paragraphOrdinal = source.GetElementOrdinal(paragraph);
            ParseCaptionCandidate(
                partUri,
                paragraph,
                source,
                state,
                fieldsByParagraph.GetValueOrDefault(paragraphOrdinal) ?? [],
                cancellationToken
            );
        }
    }

    private static IReadOnlyDictionary<int, WordFieldDefinition[]> IndexFieldsByParagraph(
        string partUri,
        LosslessXmlDocument source,
        IReadOnlyList<WordFieldDefinition> fields
    )
    {
        var result = new Dictionary<int, List<WordFieldDefinition>>();
        foreach (var field in fields)
        {
            if (
                !string.Equals(field.PartUri, partUri, StringComparison.Ordinal)
                || (uint)field.StartElementOrdinal >= (uint)source.Elements.Count
            )
            {
                continue;
            }
            var current = field.StartElementOrdinal;
            while (true)
            {
                if (IsWordElement(source.GetParsedElement(current), "p"))
                {
                    if (!result.TryGetValue(current, out var paragraphFields))
                    {
                        paragraphFields = [];
                        result.Add(current, paragraphFields);
                    }
                    paragraphFields.Add(field);
                    break;
                }
                var parent = source.GetElement(current).ParentOrdinal;
                if (parent is null)
                {
                    break;
                }
                current = parent.Value;
            }
        }
        return result.ToDictionary(
            item => item.Key,
            item => item.Value.OrderBy(field => field.StartElementOrdinal).ToArray()
        );
    }

    private RepresentationDraft ParseRepresentation(
        string partUri,
        XElement element,
        LosslessXmlDocument source,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var ordinal = source.GetElementOrdinal(element);
        var semantic = state.RequiredSemantic(partUri, ordinal, WordSemanticNodeKind.Drawing);
        var paragraph = state.RequiredAncestor(semantic, WordSemanticNodeKind.Paragraph);
        var story = state.StoryFor(paragraph);
        var container = state.ContainerFor(paragraph, story.Root);
        var alternate = element.Ancestors()
            .FirstOrDefault(ancestor =>
                ancestor.Name.NamespaceName == MarkupCompatibilityNamespace
                && ancestor.Name.LocalName == "AlternateContent"
            );
        var alternateOrdinal = alternate is null ? (int?)null : source.GetElementOrdinal(alternate);
        var alternateGroupId = alternateOrdinal is null
            ? null
            : StableId(
                "wdfac_",
                state.Package.Fingerprint,
                partUri,
                alternateOrdinal.Value.ToString(CultureInfo.InvariantCulture)
            );
        var branch = alternate is null
            ? null
            : element.Ancestors()
                .FirstOrDefault(ancestor =>
                    ReferenceEquals(ancestor.Parent, alternate)
                    && ancestor.Name.NamespaceName == MarkupCompatibilityNamespace
                    && ancestor.Name.LocalName is "Choice" or "Fallback"
                )?.Name.LocalName;

        var placementElement = EnumerateWithCancellation(
            element.DescendantsAndSelf(),
            cancellationToken
        )
            .FirstOrDefault(item => IsWordprocessingDrawingElement(item, "inline")
                || IsWordprocessingDrawingElement(item, "anchor"));
        var kind = RepresentationKind(element, placementElement);
        var objectKind = ObjectKind(element, cancellationToken);
        var graphicData = EnumerateWithCancellation(
            element.DescendantsAndSelf(),
            cancellationToken
        )
            .FirstOrDefault(item => IsDrawingMainElement(item, "graphicData"));
        var graphicDataUri = BoundMetadata(
            graphicData?.Attributes().FirstOrDefault(attribute =>
                !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == "uri"
            )?.Value,
            _options.MaxTextCharacters,
            state,
            out _
        );
        var docProperties = placementElement?.Elements()
            .Where(item => IsWordprocessingDrawingElement(item, "docPr"))
            .ToArray() ?? [];
        if (docProperties.Length > 1)
        {
            state.AddIssue(
                "FIGURE_DOC_PROPERTIES_AMBIGUOUS",
                WordFigureIssueSeverity.Error,
                "Drawing placement contains duplicate non-visual docPr elements.",
                partUri,
                ordinal
            );
        }
        var docPr = docProperties.FirstOrDefault();
        if (placementElement is not null && docPr is null)
        {
            state.AddIssue(
                "FIGURE_DOC_PROPERTIES_MISSING",
                WordFigureIssueSeverity.Warning,
                "Drawing placement has no non-visual docPr metadata.",
                partUri,
                ordinal
            );
        }
        var drawingId = ParseUnsigned(AttributeByLocal(docPr, "id"));
        if (docPr is not null && drawingId is null)
        {
            state.AddIssue(
                "FIGURE_DOC_PROPERTIES_ID_INVALID",
                WordFigureIssueSeverity.Error,
                "Drawing docPr id is missing or is not an unsigned integer.",
                partUri,
                ordinal
            );
        }

        var vmlShape = EnumerateWithCancellation(
            element.DescendantsAndSelf(),
            cancellationToken
        )
            .FirstOrDefault(item => item.Name.NamespaceName == VmlNamespace && item.Name.LocalName == "shape");
        var name = AttributeByLocal(docPr, "name") ?? AttributeByLocal(vmlShape, "id");
        var title = AttributeByLocal(docPr, "title") ?? AttributeByLocal(vmlShape, "title");
        var description = AttributeByLocal(docPr, "descr") ?? AttributeByLocal(vmlShape, "alt");
        var boundedName = BoundMetadata(name, _options.MaxTextCharacters, state, out _);
        var boundedTitle = BoundMetadata(title, _options.MaxTextCharacters, state, out var titleTruncated);
        var boundedDescription = BoundMetadata(
            description,
            _options.MaxTextCharacters,
            state,
            out var descriptionTruncated
        );
        var decorative = EnumerateWithCancellation(
            element.DescendantsAndSelf(),
            cancellationToken
        ).Any(item =>
            item.Name.NamespaceName == DecorativeNamespace
            && item.Name.LocalName == "decorative"
            && ParseOnOff(AttributeByLocal(item, "val")) == true
        );
        var accessibility = new WordFigureAccessibilityDefinition(
            boundedName,
            boundedTitle,
            title?.Length ?? 0,
            titleTruncated,
            boundedDescription,
            description?.Length ?? 0,
            descriptionTruncated,
            ParseOnOff(AttributeByLocal(docPr, "hidden")),
            decorative,
            !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(description)
        );
        var resources = ParseResources(
            partUri,
            element,
            source,
            state,
            ordinal,
            cancellationToken
        );
        var unmodeled = UnmodeledPayloadElements(
            element,
            objectKind,
            state,
            cancellationToken
        );
        var representationId = StableId(
            "wdfr_",
            state.Package.Fingerprint,
            semantic.Id.Value
        );
        if (!accessibility.HasAlternativeText && !accessibility.Decorative)
        {
            state.AddIssue(
                "FIGURE_ALT_TEXT_MISSING",
                WordFigureIssueSeverity.Warning,
                "Figure is neither marked decorative nor supplied with title/description alternative text.",
                partUri,
                ordinal,
                figureId: representationId
            );
        }
        if (objectKind == WordFigureObjectKind.Unknown)
        {
            state.AddIssue(
                "FIGURE_PAYLOAD_UNMODELED",
                WordFigureIssueSeverity.Info,
                "Figure payload is preserved but its DrawingML/VML object family is not recognized.",
                partUri,
                ordinal,
                figureId: representationId
            );
        }

        return new RepresentationDraft(
            representationId,
            semantic,
            partUri,
            ordinal,
            story.Kind,
            story.Root.Id,
            paragraph.Id,
            container.Id,
            element.Ancestors().Any(IsDeletedRevisionElement),
            kind,
            objectKind,
            graphicDataUri,
            alternateGroupId,
            branch,
            drawingId,
            ParsePlacement(kind, placementElement, vmlShape, state, partUri, ordinal),
            accessibility,
            resources,
            unmodeled
        );
    }

    private IReadOnlyList<WordFigureResourceDefinition> ParseResources(
        string partUri,
        XElement representation,
        LosslessXmlDocument source,
        BuildState state,
        int representationOrdinal,
        CancellationToken cancellationToken
    )
    {
        var declarations = new List<ResourceDeclaration>();
        var declaredKeys = new HashSet<(WordFigureResourceRole Role, string Id, int Ordinal)>();
        foreach (var element in EnumerateWithCancellation(
            representation.DescendantsAndSelf(),
            cancellationToken
        ))
        {
            foreach (var attribute in element.Attributes().Where(attribute =>
                !attribute.IsNamespaceDeclaration
                && IsRelationshipAttribute(element, attribute)
            ))
            {
                if (attribute.Value.Length > 4_096)
                {
                    throw new WordFigureLimitException(
                        "Figure relationship identifier exceeds 4096 characters."
                    );
                }
                state.AddMetadata(attribute.Value.Length);
                var declaration = new ResourceDeclaration(
                    ResourceRole(element, attribute),
                    attribute.Value,
                    source.GetElementOrdinal(element)
                );
                if (!declaredKeys.Add((
                    declaration.Role,
                    declaration.RelationshipId,
                    declaration.SourceElementOrdinal
                )))
                {
                    continue;
                }
                if (++state.ResourceCount > _options.MaxResources)
                {
                    throw new WordFigureLimitException(
                        $"Figure resource count exceeds {_options.MaxResources}."
                    );
                }
                declarations.Add(declaration);
            }
        }
        var vmlSources = EnumerateWithCancellation(
            representation.DescendantsAndSelf(),
            cancellationToken
        )
            .Where(item => item.Name.NamespaceName == VmlNamespace && item.Name.LocalName == "imagedata")
            .Select(item => new
            {
                Element = item,
                Source = AttributeByLocal(item, "src"),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Source));
        var relationshipsById = state.RelationshipsById(partUri);
        var result = new List<WordFigureResourceDefinition>();
        foreach (var declaration in declarations)
        {
            var matches = relationshipsById.GetValueOrDefault(declaration.RelationshipId) ?? [];
            if (matches.Count != 1)
            {
                state.AddIssue(
                    matches.Count == 0
                        ? "FIGURE_RELATIONSHIP_MISSING"
                        : "FIGURE_RELATIONSHIP_AMBIGUOUS",
                    WordFigureIssueSeverity.Error,
                    matches.Count == 0
                        ? "Figure relationship identifier does not exist in the source part."
                        : "Figure relationship identifier resolves to multiple relationships.",
                    partUri,
                    declaration.SourceElementOrdinal,
                    relationshipId: declaration.RelationshipId
                );
            }
            var relationship = matches.FirstOrDefault();
            var target = relationship?.Target;
            var boundedTarget = BoundMetadata(
                target,
                _options.MaxTextCharacters,
                state,
                out var targetTruncated
            );
            var boundedRelationshipType = BoundMetadata(
                relationship?.Type,
                _options.MaxTextCharacters,
                state,
                out _
            );
            OpcPart? targetPart = null;
            if (
                relationship?.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.ResolvedTargetPartUri is { } targetPartUri
            )
            {
                state.Package.Parts.TryGetValue(targetPartUri, out targetPart);
            }
            var resolved = relationship is not null
                && relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && targetPart is not null;
            if (relationship is not null && !resolved)
            {
                state.AddIssue(
                    relationship.TargetMode == OpcRelationshipTargetMode.External
                        ? "FIGURE_EXTERNAL_RESOURCE_DECLARED"
                        : "FIGURE_RESOURCE_UNRESOLVED",
                    relationship.TargetMode == OpcRelationshipTargetMode.External
                        ? WordFigureIssueSeverity.Info
                        : WordFigureIssueSeverity.Warning,
                    relationship.TargetMode == OpcRelationshipTargetMode.External
                        ? "Figure declares an external resource; the engine records it but never follows it."
                        : "Figure relationship does not resolve internally to a package part.",
                    partUri,
                    declaration.SourceElementOrdinal,
                    relationshipId: declaration.RelationshipId
                );
            }
            result.Add(new WordFigureResourceDefinition(
                StableId(
                    "wdfrs_",
                    state.Package.Fingerprint,
                    partUri,
                    representationOrdinal.ToString(CultureInfo.InvariantCulture),
                    declaration.Role.ToString(),
                    declaration.RelationshipId,
                    declaration.SourceElementOrdinal.ToString(CultureInfo.InvariantCulture)
                ),
                declaration.Role,
                declaration.RelationshipId,
                boundedRelationshipType,
                relationship?.TargetMode,
                boundedTarget,
                relationship?.ResolvedTargetPartUri,
                targetPart?.ContentType,
                targetPart?.Entry.Content.Length,
                targetPart?.Entry.Sha256,
                resolved,
                relationship?.TargetMode == OpcRelationshipTargetMode.External,
                targetTruncated,
                declaration.SourceElementOrdinal
            ));
        }
        foreach (var direct in vmlSources)
        {
            if (++state.ResourceCount > _options.MaxResources)
            {
                throw new WordFigureLimitException(
                    $"Figure resource count exceeds {_options.MaxResources}."
                );
            }
            var boundedTarget = BoundMetadata(
                direct.Source,
                _options.MaxTextCharacters,
                state,
                out var truncated
            );
            result.Add(new WordFigureResourceDefinition(
                StableId(
                    "wdfrs_",
                    state.Package.Fingerprint,
                    partUri,
                    representationOrdinal.ToString(CultureInfo.InvariantCulture),
                    "vml-src",
                    source.GetElementOrdinal(direct.Element).ToString(CultureInfo.InvariantCulture)
                ),
                WordFigureResourceRole.VmlImage,
                null,
                null,
                OpcRelationshipTargetMode.External,
                boundedTarget,
                null,
                null,
                null,
                null,
                false,
                true,
                truncated,
                source.GetElementOrdinal(direct.Element)
            ));
            state.AddIssue(
                "FIGURE_VML_DIRECT_SOURCE_DECLARED",
                WordFigureIssueSeverity.Info,
                "Legacy VML image declares a direct source; the engine records it but never follows it.",
                partUri,
                source.GetElementOrdinal(direct.Element)
            );
        }
        return result;
    }

    private void ParseCaptionCandidate(
        string partUri,
        XElement paragraphElement,
        LosslessXmlDocument source,
        BuildState state,
        IReadOnlyList<WordFieldDefinition> fields,
        CancellationToken cancellationToken
    )
    {
        var ordinal = source.GetElementOrdinal(paragraphElement);
        var paragraph = state.RequiredSemantic(partUri, ordinal, WordSemanticNodeKind.Paragraph);
        var story = state.StoryFor(paragraph);
        var container = state.ContainerFor(paragraph, story.Root);
        state.AddParagraphPosition(story.Root.Id, container.Id, paragraph.Id, ordinal);
        var styleId = AttributeByLocal(
            paragraphElement.Elements()
                .FirstOrDefault(item => IsWordElement(item, "pPr"))?
                .Elements()
                .FirstOrDefault(item => IsWordElement(item, "pStyle")),
            "val"
        );
        WordStyleDefinition? style = null;
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            state.StyleGraph.TryGetStyle(styleId, out style);
        }
        var styleName = style?.Name;
        var hasCaptionStyle = IsCaptionStyle(styleId, style);
        var sequences = fields.Where(field =>
            string.Equals(field.FieldType, "SEQ", StringComparison.OrdinalIgnoreCase)
        ).ToArray();
        if (!hasCaptionStyle && sequences.Length == 0)
        {
            return;
        }
        if (state.CaptionDrafts.Count >= _options.MaxCaptions)
        {
            throw new WordFigureLimitException(
                $"Caption count exceeds {_options.MaxCaptions}."
            );
        }
        var paragraphDeleted = paragraphElement.Ancestors().Any(IsDeletedRevisionElement);
        var activeSequences = sequences.Where(field => !field.IsInDeletedContent).ToArray();
        var visibleText = VisibleParagraphText(
            paragraphElement,
            _options.MaxTextCharacters,
            state,
            cancellationToken
        );
        var hasActiveEvidence = activeSequences.Length != 0
            || hasCaptionStyle
                && (sequences.Length == 0 || visibleText.CharacterCount != 0);
        var effectiveSequences = paragraphDeleted || !hasActiveEvidence
            ? sequences
            : activeSequences;
        var labels = effectiveSequences.Select(SequenceLabel)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (effectiveSequences.Length > 1)
        {
            state.AddIssue(
                "CAPTION_MULTIPLE_SEQUENCE_FIELDS",
                WordFigureIssueSeverity.Warning,
                "Caption candidate contains multiple SEQ fields; its primary label is not structurally unique.",
                partUri,
                ordinal
            );
        }
        var primaryLabel = labels.Length == 1 ? labels[0] : null;
        var rawResult = effectiveSequences.Length == 1
            ? effectiveSequences[0].ResultText
            : null;
        var resultText = BoundMetadata(
            rawResult,
            _options.MaxTextCharacters,
            state,
            out var resultTruncated
        );
        var captionId = StableId(
            "wdfc_",
            state.Package.Fingerprint,
            paragraph.Id.Value
        );
        state.CaptionDrafts.Add(new CaptionDraft(
            captionId,
            paragraph,
            partUri,
            ordinal,
            story.Kind,
            story.Root.Id,
            container.Id,
            paragraphDeleted || !hasActiveEvidence,
            styleId,
            styleName,
            hasCaptionStyle,
            effectiveSequences.Select(field => field.Id).ToArray(),
            labels,
            primaryLabel,
            CaptionKind(primaryLabel),
            visibleText.Value,
            visibleText.CharacterCount,
            visibleText.Truncated,
            resultText,
            rawResult?.Length ?? 0,
            resultTruncated
        ));
        if (effectiveSequences.Length == 0)
        {
            state.AddIssue(
                "CAPTION_SEQUENCE_FIELD_MISSING",
                WordFigureIssueSeverity.Info,
                "Paragraph has caption-style evidence but no SEQ field; numbering semantics are not declared.",
                partUri,
                ordinal,
                captionId: captionId
            );
        }
        else if (primaryLabel is null)
        {
            state.AddIssue(
                "CAPTION_SEQUENCE_LABEL_AMBIGUOUS",
                WordFigureIssueSeverity.Warning,
                "Caption SEQ label is missing, dynamic, or ambiguous.",
                partUri,
                ordinal,
                captionId: captionId
            );
        }
    }

    private WordFigurePlacementDefinition ParsePlacement(
        WordFigureRepresentationKind kind,
        XElement? placement,
        XElement? vmlShape,
        BuildState state,
        string partUri,
        int ordinal
    )
    {
        var extent = placement?.Elements()
            .FirstOrDefault(item => IsWordprocessingDrawingElement(item, "extent"));
        var width = ParseLong(AttributeByLocal(extent, "cx"));
        var height = ParseLong(AttributeByLocal(extent, "cy"));
        if (extent is not null && (width is null || height is null || width < 0 || height < 0))
        {
            state.AddIssue(
                "FIGURE_EXTENT_INVALID",
                WordFigureIssueSeverity.Error,
                "Drawing extent must contain non-negative integer cx and cy values.",
                partUri,
                ordinal
            );
        }
        var wrap = placement?.Elements().FirstOrDefault(item =>
            IsWordprocessingDrawingElement(item)
            && item.Name.LocalName is "wrapNone" or "wrapSquare" or "wrapThrough"
                or "wrapTight" or "wrapTopAndBottom"
        )?.Name.LocalName;
        return new WordFigurePlacementDefinition(
            kind,
            width,
            height,
            ParseLong(AttributeByLocal(placement, "distT")),
            ParseLong(AttributeByLocal(placement, "distB")),
            ParseLong(AttributeByLocal(placement, "distL")),
            ParseLong(AttributeByLocal(placement, "distR")),
            ParseLong(AttributeByLocal(placement, "relativeHeight")),
            ParseOnOff(AttributeByLocal(placement, "behindDoc")),
            ParseOnOff(AttributeByLocal(placement, "layoutInCell")),
            ParseOnOff(AttributeByLocal(placement, "allowOverlap")),
            wrap,
            ParsePosition(placement, "positionH"),
            ParsePosition(placement, "positionV"),
            BoundMetadata(
                AttributeByLocal(vmlShape, "style"),
                _options.MaxTextCharacters,
                state,
                out _
            )
        );
    }

    private static WordFigurePositionDefinition? ParsePosition(
        XElement? placement,
        string localName
    )
    {
        var position = placement?.Elements()
            .FirstOrDefault(item => IsWordprocessingDrawingElement(item, localName));
        if (position is null)
        {
            return null;
        }
        return new WordFigurePositionDefinition(
            AttributeByLocal(position, "relativeFrom"),
            position.Elements()
                .FirstOrDefault(item => IsWordprocessingDrawingElement(item, "align"))?.Value,
            ParseLong(position.Elements()
                .FirstOrDefault(item => IsWordprocessingDrawingElement(item, "posOffset"))?.Value)
        );
    }

    private static bool IsRepresentationElement(XElement element) =>
        IsWordElement(element)
        && element.Name.LocalName is "drawing" or "pict" or "object";

    private static WordFigureRepresentationKind RepresentationKind(
        XElement element,
        XElement? placement
    ) => element.Name.LocalName switch
    {
        "drawing" when placement is not null
            && IsWordprocessingDrawingElement(placement, "inline") =>
            WordFigureRepresentationKind.DrawingInline,
        "drawing" when placement is not null
            && IsWordprocessingDrawingElement(placement, "anchor") =>
            WordFigureRepresentationKind.DrawingAnchor,
        "pict" => WordFigureRepresentationKind.VmlPicture,
        "object" => WordFigureRepresentationKind.LegacyObject,
        _ => WordFigureRepresentationKind.Unknown,
    };

    private static WordFigureObjectKind ObjectKind(
        XElement element,
        CancellationToken cancellationToken
    )
    {
        var descendants = EnumerateWithCancellation(
            element.DescendantsAndSelf(),
            cancellationToken
        ).ToArray();
        if (descendants.Any(item =>
            item.Name.NamespaceName == OfficeNamespace && item.Name.LocalName == "OLEObject"
        ))
        {
            return WordFigureObjectKind.EmbeddedObject;
        }
        if (descendants.Any(item =>
            item.Name.LocalName == "chart"
            && item.Name.NamespaceName is TransitionalChartNamespace or StrictChartNamespace
        ))
        {
            return WordFigureObjectKind.Chart;
        }
        if (descendants.Any(item =>
            item.Name.LocalName == "relIds"
            && item.Name.NamespaceName is TransitionalDiagramNamespace or StrictDiagramNamespace
        ))
        {
            return WordFigureObjectKind.Diagram;
        }
        if (descendants.Any(item =>
            item.Name.LocalName == "pic"
                && item.Name.NamespaceName is TransitionalPictureNamespace or StrictPictureNamespace
            || item.Name.LocalName == "imagedata" && item.Name.NamespaceName == VmlNamespace
        ))
        {
            return WordFigureObjectKind.Picture;
        }
        if (descendants.Any(item =>
            item.Name.LocalName == "contentPart" && item.Name.NamespaceName == Word2010Namespace
        ))
        {
            return descendants.Any(item =>
                item.Name.NamespaceName.Contains("Ink", StringComparison.OrdinalIgnoreCase)
                || item.Name.NamespaceName.Contains("ink", StringComparison.OrdinalIgnoreCase)
            )
                ? WordFigureObjectKind.Ink
                : WordFigureObjectKind.ContentPart;
        }
        if (descendants.Any(item =>
            item.Name.LocalName == "wsp" && item.Name.NamespaceName == WordprocessingShapeNamespace
            || item.Name.LocalName == "shape" && item.Name.NamespaceName == VmlNamespace
            || item.Name.LocalName == "sp" && IsDrawingMainElement(item)
        ))
        {
            return WordFigureObjectKind.Shape;
        }
        return WordFigureObjectKind.Unknown;
    }

    private static IReadOnlyList<string> UnmodeledPayloadElements(
        XElement element,
        WordFigureObjectKind objectKind,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var result = new List<string>(64);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in EnumerateWithCancellation(
            element.DescendantsAndSelf(),
            cancellationToken
        ))
        {
            if (
                IsWordElement(item)
                || IsFigureInfrastructureElement(item)
                || IsModeledPayloadElement(item, objectKind)
            )
            {
                continue;
            }
            var expandedName = ExpandedNameForDiagnostic(item.Name);
            if (!seen.Add(expandedName))
            {
                continue;
            }
            state.AddMetadata(expandedName.Length);
            result.Add(expandedName);
            if (result.Count == 64)
            {
                break;
            }
        }
        return result;
    }

    private static string ExpandedNameForDiagnostic(XName name)
    {
        const int maximumComponentLength = 256;
        var namespaceName = name.NamespaceName.Length <= maximumComponentLength
            ? name.NamespaceName
            : "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(name.NamespaceName))
            ).ToLowerInvariant();
        var localName = name.LocalName.Length <= maximumComponentLength
            ? name.LocalName
            : name.LocalName[..maximumComponentLength];
        return $"{{{namespaceName}}}{localName}";
    }

    private static bool IsFigureInfrastructureElement(XElement element) =>
        IsWordprocessingDrawingElement(element)
            && element.Name.LocalName is "inline" or "anchor" or "extent" or "docPr"
                or "positionH" or "positionV" or "align" or "posOffset" or "wrapNone"
                or "wrapSquare" or "wrapThrough" or "wrapTight" or "wrapTopAndBottom"
        || IsDrawingMainElement(element)
            && element.Name.LocalName is "graphic" or "graphicData";

    private static bool IsModeledPayloadElement(
        XElement element,
        WordFigureObjectKind objectKind
    ) => objectKind switch
    {
        WordFigureObjectKind.Picture =>
            element.Name.NamespaceName is TransitionalPictureNamespace or StrictPictureNamespace
                && element.Name.LocalName is "pic" or "nvPicPr" or "cNvPr"
                    or "cNvPicPr" or "blipFill" or "spPr"
            || IsDrawingMainElement(element)
                && element.Name.LocalName is "blip" or "stretch" or "fillRect"
                    or "srcRect" or "xfrm" or "off" or "ext" or "prstGeom" or "avLst"
            || element.Name.NamespaceName == VmlNamespace
                && element.Name.LocalName is "imagedata" or "shape",
        WordFigureObjectKind.Chart =>
            element.Name.LocalName == "chart"
                && element.Name.NamespaceName is TransitionalChartNamespace or StrictChartNamespace,
        WordFigureObjectKind.Diagram =>
            element.Name.LocalName == "relIds"
                && element.Name.NamespaceName is TransitionalDiagramNamespace or StrictDiagramNamespace,
        WordFigureObjectKind.EmbeddedObject =>
            element.Name.NamespaceName == OfficeNamespace && element.Name.LocalName == "OLEObject"
            || element.Name.NamespaceName == VmlNamespace
                && element.Name.LocalName is "shape" or "imagedata",
        WordFigureObjectKind.ContentPart or WordFigureObjectKind.Ink =>
            element.Name.NamespaceName == Word2010Namespace
                && element.Name.LocalName == "contentPart",
        WordFigureObjectKind.Shape =>
            element.Name.NamespaceName == VmlNamespace && element.Name.LocalName == "shape"
            || element.Name.NamespaceName == WordprocessingShapeNamespace
                && element.Name.LocalName == "wsp"
            || IsDrawingMainElement(element) && element.Name.LocalName == "sp",
        _ => false,
    };

    private static bool IsRelationshipAttribute(XElement element, XAttribute attribute)
    {
        if (
            attribute.Name.NamespaceName is TransitionalRelationshipsNamespace
                or StrictRelationshipsNamespace
        )
        {
            return element.Name.LocalName switch
            {
                "blip" when IsDrawingMainElement(element) =>
                    attribute.Name.LocalName is "embed" or "link",
                "chart" when element.Name.NamespaceName is TransitionalChartNamespace
                    or StrictChartNamespace => attribute.Name.LocalName == "id",
                "relIds" when element.Name.NamespaceName is TransitionalDiagramNamespace
                    or StrictDiagramNamespace =>
                    attribute.Name.LocalName is "dm" or "lo" or "qs" or "cs",
                "contentPart" when element.Name.NamespaceName == Word2010Namespace =>
                    attribute.Name.LocalName == "id",
                "OLEObject" when element.Name.NamespaceName == OfficeNamespace =>
                    attribute.Name.LocalName == "id",
                "hlinkClick" or "hlinkHover" when IsDrawingMainElement(element) =>
                    attribute.Name.LocalName == "id",
                _ => false,
            };
        }
        return element.Name.NamespaceName == VmlNamespace
            && element.Name.LocalName == "imagedata"
            && attribute.Name.NamespaceName == OfficeNamespace
            && attribute.Name.LocalName == "relid";
    }

    private static WordFigureResourceRole ResourceRole(
        XElement element,
        XAttribute attribute
    )
    {
        if (element.Name.LocalName == "blip")
        {
            return attribute.Name.LocalName == "link"
                ? WordFigureResourceRole.ImageLink
                : WordFigureResourceRole.ImageEmbed;
        }
        if (element.Name.LocalName == "chart") return WordFigureResourceRole.Chart;
        if (element.Name.LocalName == "relIds")
        {
            return attribute.Name.LocalName switch
            {
                "dm" => WordFigureResourceRole.DiagramData,
                "lo" => WordFigureResourceRole.DiagramLayout,
                "qs" => WordFigureResourceRole.DiagramQuickStyle,
                "cs" => WordFigureResourceRole.DiagramColors,
                _ => WordFigureResourceRole.Other,
            };
        }
        if (element.Name.LocalName == "contentPart") return WordFigureResourceRole.ContentPart;
        if (element.Name.LocalName == "OLEObject") return WordFigureResourceRole.EmbeddedObject;
        if (element.Name.LocalName == "imagedata") return WordFigureResourceRole.VmlImage;
        if (element.Name.LocalName.StartsWith("hlink", StringComparison.Ordinal))
        {
            return WordFigureResourceRole.Hyperlink;
        }
        return WordFigureResourceRole.Other;
    }

    private static BoundedVisibleText VisibleParagraphText(
        XElement paragraph,
        int maximumCharacters,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 4_096));
        var characterCount = 0;
        foreach (var element in EnumerateWithCancellation(
            paragraph.Descendants(),
            cancellationToken
        ))
        {
            if (
                !ReferenceEquals(
                    element.Ancestors().FirstOrDefault(item => IsWordElement(item, "p")),
                    paragraph
                )
            )
            {
                continue;
            }
            if (
                IsWordElement(element, "t")
                && !element.Ancestors().Any(IsDeletedRevisionElement)
            )
            {
                AppendBounded(element.Value);
            }
            else if (IsWordElement(element, "tab"))
            {
                AppendBounded("\t");
            }
            else if (IsWordElement(element, "br") || IsWordElement(element, "cr"))
            {
                AppendBounded("\n");
            }
        }
        return new BoundedVisibleText(
            builder.ToString(),
            characterCount,
            characterCount > maximumCharacters
        );

        void AppendBounded(string value)
        {
            characterCount = checked(characterCount + value.Length);
            state.AddMetadata(value.Length);
            var remaining = maximumCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(value.AsSpan(0, Math.Min(remaining, value.Length)));
            }
        }
    }

    private static string? SequenceLabel(WordFieldDefinition field)
    {
        if (!field.InstructionParseComplete || field.Tokens.Count < 2)
        {
            return null;
        }
        return field.Tokens.Skip(1)
            .FirstOrDefault(token => token.Kind is WordFieldTokenKind.Word or WordFieldTokenKind.QuotedText)
            ?.Value;
    }

    private static WordCaptionKind CaptionKind(string? label) => label?.ToUpperInvariant() switch
    {
        "FIGURE" => WordCaptionKind.Figure,
        "TABLE" => WordCaptionKind.Table,
        "EQUATION" => WordCaptionKind.Equation,
        null or "" => WordCaptionKind.Unknown,
        _ => WordCaptionKind.Custom,
    };

    private static bool IsCaptionStyle(string? styleId, WordStyleDefinition? style) =>
        string.Equals(styleId, "Caption", StringComparison.OrdinalIgnoreCase)
        || string.Equals(style?.Name, "Caption", StringComparison.OrdinalIgnoreCase)
        || style?.Aliases.Any(alias =>
            string.Equals(alias, "Caption", StringComparison.OrdinalIgnoreCase)
        ) == true;

    private static string? BoundMetadata(
        string? value,
        int maximum,
        BuildState state,
        out bool truncated
    )
    {
        truncated = false;
        if (value is null)
        {
            return null;
        }
        state.AddMetadata(value.Length);
        if (value.Length <= maximum)
        {
            return value;
        }
        truncated = true;
        return value[..maximum];
    }

    private static string? AttributeByLocal(XElement? element, string localName) =>
        element?.Attributes().FirstOrDefault(attribute =>
            !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == localName
        )?.Value;

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static ulong? ParseUnsigned(string? value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool? ParseOnOff(string? value) => value?.ToLowerInvariant() switch
    {
        "1" or "true" or "on" => true,
        "0" or "false" or "off" => false,
        null => null,
        _ => null,
    };

    private static bool IsWordElement(XElement element) =>
        element.Name.NamespaceName is TransitionalWordNamespace or StrictWordNamespace;

    private static bool IsWordElement(XElement element, string localName) =>
        element.Name.LocalName == localName && IsWordElement(element);

    private static bool IsWordprocessingDrawingElement(XElement element) =>
        element.Name.NamespaceName is TransitionalWordprocessingDrawingNamespace
            or StrictWordprocessingDrawingNamespace;

    private static bool IsWordprocessingDrawingElement(
        XElement element,
        string localName
    ) => element.Name.LocalName == localName && IsWordprocessingDrawingElement(element);

    private static bool IsDrawingMainElement(XElement element) =>
        element.Name.NamespaceName is TransitionalDrawingMainNamespace
            or StrictDrawingMainNamespace;

    private static bool IsDrawingMainElement(XElement element, string localName) =>
        element.Name.LocalName == localName && IsDrawingMainElement(element);

    private static IEnumerable<XElement> EnumerateWithCancellation(
        IEnumerable<XElement> elements,
        CancellationToken cancellationToken
    )
    {
        var index = 0;
        foreach (var element in elements)
        {
            if ((index++ & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            yield return element;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool IsDeletedRevisionElement(XElement element) =>
        IsWordElement(element)
        && element.Name.LocalName is "del" or "moveFrom";

    private static string StableId(string prefix, params string[] values)
    {
        var material = string.Join('\u001f', values);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return prefix + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void RequireSamePackage(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        WordReferenceGraph references,
        WordStyleGraph styles
    )
    {
        if (
            !string.Equals(package.Fingerprint, semantic.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(package.Fingerprint, references.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(package.Fingerprint, styles.PackageFingerprint, StringComparison.Ordinal)
        )
        {
            throw new WordFigureProjectionException(
                "Figure/caption graph inputs do not belong to the same package snapshot."
            );
        }
    }

    private sealed record ResourceDeclaration(
        WordFigureResourceRole Role,
        string RelationshipId,
        int SourceElementOrdinal
    );

    private sealed record StoryLocation(WordStoryKind Kind, WordSemanticNode Root);

    private sealed record ParagraphPosition(
        SemanticNodeId StoryId,
        SemanticNodeId ContainerId,
        SemanticNodeId ParagraphId,
        int SourceElementOrdinal,
        int Index
    );

    private sealed record BoundedVisibleText(
        string Value,
        int CharacterCount,
        bool Truncated
    );

    private sealed class RepresentationDraft(
        string id,
        WordSemanticNode semantic,
        string partUri,
        int sourceElementOrdinal,
        WordStoryKind storyKind,
        SemanticNodeId storyRootNodeId,
        SemanticNodeId paragraphNodeId,
        SemanticNodeId containerNodeId,
        bool isInDeletedContent,
        WordFigureRepresentationKind kind,
        WordFigureObjectKind objectKind,
        string? graphicDataUri,
        string? alternateContentGroupId,
        string? alternateContentBranch,
        ulong? nonVisualDrawingId,
        WordFigurePlacementDefinition placement,
        WordFigureAccessibilityDefinition accessibility,
        IReadOnlyList<WordFigureResourceDefinition> resources,
        IReadOnlyList<string> unmodeledPayloadElements
    )
    {
        public string Id { get; } = id;
        public WordSemanticNode Semantic { get; } = semantic;
        public string PartUri { get; } = partUri;
        public int SourceElementOrdinal { get; } = sourceElementOrdinal;
        public WordStoryKind StoryKind { get; } = storyKind;
        public SemanticNodeId StoryRootNodeId { get; } = storyRootNodeId;
        public SemanticNodeId ParagraphNodeId { get; } = paragraphNodeId;
        public SemanticNodeId ContainerNodeId { get; } = containerNodeId;
        public bool IsInDeletedContent { get; } = isInDeletedContent;
        public WordFigureRepresentationKind Kind { get; } = kind;
        public WordFigureObjectKind ObjectKind { get; } = objectKind;
        public string? GraphicDataUri { get; } = graphicDataUri;
        public string? AlternateContentGroupId { get; } = alternateContentGroupId;
        public string? AlternateContentBranch { get; } = alternateContentBranch;
        public ulong? NonVisualDrawingId { get; } = nonVisualDrawingId;
        public WordFigurePlacementDefinition Placement { get; } = placement;
        public WordFigureAccessibilityDefinition Accessibility { get; } = accessibility;
        public IReadOnlyList<WordFigureResourceDefinition> Resources { get; } = resources;
        public IReadOnlyList<string> UnmodeledPayloadElements { get; } = unmodeledPayloadElements;

        public WordFigureRepresentationDefinition Freeze() => new(
            Id,
            Semantic.Id,
            PartUri,
            SourceElementOrdinal,
            Semantic.SourcePath,
            StoryKind,
            StoryRootNodeId,
            ParagraphNodeId,
            ContainerNodeId,
            IsInDeletedContent,
            Kind,
            ObjectKind,
            GraphicDataUri,
            AlternateContentGroupId,
            AlternateContentBranch,
            NonVisualDrawingId,
            Placement,
            Accessibility,
            Resources,
            UnmodeledPayloadElements
        );
    }

    private sealed class FigureDraft(
        string id,
        RepresentationDraft primary,
        IReadOnlyList<RepresentationDraft> representations,
        WordFigureRepresentationSelectionBasis representationSelectionBasis
    )
    {
        public string Id { get; } = id;
        public RepresentationDraft Primary { get; } = primary;
        public IReadOnlyList<RepresentationDraft> Representations { get; } = representations;
        public WordFigureRepresentationSelectionBasis RepresentationSelectionBasis { get; } =
            representationSelectionBasis;
        public bool HasAuthoritativePrimary =>
            RepresentationSelectionBasis
                == WordFigureRepresentationSelectionBasis.SingleDeclaredRepresentation;
        public string? SelectedCaptionId { get; set; }

        public WordFigureDefinition Freeze() => new(
            Id,
            HasAuthoritativePrimary ? Primary.ObjectKind : WordFigureObjectKind.Unknown,
            Primary.StoryKind,
            Primary.StoryRootNodeId,
            Primary.ParagraphNodeId,
            Primary.ContainerNodeId,
            Primary.PartUri,
            Primary.SourceElementOrdinal,
            Representations.All(item => item.IsInDeletedContent),
            HasAuthoritativePrimary ? Primary.Id : null,
            RepresentationSelectionBasis,
            Primary.AlternateContentGroupId,
            Representations.Select(item => item.Freeze()).ToArray(),
            Representations.SelectMany(item => item.Resources)
                .DistinctBy(item => item.Id)
                .OrderBy(item => item.SourceElementOrdinal)
                .ToArray(),
            SelectedCaptionId
        );
    }

    private sealed class CaptionDraft(
        string id,
        WordSemanticNode paragraph,
        string partUri,
        int sourceElementOrdinal,
        WordStoryKind storyKind,
        SemanticNodeId storyRootNodeId,
        SemanticNodeId containerNodeId,
        bool isInDeletedContent,
        string? paragraphStyleId,
        string? paragraphStyleName,
        bool hasCaptionStyleEvidence,
        IReadOnlyList<string> sequenceFieldIds,
        IReadOnlyList<string> sequenceLabels,
        string? primaryLabel,
        WordCaptionKind kind,
        string? text,
        int textCharacterCount,
        bool textTruncated,
        string? sequenceResultText,
        int sequenceResultCharacterCount,
        bool sequenceResultTruncated
    )
    {
        public string Id { get; } = id;
        public WordSemanticNode Paragraph { get; } = paragraph;
        public string PartUri { get; } = partUri;
        public int SourceElementOrdinal { get; } = sourceElementOrdinal;
        public WordStoryKind StoryKind { get; } = storyKind;
        public SemanticNodeId StoryRootNodeId { get; } = storyRootNodeId;
        public SemanticNodeId ContainerNodeId { get; } = containerNodeId;
        public bool IsInDeletedContent { get; } = isInDeletedContent;
        public string? ParagraphStyleId { get; } = paragraphStyleId;
        public string? ParagraphStyleName { get; } = paragraphStyleName;
        public bool HasCaptionStyleEvidence { get; } = hasCaptionStyleEvidence;
        public IReadOnlyList<string> SequenceFieldIds { get; } = sequenceFieldIds;
        public IReadOnlyList<string> SequenceLabels { get; } = sequenceLabels;
        public string? PrimaryLabel { get; } = primaryLabel;
        public WordCaptionKind Kind { get; } = kind;
        public string? Text { get; } = text;
        public int TextCharacterCount { get; } = textCharacterCount;
        public bool TextTruncated { get; } = textTruncated;
        public string? SequenceResultText { get; } = sequenceResultText;
        public int SequenceResultCharacterCount { get; } = sequenceResultCharacterCount;
        public bool SequenceResultTruncated { get; } = sequenceResultTruncated;
        public string? SelectedFigureId { get; set; }

        public WordCaptionDefinition Freeze() => new(
            Id,
            Paragraph.Id,
            PartUri,
            SourceElementOrdinal,
            Paragraph.SourcePath,
            StoryKind,
            StoryRootNodeId,
            ContainerNodeId,
            IsInDeletedContent,
            ParagraphStyleId,
            ParagraphStyleName,
            HasCaptionStyleEvidence,
            SequenceFieldIds,
            SequenceLabels,
            PrimaryLabel,
            Kind,
            Text,
            TextCharacterCount,
            TextTruncated,
            SequenceResultText,
            SequenceResultCharacterCount,
            SequenceResultTruncated,
            SelectedFigureId
        );
    }

    private sealed class AssociationDraft(
        string id,
        FigureDraft figure,
        CaptionDraft caption,
        WordFigureCaptionDirection direction,
        int paragraphDistance,
        int score,
        bool labelCompatible
    )
    {
        public string Id { get; } = id;
        public FigureDraft Figure { get; } = figure;
        public CaptionDraft Caption { get; } = caption;
        public WordFigureCaptionDirection Direction { get; } = direction;
        public int ParagraphDistance { get; } = paragraphDistance;
        public int Score { get; } = score;
        public bool LabelCompatible { get; } = labelCompatible;
        public WordFigureCaptionAssociationStatus Status { get; set; } =
            WordFigureCaptionAssociationStatus.Candidate;

        public WordFigureCaptionAssociation Freeze() => new(
            Id,
            Figure.Id,
            Caption.Id,
            Status,
            Score switch
            {
                >= 95 => WordFigureCaptionConfidence.VeryStrong,
                >= 85 => WordFigureCaptionConfidence.Strong,
                >= 70 => WordFigureCaptionConfidence.Moderate,
                _ => WordFigureCaptionConfidence.Weak,
            },
            Direction,
            ParagraphDistance,
            Score,
            true,
            Caption.SequenceFieldIds.Count != 0,
            Caption.HasCaptionStyleEvidence,
            LabelCompatible
        );
    }

    private sealed class BuildState
    {
        private readonly WordFigureCaptionGraphOptions _options;
        private readonly Dictionary<(string PartUri, int Ordinal, WordSemanticNodeKind Kind), WordSemanticNode>
            _semanticBySource;
        private readonly Dictionary<(SemanticNodeId StoryId, SemanticNodeId ContainerId), List<ParagraphPosition>>
            _paragraphs = [];
        private readonly Dictionary<
            string,
            IReadOnlyDictionary<string, IReadOnlyList<OpcRelationship>>
        > _relationshipsByPart = new(StringComparer.Ordinal);
        private readonly List<WordFigureIssue> _issues = [];
        private readonly List<FigureDraft> _figures = [];
        private readonly List<AssociationDraft> _associations = [];
        private long _metadataCharacters;
        private bool _issuesTruncated;

        public BuildState(
            WordFigureCaptionGraphOptions options,
            OpcPackageSnapshot package,
            WordSemanticDocument semanticDocument,
            WordReferenceGraph referenceGraph,
            WordStyleGraph styleGraph
        )
        {
            _options = options;
            Package = package;
            SemanticDocument = semanticDocument;
            ReferenceGraph = referenceGraph;
            StyleGraph = styleGraph;
            _semanticBySource = semanticDocument.Nodes
                .GroupBy(node => (node.SourcePartUri, node.SourceElementOrdinal, node.Kind))
                .ToDictionary(group => group.Key, group => group.First());
        }

        public OpcPackageSnapshot Package { get; }
        public WordSemanticDocument SemanticDocument { get; }
        public WordReferenceGraph ReferenceGraph { get; }
        public WordStyleGraph StyleGraph { get; }
        public List<RepresentationDraft> Representations { get; } = [];
        public List<CaptionDraft> CaptionDrafts { get; } = [];
        public int ResourceCount { get; set; }
        public long ParsedXmlBytes { get; private set; }
        public int ParsedXmlElements { get; private set; }

        public IReadOnlyDictionary<string, IReadOnlyList<OpcRelationship>> RelationshipsById(
            string partUri
        )
        {
            if (_relationshipsByPart.TryGetValue(partUri, out var cached))
            {
                return cached;
            }
            var indexed = Package.RelationshipsFrom(partUri)
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<OpcRelationship>)group.ToArray(),
                    StringComparer.Ordinal
                );
            _relationshipsByPart.Add(partUri, indexed);
            return indexed;
        }

        public WordSemanticNode RequiredSemantic(
            string partUri,
            int ordinal,
            WordSemanticNodeKind kind
        )
        {
            if (_semanticBySource.TryGetValue((partUri, ordinal, kind), out var node))
            {
                return node;
            }
            throw new WordFigureProjectionException(
                $"Source {partUri}#{ordinal} has no matching {kind} semantic node."
            );
        }

        public WordSemanticNode RequiredAncestor(
            WordSemanticNode node,
            WordSemanticNodeKind kind
        )
        {
            WordSemanticNode? current = node;
            while (current is not null)
            {
                if (current.Kind == kind)
                {
                    return current;
                }
                current = current.ParentId is { } parentId
                    && SemanticDocument.TryGetNode(parentId, out var parent)
                        ? parent
                        : null;
            }
            throw new WordFigureProjectionException(
                $"Semantic node '{node.Id}' has no {kind} ancestor."
            );
        }

        public StoryLocation StoryFor(WordSemanticNode node)
        {
            WordSemanticNode? current = node;
            while (current is not null)
            {
                var kind = current.Kind switch
                {
                    WordSemanticNodeKind.TextBox => WordStoryKind.TextBox,
                    WordSemanticNodeKind.Footnote => WordStoryKind.Footnote,
                    WordSemanticNodeKind.Endnote => WordStoryKind.Endnote,
                    WordSemanticNodeKind.Comment => WordStoryKind.Comment,
                    WordSemanticNodeKind.GlossaryEntry => WordStoryKind.GlossaryEntry,
                    WordSemanticNodeKind.Header => WordStoryKind.Header,
                    WordSemanticNodeKind.Footer => WordStoryKind.Footer,
                    WordSemanticNodeKind.Document => WordStoryKind.Main,
                    _ => (WordStoryKind?)null,
                };
                if (kind is not null)
                {
                    return new StoryLocation(kind.Value, current);
                }
                current = current.ParentId is { } parentId
                    && SemanticDocument.TryGetNode(parentId, out var parent)
                        ? parent
                        : null;
            }
            return new StoryLocation(WordStoryKind.Other, SemanticDocument.Root);
        }

        public WordSemanticNode ContainerFor(WordSemanticNode node, WordSemanticNode storyRoot)
        {
            WordSemanticNode? current = node;
            while (current is not null && current.Id != storyRoot.Id)
            {
                if (current.Kind == WordSemanticNodeKind.TableCell)
                {
                    return current;
                }
                current = current.ParentId is { } parentId
                    && SemanticDocument.TryGetNode(parentId, out var parent)
                        ? parent
                        : null;
            }
            return storyRoot;
        }

        public void AddParagraphPosition(
            SemanticNodeId storyId,
            SemanticNodeId containerId,
            SemanticNodeId paragraphId,
            int ordinal
        )
        {
            var key = (storyId, containerId);
            if (!_paragraphs.TryGetValue(key, out var list))
            {
                list = [];
                _paragraphs.Add(key, list);
            }
            list.Add(new ParagraphPosition(storyId, containerId, paragraphId, ordinal, list.Count));
        }

        public void AddParsedPart(long bytes, int elements)
        {
            ParsedXmlBytes = checked(ParsedXmlBytes + bytes);
            ParsedXmlElements = checked(ParsedXmlElements + elements);
            if (ParsedXmlBytes > _options.MaxAggregateXmlBytes)
            {
                throw new WordFigureLimitException(
                    $"Aggregate story XML exceeds {_options.MaxAggregateXmlBytes} bytes."
                );
            }
            if (ParsedXmlElements > _options.MaxAggregateElements)
            {
                throw new WordFigureLimitException(
                    $"Aggregate story XML exceeds {_options.MaxAggregateElements} elements."
                );
            }
        }

        public void EnsureCanParse(int bytes)
        {
            if (ParsedXmlBytes > _options.MaxAggregateXmlBytes - bytes)
            {
                throw new WordFigureLimitException(
                    $"Aggregate story XML exceeds {_options.MaxAggregateXmlBytes} bytes."
                );
            }
        }

        public void AddMetadata(int characters)
        {
            _metadataCharacters = checked(_metadataCharacters + characters);
            if (_metadataCharacters > _options.MaxMetadataCharacters)
            {
                throw new WordFigureLimitException(
                    $"Figure/caption metadata exceeds {_options.MaxMetadataCharacters} characters."
                );
            }
        }

        public void GroupRepresentations()
        {
            foreach (
                var group in Representations.GroupBy(
                    item => item.AlternateContentGroupId ?? item.Id,
                    StringComparer.Ordinal
                )
            )
            {
                if (_figures.Count >= _options.MaxFigures)
                {
                    throw new WordFigureLimitException(
                        $"Figure count exceeds {_options.MaxFigures}."
                    );
                }
                var ordered = group.OrderBy(item => item.AlternateContentBranch switch
                    {
                        "Choice" => 0,
                        null => 1,
                        "Fallback" => 2,
                        _ => 3,
                    })
                    .ThenBy(item => item.SourceElementOrdinal)
                    .ToArray();
                var primary = ordered[0];
                var figureId = primary.AlternateContentGroupId is null
                    ? StableId(
                        "wdfig_",
                        Package.Fingerprint,
                        primary.Semantic.Id.Value
                    )
                    : StableId(
                        "wdfig_",
                        Package.Fingerprint,
                        primary.AlternateContentGroupId
                    );
                if (ordered.Any(item =>
                    item.ParagraphNodeId != primary.ParagraphNodeId
                    || item.ContainerNodeId != primary.ContainerNodeId
                    || item.StoryRootNodeId != primary.StoryRootNodeId
                ))
                {
                    AddIssue(
                        "FIGURE_ALTERNATE_REPRESENTATION_LOCATION_MISMATCH",
                        WordFigureIssueSeverity.Error,
                        "AlternateContent representations do not occupy the same semantic paragraph/container.",
                        primary.PartUri,
                        primary.SourceElementOrdinal,
                        figureId
                    );
                }
                var selectionBasis = primary.AlternateContentGroupId is null
                    ? WordFigureRepresentationSelectionBasis.SingleDeclaredRepresentation
                    : ordered.Any(item => item.AlternateContentBranch == "Choice")
                        ? WordFigureRepresentationSelectionBasis.AlternateContentChoicePresentNotEvaluated
                        : ordered.Any(item => item.AlternateContentBranch == "Fallback")
                            ? WordFigureRepresentationSelectionBasis.AlternateContentFallbackOnly
                            : WordFigureRepresentationSelectionBasis.AlternateContentUnclassified;
                _figures.Add(new FigureDraft(figureId, primary, ordered, selectionBasis));
            }
            var figureIdByRepresentationId = _figures.SelectMany(figure =>
                figure.Representations.Select(representation => (
                    RepresentationId: representation.Id,
                    FigureId: figure.Id
                ))
            ).ToDictionary(
                item => item.RepresentationId,
                item => item.FigureId,
                StringComparer.Ordinal
            );
            for (var index = 0; index < _issues.Count; index++)
            {
                var issue = _issues[index];
                if (
                    issue.FigureId is { } representationId
                    && figureIdByRepresentationId.TryGetValue(representationId, out var figureId)
                )
                {
                    _issues[index] = issue with { FigureId = figureId };
                }
            }
            foreach (
                var duplicate in Representations
                    .Where(item => item.NonVisualDrawingId is not null)
                    .GroupBy(item => (item.PartUri, item.NonVisualDrawingId))
                    .Where(group => group.Count() > 1)
            )
            {
                foreach (var representation in duplicate)
                {
                    AddIssue(
                        "FIGURE_DOC_PROPERTIES_ID_DUPLICATE",
                        WordFigureIssueSeverity.Warning,
                        "Drawing docPr id is duplicated within the source part.",
                        representation.PartUri,
                        representation.SourceElementOrdinal,
                        figureId: figureIdByRepresentationId.GetValueOrDefault(
                            representation.Id
                        )
                    );
                }
            }
        }

        public void BuildAssociations()
        {
            var positions = _paragraphs.Values.SelectMany(items => items)
                .ToDictionary(item => item.ParagraphId);
            var captionsByLocation = new Dictionary<
                (SemanticNodeId StoryId, SemanticNodeId ContainerId),
                List<(int ParagraphIndex, CaptionDraft Caption)>
            >();
            foreach (var caption in CaptionDrafts.Where(item => !item.IsInDeletedContent))
            {
                if (!positions.TryGetValue(caption.Paragraph.Id, out var captionPosition))
                {
                    continue;
                }
                var key = (caption.StoryRootNodeId, caption.ContainerNodeId);
                if (!captionsByLocation.TryGetValue(key, out var candidates))
                {
                    candidates = [];
                    captionsByLocation.Add(key, candidates);
                }
                candidates.Add((captionPosition.Index, caption));
            }
            foreach (var candidates in captionsByLocation.Values)
            {
                candidates.Sort((left, right) =>
                {
                    var byIndex = left.ParagraphIndex.CompareTo(right.ParagraphIndex);
                    return byIndex != 0
                        ? byIndex
                        : string.CompareOrdinal(left.Caption.Id, right.Caption.Id);
                });
            }
            foreach (var figure in _figures)
            {
                if (figure.Representations.All(item => item.IsInDeletedContent))
                {
                    continue;
                }
                if (!positions.TryGetValue(figure.Primary.ParagraphNodeId, out var figurePosition))
                {
                    continue;
                }
                if (!captionsByLocation.TryGetValue(
                    (figure.Primary.StoryRootNodeId, figure.Primary.ContainerNodeId),
                    out var locationCaptions
                ))
                {
                    continue;
                }
                var lowerBound = (long)figurePosition.Index
                    - _options.MaxCaptionParagraphDistance;
                for (
                    var candidateIndex = LowerBound(locationCaptions, lowerBound);
                    candidateIndex < locationCaptions.Count;
                    candidateIndex++
                )
                {
                    var candidate = locationCaptions[candidateIndex];
                    var signedDistance = candidate.ParagraphIndex - figurePosition.Index;
                    var distance = Math.Abs(signedDistance);
                    if (distance > _options.MaxCaptionParagraphDistance)
                    {
                        break;
                    }
                    var caption = candidate.Caption;
                    var compatible = caption.Kind is not WordCaptionKind.Table
                        and not WordCaptionKind.Equation;
                    if (!compatible)
                    {
                        continue;
                    }
                    var direction = signedDistance switch
                    {
                        0 => WordFigureCaptionDirection.SameParagraph,
                        < 0 => WordFigureCaptionDirection.CaptionBeforeFigure,
                        _ => WordFigureCaptionDirection.CaptionAfterFigure,
                    };
                    var score = direction switch
                    {
                        WordFigureCaptionDirection.SameParagraph => 95,
                        WordFigureCaptionDirection.CaptionAfterFigure when distance == 1 => 90,
                        WordFigureCaptionDirection.CaptionBeforeFigure when distance == 1 => 85,
                        WordFigureCaptionDirection.CaptionAfterFigure => 68,
                        _ => 63,
                    };
                    if (caption.HasCaptionStyleEvidence && caption.SequenceFieldIds.Count != 0)
                    {
                        score += 5;
                    }
                    else if (caption.SequenceFieldIds.Count == 0)
                    {
                        score -= 15;
                    }
                    if (caption.Kind == WordCaptionKind.Figure)
                    {
                        score += 3;
                    }
                    score = Math.Clamp(score, 0, 100);
                    if (_associations.Count >= _options.MaxAssociations)
                    {
                        throw new WordFigureLimitException(
                            $"Figure-caption association count exceeds {_options.MaxAssociations}."
                        );
                    }
                    _associations.Add(new AssociationDraft(
                        StableId("wdfca_", figure.Id, caption.Id),
                        figure,
                        caption,
                        direction,
                        distance,
                        score,
                        compatible
                    ));
                }
            }

            var bestForCaption = UniqueBest(_associations, item => item.Caption.Id);
            var bestForFigure = UniqueBest(_associations, item => item.Figure.Id);
            var maximumForCaption = _associations.GroupBy(item => item.Caption.Id)
                .ToDictionary(group => group.Key, group => group.Max(item => item.Score));
            var maximumForFigure = _associations.GroupBy(item => item.Figure.Id)
                .ToDictionary(group => group.Key, group => group.Max(item => item.Score));
            foreach (var association in _associations)
            {
                var captionBest = bestForCaption.GetValueOrDefault(association.Caption.Id);
                var figureBest = bestForFigure.GetValueOrDefault(association.Figure.Id);
                if (ReferenceEquals(captionBest, association) && ReferenceEquals(figureBest, association)
                    && association.Score >= 70)
                {
                    association.Status = WordFigureCaptionAssociationStatus.Selected;
                    association.Figure.SelectedCaptionId = association.Caption.Id;
                    association.Caption.SelectedFigureId = association.Figure.Id;
                }
                else if (
                    (
                        captionBest is null
                        && association.Score == maximumForCaption[association.Caption.Id]
                    )
                    || (
                        figureBest is null
                        && association.Score == maximumForFigure[association.Figure.Id]
                    )
                )
                {
                    association.Status = WordFigureCaptionAssociationStatus.Ambiguous;
                }
            }
            var ambiguousFigureIds = _associations.Where(item =>
                item.Status == WordFigureCaptionAssociationStatus.Ambiguous
            )
                .GroupBy(item => item.Figure.Id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            var ambiguousCaptionIds = _associations.Where(item =>
                item.Status == WordFigureCaptionAssociationStatus.Ambiguous
            )
                .GroupBy(item => item.Caption.Id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var figure in _figures.Where(item => ambiguousFigureIds.Contains(item.Id)))
            {
                AddIssue(
                    "FIGURE_CAPTION_ASSOCIATION_AMBIGUOUS",
                    WordFigureIssueSeverity.Warning,
                    "Figure has multiple equally plausible nearby captions; no relation was asserted.",
                    figure.Primary.PartUri,
                    figure.Primary.SourceElementOrdinal,
                    figureId: figure.Id
                );
            }
            foreach (var caption in CaptionDrafts.Where(item =>
                ambiguousCaptionIds.Contains(item.Id)
            ))
            {
                AddIssue(
                    "FIGURE_CAPTION_ASSOCIATION_AMBIGUOUS",
                    WordFigureIssueSeverity.Warning,
                    "Caption has multiple equally plausible nearby figures; no relation was asserted.",
                    caption.PartUri,
                    caption.SourceElementOrdinal,
                    captionId: caption.Id
                );
            }
        }

        private static int LowerBound(
            IReadOnlyList<(int ParagraphIndex, CaptionDraft Caption)> candidates,
            long paragraphIndex
        )
        {
            var low = 0;
            var high = candidates.Count;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (candidates[middle].ParagraphIndex < paragraphIndex)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low;
        }

        public void AddCompletenessIssues()
        {
            foreach (var figure in _figures.Where(item => item.SelectedCaptionId is null))
            {
                AddIssue(
                    "FIGURE_CAPTION_NOT_RESOLVED",
                    WordFigureIssueSeverity.Info,
                    "Figure has no uniquely selected caption association.",
                    figure.Primary.PartUri,
                    figure.Primary.SourceElementOrdinal,
                    figure.Id
                );
            }
            foreach (var caption in CaptionDrafts.Where(item => item.SelectedFigureId is null))
            {
                AddIssue(
                    "CAPTION_FIGURE_NOT_RESOLVED",
                    WordFigureIssueSeverity.Info,
                    "Caption candidate has no uniquely selected figure association.",
                    caption.PartUri,
                    caption.SourceElementOrdinal,
                    captionId: caption.Id
                );
            }
        }

        public void AddIssue(
            string code,
            WordFigureIssueSeverity severity,
            string message,
            string? partUri = null,
            int? sourceElementOrdinal = null,
            string? figureId = null,
            string? captionId = null,
            string? relationshipId = null
        )
        {
            if (_issues.Count >= _options.MaxIssues)
            {
                _issuesTruncated = true;
                return;
            }
            _issues.Add(new WordFigureIssue(
                code,
                severity,
                message,
                partUri,
                sourceElementOrdinal,
                figureId,
                captionId,
                relationshipId
            ));
        }

        public WordFigureCaptionGraph Freeze(string fingerprint, string mainPartUri) => new(
            fingerprint,
            mainPartUri,
            _figures.OrderBy(item => item.Primary.PartUri, StringComparer.Ordinal)
                .ThenBy(item => item.Primary.SourceElementOrdinal)
                .Select(item => item.Freeze())
                .ToArray(),
            CaptionDrafts.OrderBy(item => item.PartUri, StringComparer.Ordinal)
                .ThenBy(item => item.SourceElementOrdinal)
                .Select(item => item.Freeze())
                .ToArray(),
            _associations.OrderByDescending(item => item.Score)
                .ThenBy(item => item.Figure.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Caption.Id, StringComparer.Ordinal)
                .Select(item => item.Freeze())
                .ToArray(),
            _issues,
            _issuesTruncated,
            ParsedXmlBytes,
            ParsedXmlElements
        );

        private static IReadOnlyDictionary<string, AssociationDraft?> UniqueBest(
            IEnumerable<AssociationDraft> associations,
            Func<AssociationDraft, string> keySelector
        ) => associations.GroupBy(keySelector, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group.OrderByDescending(item => item.Score).ToArray();
                    return ordered.Length == 1 || ordered[0].Score > ordered[1].Score
                        ? ordered[0]
                        : null;
                },
                StringComparer.Ordinal
            );
    }
}

public class WordFigureException : Exception
{
    public WordFigureException(string message)
        : base(message) { }

    public WordFigureException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordFigureLimitException : WordFigureException
{
    public WordFigureLimitException(string message)
        : base(message) { }
}

public sealed class WordFigureProjectionException : WordFigureException
{
    public WordFigureProjectionException(string message)
        : base(message) { }

    public WordFigureProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}
