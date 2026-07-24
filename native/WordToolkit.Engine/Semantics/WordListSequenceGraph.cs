using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordListSequenceIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordListSequenceIssue(
    string Code,
    WordListSequenceIssueSeverity Severity,
    string Message,
    SemanticNodeId? ParagraphNodeId = null,
    string? StoryId = null,
    int? NumberId = null,
    int? LevelIndex = null
);

public enum WordListCounterStatus
{
    Exact,
    UnresolvedStart,
    UnresolvedRestartRule,
    Overflow,
}

public enum WordListLabelStatus
{
    Exact,
    Hidden,
    PictureBullet,
    MissingLevelText,
    InvalidLevelText,
    ReferencedCounterUnresolved,
    UnsupportedNumberFormat,
    WordLengthLimitExceeded,
}

public enum WordListContinuationKind
{
    FirstUse,
    Continued,
    RestartedByHigherLevel,
    RestartedAfterSectionBreak,
}

public sealed record WordListCounterComponent(
    int LevelIndex,
    long? Value,
    string NumberFormat,
    string? FormattedValue,
    bool Exact
);

public sealed class WordListSequenceItem
{
    internal WordListSequenceItem(
        string id,
        string sequenceId,
        int sequenceIndex,
        SemanticNodeId paragraphNodeId,
        string storyId,
        WordStoryKind storyKind,
        string sourcePartUri,
        int sourceOrder,
        int sourceElementOrdinal,
        int numberId,
        int requestedAbstractNumberId,
        int effectiveAbstractNumberId,
        int levelIndex,
        long? counterValue,
        WordListCounterStatus counterStatus,
        WordListContinuationKind continuationKind,
        SemanticNodeId? restartTriggerParagraphNodeId,
        string? label,
        WordListLabelStatus labelStatus,
        string suffix,
        bool legalNumbering,
        int? pictureBulletId,
        IReadOnlyList<WordListCounterComponent> components,
        IReadOnlyList<string> compatibilityWarnings
    )
    {
        Id = id;
        SequenceId = sequenceId;
        SequenceIndex = sequenceIndex;
        ParagraphNodeId = paragraphNodeId;
        StoryId = storyId;
        StoryKind = storyKind;
        SourcePartUri = sourcePartUri;
        SourceOrder = sourceOrder;
        SourceElementOrdinal = sourceElementOrdinal;
        NumberId = numberId;
        RequestedAbstractNumberId = requestedAbstractNumberId;
        EffectiveAbstractNumberId = effectiveAbstractNumberId;
        LevelIndex = levelIndex;
        CounterValue = counterValue;
        CounterStatus = counterStatus;
        ContinuationKind = continuationKind;
        RestartTriggerParagraphNodeId = restartTriggerParagraphNodeId;
        Label = label;
        LabelStatus = labelStatus;
        Suffix = suffix;
        LegalNumbering = legalNumbering;
        PictureBulletId = pictureBulletId;
        Components = new ReadOnlyCollection<WordListCounterComponent>(
            components.ToArray()
        );
        CompatibilityWarnings = new ReadOnlyCollection<string>(
            compatibilityWarnings.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string Id { get; }

    public string SequenceId { get; }

    public int SequenceIndex { get; }

    public SemanticNodeId ParagraphNodeId { get; }

    public string StoryId { get; }

    public WordStoryKind StoryKind { get; }

    public string SourcePartUri { get; }

    public int SourceOrder { get; }

    public int SourceElementOrdinal { get; }

    public int NumberId { get; }

    public int RequestedAbstractNumberId { get; }

    public int EffectiveAbstractNumberId { get; }

    public int LevelIndex { get; }

    public long? CounterValue { get; }

    public WordListCounterStatus CounterStatus { get; }

    public WordListContinuationKind ContinuationKind { get; }

    public SemanticNodeId? RestartTriggerParagraphNodeId { get; }

    public string? Label { get; }

    public WordListLabelStatus LabelStatus { get; }

    public string Suffix { get; }

    public bool LegalNumbering { get; }

    public int? PictureBulletId { get; }

    public IReadOnlyList<WordListCounterComponent> Components { get; }

    public IReadOnlyList<string> CompatibilityWarnings { get; }

    public bool CounterExact => CounterStatus == WordListCounterStatus.Exact;

    public bool LabelExact => LabelStatus is WordListLabelStatus.Exact
        or WordListLabelStatus.Hidden;
}

public sealed class WordListSequenceGraph
{
    internal WordListSequenceGraph(
        string packageFingerprint,
        IReadOnlyList<WordListSequenceItem> items,
        IReadOnlyList<WordListSequenceIssue> issues,
        int examinedParagraphCount,
        int numberedParagraphCount,
        int skippedNumberedParagraphCount
    )
    {
        PackageFingerprint = packageFingerprint;
        Items = new ReadOnlyCollection<WordListSequenceItem>(items.ToArray());
        Issues = new ReadOnlyCollection<WordListSequenceIssue>(issues.ToArray());
        ExaminedParagraphCount = examinedParagraphCount;
        NumberedParagraphCount = numberedParagraphCount;
        SkippedNumberedParagraphCount = skippedNumberedParagraphCount;
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordListSequenceItem> Items { get; }

    public IReadOnlyList<WordListSequenceIssue> Issues { get; }

    public int ExaminedParagraphCount { get; }

    public int NumberedParagraphCount { get; }

    public int SkippedNumberedParagraphCount { get; }

    public int ExactCounterCount => Items.Count(item => item.CounterExact);

    public int ExactLabelCount => Items.Count(item => item.LabelExact);

    public bool AnalysisExecutionComplete => true;

    public bool CounterCoverageComplete => SkippedNumberedParagraphCount == 0
        && Items.All(item => item.CounterExact);

    public bool LabelCoverageComplete => CounterCoverageComplete
        && Items.All(item => item.LabelExact);
}

public sealed record WordListSequenceGraphOptions
{
    public static WordListSequenceGraphOptions Default { get; } = new();

    public int MaxParagraphs { get; init; } = 100_000;

    public int MaxItems { get; init; } = 100_000;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxAncestorDepth { get; init; } = 512;

    internal void Validate()
    {
        if (MaxParagraphs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxParagraphs));
        }
        if (MaxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxItems));
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

public sealed class WordListSequenceGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private static readonly Regex LevelPlaceholder = new(
        "%([1-9])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    private readonly WordListSequenceGraphOptions _options;

    public WordListSequenceGraphBuilder(WordListSequenceGraphOptions? options = null)
    {
        _options = options ?? WordListSequenceGraphOptions.Default;
        _options.Validate();
    }

    public WordListSequenceGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        WordNumberingGraph numberingGraph,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(styleGraph);
        ArgumentNullException.ThrowIfNull(numberingGraph);
        ValidateSnapshots(package, semanticDocument, styleGraph, numberingGraph);
        cancellationToken.ThrowIfCancellationRequested();

        var nodes = semanticDocument.Nodes.ToArray();
        var paragraphs = nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .ToArray();
        if (paragraphs.Length > _options.MaxParagraphs)
        {
            throw new WordListSequenceLimitException(
                $"List-sequence analysis exceeds {_options.MaxParagraphs} paragraphs."
            );
        }

        var sources = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        var states = new Dictionary<(string StoryId, int NumberId), SequenceState>();
        var items = new List<WordListSequenceItem>();
        var issues = new List<WordListSequenceIssue>();
        var issueKeys = new HashSet<string>(StringComparer.Ordinal);
        var numberedParagraphCount = 0;
        var skippedNumberedParagraphCount = 0;

        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.Kind == WordSemanticNodeKind.Section)
            {
                var sectionStory = LocateStory(semanticDocument, node);
                if (sectionStory.Kind == WordStoryKind.Main)
                {
                    ApplySectionBreak(
                        sectionStory.Id,
                        numberingGraph,
                        states
                    );
                }
                continue;
            }

            if (node.Kind != WordSemanticNodeKind.Paragraph)
            {
                continue;
            }

            ParagraphNumberingReference reference;
            try
            {
                reference = ResolveParagraphReference(
                    package,
                    node,
                    styleGraph,
                    numberingGraph,
                    sources,
                    cancellationToken
                );
            }
            catch (WordListSequenceLimitException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is WordListSequenceProjectionException
                    or WordNumberingResolutionException
            )
            {
                AddIssue(
                    issues,
                    issueKeys,
                    new WordListSequenceIssue(
                        "LIST_PARAGRAPH_NUMBERING_UNRESOLVED",
                        WordListSequenceIssueSeverity.Error,
                        exception.Message,
                        node.Id
                    )
                );
                continue;
            }

            if (reference.NumberId is null or 0)
            {
                continue;
            }

            numberedParagraphCount++;
            var story = LocateStory(semanticDocument, node);
            if (HasAmbiguousAncestor(semanticDocument, node))
            {
                skippedNumberedParagraphCount++;
                AddIssue(
                    issues,
                    issueKeys,
                    new WordListSequenceIssue(
                        "LIST_PARAGRAPH_VIEW_AMBIGUOUS",
                        WordListSequenceIssueSeverity.Error,
                        "A numbered paragraph is inside tracked-revision or unresolved Markup Compatibility content.",
                        node.Id,
                        story.Id,
                        reference.NumberId,
                        reference.LevelIndex
                    )
                );
                continue;
            }

            WordResolvedNumberingLevel resolved;
            try
            {
                resolved = numberingGraph.ResolveLevel(
                    reference.NumberId.Value,
                    reference.LevelIndex
                );
            }
            catch (WordNumberingResolutionException exception)
            {
                skippedNumberedParagraphCount++;
                AddIssue(
                    issues,
                    issueKeys,
                    new WordListSequenceIssue(
                        "LIST_LEVEL_UNRESOLVED",
                        WordListSequenceIssueSeverity.Error,
                        exception.Message,
                        node.Id,
                        story.Id,
                        reference.NumberId,
                        reference.LevelIndex
                    )
                );
                continue;
            }

            if (items.Count >= _options.MaxItems)
            {
                throw new WordListSequenceLimitException(
                    $"List-sequence analysis exceeds {_options.MaxItems} numbered paragraphs."
                );
            }

            var key = (story.Id, resolved.NumberId);
            if (!states.TryGetValue(key, out var state))
            {
                state = new SequenceState(
                    SequenceId(package.Fingerprint, story.Id, resolved.NumberId)
                );
                states.Add(key, state);
            }

            var warnings = new List<string>();
            var execution = ResolveWordExecutionLevel(
                numberingGraph,
                resolved,
                warnings
            );
            var levelState = state.Levels[resolved.LevelIndex];
            var continuation = levelState.Value is null
                ? levelState.PendingContinuation
                : WordListContinuationKind.Continued;
            var restartTrigger = levelState.Value is null
                ? levelState.RestartTriggerParagraphNodeId
                : null;
            WordListCounterStatus counterStatus;
            long? counterValue;
            if (execution.Start is null or < 0)
            {
                counterStatus = WordListCounterStatus.UnresolvedStart;
                counterValue = null;
                levelState.Exact = false;
            }
            else if (!execution.RestartRuleExact)
            {
                counterStatus = WordListCounterStatus.UnresolvedRestartRule;
                counterValue = null;
                levelState.Exact = false;
            }
            else if (levelState.Value is null)
            {
                counterValue = execution.Start.Value;
                levelState.Value = counterValue;
                levelState.Exact = true;
                counterStatus = WordListCounterStatus.Exact;
            }
            else if (levelState.Value == long.MaxValue)
            {
                counterStatus = WordListCounterStatus.Overflow;
                counterValue = null;
                levelState.Value = null;
                levelState.Exact = false;
            }
            else
            {
                counterValue = levelState.Value.Value + 1;
                levelState.Value = counterValue;
                counterStatus = levelState.Exact
                    ? WordListCounterStatus.Exact
                    : WordListCounterStatus.UnresolvedRestartRule;
            }

            levelState.PendingContinuation = WordListContinuationKind.FirstUse;
            levelState.RestartTriggerParagraphNodeId = null;
            var label = RenderLabel(
                numberingGraph,
                resolved,
                state,
                counterStatus,
                warnings
            );
            state.SequenceIndex++;
            items.Add(
                new WordListSequenceItem(
                    ItemId(package.Fingerprint, node.Id),
                    state.SequenceId,
                    state.SequenceIndex,
                    node.Id,
                    story.Id,
                    story.Kind,
                    node.SourcePartUri,
                    node.SourceOrder,
                    node.SourceElementOrdinal,
                    resolved.NumberId,
                    resolved.RequestedAbstractNumberId,
                    resolved.EffectiveAbstractNumberId,
                    resolved.LevelIndex,
                    counterValue,
                    counterStatus,
                    continuation,
                    restartTrigger,
                    label.Value,
                    label.Status,
                    resolved.Level.Suffix ?? "tab",
                    resolved.Level.IsLegal == true,
                    resolved.Level.PictureBulletId,
                    label.Components,
                    warnings.Concat(reference.CompatibilityWarnings).ToArray()
                )
            );

            ResetDeeperLevels(
                numberingGraph,
                resolved.NumberId,
                resolved.LevelIndex,
                node.Id,
                state,
                warnings
            );
        }

