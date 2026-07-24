using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed record WordNumberingSequenceRestartCommand(
    SemanticNodeId TargetParagraphNodeId,
    int ExpectedNumberId,
    int ExpectedLevelIndex,
    int StartValue
);

public sealed record WordNumberingSequenceRepairOptions
{
    public static WordNumberingSequenceRepairOptions Default { get; } = new();

    public int MaxAffectedParagraphs { get; init; } = 10_000;

    public int MaxChangedParts { get; init; } = 16;

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxAffectedParagraphs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAffectedParagraphs));
        }
        if (MaxChangedParts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChangedParts));
        }
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
    }
}

public sealed record WordNumberingSequenceRepairParagraph(
    SemanticNodeId ParagraphNodeId,
    string StoryId,
    WordStoryKind StoryKind,
    string SourcePartUri,
    string SourcePath,
    int SourceOrder,
    int LevelIndex,
    long? BeforeCounterValue,
    WordListCounterStatus BeforeCounterStatus,
    bool DirectNumberingMaterialized
);

public sealed record WordNumberingSequenceRepairPartChange(
    string PartUri,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed record WordNumberingSequenceRepairValidation(
    bool CandidatePackageStructurallyValid,
    bool TextPreserved,
    bool UnaffectedSequencesPreserved,
    bool AffectedParagraphsReassigned,
    bool TargetCounterRestarted,
    bool NoNewNumberingErrors,
    int BeforeNumberingErrorCount,
    int AfterNumberingErrorCount
)
{
    public bool Passed => CandidatePackageStructurallyValid
        && TextPreserved
        && UnaffectedSequencesPreserved
        && AffectedParagraphsReassigned
        && TargetCounterRestarted
        && NoNewNumberingErrors;
}

public sealed class WordNumberingSequenceRepairPlan
{
    private readonly WordPackageTransactionCore _transaction;

    internal WordNumberingSequenceRepairPlan(
        string planId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        SemanticNodeId targetParagraphNodeId,
        string targetSourcePartUri,
        string targetSourcePath,
        string storyId,
        WordStoryKind storyKind,
        int sourceNumberId,
        int newNumberId,
        int abstractNumberId,
        int levelIndex,
        int startValue,
        long? targetCounterBefore,
        WordListCounterStatus targetCounterStatusBefore,
        long? targetCounterAfter,
        WordListCounterStatus targetCounterStatusAfter,
        IReadOnlyList<WordNumberingSequenceRepairParagraph> affectedParagraphs,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts,
        WordNumberingSequenceRepairValidation validation,
        IReadOnlyList<string> compatibilityRules
    )
    {
        PlanId = planId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        TargetParagraphNodeId = targetParagraphNodeId;
        TargetSourcePartUri = targetSourcePartUri;
        TargetSourcePath = targetSourcePath;
        StoryId = storyId;
        StoryKind = storyKind;
        SourceNumberId = sourceNumberId;
        NewNumberId = newNumberId;
        AbstractNumberId = abstractNumberId;
        LevelIndex = levelIndex;
        StartValue = startValue;
        TargetCounterBefore = targetCounterBefore;
        TargetCounterStatusBefore = targetCounterStatusBefore;
        TargetCounterAfter = targetCounterAfter;
        TargetCounterStatusAfter = targetCounterStatusAfter;
        AffectedParagraphs = new ReadOnlyCollection<WordNumberingSequenceRepairParagraph>(
            affectedParagraphs.ToArray()
        );
        Validation = validation;
        CompatibilityRules = new ReadOnlyCollection<string>(
            compatibilityRules.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        _transaction = new WordPackageTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            parts
        );
        ChangedParts = new ReadOnlyCollection<WordNumberingSequenceRepairPartChange>(
            _transaction.Parts
                .OrderBy(part => part.PartUri, StringComparer.Ordinal)
                .Select(part => new WordNumberingSequenceRepairPartChange(
                    part.PartUri,
                    part.BeforeSha256,
                    part.AfterSha256,
                    part.BeforeContent.Length,
                    part.AfterContent.Length
                ))
                .ToArray()
        );
    }

    public string PlanId { get; }

    public string BasePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public SemanticNodeId TargetParagraphNodeId { get; }

    public string TargetSourcePartUri { get; }

    public string TargetSourcePath { get; }

    public string StoryId { get; }

    public WordStoryKind StoryKind { get; }

    public int SourceNumberId { get; }

    public int NewNumberId { get; }

    public int AbstractNumberId { get; }

    public int LevelIndex { get; }

    public int StartValue { get; }

    public long? TargetCounterBefore { get; }

    public WordListCounterStatus TargetCounterStatusBefore { get; }

    public long? TargetCounterAfter { get; }

    public WordListCounterStatus TargetCounterStatusAfter { get; }

    public IReadOnlyList<WordNumberingSequenceRepairParagraph> AffectedParagraphs { get; }

    public IReadOnlyList<WordNumberingSequenceRepairPartChange> ChangedParts { get; }

    public WordNumberingSequenceRepairValidation Validation { get; }

    public IReadOnlyList<string> CompatibilityRules { get; }

    public int DirectNumberingMaterializedCount => AffectedParagraphs.Count(item =>
        item.DirectNumberingMaterialized
    );

    public bool HasChanges => _transaction.HasChanges;

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot) =>
        _transaction.CreateMutation(currentSnapshot);

    public OpcPackageMutationBuilder CreateInverseMutation(OpcPackageSnapshot appliedSnapshot) =>
        _transaction.CreateInverseMutation(appliedSnapshot);
}

