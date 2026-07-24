using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordDocumentPropertyFamily
{
    Core,
    Extended,
    Custom,
}

public enum WordDocumentPropertyValueKind
{
    Text,
    Integer,
    UnsignedInteger,
    FloatingPoint,
    Decimal,
    Boolean,
    DateTime,
    Currency,
    ErrorCode,
    ClassId,
    Binary,
    Vector,
    Array,
    Variant,
    Empty,
    Unknown,
}

public enum WordDocumentPropertyIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordDocumentPropertyIssue(
    string Code,
    WordDocumentPropertyIssueSeverity Severity,
    string Message,
    string? PropertyId = null,
    string? PartUri = null,
    int? SourceElementOrdinal = null
);

public sealed record WordDocumentPropertyPart(
    WordDocumentPropertyFamily Family,
    string PartUri,
    string ContentType,
    bool IsPackageReachable,
    string SourceSha256
);

public sealed record WordDocumentProperty(
    string Id,
    WordDocumentPropertyFamily Family,
    string Name,
    string CanonicalName,
    WordDocumentPropertyValueKind ValueKind,
    string? Value,
    int ValueCharacterCount,
    bool HasScalarValue,
    bool IsUniquelyNamed,
    bool IsStructurallyValid,
    int? PropertyId,
    string? FormatId,
    string PartUri,
    int SourceElementOrdinal,
    bool IsPackageReachable
);

public sealed class WordDocumentPropertyGraph
{
    private readonly IReadOnlyDictionary<string, WordDocumentProperty>
        _fieldProperties;

    internal WordDocumentPropertyGraph(
        string packageFingerprint,
        IReadOnlyList<WordDocumentPropertyPart> parts,
        IReadOnlyList<WordDocumentProperty> properties,
        IReadOnlyDictionary<string, WordDocumentProperty> fieldProperties,
        IReadOnlyList<WordDocumentPropertyIssue> issues,
        bool issuesTruncated
    )
    {
        PackageFingerprint = packageFingerprint;
        Parts = new ReadOnlyCollection<WordDocumentPropertyPart>(parts.ToArray());
        Properties = new ReadOnlyCollection<WordDocumentProperty>(
            properties.ToArray()
        );
        _fieldProperties = new ReadOnlyDictionary<string, WordDocumentProperty>(
            new Dictionary<string, WordDocumentProperty>(
                fieldProperties,
                StringComparer.OrdinalIgnoreCase
            )
        );
        Issues = new ReadOnlyCollection<WordDocumentPropertyIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordDocumentPropertyPart> Parts { get; }

    public IReadOnlyList<WordDocumentProperty> Properties { get; }

    public IReadOnlyList<WordDocumentPropertyIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public bool TryResolveFieldProperty(
        string name,
        out WordDocumentProperty? property
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _fieldProperties.TryGetValue(name.Trim(), out property);
    }
}

public sealed record WordDocumentPropertyGraphOptions
{
    public static WordDocumentPropertyGraphOptions Default { get; } = new();

    public int MaxPartBytes { get; init; } = 16 * 1024 * 1024;

    public int MaxTotalXmlBytes { get; init; } = 32 * 1024 * 1024;

    public int MaxPropertiesPerPart { get; init; } = 50_000;

    public int MaxProperties { get; init; } = 100_000;

    public int MaxValueCharacters { get; init; } = 1_048_576;

    public int MaxTotalValueCharacters { get; init; } = 16 * 1024 * 1024;

    public int MaxNameCharacters { get; init; } = 4_096;

    public int MaxIssues { get; init; } = 1_000;

    internal void Validate()
    {
        if (MaxPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPartBytes));
        }
        if (MaxTotalXmlBytes <= 0 || MaxTotalXmlBytes < MaxPartBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalXmlBytes));
        }
        if (MaxPropertiesPerPart <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPropertiesPerPart));
        }
        if (MaxProperties <= 0 || MaxProperties < MaxPropertiesPerPart)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxProperties));
        }
        if (MaxValueCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxValueCharacters));
        }
        if (
            MaxTotalValueCharacters <= 0
            || MaxTotalValueCharacters < MaxValueCharacters
        )
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalValueCharacters));
        }
        if (MaxNameCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNameCharacters));
        }
        if (MaxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxIssues));
        }
    }
}

public sealed class WordDocumentPropertyGraphBuilder
{
    private const string CorePropertiesRelationship =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string ExtendedPropertiesRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";
    private const string StrictExtendedPropertiesRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/extended-properties";
    private const string CustomPropertiesRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    private const string StrictCustomPropertiesRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/custom-properties";

    private const string CorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";
    private const string ExtendedPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.extended-properties+xml";
    private const string CustomPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.custom-properties+xml";

