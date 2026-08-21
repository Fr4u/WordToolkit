using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordNumberingRebuildEntryChangeKind
{
    Add,
    Replace,
}

public sealed record WordNumberingRebuildEntryChange(
    string EntryName,
    string? PartUri,
    WordNumberingRebuildEntryChangeKind Kind,
    string? BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed record WordNumberingRebuildTargetResult(
    SemanticNodeId ParagraphNodeId,
    string CandidateFingerprint,
    WordStoryKind StoryKind,
    string SourcePartUri,
    string SourcePath,
    int SourceOrder,
    int LevelIndex,
    int? PreviousNumberId,
    int? PreviousLevelIndex,
    long? CounterValue,
    WordListCounterStatus CounterStatus,
    string? Label,
    WordListLabelStatus LabelStatus,
    bool DirectNumberingMaterialized
);

public sealed record WordNumberingRebuildCommandResult(
    string CommandId,
    int AbstractNumberId,
    int NumberId,
    string NamespaceId,
    string TemplateCode,
    WordNumberingRebuildMultiLevelKind MultiLevelKind,
    bool RestartAfterSectionBreak,
    int LevelCount,
    int TargetCount,
    IReadOnlyList<WordNumberingRebuildTargetResult> Targets
);

public sealed record WordNumberingRebuildValidation(
    bool CandidatePackageStructurallyValid,
    bool SemanticTopologyPreserved,
    bool TextPreserved,
    bool NewDefinitionsExact,
    bool TargetsReassigned,
    bool TargetCountersExact,
    bool TargetLabelsExact,
    bool UnselectedNumberingPreserved,
    bool UnaffectedSequencesPreserved,
    bool NoNewNumberingErrors,
    bool ExactInverseVerified,
    int BeforeNumberingErrorCount,
    int AfterNumberingErrorCount
)
{
    public bool Passed => CandidatePackageStructurallyValid
        && SemanticTopologyPreserved
        && TextPreserved
        && NewDefinitionsExact
        && TargetsReassigned
        && TargetCountersExact
        && TargetLabelsExact
        && UnselectedNumberingPreserved
        && UnaffectedSequencesPreserved
        && NoNewNumberingErrors
        && ExactInverseVerified;
}

public sealed class WordNumberingRebuildPlan
{
    private readonly WordPackageEntryTransactionCore _transaction;

    internal WordNumberingRebuildPlan(
        string planId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        string numberingPartUri,
        bool numberingPartCreated,
        IReadOnlyList<WordNumberingRebuildCommandResult> commands,
        IReadOnlyDictionary<string, WordPackageEntryPayload> entries,
        WordNumberingRebuildValidation validation,
        IReadOnlyList<string> compatibilityRules
    )
    {
        PlanId = planId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        NumberingPartUri = numberingPartUri;
        NumberingPartCreated = numberingPartCreated;
        Commands = new ReadOnlyCollection<WordNumberingRebuildCommandResult>(
            commands.ToArray()
        );
        Validation = validation;
        CompatibilityRules = new ReadOnlyCollection<string>(
            compatibilityRules.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        _transaction = new WordPackageEntryTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            entries
        );
        ChangedEntries = new ReadOnlyCollection<WordNumberingRebuildEntryChange>(
            _transaction.Entries.OrderBy(entry => entry.EntryName, StringComparer.Ordinal)
                .Select(entry => new WordNumberingRebuildEntryChange(
                    entry.EntryName,
                    entry.PartUri,
                    entry.Kind == WordPackageEntryChangeKind.Add
                        ? WordNumberingRebuildEntryChangeKind.Add
                        : WordNumberingRebuildEntryChangeKind.Replace,
                    entry.BeforeSha256,
                    entry.AfterSha256!,
                    entry.BeforeContent?.Length ?? 0,
                    entry.AfterContent!.Length
                ))
                .ToArray()
        );
    }

    public string PlanId { get; }

    public string BasePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public string NumberingPartUri { get; }

    public bool NumberingPartCreated { get; }

    public IReadOnlyList<WordNumberingRebuildCommandResult> Commands { get; }

    public IReadOnlyList<WordNumberingRebuildEntryChange> ChangedEntries { get; }

    public WordNumberingRebuildValidation Validation { get; }

    public IReadOnlyList<string> CompatibilityRules { get; }

    public int TargetCount => Commands.Sum(command => command.TargetCount);

    public bool HasChanges => _transaction.HasChanges;

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot) =>
        _transaction.CreateMutation(currentSnapshot);

    public OpcPackageMutationBuilder CreateInverseMutation(
        OpcPackageSnapshot appliedSnapshot
    ) => _transaction.CreateInverseMutation(appliedSnapshot);
}

