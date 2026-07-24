using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordActiveContentIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordActiveContentDeclarationKind
{
    OleObject,
    EmbeddedObject,
    LinkedObject,
    ActiveXControl,
}

public enum WordActiveContentPayloadKind
{
    OleObject,
    EmbeddedPackage,
    ActiveXXml,
    ActiveXBinary,
    VbaProject,
    VbaData,
    AttachedToolbar,
    CustomUi,
    QuickAccessToolbarCustomization,
    KeyMapCustomization,
    VbaProjectSignature,
    DigitalSignatureOrigin,
    DigitalSignature,
}

public enum WordActiveContentRelationshipRole
{
    OleObject,
    EmbeddedPackage,
    ActiveXControl,
    ActiveXControlBinary,
    VbaProject,
    VbaData,
    AttachedToolbar,
    CustomUi,
    QuickAccessToolbarCustomization,
    KeyMapCustomization,
    VbaProjectSignature,
    DigitalSignatureOrigin,
    DigitalSignature,
}

public sealed record WordActiveContentIssue(
    string Code,
    WordActiveContentIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? RelationshipId = null,
    string? SubjectId = null
);

public sealed record WordActiveContentRelationship(
    string Id,
    string SourcePartUri,
    string RelationshipId,
    string RelationshipType,
    WordActiveContentRelationshipRole Role,
    string Target,
    OpcRelationshipTargetMode TargetMode,
    string? TargetPartUri,
    string? PayloadId,
    bool IsResolved
);

public sealed record WordActiveContentDeclaration(
    string Id,
    WordActiveContentDeclarationKind Kind,
    string SourcePartUri,
    int SourceElementOrdinal,
    string? RelationshipId,
    string? RelationshipNodeId,
    string? ProgramId,
    string? ObjectType,
    string? DrawAspect,
    string? UpdateMode,
    string? ShapeId,
    string? ObjectId,
    string? ControlName,
    string? LinkType,
    string? ServerFormat,
    string? LockedField,
    bool HasFieldCodes,
    int FieldCodeCharacters,
    bool IsResolved
);

public sealed record WordActiveXControlDefinition(
    string Id,
    string PartUri,
    int SourceElementOrdinal,
    string? ClassId,
    string? Persistence,
    string? BinaryRelationshipId,
    string? BinaryRelationshipNodeId,
    string? BinaryPayloadId,
    int PropertyCount,
    bool HasLicense,
    int LicenseCharacters,
    bool IsResolved,
    IReadOnlyList<string> DeclarationIds
);

public sealed record WordActiveContentPayload(
    string Id,
    WordActiveContentPayloadKind Kind,
    string PartUri,
    string? ContentType,
    long UncompressedLength,
    string Sha256,
    bool IsPackageReachable,
    int IncomingRelationshipCount,
    bool IsXml,
    bool IsPotentiallyExecutable,
    string ContainerFamily
);

public sealed class WordActiveContentGraph
{
    private readonly IReadOnlyDictionary<string, WordActiveContentPayload> _payloadsById;
    private readonly IReadOnlyDictionary<string, WordActiveContentDeclaration> _declarationsById;

    internal WordActiveContentGraph(
        string packageFingerprint,
        bool mainDocumentMacroEnabled,
        IReadOnlyList<WordActiveContentDeclaration> declarations,
        IReadOnlyList<WordActiveXControlDefinition> controls,
        IReadOnlyList<WordActiveContentPayload> payloads,
        IReadOnlyList<WordActiveContentRelationship> relationships,
        IReadOnlyList<WordActiveContentIssue> issues,
        bool issuesTruncated
    )
    {
        PackageFingerprint = packageFingerprint;
        MainDocumentMacroEnabled = mainDocumentMacroEnabled;
        Declarations = new ReadOnlyCollection<WordActiveContentDeclaration>(declarations.ToArray());
        Controls = new ReadOnlyCollection<WordActiveXControlDefinition>(controls.ToArray());
        Payloads = new ReadOnlyCollection<WordActiveContentPayload>(payloads.ToArray());
        Relationships = new ReadOnlyCollection<WordActiveContentRelationship>(relationships.ToArray());
        Issues = new ReadOnlyCollection<WordActiveContentIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        _payloadsById = new ReadOnlyDictionary<string, WordActiveContentPayload>(
            payloads.ToDictionary(item => item.Id, StringComparer.Ordinal)
        );
        _declarationsById = new ReadOnlyDictionary<string, WordActiveContentDeclaration>(
            declarations.ToDictionary(item => item.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public bool MainDocumentMacroEnabled { get; }

    public IReadOnlyList<WordActiveContentDeclaration> Declarations { get; }

    public IReadOnlyList<WordActiveXControlDefinition> Controls { get; }

    public IReadOnlyList<WordActiveContentPayload> Payloads { get; }

    public IReadOnlyList<WordActiveContentRelationship> Relationships { get; }

    public IReadOnlyList<WordActiveContentIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public bool BinaryPayloadsDecoded => false;

    public bool EmbeddedPackagesOpened => false;

    public bool CryptographicSignatureValidationPerformed => false;

    public bool TryGetPayload(string id, out WordActiveContentPayload? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _payloadsById.TryGetValue(id, out payload);
    }

    public bool TryGetDeclaration(string id, out WordActiveContentDeclaration? declaration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _declarationsById.TryGetValue(id, out declaration);
    }
}

public sealed record WordActiveContentGraphOptions
{
    public static WordActiveContentGraphOptions Default { get; } = new();

    public int MaxRelevantRelationships { get; init; } = 100_000;

    public int MaxPayloads { get; init; } = 50_000;

    public int MaxDeclarations { get; init; } = 100_000;

    public int MaxXmlParts { get; init; } = 10_000;

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxElementsPerXmlPart { get; init; } = 500_000;

    public int MaxTotalXmlBytes { get; init; } = 256 * 1024 * 1024;

    public int MaxTotalXmlElements { get; init; } = 2_000_000;

    public int MaxMetadataCharacters { get; init; } = 4 * 1024 * 1024;

    public int MaxIssues { get; init; } = 10_000;

    internal void Validate()
    {
        if (MaxRelevantRelationships <= 0) throw new ArgumentOutOfRangeException(nameof(MaxRelevantRelationships));
        if (MaxPayloads <= 0) throw new ArgumentOutOfRangeException(nameof(MaxPayloads));
        if (MaxDeclarations <= 0) throw new ArgumentOutOfRangeException(nameof(MaxDeclarations));
        if (MaxXmlParts <= 0) throw new ArgumentOutOfRangeException(nameof(MaxXmlParts));
        if (MaxXmlPartBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        if (MaxElementsPerXmlPart <= 0) throw new ArgumentOutOfRangeException(nameof(MaxElementsPerXmlPart));
        if (MaxTotalXmlBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalXmlBytes));
        if (MaxTotalXmlElements <= 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalXmlElements));
        if (MaxMetadataCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(MaxMetadataCharacters));
        if (MaxIssues <= 0) throw new ArgumentOutOfRangeException(nameof(MaxIssues));
    }
}

public sealed class WordActiveContentGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string OfficeNamespace = "urn:schemas-microsoft-com:office:office";
    private const string ActiveXNamespace = "http://schemas.microsoft.com/office/2006/activeX";
    private const string OleObjectContentType =
        "application/vnd.openxmlformats-officedocument.oleObject";
    private const string ActiveXXmlContentType = "application/vnd.ms-office.activeX+xml";
    private const string ActiveXBinaryContentType = "application/vnd.ms-office.activeX";
    private const string VbaProjectContentType = "application/vnd.ms-office.vbaProject";
    private const string VbaDataContentType = "application/vnd.ms-word.vbaData+xml";
    private const string AttachedToolbarContentType = "application/vnd.ms-office.attachedToolbars";
    private const string WordAttachedToolbarContentType = "application/vnd.ms-word.attachedToolbars";
    private const string KeyMapCustomizationContentType =
        "application/vnd.ms-word.keyMapCustomizations+xml";
    private const string VbaProjectSignatureContentType =
        "application/vnd.ms-office.vbaProjectSignature";
    private const string SignatureOriginContentType =
        "application/vnd.openxmlformats-package.digital-signature-origin";
    private const string SignatureContentType =
        "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml";

    private const string TransitionalRelationshipBase =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StrictRelationshipBase =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/";
    private const string TransitionalRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string StrictRelationshipNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string MicrosoftOffice2006RelationshipBase =
        "http://schemas.microsoft.com/office/2006/relationships/";
    private const string MicrosoftOffice2007RelationshipBase =
        "http://schemas.microsoft.com/office/2007/relationships/";
    private const string PackageSignatureRelationshipBase =
        "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/";

    private readonly WordActiveContentGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordActiveContentGraphBuilder(WordActiveContentGraphOptions? options = null)
    {
        _options = options ?? WordActiveContentGraphOptions.Default;
        _options.Validate();
    }

    public WordActiveContentGraphBuilder(
        WordActiveContentGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordActiveContentGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordActiveContentGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.ActiveContent
        );
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.ActiveContent,
            checked(package.Relationships.Count + package.Parts.Count),
            96
        );

