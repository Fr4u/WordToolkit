using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordTemplateStyleAlignmentAction
{
    AddStyle,
    ReplaceStyle,
    AlignDependencyClosure,
}

public sealed record WordTemplateStyleAlignmentCommand(
    string CandidateId,
    string ExpectedCandidateFingerprint
);

public sealed record WordTemplateStyleAlignmentOptions
{
    public static WordTemplateStyleAlignmentOptions Default { get; } = new();

    public int MaxCandidates { get; init; } = 16_384;

    public int MaxCommands { get; init; } = 64;

    public int MaxDependencyClosure { get; init; } = 1_024;

    public int MaxXmlPartBytes { get; init; } = 128 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCandidates));
        }
        if (MaxCommands is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommands));
        }
        if (MaxDependencyClosure is < 1 or > 16_384)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDependencyClosure));
        }
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
    }
}

public sealed record WordTemplateStyleAlignmentIssue(
    string Code,
    WordStyleIssueSeverity Severity,
    string? StyleId = null
);

public sealed record WordTemplateStyleAlignmentCandidate(
    string Id,
    string Fingerprint,
    string StyleId,
    WordStyleType StyleType,
    WordTemplateStyleAlignmentAction Action,
    IReadOnlyList<string> DependencyStyleIds,
    int AddedStyleCount,
    int ReplacedStyleCount,
    int AlreadyAlignedStyleCount,
    bool ThemeContextVerified,
    bool NumberingDependenciesVerified,
    bool StylesWithEffectsMirrored
);

public sealed class WordTemplateStyleAlignmentCatalog
{
    private readonly IReadOnlyDictionary<string, WordTemplateStyleAlignmentCandidate> _byId;

    internal WordTemplateStyleAlignmentCatalog(
        string targetPackageFingerprint,
        string templatePackageFingerprint,
        string targetStylesPartUri,
        string templateStylesPartUri,
        IReadOnlyList<WordTemplateStyleAlignmentCandidate> candidates,
        IReadOnlyList<WordTemplateStyleAlignmentIssue> issues,
        int alreadyAlignedStyleCount,
        bool stylesWithEffectsSymmetric
    )
    {
        TargetPackageFingerprint = targetPackageFingerprint;
        TemplatePackageFingerprint = templatePackageFingerprint;
        TargetStylesPartUri = targetStylesPartUri;
        TemplateStylesPartUri = templateStylesPartUri;
        Candidates = new ReadOnlyCollection<WordTemplateStyleAlignmentCandidate>(
            candidates.ToArray()
        );
        Issues = new ReadOnlyCollection<WordTemplateStyleAlignmentIssue>(issues.ToArray());
        AlreadyAlignedStyleCount = alreadyAlignedStyleCount;
        StylesWithEffectsSymmetric = stylesWithEffectsSymmetric;
        _byId = new ReadOnlyDictionary<string, WordTemplateStyleAlignmentCandidate>(
            candidates.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal)
        );
    }

    public string TargetPackageFingerprint { get; }

    public string TemplatePackageFingerprint { get; }

    public string TargetStylesPartUri { get; }

    public string TemplateStylesPartUri { get; }

    public IReadOnlyList<WordTemplateStyleAlignmentCandidate> Candidates { get; }

    public IReadOnlyList<WordTemplateStyleAlignmentIssue> Issues { get; }

    public int AlreadyAlignedStyleCount { get; }

    public bool StylesWithEffectsSymmetric { get; }

    public bool AnalysisExecutionComplete => true;

    public bool AlignmentCoverageComplete => true;

    public bool CanPlan => StylesWithEffectsSymmetric
        && !Issues.Any(issue =>
            issue.Severity == WordStyleIssueSeverity.Error && issue.StyleId is null
        );

    public bool TryGetCandidate(
        string id,
        out WordTemplateStyleAlignmentCandidate? candidate
    ) => _byId.TryGetValue(id, out candidate);
}

public sealed record WordTemplateStyleAlignmentPartChange(
    string PartUri,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed record WordTemplateStyleAlignmentValidation(
    bool CandidatePackageStructurallyValid,
    bool CandidateSemanticContentPreserved,
    bool SelectedStylesMatchTemplate,
    bool DependencyClosureResolved,
    bool TargetOnlyAndUnselectedStylesPreserved,
    bool StylesWithEffectsMirrored,
    bool ThemeContextVerified,
    bool NumberingDependenciesVerified,
    bool NoNewStyleIssues,
    bool NoNewNumberingIssues,
    bool UnplannedEntriesPreserved,
    bool ExactInverseVerified,
    int AddedStyleCount,
    int ReplacedStyleCount,
    int ChangedPartCount,
    int BeforeStyleIssueCount,
    int AfterStyleIssueCount,
    int BeforeNumberingIssueCount,
    int AfterNumberingIssueCount
)
{
    public bool Passed => CandidatePackageStructurallyValid
        && CandidateSemanticContentPreserved
        && SelectedStylesMatchTemplate
        && DependencyClosureResolved
        && TargetOnlyAndUnselectedStylesPreserved
        && StylesWithEffectsMirrored
        && ThemeContextVerified
        && NumberingDependenciesVerified
        && NoNewStyleIssues
        && NoNewNumberingIssues
        && UnplannedEntriesPreserved
        && ExactInverseVerified;
}

public sealed class WordTemplateStyleAlignmentPlan
{
    private readonly WordPackageTransactionCore _transaction;

    internal WordTemplateStyleAlignmentPlan(
        string planId,
        string targetPackageFingerprint,
        string templatePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<WordTemplateStyleAlignmentCandidate> candidates,
        IReadOnlyList<string> alignedStyleIds,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts,
        WordTemplateStyleAlignmentValidation validation,
        IReadOnlyList<string> safetyRules
    )
    {
        PlanId = planId;
        TargetPackageFingerprint = targetPackageFingerprint;
        TemplatePackageFingerprint = templatePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        Candidates = new ReadOnlyCollection<WordTemplateStyleAlignmentCandidate>(
            candidates.ToArray()
        );
        AlignedStyleIds = new ReadOnlyCollection<string>(alignedStyleIds.ToArray());
        Validation = validation;
        SafetyRules = new ReadOnlyCollection<string>(safetyRules.ToArray());
        _transaction = new WordPackageTransactionCore(
            targetPackageFingerprint,
            resultPackageFingerprint,
            parts
        );
        ChangedParts = new ReadOnlyCollection<WordTemplateStyleAlignmentPartChange>(
            _transaction.Parts
                .OrderBy(part => part.PartUri, StringComparer.Ordinal)
                .Select(part => new WordTemplateStyleAlignmentPartChange(
                    part.PartUri,
                    part.BeforeSha256,
                    part.AfterSha256,
                    part.BeforeContent.Length,
                    part.AfterContent.Length
                ))
                .ToArray()
        );
    }

    public string PlanId { get; }

    public string TargetPackageFingerprint { get; }

    public string TemplatePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public IReadOnlyList<WordTemplateStyleAlignmentCandidate> Candidates { get; }

    public IReadOnlyList<string> AlignedStyleIds { get; }

    public IReadOnlyList<WordTemplateStyleAlignmentPartChange> ChangedParts { get; }

    public WordTemplateStyleAlignmentValidation Validation { get; }

    public IReadOnlyList<string> SafetyRules { get; }

    public bool HasChanges => _transaction.HasChanges;

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentTarget) =>
        _transaction.CreateMutation(currentTarget);

    public OpcPackageMutationBuilder CreateInverseMutation(OpcPackageSnapshot appliedTarget) =>
        _transaction.CreateInverseMutation(appliedTarget);
}

