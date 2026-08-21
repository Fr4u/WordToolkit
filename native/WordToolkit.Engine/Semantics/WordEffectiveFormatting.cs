using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordFormattingSourceKind
{
    DocumentDefault,
    ParagraphStyle,
    NumberingLevel,
    CharacterStyle,
    DirectParagraphFormatting,
    DirectRunFormatting,
    Theme,
    FontTable,
}

public sealed record WordFormattingContribution(
    WordFormattingSourceKind SourceKind,
    string? StyleId,
    string SourcePartUri,
    int? SourceElementOrdinal,
    string DeclaredValue,
    string ResultingValue,
    int? NumberId = null,
    int? AbstractNumberId = null,
    int? LevelIndex = null,
    WordNumberingLevelSourceKind? NumberingLevelSourceKind = null,
    string? ThemeToken = null,
    string? ThemeColorSlot = null,
    WordThemeFontCollectionKind? ThemeFontCollection = null,
    WordThemeFontRole? ThemeFontRole = null,
    string? ThemeLanguageTag = null,
    string? ThemeScript = null,
    WordThemeFontResolutionKind? ThemeFontResolutionKind = null
);

public sealed class WordEffectiveFormattingProperty
{
    internal WordEffectiveFormattingProperty(
        string name,
        string value,
        bool isToggle,
        IReadOnlyList<WordFormattingContribution> contributions
    )
    {
        Name = name;
        Value = value;
        IsToggle = isToggle;
        Contributions = new ReadOnlyCollection<WordFormattingContribution>(
            contributions.ToArray()
        );
    }

    public string Name { get; }

    public string Value { get; }

    public bool IsToggle { get; }

    public IReadOnlyList<WordFormattingContribution> Contributions { get; }
}

