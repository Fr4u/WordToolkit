using System.Collections.ObjectModel;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Rendering;

public enum WordPresentationCapabilityStatus
{
    Resolved,
    ResolvedWithDiagnostics,
    NotModeled,
}

public sealed record WordPresentationCapability(
    string Domain,
    WordPresentationCapabilityStatus Status,
    int DiagnosticCount,
    bool CoverageComplete
);

/// <summary>
/// An immutable, fingerprint-bound presentation projection shared by render backends.
/// </summary>
public sealed class WordPresentationSnapshot
{
    internal WordPresentationSnapshot(
        string packageFingerprint,
        WordSemanticDocument document,
        WordStyleGraph styles,
        WordReviewGraph reviews,
        WordEquationGraph equations,
        WordOutlineGraph outline,
        WordSectionGraph sections,
        WordNumberingGraph numbering,
        WordListSequenceGraph listSequences,
        WordTableGraph tables,
        WordReferenceGraph references,
        WordFigureCaptionGraph figures,
        WordSettingsGraph settings
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFingerprint);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(equations);
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(numbering);
        ArgumentNullException.ThrowIfNull(listSequences);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(settings);

        ValidateFingerprint(packageFingerprint, document.PackageFingerprint, nameof(document));
        ValidateFingerprint(packageFingerprint, styles.PackageFingerprint, nameof(styles));
        ValidateFingerprint(packageFingerprint, reviews.PackageFingerprint, nameof(reviews));
        ValidateFingerprint(packageFingerprint, equations.PackageFingerprint, nameof(equations));
        ValidateFingerprint(packageFingerprint, outline.PackageFingerprint, nameof(outline));
        ValidateFingerprint(packageFingerprint, sections.PackageFingerprint, nameof(sections));
        ValidateFingerprint(packageFingerprint, numbering.PackageFingerprint, nameof(numbering));
        ValidateFingerprint(
            packageFingerprint,
            listSequences.PackageFingerprint,
            nameof(listSequences)
        );
        ValidateFingerprint(packageFingerprint, tables.PackageFingerprint, nameof(tables));
        ValidateFingerprint(
            packageFingerprint,
            references.PackageFingerprint,
            nameof(references)
        );
        ValidateFingerprint(packageFingerprint, figures.PackageFingerprint, nameof(figures));
        ValidateFingerprint(packageFingerprint, settings.PackageFingerprint, nameof(settings));