        var issues = new IssueState(_options.MaxIssues);
        var relevantSourceRelationships = package.Relationships
            .Select(item => (Relationship: item, Role: ClassifyRelationship(item.Type)))
            .Where(item => item.Role is not null)
            .OrderBy(item => item.Relationship.SourcePartUri, StringComparer.Ordinal)
            .ThenBy(item => item.Relationship.Id, StringComparer.Ordinal)
            .ToArray();
        if (relevantSourceRelationships.Length > _options.MaxRelevantRelationships)
        {
            throw new WordActiveContentLimitException(
                $"Active-content relationship count exceeds {_options.MaxRelevantRelationships}."
            );
        }

        var candidateKinds = new Dictionary<string, HashSet<WordActiveContentPayloadKind>>(
            StringComparer.Ordinal
        );
        foreach (var item in relevantSourceRelationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                item.Relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && item.Relationship.ResolvedTargetPartUri is { } targetPartUri
            )
            {
                AddCandidate(candidateKinds, targetPartUri, PayloadKind(item.Role!.Value));
            }
        }
        foreach (var part in package.Parts.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = ClassifyPayloadPart(part);
            if (kind is not null)
            {
                AddCandidate(candidateKinds, part.Uri, kind.Value);
            }
        }
        if (candidateKinds.Count > _options.MaxPayloads)
        {
            throw new WordActiveContentLimitException(
                $"Active-content payload count exceeds {_options.MaxPayloads}."
            );
        }

        var reachable = PackageReachableParts(package, cancellationToken);
        var incomingCounts = package.Relationships
            .Where(item => item.TargetMode == OpcRelationshipTargetMode.Internal)
            .Where(item => item.ResolvedTargetPartUri is not null)
            .GroupBy(item => item.ResolvedTargetPartUri!, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Count(), StringComparer.Ordinal);
        var payloads = new List<WordActiveContentPayload>(candidateKinds.Count);
        var payloadByPart = new Dictionary<string, WordActiveContentPayload>(StringComparer.Ordinal);
        foreach (var pair in candidateKinds.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(pair.Key, out var part))
            {
                continue;
            }
            var kind = SelectPayloadKind(pair.Value);
            if (HasConflictingPayloadKinds(pair.Value))
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVE_PAYLOAD_ROLE_CONFLICT",
                    WordActiveContentIssueSeverity.Warning,
                    "One payload is targeted by conflicting active-content relationship roles; the highest-risk typed role was retained.",
                    part.Uri
                ));
            }
            var payload = new WordActiveContentPayload(
                StableId("wdap_", package.Fingerprint, part.Uri, kind.ToString()),
                kind,
                part.Uri,
                Bound(part.ContentType, 512),
                part.Entry.UncompressedLength,
                part.Entry.Sha256,
                reachable.Contains(part.Uri),
                incomingCounts.GetValueOrDefault(part.Uri),
                IsXml(part),
                IsPotentiallyExecutable(kind, part.ContentType),
                ContainerFamily(kind, part.ContentType)
            );
            payloads.Add(payload);
            payloadByPart.Add(part.Uri, payload);
        }
        foreach (var payload in payloads)
        {
            ChargePayload(payload);
        }

        var relationships = BuildRelationships(
            package,
            relevantSourceRelationships,
            payloadByPart,
            issues,
            cancellationToken
        );
        var xmlBudget = new XmlProjectionBudget();
        var declarations = BuildDeclarations(
            package,
            relationships,
            issues,
            xmlBudget,
            cancellationToken
        );
        var controls = BuildActiveXControls(
            package,
            payloads,
            relationships,
            declarations,
            issues,
            xmlBudget,
            cancellationToken
        );
        ValidateTopology(
            package,
            payloads,
            relationships,
            declarations,
            controls,
            issues,
            cancellationToken
        );

        foreach (var relationship in relationships)
        {
            ChargeRelationship(relationship);
        }
        foreach (var declaration in declarations)
        {
            ChargeDeclaration(declaration);
        }
        foreach (var control in controls)
        {
            ChargeControl(control);
        }
        foreach (var issue in issues.Issues)
        {
            ChargeIssue(issue);
        }
        return new WordActiveContentGraph(
            package.Fingerprint,
            IsMainDocumentMacroEnabled(package),
            declarations,
            controls,
            payloads,
            relationships,
            issues.Issues,
            issues.Truncated
        );
    }

    private IReadOnlyList<WordActiveContentRelationship> BuildRelationships(
        OpcPackageSnapshot package,
        IReadOnlyList<(OpcRelationship Relationship, WordActiveContentRelationshipRole? Role)> source,
        IReadOnlyDictionary<string, WordActiveContentPayload> payloadByPart,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var result = new List<WordActiveContentRelationship>(source.Count);
        var identityOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationship = item.Relationship;
            payloadByPart.TryGetValue(relationship.ResolvedTargetPartUri ?? string.Empty, out var payload);
            var resolved = relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.ResolvedTargetPartUri is not null
                && package.Parts.ContainsKey(relationship.ResolvedTargetPartUri)
                && payload is not null;
            var identity = string.Join(
                '\u001f',
                relationship.SourcePartUri,
                relationship.Id,
                relationship.Type,
                relationship.Target,
                relationship.TargetMode.ToString()
            );
            var occurrence = identityOccurrences.GetValueOrDefault(identity);
            identityOccurrences[identity] = checked(occurrence + 1);
            var typed = new WordActiveContentRelationship(
                StableId(
                    "wdar_",
                    package.Fingerprint,
                    identity,
                    occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ),
                relationship.SourcePartUri,
                Bound(relationship.Id, 1_024)!,
                Bound(relationship.Type, 4_096)!,
                item.Role!.Value,
                Bound(relationship.Target, 8_192)!,
                relationship.TargetMode,
                Bound(relationship.ResolvedTargetPartUri, 2_048),
                payload?.Id,
                resolved
            );
            result.Add(typed);
            if (relationship.TargetMode == OpcRelationshipTargetMode.Invalid)
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVE_RELATIONSHIP_TARGET_MODE_INVALID",
                    WordActiveContentIssueSeverity.Error,
                    "An active-content relationship has an invalid target mode.",
                    relationship.SourcePartUri == "/" ? null : relationship.SourcePartUri,
                    RelationshipId: relationship.Id,
                    SubjectId: typed.Id
                ));
            }
            else if (relationship.TargetMode == OpcRelationshipTargetMode.Internal && !resolved)
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVE_RELATIONSHIP_UNRESOLVED",
                    WordActiveContentIssueSeverity.Error,
                    "An internal active-content relationship does not resolve to a typed existing payload.",
                    relationship.SourcePartUri == "/" ? null : relationship.SourcePartUri,
                    RelationshipId: relationship.Id,
                    SubjectId: typed.Id
                ));
            }
            else if (
                relationship.TargetMode == OpcRelationshipTargetMode.External
                && item.Role is not WordActiveContentRelationshipRole.OleObject
                    and not WordActiveContentRelationshipRole.EmbeddedPackage
            )
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVE_RELATIONSHIP_EXTERNAL_FORBIDDEN",
                    WordActiveContentIssueSeverity.Error,
                    "This active-content relationship role requires an internal package target.",
                    relationship.SourcePartUri == "/" ? null : relationship.SourcePartUri,
                    RelationshipId: relationship.Id,
                    SubjectId: typed.Id
                ));
            }
        }
        foreach (
            var duplicate in result
                .GroupBy(
                    item => (item.SourcePartUri, item.RelationshipId),
                    EqualityComparer<(string SourcePartUri, string RelationshipId)>.Default
                )
                .Where(group => group.Count() > 1)
        )
        {
            foreach (var relationship in duplicate)
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVE_RELATIONSHIP_ID_DUPLICATE",
                    WordActiveContentIssueSeverity.Error,
                    "An active-content relationship ID is duplicated in one source relationship set and cannot be bound unambiguously.",
                    relationship.SourcePartUri == "/" ? null : relationship.SourcePartUri,
                    RelationshipId: relationship.RelationshipId,
                    SubjectId: relationship.Id
                ));
            }
        }
        return result;
    }

    private IReadOnlyList<WordActiveContentDeclaration> BuildDeclarations(
        OpcPackageSnapshot package,
        IReadOnlyList<WordActiveContentRelationship> relationships,
        IssueState issues,
        XmlProjectionBudget xmlBudget,
        CancellationToken cancellationToken
    )
    {
        var relevantBySource = relationships
            .Where(item => item.Role is WordActiveContentRelationshipRole.OleObject
                or WordActiveContentRelationshipRole.ActiveXControl)
            .GroupBy(item => item.SourcePartUri, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.ToArray(), StringComparer.Ordinal);
        var sourcePartUris = relevantBySource.Keys
            .Concat(package.Parts.Values
                .Where(IsWordMarkupPart)
                .Select(part => part.Uri))
            .Where(uri => uri != "/")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (sourcePartUris.Length > _options.MaxXmlParts)
        {
            throw new WordActiveContentLimitException(
                $"Active-content source XML part count exceeds {_options.MaxXmlParts}."
            );
        }
        var result = new List<WordActiveContentDeclaration>();
        var metadataCharacters = 0;
        foreach (var partUri in sourcePartUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part) || !IsXml(part))
            {
                continue;
            }
            var sourceRelationships = relevantBySource.GetValueOrDefault(partUri) ?? [];
            var document = TryParseXml(part, issues, xmlBudget, cancellationToken);
            if (document is null)
            {
                continue;
            }
            var root = document.GetParsedElement(document.Root.Ordinal);
            foreach (var element in root.DescendantsAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                WordActiveContentDeclarationKind? kind = element.Name.NamespaceName switch
                {
                    OfficeNamespace when element.Name.LocalName == "OLEObject" =>
                        WordActiveContentDeclarationKind.OleObject,
                    WordTransitionalNamespace or WordStrictNamespace
                        when element.Name.LocalName == "objectEmbed" =>
                        WordActiveContentDeclarationKind.EmbeddedObject,
                    WordTransitionalNamespace or WordStrictNamespace
                        when element.Name.LocalName == "objectLink" =>
                        WordActiveContentDeclarationKind.LinkedObject,
                    WordTransitionalNamespace or WordStrictNamespace
                        when element.Name.LocalName == "control" =>
                        WordActiveContentDeclarationKind.ActiveXControl,
                    _ => null,
                };
                if (kind is null)
                {
                    continue;
                }
                if (result.Count >= _options.MaxDeclarations)
                {
                    throw new WordActiveContentLimitException(
                        $"Active-content declaration count exceeds {_options.MaxDeclarations}."
                    );
                }
                var ordinal = document.GetElementOrdinal(element);
                var relationshipId = RelationshipId(element);
                var candidates = relationshipId is null
                    ? []
                    : sourceRelationships
                        .Where(item => item.RelationshipId == relationshipId)
                        .ToArray();
                var expectedRole = kind == WordActiveContentDeclarationKind.ActiveXControl
                    ? WordActiveContentRelationshipRole.ActiveXControl
                    : WordActiveContentRelationshipRole.OleObject;
                var roleCandidates = candidates
                    .Where(item => item.Role == expectedRole)
                    .ToArray();
                var linked = roleCandidates.Length == 1 ? roleCandidates[0] : null;
                var id = StableId(
                    "wdad_",
                    package.Fingerprint,
                    part.Uri,
                    ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    kind.Value.ToString()
                );
                var declaration = new WordActiveContentDeclaration(
                    id,
                    kind.Value,
                    part.Uri,
                    ordinal,
                    BoundMetadata(relationshipId, 1_024, ref metadataCharacters),
                    linked?.Id,
                    kind != WordActiveContentDeclarationKind.ActiveXControl
                        ? BoundMetadata(
                            AttributeAny(element, "ProgID", "progId"),
                            256,
                            ref metadataCharacters
                        )
                        : null,
                    BoundMetadata(
                        AttributeAny(element, "Type", "type")
                            ?? (kind == WordActiveContentDeclarationKind.EmbeddedObject
                                ? "Embed"
                                : kind == WordActiveContentDeclarationKind.LinkedObject
                                    ? "Link"
                                    : null),
                        64,
                        ref metadataCharacters
                    ),
                    BoundMetadata(
                        AttributeAny(element, "DrawAspect", "drawAspect"),
                        64,
                        ref metadataCharacters
                    ),
                    BoundMetadata(
                        AttributeAny(element, "UpdateMode", "updateMode"),
                        64,
                        ref metadataCharacters
                    ),
                    BoundMetadata(
                        AttributeAny(element, "ShapeID", "shapeId", "shapeid"),
                        256,
                        ref metadataCharacters
                    ),
                    BoundMetadata(
                        AttributeAny(element, "ObjectID", "objectId"),
                        256,
                        ref metadataCharacters
                    ),
                    kind == WordActiveContentDeclarationKind.ActiveXControl
                        ? BoundMetadata(
                            AttributeAny(element, "name"),
                            1_024,
                            ref metadataCharacters
                        )
                        : null,
                    BoundMetadata(
                        AttributeAny(element, "LinkType", "linkType"),
                        64,
                        ref metadataCharacters
                    ),
                    BoundMetadata(
                        AttributeAny(element, "ServerFormat", "serverFormat"),
                        128,
                        ref metadataCharacters
                    ),
                    BoundMetadata(
                        AttributeAny(element, "LockedField", "lockedField"),
                        32,
                        ref metadataCharacters
                    ),
                    AttributeAnyRaw(element, "FieldCodes", "fieldCodes") is not null,
                    AttributeAnyRaw(element, "FieldCodes", "fieldCodes")?.Length ?? 0,
                    linked is not null
                        && (
                            linked.IsResolved
                            || linked.TargetMode == OpcRelationshipTargetMode.External
                        )
                );
                result.Add(declaration);
                if (relationshipId is null)
                {
                    issues.Add(new WordActiveContentIssue(
                        "ACTIVE_DECLARATION_RELATIONSHIP_MISSING",
                        WordActiveContentIssueSeverity.Error,
                        "An active-content declaration has no relationship ID.",
                        part.Uri,
                        ordinal,
                        SubjectId: id
                    ));
                }
                else if (roleCandidates.Length > 1)
                {
                    issues.Add(new WordActiveContentIssue(
                        "ACTIVE_DECLARATION_RELATIONSHIP_AMBIGUOUS",
                        WordActiveContentIssueSeverity.Error,
                        "An active-content declaration resolves to more than one relationship with the same source ID.",
                        part.Uri,
                        ordinal,
                        relationshipId,
                        id
                    ));
                }
                else if (linked is null)
                {
                    issues.Add(new WordActiveContentIssue(
                        "ACTIVE_DECLARATION_RELATIONSHIP_UNRESOLVED",
                        WordActiveContentIssueSeverity.Error,
                        "An active-content declaration does not resolve to the expected relationship role.",
                        part.Uri,
                        ordinal,
                        relationshipId,
                        id
                    ));
                }
                if (
                    declaration.ProgramId is { } programId
                    && !IsValidOfficeProgramId(programId)
                )
                {
                    issues.Add(new WordActiveContentIssue(
                        "OLE_PROGRAM_ID_NONCONFORMING",
                        WordActiveContentIssueSeverity.Warning,
                        "The OLE ProgID violates the Office length or character restrictions.",
                        part.Uri,
                        ordinal,
                        relationshipId,
                        id
                    ));
                }
            }
        }
        return result;
    }

    private IReadOnlyList<WordActiveXControlDefinition> BuildActiveXControls(
        OpcPackageSnapshot package,
        IReadOnlyList<WordActiveContentPayload> payloads,
        IReadOnlyList<WordActiveContentRelationship> relationships,
        IReadOnlyList<WordActiveContentDeclaration> declarations,
        IssueState issues,
        XmlProjectionBudget xmlBudget,
        CancellationToken cancellationToken
    )
    {
        var result = new List<WordActiveXControlDefinition>();
        var metadataCharacters = 0;
        var relationById = relationships
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.First(), StringComparer.Ordinal);
        foreach (var payload in payloads.Where(item => item.Kind == WordActiveContentPayloadKind.ActiveXXml))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = package.Parts[payload.PartUri];
            var document = TryParseXml(part, issues, xmlBudget, cancellationToken);
            if (document is null)
            {
                continue;
            }
            var root = document.GetParsedElement(document.Root.Ordinal);
            var ordinal = document.GetElementOrdinal(root);
            if (root.Name.NamespaceName != ActiveXNamespace || root.Name.LocalName != "ocx")
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVEX_ROOT_UNEXPECTED",
                    WordActiveContentIssueSeverity.Error,
                    "An ActiveX XML persistence part does not have the expected ax:ocx root.",
                    part.Uri,
                    ordinal,
                    SubjectId: payload.Id
                ));
                continue;
            }
            var relationshipId = RelationshipId(root);
            var binaryCandidates = relationships.Where(item =>
                item.SourcePartUri == part.Uri
                && item.RelationshipId == relationshipId
                && item.Role == WordActiveContentRelationshipRole.ActiveXControlBinary
            ).ToArray();
            var linked = binaryCandidates.Length == 1 ? binaryCandidates[0] : null;
            var declarationIds = declarations
                .Where(item => item.Kind == WordActiveContentDeclarationKind.ActiveXControl)
                .Where(item => item.RelationshipNodeId is not null)
                .Where(item => relationById.TryGetValue(item.RelationshipNodeId!, out var relation)
                    && relation.TargetPartUri == part.Uri)
                .Select(item => item.Id)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var classId = BoundMetadata(Attribute(root, "classid"), 256, ref metadataCharacters);
            var control = new WordActiveXControlDefinition(
                StableId("wdax_", package.Fingerprint, part.Uri),
                part.Uri,
                ordinal,
                classId,
                BoundMetadata(Attribute(root, "persistence"), 128, ref metadataCharacters),
                BoundMetadata(relationshipId, 1_024, ref metadataCharacters),
                linked?.Id,
                linked?.PayloadId,
                root.Elements().Count(item =>
                    item.Name.NamespaceName == ActiveXNamespace
                    && item.Name.LocalName == "ocxPr"
                ),
                Attribute(root, "license") is not null,
                Attribute(root, "license")?.Length ?? 0,
                linked?.IsResolved == true,
                declarationIds
            );
            result.Add(control);
            if (classId is not null && !Guid.TryParse(classId, out _))
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVEX_CLASS_ID_INVALID",
                    WordActiveContentIssueSeverity.Warning,
                    "An ActiveX control class ID is not a GUID.",
                    part.Uri,
                    ordinal,
                    SubjectId: control.Id
                ));
            }
            if (binaryCandidates.Length > 1)
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVEX_BINARY_RELATIONSHIP_AMBIGUOUS",
                    WordActiveContentIssueSeverity.Error,
                    "An ActiveX XML persistence part resolves to more than one binary relationship with the same ID.",
                    part.Uri,
                    ordinal,
                    relationshipId,
                    control.Id
                ));
            }
            else if (relationshipId is null || linked is null)
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVEX_BINARY_RELATIONSHIP_UNRESOLVED",
                    WordActiveContentIssueSeverity.Error,
                    "An ActiveX XML persistence part does not resolve to its binary persistence payload.",
                    part.Uri,
                    ordinal,
                    relationshipId,
                    control.Id
                ));
            }
            if (declarationIds.Length == 0)
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVEX_DECLARATION_MISSING",
                    WordActiveContentIssueSeverity.Warning,
                    "An ActiveX persistence part is not referenced by a parsed Word control declaration.",
                    part.Uri,
                    ordinal,
                    SubjectId: control.Id
                ));
            }
        }
        return result;
    }

    private void ValidateTopology(
        OpcPackageSnapshot package,
        IReadOnlyList<WordActiveContentPayload> payloads,
        IReadOnlyList<WordActiveContentRelationship> relationships,
        IReadOnlyList<WordActiveContentDeclaration> declarations,
        IReadOnlyList<WordActiveXControlDefinition> controls,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var relatedPayloadIds = relationships
            .Where(item => item.PayloadId is not null)
            .Select(item => item.PayloadId!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var payload in payloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!relatedPayloadIds.Contains(payload.Id))
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVE_PAYLOAD_ORPHANED",
                    WordActiveContentIssueSeverity.Warning,
                    "An active-content payload has no typed incoming relationship.",
                    payload.PartUri,
                    SubjectId: payload.Id
                ));
            }
        }

        var vbaProjects = payloads.Where(item => item.Kind == WordActiveContentPayloadKind.VbaProject).ToArray();
        var macroEnabled = IsMainDocumentMacroEnabled(package);
        if (macroEnabled && vbaProjects.Length == 0)
        {
            issues.Add(new WordActiveContentIssue(
                "VBA_PROJECT_MISSING_FOR_MACRO_MAIN_PART",
                WordActiveContentIssueSeverity.Error,
                "The Word main-part content type is macro-enabled but no VBA project payload exists."
            ));
        }
        if (!macroEnabled && vbaProjects.Length != 0)
        {
            issues.Add(new WordActiveContentIssue(
                "VBA_PROJECT_WITH_NON_MACRO_MAIN_PART",
                WordActiveContentIssueSeverity.Warning,
                "A VBA project exists while the Word main-part content type is not macro-enabled."
            ));
        }
        if (vbaProjects.Length > 1)
        {
            issues.Add(new WordActiveContentIssue(
                "VBA_PROJECT_MULTIPLE",
                WordActiveContentIssueSeverity.Error,
                "A Word package contains more than one typed VBA project payload."
            ));
        }
        var mainPartUris = MainDocumentPartUris(package);
        if (mainPartUris.Count > 1)
        {
            issues.Add(new WordActiveContentIssue(
                "ACTIVE_WORD_MAIN_PART_AMBIGUOUS",
                WordActiveContentIssueSeverity.Error,
                "More than one resolved Word main document part is declared; active-content ownership is ambiguous."
            ));
        }
        foreach (var relationship in relationships.Where(item =>
            item.Role == WordActiveContentRelationshipRole.VbaProject
        ))
        {
            if (
                !mainPartUris.Contains(relationship.SourcePartUri)
            )
            {
                issues.Add(new WordActiveContentIssue(
                    "VBA_PROJECT_SOURCE_UNEXPECTED",
                    WordActiveContentIssueSeverity.Error,
                    "A VBA project relationship does not originate from the Word main document part.",
                    relationship.SourcePartUri == "/" ? null : relationship.SourcePartUri,
                    RelationshipId: relationship.RelationshipId,
                    SubjectId: relationship.Id
                ));
            }
        }
        if (
            payloads.Any(item => item.Kind == WordActiveContentPayloadKind.VbaData)
            && vbaProjects.Length == 0
        )
        {
            issues.Add(new WordActiveContentIssue(
                "VBA_DATA_WITHOUT_PROJECT",
                WordActiveContentIssueSeverity.Warning,
                "Word VBA metadata exists without a typed VBA project payload."
            ));
        }

        var signatureOrigins = relationships.Where(item =>
            item.Role == WordActiveContentRelationshipRole.DigitalSignatureOrigin
        ).ToArray();
        var signatures = relationships.Where(item =>
            item.Role == WordActiveContentRelationshipRole.DigitalSignature
        ).ToArray();
        if (signatures.Length != 0 && signatureOrigins.Length == 0)
        {
            issues.Add(new WordActiveContentIssue(
                "SIGNATURE_ORIGIN_MISSING",
                WordActiveContentIssueSeverity.Error,
                "Digital-signature relationships exist without a package signature-origin relationship."
            ));
        }
        if (signatureOrigins.Length > 1)
        {
            issues.Add(new WordActiveContentIssue(
                "SIGNATURE_ORIGIN_MULTIPLE",
                WordActiveContentIssueSeverity.Error,
                "More than one package signature-origin relationship is declared."
            ));
        }
        foreach (var origin in signatureOrigins.Where(item => item.SourcePartUri != "/"))
        {
            issues.Add(new WordActiveContentIssue(
                "SIGNATURE_ORIGIN_SOURCE_INVALID",
                WordActiveContentIssueSeverity.Error,
                "A package signature-origin relationship does not originate at the package root.",
                origin.SourcePartUri,
                RelationshipId: origin.RelationshipId,
                SubjectId: origin.Id
            ));
        }
        foreach (var origin in signatureOrigins)
        {
            if (
                origin.TargetPartUri is null
                || !signatures.Any(item => item.SourcePartUri == origin.TargetPartUri)
            )
            {
                issues.Add(new WordActiveContentIssue(
                    "SIGNATURE_ORIGIN_EMPTY",
                    WordActiveContentIssueSeverity.Error,
                    "A signature-origin part has no typed XML-signature relationship.",
                    origin.TargetPartUri,
                    RelationshipId: origin.RelationshipId,
                    SubjectId: origin.Id
                ));
            }
        }
        var signatureOriginPartUris = signatureOrigins
            .Where(item => item.TargetPartUri is not null)
            .Select(item => item.TargetPartUri!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var signature in signatures.Where(item =>
            !signatureOriginPartUris.Contains(item.SourcePartUri)
        ))
        {
            issues.Add(new WordActiveContentIssue(
                "SIGNATURE_SOURCE_INVALID",
                WordActiveContentIssueSeverity.Error,
                "An XML-signature relationship does not originate from a declared package signature-origin part.",
                signature.SourcePartUri == "/" ? null : signature.SourcePartUri,
                RelationshipId: signature.RelationshipId,
                SubjectId: signature.Id
            ));
        }

        var vbaProjectPartUris = vbaProjects.Select(item => item.PartUri)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var signature in relationships.Where(item =>
            item.Role == WordActiveContentRelationshipRole.VbaProjectSignature
                && !vbaProjectPartUris.Contains(item.SourcePartUri)
        ))
        {
            issues.Add(new WordActiveContentIssue(
                "VBA_PROJECT_SIGNATURE_SOURCE_INVALID",
                WordActiveContentIssueSeverity.Error,
                "A VBA project-signature relationship does not originate from a typed VBA project part.",
                signature.SourcePartUri == "/" ? null : signature.SourcePartUri,
                RelationshipId: signature.RelationshipId,
                SubjectId: signature.Id
            ));
        }

        var boundRelationshipIds = declarations
            .Where(item => item.RelationshipNodeId is not null)
            .Select(item => item.RelationshipNodeId!)
            .Concat(controls.Where(item => item.BinaryRelationshipNodeId is not null)
                .Select(item => item.BinaryRelationshipNodeId!))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var relationship in relationships.Where(item =>
            item.Role is WordActiveContentRelationshipRole.OleObject
                or WordActiveContentRelationshipRole.ActiveXControl
                or WordActiveContentRelationshipRole.ActiveXControlBinary
        ))
        {
            if (!boundRelationshipIds.Contains(relationship.Id))
            {
                issues.Add(new WordActiveContentIssue(
                    "ACTIVE_RELATIONSHIP_UNBOUND_TO_MARKUP",
                    WordActiveContentIssueSeverity.Warning,
                    "An OLE or ActiveX relationship is not bound to a parsed declaration.",
                    relationship.SourcePartUri == "/" ? null : relationship.SourcePartUri,
                    RelationshipId: relationship.RelationshipId,
                    SubjectId: relationship.Id
                ));
            }
        }

        var relationshipById = relationships
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.First(), StringComparer.Ordinal);
        foreach (var declaration in declarations.Where(item =>
            item.RelationshipNodeId is not null
            && relationshipById.ContainsKey(item.RelationshipNodeId)
        ))
        {
            var relationship = relationshipById[declaration.RelationshipNodeId!];
            if (
                declaration.Kind == WordActiveContentDeclarationKind.EmbeddedObject
                && relationship.TargetMode == OpcRelationshipTargetMode.External
            )
            {
                issues.Add(new WordActiveContentIssue(
                    "OLE_EMBED_TARGET_EXTERNAL",
                    WordActiveContentIssueSeverity.Error,
                    "A w:objectEmbed declaration targets an external object instead of an embedded payload.",
                    declaration.SourcePartUri,
                    declaration.SourceElementOrdinal,
                    declaration.RelationshipId,
                    declaration.Id
                ));
            }
            else if (
                declaration.Kind == WordActiveContentDeclarationKind.LinkedObject
                && relationship.TargetMode != OpcRelationshipTargetMode.External
            )
            {
                issues.Add(new WordActiveContentIssue(
                    "OLE_LINK_TARGET_NOT_EXTERNAL",
                    WordActiveContentIssueSeverity.Warning,
                    "A w:objectLink declaration does not use an external target.",
                    declaration.SourcePartUri,
                    declaration.SourceElementOrdinal,
                    declaration.RelationshipId,
                    declaration.Id
                ));
            }
        }
    }

    private LosslessXmlDocument? TryParseXml(
        OpcPart part,
        IssueState issues,
        XmlProjectionBudget xmlBudget,
        CancellationToken cancellationToken
    )
    {
        if (part.Entry.Content.Length > _options.MaxXmlPartBytes)
        {
            throw new WordActiveContentLimitException(
                $"Active-content XML part '{part.Uri}' exceeds {_options.MaxXmlPartBytes} bytes."
            );
        }
        var remainingBytes = checked(_options.MaxTotalXmlBytes - xmlBudget.ParsedBytes);
        if (part.Entry.Content.Length > remainingBytes)
        {
            throw new WordActiveContentLimitException(
                $"Active-content XML exceeds the {_options.MaxTotalXmlBytes}-byte aggregate limit."
            );
        }
        var remainingElements = checked(
            _options.MaxTotalXmlElements - xmlBudget.ParsedElements
        );
        if (remainingElements <= 0)
        {
            throw new WordActiveContentLimitException(
                $"Active-content XML exceeds the {_options.MaxTotalXmlElements}-element aggregate limit."
            );
        }
        xmlBudget.ParsedBytes = checked(
            xmlBudget.ParsedBytes + part.Entry.Content.Length
        );
        var options = LosslessXmlOptions.Default with
        {
            MaxSourceBytes = Math.Min(_options.MaxXmlPartBytes, remainingBytes),
            MaxXmlCharacters = Math.Min(_options.MaxXmlPartBytes, remainingBytes),
            MaxTextCharacters = Math.Min(_options.MaxXmlPartBytes, remainingBytes),
            MaxXmlElements = Math.Min(
                _options.MaxElementsPerXmlPart,
                remainingElements
            ),
        };
        try
        {
            var document = _resourceLease is null
                ? LosslessXmlDocument.Parse(part.Entry.Content, options, cancellationToken)
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.ActiveContent,
                    cancellationToken
                );
            xmlBudget.ParsedElements = checked(
                xmlBudget.ParsedElements + document.Elements.Count
            );
            return document;
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordActiveContentLimitException(
                $"Active-content XML part '{part.Uri}' exceeds a safe XML limit.",
                exception
            );
        }
        catch (LosslessXmlException)
        {
            issues.Add(new WordActiveContentIssue(
                "ACTIVE_XML_UNREADABLE",
                WordActiveContentIssueSeverity.Error,
                "An active-content XML source is not safe, bounded, well-formed XML and was not interpreted.",
                part.Uri
            ));
            return null;
        }
    }

    private static WordActiveContentRelationshipRole? ClassifyRelationship(string type)
    {
        if (type == PackageSignatureRelationshipBase + "origin")
        {
            return WordActiveContentRelationshipRole.DigitalSignatureOrigin;
        }
        if (type == PackageSignatureRelationshipBase + "signature")
        {
            return WordActiveContentRelationshipRole.DigitalSignature;
        }
        if (
            type == MicrosoftOffice2006RelationshipBase + "ui/extensibility"
            || type == MicrosoftOffice2007RelationshipBase + "ui/extensibility"
        )
        {
            return WordActiveContentRelationshipRole.CustomUi;
        }
        if (type == MicrosoftOffice2006RelationshipBase + "ui/userCustomization")
        {
            return WordActiveContentRelationshipRole.QuickAccessToolbarCustomization;
        }
        if (type == MicrosoftOffice2006RelationshipBase + "keyMapCustomizations")
        {
            return WordActiveContentRelationshipRole.KeyMapCustomization;
        }
        if (type == MicrosoftOffice2006RelationshipBase + "activeXControlBinary")
        {
            return WordActiveContentRelationshipRole.ActiveXControlBinary;
        }
        if (type == MicrosoftOffice2006RelationshipBase + "vbaProject")
        {
            return WordActiveContentRelationshipRole.VbaProject;
        }
        if (type == MicrosoftOffice2006RelationshipBase + "wordVbaData")
        {
            return WordActiveContentRelationshipRole.VbaData;
        }
        if (type == MicrosoftOffice2006RelationshipBase + "attachedToolbars")
        {
            return WordActiveContentRelationshipRole.AttachedToolbar;
        }
        if (type == MicrosoftOffice2006RelationshipBase + "vbaProjectSignature")
        {
            return WordActiveContentRelationshipRole.VbaProjectSignature;
        }
        if (
            type == TransitionalRelationshipBase + "oleObject"
            || type == StrictRelationshipBase + "oleObject"
        )
        {
            return WordActiveContentRelationshipRole.OleObject;
        }
        if (
            type == TransitionalRelationshipBase + "package"
            || type == StrictRelationshipBase + "package"
        )
        {
            return WordActiveContentRelationshipRole.EmbeddedPackage;
        }
        if (
            type == TransitionalRelationshipBase + "control"
            || type == StrictRelationshipBase + "control"
        )
        {
            return WordActiveContentRelationshipRole.ActiveXControl;
        }
        return null;
    }

    private static WordActiveContentPayloadKind PayloadKind(
        WordActiveContentRelationshipRole role
    ) => role switch
    {
        WordActiveContentRelationshipRole.OleObject => WordActiveContentPayloadKind.OleObject,
        WordActiveContentRelationshipRole.EmbeddedPackage => WordActiveContentPayloadKind.EmbeddedPackage,
        WordActiveContentRelationshipRole.ActiveXControl => WordActiveContentPayloadKind.ActiveXXml,
        WordActiveContentRelationshipRole.ActiveXControlBinary => WordActiveContentPayloadKind.ActiveXBinary,
        WordActiveContentRelationshipRole.VbaProject => WordActiveContentPayloadKind.VbaProject,
        WordActiveContentRelationshipRole.VbaData => WordActiveContentPayloadKind.VbaData,
        WordActiveContentRelationshipRole.AttachedToolbar => WordActiveContentPayloadKind.AttachedToolbar,
        WordActiveContentRelationshipRole.CustomUi => WordActiveContentPayloadKind.CustomUi,
        WordActiveContentRelationshipRole.QuickAccessToolbarCustomization => WordActiveContentPayloadKind.QuickAccessToolbarCustomization,
        WordActiveContentRelationshipRole.KeyMapCustomization => WordActiveContentPayloadKind.KeyMapCustomization,
        WordActiveContentRelationshipRole.VbaProjectSignature => WordActiveContentPayloadKind.VbaProjectSignature,
        WordActiveContentRelationshipRole.DigitalSignatureOrigin => WordActiveContentPayloadKind.DigitalSignatureOrigin,
        WordActiveContentRelationshipRole.DigitalSignature => WordActiveContentPayloadKind.DigitalSignature,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static WordActiveContentPayloadKind? ClassifyPayloadPart(OpcPart part)
    {
        if (EqualsContentType(part, OleObjectContentType)) return WordActiveContentPayloadKind.OleObject;
        if (EqualsContentType(part, ActiveXXmlContentType)) return WordActiveContentPayloadKind.ActiveXXml;
        if (EqualsContentType(part, ActiveXBinaryContentType)) return WordActiveContentPayloadKind.ActiveXBinary;
        if (EqualsContentType(part, VbaProjectContentType)) return WordActiveContentPayloadKind.VbaProject;
        if (EqualsContentType(part, VbaDataContentType)) return WordActiveContentPayloadKind.VbaData;
        if (EqualsContentType(part, AttachedToolbarContentType)
            || EqualsContentType(part, WordAttachedToolbarContentType)) return WordActiveContentPayloadKind.AttachedToolbar;
        if (EqualsContentType(part, KeyMapCustomizationContentType)) return WordActiveContentPayloadKind.KeyMapCustomization;
        if (EqualsContentType(part, VbaProjectSignatureContentType)) return WordActiveContentPayloadKind.VbaProjectSignature;
        if (EqualsContentType(part, SignatureOriginContentType)) return WordActiveContentPayloadKind.DigitalSignatureOrigin;
        if (EqualsContentType(part, SignatureContentType)) return WordActiveContentPayloadKind.DigitalSignature;
        if (part.Uri.Contains("/customUI/", StringComparison.OrdinalIgnoreCase)) return WordActiveContentPayloadKind.CustomUi;
        if (part.Uri.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase)) return WordActiveContentPayloadKind.EmbeddedPackage;
        return null;
    }

    private static WordActiveContentPayloadKind SelectPayloadKind(
        IReadOnlySet<WordActiveContentPayloadKind> kinds
    )
    {
        WordActiveContentPayloadKind[] precedence =
        [
            WordActiveContentPayloadKind.VbaProject,
            WordActiveContentPayloadKind.ActiveXBinary,
            WordActiveContentPayloadKind.ActiveXXml,
            WordActiveContentPayloadKind.OleObject,
            WordActiveContentPayloadKind.AttachedToolbar,
            WordActiveContentPayloadKind.QuickAccessToolbarCustomization,
            WordActiveContentPayloadKind.KeyMapCustomization,
            WordActiveContentPayloadKind.VbaProjectSignature,
            WordActiveContentPayloadKind.CustomUi,
            WordActiveContentPayloadKind.VbaData,
            WordActiveContentPayloadKind.EmbeddedPackage,
            WordActiveContentPayloadKind.DigitalSignature,
            WordActiveContentPayloadKind.DigitalSignatureOrigin,
        ];
        return precedence.First(kinds.Contains);
    }

    private static bool HasConflictingPayloadKinds(
        IReadOnlySet<WordActiveContentPayloadKind> kinds
    )
    {
        if (kinds.Count <= 1)
        {
            return false;
        }
        return !kinds.All(kind => kind is
            WordActiveContentPayloadKind.CustomUi
            or WordActiveContentPayloadKind.QuickAccessToolbarCustomization
            or WordActiveContentPayloadKind.KeyMapCustomization
        );
    }

    private static bool IsMainDocumentMacroEnabled(OpcPackageSnapshot package)
    {
        return MainDocumentPartUris(package).Any(item =>
            package.Parts.TryGetValue(item, out var mainPart)
            && WordPackageConformance.IsMacroEnabledWordMainContentType(
                mainPart.ContentType
            )
        );
    }

    private static IReadOnlySet<string> MainDocumentPartUris(OpcPackageSnapshot package) =>
        package.Relationships
            .Where(item => item.SourcePartUri == "/")
            .Where(item => item.TargetMode == OpcRelationshipTargetMode.Internal)
            .Where(item => WordPackageConformance.IsOfficeDocumentRelationshipType(item.Type))
            .Select(item => item.ResolvedTargetPartUri)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(package.Parts.ContainsKey)
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsPotentiallyExecutable(
        WordActiveContentPayloadKind kind,
        string? contentType
    ) => kind is WordActiveContentPayloadKind.OleObject
        or WordActiveContentPayloadKind.ActiveXXml
        or WordActiveContentPayloadKind.ActiveXBinary
        or WordActiveContentPayloadKind.VbaProject
        or WordActiveContentPayloadKind.VbaData
        or WordActiveContentPayloadKind.AttachedToolbar
        or WordActiveContentPayloadKind.CustomUi
        or WordActiveContentPayloadKind.QuickAccessToolbarCustomization
        or WordActiveContentPayloadKind.KeyMapCustomization
        or WordActiveContentPayloadKind.VbaProjectSignature
        || contentType?.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) == true;

    private static string ContainerFamily(
        WordActiveContentPayloadKind kind,
        string? contentType
    )
    {
        if (kind == WordActiveContentPayloadKind.OleObject) return "ole_compound";
        if (kind is WordActiveContentPayloadKind.ActiveXXml or WordActiveContentPayloadKind.ActiveXBinary) return "activex";
        if (kind is WordActiveContentPayloadKind.VbaProject or WordActiveContentPayloadKind.VbaData) return "vba";
        if (kind == WordActiveContentPayloadKind.VbaProjectSignature) return "vba_signature";
        if (kind is WordActiveContentPayloadKind.DigitalSignature or WordActiveContentPayloadKind.DigitalSignatureOrigin) return "digital_signature";
        if (kind is WordActiveContentPayloadKind.CustomUi
            or WordActiveContentPayloadKind.QuickAccessToolbarCustomization
            or WordActiveContentPayloadKind.KeyMapCustomization) return "office_custom_ui";
        if (kind == WordActiveContentPayloadKind.AttachedToolbar) return "attached_toolbar";
        if (contentType?.Contains("spreadsheetml", StringComparison.OrdinalIgnoreCase) == true
            || contentType?.Contains("ms-excel", StringComparison.OrdinalIgnoreCase) == true) return "excel";
        if (contentType?.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase) == true
            || contentType?.Contains("ms-word", StringComparison.OrdinalIgnoreCase) == true) return "word";
        if (contentType?.Contains("presentationml", StringComparison.OrdinalIgnoreCase) == true
            || contentType?.Contains("ms-powerpoint", StringComparison.OrdinalIgnoreCase) == true) return "powerpoint";
        if (contentType?.Contains("visio", StringComparison.OrdinalIgnoreCase) == true) return "visio";
        if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase)) return "pdf";
        return kind == WordActiveContentPayloadKind.EmbeddedPackage
            ? "embedded_package"
            : kind.ToString().ToLowerInvariant();
    }

    private static bool IsXml(OpcPart part)
    {
        if (part.Uri.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var contentType = part.ContentType;
        return string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase)
            || contentType?.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsWordMarkupPart(OpcPart part) =>
        part.Uri.StartsWith("/word/", StringComparison.OrdinalIgnoreCase)
        && IsXml(part)
        && !EqualsContentType(part, ActiveXXmlContentType);

    private static bool EqualsContentType(OpcPart part, string expected) =>
        string.Equals(part.ContentType, expected, StringComparison.OrdinalIgnoreCase);

    private static string? RelationshipId(XElement element) =>
        element.Attributes()
            .Where(attribute => attribute.Name.LocalName == "id")
            .Where(attribute =>
                attribute.Name.NamespaceName == TransitionalRelationshipNamespace
                || attribute.Name.NamespaceName == StrictRelationshipNamespace
            )
            .Select(attribute => attribute.Value.Trim())
            .FirstOrDefault(value => value.Length != 0);

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes()
            .Where(attribute => attribute.Name.LocalName == localName)
            .Where(attribute =>
                attribute.Name.NamespaceName.Length == 0
                || attribute.Name.NamespaceName == element.Name.NamespaceName
            )
            .Select(attribute => attribute.Value.Trim())
            .FirstOrDefault(value => value.Length != 0);

    private static string? AttributeAny(XElement element, params string[] localNames)
    {
        foreach (var localName in localNames)
        {
            var value = Attribute(element, localName);
            if (value is not null)
            {
                return value;
            }
        }
        return null;
    }

    private static string? AttributeAnyRaw(XElement element, params string[] localNames)
    {
        foreach (var localName in localNames)
        {
            var attribute = element.Attributes()
                .FirstOrDefault(item =>
                    item.Name.LocalName == localName
                    && (
                        item.Name.NamespaceName.Length == 0
                        || item.Name.NamespaceName == element.Name.NamespaceName
                    )
                );
            if (attribute is not null)
            {
                return attribute.Value;
            }
        }
        return null;
    }

    private string? BoundMetadata(
        string? value,
        int maximumCharacters,
        ref int totalCharacters
    )
    {
        var bounded = Bound(value, maximumCharacters);
        if (bounded is null)
        {
            return null;
        }
        totalCharacters = checked(totalCharacters + bounded.Length);
        if (totalCharacters > _options.MaxMetadataCharacters)
        {
            throw new WordActiveContentLimitException(
                $"Active-content metadata exceeds {_options.MaxMetadataCharacters} characters."
            );
        }
        return bounded;
    }

    private static string? Bound(string? value, int maximumCharacters)
    {
        if (value is null)
        {
            return null;
        }
        return value.Length <= maximumCharacters ? value : value[..maximumCharacters];
    }

    private void ChargePayload(WordActiveContentPayload item)
    {
        if (_resourceLease is null)
        {
            return;
        }
        long bytes = 256;
        bytes = AddAccountedStrings(
            bytes,
            item.Id,
            item.PartUri,
            item.ContentType,
            item.Sha256,
            item.ContainerFamily
        );
        _resourceLease.Charge(WordOperationResourceStage.ActiveContent, bytes);
    }

    private void ChargeRelationship(WordActiveContentRelationship item)
    {
        if (_resourceLease is null)
        {
            return;
        }
        long bytes = 288;
        bytes = AddAccountedStrings(
            bytes,
            item.Id,
            item.SourcePartUri,
            item.RelationshipId,
            item.RelationshipType,
            item.Target,
            item.TargetPartUri,
            item.PayloadId
        );
        _resourceLease.Charge(WordOperationResourceStage.ActiveContent, bytes);
    }

    private void ChargeDeclaration(WordActiveContentDeclaration item)
    {
        if (_resourceLease is null)
        {
            return;
        }
        long bytes = 384;
        bytes = AddAccountedStrings(
            bytes,
            item.Id,
            item.SourcePartUri,
            item.RelationshipId,
            item.RelationshipNodeId,
            item.ProgramId,
            item.ObjectType,
            item.DrawAspect,
            item.UpdateMode,
            item.ShapeId,
            item.ObjectId,
            item.ControlName,
            item.LinkType,
            item.ServerFormat,
            item.LockedField
        );
        _resourceLease.Charge(WordOperationResourceStage.ActiveContent, bytes);
    }

    private void ChargeControl(WordActiveXControlDefinition item)
    {
        if (_resourceLease is null)
        {
            return;
        }
        long bytes = 320;
        bytes = AddAccountedStrings(
            bytes,
            item.Id,
            item.PartUri,
            item.ClassId,
            item.Persistence,
            item.BinaryRelationshipId,
            item.BinaryRelationshipNodeId,
            item.BinaryPayloadId
        );
        foreach (var declarationId in item.DeclarationIds)
        {
            bytes = checked(
                bytes
                    + 16
                    + WordOperationResourceAccounting.AccountedStringBytes(
                        declarationId
                    )
            );
        }
        _resourceLease.Charge(WordOperationResourceStage.ActiveContent, bytes);
    }

    private void ChargeIssue(WordActiveContentIssue item)
    {
        if (_resourceLease is null)
        {
            return;
        }
        long bytes = 256;
        bytes = AddAccountedStrings(
            bytes,
            item.Code,
            item.Message,
            item.PartUri,
            item.RelationshipId,
            item.SubjectId
        );
        _resourceLease.Charge(WordOperationResourceStage.ActiveContent, bytes);
    }

    private static long AddAccountedStrings(long bytes, params string?[] values)
    {
        foreach (var value in values)
        {
            bytes = checked(
                bytes + WordOperationResourceAccounting.AccountedStringBytes(value)
            );
        }
        return bytes;
    }

    private static bool IsValidOfficeProgramId(string value)
    {
        if (value.Length is 0 or >= 39 || char.IsDigit(value[0]))
        {
            return false;
        }
        return value.All(character => char.IsLetterOrDigit(character) || character == '.');
    }

    private static void AddCandidate(
        IDictionary<string, HashSet<WordActiveContentPayloadKind>> candidates,
        string partUri,
        WordActiveContentPayloadKind kind
    )
    {
        if (!candidates.TryGetValue(partUri, out var kinds))
        {
            kinds = [];
            candidates.Add(partUri, kinds);
        }
        kinds.Add(kind);
    }

    private static IReadOnlySet<string> PackageReachableParts(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { "/" };
        var queue = new Queue<string>();
        queue.Enqueue("/");
        while (queue.TryDequeue(out var source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var relationship in package.RelationshipsFrom(source))
            {
                if (
                    relationship.TargetMode != OpcRelationshipTargetMode.Internal
                    || relationship.ResolvedTargetPartUri is not { } target
                    || !package.Parts.ContainsKey(target)
                )
                {
                    continue;
                }
                reachable.Add(target);
                if (visited.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }
        return reachable;
    }

    private static string StableId(string prefix, params string[] components)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join('\u001f', components));
        var hash = SHA256.HashData(payload);
        return prefix + Convert.ToBase64String(hash.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class IssueState
    {
        private readonly int _maximum;
        private readonly List<WordActiveContentIssue> _issues = [];

        public IssueState(int maximum) => _maximum = maximum;

        public IReadOnlyList<WordActiveContentIssue> Issues => _issues;

        public bool Truncated { get; private set; }

        public void Add(WordActiveContentIssue issue)
        {
            if (_issues.Count < _maximum)
            {
                _issues.Add(issue);
            }
            else
            {
                Truncated = true;
            }
        }
    }

    private sealed class XmlProjectionBudget
    {
        public int ParsedBytes { get; set; }

        public int ParsedElements { get; set; }
    }
}

public class WordActiveContentProjectionException : IOException
{
    public WordActiveContentProjectionException(string message)
        : base(message)
    { }

    public WordActiveContentProjectionException(string message, Exception innerException)
        : base(message, innerException)
    { }
}

public sealed class WordActiveContentLimitException : WordActiveContentProjectionException
{
    public WordActiveContentLimitException(string message)
        : base(message)
    { }

    public WordActiveContentLimitException(string message, Exception innerException)
        : base(message, innerException)
    { }
}
