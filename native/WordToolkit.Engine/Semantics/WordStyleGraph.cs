using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordStyleType
{
    Paragraph,
    Character,
    Table,
    Numbering,
}

public enum WordStyleIssueSeverity
{
    Warning,
    Error,
}

public sealed record WordStyleIssue(
    string Code,
    WordStyleIssueSeverity Severity,
    string Message,
    string? StyleId = null
);

public sealed class WordStylePropertySet
{
    internal WordStylePropertySet(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> unmodeledElements
    )
    {
        Values = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values, StringComparer.Ordinal)
        );
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public IReadOnlyDictionary<string, string> Values { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public bool IsFullyModeled => UnmodeledElements.Count == 0;
}

public sealed record WordLatentStyleException(
    string Name,
    bool? Locked,
    bool? SemiHidden,
    bool? UnhideWhenUsed,
    bool? QuickFormat,
    int? UiPriority
);

public sealed class WordLatentStyles
{
    internal WordLatentStyles(
        int? declaredCount,
        bool? defaultLocked,
        bool? defaultSemiHidden,
        bool? defaultUnhideWhenUsed,
        bool? defaultQuickFormat,
        int? defaultUiPriority,
        IReadOnlyList<WordLatentStyleException> exceptions
    )
    {
        DeclaredCount = declaredCount;
        DefaultLocked = defaultLocked;
        DefaultSemiHidden = defaultSemiHidden;
        DefaultUnhideWhenUsed = defaultUnhideWhenUsed;
        DefaultQuickFormat = defaultQuickFormat;
        DefaultUiPriority = defaultUiPriority;
        Exceptions = new ReadOnlyCollection<WordLatentStyleException>(
            exceptions.ToArray()
        );
    }

    public int? DeclaredCount { get; }

    public bool? DefaultLocked { get; }

    public bool? DefaultSemiHidden { get; }

    public bool? DefaultUnhideWhenUsed { get; }

    public bool? DefaultQuickFormat { get; }

    public int? DefaultUiPriority { get; }

    public IReadOnlyList<WordLatentStyleException> Exceptions { get; }
}

public sealed class WordStyleDefinition
{
    internal WordStyleDefinition(
        string styleId,
        WordStyleType type,
        string? name,
        IReadOnlyList<string> aliases,
        string? basedOnStyleId,
        string? nextStyleId,
        string? linkedStyleId,
        bool isDefault,
        bool isCustom,
        bool? quickFormat,
        bool? semiHidden,
        bool? unhideWhenUsed,
        bool? locked,
        int? uiPriority,
        int sourceElementOrdinal,
        WordStylePropertySet paragraphProperties,
        WordStylePropertySet runProperties,
        WordStylePropertySet tableProperties,
        WordStylePropertySet tableCellProperties,
        bool inheritanceResolvable,
        string? inheritanceFailure,
        IReadOnlyList<string> inheritanceChainStyleIds
    )
    {
        StyleId = styleId;
        Type = type;
        Name = name;
        Aliases = new ReadOnlyCollection<string>(aliases.ToArray());
        BasedOnStyleId = basedOnStyleId;
        NextStyleId = nextStyleId;
        LinkedStyleId = linkedStyleId;
        IsDefault = isDefault;
        IsCustom = isCustom;
        QuickFormat = quickFormat;
        SemiHidden = semiHidden;
        UnhideWhenUsed = unhideWhenUsed;
        Locked = locked;
        UiPriority = uiPriority;
        SourceElementOrdinal = sourceElementOrdinal;
        ParagraphProperties = paragraphProperties;
        RunProperties = runProperties;
        TableProperties = tableProperties;
        TableCellProperties = tableCellProperties;
        InheritanceResolvable = inheritanceResolvable;
        InheritanceFailure = inheritanceFailure;
        InheritanceChainStyleIds = new ReadOnlyCollection<string>(
            inheritanceChainStyleIds.ToArray()
        );
    }

    public string StyleId { get; }

    public WordStyleType Type { get; }

    public string? Name { get; }

    public IReadOnlyList<string> Aliases { get; }

    public string? BasedOnStyleId { get; }

    public string? NextStyleId { get; }

    public string? LinkedStyleId { get; }

    public bool IsDefault { get; }

    public bool IsCustom { get; }

    public bool? QuickFormat { get; }

    public bool? SemiHidden { get; }

    public bool? UnhideWhenUsed { get; }

    public bool? Locked { get; }

    public int? UiPriority { get; }

    public int SourceElementOrdinal { get; }

    public WordStylePropertySet ParagraphProperties { get; }

    public WordStylePropertySet RunProperties { get; }

    public WordStylePropertySet TableProperties { get; }

    public WordStylePropertySet TableCellProperties { get; }