public sealed class WordTemplateStyleAlignmentPlanner
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string CanonicalWordNamespace = "urn:wordtoolkit:wordprocessingml";

    private static readonly HashSet<string> ThemeAttributeNames = new(
        [
            "asciiTheme",
            "hAnsiTheme",
            "eastAsiaTheme",
            "cstheme",
            "themeColor",
            "themeTint",
            "themeShade",
            "themeFill",
            "themeFillTint",
            "themeFillShade",
        ],
        StringComparer.Ordinal
    );

    private readonly WordTemplateStyleAlignmentOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;
    private readonly OpcPackageReader _reader = new();
    private readonly OpcPackageSerializer _serializer = new();

    public WordTemplateStyleAlignmentPlanner(
        WordTemplateStyleAlignmentOptions? options = null
    )
    {
        _options = options ?? WordTemplateStyleAlignmentOptions.Default;
        _options.Validate();
        _xmlOptions = LosslessXmlOptions.Default with
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
            MaxTextCharacters = _options.MaxXmlPartBytes,
        };
    }

    public WordTemplateStyleAlignmentCatalog Inspect(
        OpcPackageSnapshot target,
        OpcPackageSnapshot template,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(template);
        cancellationToken.ThrowIfCancellationRequested();
        RequireStructuralPackage(target, "target");
        RequireStructuralPackage(template, "template");

        var targetSemantic = new WordSemanticProjector().Project(target, cancellationToken);
        var templateSemantic = new WordSemanticProjector().Project(
            template,
            cancellationToken
        );
        var targetGraph = new WordStyleGraphBuilder().Build(
            target,
            targetSemantic,
            cancellationToken
        );
        var templateGraph = new WordStyleGraphBuilder().Build(
            template,
            templateSemantic,
            cancellationToken
        );
        if (!targetGraph.HasStylesPart || targetGraph.StylesPartUri is null)
        {
            throw new WordSemanticEditException(
                "Template style alignment requires an existing target styles part."
            );
        }
        if (!templateGraph.HasStylesPart || templateGraph.StylesPartUri is null)
        {
            throw new WordSemanticEditException(
                "Template style alignment requires a template styles part."
            );
        }

        var targetStyles = ParseStylePart(target, targetGraph.StylesPartUri, cancellationToken);
        var templateStyles = ParseStylePart(
            template,
            templateGraph.StylesPartUri,
            cancellationToken
        );
        var effectsSymmetric = (targetGraph.StylesWithEffectsPartUri is null)
            == (templateGraph.StylesWithEffectsPartUri is null);
        StylePartModel? targetEffects = null;
        StylePartModel? templateEffects = null;
        if (effectsSymmetric && targetGraph.StylesWithEffectsPartUri is not null)
        {
            targetEffects = ParseStylePart(
                target,
                targetGraph.StylesWithEffectsPartUri,
                cancellationToken
            );
            templateEffects = ParseStylePart(
                template,
                templateGraph.StylesWithEffectsPartUri!,
                cancellationToken
            );
        }

        var issues = new List<WordTemplateStyleAlignmentIssue>();
        if (!effectsSymmetric)
        {
            issues.Add(new WordTemplateStyleAlignmentIssue(
                "TEMPLATE_STYLE_EFFECTS_PART_ASYMMETRIC",
                WordStyleIssueSeverity.Error
            ));
        }
        AddGraphIssues(issues, targetGraph, "TARGET_");
        AddGraphIssues(issues, templateGraph, "TEMPLATE_");
        var targetNumbering = new WordNumberingGraphBuilder().Build(
            target,
            targetSemantic,
            targetGraph,
            cancellationToken
        );
        var templateNumbering = new WordNumberingGraphBuilder().Build(
            template,
            templateSemantic,
            templateGraph,
            cancellationToken
        );
        var targetNumberingPart = ParseOptionalNumberingPart(
            target,
            targetNumbering.NumberingPartUri,
            cancellationToken
        );
        var templateNumberingPart = ParseOptionalNumberingPart(
            template,
            templateNumbering.NumberingPartUri,
            cancellationToken
        );
        var themeContextsEqual = ThemeContextsEqual(
            target,
            targetSemantic,
            template,
            templateSemantic,
            cancellationToken
        );

        var candidates = new List<WordTemplateStyleAlignmentCandidate>();
        var alreadyAligned = 0;
        foreach (var rootStyle in templateGraph.Styles.OrderBy(
            style => style.StyleId,
            StringComparer.Ordinal
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!effectsSymmetric)
            {
                break;
            }
            if (!TryBuildClosure(
                    rootStyle.StyleId,
                    templateGraph,
                    templateStyles,
                    templateNumbering,
                    out var closure,
                    out var failureCode,
                    cancellationToken
                ))
            {
                issues.Add(new WordTemplateStyleAlignmentIssue(
                    failureCode ?? "TEMPLATE_STYLE_DEPENDENCY_UNRESOLVED",
                    WordStyleIssueSeverity.Error,
                    rootStyle.StyleId
                ));
                continue;
            }
            if (!TargetTypesCompatible(closure, targetGraph, templateGraph))
            {
                issues.Add(new WordTemplateStyleAlignmentIssue(
                    "TEMPLATE_STYLE_TYPE_CONFLICT",
                    WordStyleIssueSeverity.Error,
                    rootStyle.StyleId
                ));
                continue;
            }
            var themeDependent = closure.Any(id =>
                UsesTheme(templateStyles.Styles[id])
                || templateEffects?.Styles.GetValueOrDefault(id) is { } effect
                    && UsesTheme(effect)
            );
            if (themeDependent && !themeContextsEqual)
            {
                issues.Add(new WordTemplateStyleAlignmentIssue(
                    "TEMPLATE_STYLE_THEME_CONTEXT_MISMATCH",
                    WordStyleIssueSeverity.Error,
                    rootStyle.StyleId
                ));
                continue;
            }
            var numberingIds = CollectNumberIds(closure, templateStyles);
            if (!NumberingDependenciesEquivalent(
                    numberingIds,
                    targetNumbering,
                    targetNumberingPart,
                    templateNumbering,
                    templateNumberingPart
                ))
            {
                issues.Add(new WordTemplateStyleAlignmentIssue(
                    "TEMPLATE_STYLE_NUMBERING_DEPENDENCY_MISMATCH",
                    WordStyleIssueSeverity.Error,
                    rootStyle.StyleId
                ));
                continue;
            }

            var counts = CountAlignmentChanges(
                closure,
                targetStyles,
                templateStyles,
                targetEffects,
                templateEffects
            );
            if (counts.Added == 0 && counts.Replaced == 0)
            {
                alreadyAligned++;
                continue;
            }
            var rootExists = targetGraph.TryGetStyle(rootStyle.StyleId, out var targetRoot)
                && targetRoot is not null;
            var rootEquivalent = rootExists
                && ElementsEquivalent(
                    targetStyles.Styles[rootStyle.StyleId],
                    templateStyles.Styles[rootStyle.StyleId]
                );
            var action = !rootExists
                ? WordTemplateStyleAlignmentAction.AddStyle
                : rootEquivalent
                    ? WordTemplateStyleAlignmentAction.AlignDependencyClosure
                    : WordTemplateStyleAlignmentAction.ReplaceStyle;
            var fingerprint = CandidateFingerprint(
                target.Fingerprint,
                template.Fingerprint,
                rootStyle.StyleId,
                closure,
                targetStyles,
                templateStyles,
                targetEffects,
                templateEffects
            );
            candidates.Add(new WordTemplateStyleAlignmentCandidate(
                CandidateId(target.Fingerprint, template.Fingerprint, fingerprint),
                fingerprint,
                rootStyle.StyleId,
                rootStyle.Type,
                action,
                new ReadOnlyCollection<string>(
                    closure.Where(id => id != rootStyle.StyleId)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToArray()
                ),
                counts.Added,
                counts.Replaced,
                counts.AlreadyAligned,
                !themeDependent || themeContextsEqual,
                true,
                effectsSymmetric
            ));
            if (candidates.Count > _options.MaxCandidates)
            {
                throw new WordSemanticTransactionLimitException(
                    $"Template exposes more than {_options.MaxCandidates} style-alignment candidates."
                );
            }
        }

        return new WordTemplateStyleAlignmentCatalog(
            target.Fingerprint,
            template.Fingerprint,
            targetGraph.StylesPartUri,
            templateGraph.StylesPartUri,
            candidates,
            issues
                .DistinctBy(issue => (issue.Code, issue.Severity, issue.StyleId))
                .OrderBy(issue => issue.StyleId, StringComparer.Ordinal)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ToArray(),
            alreadyAligned,
            effectsSymmetric
        );
    }

    public WordTemplateStyleAlignmentPlan Plan(
        OpcPackageSnapshot target,
        OpcPackageSnapshot template,
        IReadOnlyList<WordTemplateStyleAlignmentCommand> commands,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(commands);
        ValidateCommands(commands);
        var catalog = Inspect(target, template, cancellationToken);
        if (!catalog.CanPlan)
        {
            throw new WordSemanticEditException(
                "Template style alignment is blocked by target or template graph evidence."
            );
        }
        var selected = new List<WordTemplateStyleAlignmentCandidate>(commands.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            if (!seen.Add(command.CandidateId))
            {
                throw new ArgumentException(
                    "Template style-alignment candidate IDs must be unique."
                );
            }
            if (!catalog.TryGetCandidate(command.CandidateId, out var candidate)
                || candidate is null)
            {
                throw new WordSemanticPreconditionException(
                    $"Template style-alignment candidate '{command.CandidateId}' disappeared."
                );
            }
            if (!string.Equals(
                    candidate.Fingerprint,
                    command.ExpectedCandidateFingerprint,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                throw new WordSemanticPreconditionException(
                    $"Template style-alignment candidate '{command.CandidateId}' changed."
                );
            }
            selected.Add(candidate);
        }
        var alignedIds = selected
            .SelectMany(candidate => candidate.DependencyStyleIds.Append(candidate.StyleId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var context = BuildProjectionContext(target, template, cancellationToken);
        var payloads = BuildPayloads(context, alignedIds, cancellationToken);
        if (payloads.Count == 0)
        {
            throw new WordSemanticPreconditionException(
                "Selected template style-alignment candidates no longer require a change."
            );
        }
        var projected = payloads.Values.ToDictionary(
            payload => payload.EntryName,
            payload => (ReadOnlyMemory<byte>)payload.AfterContent,
            StringComparer.Ordinal
        );
        var resultFingerprint = OpcPackageFingerprint.ComputeProjected(target, projected);
        var transaction = new WordPackageTransactionCore(
            target.Fingerprint,
            resultFingerprint,
            payloads
        );
        var candidatePackage = Materialize(
            target,
            transaction.CreateMutation(target),
            cancellationToken
        );
        var validation = ValidateCandidate(
            context,
            candidatePackage,
            transaction,
            alignedIds,
            cancellationToken
        );
        if (!validation.Passed)
        {
            throw new WordSemanticEditException(
                "Template style-alignment candidate failed validation: " + validation
            );
        }
        return new WordTemplateStyleAlignmentPlan(
            CreatePlanId(
                target.Fingerprint,
                template.Fingerprint,
                resultFingerprint,
                commands
            ),
            target.Fingerprint,
            template.Fingerprint,
            resultFingerprint,
            selected,
            alignedIds,
            payloads,
            validation,
            [
                "exact_target_template_and_candidate_fingerprints_required",
                "style_ids_not_localized_names_define_identity",
                "based_on_next_and_linked_style_closure_is_atomic",
                "theme_and_numbering_dependencies_must_already_be_equivalent",
                "styles_with_effects_is_mirrored_when_present",
                "target_only_and_unselected_styles_are_preserved",
                "template_is_never_modified_or_attached",
                "semantic_content_and_exact_inverse_are_verified",
            ]
        );
    }

    private ProjectionContext BuildProjectionContext(
        OpcPackageSnapshot target,
        OpcPackageSnapshot template,
        CancellationToken cancellationToken
    )
    {
        var targetSemantic = new WordSemanticProjector().Project(target, cancellationToken);
        var templateSemantic = new WordSemanticProjector().Project(
            template,
            cancellationToken
        );
        var targetStyles = new WordStyleGraphBuilder().Build(
            target,
            targetSemantic,
            cancellationToken
        );
        var templateStyles = new WordStyleGraphBuilder().Build(
            template,
            templateSemantic,
            cancellationToken
        );
        var targetMain = ParseStylePart(target, targetStyles.StylesPartUri!, cancellationToken);
        var templateMain = ParseStylePart(
            template,
            templateStyles.StylesPartUri!,
            cancellationToken
        );
        StylePartModel? targetEffects = null;
        StylePartModel? templateEffects = null;
        if (targetStyles.StylesWithEffectsPartUri is not null)
        {
            targetEffects = ParseStylePart(
                target,
                targetStyles.StylesWithEffectsPartUri,
                cancellationToken
            );
            templateEffects = ParseStylePart(
                template,
                templateStyles.StylesWithEffectsPartUri!,
                cancellationToken
            );
        }
        var targetNumbering = new WordNumberingGraphBuilder().Build(
            target,
            targetSemantic,
            targetStyles,
            cancellationToken
        );
        var templateNumbering = new WordNumberingGraphBuilder().Build(
            template,
            templateSemantic,
            templateStyles,
            cancellationToken
        );
        var targetNumberingPart = ParseOptionalNumberingPart(
            target,
            targetNumbering.NumberingPartUri,
            cancellationToken
        );
        var templateNumberingPart = ParseOptionalNumberingPart(
            template,
            templateNumbering.NumberingPartUri,
            cancellationToken
        );
        return new ProjectionContext(
            target,
            template,
            targetSemantic,
            templateSemantic,
            targetStyles,
            templateStyles,
            targetNumbering,
            templateNumbering,
            targetNumberingPart,
            templateNumberingPart,
            targetMain,
            templateMain,
            targetEffects,
            templateEffects,
            ThemeContextsEqual(
                target,
                targetSemantic,
                template,
                templateSemantic,
                cancellationToken
            )
        );
    }

    private IReadOnlyDictionary<string, WordPackagePartPayload> BuildPayloads(
        ProjectionContext context,
        IReadOnlyCollection<string> alignedIds,
        CancellationToken cancellationToken
    )
    {
        var payloads = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal);
        AddAlignedPartPayload(
            payloads,
            context.Target,
            context.TargetMain,
            context.TemplateMain,
            alignedIds,
            cancellationToken
        );
        if (context.TargetEffects is not null && context.TemplateEffects is not null)
        {
            AddAlignedPartPayload(
                payloads,
                context.Target,
                context.TargetEffects,
                context.TemplateEffects,
                alignedIds,
                cancellationToken
            );
        }
        return payloads;
    }

    private static void AddAlignedPartPayload(
        IDictionary<string, WordPackagePartPayload> payloads,
        OpcPackageSnapshot targetPackage,
        StylePartModel target,
        StylePartModel template,
        IReadOnlyCollection<string> alignedIds,
        CancellationToken cancellationToken
    )
    {
        var patches = new List<XmlSourcePatch>();
        var additions = new List<string>();
        foreach (var styleId in alignedIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetExists = target.Styles.TryGetValue(styleId, out var targetElement);
            var templateExists = template.Styles.TryGetValue(styleId, out var templateElement);
            if (!templateExists)
            {
                if (targetExists)
                {
                    patches.Add(target.Source.CreateElementRemovalPatch(
                        target.Source.GetElementOrdinal(targetElement!)
                    ));
                }
                continue;
            }
            var translated = TranslateWordNamespace(
                templateElement!,
                target.WordNamespace,
                template.RootNamespaceDeclarations
            );
            if (targetExists)
            {
                if (!ElementsEquivalent(targetElement!, translated))
                {
                    patches.Add(target.Source.CreateElementReplacementPatch(
                        target.Source.GetElementOrdinal(targetElement!),
                        translated.ToString(SaveOptions.DisableFormatting)
                    ));
                }
            }
            else
            {
                additions.Add(translated.ToString(SaveOptions.DisableFormatting));
            }
        }
        if (additions.Count != 0)
        {
            patches.Add(target.Source.CreateElementContentInsertionPatch(
                target.Source.GetElementOrdinal(target.Root),
                string.Concat(additions),
                XmlContentInsertionPosition.Append
            ));
        }
        if (patches.Count == 0)
        {
            return;
        }
        var changed = target.Source.ApplyPatches(
            patches,
            target.Part.Entry.Sha256,
            cancellationToken
        );
        payloads.Add(target.Part.Uri, new WordPackagePartPayload(
            target.Part.Uri,
            target.Part.Entry.Name,
            target.Part.Entry.Content.ToArray(),
            changed
        ));
    }

    private WordTemplateStyleAlignmentValidation ValidateCandidate(
        ProjectionContext before,
        OpcPackageSnapshot candidate,
        WordPackageTransactionCore transaction,
        IReadOnlyCollection<string> alignedIds,
        CancellationToken cancellationToken
    )
    {
        var candidateSemantic = new WordSemanticProjector().Project(
            candidate,
            cancellationToken
        );
        var candidateStyles = new WordStyleGraphBuilder().Build(
            candidate,
            candidateSemantic,
            cancellationToken
        );
        var candidateNumbering = new WordNumberingGraphBuilder().Build(
            candidate,
            candidateSemantic,
            candidateStyles,
            cancellationToken
        );
        var candidateNumberingPart = ParseOptionalNumberingPart(
            candidate,
            candidateNumbering.NumberingPartUri,
            cancellationToken
        );
        var candidateMain = ParseStylePart(
            candidate,
            candidateStyles.StylesPartUri!,
            cancellationToken
        );
        StylePartModel? candidateEffects = null;
        if (candidateStyles.StylesWithEffectsPartUri is not null)
        {
            candidateEffects = ParseStylePart(
                candidate,
                candidateStyles.StylesWithEffectsPartUri,
                cancellationToken
            );
        }
        var selectedMatch = alignedIds.All(styleId =>
            candidateMain.Styles.TryGetValue(styleId, out var actual)
            && before.TemplateMain.Styles.TryGetValue(styleId, out var expected)
            && ElementsEquivalent(actual, expected)
        );
        var effectsMatch = before.TargetEffects is null
            ? candidateEffects is null
            : candidateEffects is not null
                && alignedIds.All(styleId =>
                {
                    var actualExists = candidateEffects.Styles.TryGetValue(
                        styleId,
                        out var actual
                    );
                    var expectedExists = before.TemplateEffects!.Styles.TryGetValue(
                        styleId,
                        out var expected
                    );
                    return actualExists == expectedExists
                        && (!actualExists || ElementsEquivalent(actual!, expected!));
                });
        var unselectedPreserved = before.TargetMain.Styles.All(pair =>
            alignedIds.Contains(pair.Key, StringComparer.Ordinal)
            || candidateMain.Styles.TryGetValue(pair.Key, out var actual)
                && ElementsEquivalent(pair.Value, actual)
        ) && (before.TargetEffects is null
            || before.TargetEffects.Styles.All(pair =>
                alignedIds.Contains(pair.Key, StringComparer.Ordinal)
                || candidateEffects!.Styles.TryGetValue(pair.Key, out var actual)
                    && ElementsEquivalent(pair.Value, actual)
            ));
        var diff = new WordSemanticDiffEngine().Compare(
            before.Target,
            before.TargetSemantic,
            candidate,
            candidateSemantic,
            cancellationToken
        );
        var changedParts = transaction.Parts.Select(part => part.PartUri)
            .ToHashSet(StringComparer.Ordinal);
        var inverse = Materialize(
            candidate,
            transaction.CreateInverseMutation(candidate),
            cancellationToken
        );
        var added = alignedIds.Count(styleId =>
            !before.TargetMain.Styles.ContainsKey(styleId)
            && candidateMain.Styles.ContainsKey(styleId)
        );
        var replaced = alignedIds.Count(styleId =>
            before.TargetMain.Styles.TryGetValue(styleId, out var oldElement)
            && candidateMain.Styles.TryGetValue(styleId, out var newElement)
            && !ElementsEquivalent(oldElement, newElement)
        );
        var themeDependent = alignedIds.Any(styleId =>
            UsesTheme(before.TemplateMain.Styles[styleId])
            || before.TemplateEffects?.Styles.GetValueOrDefault(styleId) is { } effect
                && UsesTheme(effect)
        );
        var numberIds = CollectNumberIds(alignedIds, before.TemplateMain);
        var numberingDependenciesVerified = NumberingDependenciesEquivalent(
            numberIds,
            candidateNumbering,
            candidateNumberingPart,
            before.TemplateNumbering,
            before.TemplateNumberingPart
        );
        return new WordTemplateStyleAlignmentValidation(
            CandidatePackageStructurallyValid: candidate.IsStructurallyValid,
            CandidateSemanticContentPreserved: diff.SemanticallyEquivalent
                && diff.MatchingComplete,
            SelectedStylesMatchTemplate: selectedMatch,
            DependencyClosureResolved: alignedIds.All(styleId =>
                candidateStyles.TryGetStyle(styleId, out _)
            ),
            TargetOnlyAndUnselectedStylesPreserved: unselectedPreserved,
            StylesWithEffectsMirrored: effectsMatch,
            ThemeContextVerified: !themeDependent || before.ThemeContextsEqual,
            NumberingDependenciesVerified: numberingDependenciesVerified,
            NoNewStyleIssues: NoNewIssues(
                before.TargetStyles.Issues.Select(StyleIssueIdentity),
                candidateStyles.Issues.Select(StyleIssueIdentity)
            ),
            NoNewNumberingIssues: NoNewIssues(
                before.TargetNumbering.Issues.Select(NumberingIssueIdentity),
                candidateNumbering.Issues.Select(NumberingIssueIdentity)
            ),
            UnplannedEntriesPreserved: UnplannedEntriesPreserved(
                before.Target,
                candidate,
                changedParts
            ),
            ExactInverseVerified: string.Equals(
                inverse.Fingerprint,
                before.Target.Fingerprint,
                StringComparison.Ordinal
            ),
            AddedStyleCount: added,
            ReplacedStyleCount: replaced,
            ChangedPartCount: changedParts.Count,
            BeforeStyleIssueCount: before.TargetStyles.Issues.Count,
            AfterStyleIssueCount: candidateStyles.Issues.Count,
            BeforeNumberingIssueCount: before.TargetNumbering.Issues.Count,
            AfterNumberingIssueCount: candidateNumbering.Issues.Count
        );
    }

    private bool TryBuildClosure(
        string rootStyleId,
        WordStyleGraph graph,
        StylePartModel styles,
        WordNumberingGraph numbering,
        out IReadOnlyList<string> closure,
        out string? failureCode,
        CancellationToken cancellationToken
    )
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(rootStyleId);
        failureCode = null;
        while (queue.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var styleId = queue.Dequeue();
            if (!result.Add(styleId))
            {
                continue;
            }
            if (result.Count > _options.MaxDependencyClosure)
            {
                failureCode = "TEMPLATE_STYLE_DEPENDENCY_LIMIT";
                closure = Array.Empty<string>();
                return false;
            }
            if (!graph.TryGetStyle(styleId, out var style) || style is null
                || !styles.Styles.ContainsKey(styleId))
            {
                failureCode = "TEMPLATE_STYLE_DEPENDENCY_MISSING";
                closure = Array.Empty<string>();
                return false;
            }
            foreach (var reference in new[]
            {
                style.BasedOnStyleId,
                style.NextStyleId,
                style.LinkedStyleId,
            }.Where(reference => reference is not null))
            {
                queue.Enqueue(reference!);
            }
            foreach (var numberId in CollectNumberIds([styleId], styles))
            {
                if (!numbering.TryGetInstance(numberId, out var instance)
                    || instance is null
                    || !numbering.TryGetAbstractDefinition(
                        instance.AbstractNumberId,
                        out var definition
                    )
                    || definition is null)
                {
                    failureCode = "TEMPLATE_STYLE_NUMBERING_DEPENDENCY_MISSING";
                    closure = Array.Empty<string>();
                    return false;
                }
                foreach (var linkedStyleId in new[]
                {
                    definition.NumberingStyleLinkId,
                    definition.StyleLinkId,
                }.Concat(definition.Levels.Select(level => level.ParagraphStyleId))
                    .Where(value => value is not null))
                {
                    queue.Enqueue(linkedStyleId!);
                }
            }
        }
        closure = result.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool TargetTypesCompatible(
        IEnumerable<string> closure,
        WordStyleGraph target,
        WordStyleGraph template
    ) => closure.All(styleId =>
    {
        if (!target.TryGetStyle(styleId, out var targetStyle) || targetStyle is null)
        {
            return true;
        }
        return template.TryGetStyle(styleId, out var templateStyle)
            && templateStyle is not null
            && targetStyle.Type == templateStyle.Type;
    });

    private static AlignmentCounts CountAlignmentChanges(
        IEnumerable<string> closure,
        StylePartModel target,
        StylePartModel template,
        StylePartModel? targetEffects,
        StylePartModel? templateEffects
    )
    {
        var added = 0;
        var replaced = 0;
        var aligned = 0;
        foreach (var styleId in closure)
        {
            var targetExists = target.Styles.TryGetValue(styleId, out var targetElement);
            var templateElement = template.Styles[styleId];
            var mainEquivalent = targetExists
                && ElementsEquivalent(targetElement!, templateElement);
            var effectsEquivalent = EffectsEquivalent(
                styleId,
                targetEffects,
                templateEffects
            );
            if (!targetExists)
            {
                added++;
            }
            else if (!mainEquivalent || !effectsEquivalent)
            {
                replaced++;
            }
            else
            {
                aligned++;
            }
        }
        return new AlignmentCounts(added, replaced, aligned);
    }

    private static bool EffectsEquivalent(
        string styleId,
        StylePartModel? target,
        StylePartModel? template
    )
    {
        if (target is null || template is null)
        {
            return target is null && template is null;
        }
        var targetExists = target.Styles.TryGetValue(styleId, out var targetElement);
        var templateExists = template.Styles.TryGetValue(styleId, out var templateElement);
        return targetExists == templateExists
            && (!targetExists || ElementsEquivalent(targetElement!, templateElement!));
    }

    private StylePartModel ParseStylePart(
        OpcPackageSnapshot package,
        string partUri,
        CancellationToken cancellationToken
    )
    {
        if (!package.Parts.TryGetValue(partUri, out var part))
        {
            throw new WordSemanticPreconditionException(
                $"Style part '{partUri}' disappeared during alignment inspection."
            );
        }
        var source = ParseXml(part.Entry.Content, partUri, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (root is null || !IsWordNamespace(root.Name.NamespaceName)
            || root.Name.LocalName != "styles")
        {
            throw new WordSemanticEditException(
                $"Style part '{partUri}' does not have a Word w:styles root."
            );
        }
        var w = root.Name.Namespace;
        var groups = root.Elements(w + "style")
            .Select(element => new
            {
                Element = element,
                Id = element.Attribute(w + "styleId")?.Value,
            })
            .ToArray();
        if (groups.Any(item => string.IsNullOrWhiteSpace(item.Id))
            || groups.GroupBy(item => item.Id!, StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
        {
            throw new WordSemanticEditException(
                $"Style part '{partUri}' contains a missing or duplicate style ID."
            );
        }
        var declarations = root.AncestorsAndSelf()
            .Reverse()
            .SelectMany(element => element.Attributes().Where(attribute =>
                attribute.IsNamespaceDeclaration
            ))
            .GroupBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key == "xmlns" ? string.Empty : group.Key,
                group => group.Last().Value,
                StringComparer.Ordinal
            );
        return new StylePartModel(
            part,
            source,
            root,
            w.NamespaceName,
            groups.ToDictionary(
                item => item.Id!,
                item => item.Element,
                StringComparer.Ordinal
            ),
            declarations
        );
    }

    private StylePartModel? ParseOptionalNumberingPart(
        OpcPackageSnapshot package,
        string? partUri,
        CancellationToken cancellationToken
    )
    {
        if (partUri is null)
        {
            return null;
        }
        if (!package.Parts.TryGetValue(partUri, out var part))
        {
            throw new WordSemanticPreconditionException(
                $"Numbering part '{partUri}' disappeared during template inspection."
            );
        }
        var source = ParseXml(part.Entry.Content, partUri, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (root is null || !IsWordNamespace(root.Name.NamespaceName)
            || root.Name.LocalName != "numbering")
        {
            throw new WordSemanticEditException(
                $"Numbering part '{partUri}' does not have a Word w:numbering root."
            );
        }
        return new StylePartModel(
            part,
            source,
            root,
            root.Name.NamespaceName,
            new Dictionary<string, XElement>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
    }

    private LosslessXmlDocument ParseXml(
        ReadOnlyMemory<byte> content,
        string partUri,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return LosslessXmlDocument.Parse(content, _xmlOptions, cancellationToken);
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                $"XML part '{partUri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private static IReadOnlyList<int> CollectNumberIds(
        IEnumerable<string> styleIds,
        StylePartModel styles
    ) => styleIds.SelectMany(styleId => styles.Styles[styleId]
            .DescendantsAndSelf()
            .Where(element => IsWordElement(element, "numId"))
            .Select(element => element.Attributes().FirstOrDefault(attribute =>
                IsWordNamespace(attribute.Name.NamespaceName)
                && attribute.Name.LocalName == "val"
            )?.Value)
            .Where(value => int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _
            ))
            .Select(value => int.Parse(value!, CultureInfo.InvariantCulture)))
        .Distinct()
        .Order()
        .ToArray();

    private static bool NumberingDependenciesEquivalent(
        IReadOnlyList<int> numberIds,
        WordNumberingGraph targetGraph,
        StylePartModel? targetPart,
        WordNumberingGraph templateGraph,
        StylePartModel? templatePart
    )
    {
        if (numberIds.Count == 0)
        {
            return true;
        }
        if (targetPart is null || templatePart is null)
        {
            return false;
        }
        foreach (var numberId in numberIds)
        {
            if (!targetGraph.TryGetInstance(numberId, out var targetInstance)
                || targetInstance is null
                || !templateGraph.TryGetInstance(numberId, out var templateInstance)
                || templateInstance is null
                || targetInstance.AbstractNumberId != templateInstance.AbstractNumberId
                || !targetGraph.TryGetAbstractDefinition(
                    targetInstance.AbstractNumberId,
                    out var targetAbstract
                )
                || targetAbstract is null
                || !templateGraph.TryGetAbstractDefinition(
                    templateInstance.AbstractNumberId,
                    out var templateAbstract
                )
                || templateAbstract is null)
            {
                return false;
            }
            if (targetAbstract.Levels.Any(level => level.PictureBulletId is not null)
                || templateAbstract.Levels.Any(level => level.PictureBulletId is not null))
            {
                return false;
            }
            var targetInstanceElement = ElementAtOrdinal(
                targetPart.Source,
                targetInstance.SourceElementOrdinal
            );
            var templateInstanceElement = ElementAtOrdinal(
                templatePart.Source,
                templateInstance.SourceElementOrdinal
            );
            var targetAbstractElement = ElementAtOrdinal(
                targetPart.Source,
                targetAbstract.SourceElementOrdinal
            );
            var templateAbstractElement = ElementAtOrdinal(
                templatePart.Source,
                templateAbstract.SourceElementOrdinal
            );
            if (!ElementsEquivalent(targetInstanceElement, templateInstanceElement)
                || !ElementsEquivalent(targetAbstractElement, templateAbstractElement))
            {
                return false;
            }
        }
        return true;
    }

    private static XElement ElementAtOrdinal(LosslessXmlDocument source, int ordinal) =>
        source.ParsedDocument.Root!.DescendantsAndSelf().ElementAt(ordinal);

    private static bool ThemeContextsEqual(
        OpcPackageSnapshot target,
        WordSemanticDocument targetSemantic,
        OpcPackageSnapshot template,
        WordSemanticDocument templateSemantic,
        CancellationToken cancellationToken
    )
    {
        var targetTheme = new WordThemeGraphBuilder().Build(
            target,
            targetSemantic,
            cancellationToken
        );
        var templateTheme = new WordThemeGraphBuilder().Build(
            template,
            templateSemantic,
            cancellationToken
        );
        if (targetTheme.ThemePartUri is null || templateTheme.ThemePartUri is null)
        {
            return targetTheme.ThemePartUri is null && templateTheme.ThemePartUri is null;
        }
        if (!target.Parts.TryGetValue(targetTheme.ThemePartUri, out var targetPart)
            || !template.Parts.TryGetValue(templateTheme.ThemePartUri, out var templatePart))
        {
            return false;
        }
        var targetSettings = new WordSettingsGraphBuilder().Build(
            target,
            targetSemantic,
            cancellationToken
        );
        var templateSettings = new WordSettingsGraphBuilder().Build(
            template,
            templateSemantic,
            cancellationToken
        );
        return CanonicalXmlFingerprint(targetPart.Entry.Content)
                == CanonicalXmlFingerprint(templatePart.Entry.Content)
            && ThemeLanguagesEqual(
                targetSettings.ThemeFontLanguages,
                templateSettings.ThemeFontLanguages
            );
    }

    private static bool ThemeLanguagesEqual(
        WordThemeFontLanguages? left,
        WordThemeFontLanguages? right
    ) => left is null || right is null
        ? left is null && right is null
        : string.Equals(left.Latin, right.Latin, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.EastAsian, right.EastAsian, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                left.ComplexScript,
                right.ComplexScript,
                StringComparison.OrdinalIgnoreCase
            );

    private static string CanonicalXmlFingerprint(ReadOnlyMemory<byte> content)
    {
        var source = LosslessXmlDocument.Parse(content);
        var builder = new StringBuilder();
        AppendCanonicalNode(builder, source.ParsedDocument);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static bool UsesTheme(XElement element) => element.DescendantsAndSelf()
        .Attributes()
        .Any(attribute => ThemeAttributeNames.Contains(attribute.Name.LocalName));

    private static bool ElementsEquivalent(XElement left, XElement right) => string.Equals(
        CanonicalElement(left),
        CanonicalElement(right),
        StringComparison.Ordinal
    );

    private static string CanonicalElement(XElement element)
    {
        var builder = new StringBuilder();
        AppendCanonicalElement(builder, element);
        return builder.ToString();
    }

    private static void AppendCanonicalNode(StringBuilder builder, XNode node)
    {
        switch (node)
        {
            case XDocument document:
                foreach (var child in document.Nodes())
                {
                    AppendCanonicalNode(builder, child);
                }
                break;
            case XElement element:
                AppendCanonicalElement(builder, element);
                break;
            case XText text:
                AppendValue(builder, "#text");
                AppendValue(builder, text.Value);
                break;
            case XComment comment:
                AppendValue(builder, "#comment");
                AppendValue(builder, comment.Value);
                break;
            case XProcessingInstruction instruction:
                AppendValue(builder, "#pi");
                AppendValue(builder, instruction.Target);
                AppendValue(builder, instruction.Data);
                break;
        }
    }

    private static void AppendCanonicalElement(StringBuilder builder, XElement element)
    {
        AppendValue(builder, CanonicalNamespace(element.Name.NamespaceName));
        AppendValue(builder, element.Name.LocalName);
        foreach (var attribute in element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .OrderBy(attribute => CanonicalNamespace(attribute.Name.NamespaceName), StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal))
        {
            AppendValue(builder, CanonicalNamespace(attribute.Name.NamespaceName));
            AppendValue(builder, attribute.Name.LocalName);
            AppendValue(builder, attribute.Value);
        }
        builder.Append('[');
        foreach (var node in element.Nodes())
        {
            AppendCanonicalNode(builder, node);
        }
        builder.Append(']');
    }

    private static string CanonicalNamespace(string namespaceName) =>
        IsWordNamespace(namespaceName) ? CanonicalWordNamespace : namespaceName;

    private static void AppendValue(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);

    private static XElement TranslateWordNamespace(
        XElement source,
        string targetWordNamespace,
        IReadOnlyDictionary<string, string> rootDeclarations
    )
    {
        var translated = TranslateElement(source, targetWordNamespace);
        foreach (var declaration in rootDeclarations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(declaration.Key)
                || declaration.Key == "w"
                || IsWordNamespace(declaration.Value)
                || translated.GetNamespaceOfPrefix(declaration.Key) is not null)
            {
                continue;
            }
            translated.Add(new XAttribute(
                XNamespace.Xmlns + declaration.Key,
                declaration.Value
            ));
        }
        return translated;
    }

    private static XElement TranslateElement(XElement source, string targetWordNamespace)
    {
        XName TranslateName(XName name) => IsWordNamespace(name.NamespaceName)
            ? XName.Get(name.LocalName, targetWordNamespace)
            : name;
        var result = new XElement(TranslateName(source.Name));
        foreach (var attribute in source.Attributes().Where(attribute =>
            !attribute.IsNamespaceDeclaration
        ))
        {
            result.Add(new XAttribute(TranslateName(attribute.Name), attribute.Value));
        }
        foreach (var node in source.Nodes())
        {
            result.Add(node switch
            {
                XElement element => TranslateElement(element, targetWordNamespace),
                XCData cdata => new XCData(cdata.Value),
                XText text => new XText(text.Value),
                XComment comment => new XComment(comment.Value),
                XProcessingInstruction instruction => new XProcessingInstruction(
                    instruction.Target,
                    instruction.Data
                ),
                _ => new XText(node.ToString()),
            });
        }
        return result;
    }

    private static string CandidateFingerprint(
        string targetFingerprint,
        string templateFingerprint,
        string rootStyleId,
        IReadOnlyList<string> closure,
        StylePartModel target,
        StylePartModel template,
        StylePartModel? targetEffects,
        StylePartModel? templateEffects
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-template-style-alignment-candidate-v1");
        AppendHash(hash, targetFingerprint);
        AppendHash(hash, templateFingerprint);
        AppendHash(hash, rootStyleId);
        foreach (var styleId in closure.OrderBy(id => id, StringComparer.Ordinal))
        {
            AppendHash(hash, styleId);
            AppendHash(hash, CanonicalElement(template.Styles[styleId]));
            AppendHash(hash, target.Styles.TryGetValue(styleId, out var targetElement)
                ? CanonicalElement(targetElement)
                : "<missing>");
            AppendHash(hash, templateEffects?.Styles.TryGetValue(styleId, out var te) == true
                ? CanonicalElement(te)
                : "<missing-effects>");
            AppendHash(hash, targetEffects?.Styles.TryGetValue(styleId, out var ta) == true
                ? CanonicalElement(ta)
                : "<missing-effects>");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string CandidateId(
        string targetFingerprint,
        string templateFingerprint,
        string candidateFingerprint
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-template-style-alignment-candidate-id-v1");
        AppendHash(hash, targetFingerprint);
        AppendHash(hash, templateFingerprint);
        AppendHash(hash, candidateFingerprint);
        return "wtsa_" + Convert.ToHexString(hash.GetHashAndReset().AsSpan(0, 12))
            .ToLowerInvariant();
    }

    private static string CreatePlanId(
        string targetFingerprint,
        string templateFingerprint,
        string resultFingerprint,
        IReadOnlyList<WordTemplateStyleAlignmentCommand> commands
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-template-style-alignment-plan-v1");
        AppendHash(hash, targetFingerprint);
        AppendHash(hash, templateFingerprint);
        AppendHash(hash, resultFingerprint);
        foreach (var command in commands.OrderBy(item => item.CandidateId, StringComparer.Ordinal))
        {
            AppendHash(hash, command.CandidateId);
            AppendHash(hash, command.ExpectedCandidateFingerprint.ToLowerInvariant());
        }
        return "wtsaplan_" + Convert.ToBase64String(hash.GetHashAndReset().AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AppendHash(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private void ValidateCommands(IReadOnlyList<WordTemplateStyleAlignmentCommand> commands)
    {
        if (commands.Count is < 1 || commands.Count > _options.MaxCommands)
        {
            throw new ArgumentException(
                $"Template style alignment requires between 1 and {_options.MaxCommands} commands."
            );
        }
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (command.CandidateId.Length != 29
                || !command.CandidateId.StartsWith("wtsa_", StringComparison.Ordinal)
                || !command.CandidateId[5..].All(character =>
                    character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                throw new ArgumentException(
                    "A valid template style-alignment candidate ID is required."
                );
            }
            if (command.ExpectedCandidateFingerprint.Length != 64
                || !command.ExpectedCandidateFingerprint.All(Uri.IsHexDigit))
            {
                throw new ArgumentException(
                    "The expected template style-alignment candidate fingerprint must contain 64 hexadecimal characters."
                );
            }
        }
    }

    private static void RequireStructuralPackage(OpcPackageSnapshot package, string role)
    {
        if (!package.IsStructurallyValid)
        {
            throw new WordSemanticEditException(
                $"The {role} package is not structurally valid OPC."
            );
        }
    }

    private static void AddGraphIssues(
        ICollection<WordTemplateStyleAlignmentIssue> output,
        WordStyleGraph graph,
        string prefix
    )
    {
        foreach (var issue in graph.Issues)
        {
            output.Add(new WordTemplateStyleAlignmentIssue(
                prefix + issue.Code,
                issue.Severity,
                issue.StyleId
            ));
        }
    }

    private static bool NoNewIssues(
        IEnumerable<string> before,
        IEnumerable<string> after
    )
    {
        var beforeCounts = before.GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return after.GroupBy(value => value, StringComparer.Ordinal).All(group =>
            beforeCounts.TryGetValue(group.Key, out var count) && group.Count() <= count
        );
    }

    private static string StyleIssueIdentity(WordStyleIssue issue) =>
        issue.Severity + "\u001f" + issue.Code + "\u001f" + issue.StyleId;

    private static string NumberingIssueIdentity(WordNumberingIssue issue) =>
        issue.Severity + "\u001f" + issue.Code + "\u001f"
            + issue.AbstractNumberId + "\u001f" + issue.NumberId;

    private static bool UnplannedEntriesPreserved(
        OpcPackageSnapshot before,
        OpcPackageSnapshot after,
        ISet<string> changedPartUris
    )
    {
        var beforeEntries = before.Entries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        var afterEntries = after.Entries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        if (!beforeEntries.Keys.Order().SequenceEqual(afterEntries.Keys.Order()))
        {
            return false;
        }
        return beforeEntries.All(pair =>
            pair.Value.PartUri is { } uri && changedPartUris.Contains(uri)
            || string.Equals(
                pair.Value.Sha256,
                afterEntries[pair.Key].Sha256,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    private OpcPackageSnapshot Materialize(
        OpcPackageSnapshot package,
        OpcPackageMutationBuilder mutation,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream();
        _serializer.Write(stream, mutation);
        stream.Position = 0;
        return _reader.Read(stream, cancellationToken);
    }

    private static bool IsWordElement(XElement element, string localName) =>
        IsWordNamespace(element.Name.NamespaceName) && element.Name.LocalName == localName;

    private static bool IsWordNamespace(string namespaceName) =>
        namespaceName is WordTransitionalNamespace or WordStrictNamespace;

    private sealed record StylePartModel(
        OpcPart Part,
        LosslessXmlDocument Source,
        XElement Root,
        string WordNamespace,
        IReadOnlyDictionary<string, XElement> Styles,
        IReadOnlyDictionary<string, string> RootNamespaceDeclarations
    );

    private sealed record ProjectionContext(
        OpcPackageSnapshot Target,
        OpcPackageSnapshot Template,
        WordSemanticDocument TargetSemantic,
        WordSemanticDocument TemplateSemantic,
        WordStyleGraph TargetStyles,
        WordStyleGraph TemplateStyles,
        WordNumberingGraph TargetNumbering,
        WordNumberingGraph TemplateNumbering,
        StylePartModel? TargetNumberingPart,
        StylePartModel? TemplateNumberingPart,
        StylePartModel TargetMain,
        StylePartModel TemplateMain,
        StylePartModel? TargetEffects,
        StylePartModel? TemplateEffects,
        bool ThemeContextsEqual
    );

    private sealed record AlignmentCounts(int Added, int Replaced, int AlreadyAligned);
}
