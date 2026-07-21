using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordSettingsIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordSettingsIssue(
    string Code,
    WordSettingsIssueSeverity Severity,
    string Message,
    string? ElementName = null
);

public sealed record WordBooleanSetting(
    string Name,
    bool Value,
    int SourceElementOrdinal
);

public sealed record WordThemeFontLanguages(
    string? Latin,
    string? EastAsian,
    string? ComplexScript,
    int SourceElementOrdinal
)
{
    public string? ForRole(WordThemeFontRole role) => role switch
    {
        WordThemeFontRole.Latin => Latin,
        WordThemeFontRole.EastAsian => EastAsian,
        WordThemeFontRole.ComplexScript => ComplexScript,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

public sealed record WordCompatibilitySetting(
    string Name,
    string Uri,
    string Value,
    int SourceElementOrdinal
);

public sealed record WordLegacyCompatibilityOption(
    string Name,
    bool Value,
    int SourceElementOrdinal
);

public sealed class WordCompatibilityProfile
{
    internal WordCompatibilityProfile(
        int sourceElementOrdinal,
        IReadOnlyList<WordCompatibilitySetting> settings,
        IReadOnlyList<WordLegacyCompatibilityOption> legacyOptions,
        int? compatibilityMode,
        IReadOnlyList<string> unmodeledElements
    )
    {
        SourceElementOrdinal = sourceElementOrdinal;
        Settings = new ReadOnlyCollection<WordCompatibilitySetting>(
            settings.ToArray()
        );
        LegacyOptions = new ReadOnlyCollection<WordLegacyCompatibilityOption>(
            legacyOptions.ToArray()
        );
        CompatibilityMode = compatibilityMode;
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<WordCompatibilitySetting> Settings { get; }

    public IReadOnlyList<WordLegacyCompatibilityOption> LegacyOptions { get; }

    public int? CompatibilityMode { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }
}

public sealed record WordDocumentVariable(
    string Name,
    string Value,
    int SourceElementOrdinal
);

public sealed record WordProtectionDescriptor(
    bool IsPresent,
    bool IsEnforced,
    bool FormattingRestricted,
    string? EditMode,
    string? AlgorithmName,
    string? CryptographicProviderType,
    string? CryptographicAlgorithmClass,
    string? CryptographicAlgorithmType,
    int? CryptographicAlgorithmSid,
    int? SpinCount,
    bool HasHash,
    bool HasSalt,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledAttributes
);

public sealed record WordWriteProtectionDescriptor(
    bool IsPresent,
    bool IsRecommended,
    string? AlgorithmName,
    int? SpinCount,
    bool HasHash,
    bool HasSalt,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledAttributes
);

public sealed record WordSettingsRelationshipReference(
    string RelationshipId,
    string? RelationshipType,
    string? Target,
    OpcRelationshipTargetMode? TargetMode,
    string? ResolvedTargetPartUri,
    bool IsResolved
);

public sealed record WordAttachedTemplate(
    WordSettingsRelationshipReference Relationship,
    int SourceElementOrdinal
);

public sealed record WordMailMergeSettings(
    string? MainDocumentType,
    string? DataType,
    bool LinkToQuery,
    string? Query,
    string? ConnectionString,
    bool HasOfficeDataSourceObject,
    WordSettingsRelationshipReference? DataSource,
    WordSettingsRelationshipReference? HeaderSource,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledElements
);

public sealed record WordViewSettings(
    string? View,
    string? ZoomKind,
    int? ZoomPercent,
    int? DefaultTabStopTwips,
    int? DefaultImageDpi
);

public sealed record WordSettingsElementInventory(
    string QualifiedName,
    int Count
);

public sealed class WordSettingsGraph
{
    private readonly IReadOnlyDictionary<string, WordBooleanSetting> _booleanSettings;

    internal WordSettingsGraph(
        string packageFingerprint,
        string mainPartUri,
        string? settingsPartUri,
        IReadOnlyDictionary<string, WordBooleanSetting> booleanSettings,
        WordThemeFontLanguages? themeFontLanguages,
        WordCompatibilityProfile? compatibility,
        WordProtectionDescriptor? documentProtection,
        WordWriteProtectionDescriptor? writeProtection,
        IReadOnlyList<WordDocumentVariable> documentVariables,
        WordAttachedTemplate? attachedTemplate,
        WordMailMergeSettings? mailMerge,
        WordViewSettings view,
        string? decimalSymbol,
        string? listSeparator,
        IReadOnlyList<WordSettingsIssue> issues,
        IReadOnlyList<WordSettingsElementInventory> inventory,
        IReadOnlyList<string> unmodeledRootElements
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        SettingsPartUri = settingsPartUri;
        _booleanSettings = new ReadOnlyDictionary<string, WordBooleanSetting>(
            new Dictionary<string, WordBooleanSetting>(
                booleanSettings,
                StringComparer.Ordinal
            )
        );
        BooleanSettings = _booleanSettings;
        ThemeFontLanguages = themeFontLanguages;
        Compatibility = compatibility;
        DocumentProtection = documentProtection;
        WriteProtection = writeProtection;
        DocumentVariables = new ReadOnlyCollection<WordDocumentVariable>(
            documentVariables.ToArray()
        );
        AttachedTemplate = attachedTemplate;
        MailMerge = mailMerge;
        View = view;
        DecimalSymbol = decimalSymbol;
        ListSeparator = listSeparator;
        Issues = new ReadOnlyCollection<WordSettingsIssue>(issues.ToArray());
        Inventory = new ReadOnlyCollection<WordSettingsElementInventory>(
            inventory.ToArray()
        );
        UnmodeledRootElements = new ReadOnlyCollection<string>(
            unmodeledRootElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public string? SettingsPartUri { get; }

    public bool HasSettingsPart => SettingsPartUri is not null;

    public IReadOnlyDictionary<string, WordBooleanSetting> BooleanSettings { get; }

    public WordThemeFontLanguages? ThemeFontLanguages { get; }

    public WordCompatibilityProfile? Compatibility { get; }

    public WordProtectionDescriptor? DocumentProtection { get; }

    public WordWriteProtectionDescriptor? WriteProtection { get; }

    public IReadOnlyList<WordDocumentVariable> DocumentVariables { get; }

    public WordAttachedTemplate? AttachedTemplate { get; }

    public WordMailMergeSettings? MailMerge { get; }

    public WordViewSettings View { get; }

    public string? DecimalSymbol { get; }

    public string? ListSeparator { get; }

    public IReadOnlyList<WordSettingsIssue> Issues { get; }

    public IReadOnlyList<WordSettingsElementInventory> Inventory { get; }

    public IReadOnlyList<string> UnmodeledRootElements { get; }

    public bool EvenAndOddHeaders => BooleanValue("evenAndOddHeaders");

    public bool TrackRevisions => BooleanValue("trackRevisions");

    public bool UpdateFields => BooleanValue("updateFields");

    public bool EmbedTrueTypeFonts => BooleanValue("embedTrueTypeFonts");

    public bool EmbedSystemFonts => BooleanValue("embedSystemFonts");

    public bool SaveSubsetFonts => BooleanValue("saveSubsetFonts");

    public bool TryGetBoolean(
        string name,
        out WordBooleanSetting? setting
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _booleanSettings.TryGetValue(name, out setting);
    }

    private bool BooleanValue(string name) =>
        _booleanSettings.TryGetValue(name, out var setting) && setting.Value;
}

public sealed record WordSettingsGraphOptions
{
    public static WordSettingsGraphOptions Default { get; } = new();

    public int MaxSettingsPartBytes { get; init; } = 16 * 1024 * 1024;

    public int MaxDocumentVariables { get; init; } = 100_000;

    public int MaxDocumentVariableValueCharacters { get; init; } = 1_048_576;

    public int MaxCompatibilitySettings { get; init; } = 16_384;

    internal void Validate()
    {
        if (MaxSettingsPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSettingsPartBytes));
        }
        if (MaxDocumentVariables <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDocumentVariables));
        }
        if (MaxDocumentVariableValueCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxDocumentVariableValueCharacters)
            );
        }
        if (MaxCompatibilitySettings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCompatibilitySettings));
        }
    }
}

