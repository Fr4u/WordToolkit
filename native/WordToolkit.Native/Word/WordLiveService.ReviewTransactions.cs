using System.Diagnostics;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> PlanPackageReviewDecisionsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageTextAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var path = ResolveInspectablePackagePath(arguments);
        var context = BuildPackageReviewPlan(path, arguments, cancellationToken);
        var includeDetails = arguments.Boolean("include_details", false);
        return ReviewPlanResponse(context, path, includeDetails, started);
    });

    private static Task<object> ApplyPackageReviewDecisionsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageTextAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var path = ResolveInspectablePackagePath(arguments);
        var expectedPlanId = RequiredReviewPlanId(arguments);
        var context = BuildPackageReviewPlan(path, arguments, cancellationToken);
        if (!string.Equals(context.Plan.PlanId, expectedPlanId, StringComparison.Ordinal))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "Selectors do not reproduce the reviewed revision-decision plan ID"
            );
        }
        if (context.HasDigitalSignatures)
        {
            throw new NativeToolException(
                "SIGNED_PACKAGE",
                "Direct OOXML editing is blocked because the package contains digital signatures"
            );
        }
        if (!context.Plan.CanApply)
        {
            throw new NativeToolException(
                "REVIEW_DECISION_BLOCKED",
                "The selected review decisions include structures that cannot be changed safely",
                new
                {
                    block_count = context.Plan.BlockCount,
                    blocks = ReviewBlockItems(context.Plan.Blocks, 20),
                }
            );
        }
        if (!context.Validation.NoNewErrors)
        {
            throw new NativeToolException(
                "OOXML_SCHEMA_INVALID",
                "The exact candidate package fails Microsoft Open XML schema validation",
                new
                {
                    error_count = context.Validation.ErrorCount,
                    baseline_error_count = context.Validation.BaselineErrorCount,
                    candidate_error_count = context.Validation.CandidateErrorCount,
                    errors_truncated = context.Validation.ErrorsTruncated,
                    issues = context.Validation.Issues.Take(20).Select(ValidationIssueItem).ToArray(),
                }
            );
        }
        if (!context.Plan.HasChanges)
        {
            return new
            {
                file_name = Path.GetFileName(path),
                plan_id = context.Plan.PlanId,
                applied = false,
                no_op = true,
                package_fingerprint = context.Package.Fingerprint,
                backup_path = (string?)null,
                changed_entry_names = Array.Empty<string>(),
                runtime = "dotnet-native",
                python_used = false,
                word_opened = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            };
        }

        var mutation = context.Plan.CreateMutation(context.Package);
        var result = new OpcAtomicPackageWriter().Write(
            path,
            mutation,
            new OpcAtomicWriteOptions
            {
                ExpectedDestinationFingerprint = context.Package.Fingerprint,
                ExpectedResultFingerprint = context.Plan.ResultPackageFingerprint,
                KeepBackup = arguments.Boolean("keep_backup", true),
            }
        );
        return new
        {
            file_name = Path.GetFileName(path),
            plan_id = context.Plan.PlanId,
            decision = ToSnakeCase(context.Selection.Decision.ToString()),
            applied = true,
            no_op = false,
            selected_revision_count = context.Selection.SelectedRevisionCount,
            cascaded_revision_count = context.Plan.CascadeCount,
            previous_package_fingerprint = context.Package.Fingerprint,
            package_fingerprint = result.Fingerprint,
            predicted_package_fingerprint = context.Plan.ResultPackageFingerprint,
            backup_path = result.BackupPath,
            changed_entry_names = result.ChangedEntryNames,
            diagnostic_count = result.Diagnostics.Count,
            microsoft_schema_valid = context.Validation.CandidateValid,
            microsoft_schema_no_new_errors = context.Validation.NoNewErrors,
            runtime = "dotnet-native",
            python_used = false,
            word_opened = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static object ReviewPlanResponse(
        PackageReviewPlanContext context,
        string path,
        bool includeDetails,
        long started
    )
    {
        var blockedReasons = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blockedReasons.Add("digital_signature_present");
        }
        if (!context.Plan.CanApply)
        {
            blockedReasons.Add("unsupported_or_dependent_revision_structure");
        }
        if (context.Validation.Performed && !context.Validation.NoNewErrors)
        {
            blockedReasons.Add("microsoft_schema_validation_failed");
        }
        return new
        {
            file_name = Path.GetFileName(path),
            plan_id = context.Plan.PlanId,
            decision = ToSnakeCase(context.Selection.Decision.ToString()),
            base_package_fingerprint = context.Plan.BasePackageFingerprint,
            result_package_fingerprint = context.Plan.ResultPackageFingerprint,
            package_revision_count = context.Graph.Revisions.Count,
            selected_revision_count = context.Selection.SelectedRevisionCount,
            explicit_revision_count = context.Plan.ExplicitCommandCount,
            cascaded_revision_count = context.Plan.CascadeCount,
            operation_count = context.Plan.OperationCount,
            changed_operation_count = context.Plan.ChangedOperationCount,
            changed_part_count = context.Plan.ChangedPartCount,
            removed_move_marker_count = context.Plan.RemovedMoveMarkerCount,
            total_xml_byte_delta = context.Plan.TotalXmlByteDelta,
            block_count = context.Plan.BlockCount,
            block_codes = context.Plan.Blocks
                .GroupBy(block => block.Code, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new { code = group.Key, count = group.Count() })
                .ToArray(),
            structural_plan_supported = context.Plan.CanApply,
            can_apply = blockedReasons.Count == 0,
            has_changes = context.Plan.HasChanges,
            apply_blocked = blockedReasons.Count != 0,
            apply_blocked_reasons = blockedReasons,
            candidate_validation = new
            {
                performed = context.Validation.Performed,
                valid = context.Validation.CandidateValid,
                no_new_errors = context.Validation.NoNewErrors,
                error_count = context.Validation.ErrorCount,
                baseline_error_count = context.Validation.BaselineErrorCount,
                candidate_error_count = context.Validation.CandidateErrorCount,
                errors_truncated = context.Validation.ErrorsTruncated,
                not_performed_reason = context.Validation.NotPerformedReason,
                issues = includeDetails
                    ? context.Validation.Issues.Select(ValidationIssueItem).ToArray()
                    : null,
            },
            blocks = includeDetails
                ? ReviewBlockItems(context.Plan.Blocks, 100)
                : null,
            operations = includeDetails
                ? context.Plan.Operations.Take(200).Select(operation => new
                {
                    index = operation.Index,
                    revision_id = operation.RevisionId,
                    decision = ToSnakeCase(operation.Decision.ToString()),
                    revision_kind = ToSnakeCase(operation.RevisionKind.ToString()),
                    transformation = operation.Transformation,
                    source_part_uri = BoundForResponse(operation.SourcePartUri, 512),
                    source_element_ordinal = operation.SourceElementOrdinal,
                    is_implicit = operation.IsImplicit,
                    is_absorbed = operation.IsAbsorbed,
                    absorbed_by_revision_id = operation.AbsorbedByRevisionId,
                    is_blocked = operation.IsBlocked,
                    xml_byte_delta = operation.XmlByteDelta,
                    affected_element_count = operation.AffectedElementCount,
                }).ToArray()
                : null,
            changed_parts = includeDetails
                ? context.Plan.ChangedParts.Select(part => new
                {
                    part_uri = BoundForResponse(part.PartUri, 512),
                    before_bytes = part.BeforeBytes,
                    after_bytes = part.AfterBytes,
                    byte_delta = (long)part.AfterBytes - part.BeforeBytes,
                }).ToArray()
                : null,
            sensitive_values_included = false,
            raw_xml_returned = false,
            mutation_performed = false,
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    }

    private static PackageReviewPlanContext BuildPackageReviewPlan(
        string path,
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expectedFingerprint = RequiredSha256(
            arguments,
            "expected_package_fingerprint"
        );
        var package = new OpcPackageReader().Read(path, cancellationToken);
        if (!string.Equals(
                package.Fingerprint,
                expectedFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordSemanticPreconditionException(
                "Saved package changed before the review decision plan was built."
            );
        }
        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        var graph = new WordReviewGraphBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        var selection = SelectReviewRevisions(graph, arguments);
        var plan = new WordReviewMutationPlanner(
            new WordReviewTransactionOptions
            {
                MaxCommands = 200,
                AllowCascadingRevisions = arguments.Boolean("allow_cascade", false),
            }
        ).Plan(package, graph, selection.Commands, cancellationToken);
        var hasSignatures = HasDigitalSignatures(package);
        var validation = hasSignatures
            ? CandidateSchemaValidation.NotPerformed("digital_signature_present")
            : !plan.CanApply
                ? CandidateSchemaValidation.NotPerformed("review_plan_blocked")
                : !plan.HasChanges
                    ? CandidateSchemaValidation.NotPerformed("no_changes")
                    : ValidateReviewCandidate(package, plan, cancellationToken);
        return new PackageReviewPlanContext(
            package,
            graph,
            plan,
            selection,
            hasSignatures,
            validation
        );
    }

    private static ReviewSelection SelectReviewRevisions(
        WordReviewGraph graph,
        JsonElement arguments
    )
    {
        _ = arguments.Required("decision");
        var decision = arguments.String("decision") switch
        {
            "accept" => WordReviewDecision.Accept,
            "reject" => WordReviewDecision.Reject,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "decision must be 'accept' or 'reject'"
            ),
        };
        var revisionIds = OptionalStringSet(
            arguments,
            "revision_ids",
            200,
            128,
            value => value.Length > 4
                && value.StartsWith("wdr_", StringComparison.Ordinal)
                && value[4..].All(character =>
                    char.IsAsciiLetterOrDigit(character)
                    || character is '_' or '-'
                )
        );
        var authorFingerprints = OptionalStringSet(
            arguments,
            "author_fingerprints",
            100,
            16,
            value => value.Length == 16 && value.All(Uri.IsHexDigit),
            StringComparer.OrdinalIgnoreCase
        );
        var storyKinds = OptionalEnumFilter<WordStoryKind>(arguments, "story_kinds");
        var revisionKinds = OptionalEnumFilter<WordRevisionKind>(
            arguments,
            "revision_kinds"
        );
        var selectAll = arguments.Boolean("select_all", false);
        if (
            !selectAll
            && (revisionIds is null || revisionIds.Count == 0)
            && (authorFingerprints is null || authorFingerprints.Count == 0)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Provide revision_ids or author_fingerprints, or explicitly set select_all=true"
            );
        }
        var selected = new Dictionary<string, WordRevisionDefinition>(StringComparer.Ordinal);
        if (selectAll)
        {
            foreach (var revision in graph.Revisions)
            {
                selected.TryAdd(revision.Id, revision);
            }
        }
        if (revisionIds is not null)
        {
            foreach (var id in revisionIds)
            {
                if (!graph.TryGetRevision(id, out var revision) || revision is null)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "revision_ids contains an ID absent from this package fingerprint",
                        new { revision_id = id }
                    );
                }
                selected.TryAdd(revision.Id, revision);
            }
        }
        if (authorFingerprints is not null)
        {
            foreach (var revision in graph.Revisions.Where(revision =>
                FingerprintSensitiveValue(revision.Author) is { } fingerprint
                && authorFingerprints.Contains(fingerprint)
            ))
            {
                selected.TryAdd(revision.Id, revision);
            }
        }
        var filtered = selected.Values
            .Where(revision =>
                storyKinds is null
                || storyKinds.Contains(ToSnakeCase(revision.StoryKind.ToString()))
            )
            .Where(revision =>
                revisionKinds is null
                || revisionKinds.Contains(ToSnakeCase(revision.Kind.ToString()))
            )
            .OrderBy(revision => revision.PartUri, StringComparer.Ordinal)
            .ThenBy(revision => revision.SourceElementOrdinal)
            .ThenBy(revision => revision.Id, StringComparer.Ordinal)
            .ToArray();
        if (filtered.Length == 0)
        {
            throw new NativeToolException(
                "NO_MATCH",
                "Review selectors matched no revisions in this package fingerprint"
            );
        }
        if (filtered.Length > 200)
        {
            throw new NativeToolException(
                "TRANSACTION_LIMIT",
                "Review selectors matched more than 200 revisions; narrow the filters"
            );
        }
        return new ReviewSelection(
            decision,
            filtered.Select(revision =>
                new WordReviewDecisionCommand(revision.Id, decision)
            ).ToArray(),
            filtered.Length
        );
    }

    private static HashSet<string>? OptionalStringSet(
        JsonElement arguments,
        string name,
        int maximumItems,
        int maximumLength,
        Func<string, bool> validate,
        StringComparer? comparer = null
    )
    {
        if (!arguments.TryGetProperty(name, out var node))
        {
            return null;
        }
        if (node.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException("INVALID_INPUT", $"{name} must be an array");
        }
        var result = new HashSet<string>(comparer ?? StringComparer.Ordinal);
        var itemCount = 0;
        foreach (var item in node.EnumerateArray())
        {
            itemCount++;
            if (itemCount > maximumItems)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} accepts at most {maximumItems} values"
                );
            }
            if (
                item.ValueKind != JsonValueKind.String
                || item.GetString() is not { Length: > 0 } value
                || value.Length > maximumLength
                || !validate(value)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} contains an invalid value"
                );
            }
            if (!result.Add(value))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} must contain unique values"
                );
            }
        }
        if (result.Count == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must not be empty when provided"
            );
        }
        return result;
    }

    private static HashSet<string>? OptionalEnumFilter<TEnum>(
        JsonElement arguments,
        string name
    ) where TEnum : struct, Enum
    {
        var allowed = Enum.GetValues<TEnum>()
            .Select(value => ToSnakeCase(value.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        return OptionalStringSet(
            arguments,
            name,
            allowed.Count,
            64,
            allowed.Contains
        );
    }

    private static CandidateSchemaValidation ValidateReviewCandidate(
        OpcPackageSnapshot package,
        WordReviewMutationPlan plan,
        CancellationToken cancellationToken
    ) => ValidatePackageCandidate(
        package,
        plan.CreateMutation(package),
        cancellationToken
    );

    private static CandidateSchemaValidation ValidatePackageCandidate(
        OpcPackageSnapshot package,
        OpcPackageMutationBuilder candidateMutation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var baselineStream = new MemoryStream();
            new OpcPackageSerializer().Write(
                baselineStream,
                new OpcPackageMutationBuilder(package)
            );
            using var candidateStream = new MemoryStream();
            new OpcPackageSerializer().Write(
                candidateStream,
                candidateMutation
            );
            var baseline = ValidateOpenXmlStream(baselineStream, cancellationToken);
            var candidate = ValidateOpenXmlStream(candidateStream, cancellationToken);
            if (baseline.Length > 500 || candidate.Length > 500)
            {
                return CandidateSchemaValidation.ValidationLimitExceeded(
                    Math.Min(baseline.Length, 500),
                    Math.Min(candidate.Length, 500)
                );
            }
            var baselineCounts = baseline
                .GroupBy(ValidationIssueKey)
                .ToDictionary(group => group.Key, group => group.Count());
            var newErrors = new List<CandidateValidationIssue>();
            foreach (var issue in candidate)
            {
                var key = ValidationIssueKey(issue);
                if (baselineCounts.TryGetValue(key, out var count) && count > 0)
                {
                    baselineCounts[key] = count - 1;
                }
                else
                {
                    newErrors.Add(issue);
                }
            }
            return new CandidateSchemaValidation(
                Performed: true,
                CandidateValid: candidate.Length == 0,
                NoNewErrors: newErrors.Count == 0,
                ErrorCount: newErrors.Count,
                BaselineErrorCount: baseline.Length,
                CandidateErrorCount: candidate.Length,
                ErrorsTruncated: false,
                NotPerformedReason: null,
                Issues: newErrors.Take(200).ToArray()
            );
        }
        catch (OpenXmlPackageException exception)
        {
            return CandidateSchemaValidation.OpenFailed(exception.GetType().Name);
        }
        catch (InvalidDataException exception)
        {
            return CandidateSchemaValidation.OpenFailed(exception.GetType().Name);
        }
    }

    private static CandidateValidationIssue[] ValidateOpenXmlStream(
        MemoryStream stream,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Take(501)
            .Select(error => new CandidateValidationIssue(
                error.Id,
                error.ErrorType.ToString(),
                error.Part?.Uri.ToString(),
                error.Path?.XPath,
                error.Node?.LocalName
            ))
            .ToArray();
    }

    private static string ValidationIssueKey(CandidateValidationIssue issue) =>
        string.Join(
            '\u001f',
            issue.Id ?? string.Empty,
            issue.ErrorType,
            issue.PartUri ?? string.Empty,
            issue.Path ?? string.Empty,
            issue.Node ?? string.Empty
        );

    private static object[] ReviewBlockItems(
        IEnumerable<WordReviewDecisionBlock> blocks,
        int maximum
    ) => blocks.Take(maximum).Select(block => (object)new
    {
        code = block.Code,
        message = BoundForResponse(block.Message, 512),
        revision_id = block.RevisionId,
        part_uri = BoundForResponse(block.PartUri, 512),
        source_element_ordinal = block.SourceElementOrdinal,
        related_revision_ids = block.RelatedRevisionIds.Take(20).ToArray(),
    }).ToArray();

    private static object ValidationIssueItem(CandidateValidationIssue issue) => new
    {
        id = BoundForResponse(issue.Id, 128),
        error_type = BoundForResponse(issue.ErrorType, 64),
        part_uri = BoundForResponse(issue.PartUri, 512),
        path = BoundForResponse(issue.Path, 512),
        node = BoundForResponse(issue.Node, 128),
    };

    private static string RequiredReviewPlanId(JsonElement arguments)
    {
        _ = arguments.Required("expected_plan_id");
        var value = arguments.String("expected_plan_id");
        if (
            value.Length is < 11 or > 128
            || !value.StartsWith("wrplan_", StringComparison.Ordinal)
            || value[7..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_plan_id is not a valid review decision plan ID"
            );
        }
        return value;
    }

    private sealed record PackageReviewPlanContext(
        OpcPackageSnapshot Package,
        WordReviewGraph Graph,
        WordReviewMutationPlan Plan,
        ReviewSelection Selection,
        bool HasDigitalSignatures,
        CandidateSchemaValidation Validation
    );

    private sealed record ReviewSelection(
        WordReviewDecision Decision,
        IReadOnlyList<WordReviewDecisionCommand> Commands,
        int SelectedRevisionCount
    );

    private sealed record CandidateValidationIssue(
        string? Id,
        string ErrorType,
        string? PartUri,
        string? Path,
        string? Node
    );

    private sealed record CandidateSchemaValidation(
        bool Performed,
        bool CandidateValid,
        bool NoNewErrors,
        int ErrorCount,
        int BaselineErrorCount,
        int CandidateErrorCount,
        bool ErrorsTruncated,
        string? NotPerformedReason,
        IReadOnlyList<CandidateValidationIssue> Issues
    )
    {
        internal static CandidateSchemaValidation NotPerformed(string reason) => new(
            Performed: false,
            CandidateValid: true,
            NoNewErrors: true,
            ErrorCount: 0,
            BaselineErrorCount: 0,
            CandidateErrorCount: 0,
            ErrorsTruncated: false,
            NotPerformedReason: reason,
            Issues: Array.Empty<CandidateValidationIssue>()
        );

        internal static CandidateSchemaValidation OpenFailed(string exceptionType) => new(
            Performed: true,
            CandidateValid: false,
            NoNewErrors: false,
            ErrorCount: 1,
            BaselineErrorCount: 0,
            CandidateErrorCount: 1,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues:
            [
                new CandidateValidationIssue(
                    "OPEN_XML_PACKAGE_OPEN_FAILED",
                    exceptionType,
                    null,
                    null,
                    null
                ),
            ]
        );

        internal static CandidateSchemaValidation ValidationLimitExceeded(
            int baselineErrors,
            int candidateErrors
        ) => new(
            Performed: true,
            CandidateValid: false,
            NoNewErrors: false,
            ErrorCount: 1,
            BaselineErrorCount: baselineErrors,
            CandidateErrorCount: candidateErrors,
            ErrorsTruncated: true,
            NotPerformedReason: null,
            Issues:
            [
                new CandidateValidationIssue(
                    "OPEN_XML_VALIDATION_LIMIT_EXCEEDED",
                    "Limit",
                    null,
                    null,
                    null
                ),
            ]
        );
    }
}
