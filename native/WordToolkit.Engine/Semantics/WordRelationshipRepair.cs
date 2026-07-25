using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public abstract record WordRelationshipRepairCommand;

public sealed record RemoveUnreferencedRelationshipCommand(
    string SourcePartUri,
    string RelationshipId,
    string ExpectedRelationshipFingerprint
) : WordRelationshipRepairCommand;

public sealed record RemoveOrphanRelationshipPartCommand(
    string RelationshipPartUri,
    string ExpectedEntrySha256
) : WordRelationshipRepairCommand;

public sealed record WordRelationshipRepairOptions
{
    public static WordRelationshipRepairOptions Default { get; } = new();

    public int MaxCommands { get; init; } = 100;

    public int MaxChangedEntries { get; init; } = 32;

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxCommands <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommands));
        }
        if (MaxChangedEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChangedEntries));
        }
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
    }
}

public enum WordRelationshipRepairActionKind
{
    RemoveUnreferencedRelationship,
    RemoveOrphanRelationshipPart,
}

public enum WordRelationshipRepairEntryChangeKind
{
    Add,
    Replace,
    Delete,
}

public sealed record WordRelationshipRepairAction(
    int CommandIndex,
    WordRelationshipRepairActionKind Kind,
    string RelationshipPartUri,
    string? SourcePartUri,
    string? RelationshipId,
    string? RelationshipFingerprint,
    string BeforeEntrySha256,
    int RemovedRelationshipCount
);

public sealed record WordRelationshipRepairEntryChange(
    string EntryName,
    string? PartUri,
    WordRelationshipRepairEntryChangeKind Kind,
    string? BeforeSha256,
    string? AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed record WordRelationshipRepairValidation(
    bool CandidateReparsed,
    bool SemanticProjectionPreserved,
    bool UnplannedEntriesPreserved,
    bool IntendedRelationshipsRemoved,
    bool NoUnexpectedRelationshipChanges,
    bool NoNewStructuralErrors,
    bool NoNewUnreachableParts,
    bool ExactInverseVerified,
    int BeforeRelationshipCount,
    int AfterRelationshipCount,
    int BeforeStructuralErrorCount,
    int AfterStructuralErrorCount,
    int BeforeUnreachablePartCount,
    int AfterUnreachablePartCount
)
{
    public bool Passed => CandidateReparsed
        && SemanticProjectionPreserved
        && UnplannedEntriesPreserved
        && IntendedRelationshipsRemoved
        && NoUnexpectedRelationshipChanges
        && NoNewStructuralErrors
        && NoNewUnreachableParts
        && ExactInverseVerified;
}

public sealed class WordRelationshipRepairPlan
{
    private readonly WordPackageEntryTransactionCore _transaction;

    internal WordRelationshipRepairPlan(
        string planId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<WordRelationshipRepairAction> actions,
        IReadOnlyDictionary<string, WordPackageEntryPayload> entries,
        WordRelationshipRepairValidation validation,
        IReadOnlyList<string> safetyRules
    )
    {
        PlanId = planId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        Actions = new ReadOnlyCollection<WordRelationshipRepairAction>(actions.ToArray());
        Validation = validation;
        SafetyRules = new ReadOnlyCollection<string>(safetyRules.ToArray());
        _transaction = new WordPackageEntryTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            entries
        );
        ChangedEntries = new ReadOnlyCollection<WordRelationshipRepairEntryChange>(
            _transaction.Entries
                .OrderBy(entry => entry.EntryName, StringComparer.Ordinal)
                .Select(entry => new WordRelationshipRepairEntryChange(
                    entry.EntryName,
                    entry.PartUri,
                    (WordRelationshipRepairEntryChangeKind)entry.Kind,
                    entry.BeforeSha256,
                    entry.AfterSha256,
                    entry.BeforeContent?.Length ?? 0,
                    entry.AfterContent?.Length ?? 0
                ))
                .ToArray()
        );
    }

    public string PlanId { get; }

    public string BasePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public IReadOnlyList<WordRelationshipRepairAction> Actions { get; }

    public IReadOnlyList<WordRelationshipRepairEntryChange> ChangedEntries { get; }

    public WordRelationshipRepairValidation Validation { get; }

    public IReadOnlyList<string> SafetyRules { get; }

    public bool HasChanges => _transaction.HasChanges;

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot) =>
        _transaction.CreateMutation(currentSnapshot);

    public OpcPackageMutationBuilder CreateInverseMutation(OpcPackageSnapshot appliedSnapshot) =>
        _transaction.CreateInverseMutation(appliedSnapshot);
}

