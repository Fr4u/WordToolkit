using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordMailMergeIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordMailMergeRelationshipRole
{
    DataSource,
    HeaderSource,
    OdsoSource,
    RecipientData,
}

public enum WordMailMergeFieldBindingStatus
{
    NotApplicable,
    ResolvedBySourceColumnName,
    ResolvedByWordPredefinedName,
    Ambiguous,
    Missing,
}

public enum WordMailMergeRecipientIdentityKind
{
    Missing,
    UniqueTag,
    Hash,
    Ambiguous,
}

public sealed record WordMailMergeIssue(
    string Code,
    WordMailMergeIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? SubjectId = null
);

public sealed record WordMailMergeRelationship(
    string Id,
    WordMailMergeRelationshipRole Role,
    string SourcePartUri,
    int SourceElementOrdinal,
    string RelationshipId,
    string? RelationshipType,
    string? Target,
    OpcRelationshipTargetMode? TargetMode,
    string? ResolvedTargetPartUri,
    bool RelationshipExists,
    bool RelationshipTypeValid,
    bool TargetExists,
    bool IsResolved
)
{
    public bool IsExternal => TargetMode == OpcRelationshipTargetMode.External;
}

public sealed record WordMailMergeFieldMapping(
    string Id,
    int Position,
    string? FieldType,
    string? SourceColumnName,
    string? DeclaredMappedName,
    string? WordEffectivePredefinedName,
    int? ColumnIndex,
    string? LanguageId,
    bool DynamicAddress,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledElements
);

public sealed record WordMailMergeDataSourceObject(
    string Id,
    int SourceElementOrdinal,
    string? UdlConnectionString,
    string? TableName,
    int? ColumnDelimiter,
    string? SourceType,
    bool FirstRowIsHeader,
    WordMailMergeRelationship? SourceRelationship,
    WordMailMergeRelationship? RecipientDataRelationship,
    IReadOnlyList<string> MappingIds,
    IReadOnlyList<string> UnmodeledElements
);

public sealed record WordMailMergeConfiguration(
    string Id,
    string SettingsPartUri,
    int SourceElementOrdinal,
    string? MainDocumentType,
    string? DataType,
    string? Destination,
    bool LinkToQuery,
    bool DoNotSuppressBlankLines,
    bool MailAsAttachment,
    bool ViewMergedData,
    int? ActiveRecord,
    string? CheckErrors,
    string? Query,
    string? ConnectionString,
    string? AddressFieldName,
    string? MailSubject,
    WordMailMergeRelationship? DataSourceRelationship,
    WordMailMergeRelationship? HeaderSourceRelationship,
    WordMailMergeDataSourceObject? DataSourceObject,
    IReadOnlyList<string> UnmodeledElements
)
{
    public bool HasExternalDataSource =>
        DataSourceRelationship?.IsExternal == true
        || DataSourceObject?.SourceRelationship?.IsExternal == true;

    public bool HasSensitiveConnectionMetadata =>
        Query is not null
        || ConnectionString is not null
        || DataSourceObject?.UdlConnectionString is not null;
}

public sealed record WordMailMergeRecipientDataPart(
    string Id,
    string PartUri,
    string? ContentType,
    string NamespaceUri,
    int SourceElementOrdinal,
    bool IsPackageReachable,
    int IncomingRelationshipCount,
    IReadOnlyList<string> RecipientIds
);

public sealed record WordMailMergeRecipient(
    string Id,
    string PartUri,
    int Sequence,
    bool IsIncluded,
    int? ColumnIndex,
    WordMailMergeRecipientIdentityKind IdentityKind,
    string? IdentityValue,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledElements
);

public sealed record WordMailMergeField(
    string Id,
    string ReferenceFieldId,
    string FieldType,
    string StoryId,
    string PartUri,
    int SourceElementOrdinal,
    SemanticNodeId? SemanticNodeId,
    bool IsComplete,
    bool IsInDeletedContent,
    string? TargetName,
    WordMailMergeFieldBindingStatus BindingStatus,
    IReadOnlyList<string> MappingIds
);

public sealed class WordMailMergeGraph
{
    private readonly IReadOnlyDictionary<string, WordMailMergeFieldMapping> _mappings;
    private readonly IReadOnlyDictionary<string, WordMailMergeField> _fields;

