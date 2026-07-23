using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordBibliographyIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordBibliographyIssue(
    string Code,
    WordBibliographyIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? SourceId = null
);

public sealed record WordBibliographyField(
    string Name,
    string Value,
    int SourceElementOrdinal,
    bool IsKnown
);

public sealed record WordBibliographyPerson(
    string? Last,
    string? First,
    string? Middle,
    int SourceElementOrdinal
);

public sealed record WordBibliographyContributor(
    string Role,
    IReadOnlyList<WordBibliographyPerson> People,
    IReadOnlyList<string> CorporateNames,
    int SourceElementOrdinal
);

public sealed record WordBibliographySource(
    string Id,
    string CollectionId,
    string PartUri,
    int SourceElementOrdinal,
    string? Tag,
    string? SourceType,
    bool IsSourceTypeKnown,
    string? Guid,
    bool HasAmbiguousTag,
    bool HasAmbiguousSourceType,
    bool HasAmbiguousGuid,
    int? Lcid,
    string? Title,
    string? Year,
    IReadOnlyList<WordBibliographyField> Fields,
    IReadOnlyList<WordBibliographyContributor> Contributors,
    IReadOnlyList<string> UnmodeledElements,
    bool IsTagUnique,
    bool IsGuidUnique
);

public sealed record WordBibliographyCollection(
    string Id,
    string PartUri,
    string NamespaceUri,
    int SourceElementOrdinal,
    bool IsPackageReachable,
    int IncomingRelationshipCount,
    string? SelectedStyle,
    string? StyleName,
    string? Version,
    string? Uri,
    IReadOnlyList<string> SourceIds
);

public sealed class WordBibliographyGraph
{
    private readonly IReadOnlyDictionary<string, WordBibliographySource> _sourcesById;
    private readonly IReadOnlyDictionary<string, WordBibliographySource> _sourcesByTag;

    internal WordBibliographyGraph(
        string packageFingerprint,
        IReadOnlyList<WordBibliographyCollection> collections,
        IReadOnlyList<WordBibliographySource> sources,
        IReadOnlyList<WordBibliographyIssue> issues,
        bool issuesTruncated,
        int customXmlCandidateCount
    )
    {
        PackageFingerprint = packageFingerprint;
        Collections = new ReadOnlyCollection<WordBibliographyCollection>(collections.ToArray());
        Sources = new ReadOnlyCollection<WordBibliographySource>(sources.ToArray());
        Issues = new ReadOnlyCollection<WordBibliographyIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        CustomXmlCandidateCount = customXmlCandidateCount;
        _sourcesById = new ReadOnlyDictionary<string, WordBibliographySource>(
            sources.ToDictionary(source => source.Id, StringComparer.Ordinal)
        );
        _sourcesByTag = new ReadOnlyDictionary<string, WordBibliographySource>(
            sources
                .Where(source => source.IsTagUnique && !string.IsNullOrWhiteSpace(source.Tag))
                .ToDictionary(source => source.Tag!, StringComparer.OrdinalIgnoreCase)
        );
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordBibliographyCollection> Collections { get; }

    public IReadOnlyList<WordBibliographySource> Sources { get; }

    public IReadOnlyList<WordBibliographyIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public int CustomXmlCandidateCount { get; }

    public bool TryGetSource(string id, out WordBibliographySource? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _sourcesById.TryGetValue(id, out source);
    }

    public bool TryResolveCitationTag(string tag, out WordBibliographySource? source)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            source = null;
            return false;
        }
        return _sourcesByTag.TryGetValue(tag, out source);
    }
}

public sealed record WordBibliographyGraphOptions
{
    public static WordBibliographyGraphOptions Default { get; } = new();

    public int MaxCustomXmlCandidates { get; init; } = 2_048;

    public int MaxBibliographyParts { get; init; } = 256;

    public int MaxSources { get; init; } = 100_000;

    public int MaxFieldsPerSource { get; init; } = 256;

    public int MaxContributorsPerSource { get; init; } = 64;

    public int MaxPeoplePerSource { get; init; } = 1_024;

    public int MaxCorporateNamesPerSource { get; init; } = 1_024;

    public int MaxUnmodeledElementsPerSource { get; init; } = 256;

    public int MaxPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxElementsPerPart { get; init; } = 1_000_000;

    public int MaxValueCharacters { get; init; } = 32_768;

    public long MaxMetadataCharacters { get; init; } = 32L * 1024 * 1024;

    public int MaxIssues { get; init; } = 10_000;