    private const string CorePropertiesNamespace =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    private const string DcTermsNamespace = "http://purl.org/dc/terms/";
    private const string XmlSchemaInstanceNamespace =
        "http://www.w3.org/2001/XMLSchema-instance";
    private const string ExtendedPropertiesNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private const string StrictExtendedPropertiesNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/extendedProperties";
    private const string CustomPropertiesNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    private const string StrictCustomPropertiesNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/customProperties";
    private const string VariantTypesNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
    private const string StrictVariantTypesNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/docPropsVTypes";
    private const string CustomPropertyFormatId =
        "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";

    private static readonly IReadOnlyDictionary<string, string> CoreFieldAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = "Title",
            ["subject"] = "Subject",
            ["creator"] = "Author",
            ["keywords"] = "Keywords",
            ["description"] = "Comments",
            ["lastModifiedBy"] = "LastSavedBy",
            ["revision"] = "RevisionNumber",
            ["lastPrinted"] = "LastPrinted",
            ["created"] = "CreateTime",
            ["modified"] = "SaveTime",
            ["category"] = "Category",
            ["contentStatus"] = "ContentStatus",
        };

    private static readonly IReadOnlyDictionary<string, string> ExtendedFieldAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Template"] = "Template",
            ["Manager"] = "Manager",
            ["Company"] = "Company",
            ["Pages"] = "Pages",
            ["Words"] = "Words",
            ["Characters"] = "Characters",
            ["Lines"] = "Lines",
            ["Paragraphs"] = "Paragraphs",
            ["CharactersWithSpaces"] = "CharactersWithSpaces",
            ["Application"] = "Application",
            ["AppVersion"] = "AppVersion",
            ["TotalTime"] = "TotalTime",
        };

    private static readonly IReadOnlySet<string> ExtendedIntegerProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "TotalTime",
            "Pages",
            "Words",
            "Characters",
            "DocSecurity",
            "Lines",
            "Paragraphs",
            "CharactersWithSpaces",
            "MMClips",
            "Notes",
            "HiddenSlides",
        };

    private static readonly IReadOnlySet<string> ExtendedBooleanProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ScaleCrop",
            "LinksUpToDate",
            "SharedDoc",
            "HyperlinksChanged",
        };

    private static readonly IReadOnlySet<string> ExtendedPropertyNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Template",
            "Manager",
            "Company",
            "Pages",
            "Words",
            "Characters",
            "PresentationFormat",
            "Lines",
            "Paragraphs",
            "Slides",
            "Notes",
            "TotalTime",
            "HiddenSlides",
            "MMClips",
            "ScaleCrop",
            "HeadingPairs",
            "TitlesOfParts",
            "LinksUpToDate",
            "CharactersWithSpaces",
            "SharedDoc",
            "HyperlinkBase",
            "HLinks",
            "HyperlinksChanged",
            "DigSig",
            "Application",
            "AppVersion",
            "DocSecurity",
        };

    private readonly WordDocumentPropertyGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordDocumentPropertyGraphBuilder(
        WordDocumentPropertyGraphOptions? options = null
    )
    {
        _options = options ?? WordDocumentPropertyGraphOptions.Default;
        _options.Validate();
    }

    public WordDocumentPropertyGraphBuilder(
        WordDocumentPropertyGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordDocumentPropertyGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordDocumentPropertyGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.DocumentProperties
        );

        var issues = new IssueState(_options.MaxIssues, _resourceLease);
        var candidates = DiscoverParts(package, issues, cancellationToken);
        var parts = new List<WordDocumentPropertyPart>(candidates.Count);
        var properties = new List<WordDocumentProperty>();
        var totalXmlBytes = 0;
        var totalValueCharacters = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(candidate.PartUri, out var part))
            {
                continue;
            }
            if (part.Entry.Content.Length > _options.MaxPartBytes)
            {
                throw new WordDocumentPropertyLimitException(
                    $"Document-property part exceeds {_options.MaxPartBytes} bytes."
                );
            }
            totalXmlBytes = checked(totalXmlBytes + part.Entry.Content.Length);
            if (totalXmlBytes > _options.MaxTotalXmlBytes)
            {
                throw new WordDocumentPropertyLimitException(
                    $"Document-property XML exceeds {_options.MaxTotalXmlBytes} aggregate bytes."
                );
            }

            var xml = ParsePart(part, cancellationToken);
            parts.Add(
                new WordDocumentPropertyPart(
                    candidate.Family,
                    part.Uri,
                    part.ContentType ?? string.Empty,
                    candidate.IsReachable,
                    xml.SourceSha256
                )
            );
            var parsed = candidate.Family switch
            {
                WordDocumentPropertyFamily.Core => ParseCore(
                    xml,
                    part.Uri,
                    candidate.IsReachable,
                    issues,
                    cancellationToken
                ),
                WordDocumentPropertyFamily.Extended => ParseExtended(
                    xml,
                    part.Uri,
                    candidate.IsReachable,
                    issues,
                    cancellationToken
                ),
                WordDocumentPropertyFamily.Custom => ParseCustom(
                    xml,
                    part.Uri,
                    candidate.IsReachable,
                    issues,
                    cancellationToken
                ),
                _ => throw new InvalidOperationException(
                    "Unsupported document-property family."
                ),
            };
            if (parsed.Count > _options.MaxPropertiesPerPart)
            {
                throw new WordDocumentPropertyLimitException(
                    $"Document-property count exceeds {_options.MaxPropertiesPerPart} in one part."
                );
            }
            foreach (var property in parsed)
            {
                totalValueCharacters = checked(
                    totalValueCharacters + property.ValueCharacterCount
                );
                if (totalValueCharacters > _options.MaxTotalValueCharacters)
                {
                    throw new WordDocumentPropertyLimitException(
                        $"Document-property values exceed {_options.MaxTotalValueCharacters} aggregate characters."
                    );
                }
                properties.Add(property);
                if (properties.Count > _options.MaxProperties)
                {
                    throw new WordDocumentPropertyLimitException(
                        $"Document-property count exceeds {_options.MaxProperties}."
                    );
                }
                ChargeProperty(property);
            }
        }

        var ambiguousFamilies = candidates.GroupBy(item => item.Family)
            .Where(item => item.Count() > 1)
            .Select(item => item.Key)
            .ToHashSet();
        var finalized = MarkUniqueNames(
            properties,
            ambiguousFamilies,
            issues,
            cancellationToken
        );
        var fieldProperties = BuildFieldIndex(finalized, issues, cancellationToken);
        return new WordDocumentPropertyGraph(
            package.Fingerprint,
            parts.OrderBy(item => item.Family).ThenBy(item => item.PartUri).ToArray(),
            finalized.OrderBy(item => item.Family)
                .ThenBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PartUri, StringComparer.Ordinal)
                .ThenBy(item => item.SourceElementOrdinal)
                .ToArray(),
            fieldProperties,
            issues.Items,
            issues.Truncated
        );
    }

    private IReadOnlyList<PartCandidate> DiscoverParts(
        OpcPackageSnapshot package,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var candidates = new Dictionary<string, PartCandidate>(StringComparer.Ordinal);
        foreach (var relationship in package.Relationships.Where(item => item.SourcePartUri == "/"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var family = RelationshipFamily(relationship.Type);
            if (family is null)
            {
                continue;
            }
            if (
                relationship.TargetMode != OpcRelationshipTargetMode.Internal
                || relationship.ResolvedTargetPartUri is null
                || !package.Parts.ContainsKey(relationship.ResolvedTargetPartUri)
            )
            {
                issues.Add(
                    "WDP001",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A document-property relationship has no internal resolved target."
                );
                continue;
            }
            var targetPart = package.Parts[relationship.ResolvedTargetPartUri];
            if (
                !string.Equals(
                    targetPart.ContentType,
                    FamilyContentType(family.Value),
                    StringComparison.Ordinal
                )
            )
            {
                issues.Add(
                    "WDP005",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A document-property relationship targets a part with the wrong content type.",
                    partUri: targetPart.Uri
                );
                continue;
            }
            if (candidates.ContainsKey(relationship.ResolvedTargetPartUri))
            {
                issues.Add(
                    "WDP006",
                    WordDocumentPropertyIssueSeverity.Error,
                    "Multiple document-property relationships target the same part.",
                    partUri: relationship.ResolvedTargetPartUri
                );
                continue;
            }
            candidates.Add(relationship.ResolvedTargetPartUri, new PartCandidate(
                family.Value,
                relationship.ResolvedTargetPartUri,
                IsReachable: true
            ));
        }

        foreach (var part in package.Parts.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var family = ContentTypeFamily(part.ContentType);
            if (family is null)
            {
                continue;
            }
            if (candidates.TryGetValue(part.Uri, out var existing))
            {
                if (existing.Family != family.Value)
                {
                    issues.Add(
                        "WDP002",
                        WordDocumentPropertyIssueSeverity.Error,
                        "A document-property part has contradictory relationship and content-type families.",
                        partUri: part.Uri
                    );
                }
                continue;
            }
            candidates.Add(part.Uri, new PartCandidate(family.Value, part.Uri, false));
            issues.Add(
                "WDP003",
                WordDocumentPropertyIssueSeverity.Warning,
                "A typed document-property part is not reached by its package relationship.",
                partUri: part.Uri
            );
        }

        foreach (var group in candidates.Values.GroupBy(item => item.Family))
        {
            if (group.Count() > 1)
            {
                issues.Add(
                    "WDP004",
                    WordDocumentPropertyIssueSeverity.Error,
                    "The package contains multiple parts for one document-property family."
                );
            }
        }
        return candidates.Values
            .OrderBy(item => item.Family)
            .ThenBy(item => item.PartUri, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<WordDocumentProperty> ParseCore(
        LosslessXmlDocument xml,
        string partUri,
        bool reachable,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var root = xml.ParsedDocument.Root;
        if (root?.Name != XName.Get("coreProperties", CorePropertiesNamespace))
        {
            throw new WordDocumentPropertyProjectionException(
                "The core-properties part has an invalid root element."
            );
        }
        var properties = new List<WordDocumentProperty>();
        foreach (var element in root.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = xml.GetElementOrdinal(element);
            if (!IsCorePropertyElement(element))
            {
                issues.Add(
                    "WDP010",
                    WordDocumentPropertyIssueSeverity.Info,
                    "The core-properties part contains an unmodeled element.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
                continue;
            }
            if (element.Elements().Any())
            {
                issues.Add(
                    "WDP011",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A core property is not a scalar leaf.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
                continue;
            }
            var value = element.Value;
            EnsureValueLimit(value);
            var canonical = element.Name.LocalName;
            var valueKind = CoreValueKind(canonical);
            var lexicallyValid = IsLexicallyValid(valueKind, value);
            if (!lexicallyValid)
            {
                issues.Add(
                    "WDP012",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A core property value does not match its declared scalar type.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
            }
            var typeAnnotationValid =
                canonical is not ("created" or "modified")
                || HasW3CDateTimeTypeAnnotation(element);
            if (!typeAnnotationValid)
            {
                issues.Add(
                    "WDP013",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A dcterms created/modified property does not declare xsi:type as dcterms:W3CDTF.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
            }
            properties.Add(
                CreateProperty(
                    WordDocumentPropertyFamily.Core,
                    canonical,
                    canonical,
                    valueKind,
                    value,
                    value.Length,
                    hasScalarValue: true,
                    isStructurallyValid: lexicallyValid && typeAnnotationValid,
                    propertyId: null,
                    formatId: null,
                    partUri,
                    ordinal,
                    reachable
                )
            );
        }
        return properties;
    }

    private IReadOnlyList<WordDocumentProperty> ParseExtended(
        LosslessXmlDocument xml,
        string partUri,
        bool reachable,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var root = xml.ParsedDocument.Root;
        if (
            root is null
            || root.Name.LocalName != "Properties"
            || root.Name.NamespaceName is not ExtendedPropertiesNamespace
                and not StrictExtendedPropertiesNamespace
        )
        {
            throw new WordDocumentPropertyProjectionException(
                "The extended-properties part has an invalid root element."
            );
        }
        var properties = new List<WordDocumentProperty>();
        foreach (var element in root.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = xml.GetElementOrdinal(element);
            if (element.Name.Namespace != root.Name.Namespace)
            {
                issues.Add(
                    "WDP020",
                    WordDocumentPropertyIssueSeverity.Info,
                    "The extended-properties part contains an unmodeled namespace.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
                continue;
            }
            var canonical = element.Name.LocalName;
            if (!ExtendedPropertyNames.Contains(canonical))
            {
                issues.Add(
                    "WDP021",
                    WordDocumentPropertyIssueSeverity.Info,
                    "The extended-properties part contains an unmodeled element.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
                continue;
            }
            var hasChildren = element.Elements().Any();
            var value = hasChildren ? null : element.Value;
            var characterCount = element.Value.Length;
            EnsureValueLimit(element.Value);
            var valueKind = hasChildren
                ? ComplexValueKind(element)
                : ExtendedValueKind(canonical);
            var lexicallyValid = hasChildren
                || IsLexicallyValid(valueKind, value!);
            if (!lexicallyValid)
            {
                issues.Add(
                    "WDP022",
                    WordDocumentPropertyIssueSeverity.Error,
                    "An extended property value does not match its declared scalar type.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
            }
            properties.Add(
                CreateProperty(
                    WordDocumentPropertyFamily.Extended,
                    canonical,
                    canonical,
                    valueKind,
                    value,
                    characterCount,
                    hasScalarValue: !hasChildren,
                    isStructurallyValid: lexicallyValid,
                    propertyId: null,
                    formatId: null,
                    partUri,
                    ordinal,
                    reachable
                )
            );
        }
        return properties;
    }

    private IReadOnlyList<WordDocumentProperty> ParseCustom(
        LosslessXmlDocument xml,
        string partUri,
        bool reachable,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var root = xml.ParsedDocument.Root;
        if (
            root is null
            || root.Name.LocalName != "Properties"
            || root.Name.NamespaceName is not CustomPropertiesNamespace
                and not StrictCustomPropertiesNamespace
        )
        {
            throw new WordDocumentPropertyProjectionException(
                "The custom-properties part has an invalid root element."
            );
        }
        var properties = new List<WordDocumentProperty>();
        foreach (var element in root.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = xml.GetElementOrdinal(element);
            if (
                element.Name.LocalName != "property"
                || element.Name.Namespace != root.Name.Namespace
            )
            {
                issues.Add(
                    "WDP030",
                    WordDocumentPropertyIssueSeverity.Info,
                    "The custom-properties part contains an unmodeled element.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
                continue;
            }
            var name = Attribute(element, "name")?.Trim();
            var formatId = Attribute(element, "fmtid")?.Trim();
            var pidText = Attribute(element, "pid")?.Trim();
            var pid = int.TryParse(
                pidText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedPid
            )
                ? parsedPid
                : (int?)null;
            var hasValidName = !string.IsNullOrWhiteSpace(name);
            var hasValidPid = pid is >= 2;
            var hasStandardFormatId = string.Equals(
                formatId,
                CustomPropertyFormatId,
                StringComparison.OrdinalIgnoreCase
            );
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "(missing-name)";
                issues.Add(
                    "WDP031",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A custom property has no usable name.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
            }
            else if (name.Length > _options.MaxNameCharacters)
            {
                throw new WordDocumentPropertyLimitException(
                    $"A document-property name exceeds {_options.MaxNameCharacters} characters."
                );
            }
            if (formatId is { Length: > 256 })
            {
                throw new WordDocumentPropertyLimitException(
                    "A document-property format identifier exceeds 256 characters."
                );
            }
            if (!hasValidPid)
            {
                issues.Add(
                    "WDP032",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A custom property has an invalid property ID.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
            }
            if (!hasStandardFormatId)
            {
                issues.Add(
                    "WDP033",
                    WordDocumentPropertyIssueSeverity.Warning,
                    "A custom property has a nonstandard format identifier.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
            }

            var values = element.Elements().ToArray();
            WordDocumentPropertyValueKind kind;
            string? value;
            int characterCount;
            var scalar = false;
            if (values.Length != 1)
            {
                kind = WordDocumentPropertyValueKind.Unknown;
                value = null;
                characterCount = values.Sum(item => item.Value.Length);
                issues.Add(
                    "WDP034",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A custom property does not contain exactly one typed value.",
                    partUri: partUri,
                    sourceElementOrdinal: ordinal
                );
            }
            else
            {
                var valueElement = values[0];
                characterCount = valueElement.Value.Length;
                kind = VariantValueKind(valueElement);
                scalar = IsScalarVariant(kind) && !valueElement.Elements().Any();
                value = scalar ? valueElement.Value : null;
                if (kind == WordDocumentPropertyValueKind.Unknown)
                {
                    issues.Add(
                        "WDP035",
                        WordDocumentPropertyIssueSeverity.Warning,
                        "A custom property uses an unknown typed value.",
                        partUri: partUri,
                        sourceElementOrdinal: xml.GetElementOrdinal(valueElement)
                    );
                }
                else if (scalar && !IsLexicallyValid(kind, value!))
                {
                    issues.Add(
                        "WDP036",
                        WordDocumentPropertyIssueSeverity.Error,
                        "A custom property value does not match its declared scalar type.",
                        partUri: partUri,
                        sourceElementOrdinal: xml.GetElementOrdinal(valueElement)
                    );
                }
            }
            EnsureValueLimit(characterCount);
            properties.Add(
                CreateProperty(
                    WordDocumentPropertyFamily.Custom,
                    name,
                    name,
                    kind,
                    value,
                    characterCount,
                    scalar,
                    hasValidName
                        && hasValidPid
                        && hasStandardFormatId
                        && values.Length == 1
                        && kind != WordDocumentPropertyValueKind.Unknown
                        && (!scalar || IsLexicallyValid(kind, value!)),
                    pid,
                    formatId,
                    partUri,
                    ordinal,
                    reachable
                )
            );
        }
        return properties;
    }

    private IReadOnlyList<WordDocumentProperty> MarkUniqueNames(
        IReadOnlyList<WordDocumentProperty> properties,
        IReadOnlySet<WordDocumentPropertyFamily> ambiguousFamilies,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var counts = new Dictionary<(WordDocumentPropertyFamily, string), int>(
            new FamilyNameComparer()
        );
        var customPropertyIdCounts = properties
            .Where(item =>
                item.Family == WordDocumentPropertyFamily.Custom
                && item.PropertyId is not null
            )
            .GroupBy(item => item.PropertyId!.Value)
            .ToDictionary(item => item.Key, item => item.Count());
        foreach (var property in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (property.Family, property.CanonicalName);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }
        var result = new List<WordDocumentProperty>(properties.Count);
        foreach (var property in properties)
        {
            var unique = counts[(property.Family, property.CanonicalName)] == 1;
            var hasUniquePropertyId = property.Family
                    != WordDocumentPropertyFamily.Custom
                || property.PropertyId is null
                || customPropertyIdCounts[property.PropertyId.Value] == 1;
            result.Add(
                property with
                {
                    IsUniquelyNamed = unique,
                    IsStructurallyValid = property.IsStructurallyValid
                        && hasUniquePropertyId
                        && !ambiguousFamilies.Contains(property.Family),
                }
            );
            if (!unique)
            {
                issues.Add(
                    "WDP040",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A document-property name is duplicated within its family.",
                    property.Id,
                    property.PartUri,
                    property.SourceElementOrdinal
                );
            }
            if (!hasUniquePropertyId)
            {
                issues.Add(
                    "WDP042",
                    WordDocumentPropertyIssueSeverity.Error,
                    "A custom property ID is duplicated.",
                    property.Id,
                    property.PartUri,
                    property.SourceElementOrdinal
                );
            }
        }
        return result;
    }

    private IReadOnlyDictionary<string, WordDocumentProperty> BuildFieldIndex(
        IReadOnlyList<WordDocumentProperty> properties,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var candidates = new Dictionary<string, List<WordDocumentProperty>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (
            var property in properties.Where(item =>
                item.IsUniquelyNamed
                && item.IsStructurallyValid
                && item.HasScalarValue
                && item.IsPackageReachable
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var alias in FieldAliases(property))
            {
                if (!candidates.TryGetValue(alias, out var items))
                {
                    items = [];
                    candidates.Add(alias, items);
                }
                if (!items.Any(item => item.Id == property.Id))
                {
                    items.Add(property);
                }
            }
        }
        var index = new Dictionary<string, WordDocumentProperty>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var pair in candidates)
        {
            if (pair.Value.Count == 1)
            {
                index.Add(pair.Key, pair.Value[0]);
            }
            else
            {
                issues.Add(
                    "WDP041",
                    WordDocumentPropertyIssueSeverity.Warning,
                    "A field property name is ambiguous across property families."
                );
            }
        }
        return index;
    }

    private static IEnumerable<string> FieldAliases(WordDocumentProperty property)
    {
        yield return property.Name;
        yield return property.CanonicalName;
        if (
            property.Family == WordDocumentPropertyFamily.Core
            && CoreFieldAliases.TryGetValue(property.CanonicalName, out var coreAlias)
        )
        {
            yield return coreAlias;
        }
        if (
            property.Family == WordDocumentPropertyFamily.Extended
            && ExtendedFieldAliases.TryGetValue(
                property.CanonicalName,
                out var extendedAlias
            )
        )
        {
            yield return extendedAlias;
        }
    }

    private LosslessXmlDocument ParsePart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxPartBytes,
                MaxXmlCharacters = _options.MaxPartBytes,
                MaxXmlElements = _options.MaxPropertiesPerPart * 4,
                MaxXmlDepth = 64,
                MaxTextCharacters = _options.MaxTotalValueCharacters,
            };
            return _resourceLease is null
                ? LosslessXmlDocument.Parse(part.Entry.Content, options, cancellationToken)
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.DocumentProperties,
                    cancellationToken
                );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordDocumentPropertyLimitException(
                "A document-property XML part exceeds its bounded parser limits.",
                exception
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordDocumentPropertyProjectionException(
                "A document-property part is not safe, well-formed XML.",
                exception
            );
        }
    }

    private void EnsureValueLimit(string value) => EnsureValueLimit(value.Length);

    private void EnsureValueLimit(int characters)
    {
        if (characters > _options.MaxValueCharacters)
        {
            throw new WordDocumentPropertyLimitException(
                $"A document-property value exceeds {_options.MaxValueCharacters} characters."
            );
        }
    }

    private void ChargeProperty(WordDocumentProperty property)
    {
        if (_resourceLease is null)
        {
            return;
        }
        long bytes = 512;
        foreach (
            var value in new[]
            {
                property.Id,
                property.Name,
                property.CanonicalName,
                property.Value,
                property.FormatId,
                property.PartUri,
            }
        )
        {
            bytes = checked(
                bytes + WordOperationResourceAccounting.AccountedStringBytes(value)
            );
        }
        _resourceLease.Charge(WordOperationResourceStage.DocumentProperties, bytes);
    }

    private static WordDocumentProperty CreateProperty(
        WordDocumentPropertyFamily family,
        string name,
        string canonicalName,
        WordDocumentPropertyValueKind valueKind,
        string? value,
        int valueCharacterCount,
        bool hasScalarValue,
        bool isStructurallyValid,
        int? propertyId,
        string? formatId,
        string partUri,
        int sourceElementOrdinal,
        bool reachable
    ) => new(
        StableId(family, canonicalName, partUri, sourceElementOrdinal),
        family,
        name,
        canonicalName,
        valueKind,
        value,
        valueCharacterCount,
        hasScalarValue,
        IsUniquelyNamed: false,
        isStructurallyValid,
        propertyId,
        formatId,
        partUri,
        sourceElementOrdinal,
        reachable
    );

    private static string StableId(
        WordDocumentPropertyFamily family,
        string name,
        string partUri,
        int ordinal
    )
    {
        var source = string.Join(
            '\u001f',
            family.ToString(),
            name,
            partUri,
            ordinal.ToString(CultureInfo.InvariantCulture)
        );
        return "wdp_"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))
                .ToLowerInvariant()[..24];
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attribute(localName)?.Value;

    private static bool IsCorePropertyElement(XElement element) =>
        element.Name switch
        {
            var name when name == XName.Get("title", DcNamespace) => true,
            var name when name == XName.Get("subject", DcNamespace) => true,
            var name when name == XName.Get("creator", DcNamespace) => true,
            var name when name == XName.Get("keywords", CorePropertiesNamespace) => true,
            var name when name == XName.Get("description", DcNamespace) => true,
            var name when name == XName.Get("lastModifiedBy", CorePropertiesNamespace) => true,
            var name when name == XName.Get("revision", CorePropertiesNamespace) => true,
            var name when name == XName.Get("lastPrinted", CorePropertiesNamespace) => true,
            var name when name == XName.Get("created", DcTermsNamespace) => true,
            var name when name == XName.Get("modified", DcTermsNamespace) => true,
            var name when name == XName.Get("category", CorePropertiesNamespace) => true,
            var name when name == XName.Get("contentStatus", CorePropertiesNamespace) => true,
            var name when name == XName.Get("contentType", CorePropertiesNamespace) => true,
            var name when name == XName.Get("identifier", DcNamespace) => true,
            var name when name == XName.Get("language", DcNamespace) => true,
            var name when name == XName.Get("version", CorePropertiesNamespace) => true,
            _ => false,
        };

    private static WordDocumentPropertyValueKind CoreValueKind(string name) =>
        name switch
        {
            "created" or "modified" or "lastPrinted" =>
                WordDocumentPropertyValueKind.DateTime,
            "revision" => WordDocumentPropertyValueKind.Integer,
            _ => WordDocumentPropertyValueKind.Text,
        };

    private static WordDocumentPropertyValueKind ExtendedValueKind(string name) =>
        ExtendedIntegerProperties.Contains(name)
            ? WordDocumentPropertyValueKind.Integer
            : ExtendedBooleanProperties.Contains(name)
                ? WordDocumentPropertyValueKind.Boolean
                : WordDocumentPropertyValueKind.Text;

    private static WordDocumentPropertyValueKind ComplexValueKind(XElement element) =>
        element.Descendants().Any(item => item.Name.LocalName == "vector")
            ? WordDocumentPropertyValueKind.Vector
            : WordDocumentPropertyValueKind.Unknown;

    private static WordDocumentPropertyValueKind VariantValueKind(XElement element)
    {
        if (
            element.Name.NamespaceName is not VariantTypesNamespace
                and not StrictVariantTypesNamespace
        )
        {
            return WordDocumentPropertyValueKind.Unknown;
        }
        return element.Name.LocalName switch
        {
            "lpstr" or "lpwstr" or "bstr" => WordDocumentPropertyValueKind.Text,
            "i1" or "i2" or "i4" or "i8" or "int" =>
                WordDocumentPropertyValueKind.Integer,
            "ui1" or "ui2" or "ui4" or "ui8" or "uint" =>
                WordDocumentPropertyValueKind.UnsignedInteger,
            "r4" or "r8" => WordDocumentPropertyValueKind.FloatingPoint,
            "decimal" => WordDocumentPropertyValueKind.Decimal,
            "bool" => WordDocumentPropertyValueKind.Boolean,
            "date" or "filetime" => WordDocumentPropertyValueKind.DateTime,
            "cy" => WordDocumentPropertyValueKind.Currency,
            "error" => WordDocumentPropertyValueKind.ErrorCode,
            "clsid" => WordDocumentPropertyValueKind.ClassId,
            "blob" or "oblob" or "stream" or "ostream" or "storage" or "ostorage"
                or "cf"
                or "vstream" => WordDocumentPropertyValueKind.Binary,
            "vector" => WordDocumentPropertyValueKind.Vector,
            "array" => WordDocumentPropertyValueKind.Array,
            "variant" => WordDocumentPropertyValueKind.Variant,
            "empty" or "null" => WordDocumentPropertyValueKind.Empty,
            _ => WordDocumentPropertyValueKind.Unknown,
        };
    }

    private static bool IsScalarVariant(WordDocumentPropertyValueKind kind) =>
        kind is not WordDocumentPropertyValueKind.Binary
            and not WordDocumentPropertyValueKind.Vector
            and not WordDocumentPropertyValueKind.Array
            and not WordDocumentPropertyValueKind.Variant
            and not WordDocumentPropertyValueKind.Unknown;

    private static bool IsLexicallyValid(
        WordDocumentPropertyValueKind kind,
        string value
    )
    {
        var normalized = value.Trim();
        return kind switch
        {
            WordDocumentPropertyValueKind.Integer => BigInteger.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _
            ),
            WordDocumentPropertyValueKind.UnsignedInteger => BigInteger.TryParse(
                normalized,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var unsigned
            ) && unsigned >= BigInteger.Zero,
            WordDocumentPropertyValueKind.FloatingPoint =>
                IsXmlFloatingPoint(normalized),
            WordDocumentPropertyValueKind.Decimal
                or WordDocumentPropertyValueKind.Currency => decimal.TryParse(
                    normalized,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out _
                ),
            WordDocumentPropertyValueKind.Boolean =>
                normalized is "true" or "false" or "1" or "0",
            WordDocumentPropertyValueKind.DateTime => IsXmlDateTime(normalized),
            WordDocumentPropertyValueKind.ClassId => Guid.TryParse(normalized, out _),
            WordDocumentPropertyValueKind.Empty => normalized.Length == 0,
            _ => true,
        };
    }

    private static bool IsXmlDateTime(string value)
    {
        try
        {
            _ = XmlConvert.ToDateTimeOffset(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool HasW3CDateTimeTypeAnnotation(XElement element)
    {
        var rawType = element
            .Attribute(XName.Get("type", XmlSchemaInstanceNamespace))
            ?.Value.Trim();
        if (string.IsNullOrEmpty(rawType))
        {
            return false;
        }
        var separator = rawType.IndexOf(':');
        if (separator == -1)
        {
            return rawType == "W3CDTF"
                && element.GetDefaultNamespace().NamespaceName == DcTermsNamespace;
        }
        if (separator == 0 || separator == rawType.Length - 1)
        {
            return false;
        }
        var prefix = rawType[..separator];
        var localName = rawType[(separator + 1)..];
        return localName == "W3CDTF"
            && element.GetNamespaceOfPrefix(prefix)?.NamespaceName == DcTermsNamespace;
    }

    private static bool IsXmlFloatingPoint(string value) =>
        value is "INF" or "-INF" or "NaN"
        || double.TryParse(
            value,
            NumberStyles.AllowLeadingSign
                | NumberStyles.AllowDecimalPoint
                | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out _
        );

    private static WordDocumentPropertyFamily? RelationshipFamily(string type) =>
        type switch
        {
            CorePropertiesRelationship => WordDocumentPropertyFamily.Core,
            ExtendedPropertiesRelationship or StrictExtendedPropertiesRelationship =>
                WordDocumentPropertyFamily.Extended,
            CustomPropertiesRelationship or StrictCustomPropertiesRelationship =>
                WordDocumentPropertyFamily.Custom,
            _ => null,
        };

    private static WordDocumentPropertyFamily? ContentTypeFamily(
        string? contentType
    ) => contentType switch
    {
        CorePropertiesContentType => WordDocumentPropertyFamily.Core,
        ExtendedPropertiesContentType => WordDocumentPropertyFamily.Extended,
        CustomPropertiesContentType => WordDocumentPropertyFamily.Custom,
        _ => null,
    };

    private static string FamilyContentType(WordDocumentPropertyFamily family) =>
        family switch
        {
            WordDocumentPropertyFamily.Core => CorePropertiesContentType,
            WordDocumentPropertyFamily.Extended => ExtendedPropertiesContentType,
            WordDocumentPropertyFamily.Custom => CustomPropertiesContentType,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

    private sealed record PartCandidate(
        WordDocumentPropertyFamily Family,
        string PartUri,
        bool IsReachable
    );

    private sealed class FamilyNameComparer
        : IEqualityComparer<(WordDocumentPropertyFamily Family, string Name)>
    {
        public bool Equals(
            (WordDocumentPropertyFamily Family, string Name) left,
            (WordDocumentPropertyFamily Family, string Name) right
        ) => left.Family == right.Family
            && StringComparer.OrdinalIgnoreCase.Equals(left.Name, right.Name);

        public int GetHashCode(
            (WordDocumentPropertyFamily Family, string Name) value
        ) => HashCode.Combine(
            value.Family,
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name)
        );
    }

    private sealed class IssueState
    {
        private readonly int _maximum;
        private readonly WordOperationResourceLease? _resourceLease;
        private readonly List<WordDocumentPropertyIssue> _items = [];

        public IssueState(int maximum, WordOperationResourceLease? resourceLease)
        {
            _maximum = maximum;
            _resourceLease = resourceLease;
        }

        public IReadOnlyList<WordDocumentPropertyIssue> Items => _items;

        public bool Truncated { get; private set; }

        public void Add(
            string code,
            WordDocumentPropertyIssueSeverity severity,
            string message,
            string? propertyId = null,
            string? partUri = null,
            int? sourceElementOrdinal = null
        )
        {
            if (_items.Count >= _maximum)
            {
                Truncated = true;
                return;
            }
            if (_resourceLease is not null)
            {
                long bytes = 384;
                foreach (var value in new[] { code, message, propertyId, partUri })
                {
                    bytes = checked(
                        bytes
                            + WordOperationResourceAccounting.AccountedStringBytes(value)
                    );
                }
                _resourceLease.Charge(
                    WordOperationResourceStage.DocumentProperties,
                    bytes
                );
            }
            _items.Add(
                new WordDocumentPropertyIssue(
                    code,
                    severity,
                    message,
                    propertyId,
                    partUri,
                    sourceElementOrdinal
                )
            );
        }
    }
}

public class WordDocumentPropertyProjectionException : IOException
{
    public WordDocumentPropertyProjectionException(string message)
        : base(message)
    { }

    public WordDocumentPropertyProjectionException(
        string message,
        Exception innerException
    )
        : base(message, innerException)
    { }
}

public sealed class WordDocumentPropertyLimitException
    : WordDocumentPropertyProjectionException
{
    public WordDocumentPropertyLimitException(string message)
        : base(message)
    { }

    public WordDocumentPropertyLimitException(string message, Exception innerException)
        : base(message, innerException)
    { }
}