public sealed class WordNumberingRebuildPlanner
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
    private const string ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string PackageRelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Word2012Namespace =
        "http://schemas.microsoft.com/office/word/2012/wordml";
    private const string MarkupCompatibilityNamespace =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private readonly WordNumberingRebuildOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;
    private readonly OpcPackageReader _reader = new();
    private readonly OpcPackageSerializer _serializer = new();
    private readonly WordNumberingRebuildCandidateInspector _candidateInspector;

    public WordNumberingRebuildPlanner(WordNumberingRebuildOptions? options = null)
    {
        _options = options ?? WordNumberingRebuildOptions.Default;
        _options.Validate();
        _xmlOptions = new LosslessXmlOptions
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
            MaxTextCharacters = _options.MaxXmlPartBytes,
        };
        _candidateInspector = new WordNumberingRebuildCandidateInspector(_options);
    }

    public WordNumberingRebuildPlan Plan(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<WordNumberingRebuildCommand> commands,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordSemanticPreconditionException(
                "Numbering rebuild requires package and semantic snapshots from the same document version."
            );
        }
        if (!package.IsStructurallyValid)
        {
            throw new WordSemanticEditException(
                "Numbering rebuild requires a structurally valid OPC package."
            );
        }
        WordNumberingRebuildRules.ValidateCommands(commands, _options);

        var targetIds = commands.SelectMany(command => command.Targets)
            .Select(target => target.ParagraphNodeId)
            .ToArray();
        var inspectedCandidates = new List<WordNumberingRebuildCandidate>(
            targetIds.Length
        );
        foreach (var batch in targetIds.Chunk(_options.MaxCandidateInspectionItems))
        {
            cancellationToken.ThrowIfCancellationRequested();
            inspectedCandidates.AddRange(_candidateInspector.Inspect(
                package,
                semanticDocument,
                batch,
                cancellationToken
            ));
        }
        var candidates = inspectedCandidates.ToDictionary(
            candidate => candidate.ParagraphNodeId
        );
        foreach (var target in commands.SelectMany(command => command.Targets))
        {
            var candidate = candidates[target.ParagraphNodeId];
            if (!candidate.CanRebuild)
            {
                throw new WordSemanticEditException(
                    $"Paragraph '{target.ParagraphNodeId}' cannot be rebuilt: {string.Join(',', candidate.BlockedReasons)}."
                );
            }
            if (!string.Equals(
                    candidate.Fingerprint,
                    target.ExpectedCandidateFingerprint,
                    StringComparison.Ordinal
                ))
            {
                throw new WordSemanticPreconditionException(
                    $"Paragraph '{target.ParagraphNodeId}' changed after candidate inspection."
                );
            }
        }

        var styles = new WordStyleGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            semanticDocument,
            styles,
            cancellationToken
        );
        var sequences = new WordListSequenceGraphBuilder().Build(
            package,
            semanticDocument,
            styles,
            numbering,
            cancellationToken
        );
        var w = ResolveWordNamespace(package, semanticDocument, cancellationToken);
        var resolvedCommands = ResolveCommands(
            package,
            semanticDocument,
            commands,
            candidates,
            numbering
        );
        var payloadResult = BuildEntryPayloads(
            package,
            semanticDocument,
            numbering,
            w,
            resolvedCommands,
            cancellationToken
        );
        if (payloadResult.Entries.Count > _options.MaxChangedEntries)
        {
            throw new WordSemanticTransactionLimitException(
                $"Numbering rebuild changes {payloadResult.Entries.Count} entries; the limit is {_options.MaxChangedEntries}."
            );
        }
        var candidatePackage = MaterializeCandidate(
            package,
            payloadResult.Entries,
            cancellationToken
        );
        var transaction = new WordPackageEntryTransactionCore(
            package.Fingerprint,
            candidatePackage.Fingerprint,
            payloadResult.Entries
        );
        var exactInverse = VerifyExactInverse(
            package,
            candidatePackage,
            transaction,
            cancellationToken
        );
        var validationResult = ValidateCandidate(
            package,
            semanticDocument,
            numbering,
            sequences,
            candidatePackage,
            resolvedCommands,
            exactInverse,
            cancellationToken,
            out var publicCommands
        );
        if (!validationResult.Passed)
        {
            throw new WordSemanticEditException(
                "The numbering rebuild candidate failed semantic validation."
            );
        }

        var compatibilityRules = new List<string>
        {
            "new_independent_abstract_definition_and_instance",
            "selected_paragraphs_materialized_with_direct_numbering",
            "unselected_numbering_and_sequences_preserved",
            "deterministic_supported_labels_only",
        };
        if (payloadResult.NumberingPartCreated)
        {
            compatibilityRules.Add(
                "missing_numbering_part_created_with_content_type_and_relationship"
            );
        }
        if (commands.Any(command => command.RestartAfterSectionBreak))
        {
            compatibilityRules.Add("w15_restart_numbering_after_break_declared");
        }
        return new WordNumberingRebuildPlan(
            CreatePlanId(
                package.Fingerprint,
                candidatePackage.Fingerprint,
                resolvedCommands
            ),
            package.Fingerprint,
            candidatePackage.Fingerprint,
            payloadResult.NumberingPartUri,
            payloadResult.NumberingPartCreated,
            publicCommands,
            payloadResult.Entries,
            validationResult,
            compatibilityRules
        );
    }

    private IReadOnlyList<ResolvedCommand> ResolveCommands(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<WordNumberingRebuildCommand> commands,
        IReadOnlyDictionary<SemanticNodeId, WordNumberingRebuildCandidate> candidates,
        WordNumberingGraph numbering
    )
    {
        var nextAbstractId = numbering.AbstractDefinitions.Count == 0
            ? 0
            : checked(numbering.AbstractDefinitions.Max(item => item.AbstractNumberId) + 1);
        var maximumNumberId = numbering.Instances.Count == 0
            ? 0
            : numbering.Instances.Max(item => item.NumberId);
        if (numbering.LastAssignedNumberId is { } cleanup)
        {
            maximumNumberId = Math.Max(maximumNumberId, cleanup);
        }
        if (commands.Count > int.MaxValue - nextAbstractId
            || commands.Count > int.MaxValue - maximumNumberId)
        {
            throw new WordSemanticTransactionLimitException(
                "No 32-bit numbering identifiers remain for this rebuild."
            );
        }

        var result = new List<ResolvedCommand>(commands.Count);
        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            var abstractNumberId = nextAbstractId + index;
            var numberId = maximumNumberId + index + 1;
            var namespaceId = DeterministicHexCode(
                "nsid",
                package.Fingerprint,
                command
            );
            var templateCode = DeterministicHexCode(
                "tmpl",
                package.Fingerprint,
                command
            );
            var targets = command.Targets.Select(target =>
            {
                if (!semanticDocument.TryGetNode(target.ParagraphNodeId, out var node)
                    || node is null)
                {
                    throw new WordSemanticPreconditionException(
                        $"Paragraph '{target.ParagraphNodeId}' disappeared during planning."
                    );
                }
                return new ResolvedTarget(
                    target,
                    candidates[target.ParagraphNodeId],
                    node
                );
            }).OrderBy(target => target.Node.SourceOrder).ToArray();
            result.Add(new ResolvedCommand(
                command,
                abstractNumberId,
                numberId,
                namespaceId,
                templateCode,
                targets
            ));
        }
        return result;
    }

    private PayloadBuildResult BuildEntryPayloads(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordNumberingGraph numbering,
        XNamespace w,
        IReadOnlyList<ResolvedCommand> commands,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package))
        {
            throw new WordSemanticEditException(
                "Numbering reconstruction is blocked because the package contains digital signatures."
            );
        }

        var entries = new Dictionary<string, WordPackageEntryPayload>(StringComparer.Ordinal);
        string numberingPartUri;
        var numberingPartCreated = numbering.NumberingPartUri is null;
        if (numbering.NumberingPartUri is { } existingNumberingPartUri)
        {
            numberingPartUri = existingNumberingPartUri;
            AddExistingNumberingPartPayload(
                package,
                numbering,
                numberingPartUri,
                w,
                commands,
                entries,
                cancellationToken
            );
        }
        else
        {
            numberingPartUri = AllocateNumberingPartUri(package, semanticDocument.MainPartUri);
            AddMissingNumberingInfrastructure(
                package,
                semanticDocument.MainPartUri,
                numberingPartUri,
                w,
                commands,
                entries,
                cancellationToken
            );
        }

        AddParagraphPayloads(
            package,
            commands,
            entries,
            cancellationToken
        );
        return new PayloadBuildResult(
            numberingPartUri,
            numberingPartCreated,
            new ReadOnlyDictionary<string, WordPackageEntryPayload>(entries)
        );
    }

    private void AddExistingNumberingPartPayload(
        OpcPackageSnapshot package,
        WordNumberingGraph numbering,
        string numberingPartUri,
        XNamespace w,
        IReadOnlyList<ResolvedCommand> commands,
        IDictionary<string, WordPackageEntryPayload> entries,
        CancellationToken cancellationToken
    )
    {
        if (!package.Parts.TryGetValue(numberingPartUri, out var part))
        {
            throw new WordSemanticPreconditionException(
                $"Numbering part '{numberingPartUri}' disappeared during planning."
            );
        }
        if (numbering.UnmodeledRootElements.Count != 0)
        {
            throw new WordSemanticEditException(
                "Numbering reconstruction will not append definitions to a numbering root with unmodeled children."
            );
        }
        var source = ParseXml(part.Entry.Content, numberingPartUri, cancellationToken);
        var root = source.GetParsedElement(source.Root.Ordinal);
        if (root.Name != w + "numbering")
        {
            throw new WordSemanticEditException(
                $"Numbering part '{numberingPartUri}' has an unexpected root."
            );
        }

        var abstractFragment = string.Concat(commands.Select(command =>
            CreateAbstractNumberingElement(w, command).ToString(SaveOptions.DisableFormatting)
        ));
        var numberFragment = string.Concat(commands.Select(command =>
            CreateNumberingInstanceElement(w, command).ToString(SaveOptions.DisableFormatting)
        ));
        var patches = new List<XmlSourcePatch>();
        var numbers = root.Elements(w + "num").ToArray();
        var cleanup = root.Elements(w + "numIdMacAtCleanup").ToArray();
        if (cleanup.Length > 1)
        {
            throw new WordSemanticEditException(
                "The numbering root contains duplicate numIdMacAtCleanup elements."
            );
        }

        if (numbers.Length != 0)
        {
            patches.Add(source.CreateElementSiblingInsertionPatch(
                source.GetElementOrdinal(numbers[0]),
                abstractFragment,
                XmlSiblingInsertionPosition.Before
            ));
            patches.Add(source.CreateElementSiblingInsertionPatch(
                source.GetElementOrdinal(numbers[^1]),
                numberFragment,
                XmlSiblingInsertionPosition.After
            ));
        }
        else if (cleanup.Length == 1)
        {
            patches.Add(source.CreateElementSiblingInsertionPatch(
                source.GetElementOrdinal(cleanup[0]),
                abstractFragment + numberFragment,
                XmlSiblingInsertionPosition.Before
            ));
        }
        else
        {
            patches.Add(source.CreateElementContentInsertionPatch(
                source.Root.Ordinal,
                abstractFragment + numberFragment,
                XmlContentInsertionPosition.Append
            ));
        }

        var after = source.ApplyPatches(patches, part.Entry.Sha256, cancellationToken);
        AddPayload(entries, part.Entry, numberingPartUri, after);
    }

    private void AddMissingNumberingInfrastructure(
        OpcPackageSnapshot package,
        string mainPartUri,
        string numberingPartUri,
        XNamespace w,
        IReadOnlyList<ResolvedCommand> commands,
        IDictionary<string, WordPackageEntryPayload> entries,
        CancellationToken cancellationToken
    )
    {
        if (package.RelationshipsFrom(mainPartUri).Any(relationship =>
            relationship.Type is NumberingRelationship or StrictNumberingRelationship
        ))
        {
            throw new WordSemanticEditException(
                "The main document already declares a numbering relationship that did not resolve to a usable numbering part."
            );
        }

        var numberingBytes = CreateStandaloneNumberingPart(w, commands);
        var numberingEntryName = numberingPartUri.TrimStart('/');
        entries.Add(numberingEntryName, new WordPackageEntryPayload(
            numberingEntryName,
            numberingPartUri,
            beforeContent: null,
            afterContent: numberingBytes
        ));
        AddContentTypeOverridePayload(
            package,
            numberingPartUri,
            entries,
            cancellationToken
        );
        AddMainRelationshipPayload(
            package,
            mainPartUri,
            numberingPartUri,
            w,
            entries,
            cancellationToken
        );
    }

    private void AddParagraphPayloads(
        OpcPackageSnapshot package,
        IReadOnlyList<ResolvedCommand> commands,
        IDictionary<string, WordPackageEntryPayload> entries,
        CancellationToken cancellationToken
    )
    {
        foreach (var group in commands.SelectMany(command => command.Targets.Select(target =>
            (Command: command, Target: target)
        )).GroupBy(item => item.Target.Node.SourcePartUri, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(group.Key, out var part))
            {
                throw new WordSemanticPreconditionException(
                    $"Paragraph source part '{group.Key}' disappeared during planning."
                );
            }
            var source = ParseXml(part.Entry.Content, group.Key, cancellationToken);
            var patches = new List<XmlSourcePatch>();
            foreach (var item in group.OrderBy(item => item.Target.Node.SourceOrder))
            {
                patches.Add(CreateParagraphNumberingPatch(
                    source,
                    item.Target.Node,
                    item.Target.Target.LevelIndex,
                    item.Command.NumberId,
                    out var materialized
                ));
                item.Target.DirectNumberingMaterialized = materialized;
            }
            var after = source.ApplyPatches(patches, part.Entry.Sha256, cancellationToken);
            AddPayload(entries, part.Entry, group.Key, after);
        }
    }

    private void AddContentTypeOverridePayload(
        OpcPackageSnapshot package,
        string numberingPartUri,
        IDictionary<string, WordPackageEntryPayload> entries,
        CancellationToken cancellationToken
    )
    {
        var entry = FindEntry(package, "[Content_Types].xml");
        var source = ParseXml(entry.Content, "/[Content_Types].xml", cancellationToken);
        XNamespace ct = ContentTypesNamespace;
        var root = source.GetParsedElement(source.Root.Ordinal);
        if (root.Name != ct + "Types")
        {
            throw new WordSemanticEditException(
                "[Content_Types].xml has an unexpected root."
            );
        }
        var existing = root.Elements(ct + "Override").Where(element =>
            string.Equals((string?)element.Attribute("PartName"), numberingPartUri, StringComparison.Ordinal)
        ).ToArray();
        if (existing.Length != 0)
        {
            throw new WordSemanticEditException(
                $"Content types already contain an override for absent part '{numberingPartUri}'."
            );
        }
        var fragment = new XElement(
            ct + "Override",
            new XAttribute("PartName", numberingPartUri),
            new XAttribute("ContentType", NumberingContentType)
        ).ToString(SaveOptions.DisableFormatting);
        var after = source.ApplyPatches(
            [source.CreateElementContentInsertionPatch(
                source.Root.Ordinal,
                fragment,
                XmlContentInsertionPosition.Append
            )],
            entry.Sha256,
            cancellationToken
        );
        AddPayload(entries, entry, entry.PartUri, after);
    }

    private void AddMainRelationshipPayload(
        OpcPackageSnapshot package,
        string mainPartUri,
        string numberingPartUri,
        XNamespace w,
        IDictionary<string, WordPackageEntryPayload> entries,
        CancellationToken cancellationToken
    )
    {
        var relationshipEntryName = RelationshipEntryName(mainPartUri);
        var relationshipType = w.NamespaceName == WordStrictNamespace
            ? StrictNumberingRelationship
            : NumberingRelationship;
        var relationshipTarget = RelativeRelationshipTarget(mainPartUri, numberingPartUri);
        var existing = package.Entries.SingleOrDefault(item => string.Equals(
            item.Name,
            relationshipEntryName,
            StringComparison.Ordinal
        ));
        XNamespace rel = PackageRelationshipsNamespace;

        if (existing is null)
        {
            var root = new XElement(
                rel + "Relationships",
                new XElement(
                    rel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", relationshipType),
                    new XAttribute("Target", relationshipTarget)
                )
            );
            var bytes = SerializeXmlDocument(root);
            entries.Add(relationshipEntryName, new WordPackageEntryPayload(
                relationshipEntryName,
                "/" + relationshipEntryName,
                beforeContent: null,
                afterContent: bytes
            ));
            return;
        }

        var source = ParseXml(existing.Content, "/" + relationshipEntryName, cancellationToken);
        var parsedRoot = source.GetParsedElement(source.Root.Ordinal);
        if (parsedRoot.Name != rel + "Relationships")
        {
            throw new WordSemanticEditException(
                $"Relationship part '/{relationshipEntryName}' has an unexpected root."
            );
        }
        var relationships = parsedRoot.Elements(rel + "Relationship").ToArray();
        var ids = relationships.Select(element => (string?)element.Attribute("Id"))
            .Where(value => !string.IsNullOrEmpty(value))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (ids.Count != relationships.Count(element => element.Attribute("Id") is not null))
        {
            throw new WordSemanticEditException(
                $"Relationship part '/{relationshipEntryName}' contains duplicate IDs."
            );
        }
        var relationshipId = AllocateRelationshipId(ids);
        var fragment = new XElement(
            rel + "Relationship",
            new XAttribute("Id", relationshipId),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", relationshipTarget)
        ).ToString(SaveOptions.DisableFormatting);
        var after = source.ApplyPatches(
            [source.CreateElementContentInsertionPatch(
                source.Root.Ordinal,
                fragment,
                XmlContentInsertionPosition.Append
            )],
            existing.Sha256,
            cancellationToken
        );
        AddPayload(entries, existing, existing.PartUri, after);
    }

    private byte[] CreateStandaloneNumberingPart(
        XNamespace w,
        IReadOnlyList<ResolvedCommand> commands
    )
    {
        var root = new XElement(
            w + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", w.NamespaceName)
        );
        foreach (var command in commands)
        {
            root.Add(CreateAbstractNumberingElement(w, command, declareWordNamespace: false));
        }
        foreach (var command in commands)
        {
            root.Add(CreateNumberingInstanceElement(w, command, declareWordNamespace: false));
        }
        return SerializeXmlDocument(root);
    }

    private XElement CreateAbstractNumberingElement(
        XNamespace w,
        ResolvedCommand command,
        bool declareWordNamespace = true
    )
    {
        XNamespace w15 = Word2012Namespace;
        var root = new XElement(
            w + "abstractNum",
            new XAttribute(w + "abstractNumId", command.AbstractNumberId)
        );
        if (command.Command.RestartAfterSectionBreak)
        {
            XNamespace mc = MarkupCompatibilityNamespace;
            root.SetAttributeValue(XNamespace.Xmlns + "w15", Word2012Namespace);
            root.SetAttributeValue(XNamespace.Xmlns + "mc", MarkupCompatibilityNamespace);
            root.SetAttributeValue(mc + "Ignorable", "w15");
            root.SetAttributeValue(w15 + "restartNumberingAfterBreak", "1");
        }
        if (declareWordNamespace)
        {
            root.SetAttributeValue(XNamespace.Xmlns + "w", w.NamespaceName);
        }
        root.Add(
            new XElement(w + "nsid", new XAttribute(w + "val", command.NamespaceId)),
            new XElement(
                w + "multiLevelType",
                new XAttribute(
                    w + "val",
                    WordNumberingRebuildRules.MultiLevelToken(command.Command.MultiLevelKind)
                )
            ),
            new XElement(w + "tmpl", new XAttribute(w + "val", command.TemplateCode))
        );
        foreach (var level in command.Command.Levels.OrderBy(level => level.LevelIndex))
        {
            root.Add(CreateLevelElement(w, level));
        }
        return root;
    }

    private XElement CreateLevelElement(XNamespace w, WordNumberingRebuildLevel level)
    {
        var result = new XElement(
            w + "lvl",
            new XAttribute(w + "ilvl", level.LevelIndex),
            new XElement(w + "start", new XAttribute(w + "val", level.StartValue)),
            new XElement(
                w + "numFmt",
                new XAttribute(
                    w + "val",
                    WordNumberingRebuildRules.FormatToken(level.NumberFormat)
                )
            )
        );
        if (WordNumberingRebuildRules.EffectiveRestartValue(level) is { } restart)
        {
            result.Add(new XElement(w + "lvlRestart", new XAttribute(w + "val", restart)));
        }
        if (level.IsLegal)
        {
            result.Add(new XElement(w + "isLgl"));
        }
        result.Add(
            new XElement(
                w + "suff",
                new XAttribute(
                    w + "val",
                    WordNumberingRebuildRules.SuffixToken(level.Suffix)
                )
            ),
            new XElement(w + "lvlText", new XAttribute(w + "val", level.LevelText)),
            new XElement(
                w + "lvlJc",
                new XAttribute(
                    w + "val",
                    WordNumberingRebuildRules.JustificationToken(level.Justification)
                )
            )
        );
        var leftName = w.NamespaceName == WordStrictNamespace ? "start" : "left";
        var left = WordNumberingRebuildRules.EffectiveLeftIndent(level, _options);
        var hanging = WordNumberingRebuildRules.EffectiveHangingIndent(level);
        var tab = WordNumberingRebuildRules.EffectiveTabStop(level, _options);
        result.Add(new XElement(
            w + "pPr",
            new XElement(
                w + "tabs",
                new XElement(
                    w + "tab",
                    new XAttribute(w + "val", "num"),
                    new XAttribute(w + "pos", tab)
                )
            ),
            new XElement(
                w + "ind",
                new XAttribute(w + leftName, left),
                new XAttribute(w + "hanging", hanging)
            )
        ));
        return result;
    }

    private static XElement CreateNumberingInstanceElement(
        XNamespace w,
        ResolvedCommand command,
        bool declareWordNamespace = true
    )
    {
        var result = new XElement(
            w + "num",
            new XAttribute(w + "numId", command.NumberId),
            new XElement(
                w + "abstractNumId",
                new XAttribute(w + "val", command.AbstractNumberId)
            )
        );
        if (declareWordNamespace)
        {
            result.SetAttributeValue(XNamespace.Xmlns + "w", w.NamespaceName);
        }
        return result;
    }

    private static XmlSourcePatch CreateParagraphNumberingPatch(
        LosslessXmlDocument source,
        WordSemanticNode node,
        int levelIndex,
        int numberId,
        out bool directNumberingMaterialized
    )
    {
        var paragraph = source.GetParsedElement(node.SourceElementOrdinal);
        if (!IsWordNamespace(paragraph.Name.NamespaceName)
            || paragraph.Name.LocalName != "p")
        {
            throw new WordSemanticPreconditionException(
                $"Semantic paragraph '{node.Id}' no longer binds to w:p."
            );
        }
        var w = paragraph.Name.Namespace;
        var properties = paragraph.Elements(w + "pPr").ToArray();
        if (properties.Length > 1)
        {
            throw new WordSemanticEditException(
                $"Paragraph '{node.Id}' contains duplicate pPr elements."
            );
        }
        if (properties.Length == 0)
        {
            directNumberingMaterialized = true;
            var fragment = new XElement(
                w + "pPr",
                new XAttribute(XNamespace.Xmlns + "w", w.NamespaceName),
                CreateNumberingProperties(w, levelIndex, numberId)
            ).ToString(SaveOptions.DisableFormatting);
            return source.CreateElementContentInsertionPatch(
                node.SourceElementOrdinal,
                fragment,
                XmlContentInsertionPosition.Prepend
            );
        }

        var original = properties[0];
        if (original.Elements(w + "pPrChange").Any())
        {
            throw new WordSemanticEditException(
                $"Paragraph '{node.Id}' contains tracked paragraph properties."
            );
        }
        var clone = new XElement(original);
        CopyInheritedNamespaceDeclarations(original, clone);
        var numberingProperties = clone.Elements(w + "numPr").ToArray();
        if (numberingProperties.Length > 1)
        {
            throw new WordSemanticEditException(
                $"Paragraph '{node.Id}' contains duplicate numPr elements."
            );
        }
        directNumberingMaterialized = numberingProperties.Length == 0
            || !numberingProperties[0].Elements(w + "numId").Any();
        if (numberingProperties.Length == 0)
        {
            InsertParagraphNumberingProperties(
                clone,
                CreateNumberingProperties(w, levelIndex, numberId),
                w
            );
        }
        else
        {
            SetNumberingProperties(numberingProperties[0], w, levelIndex, numberId);
        }
        return source.CreateElementReplacementPatch(
            source.GetElementOrdinal(original),
            clone.ToString(SaveOptions.DisableFormatting)
        );
    }

    private static XElement CreateNumberingProperties(
        XNamespace w,
        int levelIndex,
        int numberId
    ) => new(
        w + "numPr",
        new XElement(w + "ilvl", new XAttribute(w + "val", levelIndex)),
        new XElement(w + "numId", new XAttribute(w + "val", numberId))
    );

    private static void SetNumberingProperties(
        XElement numPr,
        XNamespace w,
        int levelIndex,
        int numberId
    )
    {
        var unknown = numPr.Elements().Where(child =>
            child.Name != w + "ilvl" && child.Name != w + "numId"
        ).ToArray();
        var levels = numPr.Elements(w + "ilvl").ToArray();
        var numbers = numPr.Elements(w + "numId").ToArray();
        if (unknown.Length != 0 || levels.Length > 1 || numbers.Length > 1)
        {
            throw new WordSemanticEditException(
                "Paragraph numPr contains tracked, unmodeled, or duplicate children."
            );
        }
        if (levels.Length == 0)
        {
            numPr.AddFirst(new XElement(w + "ilvl", new XAttribute(w + "val", levelIndex)));
        }
        else
        {
            levels[0].SetAttributeValue(w + "val", levelIndex);
        }
        if (numbers.Length == 0)
        {
            numPr.Elements(w + "ilvl").First().AddAfterSelf(
                new XElement(w + "numId", new XAttribute(w + "val", numberId))
            );
        }
        else
        {
            numbers[0].SetAttributeValue(w + "val", numberId);
        }
    }

    private static void InsertParagraphNumberingProperties(
        XElement paragraphProperties,
        XElement numberingProperties,
        XNamespace w
    )
    {
        var predecessors = new HashSet<XName>
        {
            w + "pStyle",
            w + "keepNext",
            w + "keepLines",
            w + "pageBreakBefore",
            w + "framePr",
            w + "widowControl",
        };
        var predecessor = paragraphProperties.Elements()
            .TakeWhile(element => predecessors.Contains(element.Name))
            .LastOrDefault();
        if (predecessor is null)
        {
            paragraphProperties.AddFirst(numberingProperties);
        }
        else
        {
            predecessor.AddAfterSelf(numberingProperties);
        }
    }

    private static void CopyInheritedNamespaceDeclarations(
        XElement source,
        XElement clone
    )
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ancestor in source.AncestorsAndSelf().Reverse())
        {
            foreach (var attribute in ancestor.Attributes().Where(attribute =>
                attribute.IsNamespaceDeclaration
            ))
            {
                var prefix = attribute.Name.LocalName == "xmlns"
                    ? string.Empty
                    : attribute.Name.LocalName;
                namespaces[prefix] = attribute.Value;
            }
        }
        var declared = clone.Attributes()
            .Where(attribute => attribute.IsNamespaceDeclaration)
            .Select(attribute => attribute.Name.LocalName == "xmlns"
                ? string.Empty
                : attribute.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (prefix, uri) in namespaces.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (declared.Contains(prefix) || prefix == "xml")
            {
                continue;
            }
            clone.Add(
                prefix.Length == 0
                    ? new XAttribute("xmlns", uri)
                    : new XAttribute(XNamespace.Xmlns + prefix, uri)
            );
        }
    }

    private static void AddPayload(
        IDictionary<string, WordPackageEntryPayload> entries,
        OpcPackageEntry entry,
        string? partUri,
        byte[] after
    )
    {
        if (entry.Content.Span.SequenceEqual(after))
        {
            return;
        }
        if (!entries.TryAdd(entry.Name, new WordPackageEntryPayload(
            entry.Name,
            partUri,
            entry.Content.ToArray(),
            after
        )))
        {
            throw new WordSemanticEditException(
                $"Numbering reconstruction produced overlapping entry edits for '{entry.Name}'."
            );
        }
    }

    private LosslessXmlDocument ParseXml(
        ReadOnlyMemory<byte> content,
        string sourceName,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return LosslessXmlDocument.Parse(content, _xmlOptions, cancellationToken);
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                $"XML source '{sourceName}' cannot be edited losslessly.",
                exception
            );
        }
    }

    private static OpcPackageEntry FindEntry(OpcPackageSnapshot package, string entryName) =>
        package.Entries.SingleOrDefault(entry => string.Equals(
            entry.Name,
            entryName,
            StringComparison.Ordinal
        )) ?? throw new WordSemanticPreconditionException(
            $"Package entry '{entryName}' is missing."
        );

    private static string AllocateNumberingPartUri(
        OpcPackageSnapshot package,
        string mainPartUri
    )
    {
        var slash = mainPartUri.LastIndexOf('/');
        var directory = slash <= 0 ? string.Empty : mainPartUri[..slash];
        for (var suffix = 1; suffix <= 10_000; suffix++)
        {
            var fileName = suffix == 1 ? "numbering.xml" : $"numbering{suffix}.xml";
            var candidate = directory + "/" + fileName;
            if (!package.Parts.ContainsKey(candidate)
                && !package.ContentTypes.Overrides.ContainsKey(candidate)
                && !package.Entries.Any(entry => string.Equals(
                    entry.Name,
                    candidate.TrimStart('/'),
                    StringComparison.Ordinal
                )))
            {
                return candidate;
            }
        }
        throw new WordSemanticTransactionLimitException(
            "No safe numbering part URI could be allocated."
        );
    }

    private static string RelationshipEntryName(string sourcePartUri)
    {
        var entryName = sourcePartUri.TrimStart('/');
        var slash = entryName.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : entryName[..slash];
        var file = slash < 0 ? entryName : entryName[(slash + 1)..];
        return (directory.Length == 0 ? string.Empty : directory + "/")
            + "_rels/" + file + ".rels";
    }

    private static string RelativeRelationshipTarget(
        string sourcePartUri,
        string targetPartUri
    )
    {
        var source = new Uri("https://wordtoolkit.invalid" + sourcePartUri, UriKind.Absolute);
        var target = new Uri("https://wordtoolkit.invalid" + targetPartUri, UriKind.Absolute);
        return Uri.UnescapeDataString(source.MakeRelativeUri(target).ToString());
    }

    private static string AllocateRelationshipId(IReadOnlySet<string> ids)
    {
        for (var value = 1; value <= 1_000_000; value++)
        {
            var candidate = "rId" + value.ToString(CultureInfo.InvariantCulture);
            if (!ids.Contains(candidate))
            {
                return candidate;
            }
        }
        throw new WordSemanticTransactionLimitException(
            "No relationship ID could be allocated for the numbering part."
        );
    }

    private static byte[] SerializeXmlDocument(XElement root)
    {
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false,
        };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument(standalone: true);
            root.WriteTo(writer);
            writer.WriteEndDocument();
        }
        return stream.ToArray();
    }

    private XNamespace ResolveWordNamespace(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken
    )
    {
        if (!package.Parts.TryGetValue(semanticDocument.MainPartUri, out var mainPart))
        {
            throw new WordSemanticPreconditionException(
                $"Main document part '{semanticDocument.MainPartUri}' is missing."
            );
        }
        var source = ParseXml(
            mainPart.Entry.Content,
            semanticDocument.MainPartUri,
            cancellationToken
        );
        var namespaceName = source.GetParsedElement(source.Root.Ordinal).Name.NamespaceName;
        return namespaceName switch
        {
            WordTransitionalNamespace => WordTransitionalNamespace,
            WordStrictNamespace => WordStrictNamespace,
            _ => throw new WordSemanticEditException(
                "The main document does not use a supported WordprocessingML namespace."
            ),
        };
    }

    private OpcPackageSnapshot MaterializeCandidate(
        OpcPackageSnapshot package,
        IReadOnlyDictionary<string, WordPackageEntryPayload> payloads,
        CancellationToken cancellationToken
    )
    {
        var mutation = new OpcPackageMutationBuilder(package);
        foreach (var payload in payloads.Values.OrderBy(
            payload => payload.EntryName,
            StringComparer.Ordinal
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (payload.BeforeContent is null)
            {
                mutation.AddEntry(payload.EntryName, payload.AfterContent!);
            }
            else
            {
                mutation.ReplaceEntry(
                    payload.EntryName,
                    payload.AfterContent!,
                    payload.BeforeSha256
                );
            }
        }
        using var stream = new MemoryStream();
        _serializer.Write(stream, mutation);
        stream.Position = 0;
        return _reader.Read(stream, cancellationToken);
    }

    private bool VerifyExactInverse(
        OpcPackageSnapshot baseline,
        OpcPackageSnapshot candidate,
        WordPackageEntryTransactionCore transaction,
        CancellationToken cancellationToken
    )
    {
        var inverse = transaction.CreateInverseMutation(candidate);
        using var stream = new MemoryStream();
        _serializer.Write(stream, inverse);
        stream.Position = 0;
        var restored = _reader.Read(stream, cancellationToken);
        return string.Equals(restored.Fingerprint, baseline.Fingerprint, StringComparison.Ordinal)
            && EntriesEqual(baseline, restored);
    }

    private static bool EntriesEqual(OpcPackageSnapshot left, OpcPackageSnapshot right)
    {
        if (left.Entries.Count != right.Entries.Count)
        {
            return false;
        }
        for (var index = 0; index < left.Entries.Count; index++)
        {
            var a = left.Entries[index];
            var b = right.Entries[index];
            if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                || a.LastWriteTime != b.LastWriteTime
                || a.ExternalAttributes != b.ExternalAttributes
                || !a.Content.Span.SequenceEqual(b.Content.Span))
            {
                return false;
            }
        }
        return true;
    }

    private static WordNumberingRebuildValidation ValidateCandidate(
        OpcPackageSnapshot baselinePackage,
        WordSemanticDocument baselineSemantic,
        WordNumberingGraph baselineNumbering,
        WordListSequenceGraph baselineSequences,
        OpcPackageSnapshot candidatePackage,
        IReadOnlyList<ResolvedCommand> commands,
        bool exactInverse,
        CancellationToken cancellationToken,
        out IReadOnlyList<WordNumberingRebuildCommandResult> commandResults
    )
    {
        if (!candidatePackage.IsStructurallyValid)
        {
            throw new WordSemanticEditException(
                "The numbering reconstruction candidate has structural OPC errors."
            );
        }
        var candidateSemantic = new WordSemanticProjector().Project(
            candidatePackage,
            cancellationToken
        );
        var candidateStyles = new WordStyleGraphBuilder().Build(
            candidatePackage,
            candidateSemantic,
            cancellationToken
        );
        var candidateNumbering = new WordNumberingGraphBuilder().Build(
            candidatePackage,
            candidateSemantic,
            candidateStyles,
            cancellationToken
        );
        var candidateSequences = new WordListSequenceGraphBuilder().Build(
            candidatePackage,
            candidateSemantic,
            candidateStyles,
            candidateNumbering,
            cancellationToken
        );

        var candidateParagraphs = candidateSemantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .ToDictionary(
                node => SourceKey(node.SourcePartUri, node.SourcePath),
                StringComparer.Ordinal
            );
        var candidateItems = candidateSequences.Items.ToDictionary(
            item => item.ParagraphNodeId
        );
        var targetKeys = commands.SelectMany(command => command.Targets)
            .Select(target => SourceKey(target.Node.SourcePartUri, target.Node.SourcePath))
            .ToHashSet(StringComparer.Ordinal);

        var targetsReassigned = true;
        var targetCountersExact = true;
        var targetLabelsExact = true;
        var projectedCommands = new List<WordNumberingRebuildCommandResult>(commands.Count);
        foreach (var command in commands)
        {
            var projectedTargets = new List<WordNumberingRebuildTargetResult>(
                command.Targets.Count
            );
            foreach (var target in command.Targets)
            {
                var key = SourceKey(target.Node.SourcePartUri, target.Node.SourcePath);
                WordListSequenceItem? item = null;
                if (!candidateParagraphs.TryGetValue(key, out var paragraph)
                    || !candidateItems.TryGetValue(paragraph.Id, out item)
                    || item.NumberId != command.NumberId
                    || item.LevelIndex != target.Target.LevelIndex)
                {
                    targetsReassigned = false;
                }
                if (item is null || item.CounterStatus != WordListCounterStatus.Exact)
                {
                    targetCountersExact = false;
                }
                if (item is null || item.LabelStatus is not (
                    WordListLabelStatus.Exact or WordListLabelStatus.Hidden
                ))
                {
                    targetLabelsExact = false;
                }
                projectedTargets.Add(new WordNumberingRebuildTargetResult(
                    target.Node.Id,
                    target.Candidate.Fingerprint,
                    target.Candidate.StoryKind,
                    target.Node.SourcePartUri,
                    target.Node.SourcePath,
                    target.Node.SourceOrder,
                    target.Target.LevelIndex,
                    target.Candidate.CurrentNumberId,
                    target.Candidate.CurrentLevelIndex,
                    item?.CounterValue,
                    item?.CounterStatus ?? WordListCounterStatus.UnresolvedStart,
                    item?.Label,
                    item?.LabelStatus ?? WordListLabelStatus.ReferencedCounterUnresolved,
                    target.DirectNumberingMaterialized
                ));
            }
            projectedCommands.Add(new WordNumberingRebuildCommandResult(
                command.Command.CommandId,
                command.AbstractNumberId,
                command.NumberId,
                command.NamespaceId,
                command.TemplateCode,
                command.Command.MultiLevelKind,
                command.Command.RestartAfterSectionBreak,
                command.Command.Levels.Count,
                command.Targets.Count,
                new ReadOnlyCollection<WordNumberingRebuildTargetResult>(
                    projectedTargets.ToArray()
                )
            ));
        }
        commandResults = new ReadOnlyCollection<WordNumberingRebuildCommandResult>(
            projectedCommands.ToArray()
        );

        var newDefinitionsExact = NewDefinitionsExact(
            baselineNumbering,
            candidateNumbering,
            commands
        );
        var unselectedNumberingPreserved = BaselineNumberingPreserved(
            baselineNumbering,
            candidateNumbering
        );
        var unaffectedSequencesPreserved = UnaffectedSequencesPreserved(
            baselineSemantic,
            baselineSequences,
            candidateParagraphs,
            candidateItems,
            targetKeys
        );
        var topologyPreserved = TopologyProjection(baselineSemantic)
            .SequenceEqual(TopologyProjection(candidateSemantic), StringComparer.Ordinal);
        var textPreserved = TextProjection(baselineSemantic)
            .SequenceEqual(TextProjection(candidateSemantic), StringComparer.Ordinal);
        var beforeErrors = CountNumberingErrors(baselineNumbering, baselineSequences);
        var afterErrors = CountNumberingErrors(candidateNumbering, candidateSequences);
        return new WordNumberingRebuildValidation(
            CandidatePackageStructurallyValid: candidatePackage.IsStructurallyValid,
            SemanticTopologyPreserved: topologyPreserved,
            TextPreserved: textPreserved,
            NewDefinitionsExact: newDefinitionsExact,
            TargetsReassigned: targetsReassigned,
            TargetCountersExact: targetCountersExact,
            TargetLabelsExact: targetLabelsExact,
            UnselectedNumberingPreserved: unselectedNumberingPreserved,
            UnaffectedSequencesPreserved: unaffectedSequencesPreserved,
            NoNewNumberingErrors: afterErrors <= beforeErrors,
            ExactInverseVerified: exactInverse,
            BeforeNumberingErrorCount: beforeErrors,
            AfterNumberingErrorCount: afterErrors
        );
    }

    private static bool NewDefinitionsExact(
        WordNumberingGraph baseline,
        WordNumberingGraph candidate,
        IReadOnlyList<ResolvedCommand> commands
    )
    {
        if (candidate.AbstractDefinitions.Count
                != baseline.AbstractDefinitions.Count + commands.Count
            || candidate.Instances.Count != baseline.Instances.Count + commands.Count)
        {
            return false;
        }
        foreach (var command in commands)
        {
            if (!candidate.TryGetAbstractDefinition(command.AbstractNumberId, out var definition)
                || definition is null
                || !candidate.TryGetInstance(command.NumberId, out var instance)
                || instance is null
                || instance.AbstractNumberId != command.AbstractNumberId
                || instance.LevelOverrides.Count != 0
                || !string.Equals(definition.NamespaceId, command.NamespaceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(definition.TemplateCode, command.TemplateCode, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    definition.MultiLevelType,
                    WordNumberingRebuildRules.MultiLevelToken(command.Command.MultiLevelKind),
                    StringComparison.Ordinal
                )
                || (definition.RestartNumberingAfterBreak ?? false)
                    != command.Command.RestartAfterSectionBreak
                || definition.Levels.Count != command.Command.Levels.Count)
            {
                return false;
            }
            foreach (var requested in command.Command.Levels)
            {
                if (!definition.TryGetLevel(requested.LevelIndex, out var actual)
                    || actual is null
                    || actual.Start != requested.StartValue
                    || !string.Equals(
                        actual.NumberFormat,
                        WordNumberingRebuildRules.FormatToken(requested.NumberFormat),
                        StringComparison.Ordinal
                    )
                    || actual.RestartAfterLevel
                        != WordNumberingRebuildRules.EffectiveRestartValue(requested)
                    || (actual.IsLegal ?? false) != requested.IsLegal
                    || !string.Equals(
                        actual.Suffix,
                        WordNumberingRebuildRules.SuffixToken(requested.Suffix),
                        StringComparison.Ordinal
                    )
                    || !string.Equals(actual.LevelText, requested.LevelText, StringComparison.Ordinal)
                    || !string.Equals(
                        actual.Justification,
                        WordNumberingRebuildRules.JustificationToken(requested.Justification),
                        StringComparison.Ordinal
                    )
                    || actual.PictureBulletId is not null
                    || actual.CustomNumberFormat is not null)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool BaselineNumberingPreserved(
        WordNumberingGraph baseline,
        WordNumberingGraph candidate
    )
    {
        if (baseline.LastAssignedNumberId != candidate.LastAssignedNumberId
            || baseline.PictureBullets.Count != candidate.PictureBullets.Count)
        {
            return false;
        }
        foreach (var before in baseline.AbstractDefinitions)
        {
            if (!candidate.TryGetAbstractDefinition(before.AbstractNumberId, out var after)
                || after is null
                || !string.Equals(
                    AbstractDefinitionSignature(before),
                    AbstractDefinitionSignature(after),
                    StringComparison.Ordinal
                ))
            {
                return false;
            }
        }
        foreach (var before in baseline.Instances)
        {
            if (!candidate.TryGetInstance(before.NumberId, out var after)
                || after is null
                || !string.Equals(
                    InstanceSignature(before),
                    InstanceSignature(after),
                    StringComparison.Ordinal
                ))
            {
                return false;
            }
        }
        foreach (var before in baseline.PictureBullets)
        {
            if (!candidate.TryGetPictureBullet(before.PictureBulletId, out var after)
                || after is null
                || !before.RelationshipIds.SequenceEqual(
                    after.RelationshipIds,
                    StringComparer.Ordinal
                ))
            {
                return false;
            }
        }
        return true;
    }

    private static bool UnaffectedSequencesPreserved(
        WordSemanticDocument baselineSemantic,
        WordListSequenceGraph baselineSequences,
        IReadOnlyDictionary<string, WordSemanticNode> candidateParagraphs,
        IReadOnlyDictionary<SemanticNodeId, WordListSequenceItem> candidateItems,
        IReadOnlySet<string> targetKeys
    )
    {
        foreach (var before in baselineSequences.Items)
        {
            if (!baselineSemantic.TryGetNode(before.ParagraphNodeId, out var beforeNode)
                || beforeNode is null)
            {
                return false;
            }
            var key = SourceKey(beforeNode.SourcePartUri, beforeNode.SourcePath);
            if (targetKeys.Contains(key))
            {
                continue;
            }
            if (!candidateParagraphs.TryGetValue(key, out var afterNode)
                || !candidateItems.TryGetValue(afterNode.Id, out var after)
                || before.NumberId != after.NumberId
                || before.LevelIndex != after.LevelIndex
                || before.CounterValue != after.CounterValue
                || before.CounterStatus != after.CounterStatus
                || before.LabelStatus != after.LabelStatus
                || !string.Equals(before.Label, after.Label, StringComparison.Ordinal)
                || !string.Equals(before.Suffix, after.Suffix, StringComparison.Ordinal)
                || before.LegalNumbering != after.LegalNumbering)
            {
                return false;
            }
        }
        return true;
    }

    private static string AbstractDefinitionSignature(
        WordAbstractNumberingDefinition definition
    )
    {
        var builder = new StringBuilder();
        AppendSignature(builder, definition.AbstractNumberId);
        AppendSignature(builder, definition.NamespaceId);
        AppendSignature(builder, definition.MultiLevelType);
        AppendSignature(builder, definition.Name);
        AppendSignature(builder, definition.TemplateCode);
        AppendSignature(builder, definition.NumberingStyleLinkId);
        AppendSignature(builder, definition.StyleLinkId);
        AppendSignature(builder, definition.RestartNumberingAfterBreak);
        foreach (var level in definition.Levels)
        {
            AppendSignature(builder, level.LevelIndex);
            AppendSignature(builder, level.Start);
            AppendSignature(builder, level.NumberFormat);
            AppendSignature(builder, level.CustomNumberFormat);
            AppendSignature(builder, level.RestartAfterLevel);
            AppendSignature(builder, level.ParagraphStyleId);
            AppendSignature(builder, level.IsLegal);
            AppendSignature(builder, level.Suffix);
            AppendSignature(builder, level.LevelText);
            AppendSignature(builder, level.LevelTextIsNull);
            AppendSignature(builder, level.PictureBulletId);
            AppendSignature(builder, level.Justification);
            AppendSignature(builder, level.TemplateCode);
            AppendSignature(builder, level.Tentative);
            foreach (var pair in level.ParagraphProperties.Values.OrderBy(pair => pair.Key))
            {
                AppendSignature(builder, pair.Key);
                AppendSignature(builder, pair.Value);
            }
            foreach (var pair in level.RunProperties.Values.OrderBy(pair => pair.Key))
            {
                AppendSignature(builder, pair.Key);
                AppendSignature(builder, pair.Value);
            }
            foreach (var value in level.UnmodeledElements)
            {
                AppendSignature(builder, value);
            }
        }
        foreach (var value in definition.UnmodeledElements)
        {
            AppendSignature(builder, value);
        }
        return builder.ToString();
    }

    private static string InstanceSignature(WordNumberingInstance instance)
    {
        var builder = new StringBuilder();
        AppendSignature(builder, instance.NumberId);
        AppendSignature(builder, instance.AbstractNumberId);
        foreach (var levelOverride in instance.LevelOverrides)
        {
            AppendSignature(builder, levelOverride.LevelIndex);
            AppendSignature(builder, levelOverride.StartOverride);
            AppendSignature(builder, levelOverride.Level is null
                ? null
                : LevelSignature(levelOverride.Level));
            foreach (var value in levelOverride.UnmodeledElements)
            {
                AppendSignature(builder, value);
            }
        }
        foreach (var value in instance.UnmodeledElements)
        {
            AppendSignature(builder, value);
        }
        return builder.ToString();
    }

    private static string LevelSignature(WordNumberingLevelDefinition level)
    {
        var builder = new StringBuilder();
        AppendSignature(builder, level.LevelIndex);
        AppendSignature(builder, level.Start);
        AppendSignature(builder, level.NumberFormat);
        AppendSignature(builder, level.CustomNumberFormat);
        AppendSignature(builder, level.RestartAfterLevel);
        AppendSignature(builder, level.ParagraphStyleId);
        AppendSignature(builder, level.IsLegal);
        AppendSignature(builder, level.Suffix);
        AppendSignature(builder, level.LevelText);
        AppendSignature(builder, level.LevelTextIsNull);
        AppendSignature(builder, level.PictureBulletId);
        AppendSignature(builder, level.Justification);
        return builder.ToString();
    }

    private static void AppendSignature(StringBuilder builder, object? value)
    {
        var text = value switch
        {
            null => "<null>",
            bool boolean => boolean ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        builder.Append(text.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(text)
            .Append(';');
    }

    private static IEnumerable<string> TopologyProjection(WordSemanticDocument document)
    {
        var nodes = document.Nodes.ToDictionary(node => node.Id);
        return document.Nodes.Select(node => string.Join(
            '\u001f',
            node.SourcePartUri,
            node.SourcePath,
            node.Kind.ToString(),
            node.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent)
                ? SourceKey(parent.SourcePartUri, parent.SourcePath)
                : string.Empty
        ));
    }

    private static IEnumerable<string> TextProjection(WordSemanticDocument document) =>
        document.Nodes
            .Where(node => node.Text is not null)
            .Select(node => string.Join(
                '\u001f',
                node.SourcePartUri,
                node.SourcePath,
                node.Kind.ToString(),
                node.Text
            ));

    private static int CountNumberingErrors(
        WordNumberingGraph numbering,
        WordListSequenceGraph sequences
    ) => numbering.Issues.Count(issue =>
        issue.Severity == WordNumberingIssueSeverity.Error
    ) + sequences.Issues.Count(issue =>
        issue.Severity == WordListSequenceIssueSeverity.Error
    );

    private static string SourceKey(string partUri, string sourcePath) =>
        partUri + "\u001f" + sourcePath;

    private static string DeterministicHexCode(
        string purpose,
        string packageFingerprint,
        WordNumberingRebuildCommand command
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-numbering-rebuild-code-v1");
        AppendHash(hash, purpose);
        AppendHash(hash, packageFingerprint);
        AppendCommand(hash, command);
        return Convert.ToHexString(hash.GetHashAndReset().AsSpan(0, 4));
    }

    private static string CreatePlanId(
        string baseFingerprint,
        string resultFingerprint,
        IReadOnlyList<ResolvedCommand> commands
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-numbering-rebuild-plan-v1");
        AppendHash(hash, baseFingerprint);
        AppendHash(hash, resultFingerprint);
        foreach (var command in commands)
        {
            AppendHash(hash, command.AbstractNumberId.ToString(CultureInfo.InvariantCulture));
            AppendHash(hash, command.NumberId.ToString(CultureInfo.InvariantCulture));
            AppendHash(hash, command.NamespaceId);
            AppendHash(hash, command.TemplateCode);
            AppendCommand(hash, command.Command);
        }
        return "wnrbplan_" + Convert.ToBase64String(
            hash.GetHashAndReset().AsSpan(0, 18)
        ).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void AppendCommand(
        IncrementalHash hash,
        WordNumberingRebuildCommand command
    )
    {
        AppendHash(hash, command.CommandId);
        AppendHash(hash, command.MultiLevelKind.ToString());
        AppendHash(hash, command.RestartAfterSectionBreak ? "1" : "0");
        foreach (var level in command.Levels.OrderBy(level => level.LevelIndex))
        {
            AppendHash(hash, level.LevelIndex.ToString(CultureInfo.InvariantCulture));
            AppendHash(hash, level.StartValue.ToString(CultureInfo.InvariantCulture));
            AppendHash(hash, level.NumberFormat.ToString());
            AppendHash(hash, level.LevelText);
            AppendHash(hash, level.RestartMode.ToString());
            AppendHash(hash, level.RestartTriggerLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendHash(hash, level.IsLegal ? "1" : "0");
            AppendHash(hash, level.Suffix.ToString());
            AppendHash(hash, level.Justification.ToString());
            AppendHash(hash, level.LeftIndentTwips?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendHash(hash, level.HangingIndentTwips?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendHash(hash, level.TabStopTwips?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        }
        foreach (var target in command.Targets)
        {
            AppendHash(hash, target.ParagraphNodeId.Value);
            AppendHash(hash, target.ExpectedCandidateFingerprint);
            AppendHash(hash, target.LevelIndex.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendHash(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static bool IsWordNamespace(string namespaceUri) =>
        namespaceUri is WordTransitionalNamespace or WordStrictNamespace;

    private sealed record ResolvedCommand(
        WordNumberingRebuildCommand Command,
        int AbstractNumberId,
        int NumberId,
        string NamespaceId,
        string TemplateCode,
        IReadOnlyList<ResolvedTarget> Targets
    );

    private sealed class ResolvedTarget
    {
        internal ResolvedTarget(
            WordNumberingRebuildTarget target,
            WordNumberingRebuildCandidate candidate,
            WordSemanticNode node
        )
        {
            Target = target;
            Candidate = candidate;
            Node = node;
        }

        internal WordNumberingRebuildTarget Target { get; }

        internal WordNumberingRebuildCandidate Candidate { get; }

        internal WordSemanticNode Node { get; }

        internal bool DirectNumberingMaterialized { get; set; }
    }

    private sealed record PayloadBuildResult(
        string NumberingPartUri,
        bool NumberingPartCreated,
        IReadOnlyDictionary<string, WordPackageEntryPayload> Entries
    );
}