    internal void Validate()
    {
        if (
            MaxCustomXmlCandidates <= 0
            || MaxBibliographyParts <= 0
            || MaxSources <= 0
            || MaxFieldsPerSource <= 0
            || MaxContributorsPerSource <= 0
            || MaxPeoplePerSource <= 0
            || MaxCorporateNamesPerSource <= 0
            || MaxUnmodeledElementsPerSource <= 0
            || MaxPartBytes <= 0
            || MaxElementsPerPart <= 0
            || MaxValueCharacters <= 0
            || MaxMetadataCharacters <= 0
            || MaxIssues <= 0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(WordBibliographyGraphOptions),
                "All bibliography graph limits must be positive."
            );
        }
    }
}

public sealed class WordBibliographyGraphBuilder
{
    public const string TransitionalBibliographyNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";
    public const string LegacyBibliographyNamespace =
        "http://schemas.microsoft.com/office/word/2004/10/bibliography";

    private static readonly HashSet<string> SupportedNamespaces =
        new(StringComparer.Ordinal)
        {
            TransitionalBibliographyNamespace,
            LegacyBibliographyNamespace,
        };

    private static readonly HashSet<string> SourceTypes = new(StringComparer.Ordinal)
    {
        "ArticleInAPeriodical",
        "Book",
        "BookSection",
        "JournalArticle",
        "ConferenceProceedings",
        "Report",
        "SoundRecording",
        "Performance",
        "Art",
        "DocumentFromInternetSite",
        "InternetSite",
        "Film",
        "Interview",
        "Patent",
        "ElectronicSource",
        "Case",
        "Misc",
    };

    private static readonly HashSet<string> ContributorRoles = new(StringComparer.Ordinal)
    {
        "Author",
        "BookAuthor",
        "Editor",
        "Translator",
        "Interviewer",
        "Interviewee",
        "ProducerName",
        "Composer",
        "Conductor",
        "Performer",
        "Writer",
        "Director",
        "Compiler",
        "Counsel",
        "Inventor",
        "Artist",
    };

    private static readonly HashSet<string> KnownScalarFields = new(StringComparer.Ordinal)
    {
        "AbbreviatedCaseNumber", "AlbumTitle", "Broadcaster", "BroadcastTitle",
        "CaseNumber", "ChapterNumber", "City", "Comments", "ConferenceName",
        "CountryRegion", "Court", "Day", "DayAccessed", "Department", "Distributor",
        "Edition", "Guid", "Institution", "InternetSiteTitle", "Issue", "JournalName",
        "LCID", "Medium", "Month", "MonthAccessed", "NumberVolumes", "Pages",
        "PatentNumber", "PeriodicalTitle", "ProductionCompany", "PublicationTitle",
        "Publisher", "RecordingNumber", "RefOrder", "Reporter", "ShortTitle",
        "SourceType", "StandardNumber", "StateProvince", "Station", "Tag", "Theater",
        "ThesisType", "Title", "Type", "URL", "Version", "Volume", "Year",
        "YearAccessed",
    };

    private readonly WordBibliographyGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordBibliographyGraphBuilder(WordBibliographyGraphOptions? options = null)
    {
        _options = options ?? WordBibliographyGraphOptions.Default;
        _options.Validate();
    }

    public WordBibliographyGraphBuilder(
        WordBibliographyGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordBibliographyGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordBibliographyGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.Bibliography
        );

