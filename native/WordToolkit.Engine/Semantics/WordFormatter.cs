using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordFormatterPolicy
{
    RemoveRedundantDirectFormatting,
}

public sealed record WordFormatterOptions
{
    public static WordFormatterOptions Default { get; } = new();

    public int MaxSemanticNodes { get; init; } = 100_000;

    public int MaxDirectFormattingNodes { get; init; } = 256;

    public int MaxCandidateElements { get; init; } = 4_096;

    public int MaxCompositeCandidateProofs { get; init; } = 64;

    public int MaxRemovedElements { get; init; } = 1_024;

    public int MaxAffectedNodes { get; init; } = 8_192;

    public int MaxChangedParts { get; init; } = 64;

    public int MaxSourceXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxSemanticNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSemanticNodes));
        }
        if (MaxDirectFormattingNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDirectFormattingNodes));
        }
        if (MaxCandidateElements <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCandidateElements));
        }
        if (MaxCompositeCandidateProofs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCompositeCandidateProofs));
        }
        if (MaxRemovedElements <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRemovedElements));
        }
        if (MaxAffectedNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAffectedNodes));
        }
        if (MaxChangedParts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChangedParts));
        }
        if (MaxSourceXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSourceXmlPartBytes));
        }
    }
}

public sealed record WordFormatterChange(
    int Index,
    WordFormatterPolicy Policy,
    SemanticNodeId NodeId,
    WordSemanticNodeKind NodeKind,
    string SourcePartUri,
    int SourceElementOrdinal,
    int PropertyElementOrdinal,
    string PropertyElementName,
    int PropertyCount,
    int RemovedBytes,
    string SourceElementFingerprint
);

public sealed record WordFormatterPartChange(
    string PartUri,
    string EntryName,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed record WordFormatterValidation(
    bool BasePackageStructurallyValid,
    bool CandidatePackageStructurallyValid,
    bool CandidateFingerprintMatched,
    bool ChangedOnlyPlannedParts,
    bool SemanticContentPreserved,
    bool EffectiveFormattingPreserved,
    int AffectedNodeCount,
    int CandidatePackageErrorCount
)
{
    public bool Passed =>
        BasePackageStructurallyValid
        && CandidatePackageStructurallyValid
        && CandidateFingerprintMatched
        && ChangedOnlyPlannedParts
        && SemanticContentPreserved
        && EffectiveFormattingPreserved
        && CandidatePackageErrorCount == 0;
}

public sealed class WordFormatterPlan
{
    private readonly WordPackageTransactionCore _transaction;

    internal WordFormatterPlan(
        string planId,
        IReadOnlyList<WordFormatterPolicy> policies,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        int semanticNodesScanned,
        int directFormattingNodesScanned,
        int candidateElementsScanned,
        int compositeCandidateProofs,
        IReadOnlyList<WordFormatterChange> changes,
        WordFormatterValidation validation,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts
    )
    {
        PlanId = planId;
        Policies = new ReadOnlyCollection<WordFormatterPolicy>(policies.ToArray());
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        SemanticNodesScanned = semanticNodesScanned;
        DirectFormattingNodesScanned = directFormattingNodesScanned;
        CandidateElementsScanned = candidateElementsScanned;
        CompositeCandidateProofs = compositeCandidateProofs;
        Changes = new ReadOnlyCollection<WordFormatterChange>(changes.ToArray());
        Validation = validation;
        _transaction = new WordPackageTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            parts
        );
        ChangedParts = new ReadOnlyCollection<WordFormatterPartChange>(
            _transaction.Parts
                .OrderBy(part => part.PartUri, StringComparer.Ordinal)
                .Select(part => new WordFormatterPartChange(
                    part.PartUri,
                    part.EntryName,
                    part.BeforeSha256,
                    part.AfterSha256,
                    part.BeforeContent.Length,
                    part.AfterContent.Length
                ))
                .ToArray()
        );
    }

    public string PlanId { get; }

    public IReadOnlyList<WordFormatterPolicy> Policies { get; }

    public string BasePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public int SemanticNodesScanned { get; }

    public int DirectFormattingNodesScanned { get; }

    public int CandidateElementsScanned { get; }

    public int CompositeCandidateProofs { get; }

    public IReadOnlyList<WordFormatterChange> Changes { get; }

    public IReadOnlyList<WordFormatterPartChange> ChangedParts { get; }

    public WordFormatterValidation Validation { get; }

    public bool HasChanges => _transaction.HasChanges;

    public int RemovedElementCount => Changes.Count;

    public long RemovedByteCount => Changes.Sum(change => (long)change.RemovedBytes);

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot) =>
        _transaction.CreateMutation(currentSnapshot);

    public OpcPackageMutationBuilder CreateInverseMutation(
        OpcPackageSnapshot appliedSnapshot
    ) => _transaction.CreateInverseMutation(appliedSnapshot);
}