        return new WordListSequenceGraph(
            package.Fingerprint,
            items,
            issues,
            paragraphs.Length,
            numberedParagraphCount,
            skippedNumberedParagraphCount
        );
    }

    private ParagraphNumberingReference ResolveParagraphReference(
        OpcPackageSnapshot package,
        WordSemanticNode paragraph,
        WordStyleGraph styleGraph,
        WordNumberingGraph numberingGraph,
        IDictionary<string, LosslessXmlDocument> sources,
        CancellationToken cancellationToken
    )
    {
        if (!package.Parts.TryGetValue(paragraph.SourcePartUri, out var part))
        {
            throw new WordListSequenceProjectionException(
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
            throw new WordListSequenceProjectionException(
                $"Semantic paragraph '{paragraph.Id}' no longer binds to w:p."
            );
        }
        var w = element.Name.Namespace;
        var paragraphProperties = OptionalSingleChild(element, w + "pPr");
        var styleId = ChildValue(paragraphProperties, w + "pStyle", w);
        if (
            styleId is null
            && styleGraph.DefaultStyleIds.TryGetValue(
                WordStyleType.Paragraph,
                out var defaultStyleId
            )
        )
        {
            styleId = defaultStyleId;
        }

        var effective = new Dictionary<string, string>(StringComparer.Ordinal);
        ApplyNumberingProperties(effective, styleGraph.DefaultParagraphProperties);
        var numberIdFromParagraphStyle = false;
        if (styleId is not null)
        {
            if (!styleGraph.TryGetStyle(styleId, out var style) || style is null)
            {
                throw new WordListSequenceProjectionException(
                    $"Paragraph style '{styleId}' does not exist."
                );
            }
            if (style.Type != WordStyleType.Paragraph || !style.InheritanceResolvable)
            {
                throw new WordListSequenceProjectionException(
                    style.InheritanceFailure
                        ?? $"Paragraph style '{styleId}' cannot be resolved safely."
                );
            }
            foreach (var chainId in style.InheritanceChainStyleIds)
            {
                if (!styleGraph.TryGetStyle(chainId, out var chainStyle) || chainStyle is null)
                {
                    throw new WordListSequenceProjectionException(
                        $"Paragraph style '{styleId}' lost inherited style '{chainId}'."
                    );
                }
                if (chainStyle.ParagraphProperties.Values.ContainsKey("numbering_id"))
                {
                    numberIdFromParagraphStyle = true;
                }
                ApplyNumberingProperties(effective, chainStyle.ParagraphProperties);
            }
        }

        var direct = ReadDirectNumberingProperties(paragraphProperties, w);
        if (direct.ContainsKey("numbering_id"))
        {
            numberIdFromParagraphStyle = false;
        }
        foreach (var pair in direct)
        {
            effective[pair.Key] = pair.Value;
        }

        if (!effective.TryGetValue("numbering_id", out var rawNumberId))
        {
            if (effective.ContainsKey("numbering_level"))
            {
                throw new WordListSequenceProjectionException(
                    "Paragraph declares a numbering level without a numbering instance ID."
                );
            }
            return new ParagraphNumberingReference(null, 0, Array.Empty<string>());
        }
        if (
            !int.TryParse(rawNumberId, NumberStyles.None, CultureInfo.InvariantCulture, out var numberId)
            || numberId < 0
        )
        {
            throw new WordListSequenceProjectionException(
                $"Paragraph numbering ID '{rawNumberId}' is not a non-negative integer."
            );
        }
        if (numberId == 0)
        {
            return new ParagraphNumberingReference(0, 0, Array.Empty<string>());
        }

        var levelIndex = 0;
        var warnings = new List<string>();
        if (effective.TryGetValue("numbering_level", out var rawLevel))
        {
            if (
                !int.TryParse(rawLevel, NumberStyles.None, CultureInfo.InvariantCulture, out levelIndex)
                || levelIndex is < 0 or > 8
            )
            {
                throw new WordListSequenceProjectionException(
                    $"Paragraph numbering level '{rawLevel}' is outside 0 through 8."
                );
            }
            if (!direct.ContainsKey("numbering_level"))
            {
                warnings.Add("word_uses_paragraph_style_numbering_level_against_iso_rule");
            }
        }
        else if (numberIdFromParagraphStyle && styleId is not null)
        {
            var mapped = numberingGraph.FindLevelIndexForParagraphStyle(numberId, styleId);
            if (mapped is not null)
            {
                levelIndex = mapped.Value;
            }
        }

        return new ParagraphNumberingReference(numberId, levelIndex, warnings);
    }

    private static void ApplyNumberingProperties(
        IDictionary<string, string> target,
        WordStylePropertySet properties
    )
    {
        foreach (var name in new[] { "numbering_id", "numbering_level" })
        {
            if (properties.Values.TryGetValue(name, out var value))
            {
                target[name] = value;
            }
        }
    }

    private static Dictionary<string, string> ReadDirectNumberingProperties(
        XElement? paragraphProperties,
        XNamespace w
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var numberingProperties = OptionalSingleChild(paragraphProperties, w + "numPr");
        if (numberingProperties is null)
        {
            return result;
        }
        var numberId = ChildValue(numberingProperties, w + "numId", w);
        var level = ChildValue(numberingProperties, w + "ilvl", w);
        if (numberId is not null)
        {
            result["numbering_id"] = numberId;
        }
        if (level is not null)
        {
            result["numbering_level"] = level;
        }
        return result;
    }

    private static WordExecutionLevel ResolveWordExecutionLevel(
        WordNumberingGraph graph,
        WordResolvedNumberingLevel resolved,
        ICollection<string> warnings
    )
    {
        if (!graph.TryGetInstance(resolved.NumberId, out var instance) || instance is null)
        {
            throw new WordNumberingResolutionException(
                $"Numbering instance '{resolved.NumberId}' disappeared."
            );
        }
        if (
            !graph.TryGetAbstractDefinition(
                resolved.EffectiveAbstractNumberId,
                out var definition
            )
            || definition is null
        )
        {
            throw new WordNumberingResolutionException(
                $"Effective abstract numbering definition '{resolved.EffectiveAbstractNumberId}' disappeared."
            );
        }
        definition.TryGetLevel(resolved.LevelIndex, out var baseLevel);
        instance.TryGetLevelOverride(resolved.LevelIndex, out var levelOverride);

        var start = levelOverride?.Level?.Start
            ?? levelOverride?.StartOverride
            ?? baseLevel?.Start;
        if (levelOverride?.Level?.Start is not null)
        {
            warnings.Add("word_uses_start_inside_level_override");
            if (
                levelOverride.StartOverride is not null
                && levelOverride.StartOverride != levelOverride.Level.Start
            )
            {
                warnings.Add("word_prefers_level_override_start_over_start_override");
            }
        }

        var restart = resolved.Level.RestartAfterLevel;
        if (
            resolved.LevelSourceKind == WordNumberingLevelSourceKind.InstanceOverride
            && restart is not null
        )
        {
            warnings.Add("word_ignores_restart_inside_level_override");
            restart = baseLevel?.RestartAfterLevel;
        }
        var restartRuleExact = restart is null
            || restart == 0
            || restart is >= 1 and <= 7 && restart <= resolved.LevelIndex;
        return new WordExecutionLevel(start, restart, restartRuleExact);
    }

    private static void ResetDeeperLevels(
        WordNumberingGraph graph,
        int numberId,
        int usedLevelIndex,
        SemanticNodeId triggerParagraphNodeId,
        SequenceState state,
        ICollection<string> warnings
    )
    {
        for (var levelIndex = usedLevelIndex + 1; levelIndex <= 8; levelIndex++)
        {
            WordResolvedNumberingLevel deeper;
            try
            {
                deeper = graph.ResolveLevel(numberId, levelIndex);
            }
            catch (WordNumberingResolutionException)
            {
                continue;
            }
            var execution = ResolveWordExecutionLevel(graph, deeper, warnings);
            if (!execution.RestartRuleExact)
            {
                state.Levels[levelIndex].Exact = false;
                continue;
            }
            if (execution.RestartAfterLevel == 0)
            {
                continue;
            }
            var triggerLevel = execution.RestartAfterLevel is null
                ? levelIndex - 1
                : execution.RestartAfterLevel.Value - 1;
            if (usedLevelIndex > triggerLevel)
            {
                continue;
            }
            var deeperState = state.Levels[levelIndex];
            if (deeperState.Value is null)
            {
                continue;
            }
            deeperState.Value = null;
            deeperState.PendingContinuation = WordListContinuationKind.RestartedByHigherLevel;
            deeperState.RestartTriggerParagraphNodeId = triggerParagraphNodeId;
        }
    }

    private static void ApplySectionBreak(
        string storyId,
        WordNumberingGraph graph,
        IDictionary<(string StoryId, int NumberId), SequenceState> states
    )
    {
        foreach (var pair in states.Where(pair => pair.Key.StoryId == storyId))
        {
            if (!graph.TryGetInstance(pair.Key.NumberId, out var instance) || instance is null)
            {
                continue;
            }
            if (
                !graph.TryGetAbstractResolution(instance.AbstractNumberId, out var resolution)
                || resolution?.EffectiveAbstractNumberId is not { } effectiveId
                || !graph.TryGetAbstractDefinition(effectiveId, out var definition)
                || definition?.RestartNumberingAfterBreak != true
            )
            {
                continue;
            }
            foreach (var level in pair.Value.Levels)
            {
                if (level.Value is null)
                {
                    continue;
                }
                level.Value = null;
                level.PendingContinuation = WordListContinuationKind.RestartedAfterSectionBreak;
                level.RestartTriggerParagraphNodeId = null;
            }
        }
    }

    private static RenderedLabel RenderLabel(
        WordNumberingGraph graph,
        WordResolvedNumberingLevel current,
        SequenceState state,
        WordListCounterStatus counterStatus,
        ICollection<string> warnings
    )
    {
        if (current.Level.LevelTextIsNull == true)
        {
            return new RenderedLabel(
                string.Empty,
                WordListLabelStatus.Hidden,
                Array.Empty<WordListCounterComponent>()
            );
        }
        if (current.Level.PictureBulletId is not null)
        {
            return new RenderedLabel(
                null,
                WordListLabelStatus.PictureBullet,
                Array.Empty<WordListCounterComponent>()
            );
        }
        if (current.Level.LevelText is not { } pattern)
        {
            return new RenderedLabel(
                null,
                WordListLabelStatus.MissingLevelText,
                Array.Empty<WordListCounterComponent>()
            );
        }

        var matches = LevelPlaceholder.Matches(pattern);
        if (
            matches.Count > 9
            || matches.Any(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) > current.LevelIndex + 1)
        )
        {
            warnings.Add("word_ignores_invalid_level_text_pattern");
            return new RenderedLabel(
                null,
                WordListLabelStatus.InvalidLevelText,
                Array.Empty<WordListCounterComponent>()
            );
        }
        if (matches.Count == 0)
        {
            return pattern.Length <= 31
                ? new RenderedLabel(
                    pattern,
                    WordListLabelStatus.Exact,
                    Array.Empty<WordListCounterComponent>()
                )
                : new RenderedLabel(
                    null,
                    WordListLabelStatus.WordLengthLimitExceeded,
                    Array.Empty<WordListCounterComponent>()
                );
        }
        if (counterStatus != WordListCounterStatus.Exact)
        {
            return new RenderedLabel(
                null,
                WordListLabelStatus.ReferencedCounterUnresolved,
                Array.Empty<WordListCounterComponent>()
            );
        }

        var components = new Dictionary<int, WordListCounterComponent>();
        foreach (Match match in matches)
        {
            var levelIndex = int.Parse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture
            ) - 1;
            if (components.ContainsKey(levelIndex))
            {
                continue;
            }
            var levelState = state.Levels[levelIndex];
            WordResolvedNumberingLevel referenced;
            try
            {
                referenced = graph.ResolveLevel(current.NumberId, levelIndex);
            }
            catch (WordNumberingResolutionException)
            {
                return new RenderedLabel(
                    null,
                    WordListLabelStatus.ReferencedCounterUnresolved,
                    components.Values.OrderBy(item => item.LevelIndex).ToArray()
                );
            }
            var format = current.Level.IsLegal == true
                ? "decimal"
                : referenced.Level.NumberFormat ?? "decimal";
            if (levelState.Value is null || !levelState.Exact)
            {
                components[levelIndex] = new WordListCounterComponent(
                    levelIndex,
                    levelState.Value,
                    format,
                    null,
                    false
                );
                return new RenderedLabel(
                    null,
                    WordListLabelStatus.ReferencedCounterUnresolved,
                    components.Values.OrderBy(item => item.LevelIndex).ToArray()
                );
            }
            var formatted = FormatCounter(levelState.Value.Value, format);
            components[levelIndex] = new WordListCounterComponent(
                levelIndex,
                levelState.Value,
                format,
                formatted,
                formatted is not null
            );
            if (formatted is null || referenced.Level.CustomNumberFormat is not null)
            {
                return new RenderedLabel(
                    null,
                    WordListLabelStatus.UnsupportedNumberFormat,
                    components.Values.OrderBy(item => item.LevelIndex).ToArray()
                );
            }
        }

        var rendered = LevelPlaceholder.Replace(
            pattern,
            match => components[
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1
            ].FormattedValue!
        );
        return rendered.Length <= 31
            ? new RenderedLabel(
                rendered,
                WordListLabelStatus.Exact,
                components.Values.OrderBy(item => item.LevelIndex).ToArray()
            )
            : new RenderedLabel(
                null,
                WordListLabelStatus.WordLengthLimitExceeded,
                components.Values.OrderBy(item => item.LevelIndex).ToArray()
            );
    }

    private static string? FormatCounter(long value, string format) => format switch
    {
        "decimal" => value.ToString(CultureInfo.InvariantCulture),
        "decimalZero" when value >= 0 => value < 10
            ? "0" + value.ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture),
        "upperRoman" => Roman(value, upper: true),
        "lowerRoman" => Roman(value, upper: false),
        "upperLetter" => Letters(value, upper: true),
        "lowerLetter" => Letters(value, upper: false),
        "none" => string.Empty,
        _ => null,
    };

    private static string? Roman(long value, bool upper)
    {
        if (value is < 1 or > 3999)
        {
            return null;
        }
        (int Value, string Token)[] symbols =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];
        var remaining = (int)value;
        var result = new StringBuilder();
        foreach (var symbol in symbols)
        {
            while (remaining >= symbol.Value)
            {
                result.Append(symbol.Token);
                remaining -= symbol.Value;
            }
        }
        var text = result.ToString();
        return upper ? text : text.ToLowerInvariant();
    }

    private static string? Letters(long value, bool upper)
    {
        if (value is < 1 or > 1_000_000)
        {
            return null;
        }
        var result = new StringBuilder();
        var remaining = value;
        while (remaining > 0)
        {
            remaining--;
            result.Insert(0, (char)('A' + remaining % 26));
            remaining /= 26;
        }
        var text = result.ToString();
        return upper ? text : text.ToLowerInvariant();
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
        throw new WordListSequenceLimitException(
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
        throw new WordListSequenceLimitException(
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
            return LosslessXmlDocument.Parse(
                part.Entry.Content,
                new LosslessXmlOptions
                {
                    MaxSourceBytes = _options.MaxXmlPartBytes,
                    MaxXmlCharacters = _options.MaxXmlPartBytes,
                    MaxXmlElements = 1_000_000,
                    MaxXmlDepth = 256,
                    MaxTextCharacters = _options.MaxXmlPartBytes,
                },
                cancellationToken
            );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordListSequenceLimitException(
                $"Word part '{part.Uri}' exceeds a list-sequence XML limit: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordListSequenceProjectionException(
                $"Word part '{part.Uri}' is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private void AddIssue(
        ICollection<WordListSequenceIssue> issues,
        ISet<string> keys,
        WordListSequenceIssue issue
    )
    {
        var key = string.Join(
            '\0',
            issue.Code,
            issue.ParagraphNodeId?.Value ?? string.Empty,
            issue.StoryId ?? string.Empty,
            issue.NumberId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            issue.LevelIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
        );
        if (!keys.Add(key))
        {
            return;
        }
        if (issues.Count >= _options.MaxIssues)
        {
            throw new WordListSequenceLimitException(
                $"List-sequence analysis exceeds {_options.MaxIssues} issues."
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
            throw new WordListSequenceProjectionException(
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
        var attribute = child.Attribute(w + "val");
        if (attribute is null || attribute.Value.Length == 0)
        {
            throw new WordListSequenceProjectionException(
                $"Element '{child.Name.LocalName}' has no non-empty w:val."
            );
        }
        return attribute.Value;
    }

    private static bool IsWordName(XName name, string localName) =>
        name.LocalName == localName
        && name.NamespaceName is WordTransitionalNamespace or WordStrictNamespace;

    private static string ItemId(string packageFingerprint, SemanticNodeId paragraphNodeId) =>
        StableId("wdli_", packageFingerprint + "\0" + paragraphNodeId.Value);

    private static string SequenceId(string packageFingerprint, string storyId, int numberId) =>
        StableId(
            "wdls_",
            packageFingerprint
                + "\0"
                + storyId
                + "\0"
                + numberId.ToString(CultureInfo.InvariantCulture)
        );

    private static string StableId(string prefix, string value) => prefix
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..24];

    private static void ValidateSnapshots(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        WordNumberingGraph numberingGraph
    )
    {
        if (
            !string.Equals(package.Fingerprint, semanticDocument.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(package.Fingerprint, styleGraph.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(package.Fingerprint, numberingGraph.PackageFingerprint, StringComparison.Ordinal)
        )
        {
            throw new WordListSequenceProjectionException(
                "Package, semantic, style, and numbering snapshots do not share one fingerprint."
            );
        }
    }

    private sealed class SequenceState(string sequenceId)
    {
        public string SequenceId { get; } = sequenceId;

        public int SequenceIndex { get; set; }

        public LevelState[] Levels { get; } = Enumerable.Range(0, 9)
            .Select(_ => new LevelState())
            .ToArray();
    }

    private sealed class LevelState
    {
        public long? Value { get; set; }

        public bool Exact { get; set; } = true;

        public WordListContinuationKind PendingContinuation { get; set; } =
            WordListContinuationKind.FirstUse;

        public SemanticNodeId? RestartTriggerParagraphNodeId { get; set; }
    }

    private sealed record ParagraphNumberingReference(
        int? NumberId,
        int LevelIndex,
        IReadOnlyList<string> CompatibilityWarnings
    );

    private sealed record WordExecutionLevel(
        int? Start,
        int? RestartAfterLevel,
        bool RestartRuleExact
    );

    private sealed record StoryLocation(string Id, WordStoryKind Kind);

    private sealed record RenderedLabel(
        string? Value,
        WordListLabelStatus Status,
        IReadOnlyList<WordListCounterComponent> Components
    );
}

public class WordListSequenceProjectionException : IOException
{
    public WordListSequenceProjectionException(string message)
        : base(message) { }

    public WordListSequenceProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordListSequenceLimitException : WordListSequenceProjectionException
{
    public WordListSequenceLimitException(string message)
        : base(message) { }
}