public sealed class WordRelationshipRepairPlanner
{
    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly WordRelationshipRepairOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;
    private readonly WordRelationshipUsageGraphBuilder _usageBuilder;
    private readonly OpcPackageReader _reader = new();
    private readonly OpcPackageSerializer _serializer = new();

    public WordRelationshipRepairPlanner(
        WordRelationshipRepairOptions? options = null
    )
    {
        _options = options ?? WordRelationshipRepairOptions.Default;
        _options.Validate();
        _xmlOptions = new LosslessXmlOptions
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
            MaxTextCharacters = _options.MaxXmlPartBytes,
        };
        _usageBuilder = new WordRelationshipUsageGraphBuilder(
            new WordRelationshipUsageGraphOptions
            {
                MaxXmlPartBytes = _options.MaxXmlPartBytes,
            }
        );
    }

    public WordRelationshipRepairPlan Plan(
        OpcPackageSnapshot package,
        IReadOnlyList<WordRelationshipRepairCommand> commands,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(commands);
        cancellationToken.ThrowIfCancellationRequested();
        if (commands.Count is 0)
        {
            throw new ArgumentException("At least one relationship repair command is required.");
        }
        if (commands.Count > _options.MaxCommands)
        {
            throw new WordSemanticTransactionLimitException(
                $"Relationship repair contains {commands.Count} commands; the limit is {_options.MaxCommands}."
            );
        }
        RejectDuplicateCommands(commands);

        var working = package;
        var actions = new List<WordRelationshipRepairAction>(commands.Count);
        for (var index = 0; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            working = commands[index] switch
            {
                RemoveUnreferencedRelationshipCommand relationship =>
                    RemoveRelationship(working, relationship, index, actions, cancellationToken),
                RemoveOrphanRelationshipPartCommand orphan =>
                    RemoveOrphanRelationshipPart(working, orphan, index, actions, cancellationToken),
                _ => throw new ArgumentException(
                    $"Unsupported relationship repair command '{commands[index].GetType().Name}'."
                ),
            };
        }

        var payloads = BuildPayloads(package, working);
        if (payloads.Count > _options.MaxChangedEntries)
        {
            throw new WordSemanticTransactionLimitException(
                $"Relationship repair changes {payloads.Count} package entries; the limit is {_options.MaxChangedEntries}."
            );
        }
        var transaction = new WordPackageEntryTransactionCore(
            package.Fingerprint,
            working.Fingerprint,
            payloads
        );
        var validation = ValidateCandidate(
            package,
            working,
            actions,
            payloads,
            transaction,
            cancellationToken
        );
        if (!validation.Passed)
        {
            throw new WordSemanticEditException(
                "The relationship repair candidate failed package, semantic, or inverse validation."
            );
        }

        return new WordRelationshipRepairPlan(
            CreatePlanId(package.Fingerprint, working.Fingerprint, commands),
            package.Fingerprint,
            working.Fingerprint,
            actions,
            payloads,
            validation,
            [
                "relationship_deletion_never_deletes_target_part",
                "package_root_relationships_are_not_repair_candidates",
                "implicit_and_unknown_relationship_types_are_not_removed",
                "all_markup_compatibility_branches_are_scanned",
                "candidate_is_reparsed_before_publication",
                "new_unreachable_parts_are_forbidden",
                "unplanned_entries_are_byte_preserved",
                "exact_inverse_is_verified",
            ]
        );
    }

    private OpcPackageSnapshot RemoveRelationship(
        OpcPackageSnapshot package,
        RemoveUnreferencedRelationshipCommand command,
        int commandIndex,
        ICollection<WordRelationshipRepairAction> actions,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourcePartUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RelationshipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ExpectedRelationshipFingerprint);
        if (command.SourcePartUri == OpcPartUri.PackageRoot)
        {
            throw new WordSemanticEditException(
                "Package-root relationships cannot be removed by relationship repair."
            );
        }

        var usageGraph = _usageBuilder.Build(package, cancellationToken);
        if (!usageGraph.TryGetRelationship(
                command.SourcePartUri,
                command.RelationshipId,
                out var usage
            ) || usage is null)
        {
            throw new WordSemanticPreconditionException(
                $"Relationship '{command.RelationshipId}' from '{command.SourcePartUri}' disappeared."
            );
        }
        if (!string.Equals(
                usage.Fingerprint,
                command.ExpectedRelationshipFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordSemanticPreconditionException(
                $"Relationship '{command.RelationshipId}' changed after inspection."
            );
        }
        if (!usage.MarkupRemovalCandidate)
        {
            throw new WordSemanticEditException(
                $"Relationship '{command.RelationshipId}' is not a proven unreferenced explicit relationship."
            );
        }

        var entry = FindEntryByPartUri(package, usage.RelationshipPartUri);
        var source = LosslessXmlDocument.Parse(entry.Content, _xmlOptions, cancellationToken);
        var matches = source.Elements.Where(element =>
            string.Equals(element.NamespaceUri, RelationshipsNamespace, StringComparison.Ordinal)
            && string.Equals(element.LocalName, "Relationship", StringComparison.Ordinal)
            && element.Attributes.Any(attribute =>
                attribute.NamespaceUri.Length == 0
                && string.Equals(attribute.LocalName, "Id", StringComparison.Ordinal)
                && string.Equals(attribute.Value, command.RelationshipId, StringComparison.Ordinal)
            )
        ).ToArray();
        if (matches.Length != 1)
        {
            throw new WordSemanticPreconditionException(
                $"Relationship element '{command.RelationshipId}' is not unique in '{usage.RelationshipPartUri}'."
            );
        }
        var afterContent = source.ApplyPatches(
            [source.CreateElementRemovalPatch(matches[0].Ordinal)],
            entry.Sha256,
            cancellationToken
        );
        var candidate = ApplyEntryChange(package, entry, afterContent, cancellationToken);
        actions.Add(
            new WordRelationshipRepairAction(
                commandIndex,
                WordRelationshipRepairActionKind.RemoveUnreferencedRelationship,
                usage.RelationshipPartUri,
                usage.SourcePartUri,
                usage.RelationshipId,
                usage.Fingerprint,
                entry.Sha256,
                1
            )
        );
        return candidate;
    }

    private OpcPackageSnapshot RemoveOrphanRelationshipPart(
        OpcPackageSnapshot package,
        RemoveOrphanRelationshipPartCommand command,
        int commandIndex,
        ICollection<WordRelationshipRepairAction> actions,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RelationshipPartUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ExpectedEntrySha256);
        if (string.Equals(
                command.RelationshipPartUri,
                "/" + OpcPartUri.RootRelationshipsEntryName,
                StringComparison.Ordinal
            ))
        {
            throw new WordSemanticEditException(
                "The package-root relationships part cannot be removed."
            );
        }

        var graph = _usageBuilder.Build(package, cancellationToken);
        var orphan = graph.OrphanRelationshipParts.SingleOrDefault(item =>
            string.Equals(
                item.RelationshipPartUri,
                command.RelationshipPartUri,
                StringComparison.Ordinal
            )
        ) ?? throw new WordSemanticPreconditionException(
            $"Relationship part '{command.RelationshipPartUri}' is not orphaned."
        );
        if (!string.Equals(
                orphan.EntrySha256,
                command.ExpectedEntrySha256,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordSemanticPreconditionException(
                $"Orphan relationship part '{command.RelationshipPartUri}' changed after inspection."
            );
        }
        var entry = package.Entries.Single(item =>
            string.Equals(item.Name, orphan.EntryName, StringComparison.Ordinal)
        );
        var candidate = ApplyEntryChange(package, entry, null, cancellationToken);
        actions.Add(
            new WordRelationshipRepairAction(
                commandIndex,
                WordRelationshipRepairActionKind.RemoveOrphanRelationshipPart,
                orphan.RelationshipPartUri,
                orphan.SourcePartUri,
                null,
                null,
                entry.Sha256,
                orphan.ParsedRelationshipCount
            )
        );
        return candidate;
    }

    private OpcPackageSnapshot ApplyEntryChange(
        OpcPackageSnapshot package,
        OpcPackageEntry entry,
        byte[]? afterContent,
        CancellationToken cancellationToken
    )
    {
        var mutation = new OpcPackageMutationBuilder(package);
        if (afterContent is null)
        {
            mutation.DeleteEntry(entry.Name, entry.Sha256);
        }
        else
        {
            mutation.ReplaceEntry(entry.Name, afterContent, entry.Sha256);
        }
        return Materialize(mutation, cancellationToken);
    }

    private OpcPackageSnapshot Materialize(
        OpcPackageMutationBuilder mutation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        _serializer.Write(stream, mutation);
        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        return _reader.Read(stream, cancellationToken);
    }

    private static IReadOnlyDictionary<string, WordPackageEntryPayload> BuildPayloads(
        OpcPackageSnapshot baseline,
        OpcPackageSnapshot candidate
    )
    {
        var candidateByName = candidate.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal
        );
        var payloads = new Dictionary<string, WordPackageEntryPayload>(StringComparer.Ordinal);
        foreach (var before in baseline.Entries)
        {
            if (!candidateByName.TryGetValue(before.Name, out var after))
            {
                payloads.Add(
                    before.Name,
                    new WordPackageEntryPayload(
                        before.Name,
                        before.PartUri,
                        before.Content.ToArray(),
                        null
                    )
                );
            }
            else if (!string.Equals(before.Sha256, after.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                payloads.Add(
                    before.Name,
                    new WordPackageEntryPayload(
                        before.Name,
                        before.PartUri,
                        before.Content.ToArray(),
                        after.Content.ToArray()
                    )
                );
            }
        }
        if (candidate.Entries.Any(after => !baseline.Entries.Any(before =>
                string.Equals(before.Name, after.Name, StringComparison.Ordinal)
            )))
        {
            throw new WordSemanticEditException(
                "Relationship repair unexpectedly added a package entry."
            );
        }
        return payloads;
    }

    private WordRelationshipRepairValidation ValidateCandidate(
        OpcPackageSnapshot baseline,
        OpcPackageSnapshot candidate,
        IReadOnlyList<WordRelationshipRepairAction> actions,
        IReadOnlyDictionary<string, WordPackageEntryPayload> payloads,
        WordPackageEntryTransactionCore transaction,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changedNames = payloads.Keys.ToHashSet(StringComparer.Ordinal);
        var candidateEntries = candidate.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal
        );
        var unplannedEntriesPreserved = baseline.Entries
            .Where(entry => !changedNames.Contains(entry.Name))
            .All(entry => candidateEntries.TryGetValue(entry.Name, out var after)
                && string.Equals(entry.Sha256, after.Sha256, StringComparison.OrdinalIgnoreCase));

        var removedRelationshipKeys = actions
            .Where(action => action.RelationshipId is not null)
            .Select(action => RelationshipIdentity(action.SourcePartUri!, action.RelationshipId!))
            .ToHashSet(StringComparer.Ordinal);
        var orphanPartUris = actions
            .Where(action => action.Kind == WordRelationshipRepairActionKind.RemoveOrphanRelationshipPart)
            .Select(action => action.RelationshipPartUri)
            .ToHashSet(StringComparer.Ordinal);
        var intendedRelationshipsRemoved = actions.All(action =>
            action.Kind switch
            {
                WordRelationshipRepairActionKind.RemoveUnreferencedRelationship =>
                    !candidate.Relationships.Any(relationship =>
                        string.Equals(relationship.SourcePartUri, action.SourcePartUri, StringComparison.Ordinal)
                        && string.Equals(relationship.Id, action.RelationshipId, StringComparison.Ordinal)),
                WordRelationshipRepairActionKind.RemoveOrphanRelationshipPart =>
                    !candidate.Entries.Any(entry =>
                        string.Equals(entry.PartUri, action.RelationshipPartUri, StringComparison.Ordinal)),
                _ => false,
            }
        );
        var expectedRemaining = baseline.Relationships
            .Where(relationship =>
                !removedRelationshipKeys.Contains(RelationshipIdentity(
                    relationship.SourcePartUri,
                    relationship.Id
                ))
                && !orphanPartUris.Contains(relationship.RelationshipPartUri)
            )
            .Select(RelationshipValueIdentity)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualRemaining = candidate.Relationships
            .Select(RelationshipValueIdentity)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var noUnexpectedRelationshipChanges = expectedRemaining.SequenceEqual(actualRemaining);

        var beforeErrors = StructuralErrors(baseline).ToArray();
        var afterErrors = StructuralErrors(candidate).ToArray();
        var beforeErrorKeys = beforeErrors.Select(DiagnosticIdentity).ToHashSet(StringComparer.Ordinal);
        var noNewStructuralErrors = afterErrors.All(error =>
            beforeErrorKeys.Contains(DiagnosticIdentity(error))
        );
        var beforeUnreachable = UnreachableParts(baseline);
        var afterUnreachable = UnreachableParts(candidate);
        var noNewUnreachableParts = afterUnreachable.IsSubsetOf(beforeUnreachable);

        var baselineSemantic = new WordSemanticProjector().Project(
            baseline,
            cancellationToken
        );
        var candidateSemantic = new WordSemanticProjector().Project(
            candidate,
            cancellationToken
        );
        var semanticProjectionPreserved = SemanticProjection(baselineSemantic)
            .SequenceEqual(SemanticProjection(candidateSemantic));

        var inverse = Materialize(transaction.CreateInverseMutation(candidate), cancellationToken);
        var exactInverseVerified = string.Equals(
            inverse.Fingerprint,
            baseline.Fingerprint,
            StringComparison.Ordinal
        );
        return new WordRelationshipRepairValidation(
            CandidateReparsed: true,
            SemanticProjectionPreserved: semanticProjectionPreserved,
            UnplannedEntriesPreserved: unplannedEntriesPreserved,
            IntendedRelationshipsRemoved: intendedRelationshipsRemoved,
            NoUnexpectedRelationshipChanges: noUnexpectedRelationshipChanges,
            NoNewStructuralErrors: noNewStructuralErrors,
            NoNewUnreachableParts: noNewUnreachableParts,
            ExactInverseVerified: exactInverseVerified,
            BeforeRelationshipCount: baseline.Relationships.Count,
            AfterRelationshipCount: candidate.Relationships.Count,
            BeforeStructuralErrorCount: beforeErrors.Length,
            AfterStructuralErrorCount: afterErrors.Length,
            BeforeUnreachablePartCount: beforeUnreachable.Count,
            AfterUnreachablePartCount: afterUnreachable.Count
        );
    }

    private static IEnumerable<string> SemanticProjection(WordSemanticDocument document) =>
        document.Nodes.Select(node =>
        {
            var properties = string.Join(
                '\u001e',
                node.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + pair.Value)
            );
            return string.Join(
                '\u001f',
                node.SourcePartUri,
                node.SourcePath,
                node.Kind.ToString(),
                node.Text ?? string.Empty,
                node.IdentityKind.ToString(),
                node.IdentityFingerprint,
                node.SubtreeFingerprint,
                node.StructuralFingerprint,
                properties
            );
        });

    private static IEnumerable<OpcDiagnostic> StructuralErrors(OpcPackageSnapshot package) =>
        package.Diagnostics.Where(diagnostic =>
            diagnostic.Severity is OpcDiagnosticSeverity.Error or OpcDiagnosticSeverity.Fatal
        );

    private static HashSet<string> UnreachableParts(OpcPackageSnapshot package) =>
        package.Diagnostics
            .Where(diagnostic => diagnostic.Code == "OPC040" && diagnostic.PartUri is not null)
            .Select(diagnostic => diagnostic.PartUri!)
            .ToHashSet(StringComparer.Ordinal);

    private static string DiagnosticIdentity(OpcDiagnostic diagnostic) => string.Join(
        '\u001f',
        diagnostic.Code,
        diagnostic.Severity.ToString(),
        diagnostic.PartUri ?? string.Empty,
        diagnostic.RelationshipId ?? string.Empty,
        diagnostic.Message
    );

    private static string RelationshipIdentity(string sourcePartUri, string relationshipId) =>
        sourcePartUri + "\u001f" + relationshipId;

    private static string RelationshipValueIdentity(OpcRelationship relationship) => string.Join(
        '\u001f',
        relationship.SourcePartUri,
        relationship.RelationshipPartUri,
        relationship.Id,
        relationship.Type,
        relationship.Target,
        relationship.TargetMode.ToString(),
        relationship.ResolvedTargetPartUri ?? string.Empty,
        relationship.TargetFragment ?? string.Empty
    );

    private static OpcPackageEntry FindEntryByPartUri(
        OpcPackageSnapshot package,
        string partUri
    ) => package.Entries.SingleOrDefault(entry =>
        string.Equals(entry.PartUri, partUri, StringComparison.Ordinal)
    ) ?? throw new WordSemanticPreconditionException(
        $"Package entry for relationship part '{partUri}' disappeared."
    );

    private static void RejectDuplicateCommands(
        IReadOnlyList<WordRelationshipRepairCommand> commands
    )
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var key = command switch
            {
                RemoveUnreferencedRelationshipCommand relationship =>
                    "relationship\u001f" + relationship.SourcePartUri + "\u001f" + relationship.RelationshipId,
                RemoveOrphanRelationshipPartCommand orphan =>
                    "part\u001f" + orphan.RelationshipPartUri,
                _ => throw new ArgumentException(
                    $"Unsupported relationship repair command '{command.GetType().Name}'."
                ),
            };
            if (!keys.Add(key))
            {
                throw new ArgumentException("Relationship repair contains a duplicate command.");
            }
        }
    }

    private static string CreatePlanId(
        string baseFingerprint,
        string resultFingerprint,
        IReadOnlyList<WordRelationshipRepairCommand> commands
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-relationship-repair-plan-v1");
        AppendHash(hash, baseFingerprint);
        AppendHash(hash, resultFingerprint);
        AppendHash(hash, commands.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var command in commands)
        {
            switch (command)
            {
                case RemoveUnreferencedRelationshipCommand relationship:
                    AppendHash(hash, "remove_relationship");
                    AppendHash(hash, relationship.SourcePartUri);
                    AppendHash(hash, relationship.RelationshipId);
                    AppendHash(hash, relationship.ExpectedRelationshipFingerprint);
                    break;
                case RemoveOrphanRelationshipPartCommand orphan:
                    AppendHash(hash, "remove_orphan_relationship_part");
                    AppendHash(hash, orphan.RelationshipPartUri);
                    AppendHash(hash, orphan.ExpectedEntrySha256);
                    break;
            }
        }
        return "wrrplan_" + Convert.ToBase64String(hash.GetHashAndReset().AsSpan(0, 18))
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
