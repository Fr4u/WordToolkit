using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordFigureShapeNodeKind
{
    Shape,
    Group,
    Picture,
    GraphicFrame,
    ContentPart,
}

public enum WordFigureShapeGeometryKind
{
    Preset,
    Custom,
    VmlPath,
    Ambiguous,
}

public sealed record WordFigureShapeTransformDefinition(
    long? OffsetXEmu,
    long? OffsetYEmu,
    long? WidthEmu,
    long? HeightEmu,
    long? ChildOffsetXEmu,
    long? ChildOffsetYEmu,
    long? ChildWidthEmu,
    long? ChildHeightEmu,
    long? RotationSixtyThousandthsOfDegree,
    bool? FlipHorizontal,
    bool? FlipVertical
);

public sealed record WordFigureShapeFormulaPointDefinition(string? X, string? Y);

public sealed record WordFigureShapePathCommandDefinition(
    string Kind,
    IReadOnlyList<WordFigureShapeFormulaPointDefinition> Points,
    string? WidthRadius,
    string? HeightRadius,
    string? StartAngle,
    string? SweepAngle
);

public sealed record WordFigureShapePathDefinition(
    long? Width,
    long? Height,
    string? FillMode,
    bool? Stroke,
    bool? ExtrusionAllowed,
    IReadOnlyList<WordFigureShapePathCommandDefinition> Commands
);

public sealed record WordFigureShapeGeometryDefinition(
    WordFigureShapeGeometryKind Kind,
    string? Preset,
    bool PresetRecognized,
    int AdjustmentCount,
    int GuideCount,
    int HandleCount,
    int ConnectionSiteCount,
    bool HasTextRectangle,
    IReadOnlyList<WordFigureShapePathDefinition> Paths,
    int VmlPathCharacterCount,
    string? VmlPathSha256
);

public sealed record WordFigureShapeLineDefinition(
    long? WidthEmu,
    string? Cap,
    string? Compound,
    string? Alignment,
    string? FillKind,
    string? Dash,
    string? Join,
    string? HeadEnd,
    string? TailEnd
);

public sealed record WordFigureShapeEffectsDefinition(
    bool HasEffectList,
    bool HasEffectDag,
    IReadOnlyList<string> EffectKinds
);

public sealed record WordFigureShapeTextFlowDefinition(
    bool HasTextBoxContent,
    int ParagraphCount,
    int RunCount,
    int CharacterCount,
    string? Text,
    bool TextTruncated,
    string? Anchor,
    string? VerticalFlow,
    string? Wrap,
    long? LeftInsetEmu,
    long? TopInsetEmu,
    long? RightInsetEmu,
    long? BottomInsetEmu,
    int? ColumnCount,
    long? ColumnSpacingEmu,
    bool? RightToLeftColumns,
    bool? FromWordArt,
    bool? AnchorCenter,
    bool? ForceAntiAlias,
    bool? Upright,
    bool? CompatibleLineSpacing,
    long? RotationSixtyThousandthsOfDegree,
    string? AutoFitKind,
    long? LinkedTextBoxId,
    int? LinkedTextBoxSequence
);

public sealed record WordFigureShapeNodeDefinition(
    string Id,
    string? ParentId,
    WordFigureShapeNodeKind Kind,
    int Depth,
    int SourceElementOrdinal,
    string? Name,
    bool NameTruncated,
    WordFigureShapeTransformDefinition? Transform,
    WordFigureShapeGeometryDefinition? Geometry,
    string? FillKind,
    WordFigureShapeLineDefinition? Line,
    WordFigureShapeEffectsDefinition? Effects,
    WordFigureShapeTextFlowDefinition? TextFlow,
    IReadOnlyList<WordFigureShapeNodeDefinition> Children
);

public sealed record WordFigureShapeModelDefinition(
    IReadOnlyList<WordFigureShapeNodeDefinition> Roots,
    int NodeCount,
    int GroupCount,
    int ShapeCount,
    int PictureCount,
    int PathCount,
    int PathCommandCount,
    int PathPointCount,
    int EffectCount,
    int TextCharacterCount
);

public sealed partial class WordFigureCaptionGraphBuilder
{
    private const string WordprocessingGroupNamespace =
        "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
    private const string WordprocessingCanvasNamespace =
        "http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas";

