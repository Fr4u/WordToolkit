using System.Collections.ObjectModel;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

public enum WordHeaderFooterKind
{
    Header,
    Footer,
}

public enum WordHeaderFooterVariant
{
    Default,
    First,
    Even,
}

public enum WordHeaderFooterBindingOrigin
{
    Explicit,
    Inherited,
    Blank,
}

public sealed record WordHeaderFooterBinding(
    WordHeaderFooterKind Kind,
    WordHeaderFooterVariant Variant,
    bool IsVariantEnabled,
    WordHeaderFooterBindingOrigin Origin,
    int? DefinitionSectionOrdinal,
    string? RelationshipId,
    string? PartUri,
    WordHeaderFooterVariant? DisplayFallbackVariant,
    string? EffectiveDisplayPartUri
);

public sealed class WordSectionDescriptor
{
    internal WordSectionDescriptor(
        int ordinal,
        SemanticNodeId? nodeId,
        bool isImplicit,
        SemanticNodeId? startsAfterParagraphId,
        SemanticNodeId? endsAtParagraphId,
        string breakType,
        bool titlePage,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<WordHeaderFooterBinding> bindings
    )
    {
        Ordinal = ordinal;
        NodeId = nodeId;
        IsImplicit = isImplicit;
        StartsAfterParagraphId = startsAfterParagraphId;
        EndsAtParagraphId = endsAtParagraphId;
        BreakType = breakType;
        TitlePage = titlePage;
        Properties = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(properties, StringComparer.Ordinal)
        );
        Bindings = new ReadOnlyCollection<WordHeaderFooterBinding>(
            bindings.ToArray()
        );
    }

    public int Ordinal { get; }

    public SemanticNodeId? NodeId { get; }

    public bool IsImplicit { get; }

    public SemanticNodeId? StartsAfterParagraphId { get; }

    public SemanticNodeId? EndsAtParagraphId { get; }

    public string BreakType { get; }

    public bool TitlePage { get; }

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<WordHeaderFooterBinding> Bindings { get; }

    public WordHeaderFooterBinding Binding(
        WordHeaderFooterKind kind,
        WordHeaderFooterVariant variant
    ) => Bindings.Single(binding =>
        binding.Kind == kind && binding.Variant == variant
    );
}

public sealed class WordSectionGraph
{
    internal WordSectionGraph(
        string packageFingerprint,
        string mainPartUri,
        bool evenAndOddHeaders,
        IReadOnlyList<WordSectionDescriptor> sections,
        IReadOnlyList<string> referencedStoryPartUris,
        IReadOnlyList<string> unboundStoryPartUris
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        EvenAndOddHeaders = evenAndOddHeaders;
        Sections = new ReadOnlyCollection<WordSectionDescriptor>(sections.ToArray());
        ReferencedStoryPartUris = new ReadOnlyCollection<string>(
            referencedStoryPartUris.ToArray()
        );
        UnboundStoryPartUris = new ReadOnlyCollection<string>(
            unboundStoryPartUris.ToArray()
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public bool EvenAndOddHeaders { get; }

    public IReadOnlyList<WordSectionDescriptor> Sections { get; }

    public IReadOnlyList<string> ReferencedStoryPartUris { get; }

    public IReadOnlyList<string> UnboundStoryPartUris { get; }
}

public sealed record WordSectionGraphOptions
{
    public static WordSectionGraphOptions Default { get; } = new();

    public int MaxSections { get; init; } = 4_096;

    public int MaxHeaderFooterReferences { get; init; } = 24_576;

    public int MaxSettingsBytes { get; init; } = 16 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxSections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSections));
        }

        if (MaxHeaderFooterReferences <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHeaderFooterReferences));
        }

        if (MaxSettingsBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSettingsBytes));
        }
    }
}

