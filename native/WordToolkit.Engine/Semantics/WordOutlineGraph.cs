using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordOutlineIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordOutlineLevelSourceKind
{
    DocumentDefault,
    ParagraphStyle,
    DirectParagraph,
}

public enum WordOutlineResolutionStatus
{
    Heading,
    BodyText,
    Unresolved,
}

public sealed record WordOutlineIssue(
    string Code,
    WordOutlineIssueSeverity Severity,
    string Message,
    SemanticNodeId? ParagraphNodeId = null,
    string? StoryId = null,
    int? Level = null,
    int? PreviousLevel = null
);

public sealed record WordOutlineParagraph(
    SemanticNodeId ParagraphNodeId,
    string StoryId,
    WordStoryKind StoryKind,
    string SourcePartUri,
    int SourceOrder,
    int SourceElementOrdinal,
    string? ParagraphStyleId,
    WordOutlineResolutionStatus Status,
    int? Level,
    WordOutlineLevelSourceKind? LevelSourceKind,
    string? LevelSourceStyleId,
    bool ViewAmbiguous,
    bool HierarchyEligible,
    SemanticNodeId? ParentHeadingParagraphNodeId
);

public sealed class WordOutlineHeading
{
    internal WordOutlineHeading(
        SemanticNodeId paragraphNodeId,
        string storyId,
        WordStoryKind storyKind,
        string sourcePartUri,
        int sourceOrder,
        int sourceElementOrdinal,
        int level,
        WordOutlineLevelSourceKind levelSourceKind,
        string? paragraphStyleId,
        string? levelSourceStyleId,
        SemanticNodeId? parentHeadingParagraphNodeId,
        SemanticNodeId? previousHeadingParagraphNodeId,
        SemanticNodeId? nextHeadingParagraphNodeId,
        int childHeadingCount,
        int descendantHeadingCount,
        int titleCharacterCount,
        bool titleIsEmpty,
        bool hierarchyEligible,
        bool viewAmbiguous
    )
    {
        ParagraphNodeId = paragraphNodeId;
        StoryId = storyId;
        StoryKind = storyKind;
        SourcePartUri = sourcePartUri;
        SourceOrder = sourceOrder;
        SourceElementOrdinal = sourceElementOrdinal;
        Level = level;
        LevelSourceKind = levelSourceKind;
        ParagraphStyleId = paragraphStyleId;
        LevelSourceStyleId = levelSourceStyleId;
        ParentHeadingParagraphNodeId = parentHeadingParagraphNodeId;
        PreviousHeadingParagraphNodeId = previousHeadingParagraphNodeId;
        NextHeadingParagraphNodeId = nextHeadingParagraphNodeId;
        ChildHeadingCount = childHeadingCount;
        DescendantHeadingCount = descendantHeadingCount;
        TitleCharacterCount = titleCharacterCount;
        TitleIsEmpty = titleIsEmpty;
        HierarchyEligible = hierarchyEligible;
        ViewAmbiguous = viewAmbiguous;
    }

    public SemanticNodeId ParagraphNodeId { get; }

    public string StoryId { get; }

    public WordStoryKind StoryKind { get; }

    public string SourcePartUri { get; }

    public int SourceOrder { get; }

    public int SourceElementOrdinal { get; }

    public int Level { get; }

    public WordOutlineLevelSourceKind LevelSourceKind { get; }

    public string? ParagraphStyleId { get; }

    public string? LevelSourceStyleId { get; }

    public SemanticNodeId? ParentHeadingParagraphNodeId { get; }

    public SemanticNodeId? PreviousHeadingParagraphNodeId { get; }

    public SemanticNodeId? NextHeadingParagraphNodeId { get; }

    public int ChildHeadingCount { get; }

    public int DescendantHeadingCount { get; }

    public int TitleCharacterCount { get; }

    public bool TitleIsEmpty { get; }

    public bool HierarchyEligible { get; }

    public bool ViewAmbiguous { get; }
}