    internal WordMailMergeGraph(
        string packageFingerprint,
        WordMailMergeConfiguration? configuration,
        IReadOnlyList<WordMailMergeFieldMapping> mappings,
        WordMailMergeRecipientDataPart? recipientDataPart,
        IReadOnlyList<WordMailMergeRecipient> recipients,
        IReadOnlyList<WordMailMergeField> fields,
        IReadOnlyList<WordMailMergeIssue> issues,
        bool issuesTruncated
    )
    {
        PackageFingerprint = packageFingerprint;
        Configuration = configuration;
        Mappings = new ReadOnlyCollection<WordMailMergeFieldMapping>(mappings.ToArray());
        RecipientDataPart = recipientDataPart;
        Recipients = new ReadOnlyCollection<WordMailMergeRecipient>(recipients.ToArray());
        Fields = new ReadOnlyCollection<WordMailMergeField>(fields.ToArray());
        Issues = new ReadOnlyCollection<WordMailMergeIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        _mappings = new ReadOnlyDictionary<string, WordMailMergeFieldMapping>(
            mappings.ToDictionary(item => item.Id, StringComparer.Ordinal)
        );
        _fields = new ReadOnlyDictionary<string, WordMailMergeField>(
            fields.ToDictionary(item => item.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public WordMailMergeConfiguration? Configuration { get; }

    public IReadOnlyList<WordMailMergeFieldMapping> Mappings { get; }

    public WordMailMergeRecipientDataPart? RecipientDataPart { get; }

    public IReadOnlyList<WordMailMergeRecipient> Recipients { get; }

    public IReadOnlyList<WordMailMergeField> Fields { get; }

    public IReadOnlyList<WordMailMergeIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public bool HasMailMergeEvidence => Configuration is not null || Fields.Count != 0;

    public bool TryGetMapping(string id, out WordMailMergeFieldMapping? mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _mappings.TryGetValue(id, out mapping);
    }

    public bool TryGetField(string id, out WordMailMergeField? field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _fields.TryGetValue(id, out field);
    }
}

public sealed record WordMailMergeGraphOptions
{
    public static WordMailMergeGraphOptions Default { get; } = new();

    public int MaxSettingsPartBytes { get; init; } = 16 * 1024 * 1024;

    public int MaxRecipientDataPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxRecipientXmlElements { get; init; } = 1_000_000;

    public int MaxRecipientXmlDepth { get; init; } = 256;

    public int MaxMappings { get; init; } = 4_096;

    public int MaxRecipients { get; init; } = 250_000;

    public int MaxFields { get; init; } = 250_000;

    public int MaxValueCharacters { get; init; } = 32_768;

    public long MaxMetadataCharacters { get; init; } = 64L * 1024 * 1024;

    public int MaxIssues { get; init; } = 10_000;

    internal void Validate()
    {
        if (
            MaxSettingsPartBytes <= 0
            || MaxRecipientDataPartBytes <= 0
            || MaxRecipientXmlElements <= 0
            || MaxRecipientXmlDepth <= 0
            || MaxMappings <= 0
            || MaxRecipients <= 0
            || MaxFields <= 0
            || MaxValueCharacters <= 0
            || MaxMetadataCharacters <= 0
            || MaxIssues <= 0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(WordMailMergeGraphOptions),
                "All mail-merge graph limits must be positive."
            );
        }
    }
}

public sealed class WordMailMergeGraphBuilder
{
    public const string TransitionalWordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    public const string LegacyRecipientNamespace =
        "http://schemas.microsoft.com/office/word/2006/wordml";
    public const string TransitionalRelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string StrictRelationshipsNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    public const string TransitionalMailMergeSourceRelationship =
        TransitionalRelationshipsNamespace + "/mailMergeSource";
    public const string StrictMailMergeSourceRelationship =
        StrictRelationshipsNamespace + "/mailMergeSource";
    public const string TransitionalRecipientDataRelationship =
        TransitionalRelationshipsNamespace + "/recipientData";
    public const string StrictRecipientDataRelationship =
        StrictRelationshipsNamespace + "/recipientData";
    public const string OpenXmlRecipientDataContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.mailMergeRecipientData+xml";
    public const string LegacyRecipientDataContentType =
        "application/vnd.ms-word.mailMergeRecipientData+xml";

    private static readonly string[] WordPredefinedMappingNames =
    [
        "Unique", "CourtesyTitle", "FirstName", "MiddleName", "LastName", "Suffix",
        "Nickname", "JobTitle", "Company", "Address1", "Address2", "City", "State",
        "PostalCode", "CountryorRegion", "BusinessPhone", "BusinessFax", "HomePhone",
        "HomeFax", "EmailAddress", "WebPage", "SpouseCourtesyTitle",
        "SpouseFirstName", "SpouseMiddleName", "SpouseLastName", "SpouseNickname",
        "RubyFirstName", "RubyLastName", "Address3", "Department",
    ];

    private static readonly HashSet<string> KnownMailMergeChildren = new(StringComparer.Ordinal)
    {
        "activeRecord", "addressFieldName", "checkErrors", "connectString", "dataSource",
        "dataType", "destination", "doNotSuppressBlankLines", "headerSource", "linkToQuery",
        "mailAsAttachment", "mailSubject", "mainDocumentType", "odso", "query",
        "viewMergedData",
    };

    private static readonly HashSet<string> KnownOdsoChildren = new(StringComparer.Ordinal)
    {
        "udl", "table", "src", "colDelim", "type", "fHdr", "fieldMapData",
        "recipientData",
    };

    private static readonly HashSet<string> KnownMappingChildren = new(StringComparer.Ordinal)
    {
        "type", "name", "mappedName", "column", "lid", "dynamicAddress",
    };

    private readonly WordMailMergeGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordMailMergeGraphBuilder(WordMailMergeGraphOptions? options = null)
    {
        _options = options ?? WordMailMergeGraphOptions.Default;
        _options.Validate();
    }

    public WordMailMergeGraphBuilder(
        WordMailMergeGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordMailMergeGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordMailMergeGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        var settings = (_resourceLease is null
            ? new WordSettingsGraphBuilder()
            : new WordSettingsGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var references = (_resourceLease is null
            ? new WordReferenceGraphBuilder()
            : new WordReferenceGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            cancellationToken
        );
        return Build(package, semanticDocument, settings, references, cancellationToken);
    }

    public WordMailMergeGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordSettingsGraph settings,
        WordReferenceGraph references,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(references);
        EnsureFingerprint(
            package.Fingerprint,
            semanticDocument.PackageFingerprint,
            settings.PackageFingerprint,
            references.PackageFingerprint
        );
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.MailMerge
        );

        var issues = new IssueState(_options.MaxIssues);
        var mappings = new List<WordMailMergeFieldMapping>();
        WordMailMergeConfiguration? configuration = null;
        WordMailMergeRecipientDataPart? recipientPart = null;
        var recipients = new List<WordMailMergeRecipient>();
        long metadataCharacters = 0;

        if (settings.SettingsPartUri is { } settingsPartUri)
        {
            if (!package.Parts.TryGetValue(settingsPartUri, out var settingsPart))
            {
                issues.Add(new WordMailMergeIssue(
                    "MAIL_MERGE_SETTINGS_PART_MISSING",
                    WordMailMergeIssueSeverity.Error,
                    "The settings graph identifies a mail-merge settings part that is absent from the package.",
                    settingsPartUri
                ));
            }
            else
            {
                var source = ParsePart(
                    settingsPart,
                    _options.MaxSettingsPartBytes,
                    WordOperationResourceStage.MailMerge,
                    cancellationToken
                );
                var root = source.GetParsedElement(source.Root.Ordinal);
                var w = root.Name.Namespace;
                if (!IsWordNamespace(w.NamespaceName) || root.Name.LocalName != "settings")
                {
                    throw new WordMailMergeProjectionException(
                        "The settings part root is not a supported WordprocessingML settings element."
                    );
                }
                var mailMergeElements = root.Elements(w + "mailMerge").ToArray();
                if (mailMergeElements.Length > 1)
                {
                    issues.Add(new WordMailMergeIssue(
                        "MAIL_MERGE_CONFIGURATION_DUPLICATE",
                        WordMailMergeIssueSeverity.Error,
                        "The settings part contains more than one mail-merge configuration.",
                        settingsPartUri
                    ));
                }
                if (mailMergeElements.FirstOrDefault() is { } mailMerge)
                {
                    configuration = ParseConfiguration(
                        package,
                        settingsPartUri,
                        source,
                        mailMerge,
                        w,
                        mappings,
                        ref metadataCharacters,
                        issues,
                        cancellationToken
                    );
                    var recipientRelationship =
                        configuration.DataSourceObject?.RecipientDataRelationship;
                    if (recipientRelationship?.ResolvedTargetPartUri is { } recipientPartUri)
                    {
                        (recipientPart, recipients) = ParseRecipientPart(
                            package,
                            recipientPartUri,
                            recipientRelationship,
                            ref metadataCharacters,
                            issues,
                            cancellationToken
                        );
                    }
                }
            }
        }

        var fields = ParseFields(
            references,
            configuration,
            mappings,
            ref metadataCharacters,
            issues,
            cancellationToken
        );
        if (configuration is null && fields.Count != 0)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_FIELDS_WITHOUT_CONFIGURATION",
                WordMailMergeIssueSeverity.Warning,
                "The document contains mail-merge fields but no mail-merge configuration."
            ));
        }
        if (configuration is not null && fields.Count == 0)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_CONFIGURATION_WITHOUT_FIELDS",
                WordMailMergeIssueSeverity.Info,
                "The document has mail-merge settings but no parsed mail-merge fields."
            ));
        }

        ChargeGraph(configuration, mappings, recipientPart, recipients, fields, issues.Items);
        return new WordMailMergeGraph(
            package.Fingerprint,
            configuration,
            mappings,
            recipientPart,
            recipients,
            fields,
            issues.Items,
            issues.Truncated
        );
    }

    private WordMailMergeConfiguration ParseConfiguration(
        OpcPackageSnapshot package,
        string settingsPartUri,
        LosslessXmlDocument source,
        XElement element,
        XNamespace w,
        List<WordMailMergeFieldMapping> mappings,
        ref long metadataCharacters,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var ordinal = source.GetElementOrdinal(element);
        var configurationId = StableId("wmmc_", package.Fingerprint, settingsPartUri, ordinal);
        var dataSource = ParseRelationshipChild(
            package,
            settingsPartUri,
            source,
            element,
            w,
            "dataSource",
            WordMailMergeRelationshipRole.DataSource,
            MailMergeRelationshipTypes,
            issues
        );
        var headerSource = ParseRelationshipChild(
            package,
            settingsPartUri,
            source,
            element,
            w,
            "headerSource",
            WordMailMergeRelationshipRole.HeaderSource,
            MailMergeRelationshipTypes,
            issues
        );
        WordMailMergeDataSourceObject? odso = null;
        var odsoElements = element.Elements(w + "odso").ToArray();
        if (odsoElements.Length > 1)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_ODSO_DUPLICATE",
                WordMailMergeIssueSeverity.Error,
                "The mail-merge configuration contains more than one ODSO element.",
                settingsPartUri,
                ordinal,
                configurationId
            ));
        }
        if (odsoElements.FirstOrDefault() is { } odsoElement)
        {
            odso = ParseOdso(
                package,
                settingsPartUri,
                source,
                odsoElement,
                w,
                mappings,
                ref metadataCharacters,
                issues,
                cancellationToken
            );
        }
        var unmodeled = UnmodeledChildren(element, w, KnownMailMergeChildren);
        ChargeMetadata(unmodeled, ref metadataCharacters);
        var mainDocumentType = ChildValue(element, w, "mainDocumentType", ref metadataCharacters);
        var dataType = ChildValue(element, w, "dataType", ref metadataCharacters);
        if (mainDocumentType is null)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_MAIN_DOCUMENT_TYPE_MISSING",
                WordMailMergeIssueSeverity.Warning,
                "The mail-merge configuration omits its main document type.",
                settingsPartUri,
                ordinal,
                configurationId
            ));
        }
        if (dataSource?.IsExternal == true || odso?.SourceRelationship?.IsExternal == true)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_EXTERNAL_DATA_SOURCE",
                WordMailMergeIssueSeverity.Warning,
                "The mail-merge configuration refers to an external data source that was not opened or followed.",
                settingsPartUri,
                ordinal,
                configurationId
            ));
        }
        return new WordMailMergeConfiguration(
            configurationId,
            settingsPartUri,
            ordinal,
            mainDocumentType,
            dataType,
            ChildValue(element, w, "destination", ref metadataCharacters),
            ChildOnOff(element, w, "linkToQuery"),
            ChildOnOff(element, w, "doNotSuppressBlankLines"),
            ChildOnOff(element, w, "mailAsAttachment"),
            ChildOnOff(element, w, "viewMergedData"),
            ChildInteger(element, w, "activeRecord", issues, settingsPartUri, ordinal),
            ChildValue(element, w, "checkErrors", ref metadataCharacters),
            ChildValue(element, w, "query", ref metadataCharacters),
            ChildValue(element, w, "connectString", ref metadataCharacters),
            ChildValue(element, w, "addressFieldName", ref metadataCharacters),
            ChildValue(element, w, "mailSubject", ref metadataCharacters),
            dataSource,
            headerSource,
            odso,
            new ReadOnlyCollection<string>(unmodeled)
        );
    }

    private WordMailMergeDataSourceObject ParseOdso(
        OpcPackageSnapshot package,
        string settingsPartUri,
        LosslessXmlDocument source,
        XElement element,
        XNamespace w,
        List<WordMailMergeFieldMapping> mappings,
        ref long metadataCharacters,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var ordinal = source.GetElementOrdinal(element);
        var odsoId = StableId("wmmo_", package.Fingerprint, settingsPartUri, ordinal);
        var sourceRelationship = ParseRelationshipChild(
            package,
            settingsPartUri,
            source,
            element,
            w,
            "src",
            WordMailMergeRelationshipRole.OdsoSource,
            MailMergeRelationshipTypes,
            issues
        );
        var recipientRelationship = ParseRelationshipChild(
            package,
            settingsPartUri,
            source,
            element,
            w,
            "recipientData",
            WordMailMergeRelationshipRole.RecipientData,
            RecipientRelationshipTypes,
            issues
        );
        var mappingIds = new List<string>();
        var position = 0;
        foreach (var mappingElement in element.Elements(w + "fieldMapData"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mappings.Count >= _options.MaxMappings)
            {
                throw new WordMailMergeLimitException(
                    $"Mail-merge field mapping count exceeds {_options.MaxMappings}."
                );
            }
            var mapping = ParseMapping(
                package,
                settingsPartUri,
                source,
                mappingElement,
                w,
                position++,
                ref metadataCharacters,
                issues
            );
            mappings.Add(mapping);
            mappingIds.Add(mapping.Id);
        }
        foreach (var duplicate in mappings
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceColumnName))
            .GroupBy(item => item.SourceColumnName!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_MAPPING_SOURCE_NAME_DUPLICATE",
                WordMailMergeIssueSeverity.Warning,
                "More than one ODSO mapping declares the same source column name.",
                settingsPartUri,
                ordinal,
                odsoId
            ));
        }
        var unmodeled = UnmodeledChildren(element, w, KnownOdsoChildren);
        ChargeMetadata(unmodeled, ref metadataCharacters);
        return new WordMailMergeDataSourceObject(
            odsoId,
            ordinal,
            ChildValue(element, w, "udl", ref metadataCharacters),
            ChildValue(element, w, "table", ref metadataCharacters),
            ChildInteger(element, w, "colDelim", issues, settingsPartUri, ordinal),
            ChildValue(element, w, "type", ref metadataCharacters),
            ChildOnOff(element, w, "fHdr"),
            sourceRelationship,
            recipientRelationship,
            new ReadOnlyCollection<string>(mappingIds),
            new ReadOnlyCollection<string>(unmodeled)
        );
    }

    private WordMailMergeFieldMapping ParseMapping(
        OpcPackageSnapshot package,
        string settingsPartUri,
        LosslessXmlDocument source,
        XElement element,
        XNamespace w,
        int position,
        ref long metadataCharacters,
        IssueState issues
    )
    {
        var ordinal = source.GetElementOrdinal(element);
        var id = StableId("wmmm_", package.Fingerprint, settingsPartUri, ordinal);
        var declaredMappedName = ChildValue(element, w, "mappedName", ref metadataCharacters);
        var effectiveName = position < WordPredefinedMappingNames.Length
            ? WordPredefinedMappingNames[position]
            : null;
        if (
            effectiveName is not null
            && declaredMappedName is not null
            && !string.Equals(effectiveName, declaredMappedName, StringComparison.Ordinal)
        )
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_WORD_POSITIONAL_MAPPING_OVERRIDE",
                WordMailMergeIssueSeverity.Info,
                "Microsoft Word uses the fieldMapData position for predefined address-field mapping and ignores the conflicting mappedName value.",
                settingsPartUri,
                ordinal,
                id
            ));
        }
        var unmodeled = UnmodeledChildren(element, w, KnownMappingChildren);
        ChargeMetadata(unmodeled, ref metadataCharacters);
        return new WordMailMergeFieldMapping(
            id,
            position,
            ChildValue(element, w, "type", ref metadataCharacters),
            ChildValue(element, w, "name", ref metadataCharacters),
            declaredMappedName,
            effectiveName,
            ChildInteger(element, w, "column", issues, settingsPartUri, ordinal),
            ChildValue(element, w, "lid", ref metadataCharacters),
            ChildOnOff(element, w, "dynamicAddress"),
            ordinal,
            new ReadOnlyCollection<string>(unmodeled)
        );
    }

    private (WordMailMergeRecipientDataPart? Part, List<WordMailMergeRecipient> Recipients)
        ParseRecipientPart(
            OpcPackageSnapshot package,
            string partUri,
            WordMailMergeRelationship relationship,
            ref long metadataCharacters,
            IssueState issues,
            CancellationToken cancellationToken
        )
    {
        var recipients = new List<WordMailMergeRecipient>();
        if (!package.Parts.TryGetValue(partUri, out var part))
        {
            return (null, recipients);
        }
        if (
            part.ContentType is not OpenXmlRecipientDataContentType
                and not LegacyRecipientDataContentType
        )
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RECIPIENT_CONTENT_TYPE_INVALID",
                WordMailMergeIssueSeverity.Error,
                "The mail-merge recipient data part has an unsupported content type.",
                partUri,
                SubjectId: relationship.Id
            ));
        }
        if (part.Entry.Content.Length > _options.MaxRecipientDataPartBytes)
        {
            throw new WordMailMergeLimitException(
                $"Mail-merge XML part '{part.Uri}' exceeds {_options.MaxRecipientDataPartBytes} bytes."
            );
        }
        WordOperationResourceAccounting.ChargeXmlParse(
            _resourceLease,
            WordOperationResourceStage.MailMerge,
            part.Entry.Content.Length
        );

        string? rootLocalName = null;
        string? rootNamespace = null;
        var rootOrdinal = -1;
        var elementCount = 0;
        var sequence = 0;
        var recipientState = default(RecipientParseState);
        var hasRecipientState = false;
        try
        {
            using var stream = OpenReadOnlyMemoryStream(part.Entry.Content);
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = _options.MaxRecipientDataPartBytes,
                    MaxCharactersFromEntities = 0,
                    IgnoreComments = false,
                    IgnoreProcessingInstructions = false,
                    IgnoreWhitespace = false,
                    CheckCharacters = true,
                }
            );
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (elementCount >= _options.MaxRecipientXmlElements)
                    {
                        throw new WordMailMergeLimitException(
                            $"Mail-merge recipient XML contains more than {_options.MaxRecipientXmlElements} elements."
                        );
                    }
                    if (reader.Depth + 1 > _options.MaxRecipientXmlDepth)
                    {
                        throw new WordMailMergeLimitException(
                            $"Mail-merge recipient XML depth exceeds {_options.MaxRecipientXmlDepth}."
                        );
                    }
                    var ordinal = elementCount++;
                    if (rootLocalName is null)
                    {
                        rootLocalName = reader.LocalName;
                        rootNamespace = reader.NamespaceURI;
                        rootOrdinal = ordinal;
                    }

                    if (
                        !hasRecipientState
                        && reader.Depth == 1
                        && reader.LocalName == "recipientData"
                    )
                    {
                        if (recipients.Count >= _options.MaxRecipients)
                        {
                            throw new WordMailMergeLimitException(
                                $"Mail-merge recipient count exceeds {_options.MaxRecipients}."
                            );
                        }
                        recipientState = new RecipientParseState(ordinal);
                        hasRecipientState = true;
                        if (reader.IsEmptyElement)
                        {
                            recipients.Add(CompleteRecipient(
                                package.Fingerprint,
                                partUri,
                                sequence++,
                                recipientState,
                                issues
                            ));
                            recipientState = default;
                            hasRecipientState = false;
                        }
                        continue;
                    }

                    if (hasRecipientState && reader.Depth == 2)
                    {
                        ObserveRecipientChild(
                            reader,
                            ref recipientState,
                            ref metadataCharacters
                        );
                    }
                }
                else if (
                    reader.NodeType == XmlNodeType.EndElement
                    && hasRecipientState
                    && reader.Depth == 1
                )
                {
                    recipients.Add(CompleteRecipient(
                        package.Fingerprint,
                        partUri,
                        sequence++,
                        recipientState,
                        issues
                    ));
                    recipientState = default;
                    hasRecipientState = false;
                }
            }
        }
        catch (WordMailMergeLimitException)
        {
            throw;
        }
        catch (XmlException exception)
        {
            throw new WordMailMergeProjectionException(
                $"Mail-merge XML part '{part.Uri}' is not safe, bounded, well-formed XML.",
                exception
            );
        }

        if (rootLocalName is null || rootNamespace is null || rootOrdinal < 0)
        {
            throw new WordMailMergeProjectionException(
                $"Mail-merge XML part '{part.Uri}' has no document element."
            );
        }
        if (
            rootLocalName != "recipients"
            || rootNamespace is not TransitionalWordNamespace
                and not StrictWordNamespace
                and not LegacyRecipientNamespace
        )
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RECIPIENT_ROOT_INVALID",
                WordMailMergeIssueSeverity.Error,
                "The mail-merge recipient data part has an unsupported root element or namespace.",
                partUri,
                rootOrdinal,
                relationship.Id
            ));
        }
        var incomingCount = package.Relationships.Count(item =>
            item.TargetMode == OpcRelationshipTargetMode.Internal
            && string.Equals(item.ResolvedTargetPartUri, partUri, StringComparison.Ordinal)
        );
        if (incomingCount != 1)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RECIPIENT_RELATIONSHIP_CARDINALITY",
                WordMailMergeIssueSeverity.Error,
                "A mail-merge recipient data part must have exactly one incoming relationship.",
                partUri,
                rootOrdinal,
                relationship.Id
            ));
        }
        if (package.RelationshipsFrom(partUri).Count != 0)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RECIPIENT_RELATIONSHIPS_FORBIDDEN",
                WordMailMergeIssueSeverity.Error,
                "A mail-merge recipient data part must not own relationships.",
                partUri,
                rootOrdinal,
                relationship.Id
            ));
        }
        var recipientPart = new WordMailMergeRecipientDataPart(
            StableId("wmmrp_", package.Fingerprint, partUri),
            partUri,
            part.ContentType,
            rootNamespace,
            rootOrdinal,
            PackageReachableParts(package, cancellationToken).Contains(partUri),
            incomingCount,
            new ReadOnlyCollection<string>(recipients.Select(item => item.Id).ToArray())
        );
        return (recipientPart, recipients);
    }

    private void ObserveRecipientChild(
        XmlReader reader,
        ref RecipientParseState state,
        ref long metadataCharacters
    )
    {
        var value = AttributeValue(reader, "val");
        switch (reader.LocalName)
        {
            case "active":
                if (!state.HasActive)
                {
                    state.HasActive = true;
                    state.IsIncluded = ParseOnOff(value);
                }
                break;
            case "column":
                if (!state.HasColumn)
                {
                    state.HasColumn = true;
                    state.ColumnValue = value;
                }
                break;
            case "uniqueTag":
                state.HasUniqueTag = true;
                ObserveIdentity(value, ref state, ref metadataCharacters);
                break;
            case "hash":
                ObserveIdentity(value, ref state, ref metadataCharacters);
                break;
            default:
                state.UnmodeledElements ??= new HashSet<string>(StringComparer.Ordinal);
                var qualifiedName = "{" + reader.NamespaceURI + "}" + reader.LocalName;
                if (state.UnmodeledElements.Add(qualifiedName))
                {
                    ChargeMetadata(qualifiedName, ref metadataCharacters);
                }
                break;
        }
    }

    private void ObserveIdentity(
        string? value,
        ref RecipientParseState state,
        ref long metadataCharacters
    )
    {
        if (value is null)
        {
            return;
        }
        ChargeMetadata(value, ref metadataCharacters);
        state.IdentityValueCount++;
        state.FirstIdentityValue ??= value;
    }

    private static WordMailMergeRecipient CompleteRecipient(
        string packageFingerprint,
        string partUri,
        int sequence,
        RecipientParseState state,
        IssueState issues
    )
    {
        var identityKind = state.IdentityValueCount switch
        {
            0 => WordMailMergeRecipientIdentityKind.Missing,
            > 1 => WordMailMergeRecipientIdentityKind.Ambiguous,
            _ when state.HasUniqueTag => WordMailMergeRecipientIdentityKind.UniqueTag,
            _ => WordMailMergeRecipientIdentityKind.Hash,
        };
        var unmodeled = state.UnmodeledElements is null
            ? Array.Empty<string>()
            : state.UnmodeledElements.Order(StringComparer.Ordinal).ToArray();
        var recipient = new WordMailMergeRecipient(
            StableRecipientId(packageFingerprint, partUri, state.SourceElementOrdinal),
            partUri,
            sequence,
            !state.HasActive || state.IsIncluded,
            ParseInteger(
                state.ColumnValue,
                "column",
                issues,
                partUri,
                state.SourceElementOrdinal
            ),
            identityKind,
            state.IdentityValueCount == 1 ? state.FirstIdentityValue : null,
            state.SourceElementOrdinal,
            unmodeled
        );
        if (identityKind == WordMailMergeRecipientIdentityKind.Missing)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RECIPIENT_IDENTITY_MISSING",
                WordMailMergeIssueSeverity.Warning,
                "A mail-merge recipient record has no uniqueTag or hash identity.",
                partUri,
                state.SourceElementOrdinal,
                recipient.Id
            ));
        }
        else if (identityKind == WordMailMergeRecipientIdentityKind.Ambiguous)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RECIPIENT_IDENTITY_AMBIGUOUS",
                WordMailMergeIssueSeverity.Error,
                "A mail-merge recipient record declares more than one identity value.",
                partUri,
                state.SourceElementOrdinal,
                recipient.Id
            ));
        }
        return recipient;
    }

    private static string? AttributeValue(XmlReader reader, string localName)
    {
        if (!reader.HasAttributes)
        {
            return null;
        }
        string? value = null;
        while (reader.MoveToNextAttribute())
        {
            if (!reader.Prefix.Equals("xmlns", StringComparison.Ordinal)
                && !reader.Name.Equals("xmlns", StringComparison.Ordinal)
                && reader.LocalName == localName)
            {
                value = reader.Value;
                break;
            }
        }
        reader.MoveToElement();
        return value;
    }

    private static MemoryStream OpenReadOnlyMemoryStream(ReadOnlyMemory<byte> content)
    {
        if (MemoryMarshal.TryGetArray(content, out var segment))
        {
            return new MemoryStream(
                segment.Array!,
                segment.Offset,
                segment.Count,
                writable: false,
                publiclyVisible: true
            );
        }
        return new MemoryStream(content.ToArray(), writable: false);
    }

    private struct RecipientParseState
    {
        public RecipientParseState(int sourceElementOrdinal)
        {
            SourceElementOrdinal = sourceElementOrdinal;
        }

        public int SourceElementOrdinal { get; }

        public bool HasActive { get; set; }

        public bool IsIncluded { get; set; }

        public bool HasColumn { get; set; }

        public string? ColumnValue { get; set; }

        public bool HasUniqueTag { get; set; }

        public int IdentityValueCount { get; set; }

        public string? FirstIdentityValue { get; set; }

        public HashSet<string>? UnmodeledElements { get; set; }
    }

    private static string StableRecipientId(
        string packageFingerprint,
        string partUri,
        int sourceElementOrdinal
    )
    {
        Span<char> ordinalCharacters = stackalloc char[11];
        if (!sourceElementOrdinal.TryFormat(
            ordinalCharacters,
            out var ordinalCharacterCount,
            provider: CultureInfo.InvariantCulture
        ))
        {
            throw new WordMailMergeProjectionException(
                "Mail-merge recipient ordinal could not be formatted."
            );
        }
        ordinalCharacters = ordinalCharacters[..ordinalCharacterCount];
        var byteCount = checked(
            Encoding.UTF8.GetByteCount(packageFingerprint)
                + 1
                + Encoding.UTF8.GetByteCount(partUri)
                + 1
                + Encoding.UTF8.GetByteCount(ordinalCharacters)
        );
        byte[]? rented = null;
        Span<byte> material = byteCount <= 512
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount));
        try
        {
            var offset = Encoding.UTF8.GetBytes(packageFingerprint, material);
            material[offset++] = 0;
            offset += Encoding.UTF8.GetBytes(partUri, material[offset..]);
            material[offset++] = 0;
            offset += Encoding.UTF8.GetBytes(ordinalCharacters, material[offset..]);
            Span<byte> hash = stackalloc byte[32];
            _ = SHA256.HashData(material[..offset], hash);
            Span<char> id = stackalloc char[29];
            "wmmr_".AsSpan().CopyTo(id);
            const string hex = "0123456789abcdef";
            for (var index = 0; index < 12; index++)
            {
                id[5 + index * 2] = hex[hash[index] >> 4];
                id[6 + index * 2] = hex[hash[index] & 0x0f];
            }
            return new string(id);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }

    private List<WordMailMergeField> ParseFields(
        WordReferenceGraph references,
        WordMailMergeConfiguration? configuration,
        IReadOnlyList<WordMailMergeFieldMapping> mappings,
        ref long metadataCharacters,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var result = new List<WordMailMergeField>();
        var mergeEdges = references.Edges
            .Where(edge => edge.TargetKind == WordReferenceTargetKind.MergeField)
            .GroupBy(edge => edge.SourceFieldId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var field in references.Fields.Where(item =>
            item.Classification == WordFieldClassification.MailMerge
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Count >= _options.MaxFields)
            {
                throw new WordMailMergeLimitException(
                    $"Mail-merge field count exceeds {_options.MaxFields}."
                );
            }
            var fieldType = field.FieldType ?? "UNKNOWN";
            var targetName = mergeEdges.TryGetValue(field.Id, out var edges)
                && edges.Length == 1
                    ? edges[0].TargetKey
                    : fieldType == "MERGEBARCODE"
                        ? FirstPositionalArgument(field.Tokens)
                        : null;
            ChargeMetadata(targetName, ref metadataCharacters);
            var mappingIds = Array.Empty<string>();
            var bindingStatus = WordMailMergeFieldBindingStatus.NotApplicable;
            if (fieldType is "MERGEFIELD" or "MERGEBARCODE")
            {
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    bindingStatus = WordMailMergeFieldBindingStatus.Missing;
                }
                else
                {
                    var sourceMatches = mappings.Where(mapping => string.Equals(
                        mapping.SourceColumnName,
                        targetName,
                        StringComparison.OrdinalIgnoreCase
                    )).ToArray();
                    var wordMatches = sourceMatches.Length == 0
                        ? mappings.Where(mapping => string.Equals(
                            mapping.WordEffectivePredefinedName,
                            targetName,
                            StringComparison.OrdinalIgnoreCase
                        )).ToArray()
                        : [];
                    var matches = sourceMatches.Length != 0 ? sourceMatches : wordMatches;
                    mappingIds = matches.Select(mapping => mapping.Id).ToArray();
                    bindingStatus = matches.Length switch
                    {
                        0 => WordMailMergeFieldBindingStatus.Missing,
                        > 1 => WordMailMergeFieldBindingStatus.Ambiguous,
                        _ when sourceMatches.Length != 0 =>
                            WordMailMergeFieldBindingStatus.ResolvedBySourceColumnName,
                        _ => WordMailMergeFieldBindingStatus.ResolvedByWordPredefinedName,
                    };
                }
            }
            var graphField = new WordMailMergeField(
                StableId("wmmf_", references.PackageFingerprint, field.Id),
                field.Id,
                fieldType,
                field.StoryId,
                field.PartUri,
                field.StartElementOrdinal,
                field.StartNodeId,
                field.Status == WordFieldStatus.Complete && field.InstructionParseComplete,
                field.IsInDeletedContent,
                targetName,
                bindingStatus,
                new ReadOnlyCollection<string>(mappingIds)
            );
            if (bindingStatus == WordMailMergeFieldBindingStatus.Ambiguous)
            {
                issues.Add(new WordMailMergeIssue(
                    "MAIL_MERGE_FIELD_BINDING_AMBIGUOUS",
                    WordMailMergeIssueSeverity.Error,
                    "A mail-merge field matches more than one ODSO mapping.",
                    field.PartUri,
                    field.StartElementOrdinal,
                    graphField.Id
                ));
            }
            else if (
                bindingStatus == WordMailMergeFieldBindingStatus.Missing
                && configuration is not null
            )
            {
                issues.Add(new WordMailMergeIssue(
                    "MAIL_MERGE_FIELD_BINDING_MISSING",
                    WordMailMergeIssueSeverity.Warning,
                    "A mail-merge field has no unique ODSO mapping in the saved package.",
                    field.PartUri,
                    field.StartElementOrdinal,
                    graphField.Id
                ));
            }
            result.Add(graphField);
        }
        return result;
    }

    private WordMailMergeRelationship? ParseRelationshipChild(
        OpcPackageSnapshot package,
        string sourcePartUri,
        LosslessXmlDocument source,
        XElement parent,
        XNamespace w,
        string localName,
        WordMailMergeRelationshipRole role,
        IReadOnlySet<string> validTypes,
        IssueState issues
    )
    {
        var elements = parent.Elements(w + localName).ToArray();
        if (elements.Length == 0)
        {
            return null;
        }
        var element = elements[0];
        var ordinal = source.GetElementOrdinal(element);
        if (elements.Length > 1)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RELATIONSHIP_REFERENCE_DUPLICATE",
                WordMailMergeIssueSeverity.Error,
                "A mail-merge relationship role is declared more than once.",
                sourcePartUri,
                ordinal
            ));
        }
        var relationshipId = RelationshipId(element);
        var id = StableId("wmmrel_", package.Fingerprint, sourcePartUri, role, relationshipId ?? "missing");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RELATIONSHIP_ID_MISSING",
                WordMailMergeIssueSeverity.Error,
                "A mail-merge relationship reference has no relationship ID.",
                sourcePartUri,
                ordinal,
                id
            ));
            return new WordMailMergeRelationship(
                id, role, sourcePartUri, ordinal, string.Empty, null, null, null, null,
                RelationshipExists: false, RelationshipTypeValid: false,
                TargetExists: false, IsResolved: false
            );
        }
        var candidates = package.RelationshipsFrom(sourcePartUri)
            .Where(item => item.Id == relationshipId)
            .ToArray();
        var relationship = candidates.Length == 1 ? candidates[0] : null;
        var typeValid = relationship is not null && validTypes.Contains(relationship.Type);
        var targetExists = relationship?.TargetMode switch
        {
            OpcRelationshipTargetMode.External => true,
            OpcRelationshipTargetMode.Internal => relationship.ResolvedTargetPartUri is { } target
                && package.Parts.ContainsKey(target),
            _ => false,
        };
        if (candidates.Length != 1)
        {
            issues.Add(new WordMailMergeIssue(
                candidates.Length == 0
                    ? "MAIL_MERGE_RELATIONSHIP_MISSING"
                    : "MAIL_MERGE_RELATIONSHIP_AMBIGUOUS",
                WordMailMergeIssueSeverity.Error,
                "A mail-merge relationship reference does not resolve to exactly one package relationship.",
                sourcePartUri,
                ordinal,
                id
            ));
        }
        else if (!typeValid)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RELATIONSHIP_TYPE_INVALID",
                WordMailMergeIssueSeverity.Error,
                "A mail-merge relationship uses the wrong relationship type for its role.",
                sourcePartUri,
                ordinal,
                id
            ));
        }
        else if (!targetExists)
        {
            issues.Add(new WordMailMergeIssue(
                "MAIL_MERGE_RELATIONSHIP_TARGET_MISSING",
                WordMailMergeIssueSeverity.Error,
                "A mail-merge relationship target cannot be resolved safely.",
                sourcePartUri,
                ordinal,
                id
            ));
        }
        return new WordMailMergeRelationship(
            id,
            role,
            sourcePartUri,
            ordinal,
            relationshipId,
            relationship?.Type,
            relationship?.Target,
            relationship?.TargetMode,
            relationship?.ResolvedTargetPartUri,
            candidates.Length == 1,
            typeValid,
            targetExists,
            candidates.Length == 1 && typeValid && targetExists
        );
    }

    private LosslessXmlDocument ParsePart(
        OpcPart part,
        int maximumBytes,
        WordOperationResourceStage stage,
        CancellationToken cancellationToken
    )
    {
        if (part.Entry.Content.Length > maximumBytes)
        {
            throw new WordMailMergeLimitException(
                $"Mail-merge XML part '{part.Uri}' exceeds {maximumBytes} bytes."
            );
        }
        try
        {
            var options = LosslessXmlOptions.Default with
            {
                MaxSourceBytes = maximumBytes,
                MaxXmlCharacters = maximumBytes,
                MaxTextCharacters = maximumBytes,
                MaxXmlElements = 1_000_000,
                MaxXmlDepth = 256,
            };
            return _resourceLease is null
                ? LosslessXmlDocument.Parse(part.Entry.Content, options, cancellationToken)
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    stage,
                    cancellationToken
                );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordMailMergeLimitException(
                $"Mail-merge XML part '{part.Uri}' exceeds a safe XML limit.",
                exception
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordMailMergeProjectionException(
                $"Mail-merge XML part '{part.Uri}' is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private void ChargeGraph(
        WordMailMergeConfiguration? configuration,
        IReadOnlyList<WordMailMergeFieldMapping> mappings,
        WordMailMergeRecipientDataPart? recipientPart,
        IReadOnlyList<WordMailMergeRecipient> recipients,
        IReadOnlyList<WordMailMergeField> fields,
        IReadOnlyList<WordMailMergeIssue> issues
    )
    {
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.MailMerge,
            configuration is null ? 0 : 1,
            4_096
        );
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.MailMerge,
            mappings.Count,
            2_048
        );
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.MailMerge,
            recipientPart is null ? 0 : 1,
            2_048
        );
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.MailMerge,
            recipients.Count,
            1_024
        );
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.MailMerge,
            fields.Count,
            1_536
        );
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.MailMerge,
            issues.Count,
            768
        );
    }

    private void ChargeMetadata(string? value, ref long total)
    {
        if (value is null)
        {
            return;
        }
        if (value.Length > _options.MaxValueCharacters)
        {
            throw new WordMailMergeLimitException(
                $"Mail-merge metadata exceeds {_options.MaxValueCharacters} characters."
            );
        }
        total = checked(total + value.Length);
        if (total > _options.MaxMetadataCharacters)
        {
            throw new WordMailMergeLimitException(
                $"Mail-merge metadata exceeds {_options.MaxMetadataCharacters} characters."
            );
        }
    }

    private void ChargeMetadata(IEnumerable<string> values, ref long total)
    {
        foreach (var value in values)
        {
            ChargeMetadata(value, ref total);
        }
    }

    private string? ChildValue(
        XElement parent,
        XNamespace w,
        string localName,
        ref long metadataCharacters
    )
    {
        var elements = parent.Elements(w + localName).ToArray();
        if (elements.Length != 1)
        {
            return null;
        }
        var value = AttributeValue(elements[0], "val");
        ChargeMetadata(value, ref metadataCharacters);
        return value;
    }

    private static int? ChildInteger(
        XElement parent,
        XNamespace w,
        string localName,
        IssueState issues,
        string partUri,
        int sourceOrdinal
    ) => ParseInteger(
        parent.Elements(w + localName).Select(item => AttributeValue(item, "val")).FirstOrDefault(),
        localName,
        issues,
        partUri,
        sourceOrdinal
    );

    private static int? ChildIntegerByLocalName(
        XElement parent,
        string localName,
        IssueState issues,
        string partUri,
        int sourceOrdinal
    ) => ParseInteger(
        parent.Elements().Where(item => item.Name.LocalName == localName)
            .Select(item => AttributeValue(item, "val"))
            .FirstOrDefault(),
        localName,
        issues,
        partUri,
        sourceOrdinal
    );

    private static int? ParseInteger(
        string? value,
        string localName,
        IssueState issues,
        string partUri,
        int sourceOrdinal
    )
    {
        if (value is null)
        {
            return null;
        }
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        issues.Add(new WordMailMergeIssue(
            "MAIL_MERGE_INTEGER_INVALID",
            WordMailMergeIssueSeverity.Warning,
            $"A mail-merge {localName} value is not a valid non-negative integer.",
            partUri,
            sourceOrdinal
        ));
        return null;
    }

    private static bool ChildOnOff(XElement parent, XNamespace w, string localName)
    {
        var element = parent.Elements(w + localName).FirstOrDefault();
        return element is not null && ParseOnOff(AttributeValue(element, "val"));
    }

    private static bool ChildOnOffByLocalName(XElement parent, string localName)
    {
        var element = parent.Elements().FirstOrDefault(item => item.Name.LocalName == localName);
        return element is not null && ParseOnOff(AttributeValue(element, "val"));
    }

    private static bool ParseOnOff(string? value) => value is null
        || value.Equals("1", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("on", StringComparison.OrdinalIgnoreCase);

    private static string? RelationshipId(XElement element) => element.Attributes()
        .FirstOrDefault(attribute =>
            attribute.Name.LocalName == "id"
            && attribute.Name.NamespaceName is TransitionalRelationshipsNamespace
                or StrictRelationshipsNamespace
        )?.Value;

    private static string? AttributeValue(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == localName
        )?.Value;

    private static string? FirstPositionalArgument(IReadOnlyList<WordFieldToken> tokens) =>
        tokens.Skip(1).FirstOrDefault(token => token.Kind != WordFieldTokenKind.Switch)?.Value;

    private static string[] UnmodeledChildren(
        XElement parent,
        XNamespace expectedNamespace,
        IReadOnlySet<string> knownLocalNames
    ) => parent.Elements()
        .Where(child => child.Name.Namespace != expectedNamespace
            || !knownLocalNames.Contains(child.Name.LocalName))
        .Select(child => QualifiedName(child.Name))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string QualifiedName(XName name) =>
        "{" + name.NamespaceName + "}" + name.LocalName;

    private static string StableId(string prefix, params object[] components)
    {
        var material = string.Join('\0', components.Select(component =>
            Convert.ToString(component, CultureInfo.InvariantCulture) ?? string.Empty
        ));
        return prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant()[..24];
    }

    private static void EnsureFingerprint(params string[] fingerprints)
    {
        if (fingerprints.Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new WordMailMergeProjectionException(
                "Mail-merge graph inputs do not share one package fingerprint."
            );
        }
    }

    private static bool IsWordNamespace(string value) => value is
        TransitionalWordNamespace or StrictWordNamespace;

    private static IReadOnlySet<string> MailMergeRelationshipTypes { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            TransitionalMailMergeSourceRelationship,
            StrictMailMergeSourceRelationship,
        };

    private static IReadOnlySet<string> RecipientRelationshipTypes { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            TransitionalRecipientDataRelationship,
            StrictRecipientDataRelationship,
        };

    private static IReadOnlySet<string> PackageReachableParts(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue("/");
        while (queue.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = queue.Dequeue();
            foreach (var relationship in package.RelationshipsFrom(source))
            {
                if (
                    relationship.TargetMode == OpcRelationshipTargetMode.Internal
                    && relationship.ResolvedTargetPartUri is { } target
                    && package.Parts.ContainsKey(target)
                    && reachable.Add(target)
                )
                {
                    queue.Enqueue(target);
                }
            }
        }
        return reachable;
    }

    private sealed class IssueState
    {
        private readonly int _maximum;
        private readonly List<WordMailMergeIssue> _items = [];

        public IssueState(int maximum) => _maximum = maximum;

        public IReadOnlyList<WordMailMergeIssue> Items => _items;

        public bool Truncated { get; private set; }

        public void Add(WordMailMergeIssue issue)
        {
            if (_items.Count >= _maximum)
            {
                Truncated = true;
                return;
            }
            _items.Add(issue);
        }
    }
}

public sealed class WordMailMergeLimitException : IOException
{
    public WordMailMergeLimitException(string message)
        : base(message) { }

    public WordMailMergeLimitException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordMailMergeProjectionException : IOException
{
    public WordMailMergeProjectionException(string message)
        : base(message) { }

    public WordMailMergeProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}