public sealed class WordNumberingSequenceRepairPlanner
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private readonly WordNumberingSequenceRepairOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer = new();

    public WordNumberingSequenceRepairPlanner(
        WordNumberingSequenceRepairOptions? options = null
    )
    {
        _options = options ?? WordNumberingSequenceRepairOptions.Default;
        _options.Validate();
        _xmlOptions = new LosslessXmlOptions
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
            MaxTextCharacters = _options.MaxXmlPartBytes,
        };
        _reader = new OpcPackageReader();
    }

    public WordNumberingSequenceRepairPlan PlanRestart(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordNumberingSequenceRestartCommand command,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCommand(package, semanticDocument, command);

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
        if (!numbering.HasNumberingPart || numbering.NumberingPartUri is null)
        {
            throw new WordSemanticEditException(
                "The package has no numbering part to repair."
            );
        }
        var sequences = new WordListSequenceGraphBuilder().Build(
            package,
            semanticDocument,
            styles,
            numbering,
            cancellationToken
        );
        var target = sequences.Items.SingleOrDefault(item =>
            item.ParagraphNodeId == command.TargetParagraphNodeId
        ) ?? throw new WordSemanticEditException(
            "The target paragraph is not a safely executable numbered paragraph."
        );
        if (target.NumberId != command.ExpectedNumberId)
        {
            throw new WordSemanticPreconditionException(
                $"The target paragraph now uses numbering instance '{target.NumberId}', not expected '{command.ExpectedNumberId}'."
            );
        }
        if (target.LevelIndex != command.ExpectedLevelIndex)
        {
            throw new WordSemanticPreconditionException(
                $"The target paragraph now uses numbering level '{target.LevelIndex}', not expected '{command.ExpectedLevelIndex}'."
            );
        }

        var affectedItems = sequences.Items
            .Where(item =>
                string.Equals(item.StoryId, target.StoryId, StringComparison.Ordinal)
                && item.NumberId == target.NumberId
                && item.SequenceIndex >= target.SequenceIndex
            )
            .OrderBy(item => item.SequenceIndex)
            .ToArray();
        if (affectedItems.Length == 0 || affectedItems[0].ParagraphNodeId != target.ParagraphNodeId)
        {
            throw new WordSemanticEditException(
                "The target paragraph is not the first item in the selected numbering tail."
            );
        }
        if (affectedItems.Length > _options.MaxAffectedParagraphs)
        {
            throw new WordSemanticTransactionLimitException(
                $"Numbering repair would affect {affectedItems.Length} paragraphs; the limit is {_options.MaxAffectedParagraphs}."
            );
        }
        RejectAmbiguousTail(sequences, semanticDocument, target);

        if (!numbering.TryGetInstance(target.NumberId, out var sourceInstance)
            || sourceInstance is null)
        {
            throw new WordSemanticPreconditionException(
                $"Numbering instance '{target.NumberId}' disappeared."
            );
        }
        _ = numbering.ResolveLevel(target.NumberId, target.LevelIndex);
        RejectSourceNumberingErrors(numbering, sourceInstance, target);
        var newNumberId = AllocateNumberId(numbering);

        var affected = affectedItems.Select(item =>
        {
            if (!semanticDocument.TryGetNode(item.ParagraphNodeId, out var node)
                || node is null
                || node.Kind != WordSemanticNodeKind.Paragraph)
            {
                throw new WordSemanticPreconditionException(
                    $"Numbered paragraph '{item.ParagraphNodeId}' disappeared."
                );
            }
            return new AffectedSource(node, item);
        }).ToArray();

        var payloads = BuildPayloads(
            package,
            numbering,
            sourceInstance,
            affected,
            newNumberId,
            command.StartValue,
            cancellationToken
        );
        if (payloads.Count > _options.MaxChangedParts)
        {
            throw new WordSemanticTransactionLimitException(
                $"Numbering repair changes {payloads.Count} package parts; the limit is {_options.MaxChangedParts}."
            );
        }
        var projectedEntries = payloads.Values.ToDictionary(
            payload => payload.EntryName,
            payload => (ReadOnlyMemory<byte>)payload.AfterContent,
            StringComparer.Ordinal
        );
        var resultFingerprint = OpcPackageFingerprint.ComputeProjected(
            package,
            projectedEntries
        );
        var candidate = MaterializeCandidate(package, payloads, cancellationToken);
        var validation = ValidateCandidate(
            package,
            semanticDocument,
            numbering,
            sequences,
            candidate,
            affected,
            target,
            sourceInstance,
            newNumberId,
            command.StartValue,
            cancellationToken,
            out var targetAfter
        );
        if (!validation.Passed)
        {
            throw new WordSemanticEditException(
                "The numbering repair candidate failed semantic validation."
            );
        }
        if (!string.Equals(candidate.Fingerprint, resultFingerprint, StringComparison.Ordinal))
        {
            throw new WordSemanticEditException(
                "The numbering repair candidate does not match its predicted package fingerprint."
            );
        }

        var publicAffected = affected.Select(source =>
            new WordNumberingSequenceRepairParagraph(
                source.Node.Id,
                source.Item.StoryId,
                source.Item.StoryKind,
                source.Node.SourcePartUri,
                source.Node.SourcePath,
                source.Node.SourceOrder,
                source.Item.LevelIndex,
                source.Item.CounterValue,
                source.Item.CounterStatus,
                source.DirectNumberingMaterialized
            )
        ).ToArray();
        var compatibilityRules = new List<string>
        {
            "tail_reassigned_to_cloned_numbering_instance",
            "start_override_written_for_standard_consumers",
            "source_instance_and_earlier_items_preserved",
        };
        if (sourceInstance.TryGetLevelOverride(target.LevelIndex, out var sourceOverride)
            && sourceOverride?.Level is not null)
        {
            compatibilityRules.Add(
                "replacement_level_start_synchronized_for_qualified_word_build"
            );
        }
        return new WordNumberingSequenceRepairPlan(
            CreatePlanId(
                package.Fingerprint,
                resultFingerprint,
                command,
                newNumberId,
                publicAffected
            ),
            package.Fingerprint,
            resultFingerprint,
            target.ParagraphNodeId,
            affected[0].Node.SourcePartUri,
            affected[0].Node.SourcePath,
            target.StoryId,
            target.StoryKind,
            target.NumberId,
            newNumberId,
            sourceInstance.AbstractNumberId,
            target.LevelIndex,
            command.StartValue,
            target.CounterValue,
            target.CounterStatus,
            targetAfter.CounterValue,
            targetAfter.CounterStatus,
            publicAffected,
            payloads,
            validation,
            compatibilityRules
        );
    }

    private void ValidateCommand(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordNumberingSequenceRestartCommand command
    )
    {
        if (!string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordSemanticPreconditionException(
                "Semantic projection and package snapshot have different fingerprints."
            );
        }
        if (string.IsNullOrWhiteSpace(command.TargetParagraphNodeId.Value))
        {
            throw new ArgumentException("A target paragraph node ID is required.");
        }
        if (command.ExpectedNumberId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.ExpectedNumberId),
                "The expected numbering instance ID must be positive."
            );
        }
        if (command.ExpectedLevelIndex is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.ExpectedLevelIndex),
                "The expected numbering level must be between 0 and 8."
            );
        }
        if (command.StartValue < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.StartValue),
                "Word numbering start values cannot be negative."
            );
        }
    }

    private static void RejectAmbiguousTail(
        WordListSequenceGraph graph,
        WordSemanticDocument semanticDocument,
        WordListSequenceItem target
    )
    {
        foreach (var issue in graph.Issues.Where(issue =>
            issue.ParagraphNodeId is not null
            && string.Equals(issue.StoryId, target.StoryId, StringComparison.Ordinal)
            && issue.NumberId == target.NumberId
        ))
        {
            if (semanticDocument.TryGetNode(issue.ParagraphNodeId!.Value, out var node)
                && node is not null
                && node.SourceOrder >= target.SourceOrder)
            {
                throw new WordSemanticEditException(
                    "The selected numbering tail contains a revision or unresolved paragraph and cannot be repaired without choosing a document view."
                );
            }
        }
    }

    private static void RejectSourceNumberingErrors(
        WordNumberingGraph graph,
        WordNumberingInstance instance,
        WordListSequenceItem target
    )
    {
        var related = graph.Issues.FirstOrDefault(issue =>
            issue.Severity == WordNumberingIssueSeverity.Error
            && (issue.NumberId == instance.NumberId
                || issue.AbstractNumberId == instance.AbstractNumberId
                || issue.AbstractNumberId == target.EffectiveAbstractNumberId)
        );
        if (related is not null)
        {
            throw new WordSemanticEditException(
                $"The source numbering definition is damaged: {related.Code}."
            );
        }
    }

    private static int AllocateNumberId(WordNumberingGraph graph)
    {
        var maximum = graph.Instances.Count == 0
            ? 0
            : graph.Instances.Max(instance => instance.NumberId);
        if (graph.LastAssignedNumberId is { } cleanup)
        {
            maximum = Math.Max(maximum, cleanup);
        }
        if (maximum == int.MaxValue)
        {
            throw new WordSemanticTransactionLimitException(
                "No positive 32-bit numbering instance ID remains available."
            );
        }
        return maximum + 1;
    }

    private IReadOnlyDictionary<string, WordPackagePartPayload> BuildPayloads(
        OpcPackageSnapshot package,
        WordNumberingGraph numbering,
        WordNumberingInstance sourceInstance,
        IReadOnlyList<AffectedSource> affected,
        int newNumberId,
        int startValue,
        CancellationToken cancellationToken
    )
    {
        var parts = affected.Select(source => source.Node.SourcePartUri)
            .Append(numbering.NumberingPartUri!)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                uri => uri,
                uri => package.Parts.TryGetValue(uri, out var part)
                    ? part
                    : throw new WordSemanticPreconditionException(
                        $"Source part '{uri}' no longer exists."
                    ),
                StringComparer.Ordinal
            );
        var sources = parts.ToDictionary(
            pair => pair.Key,
            pair => ParsePart(pair.Value, cancellationToken),
            StringComparer.Ordinal
        );
        var patches = parts.Keys.ToDictionary(
            uri => uri,
            _ => new List<XmlSourcePatch>(),
            StringComparer.Ordinal
        );

        var numberingSource = sources[numbering.NumberingPartUri!];
        var numberingPatches = patches[numbering.NumberingPartUri!];
        AddClonedNumberingInstance(
            numberingSource,
            sourceInstance,
            newNumberId,
            affected[0].Item.LevelIndex,
            startValue,
            numberingPatches
        );

        foreach (var source in affected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xml = sources[source.Node.SourcePartUri];
            patches[source.Node.SourcePartUri].Add(
                CreateParagraphNumberingPatch(
                    xml,
                    source.Node,
                    source.Item.LevelIndex,
                    newNumberId,
                    out var materialized
                )
            );
            source.DirectNumberingMaterialized = materialized;
        }

        var payloads = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal);
        foreach (var (uri, source) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = parts[uri];
            var changed = source.ApplyPatches(
                patches[uri],
                part.Entry.Sha256,
                cancellationToken
            );
            if (changed.AsSpan().SequenceEqual(part.Entry.Content.Span))
            {
                continue;
            }
            payloads.Add(
                uri,
                new WordPackagePartPayload(
                    uri,
                    part.Entry.Name,
                    part.Entry.Content.ToArray(),
                    changed
                )
            );
        }
        return payloads;
    }

    private LosslessXmlDocument ParsePart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return LosslessXmlDocument.Parse(
                part.Entry.Content,
                _xmlOptions,
                cancellationToken
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                $"Part '{part.Uri}' cannot be edited losslessly.",
                exception
            );
        }
    }

    private static void AddClonedNumberingInstance(
        LosslessXmlDocument source,
        WordNumberingInstance sourceInstance,
        int newNumberId,
        int levelIndex,
        int startValue,
        ICollection<XmlSourcePatch> patches
    )
    {
        var root = source.GetParsedElement(source.Root.Ordinal);
        if (!IsWordNamespace(root.Name.NamespaceName) || root.Name.LocalName != "numbering")
        {
            throw new WordSemanticPreconditionException(
                "The numbering source no longer has a w:numbering root."
            );
        }
        var w = root.Name.Namespace;
        var sourceElement = source.GetParsedElement(sourceInstance.SourceElementOrdinal);
        if (sourceElement.Name != w + "num")
        {
            throw new WordSemanticPreconditionException(
                "The source numbering instance no longer binds to w:num."
            );
        }
        var clone = new XElement(sourceElement);
        CopyInheritedNamespaceDeclarations(sourceElement, clone);
        clone.SetAttributeValue(w + "numId", newNumberId.ToString(CultureInfo.InvariantCulture));
        var overrides = clone.Elements(w + "lvlOverride")
            .Where(element => AttributeInt(element, w + "ilvl") == levelIndex)
            .ToArray();
        if (overrides.Length > 1)
        {
            throw new WordSemanticEditException(
                $"Numbering instance '{sourceInstance.NumberId}' contains duplicate overrides for level {levelIndex}."
            );
        }
        var levelOverride = overrides.SingleOrDefault();
        if (levelOverride is null)
        {
            levelOverride = new XElement(
                w + "lvlOverride",
                new XAttribute(w + "ilvl", levelIndex),
                new XElement(w + "startOverride", new XAttribute(w + "val", startValue))
            );
            clone.Add(levelOverride);
        }
        else
        {
            SetSingleValueChild(levelOverride, w + "startOverride", w, startValue, addFirst: true);
            var replacementLevels = levelOverride.Elements(w + "lvl").ToArray();
            if (replacementLevels.Length > 1)
            {
                throw new WordSemanticEditException(
                    $"Numbering override {levelIndex} contains duplicate replacement levels."
                );
            }
            if (replacementLevels.Length == 1)
            {
                SetSingleValueChild(
                    replacementLevels[0],
                    w + "start",
                    w,
                    startValue,
                    addFirst: true
                );
            }
        }

        var fragment = clone.ToString(SaveOptions.DisableFormatting);
        var cleanup = root.Elements(w + "numIdMacAtCleanup").ToArray();
        if (cleanup.Length > 1)
        {
            throw new WordSemanticEditException(
                "The numbering root contains duplicate numIdMacAtCleanup elements."
            );
        }
        if (cleanup.Length == 1)
        {
            var ordinal = source.GetElementOrdinal(cleanup[0]);
            patches.Add(source.CreateElementSiblingInsertionPatch(
                ordinal,
                fragment,
                XmlSiblingInsertionPosition.Before
            ));
            foreach (var patch in source.CreateElementAttributeValuePatches(
                ordinal,
                w.NamespaceName,
                "val",
                newNumberId.ToString(CultureInfo.InvariantCulture),
                preferredPrefix: "w"
            ))
            {
                patches.Add(patch);
            }
        }
        else
        {
            patches.Add(source.CreateElementContentInsertionPatch(
                source.Root.Ordinal,
                fragment,
                XmlContentInsertionPosition.Append
            ));
        }
    }

    private static XmlSourcePatch CreateParagraphNumberingPatch(
        LosslessXmlDocument source,
        WordSemanticNode node,
        int levelIndex,
        int newNumberId,
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
                CreateNumberingProperties(w, levelIndex, newNumberId)
            ).ToString(SaveOptions.DisableFormatting);
            return source.CreateElementContentInsertionPatch(
                node.SourceElementOrdinal,
                fragment,
                XmlContentInsertionPosition.Prepend
            );
        }

        var originalProperties = properties[0];
        var clone = new XElement(originalProperties);
        CopyInheritedNamespaceDeclarations(originalProperties, clone);
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
                CreateNumberingProperties(w, levelIndex, newNumberId),
                w
            );
        }
        else
        {
            SetNumberingProperties(
                numberingProperties[0],
                w,
                levelIndex,
                newNumberId
            );
        }
        return source.CreateElementReplacementPatch(
            source.GetElementOrdinal(originalProperties),
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
        var levels = numPr.Elements(w + "ilvl").ToArray();
        var numbers = numPr.Elements(w + "numId").ToArray();
        if (levels.Length > 1 || numbers.Length > 1)
        {
            throw new WordSemanticEditException(
                "Paragraph numPr contains duplicate ilvl or numId elements."
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
            var level = numPr.Elements(w + "ilvl").First();
            level.AddAfterSelf(new XElement(w + "numId", new XAttribute(w + "val", numberId)));
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

    private static void SetSingleValueChild(
        XElement parent,
        XName childName,
        XNamespace w,
        int value,
        bool addFirst
    )
    {
        var children = parent.Elements(childName).ToArray();
        if (children.Length > 1)
        {
            throw new WordSemanticEditException(
                $"Element '{parent.Name.LocalName}' contains duplicate '{childName.LocalName}' children."
            );
        }
        if (children.Length == 1)
        {
            children[0].SetAttributeValue(w + "val", value);
            return;
        }
        var child = new XElement(childName, new XAttribute(w + "val", value));
        if (addFirst)
        {
            parent.AddFirst(child);
        }
        else
        {
            parent.Add(child);
        }
    }

    private OpcPackageSnapshot MaterializeCandidate(
        OpcPackageSnapshot package,
        IReadOnlyDictionary<string, WordPackagePartPayload> payloads,
        CancellationToken cancellationToken
    )
    {
        var mutation = new OpcPackageMutationBuilder(package);
        foreach (var payload in payloads.Values)
        {
            mutation.ReplacePart(
                payload.PartUri,
                payload.AfterContent,
                payload.BeforeSha256
            );
        }
        using var stream = new MemoryStream();
        _serializer.Write(stream, mutation);
        stream.Position = 0;
        return _reader.Read(stream, cancellationToken);
    }

    private static WordNumberingSequenceRepairValidation ValidateCandidate(
        OpcPackageSnapshot baselinePackage,
        WordSemanticDocument baselineSemantic,
        WordNumberingGraph baselineNumbering,
        WordListSequenceGraph baselineSequences,
        OpcPackageSnapshot candidatePackage,
        IReadOnlyList<AffectedSource> affected,
        WordListSequenceItem target,
        WordNumberingInstance sourceInstance,
        int newNumberId,
        int startValue,
        CancellationToken cancellationToken,
        out WordListSequenceItem targetAfter
    )
    {
        if (!candidatePackage.IsStructurallyValid)
        {
            throw new WordSemanticEditException(
                "The numbering repair candidate has structural OPC errors."
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
        if (!candidateNumbering.TryGetInstance(newNumberId, out var cloned)
            || cloned is null
            || cloned.AbstractNumberId != sourceInstance.AbstractNumberId
            || candidateNumbering.Instances.Count != baselineNumbering.Instances.Count + 1)
        {
            throw new WordSemanticEditException(
                "The cloned numbering instance did not survive candidate projection."
            );
        }

        var candidateParagraphs = candidateSemantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .ToDictionary(
                node => SourceKey(node.SourcePartUri, node.SourcePath),
                StringComparer.Ordinal
            );
        var candidateItems = candidateSequences.Items.ToDictionary(
            item => item.ParagraphNodeId
        );
        var affectedKeys = affected.Select(source =>
            SourceKey(source.Node.SourcePartUri, source.Node.SourcePath)
        ).ToHashSet(StringComparer.Ordinal);
        var affectedReassigned = true;
        targetAfter = null!;
        foreach (var source in affected)
        {
            var key = SourceKey(source.Node.SourcePartUri, source.Node.SourcePath);
            if (!candidateParagraphs.TryGetValue(key, out var paragraph)
                || !candidateItems.TryGetValue(paragraph.Id, out var item)
                || item.NumberId != newNumberId
                || item.LevelIndex != source.Item.LevelIndex)
            {
                affectedReassigned = false;
                continue;
            }
            if (key == SourceKey(affected[0].Node.SourcePartUri, affected[0].Node.SourcePath))
            {
                targetAfter = item;
            }
        }
        var targetRestarted = targetAfter is not null
            && targetAfter.CounterStatus == WordListCounterStatus.Exact
            && targetAfter.CounterValue == startValue;

        var unaffectedPreserved = true;
        foreach (var before in baselineSequences.Items)
        {
            if (!baselineSemantic.TryGetNode(before.ParagraphNodeId, out var beforeNode)
                || beforeNode is null)
            {
                unaffectedPreserved = false;
                break;
            }
            var key = SourceKey(beforeNode.SourcePartUri, beforeNode.SourcePath);
            if (affectedKeys.Contains(key))
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
                || !string.Equals(before.Label, after.Label, StringComparison.Ordinal))
            {
                unaffectedPreserved = false;
                break;
            }
        }

        var textPreserved = TextProjection(baselineSemantic)
            .SequenceEqual(TextProjection(candidateSemantic));
        var beforeErrors = baselineNumbering.Issues.Count(issue =>
            issue.Severity == WordNumberingIssueSeverity.Error
        ) + baselineSequences.Issues.Count(issue =>
            issue.Severity == WordListSequenceIssueSeverity.Error
        );
        var afterErrors = candidateNumbering.Issues.Count(issue =>
            issue.Severity == WordNumberingIssueSeverity.Error
        ) + candidateSequences.Issues.Count(issue =>
            issue.Severity == WordListSequenceIssueSeverity.Error
        );
        return new WordNumberingSequenceRepairValidation(
            CandidatePackageStructurallyValid: candidatePackage.IsStructurallyValid,
            TextPreserved: textPreserved,
            UnaffectedSequencesPreserved: unaffectedPreserved,
            AffectedParagraphsReassigned: affectedReassigned,
            TargetCounterRestarted: targetRestarted,
            NoNewNumberingErrors: afterErrors <= beforeErrors,
            BeforeNumberingErrorCount: beforeErrors,
            AfterNumberingErrorCount: afterErrors
        );
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

    private static string SourceKey(string partUri, string sourcePath) =>
        partUri + "\u001f" + sourcePath;

    private static int AttributeInt(XElement element, XName name)
    {
        var value = element.Attribute(name)?.Value;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : -1;
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

    private static bool IsWordNamespace(string namespaceUri) =>
        namespaceUri is WordTransitionalNamespace or WordStrictNamespace;

    private static string CreatePlanId(
        string baseFingerprint,
        string resultFingerprint,
        WordNumberingSequenceRestartCommand command,
        int newNumberId,
        IReadOnlyList<WordNumberingSequenceRepairParagraph> affected
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-numbering-sequence-restart-plan-v1");
        AppendHash(hash, baseFingerprint);
        AppendHash(hash, resultFingerprint);
        AppendHash(hash, command.TargetParagraphNodeId.Value);
        AppendHash(hash, command.ExpectedNumberId.ToString(CultureInfo.InvariantCulture));
        AppendHash(hash, command.ExpectedLevelIndex.ToString(CultureInfo.InvariantCulture));
        AppendHash(hash, command.StartValue.ToString(CultureInfo.InvariantCulture));
        AppendHash(hash, newNumberId.ToString(CultureInfo.InvariantCulture));
        foreach (var paragraph in affected)
        {
            AppendHash(hash, paragraph.SourcePartUri);
            AppendHash(hash, paragraph.SourcePath);
            AppendHash(hash, paragraph.LevelIndex.ToString(CultureInfo.InvariantCulture));
        }
        return "wnrplan_" + Convert.ToBase64String(hash.GetHashAndReset().AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AppendHash(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private sealed class AffectedSource
    {
        internal AffectedSource(WordSemanticNode node, WordListSequenceItem item)
        {
            Node = node;
            Item = item;
        }

        internal WordSemanticNode Node { get; }

        internal WordListSequenceItem Item { get; }

        internal bool DirectNumberingMaterialized { get; set; }
    }
}
