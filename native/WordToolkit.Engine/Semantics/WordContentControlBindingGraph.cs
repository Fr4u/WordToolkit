using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordContentControlIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordContentControlType
{
    RichText,
    PlainText,
    Picture,
    CheckBox,
    ComboBox,
    DropDownList,
    Date,
    BuildingBlockGallery,
    BuildingBlock,
    Equation,
    Group,
    Citation,
    Bibliography,
    RepeatingSection,
    RepeatingSectionItem,
    EntityPicker,
    Unknown,
}

public enum WordContentControlLevel
{
    Block,
    Run,
    TableRow,
    TableCell,
    Unknown,
}

public enum WordContentControlLock
{
    Unspecified,
    Unlocked,
    ControlLocked,
    ContentLocked,
    ControlAndContentLocked,
    Unknown,
}

public enum WordBindingStoreKind
{
    CustomXml,
    CoreProperties,
    ExtendedProperties,
}

public enum WordBindingResolutionStatus
{
    Resolved,
    StoreIdMissing,
    StoreIdInvalid,
    StoreMissing,
    StoreAmbiguous,
    StoreUnreadable,
    PrefixMappingsInvalid,
    XPathMissing,
    XPathInvalid,
    XPathUnsupported,
    TargetMissing,
}

public sealed record WordContentControlIssue(
    string Id,
    string Code,
    WordContentControlIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? ControlId = null,
    string? StoreId = null,
    string? BindingId = null
);

public sealed record WordCustomXmlStoreDefinition(
    string Id,
    WordBindingStoreKind Kind,
    string? ItemId,
    string PartUri,
    string? ContentType,
    string? PropertiesPartUri,
    string? RootNamespaceUri,
    string? RootLocalName,
    int XmlElementCount,
    IReadOnlyList<string> SchemaReferences,
    int IncomingRelationshipCount,
    bool PropertiesRelationshipResolved,
    bool Parsed
);

public sealed record WordContentControlDefinition(
    string Id,
    SemanticNodeId SemanticNodeId,
    string PartUri,
    int SourceElementOrdinal,
    string? ParentControlId,
    WordContentControlType Type,
    bool TypeExplicit,
    WordContentControlLevel Level,
    string? NativeId,
    string? Alias,
    string? Tag,
    WordContentControlLock Lock,
    string? PlaceholderBuildingBlock,
    bool ShowingPlaceholder,
    bool Temporary,
    bool DoNotAllowInsertDeleteSection,
    string? RepeatingSectionTitle,
    string? BindingId
);

public sealed record WordContentControlBindingTarget(
    string Id,
    string BindingId,
    string StoreId,
    int SourceElementOrdinal,
    string NamespaceUri,
    string LocalName
);

public sealed record WordContentControlBindingDefinition(
    string Id,
    string ControlId,
    string PartUri,
    int SourceElementOrdinal,
    bool IsOffice2013RichTextBinding,
    string? StoreItemId,
    string? StoreId,
    string XPath,
    string PrefixMappings,
    IReadOnlyDictionary<string, string> NamespaceMappings,
    WordBindingResolutionStatus Status,
    IReadOnlyList<string> TargetIds
);

public sealed record WordRepeatingSectionDefinition(
    string Id,
    string ControlId,
    string PartUri,
    int SourceElementOrdinal,
    IReadOnlyList<string> ItemControlIds,
    int BindingTargetCount,
    bool? CardinalityMatches,
    bool DoNotAllowInsertDeleteSection
);

public sealed class WordContentControlBindingGraph
{
    private readonly IReadOnlyDictionary<string, WordContentControlDefinition>
        _controlsById;
    private readonly IReadOnlyDictionary<string, WordCustomXmlStoreDefinition>
        _storesById;

    internal WordContentControlBindingGraph(
        string packageFingerprint,
        IReadOnlyList<WordCustomXmlStoreDefinition> stores,
        IReadOnlyList<WordContentControlDefinition> controls,
        IReadOnlyList<WordContentControlBindingDefinition> bindings,
        IReadOnlyList<WordContentControlBindingTarget> targets,
        IReadOnlyList<WordRepeatingSectionDefinition> repeatingSections,
        IReadOnlyList<WordContentControlIssue> issues,
        bool issuesTruncated,
        long parsedXmlBytes,
        int parsedXmlElements
    )
    {
        PackageFingerprint = packageFingerprint;
        Stores = new ReadOnlyCollection<WordCustomXmlStoreDefinition>(stores.ToArray());
        Controls = new ReadOnlyCollection<WordContentControlDefinition>(
            controls.ToArray()
        );
        Bindings = new ReadOnlyCollection<WordContentControlBindingDefinition>(
            bindings.ToArray()
        );
        Targets = new ReadOnlyCollection<WordContentControlBindingTarget>(
            targets.ToArray()
        );
        RepeatingSections = new ReadOnlyCollection<WordRepeatingSectionDefinition>(
            repeatingSections.ToArray()
        );
        Issues = new ReadOnlyCollection<WordContentControlIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        ParsedXmlBytes = parsedXmlBytes;
        ParsedXmlElements = parsedXmlElements;
        _controlsById = new ReadOnlyDictionary<string, WordContentControlDefinition>(
            controls.ToDictionary(control => control.Id, StringComparer.Ordinal)
        );
        _storesById = new ReadOnlyDictionary<string, WordCustomXmlStoreDefinition>(
            stores.ToDictionary(store => store.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordCustomXmlStoreDefinition> Stores { get; }

    public IReadOnlyList<WordContentControlDefinition> Controls { get; }

    public IReadOnlyList<WordContentControlBindingDefinition> Bindings { get; }

    public IReadOnlyList<WordContentControlBindingTarget> Targets { get; }

    public IReadOnlyList<WordRepeatingSectionDefinition> RepeatingSections { get; }

    public IReadOnlyList<WordContentControlIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public long ParsedXmlBytes { get; }

    public int ParsedXmlElements { get; }

    public bool TryGetControl(
        string controlId,
        out WordContentControlDefinition? control
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        return _controlsById.TryGetValue(controlId, out control);
    }

    public bool TryGetStore(string storeId, out WordCustomXmlStoreDefinition? store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeId);
        return _storesById.TryGetValue(storeId, out store);
    }
}

public sealed record WordContentControlBindingGraphOptions
{
    public static WordContentControlBindingGraphOptions Default { get; } = new();

    public int MaxStoryParts { get; init; } = 256;

    public int MaxStores { get; init; } = 2_048;

    public int MaxControls { get; init; } = 100_000;

    public int MaxBindings { get; init; } = 100_000;

    public int MaxTargets { get; init; } = 250_000;

    public int MaxTargetsPerBinding { get; init; } = 10_000;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxPartBytes { get; init; } = 64 * 1024 * 1024;

    public long MaxAggregateXmlBytes { get; init; } = 256L * 1024 * 1024;

    public int MaxElementsPerPart { get; init; } = 1_000_000;

    public int MaxAggregateElements { get; init; } = 2_000_000;

    public int MaxXPathCharacters { get; init; } = 4_096;

    public int MaxPrefixMappingsCharacters { get; init; } = 32_768;

    public int MaxNamespaceMappings { get; init; } = 128;

    public long MaxMetadataCharacters { get; init; } = 16L * 1024 * 1024;

    internal void Validate()
    {
        if (
            MaxStoryParts <= 0
            || MaxStores <= 0
            || MaxControls <= 0
            || MaxBindings <= 0
            || MaxTargets <= 0
            || MaxTargetsPerBinding <= 0
            || MaxIssues <= 0
            || MaxPartBytes <= 0
            || MaxAggregateXmlBytes <= 0
            || MaxElementsPerPart <= 0
            || MaxAggregateElements <= 0
            || MaxXPathCharacters <= 0
            || MaxPrefixMappingsCharacters <= 0
            || MaxNamespaceMappings <= 0
            || MaxMetadataCharacters <= 0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(WordContentControlBindingGraphOptions),
                "All content-control graph limits must be positive."
            );
        }
        if (MaxTargetsPerBinding > MaxTargets)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTargetsPerBinding),
                "The per-binding target limit cannot exceed the aggregate target limit."
            );
        }
        if (MaxPartBytes > MaxAggregateXmlBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPartBytes),
                "The per-part byte limit cannot exceed the aggregate byte limit."
            );
        }
        if (MaxElementsPerPart > MaxAggregateElements)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxElementsPerPart),
                "The per-part element limit cannot exceed the aggregate element limit."
            );
        }
    }
}