    private static readonly IReadOnlySet<string> KnownPresetShapeTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "accentBorderCallout1", "accentBorderCallout2", "accentBorderCallout3",
            "accentCallout1", "accentCallout2", "accentCallout3", "actionButtonBackPrevious",
            "actionButtonBeginning", "actionButtonBlank", "actionButtonDocument",
            "actionButtonEnd", "actionButtonForwardNext", "actionButtonHelp", "actionButtonHome",
            "actionButtonInformation", "actionButtonMovie", "actionButtonReturn",
            "actionButtonSound", "arc", "bentArrow", "bentConnector2", "bentConnector3",
            "bentConnector4", "bentConnector5", "bentUpArrow", "bevel", "blockArc",
            "bracePair", "bracketPair", "callout1", "callout2", "callout3", "can",
            "chartPlus", "chartStar", "chartX", "chevron", "chord", "circularArrow", "cloud",
            "cloudCallout", "corner", "cornerTabs", "cube", "curvedConnector2",
            "curvedConnector3", "curvedConnector4", "curvedConnector5", "curvedDownArrow",
            "curvedLeftArrow", "curvedRightArrow", "curvedUpArrow", "decagon", "diagStripe",
            "diamond", "dodecagon", "donut", "doubleWave", "downArrow", "downArrowCallout",
            "ellipse", "ellipseRibbon", "ellipseRibbon2", "flowChartAlternateProcess",
            "flowChartCollate", "flowChartConnector", "flowChartDecision", "flowChartDelay",
            "flowChartDisplay", "flowChartDocument", "flowChartExtract", "flowChartInputOutput",
            "flowChartInternalStorage", "flowChartMagneticDisk", "flowChartMagneticDrum",
            "flowChartMagneticTape", "flowChartManualInput", "flowChartManualOperation",
            "flowChartMerge", "flowChartMultidocument", "flowChartOfflineStorage",
            "flowChartOffpageConnector", "flowChartOnlineStorage", "flowChartOr",
            "flowChartPredefinedProcess", "flowChartPreparation", "flowChartProcess",
            "flowChartPunchedCard", "flowChartPunchedTape", "flowChartSort",
            "flowChartSummingJunction", "flowChartTerminator", "foldedCorner", "frame", "funnel",
            "gear6", "gear9", "halfFrame", "heart", "heptagon", "hexagon", "homePlate",
            "horizontalScroll", "irregularSeal1", "irregularSeal2", "leftArrow",
            "leftArrowCallout", "leftBrace", "leftBracket", "leftCircularArrow",
            "leftRightArrow", "leftRightArrowCallout", "leftRightCircularArrow",
            "leftRightRibbon", "leftRightUpArrow", "leftUpArrow", "lightningBolt", "line",
            "lineInv", "mathDivide", "mathEqual", "mathMinus", "mathMultiply", "mathNotEqual",
            "mathPlus", "moon", "nonIsoscelesTrapezoid", "noSmoking", "notchedRightArrow",
            "octagon", "parallelogram", "pentagon", "pie", "pieWedge", "plaque",
            "plaqueTabs", "plus", "quadArrow", "quadArrowCallout", "rect", "ribbon", "ribbon2",
            "rightArrow", "rightArrowCallout", "rightBrace", "rightBracket", "round1Rect",
            "round2DiagRect", "round2SameRect", "roundRect", "rtTriangle", "smileyFace",
            "snip1Rect", "snip2DiagRect", "snip2SameRect", "snipRoundRect", "squareTabs",
            "star4", "star5", "star6", "star7", "star8", "star10", "star12", "star16",
            "star24", "star32", "stripedRightArrow", "sun", "swooshArrow", "teardrop",
            "trapezoid", "triangle", "upArrow", "upArrowCallout", "upDownArrow",
            "upDownArrowCallout", "uturnArrow", "verticalScroll", "wave",
            "wedgeEllipseCallout", "wedgeRectCallout", "wedgeRoundRectCallout",
        };

    private static readonly IReadOnlySet<string> ShapeEffectKinds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "alphaBiLevel", "alphaCeiling", "alphaFloor", "alphaInv", "alphaMod",
            "alphaModFix", "alphaOutset", "alphaRepl", "biLevel", "blend", "blur",
            "clrChange", "clrRepl", "duotone", "effect", "fill", "fillOverlay", "glow",
            "grayscl", "hsl", "innerShdw", "lum", "outerShdw", "prstShdw", "reflection",
            "relOff", "softEdge", "tint", "xfrm",
        };

    private static readonly IReadOnlySet<string> ShapeFillKinds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill",
        };

    private static readonly IReadOnlySet<string> ShapePathFillModes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "none", "norm", "lighten", "lightenLess", "darken", "darkenLess",
        };

    private static readonly IReadOnlySet<string> ShapeTextAnchors =
        new HashSet<string>(StringComparer.Ordinal) { "b", "ctr", "dist", "just", "t" };

    private static readonly IReadOnlySet<string> ShapeTextVerticalFlows =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "eaVert", "horz", "mongolianVert", "vert", "vert270", "wordArtVert",
            "wordArtVertRtl",
        };

    private static readonly IReadOnlySet<string> ShapeTextWrapModes =
        new HashSet<string>(StringComparer.Ordinal) { "none", "square" };

    private static readonly IReadOnlySet<string> ShapeLineCaps =
        new HashSet<string>(StringComparer.Ordinal) { "flat", "rnd", "sq" };

    private static readonly IReadOnlySet<string> ShapeLineCompounds =
        new HashSet<string>(StringComparer.Ordinal) { "dbl", "sng", "thickThin", "thinThick", "tri" };

    private static readonly IReadOnlySet<string> ShapeLineAlignments =
        new HashSet<string>(StringComparer.Ordinal) { "ctr", "in" };

    private static readonly IReadOnlySet<string> ShapeLineDashes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "dash", "dashDot", "dot", "lgDash", "lgDashDot", "lgDashDotDot", "solid",
            "sysDash", "sysDashDot", "sysDashDotDot", "sysDot",
        };

    private static readonly IReadOnlySet<string> ShapeLineEndTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "arrow", "diamond", "none", "oval", "stealth", "triangle",
        };

    private WordFigureShapeModelDefinition? ParseShapeModel(
        string partUri,
        XElement representation,
        WordFigureObjectKind objectKind,
        LosslessXmlDocument source,
        BuildState state,
        int representationOrdinal,
        CancellationToken cancellationToken
    )
    {
        if (objectKind != WordFigureObjectKind.Shape)
        {
            return null;
        }

        var roots = EnumerateWithCancellation(representation.DescendantsAndSelf(), cancellationToken)
            .Where(IsShapeModelRootElement)
            .Where(candidate => !candidate.Ancestors().TakeWhile(item => item != representation)
                .Any(IsShapeNodeElement))
            .ToArray();
        if (roots.Length == 0)
        {
            state.AddIssue(
                "FIGURE_SHAPE_ROOT_MISSING",
                WordFigureIssueSeverity.Warning,
                "Shape-classified representation has no supported DrawingML or VML shape root.",
                partUri,
                representationOrdinal
            );
            return null;
        }

        var summary = new ShapeModelSummary();
        var parsedRoots = roots.Select(root => ParseShapeNode(
                partUri,
                root,
                source,
                state,
                summary,
                parentId: null,
                depth: 0,
                representationOrdinal,
                cancellationToken
            ))
            .ToArray();
        return new WordFigureShapeModelDefinition(
            new ReadOnlyCollection<WordFigureShapeNodeDefinition>(parsedRoots),
            summary.NodeCount,
            summary.GroupCount,
            summary.ShapeCount,
            summary.PictureCount,
            summary.PathCount,
            summary.PathCommandCount,
            summary.PathPointCount,
            summary.EffectCount,
            summary.TextCharacterCount
        );
    }

    private WordFigureShapeNodeDefinition ParseShapeNode(
        string partUri,
        XElement node,
        LosslessXmlDocument source,
        BuildState state,
        ShapeModelSummary summary,
        string? parentId,
        int depth,
        int representationOrdinal,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++state.ShapeNodeCount > _options.MaxShapeNodes)
        {
            throw new WordFigureLimitException(
                $"Shape node count exceeds {_options.MaxShapeNodes}."
            );
        }
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.FiguresAndCaptions,
            1,
            1_024
        );
        summary.NodeCount++;
        var kind = ShapeNodeKind(node);
        switch (kind)
        {
            case WordFigureShapeNodeKind.Group:
                summary.GroupCount++;
                break;
            case WordFigureShapeNodeKind.Picture:
                summary.PictureCount++;
                break;
            case WordFigureShapeNodeKind.Shape:
                summary.ShapeCount++;
                break;
        }

        var ordinal = source.GetElementOrdinal(node);
        var id = StableId(
            "wdsh_",
            state.Package.Fingerprint,
            partUri,
            representationOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        var owned = OwnedShapeElements(node, cancellationToken).ToArray();
        var nonVisual = owned.FirstOrDefault(item => item.Name.LocalName == "cNvPr");
        var rawName = AttributeByLocal(nonVisual, "name")
            ?? (node.Name.NamespaceName == VmlNamespace ? AttributeByLocal(node, "id") : null);
        var name = BoundMetadata(rawName, _options.MaxTextCharacters, state, out var nameTruncated);
        var properties = ShapePropertiesElement(node, owned, kind);
        var transform = ParseShapeTransform(
            partUri,
            properties,
            state,
            representationOrdinal
        );
        var geometry = ParseShapeGeometry(
            partUri,
            node,
            properties,
            state,
            summary,
            representationOrdinal,
            cancellationToken
        );
        var fillKind = ParseShapeFillKind(node, properties);
        var line = ParseShapeLine(partUri, properties, state, representationOrdinal);
        var effects = ParseShapeEffects(
            properties,
            state,
            summary,
            cancellationToken
        );
        var textFlow = ParseShapeTextFlow(
            partUri,
            owned,
            state,
            summary,
            representationOrdinal,
            cancellationToken
        );
        var children = ChildShapeNodes(node, cancellationToken)
            .Select(child => ParseShapeNode(
                partUri,
                child,
                source,
                state,
                summary,
                id,
                checked(depth + 1),
                representationOrdinal,
                cancellationToken
            ))
            .ToArray();
        return new WordFigureShapeNodeDefinition(
            id,
            parentId,
            kind,
            depth,
            ordinal,
            name,
            nameTruncated,
            transform,
            geometry,
            fillKind,
            line,
            effects,
            textFlow,
            new ReadOnlyCollection<WordFigureShapeNodeDefinition>(children)
        );
    }

    private WordFigureShapeTransformDefinition? ParseShapeTransform(
        string partUri,
        XElement? properties,
        BuildState state,
        int representationOrdinal
    )
    {
        if (properties is null || properties.Name.NamespaceName == VmlNamespace)
        {
            return null;
        }
        var transform = properties.DescendantsAndSelf()
            .FirstOrDefault(item => IsDrawingMainElement(item, "xfrm"));
        if (transform is null)
        {
            return null;
        }
        var offset = transform.Elements().FirstOrDefault(item => IsDrawingMainElement(item, "off"));
        var extent = transform.Elements().FirstOrDefault(item => IsDrawingMainElement(item, "ext"));
        var childOffset = transform.Elements().FirstOrDefault(item => IsDrawingMainElement(item, "chOff"));
        var childExtent = transform.Elements().FirstOrDefault(item => IsDrawingMainElement(item, "chExt"));
        return new WordFigureShapeTransformDefinition(
            ShapeLongAttribute(offset, "x", partUri, representationOrdinal, state),
            ShapeLongAttribute(offset, "y", partUri, representationOrdinal, state),
            ShapeLongAttribute(extent, "cx", partUri, representationOrdinal, state),
            ShapeLongAttribute(extent, "cy", partUri, representationOrdinal, state),
            ShapeLongAttribute(childOffset, "x", partUri, representationOrdinal, state),
            ShapeLongAttribute(childOffset, "y", partUri, representationOrdinal, state),
            ShapeLongAttribute(childExtent, "cx", partUri, representationOrdinal, state),
            ShapeLongAttribute(childExtent, "cy", partUri, representationOrdinal, state),
            ShapeLongAttribute(transform, "rot", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(transform, "flipH", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(transform, "flipV", partUri, representationOrdinal, state)
        );
    }

    private WordFigureShapeGeometryDefinition? ParseShapeGeometry(
        string partUri,
        XElement node,
        XElement? properties,
        BuildState state,
        ShapeModelSummary summary,
        int representationOrdinal,
        CancellationToken cancellationToken
    )
    {
        if (node.Name.NamespaceName == VmlNamespace)
        {
            var path = AttributeByLocal(node, "path");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            state.AddMetadata(path.Length);
            return new WordFigureShapeGeometryDefinition(
                WordFigureShapeGeometryKind.VmlPath,
                null,
                false,
                0,
                0,
                0,
                0,
                false,
                Array.Empty<WordFigureShapePathDefinition>(),
                path.Length,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant()
            );
        }
        if (properties is null)
        {
            return null;
        }
        var geometryRoots = properties.Descendants()
            .Where(item => IsDrawingMainElement(item)
                && item.Name.LocalName is "prstGeom" or "custGeom")
            .ToArray();
        if (geometryRoots.Length == 0)
        {
            return null;
        }
        if (geometryRoots.Length != 1)
        {
            state.AddIssue(
                "FIGURE_SHAPE_GEOMETRY_AMBIGUOUS",
                WordFigureIssueSeverity.Error,
                "Shape contains more than one DrawingML geometry declaration.",
                partUri,
                representationOrdinal
            );
            return new WordFigureShapeGeometryDefinition(
                WordFigureShapeGeometryKind.Ambiguous,
                null,
                false,
                0,
                0,
                0,
                0,
                false,
                Array.Empty<WordFigureShapePathDefinition>(),
                0,
                null
            );
        }

        var geometry = geometryRoots[0];
        if (geometry.Name.LocalName == "prstGeom")
        {
            var rawPreset = AttributeByLocal(geometry, "prst");
            var recognized = rawPreset is not null && KnownPresetShapeTypes.Contains(rawPreset);
            if (rawPreset is not null)
            {
                state.AddMetadata(rawPreset.Length);
            }
            if (!recognized)
            {
                state.AddIssue(
                    "FIGURE_SHAPE_PRESET_INVALID",
                    WordFigureIssueSeverity.Warning,
                    "DrawingML preset geometry token is missing or is not recognized.",
                    partUri,
                    representationOrdinal
                );
            }
            var adjustments = geometry.Descendants()
                .Count(item => IsDrawingMainElement(item, "gd")
                    && item.Ancestors().Any(parent => IsDrawingMainElement(parent, "avLst")));
            return new WordFigureShapeGeometryDefinition(
                WordFigureShapeGeometryKind.Preset,
                recognized ? rawPreset : null,
                recognized,
                adjustments,
                0,
                0,
                0,
                false,
                Array.Empty<WordFigureShapePathDefinition>(),
                0,
                null
            );
        }

        var paths = geometry.Descendants()
            .Where(item => IsDrawingMainElement(item, "path")
                && item.Ancestors().Any(parent => IsDrawingMainElement(parent, "pathLst")))
            .Select(path => ParseShapePath(
                partUri,
                path,
                state,
                summary,
                representationOrdinal,
                cancellationToken
            ))
            .ToArray();
        return new WordFigureShapeGeometryDefinition(
            WordFigureShapeGeometryKind.Custom,
            null,
            false,
            0,
            geometry.Descendants().Count(item => IsDrawingMainElement(item, "gd")
                && item.Ancestors().Any(parent => IsDrawingMainElement(parent, "gdLst"))),
            geometry.Descendants().Count(item => IsDrawingMainElement(item)
                && item.Name.LocalName is "ahXY" or "ahPolar"),
            geometry.Descendants().Count(item => IsDrawingMainElement(item, "cxn")),
            geometry.Descendants().Any(item => IsDrawingMainElement(item, "rect")),
            new ReadOnlyCollection<WordFigureShapePathDefinition>(paths),
            0,
            null
        );
    }

    private WordFigureShapePathDefinition ParseShapePath(
        string partUri,
        XElement path,
        BuildState state,
        ShapeModelSummary summary,
        int representationOrdinal,
        CancellationToken cancellationToken
    )
    {
        if (++state.ShapePathCount > _options.MaxShapePaths)
        {
            throw new WordFigureLimitException($"Shape path count exceeds {_options.MaxShapePaths}.");
        }
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.FiguresAndCaptions,
            1,
            512
        );
        summary.PathCount++;
        var commands = path.Elements()
            .Where(item => IsDrawingMainElement(item)
                && item.Name.LocalName is "moveTo" or "lnTo" or "arcTo" or "quadBezTo"
                    or "cubicBezTo" or "close")
            .Select(command => ParseShapePathCommand(
                partUri,
                command,
                state,
                summary,
                representationOrdinal,
                cancellationToken
            ))
            .ToArray();
        return new WordFigureShapePathDefinition(
            ShapeLongAttribute(path, "w", partUri, representationOrdinal, state),
            ShapeLongAttribute(path, "h", partUri, representationOrdinal, state),
            ShapeTokenAttribute(
                path,
                "fill",
                ShapePathFillModes,
                "FIGURE_SHAPE_PATH_FILL_INVALID",
                partUri,
                representationOrdinal,
                state
            ),
            ShapeBooleanAttribute(path, "stroke", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(path, "extrusionOk", partUri, representationOrdinal, state),
            new ReadOnlyCollection<WordFigureShapePathCommandDefinition>(commands)
        );
    }

    private WordFigureShapePathCommandDefinition ParseShapePathCommand(
        string partUri,
        XElement command,
        BuildState state,
        ShapeModelSummary summary,
        int representationOrdinal,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++state.ShapePathCommandCount > _options.MaxShapePathCommands)
        {
            throw new WordFigureLimitException(
                $"Shape path command count exceeds {_options.MaxShapePathCommands}."
            );
        }
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.FiguresAndCaptions,
            1,
            384
        );
        summary.PathCommandCount++;
        var points = command.Elements()
            .Where(item => IsDrawingMainElement(item, "pt"))
            .Select(point =>
            {
                if (++state.ShapePathPointCount > _options.MaxShapePathPoints)
                {
                    throw new WordFigureLimitException(
                        $"Shape path point count exceeds {_options.MaxShapePathPoints}."
                    );
                }
                WordOperationResourceAccounting.ChargeItems(
                    _resourceLease,
                    WordOperationResourceStage.FiguresAndCaptions,
                    1,
                    256
                );
                summary.PathPointCount++;
                return new WordFigureShapeFormulaPointDefinition(
                    ShapeFormula(AttributeByLocal(point, "x"), state),
                    ShapeFormula(AttributeByLocal(point, "y"), state)
                );
            })
            .ToArray();
        return new WordFigureShapePathCommandDefinition(
            command.Name.LocalName,
            new ReadOnlyCollection<WordFigureShapeFormulaPointDefinition>(points),
            ShapeFormula(AttributeByLocal(command, "wR"), state),
            ShapeFormula(AttributeByLocal(command, "hR"), state),
            ShapeFormula(AttributeByLocal(command, "stAng"), state),
            ShapeFormula(AttributeByLocal(command, "swAng"), state)
        );
    }

    private WordFigureShapeLineDefinition? ParseShapeLine(
        string partUri,
        XElement? properties,
        BuildState state,
        int representationOrdinal
    )
    {
        var line = properties?.Elements().FirstOrDefault(item => IsDrawingMainElement(item, "ln"));
        if (line is null)
        {
            return null;
        }
        var dash = line.Elements().FirstOrDefault(item => IsDrawingMainElement(item, "prstDash"));
        var join = line.Elements().FirstOrDefault(item => IsDrawingMainElement(item)
            && item.Name.LocalName is "bevel" or "miter" or "round");
        var head = line.Elements().FirstOrDefault(item => IsDrawingMainElement(item, "headEnd"));
        var tail = line.Elements().FirstOrDefault(item => IsDrawingMainElement(item, "tailEnd"));
        return new WordFigureShapeLineDefinition(
            ShapeLongAttribute(line, "w", partUri, representationOrdinal, state),
            ShapeTokenAttribute(line, "cap", ShapeLineCaps, "FIGURE_SHAPE_LINE_CAP_INVALID", partUri, representationOrdinal, state),
            ShapeTokenAttribute(line, "cmpd", ShapeLineCompounds, "FIGURE_SHAPE_LINE_COMPOUND_INVALID", partUri, representationOrdinal, state),
            ShapeTokenAttribute(line, "algn", ShapeLineAlignments, "FIGURE_SHAPE_LINE_ALIGNMENT_INVALID", partUri, representationOrdinal, state),
            line.Elements().FirstOrDefault(item => IsDrawingMainElement(item)
                && ShapeFillKinds.Contains(item.Name.LocalName))?.Name.LocalName,
            ShapeTokenAttribute(dash, "val", ShapeLineDashes, "FIGURE_SHAPE_LINE_DASH_INVALID", partUri, representationOrdinal, state),
            join?.Name.LocalName,
            ShapeTokenAttribute(head, "type", ShapeLineEndTypes, "FIGURE_SHAPE_LINE_END_INVALID", partUri, representationOrdinal, state),
            ShapeTokenAttribute(tail, "type", ShapeLineEndTypes, "FIGURE_SHAPE_LINE_END_INVALID", partUri, representationOrdinal, state)
        );
    }

    private WordFigureShapeEffectsDefinition? ParseShapeEffects(
        XElement? properties,
        BuildState state,
        ShapeModelSummary summary,
        CancellationToken cancellationToken
    )
    {
        if (properties is null)
        {
            return null;
        }
        var roots = properties.Elements().Where(item => IsDrawingMainElement(item)
            && item.Name.LocalName is "effectLst" or "effectDag").ToArray();
        if (roots.Length == 0)
        {
            return null;
        }
        var kinds = new List<string>();
        foreach (var root in roots)
        {
            foreach (var effect in EnumerateWithCancellation(root.Descendants(), cancellationToken)
                .Where(item => IsDrawingMainElement(item)
                    && ShapeEffectKinds.Contains(item.Name.LocalName)))
            {
                if (++state.ShapeEffectCount > _options.MaxShapeEffects)
                {
                    throw new WordFigureLimitException(
                        $"Shape effect count exceeds {_options.MaxShapeEffects}."
                    );
                }
                WordOperationResourceAccounting.ChargeItems(
                    _resourceLease,
                    WordOperationResourceStage.FiguresAndCaptions,
                    1,
                    192
                );
                summary.EffectCount++;
                kinds.Add(effect.Name.LocalName);
            }
        }
        return new WordFigureShapeEffectsDefinition(
            roots.Any(item => item.Name.LocalName == "effectLst"),
            roots.Any(item => item.Name.LocalName == "effectDag"),
            new ReadOnlyCollection<string>(kinds)
        );
    }

    private WordFigureShapeTextFlowDefinition? ParseShapeTextFlow(
        string partUri,
        IReadOnlyList<XElement> owned,
        BuildState state,
        ShapeModelSummary summary,
        int representationOrdinal,
        CancellationToken cancellationToken
    )
    {
        var textRoot = owned.FirstOrDefault(item => item.Name.LocalName is "txbx" or "txBody"
            && (item.Name.NamespaceName == WordprocessingShapeNamespace || IsDrawingMainElement(item)));
        var body = owned.FirstOrDefault(item => item.Name.LocalName == "bodyPr"
            && (item.Name.NamespaceName == WordprocessingShapeNamespace || IsDrawingMainElement(item)));
        var linked = owned.FirstOrDefault(item => item.Name.NamespaceName == WordprocessingShapeNamespace
            && item.Name.LocalName == "linkedTxbx");
        if (textRoot is null && body is null && linked is null)
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(_options.MaxTextCharacters, 4_096));
        var characterCount = 0;
        var textLeaves = textRoot is null
            ? Array.Empty<XElement>()
            : EnumerateWithCancellation(textRoot.Descendants(), cancellationToken)
                .Where(item => IsWordElement(item, "t") || IsDrawingMainElement(item, "t"))
                .Where(item => !item.Ancestors().Any(IsDeletedRevisionElement))
                .ToArray();
        foreach (var leaf in textLeaves)
        {
            characterCount = checked(characterCount + leaf.Value.Length);
            summary.TextCharacterCount = checked(summary.TextCharacterCount + leaf.Value.Length);
            state.AddMetadata(leaf.Value.Length);
            var remaining = _options.MaxTextCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(leaf.Value.AsSpan(0, Math.Min(remaining, leaf.Value.Length)));
            }
        }
        var paragraphs = textRoot?.Descendants().Count(item =>
            IsWordElement(item, "p") || IsDrawingMainElement(item, "p")) ?? 0;
        var runs = textRoot?.Descendants().Count(item =>
            IsWordElement(item, "r") || IsDrawingMainElement(item, "r")) ?? 0;
        var autoFit = body?.Elements().FirstOrDefault(item => IsDrawingMainElement(item)
            && item.Name.LocalName is "noAutofit" or "normAutofit" or "spAutoFit")?.Name.LocalName;
        return new WordFigureShapeTextFlowDefinition(
            textRoot is not null,
            paragraphs,
            runs,
            characterCount,
            builder.ToString(),
            characterCount > _options.MaxTextCharacters,
            ShapeTokenAttribute(body, "anchor", ShapeTextAnchors, "FIGURE_SHAPE_TEXT_ANCHOR_INVALID", partUri, representationOrdinal, state),
            ShapeTokenAttribute(body, "vert", ShapeTextVerticalFlows, "FIGURE_SHAPE_TEXT_VERTICAL_INVALID", partUri, representationOrdinal, state),
            ShapeTokenAttribute(body, "wrap", ShapeTextWrapModes, "FIGURE_SHAPE_TEXT_WRAP_INVALID", partUri, representationOrdinal, state),
            ShapeLongAttribute(body, "lIns", partUri, representationOrdinal, state),
            ShapeLongAttribute(body, "tIns", partUri, representationOrdinal, state),
            ShapeLongAttribute(body, "rIns", partUri, representationOrdinal, state),
            ShapeLongAttribute(body, "bIns", partUri, representationOrdinal, state),
            ShapeIntAttribute(body, "numCol", partUri, representationOrdinal, state),
            ShapeLongAttribute(body, "spcCol", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(body, "rtlCol", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(body, "fromWordArt", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(body, "anchorCtr", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(body, "forceAA", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(body, "upright", partUri, representationOrdinal, state),
            ShapeBooleanAttribute(body, "compatLnSpc", partUri, representationOrdinal, state),
            ShapeLongAttribute(body, "rot", partUri, representationOrdinal, state),
            autoFit,
            ShapeLongAttribute(linked, "id", partUri, representationOrdinal, state),
            ShapeIntAttribute(linked, "seq", partUri, representationOrdinal, state)
        );
    }

    private static XElement? ShapePropertiesElement(
        XElement node,
        IReadOnlyList<XElement> owned,
        WordFigureShapeNodeKind kind
    )
    {
        if (node.Name.NamespaceName == VmlNamespace)
        {
            return node;
        }
        var expected = kind == WordFigureShapeNodeKind.Group ? "grpSpPr" : "spPr";
        return owned.FirstOrDefault(item => item.Name.LocalName == expected
            && (item.Name.NamespaceName is WordprocessingShapeNamespace
                or WordprocessingGroupNamespace
                || IsDrawingMainElement(item)
                || item.Name.NamespaceName is TransitionalPictureNamespace
                    or StrictPictureNamespace));
    }

    private static string? ParseShapeFillKind(XElement node, XElement? properties)
    {
        if (node.Name.NamespaceName == VmlNamespace)
        {
            return ParseOnOff(AttributeByLocal(node, "filled")) == false
                ? "noFill"
                : AttributeByLocal(node, "fillcolor") is not null ? "solidFill" : null;
        }
        return properties?.Elements().FirstOrDefault(item => IsDrawingMainElement(item)
            && ShapeFillKinds.Contains(item.Name.LocalName))?.Name.LocalName;
    }

    private long? ShapeLongAttribute(
        XElement? element,
        string attribute,
        string partUri,
        int representationOrdinal,
        BuildState state
    )
    {
        var raw = AttributeByLocal(element, attribute);
        if (raw is null)
        {
            return null;
        }
        var value = ParseLong(raw);
        if (value is null)
        {
            state.AddIssue(
                "FIGURE_SHAPE_NUMERIC_VALUE_INVALID",
                WordFigureIssueSeverity.Warning,
                "Shape numeric declaration is not a signed 64-bit integer.",
                partUri,
                representationOrdinal
            );
        }
        return value;
    }

    private int? ShapeIntAttribute(
        XElement? element,
        string attribute,
        string partUri,
        int representationOrdinal,
        BuildState state
    )
    {
        var value = ShapeLongAttribute(element, attribute, partUri, representationOrdinal, state);
        if (value is null)
        {
            return null;
        }
        if (value is < int.MinValue or > int.MaxValue)
        {
            state.AddIssue(
                "FIGURE_SHAPE_INTEGER_RANGE_INVALID",
                WordFigureIssueSeverity.Warning,
                "Shape integer declaration exceeds the supported 32-bit range.",
                partUri,
                representationOrdinal
            );
            return null;
        }
        return (int)value.Value;
    }

    private bool? ShapeBooleanAttribute(
        XElement? element,
        string attribute,
        string partUri,
        int representationOrdinal,
        BuildState state
    )
    {
        var raw = AttributeByLocal(element, attribute);
        if (raw is null)
        {
            return null;
        }
        var value = ParseOnOff(raw);
        if (value is null)
        {
            state.AddIssue(
                "FIGURE_SHAPE_BOOLEAN_VALUE_INVALID",
                WordFigureIssueSeverity.Warning,
                "Shape Boolean declaration is not a recognized on/off token.",
                partUri,
                representationOrdinal
            );
        }
        return value;
    }

    private static string? ShapeTokenAttribute(
        XElement? element,
        string attribute,
        IReadOnlySet<string> allowed,
        string issueCode,
        string partUri,
        int representationOrdinal,
        BuildState state
    )
    {
        var raw = AttributeByLocal(element, attribute);
        if (raw is null)
        {
            return null;
        }
        if (allowed.Contains(raw))
        {
            state.AddMetadata(raw.Length);
            return raw;
        }
        state.AddIssue(
            issueCode,
            WordFigureIssueSeverity.Warning,
            "Shape token is not recognized and was not projected as a trusted value.",
            partUri,
            representationOrdinal
        );
        return null;
    }

    private static string? ShapeFormula(string? value, BuildState state)
    {
        if (value is null)
        {
            return null;
        }
        if (value.Length > 256)
        {
            throw new WordFigureLimitException(
                "DrawingML geometry formula exceeds 256 characters."
            );
        }
        state.AddMetadata(value.Length);
        return value;
    }

    private static IEnumerable<XElement> OwnedShapeElements(
        XElement node,
        CancellationToken cancellationToken
    )
    {
        foreach (var item in EnumerateWithCancellation(node.DescendantsAndSelf(), cancellationToken))
        {
            if (item != node && (IsShapeNodeElement(item)
                || item.Ancestors().TakeWhile(parent => parent != node).Any(IsShapeNodeElement)))
            {
                continue;
            }
            yield return item;
        }
    }

    private static IEnumerable<XElement> ChildShapeNodes(
        XElement node,
        CancellationToken cancellationToken
    ) => EnumerateWithCancellation(node.Descendants(), cancellationToken)
        .Where(IsShapeNodeElement)
        .Where(candidate => !candidate.Ancestors().TakeWhile(parent => parent != node)
            .Any(IsShapeNodeElement));

    private static bool IsShapeModelRootElement(XElement element) =>
        ShapeNodeKindOrNull(element) is WordFigureShapeNodeKind.Shape
            or WordFigureShapeNodeKind.Group;

    private static bool IsShapeNodeElement(XElement element) =>
        ShapeNodeKindOrNull(element) is not null;

    private static bool IsModeledShapePayloadElement(XElement element)
    {
        if (IsShapeNodeElement(element))
        {
            return true;
        }
        if (element.Name.NamespaceName is WordprocessingShapeNamespace
            or WordprocessingGroupNamespace
            && element.Name.LocalName is "cNvPr" or "cNvSpPr" or "cNvGrpSpPr" or "spPr"
                or "grpSpPr" or "txbx" or "bodyPr" or "linkedTxbx")
        {
            return true;
        }
        if (element.Name.NamespaceName == VmlNamespace
            && element.Name.LocalName is "shape" or "group")
        {
            return true;
        }
        return IsDrawingMainElement(element)
            && (element.Name.LocalName is "xfrm" or "off" or "ext" or "chOff" or "chExt"
                or "prstGeom" or "custGeom" or "avLst" or "gdLst" or "gd" or "ahLst"
                or "ahXY" or "ahPolar" or "cxnLst" or "cxn" or "pos" or "rect"
                or "pathLst" or "path" or "moveTo" or "lnTo" or "arcTo" or "quadBezTo"
                or "cubicBezTo" or "close" or "pt" or "noFill" or "solidFill" or "gradFill"
                or "blipFill" or "pattFill" or "grpFill" or "ln" or "prstDash" or "bevel"
                or "miter" or "round" or "headEnd" or "tailEnd" or "effectLst" or "effectDag"
                or "cont" or "txBody" or "bodyPr" or "noAutofit" or "normAutofit"
                or "spAutoFit" or "p" or "r" or "t"
                || ShapeEffectKinds.Contains(element.Name.LocalName));
    }

    private static WordFigureShapeNodeKind ShapeNodeKind(XElement element) =>
        ShapeNodeKindOrNull(element)
        ?? throw new WordFigureProjectionException("Unsupported shape node reached the shape parser.");

    private static WordFigureShapeNodeKind? ShapeNodeKindOrNull(XElement element)
    {
        if (element.Name.NamespaceName == WordprocessingGroupNamespace
            && element.Name.LocalName is "wgp" or "grpSp"
            || element.Name.NamespaceName == WordprocessingCanvasNamespace
                && element.Name.LocalName == "wpc"
            || element.Name.NamespaceName == VmlNamespace && element.Name.LocalName == "group")
        {
            return WordFigureShapeNodeKind.Group;
        }
        if (element.Name.NamespaceName == WordprocessingShapeNamespace
            && element.Name.LocalName == "wsp"
            || IsDrawingMainElement(element, "sp")
            || element.Name.NamespaceName == VmlNamespace && element.Name.LocalName == "shape")
        {
            return WordFigureShapeNodeKind.Shape;
        }
        if (element.Name.NamespaceName is TransitionalPictureNamespace or StrictPictureNamespace
            && element.Name.LocalName == "pic")
        {
            return WordFigureShapeNodeKind.Picture;
        }
        if (element.Name.NamespaceName == WordprocessingGroupNamespace
            && element.Name.LocalName == "graphicFrame")
        {
            return WordFigureShapeNodeKind.GraphicFrame;
        }
        if (element.Name.NamespaceName == Word2010Namespace
            && element.Name.LocalName == "contentPart")
        {
            return WordFigureShapeNodeKind.ContentPart;
        }
        return null;
    }

    private sealed class ShapeModelSummary
    {
        public int NodeCount { get; set; }
        public int GroupCount { get; set; }
        public int ShapeCount { get; set; }
        public int PictureCount { get; set; }
        public int PathCount { get; set; }
        public int PathCommandCount { get; set; }
        public int PathPointCount { get; set; }
        public int EffectCount { get; set; }
        public int TextCharacterCount { get; set; }
    }
}
