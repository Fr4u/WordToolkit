using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordNumberingIssueSeverity
{
    Warning,
    Error,
}

public sealed record WordNumberingIssue(
    string Code,
    WordNumberingIssueSeverity Severity,
    string Message,
    int? AbstractNumberId = null,
    int? NumberId = null,
    int? LevelIndex = null
);

public enum WordNumberingLevelSourceKind
{
    AbstractDefinition,
    InstanceOverride,
}

public enum WordNumberingStartSourceKind
{
    Unspecified,
    LevelDefinition,
    InstanceStartOverride,
}

public sealed class WordNumberingLevelDefinition
{
    internal WordNumberingLevelDefinition(
        int levelIndex,
        int? start,
        string? numberFormat,
        string? customNumberFormat,
        int? restartAfterLevel,
        string? paragraphStyleId,
        bool? isLegal,
        string? suffix,
        string? levelText,
        bool? levelTextIsNull,
        int? pictureBulletId,
        string? justification,
        string? templateCode,
        bool? tentative,
        int sourceElementOrdinal,
        WordStylePropertySet paragraphProperties,
        WordStylePropertySet runProperties,
        IReadOnlyList<string> unmodeledElements
    )
    {
        LevelIndex = levelIndex;
        Start = start;
        NumberFormat = numberFormat;
        CustomNumberFormat = customNumberFormat;
        RestartAfterLevel = restartAfterLevel;
        ParagraphStyleId = paragraphStyleId;
        IsLegal = isLegal;
        Suffix = suffix;
        LevelText = levelText;
        LevelTextIsNull = levelTextIsNull;
        PictureBulletId = pictureBulletId;
        Justification = justification;
        TemplateCode = templateCode;
        Tentative = tentative;
        SourceElementOrdinal = sourceElementOrdinal;
        ParagraphProperties = paragraphProperties;
        RunProperties = runProperties;
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public int LevelIndex { get; }

    public int? Start { get; }

    public string? NumberFormat { get; }

    public string? CustomNumberFormat { get; }

    public int? RestartAfterLevel { get; }

    public string? ParagraphStyleId { get; }

    public bool? IsLegal { get; }

    public string? Suffix { get; }

    public string? LevelText { get; }

    public bool? LevelTextIsNull { get; }

    public int? PictureBulletId { get; }

    public string? Justification { get; }

    public string? TemplateCode { get; }

    public bool? Tentative { get; }

    public int SourceElementOrdinal { get; }

    public WordStylePropertySet ParagraphProperties { get; }

    public WordStylePropertySet RunProperties { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public bool IsFullyModeled => UnmodeledElements.Count == 0
        && ParagraphProperties.IsFullyModeled
        && RunProperties.IsFullyModeled;
}

public sealed class WordNumberingLevelOverride
{
    internal WordNumberingLevelOverride(
        int levelIndex,
        int? startOverride,
        int? startOverrideSourceElementOrdinal,
        WordNumberingLevelDefinition? level,
        int sourceElementOrdinal,
        IReadOnlyList<string> unmodeledElements
    )
    {
        LevelIndex = levelIndex;
        StartOverride = startOverride;
        StartOverrideSourceElementOrdinal = startOverrideSourceElementOrdinal;
        Level = level;
        SourceElementOrdinal = sourceElementOrdinal;
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public int LevelIndex { get; }

    public int? StartOverride { get; }

    public int? StartOverrideSourceElementOrdinal { get; }

    public WordNumberingLevelDefinition? Level { get; }

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }
}

public sealed class WordAbstractNumberingDefinition
{
    private readonly IReadOnlyDictionary<int, WordNumberingLevelDefinition> _levels;

    internal WordAbstractNumberingDefinition(
        int abstractNumberId,
        string? namespaceId,
        string? multiLevelType,
        string? name,
        string? templateCode,
        string? numberingStyleLinkId,
        string? styleLinkId,
        int sourceElementOrdinal,
        IReadOnlyList<WordNumberingLevelDefinition> levels,
        IReadOnlyList<string> unmodeledElements
    )
    {
        AbstractNumberId = abstractNumberId;
        NamespaceId = namespaceId;
        MultiLevelType = multiLevelType;
        Name = name;
        TemplateCode = templateCode;
        NumberingStyleLinkId = numberingStyleLinkId;
        StyleLinkId = styleLinkId;
        SourceElementOrdinal = sourceElementOrdinal;
        Levels = new ReadOnlyCollection<WordNumberingLevelDefinition>(
            levels.OrderBy(level => level.LevelIndex).ToArray()
        );
        _levels = new ReadOnlyDictionary<int, WordNumberingLevelDefinition>(
            Levels.ToDictionary(level => level.LevelIndex)
        );
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public int AbstractNumberId { get; }

    public string? NamespaceId { get; }

    public string? MultiLevelType { get; }

    public string? Name { get; }

    public string? TemplateCode { get; }

    public string? NumberingStyleLinkId { get; }

    public string? StyleLinkId { get; }

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<WordNumberingLevelDefinition> Levels { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public bool TryGetLevel(int levelIndex, out WordNumberingLevelDefinition? level) =>
        _levels.TryGetValue(levelIndex, out level);
}

public sealed class WordNumberingInstance
{
    private readonly IReadOnlyDictionary<int, WordNumberingLevelOverride> _overrides;

    internal WordNumberingInstance(
        int numberId,
        int abstractNumberId,
        int sourceElementOrdinal,
        IReadOnlyList<WordNumberingLevelOverride> overrides,
        IReadOnlyList<string> unmodeledElements
    )
    {
        NumberId = numberId;
        AbstractNumberId = abstractNumberId;
        SourceElementOrdinal = sourceElementOrdinal;
        LevelOverrides = new ReadOnlyCollection<WordNumberingLevelOverride>(
            overrides.OrderBy(item => item.LevelIndex).ToArray()
        );
        _overrides = new ReadOnlyDictionary<int, WordNumberingLevelOverride>(
            LevelOverrides.ToDictionary(item => item.LevelIndex)
        );
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public int NumberId { get; }

    public int AbstractNumberId { get; }

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<WordNumberingLevelOverride> LevelOverrides { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public bool TryGetLevelOverride(
        int levelIndex,
        out WordNumberingLevelOverride? levelOverride
    ) => _overrides.TryGetValue(levelIndex, out levelOverride);
}

public sealed class WordNumberingPictureBullet
{
    internal WordNumberingPictureBullet(
        int pictureBulletId,
        int sourceElementOrdinal,
        IReadOnlyList<string> relationshipIds
    )
    {
        PictureBulletId = pictureBulletId;
        SourceElementOrdinal = sourceElementOrdinal;
        RelationshipIds = new ReadOnlyCollection<string>(
            relationshipIds.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public int PictureBulletId { get; }

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<string> RelationshipIds { get; }
}

public sealed class WordAbstractNumberingResolution
{
    internal WordAbstractNumberingResolution(
        int requestedAbstractNumberId,
        int? effectiveAbstractNumberId,
        bool resolvable,
        string? failure,
        IReadOnlyList<int> abstractNumberChain,
        IReadOnlyList<string> numberingStyleChain
    )
    {
        RequestedAbstractNumberId = requestedAbstractNumberId;
        EffectiveAbstractNumberId = effectiveAbstractNumberId;
        Resolvable = resolvable;
        Failure = failure;
        AbstractNumberChain = new ReadOnlyCollection<int>(abstractNumberChain.ToArray());
        NumberingStyleChain = new ReadOnlyCollection<string>(numberingStyleChain.ToArray());
    }

    public int RequestedAbstractNumberId { get; }

    public int? EffectiveAbstractNumberId { get; }

    public bool Resolvable { get; }

    public string? Failure { get; }

    public IReadOnlyList<int> AbstractNumberChain { get; }

    public IReadOnlyList<string> NumberingStyleChain { get; }
}

public sealed class WordResolvedNumberingLevel
{
    internal WordResolvedNumberingLevel(
        int numberId,
        int requestedAbstractNumberId,
        int effectiveAbstractNumberId,
        int levelIndex,
        WordNumberingLevelDefinition level,
        WordNumberingLevelSourceKind levelSourceKind,
        int? effectiveStart,
        WordNumberingStartSourceKind startSourceKind,
        int sourceElementOrdinal,
        int? startSourceElementOrdinal,
        IReadOnlyList<int> abstractNumberChain,
        IReadOnlyList<string> numberingStyleChain
    )
    {
        NumberId = numberId;
        RequestedAbstractNumberId = requestedAbstractNumberId;
        EffectiveAbstractNumberId = effectiveAbstractNumberId;
        LevelIndex = levelIndex;
        Level = level;
        LevelSourceKind = levelSourceKind;
        EffectiveStart = effectiveStart;
        StartSourceKind = startSourceKind;
        SourceElementOrdinal = sourceElementOrdinal;
        StartSourceElementOrdinal = startSourceElementOrdinal;
        AbstractNumberChain = new ReadOnlyCollection<int>(abstractNumberChain.ToArray());
        NumberingStyleChain = new ReadOnlyCollection<string>(numberingStyleChain.ToArray());
    }

    public int NumberId { get; }

    public int RequestedAbstractNumberId { get; }

    public int EffectiveAbstractNumberId { get; }

    public int LevelIndex { get; }

    public WordNumberingLevelDefinition Level { get; }

    public WordNumberingLevelSourceKind LevelSourceKind { get; }

    public int? EffectiveStart { get; }

    public WordNumberingStartSourceKind StartSourceKind { get; }

    public int SourceElementOrdinal { get; }

    public int? StartSourceElementOrdinal { get; }

    public IReadOnlyList<int> AbstractNumberChain { get; }

    public IReadOnlyList<string> NumberingStyleChain { get; }
}

public sealed class WordNumberingGraph
{
    private readonly IReadOnlyDictionary<int, WordAbstractNumberingDefinition>
        _abstractDefinitions;
    private readonly IReadOnlyDictionary<int, WordNumberingInstance> _instances;
    private readonly IReadOnlyDictionary<int, WordNumberingPictureBullet> _pictureBullets;
    private readonly IReadOnlyDictionary<int, WordAbstractNumberingResolution>
        _abstractResolutions;

    internal WordNumberingGraph(
        string packageFingerprint,
        string mainPartUri,
        string? numberingPartUri,
        IReadOnlyList<WordAbstractNumberingDefinition> abstractDefinitions,
        IReadOnlyList<WordNumberingInstance> instances,
        IReadOnlyList<WordNumberingPictureBullet> pictureBullets,
        int? lastAssignedNumberId,
        IReadOnlyDictionary<int, WordAbstractNumberingResolution> abstractResolutions,
        IReadOnlyList<WordNumberingIssue> issues,
        IReadOnlyList<string> unmodeledRootElements
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        NumberingPartUri = numberingPartUri;
        AbstractDefinitions = new ReadOnlyCollection<WordAbstractNumberingDefinition>(
            abstractDefinitions.OrderBy(item => item.AbstractNumberId).ToArray()
        );
        Instances = new ReadOnlyCollection<WordNumberingInstance>(
            instances.OrderBy(item => item.NumberId).ToArray()
        );
        PictureBullets = new ReadOnlyCollection<WordNumberingPictureBullet>(
            pictureBullets.OrderBy(item => item.PictureBulletId).ToArray()
        );
        LastAssignedNumberId = lastAssignedNumberId;
        Issues = new ReadOnlyCollection<WordNumberingIssue>(issues.ToArray());
        UnmodeledRootElements = new ReadOnlyCollection<string>(
            unmodeledRootElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        _abstractDefinitions = new ReadOnlyDictionary<
            int,
            WordAbstractNumberingDefinition
        >(AbstractDefinitions.ToDictionary(item => item.AbstractNumberId));
        _instances = new ReadOnlyDictionary<int, WordNumberingInstance>(
            Instances.ToDictionary(item => item.NumberId)
        );
        _pictureBullets = new ReadOnlyDictionary<int, WordNumberingPictureBullet>(
            PictureBullets.ToDictionary(item => item.PictureBulletId)
        );
        _abstractResolutions = new ReadOnlyDictionary<
            int,
            WordAbstractNumberingResolution
        >(new Dictionary<int, WordAbstractNumberingResolution>(abstractResolutions));
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public string? NumberingPartUri { get; }

    public bool HasNumberingPart => NumberingPartUri is not null;

    public IReadOnlyList<WordAbstractNumberingDefinition> AbstractDefinitions { get; }

    public IReadOnlyList<WordNumberingInstance> Instances { get; }

    public IReadOnlyList<WordNumberingPictureBullet> PictureBullets { get; }

    public int? LastAssignedNumberId { get; }

    public IReadOnlyList<WordNumberingIssue> Issues { get; }

    public IReadOnlyList<string> UnmodeledRootElements { get; }

    public bool TryGetAbstractDefinition(
        int abstractNumberId,
        out WordAbstractNumberingDefinition? definition
    ) => _abstractDefinitions.TryGetValue(abstractNumberId, out definition);

    public bool TryGetInstance(int numberId, out WordNumberingInstance? instance) =>
        _instances.TryGetValue(numberId, out instance);

    public bool TryGetPictureBullet(
        int pictureBulletId,
        out WordNumberingPictureBullet? pictureBullet
    ) => _pictureBullets.TryGetValue(pictureBulletId, out pictureBullet);

    public bool TryGetAbstractResolution(
        int abstractNumberId,
        out WordAbstractNumberingResolution? resolution
    ) => _abstractResolutions.TryGetValue(abstractNumberId, out resolution);

    public int? FindLevelIndexForParagraphStyle(
        int numberId,
        string paragraphStyleId
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paragraphStyleId);
        if (numberId <= 0)
        {
            throw new WordNumberingResolutionException(
                "A paragraph-style level lookup requires a positive numbering ID."
            );
        }

        if (!_instances.TryGetValue(numberId, out var instance))
        {
            throw new WordNumberingResolutionException(
                $"Numbering instance '{numberId}' does not exist."
            );
        }

        if (
            !_abstractResolutions.TryGetValue(instance.AbstractNumberId, out var resolution)
            || !resolution.Resolvable
            || resolution.EffectiveAbstractNumberId is not { } effectiveAbstractNumberId
            || !_abstractDefinitions.TryGetValue(
                effectiveAbstractNumberId,
                out var definition
            )
        )
        {
            throw new WordNumberingResolutionException(
                resolution?.Failure
                    ?? $"Abstract numbering definition '{instance.AbstractNumberId}' cannot be resolved."
            );
        }

        var matches = new List<int>();
        for (var levelIndex = 0; levelIndex <= 8; levelIndex++)
        {
            instance.TryGetLevelOverride(levelIndex, out var levelOverride);
            if (
                levelOverride?.Level is { } overrideLevel
                && overrideLevel.LevelIndex != levelIndex
            )
            {
                throw new WordNumberingResolutionException(
                    $"Numbering instance '{numberId}' has a mismatched override for level {levelIndex}."
                );
            }

            var level = levelOverride?.Level;
            if (level is null)
            {
                definition.TryGetLevel(levelIndex, out level);
            }

            if (
                level?.ParagraphStyleId is { } candidate
                && string.Equals(candidate, paragraphStyleId, StringComparison.Ordinal)
            )
            {
                matches.Add(levelIndex);
            }
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new WordNumberingResolutionException(
                $"Numbering instance '{numberId}' maps paragraph style '{paragraphStyleId}' to multiple levels: {string.Join(", ", matches)}."
            ),
        };
    }

    public WordResolvedNumberingLevel ResolveLevel(int numberId, int levelIndex)
    {
        if (numberId == 0)
        {
            throw new WordNumberingResolutionException(
                "Numbering ID 0 removes numbering and cannot resolve to a numbering level."
            );
        }

        if (levelIndex is < 0 or > 8)
        {
            throw new WordNumberingResolutionException(
                $"Numbering level {levelIndex} is outside Word's supported range 0 through 8."
            );
        }

        if (!_instances.TryGetValue(numberId, out var instance))
        {
            throw new WordNumberingResolutionException(
                $"Numbering instance '{numberId}' does not exist."
            );
        }

        if (
            !_abstractResolutions.TryGetValue(instance.AbstractNumberId, out var resolution)
            || !resolution.Resolvable
            || resolution.EffectiveAbstractNumberId is not { } effectiveAbstractNumberId
        )
        {
            throw new WordNumberingResolutionException(
                resolution?.Failure
                    ?? $"Abstract numbering definition '{instance.AbstractNumberId}' cannot be resolved."
            );
        }

        if (!_abstractDefinitions.TryGetValue(effectiveAbstractNumberId, out var definition))
        {
            throw new WordNumberingResolutionException(
                $"Resolved abstract numbering definition '{effectiveAbstractNumberId}' is missing."
            );
        }

        instance.TryGetLevelOverride(levelIndex, out var levelOverride);
        if (
            levelOverride?.Level is { } overrideLevel
            && overrideLevel.LevelIndex != levelIndex
        )
        {
            throw new WordNumberingResolutionException(
                $"Numbering instance '{numberId}' has a mismatched override for level {levelIndex}."
            );
        }

        WordNumberingLevelDefinition? level;
        WordNumberingLevelSourceKind levelSourceKind;
        int sourceElementOrdinal;
        if (levelOverride?.Level is { } replacement)
        {
            level = replacement;
            levelSourceKind = WordNumberingLevelSourceKind.InstanceOverride;
            sourceElementOrdinal = replacement.SourceElementOrdinal;
        }
        else
        {
            definition.TryGetLevel(levelIndex, out level);
            levelSourceKind = WordNumberingLevelSourceKind.AbstractDefinition;
            sourceElementOrdinal = level?.SourceElementOrdinal ?? -1;
        }

        if (level is null)
        {
            throw new WordNumberingResolutionException(
                $"Numbering instance '{numberId}' has no effective definition for level {levelIndex}."
            );
        }

        if (
            level.ParagraphProperties.Values.ContainsKey("numbering_id")
            || level.ParagraphProperties.Values.ContainsKey("numbering_level")
        )
        {
            throw new WordNumberingResolutionException(
                $"Effective numbering level {levelIndex} recursively declares numPr."
            );
        }

        var effectiveStart = levelOverride?.StartOverride ?? level.Start;
        var startSourceKind = levelOverride?.StartOverride is not null
            ? WordNumberingStartSourceKind.InstanceStartOverride
            : level.Start is not null
                ? WordNumberingStartSourceKind.LevelDefinition
                : WordNumberingStartSourceKind.Unspecified;
        var startSourceElementOrdinal = levelOverride?.StartOverride is not null
            ? levelOverride.StartOverrideSourceElementOrdinal
            : level.Start is not null
                ? level.SourceElementOrdinal
                : null;
        return new WordResolvedNumberingLevel(
            numberId,
            instance.AbstractNumberId,
            effectiveAbstractNumberId,
            levelIndex,
            level,
            levelSourceKind,
            effectiveStart,
            startSourceKind,
            sourceElementOrdinal,
            startSourceElementOrdinal,
            resolution.AbstractNumberChain,
            resolution.NumberingStyleChain
        );
    }
}

public sealed record WordNumberingGraphOptions
{
    public static WordNumberingGraphOptions Default { get; } = new();

    public int MaxNumberingPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxAbstractDefinitions { get; init; } = 65_536;

    public int MaxInstances { get; init; } = 262_144;

    public int MaxPictureBullets { get; init; } = 65_536;

    public int MaxLevelsPerDefinition { get; init; } = 64;

    public int MaxLinkDepth { get; init; } = 1_024;

    internal void Validate()
    {
        if (MaxNumberingPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNumberingPartBytes));
        }

        if (MaxAbstractDefinitions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAbstractDefinitions));
        }

        if (MaxInstances <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxInstances));
        }

        if (MaxPictureBullets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPictureBullets));
        }

        if (MaxLevelsPerDefinition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLevelsPerDefinition));
        }

