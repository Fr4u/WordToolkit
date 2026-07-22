using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordMceIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordMceRuleKind
{
    Ignorable,
    ProcessContent,
    MustUnderstand,
    LegacyPreserveElements,
    LegacyPreserveAttributes,
    Unknown,
}

public enum WordMceElementDisposition
{
    RetainedWithIgnoredAttributes,
    Ignored,
    Unwrapped,
}

public enum WordMceBranchKind
{
    Choice,
    Fallback,
}

public sealed record WordMceExpandedName(string NamespaceUri, string LocalName);

public sealed record WordMceApplicationConfiguration
{
    public static WordMceApplicationConfiguration Empty { get; } = new();

    public IReadOnlyCollection<string> UnderstoodNamespaces { get; init; } =
        Array.Empty<string>();

    public IReadOnlyCollection<WordMceExpandedName> ApplicationDefinedExtensionElements
    {
        get;
        init;
    } = Array.Empty<WordMceExpandedName>();
}

public sealed record WordMceIssue(
    string Code,
    WordMceIssueSeverity Severity,
    string Message,
    string? PartId = null,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? RuleId = null,
    string? AlternateContentId = null
);

public sealed record WordMceNamespaceDefinition(
    string Id,
    string NamespaceUri,
    int ElementOccurrenceCount,
    int AttributeOccurrenceCount,
    int IgnorableDeclarationCount,
    int ProcessContentReferenceCount,
    int MustUnderstandReferenceCount,
    int ChoiceRequirementCount,
    bool UnderstoodByConfiguration
);

public sealed record WordMceNameReference(
    string NamespaceId,
    string NamespaceUri,
    string? LocalName,
    bool IsWildcard
);

public sealed record WordMceRuleDefinition(
    string Id,
    string PartId,
    string PartUri,
    WordMceRuleKind Kind,
    int SourceElementOrdinal,
    int TokenCount,
    int InvalidTokenCount,
    IReadOnlyList<WordMceNameReference> ResolvedNames
);

public sealed record WordMceBranchDefinition(
    string Id,
    WordMceBranchKind Kind,
    int SourceElementOrdinal,
    IReadOnlyList<string> RequiredNamespaceIds,
    bool RequirementsValid,
    bool Selected
);

public sealed record WordMceAlternateContentDefinition(
    string Id,
    string PartId,
    string PartUri,
    int SourceElementOrdinal,
    bool StructureConformant,
    string? SelectedBranchId,
    IReadOnlyList<WordMceBranchDefinition> Branches
);

public sealed record WordMceAffectedElement(
    string Id,
    string PartId,
    string PartUri,
    int SourceElementOrdinal,
    string NamespaceId,
    string NamespaceUri,
    string LocalName,
    WordMceElementDisposition Disposition,
    int IgnoredAttributeCount,
    bool AffectsOutput
);

public sealed record WordMceMustUnderstandMismatch(
    string Id,
    string PartId,
    string PartUri,
    int SourceElementOrdinal,
    string NamespaceId,
    string NamespaceUri,
    bool AffectsOutput
);

public sealed record WordMcePartDefinition(
    string Id,
    string PartUri,
    string? ContentType,
    string SourceSha256,
    int ElementCount,
    int RuleCount,
    int AlternateContentCount,
    int AffectedElementCount,
    int MustUnderstandMismatchCount,
    int IssueCount,
    bool Parsed
);

public sealed class WordMarkupCompatibilityGraph
{
    internal WordMarkupCompatibilityGraph(
        string packageFingerprint,
        string applicationConfigurationFingerprint,
        IReadOnlyList<WordMceNamespaceDefinition> namespaces,
        IReadOnlyList<WordMcePartDefinition> parts,
        IReadOnlyList<WordMceRuleDefinition> rules,
        IReadOnlyList<WordMceAlternateContentDefinition> alternateContent,
        IReadOnlyList<WordMceAffectedElement> affectedElements,
        IReadOnlyList<WordMceMustUnderstandMismatch> mustUnderstandMismatches,
        IReadOnlyList<WordMceIssue> issues,
        bool issuesTruncated,
        long parsedXmlBytes,
        int parsedElementCount
    )
    {
        PackageFingerprint = packageFingerprint;
        ApplicationConfigurationFingerprint = applicationConfigurationFingerprint;
        Namespaces = new ReadOnlyCollection<WordMceNamespaceDefinition>(
            namespaces.ToArray()
        );
        Parts = new ReadOnlyCollection<WordMcePartDefinition>(parts.ToArray());
        Rules = new ReadOnlyCollection<WordMceRuleDefinition>(rules.ToArray());
        AlternateContent = new ReadOnlyCollection<WordMceAlternateContentDefinition>(
            alternateContent.ToArray()
        );
        AffectedElements = new ReadOnlyCollection<WordMceAffectedElement>(
            affectedElements.ToArray()
        );
        MustUnderstandMismatches = new ReadOnlyCollection<WordMceMustUnderstandMismatch>(
            mustUnderstandMismatches.ToArray()
        );
        Issues = new ReadOnlyCollection<WordMceIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        ParsedXmlBytes = parsedXmlBytes;
        ParsedElementCount = parsedElementCount;
    }

    public string PackageFingerprint { get; }

    public string ApplicationConfigurationFingerprint { get; }

    public IReadOnlyList<WordMceNamespaceDefinition> Namespaces { get; }

    public IReadOnlyList<WordMcePartDefinition> Parts { get; }

    public IReadOnlyList<WordMceRuleDefinition> Rules { get; }

    public IReadOnlyList<WordMceAlternateContentDefinition> AlternateContent { get; }

    public IReadOnlyList<WordMceAffectedElement> AffectedElements { get; }

    public IReadOnlyList<WordMceMustUnderstandMismatch> MustUnderstandMismatches { get; }

    public IReadOnlyList<WordMceIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public long ParsedXmlBytes { get; }

    public int ParsedElementCount { get; }
}

public sealed record WordMarkupCompatibilityGraphOptions
{
    public static WordMarkupCompatibilityGraphOptions Default { get; } = new();

    public int MaxXmlParts { get; init; } = 4_096;

    public long MaxTotalXmlBytes { get; init; } = 256L * 1024 * 1024;

    public int MaxXmlBytesPerPart { get; init; } = 64 * 1024 * 1024;

    public int MaxElementsPerPart { get; init; } = 500_000;

    public int MaxTotalElements { get; init; } = 1_000_000;

    public int MaxNamespaces { get; init; } = 8_192;

    public int MaxRules { get; init; } = 200_000;

    public int MaxAlternateContent { get; init; } = 100_000;

    public int MaxAffectedElements { get; init; } = 500_000;

    public int MaxMustUnderstandMismatches { get; init; } = 100_000;

    public int MaxConfigurationNamespaces { get; init; } = 1_024;

