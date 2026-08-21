using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> PlanPackageMergeAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageMergeAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = ParsePackageMergePageRequest(arguments);
        var context = BuildPackageMergePlan(arguments, cancellationToken);
        return PackageMergePlanResponse(context, request, started);
    });

    private static Task<object> ApplyPackageMergeAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageMergeAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        _ = RequiredSha256(arguments, "expected_ancestor_fingerprint");
        _ = RequiredSha256(arguments, "expected_left_fingerprint");
        _ = RequiredSha256(arguments, "expected_right_fingerprint");
        var expectedApplyPlanId = RequiredMergeApplyPlanId(arguments);
        var context = BuildPackageMergePlan(arguments, cancellationToken);
        if (!string.Equals(
                context.ApplyPlanId,
                expectedApplyPlanId,
                StringComparison.Ordinal
            ))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "The current ancestor, branches, resolutions and output path do not reproduce the reviewed merge apply-plan ID"
            );
        }

        var policy = ParsePackagePatchPolicy(arguments);
        var blocks = PackageMergeBlockCodes(context, policy);
        if (blocks.Count != 0)
        {
            throw new NativeToolException(
                "MERGE_POLICY_BLOCKED",
                "The merge has unresolved conflicts, requires authorization, or failed a non-overridable safety check",
                new
                {
                    merge_id = context.Plan.MergeId,
                    merge_apply_plan_id = context.ApplyPlanId,
                    block_codes = blocks,
                }
            );
        }

        var patch = context.Plan.Patch
            ?? throw new NativeToolException(
                "MERGE_POLICY_BLOCKED",
                "The merge has no materialized candidate"
            );
        var candidate = context.Plan.CandidatePackage!;
        cancellationToken.ThrowIfCancellationRequested();
        var result = new OpcAtomicPackageWriter().Write(
            context.OutputPath,
            patch.CreateMutation(context.AncestorPackage, cancellationToken),
            new OpcAtomicWriteOptions
            {
                ExpectedDestinationFingerprint = context.AncestorPackage.Fingerprint,
                ExpectedResultFingerprint = patch.ResultPackageFingerprint,
                AllowStructuralErrors = !candidate.IsStructurallyValid,
                KeepBackup = false,
                RequireNewDestination = true,
            }
        );
        return new
        {
            ancestor_file_name = Path.GetFileName(context.AncestorPath),
            left_file_name = Path.GetFileName(context.LeftPath),
            right_file_name = Path.GetFileName(context.RightPath),
            output_file_name = Path.GetFileName(context.OutputPath),
            output_path = context.OutputPath,
            merge_id = context.Plan.MergeId,
            merge_apply_plan_id = context.ApplyPlanId,
            created = true,
            overwritten = false,
            no_op_content = patch.IsNoOp,
            ancestor_package_fingerprint = context.AncestorPackage.Fingerprint,
            left_package_fingerprint = context.LeftPackage.Fingerprint,
            right_package_fingerprint = context.RightPackage.Fingerprint,
            package_fingerprint = result.Fingerprint,
            predicted_package_fingerprint = patch.ResultPackageFingerprint,
            conflict_count = context.Plan.ConflictCount,
            resolved_conflict_count = context.Plan.ResolvedConflictCount,
            changed_entry_names = result.ChangedEntryNames,
            diagnostic_count = result.Diagnostics.Count,
            digital_signatures_may_be_invalidated =
                context.Plan.ResultPlan!.RiskAssessment.DigitalSignaturesPresent
                && !patch.IsNoOp,
            explicit_authorizations = ExplicitAuthorizationNames(policy),
            raw_xml_returned = false,
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static PackageMergePlanContext BuildPackageMergePlan(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ancestorPath = ResolveComparedPackagePath(arguments, "ancestor_path");
        var leftPath = ResolveComparedPackagePath(arguments, "left_path");
        var rightPath = ResolveComparedPackagePath(arguments, "right_path");
        var outputPath = ResolvePackageMergeOutputPath(arguments);
        var reader = new OpcPackageReader();
        var projector = new WordSemanticProjector();

        var ancestorPackage = reader.Read(ancestorPath, cancellationToken);
        VerifyOptionalFingerprint(
            ancestorPackage.Fingerprint,
            OptionalFingerprint(arguments, "expected_ancestor_fingerprint"),
            "ancestor"
        );
        var ancestorDocument = projector.Project(
            ancestorPackage,
            cancellationToken
        );
        var leftPackage = reader.Read(leftPath, cancellationToken);
        VerifyOptionalFingerprint(
            leftPackage.Fingerprint,
            OptionalFingerprint(arguments, "expected_left_fingerprint"),
            "left"
        );
        var leftDocument = projector.Project(leftPackage, cancellationToken);
        var rightPackage = reader.Read(rightPath, cancellationToken);
        VerifyOptionalFingerprint(
            rightPackage.Fingerprint,
            OptionalFingerprint(arguments, "expected_right_fingerprint"),
            "right"
        );
        var rightDocument = projector.Project(rightPackage, cancellationToken);
        var resolutions = ParsePackageMergeResolutions(arguments);
        var plan = new WordPackageThreeWayMergePlanner().Plan(
            ancestorPackage,
            ancestorDocument,
            leftPackage,
            leftDocument,
            rightPackage,
            rightDocument,
            resolutions,
            cancellationToken
        );
        var validation = plan.Patch is null
            ? CandidateSchemaValidation.NotPerformed("unresolved_conflicts")
            : plan.Patch.IsNoOp
                ? CandidateSchemaValidation.NotPerformed("no_changes")
                : ValidatePackageCandidate(
                    ancestorPackage,
                    plan.CreateMutation(ancestorPackage),
                    cancellationToken
                );
        var formatHardBlocks = plan.CandidatePackage is null
            ? Array.Empty<string>()
            : PackagePatchFormatHardBlockCodes(outputPath, plan.CandidatePackage);
        var hardBlocks = PackageMergeHardBlockCodes(
            plan,
            validation,
            formatHardBlocks
        );
        var applyPlanId = ComputeMergeApplyPlanId(
            plan,
            validation,
            outputPath,
            hardBlocks
        );
        return new PackageMergePlanContext(
            ancestorPath,
            leftPath,
            rightPath,
            outputPath,
            ancestorPackage,
            leftPackage,
            rightPackage,
            plan,
            validation,
            hardBlocks,
            applyPlanId
        );
    }

    private static object PackageMergePlanResponse(
        PackageMergePlanContext context,
        PackageMergePageRequest request,
        long started
    )
    {
        var page = PackageMergePage(context, request);
        var defaultBlocks = PackageMergeBlockCodes(
            context,
            new WordPackagePatchApplyPolicy()
        );
        var patch = context.Plan.Patch;
        var resultPlan = context.Plan.ResultPlan;
        return new
        {
            ancestor_file_name = Path.GetFileName(context.AncestorPath),
            left_file_name = Path.GetFileName(context.LeftPath),
            right_file_name = Path.GetFileName(context.RightPath),
            output_file_name = Path.GetFileName(context.OutputPath),
            output_path = context.OutputPath,
            merge_id = context.Plan.MergeId,
            merge_apply_plan_id = context.ApplyPlanId,
            ancestor_package_fingerprint = context.AncestorPackage.Fingerprint,
            left_package_fingerprint = context.LeftPackage.Fingerprint,
            right_package_fingerprint = context.RightPackage.Fingerprint,
            result_package_fingerprint = context.Plan.ResultPackageFingerprint,
            candidate_materialized = context.Plan.CanMaterialize,
            conflict_count = context.Plan.ConflictCount,
            resolved_conflict_count = context.Plan.ResolvedConflictCount,
            unresolved_conflict_count = context.Plan.UnresolvedConflictCount,
            entry_decision_count = context.Plan.EntryDecisions.Count,
            entry_outcome_counts = Enum.GetValues<WordPackageMergeEntryOutcome>()
                .ToDictionary(
                    value => ToSnakeCase(value.ToString()),
                    value => context.Plan.EntryDecisions.Count(decision =>
                        decision.Outcome == value
                    ),
                    StringComparer.Ordinal
                ),
            semantic_text_change_count = context.Plan.EntryDecisions.Sum(decision =>
                decision.SemanticTextChangeCount
            ),
            operation_count = patch?.OperationCount,
            no_op_content = patch?.IsNoOp,
            semantic = resultPlan is null
                ? null
                : SemanticPatchSummary(resultPlan.SemanticDiff),
            risk = resultPlan is null
                ? null
                : PackagePatchRiskSummary(resultPlan.RiskAssessment),
            openxml_schema_validation = SchemaValidationSummary(context.Validation),
            default_policy = new
            {
                can_apply = defaultBlocks.Count == 0,
                block_codes = defaultBlocks,
            },
            hard_block_codes = context.HardBlockCodes,
            required_authorizations = resultPlan is null
                ? Array.Empty<string>()
                : RequiredAuthorizationNames(
                    resultPlan.RiskAssessment,
                    SchemaValidationHasNewErrors(context.Validation),
                    hasChanges: patch is { IsNoOp: false }
                ),
            resolution_choices = new[] { "use_ancestor", "use_left", "use_right" },
            view = request.View,
            filtered_item_count = page.FilteredCount,
            offset = page.Offset,
            returned_item_count = page.Items.Length,
            next_offset = page.NextOffset,
            items = page.Items,
            hashes_included = request.IncludeHashes,
            text_previews_included = request.IncludeTextPreviews,
            raw_payloads_returned = false,
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

    private static MergePage PackageMergePage(
        PackageMergePlanContext context,
        PackageMergePageRequest request
    ) => request.View switch
    {
        "conflicts" => PageMergeItems(
            context.Plan.Conflicts,
            request,
            conflict => MergeConflictItem(conflict, context.Plan, request)
        ),
        "entries" => PageMergeItems(
            context.Plan.EntryDecisions,
            request,
            decision => (object)new
            {
                entry_name = BoundForResponse(decision.EntryName, 512),
                part_uri = BoundForResponse(decision.PartUri, 512),
                outcome = ToSnakeCase(decision.Outcome.ToString()),
                semantic_text_change_count = decision.SemanticTextChangeCount,
                conflict_count = decision.ConflictCount,
                is_infrastructure = decision.IsInfrastructure,
            }
        ),
        "operations" or "risks" or "schema_errors" =>
            PackageMergePatchPage(context, request),
        _ => MergePage.Empty,
    };

    private static MergePage PackageMergePatchPage(
        PackageMergePlanContext context,
        PackageMergePageRequest request
    )
    {
        if (context.Plan.ResultPlan is null)
        {
            return MergePage.Empty;
        }
        var patchRequest = new PackagePatchPageRequest(
            request.View,
            request.Offset,
            request.MaxItems,
            request.IncludeHashes
        );
        var page = PackagePatchPlanPage(
            context.Plan.ResultPlan,
            patchRequest,
            context.Validation
        );
        return new MergePage(
            page.FilteredCount,
            page.Offset,
            page.NextOffset,
            page.Items
        );
    }

    private static object MergeConflictItem(
        WordPackageMergeConflict conflict,
        WordPackageMergePlan plan,
        PackageMergePageRequest request
    ) => new
    {
        conflict_id = conflict.ConflictId,
        kind = ToSnakeCase(conflict.Kind.ToString()),
        entry_name = BoundForResponse(conflict.EntryName, 512),
        part_uri = BoundForResponse(conflict.PartUri, 512),
        source_path = BoundForResponse(conflict.SourcePath, 768),
        ancestor_node_id = conflict.AncestorNodeId?.Value,
        resolved = plan.Resolutions.TryGetValue(conflict.ConflictId, out var choice),
        resolution = plan.Resolutions.TryGetValue(conflict.ConflictId, out choice)
            ? ToSnakeCase(choice.ToString())
            : null,
        ancestor = MergeConflictSide(
            conflict.AncestorSha256,
            conflict.AncestorBytes,
            conflict.AncestorText,
            request
        ),
        left = MergeConflictSide(
            conflict.LeftSha256,
            conflict.LeftBytes,
            conflict.LeftText,
            request
        ),
        right = MergeConflictSide(
            conflict.RightSha256,
            conflict.RightBytes,
            conflict.RightText,
            request
        ),
        is_infrastructure = conflict.IsInfrastructure,
    };

    private static object MergeConflictSide(
        string? sha256,
        long? bytes,
        WordPackageMergeTextSnapshot? text,
        PackageMergePageRequest request
    ) => new
    {
        bytes,
        sha256 = request.IncludeHashes ? sha256 : null,
        text_characters = text?.CharacterCount,
        text_sha256 = request.IncludeHashes ? text?.Sha256 : null,
        text_preview = request.IncludeTextPreviews
            ? BoundForResponse(text?.Preview, 512)
            : null,
        text_preview_truncated = request.IncludeTextPreviews
            ? text?.PreviewTruncated
            : null,
    };

    private static MergePage PageMergeItems<T>(
        IReadOnlyList<T> source,
        PackageMergePageRequest request,
        Func<T, object> selector
    )
    {
        var items = source.Skip(request.Offset)
            .Take(request.MaxItems)
            .Select(selector)
            .ToArray();
        var nextOffset = request.Offset + items.Length < source.Count
            ? request.Offset + items.Length
            : (int?)null;
        return new MergePage(source.Count, request.Offset, nextOffset, items);
    }

    private static PackageMergePageRequest ParsePackageMergePageRequest(
        JsonElement arguments
    )
    {
        var view = arguments.String("view", "summary");
        if (view is not (
                "summary"
                or "conflicts"
                or "entries"
                or "operations"
                or "risks"
                or "schema_errors"
            ))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, conflicts, entries, operations, risks, or schema_errors"
            );
        }
        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_items") ?? 50;
        if (offset is < 0 or > int.MaxValue)
        {
            throw new NativeToolException("INVALID_INPUT", "offset is out of range");
        }
        if (maximum is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 200"
            );
        }
        return new PackageMergePageRequest(
            view,
            (int)offset,
            (int)maximum,
            arguments.Boolean("include_hashes", false),
            arguments.Boolean("include_text_previews", false)
        );
    }

    private static IReadOnlyList<WordPackageMergeResolution>
        ParsePackageMergeResolutions(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("resolutions", out var node))
        {
            return Array.Empty<WordPackageMergeResolution>();
        }
        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() > 20_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "resolutions must be an array with at most 20,000 items"
            );
        }
        var result = new List<WordPackageMergeResolution>(node.GetArrayLength());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Every merge resolution must be an object"
                );
            }
            _ = item.Required("conflict_id");
            _ = item.Required("choice");
            var conflictId = item.String("conflict_id");
            if (
                conflictId.Length is < 12 or > 96
                || !conflictId.StartsWith("wtmc_", StringComparison.Ordinal)
                || conflictId[5..].Any(character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character is not '_' and not '-'
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "conflict_id is not a valid WordToolkit merge conflict ID"
                );
            }
            if (!seen.Add(conflictId))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Each merge conflict may be resolved only once"
                );
            }
            var choice = item.String("choice") switch
            {
                "use_ancestor" => WordPackageMergeResolutionChoice.UseAncestor,
                "use_left" => WordPackageMergeResolutionChoice.UseLeft,
                "use_right" => WordPackageMergeResolutionChoice.UseRight,
                _ => throw new NativeToolException(
                    "INVALID_INPUT",
                    "resolution choice must be use_ancestor, use_left, or use_right"
                ),
            };
            result.Add(new WordPackageMergeResolution(conflictId, choice));
        }
        return result;
    }

    private static IReadOnlyList<string> PackageMergeBlockCodes(
        PackageMergePlanContext context,
        WordPackagePatchApplyPolicy policy
    )
    {
        var blocks = context.Plan.Evaluate(policy).BlockCodes.ToList();
        if (
            SchemaValidationHasNewErrors(context.Validation)
            && !policy.AllowNewStructuralErrors
        )
        {
            blocks.Add("new_openxml_schema_errors");
        }
        blocks.AddRange(context.HardBlockCodes);
        return blocks.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> PackageMergeHardBlockCodes(
        WordPackageMergePlan plan,
        CandidateSchemaValidation validation,
        IReadOnlyList<string> formatHardBlocks
    )
    {
        var blocks = new List<string>(formatHardBlocks);
        if (plan.UnresolvedConflictCount != 0)
        {
            blocks.Add("unresolved_merge_conflicts");
            return blocks.Distinct(StringComparer.Ordinal).ToArray();
        }
        if (!validation.Performed && validation.NotPerformedReason == "no_changes")
        {
            return blocks.Distinct(StringComparer.Ordinal).ToArray();
        }
        if (!validation.Performed)
        {
            blocks.Add("openxml_validation_not_performed");
        }
        if (validation.ErrorsTruncated)
        {
            blocks.Add("openxml_validation_limit_exceeded");
        }
        if (validation.Issues.Any(issue =>
                issue.Id == "OPEN_XML_PACKAGE_OPEN_FAILED"
            ))
        {
            blocks.Add("candidate_not_openable_by_openxml_sdk");
        }
        return blocks.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ComputeMergeApplyPlanId(
        WordPackageMergePlan plan,
        CandidateSchemaValidation validation,
        string outputPath,
        IReadOnlyList<string> hardBlocks
    )
    {
        var fields = new List<string>
        {
            "wordtoolkit-package-merge-apply-plan-v1",
            plan.MergeId,
            plan.AncestorPackageFingerprint,
            plan.LeftPackageFingerprint,
            plan.RightPackageFingerprint,
            plan.ResultPackageFingerprint ?? string.Empty,
            Path.GetFullPath(outputPath).ToUpperInvariant(),
            validation.Performed ? "1" : "0",
            validation.NoNewErrors ? "1" : "0",
            validation.ErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validation.BaselineErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validation.CandidateErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validation.ErrorsTruncated ? "1" : "0",
            validation.NotPerformedReason ?? string.Empty,
        };
        fields.AddRange(hardBlocks.Order(StringComparer.Ordinal));
        fields.AddRange(validation.Issues
            .OrderBy(ValidationIssueKey, StringComparer.Ordinal)
            .Select(issue => string.Join(
                '\u001e',
                issue.Id ?? string.Empty,
                issue.ErrorType,
                issue.PartUri ?? string.Empty,
                issue.Path ?? string.Empty,
                issue.Node ?? string.Empty
            )));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', fields)));
        return "wtmergeapply_" + Convert.ToBase64String(digest.AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ResolvePackageMergeOutputPath(JsonElement arguments)
    {
        var rawPath = arguments.String("output_path");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "output_path must be a non-empty string"
            );
        }
        string path;
        try
        {
            path = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "output_path is not a valid filesystem path"
            );
        }
        if (!InspectWordPackageContract.IsSupportedFileName(path))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "output_path must use DOCX, DOCM, DOTX, or DOTM"
            );
        }
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                "The merge output directory does not exist"
            );
        }
        if (File.Exists(path))
        {
            throw new NativeToolException(
                "ALREADY_EXISTS",
                "The merge output already exists; merge never overwrites a file"
            );
        }
        return path;
    }

    private static string RequiredMergeApplyPlanId(JsonElement arguments)
    {
        _ = arguments.Required("expected_merge_apply_plan_id");
        var value = arguments.String("expected_merge_apply_plan_id");
        if (
            value.Length is < 20 or > 96
            || !value.StartsWith("wtmergeapply_", StringComparison.Ordinal)
            || value[13..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_merge_apply_plan_id is not a valid merge apply-plan ID"
            );
        }
        return value;
    }

    private static Task<object> ExecutePackageMergeAction(Func<object> action)
    {
        try
        {
            return ExecutePackagePatchAction(action);
        }
        catch (WordPackageMergeLimitException exception)
        {
            throw new NativeToolException(
                "MERGE_LIMIT",
                BoundForResponse(exception.Message, 512)
                    ?? "Merge safety limit exceeded"
            );
        }
        catch (WordPackageMergePreconditionException exception)
        {
            throw new NativeToolException(
                "MERGE_PRECONDITION_FAILED",
                BoundForResponse(exception.Message, 512)
                    ?? "Merge precondition failed"
            );
        }
        catch (WordPackageMergeException exception)
        {
            throw new NativeToolException(
                "MERGE_FAILED",
                BoundForResponse(exception.Message, 512) ?? "Merge failed"
            );
        }
    }

    private sealed record PackageMergePageRequest(
        string View,
        int Offset,
        int MaxItems,
        bool IncludeHashes,
        bool IncludeTextPreviews
    );

    private sealed record MergePage(
        int FilteredCount,
        int Offset,
        int? NextOffset,
        object[] Items
    )
    {
        internal static MergePage Empty { get; } = new(
            0,
            0,
            null,
            Array.Empty<object>()
        );
    }

    private sealed record PackageMergePlanContext(
        string AncestorPath,
        string LeftPath,
        string RightPath,
        string OutputPath,
        OpcPackageSnapshot AncestorPackage,
        OpcPackageSnapshot LeftPackage,
        OpcPackageSnapshot RightPackage,
        WordPackageMergePlan Plan,
        CandidateSchemaValidation Validation,
        IReadOnlyList<string> HardBlockCodes,
        string ApplyPlanId
    );
}
