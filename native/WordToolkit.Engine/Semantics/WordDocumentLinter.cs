using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordLintSeverity
{
    Info,
    Warning,
    Error,
    Fatal,
}

public enum WordLintRulePack
{
    Core,
    Styles,
    Accessibility,
    Security,
}

public enum WordLintCategory
{
    Package,
    Relationship,
    Semantic,
    Style,
    Formatting,
    Numbering,
    Reference,
    Theme,
    Settings,
    Font,
    Accessibility,
    Security,
}

public enum WordLintConfidence
{
    Medium,
    High,
    Certain,
}

public enum WordLintFixSafety
{
    None,
    ReviewRequired,
    ExternalApplicationRequired,
}

public sealed record WordLintRuleDescriptor(
    string Id,
    WordLintRulePack Pack,
    WordLintCategory Category,
    string Description
);

public sealed record WordLintSourceLocation(
    string? PartUri,
    int? SourceElementOrdinal,
    string? SourcePath,
    XmlSourceSpan? ByteSpan,
    SemanticNodeId? SemanticNodeId,
    string? RelationshipId
);

public sealed record WordLintFixMetadata(
    string Kind,
    WordLintFixSafety Safety,
    bool IsImplemented,
    bool RequiresPreview,
    string? BlockingReason
);

public sealed record WordLintFinding(
    string Id,
    string RuleId,
    WordLintRulePack RulePack,
    WordLintCategory Category,
    WordLintSeverity Severity,
    WordLintConfidence Confidence,
    string Message,
    string? RelatedCode,
    string? SubjectKind,
    string? SubjectFingerprint,
    int EvidenceCount,
    WordLintSourceLocation Source,
    WordLintFixMetadata Fix
);

public sealed record WordLintCoverage(
    int SemanticNodeCount,
    int SemanticNodesScanned,
    int FormattingNodesScanned,
    int HeadingCount,
    int DrawingCount,
    int TableCount,
    IReadOnlyList<string> ExplicitlyUnmodeledDomains,
    IReadOnlyList<string> Omissions
)
{
    public bool ExecutionComplete => Omissions.Count == 0;

    public bool DocumentCoverageComplete =>
        Omissions.Count == 0 && ExplicitlyUnmodeledDomains.Count == 0;

    public bool Complete => DocumentCoverageComplete;
}

public sealed class WordLintReport
{
    internal WordLintReport(
        string packageFingerprint,
        string mainPartUri,
        IReadOnlyList<WordLintRuleDescriptor> evaluatedRules,
        IReadOnlyList<WordLintFinding> findings,
        int matchedFindingCount,
        int visibleFindingCount,
        int suppressedFindingCount,
        int severityFilteredFindingCount,
        IReadOnlyDictionary<WordLintSeverity, int> severityCounts,
        IReadOnlyDictionary<WordLintCategory, int> categoryCounts,
        IReadOnlyDictionary<string, int> ruleCounts,
        WordLintCoverage coverage
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        EvaluatedRules = new ReadOnlyCollection<WordLintRuleDescriptor>(
            evaluatedRules.ToArray()
        );
        Findings = new ReadOnlyCollection<WordLintFinding>(findings.ToArray());
        MatchedFindingCount = matchedFindingCount;
        VisibleFindingCount = visibleFindingCount;
        SuppressedFindingCount = suppressedFindingCount;
        SeverityFilteredFindingCount = severityFilteredFindingCount;
        SeverityCounts = new ReadOnlyDictionary<WordLintSeverity, int>(
            new Dictionary<WordLintSeverity, int>(severityCounts)
        );
        CategoryCounts = new ReadOnlyDictionary<WordLintCategory, int>(
            new Dictionary<WordLintCategory, int>(categoryCounts)
        );
        RuleCounts = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(ruleCounts, StringComparer.Ordinal)
        );
        Coverage = coverage;
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public IReadOnlyList<WordLintRuleDescriptor> EvaluatedRules { get; }

    public IReadOnlyList<WordLintFinding> Findings { get; }

    public int MatchedFindingCount { get; }

    public int VisibleFindingCount { get; }

    public int SuppressedFindingCount { get; }

    public int SeverityFilteredFindingCount { get; }

    public IReadOnlyDictionary<WordLintSeverity, int> SeverityCounts { get; }

    public IReadOnlyDictionary<WordLintCategory, int> CategoryCounts { get; }

    public IReadOnlyDictionary<string, int> RuleCounts { get; }

    public WordLintCoverage Coverage { get; }

    public bool FindingsTruncated => Findings.Count < VisibleFindingCount;

    public bool Complete => Coverage.Complete && !FindingsTruncated;
}

public sealed record WordDocumentLinterOptions
{
    public static WordDocumentLinterOptions Default { get; } = new();

    public int MaxFindings { get; init; } = 10_000;

    public int MaxSemanticNodes { get; init; } = 250_000;

    public int MaxSourceXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    public long MaxCachedSourceXmlBytes { get; init; } = 256L * 1024 * 1024;

    public int MaxDependencyNodes { get; init; } = 100_000;

    public int MaxDependencyEdges { get; init; } = 200_000;

    public int MaxDependencyIssues { get; init; } = 10_000;

    public WordLintSeverity MinimumSeverity { get; init; } = WordLintSeverity.Info;

    public IReadOnlyCollection<WordLintRulePack>? EnabledRulePacks { get; init; }

    public IReadOnlyCollection<string> SuppressedRuleIds { get; init; } =
        Array.Empty<string>();

    public IReadOnlyCollection<string> SuppressedFindingIds { get; init; } =
        Array.Empty<string>();

    internal void Validate(IReadOnlySet<string> knownRuleIds)
    {
        if (!Enum.IsDefined(MinimumSeverity))
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSeverity));
        }
        if (MaxFindings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFindings));
        }
        if (MaxSemanticNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSemanticNodes));
        }
        if (MaxSourceXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSourceXmlPartBytes));
        }
        if (MaxCachedSourceXmlBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCachedSourceXmlBytes));
        }
        if (MaxDependencyNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDependencyNodes));
        }
        if (MaxDependencyEdges <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDependencyEdges));
        }
        if (MaxDependencyIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDependencyIssues));
        }

        if (EnabledRulePacks is not null)
        {
            if (EnabledRulePacks.Count == 0)
            {
                throw new ArgumentException(
                    "At least one lint rule pack must be enabled.",
                    nameof(EnabledRulePacks)
                );
            }
            if (EnabledRulePacks.Any(pack => !Enum.IsDefined(pack)))
            {
                throw new ArgumentOutOfRangeException(nameof(EnabledRulePacks));
            }
            if (EnabledRulePacks.Count != EnabledRulePacks.Distinct().Count())
            {
                throw new ArgumentException(
                    "Enabled lint rule packs must be unique.",
                    nameof(EnabledRulePacks)
                );
            }
        }
        if (
            SuppressedRuleIds.Count
            != SuppressedRuleIds.Distinct(StringComparer.Ordinal).Count()
        )
        {
            throw new ArgumentException(
                "Suppressed lint rule IDs must be unique.",
                nameof(SuppressedRuleIds)
            );
        }
        if (
            SuppressedFindingIds.Count
            != SuppressedFindingIds.Distinct(StringComparer.Ordinal).Count()
        )
        {
            throw new ArgumentException(
                "Suppressed lint finding IDs must be unique.",
                nameof(SuppressedFindingIds)
            );
        }

        foreach (var ruleId in SuppressedRuleIds)
        {
            if (!knownRuleIds.Contains(ruleId))
            {
                throw new ArgumentException(
                    $"Unknown lint rule ID '{ruleId}'.",
                    nameof(SuppressedRuleIds)
                );
            }
        }

        foreach (var findingId in SuppressedFindingIds)
        {
            if (
                !findingId.StartsWith("wtlint_", StringComparison.Ordinal)
                || findingId.Length != 31
                || findingId.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") >= 0
            )
            {
                throw new ArgumentException(
                    $"Invalid lint finding ID '{findingId}'.",
                    nameof(SuppressedFindingIds)
                );
            }
        }
    }
}