public sealed class WordContentControlBindingGraphBuilder
{
    private const string TransitionalWordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string Word2010Namespace =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string Word2013Namespace =
        "http://schemas.microsoft.com/office/word/2012/wordml";
    private const string CorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";
    private const string ExtendedPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.extended-properties+xml";
    private const string CustomXmlPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";
    private const string CorePropertiesItemId =
        "6c3c8bc8-f283-45ae-878a-bab7291924a1";
    private const string ExtendedPropertiesItemId =
        "6668398d-a668-4e3e-a5eb-62b293d839f1";

    private static readonly IReadOnlyDictionary<(string NamespaceUri, string LocalName), WordContentControlType>
        ExplicitTypes = new Dictionary<(string NamespaceUri, string LocalName), WordContentControlType>
        {
            [(TransitionalWordNamespace, "text")] = WordContentControlType.PlainText,
            [(StrictWordNamespace, "text")] = WordContentControlType.PlainText,
            [(TransitionalWordNamespace, "picture")] = WordContentControlType.Picture,
            [(StrictWordNamespace, "picture")] = WordContentControlType.Picture,
            [(TransitionalWordNamespace, "comboBox")] = WordContentControlType.ComboBox,
            [(StrictWordNamespace, "comboBox")] = WordContentControlType.ComboBox,
            [(TransitionalWordNamespace, "dropDownList")] = WordContentControlType.DropDownList,
            [(StrictWordNamespace, "dropDownList")] = WordContentControlType.DropDownList,
            [(TransitionalWordNamespace, "date")] = WordContentControlType.Date,
            [(StrictWordNamespace, "date")] = WordContentControlType.Date,
            [(TransitionalWordNamespace, "docPartList")] = WordContentControlType.BuildingBlockGallery,
            [(StrictWordNamespace, "docPartList")] = WordContentControlType.BuildingBlockGallery,
            [(TransitionalWordNamespace, "docPartObj")] = WordContentControlType.BuildingBlock,
            [(StrictWordNamespace, "docPartObj")] = WordContentControlType.BuildingBlock,
            [(TransitionalWordNamespace, "equation")] = WordContentControlType.Equation,
            [(StrictWordNamespace, "equation")] = WordContentControlType.Equation,
            [(TransitionalWordNamespace, "group")] = WordContentControlType.Group,
            [(StrictWordNamespace, "group")] = WordContentControlType.Group,
            [(TransitionalWordNamespace, "citation")] = WordContentControlType.Citation,
            [(StrictWordNamespace, "citation")] = WordContentControlType.Citation,
            [(TransitionalWordNamespace, "bibliography")] = WordContentControlType.Bibliography,
            [(StrictWordNamespace, "bibliography")] = WordContentControlType.Bibliography,
            [(TransitionalWordNamespace, "richText")] = WordContentControlType.RichText,
            [(StrictWordNamespace, "richText")] = WordContentControlType.RichText,
            [(Word2010Namespace, "checkbox")] = WordContentControlType.CheckBox,
            [(Word2010Namespace, "entityPicker")] = WordContentControlType.EntityPicker,
            [(Word2013Namespace, "repeatingSection")] = WordContentControlType.RepeatingSection,
            [(Word2013Namespace, "repeatingSectionItem")] = WordContentControlType.RepeatingSectionItem,
        };

    private readonly WordContentControlBindingGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordContentControlBindingGraphBuilder(
        WordContentControlBindingGraphOptions? options = null
    )
    {
        _options = options ?? WordContentControlBindingGraphOptions.Default;
        _options.Validate();
    }

    public WordContentControlBindingGraphBuilder(
        WordContentControlBindingGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordContentControlBindingGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordContentControlBindingGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        var semantic = _resourceLease is null
            ? new WordSemanticProjector().Project(package, cancellationToken)
            : new WordSemanticProjector(null, _resourceLease).Project(
                package,
                cancellationToken
            );
        return Build(package, semantic, cancellationToken);
    }

    public WordContentControlBindingGraph Build(
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
            WordOperationResourceStage.ContentControls
        );
        if (!string.Equals(
            package.Fingerprint,
            semanticDocument.PackageFingerprint,
            StringComparison.Ordinal
        ))
        {
            throw new WordContentControlProjectionException(
                "The semantic document does not belong to the supplied package snapshot."
            );
        }

        var state = new BuildState(_options, _resourceLease);
        DiscoverCustomXmlStores(package, state, cancellationToken);
        DiscoverBuiltInStore(
            package,
            WordBindingStoreKind.CoreProperties,
            CorePropertiesContentType,
            CorePropertiesItemId,
            state,
            cancellationToken
        );
        DiscoverBuiltInStore(
            package,
            WordBindingStoreKind.ExtendedProperties,
            ExtendedPropertiesContentType,
            ExtendedPropertiesItemId,
            state,
            cancellationToken
        );
        DiscoverControls(package, semanticDocument, state, cancellationToken);
        ResolveBindings(state, cancellationToken);
        BuildRepeatingSections(state, cancellationToken);
        ValidateControlIdentities(state, cancellationToken);

