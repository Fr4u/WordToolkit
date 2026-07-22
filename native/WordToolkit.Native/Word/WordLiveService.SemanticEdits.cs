using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> PlanPackageSemanticEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageTextAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var path = ResolveInspectablePackagePath(arguments);
        var context = BuildPackageSemanticEditPlan(path, arguments, cancellationToken);
        return SemanticEditPlanResponse(
            context,
            path,
            arguments.Boolean("include_details", false),
            started
        );
    });

    private static Task<object> ApplyPackageSemanticEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageTextAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var path = ResolveInspectablePackagePath(arguments);
        var expectedPlanId = RequiredSemanticEditPlanId(arguments);
        var context = BuildPackageSemanticEditPlan(path, arguments, cancellationToken);
        if (!string.Equals(context.Plan.PlanId, expectedPlanId, StringComparison.Ordinal))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "Commands do not reproduce the reviewed semantic edit plan ID"
            );
        }
        if (context.HasDigitalSignatures)
        {
            throw new NativeToolException(
                "SIGNED_PACKAGE",
                "Direct OOXML editing is blocked because the package contains digital signatures"
            );
        }
        if (!context.Validation.NoNewErrors)
        {
            throw new NativeToolException(
                "OOXML_SCHEMA_INVALID",
                "The exact candidate package introduces Microsoft Open XML schema errors",
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
                microsoft_schema_valid = context.Validation.CandidateValid,
                microsoft_schema_no_new_errors = context.Validation.NoNewErrors,
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

        var result = new OpcAtomicPackageWriter().Write(
            path,
            context.Plan.CreateMutation(context.Package),
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
            applied = true,
            no_op = false,
            operation_count = context.Plan.OperationCount,
            previous_package_fingerprint = context.Package.Fingerprint,
            package_fingerprint = result.Fingerprint,
            predicted_package_fingerprint = context.Plan.ResultPackageFingerprint,
            backup_path = result.BackupPath,
            changed_entry_names = result.ChangedEntryNames,
            diagnostic_count = result.Diagnostics.Count,
            microsoft_schema_valid = context.Validation.CandidateValid,
            microsoft_schema_no_new_errors = context.Validation.NoNewErrors,
            raw_xml_returned = false,
            mutation_performed = true,
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static object SemanticEditPlanResponse(
        PackageSemanticEditPlanContext context,
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
        if (!context.Validation.NoNewErrors)
        {
            blockedReasons.Add("microsoft_schema_validation_failed");
        }
        return new
        {
            file_name = Path.GetFileName(path),
            plan_id = context.Plan.PlanId,
            base_package_fingerprint = context.Plan.BasePackageFingerprint,
            result_package_fingerprint = context.Plan.ResultPackageFingerprint,
            operation_count = context.Plan.OperationCount,
            changed_operation_count = context.Plan.ChangedOperationCount,
            changed_part_count = context.Plan.ChangedPartCount,
            total_xml_byte_delta = context.Plan.TotalXmlByteDelta,
            has_changes = context.Plan.HasChanges,
            can_apply = blockedReasons.Count == 0,
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
            operations = includeDetails
                ? context.Plan.Operations.Select(operation => new
                {
                    index = operation.Index,
                    kind = operation.Kind,
                    node_id = operation.NodeId.Value,
                    property_name = operation.PropertyName,
                    before_value = BoundForResponse(operation.BeforeValue, 253),
                    after_value = BoundForResponse(operation.AfterValue, 253),
                    source_part_uri = BoundForResponse(operation.SourcePartUri, 512),
                    source_element_ordinal = operation.SourceElementOrdinal,
                    xml_byte_delta = operation.XmlByteDelta,
                    has_change = operation.HasChange,
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

    private static PackageSemanticEditPlanContext BuildPackageSemanticEditPlan(
        string path,
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commands = ParseSemanticEditCommands(arguments);
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
                "Saved package changed before the semantic edit plan was built."
            );
        }
        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        var plan = new WordSemanticTransactionPlanner(
            new WordSemanticTransactionOptions { MaxCommands = 200 }
        ).PlanStyleAssignments(package, semantic, commands, cancellationToken);
        var validation = ValidatePackageCandidate(
            package,
            plan.CreateMutation(package),
            cancellationToken
        );
        return new PackageSemanticEditPlanContext(
            package,
            plan,
            HasDigitalSignatures(package),
            validation
        );
    }

    private static IReadOnlyList<WordStyleAssignmentCommand> ParseSemanticEditCommands(
        JsonElement arguments
    )
    {
        var array = arguments.RequiredArray("commands");
        if (array.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "commands must contain between 1 and 200 semantic edits"
            );
        }
        var result = new List<WordStyleAssignmentCommand>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Every semantic edit command must be an object"
                );
            }
            foreach (var property in item.EnumerateObject())
            {
                if (property.Name is not "type"
                    and not "node_id"
                    and not "style_id"
                    and not "expected_style_id"
                    and not "require_no_explicit_style")
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "A semantic edit command contains an unknown property"
                    );
                }
            }
            _ = item.Required("type");
            _ = item.Required("node_id");
            _ = item.Required("style_id");
            if (!string.Equals(item.String("type"), "set_style", StringComparison.Ordinal))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "The current semantic edit command type must be set_style"
                );
            }
            var nodeId = item.String("node_id");
            ValidateSemanticNodeId(nodeId);
            var styleId = item.String("style_id");
            var expectedStyleId = OptionalString(item, "expected_style_id");
            var requireNoExplicitStyle = item.Boolean(
                "require_no_explicit_style",
                false
            );
            if (
                string.IsNullOrWhiteSpace(styleId)
                || styleId.Length > 253
                || expectedStyleId is not null
                    && (string.IsNullOrWhiteSpace(expectedStyleId) || expectedStyleId.Length > 253)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "style_id and expected_style_id must contain between 1 and 253 characters"
                );
            }
            if (requireNoExplicitStyle && expectedStyleId is not null)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Use expected_style_id or require_no_explicit_style, never both"
                );
            }
            result.Add(
                new WordStyleAssignmentCommand(
                    new SemanticNodeId(nodeId),
                    styleId,
                    expectedStyleId,
                    requireNoExplicitStyle
                )
            );
        }
        return result;
    }

    private static void ValidateSemanticNodeId(string nodeId)
    {
        if (
            nodeId.Length is < 5 or > 128
            || !nodeId.StartsWith("wdn_", StringComparison.Ordinal)
            || nodeId[4..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "node_id is not a valid semantic node ID"
            );
        }
    }

    private static string RequiredSemanticEditPlanId(JsonElement arguments)
    {
        _ = arguments.Required("expected_plan_id");
        var value = arguments.String("expected_plan_id");
        if (
            value.Length is < 12 or > 128
            || !value.StartsWith("wseplan_", StringComparison.Ordinal)
            || value[8..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_plan_id is not a valid semantic edit plan ID"
            );
        }
        return value;
    }

    private sealed record PackageSemanticEditPlanContext(
        OpcPackageSnapshot Package,
        WordSemanticTransactionPlan Plan,
        bool HasDigitalSignatures,
        CandidateSchemaValidation Validation
    );
}