public sealed class WordOutlineGraph
{
    private readonly IReadOnlyDictionary<SemanticNodeId, WordOutlineHeading> _headingsByParagraphId;
    private readonly IReadOnlyDictionary<SemanticNodeId, WordOutlineParagraph> _paragraphsById;

    internal WordOutlineGraph(
        string packageFingerprint,
        IReadOnlyList<WordOutlineParagraph> paragraphs,
        IReadOnlyList<WordOutlineHeading> headings,
        IReadOnlyList<WordOutlineIssue> issues,
        int examinedParagraphCount,
        int bodyTextParagraphCount,
        int unresolvedParagraphCount,
        int skippedHeadingCount
    )
    {
        PackageFingerprint = packageFingerprint;
        Paragraphs = new ReadOnlyCollection<WordOutlineParagraph>(paragraphs.ToArray());
        Headings = new ReadOnlyCollection<WordOutlineHeading>(headings.ToArray());
        Issues = new ReadOnlyCollection<WordOutlineIssue>(issues.ToArray());
        ExaminedParagraphCount = examinedParagraphCount;
        BodyTextParagraphCount = bodyTextParagraphCount;
        UnresolvedParagraphCount = unresolvedParagraphCount;
        SkippedHeadingCount = skippedHeadingCount;
        _headingsByParagraphId = new ReadOnlyDictionary<SemanticNodeId, WordOutlineHeading>(
            headings.ToDictionary(heading => heading.ParagraphNodeId)
        );
        _paragraphsById = new ReadOnlyDictionary<SemanticNodeId, WordOutlineParagraph>(
            paragraphs.ToDictionary(paragraph => paragraph.ParagraphNodeId)
        );
        if (Paragraphs.Count != ExaminedParagraphCount)
        {
            throw new WordOutlineProjectionException(
                "Outline graph did not retain exactly one resolution record per paragraph."
            );
        }
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordOutlineParagraph> Paragraphs { get; }

    public IReadOnlyList<WordOutlineHeading> Headings { get; }

    public IReadOnlyList<WordOutlineIssue> Issues { get; }

    public int ExaminedParagraphCount { get; }

    public int BodyTextParagraphCount { get; }

    public int UnresolvedParagraphCount { get; }

    public int SkippedHeadingCount { get; }

    public int HeadingCount => Headings.Count;

    public int StoryCount => Paragraphs
        .Select(paragraph => paragraph.StoryId)
        .Distinct(StringComparer.Ordinal)
        .Count();

    public int HierarchyHeadingCount => Headings.Count(heading => heading.HierarchyEligible);

    public int RootHeadingCount => Headings.Count(heading =>
        heading.HierarchyEligible && heading.ParentHeadingParagraphNodeId is null
    );

    public bool AnalysisExecutionComplete => true;

    public bool OutlineCoverageComplete => UnresolvedParagraphCount == 0
        && SkippedHeadingCount == 0;

    public bool TryGetHeadingForParagraph(
        SemanticNodeId paragraphNodeId,
        out WordOutlineHeading? heading
    ) => _headingsByParagraphId.TryGetValue(paragraphNodeId, out heading);

    public bool TryGetParagraph(
        SemanticNodeId paragraphNodeId,
        out WordOutlineParagraph? paragraph
    ) => _paragraphsById.TryGetValue(paragraphNodeId, out paragraph);
}

public sealed record WordOutlineGraphOptions
{
    public static WordOutlineGraphOptions Default { get; } = new();

    public int MaxParagraphs { get; init; } = 100_000;

    public int MaxHeadings { get; init; } = 100_000;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxAncestorDepth { get; init; } = 512;

    internal void Validate()
    {
        if (MaxParagraphs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxParagraphs));
        }
        if (MaxHeadings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHeadings));
        }
        if (MaxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxIssues));
        }
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
        if (MaxAncestorDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAncestorDepth));
        }
    }
}

