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
    private static readonly HashSet<string> FormatterPlanArguments = new(
        [
            "local_path",
            "output_path",
            "expected_package_fingerprint",
            "policies",
            "include_details",
            "include_source",
            "detail_offset",
            "detail_limit",
        ],
        StringComparer.Ordinal
    );

    private static readonly HashSet<string> FormatterApplyArguments = new(
        [
            "local_path",
            "output_path",
            "expected_package_fingerprint",
            "policies",
            "expected_formatter_apply_plan_id",
        ],
        StringComparer.Ordinal
    );

    private static Task<object> PlanPackageFormatAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteFormatterAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        ValidateFormatterArgumentNames(arguments, FormatterPlanArguments);
        var responseOptions = ReadFormatterResponseOptions(arguments);
        var context = BuildFormatterPlan(arguments, cancellationToken);
        return FormatterPlanResponse(context, responseOptions, started);
    });

    private static Task<object> ApplyPackageFormatAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteFormatterAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        ValidateFormatterArgumentNames(arguments, FormatterApplyArguments);
        var expectedApplyPlanId = RequiredFormatterApplyPlanId(arguments);
        var context = BuildFormatterPlan(arguments, cancellationToken);
        if (!string.Equals(
            context.ApplyPlanId,
            expectedApplyPlanId,
            StringComparison.Ordinal
        ))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "The package, policy set, validation result, and output path do not reproduce the reviewed formatter apply-plan ID"
            );
        }

        if (!context.Plan.HasChanges)
        {
            return new
            {
                operation_contract = "wordtoolkit.apply_ooxml_format/1.0",
                source_file_name = Path.GetFileName(context.SourcePath),
                output_file_name = Path.GetFileName(context.OutputPath),
                formatter_plan_id = context.Plan.PlanId,
                formatter_apply_plan_id = context.ApplyPlanId,
                created = false,
                no_op = true,
                overwritten = false,
                source_package_fingerprint = context.Package.Fingerprint,
                package_fingerprint = context.Package.Fingerprint,
                predicted_package_fingerprint = context.Plan.ResultPackageFingerprint,
                changed_entry_names = Array.Empty<string>(),
                raw_text_returned = false,
                raw_xml_returned = false,
                word_opened = false,
                source_document_modified = false,
                mutation_performed = false,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            };
        }

        var blockCodes = FormatterBlockCodes(context);
        if (blockCodes.Length != 0)
        {
            throw new NativeToolException(
                "FORMAT_POLICY_BLOCKED",
                "The formatter apply is blocked by package safety or candidate validation",
                new
                {
                    formatter_plan_id = context.Plan.PlanId,
                    formatter_apply_plan_id = context.ApplyPlanId,
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
            operation_contract = "wordtoolkit.apply_ooxml_format/1.0",
            source_file_name = Path.GetFileName(context.SourcePath),
            output_file_name = Path.GetFileName(context.OutputPath),
            output_path = context.OutputPath,
            formatter_plan_id = context.Plan.PlanId,
            formatter_apply_plan_id = context.ApplyPlanId,
            created = true,
            no_op = false,
            overwritten = false,
            source_package_fingerprint = context.Package.Fingerprint,
            package_fingerprint = result.Fingerprint,
            predicted_package_fingerprint = context.Plan.ResultPackageFingerprint,
            removed_element_count = context.Plan.RemovedElementCount,
            removed_byte_count = context.Plan.RemovedByteCount,
            changed_entry_names = result.ChangedEntryNames,
            diagnostic_count = result.Diagnostics.Count,
            raw_text_returned = false,
            raw_xml_returned = false,
            word_opened = false,
            source_document_modified = false,
            mutation_performed = true,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static FormatterPlanContext BuildFormatterPlan(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = ResolveInspectablePackagePath(arguments);
        var outputPath = ResolveFormatterOutputPath(arguments, sourcePath);
        var expectedFingerprint = RequiredSha256(
            arguments,
            "expected_package_fingerprint"
        );
        var policies = RequiredFormatterPolicies(arguments);
        var package = new OpcPackageReader().Read(sourcePath, cancellationToken);
        var semantic = new WordSemanticProjector().Project(
            package,
            cancellationToken
        );
        if (
            !package.Parts.TryGetValue(semantic.MainPartUri, out var mainPart)
            || !WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
                sourcePath,
                mainPart.ContentType
            )
        )
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The filename extension does not match the Word main-part content type"
            );
        }

        var plan = new WordFormatterPlanner().Plan(
            package,
            semantic,
            expectedFingerprint,
            policies,
            cancellationToken
        );
        var schemaValidation = ValidatePackageCandidate(
            package,
            plan.CreateMutation(package),
            cancellationToken
        );
        var hasDigitalSignatures = HasDigitalSignatures(package);
        var applyPlanId = ComputeFormatterApplyPlanId(
            plan.PlanId,
            outputPath,
            schemaValidation,
            hasDigitalSignatures
        );
        return new FormatterPlanContext(
            sourcePath,
            outputPath,
            package,
            plan,
            schemaValidation,
            hasDigitalSignatures,
            applyPlanId
        );
    }

    private static object FormatterPlanResponse(
        FormatterPlanContext context,
        FormatterResponseOptions responseOptions,
        long started
    )
    {
        var includeDetails = responseOptions.IncludeDetails;
        var includeSource = responseOptions.IncludeSource;
        var offset = responseOptions.Offset;
        var limit = responseOptions.Limit;
        var detailPage = context.Plan.Changes.Skip(offset).Take(limit).ToArray();
        var blocks = FormatterBlockCodes(context);
        return new
        {
            operation_contract = "wordtoolkit.plan_ooxml_format/1.0",
            source_file_name = Path.GetFileName(context.SourcePath),
            output_file_name = Path.GetFileName(context.OutputPath),
            formatter_plan_id = context.Plan.PlanId,
            formatter_apply_plan_id = context.ApplyPlanId,
            base_package_fingerprint = context.Plan.BasePackageFingerprint,
            result_package_fingerprint = context.Plan.ResultPackageFingerprint,
            policies = context.Plan.Policies.Select(FormatterPolicyName).ToArray(),
            has_changes = context.Plan.HasChanges,
            removed_element_count = context.Plan.RemovedElementCount,
            removed_byte_count = context.Plan.RemovedByteCount,
            changed_part_count = context.Plan.ChangedParts.Count,
            scan = new
            {
                semantic_nodes = context.Plan.SemanticNodesScanned,
                direct_formatting_nodes = context.Plan.DirectFormattingNodesScanned,
                candidate_elements = context.Plan.CandidateElementsScanned,
            },
            detail_page = includeDetails
                ? new
                {
                    offset,
                    limit,
                    returned_count = detailPage.Length,
                    total_count = context.Plan.Changes.Count,
                    truncated = offset + detailPage.Length < context.Plan.Changes.Count,
                    items = detailPage.Select(change => new
                    {
                        change_index = change.Index,
                        policy = FormatterPolicyName(change.Policy),
                        node_id = change.NodeId.Value,
                        node_kind = ToSnakeCase(change.NodeKind.ToString()),
                        property_element = change.PropertyElementName,
                        property_count = change.PropertyCount,
                        removed_bytes = change.RemovedBytes,
                        source_element_fingerprint = change.SourceElementFingerprint,
                        source = includeSource
                            ? new
                            {
                                part_uri = BoundForResponse(change.SourcePartUri, 512),
                                source_element_ordinal = change.SourceElementOrdinal,
                                property_element_ordinal = change.PropertyElementOrdinal,
                            }
                            : null,
                    }).ToArray(),
                }
                : null,
            changed_parts = includeDetails
                ? context.Plan.ChangedParts.Select(part => new
                {
                    part_uri = includeSource
                        ? BoundForResponse(part.PartUri, 512)
                        : null,
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
                base_package_structurally_valid = context.Plan.Validation
                    .BasePackageStructurallyValid,
                candidate_package_structurally_valid = context.Plan.Validation
                    .CandidatePackageStructurallyValid,
                candidate_fingerprint_matched = context.Plan.Validation
                    .CandidateFingerprintMatched,
                changed_only_planned_parts = context.Plan.Validation
                    .ChangedOnlyPlannedParts,
                semantic_content_preserved = context.Plan.Validation
                    .SemanticContentPreserved,
                effective_formatting_preserved = context.Plan.Validation
                    .EffectiveFormattingPreserved,
                affected_node_count = context.Plan.Validation.AffectedNodeCount,
                candidate_package_error_count = context.Plan.Validation
                    .CandidatePackageErrorCount,
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
            apply_blocked = context.Plan.HasChanges && blocks.Length != 0,
            apply_block_codes = context.Plan.HasChanges ? blocks : Array.Empty<string>(),
            output_must_not_exist = true,
            no_incidental_formatting_on_save = true,
            raw_text_returned = false,
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

    private static void ValidateFormatterArgumentNames(
        JsonElement arguments,
        IReadOnlySet<string> allowedNames
    )
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "formatter arguments must be an object"
            );
        }
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowedNames.Contains(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "formatter request contains an unsupported argument",
                    new { field = property.Name }
                );
            }
        }
    }

    private static FormatterResponseOptions ReadFormatterResponseOptions(
        JsonElement arguments
    )
    {
        var offset = arguments.NullableInt64("detail_offset") ?? 0;
        var limit = arguments.NullableInt64("detail_limit") ?? 50;
        if (offset is < 0 or > 1_000_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail_offset must be between 0 and 1000000"
            );
        }
        if (limit is < 1 or > 100)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail_limit must be between 1 and 100"
            );
        }
        return new FormatterResponseOptions(
            arguments.Boolean("include_details", false),
            arguments.Boolean("include_source", false),
            checked((int)offset),
            checked((int)limit)
        );
    }

    private static IReadOnlyList<WordFormatterPolicy> RequiredFormatterPolicies(
        JsonElement arguments
    )
    {
        var raw = arguments.Required("policies");
        if (raw.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "policies must be an array"
            );
        }
        var values = raw.EnumerateArray().ToArray();
        if (values.Length is < 1 or > 8)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "policies must contain between 1 and 8 items"
            );
        }
        var result = new HashSet<WordFormatterPolicy>();
        foreach (var value in values)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "every formatter policy must be a string"
                );
            }
            var parsed = value.GetString() switch
            {
                "remove_redundant_direct_formatting" =>
                    WordFormatterPolicy.RemoveRedundantDirectFormatting,
                _ => throw new NativeToolException(
                    "INVALID_INPUT",
                    "unsupported formatter policy"
                ),
            };
            if (!result.Add(parsed))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "policies contains a duplicate item"
                );
            }
        }
        return result.Order().ToArray();
    }

    private static string FormatterPolicyName(WordFormatterPolicy policy) => policy switch
    {
        WordFormatterPolicy.RemoveRedundantDirectFormatting =>
            "remove_redundant_direct_formatting",
        _ => throw new InvalidOperationException("Unknown formatter policy."),
    };

    private static string[] FormatterBlockCodes(FormatterPlanContext context)
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

    private static string ResolveFormatterOutputPath(
        JsonElement arguments,
        string sourcePath
    )
    {
        var rawPath = arguments.Required("output_path").GetString();
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
                "formatting requires a new output path and never overwrites the source"
            );
        }
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                "the formatter output directory does not exist"
            );
        }
        if (File.Exists(outputPath))
        {
            throw new NativeToolException(
                "ALREADY_EXISTS",
                "the formatter output already exists; formatting never overwrites a file"
            );
        }
        return outputPath;
    }

    private static string ComputeFormatterApplyPlanId(
        string formatterPlanId,
        string outputPath,
        CandidateSchemaValidation validation,
        bool hasDigitalSignatures
    )
    {
        var fields = new[]
        {
            formatterPlanId,
            outputPath.ToUpperInvariant(),
            validation.Performed.ToString(),
            validation.NoNewErrors.ToString(),
            validation.ErrorCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            validation.ErrorsTruncated.ToString(),
            hasDigitalSignatures.ToString(),
        };
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\u001f', fields))
        );
        return "wtfmtapply_" + Convert.ToBase64String(digest.AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string RequiredFormatterApplyPlanId(JsonElement arguments)
    {
        var value = arguments.Required("expected_formatter_apply_plan_id").GetString();
        if (
            value is null
            || value.Length is < 20 or > 96
            || !value.StartsWith("wtfmtapply_", StringComparison.Ordinal)
            || value[11..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_formatter_apply_plan_id is not a valid formatter apply-plan ID"
            );
        }
        return value;
    }

    private static Task<object> ExecuteFormatterAction(Func<object> action)
    {
        try
        {
            return Task.FromResult(action());
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (WordFormatterLimitException exception)
        {
            throw new NativeToolException(
                "FORMATTER_LIMIT",
                BoundForResponse(exception.Message, 512)
                    ?? "Formatter safety limit exceeded"
            );
        }
        catch (WordFormatterPreconditionException exception)
        {
            throw new NativeToolException(
                "FORMATTER_PRECONDITION",
                BoundForResponse(exception.Message, 512)
                    ?? "Formatter precondition failed"
            );
        }
        catch (WordFormatterValidationException exception)
        {
            throw new NativeToolException(
                "FORMATTER_VALIDATION_FAILED",
                BoundForResponse(exception.Message, 512)
                    ?? "Formatter validation failed",
                new
                {
                    exception.Validation.SemanticContentPreserved,
                    exception.Validation.EffectiveFormattingPreserved,
                    exception.Validation.ChangedOnlyPlannedParts,
                    exception.Validation.CandidateFingerprintMatched,
                }
            );
        }
        catch (WordFormatterException exception)
        {
            throw new NativeToolException(
                "UNSAFE_FORMAT",
                BoundForResponse(exception.Message, 512)
                    ?? "Formatter candidate is unsafe"
            );
        }
        catch (Exception exception) when (IsFormattingGraphFailure(exception))
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package formatting cascade cannot be resolved safely",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageConcurrencyException exception)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The formatter output changed during the atomic write",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageResultMismatchException exception)
        {
            throw new NativeToolException(
                "RESULT_MISMATCH",
                "The written package does not match the reviewed formatter plan",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageValidationException exception)
        {
            throw new NativeToolException(
                "VALIDATION_FAILED",
                "The formatter candidate failed structural validation",
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
                BoundForResponse(exception.Message, 512)
                    ?? "Invalid formatter request"
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
                "The formatter package cannot be read or written with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The formatter operation could not be planned or written",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static bool IsFormattingGraphFailure(Exception exception) => exception is
        WordSemanticLimitException
        or WordSemanticProjectionException
        or WordStyleLimitException
        or WordStyleProjectionException
        or WordNumberingLimitException
        or WordNumberingProjectionException
        or WordNumberingResolutionException
        or WordThemeLimitException
        or WordThemeProjectionException
        or WordThemeResolutionException
        or WordSettingsLimitException
        or WordSettingsProjectionException
        or WordFontTableLimitException
        or WordFontTableProjectionException
        or WordFormattingLimitException
        or WordFormattingResolutionException;

    private sealed record FormatterPlanContext(
        string SourcePath,
        string OutputPath,
        OpcPackageSnapshot Package,
        WordFormatterPlan Plan,
        CandidateSchemaValidation SchemaValidation,
        bool HasDigitalSignatures,
        string ApplyPlanId
    );

    private sealed record FormatterResponseOptions(
        bool IncludeDetails,
        bool IncludeSource,
        int Offset,
        int Limit
    );
}