        PackageFingerprint = packageFingerprint;
        Document = document;
        Styles = styles;
        Reviews = reviews;
        Equations = equations;
        Outline = outline;
        Sections = sections;
        Numbering = numbering;
        ListSequences = listSequences;
        Tables = tables;
        References = references;
        Figures = figures;
        Settings = settings;
        RevisionsBySemanticNodeId = FreezeIndex(
            reviews.Revisions,
            revision => revision.SemanticNodeId,
            "revision"
        );
        EquationsBySemanticNodeId = FreezeIndex(
            equations.Equations,
            equation => equation.SemanticNodeId,
            "equation"
        );
        HeadingsByParagraphNodeId = new ReadOnlyDictionary<
            SemanticNodeId,
            WordOutlineHeading
        >(outline.Headings.ToDictionary(heading => heading.ParagraphNodeId));
        UnmodeledDomains = new ReadOnlyCollection<string>(
            [
                "active_content_execution",
                "drawing_layout",
                "field_evaluation",
                "font_metrics",
                "line_breaking",
                "rendered_page_geometry",
            ]
        );
        Capabilities = BuildCapabilities();
        Warnings = BuildWarnings();
    }

    public string PackageFingerprint { get; }

    public WordSemanticDocument Document { get; }

    public WordStyleGraph Styles { get; }

    public WordReviewGraph Reviews { get; }

    public WordEquationGraph Equations { get; }

    public WordOutlineGraph Outline { get; }

    public WordSectionGraph Sections { get; }

    public WordNumberingGraph Numbering { get; }

    public WordListSequenceGraph ListSequences { get; }

    public WordTableGraph Tables { get; }

    public WordReferenceGraph References { get; }

    public WordFigureCaptionGraph Figures { get; }

    public WordSettingsGraph Settings { get; }

    public IReadOnlyDictionary<string, WordPresentationCapability> Capabilities { get; }

    public IReadOnlyList<string> UnmodeledDomains { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyDictionary<SemanticNodeId, WordRevisionDefinition>
        RevisionsBySemanticNodeId
    { get; }

    public IReadOnlyDictionary<SemanticNodeId, WordEquationDefinition>
        EquationsBySemanticNodeId
    { get; }

    public IReadOnlyDictionary<SemanticNodeId, WordOutlineHeading>
        HeadingsByParagraphNodeId
    { get; }

    private IReadOnlyDictionary<string, WordPresentationCapability> BuildCapabilities()
    {
        var capabilities = new Dictionary<string, WordPresentationCapability>(
            StringComparer.Ordinal
        );

        AddCapability(
            capabilities,
            "semantic_structure",
            Document.Warnings.Count,
            Document.Warnings.Count == 0
        );
        AddCapability(capabilities, "styles", Styles.Issues.Count, Styles.Issues.Count == 0);
        AddCapability(capabilities, "reviews", Reviews.Issues.Count, Reviews.Issues.Count == 0);
        AddCapability(
            capabilities,
            "equations",
            Equations.Issues.Count,
            Equations.Issues.Count == 0
        );
        AddCapability(
            capabilities,
            "outline",
            Outline.Issues.Count,
            Outline.OutlineCoverageComplete
        );
        AddCapability(
            capabilities,
            "sections",
            Sections.UnboundStoryPartUris.Count,
            Sections.UnboundStoryPartUris.Count == 0
        );
        AddCapability(
            capabilities,
            "numbering",
            Numbering.Issues.Count + Numbering.UnmodeledRootElements.Count,
            Numbering.Issues.Count == 0 && Numbering.UnmodeledRootElements.Count == 0
        );
        AddCapability(
            capabilities,
            "list_sequences",
            ListSequences.Issues.Count,
            ListSequences.CounterCoverageComplete && ListSequences.LabelCoverageComplete
        );
        AddCapability(capabilities, "tables", Tables.Issues.Count, Tables.Issues.Count == 0);
        AddCapability(
            capabilities,
            "references",
            References.Issues.Count,
            References.Issues.Count == 0 && !References.IssuesTruncated
        );
        AddCapability(
            capabilities,
            "figures",
            Figures.Issues.Count,
            Figures.Issues.Count == 0 && !Figures.IssuesTruncated
        );
        AddCapability(
            capabilities,
            "settings",
            Settings.Issues.Count + Settings.UnmodeledRootElements.Count,
            Settings.Issues.Count == 0 && Settings.UnmodeledRootElements.Count == 0
        );
        foreach (var domain in UnmodeledDomains)
        {
            capabilities.Add(
                domain,
                new WordPresentationCapability(
                    domain,
                    WordPresentationCapabilityStatus.NotModeled,
                    DiagnosticCount: 0,
                    CoverageComplete: false
                )
            );
        }

        return new ReadOnlyDictionary<string, WordPresentationCapability>(capabilities);
    }

    private IReadOnlyList<string> BuildWarnings()
    {
        var warnings = Capabilities.Values
            .Where(capability =>
                capability.Status == WordPresentationCapabilityStatus.ResolvedWithDiagnostics
            )
            .Select(capability =>
                capability.Domain.ToUpperInvariant() + "_PRESENTATION_DIAGNOSTICS"
            )
            .Concat(UnmodeledDomains.Select(domain =>
                domain.ToUpperInvariant() + "_NOT_MODELED"
            ))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new ReadOnlyCollection<string>(warnings);
    }

    private static void AddCapability(
        IDictionary<string, WordPresentationCapability> capabilities,
        string domain,
        int diagnosticCount,
        bool coverageComplete
    )
    {
        capabilities.Add(
            domain,
            new WordPresentationCapability(
                domain,
                diagnosticCount == 0 && coverageComplete
                    ? WordPresentationCapabilityStatus.Resolved
                    : WordPresentationCapabilityStatus.ResolvedWithDiagnostics,
                diagnosticCount,
                coverageComplete
            )
        );
    }

    private static IReadOnlyDictionary<SemanticNodeId, TValue> FreezeIndex<TValue>(
        IEnumerable<TValue> values,
        Func<TValue, SemanticNodeId?> nodeId,
        string projectionName
    )
        where TValue : class
    {
        var index = new Dictionary<SemanticNodeId, TValue>();
        foreach (var value in values)
        {
            var id = nodeId(value);
            if (id is null)
            {
                continue;
            }
            if (!index.TryAdd(id.Value, value))
            {
                throw new WordPresentationSnapshotException(
                    $"The {projectionName} projection contains duplicate semantic node identities."
                );
            }
        }
        return new ReadOnlyDictionary<SemanticNodeId, TValue>(index);
    }

    private static void ValidateFingerprint(
        string expected,
        string actual,
        string projectionName
    )
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new WordPresentationSnapshotException(
                $"The {projectionName} projection does not match the presentation snapshot package."
            );
        }
    }
}

public sealed class WordPresentationSnapshotBuilder
{
    private readonly WordSemanticProjector _semanticProjector;

    public WordPresentationSnapshotBuilder(
        WordSemanticProjectionOptions? semanticOptions = null
    )
    {
        _semanticProjector = new WordSemanticProjector(semanticOptions);
    }

    public WordPresentationSnapshot Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _semanticProjector.Project(package, cancellationToken);
        var styles = new WordStyleGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );
        var reviews = new WordReviewGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );
        var equations = new WordEquationGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );
        var outline = new WordOutlineGraphBuilder().Build(
            package,
            document,
            styles,
            cancellationToken
        );
        var sections = new WordSectionGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            document,
            styles,
            cancellationToken
        );
        var listSequences = new WordListSequenceGraphBuilder().Build(
            package,
            document,
            styles,
            numbering,
            cancellationToken
        );
        var tables = new WordTableGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );
        var references = new WordReferenceGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );
        var figures = new WordFigureCaptionGraphBuilder().Build(
            package,
            document,
            references,
            styles,
            cancellationToken
        );
        var settings = new WordSettingsGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );

        return new WordPresentationSnapshot(
            package.Fingerprint,
            document,
            styles,
            reviews,
            equations,
            outline,
            sections,
            numbering,
            listSequences,
            tables,
            references,
            figures,
            settings
        );
    }
}

public sealed class WordPresentationSnapshotException : InvalidOperationException
{
    public WordPresentationSnapshotException(string message)
        : base(message) { }
}