public sealed class WordFormatterPlanner
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private static readonly IReadOnlySet<string> ParagraphStructuralProperties =
        new HashSet<string>(
            ["pStyle", "numPr", "sectPr", "pPrChange", "rPr"],
            StringComparer.Ordinal
        );

    private static readonly IReadOnlySet<string> RunStructuralProperties =
        new HashSet<string>(["rStyle", "rPrChange"], StringComparer.Ordinal);

    // These elements form mutually superseding property groups. Removing one needs a
    // group-aware equivalence proof, not a scalar equality check.
    private static readonly IReadOnlySet<string> ParagraphCompositeProperties =
        new HashSet<string>(["shd"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> RunCompositeProperties =
        new HashSet<string>(["rFonts", "color", "u", "shd"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> UnresolvedCascadeLayers =
        new HashSet<string>(
            [
                "conditional_table_style_properties",
                "numbering_level_properties",
                "revision_view_formatting",
                "unmodeled_property_elements",
                "word_style_numbering_level_compatibility",
            ],
            StringComparer.Ordinal
        );

    private readonly WordFormatterOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;

    public WordFormatterPlanner(
        WordFormatterOptions? options = null,
        LosslessXmlOptions? xmlOptions = null
    )
    {
        _options = options ?? WordFormatterOptions.Default;
        _options.Validate();
        _xmlOptions = xmlOptions ?? new LosslessXmlOptions
        {
            MaxSourceBytes = _options.MaxSourceXmlPartBytes,
            MaxXmlCharacters = _options.MaxSourceXmlPartBytes,
            MaxTextCharacters = _options.MaxSourceXmlPartBytes,
        };
        _xmlOptions.Validate();
    }

    public WordFormatterPlan Plan(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        string expectedPackageFingerprint,
        IEnumerable<WordFormatterPolicy> policies,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPackageFingerprint);
        ArgumentNullException.ThrowIfNull(policies);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCompatibleSnapshot(
            package,
            semanticDocument,
            expectedPackageFingerprint
        );
        if (!package.IsStructurallyValid)
        {
            throw new WordFormatterPreconditionException(
                "Formatting requires a structurally valid base package."
            );
        }

        var selectedPolicies = policies.Distinct().Order().ToArray();
        if (selectedPolicies.Length == 0)
        {
            throw new ArgumentException(
                "At least one formatter policy is required.",
                nameof(policies)
            );
        }
        if (selectedPolicies.Any(policy =>
            policy != WordFormatterPolicy.RemoveRedundantDirectFormatting
        ))
        {
            throw new WordFormatterPreconditionException(
                "The formatter request contains an unsupported policy."
            );
        }

        var semanticNodes = semanticDocument.Nodes.ToArray();
        if (semanticNodes.Length > _options.MaxSemanticNodes)
        {
            throw new WordFormatterLimitException(
                $"Semantic projection contains {semanticNodes.Length} nodes; formatter limit is {_options.MaxSemanticNodes}."
            );
        }

        var baseline = BuildFormattingContext(
            package,
            semanticDocument,
            cancellationToken
        );
        var sources = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        var sourceParts = new Dictionary<string, OpcPart>(StringComparer.Ordinal);
        var patches = new Dictionary<string, List<XmlSourcePatch>>(StringComparer.Ordinal);
        var changes = new List<WordFormatterChange>();
        var affectedKeys = new HashSet<FormattingNodeKey>();
        var directFormattingNodes = 0;
        var candidateElements = 0;
        var compositeCandidateProofs = 0;

        foreach (
            var node in semanticNodes.Where(node =>
                node.Kind is WordSemanticNodeKind.Paragraph or WordSemanticNodeKind.Run
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = GetSource(
                package,
                node.SourcePartUri,
                sources,
                sourceParts,
                patches,
                cancellationToken
            );
            var owner = BoundFormattingOwner(source, node);
            var propertyContainerName = node.Kind == WordSemanticNodeKind.Paragraph
                ? "pPr"
                : "rPr";
            var containers = owner.Children.Where(child =>
                IsWordElement(child, propertyContainerName)
            ).Take(2).ToArray();
            if (containers.Length > 1)
            {
                throw new WordFormatterPreconditionException(
                    $"Semantic node '{node.Id}' has duplicate {propertyContainerName} elements."
                );
            }
            if (containers.Length == 0)
            {
                continue;
            }

            var propertyContainer = containers[0];
            var eligibleChildren = propertyContainer.Children.Where(child =>
                IsWordNamespace(child.NamespaceUri)
                && !IsStructuralProperty(node.Kind, child.LocalName)
            ).ToArray();
            if (eligibleChildren.Length == 0)
            {
                continue;
            }
            directFormattingNodes++;
            if (directFormattingNodes > _options.MaxDirectFormattingNodes)
            {
                throw new WordFormatterLimitException(
                    $"Document has more than {_options.MaxDirectFormattingNodes} semantic objects with direct formatting."
                );
            }

            WordEffectiveFormatting effective;
            try
            {
                effective = baseline.Resolver.Resolve(
                    baseline.Package,
                    baseline.Semantic,
                    baseline.Styles,
                    baseline.Numbering,
                    baseline.Theme,
                    baseline.Settings,
                    baseline.Fonts,
                    node.Id,
                    cancellationToken
                );
            }
            catch (WordFormattingResolutionException exception)
            {
                throw new WordFormatterPreconditionException(
                    $"Direct formatting for semantic node '{node.Id}' cannot be resolved safely.",
                    exception
                );
            }
            if (effective.CoverageOmissions.Any(UnresolvedCascadeLayers.Contains))
            {
                continue;
            }

            foreach (var propertyElement in eligibleChildren)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidateElements++;
                if (candidateElements > _options.MaxCandidateElements)
                {
                    throw new WordFormatterLimitException(
                        $"Direct-formatting candidate count exceeds {_options.MaxCandidateElements}."
                    );
                }
                var propertySet = ReadSinglePropertySet(
                    source,
                    propertyElement,
                    node.Kind
                );
                if (
                    !propertySet.IsFullyModeled
                    || propertySet.Values.Count == 0
                )
                {
                    continue;
                }
                if (changes.Count >= _options.MaxRemovedElements)
                {
                    throw new WordFormatterLimitException(
                        $"Formatter would remove more than {_options.MaxRemovedElements} elements."
                    );
                }

                var patch = source.CreateElementRemovalPatch(propertyElement.Ordinal);
                var isComposite = IsCompositeProperty(
                    node.Kind,
                    propertyElement.LocalName
                );
                HashSet<FormattingNodeKey>? trialAffected = null;
                if (isComposite)
                {
                    compositeCandidateProofs++;
                    if (compositeCandidateProofs > _options.MaxCompositeCandidateProofs)
                    {
                        throw new WordFormatterLimitException(
                            $"Composite direct-formatting proof count exceeds {_options.MaxCompositeCandidateProofs}."
                        );
                    }
                    trialAffected = new HashSet<FormattingNodeKey>(affectedKeys);
                    AddAffectedNodes(node, trialAffected);
                    if (trialAffected.Count > _options.MaxAffectedNodes)
                    {
                        throw new WordFormatterLimitException(
                            $"Formatter would require more than {_options.MaxAffectedNodes} effective-formatting proofs."
                        );
                    }
                    patches[node.SourcePartUri].Add(patch);
                    if (!ProvesCompositeRemovalEquivalent(
                        baseline,
                        sources,
                        sourceParts,
                        patches,
                        trialAffected,
                        cancellationToken
                    ))
                    {
                        patches[node.SourcePartUri].RemoveAt(
                            patches[node.SourcePartUri].Count - 1
                        );
                        continue;
                    }
                }
                else
                {
                    if (!IsRedundant(
                        effective,
                        node,
                        propertySet.Values.Keys
                    ))
                    {
                        continue;
                    }
                    patches[node.SourcePartUri].Add(patch);
                }
                var fingerprint = FingerprintSourceElement(
                    sourceParts[node.SourcePartUri].Entry.Content.Span,
                    propertyElement.FullSpan
                );
                changes.Add(
                    new WordFormatterChange(
                        changes.Count,
                        WordFormatterPolicy.RemoveRedundantDirectFormatting,
                        node.Id,
                        node.Kind,
                        node.SourcePartUri,
                        node.SourceElementOrdinal,
                        propertyElement.Ordinal,
                        propertyElement.LocalName,
                        propertySet.Values.Count,
                        propertyElement.FullSpan.ByteLength,
                        fingerprint
                    )
                );
                if (trialAffected is not null)
                {
                    affectedKeys.UnionWith(trialAffected);
                }
                else
                {
                    AddAffectedNodes(node, affectedKeys);
                }
                if (affectedKeys.Count > _options.MaxAffectedNodes)
                {
                    throw new WordFormatterLimitException(
                        $"Formatter would require more than {_options.MaxAffectedNodes} effective-formatting proofs."
                    );
                }
            }
        }

        var payloads = BuildPayloads(
            sources,
            sourceParts,
            patches,
            cancellationToken
        );
        if (payloads.Count > _options.MaxChangedParts)
        {
            throw new WordFormatterLimitException(
                $"Formatter would change {payloads.Count} parts; limit is {_options.MaxChangedParts}."
            );
        }
        var projectedEntries = payloads.Values.ToDictionary(
            payload => payload.EntryName,
            payload => (ReadOnlyMemory<byte>)payload.AfterContent,
            StringComparer.Ordinal
        );
        var resultFingerprint = payloads.Count == 0
            ? package.Fingerprint
            : OpcPackageFingerprint.ComputeProjected(package, projectedEntries);
        var transaction = new WordPackageTransactionCore(
            package.Fingerprint,
            resultFingerprint,
            payloads
        );
        var validation = ValidateCandidate(
            baseline,
            transaction,
            resultFingerprint,
            payloads.Keys,
            affectedKeys,
            cancellationToken
        );
        if (!validation.Passed)
        {
            throw new WordFormatterValidationException(
                "The formatter candidate did not preserve semantic content and effective formatting.",
                validation
            );
        }

        return new WordFormatterPlan(
            CreatePlanId(package.Fingerprint, selectedPolicies, changes),
            selectedPolicies,
            package.Fingerprint,
            resultFingerprint,
            semanticNodes.Length,
            directFormattingNodes,
            candidateElements,
            compositeCandidateProofs,
            changes,
            validation,
            payloads
        );
    }

    private bool ProvesCompositeRemovalEquivalent(
        FormattingContext baseline,
        IReadOnlyDictionary<string, LosslessXmlDocument> sources,
        IReadOnlyDictionary<string, OpcPart> sourceParts,
        IReadOnlyDictionary<string, List<XmlSourcePatch>> patches,
        IReadOnlySet<FormattingNodeKey> affectedKeys,
        CancellationToken cancellationToken
    )
    {
        var payloads = BuildPayloads(
            sources,
            sourceParts,
            patches,
            cancellationToken
        );
        if (payloads.Count == 0)
        {
            return false;
        }
        if (payloads.Count > _options.MaxChangedParts)
        {
            throw new WordFormatterLimitException(
                $"Formatter would change {payloads.Count} parts; limit is {_options.MaxChangedParts}."
            );
        }
        var projectedEntries = payloads.Values.ToDictionary(
            payload => payload.EntryName,
            payload => (ReadOnlyMemory<byte>)payload.AfterContent,
            StringComparer.Ordinal
        );
        var resultFingerprint = OpcPackageFingerprint.ComputeProjected(
            baseline.Package,
            projectedEntries
        );
        var transaction = new WordPackageTransactionCore(
            baseline.Package.Fingerprint,
            resultFingerprint,
            payloads
        );
        try
        {
            return ValidateCandidate(
                baseline,
                transaction,
                resultFingerprint,
                payloads.Keys,
                affectedKeys,
                cancellationToken
            ).Passed;
        }
        catch (WordFormatterValidationException)
        {
            return false;
        }
    }

    private FormattingContext BuildFormattingContext(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        CancellationToken cancellationToken
    )
    {
        var styles = new WordStyleGraphBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            semantic,
            styles,
            cancellationToken
        );
        var theme = new WordThemeGraphBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        var settings = new WordSettingsGraphBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        var fonts = new WordFontTableGraphBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        return new FormattingContext(
            package,
            semantic,
            styles,
            numbering,
            theme,
            settings,
            fonts,
            new WordEffectiveFormattingResolver()
        );
    }

    private WordFormatterValidation ValidateCandidate(
        FormattingContext baseline,
        WordPackageTransactionCore transaction,
        string expectedResultFingerprint,
        IEnumerable<string> plannedPartUris,
        IReadOnlySet<FormattingNodeKey> affectedKeys,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        new OpcPackageSerializer().Write(
            stream,
            transaction.CreateMutation(baseline.Package),
            OpcSerializationMode.Preserve
        );
        stream.Position = 0;
        var candidatePackage = new OpcPackageReader().Read(stream, cancellationToken);
        var fingerprintMatched = string.Equals(
            candidatePackage.Fingerprint,
            expectedResultFingerprint,
            StringComparison.Ordinal
        );
        var actualChangedParts = ChangedPartUris(
            baseline.Package,
            candidatePackage
        );
        var expectedChangedParts = plannedPartUris.ToHashSet(StringComparer.Ordinal);
        var changedOnlyPlannedParts = actualChangedParts.SetEquals(expectedChangedParts);
        var candidateErrors = candidatePackage.Diagnostics.Count(diagnostic =>
            diagnostic.Severity is OpcDiagnosticSeverity.Error
                or OpcDiagnosticSeverity.Fatal
        );

        WordSemanticDocument candidateSemantic;
        try
        {
            candidateSemantic = new WordSemanticProjector().Project(
                candidatePackage,
                cancellationToken
            );
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new WordFormatterValidationException(
                "The formatter candidate cannot be projected semantically.",
                new WordFormatterValidation(
                    baseline.Package.IsStructurallyValid,
                    candidatePackage.IsStructurallyValid,
                    fingerprintMatched,
                    changedOnlyPlannedParts,
                    false,
                    false,
                    affectedKeys.Count,
                    candidateErrors
                ),
                exception
            );
        }
        var semanticContentPreserved = string.Equals(
            SemanticContentFingerprint(baseline.Semantic),
            SemanticContentFingerprint(candidateSemantic),
            StringComparison.Ordinal
        );
        var candidate = BuildFormattingContext(
            candidatePackage,
            candidateSemantic,
            cancellationToken
        );
        var effectiveFormattingPreserved = CompareAffectedFormatting(
            baseline,
            candidate,
            affectedKeys,
            cancellationToken
        );
        return new WordFormatterValidation(
            baseline.Package.IsStructurallyValid,
            candidatePackage.IsStructurallyValid,
            fingerprintMatched,
            changedOnlyPlannedParts,
            semanticContentPreserved,
            effectiveFormattingPreserved,
            affectedKeys.Count,
            candidateErrors
        );
    }

    private static bool CompareAffectedFormatting(
        FormattingContext before,
        FormattingContext after,
        IReadOnlySet<FormattingNodeKey> affectedKeys,
        CancellationToken cancellationToken
    )
    {
        var beforeNodes = before.Semantic.Nodes.ToDictionary(
            FormattingNodeKey.From,
            node => node
        );
        var afterNodes = after.Semantic.Nodes.ToDictionary(
            FormattingNodeKey.From,
            node => node
        );
        foreach (var key in affectedKeys.OrderBy(key => key.PartUri, StringComparer.Ordinal)
            .ThenBy(key => key.SourcePath, StringComparer.Ordinal)
            .ThenBy(key => key.Kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !beforeNodes.TryGetValue(key, out var beforeNode)
                || !afterNodes.TryGetValue(key, out var afterNode)
            )
            {
                return false;
            }
            var beforeFormatting = before.Resolver.Resolve(
                before.Package,
                before.Semantic,
                before.Styles,
                before.Numbering,
                before.Theme,
                before.Settings,
                before.Fonts,
                beforeNode.Id,
                cancellationToken
            );
            var afterFormatting = after.Resolver.Resolve(
                after.Package,
                after.Semantic,
                after.Styles,
                after.Numbering,
                after.Theme,
                after.Settings,
                after.Fonts,
                afterNode.Id,
                cancellationToken
            );
            if (!EquivalentFormatting(beforeFormatting, afterFormatting))
            {
                return false;
            }
        }
        return true;
    }

    private static bool EquivalentFormatting(
        WordEffectiveFormatting before,
        WordEffectiveFormatting after
    ) =>
        before.NodeKind == after.NodeKind
        && string.Equals(
            before.ParagraphStyleId,
            after.ParagraphStyleId,
            StringComparison.Ordinal
        )
        && string.Equals(
            before.CharacterStyleId,
            after.CharacterStyleId,
            StringComparison.Ordinal
        )
        && before.NumberingRemoved == after.NumberingRemoved
        && EquivalentPropertyMap(
            before.ParagraphProperties,
            after.ParagraphProperties
        )
        && EquivalentPropertyMap(before.RunProperties, after.RunProperties)
        && before.UnmodeledElements.SequenceEqual(
            after.UnmodeledElements,
            StringComparer.Ordinal
        )
        && before.CoverageOmissions.SequenceEqual(
            after.CoverageOmissions,
            StringComparer.Ordinal
        )
        && before.CompatibilityWarnings.SequenceEqual(
            after.CompatibilityWarnings,
            StringComparer.Ordinal
        );

    private static bool EquivalentPropertyMap(
        IReadOnlyDictionary<string, WordEffectiveFormattingProperty> before,
        IReadOnlyDictionary<string, WordEffectiveFormattingProperty> after
    )
    {
        if (before.Count != after.Count)
        {
            return false;
        }
        foreach (var pair in before)
        {
            if (
                !after.TryGetValue(pair.Key, out var candidate)
                || !string.Equals(
                    pair.Value.Value,
                    candidate.Value,
                    StringComparison.Ordinal
                )
                || pair.Value.IsToggle != candidate.IsToggle
            )
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsRedundant(
        WordEffectiveFormatting effective,
        WordSemanticNode node,
        IEnumerable<string> propertyNames
    )
    {
        var properties = node.Kind == WordSemanticNodeKind.Paragraph
            ? effective.ParagraphProperties
            : effective.RunProperties;
        var expectedSource = node.Kind == WordSemanticNodeKind.Paragraph
            ? WordFormattingSourceKind.DirectParagraphFormatting
            : WordFormattingSourceKind.DirectRunFormatting;
        foreach (var propertyName in propertyNames)
        {
            if (!properties.TryGetValue(propertyName, out var property))
            {
                return false;
            }
            var contributions = property.Contributions;
            if (contributions.Count < 2)
            {
                return false;
            }
            var directIndex = contributions.Count - 1;
            var direct = contributions[directIndex];
            if (
                direct.SourceKind != expectedSource
                || direct.SourceElementOrdinal != node.SourceElementOrdinal
                || !string.Equals(
                    direct.ResultingValue,
                    contributions[directIndex - 1].ResultingValue,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }
        }
        return true;
    }

    private static WordStylePropertySet ReadSinglePropertySet(
        LosslessXmlDocument source,
        XmlSourceElement propertyElement,
        WordSemanticNodeKind nodeKind
    )
    {
        var parsed = source.GetParsedElement(propertyElement.Ordinal);
        var containerName = nodeKind == WordSemanticNodeKind.Paragraph
            ? "pPr"
            : "rPr";
        var container = new XElement(
            XName.Get(containerName, propertyElement.NamespaceUri),
            new XElement(parsed)
        );
        return WordStyleGraphBuilder.ReadFormattingProperties(
            container,
            nodeKind == WordSemanticNodeKind.Paragraph
                ? WordStyleGraphBuilder.WordFormattingDomain.Paragraph
                : WordStyleGraphBuilder.WordFormattingDomain.Run
        );
    }

    private LosslessXmlDocument GetSource(
        OpcPackageSnapshot package,
        string partUri,
        IDictionary<string, LosslessXmlDocument> sources,
        IDictionary<string, OpcPart> sourceParts,
        IDictionary<string, List<XmlSourcePatch>> patches,
        CancellationToken cancellationToken
    )
    {
        if (sources.TryGetValue(partUri, out var cached))
        {
            return cached;
        }
        if (!package.Parts.TryGetValue(partUri, out var part))
        {
            throw new WordFormatterPreconditionException(
                $"Semantic source part '{partUri}' is missing."
            );
        }
        try
        {
            var source = LosslessXmlDocument.Parse(
                part.Entry.Content,
                _xmlOptions,
                cancellationToken
            );
            sources.Add(partUri, source);
            sourceParts.Add(partUri, part);
            patches.Add(partUri, []);
            return source;
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordFormatterLimitException(
                $"Semantic source part '{partUri}' exceeds the formatter XML limit.",
                exception
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordFormatterPreconditionException(
                $"Semantic source part '{partUri}' cannot be edited losslessly.",
                exception
            );
        }
    }

    private static XmlSourceElement BoundFormattingOwner(
        LosslessXmlDocument source,
        WordSemanticNode node
    )
    {
        XmlSourceElement element;
        try
        {
            element = source.GetElement(node.SourceElementOrdinal);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new WordFormatterPreconditionException(
                $"Semantic node '{node.Id}' lost its source element.",
                exception
            );
        }
        var expectedName = node.Kind == WordSemanticNodeKind.Paragraph ? "p" : "r";
        if (!IsWordElement(element, expectedName))
        {
            throw new WordFormatterPreconditionException(
                $"Semantic node '{node.Id}' no longer binds to w:{expectedName}."
            );
        }
        return element;
    }

    private static Dictionary<string, WordPackagePartPayload> BuildPayloads(
        IReadOnlyDictionary<string, LosslessXmlDocument> sources,
        IReadOnlyDictionary<string, OpcPart> sourceParts,
        IReadOnlyDictionary<string, List<XmlSourcePatch>> patches,
        CancellationToken cancellationToken
    )
    {
        var payloads = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal);
        foreach (var pair in patches.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pair.Value.Count == 0)
            {
                continue;
            }
            var source = sources[pair.Key];
            var part = sourceParts[pair.Key];
            byte[] changed;
            try
            {
                changed = source.ApplyPatches(
                    pair.Value,
                    part.Entry.Sha256,
                    cancellationToken
                );
            }
            catch (LosslessXmlException exception)
            {
                throw new WordFormatterException(
                    $"Formatter patches for '{part.Uri}' do not form safe XML.",
                    exception
                );
            }
            if (!changed.AsSpan().SequenceEqual(part.Entry.Content.Span))
            {
                payloads.Add(
                    part.Uri,
                    new WordPackagePartPayload(
                        part.Uri,
                        part.Entry.Name,
                        part.Entry.Content.ToArray(),
                        changed
                    )
                );
            }
        }
        return payloads;
    }

    private static HashSet<string> ChangedPartUris(
        OpcPackageSnapshot before,
        OpcPackageSnapshot after
    ) => before.Parts.Keys.Union(after.Parts.Keys, StringComparer.Ordinal)
        .Where(uri =>
            !before.Parts.TryGetValue(uri, out var beforePart)
            || !after.Parts.TryGetValue(uri, out var afterPart)
            || !string.Equals(
                beforePart.Entry.Sha256,
                afterPart.Entry.Sha256,
                StringComparison.OrdinalIgnoreCase
            )
        )
        .ToHashSet(StringComparer.Ordinal);

    private static void AddAffectedNodes(
        WordSemanticNode node,
        ISet<FormattingNodeKey> affected
    )
    {
        affected.Add(FormattingNodeKey.From(node));
        if (node.Kind == WordSemanticNodeKind.Paragraph)
        {
            foreach (var child in node.DescendantsAndSelf().Where(item =>
                item.Kind == WordSemanticNodeKind.Run
            ))
            {
                affected.Add(FormattingNodeKey.From(child));
            }
        }
    }

    private static string SemanticContentFingerprint(WordSemanticDocument document)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, document.MainPartUri);
        foreach (var warning in document.Warnings.Order(StringComparer.Ordinal))
        {
            Append(hash, warning);
        }
        foreach (var node in document.Nodes.OrderBy(node =>
            node.SourcePartUri,
            StringComparer.Ordinal
        ).ThenBy(node => node.SourcePath, StringComparer.Ordinal).ThenBy(node => node.Kind))
        {
            Append(hash, node.SourcePartUri);
            Append(hash, node.SourcePath);
            Append(hash, node.Kind.ToString());
            Append(hash, node.Text ?? string.Empty);
            foreach (var property in node.Properties.OrderBy(
                property => property.Key,
                StringComparer.Ordinal
            ))
            {
                Append(hash, property.Key);
                Append(hash, property.Value);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string FingerprintSourceElement(
        ReadOnlySpan<byte> source,
        XmlSourceSpan span
    ) => "sha256:" + Convert.ToHexString(
        SHA256.HashData(source.Slice(span.ByteOffset, span.ByteLength))
    ).ToLowerInvariant();

    private static string CreatePlanId(
        string packageFingerprint,
        IEnumerable<WordFormatterPolicy> policies,
        IEnumerable<WordFormatterChange> changes
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "wordtoolkit.formatter.plan/1.0");
        Append(hash, packageFingerprint);
        foreach (var policy in policies)
        {
            Append(hash, policy.ToString());
        }
        foreach (var change in changes)
        {
            Append(hash, change.SourcePartUri);
            Append(hash, change.SourceElementOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, change.PropertyElementOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, change.PropertyElementName);
            Append(hash, change.SourceElementFingerprint);
        }
        return "wtfmt_" + Convert.ToHexString(
            hash.GetHashAndReset().AsSpan(0, 15)
        ).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsStructuralProperty(
        WordSemanticNodeKind nodeKind,
        string localName
    ) => (nodeKind == WordSemanticNodeKind.Paragraph
        ? ParagraphStructuralProperties
        : RunStructuralProperties).Contains(localName);

    private static bool IsCompositeProperty(
        WordSemanticNodeKind nodeKind,
        string localName
    ) => (nodeKind == WordSemanticNodeKind.Paragraph
        ? ParagraphCompositeProperties
        : RunCompositeProperties).Contains(localName);

    private static bool IsWordElement(XmlSourceElement element, string localName) =>
        IsWordNamespace(element.NamespaceUri)
        && string.Equals(element.LocalName, localName, StringComparison.Ordinal);

    private static bool IsWordNamespace(string namespaceUri) =>
        string.Equals(namespaceUri, WordTransitionalNamespace, StringComparison.Ordinal)
        || string.Equals(namespaceUri, WordStrictNamespace, StringComparison.Ordinal);

    private static void EnsureCompatibleSnapshot(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        string expectedPackageFingerprint
    )
    {
        if (
            !string.Equals(
                package.Fingerprint,
                expectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.Equals(
                package.Fingerprint,
                semantic.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordFormatterPreconditionException(
                "Package, semantic projection, and expected fingerprint do not match."
            );
        }
    }

    private sealed record FormattingContext(
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic,
        WordStyleGraph Styles,
        WordNumberingGraph Numbering,
        WordThemeGraph Theme,
        WordSettingsGraph Settings,
        WordFontTableGraph Fonts,
        WordEffectiveFormattingResolver Resolver
    );

    private readonly record struct FormattingNodeKey(
        string PartUri,
        string SourcePath,
        WordSemanticNodeKind Kind
    )
    {
        public static FormattingNodeKey From(WordSemanticNode node) => new(
            node.SourcePartUri,
            node.SourcePath,
            node.Kind
        );
    }
}

public class WordFormatterException : InvalidOperationException
{
    public WordFormatterException(string message)
        : base(message)
    {
    }

    public WordFormatterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordFormatterPreconditionException : WordFormatterException
{
    public WordFormatterPreconditionException(string message)
        : base(message)
    {
    }

    public WordFormatterPreconditionException(
        string message,
        Exception innerException
    ) : base(message, innerException)
    {
    }
}

public sealed class WordFormatterLimitException : WordFormatterException
{
    public WordFormatterLimitException(string message)
        : base(message)
    {
    }

    public WordFormatterLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordFormatterValidationException : WordFormatterException
{
    public WordFormatterValidationException(
        string message,
        WordFormatterValidation validation
    ) : base(message)
    {
        Validation = validation;
    }

    public WordFormatterValidationException(
        string message,
        WordFormatterValidation validation,
        Exception innerException
    ) : base(message, innerException)
    {
        Validation = validation;
    }

    public WordFormatterValidation Validation { get; }
}