        if (MaxLinkDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLinkDepth));
        }
    }
}

public sealed class WordNumberingGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string NumberingRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
    private const string StrictNumberingRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/numbering";
    private const string NumberingContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
    private const string OfficeRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string StrictOfficeRelationshipNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";

    private readonly WordNumberingGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordNumberingGraphBuilder(WordNumberingGraphOptions? options = null)
    {
        _options = options ?? WordNumberingGraphOptions.Default;
        _options.Validate();
    }

    public WordNumberingGraphBuilder(
        WordNumberingGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordNumberingGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordNumberingGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styleGraph,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(styleGraph);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.Numbering
        );
        ValidateSnapshots(package, semanticDocument, styleGraph);
        var numberingPart = ResolveNumberingPart(package, semanticDocument.MainPartUri);
        if (numberingPart is null)
        {
            return new WordNumberingGraph(
                package.Fingerprint,
                semanticDocument.MainPartUri,
                null,
                Array.Empty<WordAbstractNumberingDefinition>(),
                Array.Empty<WordNumberingInstance>(),
                Array.Empty<WordNumberingPictureBullet>(),
                null,
                new Dictionary<int, WordAbstractNumberingResolution>(),
                Array.Empty<WordNumberingIssue>(),
                Array.Empty<string>()
            );
        }

        var source = ParseNumberingPart(numberingPart, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || !IsWordNamespace(root.Name.NamespaceName)
            || root.Name.LocalName != "numbering"
        )
        {
            throw new WordNumberingProjectionException(
                "Word numbering part does not have a w:numbering root element."
            );
        }

        var w = root.Name.Namespace;
        var abstractElements = root.Elements(w + "abstractNum").ToArray();
        var instanceElements = root.Elements(w + "num").ToArray();
        var pictureElements = root.Elements(w + "numPicBullet").ToArray();
        EnforceCount(abstractElements.Length, _options.MaxAbstractDefinitions, "abstract numbering definitions");
        EnforceCount(instanceElements.Length, _options.MaxInstances, "numbering instances");
        EnforceCount(pictureElements.Length, _options.MaxPictureBullets, "picture bullets");

        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.Numbering,
            checked(abstractElements.Length + instanceElements.Length + pictureElements.Length),
            2_048
        );

        var abstractDefinitions = abstractElements
            .Select(element => ParseAbstractDefinition(element, w, source))
            .ToArray();
        var instances = instanceElements
            .Select(element => ParseInstance(element, w, source))
            .ToArray();
        var pictureBullets = pictureElements
            .Select(element => ParsePictureBullet(element, w, source))
            .ToArray();
        RejectDuplicateIds(
            abstractDefinitions.Select(item => item.AbstractNumberId),
            "abstract numbering definition"
        );
        RejectDuplicateIds(instances.Select(item => item.NumberId), "numbering instance");
        RejectDuplicateIds(
            pictureBullets.Select(item => item.PictureBulletId),
            "picture bullet"
        );

        var abstractById = abstractDefinitions.ToDictionary(item => item.AbstractNumberId);
        var instancesById = instances.ToDictionary(item => item.NumberId);
        var pictureById = pictureBullets.ToDictionary(item => item.PictureBulletId);
        var issues = new List<WordNumberingIssue>();
        ValidateDefinitions(abstractDefinitions, styleGraph, pictureById, issues);
        ValidateInstances(
            instances,
            abstractById,
            styleGraph,
            pictureById,
            issues
        );
        ValidatePictureBulletRelationships(package, numberingPart.Uri, pictureBullets, issues);
        var resolutions = ResolveAbstractLinks(
            abstractDefinitions,
            abstractById,
            instancesById,
            styleGraph
        );
        ValidateStyleLinks(
            abstractById,
            instancesById,
            styleGraph,
            resolutions,
            issues
        );
        foreach (var resolution in resolutions.Values.Where(item => !item.Resolvable))
        {
            issues.Add(
                new WordNumberingIssue(
                    "NUMBERING_ABSTRACT_LINK_UNRESOLVED",
                    WordNumberingIssueSeverity.Error,
                    resolution.Failure
                        ?? $"Abstract numbering definition '{resolution.RequestedAbstractNumberId}' is unresolved.",
                    AbstractNumberId: resolution.RequestedAbstractNumberId
                )
            );
        }

        var knownRootNames = new HashSet<XName>
        {
            w + "abstractNum",
            w + "num",
            w + "numPicBullet",
            w + "numIdMacAtCleanup",
        };
        var unmodeledRoot = root.Elements()
            .Where(element => !knownRootNames.Contains(element.Name))
            .Select(element => QualifiedName(element.Name))
            .ToArray();
        var cleanup = OptionalSingleChild(root, w + "numIdMacAtCleanup");
        var lastAssignedNumberId = cleanup is null
            ? (int?)null
            : RequiredNonNegativeIntAttribute(
                cleanup,
                w + "val",
                "last assigned numbering ID"
            );
        return new WordNumberingGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            numberingPart.Uri,
            abstractDefinitions,
            instances,
            pictureBullets,
            lastAssignedNumberId,
            resolutions,
            issues,
            unmodeledRoot
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
            throw new WordNumberingProjectionException(
                "Numbering graph requires package, semantic, and style snapshots from the same document version."
            );
        }
    }

    private static OpcPart? ResolveNumberingPart(
        OpcPackageSnapshot package,
        string mainPartUri
    )
    {
        var relationships = package.RelationshipsFrom(mainPartUri)
            .Where(relationship => relationship.Type is NumberingRelationship or StrictNumberingRelationship)
            .ToArray();
        if (relationships.Length == 0)
        {
            return null;
        }

        if (relationships.Length != 1)
        {
            throw new WordNumberingProjectionException(
                "Main document part contains multiple numbering relationships."
            );
        }

        var relationship = relationships[0];
        if (
            relationship.TargetMode != OpcRelationshipTargetMode.Internal
            || relationship.ResolvedTargetPartUri is null
            || !package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var part)
            || !string.Equals(
                part.ContentType,
                NumberingContentType,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new WordNumberingProjectionException(
                "The numbering relationship does not resolve to a valid Word numbering part."
            );
        }

        return part;
    }

    private LosslessXmlDocument ParseNumberingPart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxNumberingPartBytes,
                MaxXmlCharacters = _options.MaxNumberingPartBytes,
                MaxXmlElements = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(
                        (long)(_options.MaxAbstractDefinitions + _options.MaxInstances)
                            * 256,
                        32_768
                    )
                ),
                MaxXmlDepth = 128,
                MaxTextCharacters = _options.MaxNumberingPartBytes,
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
                    WordOperationResourceStage.Numbering,
                    cancellationToken
                );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordNumberingLimitException(
                "Word numbering part exceeds a numbering-graph XML limit: "
                    + exception.Message
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordNumberingProjectionException(
                "Word numbering part is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private WordAbstractNumberingDefinition ParseAbstractDefinition(
        XElement element,
        XNamespace w,
        LosslessXmlDocument source
    )
    {
        var abstractNumberId = RequiredNonNegativeIntAttribute(
            element,
            w + "abstractNumId",
            "abstract numbering ID"
        );
        var levelElements = element.Elements(w + "lvl").ToArray();
        EnforceCount(
            levelElements.Length,
            _options.MaxLevelsPerDefinition,
            $"levels in abstract numbering definition '{abstractNumberId}'"
        );
        var levels = levelElements
            .Select(level => ParseLevel(level, w, source))
            .ToArray();
        RejectDuplicateIds(
            levels.Select(level => level.LevelIndex),
            $"level in abstract numbering definition '{abstractNumberId}'"
        );
        var knownNames = new HashSet<XName>
        {
            w + "nsid",
            w + "multiLevelType",
            w + "name",
            w + "tmpl",
            w + "numStyleLink",
            w + "styleLink",
            w + "lvl",
        };
        return new WordAbstractNumberingDefinition(
            abstractNumberId,
            ChildValue(element, w + "nsid", w),
            ChildValue(element, w + "multiLevelType", w),
            ChildValue(element, w + "name", w),
            ChildValue(element, w + "tmpl", w),
            ChildStyleId(element, w + "numStyleLink", w),
            ChildStyleId(element, w + "styleLink", w),
            source.GetElementOrdinal(element),
            levels,
            FindUnmodeled(
                element,
                knownNames,
                new HashSet<XName> { w + "abstractNumId" }
            )
        );
    }

    private WordNumberingInstance ParseInstance(
        XElement element,
        XNamespace w,
        LosslessXmlDocument source
    )
    {
        var numberId = RequiredNonNegativeIntAttribute(
            element,
            w + "numId",
            "numbering instance ID"
        );
        if (numberId == 0)
        {
            throw new WordNumberingProjectionException(
                "A w:num definition cannot use reserved numbering ID 0."
            );
        }

        var abstractReference = RequiredSingleChild(element, w + "abstractNumId");
        var abstractNumberId = RequiredNonNegativeIntAttribute(
            abstractReference,
            w + "val",
            $"abstract numbering reference for instance '{numberId}'"
        );
        var overrideElements = element.Elements(w + "lvlOverride").ToArray();
        EnforceCount(
            overrideElements.Length,
            _options.MaxLevelsPerDefinition,
            $"overrides in numbering instance '{numberId}'"
        );
        var overrides = overrideElements
            .Select(item => ParseLevelOverride(item, w, source))
            .ToArray();
        RejectDuplicateIds(
            overrides.Select(item => item.LevelIndex),
            $"level override in numbering instance '{numberId}'"
        );
        var knownNames = new HashSet<XName>
        {
            w + "abstractNumId",
            w + "lvlOverride",
        };
        return new WordNumberingInstance(
            numberId,
            abstractNumberId,
            source.GetElementOrdinal(element),
            overrides,
            FindUnmodeled(
                element,
                knownNames,
                new HashSet<XName> { w + "numId" }
            )
        );
    }

    private WordNumberingLevelOverride ParseLevelOverride(
        XElement element,
        XNamespace w,
        LosslessXmlDocument source
    )
    {
        var levelIndex = RequiredNonNegativeIntAttribute(
            element,
            w + "ilvl",
            "override level index"
        );
        var startElement = OptionalSingleChild(element, w + "startOverride");
        var levelElement = OptionalSingleChild(element, w + "lvl");
        var knownNames = new HashSet<XName>
        {
            w + "startOverride",
            w + "lvl",
        };
        return new WordNumberingLevelOverride(
            levelIndex,
            startElement is null
                ? null
                : RequiredIntAttribute(startElement, w + "val", "start override"),
            startElement is null ? null : source.GetElementOrdinal(startElement),
            levelElement is null ? null : ParseLevel(levelElement, w, source),
            source.GetElementOrdinal(element),
            FindUnmodeled(
                element,
                knownNames,
                new HashSet<XName> { w + "ilvl" }
            )
        );
    }

    private static WordNumberingLevelDefinition ParseLevel(
        XElement element,
        XNamespace w,
        LosslessXmlDocument source
    )
    {
        var levelIndex = RequiredNonNegativeIntAttribute(
            element,
            w + "ilvl",
            "numbering level index"
        );
        var start = OptionalSingleChild(element, w + "start");
        var numberFormat = OptionalSingleChild(element, w + "numFmt");
        var restart = OptionalSingleChild(element, w + "lvlRestart");
        var legal = OptionalSingleChild(element, w + "isLgl");
        var suffix = OptionalSingleChild(element, w + "suff");
        var levelText = OptionalSingleChild(element, w + "lvlText");
        var picture = OptionalSingleChild(element, w + "lvlPicBulletId");
        var justification = OptionalSingleChild(element, w + "lvlJc");
        var paragraphProperties = OptionalSingleChild(element, w + "pPr");
        var runProperties = OptionalSingleChild(element, w + "rPr");
        var knownNames = new HashSet<XName>
        {
            w + "start",
            w + "numFmt",
            w + "lvlRestart",
            w + "pStyle",
            w + "isLgl",
            w + "suff",
            w + "lvlText",
            w + "lvlPicBulletId",
            w + "lvlJc",
            w + "pPr",
            w + "rPr",
        };
        return new WordNumberingLevelDefinition(
            levelIndex,
            start is null ? null : RequiredIntAttribute(start, w + "val", "level start"),
            numberFormat is null
                ? null
                : RequiredAttribute(numberFormat, w + "val", "number format"),
            numberFormat?.Attribute(w + "format")?.Value,
            restart is null
                ? null
                : RequiredNonNegativeIntAttribute(restart, w + "val", "restart level"),
            ChildStyleId(element, w + "pStyle", w),
            legal is null ? null : ParseOnOffElement(legal),
            suffix is null ? null : RequiredAttribute(suffix, w + "val", "level suffix"),
            levelText is null
                ? null
                : RequiredAttribute(levelText, w + "val", "level text", allowEmpty: true),
            levelText is null ? null : OptionalOnOffAttribute(levelText, w + "null"),
            picture is null
                ? null
                : RequiredNonNegativeIntAttribute(
                    picture,
                    w + "val",
                    "picture bullet ID"
                ),
            justification is null
                ? null
                : RequiredAttribute(
                    justification,
                    w + "val",
                    "level justification"
                ),
            element.Attribute(w + "tplc")?.Value,
            OptionalOnOffAttribute(element, w + "tentative"),
            source.GetElementOrdinal(element),
            ReadFormattingProperties(paragraphProperties, isParagraph: true),
            ReadFormattingProperties(runProperties, isParagraph: false),
            FindUnmodeled(
                element,
                knownNames,
                new HashSet<XName> { w + "ilvl", w + "tplc", w + "tentative" }
            )
        );
    }

    private static WordNumberingPictureBullet ParsePictureBullet(
        XElement element,
        XNamespace w,
        LosslessXmlDocument source
    )
    {
        var pictureBulletId = RequiredNonNegativeIntAttribute(
            element,
            w + "numPicBulletId",
            "picture bullet ID"
        );
        var relationshipIds = element.DescendantsAndSelf()
            .Attributes()
            .Where(attribute =>
                attribute.Name.LocalName == "id"
                && attribute.Name.NamespaceName is
                    OfficeRelationshipNamespace or StrictOfficeRelationshipNamespace
            )
            .Select(attribute => attribute.Value)
            .Where(value => value.Length != 0)
            .ToArray();
        return new WordNumberingPictureBullet(
            pictureBulletId,
            source.GetElementOrdinal(element),
            relationshipIds
        );
    }

    private static WordStylePropertySet ReadFormattingProperties(
        XElement? element,
        bool isParagraph
    )
    {
        try
        {
            return WordStyleGraphBuilder.ReadFormattingProperties(
                element,
                isParagraph
                    ? WordStyleGraphBuilder.WordFormattingDomain.Paragraph
                    : WordStyleGraphBuilder.WordFormattingDomain.Run
            );
        }
        catch (WordStyleProjectionException exception)
        {
            throw new WordNumberingProjectionException(
                "Numbering-level formatting properties are structurally ambiguous.",
                exception
            );
        }
    }

    private static void ValidateDefinitions(
        IReadOnlyList<WordAbstractNumberingDefinition> definitions,
        WordStyleGraph styleGraph,
        IReadOnlyDictionary<int, WordNumberingPictureBullet> pictureBullets,
        List<WordNumberingIssue> issues
    )
    {
        foreach (var definition in definitions)
        {
            if (definition.NumberingStyleLinkId is not null && definition.Levels.Count != 0)
            {
                issues.Add(
                    new WordNumberingIssue(
                        "NUMBERING_LINKED_ABSTRACT_HAS_LEVELS",
                        WordNumberingIssueSeverity.Warning,
                        $"Abstract numbering definition '{definition.AbstractNumberId}' has both numStyleLink and local levels; linked-style resolution determines the effective definition.",
                        AbstractNumberId: definition.AbstractNumberId
                    )
                );
            }

            foreach (var level in definition.Levels)
            {
                if (level.LevelIndex > 8)
                {
                    issues.Add(
                        new WordNumberingIssue(
                            "NUMBERING_LEVEL_OUT_OF_RANGE",
                            WordNumberingIssueSeverity.Error,
                            $"Abstract numbering definition '{definition.AbstractNumberId}' declares level {level.LevelIndex}; Word supports levels 0 through 8.",
                            definition.AbstractNumberId,
                            LevelIndex: level.LevelIndex
                        )
                    );
                }

                if (
                    level.PictureBulletId is { } pictureBulletId
                    && !pictureBullets.ContainsKey(pictureBulletId)
                )
                {
                    issues.Add(
                        new WordNumberingIssue(
                            "NUMBERING_PICTURE_BULLET_MISSING",
                            WordNumberingIssueSeverity.Error,
                            $"Level {level.LevelIndex} refers to missing picture bullet '{pictureBulletId}'.",
                            definition.AbstractNumberId,
                            LevelIndex: level.LevelIndex
                        )
                    );
                }

                ValidateLevelParagraphStyle(
                    definition.AbstractNumberId,
                    level,
                    styleGraph,
                    issues
                );
                ValidateRecursiveLevelReference(
                    definition.AbstractNumberId,
                    null,
                    level,
                    issues
                );
            }
        }

        foreach (
            var group in definitions
                .Where(item => item.StyleLinkId is not null)
                .GroupBy(item => item.StyleLinkId!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            issues.Add(
                new WordNumberingIssue(
                    "NUMBERING_STYLE_LINK_AMBIGUOUS",
                    WordNumberingIssueSeverity.Error,
                    $"Numbering style '{group.Key}' is claimed by multiple abstract numbering definitions."
                )
            );
        }
    }

    private static void ValidateLevelParagraphStyle(
        int abstractNumberId,
        WordNumberingLevelDefinition level,
        WordStyleGraph styleGraph,
        List<WordNumberingIssue> issues
    )
    {
        if (level.ParagraphStyleId is not { } styleId)
        {
            return;
        }

        if (!styleGraph.TryGetStyle(styleId, out var style) || style is null)
        {
            issues.Add(
                new WordNumberingIssue(
                    "NUMBERING_LEVEL_STYLE_MISSING",
                    WordNumberingIssueSeverity.Warning,
                    $"Level {level.LevelIndex} refers to missing paragraph style '{styleId}'.",
                    abstractNumberId,
                    LevelIndex: level.LevelIndex
                )
            );
        }
        else if (style.Type != WordStyleType.Paragraph)
        {
            issues.Add(
                new WordNumberingIssue(
                    "NUMBERING_LEVEL_STYLE_TYPE_MISMATCH",
                    WordNumberingIssueSeverity.Warning,
                    $"Level {level.LevelIndex} refers to style '{styleId}' of type {style.Type}, not Paragraph.",
                    abstractNumberId,
                    LevelIndex: level.LevelIndex
                )
            );
        }
    }

    private static void ValidateInstances(
        IReadOnlyList<WordNumberingInstance> instances,
        IReadOnlyDictionary<int, WordAbstractNumberingDefinition> abstractDefinitions,
        WordStyleGraph styleGraph,
        IReadOnlyDictionary<int, WordNumberingPictureBullet> pictureBullets,
        List<WordNumberingIssue> issues
    )
    {
        foreach (var instance in instances)
        {
            if (!abstractDefinitions.ContainsKey(instance.AbstractNumberId))
            {
                issues.Add(
                    new WordNumberingIssue(
                        "NUMBERING_ABSTRACT_MISSING",
                        WordNumberingIssueSeverity.Error,
                        $"Numbering instance '{instance.NumberId}' refers to missing abstract definition '{instance.AbstractNumberId}'.",
                        instance.AbstractNumberId,
                        instance.NumberId
                    )
                );
            }

            foreach (var levelOverride in instance.LevelOverrides)
            {
                if (levelOverride.LevelIndex > 8)
                {
                    issues.Add(
                        new WordNumberingIssue(
                            "NUMBERING_OVERRIDE_OUT_OF_RANGE",
                            WordNumberingIssueSeverity.Error,
                            $"Numbering instance '{instance.NumberId}' overrides level {levelOverride.LevelIndex}; Word supports levels 0 through 8.",
                            instance.AbstractNumberId,
                            instance.NumberId,
                            levelOverride.LevelIndex
                        )
                    );
                }

                if (
                    levelOverride.Level is { } level
                    && level.LevelIndex != levelOverride.LevelIndex
                )
                {
                    issues.Add(
                        new WordNumberingIssue(
                            "NUMBERING_OVERRIDE_LEVEL_MISMATCH",
                            WordNumberingIssueSeverity.Error,
                            $"Numbering instance '{instance.NumberId}' override {levelOverride.LevelIndex} contains a level definition for {level.LevelIndex}.",
                            instance.AbstractNumberId,
                            instance.NumberId,
                            levelOverride.LevelIndex
                        )
                    );
                }

                if (levelOverride.StartOverride is null && levelOverride.Level is null)
                {
                    issues.Add(
                        new WordNumberingIssue(
                            "NUMBERING_OVERRIDE_EMPTY",
                            WordNumberingIssueSeverity.Warning,
                            $"Numbering instance '{instance.NumberId}' has an empty override for level {levelOverride.LevelIndex}.",
                            instance.AbstractNumberId,
                            instance.NumberId,
                            levelOverride.LevelIndex
                        )
                    );
                }

                if (levelOverride.Level is { } replacement)
                {
                    if (
                        replacement.PictureBulletId is { } pictureBulletId
                        && !pictureBullets.ContainsKey(pictureBulletId)
                    )
                    {
                        issues.Add(
                            new WordNumberingIssue(
                                "NUMBERING_PICTURE_BULLET_MISSING",
                                WordNumberingIssueSeverity.Error,
                                $"Override level {replacement.LevelIndex} refers to missing picture bullet '{pictureBulletId}'.",
                                instance.AbstractNumberId,
                                instance.NumberId,
                                levelOverride.LevelIndex
                            )
                        );
                    }

                    ValidateLevelParagraphStyle(
                        instance.AbstractNumberId,
                        replacement,
                        styleGraph,
                        issues
                    );
                    ValidateRecursiveLevelReference(
                        instance.AbstractNumberId,
                        instance.NumberId,
                        replacement,
                        issues
                    );
                }
            }
        }
    }

    private static void ValidateRecursiveLevelReference(
        int abstractNumberId,
        int? numberId,
        WordNumberingLevelDefinition level,
        List<WordNumberingIssue> issues
    )
    {
        if (
            !level.ParagraphProperties.Values.ContainsKey("numbering_id")
            && !level.ParagraphProperties.Values.ContainsKey("numbering_level")
        )
        {
            return;
        }

        issues.Add(
            new WordNumberingIssue(
                "NUMBERING_LEVEL_RECURSIVE_REFERENCE",
                WordNumberingIssueSeverity.Error,
                $"Level {level.LevelIndex} recursively declares numPr in its paragraph properties.",
                abstractNumberId,
                numberId,
                level.LevelIndex
            )
        );
    }

    private static void ValidatePictureBulletRelationships(
        OpcPackageSnapshot package,
        string numberingPartUri,
        IReadOnlyList<WordNumberingPictureBullet> pictureBullets,
        List<WordNumberingIssue> issues
    )
    {
        var relationships = package.RelationshipsFrom(numberingPartUri)
            .ToDictionary(relationship => relationship.Id, StringComparer.Ordinal);
        foreach (var pictureBullet in pictureBullets)
        {
            foreach (var relationshipId in pictureBullet.RelationshipIds)
            {
                if (
                    !relationships.TryGetValue(relationshipId, out var relationship)
                    || relationship.TargetMode != OpcRelationshipTargetMode.Internal
                    || relationship.ResolvedTargetPartUri is null
                    || !package.Parts.ContainsKey(relationship.ResolvedTargetPartUri)
                )
                {
                    issues.Add(
                        new WordNumberingIssue(
                            "NUMBERING_PICTURE_RELATIONSHIP_MISSING",
                            WordNumberingIssueSeverity.Error,
                            $"Picture bullet '{pictureBullet.PictureBulletId}' refers to missing or invalid relationship '{relationshipId}'."
                        )
                    );
                }
            }
        }
    }

    private IReadOnlyDictionary<int, WordAbstractNumberingResolution> ResolveAbstractLinks(
        IReadOnlyList<WordAbstractNumberingDefinition> definitions,
        IReadOnlyDictionary<int, WordAbstractNumberingDefinition> abstractDefinitions,
        IReadOnlyDictionary<int, WordNumberingInstance> instances,
        WordStyleGraph styleGraph
    )
    {
        var results = new Dictionary<int, WordAbstractNumberingResolution>();
        var active = new List<int>();

        WordAbstractNumberingResolution Resolve(int abstractNumberId)
        {
            if (results.TryGetValue(abstractNumberId, out var cached))
            {
                return cached;
            }

            if (active.Count >= _options.MaxLinkDepth)
            {
                return Failed(
                    abstractNumberId,
                    $"Abstract numbering link exceeds the {_options.MaxLinkDepth}-definition limit.",
                    active.Append(abstractNumberId)
                );
            }

            var cycleStart = active.IndexOf(abstractNumberId);
            if (cycleStart >= 0)
            {
                var cycle = active.Skip(cycleStart).Append(abstractNumberId).ToArray();
                return Failed(
                    abstractNumberId,
                    "Circular numbering-style link: " + string.Join(" -> ", cycle),
                    cycle
                );
            }

            if (!abstractDefinitions.TryGetValue(abstractNumberId, out var definition))
            {
                return Failed(
                    abstractNumberId,
                    $"Abstract numbering definition '{abstractNumberId}' does not exist.",
                    [abstractNumberId]
                );
            }

            if (definition.NumberingStyleLinkId is not { } styleId)
            {
                var terminal = new WordAbstractNumberingResolution(
                    abstractNumberId,
                    abstractNumberId,
                    true,
                    null,
                    [abstractNumberId],
                    Array.Empty<string>()
                );
                results[abstractNumberId] = terminal;
                return terminal;
            }

            if (!TryResolveNumberingStyleId(styleGraph, styleId, out var numberId, out var failure))
            {
                var failed = Failed(
                    abstractNumberId,
                    failure!,
                    [abstractNumberId],
                    [styleId]
                );
                results[abstractNumberId] = failed;
                return failed;
            }

            if (!instances.TryGetValue(numberId, out var instance))
            {
                var failed = Failed(
                    abstractNumberId,
                    $"Numbering style '{styleId}' refers to missing numbering instance '{numberId}'.",
                    [abstractNumberId],
                    [styleId]
                );
                results[abstractNumberId] = failed;
                return failed;
            }

            active.Add(abstractNumberId);
            var target = Resolve(instance.AbstractNumberId);
            active.RemoveAt(active.Count - 1);
            WordAbstractNumberingResolution result;
            if (!target.Resolvable)
            {
                result = Failed(
                    abstractNumberId,
                    target.Failure!,
                    new[] { abstractNumberId }.Concat(target.AbstractNumberChain),
                    new[] { styleId }.Concat(target.NumberingStyleChain)
                );
            }
            else
            {
                result = new WordAbstractNumberingResolution(
                    abstractNumberId,
                    target.EffectiveAbstractNumberId,
                    true,
                    null,
                    new[] { abstractNumberId }.Concat(target.AbstractNumberChain).ToArray(),
                    new[] { styleId }.Concat(target.NumberingStyleChain).ToArray()
                );
            }

            results[abstractNumberId] = result;
            return result;
        }

        foreach (var definition in definitions)
        {
            Resolve(definition.AbstractNumberId);
        }

        return results;
    }

    private static WordAbstractNumberingResolution Failed(
        int requestedAbstractNumberId,
        string failure,
        IEnumerable<int> abstractNumberChain,
        IEnumerable<string>? numberingStyleChain = null
    ) => new(
        requestedAbstractNumberId,
        null,
        false,
        failure,
        abstractNumberChain.ToArray(),
        numberingStyleChain?.ToArray() ?? Array.Empty<string>()
    );

    private static void ValidateStyleLinks(
        IReadOnlyDictionary<int, WordAbstractNumberingDefinition> definitions,
        IReadOnlyDictionary<int, WordNumberingInstance> instances,
        WordStyleGraph styleGraph,
        IReadOnlyDictionary<int, WordAbstractNumberingResolution> resolutions,
        List<WordNumberingIssue> issues
    )
    {
        foreach (var definition in definitions.Values)
        {
            if (definition.NumberingStyleLinkId is { } numberingStyleId)
            {
                if (
                    resolutions.TryGetValue(definition.AbstractNumberId, out var resolution)
                    && resolution.Resolvable
                    && resolution.EffectiveAbstractNumberId is { } effectiveId
                    && definitions.TryGetValue(effectiveId, out var effectiveDefinition)
                )
                {
                    if (effectiveDefinition.StyleLinkId is not { } claimedStyle)
                    {
                        issues.Add(
                            new WordNumberingIssue(
                                "NUMBERING_STYLE_LINK_NOT_RECIPROCAL",
                                WordNumberingIssueSeverity.Warning,
                                $"Abstract definition '{definition.AbstractNumberId}' requests numbering style '{numberingStyleId}', but the effective definition does not claim that style.",
                                AbstractNumberId: definition.AbstractNumberId
                            )
                        );
                    }
                    else if (
                        !string.Equals(
                            claimedStyle,
                            numberingStyleId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        issues.Add(
                            new WordNumberingIssue(
                                "NUMBERING_STYLE_LINK_MISMATCH",
                                WordNumberingIssueSeverity.Error,
                                $"Abstract definition '{definition.AbstractNumberId}' requests numbering style '{numberingStyleId}', but resolves to a definition claimed by '{claimedStyle}'.",
                                AbstractNumberId: definition.AbstractNumberId
                            )
                        );
                    }
                }
            }

            if (definition.StyleLinkId is not { } styleLinkId)
            {
                continue;
            }

            if (!TryResolveNumberingStyleId(styleGraph, styleLinkId, out var numberId, out var failure))
            {
                issues.Add(
                    new WordNumberingIssue(
                        "NUMBERING_STYLE_LINK_INVALID",
                        WordNumberingIssueSeverity.Error,
                        failure!,
                        AbstractNumberId: definition.AbstractNumberId
                    )
                );
                continue;
            }

            if (!instances.TryGetValue(numberId, out var instance))
            {
                issues.Add(
                    new WordNumberingIssue(
                        "NUMBERING_STYLE_INSTANCE_MISSING",
                        WordNumberingIssueSeverity.Error,
                        $"Numbering style '{styleLinkId}' refers to missing instance '{numberId}'.",
                        definition.AbstractNumberId,
                        numberId
                    )
                );
                continue;
            }

            if (
                !resolutions.TryGetValue(instance.AbstractNumberId, out var styleResolution)
                || !styleResolution.Resolvable
                || styleResolution.EffectiveAbstractNumberId != definition.AbstractNumberId
            )
            {
                issues.Add(
                    new WordNumberingIssue(
                        "NUMBERING_STYLE_TARGET_MISMATCH",
                        WordNumberingIssueSeverity.Error,
                        $"Numbering style '{styleLinkId}' does not resolve back to abstract definition '{definition.AbstractNumberId}'.",
                        definition.AbstractNumberId,
                        numberId
                    )
                );
            }
        }
    }

    private static bool TryResolveNumberingStyleId(
        WordStyleGraph styleGraph,
        string styleId,
        out int numberId,
        out string? failure
    )
    {
        numberId = 0;
        if (!styleGraph.TryGetStyle(styleId, out var style) || style is null)
        {
            failure = $"Numbering style '{styleId}' does not exist.";
            return false;
        }

        if (style.Type != WordStyleType.Numbering)
        {
            failure = $"Style '{styleId}' is {style.Type}, not a numbering style.";
            return false;
        }

        if (!style.InheritanceResolvable)
        {
            failure = style.InheritanceFailure
                ?? $"Numbering style '{styleId}' has an unresolved inheritance chain.";
            return false;
        }

        string? rawNumberId = null;
        foreach (var chainId in style.InheritanceChainStyleIds)
        {
            if (
                !styleGraph.TryGetStyle(chainId, out var chainStyle)
                || chainStyle is null
            )
            {
                failure = $"Numbering style '{styleId}' lost inherited style '{chainId}'.";
                return false;
            }

            if (
                chainStyle.ParagraphProperties.Values.TryGetValue(
                    "numbering_id",
                    out var declared
                )
            )
            {
                rawNumberId = declared;
            }
        }

        if (
            rawNumberId is null
            || !int.TryParse(
                rawNumberId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out numberId
            )
            || numberId <= 0
        )
        {
            failure = $"Numbering style '{styleId}' has no usable positive numId.";
            return false;
        }

        failure = null;
        return true;
    }

    private static string? ChildValue(XElement parent, XName childName, XNamespace w)
    {
        var child = OptionalSingleChild(parent, childName);
        return child is null
            ? null
            : RequiredAttribute(child, w + "val", childName.LocalName, allowEmpty: true);
    }

    private static string? ChildStyleId(
        XElement parent,
        XName childName,
        XNamespace w
    )
    {
        var value = ChildValue(parent, childName, w);
        if (value is null)
        {
            return null;
        }

        if (value.Length is 0 or > 253)
        {
            throw new WordNumberingProjectionException(
                $"'{childName.LocalName}' has an invalid style ID length."
            );
        }

        return value;
    }

    private static IReadOnlyList<string> FindUnmodeled(
        XElement element,
        IReadOnlySet<XName> knownChildren,
        IReadOnlySet<XName> knownAttributes
    )
    {
        var result = element.Elements()
            .Where(child => !knownChildren.Contains(child.Name))
            .Select(child => QualifiedName(child.Name))
            .ToList();
        result.AddRange(
            element.Attributes()
                .Where(attribute =>
                    !attribute.IsNamespaceDeclaration
                    && !knownAttributes.Contains(attribute.Name)
                    && attribute.Name.NamespaceName
                        != "http://schemas.openxmlformats.org/markup-compatibility/2006"
                )
                .Select(attribute => "@" + QualifiedName(attribute.Name))
        );
        return result;
    }

    private static XElement RequiredSingleChild(XElement parent, XName name) =>
        OptionalSingleChild(parent, name)
        ?? throw new WordNumberingProjectionException(
            $"Element '{parent.Name.LocalName}' has no '{name.LocalName}' child."
        );

    private static XElement? OptionalSingleChild(XElement? parent, XName name)
    {
        if (parent is null)
        {
            return null;
        }

        var children = parent.Elements(name).Take(2).ToArray();
        if (children.Length > 1)
        {
            throw new WordNumberingProjectionException(
                $"Element '{parent.Name.LocalName}' contains duplicate '{name.LocalName}' children."
            );
        }

        return children.FirstOrDefault();
    }

    private static string RequiredAttribute(
        XElement element,
        XName name,
        string description,
        bool allowEmpty = false
    )
    {
        var attribute = element.Attribute(name);
        if (attribute is null || !allowEmpty && attribute.Value.Length == 0)
        {
            throw new WordNumberingProjectionException(
                $"Element '{element.Name.LocalName}' has no {description}."
            );
        }

        return attribute.Value;
    }

    private static int RequiredNonNegativeIntAttribute(
        XElement element,
        XName name,
        string description
    )
    {
        var result = RequiredIntAttribute(element, name, description);
        if (result < 0)
        {
            throw new WordNumberingProjectionException(
                $"'{description}' cannot be negative."
            );
        }

        return result;
    }

    private static int RequiredIntAttribute(
        XElement element,
        XName name,
        string description
    )
    {
        var raw = RequiredAttribute(element, name, description);
        if (
            !int.TryParse(
                raw,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var result
            )
        )
        {
            throw new WordNumberingProjectionException(
                $"'{description}' has invalid integer value '{raw}'."
            );
        }

        return result;
    }

    private static bool? OptionalOnOffAttribute(XElement element, XName name)
    {
        var value = element.Attribute(name)?.Value;
        return value is null ? null : ParseOnOff(value, name.LocalName);
    }

    private static bool ParseOnOffElement(XElement element)
    {
        var raw = element.Attribute(element.Name.Namespace + "val")?.Value;
        return raw is null || ParseOnOff(raw, element.Name.LocalName);
    }

    private static bool ParseOnOff(string value, string description) =>
        value.ToLowerInvariant() switch
        {
            "true" or "1" or "on" => true,
            "false" or "0" or "off" => false,
            _ => throw new WordNumberingProjectionException(
                $"'{description}' has invalid on/off value '{value}'."
            ),
        };

    private static void EnforceCount(int actual, int maximum, string description)
    {
        if (actual > maximum)
        {
            throw new WordNumberingLimitException(
                $"Numbering part contains {actual} {description}; limit is {maximum}."
            );
        }
    }

    private static void RejectDuplicateIds(IEnumerable<int> ids, string description)
    {
        var duplicate = ids.GroupBy(value => value).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordNumberingProjectionException(
                $"Numbering part contains duplicate {description} ID '{duplicate.Key}'."
            );
        }
    }

    private static string QualifiedName(XName name) =>
        $"{{{name.NamespaceName}}}{name.LocalName}";

    private static bool IsWordNamespace(string namespaceName) =>
        string.Equals(namespaceName, WordTransitionalNamespace, StringComparison.Ordinal)
        || string.Equals(namespaceName, WordStrictNamespace, StringComparison.Ordinal);
}

public class WordNumberingProjectionException : IOException
{
    public WordNumberingProjectionException(string message)
        : base(message)
    {
    }

    public WordNumberingProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordNumberingLimitException : WordNumberingProjectionException
{
    public WordNumberingLimitException(string message)
        : base(message)
    {
    }
}

public sealed class WordNumberingResolutionException : IOException
{
    public WordNumberingResolutionException(string message)
        : base(message)
    {
    }
}
