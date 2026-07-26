using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordEquationRepairKind
{
    RemoveRedundantDuplicatePropertyContainer,
    RemoveRedundantDuplicateProperty,
}

public sealed record WordEquationRepairCommand(
    WordEquationRepairKind Kind,
    string CandidateId,
    string ExpectedCandidateFingerprint
);

public sealed record WordEquationRepairOptions
{
    public static WordEquationRepairOptions Default { get; } = new();

    public int MaxCandidates { get; init; } = 10_000;

    public int MaxCommands { get; init; } = 32;

    public int MaxXmlPartBytes { get; init; } = 128 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCandidates));
        }
        if (MaxCommands is < 1 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommands));
        }
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
    }
}

public sealed record WordEquationRepairCandidate(
    string Id,
    string Fingerprint,
    WordEquationRepairKind Kind,
    string IssueCode,
    string PartUri,
    int ParentElementOrdinal,
    int RetainedElementOrdinal,
    IReadOnlyList<int> RemovedElementOrdinals,
    string ParentElementName,
    string DuplicateElementName,
    int RemovedXmlElementCount,
    string? EquationId,
    string? NodeId
)
{
    public int RemovedElementCount => RemovedElementOrdinals.Count;
}

public sealed class WordEquationRepairCatalog
{
    private readonly IReadOnlyDictionary<string, WordEquationRepairCandidate> _byId;