public sealed class WordSectionGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string RelationshipsTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelationshipsStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string HeaderContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
    private const string FooterContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";

    private readonly WordSectionGraphOptions _options;

    public WordSectionGraphBuilder(WordSectionGraphOptions? options = null)
    {
        _options = options ?? WordSectionGraphOptions.Default;
        _options.Validate();
    }

    public WordSectionGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSectionProjectionException(
                "Section graph requires a semantic projection of the same package snapshot."
            );
        }

        var evenAndOddHeaders = ReadEvenAndOddHeaders(
            package,
            semanticDocument,
            cancellationToken
        );
        var relationships = IndexMainRelationships(
            package,
            semanticDocument.MainPartUri
        );
        var sectionNodes = semanticDocument.Nodes
            .Where(node =>
                node.Kind == WordSemanticNodeKind.Section
                && node.SourcePartUri == semanticDocument.MainPartUri
            )
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        if (sectionNodes.Length > _options.MaxSections)
        {
            throw new WordSectionLimitException(
                $"Document contains {sectionNodes.Length} explicit sections; "
                    + $"limit is {_options.MaxSections}."
            );
        }

        var inputs = CreateSectionInputs(semanticDocument, sectionNodes);
        if (inputs.Count > _options.MaxSections)
        {
            throw new WordSectionLimitException(
                $"Document resolves to {inputs.Count} sections; limit is "
                    + $"{_options.MaxSections}."
            );
        }

        var previousDefinitions = new Dictionary<BindingKey, StoryDefinition>();
        var sections = new List<WordSectionDescriptor>(inputs.Count);
        var referenceCount = 0;
        for (var index = 0; index < inputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs[index];
            var explicitReferences = ResolveExplicitReferences(
                package,
                semanticDocument,
                relationships,
                input.Node,
                index + 1
            );
            checked
            {
                referenceCount += explicitReferences.Count;
            }

            if (referenceCount > _options.MaxHeaderFooterReferences)
            {
                throw new WordSectionLimitException(
                    "Document exceeds the configured header/footer reference limit."
                );
            }

            var definitions = new Dictionary<BindingKey, StoryDefinition>();
            foreach (var key in BindingKeys)
            {
                if (explicitReferences.TryGetValue(key, out var explicitReference))
                {
                    definitions[key] = new StoryDefinition(
                        index + 1,
                        explicitReference.RelationshipId,
                        explicitReference.PartUri
                    );
                }
                else if (previousDefinitions.TryGetValue(key, out var inherited))
                {
                    definitions[key] = inherited;
                }
            }

            var titlePage = ParseRequiredBooleanProperty(
                input.Properties,
                "title_page",
                defaultValue: false,
                index + 1
            );
            var bindings = CreateBindings(
                titlePage,
                evenAndOddHeaders,
                explicitReferences,
                definitions
            );
            sections.Add(
                new WordSectionDescriptor(
                    index + 1,
                    input.Node?.Id,
                    input.Node is null,
                    input.StartsAfterParagraphId,
                    input.EndsAtParagraphId,
                    input.Properties.TryGetValue("break_type", out var breakType)
                        ? breakType
                        : "nextPage",
                    titlePage,
                    input.Properties,
                    bindings
                )
            );
            previousDefinitions = definitions;
        }

        var referencedParts = sections
            .SelectMany(section => section.Bindings)
            .Select(binding => binding.PartUri)
            .Where(uri => uri is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var referencedSet = referencedParts.ToHashSet(StringComparer.Ordinal);
        var unboundParts = semanticDocument.Nodes
            .Where(node => node.Kind is WordSemanticNodeKind.Header
                or WordSemanticNodeKind.Footer)
            .Select(node => node.SourcePartUri)
            .Distinct(StringComparer.Ordinal)
            .Where(uri => !referencedSet.Contains(uri))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new WordSectionGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            evenAndOddHeaders,
            sections,
            referencedParts,
            unboundParts
        );
    }

    private IReadOnlyList<SectionInput> CreateSectionInputs(
        WordSemanticDocument semanticDocument,
        IReadOnlyList<WordSemanticNode> sectionNodes
    )
    {
        if (sectionNodes.Count == 0)
        {
            return
            [
                new SectionInput(
                    null,
                    null,
                    null,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                ),
            ];
        }

        var inputs = new List<SectionInput>(sectionNodes.Count + 1);
        SemanticNodeId? previousBoundary = null;
        for (var index = 0; index < sectionNodes.Count; index++)
        {
            var sectionNode = sectionNodes[index];
            if (
                sectionNode.ParentId is not { } parentId
                || !semanticDocument.TryGetNode(parentId, out var parent)
                || parent is null
            )
            {
                throw new WordSectionProjectionException(
                    $"Section node '{sectionNode.Id}' has no semantic parent."
                );
            }

            SemanticNodeId? endBoundary = parent.Kind switch
            {
                WordSemanticNodeKind.Paragraph => parent.Id,
                WordSemanticNodeKind.Body => null,
                _ => throw new WordSectionProjectionException(
                    $"Section node '{sectionNode.Id}' is nested under {parent.Kind}."
                ),
            };
            if (
                parent.Kind == WordSemanticNodeKind.Body
                && index != sectionNodes.Count - 1
            )
            {
                throw new WordSectionProjectionException(
                    "A body-level final section-properties node is followed by "
                        + "another section-properties node."
                );
            }
            if (parent.Kind == WordSemanticNodeKind.Body)
            {
                var sectionSubtreeEnd = sectionNode.DescendantsAndSelf()
                    .Max(node => node.SourceOrder);
                if (semanticDocument.Nodes.Any(node =>
                    node.SourcePartUri == semanticDocument.MainPartUri
                    && node.SourceOrder > sectionSubtreeEnd
                ))
                {
                    throw new WordSectionProjectionException(
                        "The body-level final section-properties node is followed "
                            + "by semantic document content."
                    );
                }
            }
            inputs.Add(
                new SectionInput(
                    sectionNode,
                    previousBoundary,
                    endBoundary,
                    sectionNode.Properties
                )
            );
            previousBoundary = endBoundary;
        }

        var lastNode = sectionNodes[^1];
        if (
            lastNode.ParentId is { } lastParentId
            && semanticDocument.TryGetNode(lastParentId, out var lastParent)
            && lastParent?.Kind == WordSemanticNodeKind.Paragraph
        )
        {
            inputs.Add(
                new SectionInput(
                    null,
                    previousBoundary,
                    null,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                )
            );
        }

        return inputs;
    }

    private Dictionary<string, OpcRelationship> IndexMainRelationships(
        OpcPackageSnapshot package,
        string mainPartUri
    )
    {
        var result = new Dictionary<string, OpcRelationship>(StringComparer.Ordinal);
        foreach (var relationship in package.RelationshipsFrom(mainPartUri))
        {
            if (!result.TryAdd(relationship.Id, relationship))
            {
                throw new WordSectionProjectionException(
                    $"Main part contains duplicate relationship ID '{relationship.Id}'."
                );
            }
        }

        return result;
    }

    private Dictionary<BindingKey, ExplicitStoryReference> ResolveExplicitReferences(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyDictionary<string, OpcRelationship> relationships,
        WordSemanticNode? sectionNode,
        int sectionOrdinal
    )
    {
        var result = new Dictionary<BindingKey, ExplicitStoryReference>();
        if (sectionNode is null)
        {
            return result;
        }

        foreach (
            var reference in sectionNode.Children.Where(child => child.Kind is
                WordSemanticNodeKind.HeaderReference
                or WordSemanticNodeKind.FooterReference)
        )
        {
            var kind = reference.Kind == WordSemanticNodeKind.HeaderReference
                ? WordHeaderFooterKind.Header
                : WordHeaderFooterKind.Footer;
            if (
                !reference.Properties.TryGetValue("type", out var rawVariant)
                || !TryParseVariant(rawVariant, out var variant)
            )
            {
                throw new WordSectionProjectionException(
                    $"Section {sectionOrdinal} contains a {kind} reference with "
                        + "a missing or unknown type."
                );
            }

            if (
                !reference.Properties.TryGetValue("relationship_id", out var relationshipId)
                || !relationships.TryGetValue(relationshipId, out var relationship)
            )
            {
                throw new WordSectionProjectionException(
                    $"Section {sectionOrdinal} {kind}/{variant} reference does not "
                        + "resolve to a main-part relationship."
                );
            }

            ValidateStoryRelationship(
                package,
                semanticDocument,
                relationship,
                kind,
                sectionOrdinal,
                variant
            );
            var key = new BindingKey(kind, variant);
            if (
                !result.TryAdd(
                    key,
                    new ExplicitStoryReference(
                        relationshipId,
                        relationship.ResolvedTargetPartUri!
                    )
                )
            )
            {
                throw new WordSectionProjectionException(
                    $"Section {sectionOrdinal} defines {kind}/{variant} more than once."
                );
            }
        }

        return result;
    }

    private static void ValidateStoryRelationship(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        OpcRelationship relationship,
        WordHeaderFooterKind kind,
        int sectionOrdinal,
        WordHeaderFooterVariant variant
    )
    {
        var expectedTypeName = kind == WordHeaderFooterKind.Header
            ? "header"
            : "footer";
        if (
            !IsRelationshipType(relationship.Type, expectedTypeName)
            || relationship.TargetMode != OpcRelationshipTargetMode.Internal
            || relationship.ResolvedTargetPartUri is null
            || !package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var part)
        )
        {
            throw new WordSectionProjectionException(
                $"Section {sectionOrdinal} {kind}/{variant} relationship is not a "
                    + "valid internal Word story relationship."
            );
        }

        var expectedContentType = kind == WordHeaderFooterKind.Header
            ? HeaderContentType
            : FooterContentType;
        if (
            !string.Equals(
                part.ContentType,
                expectedContentType,
                StringComparison.OrdinalIgnoreCase
            )
            || !semanticDocument.ProjectedPartUris.Contains(
                part.Uri,
                StringComparer.Ordinal
            )
        )
        {
            throw new WordSectionProjectionException(
                $"Section {sectionOrdinal} {kind}/{variant} target '{part.Uri}' "
                    + "is not a projected Word story part."
            );
        }
    }

    private static IReadOnlyList<WordHeaderFooterBinding> CreateBindings(
        bool titlePage,
        bool evenAndOddHeaders,
        IReadOnlyDictionary<BindingKey, ExplicitStoryReference> explicitReferences,
        IReadOnlyDictionary<BindingKey, StoryDefinition> definitions
    )
    {
        var result = new List<WordHeaderFooterBinding>(BindingKeys.Length);
        foreach (var key in BindingKeys)
        {
            definitions.TryGetValue(key, out var definition);
            var enabled = key.Variant switch
            {
                WordHeaderFooterVariant.Default => true,
                WordHeaderFooterVariant.First => titlePage,
                WordHeaderFooterVariant.Even => evenAndOddHeaders,
                _ => throw new ArgumentOutOfRangeException(),
            };
            var origin = explicitReferences.ContainsKey(key)
                ? WordHeaderFooterBindingOrigin.Explicit
                : definition is not null
                    ? WordHeaderFooterBindingOrigin.Inherited
                    : WordHeaderFooterBindingOrigin.Blank;
            var defaultKey = new BindingKey(
                key.Kind,
                WordHeaderFooterVariant.Default
            );
            definitions.TryGetValue(defaultKey, out var defaultDefinition);
            result.Add(
                new WordHeaderFooterBinding(
                    key.Kind,
                    key.Variant,
                    enabled,
                    origin,
                    definition?.SectionOrdinal,
                    definition?.RelationshipId,
                    definition?.PartUri,
                    enabled ? null : WordHeaderFooterVariant.Default,
                    enabled ? definition?.PartUri : defaultDefinition?.PartUri
                )
            );
        }

        return result;
    }

    private bool ReadEvenAndOddHeaders(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return new WordSettingsGraphBuilder(
                new WordSettingsGraphOptions
                {
                    MaxSettingsPartBytes = _options.MaxSettingsBytes,
                }
            ).Build(
                package,
                semanticDocument,
                cancellationToken
            ).EvenAndOddHeaders;
        }
        catch (WordSettingsLimitException exception)
        {
            throw new WordSectionLimitException(
                "Word settings part exceeds a section-graph limit: " + exception.Message
            );
        }
        catch (WordSettingsProjectionException exception)
        {
            throw new WordSectionProjectionException(
                "Word settings part cannot be resolved for the section graph.",
                exception
            );
        }
    }

    private static bool ParseRequiredBooleanProperty(
        IReadOnlyDictionary<string, string> properties,
        string name,
        bool defaultValue,
        int sectionOrdinal
    )
    {
        if (!properties.TryGetValue(name, out var raw))
        {
            return defaultValue;
        }

        return raw switch
        {
            "true" => true,
            "false" => false,
            _ => throw new WordSectionProjectionException(
                $"Section {sectionOrdinal} has invalid Boolean property '{name}'."
            ),
        };
    }

    private static bool TryParseVariant(
        string value,
        out WordHeaderFooterVariant variant
    )
    {
        variant = value switch
        {
            "default" => WordHeaderFooterVariant.Default,
            "first" => WordHeaderFooterVariant.First,
            "even" => WordHeaderFooterVariant.Even,
            _ => default,
        };
        return value is "default" or "first" or "even";
    }

    private static bool IsRelationshipType(string value, string name) =>
        string.Equals(
            value,
            RelationshipsTransitionalNamespace + "/" + name,
            StringComparison.Ordinal
        )
        || string.Equals(
            value,
            RelationshipsStrictNamespace + "/" + name,
            StringComparison.Ordinal
        );

    private static bool IsWordNamespace(string value) =>
        value is WordTransitionalNamespace or WordStrictNamespace;

    private static readonly BindingKey[] BindingKeys =
    [
        new(WordHeaderFooterKind.Header, WordHeaderFooterVariant.Default),
        new(WordHeaderFooterKind.Header, WordHeaderFooterVariant.First),
        new(WordHeaderFooterKind.Header, WordHeaderFooterVariant.Even),
        new(WordHeaderFooterKind.Footer, WordHeaderFooterVariant.Default),
        new(WordHeaderFooterKind.Footer, WordHeaderFooterVariant.First),
        new(WordHeaderFooterKind.Footer, WordHeaderFooterVariant.Even),
    ];

    private sealed record SectionInput(
        WordSemanticNode? Node,
        SemanticNodeId? StartsAfterParagraphId,
        SemanticNodeId? EndsAtParagraphId,
        IReadOnlyDictionary<string, string> Properties
    );

    private readonly record struct BindingKey(
        WordHeaderFooterKind Kind,
        WordHeaderFooterVariant Variant
    );

    private sealed record ExplicitStoryReference(
        string RelationshipId,
        string PartUri
    );

    private sealed record StoryDefinition(
        int SectionOrdinal,
        string RelationshipId,
        string PartUri
    );
}

public class WordSectionProjectionException : IOException
{
    public WordSectionProjectionException(string message)
        : base(message)
    {
    }

    public WordSectionProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordSectionLimitException : WordSectionProjectionException
{
    public WordSectionLimitException(string message)
        : base(message)
    {
    }
}