public sealed class WordSettingsGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string Word2010Namespace =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string RelationshipsTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelationshipsStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string SettingsRelationship =
        RelationshipsTransitionalNamespace + "/settings";
    private const string StrictSettingsRelationship =
        RelationshipsStrictNamespace + "/settings";
    private const string SettingsContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
    private const string CompatibilityModeUri =
        "http://schemas.microsoft.com/office/word";

    private static readonly IReadOnlySet<string> BooleanSettingNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "autoHyphenation",
            "bookFoldPrinting",
            "bookFoldRevPrinting",
            "doNotAutoCompressPictures",
            "doNotEmbedSmartTags",
            "doNotHyphenateCaps",
            "doNotIncludeSubdocsInStats",
            "doNotShadeFormData",
            "doNotTrackFormatting",
            "doNotTrackMoves",
            "embedSystemFonts",
            "embedTrueTypeFonts",
            "evenAndOddHeaders",
            "formsDesign",
            "gutterAtTop",
            "linkStyles",
            "mirrorMargins",
            "noPunctuationKerning",
            "printFormsData",
            "printFractionalCharacterWidth",
            "printPostScriptOverText",
            "removeDateAndTime",
            "removePersonalInformation",
            "saveFormsData",
            "saveInvalidXml",
            "savePreviewPicture",
            "saveSubsetFonts",
            "showEnvelope",
            "showXMLTags",
            "trackRevisions",
            "updateFields",
            "useXSLTWhenSaving",
        };

    private static readonly IReadOnlySet<string> TypedRootNames =
        new HashSet<string>(BooleanSettingNames, StringComparer.Ordinal)
        {
            "attachedTemplate",
            "compat",
            "decimalSymbol",
            "defaultImageDpi",
            "defaultTabStop",
            "documentProtection",
            "docVars",
            "listSeparator",
            "mailMerge",
            "themeFontLang",
            "view",
            "writeProtection",
            "zoom",
        };

    private readonly WordSettingsGraphOptions _options;

    public WordSettingsGraphBuilder(WordSettingsGraphOptions? options = null)
    {
        _options = options ?? WordSettingsGraphOptions.Default;
        _options.Validate();
    }

    public WordSettingsGraph Build(
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
            throw new WordSettingsProjectionException(
                "Settings graph requires package and semantic snapshots from the same document version."
            );
        }

        var part = ResolveSettingsPart(package, semanticDocument.MainPartUri);
        if (part is null)
        {
            return EmptyGraph(package.Fingerprint, semanticDocument.MainPartUri);
        }

        var source = ParseSettingsPart(part, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || !IsWordNamespace(root.Name.NamespaceName)
            || root.Name.LocalName != "settings"
        )
        {
            throw new WordSettingsProjectionException(
                "Word settings part does not have a w:settings root element."
            );
        }

        var w = root.Name.Namespace;
        var issues = new List<WordSettingsIssue>();
        var booleans = ParseBooleanSettings(root, w, source);
        var themeLanguages = ParseThemeFontLanguages(root, w, source);
        var compatibility = ParseCompatibility(root, w, source, issues);
        var documentProtection = ParseDocumentProtection(root, w, source, issues);
        var writeProtection = ParseWriteProtection(root, w, source, issues);
        var variables = ParseDocumentVariables(root, w, source, issues);
        var attachedTemplate = ParseAttachedTemplate(
            package,
            part.Uri,
            root,
            w,
            source,
            issues
        );
        var mailMerge = ParseMailMerge(
            package,
            part.Uri,
            root,
            w,
            source,
            issues
        );
        var view = ParseView(root, w);
        var decimalSymbol = ParseOptionalVal(root, w, "decimalSymbol", 16);
        var listSeparator = ParseOptionalVal(root, w, "listSeparator", 16);

        if (
            booleans.TryGetValue("saveSubsetFonts", out var subset)
            && subset.Value
            && (!booleans.TryGetValue("embedTrueTypeFonts", out var embed)
                || !embed.Value)
        )
        {
            issues.Add(
                new WordSettingsIssue(
                    "SETTINGS_FONT_SUBSETTING_INACTIVE",
                    WordSettingsIssueSeverity.Warning,
                    "saveSubsetFonts is enabled, but embedTrueTypeFonts is absent or false, so subsetting has no effect.",
                    "saveSubsetFonts"
                )
            );
        }

        var inventory = root.Elements()
            .GroupBy(QualifiedName, StringComparer.Ordinal)
            .Select(group => new WordSettingsElementInventory(group.Key, group.Count()))
            .OrderBy(item => item.QualifiedName, StringComparer.Ordinal)
            .ToArray();
        var unmodeled = root.Elements()
            .Where(element =>
                !IsTypedRootElement(element)
            )
            .Select(element => QualifiedName(element.Name))
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToArray();

        return new WordSettingsGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            part.Uri,
            booleans,
            themeLanguages,
            compatibility,
            documentProtection,
            writeProtection,
            variables,
            attachedTemplate,
            mailMerge,
            view,
            decimalSymbol,
            listSeparator,
            issues,
            inventory,
            unmodeled
        );
    }

    private static WordSettingsGraph EmptyGraph(
        string fingerprint,
        string mainPartUri
    ) => new(
        fingerprint,
        mainPartUri,
        null,
        new Dictionary<string, WordBooleanSetting>(StringComparer.Ordinal),
        null,
        null,
        null,
        null,
        Array.Empty<WordDocumentVariable>(),
        null,
        null,
        new WordViewSettings(null, null, null, null, null),
        null,
        null,
        Array.Empty<WordSettingsIssue>(),
        Array.Empty<WordSettingsElementInventory>(),
        Array.Empty<string>()
    );

    private static Dictionary<string, WordBooleanSetting> ParseBooleanSettings(
        XElement root,
        XNamespace w,
        LosslessXmlDocument source
    )
    {
        var result = new Dictionary<string, WordBooleanSetting>(StringComparer.Ordinal);
        foreach (var name in BooleanSettingNames)
        {
            var element = OptionalSingleChild(root, w + name);
            if (element is null)
            {
                continue;
            }
            result.Add(
                name,
                new WordBooleanSetting(
                    name,
                    ParseOnOff(element, name),
                    source.GetElementOrdinal(element)
                )
            );
        }
        return result;
    }

    private static WordThemeFontLanguages? ParseThemeFontLanguages(
        XElement root,
        XNamespace w,
        LosslessXmlDocument source
    )
    {
        var element = OptionalSingleChild(root, w + "themeFontLang");
        if (element is null)
        {
            return null;
        }
        var latin = OptionalBoundedAttribute(element, w + "val", 256);
        var eastAsian = OptionalBoundedAttribute(element, w + "eastAsia", 256);
        var complex = OptionalBoundedAttribute(element, w + "bidi", 256);
        return new WordThemeFontLanguages(
            latin,
            eastAsian,
            complex,
            source.GetElementOrdinal(element)
        );
    }

    private WordCompatibilityProfile? ParseCompatibility(
        XElement root,
        XNamespace w,
        LosslessXmlDocument source,
        List<WordSettingsIssue> issues
    )
    {
        var compat = OptionalSingleChild(root, w + "compat");
        if (compat is null)
        {
            return null;
        }

        var settings = new List<WordCompatibilitySetting>();
        var legacy = new List<WordLegacyCompatibilityOption>();
        var unmodeled = new List<string>();
        foreach (var child in compat.Elements())
        {
            if (settings.Count + legacy.Count >= _options.MaxCompatibilitySettings)
            {
                throw new WordSettingsLimitException(
                    "Compatibility settings exceed the configured item limit."
                );
            }
            if (!IsWordNamespace(child.Name.NamespaceName))
            {
                unmodeled.Add(QualifiedName(child.Name));
                continue;
            }
            if (child.Name.LocalName == "compatSetting")
            {
                settings.Add(
                    new WordCompatibilitySetting(
                        RequiredBoundedAttribute(child, w + "name", 256),
                        RequiredBoundedAttribute(child, w + "uri", 2_048),
                        RequiredBoundedAttribute(child, w + "val", 2_048),
                        source.GetElementOrdinal(child)
                    )
                );
                continue;
            }

            try
            {
                legacy.Add(
                    new WordLegacyCompatibilityOption(
                        child.Name.LocalName,
                        ParseOnOff(child, child.Name.LocalName),
                        source.GetElementOrdinal(child)
                    )
                );
            }
            catch (WordSettingsProjectionException exception)
            {
                unmodeled.Add(QualifiedName(child.Name));
                issues.Add(
                    new WordSettingsIssue(
                        "SETTINGS_COMPATIBILITY_OPTION_UNMODELED",
                        WordSettingsIssueSeverity.Warning,
                        $"Compatibility option '{child.Name.LocalName}' could not be interpreted as an on/off value: {exception.Message}",
                        child.Name.LocalName
                    )
                );
            }
        }

        int? mode = null;
        var modeCandidates = settings
            .Where(setting =>
                string.Equals(setting.Name, "compatibilityMode", StringComparison.Ordinal)
                && string.Equals(setting.Uri, CompatibilityModeUri, StringComparison.Ordinal)
            )
            .ToArray();
        var parsedModes = new List<int>();
        foreach (var candidate in modeCandidates)
        {
            if (
                int.TryParse(
                    candidate.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsed
                )
                && parsed >= 0
            )
            {
                parsedModes.Add(parsed);
            }
            else
            {
                issues.Add(
                    new WordSettingsIssue(
                        "SETTINGS_COMPATIBILITY_MODE_INVALID",
                        WordSettingsIssueSeverity.Error,
                        $"Compatibility mode value '{candidate.Value}' is not a non-negative integer.",
                        "compatSetting"
                    )
                );
            }
        }
        var distinctModes = parsedModes.Distinct().ToArray();
        if (distinctModes.Length == 1)
        {
            mode = distinctModes[0];
        }
        else if (distinctModes.Length > 1)
        {
            issues.Add(
                new WordSettingsIssue(
                    "SETTINGS_COMPATIBILITY_MODE_CONFLICT",
                    WordSettingsIssueSeverity.Error,
                    "The settings part declares conflicting compatibilityMode values.",
                    "compatSetting"
                )
            );
        }

        return new WordCompatibilityProfile(
            source.GetElementOrdinal(compat),
            settings,
            legacy,
            mode,
            unmodeled
        );
    }

    private static WordProtectionDescriptor? ParseDocumentProtection(
        XElement root,
        XNamespace w,
        LosslessXmlDocument source,
        List<WordSettingsIssue> issues
    )
    {
        var element = OptionalSingleChild(root, w + "documentProtection");
        if (element is null)
        {
            return null;
        }
        var spin = ParseOptionalNonNegativeInt(
            FirstAttributeByLocalName(element, "cryptSpinCount", "spinCount"),
            "documentProtection spin count",
            issues,
            "documentProtection"
        );
        var sid = ParseOptionalNonNegativeInt(
            OptionalAttribute(element, w + "cryptAlgorithmSid"),
            "documentProtection cryptographic algorithm SID",
            issues,
            "documentProtection"
        );
        var hasHash = HasNonEmptyAttribute(element, "hash", "hashValue");
        var hasSalt = HasNonEmptyAttribute(element, "salt", "saltValue");
        issues.Add(
            new WordSettingsIssue(
                "SETTINGS_DOCUMENT_PROTECTION_NOT_SECURITY_BOUNDARY",
                WordSettingsIssueSeverity.Info,
                "Word document protection is an editing restriction, not encryption or a security boundary.",
                "documentProtection"
            )
        );
        return new WordProtectionDescriptor(
            true,
            ParseOptionalOnOffAttribute(element, w + "enforcement", false),
            ParseOptionalOnOffAttribute(element, w + "formatting", false),
            OptionalBoundedAttribute(element, w + "edit", 128),
            OptionalBoundedAttributeByLocalName(element, "algorithmName", 256),
            OptionalBoundedAttribute(element, w + "cryptProviderType", 128),
            OptionalBoundedAttribute(element, w + "cryptAlgorithmClass", 128),
            OptionalBoundedAttribute(element, w + "cryptAlgorithmType", 128),
            sid,
            spin,
            hasHash,
            hasSalt,
            source.GetElementOrdinal(element),
            UnknownAttributes(
                element,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "algorithmName",
                    "cryptAlgorithmClass",
                    "cryptAlgorithmSid",
                    "cryptAlgorithmType",
                    "cryptProvider",
                    "cryptProviderType",
                    "cryptProviderTypeExt",
                    "cryptProviderTypeExtSource",
                    "cryptSpinCount",
                    "edit",
                    "enforcement",
                    "formatting",
                    "hash",
                    "hashValue",
                    "salt",
                    "saltValue",
                    "spinCount",
                }
            )
        );
    }

    private static WordWriteProtectionDescriptor? ParseWriteProtection(
        XElement root,
        XNamespace w,
        LosslessXmlDocument source,
        List<WordSettingsIssue> issues
    )
    {
        var element = OptionalSingleChild(root, w + "writeProtection");
        if (element is null)
        {
            return null;
        }
        return new WordWriteProtectionDescriptor(
            true,
            ParseOptionalOnOffAttribute(element, w + "recommended", false),
            OptionalBoundedAttributeByLocalName(element, "algorithmName", 256),
            ParseOptionalNonNegativeInt(
                FirstAttributeByLocalName(element, "cryptSpinCount", "spinCount"),
                "writeProtection spin count",
                issues,
                "writeProtection"
            ),
            HasNonEmptyAttribute(element, "hash", "hashValue"),
            HasNonEmptyAttribute(element, "salt", "saltValue"),
            source.GetElementOrdinal(element),
            UnknownAttributes(
                element,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "algorithmName",
                    "cryptAlgorithmClass",
                    "cryptAlgorithmSid",
                    "cryptAlgorithmType",
                    "cryptProvider",
                    "cryptProviderType",
                    "cryptProviderTypeExt",
                    "cryptProviderTypeExtSource",
                    "cryptSpinCount",
                    "hash",
                    "hashValue",
                    "recommended",
                    "salt",
                    "saltValue",
                    "spinCount",
                }
            )
        );
    }

    private IReadOnlyList<WordDocumentVariable> ParseDocumentVariables(
        XElement root,
        XNamespace w,
        LosslessXmlDocument source,
        List<WordSettingsIssue> issues
    )
    {
        var container = OptionalSingleChild(root, w + "docVars");
        if (container is null)
        {
            return Array.Empty<WordDocumentVariable>();
        }
        var elements = container.Elements(w + "docVar").ToArray();
        if (elements.Length > _options.MaxDocumentVariables)
        {
            throw new WordSettingsLimitException(
                "Document variables exceed the configured item limit."
            );
        }
        var result = elements.Select(element =>
        {
            var name = RequiredBoundedAttribute(element, w + "name", 2_048);
            var value = RequiredBoundedAttribute(
                element,
                w + "val",
                _options.MaxDocumentVariableValueCharacters,
                allowEmpty: true
            );
            return new WordDocumentVariable(
                name,
                value,
                source.GetElementOrdinal(element)
            );
        }).ToArray();
        foreach (
            var duplicate in result.GroupBy(
                variable => variable.Name,
                StringComparer.OrdinalIgnoreCase
            ).Where(group => group.Count() > 1)
        )
        {
            issues.Add(
                new WordSettingsIssue(
                    "SETTINGS_DOCUMENT_VARIABLE_DUPLICATE",
                    WordSettingsIssueSeverity.Warning,
                    $"Document variable name '{duplicate.Key}' is declared more than once.",
                    "docVar"
                )
            );
        }
        return result;
    }

    private static WordAttachedTemplate? ParseAttachedTemplate(
        OpcPackageSnapshot package,
        string settingsPartUri,
        XElement root,
        XNamespace w,
        LosslessXmlDocument source,
        List<WordSettingsIssue> issues
    )
    {
        var element = OptionalSingleChild(root, w + "attachedTemplate");
        if (element is null)
        {
            return null;
        }
        var id = RequiredRelationshipId(element);
        var relationship = ResolveRelationshipReference(
            package,
            settingsPartUri,
            id,
            issues,
            "attachedTemplate"
        );
        return new WordAttachedTemplate(
            relationship,
            source.GetElementOrdinal(element)
        );
    }

    private static WordMailMergeSettings? ParseMailMerge(
        OpcPackageSnapshot package,
        string settingsPartUri,
        XElement root,
        XNamespace w,
        LosslessXmlDocument source,
        List<WordSettingsIssue> issues
    )
    {
        var element = OptionalSingleChild(root, w + "mailMerge");
        if (element is null)
        {
            return null;
        }
        var dataSourceElement = OptionalSingleChild(element, w + "dataSource");
        var headerSourceElement = OptionalSingleChild(element, w + "headerSource");
        var dataSource = dataSourceElement is null
            ? null
            : ResolveRelationshipReference(
                package,
                settingsPartUri,
                RequiredRelationshipId(dataSourceElement),
                issues,
                "dataSource"
            );
        var headerSource = headerSourceElement is null
            ? null
            : ResolveRelationshipReference(
                package,
                settingsPartUri,
                RequiredRelationshipId(headerSourceElement),
                issues,
                "headerSource"
            );
        var known = new HashSet<XName>
        {
            w + "activeRecord",
            w + "addressFieldName",
            w + "checkErrors",
            w + "connectString",
            w + "dataSource",
            w + "dataType",
            w + "destination",
            w + "doNotSuppressBlankLines",
            w + "headerSource",
            w + "linkToQuery",
            w + "mailAsAttachment",
            w + "mainDocumentType",
            w + "odso",
            w + "query",
            w + "viewMergedData",
        };
        return new WordMailMergeSettings(
            OptionalChildVal(element, w, "mainDocumentType", 256),
            OptionalChildVal(element, w, "dataType", 256),
            OptionalSingleChild(element, w + "linkToQuery") is { } link
                && ParseOnOff(link, "linkToQuery"),
            OptionalChildVal(element, w, "query", 32_768),
            OptionalChildVal(element, w, "connectString", 32_768),
            OptionalSingleChild(element, w + "odso") is not null,
            dataSource,
            headerSource,
            source.GetElementOrdinal(element),
            element.Elements()
                .Where(child => !known.Contains(child.Name))
                .Select(child => QualifiedName(child.Name))
                .Distinct(StringComparer.Ordinal)
                .Order()
                .ToArray()
        );
    }

    private static WordViewSettings ParseView(XElement root, XNamespace w)
    {
        var view = OptionalSingleChild(root, w + "view");
        var zoom = OptionalSingleChild(root, w + "zoom");
        var defaultImageDpi = OptionalSingleChildInNamespaces(
            root,
            "defaultImageDpi",
            w.NamespaceName,
            Word2010Namespace
        );
        return new WordViewSettings(
            view is null ? null : OptionalBoundedAttribute(view, w + "val", 128),
            zoom is null ? null : OptionalBoundedAttribute(zoom, w + "val", 128),
            zoom is null
                ? null
                : ParseOptionalIntegerAttribute(zoom, w + "percent", "zoom percent"),
            ParseOptionalChildInteger(root, w, "defaultTabStop"),
            defaultImageDpi is null
                ? null
                : ParseOptionalIntegerAttribute(
                    defaultImageDpi,
                    defaultImageDpi.Name.Namespace + "val",
                    "defaultImageDpi"
                )
        );
    }

    private static string? ParseOptionalVal(
        XElement root,
        XNamespace w,
        string name,
        int maximumLength
    )
    {
        var element = OptionalSingleChild(root, w + name);
        return element is null
            ? null
            : OptionalBoundedAttribute(element, w + "val", maximumLength);
    }

    private static int? ParseOptionalChildInteger(
        XElement root,
        XNamespace w,
        string name
    )
    {
        var element = OptionalSingleChild(root, w + name);
        return element is null
            ? null
            : ParseOptionalIntegerAttribute(element, w + "val", name);
    }

    private static int? ParseOptionalIntegerAttribute(
        XElement element,
        XName name,
        string description
    )
    {
        var raw = OptionalAttribute(element, name);
        if (raw is null)
        {
            return null;
        }
        if (
            !int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
        {
            throw new WordSettingsProjectionException(
                $"{description} value '{raw}' is not an integer."
            );
        }
        return value;
    }

    private static int? ParseOptionalNonNegativeInt(
        string? raw,
        string description,
        List<WordSettingsIssue> issues,
        string elementName
    )
    {
        if (raw is null)
        {
            return null;
        }
        if (
            int.TryParse(
                raw,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed
            )
            && parsed >= 0
        )
        {
            return parsed;
        }
        issues.Add(
            new WordSettingsIssue(
                "SETTINGS_NUMERIC_VALUE_INVALID",
                WordSettingsIssueSeverity.Error,
                $"{description} value '{raw}' is not a non-negative integer.",
                elementName
            )
        );
        return null;
    }

    private static WordSettingsRelationshipReference ResolveRelationshipReference(
        OpcPackageSnapshot package,
        string sourcePartUri,
        string id,
        List<WordSettingsIssue> issues,
        string elementName
    )
    {
        var candidates = package.RelationshipsFrom(sourcePartUri)
            .Where(relationship => string.Equals(relationship.Id, id, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            issues.Add(
                new WordSettingsIssue(
                    "SETTINGS_RELATIONSHIP_UNRESOLVED",
                    WordSettingsIssueSeverity.Error,
                    candidates.Length == 0
                        ? $"Relationship '{id}' referenced by {elementName} is missing."
                        : $"Relationship '{id}' referenced by {elementName} is duplicated.",
                    elementName
                )
            );
            return new WordSettingsRelationshipReference(
                id,
                null,
                null,
                null,
                null,
                false
            );
        }
        var relationship = candidates[0];
        var resolved = relationship.TargetMode == OpcRelationshipTargetMode.External
            || relationship.ResolvedTargetPartUri is not null
                && package.Parts.ContainsKey(relationship.ResolvedTargetPartUri);
        if (!resolved)
        {
            issues.Add(
                new WordSettingsIssue(
                    "SETTINGS_RELATIONSHIP_TARGET_INVALID",
                    WordSettingsIssueSeverity.Error,
                    $"Relationship '{id}' referenced by {elementName} has no usable target.",
                    elementName
                )
            );
        }
        return new WordSettingsRelationshipReference(
            id,
            relationship.Type,
            relationship.Target,
            relationship.TargetMode,
            relationship.ResolvedTargetPartUri,
            resolved
        );
    }

    private static OpcPart? ResolveSettingsPart(
        OpcPackageSnapshot package,
        string mainPartUri
    )
    {
        var relationships = package.RelationshipsFrom(mainPartUri)
            .Where(relationship =>
                relationship.Type is SettingsRelationship or StrictSettingsRelationship
            )
            .ToArray();
        if (relationships.Length == 0)
        {
            return null;
        }
        if (relationships.Length != 1)
        {
            throw new WordSettingsProjectionException(
                "Main document part contains multiple settings relationships."
            );
        }
        var relationship = relationships[0];
        if (
            relationship.TargetMode != OpcRelationshipTargetMode.Internal
            || relationship.ResolvedTargetPartUri is null
            || !package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var part)
            || !string.Equals(
                part.ContentType,
                SettingsContentType,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new WordSettingsProjectionException(
                "Settings relationship does not resolve to a valid Word settings part."
            );
        }
        return part;
    }

    private LosslessXmlDocument ParseSettingsPart(
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
                    MaxSourceBytes = _options.MaxSettingsPartBytes,
                    MaxXmlCharacters = _options.MaxSettingsPartBytes,
                    MaxXmlElements = 262_144,
                    MaxXmlDepth = 128,
                    MaxTextCharacters = _options.MaxSettingsPartBytes,
                },
                cancellationToken
            );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordSettingsLimitException(
                "Word settings part exceeds a settings-graph XML limit: "
                    + exception.Message
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSettingsProjectionException(
                "Word settings part is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private static XElement? OptionalSingleChild(XElement parent, XName name)
    {
        var children = parent.Elements(name).Take(2).ToArray();
        if (children.Length > 1)
        {
            throw new WordSettingsProjectionException(
                $"Element '{parent.Name.LocalName}' contains duplicate '{name.LocalName}' children."
            );
        }
        return children.SingleOrDefault();
    }

    private static XElement? OptionalSingleChildInNamespaces(
        XElement parent,
        string localName,
        params string[] namespaceNames
    )
    {
        var accepted = namespaceNames.ToHashSet(StringComparer.Ordinal);
        var children = parent.Elements()
            .Where(element =>
                element.Name.LocalName == localName
                && accepted.Contains(element.Name.NamespaceName)
            )
            .Take(2)
            .ToArray();
        if (children.Length > 1)
        {
            throw new WordSettingsProjectionException(
                $"Element '{parent.Name.LocalName}' contains duplicate '{localName}' children."
            );
        }
        return children.SingleOrDefault();
    }

    private static string? OptionalChildVal(
        XElement parent,
        XNamespace w,
        string name,
        int maximumLength
    )
    {
        var child = OptionalSingleChild(parent, w + name);
        return child is null
            ? null
            : OptionalBoundedAttribute(child, w + "val", maximumLength);
    }

    private static bool ParseOnOff(XElement element, string description)
    {
        var raw = element.Attribute(element.Name.Namespace + "val")?.Value;
        return raw?.ToLowerInvariant() switch
        {
            null or "true" or "1" or "on" => true,
            "false" or "0" or "off" => false,
            _ => throw new WordSettingsProjectionException(
                $"Word setting '{description}' has invalid on/off value '{raw}'."
            ),
        };
    }

    private static bool ParseOptionalOnOffAttribute(
        XElement element,
        XName name,
        bool defaultValue
    )
    {
        var raw = element.Attribute(name)?.Value;
        return raw?.ToLowerInvariant() switch
        {
            null => defaultValue,
            "true" or "1" or "on" => true,
            "false" or "0" or "off" => false,
            _ => throw new WordSettingsProjectionException(
                $"Attribute '{name.LocalName}' has invalid on/off value '{raw}'."
            ),
        };
    }

    private static string RequiredRelationshipId(XElement element)
    {
        var values = element.Attributes()
            .Where(attribute =>
                attribute.Name.LocalName == "id"
                && attribute.Name.NamespaceName is RelationshipsTransitionalNamespace
                    or RelationshipsStrictNamespace
            )
            .Select(attribute => attribute.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            throw new WordSettingsProjectionException(
                $"Element '{element.Name.LocalName}' does not declare exactly one relationship ID."
            );
        }
        if (values[0].Length > 1_024)
        {
            throw new WordSettingsProjectionException(
                $"Relationship ID on '{element.Name.LocalName}' is too long."
            );
        }
        return values[0];
    }

    private static string RequiredBoundedAttribute(
        XElement element,
        XName name,
        int maximumLength,
        bool allowEmpty = false
    )
    {
        var value = element.Attribute(name)?.Value;
        if (
            value is null
            || (!allowEmpty && value.Length == 0)
            || value.Length > maximumLength
        )
        {
            throw new WordSettingsProjectionException(
                $"Attribute '{name.LocalName}' on '{element.Name.LocalName}' is missing or exceeds its bound."
            );
        }
        return value;
    }

    private static string? OptionalBoundedAttribute(
        XElement element,
        XName name,
        int maximumLength
    )
    {
        var value = element.Attribute(name)?.Value;
        if (value is not null && value.Length > maximumLength)
        {
            throw new WordSettingsProjectionException(
                $"Attribute '{name.LocalName}' on '{element.Name.LocalName}' exceeds its bound."
            );
        }
        return value;
    }

    private static string? OptionalBoundedAttributeByLocalName(
        XElement element,
        string localName,
        int maximumLength
    )
    {
        var attributes = element.Attributes()
            .Where(attribute =>
                !attribute.IsNamespaceDeclaration
                && attribute.Name.LocalName == localName
            )
            .Take(2)
            .ToArray();
        if (attributes.Length > 1)
        {
            throw new WordSettingsProjectionException(
                $"Element '{element.Name.LocalName}' contains ambiguous '{localName}' attributes."
            );
        }
        var value = attributes.SingleOrDefault()?.Value;
        if (value is not null && value.Length > maximumLength)
        {
            throw new WordSettingsProjectionException(
                $"Attribute '{localName}' on '{element.Name.LocalName}' exceeds its bound."
            );
        }
        return value;
    }

    private static string? OptionalAttribute(XElement element, XName name) =>
        element.Attribute(name)?.Value;

    private static string? FirstAttributeByLocalName(
        XElement element,
        params string[] names
    )
    {
        foreach (var name in names)
        {
            var value = element.Attributes()
                .FirstOrDefault(attribute =>
                    !attribute.IsNamespaceDeclaration
                    && attribute.Name.LocalName == name
                )
                ?.Value;
            if (value is not null)
            {
                return value;
            }
        }
        return null;
    }

    private static bool HasNonEmptyAttribute(
        XElement element,
        params string[] names
    ) => names.Any(name =>
        element.Attributes().Any(attribute =>
            !attribute.IsNamespaceDeclaration
            && attribute.Name.LocalName == name
            && !string.IsNullOrEmpty(attribute.Value)
        )
    );

    private static IReadOnlyList<string> UnknownAttributes(
        XElement element,
        IReadOnlySet<string> knownLocalNames
    ) => element.Attributes()
        .Where(attribute =>
            !attribute.IsNamespaceDeclaration
            && !knownLocalNames.Contains(attribute.Name.LocalName)
        )
        .Select(attribute => "@" + QualifiedName(attribute.Name))
        .Distinct(StringComparer.Ordinal)
        .Order()
        .ToArray();

    private static bool IsWordNamespace(string value) =>
        value is WordTransitionalNamespace or WordStrictNamespace;

    private static bool IsTypedRootElement(XElement element) =>
        IsWordNamespace(element.Name.NamespaceName)
            && TypedRootNames.Contains(element.Name.LocalName)
        || element.Name.NamespaceName == Word2010Namespace
            && element.Name.LocalName == "defaultImageDpi";

    private static string QualifiedName(XElement element) =>
        QualifiedName(element.Name);

    private static string QualifiedName(XName name) =>
        $"{{{name.NamespaceName}}}{name.LocalName}";
}

public class WordSettingsProjectionException : IOException
{
    public WordSettingsProjectionException(string message)
        : base(message)
    {
    }

    public WordSettingsProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordSettingsLimitException : WordSettingsProjectionException
{
    public WordSettingsLimitException(string message)
        : base(message)
    {
    }
}
