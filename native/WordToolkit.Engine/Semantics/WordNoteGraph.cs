using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordNoteKind
{
    Footnote,
    Endnote,
}

public enum WordNoteDefinitionType
{
    Normal,
    Separator,
    ContinuationSeparator,
    ContinuationNotice,
    Unknown,
}

public enum WordNoteIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordNoteGraphOptions
{
    public static WordNoteGraphOptions Default { get; } = new();

    public int MaxDefinitions { get; init; } = 100_000;

    public int MaxReferences { get; init; } = 100_000;

    public int MaxSpecialReferences { get; init; } = 10_000;

    public int MaxPropertyScopes { get; init; } = 10_000;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxDefinitions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDefinitions));
        }
        if (MaxReferences <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReferences));
        }
        if (MaxSpecialReferences <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSpecialReferences));
        }
        if (MaxPropertyScopes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPropertyScopes));
        }
        if (MaxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxIssues));
        }
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
    }
}

public sealed record WordNoteDefinition(
    string Id,
    string Fingerprint,
    WordNoteKind Kind,
    WordNoteDefinitionType DefinitionType,
    int? OoxmlId,
    string? RawOoxmlId,
    string PartUri,
    int SourceElementOrdinal,
    SemanticNodeId? SemanticNodeId,
    string StructuralFingerprint,
    string ContentFingerprint,
    int ReferenceCount,
    int SpecialReferenceCount,
    int ParagraphCount,
    int TextCharacterCount,
    bool HasReferenceMark,
    bool HasComplexContent,
    bool IsOrphan,
    bool EmptyOrphanRemovalCandidate,
    bool RedundantDuplicateRemovalCandidate
);

public sealed record WordNoteReference(
    string Id,
    string Fingerprint,
    WordNoteKind Kind,
    int? OoxmlId,
    string? RawOoxmlId,
    string PartUri,
    int SourceElementOrdinal,
    SemanticNodeId? SemanticNodeId,
    bool CustomMarkFollows,
    bool CustomMarkValueValid,
    bool NestedInsideNoteStory,
    string ResolutionStatus,
    IReadOnlyList<string> MatchingDefinitionIds
);

public sealed record WordNoteSpecialReference(
    string Id,
    string Fingerprint,
    WordNoteKind Kind,
    int? OoxmlId,
    string? RawOoxmlId,
    string PartUri,
    int SourceElementOrdinal,
    string ResolutionStatus,
    IReadOnlyList<string> MatchingDefinitionIds
);

public sealed record WordNoteNumberingPolicy(
    string Id,
    WordNoteKind Kind,
    string Scope,
    int? SectionIndex,
    string PartUri,
    int SourceElementOrdinal,
    string? Position,
    string? NumberFormat,
    int? NumberStart,
    string? RawNumberStart,
    string? NumberRestart,
    bool ValuesValid,
    IReadOnlyList<string> DuplicateProperties
);

public sealed record WordNoteIssue(
    string Id,
    string Fingerprint,
    string Code,
    WordNoteIssueSeverity Severity,
    WordNoteKind? Kind,
    string Message,
    string? SubjectId,
    string? PartUri,
    int? SourceElementOrdinal,
    bool RepairCandidate
);

public sealed class WordNoteGraph
{
    internal WordNoteGraph(
        string packageFingerprint,
        string mainPartUri,
        IReadOnlyList<WordNoteDefinition> definitions,
        IReadOnlyList<WordNoteReference> references,
        IReadOnlyList<WordNoteSpecialReference> specialReferences,
        IReadOnlyList<WordNoteNumberingPolicy> numberingPolicies,
        IReadOnlyList<WordNoteIssue> issues,
        bool analysisExecutionComplete,
        bool documentCoverageComplete,
        bool issuesTruncated
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        Definitions = new ReadOnlyCollection<WordNoteDefinition>(definitions.ToArray());
        References = new ReadOnlyCollection<WordNoteReference>(references.ToArray());
        SpecialReferences = new ReadOnlyCollection<WordNoteSpecialReference>(
            specialReferences.ToArray()
        );
        NumberingPolicies = new ReadOnlyCollection<WordNoteNumberingPolicy>(
            numberingPolicies.ToArray()
        );
        Issues = new ReadOnlyCollection<WordNoteIssue>(issues.ToArray());
        AnalysisExecutionComplete = analysisExecutionComplete;
        DocumentCoverageComplete = documentCoverageComplete;
        IssuesTruncated = issuesTruncated;
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public IReadOnlyList<WordNoteDefinition> Definitions { get; }

    public IReadOnlyList<WordNoteReference> References { get; }

    public IReadOnlyList<WordNoteSpecialReference> SpecialReferences { get; }

    public IReadOnlyList<WordNoteNumberingPolicy> NumberingPolicies { get; }

    public IReadOnlyList<WordNoteIssue> Issues { get; }

    public bool AnalysisExecutionComplete { get; }

    public bool DocumentCoverageComplete { get; }

    public bool IssuesTruncated { get; }
}

public sealed class WordNoteGraphBuilder
{
    private const string TransitionalRelationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StrictRelationships =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/";

    private static readonly HashSet<string> EmptyOrdinaryNoteAllowlist = new(
        [
            "p",
            "pPr",
            "pStyle",
            "r",
            "rPr",
            "rStyle",
            "t",
            "delText",
            "footnoteRef",
            "endnoteRef",
        ],
        StringComparer.Ordinal
    );

    private readonly WordNoteGraphOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;

