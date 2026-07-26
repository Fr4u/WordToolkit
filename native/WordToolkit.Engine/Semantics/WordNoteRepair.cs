using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordNoteRepairKind
{
    RemoveEmptyOrphanDefinition,
    RemoveRedundantDuplicateDefinition,
}

public sealed record WordNoteRepairCommand(
    WordNoteRepairKind Kind,
    string DefinitionId,
    string ExpectedDefinitionFingerprint
);

public sealed record WordNoteRepairOptions
{
    public static WordNoteRepairOptions Default { get; } = new();

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
    }
}

public sealed record WordNoteRepairPartChange(
    string PartUri,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed record WordNoteRepairValidation(
    bool CandidatePackageStructurallyValid,
    bool CandidateNoteGraphComplete,
    bool TargetDefinitionRemoved,
    bool UntargetedDefinitionsPreserved,
    bool ReferenceMarkupPreserved,
    bool SpecialReferenceMarkupPreserved,
    bool NumberingPoliciesPreserved,
    bool NoNewNoteIssues,
    bool UnplannedEntriesPreserved,
    bool ExactInverseVerified,
    int BeforeNoteIssueCount,
    int AfterNoteIssueCount,
    int BeforeNoteErrorCount,
    int AfterNoteErrorCount
)
{
    public bool Passed => CandidatePackageStructurallyValid
        && CandidateNoteGraphComplete
        && TargetDefinitionRemoved
        && UntargetedDefinitionsPreserved
        && ReferenceMarkupPreserved
        && SpecialReferenceMarkupPreserved
        && NumberingPoliciesPreserved
        && NoNewNoteIssues
        && UnplannedEntriesPreserved
        && ExactInverseVerified;
}

public sealed class WordNoteRepairPlan
{
    private readonly WordPackageTransactionCore _transaction;

    internal WordNoteRepairPlan(
        string planId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        WordNoteRepairKind repairKind,
        WordNoteDefinition targetDefinition,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts,
        WordNoteRepairValidation validation,
        IReadOnlyList<string> safetyRules
    )
    {
        PlanId = planId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        RepairKind = repairKind;
        TargetDefinition = targetDefinition;
        Validation = validation;
        SafetyRules = new ReadOnlyCollection<string>(safetyRules.ToArray());
        _transaction = new WordPackageTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            parts
        );
        ChangedParts = new ReadOnlyCollection<WordNoteRepairPartChange>(
            _transaction.Parts
                .OrderBy(part => part.PartUri, StringComparer.Ordinal)
                .Select(part => new WordNoteRepairPartChange(
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

    public WordNoteRepairKind RepairKind { get; }

    public WordNoteDefinition TargetDefinition { get; }

    public IReadOnlyList<WordNoteRepairPartChange> ChangedParts { get; }

    public WordNoteRepairValidation Validation { get; }

    public IReadOnlyList<string> SafetyRules { get; }

    public bool HasChanges => _transaction.HasChanges;

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot) =>
        _transaction.CreateMutation(currentSnapshot);

    public OpcPackageMutationBuilder CreateInverseMutation(OpcPackageSnapshot appliedSnapshot) =>
        _transaction.CreateInverseMutation(appliedSnapshot);
}

public sealed class WordNoteRepairPlanner
{
    private readonly WordNoteRepairOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;
    private readonly OpcPackageReader _reader = new();
    private readonly OpcPackageSerializer _serializer = new();

    public WordNoteRepairPlanner(WordNoteRepairOptions? options = null)
    {
        _options = options ?? WordNoteRepairOptions.Default;
        _options.Validate();
        _xmlOptions = LosslessXmlOptions.Default with
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
            MaxTextCharacters = _options.MaxXmlPartBytes,
        };
    }

    public WordNoteRepairPlan Plan(
        OpcPackageSnapshot package,
        WordNoteRepairCommand command,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCommand(command);
        if (!package.IsStructurallyValid)
        {
            throw new WordSemanticEditException(
                "A structurally invalid OPC package cannot be repaired through the guarded note operation."
            );
        }

        var before = new WordNoteGraphBuilder(new WordNoteGraphOptions
        {
            MaxXmlPartBytes = _options.MaxXmlPartBytes,
        }).Build(package, cancellationToken: cancellationToken);
        if (!before.AnalysisExecutionComplete || !before.DocumentCoverageComplete)
        {
            throw new WordSemanticEditException(
                "Note repair requires a complete note graph; the package contains unresolved or ambiguous note sources."
            );
        }
        var target = before.Definitions.SingleOrDefault(definition =>
            string.Equals(definition.Id, command.DefinitionId, StringComparison.Ordinal)
        ) ?? throw new WordSemanticPreconditionException(
            $"Note definition '{command.DefinitionId}' disappeared after inspection."
        );
        if (!string.Equals(
                target.Fingerprint,
                command.ExpectedDefinitionFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordSemanticPreconditionException(
                $"Note definition '{command.DefinitionId}' changed after inspection."
            );
        }
        EnsureCandidateMatchesCommand(target, command.Kind);

        if (!package.Parts.TryGetValue(target.PartUri, out var part))
        {
            throw new WordSemanticPreconditionException(
                $"Note source part '{target.PartUri}' disappeared after inspection."
            );
        }
        var source = ParsePart(part.Entry.Content, target.PartUri, cancellationToken);
        var element = source.GetParsedElement(target.SourceElementOrdinal);
        if (!IsExpectedDefinition(element, target))
        {
            throw new WordSemanticPreconditionException(
                $"Note definition '{command.DefinitionId}' no longer resolves to its inspected XML element."
            );
        }
        var afterContent = source.ApplyPatches(
            [source.CreateElementRemovalPatch(target.SourceElementOrdinal)],
            part.Entry.Sha256,
            cancellationToken
        );
        var payload = new WordPackagePartPayload(
            target.PartUri,
            part.Entry.Name,
            part.Entry.Content.ToArray(),
            afterContent
        );
        var payloads = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal)
        {
            [target.PartUri] = payload,
        };
        var projected = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
        {
            [part.Entry.Name] = afterContent,
        };
        var resultFingerprint = OpcPackageFingerprint.ComputeProjected(package, projected);
        var transaction = new WordPackageTransactionCore(
            package.Fingerprint,
            resultFingerprint,
            payloads
        );
        var candidate = Materialize(package, transaction.CreateMutation(package), cancellationToken);
        if (!string.Equals(candidate.Fingerprint, resultFingerprint, StringComparison.Ordinal))
        {
            throw new WordSemanticEditException(
                "The note repair candidate does not match its predicted package fingerprint."
            );
        }
        var after = new WordNoteGraphBuilder(new WordNoteGraphOptions
        {
            MaxXmlPartBytes = _options.MaxXmlPartBytes,
        }).Build(candidate, cancellationToken: cancellationToken);
        var validation = ValidateCandidate(
            package,
            candidate,
            transaction,
            before,
            after,
            target,
            cancellationToken
        );
        if (!validation.Passed)
        {
            throw new WordSemanticEditException(
                "The note repair candidate failed structural or semantic validation."
            );
        }

        return new WordNoteRepairPlan(
            CreatePlanId(package.Fingerprint, resultFingerprint, command),
            package.Fingerprint,
            resultFingerprint,
            command.Kind,
            target,
            payloads,
            validation,
            [
                "source_definition_fingerprint_required",
                "only_explicit_removal_candidates_supported",
                "note_content_is_never_synthesized",
                "all_reference_markup_and_numbering_policies_preserved",
                "candidate_reprojected_before_apply",
                "exact_inverse_verified",
            ]
        );
    }

    private static void ValidateCommand(WordNoteRepairCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ExpectedDefinitionFingerprint);
        if (command.DefinitionId.Length != 28
            || !command.DefinitionId.StartsWith("wnd_", StringComparison.Ordinal)
            || !command.DefinitionId[4..].All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'
            ))
        {
            throw new ArgumentException("A valid note definition ID is required.");
        }
        if (command.ExpectedDefinitionFingerprint.Length != 64
            || !command.ExpectedDefinitionFingerprint.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "The expected note definition fingerprint must be exactly 64 hexadecimal characters."
            );
        }
    }

    private static void EnsureCandidateMatchesCommand(
        WordNoteDefinition target,
        WordNoteRepairKind repairKind
    )
    {
        var allowed = repairKind switch
        {
            WordNoteRepairKind.RemoveEmptyOrphanDefinition =>
                target.EmptyOrphanRemovalCandidate,
            WordNoteRepairKind.RemoveRedundantDuplicateDefinition =>
                target.RedundantDuplicateRemovalCandidate,
            _ => false,
        };
        if (!allowed)
        {
            throw new WordSemanticEditException(
                $"Note definition '{target.Id}' is not an eligible '{repairKind}' candidate."
            );
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
                $"Note source part '{partUri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private static bool IsExpectedDefinition(XElement element, WordNoteDefinition target)
    {
        var expectedName = target.Kind == WordNoteKind.Footnote ? "footnote" : "endnote";
        var rawId = element.Attributes().SingleOrDefault(attribute =>
            !attribute.IsNamespaceDeclaration
            && attribute.Name.LocalName == "id"
            && attribute.Name.NamespaceName is WordPackageConformance.TransitionalWordNamespace
                or WordPackageConformance.StrictWordNamespace
        )?.Value;
        return element.Name.LocalName == expectedName
            && element.Name.NamespaceName is WordPackageConformance.TransitionalWordNamespace
                or WordPackageConformance.StrictWordNamespace
            && string.Equals(rawId, target.RawOoxmlId, StringComparison.Ordinal);
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

    private WordNoteRepairValidation ValidateCandidate(
        OpcPackageSnapshot package,
        OpcPackageSnapshot candidate,
        WordPackageTransactionCore transaction,
        WordNoteGraph before,
        WordNoteGraph after,
        WordNoteDefinition target,
        CancellationToken cancellationToken
    )
    {
        var beforeDefinitions = before.Definitions
            .Where(definition => definition.Id != target.Id)
            .Select(DefinitionIdentity)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var afterDefinitions = after.Definitions
            .Select(DefinitionIdentity)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var beforeIssues = before.Issues
            .GroupBy(IssueIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var afterIssues = after.Issues
            .GroupBy(IssueIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var noNewIssues = afterIssues.All(pair =>
            beforeIssues.TryGetValue(pair.Key, out var count) && pair.Value <= count
        );

        var inverse = Materialize(
            candidate,
            transaction.CreateInverseMutation(candidate),
            cancellationToken
        );
        return new WordNoteRepairValidation(
            CandidatePackageStructurallyValid: candidate.IsStructurallyValid,
            CandidateNoteGraphComplete: after.AnalysisExecutionComplete
                && after.DocumentCoverageComplete,
            TargetDefinitionRemoved: after.Definitions.Count == before.Definitions.Count - 1,
            UntargetedDefinitionsPreserved: beforeDefinitions.SequenceEqual(afterDefinitions),
            ReferenceMarkupPreserved: MultisetEqual(
                before.References.Select(ReferenceIdentity),
                after.References.Select(ReferenceIdentity)
            ),
            SpecialReferenceMarkupPreserved: MultisetEqual(
                before.SpecialReferences.Select(SpecialReferenceIdentity),
                after.SpecialReferences.Select(SpecialReferenceIdentity)
            ),
            NumberingPoliciesPreserved: MultisetEqual(
                before.NumberingPolicies.Select(PolicyIdentity),
                after.NumberingPolicies.Select(PolicyIdentity)
            ),
            NoNewNoteIssues: noNewIssues,
            UnplannedEntriesPreserved: UnplannedEntriesPreserved(
                package,
                candidate,
                target.PartUri
            ),
            ExactInverseVerified: string.Equals(
                inverse.Fingerprint,
                package.Fingerprint,
                StringComparison.Ordinal
            ),
            BeforeNoteIssueCount: before.Issues.Count,
            AfterNoteIssueCount: after.Issues.Count,
            BeforeNoteErrorCount: before.Issues.Count(issue =>
                issue.Severity == WordNoteIssueSeverity.Error
            ),
            AfterNoteErrorCount: after.Issues.Count(issue =>
                issue.Severity == WordNoteIssueSeverity.Error
            )
        );
    }

    private static bool UnplannedEntriesPreserved(
        OpcPackageSnapshot before,
        OpcPackageSnapshot after,
        string changedPartUri
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
            if (string.Equals(pair.Value.PartUri, changedPartUri, StringComparison.Ordinal))
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

    private static bool MultisetEqual(IEnumerable<string> before, IEnumerable<string> after) =>
        before.OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(after.OrderBy(value => value, StringComparer.Ordinal));

    private static string IssueIdentity(WordNoteIssue issue) =>
        issue.Severity + "\u001f" + issue.Code;

    private static string DefinitionIdentity(WordNoteDefinition definition) => string.Join(
        '\u001f',
        definition.Kind,
        definition.DefinitionType,
        definition.OoxmlId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        definition.RawOoxmlId ?? string.Empty,
        definition.ContentFingerprint,
        definition.ParagraphCount.ToString(CultureInfo.InvariantCulture),
        definition.TextCharacterCount.ToString(CultureInfo.InvariantCulture),
        definition.HasReferenceMark,
        definition.HasComplexContent
    );

    private static string ReferenceIdentity(WordNoteReference reference) => string.Join(
        '\u001f',
        reference.Kind,
        reference.OoxmlId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        reference.RawOoxmlId ?? string.Empty,
        reference.PartUri,
        reference.CustomMarkFollows,
        reference.CustomMarkValueValid,
        reference.NestedInsideNoteStory
    );

    private static string SpecialReferenceIdentity(WordNoteSpecialReference reference) => string.Join(
        '\u001f',
        reference.Kind,
        reference.OoxmlId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        reference.RawOoxmlId ?? string.Empty,
        reference.PartUri
    );

    private static string PolicyIdentity(WordNoteNumberingPolicy policy) => string.Join(
        '\u001f',
        policy.Kind,
        policy.Scope,
        policy.SectionIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        policy.PartUri,
        policy.Position ?? string.Empty,
        policy.NumberFormat ?? string.Empty,
        policy.NumberStart?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        policy.RawNumberStart ?? string.Empty,
        policy.NumberRestart ?? string.Empty,
        policy.ValuesValid,
        string.Join(',', policy.DuplicateProperties)
    );

    private static string CreatePlanId(
        string baseFingerprint,
        string resultFingerprint,
        WordNoteRepairCommand command
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-note-repair-plan-v1");
        AppendHash(hash, baseFingerprint);
        AppendHash(hash, resultFingerprint);
        AppendHash(hash, command.Kind.ToString());
        AppendHash(hash, command.DefinitionId);
        AppendHash(hash, command.ExpectedDefinitionFingerprint.ToLowerInvariant());
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
}