    public int MaxApplicationDefinedExtensionElements { get; init; } = 1_024;

    public int MaxIssues { get; init; } = 10_000;

    internal void Validate()
    {
        if (MaxXmlParts <= 0) throw new ArgumentOutOfRangeException(nameof(MaxXmlParts));
        if (MaxTotalXmlBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalXmlBytes));
        if (MaxXmlBytesPerPart <= 0) throw new ArgumentOutOfRangeException(nameof(MaxXmlBytesPerPart));
        if (MaxElementsPerPart <= 0) throw new ArgumentOutOfRangeException(nameof(MaxElementsPerPart));
        if (MaxTotalElements <= 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalElements));
        if (MaxNamespaces <= 0) throw new ArgumentOutOfRangeException(nameof(MaxNamespaces));
        if (MaxRules <= 0) throw new ArgumentOutOfRangeException(nameof(MaxRules));
        if (MaxAlternateContent <= 0) throw new ArgumentOutOfRangeException(nameof(MaxAlternateContent));
        if (MaxAffectedElements <= 0) throw new ArgumentOutOfRangeException(nameof(MaxAffectedElements));
        if (MaxMustUnderstandMismatches <= 0) throw new ArgumentOutOfRangeException(nameof(MaxMustUnderstandMismatches));
        if (MaxConfigurationNamespaces <= 0) throw new ArgumentOutOfRangeException(nameof(MaxConfigurationNamespaces));
        if (MaxApplicationDefinedExtensionElements <= 0) throw new ArgumentOutOfRangeException(nameof(MaxApplicationDefinedExtensionElements));
        if (MaxIssues <= 0) throw new ArgumentOutOfRangeException(nameof(MaxIssues));
    }
}

public sealed class WordMarkupCompatibilityGraphBuilder
{
    public const string MarkupCompatibilityNamespace =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";
    private readonly WordMarkupCompatibilityGraphOptions _options;

    public WordMarkupCompatibilityGraphBuilder(
        WordMarkupCompatibilityGraphOptions? options = null
    )
    {
        _options = options ?? WordMarkupCompatibilityGraphOptions.Default;
        _options.Validate();
    }

    public WordMarkupCompatibilityGraph Build(
        OpcPackageSnapshot package,
        WordMceApplicationConfiguration? applicationConfiguration = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        applicationConfiguration ??= WordMceApplicationConfiguration.Empty;
        cancellationToken.ThrowIfCancellationRequested();

        var understoodNamespaces = ValidateConfigurationNamespaces(
            applicationConfiguration.UnderstoodNamespaces
        );
        var extensionElements = ValidateExtensionElements(
            applicationConfiguration.ApplicationDefinedExtensionElements
        );
        var configurationFingerprint = ConfigurationFingerprint(
            understoodNamespaces,
            extensionElements
        );
        var issues = new IssueState(_options.MaxIssues);
        var namespaceStats = new Dictionary<string, NamespaceStats>(StringComparer.Ordinal);
        var parts = new List<WordMcePartDefinition>();
        var rules = new List<WordMceRuleDefinition>();
        var alternateContent = new List<WordMceAlternateContentDefinition>();
        var affectedElements = new List<WordMceAffectedElement>();
        var mustUnderstandMismatches = new List<WordMceMustUnderstandMismatch>();
        var xmlParts = package.Parts.Values
            .Where(IsXmlPart)
            .OrderBy(part => part.Uri, StringComparer.Ordinal)
            .ToArray();
        if (xmlParts.Length > _options.MaxXmlParts)
        {
            throw new WordMceLimitException(
                $"Package contains {xmlParts.Length} XML parts; limit is {_options.MaxXmlParts}."
            );
        }

        long totalBytes = 0;
        var totalElements = 0;
        foreach (var part in xmlParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partBytes = part.Entry.Content.Length;
            if (partBytes > _options.MaxXmlBytesPerPart)
            {
                throw new WordMceLimitException(
                    $"XML part '{part.Uri}' contains {partBytes} bytes; per-part limit is {_options.MaxXmlBytesPerPart}."
                );
            }
            totalBytes = checked(totalBytes + partBytes);
            if (totalBytes > _options.MaxTotalXmlBytes)
            {
                throw new WordMceLimitException(
                    $"XML parts contain more than {_options.MaxTotalXmlBytes} bytes in total."
                );
            }

            var partId = StableId("wmcp_", part.Uri);
            var partIssueStart = issues.Count;
            var partRuleStart = rules.Count;
            var partAlternateStart = alternateContent.Count;
            var partAffectedStart = affectedElements.Count;
            var partMismatchStart = mustUnderstandMismatches.Count;
            LosslessXmlDocument xml;
            try
            {
                xml = LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    new LosslessXmlOptions
                    {
                        MaxSourceBytes = _options.MaxXmlBytesPerPart,
                        MaxXmlCharacters = _options.MaxXmlBytesPerPart,
                        MaxXmlElements = _options.MaxElementsPerPart,
                        MaxXmlDepth = 256,
                        MaxTextCharacters = _options.MaxXmlBytesPerPart,
                    },
                    cancellationToken
                );
            }
            catch (LosslessXmlLimitException exception)
            {
                throw new WordMceLimitException(
                    $"XML part '{part.Uri}' exceeds an MCE parsing limit.",
                    exception
                );
            }
            catch (LosslessXmlException)
            {
                issues.Add(new WordMceIssue(
                    "MCE_XML_PART_NOT_WELL_FORMED",
                    WordMceIssueSeverity.Error,
                    "An XML-typed part could not be parsed as safe, well-formed XML.",
                    partId,
                    part.Uri
                ));
                parts.Add(new WordMcePartDefinition(
                    partId,
                    part.Uri,
                    part.ContentType,
                    part.Entry.Sha256,
                    0,
                    0,
                    0,
                    0,
                    0,
                    issues.Count - partIssueStart,
                    false
                ));
                continue;
            }

            totalElements = checked(totalElements + xml.Elements.Count);
            if (totalElements > _options.MaxTotalElements)
            {
                throw new WordMceLimitException(
                    $"Parsed XML contains more than {_options.MaxTotalElements} elements in total."
                );
            }

            AnalyzePart(
                part,
                partId,
                xml,
                understoodNamespaces,
                extensionElements,
                namespaceStats,
                rules,
                alternateContent,
                affectedElements,
                mustUnderstandMismatches,
                issues,
                cancellationToken
            );
            parts.Add(new WordMcePartDefinition(
                partId,
                part.Uri,
                part.ContentType,
                xml.SourceSha256,
                xml.Elements.Count,
                rules.Count - partRuleStart,
                alternateContent.Count - partAlternateStart,
                affectedElements.Count - partAffectedStart,
                mustUnderstandMismatches.Count - partMismatchStart,
                issues.Count - partIssueStart,
                true
            ));
        }