public sealed class WordEffectiveFormatting
{
    internal WordEffectiveFormatting(
        SemanticNodeId nodeId,
        WordSemanticNodeKind nodeKind,
        SemanticNodeId paragraphNodeId,
        string sourcePartUri,
        string? paragraphStyleId,
        string? characterStyleId,
        WordResolvedNumberingLevel? numbering,
        bool numberingRemoved,
        IReadOnlyDictionary<string, WordEffectiveFormattingProperty> paragraphProperties,
        IReadOnlyDictionary<string, WordEffectiveFormattingProperty> runProperties,
        IReadOnlyList<string> unmodeledElements,
        IReadOnlyList<string> coverageOmissions,
        IReadOnlyList<string> compatibilityWarnings
    )
    {
        NodeId = nodeId;
        NodeKind = nodeKind;
        ParagraphNodeId = paragraphNodeId;
        SourcePartUri = sourcePartUri;
        ParagraphStyleId = paragraphStyleId;
        CharacterStyleId = characterStyleId;
        Numbering = numbering;
        NumberingRemoved = numberingRemoved;
        ParagraphProperties = new ReadOnlyDictionary<
            string,
            WordEffectiveFormattingProperty
        >(
            new Dictionary<string, WordEffectiveFormattingProperty>(
                paragraphProperties,
                StringComparer.Ordinal
            )
        );
        RunProperties = new ReadOnlyDictionary<
            string,
            WordEffectiveFormattingProperty
        >(
            new Dictionary<string, WordEffectiveFormattingProperty>(
                runProperties,
                StringComparer.Ordinal
            )
        );
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        CoverageOmissions = new ReadOnlyCollection<string>(
            coverageOmissions.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        CompatibilityWarnings = new ReadOnlyCollection<string>(
            compatibilityWarnings.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public SemanticNodeId NodeId { get; }

    public WordSemanticNodeKind NodeKind { get; }

    public SemanticNodeId ParagraphNodeId { get; }

    public string SourcePartUri { get; }

    public string? ParagraphStyleId { get; }

    public string? CharacterStyleId { get; }

    public WordResolvedNumberingLevel? Numbering { get; }

    public bool NumberingRemoved { get; }

    public IReadOnlyDictionary<string, WordEffectiveFormattingProperty> ParagraphProperties { get; }

    public IReadOnlyDictionary<string, WordEffectiveFormattingProperty> RunProperties { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public IReadOnlyList<string> CoverageOmissions { get; }

    public IReadOnlyList<string> CompatibilityWarnings { get; }

    public bool IsFullyResolved => CoverageOmissions.Count == 0
        && CompatibilityWarnings.Count == 0;
}

public sealed record WordEffectiveFormattingOptions
{
    public static WordEffectiveFormattingOptions Default { get; } = new();

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxAncestorDepth { get; init; } = 512;

    internal void Validate()
    {
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

public sealed class WordEffectiveFormattingResolver
{
    private static readonly HashSet<string> ToggleRunProperties =
    [
        "all_caps",
        "bold",
        "bold_complex_script",
        "emboss",
        "hidden",
        "imprint",
        "italic",
        "italic_complex_script",
        "outline",
        "shadow",
        "small_caps",
        "strike",
    ];

    private static readonly IReadOnlyList<IReadOnlyList<string>> RunCompositePropertyGroups =
    [
        ["font_ascii", "font_ascii_theme"],
        ["font_high_ansi", "font_high_ansi_theme"],
        ["font_east_asia", "font_east_asia_theme"],
        ["font_complex_script", "font_complex_script_theme"],
        ["color_value", "color_theme", "color_theme_tint", "color_theme_shade"],
        [
            "underline_value",
            "underline_color",
            "underline_theme",
            "underline_theme_tint",
            "underline_theme_shade",
        ],
        [
            "shading_pattern",
            "shading_color",
            "shading_fill",
            "shading_theme_color",
            "shading_theme_tint",
            "shading_theme_shade",
            "shading_theme_fill",
            "shading_theme_fill_tint",
            "shading_theme_fill_shade",
        ],
    ];

    private static readonly IReadOnlyList<IReadOnlyList<string>> ParagraphCompositePropertyGroups =
    [
        [
            "shading_pattern",
            "shading_color",
            "shading_fill",
            "shading_theme_color",
            "shading_theme_tint",
            "shading_theme_shade",
            "shading_theme_fill",
            "shading_theme_fill_tint",
            "shading_theme_fill_shade",
        ],
    ];

    private static readonly IReadOnlyList<ThemeFontBinding> ThemeFontBindings =
    [
        new("font_ascii_theme", "font_ascii_resolved", "font_ascii_document_font"),
        new("font_high_ansi_theme", "font_high_ansi_resolved", "font_high_ansi_document_font"),
        new("font_east_asia_theme", "font_east_asia_resolved", "font_east_asia_document_font"),
        new("font_complex_script_theme", "font_complex_script_resolved", "font_complex_script_document_font"),
    ];

    private static readonly IReadOnlyList<ThemeColorBinding> RunThemeColorBindings =
    [
        new(
            "color_theme",
            "color_theme_tint",
            "color_theme_shade",
            "color_value",
            "color_resolved_rgb"
        ),
        new(
            "underline_theme",
            "underline_theme_tint",
            "underline_theme_shade",
            "underline_color",
            "underline_resolved_rgb"
        ),
        new(
            "shading_theme_color",
            "shading_theme_tint",
            "shading_theme_shade",
            "shading_color",
            "shading_color_resolved_rgb"
        ),
        new(
            "shading_theme_fill",
            "shading_theme_fill_tint",
            "shading_theme_fill_shade",
            "shading_fill",
            "shading_fill_resolved_rgb"
        ),
    ];

    private static readonly IReadOnlyList<ThemeColorBinding> ParagraphThemeColorBindings =
    [
        new(
            "shading_theme_color",
            "shading_theme_tint",
            "shading_theme_shade",
            "shading_color",
            "shading_color_resolved_rgb"
        ),
        new(
            "shading_theme_fill",
            "shading_theme_fill_tint",
            "shading_theme_fill_shade",
            "shading_fill",
            "shading_fill_resolved_rgb"
        ),
    ];

    private readonly WordEffectiveFormattingOptions _options;

    public WordEffectiveFormattingResolver(
        WordEffectiveFormattingOptions? options = null
    )
    {
        _options = options ?? WordEffectiveFormattingOptions.Default;
        _options.Validate();
    }

    public WordEffectiveFormatting Resolve(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        SemanticNodeId nodeId,
        CancellationToken cancellationToken = default
    ) => ResolveCore(
        package,
        semanticDocument,
        styleGraph,
        numberingGraph: null,
        themeGraph: null,
        settingsGraph: null,
        fontTableGraph: null,
        nodeId,
        cancellationToken
    );

    public WordEffectiveFormatting Resolve(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        WordNumberingGraph numberingGraph,
        SemanticNodeId nodeId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(numberingGraph);
        return ResolveCore(
            package,
            semanticDocument,
            styleGraph,
            numberingGraph,
            themeGraph: null,
            settingsGraph: null,
            fontTableGraph: null,
            nodeId,
            cancellationToken
        );
    }

    public WordEffectiveFormatting Resolve(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        WordThemeGraph themeGraph,
        SemanticNodeId nodeId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(themeGraph);
        return ResolveCore(
            package,
            semanticDocument,
            styleGraph,
            numberingGraph: null,
            themeGraph,
            settingsGraph: null,
            fontTableGraph: null,
            nodeId,
            cancellationToken
        );
    }

    public WordEffectiveFormatting Resolve(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        WordNumberingGraph numberingGraph,
        WordThemeGraph themeGraph,
        SemanticNodeId nodeId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(numberingGraph);
        ArgumentNullException.ThrowIfNull(themeGraph);
        return ResolveCore(
            package,
            semanticDocument,
            styleGraph,
            numberingGraph,
            themeGraph,
            settingsGraph: null,
            fontTableGraph: null,
            nodeId,
            cancellationToken
        );
    }

    public WordEffectiveFormatting Resolve(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        WordNumberingGraph numberingGraph,
        WordThemeGraph themeGraph,
        WordSettingsGraph settingsGraph,
        WordFontTableGraph fontTableGraph,
        SemanticNodeId nodeId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(numberingGraph);
        ArgumentNullException.ThrowIfNull(themeGraph);
        ArgumentNullException.ThrowIfNull(settingsGraph);
        ArgumentNullException.ThrowIfNull(fontTableGraph);
        return ResolveCore(
            package,
            semanticDocument,
            styleGraph,
            numberingGraph,
            themeGraph,
            settingsGraph,
            fontTableGraph,
            nodeId,
            cancellationToken
        );
    }

    private WordEffectiveFormatting ResolveCore(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        WordNumberingGraph? numberingGraph,
        WordThemeGraph? themeGraph,
        WordSettingsGraph? settingsGraph,
        WordFontTableGraph? fontTableGraph,
        SemanticNodeId nodeId,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(styleGraph);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSnapshots(
            package,
            semanticDocument,
            styleGraph,
            numberingGraph,
            themeGraph,
            settingsGraph,
            fontTableGraph
        );
        if (!semanticDocument.TryGetNode(nodeId, out var selected) || selected is null)
        {
            throw new WordFormattingResolutionException(
                $"Semantic node '{nodeId}' does not exist in the supplied snapshot."
            );
        }

        if (selected.Kind is not WordSemanticNodeKind.Paragraph and not WordSemanticNodeKind.Run)
        {
            throw new WordFormattingResolutionException(
                $"Effective formatting requires a paragraph or run node, not {selected.Kind}."
            );
        }

        var paragraph = selected.Kind == WordSemanticNodeKind.Paragraph
            ? selected
            : FindParagraphAncestor(semanticDocument, selected);
        if (!string.Equals(paragraph.SourcePartUri, selected.SourcePartUri, StringComparison.Ordinal))
        {
            throw new WordFormattingResolutionException(
                "Run and owning paragraph resolve to different source parts."
            );
        }

        if (!package.Parts.TryGetValue(selected.SourcePartUri, out var part))
        {
            throw new WordFormattingResolutionException(
                $"Source part '{selected.SourcePartUri}' is missing from the package."
            );
        }

        var source = ParseSourcePart(part, cancellationToken);
        var paragraphElement = GetBoundElement(
            source,
            paragraph,
            "p"
        );
        var runElement = selected.Kind == WordSemanticNodeKind.Run
            ? GetBoundElement(source, selected, "r")
            : null;
        var w = paragraphElement.Name.Namespace;
        var paragraphPropertyElement = OptionalSingleChild(
            paragraphElement,
            w + "pPr",
            "paragraph properties"
        );
        var runPropertyElement = runElement is null
            ? null
            : OptionalSingleChild(runElement, w + "rPr", "run properties");
        var paragraphStyleId = ReferencedStyleId(
            paragraphPropertyElement,
            w + "pStyle",
            w
        );
        if (
            paragraphStyleId is null
            && styleGraph.DefaultStyleIds.TryGetValue(
                WordStyleType.Paragraph,
                out var defaultParagraphStyleId
            )
        )
        {
            paragraphStyleId = defaultParagraphStyleId;
        }

        var characterStyleId = ReferencedStyleId(
            runPropertyElement,
            w + "rStyle",
            w
        );
        var directParagraph = ReadDirectProperties(
            paragraphPropertyElement,
            WordStyleGraphBuilder.WordFormattingDomain.Paragraph
        );
        var directRun = ReadDirectProperties(
            runPropertyElement,
            WordStyleGraphBuilder.WordFormattingDomain.Run
        );
        var paragraphStates = new Dictionary<string, MutableProperty>(
            StringComparer.Ordinal
        );
        var runStates = new Dictionary<string, MutableProperty>(StringComparer.Ordinal);
        var unmodeled = new List<string>();
        var omissions = new List<string>
        {
            "application_defaults_for_unspecified_properties",
        };
        var warnings = new List<string>();

        ApplySet(
            paragraphStates,
            styleGraph.DefaultParagraphProperties,
            new FormattingSource(
                WordFormattingSourceKind.DocumentDefault,
                null,
                styleGraph.StylesPartUri ?? styleGraph.MainPartUri,
                null,
                StyleLevel: false
            ),
            isRunProperties: false
        );
        ApplySet(
            runStates,
            styleGraph.DefaultRunProperties,
            new FormattingSource(
                WordFormattingSourceKind.DocumentDefault,
                null,
                styleGraph.StylesPartUri ?? styleGraph.MainPartUri,
                null,
                StyleLevel: false
            ),
            isRunProperties: true
        );
        AddUnmodeled(
            unmodeled,
            "document_default:paragraph",
            styleGraph.DefaultParagraphProperties
        );
        AddUnmodeled(
            unmodeled,
            "document_default:run",
            styleGraph.DefaultRunProperties
        );

        if (paragraphStyleId is not null)
        {
            foreach (
                var style in ResolveStyleChain(
                    styleGraph,
                    paragraphStyleId,
                    WordStyleType.Paragraph
                )
            )
            {
                var sourceInfo = new FormattingSource(
                    WordFormattingSourceKind.ParagraphStyle,
                    style.StyleId,
                    styleGraph.StylesPartUri!,
                    style.SourceElementOrdinal,
                    StyleLevel: true
                );
                ApplySet(
                    paragraphStates,
                    style.ParagraphProperties,
                    sourceInfo,
                    isRunProperties: false
                );
                ApplySet(
                    runStates,
                    style.RunProperties,
                    sourceInfo,
                    isRunProperties: true
                );
                AddUnmodeled(
                    unmodeled,
                    $"paragraph_style:{style.StyleId}:paragraph",
                    style.ParagraphProperties
                );
                AddUnmodeled(
                    unmodeled,
                    $"paragraph_style:{style.StyleId}:run",
                    style.RunProperties
                );
            }
        }

        WordResolvedNumberingLevel? resolvedNumbering = null;
        var numberingRemoved = false;
        var numberingReference = ResolveNumberingReference(
            paragraphStates,
            directParagraph,
            numberingGraph,
            paragraphStyleId
        );
        if (numberingReference.NumberId is { } numberId)
        {
            numberingRemoved = numberId == 0;
            if (!numberingRemoved && numberingGraph is not null)
            {
                try
                {
                    resolvedNumbering = numberingGraph.ResolveLevel(
                        numberId,
                        numberingReference.LevelIndex
                    );
                }
                catch (WordNumberingResolutionException exception)
                {
                    throw new WordFormattingResolutionException(
                        "The paragraph's numbering level cannot be resolved safely.",
                        exception
                    );
                }

                var numberingSource = new FormattingSource(
                    WordFormattingSourceKind.NumberingLevel,
                    null,
                    numberingGraph.NumberingPartUri!,
                    resolvedNumbering.SourceElementOrdinal,
                    StyleLevel: true,
                    NumberId: resolvedNumbering.NumberId,
                    AbstractNumberId: resolvedNumbering.EffectiveAbstractNumberId,
                    LevelIndex: resolvedNumbering.LevelIndex,
                    NumberingLevelSourceKind: resolvedNumbering.LevelSourceKind
                );
                ApplySet(
                    paragraphStates,
                    resolvedNumbering.Level.ParagraphProperties,
                    numberingSource,
                    isRunProperties: false
                );
                ApplySet(
                    runStates,
                    resolvedNumbering.Level.RunProperties,
                    numberingSource,
                    isRunProperties: true
                );
                AddUnmodeled(
                    unmodeled,
                    $"numbering:{numberId}:{resolvedNumbering.LevelIndex}:paragraph",
                    resolvedNumbering.Level.ParagraphProperties
                );
                AddUnmodeled(
                    unmodeled,
                    $"numbering:{numberId}:{resolvedNumbering.LevelIndex}:run",
                    resolvedNumbering.Level.RunProperties
                );
                unmodeled.AddRange(
                    resolvedNumbering.Level.UnmodeledElements.Select(name =>
                        $"numbering:{numberId}:{resolvedNumbering.LevelIndex}:{name}"
                    )
                );
            }
        }

        if (characterStyleId is not null)
        {
            foreach (
                var style in ResolveStyleChain(
                    styleGraph,
                    characterStyleId,
                    WordStyleType.Character
                )
            )
            {
                var sourceInfo = new FormattingSource(
                    WordFormattingSourceKind.CharacterStyle,
                    style.StyleId,
                    styleGraph.StylesPartUri!,
                    style.SourceElementOrdinal,
                    StyleLevel: true
                );
                ApplySet(
                    runStates,
                    style.RunProperties,
                    sourceInfo,
                    isRunProperties: true
                );
                AddUnmodeled(
                    unmodeled,
                    $"character_style:{style.StyleId}:run",
                    style.RunProperties
                );
            }
        }

        ApplySet(
            paragraphStates,
            directParagraph,
            new FormattingSource(
                WordFormattingSourceKind.DirectParagraphFormatting,
                null,
                paragraph.SourcePartUri,
                paragraph.SourceElementOrdinal,
                StyleLevel: false
            ),
            isRunProperties: false
        );
        ApplySet(
            runStates,
            directRun,
            new FormattingSource(
                WordFormattingSourceKind.DirectRunFormatting,
                null,
                selected.SourcePartUri,
                selected.SourceElementOrdinal,
                StyleLevel: false
            ),
            isRunProperties: true
        );
        AddUnmodeled(unmodeled, "direct:paragraph", directParagraph);
        AddUnmodeled(unmodeled, "direct:run", directRun);

        if (HasAncestor(semanticDocument, selected, WordSemanticNodeKind.Table))
        {
            omissions.Add("conditional_table_style_properties");
        }

        if (numberingReference.NumberId is > 0 && numberingGraph is null)
        {
            omissions.Add("numbering_level_properties");
        }

        if (
            numberingReference.NumberId is > 0
            &&
            numberingReference.LevelFromParagraphStyle
            && !numberingReference.LevelOverriddenDirectly
        )
        {
            warnings.Add(
                "numbering_level: Microsoft Word does not follow the standard rule that ignores ilvl inside a paragraph style; Word documents using it can behave unpredictably."
            );
            omissions.Add("word_style_numbering_level_compatibility");
        }

        var hasThemeReferences = paragraphStates.Keys.Concat(runStates.Keys).Any(name =>
            name.Contains("theme", StringComparison.Ordinal)
        );
        if (hasThemeReferences && themeGraph is null)
        {
            omissions.Add("theme_value_resolution");
        }
        else if (hasThemeReferences)
        {
            ResolveThemeValues(
                themeGraph!,
                settingsGraph,
                fontTableGraph,
                paragraphStates,
                runStates,
                omissions,
                warnings
            );
        }

        if (HasAncestor(semanticDocument, selected, WordSemanticNodeKind.Revision))
        {
            omissions.Add("revision_view_formatting");
        }

        if (unmodeled.Count != 0)
        {
            omissions.Add("unmodeled_property_elements");
        }

        var hasDefaultTrueToggleWarning = false;
        foreach (var pair in runStates)
        {
            if (
                pair.Value.IsToggle
                && pair.Value.DocumentDefaultWasTrue
                && pair.Value.StyleContributionCount > 1
                && !pair.Value.HasDirectContribution
            )
            {
                warnings.Add(
                    $"{pair.Key}: Microsoft Word's default-true multi-level toggle behavior differs from the base ECMA rule."
                );
                hasDefaultTrueToggleWarning = true;
            }
        }
        if (hasDefaultTrueToggleWarning)
        {
            omissions.Add("word_default_true_toggle_compatibility");
        }

        return new WordEffectiveFormatting(
            selected.Id,
            selected.Kind,
            paragraph.Id,
            selected.SourcePartUri,
            paragraphStyleId,
            characterStyleId,
            resolvedNumbering,
            numberingRemoved,
            Freeze(paragraphStates),
            Freeze(runStates),
            unmodeled,
            omissions,
            warnings
        );
    }

    private static void ResolveThemeValues(
        WordThemeGraph themeGraph,
        WordSettingsGraph? settingsGraph,
        WordFontTableGraph? fontTableGraph,
        Dictionary<string, MutableProperty> paragraphStates,
        Dictionary<string, MutableProperty> runStates,
        List<string> omissions,
        List<string> warnings
    )
    {
        if (!themeGraph.HasThemePart || themeGraph.ThemePartUri is null)
        {
            omissions.Add("theme_value_resolution");
            warnings.Add(
                "Theme-backed formatting is active, but the document has no usable Office theme part."
            );
            return;
        }

        foreach (var binding in ThemeFontBindings)
        {
            if (!runStates.TryGetValue(binding.ThemeProperty, out var themeValue))
            {
                continue;
            }

            try
            {
                var resolved = themeGraph.ResolveFont(
                    themeValue.Value,
                    settingsGraph?.ThemeFontLanguages
                );
                ApplyResolvedProperty(
                    runStates,
                    binding.ResolvedProperty,
                    themeValue.Value,
                    resolved.Typeface,
                    new FormattingSource(
                        WordFormattingSourceKind.Theme,
                        null,
                        themeGraph.ThemePartUri,
                        resolved.SourceElementOrdinal,
                        StyleLevel: false,
                        ThemeToken: resolved.RequestedToken,
                        ThemeFontCollection: resolved.CollectionKind,
                        ThemeFontRole: resolved.Role,
                        ThemeLanguageTag: resolved.LanguageTag,
                        ThemeScript: resolved.Script,
                        ThemeFontResolutionKind: resolved.ResolutionKind
                    )
                );
                if (
                    fontTableGraph?.FontTablePartUri is { } fontTablePartUri
                    && fontTableGraph.TryGetFont(resolved.Typeface, out var font)
                    && font is not null
                )
                {
                    var documentFontStatus = font.EmbeddedFaces.Count == 0
                        ? "declared"
                        : font.HasWordReadableEmbeddedFace
                            ? "declared_embedded"
                            : "declared_embedded_unreadable";
                    ApplyResolvedProperty(
                        runStates,
                        binding.DocumentFontProperty,
                        resolved.Typeface,
                        documentFontStatus,
                        new FormattingSource(
                            WordFormattingSourceKind.FontTable,
                            null,
                            fontTablePartUri,
                            font.SourceElementOrdinal,
                            StyleLevel: false
                        )
                    );
                    if (
                        font.EmbeddedFaces.Count > 0
                        && !font.HasWordReadableEmbeddedFace
                    )
                    {
                        omissions.Add("font_embedding_resolution");
                        warnings.Add(
                            $"{binding.ResolvedProperty}: font '{resolved.Typeface}' has embedded faces, but none is readable by Word under the declared relationship, content type, and font key."
                        );
                    }
                }
            }
            catch (WordThemeResolutionException exception)
            {
                omissions.Add(
                    exception.Message.Contains(
                        "language-dependent",
                        StringComparison.Ordinal
                    )
                        ? "theme_language_font_resolution"
                        : "theme_font_value_resolution"
                );
                warnings.Add($"{binding.ThemeProperty}: {exception.Message}");
            }
        }

        ResolveThemeColors(
            themeGraph,
            paragraphStates,
            ParagraphThemeColorBindings,
            omissions,
            warnings
        );
        ResolveThemeColors(
            themeGraph,
            runStates,
            RunThemeColorBindings,
            omissions,
            warnings
        );
    }

    private static void ResolveThemeColors(
        WordThemeGraph themeGraph,
        Dictionary<string, MutableProperty> states,
        IReadOnlyList<ThemeColorBinding> bindings,
        List<string> omissions,
        List<string> warnings
    )
    {
        foreach (var binding in bindings)
        {
            var tint = states.TryGetValue(binding.TintProperty, out var tintValue)
                ? tintValue.Value
                : null;
            var shade = states.TryGetValue(binding.ShadeProperty, out var shadeValue)
                ? shadeValue.Value
                : null;
            if (!states.TryGetValue(binding.ThemeProperty, out var themeValue))
            {
                if (tint is not null || shade is not null)
                {
                    omissions.Add("theme_color_value_resolution");
                    warnings.Add(
                        $"{binding.ResolvedProperty}: theme tint or shade is present without its required theme color token."
                    );
                }
                continue;
            }

            try
            {
                var resolved = themeGraph.ResolveColor(themeValue.Value, tint, shade);
                ApplyResolvedProperty(
                    states,
                    binding.ResolvedProperty,
                    themeValue.Value,
                    resolved.EffectiveRgb,
                    new FormattingSource(
                        WordFormattingSourceKind.Theme,
                        null,
                        themeGraph.ThemePartUri!,
                        resolved.SourceElementOrdinal,
                        StyleLevel: false,
                        ThemeToken: resolved.RequestedToken,
                        ThemeColorSlot: resolved.ColorSlot
                    )
                );

                if (
                    states.TryGetValue(binding.CachedProperty, out var cachedValue)
                    && TryNormalizeRgb(cachedValue.Value, out var cachedRgb)
                    && !string.Equals(
                        cachedRgb,
                        resolved.EffectiveRgb,
                        StringComparison.Ordinal
                    )
                )
                {
                    if (tint is not null || shade is not null)
                    {
                        omissions.Add("theme_color_transform_word_quantization");
                        warnings.Add(
                            $"{binding.ResolvedProperty}: computed theme color {resolved.EffectiveRgb} differs from cached WordprocessingML color {cachedRgb}; Office uses implementation-specific HSL quantization."
                        );
                    }
                    else
                    {
                        warnings.Add(
                            $"{binding.ResolvedProperty}: resolved theme color {resolved.EffectiveRgb} differs from cached WordprocessingML color {cachedRgb}."
                        );
                    }
                }
            }
            catch (WordThemeResolutionException exception)
            {
                omissions.Add("theme_color_value_resolution");
                warnings.Add($"{binding.ThemeProperty}: {exception.Message}");
            }
        }
    }

    private static void ApplyResolvedProperty(
        Dictionary<string, MutableProperty> states,
        string propertyName,
        string declaredValue,
        string resolvedValue,
        FormattingSource source
    )
    {
        if (!states.TryGetValue(propertyName, out var state))
        {
            state = new MutableProperty(propertyName, isToggle: false);
            states.Add(propertyName, state);
        }

        state.Apply(resolvedValue, source, declaredValue);
    }

    private static bool TryNormalizeRgb(string value, out string normalized)
    {
        normalized = string.Empty;
        if (
            value.Length != 6
            || !uint.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out _
            )
        )
        {
            return false;
        }

        normalized = value.ToUpperInvariant();
        return true;
    }

    private static void ValidateSnapshots(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        WordNumberingGraph? numberingGraph,
        WordThemeGraph? themeGraph,
        WordSettingsGraph? settingsGraph,
        WordFontTableGraph? fontTableGraph
    )
    {
        if (
            !string.Equals(package.Fingerprint, semanticDocument.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(package.Fingerprint, styleGraph.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(semanticDocument.MainPartUri, styleGraph.MainPartUri, StringComparison.Ordinal)
            || numberingGraph is not null
                && (
                    !string.Equals(package.Fingerprint, numberingGraph.PackageFingerprint, StringComparison.Ordinal)
                    || !string.Equals(semanticDocument.MainPartUri, numberingGraph.MainPartUri, StringComparison.Ordinal)
                )
            || themeGraph is not null
                && (
                    !string.Equals(package.Fingerprint, themeGraph.PackageFingerprint, StringComparison.Ordinal)
                    || !string.Equals(semanticDocument.MainPartUri, themeGraph.MainPartUri, StringComparison.Ordinal)
                )
            || settingsGraph is not null
                && (
                    !string.Equals(package.Fingerprint, settingsGraph.PackageFingerprint, StringComparison.Ordinal)
                    || !string.Equals(semanticDocument.MainPartUri, settingsGraph.MainPartUri, StringComparison.Ordinal)
                )
            || fontTableGraph is not null
                && (
                    !string.Equals(package.Fingerprint, fontTableGraph.PackageFingerprint, StringComparison.Ordinal)
                    || !string.Equals(semanticDocument.MainPartUri, fontTableGraph.MainPartUri, StringComparison.Ordinal)
                )
        )
        {
            throw new WordFormattingResolutionException(
                "Effective formatting requires package, semantic, style, numbering, theme, settings, and font-table snapshots from the same document version."
            );
        }
    }

    private WordSemanticNode FindParagraphAncestor(
        WordSemanticDocument document,
        WordSemanticNode node
    )
    {
        var current = node;
        for (var depth = 0; depth < _options.MaxAncestorDepth; depth++)
        {
            if (
                current.ParentId is not { } parentId
                || !document.TryGetNode(parentId, out var parent)
                || parent is null
            )
            {
                break;
            }

            if (parent.Kind == WordSemanticNodeKind.Paragraph)
            {
                return parent;
            }

            current = parent;
        }

        throw new WordFormattingResolutionException(
            $"Run node '{node.Id}' has no bounded paragraph ancestor."
        );
    }

    private bool HasAncestor(
        WordSemanticDocument document,
        WordSemanticNode node,
        WordSemanticNodeKind kind
    )
    {
        var current = node;
        for (var depth = 0; depth < _options.MaxAncestorDepth; depth++)
        {
            if (current.Kind == kind)
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

        throw new WordFormattingLimitException(
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
            throw new WordFormattingLimitException(
                $"Word part '{part.Uri}' exceeds an effective-formatting XML limit: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordFormattingResolutionException(
                $"Word part '{part.Uri}' is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private static XElement GetBoundElement(
        LosslessXmlDocument source,
        WordSemanticNode node,
        string expectedLocalName
    )
    {
        var element = source.GetParsedElement(node.SourceElementOrdinal);
        if (
            element.Name.LocalName != expectedLocalName
            || element.Name.NamespaceName is not (
                "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                or "http://purl.oclc.org/ooxml/wordprocessingml/main"
            )
        )
        {
            throw new WordFormattingResolutionException(
                $"Semantic node '{node.Id}' no longer binds to the expected w:{expectedLocalName} element."
            );
        }

        return element;
    }

    private static XElement? OptionalSingleChild(
        XElement? parent,
        XName name,
        string description
    )
    {
        if (parent is null)
        {
            return null;
        }

        var matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length > 1)
        {
            throw new WordFormattingResolutionException(
                $"Element '{parent.Name.LocalName}' contains duplicate {description}."
            );
        }

        return matches.FirstOrDefault();
    }

    private static string? ReferencedStyleId(
        XElement? propertyElement,
        XName referenceName,
        XNamespace w
    )
    {
        var reference = OptionalSingleChild(
            propertyElement,
            referenceName,
            referenceName.LocalName
        );
        if (reference is null)
        {
            return null;
        }

        var value = reference.Attribute(w + "val")?.Value;
        if (string.IsNullOrEmpty(value))
        {
            throw new WordFormattingResolutionException(
                $"Style reference '{referenceName.LocalName}' has no style ID."
            );
        }

        return value;
    }

    private static WordStylePropertySet ReadDirectProperties(
        XElement? propertyElement,
        WordStyleGraphBuilder.WordFormattingDomain domain
    )
    {
        try
        {
            return WordStyleGraphBuilder.ReadFormattingProperties(
                propertyElement,
                domain
            );
        }
        catch (WordStyleProjectionException exception)
        {
            throw new WordFormattingResolutionException(
                "Direct formatting properties are structurally ambiguous.",
                exception
            );
        }
    }

    private static IReadOnlyList<WordStyleDefinition> ResolveStyleChain(
        WordStyleGraph graph,
        string styleId,
        WordStyleType expectedType
    )
    {
        if (!graph.TryGetStyle(styleId, out var selected) || selected is null)
        {
            throw new WordFormattingResolutionException(
                $"Content refers to missing style '{styleId}'."
            );
        }

        if (selected.Type != expectedType)
        {
            throw new WordFormattingResolutionException(
                $"Content uses style '{styleId}' as {expectedType}, but it is {selected.Type}."
            );
        }

        if (!selected.InheritanceResolvable)
        {
            throw new WordFormattingResolutionException(
                selected.InheritanceFailure
                    ?? $"Style '{styleId}' has an unresolved inheritance chain."
            );
        }

        var result = new List<WordStyleDefinition>(
            selected.InheritanceChainStyleIds.Count
        );
        foreach (var chainId in selected.InheritanceChainStyleIds)
        {
            if (!graph.TryGetStyle(chainId, out var style) || style is null)
            {
                throw new WordFormattingResolutionException(
                    $"Resolved chain for '{styleId}' lost style '{chainId}'."
                );
            }

            result.Add(style);
        }

        return result;
    }

    private static NumberingReference ResolveNumberingReference(
        IReadOnlyDictionary<string, MutableProperty> paragraphStates,
        WordStylePropertySet directParagraph,
        WordNumberingGraph? numberingGraph,
        string? paragraphStyleId
    )
    {
        var hasDirectNumberId = directParagraph.Values.TryGetValue(
            "numbering_id",
            out var directNumberId
        );
        var hasInheritedNumberId = paragraphStates.TryGetValue(
            "numbering_id",
            out var inheritedNumberId
        );
        var rawNumberId = hasDirectNumberId
            ? directNumberId
            : hasInheritedNumberId
                ? inheritedNumberId!.Value
                : null;
        var numberIdFromParagraphStyle = !hasDirectNumberId
            && hasInheritedNumberId
            && inheritedNumberId!.LastSourceKind
                == WordFormattingSourceKind.ParagraphStyle;
        var hasDirectLevel = directParagraph.Values.TryGetValue(
            "numbering_level",
            out var directLevel
        );
        var hasInheritedLevel = paragraphStates.TryGetValue(
            "numbering_level",
            out var inheritedLevel
        );
        var rawLevel = hasDirectLevel
            ? directLevel
            : hasInheritedLevel
                ? inheritedLevel!.Value
                : null;
        if (rawNumberId is null)
        {
            if (rawLevel is not null)
            {
                throw new WordFormattingResolutionException(
                    "Paragraph formatting declares a numbering level without a numbering instance ID."
                );
            }

            return new NumberingReference(null, 0, false, hasDirectLevel, false);
        }

        if (
            !int.TryParse(
                rawNumberId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var numberId
            )
        )
        {
            throw new WordFormattingResolutionException(
                $"Paragraph numbering ID '{rawNumberId}' is not a non-negative integer."
            );
        }

        var levelIndex = 0;
        if (
            rawLevel is not null
            && !int.TryParse(
                rawLevel,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out levelIndex
            )
        )
        {
            throw new WordFormattingResolutionException(
                $"Paragraph numbering level '{rawLevel}' is not a non-negative integer."
            );
        }

        var levelInferredFromParagraphStyle = false;
        if (
            rawLevel is null
            && numberId > 0
            && numberIdFromParagraphStyle
            && numberingGraph is not null
            && paragraphStyleId is not null
        )
        {
            try
            {
                if (
                    numberingGraph.FindLevelIndexForParagraphStyle(
                        numberId,
                        paragraphStyleId
                    ) is { } mappedLevel
                )
                {
                    levelIndex = mappedLevel;
                    levelInferredFromParagraphStyle = true;
                }
            }
            catch (WordNumberingResolutionException exception)
            {
                throw new WordFormattingResolutionException(
                    "The paragraph style cannot be mapped to one numbering level safely.",
                    exception
                );
            }
        }

        if (levelIndex is < 0 or > 8)
        {
            throw new WordFormattingResolutionException(
                $"Paragraph numbering level {levelIndex} is outside Word's supported range 0 through 8."
            );
        }

        return new NumberingReference(
            numberId,
            levelIndex,
            hasInheritedLevel
                && inheritedLevel!.LastSourceKind
                    == WordFormattingSourceKind.ParagraphStyle,
            hasDirectLevel,
            levelInferredFromParagraphStyle
        );
    }

    private static void ApplySet(
        Dictionary<string, MutableProperty> states,
        WordStylePropertySet propertySet,
        FormattingSource source,
        bool isRunProperties
    )
    {
        ClearSupersededCompositeProperties(
            states,
            propertySet.Values,
            isRunProperties
                ? RunCompositePropertyGroups
                : ParagraphCompositePropertyGroups
        );
        foreach (var pair in propertySet.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var isToggle = isRunProperties && ToggleRunProperties.Contains(pair.Key);
            if (!states.TryGetValue(pair.Key, out var state))
            {
                state = new MutableProperty(pair.Key, isToggle);
                states.Add(pair.Key, state);
            }
            else if (state.IsToggle != isToggle)
            {
                throw new WordFormattingResolutionException(
                    $"Formatting property '{pair.Key}' changes behavior between hierarchy levels."
                );
            }

            state.Apply(pair.Value, source);
        }
    }

    private static void ClearSupersededCompositeProperties(
        Dictionary<string, MutableProperty> states,
        IReadOnlyDictionary<string, string> incoming,
        IReadOnlyList<IReadOnlyList<string>> groups
    )
    {
        foreach (var group in groups)
        {
            if (!group.Any(incoming.ContainsKey))
            {
                continue;
            }

            foreach (var name in group)
            {
                if (!incoming.ContainsKey(name))
                {
                    states.Remove(name);
                }
            }
        }
    }

    private static void AddUnmodeled(
        List<string> result,
        string source,
        WordStylePropertySet propertySet
    )
    {
        result.AddRange(propertySet.UnmodeledElements.Select(name => $"{source}:{name}"));
    }

    private static IReadOnlyDictionary<string, WordEffectiveFormattingProperty> Freeze(
        IReadOnlyDictionary<string, MutableProperty> states
    ) => states.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(
        pair => pair.Key,
        pair => pair.Value.Freeze(),
        StringComparer.Ordinal
    );

    private sealed record FormattingSource(
        WordFormattingSourceKind Kind,
        string? StyleId,
        string PartUri,
        int? ElementOrdinal,
        bool StyleLevel,
        int? NumberId = null,
        int? AbstractNumberId = null,
        int? LevelIndex = null,
        WordNumberingLevelSourceKind? NumberingLevelSourceKind = null,
        string? ThemeToken = null,
        string? ThemeColorSlot = null,
        WordThemeFontCollectionKind? ThemeFontCollection = null,
        WordThemeFontRole? ThemeFontRole = null,
        string? ThemeLanguageTag = null,
        string? ThemeScript = null,
        WordThemeFontResolutionKind? ThemeFontResolutionKind = null
    );

    private sealed record ThemeFontBinding(
        string ThemeProperty,
        string ResolvedProperty,
        string DocumentFontProperty
    );

    private sealed record ThemeColorBinding(
        string ThemeProperty,
        string TintProperty,
        string ShadeProperty,
        string CachedProperty,
        string ResolvedProperty
    );

    private sealed record NumberingReference(
        int? NumberId,
        int LevelIndex,
        bool LevelFromParagraphStyle,
        bool LevelOverriddenDirectly,
        bool LevelInferredFromParagraphStyle
    );

    private sealed class MutableProperty
    {
        private readonly List<WordFormattingContribution> _contributions = [];

        public MutableProperty(string name, bool isToggle)
        {
            Name = name;
            IsToggle = isToggle;
        }

        public string Name { get; }

        public bool IsToggle { get; }

        public string Value { get; private set; } = string.Empty;

        public bool DocumentDefaultWasTrue { get; private set; }

        public int StyleContributionCount { get; private set; }

        public bool HasDirectContribution { get; private set; }

        public WordFormattingSourceKind? LastSourceKind { get; private set; }

        public void Apply(
            string declaredValue,
            FormattingSource source,
            string? contributionDeclaredValue = null
        )
        {
            if (IsToggle)
            {
                if (!bool.TryParse(declaredValue, out var declared))
                {
                    throw new WordFormattingResolutionException(
                        $"Toggle property '{Name}' has non-Boolean value '{declaredValue}'."
                    );
                }

                var current = string.Equals(Value, "true", StringComparison.Ordinal);
                if (source.StyleLevel)
                {
                    StyleContributionCount++;
                    if (declared)
                    {
                        current = !current;
                    }
                }
                else
                {
                    current = declared;
                    HasDirectContribution = source.Kind is
                        WordFormattingSourceKind.DirectParagraphFormatting
                        or WordFormattingSourceKind.DirectRunFormatting;
                    if (source.Kind == WordFormattingSourceKind.DocumentDefault)
                    {
                        DocumentDefaultWasTrue = declared;
                    }
                }

                Value = current ? "true" : "false";
            }
            else
            {
                Value = declaredValue;
                HasDirectContribution |= source.Kind is
                    WordFormattingSourceKind.DirectParagraphFormatting
                    or WordFormattingSourceKind.DirectRunFormatting;
            }

            _contributions.Add(
                new WordFormattingContribution(
                    source.Kind,
                    source.StyleId,
                    source.PartUri,
                    source.ElementOrdinal,
                    contributionDeclaredValue ?? declaredValue,
                    Value,
                    source.NumberId,
                    source.AbstractNumberId,
                    source.LevelIndex,
                    source.NumberingLevelSourceKind,
                    source.ThemeToken,
                    source.ThemeColorSlot,
                    source.ThemeFontCollection,
                    source.ThemeFontRole,
                    source.ThemeLanguageTag,
                    source.ThemeScript,
                    source.ThemeFontResolutionKind
                )
            );
            LastSourceKind = source.Kind;
        }

        public WordEffectiveFormattingProperty Freeze() => new(
            Name,
            Value,
            IsToggle,
            _contributions
        );
    }
}

public class WordFormattingResolutionException : IOException
{
    public WordFormattingResolutionException(string message)
        : base(message)
    {
    }

    public WordFormattingResolutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordFormattingLimitException : WordFormattingResolutionException
{
    public WordFormattingLimitException(string message)
        : base(message)
    {
    }
}