    public bool InheritanceResolvable { get; }

    public string? InheritanceFailure { get; }

    public IReadOnlyList<string> InheritanceChainStyleIds { get; }
}

public sealed class WordStyleGraph
{
    private readonly IReadOnlyDictionary<string, WordStyleDefinition> _stylesById;

    internal WordStyleGraph(
        string packageFingerprint,
        string mainPartUri,
        string? stylesPartUri,
        string? stylesWithEffectsPartUri,
        WordStylePropertySet defaultParagraphProperties,
        WordStylePropertySet defaultRunProperties,
        WordLatentStyles? latentStyles,
        IReadOnlyList<WordStyleDefinition> styles,
        IReadOnlyDictionary<WordStyleType, string> defaultStyleIds,
        IReadOnlyList<WordStyleIssue> issues
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        StylesPartUri = stylesPartUri;
        StylesWithEffectsPartUri = stylesWithEffectsPartUri;
        DefaultParagraphProperties = defaultParagraphProperties;
        DefaultRunProperties = defaultRunProperties;
        LatentStyles = latentStyles;
        Styles = new ReadOnlyCollection<WordStyleDefinition>(styles.ToArray());
        DefaultStyleIds = new ReadOnlyDictionary<WordStyleType, string>(
            new Dictionary<WordStyleType, string>(defaultStyleIds)
        );
        Issues = new ReadOnlyCollection<WordStyleIssue>(issues.ToArray());
        _stylesById = new ReadOnlyDictionary<string, WordStyleDefinition>(
            styles.ToDictionary(style => style.StyleId, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public string? StylesPartUri { get; }

    public string? StylesWithEffectsPartUri { get; }

    public bool HasStylesPart => StylesPartUri is not null;

    public WordStylePropertySet DefaultParagraphProperties { get; }

    public WordStylePropertySet DefaultRunProperties { get; }

    public WordLatentStyles? LatentStyles { get; }

    public IReadOnlyList<WordStyleDefinition> Styles { get; }

    public IReadOnlyDictionary<WordStyleType, string> DefaultStyleIds { get; }

    public IReadOnlyList<WordStyleIssue> Issues { get; }

    public bool TryGetStyle(string styleId, out WordStyleDefinition? style)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleId);
        return _stylesById.TryGetValue(styleId, out style);
    }
}

public sealed record WordStyleGraphOptions
{
    public static WordStyleGraphOptions Default { get; } = new();

    public int MaxStylesPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxStyles { get; init; } = 16_384;

    public int MaxLatentStyleExceptions { get; init; } = 16_384;

    public int MaxInheritanceDepth { get; init; } = 1_024;

    internal void Validate()
    {
        if (MaxStylesPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStylesPartBytes));
        }

        if (MaxStyles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStyles));
        }

        if (MaxLatentStyleExceptions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLatentStyleExceptions));
        }

        if (MaxInheritanceDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxInheritanceDepth));
        }
    }
}