    internal WordEquationRepairCatalog(
        string packageFingerprint,
        string mainPartUri,
        WordEquationGraph equationGraph,
        IReadOnlyList<WordEquationRepairCandidate> candidates
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        EquationGraph = equationGraph;
        Candidates = new ReadOnlyCollection<WordEquationRepairCandidate>(
            candidates.ToArray()
        );
        _byId = new ReadOnlyDictionary<string, WordEquationRepairCandidate>(
            candidates.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public WordEquationGraph EquationGraph { get; }

    public IReadOnlyList<WordEquationRepairCandidate> Candidates { get; }

    public bool AnalysisExecutionComplete => true;

    public bool RepairCoverageComplete => !EquationGraph.IssuesTruncated;

    public bool TryGetCandidate(
        string id,
        out WordEquationRepairCandidate? candidate
    ) => _byId.TryGetValue(id, out candidate);
}

public sealed record WordEquationRepairPartChange(
    string PartUri,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed record WordEquationRepairValidation(
    bool CandidatePackageStructurallyValid,
    bool CandidateEquationGraphComplete,
    bool SelectedDuplicateGroupsRemoved,
    bool ExpectedElementCountRemoved,
    bool NormalizedMathSemanticsPreserved,
    bool NoNewEquationIssues,
    bool UnplannedEntriesPreserved,
    bool ExactInverseVerified,
    int RemovedElementCount,
    int BeforeEquationIssueCount,
    int AfterEquationIssueCount,
    int BeforeEquationErrorCount,
    int AfterEquationErrorCount
)
{
    public bool Passed => CandidatePackageStructurallyValid
        && CandidateEquationGraphComplete
        && SelectedDuplicateGroupsRemoved
        && ExpectedElementCountRemoved
        && NormalizedMathSemanticsPreserved
        && NoNewEquationIssues
        && UnplannedEntriesPreserved
        && ExactInverseVerified;
}

public sealed class WordEquationRepairPlan
{
    private readonly WordPackageTransactionCore _transaction;

    internal WordEquationRepairPlan(
        string planId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<WordEquationRepairCandidate> candidates,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts,
        WordEquationRepairValidation validation,
        IReadOnlyList<string> safetyRules
    )
    {
        PlanId = planId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        Candidates = new ReadOnlyCollection<WordEquationRepairCandidate>(
            candidates.ToArray()
        );
        Validation = validation;
        SafetyRules = new ReadOnlyCollection<string>(safetyRules.ToArray());
        _transaction = new WordPackageTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            parts
        );
        ChangedParts = new ReadOnlyCollection<WordEquationRepairPartChange>(
            _transaction.Parts
                .OrderBy(part => part.PartUri, StringComparer.Ordinal)
                .Select(part => new WordEquationRepairPartChange(
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

    public IReadOnlyList<WordEquationRepairCandidate> Candidates { get; }

    public IReadOnlyList<WordEquationRepairPartChange> ChangedParts { get; }

    public WordEquationRepairValidation Validation { get; }

    public IReadOnlyList<string> SafetyRules { get; }

    public bool HasChanges => _transaction.HasChanges;

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot) =>
        _transaction.CreateMutation(currentSnapshot);

    public OpcPackageMutationBuilder CreateInverseMutation(OpcPackageSnapshot appliedSnapshot) =>
        _transaction.CreateInverseMutation(appliedSnapshot);
}

public sealed class WordEquationRepairPlanner
{
    private const string MathTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private static readonly IReadOnlyDictionary<string, string> PropertyContainerByObject =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acc"] = "accPr",
            ["bar"] = "barPr",
            ["borderBox"] = "borderBoxPr",
            ["box"] = "boxPr",
            ["d"] = "dPr",
            ["eqArr"] = "eqArrPr",
            ["f"] = "fPr",
            ["func"] = "funcPr",
            ["groupChr"] = "groupChrPr",
            ["limLow"] = "limLowPr",
            ["limUpp"] = "limUppPr",
            ["m"] = "mPr",
            ["nary"] = "naryPr",
            ["oMathPara"] = "oMathParaPr",
            ["phant"] = "phantPr",
            ["rad"] = "radPr",
            ["r"] = "rPr",
            ["sPre"] = "sPrePr",
            ["sSub"] = "sSubPr",
            ["sSubSup"] = "sSubSupPr",
            ["sSup"] = "sSupPr",
        };

    private static readonly HashSet<string> PropertyContainerNames = new(
        PropertyContainerByObject.Values.Concat(new[] { "argPr", "ctrlPr", "mcPr", "mathPr" }),
        StringComparer.Ordinal
    );

    private readonly WordEquationRepairOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;
    private readonly OpcPackageReader _reader = new();
    private readonly OpcPackageSerializer _serializer = new();

    public WordEquationRepairPlanner(WordEquationRepairOptions? options = null)
    {
        _options = options ?? WordEquationRepairOptions.Default;
        _options.Validate();
        _xmlOptions = LosslessXmlOptions.Default with
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
            MaxTextCharacters = _options.MaxXmlPartBytes,
        };
    }

    public WordEquationRepairCatalog Inspect(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        if (!package.IsStructurallyValid)
        {
            throw new WordSemanticEditException(
                "A structurally invalid OPC package cannot be inspected through the guarded OfficeMath repair operation."
            );
        }

        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        var graph = new WordEquationGraphBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        var partUris = semantic.ProjectedPartUris
            .Concat(graph.Settings is null ? Array.Empty<string>() : new[] { graph.Settings.PartUri })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var candidates = new List<WordEquationRepairCandidate>();
        foreach (var partUri in partUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordSemanticEditException(
                    $"OfficeMath source part '{partUri}' disappeared during inspection."
                );
            }
            var source = ParsePart(part.Entry.Content, partUri, cancellationToken);
            DiscoverCandidates(
                package.Fingerprint,
                partUri,
                source,
                graph,
                candidates,
                cancellationToken
            );
            if (candidates.Count > _options.MaxCandidates)
            {
                throw new WordEquationLimitException(
                    $"Document contains more than {_options.MaxCandidates} OfficeMath repair candidates."
                );
            }
        }

        return new WordEquationRepairCatalog(
            package.Fingerprint,
            semantic.MainPartUri,
            graph,
            candidates
                .OrderBy(candidate => candidate.PartUri, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.ParentElementOrdinal)
                .ThenBy(candidate => candidate.RetainedElementOrdinal)
                .ToArray()
        );
    }

    public WordEquationRepairPlan Plan(
        OpcPackageSnapshot package,
        IReadOnlyList<WordEquationRepairCommand> commands,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(commands);
        ValidateCommands(commands);
        var before = Inspect(package, cancellationToken);
        if (!before.AnalysisExecutionComplete || !before.RepairCoverageComplete)
        {
            throw new WordSemanticEditException(
                "OfficeMath repair requires a complete candidate scan and an untruncated equation issue graph."
            );
        }

        var selected = new List<WordEquationRepairCandidate>(commands.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(command.CandidateId))
            {
                throw new ArgumentException("OfficeMath repair candidate IDs must be unique.");
            }
            if (!before.TryGetCandidate(command.CandidateId, out var candidate)
                || candidate is null)
            {
                throw new WordSemanticPreconditionException(
                    $"OfficeMath repair candidate '{command.CandidateId}' disappeared after inspection."
                );
            }
            if (candidate.Kind != command.Kind)
            {
                throw new WordSemanticPreconditionException(
                    $"OfficeMath repair candidate '{command.CandidateId}' changed kind after inspection."
                );
            }
            if (!string.Equals(
                    candidate.Fingerprint,
                    command.ExpectedCandidateFingerprint,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                throw new WordSemanticPreconditionException(
                    $"OfficeMath repair candidate '{command.CandidateId}' changed after inspection."
                );
            }
            selected.Add(candidate);
        }

        var payloads = BuildPayloads(package, selected, cancellationToken);
        var projected = payloads.Values.ToDictionary(
            payload => payload.EntryName,
            payload => (ReadOnlyMemory<byte>)payload.AfterContent,
            StringComparer.Ordinal
        );
        var resultFingerprint = OpcPackageFingerprint.ComputeProjected(package, projected);
        var transaction = new WordPackageTransactionCore(
            package.Fingerprint,
            resultFingerprint,
            payloads
        );
        var candidatePackage = Materialize(
            package,
            transaction.CreateMutation(package),
            cancellationToken
        );
        if (!string.Equals(
                candidatePackage.Fingerprint,
                resultFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordSemanticEditException(
                "The OfficeMath repair candidate does not match its predicted package fingerprint."
            );
        }
        var after = Inspect(candidatePackage, cancellationToken);
        var validation = ValidateCandidate(
            package,
            candidatePackage,
            transaction,
            before,
            after,
            selected,
            cancellationToken
        );
        if (!validation.Passed)
        {
            throw new WordSemanticEditException(
                "The OfficeMath repair candidate failed structural or semantic validation: "
                    + validation
            );
        }

        return new WordEquationRepairPlan(
            CreatePlanId(package.Fingerprint, resultFingerprint, commands),
            package.Fingerprint,
            resultFingerprint,
            selected,
            payloads,
            validation,
            [
                "exact_package_and_candidate_fingerprints_required",
                "only_canonically_identical_later_duplicates_are_removed",
                "ambiguous_or_non_equivalent_math_is_never_rewritten",
                "normalized_math_semantics_must_be_identical",
                "candidate_reprojected_before_apply",
                "no_new_equation_issue_allowed",
                "exact_inverse_verified",
            ]
        );
    }

    internal string NormalizedPartFingerprint(
        ReadOnlyMemory<byte> content,
        string partUri,
        CancellationToken cancellationToken = default
    )
    {
        var source = ParsePart(content, partUri, cancellationToken);
        var ignored = RedundantDuplicateElements(source, cancellationToken);
        var builder = new StringBuilder();
        AppendCanonicalNode(builder, source.ParsedDocument, ignored);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private IReadOnlyDictionary<string, WordPackagePartPayload> BuildPayloads(
        OpcPackageSnapshot package,
        IReadOnlyList<WordEquationRepairCandidate> selected,
        CancellationToken cancellationToken
    )
    {
        var result = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal);
        foreach (var group in selected.GroupBy(candidate => candidate.PartUri, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(group.Key, out var part))
            {
                throw new WordSemanticPreconditionException(
                    $"OfficeMath source part '{group.Key}' disappeared after inspection."
                );
            }
            var source = ParsePart(part.Entry.Content, group.Key, cancellationToken);
            var removalOrdinals = group.SelectMany(candidate => candidate.RemovedElementOrdinals)
                .ToArray();
            if (removalOrdinals.Distinct().Count() != removalOrdinals.Length)
            {
                throw new WordSemanticEditException(
                    "Selected OfficeMath repair candidates contain overlapping removals."
                );
            }
            var patches = removalOrdinals
                .Select(source.CreateElementRemovalPatch)
                .ToArray();
            var afterContent = source.ApplyPatches(
                patches,
                part.Entry.Sha256,
                cancellationToken
            );
            result.Add(
                group.Key,
                new WordPackagePartPayload(
                    group.Key,
                    part.Entry.Name,
                    part.Entry.Content.ToArray(),
                    afterContent
                )
            );
        }
        return result;
    }

    private WordEquationRepairValidation ValidateCandidate(
        OpcPackageSnapshot package,
        OpcPackageSnapshot candidate,
        WordPackageTransactionCore transaction,
        WordEquationRepairCatalog before,
        WordEquationRepairCatalog after,
        IReadOnlyList<WordEquationRepairCandidate> selected,
        CancellationToken cancellationToken
    )
    {
        var targetedByCode = selected
            .GroupBy(item => item.IssueCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var beforeIssues = IssueCounts(before.EquationGraph.Issues);
        var afterIssues = IssueCounts(after.EquationGraph.Issues);
        var selectedGroupsRemoved = targetedByCode.All(pair =>
            beforeIssues.TryGetValue(pair.Key, out var beforeCount)
            && afterIssues.GetValueOrDefault(pair.Key) <= beforeCount - pair.Value
        );
        var beforeIssueIdentities = IssueIdentityCounts(before.EquationGraph.Issues);
        var afterIssueIdentities = IssueIdentityCounts(after.EquationGraph.Issues);
        var noNewIssues = afterIssueIdentities.All(pair =>
            beforeIssueIdentities.TryGetValue(pair.Key, out var count)
                && pair.Value <= count
        );
        var changedPartUris = selected.Select(item => item.PartUri)
            .ToHashSet(StringComparer.Ordinal);
        var normalizedPreserved = changedPartUris.All(partUri =>
            package.Parts.TryGetValue(partUri, out var beforePart)
            && candidate.Parts.TryGetValue(partUri, out var afterPart)
            && string.Equals(
                NormalizedPartFingerprint(beforePart.Entry.Content, partUri, cancellationToken),
                NormalizedPartFingerprint(afterPart.Entry.Content, partUri, cancellationToken),
                StringComparison.Ordinal
            )
        );
        var expectedRemoved = selected.Sum(item => item.RemovedXmlElementCount);
        var actualRemoved = changedPartUris.Sum(partUri =>
        {
            var beforePart = package.Parts[partUri];
            var afterPart = candidate.Parts[partUri];
            var beforeSource = ParsePart(beforePart.Entry.Content, partUri, cancellationToken);
            var afterSource = ParsePart(afterPart.Entry.Content, partUri, cancellationToken);
            return beforeSource.ParsedDocument.Descendants().Count()
                - afterSource.ParsedDocument.Descendants().Count();
        });
        var inverse = Materialize(
            candidate,
            transaction.CreateInverseMutation(candidate),
            cancellationToken
        );
        return new WordEquationRepairValidation(
            CandidatePackageStructurallyValid: candidate.IsStructurallyValid,
            CandidateEquationGraphComplete: after.AnalysisExecutionComplete
                && after.RepairCoverageComplete,
            SelectedDuplicateGroupsRemoved: selectedGroupsRemoved,
            ExpectedElementCountRemoved: actualRemoved == expectedRemoved,
            NormalizedMathSemanticsPreserved: normalizedPreserved,
            NoNewEquationIssues: noNewIssues,
            UnplannedEntriesPreserved: UnplannedEntriesPreserved(
                package,
                candidate,
                changedPartUris
            ),
            ExactInverseVerified: string.Equals(
                inverse.Fingerprint,
                package.Fingerprint,
                StringComparison.Ordinal
            ),
            RemovedElementCount: actualRemoved,
            BeforeEquationIssueCount: before.EquationGraph.Issues.Count,
            AfterEquationIssueCount: after.EquationGraph.Issues.Count,
            BeforeEquationErrorCount: before.EquationGraph.Issues.Count(issue =>
                issue.Severity == WordEquationIssueSeverity.Error
            ),
            AfterEquationErrorCount: after.EquationGraph.Issues.Count(issue =>
                issue.Severity == WordEquationIssueSeverity.Error
            )
        );
    }

    private void DiscoverCandidates(
        string packageFingerprint,
        string partUri,
        LosslessXmlDocument source,
        WordEquationGraph graph,
        ICollection<WordEquationRepairCandidate> candidates,
        CancellationToken cancellationToken
    )
    {
        var root = source.ParsedDocument.Root
            ?? throw new WordSemanticEditException(
                $"OfficeMath source part '{partUri}' has no XML root."
            );
        var redundantContainers = new HashSet<XElement>(ReferenceEqualityComparer.Instance);
        foreach (var parent in root.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryPropertyContainerName(parent, out var propertyContainerName))
            {
                continue;
            }
            var group = parent.Elements()
                .Where(child => IsMathElement(child, propertyContainerName))
                .ToArray();
            if (group.Length <= 1 || !AllCanonicallyEqual(group))
            {
                continue;
            }
            foreach (var duplicate in group.Skip(1))
            {
                redundantContainers.Add(duplicate);
            }
            var issueCode = IssueCodeForContainer(parent);
            if (!HasMatchingIssue(
                    graph,
                    issueCode,
                    partUri,
                    source.GetElementOrdinal(parent),
                    group.Skip(1).Select(source.GetElementOrdinal)
                ))
            {
                continue;
            }
            AddCandidate(
                packageFingerprint,
                partUri,
                source,
                graph,
                candidates,
                WordEquationRepairKind.RemoveRedundantDuplicatePropertyContainer,
                issueCode,
                parent,
                group[0],
                group.Skip(1).ToArray()
            );
        }

        foreach (var container in root.DescendantsAndSelf().Where(IsPropertyContainer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (redundantContainers.Contains(container)
                || container.Ancestors().Any(redundantContainers.Contains))
            {
                continue;
            }
            foreach (var group in container.Elements()
                .Where(IsMathNamespaceElement)
                .GroupBy(child => child.Name)
                .Where(group => group.Count() > 1))
            {
                var elements = group.ToArray();
                if (!AllCanonicallyEqual(elements))
                {
                    continue;
                }
                if (!HasMatchingIssue(
                        graph,
                        "MATH_PROPERTY_DUPLICATE",
                        partUri,
                        source.GetElementOrdinal(container),
                        elements.Skip(1).Select(source.GetElementOrdinal)
                    ))
                {
                    continue;
                }
                AddCandidate(
                    packageFingerprint,
                    partUri,
                    source,
                    graph,
                    candidates,
                    WordEquationRepairKind.RemoveRedundantDuplicateProperty,
                    "MATH_PROPERTY_DUPLICATE",
                    container,
                    elements[0],
                    elements.Skip(1).ToArray()
                );
            }
        }
    }

    private static void AddCandidate(
        string packageFingerprint,
        string partUri,
        LosslessXmlDocument source,
        WordEquationGraph graph,
        ICollection<WordEquationRepairCandidate> candidates,
        WordEquationRepairKind kind,
        string issueCode,
        XElement parent,
        XElement retained,
        IReadOnlyList<XElement> removed
    )
    {
        var parentOrdinal = source.GetElementOrdinal(parent);
        var retainedOrdinal = source.GetElementOrdinal(retained);
        var removedOrdinals = removed.Select(source.GetElementOrdinal).ToArray();
        var canonical = CanonicalElement(retained);
        var fingerprint = CandidateFingerprint(
            kind,
            partUri,
            parentOrdinal,
            retainedOrdinal,
            removedOrdinals,
            canonical
        );
        var id = CandidateId(packageFingerprint, fingerprint);
        var equationElement = parent.AncestorsAndSelf()
            .FirstOrDefault(element => IsMathElement(element, "oMath"))
            ?? parent.Descendants()
                .Where(element => IsMathElement(element, "oMath"))
                .Take(2)
                .SingleOrDefault();
        WordEquationDefinition? equation = null;
        if (equationElement is not null)
        {
            var equationOrdinal = source.GetElementOrdinal(equationElement);
            equation = graph.Equations.SingleOrDefault(item =>
                string.Equals(item.PartUri, partUri, StringComparison.Ordinal)
                && item.SourceElementOrdinal == equationOrdinal
            );
        }
        var node = equation?.Root.DescendantsAndSelf()
            .FirstOrDefault(item => item.SourceElementOrdinal == parentOrdinal);
        candidates.Add(new WordEquationRepairCandidate(
            id,
            fingerprint,
            kind,
            issueCode,
            partUri,
            parentOrdinal,
            retainedOrdinal,
            new ReadOnlyCollection<int>(removedOrdinals),
            QualifiedName(parent),
            QualifiedName(retained),
            removed.Sum(element => element.DescendantsAndSelf().Count()),
            equation?.Id,
            node?.Id
        ));
    }

    private HashSet<XElement> RedundantDuplicateElements(
        LosslessXmlDocument source,
        CancellationToken cancellationToken
    )
    {
        var root = source.ParsedDocument.Root
            ?? throw new WordSemanticEditException("OfficeMath source XML has no root.");
        var ignored = new HashSet<XElement>(ReferenceEqualityComparer.Instance);
        foreach (var parent in root.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryPropertyContainerName(parent, out var propertyContainerName))
            {
                continue;
            }
            var group = parent.Elements()
                .Where(child => IsMathElement(child, propertyContainerName))
                .ToArray();
            if (group.Length > 1 && AllCanonicallyEqual(group))
            {
                foreach (var duplicate in group.Skip(1))
                {
                    ignored.Add(duplicate);
                }
            }
        }
        foreach (var container in root.DescendantsAndSelf().Where(IsPropertyContainer))
        {
            if (ignored.Contains(container) || container.Ancestors().Any(ignored.Contains))
            {
                continue;
            }
            foreach (var group in container.Elements()
                .Where(IsMathNamespaceElement)
                .GroupBy(child => child.Name)
                .Where(group => group.Count() > 1))
            {
                var elements = group.ToArray();
                if (AllCanonicallyEqual(elements))
                {
                    foreach (var duplicate in elements.Skip(1))
                    {
                        ignored.Add(duplicate);
                    }
                }
            }
        }
        return ignored;
    }

    private static bool TryPropertyContainerName(XElement parent, out string name)
    {
        if (IsMathNamespaceElement(parent)
            && PropertyContainerByObject.TryGetValue(parent.Name.LocalName, out name!))
        {
            return true;
        }
        if (IsWordElement(parent, "settings"))
        {
            name = "mathPr";
            return true;
        }
        name = string.Empty;
        return false;
    }

    private static string IssueCodeForContainer(XElement parent)
    {
        if (IsWordElement(parent, "settings"))
        {
            return "MATH_SETTINGS_DUPLICATE";
        }
        return parent.Name.LocalName switch
        {
            "oMathPara" => "MATH_PARAGRAPH_PROPERTIES_DUPLICATE",
            "r" => "MATH_RUN_PROPERTIES_DUPLICATE",
            _ => "MATH_PROPERTIES_DUPLICATE",
        };
    }

    private static bool HasMatchingIssue(
        WordEquationGraph graph,
        string issueCode,
        string partUri,
        int parentOrdinal,
        IEnumerable<int> removedOrdinals
    )
    {
        var ordinals = removedOrdinals.Append(parentOrdinal).ToHashSet();
        return graph.Issues.Any(issue =>
            string.Equals(issue.Code, issueCode, StringComparison.Ordinal)
            && string.Equals(issue.PartUri, partUri, StringComparison.Ordinal)
            && issue.SourceElementOrdinal is { } ordinal
            && ordinals.Contains(ordinal)
        );
    }

    private static bool IsPropertyContainer(XElement element) =>
        IsMathNamespaceElement(element)
        && PropertyContainerNames.Contains(element.Name.LocalName);

    private static bool IsMathElement(XElement element, string localName) =>
        IsMathNamespaceElement(element)
        && element.Name.LocalName == localName;

    private static bool IsMathNamespaceElement(XElement element) =>
        element.Name.NamespaceName is MathTransitionalNamespace or MathStrictNamespace;

    private static bool IsWordElement(XElement element, string localName) =>
        element.Name.LocalName == localName
        && element.Name.NamespaceName is WordTransitionalNamespace or WordStrictNamespace;

    private static bool AllCanonicallyEqual(IReadOnlyList<XElement> elements)
    {
        var first = CanonicalElement(elements[0]);
        return elements.Skip(1).All(element =>
            string.Equals(CanonicalElement(element), first, StringComparison.Ordinal)
        );
    }

    private static string CanonicalElement(XElement element)
    {
        var builder = new StringBuilder();
        AppendCanonicalElement(builder, element, null);
        return builder.ToString();
    }

    private static void AppendCanonicalNode(
        StringBuilder builder,
        XDocument document,
        ISet<XElement>? ignored
    )
    {
        builder.Append("document[");
        AppendCanonicalNodes(builder, document.Nodes(), ignored);
        builder.Append(']');
    }

    private static void AppendCanonicalNodes(
        StringBuilder builder,
        IEnumerable<XNode> nodes,
        ISet<XElement>? ignored
    )
    {
        StringBuilder? text = null;
        foreach (var node in nodes)
        {
            if (node is XElement ignoredElement
                && ignored?.Contains(ignoredElement) == true)
            {
                continue;
            }
            if (node is XText textNode)
            {
                (text ??= new StringBuilder()).Append(textNode.Value);
                continue;
            }
            if (text is not null)
            {
                AppendCanonicalValue(builder, "#text");
                AppendCanonicalValue(builder, text.ToString());
                text = null;
            }
            AppendCanonicalNode(builder, node, ignored);
        }
        if (text is not null)
        {
            AppendCanonicalValue(builder, "#text");
            AppendCanonicalValue(builder, text.ToString());
        }
    }

    private static void AppendCanonicalNode(
        StringBuilder builder,
        XNode node,
        ISet<XElement>? ignored
    )
    {
        switch (node)
        {
            case XElement element when ignored?.Contains(element) != true:
                AppendCanonicalElement(builder, element, ignored);
                break;
            case XText text:
                AppendCanonicalValue(builder, "#text");
                AppendCanonicalValue(builder, text.Value);
                break;
            case XComment comment:
                AppendCanonicalValue(builder, "#comment");
                AppendCanonicalValue(builder, comment.Value);
                break;
            case XProcessingInstruction instruction:
                AppendCanonicalValue(builder, "#processing-instruction");
                AppendCanonicalValue(builder, instruction.Target);
                AppendCanonicalValue(builder, instruction.Data);
                break;
            case XDocumentType documentType:
                AppendCanonicalValue(builder, "#doctype");
                AppendCanonicalValue(builder, documentType.Name);
                AppendCanonicalValue(builder, documentType.PublicId ?? string.Empty);
                AppendCanonicalValue(builder, documentType.SystemId ?? string.Empty);
                AppendCanonicalValue(builder, documentType.InternalSubset ?? string.Empty);
                break;
        }
    }

    private static void AppendCanonicalElement(
        StringBuilder builder,
        XElement element,
        ISet<XElement>? ignored
    )
    {
        AppendCanonicalValue(builder, element.Name.NamespaceName);
        AppendCanonicalValue(builder, element.Name.LocalName);
        foreach (var attribute in element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .OrderBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal))
        {
            AppendCanonicalValue(builder, attribute.Name.NamespaceName);
            AppendCanonicalValue(builder, attribute.Name.LocalName);
            AppendCanonicalValue(builder, attribute.Value);
        }
        builder.Append('[');
        AppendCanonicalNodes(builder, element.Nodes(), ignored);
        builder.Append(']');
    }

    private static void AppendCanonicalValue(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);

    private static string QualifiedName(XElement element) =>
        IsMathNamespaceElement(element)
            ? "m:" + element.Name.LocalName
            : IsWordElement(element, element.Name.LocalName)
                ? "w:" + element.Name.LocalName
                : element.Name.ToString();

    private static string CandidateFingerprint(
        WordEquationRepairKind kind,
        string partUri,
        int parentOrdinal,
        int retainedOrdinal,
        IReadOnlyList<int> removedOrdinals,
        string canonical
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-equation-repair-candidate-v1");
        AppendHash(hash, kind.ToString());
        AppendHash(hash, partUri);
        AppendHash(hash, parentOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendHash(hash, retainedOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var ordinal in removedOrdinals)
        {
            AppendHash(hash, ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        AppendHash(hash, canonical);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string CandidateId(string packageFingerprint, string candidateFingerprint)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-equation-repair-candidate-id-v1");
        AppendHash(hash, packageFingerprint);
        AppendHash(hash, candidateFingerprint);
        return "wder_" + Convert.ToHexString(hash.GetHashAndReset().AsSpan(0, 12))
            .ToLowerInvariant();
    }

    private static string CreatePlanId(
        string baseFingerprint,
        string resultFingerprint,
        IReadOnlyList<WordEquationRepairCommand> commands
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-equation-repair-plan-v1");
        AppendHash(hash, baseFingerprint);
        AppendHash(hash, resultFingerprint);
        foreach (var command in commands.OrderBy(item => item.CandidateId, StringComparer.Ordinal))
        {
            AppendHash(hash, command.Kind.ToString());
            AppendHash(hash, command.CandidateId);
            AppendHash(hash, command.ExpectedCandidateFingerprint.ToLowerInvariant());
        }
        return "werplan_" + Convert.ToBase64String(hash.GetHashAndReset().AsSpan(0, 18))
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

    private static Dictionary<string, int> IssueCounts(
        IEnumerable<WordEquationIssue> issues
    ) => issues.GroupBy(issue => issue.Code, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static Dictionary<string, int> IssueIdentityCounts(
        IEnumerable<WordEquationIssue> issues
    ) => issues.GroupBy(
            issue => issue.Severity + "\u001f" + issue.Code,
            StringComparer.Ordinal
        )
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static bool UnplannedEntriesPreserved(
        OpcPackageSnapshot before,
        OpcPackageSnapshot after,
        ISet<string> changedPartUris
    )
    {
        var beforeEntries = before.Entries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        var afterEntries = after.Entries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        if (!beforeEntries.Keys.Order().SequenceEqual(afterEntries.Keys.Order()))
        {
            return false;
        }
        foreach (var pair in beforeEntries)
        {
            if (pair.Value.PartUri is { } partUri && changedPartUris.Contains(partUri))
            {
                continue;
            }
            if (!string.Equals(
                    pair.Value.Sha256,
                    afterEntries[pair.Key].Sha256,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return false;
            }
        }
        return true;
    }

    private void ValidateCommands(IReadOnlyList<WordEquationRepairCommand> commands)
    {
        if (commands.Count is < 1 || commands.Count > _options.MaxCommands)
        {
            throw new ArgumentException(
                $"OfficeMath repair requires between 1 and {_options.MaxCommands} commands."
            );
        }
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (command.CandidateId.Length != 29
                || !command.CandidateId.StartsWith("wder_", StringComparison.Ordinal)
                || !command.CandidateId[5..].All(character =>
                    character is >= '0' and <= '9' or >= 'a' and <= 'f'
                ))
            {
                throw new ArgumentException("A valid OfficeMath repair candidate ID is required.");
            }
            if (command.ExpectedCandidateFingerprint.Length != 64
                || !command.ExpectedCandidateFingerprint.All(Uri.IsHexDigit))
            {
                throw new ArgumentException(
                    "The expected OfficeMath repair candidate fingerprint must be exactly 64 hexadecimal characters."
                );
            }
        }
    }

    private LosslessXmlDocument ParsePart(
        ReadOnlyMemory<byte> content,
        string partUri,
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
                $"OfficeMath source part '{partUri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private OpcPackageSnapshot Materialize(
        OpcPackageSnapshot package,
        OpcPackageMutationBuilder mutation,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream();
        _serializer.Write(stream, mutation);
        stream.Position = 0;
        return _reader.Read(stream, cancellationToken);
    }

}
