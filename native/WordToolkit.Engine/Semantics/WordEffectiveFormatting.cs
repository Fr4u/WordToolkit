using System.Collections.ObjectModel;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordFormattingSourceKind
{
    DocumentDefault,
    ParagraphStyle,
    CharacterStyle,
    DirectParagraphFormatting,
    DirectRunFormatting,
}

public sealed record WordFormattingContribution(
    WordFormattingSourceKind SourceKind,
    string? StyleId,
    string SourcePartUri,
    int? SourceElementOrdinal,
    string DeclaredValue,
    string ResultingValue
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
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(styleGraph);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSnapshots(package, semanticDocument, styleGraph);
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

        if (
            paragraphStates.ContainsKey("numbering_id")
            || paragraphStates.ContainsKey("numbering_level")
        )
        {
            omissions.Add("numbering_level_properties");
        }

        if (
            paragraphStates.Keys.Concat(runStates.Keys).Any(name =>
                name.Contains("theme", StringComparison.Ordinal)
            )
        )
        {
            omissions.Add("theme_value_resolution");
        }

        if (HasAncestor(semanticDocument, selected, WordSemanticNodeKind.Revision))
        {
            omissions.Add("revision_view_formatting");
        }

        if (unmodeled.Count != 0)
        {
            omissions.Add("unmodeled_property_elements");
        }

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
            }
        }
        if (warnings.Count != 0)
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
            Freeze(paragraphStates),
            Freeze(runStates),
            unmodeled,
            omissions,
            warnings
        );
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
            || !string.Equals(semanticDocument.MainPartUri, styleGraph.MainPartUri, StringComparison.Ordinal)
        )
        {
            throw new WordFormattingResolutionException(
                "Effective formatting requires package, semantic, and style snapshots from the same document version."
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

    private static void ApplySet(
        Dictionary<string, MutableProperty> states,
        WordStylePropertySet propertySet,
        FormattingSource source,
        bool isRunProperties
    )
    {
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
        bool StyleLevel
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

        public void Apply(string declaredValue, FormattingSource source)
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
                    declaredValue,
                    Value
                )
            );
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