public sealed class WordDocumentLinter
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string DublinCoreNamespace =
        "http://purl.org/dc/elements/1.1/";

    private const string CoreOpcDiagnostic = "WTL_CORE_OPC_DIAGNOSTIC";
    private const string CoreDependencyDiagnostic = "WTL_CORE_DEPENDENCY_DIAGNOSTIC";
    private const string StyleGraphDiagnostic = "WTL_STYLE_GRAPH_DIAGNOSTIC";
    private const string NumberingGraphDiagnostic = "WTL_NUMBERING_GRAPH_DIAGNOSTIC";
    private const string NumberingSequenceDiagnostic =
        "WTL_NUMBERING_SEQUENCE_DIAGNOSTIC";
    private const string NumberingCounterUnresolved =
        "WTL_NUMBERING_COUNTER_UNRESOLVED";
    private const string NumberingLabelInvalid = "WTL_NUMBERING_LABEL_INVALID";
    private const string UnusedExplicitRelationship =
        "WTL_RELATIONSHIP_UNUSED_EXPLICIT";
    private const string OrphanRelationshipPart =
        "WTL_RELATIONSHIP_ORPHAN_PART";
    private const string ReferenceGraphDiagnostic = "WTL_REFERENCE_GRAPH_DIAGNOSTIC";
    private const string ThemeGraphDiagnostic = "WTL_THEME_GRAPH_DIAGNOSTIC";
    private const string SettingsGraphDiagnostic = "WTL_SETTINGS_GRAPH_DIAGNOSTIC";
    private const string FontGraphDiagnostic = "WTL_FONT_GRAPH_DIAGNOSTIC";
    private const string UnboundSectionStory = "WTL_CORE_UNBOUND_SECTION_STORY";
    private const string UnusedStyle = "WTL_STYLE_UNUSED";
    private const string EquivalentStyleFormatting =
        "WTL_STYLE_EQUIVALENT_FORMATTING";
    private const string DirectFormatting = "WTL_FORMATTING_DIRECT_OVERRIDE";
    private const string ExternalRelationship =
        "WTL_SECURITY_EXTERNAL_RELATIONSHIP";
    private const string HiddenText = "WTL_SECURITY_HIDDEN_TEXT";
    private const string HeadingOrder = "WTL_ACCESSIBILITY_HEADING_ORDER";
    private const string DrawingAltText = "WTL_ACCESSIBILITY_DRAWING_ALT_TEXT";
    private const string TableHeader = "WTL_ACCESSIBILITY_TABLE_HEADER";
    private const string DocumentTitle = "WTL_ACCESSIBILITY_DOCUMENT_TITLE";

    private static readonly IReadOnlyList<WordLintRuleDescriptor> Rules =
        new ReadOnlyCollection<WordLintRuleDescriptor>(
            [
                new(CoreOpcDiagnostic, WordLintRulePack.Core, WordLintCategory.Package,
                    "Surface bounded OPC and package-relationship diagnostics."),
                new(CoreDependencyDiagnostic, WordLintRulePack.Core, WordLintCategory.Semantic,
                    "Surface unresolved cross-domain dependency edges and unreachable parts."),
                new(StyleGraphDiagnostic, WordLintRulePack.Styles, WordLintCategory.Style,
                    "Surface typed style graph corruption and inheritance diagnostics."),
                new(NumberingGraphDiagnostic, WordLintRulePack.Core, WordLintCategory.Numbering,
                    "Surface typed numbering definition, instance, and level diagnostics."),
                new(NumberingSequenceDiagnostic, WordLintRulePack.Core, WordLintCategory.Numbering,
                    "Surface paragraph-level numbering execution diagnostics without selecting a revision view."),
                new(NumberingCounterUnresolved, WordLintRulePack.Core, WordLintCategory.Numbering,
                    "Report numbered paragraphs whose counter cannot be executed exactly."),
                new(NumberingLabelInvalid, WordLintRulePack.Core, WordLintCategory.Numbering,
                    "Report malformed or Word-length-invalid numbering labels."),
                new(UnusedExplicitRelationship, WordLintRulePack.Core, WordLintCategory.Relationship,
                    "Report explicit OPC relationships with no markup consumer in any compatibility branch."),
                new(OrphanRelationshipPart, WordLintRulePack.Core, WordLintCategory.Relationship,
                    "Report relationship parts whose owning source part does not exist."),
                new(ReferenceGraphDiagnostic, WordLintRulePack.Core, WordLintCategory.Reference,
                    "Surface malformed fields, bookmarks, and reference targets."),
                new(ThemeGraphDiagnostic, WordLintRulePack.Core, WordLintCategory.Theme,
                    "Surface typed theme color and font diagnostics."),
                new(SettingsGraphDiagnostic, WordLintRulePack.Core, WordLintCategory.Settings,
                    "Surface typed document settings diagnostics."),
                new(FontGraphDiagnostic, WordLintRulePack.Core, WordLintCategory.Font,
                    "Surface typed font-table and embedded-font diagnostics."),
                new(UnboundSectionStory, WordLintRulePack.Core, WordLintCategory.Reference,
                    "Report header or footer parts not bound by any effective section."),
                new(UnusedStyle, WordLintRulePack.Styles, WordLintCategory.Style,
                    "Report explicit styles with no semantic, default, inheritance, numbering, or link use."),
                new(EquivalentStyleFormatting, WordLintRulePack.Styles, WordLintCategory.Style,
                    "Group styles whose fully modeled declared formatting is equivalent."),
                new(DirectFormatting, WordLintRulePack.Styles, WordLintCategory.Formatting,
                    "Report direct paragraph or run properties that bypass reusable style definitions."),
                new(ExternalRelationship, WordLintRulePack.Security, WordLintCategory.Security,
                    "Report external OPC relationships without following their targets."),
                new(HiddenText, WordLintRulePack.Security, WordLintCategory.Security,
                    "Report directly hidden run content without returning its text."),
                new(HeadingOrder, WordLintRulePack.Accessibility, WordLintCategory.Accessibility,
                    "Report heading outlines that start below level one or skip a level."),
                new(DrawingAltText, WordLintRulePack.Accessibility, WordLintCategory.Accessibility,
                    "Report DrawingML or VML drawings without bounded alternative text metadata."),
                new(TableHeader, WordLintRulePack.Accessibility, WordLintCategory.Accessibility,
                    "Report multi-row tables whose first row is not marked as a repeating header."),
                new(DocumentTitle, WordLintRulePack.Accessibility, WordLintCategory.Accessibility,
                    "Report a missing or empty package core title."),
            ]
        );

    private static readonly IReadOnlySet<string> KnownRuleIds = Rules
        .Select(rule => rule.Id)
        .ToHashSet(StringComparer.Ordinal);

    private readonly WordDocumentLinterOptions _options;

    public WordDocumentLinter(WordDocumentLinterOptions? options = null)
    {
        _options = options ?? WordDocumentLinterOptions.Default;
        _options.Validate(KnownRuleIds);
    }

    public static IReadOnlyList<WordLintRuleDescriptor> RuleCatalog => Rules;

    public WordLintReport Analyze(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFingerprint(package.Fingerprint, semanticDocument.PackageFingerprint);

        var styles = new WordStyleGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            semanticDocument,
            styles,
            cancellationToken
        );
        var references = new WordReferenceGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var sections = new WordSectionGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var theme = new WordThemeGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var settings = new WordSettingsGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var fonts = new WordFontTableGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var charts = new WordChartGraphBuilder().Build(package, cancellationToken);
        var contentControls = new WordContentControlBindingGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var tables = new WordTableGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var dependencies = new WordDependencyGraphBuilder(
            new WordDependencyGraphOptions
            {
                MaxNodes = _options.MaxDependencyNodes,
                MaxEdges = _options.MaxDependencyEdges,
                MaxIssues = _options.MaxDependencyIssues,
            }
        ).Build(
            package,
            semanticDocument,
            styles,
            numbering,
            references,
            sections,
            charts,
            contentControls,
            tables,
            cancellationToken
        );
        return Analyze(
            package,
            semanticDocument,
            styles,
            numbering,
            references,
            sections,
            theme,
            settings,
            fonts,
            dependencies,
            tables,
            cancellationToken
        );
    }

    public WordLintReport Analyze(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        WordThemeGraph theme,
        WordSettingsGraph settings,
        WordFontTableGraph fonts,
        WordDependencyGraph dependencies,
        CancellationToken cancellationToken = default
    )
    {
        var tables = new WordTableGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        return Analyze(
            package,
            semanticDocument,
            styles,
            numbering,
            references,
            sections,
            theme,
            settings,
            fonts,
            dependencies,
            tables,
            cancellationToken
        );
    }

    public WordLintReport Analyze(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        WordThemeGraph theme,
        WordSettingsGraph settings,
        WordFontTableGraph fonts,
        WordDependencyGraph dependencies,
        WordTableGraph tables,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(numbering);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(tables);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFingerprint(
            package.Fingerprint,
            semanticDocument.PackageFingerprint,
            styles.PackageFingerprint,
            numbering.PackageFingerprint,
            references.PackageFingerprint,
            sections.PackageFingerprint,
            theme.PackageFingerprint,
            settings.PackageFingerprint,
            fonts.PackageFingerprint,
            dependencies.PackageFingerprint,
            tables.PackageFingerprint
        );

        var enabledPacks = (_options.EnabledRulePacks
                ?? Enum.GetValues<WordLintRulePack>())
            .ToHashSet();
        var state = new LintState(_options, enabledPacks, package.Fingerprint);
        var sources = new SourceIndex(package, _options, cancellationToken);
        var scannedNodes = semanticDocument.Nodes
            .Take(_options.MaxSemanticNodes)
            .ToArray();
        if (semanticDocument.NodeCount > scannedNodes.Length)
        {
            sources.AddOmission("semantic_node_scan_truncated");
        }

        AddCoreDiagnostics(state, sources, package, dependencies, cancellationToken);
        AddTypedGraphDiagnostics(
            state,
            sources,
            styles,
            numbering,
            references,
            sections,
            theme,
            settings,
            fonts,
            cancellationToken
        );
        WordListSequenceGraph? listSequences = null;
        if (state.Enabled(NumberingSequenceDiagnostic)
            || state.Enabled(NumberingCounterUnresolved)
            || state.Enabled(NumberingLabelInvalid))
        {
            try
            {
                listSequences = new WordListSequenceGraphBuilder(
                    new WordListSequenceGraphOptions
                    {
                        MaxParagraphs = _options.MaxSemanticNodes,
                        MaxItems = _options.MaxSemanticNodes,
                        MaxIssues = _options.MaxFindings,
                        MaxXmlPartBytes = _options.MaxSourceXmlPartBytes,
                    }
                ).Build(
                    package,
                    semanticDocument,
                    styles,
                    numbering,
                    cancellationToken
                );
                AddNumberingSequenceFindings(
                    state,
                    sources,
                    semanticDocument,
                    listSequences,
                    cancellationToken
                );
            }
            catch (WordListSequenceLimitException)
            {
                sources.AddOmission("numbering_sequence_analysis_limit");
            }
        }
        AddExternalRelationshipFindings(
            state,
            sources,
            package,
            cancellationToken
        );
        AddRelationshipRepairFindings(
            state,
            sources,
            package,
            cancellationToken
        );
        var drawingCount = AddDrawingAccessibilityFindings(
            state,
            sources,
            scannedNodes,
            cancellationToken
        );
        var tableCount = AddTableAccessibilityFindings(
            state,
            sources,
            scannedNodes,
            tables,
            cancellationToken
        );
        var headingCount = AddHeadingFindings(
            state,
            sources,
            semanticDocument.MainPartUri,
            scannedNodes,
            styles,
            cancellationToken
        );
        AddDocumentTitleFinding(state, sources, package, cancellationToken);
        AddUnusedStyleFindings(state, sources, styles, dependencies, cancellationToken);
        AddEquivalentStyleFindings(state, sources, styles, cancellationToken);
        var formattingCount = AddDirectFormattingAndHiddenTextFindings(
            state,
            sources,
            scannedNodes,
            cancellationToken
        );

        var explicitlyUnmodeled = dependencies.Coverage.ExplicitlyUnmodeledDomains
            .ToHashSet(StringComparer.Ordinal);
        if (listSequences?.SkippedNumberedParagraphCount > 0)
        {
            explicitlyUnmodeled.Add("numbering_revision_or_mce_view_selection");
        }
        if (listSequences?.Items.Any(item =>
                item.LabelStatus == WordListLabelStatus.PictureBullet
            ) == true)
        {
            explicitlyUnmodeled.Add("numbering_picture_bullet_rendering");
        }
        if (listSequences?.Items.Any(item =>
                item.LabelStatus == WordListLabelStatus.UnsupportedNumberFormat
            ) == true)
        {
            explicitlyUnmodeled.Add("numbering_locale_or_custom_label_rendering");
        }
        var coverage = new WordLintCoverage(
            semanticDocument.NodeCount,
            scannedNodes.Length,
            formattingCount,
            headingCount,
            drawingCount,
            tableCount,
            explicitlyUnmodeled.Order(StringComparer.Ordinal).ToArray(),
            sources.Omissions
        );
        return state.Materialize(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            coverage
        );
    }

    private static void AddNumberingSequenceFindings(
        LintState state,
        SourceIndex sources,
        WordSemanticDocument semanticDocument,
        WordListSequenceGraph sequences,
        CancellationToken cancellationToken
    )
    {
        if (state.Enabled(NumberingSequenceDiagnostic))
        {
            foreach (var issue in sequences.Issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WordSemanticNode? paragraph = null;
                if (issue.ParagraphNodeId is { } paragraphId)
                {
                    semanticDocument.TryGetNode(paragraphId, out paragraph);
                }
                state.Add(
                    NumberingSequenceDiagnostic,
                    Map(issue.Severity),
                    WordLintConfidence.Certain,
                    "The paragraph-level numbering executor emitted a bounded diagnostic.",
                    issue.Code,
                    "numbered_paragraph",
                    issue.Code + "\0" + issue.ParagraphNodeId + "\0" + issue.StoryId
                        + "\0" + issue.NumberId + "\0" + issue.LevelIndex,
                    1,
                    paragraph is null
                        ? sources.Location(null, null, null, null, null)
                        : sources.Location(paragraph),
                    ManualFix("repair_numbering_sequence")
                );
            }
        }

        foreach (var item in sequences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!semanticDocument.TryGetNode(item.ParagraphNodeId, out var paragraph)
                || paragraph is null)
            {
                continue;
            }
            if (state.Enabled(NumberingCounterUnresolved)
                && item.CounterStatus != WordListCounterStatus.Exact)
            {
                state.Add(
                    NumberingCounterUnresolved,
                    WordLintSeverity.Warning,
                    WordLintConfidence.Certain,
                    "A numbered paragraph has no exact executable counter under the current Word compatibility profile.",
                    item.CounterStatus.ToString(),
                    "numbered_paragraph",
                    item.ParagraphNodeId.Value + "\0" + item.CounterStatus,
                    1,
                    sources.Location(paragraph),
                    ManualFix("repair_numbering_counter")
                );
            }
            if (!state.Enabled(NumberingLabelInvalid)
                || item.LabelStatus is not (
                    WordListLabelStatus.MissingLevelText
                    or WordListLabelStatus.InvalidLevelText
                    or WordListLabelStatus.WordLengthLimitExceeded
                ))
            {
                continue;
            }
            state.Add(
                NumberingLabelInvalid,
                WordLintSeverity.Warning,
                WordLintConfidence.Certain,
                "A numbering label is missing, malformed, or exceeds Word's supported label length.",
                item.LabelStatus.ToString(),
                "numbered_paragraph",
                item.ParagraphNodeId.Value + "\0" + item.LabelStatus,
                1,
                sources.Location(paragraph),
                ManualFix("repair_numbering_label")
            );
        }
    }

    private static void AddCoreDiagnostics(
        LintState state,
        SourceIndex sources,
        OpcPackageSnapshot package,
        WordDependencyGraph dependencies,
        CancellationToken cancellationToken
    )
    {
        if (state.Enabled(CoreOpcDiagnostic))
        {
            foreach (var issue in package.Diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.Add(
                    CoreOpcDiagnostic,
                    Map(issue.Severity),
                    WordLintConfidence.Certain,
                    "The package emitted a bounded OPC structure diagnostic.",
                    issue.Code,
                    "opc_diagnostic",
                    issue.Code + "\0" + issue.PartUri + "\0" + issue.RelationshipId,
                    1,
                    sources.Location(
                        issue.PartUri,
                        null,
                        null,
                        null,
                        issue.RelationshipId
                    ),
                    ManualFix("repair_package_structure")
                );
            }
        }

        if (!state.Enabled(CoreDependencyDiagnostic))
        {
            return;
        }

        foreach (var issue in dependencies.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Add(
                CoreDependencyDiagnostic,
                Map(issue.Severity),
                WordLintConfidence.Certain,
                "The cross-domain dependency graph emitted a bounded diagnostic.",
                issue.Code,
                "dependency",
                issue.Code + "\0" + issue.NodeId + "\0" + issue.EdgeId,
                1,
                sources.Location(
                    issue.PartUri,
                    issue.SourceElementOrdinal,
                    null,
                    null,
                    null
                ),
                ManualFix("repair_dependency")
            );
        }
    }

    private static void AddTypedGraphDiagnostics(
        LintState state,
        SourceIndex sources,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        WordThemeGraph theme,
        WordSettingsGraph settings,
        WordFontTableGraph fonts,
        CancellationToken cancellationToken
    )
    {
        if (state.Enabled(StyleGraphDiagnostic))
        {
            foreach (var issue in styles.Issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ordinal = issue.StyleId is not null
                    && styles.TryGetStyle(issue.StyleId, out var style)
                    ? style?.SourceElementOrdinal
                    : null;
                state.Add(
                    StyleGraphDiagnostic,
                    Map(issue.Severity),
                    WordLintConfidence.Certain,
                    "The typed style graph emitted a bounded diagnostic.",
                    issue.Code,
                    "style",
                    issue.Code + "\0" + issue.StyleId,
                    1,
                    sources.Location(styles.StylesPartUri, ordinal, null, null, null),
                    ManualFix("repair_style_graph")
                );
            }
        }

        if (state.Enabled(NumberingGraphDiagnostic))
        {
            foreach (var issue in numbering.Issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int? ordinal = null;
                if (
                    issue.NumberId is { } numberId
                    && numbering.TryGetInstance(numberId, out var instance)
                )
                {
                    ordinal = instance?.SourceElementOrdinal;
                }
                else if (
                    issue.AbstractNumberId is { } abstractId
                    && numbering.TryGetAbstractDefinition(abstractId, out var definition)
                )
                {
                    ordinal = definition?.SourceElementOrdinal;
                }
                state.Add(
                    NumberingGraphDiagnostic,
                    Map(issue.Severity),
                    WordLintConfidence.Certain,
                    "The typed numbering graph emitted a bounded diagnostic.",
                    issue.Code,
                    "numbering",
                    issue.Code + "\0" + issue.AbstractNumberId + "\0" + issue.NumberId,
                    1,
                    sources.Location(numbering.NumberingPartUri, ordinal, null, null, null),
                    ManualFix("repair_numbering_graph")
                );
            }
        }

        if (state.Enabled(ReferenceGraphDiagnostic))
        {
            foreach (var issue in references.Issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.Add(
                    ReferenceGraphDiagnostic,
                    Map(issue.Severity),
                    WordLintConfidence.Certain,
                    "The typed field or bookmark graph emitted a bounded diagnostic.",
                    issue.Code,
                    "reference",
                    issue.Code + "\0" + issue.StoryId + "\0" + issue.SubjectId,
                    1,
                    sources.Location(
                        issue.PartUri,
                        issue.SourceElementOrdinal,
                        null,
                        null,
                        null
                    ),
                    ManualFix("repair_reference_graph")
                );
            }
        }

        if (state.Enabled(UnboundSectionStory))
        {
            foreach (var partUri in sections.UnboundStoryPartUris)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.Add(
                    UnboundSectionStory,
                    WordLintSeverity.Warning,
                    WordLintConfidence.Certain,
                    "A header or footer story part is not bound by any effective section.",
                    null,
                    "story_part",
                    partUri,
                    1,
                    sources.Location(partUri, null, null, null, null),
                    ManualFix("remove_or_rebind_story_part")
                );
            }
        }

        if (state.Enabled(ThemeGraphDiagnostic))
        {
            foreach (var issue in theme.Issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.Add(
                    ThemeGraphDiagnostic,
                    Map(issue.Severity),
                    WordLintConfidence.Certain,
                    "The typed theme graph emitted a bounded diagnostic.",
                    issue.Code,
                    "theme",
                    issue.Code + "\0" + issue.ColorSlot + "\0" + issue.FontCollection,
                    1,
                    sources.Location(theme.ThemePartUri, null, null, null, null),
                    ManualFix("repair_theme_graph")
                );
            }
        }

        if (state.Enabled(SettingsGraphDiagnostic))
        {
            foreach (var issue in settings.Issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.Add(
                    SettingsGraphDiagnostic,
                    Map(issue.Severity),
                    WordLintConfidence.Certain,
                    "The typed document-settings graph emitted a bounded diagnostic.",
                    issue.Code,
                    "setting",
                    issue.Code + "\0" + issue.ElementName,
                    1,
                    sources.Location(settings.SettingsPartUri, null, null, null, null),
                    ManualFix("repair_document_settings")
                );
            }
        }

        if (!state.Enabled(FontGraphDiagnostic))
        {
            return;
        }

        foreach (var issue in fonts.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = issue.FontName is not null
                && fonts.TryGetFont(issue.FontName, out var font)
                ? font?.SourceElementOrdinal
                : null;
            state.Add(
                FontGraphDiagnostic,
                Map(issue.Severity),
                WordLintConfidence.Certain,
                "The typed font-table graph emitted a bounded diagnostic.",
                issue.Code,
                "font",
                issue.Code + "\0" + issue.FontName + "\0" + issue.RelationshipId,
                1,
                sources.Location(
                    issue.PartUri ?? fonts.FontTablePartUri,
                    ordinal,
                    null,
                    null,
                    issue.RelationshipId
                ),
                ManualFix("repair_font_table")
            );
        }
    }

    private static void AddExternalRelationshipFindings(
        LintState state,
        SourceIndex sources,
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        if (!state.Enabled(ExternalRelationship))
        {
            return;
        }

        foreach (
            var relationship in package.Relationships
                .Where(item => item.TargetMode == OpcRelationshipTargetMode.External)
                .OrderBy(item => item.SourcePartUri, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Add(
                ExternalRelationship,
                WordLintSeverity.Warning,
                WordLintConfidence.Certain,
                "The package contains an external relationship. Its target was not followed and requires policy review.",
                null,
                "external_relationship",
                relationship.SourcePartUri + "\0" + relationship.Id + "\0" + relationship.Type,
                1,
                sources.RelationshipLocation(relationship),
                ManualFix("remove_or_authorize_external_relationship")
            );
        }
    }

    private void AddRelationshipRepairFindings(
        LintState state,
        SourceIndex sources,
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        if (!state.Enabled(UnusedExplicitRelationship)
            && !state.Enabled(OrphanRelationshipPart))
        {
            return;
        }
        WordRelationshipUsageGraph graph;
        try
        {
            graph = new WordRelationshipUsageGraphBuilder(
                new WordRelationshipUsageGraphOptions
                {
                    MaxRelationships = _options.MaxDependencyEdges,
                    MaxOwnerXmlParts = _options.MaxDependencyNodes,
                    MaxXmlPartBytes = _options.MaxSourceXmlPartBytes,
                    MaxReferencesPerRelationship = 1,
                }
            ).Build(package, cancellationToken);
        }
        catch (WordRelationshipUsageLimitException)
        {
            sources.AddOmission("relationship_usage_analysis_limit");
            return;
        }

        if (state.Enabled(UnusedExplicitRelationship))
        {
            foreach (var usage in graph.Relationships.Where(item =>
                item.MarkupRemovalCandidate
            ))
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.Add(
                    UnusedExplicitRelationship,
                    WordLintSeverity.Warning,
                    WordLintConfidence.Certain,
                    "An explicit OPC relationship has no markup consumer in any compatibility branch.",
                    "UNREFERENCED_EXPLICIT_RELATIONSHIP",
                    "relationship",
                    usage.Fingerprint,
                    1,
                    sources.Location(
                        usage.RelationshipPartUri,
                        null,
                        null,
                        null,
                        usage.RelationshipId
                    ),
                    ManualFix("remove_unreferenced_relationship")
                );
            }
        }
        if (!state.Enabled(OrphanRelationshipPart))
        {
            return;
        }
        foreach (var orphan in graph.OrphanRelationshipParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Add(
                OrphanRelationshipPart,
                WordLintSeverity.Error,
                WordLintConfidence.Certain,
                "A relationship part has no existing owning source part.",
                "ORPHAN_RELATIONSHIP_PART",
                "relationship_part",
                orphan.EntrySha256,
                Math.Max(1, orphan.ParsedRelationshipCount),
                sources.Location(
                    orphan.RelationshipPartUri,
                    null,
                    null,
                    null,
                    null
                ),
                ManualFix("remove_orphan_relationship_part")
            );
        }
    }

    private static int AddDrawingAccessibilityFindings(
        LintState state,
        SourceIndex sources,
        IReadOnlyList<WordSemanticNode> nodes,
        CancellationToken cancellationToken
    )
    {
        var drawings = nodes.Where(node => node.Kind == WordSemanticNodeKind.Drawing)
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        if (!state.Enabled(DrawingAltText))
        {
            return drawings.Length;
        }

        foreach (var node in drawings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryElement(node, out var element) || element is null)
            {
                continue;
            }

            var inspected = false;
            var hasAlternativeText = false;
            if (element.LocalName == "drawing")
            {
                var properties = DescendantsAndSelf(element)
                    .Where(item => item.LocalName == "docPr")
                    .ToArray();
                inspected = properties.Length != 0;
                hasAlternativeText = properties.Any(item =>
                    HasNonBlankAttribute(item, "descr")
                    || HasNonBlankAttribute(item, "title")
                );
            }
            else if (element.LocalName == "pict")
            {
                inspected = true;
                hasAlternativeText = DescendantsAndSelf(element).Any(item =>
                    HasNonBlankAttribute(item, "alt")
                    || HasNonBlankAttribute(item, "title")
                );
            }

            if (!inspected || hasAlternativeText)
            {
                continue;
            }

            state.Add(
                DrawingAltText,
                WordLintSeverity.Warning,
                WordLintConfidence.High,
                "A drawing has no non-empty title or description for assistive technology.",
                null,
                "drawing",
                node.Id.Value,
                1,
                sources.Location(node),
                ManualFix("set_drawing_alternative_text")
            );
        }
        return drawings.Length;
    }

    private static int AddTableAccessibilityFindings(
        LintState state,
        SourceIndex sources,
        IReadOnlyList<WordSemanticNode> nodes,
        WordTableGraph tableGraph,
        CancellationToken cancellationToken
    )
    {
        var scannedTableIds = nodes.Where(node => node.Kind == WordSemanticNodeKind.Table)
            .Select(node => node.Id)
            .ToHashSet();
        var tables = tableGraph.Tables.Where(table =>
                scannedTableIds.Contains(table.SemanticNodeId)
            )
            .OrderBy(table => table.SourceElementOrdinal)
            .ToArray();
        if (!state.Enabled(TableHeader))
        {
            return tables.Length;
        }

        var rowsById = tableGraph.Rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (table.RowIds.Count < 2 || !rowsById.TryGetValue(table.RowIds[0], out var firstRow))
            {
                continue;
            }
            if (firstRow.HeaderEffective)
            {
                continue;
            }

            state.Add(
                TableHeader,
                WordLintSeverity.Info,
                WordLintConfidence.Medium,
                "A multi-row table does not mark its first row as a repeating header. Confirm whether the table has column headings.",
                null,
                "table",
                table.Id,
                table.RowIds.Count,
                sources.Location(
                    firstRow.PartUri,
                    firstRow.SourceElementOrdinal,
                    null,
                    firstRow.SemanticNodeId,
                    null
                ),
                ManualFix("mark_table_header_row")
            );
        }
        return tables.Length;
    }

    private static int AddHeadingFindings(
        LintState state,
        SourceIndex sources,
        string mainPartUri,
        IReadOnlyList<WordSemanticNode> nodes,
        WordStyleGraph styles,
        CancellationToken cancellationToken
    )
    {
        if (!state.Enabled(HeadingOrder))
        {
            return 0;
        }
        var headings = new List<(WordSemanticNode Node, int Level)>();
        foreach (
            var node in nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && string.Equals(node.SourcePartUri, mainPartUri, StringComparison.Ordinal)
            ).OrderBy(node => node.SourceOrder)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var level = OutlineLevel(node, sources, styles);
            if (level is >= 1 and <= 9)
            {
                headings.Add((node, level.Value));
            }
        }

        if (headings.Count == 0)
        {
            return headings.Count;
        }

        if (headings[0].Level > 1)
        {
            state.Add(
                HeadingOrder,
                WordLintSeverity.Warning,
                WordLintConfidence.High,
                "The first outline heading starts below level one.",
                null,
                "heading",
                headings[0].Node.Id.Value + "\0start",
                1,
                sources.Location(headings[0].Node),
                ManualFix("normalize_heading_hierarchy")
            );
        }

        for (var index = 1; index < headings.Count; index++)
        {
            var previous = headings[index - 1];
            var current = headings[index];
            if (current.Level <= previous.Level + 1)
            {
                continue;
            }
            state.Add(
                HeadingOrder,
                WordLintSeverity.Warning,
                WordLintConfidence.High,
                "The heading outline skips one or more levels.",
                null,
                "heading",
                current.Node.Id.Value + "\0" + previous.Level + "\0" + current.Level,
                current.Level - previous.Level,
                sources.Location(current.Node),
                ManualFix("normalize_heading_hierarchy")
            );
        }
        return headings.Count;
    }

    private static void AddDocumentTitleFinding(
        LintState state,
        SourceIndex sources,
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        if (!state.Enabled(DocumentTitle))
        {
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var relationships = package.Relationships.Where(item =>
            item.SourcePartUri == "/"
            && item.TargetMode == OpcRelationshipTargetMode.Internal
            && item.Type.EndsWith("/metadata/core-properties", StringComparison.Ordinal)
        ).ToArray();
        var relationship = relationships.FirstOrDefault();
        var partUri = relationship?.ResolvedTargetPartUri;
        XmlSourceElement[] titles = [];
        XmlSourceElement? coreRoot = null;
        if (
            partUri is not null
            && sources.TryRoot(partUri, out var root)
            && root is not null
        )
        {
            coreRoot = root;
            titles = DescendantsAndSelf(root).Where(item =>
                item.LocalName == "title"
                && item.NamespaceUri == DublinCoreNamespace
            ).ToArray();
        }
        var title = titles.FirstOrDefault();
        var hasTitle = titles.Any(item => !string.IsNullOrWhiteSpace(item.Value));
        if (hasTitle)
        {
            return;
        }

        state.Add(
            DocumentTitle,
            WordLintSeverity.Warning,
            WordLintConfidence.Certain,
            "The package core properties do not contain a non-empty document title.",
            null,
            "document",
            "core_title",
            1,
            sources.Location(
                partUri,
                title?.Ordinal,
                title is null ? null : "/cp:coreProperties/dc:title",
                null,
                relationship?.Id
            ),
            relationships.Length == 1
                && package.Parts.TryGetValue(partUri ?? string.Empty, out var corePart)
                && string.Equals(
                    corePart.ContentType,
                    "application/vnd.openxmlformats-package.core-properties+xml",
                    StringComparison.OrdinalIgnoreCase
                )
                && coreRoot is not null
                && coreRoot.LocalName == "coreProperties"
                && coreRoot.NamespaceUri
                    == "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                && titles.Length == 1
                && title is not null
                && title.ParentOrdinal == coreRoot.Ordinal
                && title.Children.Count == 0
                && !title.HasLexicalMarkupInContent
                ? ImplementedFix("set_document_title")
                : ManualFix("set_document_title")
        );
    }

    private static void AddUnusedStyleFindings(
        LintState state,
        SourceIndex sources,
        WordStyleGraph styles,
        WordDependencyGraph dependencies,
        CancellationToken cancellationToken
    )
    {
        if (!state.Enabled(UnusedStyle))
        {
            return;
        }
        var styleNodes = dependencies.Nodes
            .Where(node => node.Kind == WordDependencyNodeKind.Style)
            .GroupBy(node => node.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        foreach (var style in styles.Styles.OrderBy(item => item.SourceElementOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (style.IsDefault || !styleNodes.TryGetValue(style.StyleId, out var node))
            {
                continue;
            }
            var referenced = false;
            foreach (var edge in dependencies.IncomingView(node.Id))
            {
                if (edge.Kind == WordDependencyEdgeKind.DefinesStyle)
                {
                    continue;
                }
                referenced = true;
                break;
            }
            if (referenced)
            {
                continue;
            }

            state.Add(
                UnusedStyle,
                WordLintSeverity.Info,
                WordLintConfidence.High,
                "An explicit non-default style has no modeled semantic, numbering, inheritance, link, or default use.",
                null,
                "style",
                style.StyleId,
                1,
                sources.Location(
                    styles.StylesPartUri,
                    style.SourceElementOrdinal,
                    null,
                    null,
                    null
                ),
                ManualFix("delete_unused_style")
            );
        }
    }

    private static void AddEquivalentStyleFindings(
        LintState state,
        SourceIndex sources,
        WordStyleGraph styles,
        CancellationToken cancellationToken
    )
    {
        if (!state.Enabled(EquivalentStyleFormatting))
        {
            return;
        }
        var groups = styles.Styles
            .Where(FullyModeled)
            .GroupBy(StyleFormattingSignature, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Min(style => style.SourceElementOrdinal));
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = group.OrderBy(style => style.SourceElementOrdinal).ToArray();
            var subject = string.Join(
                "\0",
                items.Select(style => style.StyleId).Order(StringComparer.Ordinal)
            );
            state.Add(
                EquivalentStyleFormatting,
                WordLintSeverity.Info,
                WordLintConfidence.Certain,
                $"{items.Length} styles have equivalent fully modeled declared formatting. Their names, links, UI metadata, and usage may still differ.",
                null,
                "style_group",
                subject,
                items.Length,
                sources.Location(
                    styles.StylesPartUri,
                    items[0].SourceElementOrdinal,
                    null,
                    null,
                    null
                ),
                ManualFix("consolidate_equivalent_styles")
            );
        }
    }

    private static int AddDirectFormattingAndHiddenTextFindings(
        LintState state,
        SourceIndex sources,
        IReadOnlyList<WordSemanticNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (!state.Enabled(DirectFormatting) && !state.Enabled(HiddenText))
        {
            return 0;
        }
        var formattingNodes = nodes.Where(node =>
            node.Kind is WordSemanticNodeKind.Paragraph or WordSemanticNodeKind.Run
        ).OrderBy(node => node.SourceOrder).ToArray();
        var scannedCount = 0;
        foreach (var node in formattingNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryElement(node, out var element) || element is null)
            {
                continue;
            }
            scannedCount++;
            var propertyName = node.Kind == WordSemanticNodeKind.Paragraph ? "pPr" : "rPr";
            var properties = element.Children.FirstOrDefault(child =>
                IsWordElement(child, propertyName)
            );
            if (properties is null)
            {
                continue;
            }

            if (node.Kind == WordSemanticNodeKind.Run && state.Enabled(HiddenText))
            {
                var hidden = properties.Children.Any(child =>
                    IsWordElement(child, "vanish") && OnOffEnabled(child)
                );
                if (hidden)
                {
                    state.Add(
                        HiddenText,
                        WordLintSeverity.Warning,
                        WordLintConfidence.Certain,
                        "A run contains directly hidden text. The text was not returned.",
                        null,
                        "run",
                        node.Id.Value + "\0hidden",
                        1,
                        sources.Location(
                            node.SourcePartUri,
                            properties.Ordinal,
                            node.SourcePath + "/w:rPr[1]",
                            node.Id,
                            null
                        ),
                        ManualFix("review_hidden_text")
                    );
                }
            }

            if (!state.Enabled(DirectFormatting))
            {
                continue;
            }
            var excluded = node.Kind == WordSemanticNodeKind.Paragraph
                ? ParagraphStructuralProperties
                : RunStructuralProperties;
            var directPropertyCount = properties.Children.Count(child =>
                IsWordNamespace(child.NamespaceUri) && !excluded.Contains(child.LocalName)
            );
            if (directPropertyCount == 0)
            {
                continue;
            }
            var hasStyle = node.Properties.ContainsKey("style_id");
            state.Add(
                DirectFormatting,
                hasStyle ? WordLintSeverity.Warning : WordLintSeverity.Info,
                WordLintConfidence.Certain,
                hasStyle
                    ? "Direct formatting overrides a reusable style on this semantic object."
                    : "Direct formatting is attached to a semantic object without an explicit reusable style.",
                null,
                node.Kind == WordSemanticNodeKind.Paragraph ? "paragraph" : "run",
                node.Id.Value,
                directPropertyCount,
                sources.Location(
                    node.SourcePartUri,
                    properties.Ordinal,
                    node.SourcePath + (node.Kind == WordSemanticNodeKind.Paragraph
                        ? "/w:pPr[1]"
                        : "/w:rPr[1]"),
                    node.Id,
                    null
                ),
                ManualFix("normalize_direct_formatting")
            );
        }
        return scannedCount;
    }

    private static readonly IReadOnlySet<string> ParagraphStructuralProperties =
        new HashSet<string>(["pStyle", "numPr", "sectPr", "pPrChange"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> RunStructuralProperties =
        new HashSet<string>(["rStyle", "rPrChange"], StringComparer.Ordinal);

    private static int? OutlineLevel(
        WordSemanticNode node,
        SourceIndex sources,
        WordStyleGraph styles
    )
    {
        if (sources.TryElement(node, out var element) && element is not null)
        {
            var direct = element.Children
                .Where(child => IsWordElement(child, "pPr"))
                .SelectMany(child => child.Children)
                .FirstOrDefault(child => IsWordElement(child, "outlineLvl"));
            if (direct is not null && TryWordIntegerAttribute(direct, "val", out var value))
            {
                return value == 9 ? null : value + 1;
            }
        }

        if (
            !node.Properties.TryGetValue("style_id", out var styleId)
            || !styles.TryGetStyle(styleId, out var style)
            || style is null
            || !style.InheritanceResolvable
        )
        {
            return null;
        }
        int? result = null;
        foreach (var chainId in style.InheritanceChainStyleIds)
        {
            if (
                styles.TryGetStyle(chainId, out var chainStyle)
                && chainStyle is not null
                && chainStyle.ParagraphProperties.Values.TryGetValue(
                    "outline_level",
                    out var raw
                )
                && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            )
            {
                result = value == 9 ? null : value + 1;
            }
        }
        return result;
    }

    private static bool FullyModeled(WordStyleDefinition style) =>
        style.ParagraphProperties.IsFullyModeled
        && style.RunProperties.IsFullyModeled
        && style.TableProperties.IsFullyModeled
        && style.TableCellProperties.IsFullyModeled;

    private static string StyleFormattingSignature(WordStyleDefinition style)
    {
        var builder = new StringBuilder();
        Append(builder, style.Type.ToString());
        Append(builder, style.BasedOnStyleId ?? "");
        AppendProperties(builder, style.ParagraphProperties);
        AppendProperties(builder, style.RunProperties);
        AppendProperties(builder, style.TableProperties);
        AppendProperties(builder, style.TableCellProperties);
        return builder.ToString();
    }

    private static void AppendProperties(
        StringBuilder builder,
        WordStylePropertySet properties
    )
    {
        foreach (var pair in properties.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            Append(builder, pair.Key);
            Append(builder, pair.Value);
        }
        builder.Append('|');
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value).Append(';');

    private static IEnumerable<XmlSourceElement> DescendantsAndSelf(
        XmlSourceElement root
    )
    {
        var stack = new Stack<XmlSourceElement>();
        stack.Push(root);
        while (stack.TryPop(out var current))
        {
            yield return current;
            for (var index = current.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(current.Children[index]);
            }
        }
    }

    private static bool IsWordElement(XmlSourceElement element, string localName) =>
        element.LocalName == localName && IsWordNamespace(element.NamespaceUri);

    private static bool IsWordNamespace(string value) =>
        value is WordTransitionalNamespace or WordStrictNamespace;

    private static bool HasNonBlankAttribute(XmlSourceElement element, string localName) =>
        element.Attributes.Any(attribute =>
            attribute.LocalName == localName && !string.IsNullOrWhiteSpace(attribute.Value)
        );

    private static bool OnOffEnabled(XmlSourceElement element)
    {
        var value = element.Attributes.FirstOrDefault(attribute =>
            attribute.LocalName == "val" && IsWordNamespace(attribute.NamespaceUri)
        )?.Value;
        return value is null
            || value is not ("0" or "false" or "off" or "no");
    }

    private static bool TryWordIntegerAttribute(
        XmlSourceElement element,
        string localName,
        out int value
    )
    {
        var raw = element.Attributes.FirstOrDefault(attribute =>
            attribute.LocalName == localName && IsWordNamespace(attribute.NamespaceUri)
        )?.Value;
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static WordLintFixMetadata ManualFix(string kind) => new(
        kind,
        WordLintFixSafety.ReviewRequired,
        IsImplemented: false,
        RequiresPreview: true,
        BlockingReason: "The typed repair engine for this rule is not implemented."
    );

    private static WordLintFixMetadata ImplementedFix(string kind) => new(
        kind,
        WordLintFixSafety.ReviewRequired,
        IsImplemented: true,
        RequiresPreview: true,
        BlockingReason: null
    );

    private static WordLintSeverity Map(OpcDiagnosticSeverity severity) => severity switch
    {
        OpcDiagnosticSeverity.Info => WordLintSeverity.Info,
        OpcDiagnosticSeverity.Warning => WordLintSeverity.Warning,
        OpcDiagnosticSeverity.Error => WordLintSeverity.Error,
        OpcDiagnosticSeverity.Fatal => WordLintSeverity.Fatal,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static WordLintSeverity Map(WordDependencyIssueSeverity severity) => severity switch
    {
        WordDependencyIssueSeverity.Info => WordLintSeverity.Info,
        WordDependencyIssueSeverity.Warning => WordLintSeverity.Warning,
        WordDependencyIssueSeverity.Error => WordLintSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static WordLintSeverity Map(WordStyleIssueSeverity severity) => severity switch
    {
        WordStyleIssueSeverity.Warning => WordLintSeverity.Warning,
        WordStyleIssueSeverity.Error => WordLintSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static WordLintSeverity Map(WordNumberingIssueSeverity severity) => severity switch
    {
        WordNumberingIssueSeverity.Warning => WordLintSeverity.Warning,
        WordNumberingIssueSeverity.Error => WordLintSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static WordLintSeverity Map(WordListSequenceIssueSeverity severity) => severity switch
    {
        WordListSequenceIssueSeverity.Info => WordLintSeverity.Info,
        WordListSequenceIssueSeverity.Warning => WordLintSeverity.Warning,
        WordListSequenceIssueSeverity.Error => WordLintSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static WordLintSeverity Map(WordReferenceIssueSeverity severity) => severity switch
    {
        WordReferenceIssueSeverity.Info => WordLintSeverity.Info,
        WordReferenceIssueSeverity.Warning => WordLintSeverity.Warning,
        WordReferenceIssueSeverity.Error => WordLintSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static WordLintSeverity Map(WordThemeIssueSeverity severity) => severity switch
    {
        WordThemeIssueSeverity.Warning => WordLintSeverity.Warning,
        WordThemeIssueSeverity.Error => WordLintSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static WordLintSeverity Map(WordSettingsIssueSeverity severity) => severity switch
    {
        WordSettingsIssueSeverity.Info => WordLintSeverity.Info,
        WordSettingsIssueSeverity.Warning => WordLintSeverity.Warning,
        WordSettingsIssueSeverity.Error => WordLintSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static WordLintSeverity Map(WordFontTableIssueSeverity severity) => severity switch
    {
        WordFontTableIssueSeverity.Info => WordLintSeverity.Info,
        WordFontTableIssueSeverity.Warning => WordLintSeverity.Warning,
        WordFontTableIssueSeverity.Error => WordLintSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static void EnsureFingerprint(string expected, params string[] actual)
    {
        if (actual.Any(value => !string.Equals(expected, value, StringComparison.Ordinal)))
        {
            throw new WordLintProjectionException(
                "Linting requires every typed graph to come from the same package fingerprint."
            );
        }
    }

    private sealed class LintState
    {
        private readonly WordDocumentLinterOptions _options;
        private readonly IReadOnlySet<WordLintRulePack> _enabledPacks;
        private readonly HashSet<string> _suppressedRules;
        private readonly HashSet<string> _suppressedFindings;
        private readonly string _packageFingerprint;
        private readonly List<WordLintFinding> _findings = [];
        private readonly Dictionary<WordLintSeverity, int> _severityCounts = [];
        private readonly Dictionary<WordLintCategory, int> _categoryCounts = [];
        private readonly Dictionary<string, int> _ruleCounts = new(StringComparer.Ordinal);

        public LintState(
            WordDocumentLinterOptions options,
            IReadOnlySet<WordLintRulePack> enabledPacks,
            string packageFingerprint
        )
        {
            _options = options;
            _enabledPacks = enabledPacks;
            _packageFingerprint = packageFingerprint;
            _suppressedRules = options.SuppressedRuleIds.ToHashSet(StringComparer.Ordinal);
            _suppressedFindings = options.SuppressedFindingIds.ToHashSet(StringComparer.Ordinal);
        }

        public int MatchedCount { get; private set; }

        public int VisibleCount { get; private set; }

        public int SuppressedCount { get; private set; }

        public int SeverityFilteredCount { get; private set; }

        public bool Enabled(string ruleId)
        {
            var rule = Rules.Single(item => item.Id == ruleId);
            return _enabledPacks.Contains(rule.Pack);
        }

        public void Add(
            string ruleId,
            WordLintSeverity severity,
            WordLintConfidence confidence,
            string message,
            string? relatedCode,
            string? subjectKind,
            string? subject,
            int evidenceCount,
            WordLintSourceLocation source,
            WordLintFixMetadata fix
        )
        {
            var rule = Rules.Single(item => item.Id == ruleId);
            if (!_enabledPacks.Contains(rule.Pack))
            {
                return;
            }
            MatchedCount++;
            var identity = string.Join(
                "\0",
                _packageFingerprint,
                ruleId,
                relatedCode ?? "",
                source.PartUri ?? "",
                source.SourceElementOrdinal?.ToString(CultureInfo.InvariantCulture) ?? "",
                source.SourcePath ?? "",
                source.SemanticNodeId?.Value ?? "",
                source.RelationshipId ?? "",
                subject ?? ""
            );
            var id = "wtlint_" + Hash(identity, 12);
            if (_suppressedRules.Contains(ruleId) || _suppressedFindings.Contains(id))
            {
                SuppressedCount++;
                return;
            }
            if (severity < _options.MinimumSeverity)
            {
                SeverityFilteredCount++;
                return;
            }

            VisibleCount++;
            _severityCounts.TryGetValue(severity, out var severityCount);
            _severityCounts[severity] = severityCount + 1;
            _categoryCounts.TryGetValue(rule.Category, out var categoryCount);
            _categoryCounts[rule.Category] = categoryCount + 1;
            _ruleCounts.TryGetValue(ruleId, out var ruleCount);
            _ruleCounts[ruleId] = ruleCount + 1;
            if (_findings.Count >= _options.MaxFindings)
            {
                return;
            }
            _findings.Add(
                new WordLintFinding(
                    id,
                    rule.Id,
                    rule.Pack,
                    rule.Category,
                    severity,
                    confidence,
                    message,
                    relatedCode,
                    subjectKind,
                    subject is null ? null : Hash(subject, 8),
                    evidenceCount,
                    source,
                    fix
                )
            );
        }

        public WordLintReport Materialize(
            string packageFingerprint,
            string mainPartUri,
            WordLintCoverage coverage
        )
        {
            var findings = _findings
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.Category)
                .ThenBy(item => item.Source.PartUri, StringComparer.Ordinal)
                .ThenBy(item => item.Source.SourceElementOrdinal)
                .ThenBy(item => item.RuleId, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            var evaluated = Rules.Where(rule => _enabledPacks.Contains(rule.Pack)).ToArray();
            return new WordLintReport(
                packageFingerprint,
                mainPartUri,
                evaluated,
                findings,
                MatchedCount,
                VisibleCount,
                SuppressedCount,
                SeverityFilteredCount,
                _severityCounts,
                _categoryCounts,
                _ruleCounts,
                coverage
            );
        }

        private static string Hash(string value, int bytes)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash.AsSpan(0, bytes)).ToLowerInvariant();
        }
    }

    private sealed class SourceIndex
    {
        private readonly OpcPackageSnapshot _package;
        private readonly WordDocumentLinterOptions _options;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, LosslessXmlDocument> _documents =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _omissions = new(StringComparer.Ordinal);
        private long _cachedBytes;

        public SourceIndex(
            OpcPackageSnapshot package,
            WordDocumentLinterOptions options,
            CancellationToken cancellationToken
        )
        {
            _package = package;
            _options = options;
            _cancellationToken = cancellationToken;
        }

        public IReadOnlyList<string> Omissions => _omissions
            .Order(StringComparer.Ordinal)
            .ToArray();

        public void AddOmission(string omission) => _omissions.Add(omission);

        public WordLintSourceLocation Location(WordSemanticNode node) => Location(
            node.SourcePartUri,
            node.SourceElementOrdinal,
            node.SourcePath,
            node.Id,
            null
        );

        public WordLintSourceLocation Location(
            string? partUri,
            int? sourceElementOrdinal,
            string? sourcePath,
            SemanticNodeId? semanticNodeId,
            string? relationshipId
        )
        {
            XmlSourceSpan? span = null;
            if (
                partUri is not null
                && sourceElementOrdinal is { } ordinal
                && TryDocument(partUri, out var document)
                && document is not null
            )
            {
                try
                {
                    span = document.GetElement(ordinal).FullSpan;
                }
                catch (ArgumentOutOfRangeException)
                {
                    AddOmission("source_ordinal_unavailable");
                }
            }
            return new WordLintSourceLocation(
                partUri,
                sourceElementOrdinal,
                sourcePath,
                span,
                semanticNodeId,
                relationshipId
            );
        }

        public WordLintSourceLocation RelationshipLocation(
            OpcRelationship relationship
        )
        {
            if (
                TryRoot(relationship.RelationshipPartUri, out var root)
                && root is not null
            )
            {
                var element = root.Children.FirstOrDefault(child =>
                    child.LocalName == "Relationship"
                    && child.Attributes.Any(attribute =>
                        attribute.LocalName == "Id"
                        && string.Equals(
                            attribute.Value,
                            relationship.Id,
                            StringComparison.Ordinal
                        )
                    )
                );
                if (element is not null)
                {
                    return new WordLintSourceLocation(
                        relationship.RelationshipPartUri,
                        element.Ordinal,
                        "/Relationships/Relationship",
                        element.FullSpan,
                        null,
                        relationship.Id
                    );
                }
                AddOmission("relationship_source_unavailable");
            }
            return new WordLintSourceLocation(
                relationship.RelationshipPartUri,
                null,
                null,
                null,
                null,
                relationship.Id
            );
        }

        public bool TryElement(
            WordSemanticNode node,
            out XmlSourceElement? element
        )
        {
            element = null;
            if (!TryDocument(node.SourcePartUri, out var document) || document is null)
            {
                return false;
            }
            try
            {
                element = document.GetElement(node.SourceElementOrdinal);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                AddOmission("source_ordinal_unavailable");
                return false;
            }
        }

        public bool TryRoot(string partUri, out XmlSourceElement? root)
        {
            root = null;
            if (!TryDocument(partUri, out var document) || document is null)
            {
                return false;
            }
            root = document.Root;
            return true;
        }

        private bool TryDocument(
            string partUri,
            out LosslessXmlDocument? document
        )
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_documents.TryGetValue(partUri, out var cached))
            {
                document = cached;
                return true;
            }
            ReadOnlyMemory<byte> content;
            if (_package.Parts.TryGetValue(partUri, out var part))
            {
                content = part.Entry.Content;
            }
            else
            {
                var infrastructureEntry = _package.Entries.FirstOrDefault(entry =>
                    !entry.IsDirectory
                    && string.Equals(entry.PartUri, partUri, StringComparison.Ordinal)
                );
                if (infrastructureEntry is null)
                {
                    AddOmission("source_part_unavailable");
                    document = null;
                    return false;
                }
                content = infrastructureEntry.Content;
            }
            var length = content.Length;
            if (length > _options.MaxSourceXmlPartBytes)
            {
                AddOmission("source_xml_part_limit");
                document = null;
                return false;
            }
            if (_cachedBytes + length > _options.MaxCachedSourceXmlBytes)
            {
                AddOmission("source_xml_cache_limit");
                document = null;
                return false;
            }
            try
            {
                document = LosslessXmlDocument.Parse(
                    content,
                    new LosslessXmlOptions
                    {
                        MaxSourceBytes = _options.MaxSourceXmlPartBytes,
                        MaxXmlCharacters = Math.Max(
                            _options.MaxSourceXmlPartBytes,
                            (long)_options.MaxSourceXmlPartBytes * 4
                        ),
                    },
                    _cancellationToken
                );
            }
            catch (LosslessXmlException)
            {
                AddOmission("source_xml_unavailable");
                document = null;
                return false;
            }
            _documents.Add(partUri, document);
            _cachedBytes += length;
            return true;
        }
    }
}

public sealed class WordLintProjectionException : InvalidOperationException
{
    public WordLintProjectionException(string message)
        : base(message)
    {
    }
}