        var issues = new IssueState(_options.MaxIssues);
        var candidateUris = CandidatePartUris(package, cancellationToken);
        if (candidateUris.Count > _options.MaxCustomXmlCandidates)
        {
            throw new WordBibliographyLimitException(
                $"Custom XML candidate count exceeds {_options.MaxCustomXmlCandidates}."
            );
        }
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.Bibliography,
            candidateUris.Count,
            128
        );

        var incomingCounts = package.Relationships
            .Where(relationship => relationship.TargetMode == OpcRelationshipTargetMode.Internal)
            .Where(relationship => relationship.ResolvedTargetPartUri is not null)
            .GroupBy(relationship => relationship.ResolvedTargetPartUri!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var reachable = PackageReachableParts(package, cancellationToken);
        var collections = new List<WordBibliographyCollection>();
        var sources = new List<WordBibliographySource>();
        long metadataCharacters = 0;

        foreach (var partUri in candidateUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                continue;
            }
            if (part.Entry.Content.Length > _options.MaxPartBytes)
            {
                throw new WordBibliographyLimitException(
                    $"Custom XML part '{part.Uri}' exceeds {_options.MaxPartBytes} bytes."
                );
            }

            LosslessXmlDocument document;
            try
            {
                var xmlOptions = LosslessXmlOptions.Default with
                {
                    MaxSourceBytes = _options.MaxPartBytes,
                    MaxXmlElements = _options.MaxElementsPerPart,
                };
                document = _resourceLease is null
                    ? LosslessXmlDocument.Parse(part.Entry.Content, xmlOptions, cancellationToken)
                    : LosslessXmlDocument.Parse(
                        part.Entry.Content,
                        xmlOptions,
                        _resourceLease,
                        WordOperationResourceStage.Bibliography,
                        cancellationToken
                    );
            }
            catch (LosslessXmlParseException)
            {
                issues.Add(new WordBibliographyIssue(
                    "BIB_CUSTOM_XML_NOT_WELL_FORMED",
                    WordBibliographyIssueSeverity.Warning,
                    "A custom XML candidate is not safe, well-formed XML and was not interpreted as bibliography data.",
                    part.Uri
                ));
                continue;
            }
            catch (LosslessXmlLimitException exception)
            {
                throw new WordBibliographyLimitException(
                    $"Custom XML part '{part.Uri}' exceeds a safe XML limit.",
                    exception
                );
            }
            catch (LosslessXmlException)
            {
                issues.Add(new WordBibliographyIssue(
                    "BIB_CUSTOM_XML_UNREADABLE",
                    WordBibliographyIssueSeverity.Warning,
                    "A custom XML candidate could not be decoded safely and was not interpreted as bibliography data.",
                    part.Uri
                ));
                continue;
            }

            var root = document.GetParsedElement(document.Root.Ordinal);
            if (
                root.Name.LocalName != "Sources"
                || !SupportedNamespaces.Contains(root.Name.NamespaceName)
            )
            {
                continue;
            }
            if (collections.Count >= _options.MaxBibliographyParts)
            {
                throw new WordBibliographyLimitException(
                    $"Bibliography part count exceeds {_options.MaxBibliographyParts}."
                );
            }

            var collectionId = StableId(
                "wbc_",
                part.Uri,
                root.Name.NamespaceName
            );
            var sourceIds = new List<string>();
            var sourceIndex = 0;
            foreach (var element in root.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element.Name.NamespaceName != root.Name.NamespaceName)
                {
                    issues.Add(new WordBibliographyIssue(
                        "BIB_COLLECTION_EXTENSION",
                        WordBibliographyIssueSeverity.Info,
                        "The bibliography collection contains a preserved extension element.",
                        part.Uri,
                        document.GetElementOrdinal(element)
                    ));
                    continue;
                }
                if (element.Name.LocalName != "Source")
                {
                    issues.Add(new WordBibliographyIssue(
                        "BIB_COLLECTION_CHILD_UNMODELED",
                        WordBibliographyIssueSeverity.Info,
                        "The bibliography collection contains an unmodeled child element.",
                        part.Uri,
                        document.GetElementOrdinal(element)
                    ));
                    continue;
                }
                if (sources.Count >= _options.MaxSources)
                {
                    throw new WordBibliographyLimitException(
                        $"Bibliography source count exceeds {_options.MaxSources}."
                    );
                }
                var source = ParseSource(
                    document,
                    element,
                    collectionId,
                    part.Uri,
                    sourceIndex++,
                    ref metadataCharacters,
                    issues,
                    cancellationToken
                );
                ChargeSource(source);
                sourceIds.Add(source.Id);
                sources.Add(source);
            }

            var selectedStyle = BoundedAttribute(root, "SelectedStyle", ref metadataCharacters);
            var styleName = BoundedAttribute(root, "StyleName", ref metadataCharacters);
            var version = BoundedAttribute(root, "Version", ref metadataCharacters);
            var uri = BoundedAttribute(root, "URI", ref metadataCharacters);
            var collection = new WordBibliographyCollection(
                collectionId,
                part.Uri,
                root.Name.NamespaceName,
                document.Root.Ordinal,
                reachable.Contains(part.Uri),
                incomingCounts.GetValueOrDefault(part.Uri),
                selectedStyle,
                styleName,
                version,
                uri,
                new ReadOnlyCollection<string>(sourceIds)
            );
            ChargeCollection(collection);
            collections.Add(collection);
        }

        var uniqueSources = AssignStableSourceIds(MarkUniqueIdentities(sources));
        var remappedSourceIds = sources.Zip(uniqueSources)
            .ToDictionary(pair => pair.First.Id, pair => pair.Second.Id, StringComparer.Ordinal);
        var remappedCollections = collections.Select(collection => collection with
        {
            SourceIds = new ReadOnlyCollection<string>(
                collection.SourceIds.Select(sourceId => remappedSourceIds[sourceId]).ToArray()
            ),
        }).ToArray();
        ApplyIdentityDiagnostics(uniqueSources, issues, cancellationToken);
        if (collections.Count > 1)
        {
            issues.Add(new WordBibliographyIssue(
                "BIB_MULTIPLE_COLLECTIONS",
                WordBibliographyIssueSeverity.Warning,
                "The package contains more than one bibliography source collection."
            ));
        }
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.Bibliography,
            issues.Items.Count,
            512
        );
        var sourceIdsByLocation = uniqueSources.ToDictionary(
            source => (source.PartUri, source.SourceElementOrdinal),
            source => source.Id
        );
        var linkedIssues = issues.Items.Select(issue =>
            issue.SourceId is null
                && issue.PartUri is not null
                && issue.SourceElementOrdinal is { } ordinal
                && sourceIdsByLocation.TryGetValue(
                    (issue.PartUri, ordinal),
                    out var sourceId
                )
                    ? issue with { SourceId = sourceId }
                    : issue
        ).ToArray();

        return new WordBibliographyGraph(
            package.Fingerprint,
            remappedCollections,
            uniqueSources,
            linkedIssues,
            issues.Truncated,
            candidateUris.Count
        );
    }

    private WordBibliographySource ParseSource(
        LosslessXmlDocument document,
        XElement element,
        string collectionId,
        string partUri,
        int sourceIndex,
        ref long metadataCharacters,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var sourceOrdinal = document.GetElementOrdinal(element);
        var fields = new List<WordBibliographyField>();
        var contributors = new List<WordBibliographyContributor>();
        var unmodeled = new HashSet<(string NamespaceUri, string LocalName)>();
        var peopleCount = 0;
        var corporateNameCount = 0;
        foreach (var child in element.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.Name.NamespaceName != element.Name.NamespaceName)
            {
                AddUnmodeledElement(
                    unmodeled,
                    child.Name,
                    qualifyNamespace: true,
                    ref metadataCharacters
                );
                continue;
            }
            if (ContributorRoles.Contains(child.Name.LocalName))
            {
                if (contributors.Count >= _options.MaxContributorsPerSource)
                {
                    throw new WordBibliographyLimitException(
                        $"A bibliography source exceeds {_options.MaxContributorsPerSource} contributors."
                    );
                }
                contributors.Add(ParseContributor(
                    document,
                    child,
                    ref peopleCount,
                    ref corporateNameCount,
                    ref metadataCharacters,
                    cancellationToken
                ));
                continue;
            }
            if (child.HasElements)
            {
                AddUnmodeledElement(
                    unmodeled,
                    child.Name,
                    qualifyNamespace: false,
                    ref metadataCharacters
                );
                continue;
            }
            if (fields.Count >= _options.MaxFieldsPerSource)
            {
                throw new WordBibliographyLimitException(
                    $"A bibliography source exceeds {_options.MaxFieldsPerSource} scalar fields."
                );
            }
            var value = BoundedValue(child.Value, child.Name.LocalName, ref metadataCharacters);
            fields.Add(new WordBibliographyField(
                child.Name.LocalName,
                value,
                document.GetElementOrdinal(child),
                KnownScalarFields.Contains(child.Name.LocalName)
            ));
            if (!KnownScalarFields.Contains(child.Name.LocalName))
            {
                AddUnmodeledElement(
                    unmodeled,
                    child.Name,
                    qualifyNamespace: false,
                    ref metadataCharacters
                );
            }
        }

        var tagFieldCount = fields.Count(field => field.Name == "Tag");
        var sourceTypeFieldCount = fields.Count(field => field.Name == "SourceType");
        var guidFieldCount = fields.Count(field => field.Name == "Guid");
        var lcidFieldCount = fields.Count(field => field.Name == "LCID");
        var titleFieldCount = fields.Count(field => field.Name == "Title");
        var yearFieldCount = fields.Count(field => field.Name == "Year");
        var firstTag = SingletonValue(fields, "Tag", partUri, sourceOrdinal, issues);
        var firstSourceType = SingletonValue(
            fields,
            "SourceType",
            partUri,
            sourceOrdinal,
            issues
        );
        var firstGuid = SingletonValue(fields, "Guid", partUri, sourceOrdinal, issues);
        var firstLcid = SingletonValue(fields, "LCID", partUri, sourceOrdinal, issues);
        var firstTitle = SingletonValue(fields, "Title", partUri, sourceOrdinal, issues);
        var firstYear = SingletonValue(fields, "Year", partUri, sourceOrdinal, issues);
        var tag = tagFieldCount == 1 ? firstTag : null;
        var sourceType = sourceTypeFieldCount == 1 ? firstSourceType : null;
        var rawGuid = guidFieldCount == 1 ? firstGuid : null;
        var rawLcid = lcidFieldCount == 1 ? firstLcid : null;
        var guid = NormalizeGuid(rawGuid, partUri, sourceOrdinal, issues);
        var lcid = ParseLcid(rawLcid, partUri, sourceOrdinal, issues);
        if (tagFieldCount <= 1 && string.IsNullOrWhiteSpace(tag))
        {
            issues.Add(new WordBibliographyIssue(
                "BIB_SOURCE_TAG_MISSING",
                WordBibliographyIssueSeverity.Error,
                "A bibliography source has no non-empty Tag and cannot satisfy a CITATION field.",
                partUri,
                sourceOrdinal
            ));
        }
        if (sourceTypeFieldCount <= 1 && string.IsNullOrWhiteSpace(sourceType))
        {
            issues.Add(new WordBibliographyIssue(
                "BIB_SOURCE_TYPE_MISSING",
                WordBibliographyIssueSeverity.Error,
                "A bibliography source has no SourceType.",
                partUri,
                sourceOrdinal
            ));
        }
        else if (
            sourceTypeFieldCount == 1
            && sourceType is not null
            && !SourceTypes.Contains(sourceType)
        )
        {
            issues.Add(new WordBibliographyIssue(
                "BIB_SOURCE_TYPE_UNKNOWN",
                WordBibliographyIssueSeverity.Warning,
                "A bibliography source declares an unknown SourceType.",
                partUri,
                sourceOrdinal
            ));
        }

        var id = StableId(
            "wbs_pending_",
            partUri,
            sourceIndex.ToString(CultureInfo.InvariantCulture)
        );
        return new WordBibliographySource(
            id,
            collectionId,
            partUri,
            sourceOrdinal,
            tag,
            sourceType,
            sourceType is not null && SourceTypes.Contains(sourceType),
            guid,
            HasAmbiguousTag: tagFieldCount > 1,
            HasAmbiguousSourceType: sourceTypeFieldCount > 1,
            HasAmbiguousGuid: guidFieldCount > 1,
            lcid,
            titleFieldCount == 1 ? firstTitle : null,
            yearFieldCount == 1 ? firstYear : null,
            new ReadOnlyCollection<WordBibliographyField>(fields),
            new ReadOnlyCollection<WordBibliographyContributor>(contributors),
            new ReadOnlyCollection<string>(
                unmodeled.Select(FormatUnmodeledElement)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            ),
            IsTagUnique: false,
            IsGuidUnique: false
        );
    }

    private WordBibliographyContributor ParseContributor(
        LosslessXmlDocument document,
        XElement element,
        ref int peopleCount,
        ref int corporateNameCount,
        ref long metadataCharacters,
        CancellationToken cancellationToken
    )
    {
        var people = new List<WordBibliographyPerson>();
        var corporate = new List<string>();
        foreach (var person in element.Descendants().Where(item =>
            item.Name.NamespaceName == element.Name.NamespaceName
            && item.Name.LocalName == "Person"
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (peopleCount >= _options.MaxPeoplePerSource)
            {
                throw new WordBibliographyLimitException(
                    $"A bibliography source exceeds {_options.MaxPeoplePerSource} people."
                );
            }
            peopleCount++;
            people.Add(new WordBibliographyPerson(
                DescendantLeafValue(person, "Last", ref metadataCharacters),
                DescendantLeafValue(person, "First", ref metadataCharacters),
                DescendantLeafValue(person, "Middle", ref metadataCharacters),
                document.GetElementOrdinal(person)
            ));
        }
        foreach (
            var item in element.Descendants().Where(item =>
                item.Name.NamespaceName == element.Name.NamespaceName
                && item.Name.LocalName == "Corporate"
                && !item.HasElements
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (corporateNameCount >= _options.MaxCorporateNamesPerSource)
            {
                throw new WordBibliographyLimitException(
                    $"A bibliography source exceeds {_options.MaxCorporateNamesPerSource} corporate names."
                );
            }
            corporateNameCount++;
            corporate.Add(BoundedValue(item.Value, "Corporate", ref metadataCharacters));
        }
        return new WordBibliographyContributor(
            element.Name.LocalName,
            new ReadOnlyCollection<WordBibliographyPerson>(people),
            new ReadOnlyCollection<string>(corporate),
            document.GetElementOrdinal(element)
        );
    }

    private void ChargeSource(WordBibliographySource source)
    {
        if (_resourceLease is null)
        {
            return;
        }
        long bytes = 2_048;
        bytes = checked(bytes + WordOperationResourceAccounting.AccountedStringBytes(source.Id));
        bytes = checked(
            bytes + WordOperationResourceAccounting.AccountedStringBytes(source.CollectionId)
        );
        bytes = checked(
            bytes + WordOperationResourceAccounting.AccountedStringBytes(source.PartUri)
        );
        bytes = checked(bytes + WordOperationResourceAccounting.AccountedStringBytes(source.Tag));
        bytes = checked(
            bytes + WordOperationResourceAccounting.AccountedStringBytes(source.SourceType)
        );
        bytes = checked(bytes + WordOperationResourceAccounting.AccountedStringBytes(source.Guid));
        bytes = checked(bytes + WordOperationResourceAccounting.AccountedStringBytes(source.Title));
        bytes = checked(bytes + WordOperationResourceAccounting.AccountedStringBytes(source.Year));
        foreach (var field in source.Fields)
        {
            bytes = checked(bytes + 192);
            bytes = checked(
                bytes + WordOperationResourceAccounting.AccountedStringBytes(field.Name)
            );
            bytes = checked(
                bytes + WordOperationResourceAccounting.AccountedStringBytes(field.Value)
            );
        }
        foreach (var contributor in source.Contributors)
        {
            bytes = checked(bytes + 256);
            bytes = checked(
                bytes + WordOperationResourceAccounting.AccountedStringBytes(contributor.Role)
            );
            foreach (var person in contributor.People)
            {
                bytes = checked(bytes + 256);
                bytes = checked(
                    bytes + WordOperationResourceAccounting.AccountedStringBytes(person.Last)
                );
                bytes = checked(
                    bytes + WordOperationResourceAccounting.AccountedStringBytes(person.First)
                );
                bytes = checked(
                    bytes + WordOperationResourceAccounting.AccountedStringBytes(person.Middle)
                );
            }
            foreach (var corporateName in contributor.CorporateNames)
            {
                bytes = checked(bytes + 128);
                bytes = checked(
                    bytes
                        + WordOperationResourceAccounting.AccountedStringBytes(corporateName)
                );
            }
        }
        foreach (var unmodeled in source.UnmodeledElements)
        {
            bytes = checked(bytes + 96);
            bytes = checked(
                bytes + WordOperationResourceAccounting.AccountedStringBytes(unmodeled)
            );
        }
        _resourceLease.Charge(WordOperationResourceStage.Bibliography, bytes);
    }

    private void ChargeCollection(WordBibliographyCollection collection)
    {
        if (_resourceLease is null)
        {
            return;
        }
        long bytes = 1_024;
        foreach (
            var value in new[]
            {
                collection.Id,
                collection.PartUri,
                collection.NamespaceUri,
                collection.SelectedStyle,
                collection.StyleName,
                collection.Version,
                collection.Uri,
            }
        )
        {
            bytes = checked(
                bytes + WordOperationResourceAccounting.AccountedStringBytes(value)
            );
        }
        foreach (var sourceId in collection.SourceIds)
        {
            bytes = checked(bytes + 32);
            bytes = checked(
                bytes + WordOperationResourceAccounting.AccountedStringBytes(sourceId)
            );
        }
        _resourceLease.Charge(WordOperationResourceStage.Bibliography, bytes);
    }

    private string? DescendantLeafValue(
        XElement parent,
        string localName,
        ref long metadataCharacters
    )
    {
        var values = parent.Elements()
            .Where(item =>
                item.Name.NamespaceName == parent.Name.NamespaceName
                && item.Name.LocalName == localName
                && !item.HasElements
            )
            .Select(item => item.Value)
            .ToArray();
        return values.Length == 0
            ? null
            : BoundedValue(values[0], localName, ref metadataCharacters);
    }

    private string BoundedValue(string value, string field, ref long metadataCharacters)
    {
        if (value.Length > _options.MaxValueCharacters)
        {
            throw new WordBibliographyLimitException(
                $"Bibliography value '{field}' exceeds {_options.MaxValueCharacters} characters."
            );
        }
        metadataCharacters = checked(metadataCharacters + value.Length);
        if (metadataCharacters > _options.MaxMetadataCharacters)
        {
            throw new WordBibliographyLimitException(
                $"Bibliography metadata exceeds {_options.MaxMetadataCharacters} characters."
            );
        }
        return value;
    }

    private void AddUnmodeledElement(
        HashSet<(string NamespaceUri, string LocalName)> elements,
        XName name,
        bool qualifyNamespace,
        ref long metadataCharacters
    )
    {
        var identity = (
            NamespaceUri: qualifyNamespace ? name.NamespaceName : string.Empty,
            name.LocalName
        );
        if (elements.Contains(identity))
        {
            return;
        }
        if (elements.Count >= _options.MaxUnmodeledElementsPerSource)
        {
            throw new WordBibliographyLimitException(
                $"A bibliography source exceeds {_options.MaxUnmodeledElementsPerSource} unique unmodeled element names."
            );
        }
        var characters = checked(
            identity.NamespaceUri.Length
                + identity.LocalName.Length
                + (identity.NamespaceUri.Length == 0 ? 0 : 2)
        );
        if (characters > _options.MaxValueCharacters)
        {
            throw new WordBibliographyLimitException(
                $"An unmodeled bibliography element name exceeds {_options.MaxValueCharacters} characters."
            );
        }
        metadataCharacters = checked(metadataCharacters + characters);
        if (metadataCharacters > _options.MaxMetadataCharacters)
        {
            throw new WordBibliographyLimitException(
                $"Bibliography metadata exceeds {_options.MaxMetadataCharacters} characters."
            );
        }
        elements.Add(identity);
    }

    private static string FormatUnmodeledElement(
        (string NamespaceUri, string LocalName) name
    ) => name.NamespaceUri.Length == 0
        ? name.LocalName
        : "{" + name.NamespaceUri + "}" + name.LocalName;

    private string? BoundedAttribute(XElement element, string name, ref long metadataCharacters)
    {
        var value = element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0
                && attribute.Name.LocalName == name
            )
            ?.Value;
        return value is null ? null : BoundedValue(value, name, ref metadataCharacters);
    }

    private static string? SingletonValue(
        IReadOnlyList<WordBibliographyField> fields,
        string name,
        string partUri,
        int sourceOrdinal,
        IssueState issues
    )
    {
        var matches = fields.Where(field => field.Name == name).ToArray();
        if (matches.Length > 1)
        {
            issues.Add(new WordBibliographyIssue(
                "BIB_SOURCE_FIELD_DUPLICATE",
                WordBibliographyIssueSeverity.Error,
                $"A bibliography source contains duplicate {name} fields.",
                partUri,
                sourceOrdinal
            ));
        }
        return matches.FirstOrDefault()?.Value;
    }

    private static string? NormalizeGuid(
        string? value,
        string partUri,
        int sourceOrdinal,
        IssueState issues
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (System.Guid.TryParse(value, out var parsed))
        {
            return parsed.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
        }
        issues.Add(new WordBibliographyIssue(
            "BIB_SOURCE_GUID_INVALID",
            WordBibliographyIssueSeverity.Warning,
            "A bibliography source Guid is not a valid GUID.",
            partUri,
            sourceOrdinal
        ));
        return null;
    }

    private static int? ParseLcid(
        string? value,
        string partUri,
        int sourceOrdinal,
        IssueState issues
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var lcid)
            && lcid >= 0
        )
        {
            return lcid;
        }
        issues.Add(new WordBibliographyIssue(
            "BIB_SOURCE_LCID_INVALID",
            WordBibliographyIssueSeverity.Warning,
            "A bibliography source LCID is not a non-negative integer.",
            partUri,
            sourceOrdinal
        ));
        return null;
    }

    private static void ApplyIdentityDiagnostics(
        IReadOnlyList<WordBibliographySource> sources,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var group in sources
                .Where(source => !string.IsNullOrWhiteSpace(source.Tag))
                .GroupBy(source => source.Tag!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
        )
        {
            foreach (var source in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                issues.Add(new WordBibliographyIssue(
                    "BIB_SOURCE_TAG_DUPLICATE",
                    WordBibliographyIssueSeverity.Error,
                    "A bibliography source Tag is duplicated case-insensitively and cannot be resolved unambiguously.",
                    source.PartUri,
                    source.SourceElementOrdinal,
                    source.Id
                ));
            }
        }
        foreach (
            var group in sources
                .Where(source => source.Guid is not null)
                .GroupBy(source => source.Guid!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            foreach (var source in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                issues.Add(new WordBibliographyIssue(
                    "BIB_SOURCE_GUID_DUPLICATE",
                    WordBibliographyIssueSeverity.Warning,
                    "A bibliography source Guid is duplicated.",
                    source.PartUri,
                    source.SourceElementOrdinal,
                    source.Id
                ));
            }
        }
    }

    private static IReadOnlyList<WordBibliographySource> MarkUniqueIdentities(
        IReadOnlyList<WordBibliographySource> sources
    )
    {
        var tagCounts = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Tag))
            .GroupBy(source => source.Tag!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var guidCounts = sources
            .Where(source => source.Guid is not null)
            .GroupBy(source => source.Guid!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return sources.Select(source => source with
        {
            IsTagUnique = source.Tag is not null && tagCounts.GetValueOrDefault(source.Tag) == 1,
            IsGuidUnique = source.Guid is not null && guidCounts.GetValueOrDefault(source.Guid) == 1,
        })
            .ToArray();
    }

    private static IReadOnlyList<WordBibliographySource> AssignStableSourceIds(
        IReadOnlyList<WordBibliographySource> sources
    )
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        return sources.Select(source =>
        {
            var identity = source.IsGuidUnique
                ? "guid:" + source.Guid
                : source.IsTagUnique
                    ? "tag:" + source.Tag!.ToUpperInvariant()
                    : source.Guid is not null
                        ? "ambiguous-guid:" + source.Guid
                            + "|tag:" + source.Tag?.ToUpperInvariant()
                        : !string.IsNullOrWhiteSpace(source.Tag)
                            ? "ambiguous-tag:" + source.Tag.ToUpperInvariant()
                            : "ordinal:" + source.SourceElementOrdinal.ToString(
                                CultureInfo.InvariantCulture
                            );
            var occurrenceKey = source.PartUri + "\u001f" + identity;
            var occurrence = occurrences.TryGetValue(occurrenceKey, out var previous)
                ? previous + 1
                : 1;
            occurrences[occurrenceKey] = occurrence;
            return source with
            {
                Id = StableId(
                    "wbs_",
                    source.PartUri,
                    identity,
                    occurrence.ToString(CultureInfo.InvariantCulture)
                ),
            };
        }).ToArray();
    }

    private static IReadOnlyList<string> CandidatePartUris(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in package.Relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.ResolvedTargetPartUri is not null
                && RelationshipKind(relationship.Type) == "customXml"
            )
            {
                candidates.Add(relationship.ResolvedTargetPartUri);
            }
            if (RelationshipKind(relationship.Type) == "customXmlProps")
            {
                candidates.Add(relationship.SourcePartUri);
            }
        }
        foreach (
            var part in package.Parts.Values.Where(part =>
                part.Uri.StartsWith("/customXml/", StringComparison.OrdinalIgnoreCase)
                && part.Uri.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !part.Uri.Contains("itemProps", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            candidates.Add(part.Uri);
        }
        return candidates.Order(StringComparer.Ordinal).ToArray();
    }

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
                    relationship.TargetMode != OpcRelationshipTargetMode.Internal
                    || relationship.ResolvedTargetPartUri is not { } target
                    || !package.Parts.ContainsKey(target)
                    || !reachable.Add(target)
                )
                {
                    continue;
                }
                queue.Enqueue(target);
            }
        }
        return reachable;
    }

    private static string RelationshipKind(string relationshipType)
    {
        var index = relationshipType.LastIndexOf('/');
        return index < 0 ? relationshipType : relationshipType[(index + 1)..];
    }

    private static string StableId(string prefix, params string[] components)
    {
        var material = string.Join("\u001f", components);
        return prefix
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
                .ToLowerInvariant()[..24];
    }

    private sealed class IssueState
    {
        private readonly int _maximum;
        private readonly List<WordBibliographyIssue> _items = [];

        public IssueState(int maximum) => _maximum = maximum;

        public IReadOnlyList<WordBibliographyIssue> Items => _items;

        public bool Truncated { get; private set; }

        public void Add(WordBibliographyIssue issue)
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
}

public sealed class WordBibliographyLimitException : IOException
{
    public WordBibliographyLimitException(string message)
        : base(message) { }

    public WordBibliographyLimitException(string message, Exception innerException)
        : base(message, innerException) { }
}