public sealed class WordOutlineGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private readonly WordOutlineGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordOutlineGraphBuilder(WordOutlineGraphOptions? options = null)
    {
        _options = options ?? WordOutlineGraphOptions.Default;
        _options.Validate();
    }

    public WordOutlineGraphBuilder(
        WordOutlineGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordOutlineGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordOutlineGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(styleGraph);
        ValidateSnapshots(package, semanticDocument, styleGraph);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.Outline
        );

        var paragraphs = semanticDocument.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        if (paragraphs.Length > _options.MaxParagraphs)
        {
            throw new WordOutlineLimitException(
                $"Outline analysis exceeds {_options.MaxParagraphs} paragraphs."
            );
        }

        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.Outline,
            paragraphs.Length,
            512
        );
        var sources = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        var inheritedResolutionCache = new Dictionary<
            (bool HasStyle, string? StyleId),
            CachedInheritedResolution
        >();
        var mutableHeadings = new List<MutableHeading>();
        var paragraphRecords = new List<MutableOutlineParagraph>(paragraphs.Length);
        var issues = new List<WordOutlineIssue>();
        var issueKeys = new HashSet<string>(StringComparer.Ordinal);
        var bodyTextParagraphCount = 0;
        var unresolvedParagraphCount = 0;
        var skippedHeadingCount = 0;

        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var story = LocateStory(semanticDocument, paragraph);
            var viewAmbiguous = HasAmbiguousAncestor(semanticDocument, paragraph);
            OutlineResolution resolution;
            try
            {
                resolution = ResolveOutlineLevel(
                    package,
                    paragraph,
                    styleGraph,
                    sources,
                    inheritedResolutionCache,
                    cancellationToken
                );
            }
            catch (WordOutlineLimitException)
            {
                throw;
            }
            catch (WordOutlineProjectionException exception)
            {
                unresolvedParagraphCount++;
                paragraphRecords.Add(new MutableOutlineParagraph(
                    paragraph,
                    story,
                    paragraph.Properties.GetValueOrDefault("style_id"),
                    WordOutlineResolutionStatus.Unresolved,
                    null,
                    null,
                    null,
                    viewAmbiguous,
                    hierarchyEligible: false
                ));
                AddIssue(
                    issues,
                    issueKeys,
                    new WordOutlineIssue(
                        "OUTLINE_LEVEL_UNRESOLVED",
                        WordOutlineIssueSeverity.Error,
                        exception.Message,
                        paragraph.Id,
                        StoryId: story.Id
                    )
                );
                continue;
            }

            if (resolution.RawLevel == 9)
            {
                bodyTextParagraphCount++;
                paragraphRecords.Add(new MutableOutlineParagraph(
                    paragraph,
                    story,
                    resolution.ParagraphStyleId,
                    WordOutlineResolutionStatus.BodyText,
                    null,
                    resolution.SourceKind,
                    resolution.LevelSourceStyleId,
                    viewAmbiguous,
                    hierarchyEligible: false
                ));
                continue;
            }

            var paragraphRecord = new MutableOutlineParagraph(
                paragraph,
                story,
                resolution.ParagraphStyleId,
                WordOutlineResolutionStatus.Heading,
                resolution.RawLevel + 1,
                resolution.SourceKind,
                resolution.LevelSourceStyleId,
                viewAmbiguous,
                hierarchyEligible: !viewAmbiguous
            );
            paragraphRecords.Add(paragraphRecord);

            if (viewAmbiguous)
            {
                skippedHeadingCount++;
                AddIssue(
                    issues,
                    issueKeys,
                    new WordOutlineIssue(
                        "OUTLINE_VIEW_AMBIGUOUS",
                        WordOutlineIssueSeverity.Error,
                        "A heading paragraph is inside tracked-revision or unresolved Markup Compatibility content.",
                        paragraph.Id,
                        StoryId: story.Id,
                        Level: resolution.RawLevel + 1
                    )
                );
            }

            if (mutableHeadings.Count >= _options.MaxHeadings)
            {
                throw new WordOutlineLimitException(
                    $"Outline analysis exceeds {_options.MaxHeadings} headings."
                );
            }

            var title = HeadingTitleMetrics(paragraph);
            var levelSourceKind = resolution.SourceKind
                ?? throw new WordOutlineProjectionException(
                    "A heading level has no declared direct, style, or document-default source."
                );
            var heading = new MutableHeading(
                paragraph,
                story,
                resolution.RawLevel + 1,
                levelSourceKind,
                resolution.ParagraphStyleId,
                resolution.LevelSourceStyleId,
                title.CharacterCount,
                title.IsEmpty,
                hierarchyEligible: !viewAmbiguous,
                viewAmbiguous
            );
            heading.ParagraphRecord = paragraphRecord;
            mutableHeadings.Add(heading);
            if (heading.TitleIsEmpty)
            {
                AddIssue(
                    issues,
                    issueKeys,
                    new WordOutlineIssue(
                        "OUTLINE_EMPTY_HEADING",
                        WordOutlineIssueSeverity.Warning,
                        "An outline heading has no non-whitespace text.",
                        paragraph.Id,
                        story.Id,
                        heading.Level
                    )
                );
            }
        }

        BuildHierarchy(mutableHeadings, issues, issueKeys);
        ComputeDescendantCounts(mutableHeadings);
        var headings = mutableHeadings.Select(heading => heading.Freeze()).ToArray();
        var frozenParagraphs = paragraphRecords
            .OrderBy(paragraph => paragraph.Node.SourceOrder)
            .Select(paragraph => paragraph.Freeze())
            .ToArray();
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.Outline,
            headings.Length,
            1_024
        );
        return new WordOutlineGraph(
            package.Fingerprint,
            frozenParagraphs,
            headings,
            issues,
            paragraphs.Length,
            bodyTextParagraphCount,
            unresolvedParagraphCount,
            skippedHeadingCount
        );
    }

    private OutlineResolution ResolveOutlineLevel(
        OpcPackageSnapshot package,
        WordSemanticNode paragraph,
        WordStyleGraph styleGraph,
        IDictionary<string, LosslessXmlDocument> sources,
        IDictionary<
            (bool HasStyle, string? StyleId),
            CachedInheritedResolution
        > inheritedResolutionCache,
        CancellationToken cancellationToken
    )
    {
        if (!package.Parts.TryGetValue(paragraph.SourcePartUri, out var part))
        {
            throw new WordOutlineProjectionException(
                $"Paragraph source part '{paragraph.SourcePartUri}' is missing."
            );
        }
        if (!sources.TryGetValue(part.Uri, out var source))
        {
            source = ParseSourcePart(part, cancellationToken);
            sources.Add(part.Uri, source);
        }

        var element = source.GetParsedElement(paragraph.SourceElementOrdinal);
        if (!IsWordName(element.Name, "p"))
        {
            throw new WordOutlineProjectionException(
                $"Semantic paragraph '{paragraph.Id}' no longer binds to w:p."
            );
        }
        var w = element.Name.Namespace;
        var paragraphProperties = OptionalSingleChild(element, w + "pPr");
        var paragraphStyleId = ChildValue(paragraphProperties, w + "pStyle", w);
        var directLevel = ReadOutlineLevel(paragraphProperties, w, "direct paragraph");
        if (directLevel is not null)
        {
            return new OutlineResolution(
                directLevel.Value,
                WordOutlineLevelSourceKind.DirectParagraph,
                paragraphStyleId,
                null
            );
        }

        if (paragraphStyleId is null)
        {
            styleGraph.DefaultStyleIds.TryGetValue(
                WordStyleType.Paragraph,
                out paragraphStyleId
            );
        }

        var cacheKey = (paragraphStyleId is not null, paragraphStyleId);
        if (!inheritedResolutionCache.TryGetValue(cacheKey, out var inherited))
        {
            try
            {
                inherited = ResolveInheritedOutlineLevel(
                    paragraphStyleId,
                    styleGraph
                );
            }
            catch (WordOutlineProjectionException exception)
            {
                inherited = new CachedInheritedResolution(
                    RawLevel: 9,
                    SourceKind: null,
                    LevelSourceStyleId: null,
                    Error: exception.Message
                );
            }
            inheritedResolutionCache.Add(cacheKey, inherited);
        }
        if (inherited.Error is not null)
        {
            throw new WordOutlineProjectionException(inherited.Error);
        }
        return new OutlineResolution(
            inherited.RawLevel,
            inherited.SourceKind,
            paragraphStyleId,
            inherited.LevelSourceStyleId
        );
    }

    private static CachedInheritedResolution ResolveInheritedOutlineLevel(
        string? paragraphStyleId,
        WordStyleGraph styleGraph
    )
    {
        if (paragraphStyleId is not null)
        {
            if (!styleGraph.TryGetStyle(paragraphStyleId, out var style) || style is null)
            {
                throw new WordOutlineProjectionException(
                    $"Paragraph style '{paragraphStyleId}' does not exist."
                );
            }
            if (style.Type != WordStyleType.Paragraph)
            {
                throw new WordOutlineProjectionException(
                    $"Content uses style '{paragraphStyleId}' as a paragraph style, but it is {style.Type}."
                );
            }
            if (!style.InheritanceResolvable)
            {
                throw new WordOutlineProjectionException(
                    style.InheritanceFailure
                        ?? $"Paragraph style '{paragraphStyleId}' has an unresolved inheritance chain."
                );
            }
            foreach (var chainId in style.InheritanceChainStyleIds.Reverse())
            {
                if (
                    !styleGraph.TryGetStyle(chainId, out var chainStyle)
                    || chainStyle is null
                )
                {
                    throw new WordOutlineProjectionException(
                        $"Resolved chain for '{paragraphStyleId}' lost style '{chainId}'."
                    );
                }
                var candidate = ReadPropertyOutlineLevel(
                    chainStyle.ParagraphProperties,
                    $"style '{chainId}'"
                );
                if (candidate is null)
                {
                    continue;
                }
                return new CachedInheritedResolution(
                    candidate.Value,
                    WordOutlineLevelSourceKind.ParagraphStyle,
                    chainId,
                    Error: null
                );
            }
        }

        var documentDefault = ReadPropertyOutlineLevel(
            styleGraph.DefaultParagraphProperties,
            "document default paragraph properties"
        );
        return new CachedInheritedResolution(
            documentDefault ?? 9,
            documentDefault is null
                ? null
                : WordOutlineLevelSourceKind.DocumentDefault,
            LevelSourceStyleId: null,
            Error: null
        );
    }

    private static int? ReadPropertyOutlineLevel(
        WordStylePropertySet properties,
        string description
    )
    {
        if (!properties.Values.TryGetValue("outline_level", out var raw))
        {
            return null;
        }
        return ParseOutlineLevel(raw, description);
    }

    private static int? ReadOutlineLevel(
        XElement? paragraphProperties,
        XNamespace w,
        string description
    )
    {
        var outline = OptionalSingleChild(paragraphProperties, w + "outlineLvl");
        if (outline is null)
        {
            return null;
        }
        var attributes = outline.Attributes(w + "val").Take(2).ToArray();
        if (attributes.Length != 1 || attributes[0].Value.Length == 0)
        {
            throw new WordOutlineProjectionException(
                $"The {description} outline level must contain exactly one non-empty w:val."
            );
        }
        return ParseOutlineLevel(attributes[0].Value, description);
    }

    private static int ParseOutlineLevel(string raw, string description)
    {
        if (
            !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value is < 0 or > 9
        )
        {
            throw new WordOutlineProjectionException(
                $"The {description} outline level '{raw}' is outside 0 through 9."
            );
        }
        return value;
    }

    private void BuildHierarchy(
        IReadOnlyList<MutableHeading> headings,
        ICollection<WordOutlineIssue> issues,
        ISet<string> issueKeys
    )
    {
        foreach (var storyGroup in headings
            .Where(heading => heading.HierarchyEligible)
            .GroupBy(heading => heading.Story.Id, StringComparer.Ordinal))
        {
            var stack = new List<MutableHeading>();
            MutableHeading? previous = null;
            foreach (var heading in storyGroup.OrderBy(item => item.Node.SourceOrder))
            {
                while (stack.Count > 0 && stack[^1].Level >= heading.Level)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                heading.Parent = stack.Count == 0 ? null : stack[^1];
                heading.Previous = previous;
                if (previous is not null)
                {
                    previous.Next = heading;
                }
                if (heading.Parent is not null)
                {
                    heading.Parent.Children.Add(heading);
                }
                heading.ParagraphRecord!.ParentHeadingParagraphNodeId = heading.Parent?.Node.Id;
                if (previous is null && heading.Level > 1)
                {
                    AddIssue(
                        issues,
                        issueKeys,
                        new WordOutlineIssue(
                            "OUTLINE_FIRST_LEVEL_SKIPPED",
                            WordOutlineIssueSeverity.Warning,
                            "The first outline heading in a story starts below level one.",
                            heading.Node.Id,
                            heading.Story.Id,
                            heading.Level
                        )
                    );
                }
                else if (previous is not null && heading.Level > previous.Level + 1)
                {
                    AddIssue(
                        issues,
                        issueKeys,
                        new WordOutlineIssue(
                            "OUTLINE_LEVEL_SKIPPED",
                            WordOutlineIssueSeverity.Warning,
                            "The heading outline skips one or more levels.",
                            heading.Node.Id,
                            heading.Story.Id,
                            heading.Level,
                            previous.Level
                        )
                    );
                }
                stack.Add(heading);
                previous = heading;
            }
        }
    }

    private static void ComputeDescendantCounts(IReadOnlyList<MutableHeading> headings)
    {
        for (var index = headings.Count - 1; index >= 0; index--)
        {
            var heading = headings[index];
            if (heading.Parent is not null)
            {
                heading.Parent.DescendantHeadingCount += 1 + heading.DescendantHeadingCount;
            }
        }
    }

    private StoryLocation LocateStory(
        WordSemanticDocument document,
        WordSemanticNode node
    )
    {
        var current = node;
        for (var depth = 0; depth < _options.MaxAncestorDepth; depth++)
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
                return new StoryLocation(current.Id.Value, kind.Value);
            }
            if (
                current.ParentId is not { } parentId
                || !document.TryGetNode(parentId, out var parent)
                || parent is null
            )
            {
                break;
            }
            current = parent;
        }
        throw new WordOutlineLimitException(
            $"Semantic ancestry exceeds {_options.MaxAncestorDepth} nodes or has no story root."
        );
    }

    private bool HasAmbiguousAncestor(
        WordSemanticDocument document,
        WordSemanticNode node
    )
    {
        var current = node;
        for (var depth = 0; depth < _options.MaxAncestorDepth; depth++)
        {
            if (
                current.Kind is WordSemanticNodeKind.Revision
                    or WordSemanticNodeKind.AlternateContent
            )
            {
                return true;
            }
            if (
                current.ParentId is not { } parentId
                || !document.TryGetNode(parentId, out var parent)
                || parent is null
            )
            {
                return false;
            }
            current = parent;
        }
        throw new WordOutlineLimitException(
            $"Semantic ancestry exceeds {_options.MaxAncestorDepth} nodes."
        );
    }

    private LosslessXmlDocument ParseSourcePart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxXmlPartBytes,
                MaxXmlCharacters = _options.MaxXmlPartBytes,
                MaxXmlElements = 1_000_000,
                MaxXmlDepth = 256,
                MaxTextCharacters = _options.MaxXmlPartBytes,
            };
            return _resourceLease is null
                ? LosslessXmlDocument.Parse(part.Entry.Content, options, cancellationToken)
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.Outline,
                    cancellationToken
                );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordOutlineLimitException(
                $"Word part '{part.Uri}' exceeds an outline XML limit: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordOutlineProjectionException(
                $"Word part '{part.Uri}' is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private void AddIssue(
        ICollection<WordOutlineIssue> issues,
        ISet<string> keys,
        WordOutlineIssue issue
    )
    {
        var key = string.Join(
            '\0',
            issue.Code,
            issue.ParagraphNodeId?.Value ?? string.Empty,
            issue.StoryId ?? string.Empty,
            issue.Level?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            issue.PreviousLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
        );
        if (!keys.Add(key))
        {
            return;
        }
        if (issues.Count >= _options.MaxIssues)
        {
            throw new WordOutlineLimitException(
                $"Outline analysis exceeds {_options.MaxIssues} issues."
            );
        }
        issues.Add(issue);
    }

    private static XElement? OptionalSingleChild(XElement? parent, XName name)
    {
        if (parent is null)
        {
            return null;
        }
        var children = parent.Elements(name).Take(2).ToArray();
        if (children.Length > 1)
        {
            throw new WordOutlineProjectionException(
                $"Element '{parent.Name.LocalName}' contains duplicate '{name.LocalName}' children."
            );
        }
        return children.FirstOrDefault();
    }

    private static string? ChildValue(XElement? parent, XName childName, XNamespace w)
    {
        var child = OptionalSingleChild(parent, childName);
        if (child is null)
        {
            return null;
        }
        var attributes = child.Attributes(w + "val").Take(2).ToArray();
        if (attributes.Length != 1 || attributes[0].Value.Length == 0)
        {
            throw new WordOutlineProjectionException(
                $"Element '{child.Name.LocalName}' must contain exactly one non-empty w:val."
            );
        }
        return attributes[0].Value;
    }

    private static bool IsWordName(XName name, string localName) =>
        name.LocalName == localName
        && name.NamespaceName is WordTransitionalNamespace or WordStrictNamespace;

    private static HeadingTitle HeadingTitleMetrics(WordSemanticNode paragraph)
    {
        var characterCount = 0;
        var hasNonWhitespace = false;
        foreach (var node in paragraph.DescendantsAndSelf())
        {
            var value = node.Kind switch
            {
                WordSemanticNodeKind.Text => node.Text,
                WordSemanticNodeKind.Tab => "\t",
                WordSemanticNodeKind.Break => "\n",
                _ => null,
            };
            if (!string.IsNullOrEmpty(value))
            {
                checked
                {
                    characterCount += value.Length;
                }
                if (!hasNonWhitespace && value.Any(character => !char.IsWhiteSpace(character)))
                {
                    hasNonWhitespace = true;
                }
            }
        }
        return new HeadingTitle(characterCount, !hasNonWhitespace);
    }

    private static void ValidateSnapshots(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph
    )
    {
        if (
            !string.Equals(package.Fingerprint, semanticDocument.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(package.Fingerprint, styleGraph.PackageFingerprint, StringComparison.Ordinal)
        )
        {
            throw new WordOutlineProjectionException(
                "Package, semantic, and style snapshots do not share one fingerprint."
            );
        }
    }

    private sealed class MutableHeading
    {
        public MutableHeading(
            WordSemanticNode node,
            StoryLocation story,
            int level,
            WordOutlineLevelSourceKind levelSourceKind,
            string? paragraphStyleId,
            string? levelSourceStyleId,
            int titleCharacterCount,
            bool titleIsEmpty,
            bool hierarchyEligible,
            bool viewAmbiguous
        )
        {
            Node = node;
            Story = story;
            Level = level;
            LevelSourceKind = levelSourceKind;
            ParagraphStyleId = paragraphStyleId;
            LevelSourceStyleId = levelSourceStyleId;
            TitleCharacterCount = titleCharacterCount;
            TitleIsEmpty = titleIsEmpty;
            HierarchyEligible = hierarchyEligible;
            ViewAmbiguous = viewAmbiguous;
        }

        public WordSemanticNode Node { get; }

        public StoryLocation Story { get; }

        public int Level { get; }

        public WordOutlineLevelSourceKind LevelSourceKind { get; }

        public string? ParagraphStyleId { get; }

        public string? LevelSourceStyleId { get; }

        public int TitleCharacterCount { get; }

        public bool TitleIsEmpty { get; }

        public bool HierarchyEligible { get; }

        public bool ViewAmbiguous { get; }

        public MutableOutlineParagraph? ParagraphRecord { get; set; }

        public MutableHeading? Parent { get; set; }

        public MutableHeading? Previous { get; set; }

        public MutableHeading? Next { get; set; }

        public List<MutableHeading> Children { get; } = [];

        public int DescendantHeadingCount { get; set; }

        public WordOutlineHeading Freeze() => new(
            Node.Id,
            Story.Id,
            Story.Kind,
            Node.SourcePartUri,
            Node.SourceOrder,
            Node.SourceElementOrdinal,
            Level,
            LevelSourceKind,
            ParagraphStyleId,
            LevelSourceStyleId,
            Parent?.Node.Id,
            Previous?.Node.Id,
            Next?.Node.Id,
            Children.Count,
            DescendantHeadingCount,
            TitleCharacterCount,
            TitleIsEmpty,
            HierarchyEligible,
            ViewAmbiguous
        );
    }

    private sealed class MutableOutlineParagraph
    {
        public MutableOutlineParagraph(
            WordSemanticNode node,
            StoryLocation story,
            string? paragraphStyleId,
            WordOutlineResolutionStatus status,
            int? level,
            WordOutlineLevelSourceKind? levelSourceKind,
            string? levelSourceStyleId,
            bool viewAmbiguous,
            bool hierarchyEligible
        )
        {
            Node = node;
            Story = story;
            ParagraphStyleId = paragraphStyleId;
            Status = status;
            Level = level;
            LevelSourceKind = levelSourceKind;
            LevelSourceStyleId = levelSourceStyleId;
            ViewAmbiguous = viewAmbiguous;
            HierarchyEligible = hierarchyEligible;
        }

        public WordSemanticNode Node { get; }

        public StoryLocation Story { get; }

        public string? ParagraphStyleId { get; }

        public WordOutlineResolutionStatus Status { get; }

        public int? Level { get; }

        public WordOutlineLevelSourceKind? LevelSourceKind { get; }

        public string? LevelSourceStyleId { get; }

        public bool ViewAmbiguous { get; }

        public bool HierarchyEligible { get; }

        public SemanticNodeId? ParentHeadingParagraphNodeId { get; set; }

        public WordOutlineParagraph Freeze() => new(
            Node.Id,
            Story.Id,
            Story.Kind,
            Node.SourcePartUri,
            Node.SourceOrder,
            Node.SourceElementOrdinal,
            ParagraphStyleId,
            Status,
            Level,
            LevelSourceKind,
            LevelSourceStyleId,
            ViewAmbiguous,
            HierarchyEligible,
            ParentHeadingParagraphNodeId
        );
    }

    private sealed record OutlineResolution(
        int RawLevel,
        WordOutlineLevelSourceKind? SourceKind,
        string? ParagraphStyleId,
        string? LevelSourceStyleId
    );

    private sealed record CachedInheritedResolution(
        int RawLevel,
        WordOutlineLevelSourceKind? SourceKind,
        string? LevelSourceStyleId,
        string? Error
    );

    private sealed record HeadingTitle(int CharacterCount, bool IsEmpty);

    private sealed record StoryLocation(string Id, WordStoryKind Kind);
}

public class WordOutlineProjectionException : IOException
{
    public WordOutlineProjectionException(string message)
        : base(message) { }

    public WordOutlineProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordOutlineLimitException : WordOutlineProjectionException
{
    public WordOutlineLimitException(string message)
        : base(message) { }
}