        return state.Freeze(package.Fingerprint);
    }

    private void DiscoverCustomXmlStores(
        OpcPackageSnapshot package,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var incoming = package.Relationships
            .Where(relationship => RelationshipKind(relationship.Type) == "customXml")
            .Where(relationship => relationship.TargetMode == OpcRelationshipTargetMode.Internal)
            .Where(relationship => relationship.ResolvedTargetPartUri is not null)
            .GroupBy(
                relationship => relationship.ResolvedTargetPartUri!,
                StringComparer.Ordinal
            )
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var candidateUris = new HashSet<string>(incoming.Keys, StringComparer.Ordinal);
        foreach (
            var relationship in package.Relationships.Where(relationship =>
                RelationshipKind(relationship.Type) == "customXmlProps"
            )
        )
        {
            candidateUris.Add(relationship.SourcePartUri);
        }

        foreach (var partUri in candidateUris.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.Stores.Count >= _options.MaxStores)
            {
                throw new WordContentControlLimitException(
                    $"Custom XML store count exceeds {_options.MaxStores}."
                );
            }
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                state.Issues.Add(
                    "CCB_CUSTOM_XML_PART_MISSING",
                    WordContentControlIssueSeverity.Error,
                    "A custom XML relationship target is missing from the package.",
                    partUri
                );
                continue;
            }

            var propertyRelationships = package.RelationshipsFrom(partUri)
                .Where(relationship => RelationshipKind(relationship.Type) == "customXmlProps")
                .ToArray();
            if (propertyRelationships.Length != 1)
            {
                state.Issues.Add(
                    "CCB_CUSTOM_XML_PROPERTIES_CARDINALITY",
                    WordContentControlIssueSeverity.Error,
                    "A custom XML store must have exactly one custom XML properties relationship.",
                    partUri
                );
            }

            OpcPart? propertiesPart = null;
            var propertiesResolved = false;
            if (propertyRelationships.Length == 1)
            {
                var relationship = propertyRelationships[0];
                if (
                    relationship.TargetMode == OpcRelationshipTargetMode.Internal
                    && relationship.ResolvedTargetPartUri is not null
                    && package.Parts.TryGetValue(
                        relationship.ResolvedTargetPartUri,
                        out propertiesPart
                    )
                )
                {
                    propertiesResolved = true;
                    if (!string.Equals(
                        propertiesPart.ContentType,
                        CustomXmlPropertiesContentType,
                        StringComparison.OrdinalIgnoreCase
                    ))
                    {
                        state.Issues.Add(
                            "CCB_CUSTOM_XML_PROPERTIES_CONTENT_TYPE",
                            WordContentControlIssueSeverity.Error,
                            "The custom XML properties part has an unexpected content type.",
                            propertiesPart.Uri
                        );
                    }
                }
                else
                {
                    state.Issues.Add(
                        "CCB_CUSTOM_XML_PROPERTIES_UNRESOLVED",
                        WordContentControlIssueSeverity.Error,
                        "The custom XML properties relationship does not resolve to an internal part.",
                        partUri
                    );
                }
            }

            var storeParsed = state.TryParseXml(
                part,
                cancellationToken,
                out var storeDocument
            );
            if (!storeParsed)
            {
                state.Issues.Add(
                    "CCB_CUSTOM_XML_NOT_WELL_FORMED",
                    WordContentControlIssueSeverity.Error,
                    "A custom XML data-store part is not well-formed XML.",
                    part.Uri
                );
            }
            var itemId = default(string);
            var schemaReferences = Array.Empty<string>();
            if (propertiesPart is not null)
            {
                if (
                    !state.TryParseXml(
                        propertiesPart,
                        cancellationToken,
                        out var propertiesDocument
                    )
                )
                {
                    state.Issues.Add(
                        "CCB_CUSTOM_XML_PROPERTIES_NOT_WELL_FORMED",
                        WordContentControlIssueSeverity.Error,
                        "A custom XML properties part is not well-formed XML.",
                        propertiesPart.Uri
                    );
                }
                else if (propertiesDocument is not null)
                {
                    var root = propertiesDocument.GetParsedElement(
                        propertiesDocument.Root.Ordinal
                    );
                    if (root.Name.LocalName != "datastoreItem")
                    {
                        state.Issues.Add(
                            "CCB_CUSTOM_XML_PROPERTIES_ROOT",
                            WordContentControlIssueSeverity.Error,
                            "The custom XML properties part root is not datastoreItem.",
                            propertiesPart.Uri,
                            propertiesDocument.Root.Ordinal
                        );
                    }
                    var rawItemId = Attribute(root, "itemID");
                    if (!TryNormalizeGuid(rawItemId, out itemId))
                    {
                        state.Issues.Add(
                            "CCB_CUSTOM_XML_ITEM_ID_INVALID",
                            WordContentControlIssueSeverity.Error,
                            "The custom XML properties part has no valid itemID GUID.",
                            propertiesPart.Uri,
                            propertiesDocument.Root.Ordinal
                        );
                        itemId = null;
                    }
                    schemaReferences = root.Descendants()
                        .Where(element => element.Name.LocalName == "schemaRef")
                        .Select(element => Attribute(element, "uri"))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    state.AccountMetadata(schemaReferences);
                }
            }

            var storeRoot = storeDocument?.GetParsedElement(storeDocument.Root.Ordinal);
            state.AccountMetadata(
                itemId,
                part.Uri,
                propertiesPart?.Uri,
                storeRoot?.Name.NamespaceName,
                storeRoot?.Name.LocalName
            );
            var storeId = StableId(
                "wccs_",
                WordBindingStoreKind.CustomXml.ToString(),
                part.Uri,
                itemId ?? "missing"
            );
            state.AddStore(
                new StoreWork(
                    new WordCustomXmlStoreDefinition(
                        storeId,
                        WordBindingStoreKind.CustomXml,
                        itemId,
                        part.Uri,
                        part.ContentType,
                        propertiesPart?.Uri,
                        storeRoot?.Name.NamespaceName,
                        storeRoot?.Name.LocalName,
                        storeDocument?.Elements.Count ?? 0,
                        schemaReferences,
                        incoming.GetValueOrDefault(partUri),
                        propertiesResolved,
                        storeParsed
                    ),
                    storeDocument
                )
            );
        }
    }

    private void DiscoverBuiltInStore(
        OpcPackageSnapshot package,
        WordBindingStoreKind kind,
        string contentType,
        string itemId,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var parts = package.Parts.Values
            .Where(part => string.Equals(
                part.ContentType,
                contentType,
                StringComparison.OrdinalIgnoreCase
            ))
            .OrderBy(part => part.Uri, StringComparer.Ordinal)
            .ToArray();
        if (parts.Length > 1)
        {
            state.Issues.Add(
                "CCB_BUILTIN_STORE_AMBIGUOUS",
                WordContentControlIssueSeverity.Error,
                "More than one package part claims the same built-in property-store content type."
            );
        }
        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.Stores.Count >= _options.MaxStores)
            {
                throw new WordContentControlLimitException(
                    $"Binding-store count exceeds {_options.MaxStores}."
                );
            }
            var parsed = state.TryParseXml(part, cancellationToken, out var document);
            if (!parsed)
            {
                state.Issues.Add(
                    "CCB_BUILTIN_STORE_NOT_WELL_FORMED",
                    WordContentControlIssueSeverity.Error,
                    "A built-in Word property-store part is not well-formed XML.",
                    part.Uri
                );
            }
            var root = document?.GetParsedElement(document.Root.Ordinal);
            var storeId = StableId("wccs_", kind.ToString(), part.Uri, itemId);
            state.AccountMetadata(
                itemId,
                part.Uri,
                root?.Name.NamespaceName,
                root?.Name.LocalName
            );
            state.AddStore(
                new StoreWork(
                    new WordCustomXmlStoreDefinition(
                        storeId,
                        kind,
                        itemId,
                        part.Uri,
                        part.ContentType,
                        null,
                        root?.Name.NamespaceName,
                        root?.Name.LocalName,
                        document?.Elements.Count ?? 0,
                        Array.Empty<string>(),
                        package.Relationships.Count(relationship =>
                            relationship.ResolvedTargetPartUri == part.Uri
                        ),
                        true,
                        parsed
                    ),
                    document
                )
            );
        }
    }

    private void DiscoverControls(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var semanticNodes = semanticDocument.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.ContentControl)
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        if (semanticNodes.Length > _options.MaxControls)
        {
            throw new WordContentControlLimitException(
                $"Content-control count exceeds {_options.MaxControls}."
            );
        }
        var projectedParts = semanticNodes
            .Select(node => node.SourcePartUri)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (projectedParts.Length > _options.MaxStoryParts)
        {
            throw new WordContentControlLimitException(
                $"Content-control story-part count exceeds {_options.MaxStoryParts}."
            );
        }

        var documents = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        foreach (var partUri in projectedParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordContentControlProjectionException(
                    $"Projected story part '{partUri}' is missing from the package."
                );
            }
            documents.Add(partUri, state.ParseXml(part, cancellationToken));
        }

        var nodesById = semanticDocument.Nodes.ToDictionary(node => node.Id);
        var nodeToControlId = semanticNodes.ToDictionary(
            node => node.Id,
            node => StableId(
                "wccc_",
                node.SourcePartUri,
                node.SourceElementOrdinal.ToString(CultureInfo.InvariantCulture),
                node.Id.Value
            )
        );
        foreach (var node in semanticNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = documents[node.SourcePartUri];
            var sdt = document.GetParsedElement(node.SourceElementOrdinal);
            if (!IsWordElement(sdt, "sdt"))
            {
                throw new WordContentControlProjectionException(
                    "A semantic content-control node does not point to a Word sdt element."
                );
            }
            var properties = sdt.Elements().FirstOrDefault(element =>
                IsWordElement(element, "sdtPr")
            );
            var controlId = nodeToControlId[node.Id];
            var parentControlId = FindParentControlId(
                node,
                nodesById,
                nodeToControlId
            );
            var (type, typeExplicit) = DetectType(
                properties,
                state.Issues,
                node.SourcePartUri,
                node.SourceElementOrdinal,
                controlId
            );
            var level = DetectLevel(sdt);
            var nativeId = ChildValue(properties, "id");
            var alias = ChildValue(properties, "alias");
            var tag = ChildValue(properties, "tag");
            var placeholder = properties?.Elements()
                .FirstOrDefault(element => IsWordElement(element, "placeholder"))
                ?.Elements()
                .FirstOrDefault(element => IsWordElement(element, "docPart"))
                ?.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "val")
                ?.Value;
            var showingPlaceholder = OnOffChild(properties, "showingPlcHdr");
            var temporary = OnOffChild(properties, "temporary");
            var repeatingProperties = properties?.Elements().FirstOrDefault(element =>
                element.Name.NamespaceName == Word2013Namespace
                && element.Name.LocalName == "repeatingSection"
            );
            var doNotAllowInsertDelete = OnOffChild(
                repeatingProperties,
                "doNotAllowInsertDeleteSection",
                Word2013Namespace
            );
            var repeatingTitle = repeatingProperties?.Elements()
                .FirstOrDefault(element =>
                    element.Name.NamespaceName == Word2013Namespace
                    && element.Name.LocalName == "sectionTitle"
                )
                ?.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "val")
                ?.Value;
            state.AccountMetadata(
                nativeId,
                alias,
                tag,
                placeholder,
                repeatingTitle,
                node.SourcePartUri
            );

            var bindingElements = properties?.Elements()
                .Where(element =>
                    element.Name.LocalName == "dataBinding"
                    && (
                        IsWordNamespace(element.Name.NamespaceName)
                        || element.Name.NamespaceName == Word2013Namespace
                    )
                )
                .ToArray() ?? Array.Empty<XElement>();
            if (bindingElements.Length > 1)
            {
                state.Issues.Add(
                    "CCB_MULTIPLE_BINDINGS",
                    WordContentControlIssueSeverity.Error,
                    "A content control declares more than one dataBinding element.",
                    node.SourcePartUri,
                    node.SourceElementOrdinal,
                    controlId
                );
            }
            var bindingId = default(string);
            if (bindingElements.Length != 0)
            {
                if (state.BindingWorks.Count >= _options.MaxBindings)
                {
                    throw new WordContentControlLimitException(
                        $"Content-control binding count exceeds {_options.MaxBindings}."
                    );
                }
                var bindingElement = bindingElements[0];
                bindingId = StableId(
                    "wccb_",
                    controlId,
                    document.GetElementOrdinal(bindingElement).ToString(
                        CultureInfo.InvariantCulture
                    )
                );
                var rawStoreItemId = Attribute(bindingElement, "storeItemID")
                    ?? Attribute(bindingElement, "storeItemId");
                var xpath = Attribute(bindingElement, "xpath") ?? string.Empty;
                var prefixMappings = Attribute(bindingElement, "prefixMappings")
                    ?? string.Empty;
                state.AccountMetadata(rawStoreItemId, xpath, prefixMappings);
                state.BindingWorks.Add(
                    new BindingWork(
                        bindingId,
                        controlId,
                        node.SourcePartUri,
                        document.GetElementOrdinal(bindingElement),
                        bindingElement.Name.NamespaceName == Word2013Namespace,
                        rawStoreItemId,
                        xpath,
                        prefixMappings,
                        type,
                        level
                    )
                );
            }

            state.ControlWorks.Add(
                new ControlWork(
                    controlId,
                    node.Id,
                    node.SourcePartUri,
                    node.SourceElementOrdinal,
                    parentControlId,
                    type,
                    typeExplicit,
                    level,
                    nativeId,
                    alias,
                    tag,
                    ParseLock(properties),
                    placeholder,
                    showingPlaceholder,
                    temporary,
                    doNotAllowInsertDelete,
                    repeatingTitle,
                    bindingId
                )
            );
        }
    }

    private void ResolveBindings(BuildState state, CancellationToken cancellationToken)
    {
        var storesByItemId = state.Stores
            .Where(store => store.Definition.ItemId is not null)
            .GroupBy(store => store.Definition.ItemId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase
            );
        foreach (var duplicate in storesByItemId.Where(pair => pair.Value.Length > 1))
        {
            state.Issues.Add(
                "CCB_DUPLICATE_STORE_ITEM_ID",
                WordContentControlIssueSeverity.Error,
                "More than one data store has the same normalized itemID.",
                storeId: duplicate.Value[0].Definition.Id
            );
        }

        foreach (var work in state.BindingWorks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = WordBindingResolutionStatus.Resolved;
            var storeItemId = default(string);
            StoreWork? store = null;
            if (string.IsNullOrWhiteSpace(work.RawStoreItemId))
            {
                status = WordBindingResolutionStatus.StoreIdMissing;
            }
            else if (!TryNormalizeGuid(work.RawStoreItemId, out storeItemId))
            {
                status = WordBindingResolutionStatus.StoreIdInvalid;
            }
            else if (!storesByItemId.TryGetValue(storeItemId, out var stores))
            {
                status = WordBindingResolutionStatus.StoreMissing;
            }
            else if (stores.Length != 1)
            {
                status = WordBindingResolutionStatus.StoreAmbiguous;
            }
            else
            {
                store = stores[0];
            }

            IReadOnlyDictionary<string, string> mappings =
                new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                );
            if (status == WordBindingResolutionStatus.Resolved)
            {
                var mappingResult = ParsePrefixMappings(work.PrefixMappings);
                if (!mappingResult.Valid)
                {
                    status = WordBindingResolutionStatus.PrefixMappingsInvalid;
                }
                else
                {
                    mappings = new ReadOnlyDictionary<string, string>(
                        mappingResult.Mappings
                    );
                    state.AccountMetadata(mappingResult.Mappings.Keys);
                    state.AccountMetadata(mappingResult.Mappings.Values);
                }
            }

            var targets = Array.Empty<XElement>();
            if (status == WordBindingResolutionStatus.Resolved)
            {
                if (string.IsNullOrWhiteSpace(work.XPath))
                {
                    status = WordBindingResolutionStatus.XPathMissing;
                }
                else
                {
                    var pathResult = ParseXPath(work.XPath, mappings);
                    if (pathResult.Status != WordBindingResolutionStatus.Resolved)
                    {
                        status = pathResult.Status;
                    }
                    else if (store?.Document is null)
                    {
                        status = WordBindingResolutionStatus.StoreUnreadable;
                    }
                    else
                    {
                        targets = store.EvaluateXPath(
                            pathResult.Steps,
                            _options.MaxTargetsPerBinding,
                            cancellationToken
                        );
                        if (targets.Length == 0)
                        {
                            status = WordBindingResolutionStatus.TargetMissing;
                        }
                    }
                }
            }

            if (targets.Length > _options.MaxTargetsPerBinding)
            {
                throw new WordContentControlLimitException(
                    $"Binding target count exceeds {_options.MaxTargetsPerBinding} for one control."
                );
            }
            if (state.Targets.Count + targets.Length > _options.MaxTargets)
            {
                throw new WordContentControlLimitException(
                    $"Aggregate binding target count exceeds {_options.MaxTargets}."
                );
            }

            var targetIds = new List<string>(targets.Length);
            if (store?.Document is { } storeDocument)
            {
                foreach (var target in targets)
                {
                    var ordinal = storeDocument.GetElementOrdinal(target);
                    var targetId = StableId(
                        "wcct_",
                        work.Id,
                        store.Definition.Id,
                        ordinal.ToString(CultureInfo.InvariantCulture)
                    );
                    targetIds.Add(targetId);
                    state.Targets.Add(
                        new WordContentControlBindingTarget(
                            targetId,
                            work.Id,
                            store.Definition.Id,
                            ordinal,
                            target.Name.NamespaceName,
                            target.Name.LocalName
                        )
                    );
                    state.AccountMetadata(
                        target.Name.NamespaceName,
                        target.Name.LocalName
                    );
                }
            }

            var definition = new WordContentControlBindingDefinition(
                work.Id,
                work.ControlId,
                work.PartUri,
                work.SourceElementOrdinal,
                work.IsOffice2013RichTextBinding,
                storeItemId,
                store?.Definition.Id,
                work.XPath,
                work.PrefixMappings,
                mappings,
                status,
                targetIds
            );
            state.Bindings.Add(definition);
            AddBindingDiagnostics(work, definition, state.Issues);
        }
    }

    private static void AddBindingDiagnostics(
        BindingWork work,
        WordContentControlBindingDefinition definition,
        IssueState issues
    )
    {
        if (definition.Status != WordBindingResolutionStatus.Resolved)
        {
            issues.Add(
                "CCB_BINDING_" + definition.Status.ToString().ToUpperInvariant(),
                definition.Status == WordBindingResolutionStatus.XPathUnsupported
                    ? WordContentControlIssueSeverity.Warning
                    : WordContentControlIssueSeverity.Error,
                "A content-control binding could not be resolved: "
                    + definition.Status.ToString() + ".",
                work.PartUri,
                work.SourceElementOrdinal,
                work.ControlId,
                definition.StoreId,
                work.Id
            );
        }
        if (
            work.Level == WordContentControlLevel.TableRow
            && definition.Status == WordBindingResolutionStatus.Resolved
        )
        {
            issues.Add(
                "CCB_ROW_LEVEL_BINDING",
                WordContentControlIssueSeverity.Error,
                "A row-level content control declares mapped XML even though row-level SDT content cannot be mapped.",
                work.PartUri,
                work.SourceElementOrdinal,
                work.ControlId,
                definition.StoreId,
                work.Id
            );
        }
        if (
            work.ControlType is WordContentControlType.RichText
                or WordContentControlType.BuildingBlockGallery
            && !work.IsOffice2013RichTextBinding
        )
        {
            issues.Add(
                "CCB_STANDARD_BINDING_IGNORED_FOR_CONTROL_TYPE",
                WordContentControlIssueSeverity.Warning,
                "The standard dataBinding is ignored for this content-control type.",
                work.PartUri,
                work.SourceElementOrdinal,
                work.ControlId,
                definition.StoreId,
                work.Id
            );
        }
        if (
            definition.Status == WordBindingResolutionStatus.Resolved
            && definition.TargetIds.Count > 1
            && work.ControlType != WordContentControlType.RepeatingSection
        )
        {
            issues.Add(
                "CCB_NONREPEATING_MULTIPLE_TARGETS",
                WordContentControlIssueSeverity.Warning,
                "A non-repeating content control binding selects more than one XML element.",
                work.PartUri,
                work.SourceElementOrdinal,
                work.ControlId,
                definition.StoreId,
                work.Id
            );
        }
    }

    private static void BuildRepeatingSections(
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var controlsByParent = state.ControlWorks
            .Where(control => control.ParentControlId is not null)
            .GroupBy(control => control.ParentControlId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var bindingById = state.Bindings.ToDictionary(binding => binding.Id);
        foreach (
            var container in state.ControlWorks.Where(control =>
                control.Type == WordContentControlType.RepeatingSection
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var children = controlsByParent.GetValueOrDefault(container.Id)
                ?? Array.Empty<ControlWork>();
            var items = children
                .Where(child => child.Type == WordContentControlType.RepeatingSectionItem)
                .ToArray();
            if (children.Length != items.Length)
            {
                state.Issues.Add(
                    "CCB_REPEATING_SECTION_CHILD_TYPE",
                    WordContentControlIssueSeverity.Error,
                    "A repeating section contains a direct child content control that is not a repeating-section item.",
                    container.PartUri,
                    container.SourceElementOrdinal,
                    container.Id
                );
            }
            var targetCount = 0;
            bool? cardinalityMatches = null;
            if (
                container.BindingId is not null
                && bindingById.TryGetValue(container.BindingId, out var binding)
                && binding.Status == WordBindingResolutionStatus.Resolved
            )
            {
                targetCount = binding.TargetIds.Count;
                cardinalityMatches = targetCount == items.Length;
                if (!cardinalityMatches.Value)
                {
                    state.Issues.Add(
                        "CCB_REPEATING_SECTION_CARDINALITY",
                        WordContentControlIssueSeverity.Error,
                        "The repeating-section item count does not match the binding target count.",
                        container.PartUri,
                        container.SourceElementOrdinal,
                        container.Id,
                        binding.StoreId,
                        binding.Id
                    );
                }
            }
            state.RepeatingSections.Add(
                new WordRepeatingSectionDefinition(
                    StableId("wccr_", container.Id),
                    container.Id,
                    container.PartUri,
                    container.SourceElementOrdinal,
                    items.Select(item => item.Id).ToArray(),
                    targetCount,
                    cardinalityMatches,
                    container.DoNotAllowInsertDeleteSection
                )
            );
        }

        foreach (
            var item in state.ControlWorks.Where(control =>
                control.Type == WordContentControlType.RepeatingSectionItem
            )
        )
        {
            var parent = item.ParentControlId is null
                ? null
                : state.ControlWorks.FirstOrDefault(control => control.Id == item.ParentControlId);
            if (parent?.Type != WordContentControlType.RepeatingSection)
            {
                state.Issues.Add(
                    "CCB_REPEATING_ITEM_PARENT",
                    WordContentControlIssueSeverity.Error,
                    "A repeating-section item is not directly contained by a repeating section.",
                    item.PartUri,
                    item.SourceElementOrdinal,
                    item.Id
                );
            }
        }
    }

    private static void ValidateControlIdentities(
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var group in state.ControlWorks
                .Where(control => !string.IsNullOrWhiteSpace(control.NativeId))
                .GroupBy(control => control.NativeId!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var control in group)
            {
                state.Issues.Add(
                    "CCB_DUPLICATE_CONTROL_ID",
                    WordContentControlIssueSeverity.Warning,
                    "More than one content control uses the same native w:id value.",
                    control.PartUri,
                    control.SourceElementOrdinal,
                    control.Id
                );
            }
        }
    }

    private PrefixMappingParse ParsePrefixMappings(string value)
    {
        if (value.Length > _options.MaxPrefixMappingsCharacters)
        {
            throw new WordContentControlLimitException(
                $"prefixMappings exceeds {_options.MaxPrefixMappingsCharacters} characters."
            );
        }
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        while (true)
        {
            SkipWhitespace(value, ref index);
            if (index == value.Length)
            {
                return new PrefixMappingParse(true, mappings);
            }
            const string marker = "xmlns:";
            if (!value.AsSpan(index).StartsWith(marker, StringComparison.Ordinal))
            {
                return new PrefixMappingParse(false, mappings);
            }
            index += marker.Length;
            var prefixStart = index;
            while (
                index < value.Length
                && !char.IsWhiteSpace(value[index])
                && value[index] != '='
            )
            {
                index++;
            }
            var prefix = value[prefixStart..index];
            if (!IsNcName(prefix))
            {
                return new PrefixMappingParse(false, mappings);
            }
            SkipWhitespace(value, ref index);
            if (index >= value.Length || value[index++] != '=')
            {
                return new PrefixMappingParse(false, mappings);
            }
            SkipWhitespace(value, ref index);
            if (index >= value.Length || value[index] is not ('\'' or '"'))
            {
                return new PrefixMappingParse(false, mappings);
            }
            var quote = value[index++];
            var uriStart = index;
            while (index < value.Length && value[index] != quote)
            {
                index++;
            }
            if (index == value.Length)
            {
                return new PrefixMappingParse(false, mappings);
            }
            var namespaceUri = value[uriStart..index++];
            if (
                namespaceUri.Length == 0
                || mappings.ContainsKey(prefix)
                || mappings.Count >= _options.MaxNamespaceMappings
            )
            {
                return new PrefixMappingParse(false, mappings);
            }
            mappings.Add(prefix, namespaceUri);
        }
    }

    private XPathParse ParseXPath(
        string xpath,
        IReadOnlyDictionary<string, string> mappings
    )
    {
        if (xpath.Length > _options.MaxXPathCharacters)
        {
            throw new WordContentControlLimitException(
                $"Binding XPath exceeds {_options.MaxXPathCharacters} characters."
            );
        }
        if (xpath.Length < 2 || xpath[0] != '/' || xpath.Contains("//", StringComparison.Ordinal))
        {
            return new XPathParse(
                WordBindingResolutionStatus.XPathUnsupported,
                Array.Empty<XPathStep>()
            );
        }
        var rawSteps = xpath[1..].Split('/');
        var steps = new List<XPathStep>(rawSteps.Length);
        foreach (var rawStep in rawSteps)
        {
            if (rawStep.Length == 0)
            {
                return new XPathParse(
                    WordBindingResolutionStatus.XPathInvalid,
                    Array.Empty<XPathStep>()
                );
            }
            var name = rawStep;
            int? position = null;
            var bracket = rawStep.IndexOf('[');
            if (bracket >= 0)
            {
                if (
                    !rawStep.EndsWith(']')
                    || rawStep.IndexOf('[', bracket + 1) >= 0
                    || rawStep.IndexOf(']') != rawStep.Length - 1
                )
                {
                    return new XPathParse(
                        WordBindingResolutionStatus.XPathUnsupported,
                        Array.Empty<XPathStep>()
                    );
                }
                name = rawStep[..bracket];
                var predicate = rawStep[(bracket + 1)..^1];
                if (
                    !int.TryParse(
                        predicate,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedPosition
                    )
                    || parsedPosition <= 0
                )
                {
                    return new XPathParse(
                        WordBindingResolutionStatus.XPathUnsupported,
                        Array.Empty<XPathStep>()
                    );
                }
                position = parsedPosition;
            }
            var separator = name.IndexOf(':');
            string namespaceUri;
            string localName;
            if (separator < 0)
            {
                namespaceUri = string.Empty;
                localName = name;
            }
            else
            {
                if (
                    separator == 0
                    || separator == name.Length - 1
                    || name.IndexOf(':', separator + 1) >= 0
                )
                {
                    return new XPathParse(
                        WordBindingResolutionStatus.XPathInvalid,
                        Array.Empty<XPathStep>()
                    );
                }
                var prefix = name[..separator];
                localName = name[(separator + 1)..];
                if (!mappings.TryGetValue(prefix, out namespaceUri!))
                {
                    return new XPathParse(
                        WordBindingResolutionStatus.XPathInvalid,
                        Array.Empty<XPathStep>()
                    );
                }
            }
            if (!IsNcName(localName))
            {
                return new XPathParse(
                    WordBindingResolutionStatus.XPathUnsupported,
                    Array.Empty<XPathStep>()
                );
            }
            steps.Add(new XPathStep(namespaceUri, localName, position));
        }
        return new XPathParse(WordBindingResolutionStatus.Resolved, steps);
    }

    private static bool Matches(XElement element, XPathStep step) =>
        string.Equals(element.Name.NamespaceName, step.NamespaceUri, StringComparison.Ordinal)
        && string.Equals(element.Name.LocalName, step.LocalName, StringComparison.Ordinal);

    private static string? FindParentControlId(
        WordSemanticNode node,
        IReadOnlyDictionary<SemanticNodeId, WordSemanticNode> nodesById,
        IReadOnlyDictionary<SemanticNodeId, string> controlIds
    )
    {
        var parentId = node.ParentId;
        while (parentId is not null && nodesById.TryGetValue(parentId.Value, out var parent))
        {
            if (controlIds.TryGetValue(parent.Id, out var controlId))
            {
                return controlId;
            }
            parentId = parent.ParentId;
        }
        return null;
    }

    private static (WordContentControlType Type, bool Explicit) DetectType(
        XElement? properties,
        IssueState issues,
        string partUri,
        int sourceElementOrdinal,
        string controlId
    )
    {
        if (properties is null)
        {
            issues.Add(
                "CCB_PROPERTIES_MISSING",
                WordContentControlIssueSeverity.Warning,
                "A content control has no sdtPr element.",
                partUri,
                sourceElementOrdinal,
                controlId
            );
            return (WordContentControlType.RichText, false);
        }
        var types = properties.Elements()
            .Where(element => ExplicitTypes.ContainsKey((
                element.Name.NamespaceName,
                element.Name.LocalName
            )))
            .Select(element => ExplicitTypes[(element.Name.NamespaceName, element.Name.LocalName)])
            .ToArray();
        if (types.Length == 0)
        {
            return (WordContentControlType.RichText, false);
        }
        if (types.Length > 1)
        {
            issues.Add(
                "CCB_MULTIPLE_CONTROL_TYPES",
                WordContentControlIssueSeverity.Error,
                "A content control declares more than one mutually exclusive type.",
                partUri,
                sourceElementOrdinal,
                controlId
            );
            return (WordContentControlType.Unknown, true);
        }
        return (types[0], true);
    }

    private static WordContentControlLevel DetectLevel(XElement sdt)
    {
        var parent = sdt.Parent;
        var content = sdt.Elements().FirstOrDefault(element =>
            IsWordElement(element, "sdtContent")
        );
        if (parent is null || content is null)
        {
            return WordContentControlLevel.Unknown;
        }
        if (IsWordElement(parent, "p"))
        {
            return WordContentControlLevel.Run;
        }
        if (IsWordElement(parent, "tbl") && content.Elements().Any(element => IsWordElement(element, "tr")))
        {
            return WordContentControlLevel.TableRow;
        }
        if (IsWordElement(parent, "tr") && content.Elements().Any(element => IsWordElement(element, "tc")))
        {
            return WordContentControlLevel.TableCell;
        }
        if (
            content.Elements().Any(element =>
                IsWordElement(element, "p") || IsWordElement(element, "tbl")
            )
        )
        {
            return WordContentControlLevel.Block;
        }
        return WordContentControlLevel.Unknown;
    }

    private static WordContentControlLock ParseLock(XElement? properties)
    {
        var value = ChildValue(properties, "lock");
        return value switch
        {
            null => WordContentControlLock.Unspecified,
            "unlocked" => WordContentControlLock.Unlocked,
            "sdtLocked" => WordContentControlLock.ControlLocked,
            "contentLocked" => WordContentControlLock.ContentLocked,
            "sdtContentLocked" => WordContentControlLock.ControlAndContentLocked,
            _ => WordContentControlLock.Unknown,
        };
    }

    private static string? ChildValue(XElement? parent, string localName) =>
        parent?.Elements()
            .FirstOrDefault(element => IsWordElement(element, localName))
            ?.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "val")
            ?.Value;

    private static bool OnOffChild(
        XElement? parent,
        string localName,
        string? namespaceUri = null
    )
    {
        var element = parent?.Elements().FirstOrDefault(child =>
            child.Name.LocalName == localName
            && (
                namespaceUri is null
                    ? IsWordNamespace(child.Name.NamespaceName)
                    : child.Name.NamespaceName == namespaceUri
            )
        );
        if (element is null)
        {
            return false;
        }
        var value = element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "val")
            ?.Value;
        return value is null
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value == "1";
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes()
            .FirstOrDefault(attribute =>
                !attribute.IsNamespaceDeclaration
                && attribute.Name.LocalName == localName
            )
            ?.Value;

    private static bool TryNormalizeGuid(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!Guid.TryParse(value?.Trim(), out var guid))
        {
            return false;
        }
        normalized = guid.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
        return true;
    }

    private static bool IsWordElement(XElement element, string localName) =>
        IsWordNamespace(element.Name.NamespaceName)
        && element.Name.LocalName == localName;

    private static bool IsWordNamespace(string namespaceUri) =>
        namespaceUri is TransitionalWordNamespace or StrictWordNamespace;

    private static string? RelationshipKind(string relationshipType)
    {
        if (!Uri.TryCreate(relationshipType, UriKind.Absolute, out var uri))
        {
            return null;
        }
        var path = uri.AbsolutePath.TrimEnd('/');
        var separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static void SkipWhitespace(string value, ref int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }
    }

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

    private static string StableId(string prefix, params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                length,
                bytes.Length
            );
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return prefix
            + Convert.ToHexString(hash.GetHashAndReset().AsSpan(0, 16))
                .ToLowerInvariant();
    }

    private sealed class BuildState
    {
        private readonly WordContentControlBindingGraphOptions _options;
        private readonly WordOperationResourceLease? _resourceLease;
        private readonly HashSet<string> _parsedPartUris = new(StringComparer.Ordinal);
        private long _metadataCharacters;

        public BuildState(
            WordContentControlBindingGraphOptions options,
            WordOperationResourceLease? resourceLease
        )
        {
            _options = options;
            _resourceLease = resourceLease;
            Issues = new IssueState(options.MaxIssues);
        }

        public List<StoreWork> Stores { get; } = [];

        public List<ControlWork> ControlWorks { get; } = [];

        public List<BindingWork> BindingWorks { get; } = [];

        public List<WordContentControlBindingDefinition> Bindings { get; } = [];

        public List<WordContentControlBindingTarget> Targets { get; } = [];

        public List<WordRepeatingSectionDefinition> RepeatingSections { get; } = [];

        public IssueState Issues { get; }

        public long ParsedXmlBytes { get; private set; }

        public int ParsedXmlElements { get; private set; }

        public LosslessXmlDocument ParseXml(
            OpcPart part,
            CancellationToken cancellationToken
        )
        {
            if (part.Entry.Content.Length > _options.MaxPartBytes)
            {
                throw new WordContentControlLimitException(
                    $"XML part '{part.Uri}' exceeds {_options.MaxPartBytes} bytes."
                );
            }
            if (
                !_parsedPartUris.Contains(part.Uri)
                && ParsedXmlBytes > _options.MaxAggregateXmlBytes - part.Entry.Content.Length
            )
            {
                throw new WordContentControlLimitException(
                    $"Aggregate parsed XML exceeds {_options.MaxAggregateXmlBytes} bytes."
                );
            }
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxPartBytes,
                MaxXmlCharacters = _options.MaxPartBytes,
                MaxXmlElements = _options.MaxElementsPerPart,
                MaxXmlDepth = 256,
                MaxTextCharacters = _options.MaxPartBytes,
            };
            var document = _resourceLease is null
                ? LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    cancellationToken
                )
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.ContentControls,
                    cancellationToken
                );
            if (_parsedPartUris.Add(part.Uri))
            {
                checked
                {
                    ParsedXmlBytes += part.Entry.Content.Length;
                    ParsedXmlElements += document.Elements.Count;
                }
                if (ParsedXmlBytes > _options.MaxAggregateXmlBytes)
                {
                    throw new WordContentControlLimitException(
                        $"Aggregate parsed XML exceeds {_options.MaxAggregateXmlBytes} bytes."
                    );
                }
                if (ParsedXmlElements > _options.MaxAggregateElements)
                {
                    throw new WordContentControlLimitException(
                        $"Aggregate parsed XML exceeds {_options.MaxAggregateElements} elements."
                    );
                }
            }
            return document;
        }

        public bool TryParseXml(
            OpcPart part,
            CancellationToken cancellationToken,
            out LosslessXmlDocument? document
        )
        {
            try
            {
                document = ParseXml(part, cancellationToken);
                return true;
            }
            catch (LosslessXmlParseException)
            {
                document = null;
                return false;
            }
            catch (LosslessXmlEncodingException)
            {
                document = null;
                return false;
            }
        }

        public void AddStore(StoreWork store)
        {
            Stores.Add(store);
        }

        public void AccountMetadata(params string?[] values)
        {
            foreach (var value in values)
            {
                if (value is null)
                {
                    continue;
                }
                checked
                {
                    _metadataCharacters += value.Length;
                }
                if (_metadataCharacters > _options.MaxMetadataCharacters)
                {
                    throw new WordContentControlLimitException(
                        $"Binding metadata exceeds {_options.MaxMetadataCharacters} characters."
                    );
                }
            }
        }

        public void AccountMetadata(IEnumerable<string> values) =>
            AccountMetadata(values.Cast<string?>().ToArray());

        public WordContentControlBindingGraph Freeze(string packageFingerprint)
        {
            var controls = ControlWorks.Select(control => control.Freeze()).ToArray();
            return new WordContentControlBindingGraph(
                packageFingerprint,
                Stores.Select(store => store.Definition).ToArray(),
                controls,
                Bindings,
                Targets,
                RepeatingSections,
                Issues.Items,
                Issues.Truncated,
                ParsedXmlBytes,
                ParsedXmlElements
            );
        }
    }

    private sealed class IssueState
    {
        private readonly int _maximum;
        private int _sequence;

        public IssueState(int maximum)
        {
            _maximum = maximum;
        }

        public List<WordContentControlIssue> Items { get; } = [];

        public bool Truncated { get; private set; }

        public void Add(
            string code,
            WordContentControlIssueSeverity severity,
            string message,
            string? partUri = null,
            int? sourceElementOrdinal = null,
            string? controlId = null,
            string? storeId = null,
            string? bindingId = null
        )
        {
            var sequence = _sequence++;
            if (Items.Count >= _maximum)
            {
                Truncated = true;
                return;
            }
            Items.Add(
                new WordContentControlIssue(
                    StableId(
                        "wcci_",
                        code,
                        partUri ?? string.Empty,
                        sourceElementOrdinal?.ToString(CultureInfo.InvariantCulture)
                            ?? string.Empty,
                        sequence.ToString(CultureInfo.InvariantCulture)
                    ),
                    code,
                    severity,
                    message,
                    partUri,
                    sourceElementOrdinal,
                    controlId,
                    storeId,
                    bindingId
                )
            );
        }
    }

    private sealed class StoreWork
    {
        private readonly Dictionary<
            XElement,
            Dictionary<(string NamespaceUri, string LocalName), XElement[]>
        > _childrenByName = new(ReferenceEqualityComparer.Instance);

        public StoreWork(
            WordCustomXmlStoreDefinition definition,
            LosslessXmlDocument? document
        )
        {
            Definition = definition;
            Document = document;
        }

        public WordCustomXmlStoreDefinition Definition { get; }

        public LosslessXmlDocument? Document { get; }

        public XElement[] EvaluateXPath(
            IReadOnlyList<XPathStep> steps,
            int maxTargets,
            CancellationToken cancellationToken
        )
        {
            if (Document is null || steps.Count == 0)
            {
                return Array.Empty<XElement>();
            }
            cancellationToken.ThrowIfCancellationRequested();
            var root = Document.GetParsedElement(Document.Root.Ordinal);
            var first = steps[0];
            if (!Matches(root, first) || first.Position is > 1)
            {
                return Array.Empty<XElement>();
            }
            var current = new[] { root };
            for (var stepIndex = 1; stepIndex < steps.Count; stepIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = steps[stepIndex];
                var next = new List<XElement>();
                foreach (var parent in current)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var matching = Children(parent, step);
                    if (step.Position is int position)
                    {
                        if (position <= matching.Length)
                        {
                            next.Add(matching[position - 1]);
                        }
                    }
                    else
                    {
                        if (next.Count + matching.Length > maxTargets)
                        {
                            throw new WordContentControlLimitException(
                                $"Binding target count exceeds {maxTargets} for one control."
                            );
                        }
                        next.AddRange(matching);
                    }
                    if (next.Count > maxTargets)
                    {
                        throw new WordContentControlLimitException(
                            $"Binding target count exceeds {maxTargets} for one control."
                        );
                    }
                }
                current = next.ToArray();
                if (current.Length == 0)
                {
                    break;
                }
            }
            return current;
        }

        private XElement[] Children(XElement parent, XPathStep step)
        {
            if (!_childrenByName.TryGetValue(parent, out var byName))
            {
                byName = parent
                    .Elements()
                    .GroupBy(element =>
                        (element.Name.NamespaceName, element.Name.LocalName)
                    )
                    .ToDictionary(group => group.Key, group => group.ToArray());
                _childrenByName.Add(parent, byName);
            }
            return byName.GetValueOrDefault((step.NamespaceUri, step.LocalName))
                ?? Array.Empty<XElement>();
        }
    }

    private sealed record BindingWork(
        string Id,
        string ControlId,
        string PartUri,
        int SourceElementOrdinal,
        bool IsOffice2013RichTextBinding,
        string? RawStoreItemId,
        string XPath,
        string PrefixMappings,
        WordContentControlType ControlType,
        WordContentControlLevel Level
    );

    private sealed record ControlWork(
        string Id,
        SemanticNodeId SemanticNodeId,
        string PartUri,
        int SourceElementOrdinal,
        string? ParentControlId,
        WordContentControlType Type,
        bool TypeExplicit,
        WordContentControlLevel Level,
        string? NativeId,
        string? Alias,
        string? Tag,
        WordContentControlLock Lock,
        string? PlaceholderBuildingBlock,
        bool ShowingPlaceholder,
        bool Temporary,
        bool DoNotAllowInsertDeleteSection,
        string? RepeatingSectionTitle,
        string? BindingId
    )
    {
        public WordContentControlDefinition Freeze() => new(
            Id,
            SemanticNodeId,
            PartUri,
            SourceElementOrdinal,
            ParentControlId,
            Type,
            TypeExplicit,
            Level,
            NativeId,
            Alias,
            Tag,
            Lock,
            PlaceholderBuildingBlock,
            ShowingPlaceholder,
            Temporary,
            DoNotAllowInsertDeleteSection,
            RepeatingSectionTitle,
            BindingId
        );
    }

    private sealed record PrefixMappingParse(
        bool Valid,
        Dictionary<string, string> Mappings
    );

    private sealed record XPathStep(
        string NamespaceUri,
        string LocalName,
        int? Position
    );

    private sealed record XPathParse(
        WordBindingResolutionStatus Status,
        IReadOnlyList<XPathStep> Steps
    );
}

public class WordContentControlException : InvalidOperationException
{
    public WordContentControlException(string message)
        : base(message)
    {
    }

    public WordContentControlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordContentControlLimitException : WordContentControlException
{
    public WordContentControlLimitException(string message)
        : base(message)
    {
    }
}

public sealed class WordContentControlProjectionException : WordContentControlException
{
    public WordContentControlProjectionException(string message)
        : base(message)
    {
    }
}