    public WordNoteGraphBuilder(WordNoteGraphOptions? options = null)
    {
        _options = options ?? WordNoteGraphOptions.Default;
        _options.Validate();
        _xmlOptions = LosslessXmlOptions.Default with
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
        };
    }

    public WordNoteGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument? semanticDocument = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        semanticDocument ??= new WordSemanticProjector().Project(
            package,
            cancellationToken
        );
        if (!string.Equals(
                semanticDocument.PackageFingerprint,
                package.Fingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordNoteProjectionException(
                "Semantic document does not belong to the supplied package snapshot."
            );
        }

        var mainPartUri = semanticDocument.MainPartUri;
        var state = new BuildState(_options, package.Fingerprint);
        var sources = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        var noteParts = ResolveNoteParts(package, mainPartUri, state, cancellationToken);

        var mutableDefinitions = new List<MutableDefinition>();
        foreach (var node in semanticDocument.Nodes.Where(node =>
            node.Kind is WordSemanticNodeKind.Footnote or WordSemanticNodeKind.Endnote
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mutableDefinitions.Count == _options.MaxDefinitions)
            {
                throw new WordNoteLimitException(
                    $"Document contains more than {_options.MaxDefinitions} note definitions."
                );
            }
            var source = ParsePart(package, node.SourcePartUri, sources, cancellationToken);
            var element = source.GetParsedElement(node.SourceElementOrdinal);
            var kind = node.Kind == WordSemanticNodeKind.Footnote
                ? WordNoteKind.Footnote
                : WordNoteKind.Endnote;
            mutableDefinitions.Add(CreateDefinition(
                package.Fingerprint,
                node,
                element,
                kind,
                source
            ));
        }

        var mutableReferences = new List<MutableReference>();
        foreach (var node in semanticDocument.Nodes.Where(node =>
            node.Kind is WordSemanticNodeKind.FootnoteReference
                or WordSemanticNodeKind.EndnoteReference
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mutableReferences.Count == _options.MaxReferences)
            {
                throw new WordNoteLimitException(
                    $"Document contains more than {_options.MaxReferences} note references."
                );
            }
            var source = ParsePart(package, node.SourcePartUri, sources, cancellationToken);
            var element = source.GetParsedElement(node.SourceElementOrdinal);
            var kind = node.Kind == WordSemanticNodeKind.FootnoteReference
                ? WordNoteKind.Footnote
                : WordNoteKind.Endnote;
            mutableReferences.Add(CreateReference(
                package.Fingerprint,
                node,
                element,
                kind,
                noteParts
            ));
        }

        var specialReferences = new List<MutableSpecialReference>();
        var numberingPolicies = new List<WordNoteNumberingPolicy>();
        ProjectSettingsAndSections(
            package,
            mainPartUri,
            sources,
            specialReferences,
            numberingPolicies,
            state,
            cancellationToken
        );

        Resolve(
            mutableDefinitions,
            mutableReferences,
            specialReferences,
            state,
            cancellationToken
        );

        var definitions = mutableDefinitions
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.PartUri, StringComparer.Ordinal)
            .ThenBy(item => item.SourceElementOrdinal)
            .Select(item => item.Freeze())
            .ToArray();
        var references = mutableReferences
            .OrderBy(item => item.PartUri, StringComparer.Ordinal)
            .ThenBy(item => item.SourceElementOrdinal)
            .Select(item => item.Freeze())
            .ToArray();
        var frozenSpecialReferences = specialReferences
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.SourceElementOrdinal)
            .Select(item => item.Freeze())
            .ToArray();

        return new WordNoteGraph(
            package.Fingerprint,
            mainPartUri,
            definitions,
            references,
            frozenSpecialReferences,
            numberingPolicies
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.Scope, StringComparer.Ordinal)
                .ThenBy(item => item.SectionIndex)
                .ToArray(),
            state.Issues,
            analysisExecutionComplete: true,
            documentCoverageComplete: state.DocumentCoverageComplete,
            state.IssuesTruncated
        );
    }

    private static Dictionary<WordNoteKind, string?> ResolveNoteParts(
        OpcPackageSnapshot package,
        string mainPartUri,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var result = new Dictionary<WordNoteKind, string?>
        {
            [WordNoteKind.Footnote] = null,
            [WordNoteKind.Endnote] = null,
        };
        foreach (var kind in result.Keys.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipName = kind == WordNoteKind.Footnote
                ? "footnotes"
                : "endnotes";
            var relationships = package.RelationshipsFrom(mainPartUri)
                .Where(item => RelationshipName(item.Type) == relationshipName)
                .ToArray();
            if (relationships.Length > 1)
            {
                state.CoverageIssue(
                    "NOTE_PART_RELATIONSHIP_AMBIGUOUS",
                    WordNoteIssueSeverity.Error,
                    kind,
                    "The main document has more than one relationship for this note part.",
                    null,
                    mainPartUri,
                    null
                );
                continue;
            }
            if (relationships.Length == 0)
            {
                continue;
            }
            var relationship = relationships[0];
            if (relationship.TargetMode != OpcRelationshipTargetMode.Internal
                || relationship.ResolvedTargetPartUri is null
                || !package.Parts.ContainsKey(relationship.ResolvedTargetPartUri))
            {
                state.CoverageIssue(
                    "NOTE_PART_RELATIONSHIP_UNRESOLVED",
                    WordNoteIssueSeverity.Error,
                    kind,
                    "The note-part relationship does not resolve to one internal package part.",
                    null,
                    mainPartUri,
                    null
                );
                continue;
            }
            result[kind] = relationship.ResolvedTargetPartUri;
        }
        return result;
    }

    private MutableDefinition CreateDefinition(
        string packageFingerprint,
        WordSemanticNode node,
        XElement element,
        WordNoteKind kind,
        LosslessXmlDocument source
    )
    {
        var rawId = WordAttribute(element, "id");
        var id = ParseInt32(rawId);
        var type = DefinitionType(WordAttribute(element, "type"));
        var descendants = element.DescendantsAndSelf().ToArray();
        var paragraphCount = descendants.Count(item => IsWordElement(item, "p"));
        var textCharacterCount = descendants
            .Where(item => IsWordElement(item, "t") || IsWordElement(item, "delText"))
            .Sum(item => item.Value.Length);
        var hasReferenceMark = descendants.Any(item =>
            IsWordElement(item, kind == WordNoteKind.Footnote ? "footnoteRef" : "endnoteRef")
        );
        var hasComplexContent = descendants
            .Skip(1)
            .Any(item =>
                IsWordNamespace(item.Name.NamespaceName)
                    ? !EmptyOrdinaryNoteAllowlist.Contains(item.Name.LocalName)
                    : true
            );
        var canonical = element.ToString(SaveOptions.DisableFormatting);
        var contentFingerprint = HashText(canonical);
        var fingerprint = Fingerprint(
            "definition",
            packageFingerprint,
            kind.ToString(),
            node.SourcePartUri,
            node.SourceElementOrdinal.ToString(CultureInfo.InvariantCulture),
            rawId ?? string.Empty,
            type.ToString(),
            contentFingerprint
        );
        return new MutableDefinition(
            StableId("wnd_", fingerprint),
            fingerprint,
            kind,
            type,
            id,
            rawId,
            node.SourcePartUri,
            node.SourceElementOrdinal,
            node.Id,
            node.StructuralFingerprint,
            contentFingerprint,
            paragraphCount,
            textCharacterCount,
            hasReferenceMark,
            hasComplexContent,
            canonical
        );
    }

    private static MutableReference CreateReference(
        string packageFingerprint,
        WordSemanticNode node,
        XElement element,
        WordNoteKind kind,
        IReadOnlyDictionary<WordNoteKind, string?> noteParts
    )
    {
        var rawId = WordAttribute(element, "id");
        var id = ParseInt32(rawId);
        var custom = ParseOnOff(WordAttribute(element, "customMarkFollows"));
        var nested = noteParts.Values.Any(part =>
            part is not null
            && string.Equals(part, node.SourcePartUri, StringComparison.Ordinal)
        );
        var fingerprint = Fingerprint(
            "reference",
            packageFingerprint,
            kind.ToString(),
            node.SourcePartUri,
            node.SourceElementOrdinal.ToString(CultureInfo.InvariantCulture),
            rawId ?? string.Empty,
            custom.Raw ?? string.Empty
        );
        return new MutableReference(
            StableId("wnr_", fingerprint),
            fingerprint,
            kind,
            id,
            rawId,
            node.SourcePartUri,
            node.SourceElementOrdinal,
            node.Id,
            custom.Value,
            custom.Valid,
            nested
        );
    }

    private void ProjectSettingsAndSections(
        OpcPackageSnapshot package,
        string mainPartUri,
        IDictionary<string, LosslessXmlDocument> sources,
        ICollection<MutableSpecialReference> specialReferences,
        ICollection<WordNoteNumberingPolicy> numberingPolicies,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var settingsRelationships = package.RelationshipsFrom(mainPartUri)
            .Where(item => RelationshipName(item.Type) == "settings")
            .ToArray();
        if (settingsRelationships.Length > 1)
        {
            state.CoverageIssue(
                "NOTE_SETTINGS_RELATIONSHIP_AMBIGUOUS",
                WordNoteIssueSeverity.Error,
                null,
                "The main document has more than one settings relationship.",
                null,
                mainPartUri,
                null
            );
        }
        else if (settingsRelationships.Length == 1)
        {
            var relationship = settingsRelationships[0];
            if (relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.ResolvedTargetPartUri is { } settingsUri
                && package.Parts.ContainsKey(settingsUri))
            {
                var source = ParsePart(package, settingsUri, sources, cancellationToken);
                var root = source.ParsedDocument.Root;
                if (root is not null && IsWordElement(root, "settings"))
                {
                    foreach (var kind in Enum.GetValues<WordNoteKind>())
                    {
                        var propertyName = kind == WordNoteKind.Footnote
                            ? "footnotePr"
                            : "endnotePr";
                        var properties = root.Elements()
                            .Where(item => IsWordElement(item, propertyName))
                            .ToArray();
                        if (properties.Length > 1)
                        {
                            state.CoverageIssue(
                                "NOTE_DOCUMENT_PROPERTIES_DUPLICATE",
                                WordNoteIssueSeverity.Error,
                                kind,
                                "Document-wide note properties are duplicated.",
                                null,
                                settingsUri,
                                source.GetElementOrdinal(properties[1])
                            );
                        }
                        if (properties.Length != 1)
                        {
                            continue;
                        }
                        ProjectPropertySet(
                            package.Fingerprint,
                            source,
                            settingsUri,
                            properties[0],
                            kind,
                            "document",
                            null,
                            specialReferences,
                            numberingPolicies,
                            state
                        );
                    }
                }
                else
                {
                    state.CoverageIssue(
                        "NOTE_SETTINGS_ROOT_INVALID",
                        WordNoteIssueSeverity.Error,
                        null,
                        "The settings part does not have the expected WordprocessingML root.",
                        null,
                        settingsUri,
                        null
                    );
                }
            }
            else
            {
                state.CoverageIssue(
                    "NOTE_SETTINGS_RELATIONSHIP_UNRESOLVED",
                    WordNoteIssueSeverity.Error,
                    null,
                    "The settings relationship does not resolve to one internal part.",
                    null,
                    mainPartUri,
                    null
                );
            }
        }

        if (!package.Parts.TryGetValue(mainPartUri, out var mainPart))
        {
            return;
        }
        var mainSource = ParsePart(package, mainPartUri, sources, cancellationToken);
        var sections = mainSource.ParsedDocument.Descendants()
            .Where(item => IsWordElement(item, "sectPr"))
            .ToArray();
        for (var index = 0; index < sections.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var kind in Enum.GetValues<WordNoteKind>())
            {
                var propertyName = kind == WordNoteKind.Footnote
                    ? "footnotePr"
                    : "endnotePr";
                var properties = sections[index].Elements()
                    .Where(item => IsWordElement(item, propertyName))
                    .ToArray();
                if (properties.Length > 1)
                {
                    state.CoverageIssue(
                        "NOTE_SECTION_PROPERTIES_DUPLICATE",
                        WordNoteIssueSeverity.Error,
                        kind,
                        "Section-wide note properties are duplicated.",
                        null,
                        mainPartUri,
                        mainSource.GetElementOrdinal(properties[1])
                    );
                }
                if (properties.Length != 1)
                {
                    continue;
                }
                ProjectPropertySet(
                    package.Fingerprint,
                    mainSource,
                    mainPartUri,
                    properties[0],
                    kind,
                    "section",
                    index + 1,
                    specialReferences,
                    numberingPolicies,
                    state
                );
            }
        }
    }

    private void ProjectPropertySet(
        string packageFingerprint,
        LosslessXmlDocument source,
        string partUri,
        XElement element,
        WordNoteKind kind,
        string scope,
        int? sectionIndex,
        ICollection<MutableSpecialReference> specialReferences,
        ICollection<WordNoteNumberingPolicy> numberingPolicies,
        BuildState state
    )
    {
        if (numberingPolicies.Count == _options.MaxPropertyScopes)
        {
            throw new WordNoteLimitException(
                $"Document contains more than {_options.MaxPropertyScopes} note-property scopes."
            );
        }
        var ordinal = source.GetElementOrdinal(element);
        var position = SinglePropertyValue(element, "pos", state, kind, partUri, ordinal);
        var format = SinglePropertyValue(element, "numFmt", state, kind, partUri, ordinal);
        var rawStart = SinglePropertyValue(element, "numStart", state, kind, partUri, ordinal);
        var restart = SinglePropertyValue(element, "numRestart", state, kind, partUri, ordinal);
        var duplicates = new[] { "pos", "numFmt", "numStart", "numRestart" }
            .Where(name => element.Elements().Count(item => IsWordElement(item, name)) > 1)
            .ToArray();
        var numberStart = ParseInt32(rawStart);
        var valuesValid = duplicates.Length == 0
            && (rawStart is null || numberStart is >= 1)
            && (restart is null
                || (kind == WordNoteKind.Footnote
                    ? restart is "continuous" or "eachSect" or "eachPage"
                    : restart is "continuous" or "eachSect"))
            && (position is null
                || (kind == WordNoteKind.Footnote
                    ? position is "pageBottom" or "beneathText" or "sectEnd" or "docEnd"
                    : position is "sectEnd" or "docEnd"));
        if (!valuesValid)
        {
            state.AddIssue(
                "NOTE_NUMBERING_PROPERTIES_INVALID",
                WordNoteIssueSeverity.Error,
                kind,
                "A note numbering or placement property is duplicated or has an invalid value.",
                null,
                partUri,
                ordinal,
                repairCandidate: false
            );
        }
        var policyFingerprint = Fingerprint(
            "policy",
            packageFingerprint,
            kind.ToString(),
            scope,
            sectionIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            source.SourceSha256,
            ordinal.ToString(CultureInfo.InvariantCulture)
        );
        numberingPolicies.Add(new WordNoteNumberingPolicy(
            StableId("wnp_", policyFingerprint),
            kind,
            scope,
            sectionIndex,
            partUri,
            ordinal,
            position,
            format,
            numberStart,
            rawStart,
            restart,
            valuesValid,
            duplicates
        ));

        var specialName = kind == WordNoteKind.Footnote ? "footnote" : "endnote";
        var specialReferenceElements = element.Elements()
            .Where(item => IsWordElement(item, specialName))
            .ToArray();
        if (scope != "document" && specialReferenceElements.Length > 0)
        {
            state.AddIssue(
                "NOTE_SECTION_SPECIAL_REFERENCE_INVALID",
                WordNoteIssueSeverity.Error,
                kind,
                "A section-wide note property set contains a document-wide special-note reference.",
                null,
                partUri,
                source.GetElementOrdinal(specialReferenceElements[0]),
                repairCandidate: false
            );
            return;
        }
        foreach (var child in specialReferenceElements)
        {
            if (specialReferences.Count == _options.MaxSpecialReferences)
            {
                throw new WordNoteLimitException(
                    $"Document contains more than {_options.MaxSpecialReferences} special-note references."
                );
            }
            var rawId = WordAttribute(child, "id");
            var id = ParseInt32(rawId);
            var childOrdinal = source.GetElementOrdinal(child);
            var fingerprint = Fingerprint(
                "special-reference",
                packageFingerprint,
                kind.ToString(),
                partUri,
                childOrdinal.ToString(CultureInfo.InvariantCulture),
                rawId ?? string.Empty
            );
            specialReferences.Add(new MutableSpecialReference(
                StableId("wns_", fingerprint),
                fingerprint,
                kind,
                id,
                rawId,
                partUri,
                childOrdinal
            ));
        }
    }

    private static void Resolve(
        IReadOnlyList<MutableDefinition> definitions,
        IReadOnlyList<MutableReference> references,
        IReadOnlyList<MutableSpecialReference> specialReferences,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var validDefinitions = definitions
            .Where(item => item.OoxmlId is not null)
            .GroupBy(item => (item.Kind, item.OoxmlId!.Value))
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (definition.OoxmlId is null)
            {
                state.AddIssue(
                    "NOTE_DEFINITION_ID_INVALID",
                    WordNoteIssueSeverity.Error,
                    definition.Kind,
                    "A note definition is missing a valid 32-bit signed id.",
                    definition.Id,
                    definition.PartUri,
                    definition.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
            if (definition.DefinitionType == WordNoteDefinitionType.Unknown)
            {
                state.AddIssue(
                    "NOTE_DEFINITION_TYPE_UNKNOWN",
                    WordNoteIssueSeverity.Error,
                    definition.Kind,
                    "A note definition has an unsupported type value.",
                    definition.Id,
                    definition.PartUri,
                    definition.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
        }

        foreach (var group in validDefinitions.Values.Where(items => items.Length > 1))
        {
            var first = group[0];
            var allCanonicalEqual = group.All(item =>
                string.Equals(item.CanonicalXml, first.CanonicalXml, StringComparison.Ordinal)
            );
            foreach (var duplicate in group.Skip(1))
            {
                duplicate.RedundantDuplicateRemovalCandidate = allCanonicalEqual;
            }
            state.AddIssue(
                "NOTE_DEFINITION_ID_DUPLICATE",
                WordNoteIssueSeverity.Error,
                first.Kind,
                allCanonicalEqual
                    ? "Several byte-independent but canonically equal note definitions use one id. Redundant later definitions are removable candidates."
                    : "Several non-equivalent note definitions use one id and cannot be repaired automatically.",
                first.Id,
                first.PartUri,
                first.SourceElementOrdinal,
                repairCandidate: allCanonicalEqual
            );
        }

        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.OoxmlId is null)
            {
                reference.ResolutionStatus = "invalid_id";
                state.AddIssue(
                    "NOTE_REFERENCE_ID_INVALID",
                    WordNoteIssueSeverity.Error,
                    reference.Kind,
                    "A note reference is missing a valid 32-bit signed id.",
                    reference.Id,
                    reference.PartUri,
                    reference.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
            else if (!validDefinitions.TryGetValue(
                (reference.Kind, reference.OoxmlId.Value),
                out var matches
            ))
            {
                reference.ResolutionStatus = "definition_missing";
                state.AddIssue(
                    "NOTE_REFERENCE_DEFINITION_MISSING",
                    WordNoteIssueSeverity.Error,
                    reference.Kind,
                    "A note reference has no definition with the same id.",
                    reference.Id,
                    reference.PartUri,
                    reference.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
            else if (matches.Length != 1)
            {
                reference.ResolutionStatus = "definition_ambiguous";
                reference.MatchingDefinitionIds.AddRange(matches.Select(item => item.Id));
            }
            else
            {
                reference.ResolutionStatus = matches[0].DefinitionType
                    == WordNoteDefinitionType.Normal
                        ? "resolved"
                        : "special_definition";
                reference.MatchingDefinitionIds.Add(matches[0].Id);
                matches[0].ReferenceCount++;
                if (matches[0].DefinitionType != WordNoteDefinitionType.Normal)
                {
                    state.AddIssue(
                        "NOTE_REFERENCE_TARGET_SPECIAL",
                        WordNoteIssueSeverity.Error,
                        reference.Kind,
                        "An ordinary note reference targets a special note definition.",
                        reference.Id,
                        reference.PartUri,
                        reference.SourceElementOrdinal,
                        repairCandidate: false
                    );
                }
            }
            if (!reference.CustomMarkValueValid)
            {
                state.AddIssue(
                    "NOTE_REFERENCE_CUSTOM_MARK_VALUE_INVALID",
                    WordNoteIssueSeverity.Error,
                    reference.Kind,
                    "The customMarkFollows attribute is not a valid Word on/off value.",
                    reference.Id,
                    reference.PartUri,
                    reference.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
            if (reference.NestedInsideNoteStory)
            {
                state.AddIssue(
                    "NOTE_REFERENCE_NESTED_IN_NOTE",
                    WordNoteIssueSeverity.Error,
                    reference.Kind,
                    "A footnote/endnote reference occurs inside a note story, which is non-conformant.",
                    reference.Id,
                    reference.PartUri,
                    reference.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
        }

        foreach (var duplicateGroup in specialReferences
            .Where(item => item.OoxmlId is not null)
            .GroupBy(item => (item.Kind, item.OoxmlId!.Value))
            .Where(group => group.Count() > 1))
        {
            var duplicate = duplicateGroup.Skip(1).First();
            state.AddIssue(
                "NOTE_SPECIAL_REFERENCE_DUPLICATE",
                WordNoteIssueSeverity.Error,
                duplicate.Kind,
                "Document-wide note properties reference the same special definition more than once.",
                duplicate.Id,
                duplicate.PartUri,
                duplicate.SourceElementOrdinal,
                repairCandidate: false
            );
        }

        foreach (var special in specialReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (special.OoxmlId is null)
            {
                special.ResolutionStatus = "invalid_id";
                state.AddIssue(
                    "NOTE_SPECIAL_REFERENCE_ID_INVALID",
                    WordNoteIssueSeverity.Error,
                    special.Kind,
                    "A document-wide special-note reference has an invalid id.",
                    special.Id,
                    special.PartUri,
                    special.SourceElementOrdinal,
                    repairCandidate: false
                );
                continue;
            }
            if (!validDefinitions.TryGetValue((special.Kind, special.OoxmlId.Value), out var matches))
            {
                special.ResolutionStatus = "definition_missing";
                state.AddIssue(
                    "NOTE_SPECIAL_REFERENCE_DEFINITION_MISSING",
                    WordNoteIssueSeverity.Error,
                    special.Kind,
                    "A document-wide special-note reference has no matching definition. Its intended special type is not inferable from the id alone.",
                    special.Id,
                    special.PartUri,
                    special.SourceElementOrdinal,
                    repairCandidate: false
                );
                continue;
            }
            special.MatchingDefinitionIds.AddRange(matches.Select(item => item.Id));
            if (matches.Length != 1)
            {
                special.ResolutionStatus = "definition_ambiguous";
                continue;
            }
            var match = matches[0];
            match.SpecialReferenceCount++;
            if (match.DefinitionType == WordNoteDefinitionType.Normal)
            {
                special.ResolutionStatus = "normal_definition";
                state.AddIssue(
                    "NOTE_SPECIAL_REFERENCE_TARGET_NORMAL",
                    WordNoteIssueSeverity.Error,
                    special.Kind,
                    "A document-wide special-note reference targets a normal note definition.",
                    special.Id,
                    special.PartUri,
                    special.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
            else if (match.DefinitionType == WordNoteDefinitionType.Unknown)
            {
                special.ResolutionStatus = "unknown_definition_type";
                state.AddIssue(
                    "NOTE_SPECIAL_REFERENCE_TARGET_UNKNOWN_TYPE",
                    WordNoteIssueSeverity.Error,
                    special.Kind,
                    "A document-wide special-note reference targets a definition with an unsupported type.",
                    special.Id,
                    special.PartUri,
                    special.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
            else
            {
                special.ResolutionStatus = "resolved";
            }
        }

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (definition.DefinitionType != WordNoteDefinitionType.Normal)
            {
                if (definition.DefinitionType != WordNoteDefinitionType.Unknown
                    && definition.OoxmlId is not null
                    && definition.SpecialReferenceCount == 0)
                {
                    state.AddIssue(
                        "NOTE_SPECIAL_DEFINITION_UNREFERENCED",
                        WordNoteIssueSeverity.Warning,
                        definition.Kind,
                        "A special note definition is not selected by document-wide note properties.",
                        definition.Id,
                        definition.PartUri,
                        definition.SourceElementOrdinal,
                        repairCandidate: false
                    );
                }
                continue;
            }
            definition.IsOrphan = definition.OoxmlId is not null
                && definition.ReferenceCount == 0
                && definition.SpecialReferenceCount == 0;
            definition.EmptyOrphanRemovalCandidate = definition.IsOrphan
                && definition.TextCharacterCount == 0
                && !definition.HasComplexContent
                && !definition.RedundantDuplicateRemovalCandidate;
            if (definition.IsOrphan)
            {
                state.AddIssue(
                    "NOTE_DEFINITION_ORPHAN",
                    WordNoteIssueSeverity.Warning,
                    definition.Kind,
                    definition.EmptyOrphanRemovalCandidate
                        ? "An empty ordinary note definition has no reference and is a guarded removal candidate."
                        : "An ordinary note definition has no reference; content or unsupported structure prevents automatic removal.",
                    definition.Id,
                    definition.PartUri,
                    definition.SourceElementOrdinal,
                    definition.EmptyOrphanRemovalCandidate
                );
            }
            if (definition.ReferenceCount == 1
                && !definition.HasReferenceMark)
            {
                state.AddIssue(
                    "NOTE_DEFINITION_REFERENCE_MARK_MISSING",
                    WordNoteIssueSeverity.Warning,
                    definition.Kind,
                    "A referenced ordinary note definition has no automatic reference mark. A custom mark may be intentional, so no repair is inferred.",
                    definition.Id,
                    definition.PartUri,
                    definition.SourceElementOrdinal,
                    repairCandidate: false
                );
            }
        }
    }

    private LosslessXmlDocument ParsePart(
        OpcPackageSnapshot package,
        string partUri,
        IDictionary<string, LosslessXmlDocument> cache,
        CancellationToken cancellationToken
    )
    {
        if (cache.TryGetValue(partUri, out var cached))
        {
            return cached;
        }
        if (!package.Parts.TryGetValue(partUri, out var part))
        {
            throw new WordNoteProjectionException(
                $"Projected note source part '{partUri}' is missing."
            );
        }
        try
        {
            var parsed = LosslessXmlDocument.Parse(
                part.Entry.Content,
                _xmlOptions,
                cancellationToken
            );
            cache.Add(partUri, parsed);
            return parsed;
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordNoteLimitException(
                $"Note source part '{partUri}' exceeds XML limits: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordNoteProjectionException(
                $"Note source part '{partUri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private static string? SinglePropertyValue(
        XElement parent,
        string localName,
        BuildState state,
        WordNoteKind kind,
        string partUri,
        int parentOrdinal
    )
    {
        var matches = parent.Elements()
            .Where(item => IsWordElement(item, localName))
            .ToArray();
        if (matches.Length > 1)
        {
            state.AddIssue(
                "NOTE_PROPERTY_DUPLICATE",
                WordNoteIssueSeverity.Error,
                kind,
                $"Note properties contain duplicate '{localName}' elements.",
                partUri,
                null,
                parentOrdinal,
                repairCandidate: false
            );
        }
        return matches.Length == 0 ? null : WordAttribute(matches[0], "val");
    }

    private static string? RelationshipName(string value)
    {
        if (value.StartsWith(TransitionalRelationships, StringComparison.Ordinal))
        {
            return value[TransitionalRelationships.Length..];
        }
        if (value.StartsWith(StrictRelationships, StringComparison.Ordinal))
        {
            return value[StrictRelationships.Length..];
        }
        return null;
    }

    private static WordNoteDefinitionType DefinitionType(string? value) => value switch
    {
        null or "normal" => WordNoteDefinitionType.Normal,
        "separator" => WordNoteDefinitionType.Separator,
        "continuationSeparator" => WordNoteDefinitionType.ContinuationSeparator,
        "continuationNotice" => WordNoteDefinitionType.ContinuationNotice,
        _ => WordNoteDefinitionType.Unknown,
    };

    private static bool IsWordElement(XElement element, string localName) =>
        element.Name.LocalName == localName
        && IsWordNamespace(element.Name.NamespaceName);

    private static bool IsWordNamespace(string value) =>
        value is WordPackageConformance.TransitionalWordNamespace
            or WordPackageConformance.StrictWordNamespace;

    private static string? WordAttribute(XElement element, string localName)
    {
        var matches = element.Attributes()
            .Where(attribute =>
                !attribute.IsNamespaceDeclaration
                && attribute.Name.LocalName == localName
                && IsWordNamespace(attribute.Name.NamespaceName)
            )
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new WordNoteProjectionException(
                $"Element '{element.Name.LocalName}' has ambiguous '{localName}' attributes."
            );
        }
        return matches.SingleOrDefault()?.Value;
    }

    private static int? ParseInt32(string? value) => int.TryParse(
        value,
        NumberStyles.AllowLeadingSign,
        CultureInfo.InvariantCulture,
        out var parsed
    ) ? parsed : null;

    private static (bool Value, bool Valid, string? Raw) ParseOnOff(string? value) =>
        value?.ToLowerInvariant() switch
        {
            null or "false" or "0" or "off" => (false, true, value),
            "true" or "1" or "on" => (true, true, value),
            _ => (false, false, value),
        };

    private static string StableId(string prefix, string fingerprint) =>
        prefix + fingerprint[..24];

    private static string HashText(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))
    ).ToLowerInvariant();

    private static string Fingerprint(string domain, params string[] values)
    {
        var builder = new StringBuilder(domain.Length + values.Sum(item => item.Length) + 64);
        builder.Append(domain.Length).Append(':').Append(domain);
        foreach (var value in values)
        {
            builder.Append(value.Length).Append(':').Append(value);
        }
        return HashText(builder.ToString());
    }

    private sealed class BuildState
    {
        private readonly WordNoteGraphOptions _options;
        private readonly string _packageFingerprint;

        internal BuildState(WordNoteGraphOptions options, string packageFingerprint)
        {
            _options = options;
            _packageFingerprint = packageFingerprint;
        }

        internal List<WordNoteIssue> Issues { get; } = new();

        internal bool IssuesTruncated { get; private set; }

        internal bool DocumentCoverageComplete { get; set; } = true;

        internal void CoverageIssue(
            string code,
            WordNoteIssueSeverity severity,
            WordNoteKind? kind,
            string message,
            string? subjectId,
            string? partUri,
            int? sourceElementOrdinal
        )
        {
            DocumentCoverageComplete = false;
            AddIssue(
                code,
                severity,
                kind,
                message,
                subjectId,
                partUri,
                sourceElementOrdinal,
                repairCandidate: false
            );
        }

        internal void AddIssue(
            string code,
            WordNoteIssueSeverity severity,
            WordNoteKind? kind,
            string message,
            string? subjectId,
            string? partUri,
            int? sourceElementOrdinal,
            bool repairCandidate
        )
        {
            if (Issues.Count == _options.MaxIssues)
            {
                IssuesTruncated = true;
                DocumentCoverageComplete = false;
                return;
            }
            var fingerprint = Fingerprint(
                "issue",
                _packageFingerprint,
                code,
                kind?.ToString() ?? string.Empty,
                subjectId ?? string.Empty,
                partUri ?? string.Empty,
                sourceElementOrdinal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            );
            Issues.Add(new WordNoteIssue(
                StableId("wni_", fingerprint),
                fingerprint,
                code,
                severity,
                kind,
                message,
                subjectId,
                partUri,
                sourceElementOrdinal,
                repairCandidate
            ));
        }
    }

    private sealed class MutableDefinition
    {
        internal MutableDefinition(
            string id,
            string fingerprint,
            WordNoteKind kind,
            WordNoteDefinitionType definitionType,
            int? ooxmlId,
            string? rawOoxmlId,
            string partUri,
            int sourceElementOrdinal,
            SemanticNodeId? semanticNodeId,
            string structuralFingerprint,
            string contentFingerprint,
            int paragraphCount,
            int textCharacterCount,
            bool hasReferenceMark,
            bool hasComplexContent,
            string canonicalXml
        )
        {
            Id = id;
            Fingerprint = fingerprint;
            Kind = kind;
            DefinitionType = definitionType;
            OoxmlId = ooxmlId;
            RawOoxmlId = rawOoxmlId;
            PartUri = partUri;
            SourceElementOrdinal = sourceElementOrdinal;
            SemanticNodeId = semanticNodeId;
            StructuralFingerprint = structuralFingerprint;
            ContentFingerprint = contentFingerprint;
            ParagraphCount = paragraphCount;
            TextCharacterCount = textCharacterCount;
            HasReferenceMark = hasReferenceMark;
            HasComplexContent = hasComplexContent;
            CanonicalXml = canonicalXml;
        }

        internal string Id { get; }
        internal string Fingerprint { get; }
        internal WordNoteKind Kind { get; }
        internal WordNoteDefinitionType DefinitionType { get; }
        internal int? OoxmlId { get; }
        internal string? RawOoxmlId { get; }
        internal string PartUri { get; }
        internal int SourceElementOrdinal { get; }
        internal SemanticNodeId? SemanticNodeId { get; }
        internal string StructuralFingerprint { get; }

        internal string ContentFingerprint { get; }
        internal int ReferenceCount { get; set; }
        internal int SpecialReferenceCount { get; set; }
        internal int ParagraphCount { get; }
        internal int TextCharacterCount { get; }
        internal bool HasReferenceMark { get; }
        internal bool HasComplexContent { get; }
        internal bool IsOrphan { get; set; }
        internal bool EmptyOrphanRemovalCandidate { get; set; }
        internal bool RedundantDuplicateRemovalCandidate { get; set; }
        internal string CanonicalXml { get; }

        internal WordNoteDefinition Freeze() => new(
            Id,
            Fingerprint,
            Kind,
            DefinitionType,
            OoxmlId,
            RawOoxmlId,
            PartUri,
            SourceElementOrdinal,
            SemanticNodeId,
            StructuralFingerprint,
            ContentFingerprint,
            ReferenceCount,
            SpecialReferenceCount,
            ParagraphCount,
            TextCharacterCount,
            HasReferenceMark,
            HasComplexContent,
            IsOrphan,
            EmptyOrphanRemovalCandidate,
            RedundantDuplicateRemovalCandidate
        );
    }

    private sealed class MutableReference
    {
        internal MutableReference(
            string id,
            string fingerprint,
            WordNoteKind kind,
            int? ooxmlId,
            string? rawOoxmlId,
            string partUri,
            int sourceElementOrdinal,
            SemanticNodeId? semanticNodeId,
            bool customMarkFollows,
            bool customMarkValueValid,
            bool nestedInsideNoteStory
        )
        {
            Id = id;
            Fingerprint = fingerprint;
            Kind = kind;
            OoxmlId = ooxmlId;
            RawOoxmlId = rawOoxmlId;
            PartUri = partUri;
            SourceElementOrdinal = sourceElementOrdinal;
            SemanticNodeId = semanticNodeId;
            CustomMarkFollows = customMarkFollows;
            CustomMarkValueValid = customMarkValueValid;
            NestedInsideNoteStory = nestedInsideNoteStory;
        }

        internal string Id { get; }
        internal string Fingerprint { get; }
        internal WordNoteKind Kind { get; }
        internal int? OoxmlId { get; }
        internal string? RawOoxmlId { get; }
        internal string PartUri { get; }
        internal int SourceElementOrdinal { get; }
        internal SemanticNodeId? SemanticNodeId { get; }
        internal bool CustomMarkFollows { get; }
        internal bool CustomMarkValueValid { get; }
        internal bool NestedInsideNoteStory { get; }
        internal string ResolutionStatus { get; set; } = "unresolved";
        internal List<string> MatchingDefinitionIds { get; } = new();

        internal WordNoteReference Freeze() => new(
            Id,
            Fingerprint,
            Kind,
            OoxmlId,
            RawOoxmlId,
            PartUri,
            SourceElementOrdinal,
            SemanticNodeId,
            CustomMarkFollows,
            CustomMarkValueValid,
            NestedInsideNoteStory,
            ResolutionStatus,
            MatchingDefinitionIds.ToArray()
        );
    }

    private sealed class MutableSpecialReference
    {
        internal MutableSpecialReference(
            string id,
            string fingerprint,
            WordNoteKind kind,
            int? ooxmlId,
            string? rawOoxmlId,
            string partUri,
            int sourceElementOrdinal
        )
        {
            Id = id;
            Fingerprint = fingerprint;
            Kind = kind;
            OoxmlId = ooxmlId;
            RawOoxmlId = rawOoxmlId;
            PartUri = partUri;
            SourceElementOrdinal = sourceElementOrdinal;
        }

        internal string Id { get; }
        internal string Fingerprint { get; }
        internal WordNoteKind Kind { get; }
        internal int? OoxmlId { get; }
        internal string? RawOoxmlId { get; }
        internal string PartUri { get; }
        internal int SourceElementOrdinal { get; }
        internal string ResolutionStatus { get; set; } = "unresolved";
        internal List<string> MatchingDefinitionIds { get; } = new();

        internal WordNoteSpecialReference Freeze() => new(
            Id,
            Fingerprint,
            Kind,
            OoxmlId,
            RawOoxmlId,
            PartUri,
            SourceElementOrdinal,
            ResolutionStatus,
            MatchingDefinitionIds.ToArray()
        );
    }
}

public class WordNoteProjectionException : IOException
{
    public WordNoteProjectionException(string message)
        : base(message)
    {
    }

    public WordNoteProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordNoteLimitException : WordNoteProjectionException
{
    public WordNoteLimitException(string message)
        : base(message)
    {
    }
}