        var namespaceDefinitions = namespaceStats
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value.ToDefinition(
                pair.Key,
                understoodNamespaces.Contains(pair.Key)
            ))
            .ToArray();
        return new WordMarkupCompatibilityGraph(
            package.Fingerprint,
            configurationFingerprint,
            namespaceDefinitions,
            parts,
            rules,
            alternateContent,
            affectedElements,
            mustUnderstandMismatches,
            issues.Items,
            issues.Truncated,
            totalBytes,
            totalElements
        );
    }

    private void AnalyzePart(
        OpcPart part,
        string partId,
        LosslessXmlDocument xml,
        HashSet<string> understoodNamespaces,
        HashSet<ExpandedNameKey> extensionElements,
        Dictionary<string, NamespaceStats> namespaceStats,
        List<WordMceRuleDefinition> rules,
        List<WordMceAlternateContentDefinition> alternateContent,
        List<WordMceAffectedElement> affectedElements,
        List<WordMceMustUnderstandMismatch> mustUnderstandMismatches,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var parsedElements = xml.ParsedDocument.Root?.DescendantsAndSelf().ToArray()
            ?? throw new WordMceProjectionException(
                $"XML part '{part.Uri}' does not contain a document element."
            );
        if (parsedElements.Length != xml.Elements.Count)
        {
            throw new WordMceProjectionException(
                $"XML part '{part.Uri}' has inconsistent lexical and parsed element counts."
            );
        }

        var contexts = new ElementContext[xml.Elements.Count];
        var dispositions = new WordMceElementDisposition?[xml.Elements.Count];
        var ignoredAttributeCounts = new int[xml.Elements.Count];
        var localMustUnderstand = new IReadOnlyList<string>[xml.Elements.Count];
        var alternateStructures = new Dictionary<int, AlternateStructure>();

        for (var ordinal = 0; ordinal < xml.Elements.Count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = xml.Elements[ordinal];
            var parsed = parsedElements[ordinal];
            CountNamespaceOccurrences(parsed, namespaceStats);
            EnsureNamespaceLimit(namespaceStats.Count);

            var parentContext = source.ParentOrdinal is int parentOrdinal
                ? contexts[parentOrdinal]
                : ElementContext.Empty;
            var context = parentContext;
            var expandedName = new ExpandedNameKey(
                parsed.Name.NamespaceName,
                parsed.Name.LocalName
            );
            if (
                !context.InsideApplicationDefinedExtension
                && extensionElements.Contains(expandedName)
            )
            {
                context = context.EnterApplicationDefinedExtension();
            }

            if (context.InsideApplicationDefinedExtension)
            {
                localMustUnderstand[ordinal] = Array.Empty<string>();
                contexts[ordinal] = context;
                continue;
            }

            var mcAttributes = source.Attributes
                .Where(attribute => string.Equals(
                    attribute.NamespaceUri,
                    MarkupCompatibilityNamespace,
                    StringComparison.Ordinal
                ))
                .ToArray();
            foreach (var attribute in mcAttributes)
            {
                if (attribute.LocalName == "Ignorable")
                {
                    var parsedRule = ParsePrefixRule(
                        part,
                        partId,
                        source,
                        parsed,
                        WordMceRuleKind.Ignorable,
                        attribute.Value,
                        allowEmpty: true,
                        issues
                    );
                    AddRule(rules, parsedRule.Rule);
                    foreach (var namespaceUri in parsedRule.NamespaceUris)
                    {
                        NamespaceStat(namespaceStats, namespaceUri)
                            .IgnorableDeclarationCount++;
                    }
                    context = context.WithIgnorableNamespaces(
                        parsedRule.NamespaceUris
                    );
                    continue;
                }

                if (attribute.LocalName == "ProcessContent")
                {
                    var parsedRule = ParseQualifiedNameRule(
                        part,
                        partId,
                        source,
                        parsed,
                        WordMceRuleKind.ProcessContent,
                        attribute.Value,
                        allowEmpty: true,
                        context.IgnorableNamespaces,
                        issues
                    );
                    AddRule(rules, parsedRule.Rule);
                    foreach (var name in parsedRule.Names)
                    {
                        NamespaceStat(namespaceStats, name.NamespaceUri)
                            .ProcessContentReferenceCount++;
                    }
                    context = context.WithProcessContentNames(parsedRule.Names);
                    continue;
                }

                if (attribute.LocalName == "MustUnderstand")
                {
                    var parsedRule = ParsePrefixRule(
                        part,
                        partId,
                        source,
                        parsed,
                        WordMceRuleKind.MustUnderstand,
                        attribute.Value,
                        allowEmpty: true,
                        issues
                    );
                    AddRule(rules, parsedRule.Rule);
                    localMustUnderstand[ordinal] = parsedRule.NamespaceUris;
                    foreach (var namespaceUri in parsedRule.NamespaceUris)
                    {
                        NamespaceStat(namespaceStats, namespaceUri)
                            .MustUnderstandReferenceCount++;
                    }
                    continue;
                }

                if (
                    attribute.LocalName is "PreserveElements" or "PreserveAttributes"
                )
                {
                    var kind = attribute.LocalName == "PreserveElements"
                        ? WordMceRuleKind.LegacyPreserveElements
                        : WordMceRuleKind.LegacyPreserveAttributes;
                    var parsedRule = ParseQualifiedNameRule(
                        part,
                        partId,
                        source,
                        parsed,
                        kind,
                        attribute.Value,
                        allowEmpty: true,
                        context.IgnorableNamespaces,
                        issues
                    );
                    AddRule(rules, parsedRule.Rule);
                    issues.Add(new WordMceIssue(
                        "MCE_LEGACY_PRESERVATION_HINT",
                        WordMceIssueSeverity.Info,
                        "A legacy preservation hint is retained and inventoried but is not part of the ECMA-376 Part 3 fifth-edition processing model.",
                        partId,
                        part.Uri,
                        source.Ordinal,
                        parsedRule.Rule.Id
                    ));
                    continue;
                }

                var unknownRule = new WordMceRuleDefinition(
                    StableId(
                        "wmcr_",
                        part.Uri,
                        source.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        attribute.LocalName
                    ),
                    partId,
                    part.Uri,
                    WordMceRuleKind.Unknown,
                    source.Ordinal,
                    SplitTokens(attribute.Value).Length,
                    SplitTokens(attribute.Value).Length,
                    Array.Empty<WordMceNameReference>()
                );
                AddRule(rules, unknownRule);
                issues.Add(new WordMceIssue(
                    "MCE_UNKNOWN_ATTRIBUTE",
                    WordMceIssueSeverity.Error,
                    "An attribute in the Markup Compatibility namespace is not defined by the supported current or legacy MCE vocabulary.",
                    partId,
                    part.Uri,
                    source.Ordinal,
                    unknownRule.Id
                ));
            }
            localMustUnderstand[ordinal] ??= Array.Empty<string>();
            EnsureNamespaceLimit(namespaceStats.Count);

            if (!context.InsideApplicationDefinedExtension)
            {
                var namespaceUri = parsed.Name.NamespaceName;
                if (
                    context.IgnorableNamespaces.Contains(namespaceUri)
                    && !understoodNamespaces.Contains(namespaceUri)
                )
                {
                    dispositions[ordinal] = context.ProcessContentNames.Any(name =>
                        name.Matches(namespaceUri, parsed.Name.LocalName)
                    )
                        ? WordMceElementDisposition.Unwrapped
                        : WordMceElementDisposition.Ignored;
                }

                ignoredAttributeCounts[ordinal] = parsed.Attributes().Count(attribute =>
                    !attribute.IsNamespaceDeclaration
                    && attribute.Name.NamespaceName.Length != 0
                    && attribute.Name.NamespaceName != MarkupCompatibilityNamespace
                    && context.IgnorableNamespaces.Contains(attribute.Name.NamespaceName)
                    && !understoodNamespaces.Contains(attribute.Name.NamespaceName)
                );
            }

            if (dispositions[ordinal] == WordMceElementDisposition.Unwrapped)
            {
                var prohibitedXmlAttribute = parsed.Attributes().Any(attribute =>
                    attribute.Name.NamespaceName == XmlNamespace
                    && attribute.Name.LocalName is "base" or "lang" or "space"
                );
                if (prohibitedXmlAttribute)
                {
                    issues.Add(new WordMceIssue(
                        "MCE_UNWRAPPED_ELEMENT_HAS_XML_CONTEXT_ATTRIBUTE",
                        WordMceIssueSeverity.Error,
                        "An element selected for ProcessContent unwrapping carries xml:base, xml:lang, or xml:space, which the reference processing model forbids.",
                        partId,
                        part.Uri,
                        source.Ordinal
                    ));
                }
            }

            if (parsed.Name.NamespaceName == MarkupCompatibilityNamespace)
            {
                ValidateMceElement(
                    part,
                    partId,
                    source,
                    parsed,
                    context,
                    issues
                );
            }
            contexts[ordinal] = context;
        }

        foreach (var source in xml.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = parsedElements[source.Ordinal];
            if (
                parsed.Name.NamespaceName != MarkupCompatibilityNamespace
                || parsed.Name.LocalName != "AlternateContent"
                || contexts[source.Ordinal].InsideApplicationDefinedExtension
            )
            {
                continue;
            }
            var structure = BuildAlternateStructure(
                part,
                partId,
                source,
                parsedElements,
                contexts[source.Ordinal],
                understoodNamespaces,
                namespaceStats,
                issues
            );
            EnsureNamespaceLimit(namespaceStats.Count);
            alternateStructures.Add(source.Ordinal, structure);
            AddAlternateContent(alternateContent, structure.Definition);
        }

        var visible = new bool[xml.Elements.Count];
        for (var ordinal = 0; ordinal < xml.Elements.Count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = xml.Elements[ordinal];
            var isVisible = source.ParentOrdinal is not int parentOrdinal
                || ChildIsVisible(
                    source,
                    xml.Elements[parentOrdinal],
                    visible[parentOrdinal],
                    dispositions[parentOrdinal],
                    alternateStructures
                );
            visible[ordinal] = isVisible;

            var disposition = dispositions[ordinal];
            if (disposition is not null || ignoredAttributeCounts[ordinal] > 0)
            {
                var parsed = parsedElements[ordinal];
                AddAffectedElement(affectedElements, new WordMceAffectedElement(
                    StableId(
                        "wmce_",
                        part.Uri,
                        ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ),
                    partId,
                    part.Uri,
                    ordinal,
                    NamespaceId(parsed.Name.NamespaceName),
                    parsed.Name.NamespaceName,
                    parsed.Name.LocalName,
                    disposition ?? WordMceElementDisposition.RetainedWithIgnoredAttributes,
                    ignoredAttributeCounts[ordinal],
                    isVisible
                ));
            }

            if (
                !isVisible
                || dispositions[ordinal] == WordMceElementDisposition.Ignored
                || contexts[ordinal].InsideApplicationDefinedExtension
            )
            {
                continue;
            }
            foreach (var namespaceUri in localMustUnderstand[ordinal])
            {
                if (understoodNamespaces.Contains(namespaceUri))
                {
                    continue;
                }
                AddMismatch(mustUnderstandMismatches, new WordMceMustUnderstandMismatch(
                    StableId(
                        "wmcm_",
                        part.Uri,
                        ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        namespaceUri
                    ),
                    partId,
                    part.Uri,
                    ordinal,
                    NamespaceId(namespaceUri),
                    namespaceUri,
                    true
                ));
            }
        }
    }

    private AlternateStructure BuildAlternateStructure(
        OpcPart part,
        string partId,
        XmlSourceElement source,
        IReadOnlyList<XElement> parsedElements,
        ElementContext context,
        HashSet<string> understoodNamespaces,
        Dictionary<string, NamespaceStats> namespaceStats,
        IssueState issues
    )
    {
        var branches = new List<WordMceBranchDefinition>();
        var selected = false;
        string? selectedBranchId = null;
        var conformant = AlternateContentStructureIsConformant(
            parsedElements[source.Ordinal],
            context
        );
        var seenFallback = false;
        var choiceCount = 0;
        foreach (var child in source.Children)
        {
            var parsedChild = parsedElements[child.Ordinal];
            if (parsedChild.Name.NamespaceName != MarkupCompatibilityNamespace)
            {
                continue;
            }
            if (parsedChild.Name.LocalName == "Choice")
            {
                choiceCount++;
                if (seenFallback)
                {
                    conformant = false;
                }
                var requires = ParseRequires(
                    part,
                    partId,
                    child,
                    parsedChild,
                    issues
                );
                conformant &= requires.Valid;
                foreach (var namespaceUri in requires.NamespaceUris)
                {
                    NamespaceStat(namespaceStats, namespaceUri).ChoiceRequirementCount++;
                }
                var branchId = StableId(
                    "wmcb_",
                    part.Uri,
                    child.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
                );
                var branchSelected = !selected
                    && requires.Valid
                    && requires.NamespaceUris.All(understoodNamespaces.Contains);
                if (branchSelected)
                {
                    selected = true;
                    selectedBranchId = branchId;
                }
                branches.Add(new WordMceBranchDefinition(
                    branchId,
                    WordMceBranchKind.Choice,
                    child.Ordinal,
                    requires.NamespaceUris.Select(NamespaceId).ToArray(),
                    requires.Valid,
                    branchSelected
                ));
                continue;
            }
            if (parsedChild.Name.LocalName == "Fallback")
            {
                if (seenFallback)
                {
                    conformant = false;
                }
                seenFallback = true;
                var branchId = StableId(
                    "wmcb_",
                    part.Uri,
                    child.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
                );
                var branchSelected = !selected;
                if (branchSelected)
                {
                    selected = true;
                    selectedBranchId = branchId;
                }
                branches.Add(new WordMceBranchDefinition(
                    branchId,
                    WordMceBranchKind.Fallback,
                    child.Ordinal,
                    Array.Empty<string>(),
                    true,
                    branchSelected
                ));
                continue;
            }
            conformant = false;
        }
        if (choiceCount == 0)
        {
            conformant = false;
        }

        var id = StableId(
            "wmca_",
            part.Uri,
            source.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        return new AlternateStructure(
            new WordMceAlternateContentDefinition(
                id,
                partId,
                part.Uri,
                source.Ordinal,
                conformant,
                selectedBranchId,
                branches
            ),
            branches.ToDictionary(branch => branch.SourceElementOrdinal)
        );
    }

    private static bool AlternateContentStructureIsConformant(
        XElement alternate,
        ElementContext context
    )
    {
        if (alternate.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
            .Any(attribute =>
                attribute.Name.NamespaceName.Length == 0
                || (
                    attribute.Name.NamespaceName != MarkupCompatibilityNamespace
                    && !context.IgnorableNamespaces.Contains(attribute.Name.NamespaceName)
                )
            ))
        {
            return false;
        }

        var choiceCount = 0;
        var fallbackCount = 0;
        var fallbackSeen = false;
        foreach (var child in alternate.Elements())
        {
            if (child.Name.NamespaceName != MarkupCompatibilityNamespace)
            {
                if (!context.IgnorableNamespaces.Contains(child.Name.NamespaceName))
                {
                    return false;
                }
                continue;
            }
            if (child.Name.LocalName == "Choice")
            {
                if (fallbackSeen)
                {
                    return false;
                }
                choiceCount++;
                continue;
            }
            if (child.Name.LocalName == "Fallback")
            {
                fallbackSeen = true;
                fallbackCount++;
                continue;
            }
            return false;
        }
        return choiceCount > 0 && fallbackCount <= 1;
    }

    private void ValidateMceElement(
        OpcPart part,
        string partId,
        XmlSourceElement source,
        XElement parsed,
        ElementContext context,
        IssueState issues
    )
    {
        if (parsed.Attributes().Any(attribute => attribute.Name.NamespaceName == XmlNamespace))
        {
            issues.Add(new WordMceIssue(
                "MCE_ELEMENT_HAS_XML_NAMESPACE_ATTRIBUTE",
                WordMceIssueSeverity.Error,
                "An MCE element contains an attribute in the XML namespace.",
                partId,
                part.Uri,
                source.Ordinal
            ));
        }

        if (parsed.Name.LocalName is not "AlternateContent" and not "Choice" and not "Fallback")
        {
            issues.Add(new WordMceIssue(
                "MCE_UNKNOWN_ELEMENT",
                WordMceIssueSeverity.Error,
                "An element in the Markup Compatibility namespace is not part of the supported MCE vocabulary.",
                partId,
                part.Uri,
                source.Ordinal
            ));
            return;
        }

        if (parsed.Name.LocalName == "AlternateContent")
        {
            ValidateAlternateContent(part, partId, source, parsed, context, issues);
            return;
        }

        var parent = parsed.Parent;
        if (
            parent is null
            || parent.Name.NamespaceName != MarkupCompatibilityNamespace
            || parent.Name.LocalName != "AlternateContent"
        )
        {
            issues.Add(new WordMceIssue(
                parsed.Name.LocalName == "Choice"
                    ? "MCE_CHOICE_PARENT_INVALID"
                    : "MCE_FALLBACK_PARENT_INVALID",
                WordMceIssueSeverity.Error,
                "Choice and Fallback elements must be direct children of AlternateContent.",
                partId,
                part.Uri,
                source.Ordinal
            ));
        }

        var allowedUnqualified = parsed.Name.LocalName == "Choice" ? "Requires" : null;
        foreach (var attribute in parsed.Attributes().Where(attribute =>
            !attribute.IsNamespaceDeclaration
        ))
        {
            if (attribute.Name.NamespaceName.Length == 0)
            {
                if (attribute.Name.LocalName != allowedUnqualified)
                {
                    issues.Add(new WordMceIssue(
                        "MCE_UNEXPECTED_UNQUALIFIED_ATTRIBUTE",
                        WordMceIssueSeverity.Error,
                        "An MCE branch contains an unqualified attribute that is not allowed.",
                        partId,
                        part.Uri,
                        source.Ordinal
                    ));
                }
                continue;
            }
            if (
                attribute.Name.NamespaceName != MarkupCompatibilityNamespace
                && !context.IgnorableNamespaces.Contains(attribute.Name.NamespaceName)
            )
            {
                issues.Add(new WordMceIssue(
                    "MCE_BRANCH_ATTRIBUTE_NAMESPACE_NOT_IGNORABLE",
                    WordMceIssueSeverity.Error,
                    "A qualified MCE branch attribute is neither an MCE attribute nor declared ignorable.",
                    partId,
                    part.Uri,
                    source.Ordinal
                ));
            }
        }
    }

    private static void ValidateAlternateContent(
        OpcPart part,
        string partId,
        XmlSourceElement source,
        XElement parsed,
        ElementContext context,
        IssueState issues
    )
    {
        foreach (var attribute in parsed.Attributes().Where(attribute =>
            !attribute.IsNamespaceDeclaration
        ))
        {
            if (attribute.Name.NamespaceName.Length == 0)
            {
                issues.Add(new WordMceIssue(
                    "MCE_ALTERNATE_CONTENT_UNQUALIFIED_ATTRIBUTE",
                    WordMceIssueSeverity.Error,
                    "AlternateContent cannot contain unqualified attributes.",
                    partId,
                    part.Uri,
                    source.Ordinal
                ));
            }
            else if (
                attribute.Name.NamespaceName != MarkupCompatibilityNamespace
                && !context.IgnorableNamespaces.Contains(attribute.Name.NamespaceName)
            )
            {
                issues.Add(new WordMceIssue(
                    "MCE_ALTERNATE_CONTENT_ATTRIBUTE_NAMESPACE_NOT_IGNORABLE",
                    WordMceIssueSeverity.Error,
                    "A qualified AlternateContent attribute is neither an MCE attribute nor declared ignorable.",
                    partId,
                    part.Uri,
                    source.Ordinal
                ));
            }
        }

        var choiceCount = 0;
        var fallbackCount = 0;
        var fallbackSeen = false;
        foreach (var child in parsed.Elements())
        {
            if (child.Name.NamespaceName == MarkupCompatibilityNamespace)
            {
                if (child.Name.LocalName == "Choice")
                {
                    choiceCount++;
                    if (fallbackSeen)
                    {
                        issues.Add(new WordMceIssue(
                            "MCE_CHOICE_AFTER_FALLBACK",
                            WordMceIssueSeverity.Error,
                            "A Choice element appears after Fallback.",
                            partId,
                            part.Uri,
                            source.Ordinal
                        ));
                    }
                }
                else if (child.Name.LocalName == "Fallback")
                {
                    fallbackSeen = true;
                    fallbackCount++;
                }
                else
                {
                    issues.Add(new WordMceIssue(
                        "MCE_ALTERNATE_CONTENT_CHILD_INVALID",
                        WordMceIssueSeverity.Error,
                        "AlternateContent contains an unsupported child in the MCE namespace.",
                        partId,
                        part.Uri,
                        source.Ordinal
                    ));
                }
            }
            else if (!context.IgnorableNamespaces.Contains(child.Name.NamespaceName))
            {
                issues.Add(new WordMceIssue(
                    "MCE_ALTERNATE_CONTENT_EXTENSION_CHILD_NOT_IGNORABLE",
                    WordMceIssueSeverity.Error,
                    "A non-MCE child of AlternateContent is not declared ignorable.",
                    partId,
                    part.Uri,
                    source.Ordinal
                ));
            }
        }
        if (choiceCount == 0)
        {
            issues.Add(new WordMceIssue(
                "MCE_ALTERNATE_CONTENT_CHOICE_MISSING",
                WordMceIssueSeverity.Error,
                "AlternateContent must contain at least one Choice child.",
                partId,
                part.Uri,
                source.Ordinal
            ));
        }
        if (fallbackCount > 1)
        {
            issues.Add(new WordMceIssue(
                "MCE_ALTERNATE_CONTENT_MULTIPLE_FALLBACKS",
                WordMceIssueSeverity.Error,
                "AlternateContent cannot contain more than one Fallback child.",
                partId,
                part.Uri,
                source.Ordinal
            ));
        }
    }

    private PrefixRuleParse ParsePrefixRule(
        OpcPart part,
        string partId,
        XmlSourceElement source,
        XElement parsed,
        WordMceRuleKind kind,
        string value,
        bool allowEmpty,
        IssueState issues
    )
    {
        var tokens = SplitTokens(value);
        var namespaceUris = new List<string>();
        var invalid = 0;
        if (!allowEmpty && tokens.Length == 0)
        {
            invalid++;
            AddTokenIssue(part, partId, source, kind, "MCE_RULE_VALUE_EMPTY", issues);
        }
        foreach (var token in tokens)
        {
            if (!IsNcName(token))
            {
                invalid++;
                AddTokenIssue(part, partId, source, kind, "MCE_PREFIX_TOKEN_INVALID", issues);
                continue;
            }
            var namespaceUri = parsed.GetNamespaceOfPrefix(token)?.NamespaceName;
            if (string.IsNullOrEmpty(namespaceUri))
            {
                invalid++;
                AddTokenIssue(part, partId, source, kind, "MCE_PREFIX_UNBOUND", issues);
                continue;
            }
            if (namespaceUri == MarkupCompatibilityNamespace)
            {
                invalid++;
                AddTokenIssue(part, partId, source, kind, "MCE_PREFIX_BINDS_MCE_NAMESPACE", issues);
                continue;
            }
            namespaceUris.Add(namespaceUri);
        }
        var id = StableId(
            "wmcr_",
            part.Uri,
            source.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            kind.ToString()
        );
        var names = namespaceUris.Distinct(StringComparer.Ordinal)
            .Select(namespaceUri => new WordMceNameReference(
                NamespaceId(namespaceUri),
                namespaceUri,
                null,
                false
            ))
            .ToArray();
        return new PrefixRuleParse(
            new WordMceRuleDefinition(
                id,
                partId,
                part.Uri,
                kind,
                source.Ordinal,
                tokens.Length,
                invalid,
                names
            ),
            namespaceUris.Distinct(StringComparer.Ordinal).ToArray()
        );
    }

    private QualifiedNameRuleParse ParseQualifiedNameRule(
        OpcPart part,
        string partId,
        XmlSourceElement source,
        XElement parsed,
        WordMceRuleKind kind,
        string value,
        bool allowEmpty,
        IReadOnlySet<string> effectiveIgnorableNamespaces,
        IssueState issues
    )
    {
        var tokens = SplitTokens(value);
        var names = new List<ExpandedNamePattern>();
        var invalid = 0;
        if (!allowEmpty && tokens.Length == 0)
        {
            invalid++;
            AddTokenIssue(part, partId, source, kind, "MCE_RULE_VALUE_EMPTY", issues);
        }
        foreach (var token in tokens)
        {
            var separator = token.IndexOf(':');
            if (
                separator <= 0
                || separator == token.Length - 1
                || token.IndexOf(':', separator + 1) >= 0
            )
            {
                invalid++;
                AddTokenIssue(part, partId, source, kind, "MCE_QUALIFIED_NAME_TOKEN_INVALID", issues);
                continue;
            }
            var prefix = token[..separator];
            var localName = token[(separator + 1)..];
            if (!IsNcName(prefix) || (localName != "*" && !IsNcName(localName)))
            {
                invalid++;
                AddTokenIssue(part, partId, source, kind, "MCE_QUALIFIED_NAME_TOKEN_INVALID", issues);
                continue;
            }
            var namespaceUri = parsed.GetNamespaceOfPrefix(prefix)?.NamespaceName;
            if (string.IsNullOrEmpty(namespaceUri))
            {
                invalid++;
                AddTokenIssue(part, partId, source, kind, "MCE_PREFIX_UNBOUND", issues);
                continue;
            }
            if (namespaceUri == MarkupCompatibilityNamespace)
            {
                invalid++;
                AddTokenIssue(part, partId, source, kind, "MCE_PREFIX_BINDS_MCE_NAMESPACE", issues);
                continue;
            }
            if (!effectiveIgnorableNamespaces.Contains(namespaceUri))
            {
                invalid++;
                AddTokenIssue(part, partId, source, kind, "MCE_RULE_NAMESPACE_NOT_IGNORABLE", issues);
                continue;
            }
            names.Add(new ExpandedNamePattern(namespaceUri, localName));
        }
        var id = StableId(
            "wmcr_",
            part.Uri,
            source.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            kind.ToString()
        );
        var distinctNames = names.Distinct().ToArray();
        return new QualifiedNameRuleParse(
            new WordMceRuleDefinition(
                id,
                partId,
                part.Uri,
                kind,
                source.Ordinal,
                tokens.Length,
                invalid,
                distinctNames.Select(name => new WordMceNameReference(
                    NamespaceId(name.NamespaceUri),
                    name.NamespaceUri,
                    name.LocalName == "*" ? null : name.LocalName,
                    name.LocalName == "*"
                )).ToArray()
            ),
            distinctNames
        );
    }

    private static RequiresParse ParseRequires(
        OpcPart part,
        string partId,
        XmlSourceElement source,
        XElement parsed,
        IssueState issues
    )
    {
        var requiresAttributes = parsed.Attributes().Where(attribute =>
            !attribute.IsNamespaceDeclaration
            && attribute.Name.NamespaceName.Length == 0
            && attribute.Name.LocalName == "Requires"
        ).ToArray();
        if (requiresAttributes.Length != 1)
        {
            issues.Add(new WordMceIssue(
                "MCE_CHOICE_REQUIRES_MISSING",
                WordMceIssueSeverity.Error,
                "Choice must contain exactly one unqualified Requires attribute.",
                partId,
                part.Uri,
                source.Ordinal
            ));
            return new RequiresParse(Array.Empty<string>(), false);
        }
        var tokens = SplitTokens(requiresAttributes[0].Value);
        if (tokens.Length == 0)
        {
            issues.Add(new WordMceIssue(
                "MCE_CHOICE_REQUIRES_EMPTY",
                WordMceIssueSeverity.Error,
                "Choice Requires must contain at least one namespace prefix.",
                partId,
                part.Uri,
                source.Ordinal
            ));
            return new RequiresParse(Array.Empty<string>(), false);
        }
        var namespaceUris = new List<string>();
        var valid = true;
        foreach (var token in tokens)
        {
            if (!IsNcName(token))
            {
                valid = false;
                continue;
            }
            var namespaceUri = parsed.GetNamespaceOfPrefix(token)?.NamespaceName;
            if (
                string.IsNullOrEmpty(namespaceUri)
                || namespaceUri == MarkupCompatibilityNamespace
            )
            {
                valid = false;
                continue;
            }
            namespaceUris.Add(namespaceUri);
        }
        if (!valid)
        {
            issues.Add(new WordMceIssue(
                "MCE_CHOICE_REQUIRES_INVALID",
                WordMceIssueSeverity.Error,
                "Choice Requires contains an invalid, unbound, or MCE-bound namespace prefix.",
                partId,
                part.Uri,
                source.Ordinal
            ));
        }
        return new RequiresParse(
            namespaceUris.Distinct(StringComparer.Ordinal).ToArray(),
            valid
        );
    }

    private static bool ChildIsVisible(
        XmlSourceElement child,
        XmlSourceElement parent,
        bool parentVisible,
        WordMceElementDisposition? parentDisposition,
        IReadOnlyDictionary<int, AlternateStructure> alternates
    )
    {
        if (!parentVisible || parentDisposition == WordMceElementDisposition.Ignored)
        {
            return false;
        }
        if (alternates.TryGetValue(parent.Ordinal, out var alternate))
        {
            return alternate.BranchesByOrdinal.TryGetValue(
                child.Ordinal,
                out var branch
            ) && branch.Selected;
        }
        return true;
    }

    private HashSet<string> ValidateConfigurationNamespaces(
        IReadOnlyCollection<string> namespaces
    )
    {
        ArgumentNullException.ThrowIfNull(namespaces);
        if (namespaces.Count > _options.MaxConfigurationNamespaces)
        {
            throw new WordMceLimitException(
                $"Application configuration contains more than {_options.MaxConfigurationNamespaces} understood namespaces."
            );
        }
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var namespaceUri in namespaces)
        {
            if (string.IsNullOrWhiteSpace(namespaceUri))
            {
                throw new ArgumentException(
                    "Understood namespace URIs cannot be empty.",
                    nameof(namespaces)
                );
            }
            result.Add(namespaceUri);
        }
        return result;
    }

    private HashSet<ExpandedNameKey> ValidateExtensionElements(
        IReadOnlyCollection<WordMceExpandedName> elements
    )
    {
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count > _options.MaxApplicationDefinedExtensionElements)
        {
            throw new WordMceLimitException(
                $"Markup configuration contains more than {_options.MaxApplicationDefinedExtensionElements} application-defined extension elements."
            );
        }
        var result = new HashSet<ExpandedNameKey>();
        foreach (var element in elements)
        {
            ArgumentNullException.ThrowIfNull(element);
            if (string.IsNullOrWhiteSpace(element.NamespaceUri))
            {
                throw new ArgumentException(
                    "Application-defined extension namespace URIs cannot be empty.",
                    nameof(elements)
                );
            }
            if (!IsNcName(element.LocalName))
            {
                throw new ArgumentException(
                    "Application-defined extension local names must be XML NCNames.",
                    nameof(elements)
                );
            }
            result.Add(new ExpandedNameKey(element.NamespaceUri, element.LocalName));
        }
        return result;
    }

    private static string ConfigurationFingerprint(
        IEnumerable<string> understoodNamespaces,
        IEnumerable<ExpandedNameKey> extensionElements
    )
    {
        var values = understoodNamespaces.Order(StringComparer.Ordinal)
            .Select(value => "n:" + value)
            .Concat(extensionElements.OrderBy(value => value.NamespaceUri, StringComparer.Ordinal)
                .ThenBy(value => value.LocalName, StringComparer.Ordinal)
                .Select(value => "e:" + value.NamespaceUri + "\u001f" + value.LocalName));
        return StableId("wmcc_", values.ToArray());
    }

    private static bool IsXmlPart(OpcPart part)
    {
        var contentType = part.ContentType;
        return contentType is not null
            && (
                contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
            );
    }

    private void AddRule(List<WordMceRuleDefinition> target, WordMceRuleDefinition rule)
    {
        if (target.Count >= _options.MaxRules)
        {
            throw new WordMceLimitException(
                $"MCE rule count exceeds {_options.MaxRules}."
            );
        }
        target.Add(rule);
    }

    private void AddAlternateContent(
        List<WordMceAlternateContentDefinition> target,
        WordMceAlternateContentDefinition value
    )
    {
        if (target.Count >= _options.MaxAlternateContent)
        {
            throw new WordMceLimitException(
                $"AlternateContent count exceeds {_options.MaxAlternateContent}."
            );
        }
        target.Add(value);
    }

    private void AddAffectedElement(
        List<WordMceAffectedElement> target,
        WordMceAffectedElement value
    )
    {
        if (target.Count >= _options.MaxAffectedElements)
        {
            throw new WordMceLimitException(
                $"MCE-affected element count exceeds {_options.MaxAffectedElements}."
            );
        }
        target.Add(value);
    }

    private void AddMismatch(
        List<WordMceMustUnderstandMismatch> target,
        WordMceMustUnderstandMismatch value
    )
    {
        if (target.Count >= _options.MaxMustUnderstandMismatches)
        {
            throw new WordMceLimitException(
                $"MustUnderstand mismatch count exceeds {_options.MaxMustUnderstandMismatches}."
            );
        }
        target.Add(value);
    }

    private void EnsureNamespaceLimit(int count)
    {
        if (count > _options.MaxNamespaces)
        {
            throw new WordMceLimitException(
                $"Distinct namespace count exceeds {_options.MaxNamespaces}."
            );
        }
    }

    private static void CountNamespaceOccurrences(
        XElement element,
        IDictionary<string, NamespaceStats> stats
    )
    {
        if (element.Name.NamespaceName.Length != 0)
        {
            NamespaceStat(stats, element.Name.NamespaceName).ElementOccurrenceCount++;
        }
        foreach (var attribute in element.Attributes())
        {
            if (
                attribute.IsNamespaceDeclaration
                || attribute.Name.NamespaceName.Length == 0
                || attribute.Name.NamespaceName == XmlnsNamespace
            )
            {
                continue;
            }
            NamespaceStat(stats, attribute.Name.NamespaceName).AttributeOccurrenceCount++;
        }
    }

    private static NamespaceStats NamespaceStat(
        IDictionary<string, NamespaceStats> stats,
        string namespaceUri
    )
    {
        if (!stats.TryGetValue(namespaceUri, out var result))
        {
            result = new NamespaceStats();
            stats.Add(namespaceUri, result);
        }
        return result;
    }

    private static void AddTokenIssue(
        OpcPart part,
        string partId,
        XmlSourceElement source,
        WordMceRuleKind kind,
        string code,
        IssueState issues
    )
    {
        issues.Add(new WordMceIssue(
            code,
            WordMceIssueSeverity.Error,
            "An MCE rule contains a token that cannot be resolved under the in-scope namespace declarations and compatibility rules.",
            partId,
            part.Uri,
            source.Ordinal,
            StableId(
                "wmcr_",
                part.Uri,
                source.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                kind.ToString()
            )
        ));
    }

    private static string[] SplitTokens(string value) => value.Split(
        [' ', '\t', '\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries
    );

    private static bool IsNcName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        try
        {
            XmlConvert.VerifyNCName(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string NamespaceId(string namespaceUri) => StableId(
        "wmcn_",
        namespaceUri
    );

    private static string StableId(string prefix, params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return prefix + Convert.ToHexString(hash.GetHashAndReset().AsSpan(0, 16))
            .ToLowerInvariant();
    }

    private sealed class ElementContext
    {
        public static ElementContext Empty { get; } = new(
            new HashSet<string>(StringComparer.Ordinal),
            Array.Empty<ExpandedNamePattern>(),
            false
        );

        private ElementContext(
            HashSet<string> ignorableNamespaces,
            IReadOnlyList<ExpandedNamePattern> processContentNames,
            bool insideApplicationDefinedExtension
        )
        {
            IgnorableNamespaces = ignorableNamespaces;
            ProcessContentNames = processContentNames;
            InsideApplicationDefinedExtension = insideApplicationDefinedExtension;
        }

        public IReadOnlySet<string> IgnorableNamespaces { get; }

        public IReadOnlyList<ExpandedNamePattern> ProcessContentNames { get; }

        public bool InsideApplicationDefinedExtension { get; }

        public ElementContext WithIgnorableNamespaces(
            IReadOnlyList<string> namespaceUris
        )
        {
            if (
                namespaceUris.Count == 0
                || namespaceUris.All(IgnorableNamespaces.Contains)
            )
            {
                return this;
            }
            var merged = new HashSet<string>(IgnorableNamespaces, StringComparer.Ordinal);
            merged.UnionWith(namespaceUris);
            return new ElementContext(
                merged,
                ProcessContentNames,
                InsideApplicationDefinedExtension
            );
        }

        public ElementContext WithProcessContentNames(
            IReadOnlyList<ExpandedNamePattern> names
        )
        {
            if (names.Count == 0 || names.All(ProcessContentNames.Contains))
            {
                return this;
            }
            var merged = ProcessContentNames.Concat(names).Distinct().ToArray();
            return new ElementContext(
                new HashSet<string>(IgnorableNamespaces, StringComparer.Ordinal),
                merged,
                InsideApplicationDefinedExtension
            );
        }

        public ElementContext EnterApplicationDefinedExtension() =>
            InsideApplicationDefinedExtension
                ? this
                : new ElementContext(
                    new HashSet<string>(IgnorableNamespaces, StringComparer.Ordinal),
                    ProcessContentNames,
                    true
                );
    }

    private sealed class NamespaceStats
    {
        public int ElementOccurrenceCount { get; set; }

        public int AttributeOccurrenceCount { get; set; }

        public int IgnorableDeclarationCount { get; set; }

        public int ProcessContentReferenceCount { get; set; }

        public int MustUnderstandReferenceCount { get; set; }

        public int ChoiceRequirementCount { get; set; }

        public WordMceNamespaceDefinition ToDefinition(
            string namespaceUri,
            bool understood
        ) => new(
            NamespaceId(namespaceUri),
            namespaceUri,
            ElementOccurrenceCount,
            AttributeOccurrenceCount,
            IgnorableDeclarationCount,
            ProcessContentReferenceCount,
            MustUnderstandReferenceCount,
            ChoiceRequirementCount,
            understood
        );
    }

    private sealed class IssueState
    {
        private readonly int _maximum;
        private readonly List<WordMceIssue> _items = [];

        public IssueState(int maximum)
        {
            _maximum = maximum;
        }

        public int Count => _items.Count;

        public IReadOnlyList<WordMceIssue> Items => _items;

        public bool Truncated { get; private set; }

        public void Add(WordMceIssue issue)
        {
            if (_items.Count < _maximum)
            {
                _items.Add(issue);
            }
            else
            {
                Truncated = true;
            }
        }
    }

    private readonly record struct ExpandedNameKey(string NamespaceUri, string LocalName);

    private readonly record struct ExpandedNamePattern(string NamespaceUri, string LocalName)
    {
        public bool Matches(string namespaceUri, string localName) =>
            string.Equals(NamespaceUri, namespaceUri, StringComparison.Ordinal)
            && (
                LocalName == "*"
                || string.Equals(LocalName, localName, StringComparison.Ordinal)
            );
    }

    private sealed record PrefixRuleParse(
        WordMceRuleDefinition Rule,
        IReadOnlyList<string> NamespaceUris
    );

    private sealed record QualifiedNameRuleParse(
        WordMceRuleDefinition Rule,
        IReadOnlyList<ExpandedNamePattern> Names
    );

    private sealed record RequiresParse(
        IReadOnlyList<string> NamespaceUris,
        bool Valid
    );

    private sealed record AlternateStructure(
        WordMceAlternateContentDefinition Definition,
        IReadOnlyDictionary<int, WordMceBranchDefinition> BranchesByOrdinal
    );
}

public class WordMceException : InvalidOperationException
{
    public WordMceException(string message)
        : base(message)
    {
    }

    public WordMceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordMceLimitException : WordMceException
{
    public WordMceLimitException(string message)
        : base(message)
    {
    }

    public WordMceLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordMceProjectionException : WordMceException
{
    public WordMceProjectionException(string message)
        : base(message)
    {
    }
}
