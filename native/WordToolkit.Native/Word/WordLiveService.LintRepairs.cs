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
    private static Task<object> PlanPackageLintRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteLintRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var context = BuildLintRepairPlan(arguments, cancellationToken);
        return LintRepairPlanResponse(
            context,
            arguments.Boolean("include_details", false),
            started
        );
    });

    private static Task<object> ApplyPackageLintRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteLintRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var expectedApplyPlanId = RequiredLintRepairApplyPlanId(arguments);
        var context = BuildLintRepairPlan(arguments, cancellationToken);
        if (!string.Equals(
            context.ApplyPlanId,
            expectedApplyPlanId,
            StringComparison.Ordinal
        ))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "The package, finding, repair value, and output path do not reproduce the reviewed lint-repair apply-plan ID"
            );
        }
        var blockCodes = LintRepairBlockCodes(context);
        if (blockCodes.Length != 0)
        {
            throw new NativeToolException(
                "REPAIR_POLICY_BLOCKED",
                "The lint repair is blocked by package safety or candidate validation",
                new
                {
                    repair_plan_id = context.Plan.PlanId,
                    lint_repair_apply_plan_id = context.ApplyPlanId,
                    block_codes = blockCodes,
                }
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = new OpcAtomicPackageWriter().Write(
            context.OutputPath,
            context.Plan.CreateMutation(context.Package),
            new OpcAtomicWriteOptions
            {
                ExpectedDestinationFingerprint = context.Package.Fingerprint,
                ExpectedResultFingerprint = context.Plan.ResultPackageFingerprint,
                KeepBackup = false,
                RequireNewDestination = true,
            }
        );
        return new
        {
            source_file_name = Path.GetFileName(context.SourcePath),
            output_file_name = Path.GetFileName(context.OutputPath),
            output_path = context.OutputPath,
            repair_kind = "set_document_title",
            finding_id = context.Plan.FindingId,
            rule_id = context.Plan.RuleId,
            repair_plan_id = context.Plan.PlanId,
            lint_repair_apply_plan_id = context.ApplyPlanId,
            created = true,
            overwritten = false,
            source_package_fingerprint = context.Package.Fingerprint,
            package_fingerprint = result.Fingerprint,
            predicted_package_fingerprint = context.Plan.ResultPackageFingerprint,
            changed_entry_names = result.ChangedEntryNames,
            diagnostic_count = result.Diagnostics.Count,
            raw_title_returned = false,
            raw_xml_returned = false,
            word_opened = false,
            source_document_modified = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static LintRepairPlanContext BuildLintRepairPlan(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = ResolveInspectablePackagePath(arguments);
        var outputPath = ResolveLintRepairOutputPath(arguments, sourcePath);
        var expectedFingerprint = RequiredSha256(
            arguments,
            "expected_package_fingerprint"
        );
        _ = arguments.Required("finding_id");
        var findingId = arguments.String("finding_id");
        if (
            findingId.Length != 31
            || !findingId.StartsWith("wtlint_", StringComparison.Ordinal)
            || findingId[7..].Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsUpper(character)
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "finding_id must be a lowercase package-bound Word lint finding ID"
            );
        }
        _ = arguments.Required("repair_kind");
        if (!string.Equals(
            arguments.String("repair_kind"),
            "set_document_title",
            StringComparison.Ordinal
        ))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "repair_kind must be set_document_title"
            );
        }
        _ = arguments.Required("new_document_title");
        var newTitle = arguments.String("new_document_title");
        if (newTitle.Length > 255)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "new_document_title cannot exceed 255 characters"
            );
        }

        var package = new OpcPackageReader().Read(sourcePath, cancellationToken);
        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        var plan = new WordLintRepairPlanner().PlanSetDocumentTitle(
            package,
            semantic,
            expectedFingerprint,
            findingId,
            newTitle,
            cancellationToken
        );
        var schemaValidation = ValidatePackageCandidate(
            package,
            plan.CreateMutation(package),
            cancellationToken
        );
        var hasDigitalSignatures = HasDigitalSignatures(package);
        var applyPlanId = ComputeLintRepairApplyPlanId(
            plan.PlanId,
            outputPath,
            schemaValidation,
            hasDigitalSignatures
        );
        return new LintRepairPlanContext(
            sourcePath,
            outputPath,
            package,
            plan,
            schemaValidation,
            hasDigitalSignatures,
            applyPlanId
        );
    }

    private static object LintRepairPlanResponse(
        LintRepairPlanContext context,
        bool includeDetails,
        long started
    )
    {
        var blocks = LintRepairBlockCodes(context);
        return new
        {
            source_file_name = Path.GetFileName(context.SourcePath),
            output_file_name = Path.GetFileName(context.OutputPath),
            output_path = context.OutputPath,
            repair_kind = "set_document_title",
            finding_id = context.Plan.FindingId,
            rule_id = context.Plan.RuleId,
            repair_plan_id = context.Plan.PlanId,
            lint_repair_apply_plan_id = context.ApplyPlanId,
            base_package_fingerprint = context.Plan.BasePackageFingerprint,
            result_package_fingerprint = context.Plan.ResultPackageFingerprint,
            has_changes = context.Plan.HasChanges,
            changed_part_count = context.Plan.ChangedParts.Count,
            before_characters = context.Plan.BeforeCharacters,
            after_characters = context.Plan.AfterCharacters,
            before_value_fingerprint = context.Plan.BeforeValueFingerprint,
            after_value_fingerprint = context.Plan.AfterValueFingerprint,
            source = includeDetails
                ? new
                {
                    part_uri = BoundForResponse(context.Plan.SourcePartUri, 512),
                    source_element_ordinal = context.Plan.SourceElementOrdinal,
                }
                : null,
            changed_parts = includeDetails
                ? context.Plan.ChangedParts.Select(part => new
                {
                    part_uri = BoundForResponse(part.PartUri, 512),
                    before_sha256 = part.BeforeSha256,
                    after_sha256 = part.AfterSha256,
                    before_bytes = part.BeforeBytes,
                    after_bytes = part.AfterBytes,
                    byte_delta = (long)part.AfterBytes - part.BeforeBytes,
                }).ToArray()
                : null,
            validation = new
            {
                engine_passed = context.Plan.Validation.Passed,
                package_structurally_valid = context.Plan.Validation
                    .CandidatePackageStructurallyValid,
                changed_only_expected_part = context.Plan.Validation
                    .ChangedOnlyExpectedPart,
                target_finding_resolved = context.Plan.Validation
                    .TargetFindingResolved,
                openxml_performed = context.SchemaValidation.Performed,
                openxml_candidate_valid = context.SchemaValidation.CandidateValid,
                openxml_no_new_errors = context.SchemaValidation.NoNewErrors,
                openxml_baseline_error_count = context.SchemaValidation
                    .BaselineErrorCount,
                openxml_candidate_error_count = context.SchemaValidation
                    .CandidateErrorCount,
                openxml_new_error_count = context.SchemaValidation.ErrorCount,
                openxml_errors_truncated = context.SchemaValidation.ErrorsTruncated,
                openxml_issues = includeDetails
                    ? context.SchemaValidation.Issues.Take(20)
                        .Select(ValidationIssueItem).ToArray()
                    : null,
            },
            apply_blocked = blocks.Length != 0,
            apply_block_codes = blocks,
            output_must_not_exist = true,
            raw_title_returned = false,
            raw_xml_returned = false,
            word_opened = false,
            source_document_modified = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    }

    private static string[] LintRepairBlockCodes(LintRepairPlanContext context)
    {
        var blocks = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blocks.Add("digital_signature_present");
        }
        if (!context.Plan.Validation.Passed)
        {
            blocks.Add("engine_validation_failed");
        }
        if (!context.SchemaValidation.Performed)
        {
            blocks.Add("openxml_validation_not_performed");
        }
        else if (!context.SchemaValidation.NoNewErrors)
        {
            blocks.Add("openxml_new_validation_errors");
        }
        if (context.SchemaValidation.ErrorsTruncated)
        {
            blocks.Add("openxml_validation_limit_exceeded");
        }
        return blocks.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ResolveLintRepairOutputPath(
        JsonElement arguments,
        string sourcePath
    )
    {
        _ = arguments.Required("output_path");
        var rawPath = arguments.String("output_path");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "output_path must be a non-empty path"
            );
        }
        string outputPath;
        try
        {
            outputPath = Path.GetFullPath(rawPath);
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
        if (!InspectWordPackageContract.IsSupportedFileName(outputPath))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "output_path must use DOCX, DOCM, DOTX, or DOTM"
            );
        }
        if (!string.Equals(
            Path.GetExtension(outputPath),
            Path.GetExtension(sourcePath),
            StringComparison.OrdinalIgnoreCase
        ))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "output_path must preserve the source package extension"
            );
        }
        if (string.Equals(outputPath, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "lint repair requires a new output path and never overwrites the source"
            );
        }
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                "The lint-repair output directory does not exist"
            );
        }
        if (File.Exists(outputPath))
        {
            throw new NativeToolException(
                "ALREADY_EXISTS",
                "The lint-repair output already exists; repair never overwrites a file"
            );
        }
        return outputPath;
    }

    private static string ComputeLintRepairApplyPlanId(
        string repairPlanId,
        string outputPath,
        CandidateSchemaValidation validation,
        bool hasDigitalSignatures
    )
    {
        var fields = new[]
        {
            repairPlanId,
            outputPath.ToUpperInvariant(),
            validation.Performed.ToString(),
            validation.NoNewErrors.ToString(),
            validation.ErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validation.ErrorsTruncated.ToString(),
            hasDigitalSignatures.ToString(),
        };
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\u001f', fields))
        );
        return "wtlintapply_" + Convert.ToBase64String(digest.AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string RequiredLintRepairApplyPlanId(JsonElement arguments)
    {
        _ = arguments.Required("expected_lint_repair_apply_plan_id");
        var value = arguments.String("expected_lint_repair_apply_plan_id");
        if (
            value.Length is < 20 or > 96
            || !value.StartsWith("wtlintapply_", StringComparison.Ordinal)
            || value[12..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_lint_repair_apply_plan_id is not a valid lint-repair apply-plan ID"
            );
        }
        return value;
    }

    private static Task<object> ExecuteLintRepairAction(Func<object> action)
    {
        try
        {
            return Task.FromResult(action());
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (WordLintRepairLimitException exception)
        {
            throw new NativeToolException(
                "REPAIR_LIMIT",
                BoundForResponse(exception.Message, 512) ?? "Lint repair limit exceeded"
            );
        }
        catch (WordLintRepairPreconditionException exception)
        {
            throw new NativeToolException(
                "STALE_LINT_FINDING",
                BoundForResponse(exception.Message, 512) ?? "Lint repair evidence is stale"
            );
        }
        catch (WordLintRepairValidationException exception)
        {
            throw new NativeToolException(
                "REPAIR_VALIDATION_FAILED",
                BoundForResponse(exception.Message, 512) ?? "Lint repair validation failed"
            );
        }
        catch (WordLintRepairException exception)
        {
            throw new NativeToolException(
                "UNSAFE_REPAIR",
                BoundForResponse(exception.Message, 512) ?? "Lint repair is unsafe"
            );
        }
        catch (WordSemanticLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageConcurrencyException exception)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The lint-repair output changed during the atomic write",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageResultMismatchException exception)
        {
            throw new NativeToolException(
                "RESULT_MISMATCH",
                "The written package does not match the reviewed lint-repair plan",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageValidationException exception)
        {
            throw new NativeToolException(
                "VALIDATION_FAILED",
                "The lint-repair candidate failed structural validation",
                new { diagnostic_count = exception.Diagnostics.Count }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded OPC safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                BoundForResponse(exception.Message, 512) ?? "Invalid lint repair request"
            );
        }
        catch (InvalidDataException exception)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The lint-repair package cannot be read or written with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The lint repair could not be planned or written",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private sealed record LintRepairPlanContext(
        string SourcePath,
        string OutputPath,
        OpcPackageSnapshot Package,
        WordLintRepairPlan Plan,
        CandidateSchemaValidation SchemaValidation,
        bool HasDigitalSignatures,
        string ApplyPlanId
    );
}