public sealed class WordStyleGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string StylesRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string StrictStylesRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/styles";
    private const string StylesWithEffectsRelationship =
        "http://schemas.microsoft.com/office/2007/relationships/stylesWithEffects";
    private const string StylesContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
    private const string StylesWithEffectsContentType =
        "application/vnd.ms-word.stylesWithEffects+xml";

    private static readonly WordStylePropertySet EmptyPropertySet = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        Array.Empty<string>()
    );

    private readonly WordStyleGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordStyleGraphBuilder(WordStyleGraphOptions? options = null)
    {
        _options = options ?? WordStyleGraphOptions.Default;
        _options.Validate();
    }

    public WordStyleGraphBuilder(
        WordStyleGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordStyleGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordStyleGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.Styles
        );
        if (
            !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordStyleProjectionException(
                "Style graph requires a semantic projection of the same package snapshot."
            );
        }

        var effectsPartUri = ResolveOptionalPart(
            package,
            semanticDocument.MainPartUri,
            StylesWithEffectsRelationship,
            StylesWithEffectsContentType,
            "stylesWithEffects"
        )?.Uri;
        var stylesPart = ResolveStylesPart(package, semanticDocument.MainPartUri);
        if (stylesPart is null)
        {
            return new WordStyleGraph(
                package.Fingerprint,
                semanticDocument.MainPartUri,
                null,
                effectsPartUri,
                EmptyPropertySet,
                EmptyPropertySet,
                null,
                Array.Empty<WordStyleDefinition>(),
                new Dictionary<WordStyleType, string>(),
                Array.Empty<WordStyleIssue>()
            );
        }

        var source = ParseStylesPart(stylesPart, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || !IsWordNamespace(root.Name.NamespaceName)
            || root.Name.LocalName != "styles"
        )
        {
            throw new WordStyleProjectionException(
                "Word styles part does not have a w:styles root element."
            );
        }

        var wordNamespace = root.Name.Namespace;
        var docDefaults = OptionalSingleChild(root, wordNamespace + "docDefaults");
        var defaultParagraphProperties = ReadFormattingProperties(
            OptionalSingleChild(docDefaults, wordNamespace + "pPrDefault"),
            wordNamespace + "pPr",
            WordFormattingDomain.Paragraph
        );
        var defaultRunProperties = ReadFormattingProperties(
            OptionalSingleChild(docDefaults, wordNamespace + "rPrDefault"),
            wordNamespace + "rPr",
            WordFormattingDomain.Run
        );
        var latentStyles = ParseLatentStyles(
            OptionalSingleChild(root, wordNamespace + "latentStyles"),
            wordNamespace
        );
        var styleElements = root.Elements(wordNamespace + "style").ToArray();
        if (styleElements.Length > _options.MaxStyles)
        {
            throw new WordStyleLimitException(
                $"Styles part contains {styleElements.Length} styles; limit is {_options.MaxStyles}."
            );
        }

        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.Styles,
            styleElements.Length,
            2_048
        );

        var parsed = styleElements
            .Select(element => ParseStyle(element, wordNamespace, source))
            .ToArray();
        var duplicate = parsed.GroupBy(style => style.StyleId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordStyleProjectionException(
                $"Styles part contains duplicate style ID '{duplicate.Key}'."
            );
        }

        var parsedById = parsed.ToDictionary(
            style => style.StyleId,
            StringComparer.Ordinal
        );
        var issues = new List<WordStyleIssue>();
        var defaultStyleIds = ResolveDefaultStyleIds(parsed, issues);
        ValidateAuxiliaryReferences(parsed, parsedById, issues);
        var resolutions = parsed.ToDictionary(
            style => style.StyleId,
            style => ResolveInheritance(style, parsedById),
            StringComparer.Ordinal
        );
        foreach (var pair in resolutions.Where(pair => !pair.Value.Resolvable))
        {
            issues.Add(
                new WordStyleIssue(
                    "STYLE_INHERITANCE_UNRESOLVED",
                    WordStyleIssueSeverity.Error,
                    pair.Value.Failure!,
                    pair.Key
                )
            );
        }

        var definitions = parsed.Select(style =>
        {
            var resolution = resolutions[style.StyleId];
            return style.Freeze(resolution);
        }).ToArray();
        return new WordStyleGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            stylesPart.Uri,
            effectsPartUri,
            defaultParagraphProperties,
            defaultRunProperties,
            latentStyles,
            definitions,
            defaultStyleIds,
            issues
        );
    }

    private OpcPart? ResolveStylesPart(OpcPackageSnapshot package, string mainPartUri) =>
        ResolveOptionalPart(
            package,
            mainPartUri,
            [StylesRelationship, StrictStylesRelationship],
            StylesContentType,
            "styles"
        );

    private static OpcPart? ResolveOptionalPart(
        OpcPackageSnapshot package,
        string mainPartUri,
        string relationshipType,
        string contentType,
        string description
    ) => ResolveOptionalPart(
        package,
        mainPartUri,
        [relationshipType],
        contentType,
        description
    );

    private static OpcPart? ResolveOptionalPart(
        OpcPackageSnapshot package,
        string mainPartUri,
        IReadOnlyCollection<string> relationshipTypes,
        string contentType,
        string description
    )
    {
        var relationships = package.RelationshipsFrom(mainPartUri)
            .Where(relationship => relationshipTypes.Contains(
                relationship.Type,
                StringComparer.Ordinal
            ))
            .ToArray();
        if (relationships.Length == 0)
        {
            return null;
        }

        if (relationships.Length != 1)
        {
            throw new WordStyleProjectionException(
                $"Main document part contains multiple {description} relationships."
            );
        }

        var relationship = relationships[0];
        if (
            relationship.TargetMode != OpcRelationshipTargetMode.Internal
            || relationship.ResolvedTargetPartUri is null
            || !package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var part)
            || !string.Equals(part.ContentType, contentType, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new WordStyleProjectionException(
                $"The {description} relationship does not resolve to a valid Word {description} part."
            );
        }

        return part;
    }

    private LosslessXmlDocument ParseStylesPart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxStylesPartBytes,
                MaxXmlCharacters = _options.MaxStylesPartBytes,
                MaxXmlElements = (int)Math.Min(
                    int.MaxValue,
                    Math.Max((long)_options.MaxStyles * 128, 32_768)
                ),
                MaxXmlDepth = 128,
                MaxTextCharacters = _options.MaxStylesPartBytes,
            };
            return _resourceLease is null
                ? LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    cancellationToken
                )
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.Styles,
                    cancellationToken
                );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordStyleLimitException(
                "Word styles part exceeds a style-graph XML limit: " + exception.Message
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordStyleProjectionException(
                "Word styles part is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private WordLatentStyles? ParseLatentStyles(XElement? element, XNamespace w)
    {
        if (element is null)
        {
            return null;
        }

        var exceptionElements = element.Elements(w + "lsdException").ToArray();
        if (exceptionElements.Length > _options.MaxLatentStyleExceptions)
        {
            throw new WordStyleLimitException(
                "Styles part exceeds the configured latent-style exception limit."
            );
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exceptions = new List<WordLatentStyleException>(exceptionElements.Length);
        foreach (var exception in exceptionElements)
        {
            var name = RequiredAttribute(exception, w + "name", "latent style name");
            if (!names.Add(name))
            {
                throw new WordStyleProjectionException(
                    $"Latent styles contain duplicate exception name '{name}'."
                );
            }

            exceptions.Add(
                new WordLatentStyleException(
                    name,
                    OptionalOnOffAttribute(exception, w + "locked"),
                    OptionalOnOffAttribute(exception, w + "semiHidden"),
                    OptionalOnOffAttribute(exception, w + "unhideWhenUsed"),
                    OptionalOnOffAttribute(exception, w + "qFormat"),
                    OptionalNonNegativeInt(exception, w + "uiPriority")
                )
            );
        }

        return new WordLatentStyles(
            OptionalNonNegativeInt(element, w + "count"),
            OptionalOnOffAttribute(element, w + "defLockedState"),
            OptionalOnOffAttribute(element, w + "defSemiHidden"),
            OptionalOnOffAttribute(element, w + "defUnhideWhenUsed"),
            OptionalOnOffAttribute(element, w + "defQFormat"),
            OptionalNonNegativeInt(element, w + "defUIPriority"),
            exceptions
        );
    }

    private static ParsedStyle ParseStyle(
        XElement element,
        XNamespace w,
        LosslessXmlDocument source
    )
    {
        var styleId = RequiredAttribute(element, w + "styleId", "style ID");
        if (styleId.Length > 253)
        {
            throw new WordStyleProjectionException(
                $"Style ID '{styleId[..Math.Min(styleId.Length, 80)]}' exceeds Word's 253-character limit."
            );
        }

        var rawType = RequiredAttribute(element, w + "type", $"style '{styleId}' type");
        var type = rawType switch
        {
            "paragraph" => WordStyleType.Paragraph,
            "character" => WordStyleType.Character,
            "table" => WordStyleType.Table,
            "numbering" => WordStyleType.Numbering,
            _ => throw new WordStyleProjectionException(
                $"Style '{styleId}' has unknown type '{rawType}'."
            ),
        };
        var name = ChildValue(element, w + "name", w);
        var aliases = (ChildValue(element, w + "aliases", w) ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ParsedStyle(
            styleId,
            type,
            name,
            aliases,
            ChildValue(element, w + "basedOn", w),
            ChildValue(element, w + "next", w),
            ChildValue(element, w + "link", w),
            OptionalOnOffAttribute(element, w + "default") ?? false,
            OptionalOnOffAttribute(element, w + "customStyle") ?? false,
            ChildOnOff(element, w + "qFormat", w),
            ChildOnOff(element, w + "semiHidden", w),
            ChildOnOff(element, w + "unhideWhenUsed", w),
            ChildOnOff(element, w + "locked", w),
            ChildNonNegativeInt(element, w + "uiPriority", w),
            source.GetElementOrdinal(element),
            ReadFormattingProperties(element, w + "pPr", WordFormattingDomain.Paragraph),
            ReadFormattingProperties(element, w + "rPr", WordFormattingDomain.Run),
            ReadFormattingProperties(element, w + "tblPr", WordFormattingDomain.Table),
            ReadFormattingProperties(element, w + "tcPr", WordFormattingDomain.TableCell)
        );
    }

    private IReadOnlyDictionary<WordStyleType, string> ResolveDefaultStyleIds(
        IReadOnlyList<ParsedStyle> styles,
        List<WordStyleIssue> issues
    )
    {
        var result = new Dictionary<WordStyleType, string>();
        foreach (var group in styles.Where(style => style.IsDefault).GroupBy(style => style.Type))
        {
            var defaults = group.ToArray();
            if (defaults.Length == 1)
            {
                result[group.Key] = defaults[0].StyleId;
                continue;
            }

            issues.Add(
                new WordStyleIssue(
                    "STYLE_DEFAULT_AMBIGUOUS",
                    WordStyleIssueSeverity.Error,
                    $"Style type '{group.Key}' declares {defaults.Length} default styles; no default was selected."
                )
            );
        }

        return result;
    }

    private static void ValidateAuxiliaryReferences(
        IReadOnlyList<ParsedStyle> styles,
        IReadOnlyDictionary<string, ParsedStyle> stylesById,
        List<WordStyleIssue> issues
    )
    {
        foreach (var style in styles)
        {
            if (style.NextStyleId is { } next)
            {
                if (!stylesById.TryGetValue(next, out var target))
                {
                    issues.Add(new WordStyleIssue(
                        "STYLE_NEXT_MISSING",
                        WordStyleIssueSeverity.Warning,
                        $"Style '{style.StyleId}' refers to missing next style '{next}'.",
                        style.StyleId
                    ));
                }
                else if (style.Type != WordStyleType.Paragraph || target.Type != WordStyleType.Paragraph)
                {
                    issues.Add(new WordStyleIssue(
                        "STYLE_NEXT_TYPE_MISMATCH",
                        WordStyleIssueSeverity.Warning,
                        $"Style '{style.StyleId}' has a next reference incompatible with paragraph editing behavior.",
                        style.StyleId
                    ));
                }
            }

            if (style.LinkedStyleId is not { } linked)
            {
                continue;
            }

            if (!stylesById.TryGetValue(linked, out var linkedStyle))
            {
                issues.Add(new WordStyleIssue(
                    "STYLE_LINK_MISSING",
                    WordStyleIssueSeverity.Warning,
                    $"Style '{style.StyleId}' refers to missing linked style '{linked}'.",
                    style.StyleId
                ));
                continue;
            }

            var validTypes = style.Type == WordStyleType.Paragraph
                && linkedStyle.Type == WordStyleType.Character
                || style.Type == WordStyleType.Character
                && linkedStyle.Type == WordStyleType.Paragraph;
            if (!validTypes)
            {
                issues.Add(new WordStyleIssue(
                    "STYLE_LINK_TYPE_MISMATCH",
                    WordStyleIssueSeverity.Warning,
                    $"Style '{style.StyleId}' links to incompatible style type '{linkedStyle.Type}'.",
                    style.StyleId
                ));
            }
            else if (!string.Equals(linkedStyle.LinkedStyleId, style.StyleId, StringComparison.Ordinal))
            {
                issues.Add(new WordStyleIssue(
                    "STYLE_LINK_NOT_RECIPROCAL",
                    WordStyleIssueSeverity.Warning,
                    $"Style '{style.StyleId}' link to '{linked}' is not reciprocal.",
                    style.StyleId
                ));
            }
        }
    }

    private InheritanceResolution ResolveInheritance(
        ParsedStyle style,
        IReadOnlyDictionary<string, ParsedStyle> stylesById
    )
    {
        var reverseChain = new List<string>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        var current = style;
        while (true)
        {
            if (reverseChain.Count >= _options.MaxInheritanceDepth)
            {
                return InheritanceResolution.Failed(
                    $"Style '{style.StyleId}' exceeds the {_options.MaxInheritanceDepth}-level inheritance limit."
                );
            }

            if (positions.TryGetValue(current.StyleId, out var cycleStart))
            {
                var cycle = reverseChain.Skip(cycleStart).Append(current.StyleId);
                return InheritanceResolution.Failed(
                    $"Style '{style.StyleId}' has a circular basedOn chain: {string.Join(" -> ", cycle)}."
                );
            }

            positions[current.StyleId] = reverseChain.Count;
            reverseChain.Add(current.StyleId);
            if (current.BasedOnStyleId is not { } parentId)
            {
                reverseChain.Reverse();
                return InheritanceResolution.Succeeded(reverseChain);
            }

            if (!stylesById.TryGetValue(parentId, out var parent))
            {
                return InheritanceResolution.Failed(
                    $"Style '{style.StyleId}' inherits from missing style '{parentId}'."
                );
            }

            if (parent.Type != current.Type)
            {
                return InheritanceResolution.Failed(
                    $"Style '{current.StyleId}' ({current.Type}) inherits from '{parent.StyleId}' ({parent.Type})."
                );
            }

            current = parent;
        }
    }

    private static WordStylePropertySet ReadFormattingProperties(
        XElement? parent,
        XName propertyElementName,
        WordFormattingDomain domain
    )
    {
        if (parent is null)
        {
            return EmptyPropertySet;
        }

        var propertyElement = OptionalSingleChild(parent, propertyElementName);
        return ReadFormattingProperties(propertyElement, domain);
    }

    internal static WordStylePropertySet ReadFormattingProperties(
        XElement? propertyElement,
        WordFormattingDomain domain
    )
    {
        if (propertyElement is null)
        {
            return EmptyPropertySet;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var unmodeled = new List<string>();
        foreach (var child in propertyElement.Elements())
        {
            if (!IsWordNamespace(child.Name.NamespaceName))
            {
                unmodeled.Add(QualifiedName(child.Name));
                continue;
            }

            if (!TryReadFormattingElement(child, domain, values))
            {
                unmodeled.Add(child.Name.LocalName);
            }
        }

        return new WordStylePropertySet(values, unmodeled);
    }

    private static bool TryReadFormattingElement(
        XElement element,
        WordFormattingDomain domain,
        Dictionary<string, string> values
    )
    {
        var localName = element.Name.LocalName;
        var w = element.Name.Namespace;
        if (domain == WordFormattingDomain.Paragraph)
        {
            if (localName == "pStyle")
            {
                return true;
            }

            if (ParagraphOnOffProperties.TryGetValue(localName, out var onOffName))
            {
                AddUnique(values, onOffName, ParseOnOffElement(element));
                return true;
            }

            if (ParagraphValueProperties.TryGetValue(localName, out var valueName))
            {
                AddUnique(values, valueName, RequiredAttribute(element, w + "val", localName));
                return true;
            }

            if (localName == "spacing")
            {
                AddAttributes(values, element, "spacing", ParagraphSpacingAttributes);
                return true;
            }

            if (localName == "ind")
            {
                AddAttributes(values, element, "indent", ParagraphIndentAttributes);
                return true;
            }

            if (localName == "shd")
            {
                AddAttributes(values, element, "shading", ShadingAttributes);
                return true;
            }

            if (localName == "numPr")
            {
                var level = OptionalSingleChild(element, w + "ilvl");
                var number = OptionalSingleChild(element, w + "numId");
                if (level is not null)
                {
                    AddUnique(values, "numbering_level", RequiredAttribute(level, w + "val", "ilvl"));
                }
                if (number is not null)
                {
                    AddUnique(values, "numbering_id", RequiredAttribute(number, w + "val", "numId"));
                }
                return element.Elements().All(child => child.Name == w + "ilvl" || child.Name == w + "numId");
            }

            return false;
        }

        if (domain == WordFormattingDomain.Run)
        {
            if (localName == "rStyle")
            {
                return true;
            }

            if (RunOnOffProperties.TryGetValue(localName, out var onOffName))
            {
                AddUnique(values, onOffName, ParseOnOffElement(element));
                return true;
            }

            if (RunValueProperties.TryGetValue(localName, out var valueName))
            {
                AddUnique(values, valueName, RequiredAttribute(element, w + "val", localName));
                return true;
            }

            if (localName == "rFonts")
            {
                AddAttributes(values, element, "font", RunFontAttributes);
                return true;
            }

            if (localName == "color")
            {
                AddAttributes(values, element, "color", ColorAttributes);
                return true;
            }

            if (localName == "u")
            {
                AddAttributes(values, element, "underline", UnderlineAttributes);
                return true;
            }

            if (localName == "shd")
            {
                AddAttributes(values, element, "shading", ShadingAttributes);
                return true;
            }

            if (localName == "lang")
            {
                AddAttributes(values, element, "language", LanguageAttributes);
                return true;
            }

            return false;
        }

        return false;
    }

    private static void AddAttributes(
        Dictionary<string, string> values,
        XElement element,
        string prefix,
        IReadOnlyDictionary<string, string> mappings
    )
    {
        var w = element.Name.Namespace;
        foreach (var mapping in mappings)
        {
            var attribute = element.Attribute(w + mapping.Key);
            if (attribute is not null)
            {
                AddUnique(values, $"{prefix}_{mapping.Value}", attribute.Value);
            }
        }

        if (element.Attributes().Any(attribute =>
            !attribute.IsNamespaceDeclaration
            && attribute.Name.Namespace == w
            && !mappings.ContainsKey(attribute.Name.LocalName)
        ))
        {
            throw new WordStyleProjectionException(
                $"Formatting element '{element.Name.LocalName}' contains an unmodeled Word attribute."
            );
        }
    }

    private static void AddUnique(
        Dictionary<string, string> values,
        string name,
        string value
    )
    {
        if (!values.TryAdd(name, value))
        {
            throw new WordStyleProjectionException(
                $"Formatting property '{name}' is declared more than once in one property set."
            );
        }
    }

    private static string? ChildValue(XElement parent, XName name, XNamespace w)
    {
        var child = OptionalSingleChild(parent, name);
        return child?.Attribute(w + "val")?.Value;
    }

    private static bool? ChildOnOff(XElement parent, XName name, XNamespace w)
    {
        _ = w;
        var child = OptionalSingleChild(parent, name);
        return child is null ? null : ParseOnOffElement(child) == "true";
    }

    private static int? ChildNonNegativeInt(XElement parent, XName name, XNamespace w)
    {
        var child = OptionalSingleChild(parent, name);
        return child is null ? null : OptionalNonNegativeInt(child, w + "val");
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
            throw new WordStyleProjectionException(
                $"Element '{parent.Name.LocalName}' contains duplicate '{name.LocalName}' children."
            );
        }

        return children.FirstOrDefault();
    }

    private static string RequiredAttribute(XElement element, XName name, string description)
    {
        var value = element.Attribute(name)?.Value;
        if (string.IsNullOrEmpty(value))
        {
            throw new WordStyleProjectionException(
                $"Element '{element.Name.LocalName}' has no {description}."
            );
        }

        return value;
    }

    private static bool? OptionalOnOffAttribute(XElement element, XName name)
    {
        var value = element.Attribute(name)?.Value;
        return value is null ? null : ParseOnOff(value, name.LocalName);
    }

    private static string ParseOnOffElement(XElement element)
    {
        var raw = element.Attribute(element.Name.Namespace + "val")?.Value;
        return (raw is null ? true : ParseOnOff(raw, element.Name.LocalName))
            ? "true"
            : "false";
    }

    private static bool ParseOnOff(string value, string description) =>
        value.ToLowerInvariant() switch
        {
            "true" or "1" or "on" => true,
            "false" or "0" or "off" => false,
            _ => throw new WordStyleProjectionException(
                $"'{description}' has invalid on/off value '{value}'."
            ),
        };

    private static int? OptionalNonNegativeInt(XElement element, XName name)
    {
        var value = element.Attribute(name)?.Value;
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0)
        {
            throw new WordStyleProjectionException(
                $"'{name.LocalName}' has invalid non-negative integer value '{value}'."
            );
        }

        return result;
    }

    private static string QualifiedName(XName name) =>
        $"{{{name.NamespaceName}}}{name.LocalName}";

    private static bool IsWordNamespace(string namespaceName) =>
        string.Equals(namespaceName, WordTransitionalNamespace, StringComparison.Ordinal)
        || string.Equals(namespaceName, WordStrictNamespace, StringComparison.Ordinal);

    internal enum WordFormattingDomain
    {
        Paragraph,
        Run,
        Table,
        TableCell,
    }

    private sealed record ParsedStyle(
        string StyleId,
        WordStyleType Type,
        string? Name,
        IReadOnlyList<string> Aliases,
        string? BasedOnStyleId,
        string? NextStyleId,
        string? LinkedStyleId,
        bool IsDefault,
        bool IsCustom,
        bool? QuickFormat,
        bool? SemiHidden,
        bool? UnhideWhenUsed,
        bool? Locked,
        int? UiPriority,
        int SourceElementOrdinal,
        WordStylePropertySet ParagraphProperties,
        WordStylePropertySet RunProperties,
        WordStylePropertySet TableProperties,
        WordStylePropertySet TableCellProperties
    )
    {
        public WordStyleDefinition Freeze(InheritanceResolution resolution) => new(
            StyleId,
            Type,
            Name,
            Aliases,
            BasedOnStyleId,
            NextStyleId,
            LinkedStyleId,
            IsDefault,
            IsCustom,
            QuickFormat,
            SemiHidden,
            UnhideWhenUsed,
            Locked,
            UiPriority,
            SourceElementOrdinal,
            ParagraphProperties,
            RunProperties,
            TableProperties,
            TableCellProperties,
            resolution.Resolvable,
            resolution.Failure,
            resolution.Chain
        );
    }

    private sealed record InheritanceResolution(
        bool Resolvable,
        string? Failure,
        IReadOnlyList<string> Chain
    )
    {
        public static InheritanceResolution Succeeded(IReadOnlyList<string> chain) =>
            new(true, null, chain);

        public static InheritanceResolution Failed(string failure) =>
            new(false, failure, Array.Empty<string>());
    }

    private static readonly IReadOnlyDictionary<string, string> ParagraphOnOffProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["keepNext"] = "keep_with_next",
            ["keepLines"] = "keep_together",
            ["pageBreakBefore"] = "page_break_before",
            ["widowControl"] = "widow_control",
            ["suppressLineNumbers"] = "suppress_line_numbers",
            ["contextualSpacing"] = "contextual_spacing",
            ["bidi"] = "bidi",
            ["mirrorIndents"] = "mirror_indents",
            ["suppressOverlap"] = "suppress_overlap",
            ["adjustRightInd"] = "adjust_right_indent",
            ["snapToGrid"] = "snap_to_grid",
            ["kinsoku"] = "kinsoku",
            ["wordWrap"] = "word_wrap",
            ["overflowPunct"] = "overflow_punctuation",
            ["topLinePunct"] = "top_line_punctuation",
            ["autoSpaceDE"] = "auto_space_de",
            ["autoSpaceDN"] = "auto_space_dn",
        };

    private static readonly IReadOnlyDictionary<string, string> ParagraphValueProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jc"] = "alignment",
            ["outlineLvl"] = "outline_level",
            ["textDirection"] = "text_direction",
            ["divId"] = "division_id",
        };

    private static readonly IReadOnlyDictionary<string, string> RunOnOffProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["b"] = "bold",
            ["bCs"] = "bold_complex_script",
            ["i"] = "italic",
            ["iCs"] = "italic_complex_script",
            ["caps"] = "all_caps",
            ["smallCaps"] = "small_caps",
            ["strike"] = "strike",
            ["dstrike"] = "double_strike",
            ["outline"] = "outline",
            ["shadow"] = "shadow",
            ["emboss"] = "emboss",
            ["imprint"] = "imprint",
            ["vanish"] = "hidden",
            ["specVanish"] = "special_hidden",
            ["noProof"] = "no_proof",
            ["snapToGrid"] = "snap_to_grid",
            ["rtl"] = "rtl",
            ["cs"] = "complex_script",
            ["webHidden"] = "web_hidden",
        };

    private static readonly IReadOnlyDictionary<string, string> RunValueProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sz"] = "size_half_points",
            ["szCs"] = "size_complex_script_half_points",
            ["vertAlign"] = "vertical_alignment",
            ["highlight"] = "highlight",
            ["spacing"] = "character_spacing_twips",
            ["w"] = "character_scale_percent",
            ["position"] = "position_half_points",
            ["kern"] = "kerning_half_points",
            ["effect"] = "text_effect",
            ["em"] = "emphasis_mark",
        };

    private static readonly IReadOnlyDictionary<string, string> ParagraphSpacingAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["before"] = "before_twips",
            ["after"] = "after_twips",
            ["line"] = "line",
            ["lineRule"] = "line_rule",
            ["beforeLines"] = "before_lines_hundredths",
            ["afterLines"] = "after_lines_hundredths",
            ["beforeAutospacing"] = "before_auto",
            ["afterAutospacing"] = "after_auto",
        };

    private static readonly IReadOnlyDictionary<string, string> ParagraphIndentAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["left"] = "left_twips",
            ["right"] = "right_twips",
            ["start"] = "start_twips",
            ["end"] = "end_twips",
            ["firstLine"] = "first_line_twips",
            ["hanging"] = "hanging_twips",
            ["leftChars"] = "left_chars_hundredths",
            ["rightChars"] = "right_chars_hundredths",
            ["startChars"] = "start_chars_hundredths",
            ["endChars"] = "end_chars_hundredths",
            ["firstLineChars"] = "first_line_chars_hundredths",
            ["hangingChars"] = "hanging_chars_hundredths",
        };

    private static readonly IReadOnlyDictionary<string, string> RunFontAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ascii"] = "ascii",
            ["hAnsi"] = "high_ansi",
            ["eastAsia"] = "east_asia",
            ["cs"] = "complex_script",
            ["asciiTheme"] = "ascii_theme",
            ["hAnsiTheme"] = "high_ansi_theme",
            ["eastAsiaTheme"] = "east_asia_theme",
            ["cstheme"] = "complex_script_theme",
            ["hint"] = "hint",
        };

    private static readonly IReadOnlyDictionary<string, string> ColorAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["val"] = "value",
            ["themeColor"] = "theme",
            ["themeTint"] = "theme_tint",
            ["themeShade"] = "theme_shade",
        };

    private static readonly IReadOnlyDictionary<string, string> LanguageAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["val"] = "value",
            ["eastAsia"] = "east_asia",
            ["bidi"] = "bidi",
        };

    private static readonly IReadOnlyDictionary<string, string> UnderlineAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["val"] = "value",
            ["color"] = "color",
            ["themeColor"] = "theme",
            ["themeTint"] = "theme_tint",
            ["themeShade"] = "theme_shade",
        };

    private static readonly IReadOnlyDictionary<string, string> ShadingAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["val"] = "pattern",
            ["color"] = "color",
            ["fill"] = "fill",
            ["themeColor"] = "theme_color",
            ["themeTint"] = "theme_tint",
            ["themeShade"] = "theme_shade",
            ["themeFill"] = "theme_fill",
            ["themeFillTint"] = "theme_fill_tint",
            ["themeFillShade"] = "theme_fill_shade",
        };
}

public class WordStyleProjectionException : IOException
{
    public WordStyleProjectionException(string message)
        : base(message)
    {
    }

    public WordStyleProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordStyleLimitException : WordStyleProjectionException
{
    public WordStyleLimitException(string message)
        : base(message)
    {
    }
}
