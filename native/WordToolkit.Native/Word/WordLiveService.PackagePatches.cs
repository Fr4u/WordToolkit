using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> PlanPackagePatchAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackagePatchAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = ParsePackagePatchPageRequest(arguments, allowSchemaView: false);
        var context = BuildPackagePatchPlan(arguments, cancellationToken);
        return PackagePatchPlanResponse(context, request, started);
    });

    private static Task<object> CreatePackagePatchAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackagePatchAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        _ = RequiredSha256(arguments, "expected_before_fingerprint");
        _ = RequiredSha256(arguments, "expected_after_fingerprint");
        var expectedPatchId = RequiredPackagePatchId(arguments, "expected_patch_id");
        var outputPath = ResolvePackagePatchPath(
            arguments,
            "patch_path",
            mustExist: false
        );
        var context = BuildPackagePatchPlan(arguments, cancellationToken);
        if (!string.Equals(
                context.Plan.Patch.PatchId,
                expectedPatchId,
                StringComparison.Ordinal
            ))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "The current documents do not reproduce the reviewed patch ID"
            );
        }

        var artifact = WritePackagePatchArtifact(
            outputPath,
            context.Plan.Patch,
            cancellationToken
        );
        return new
        {
            before_file_name = Path.GetFileName(context.BeforePath),
            after_file_name = Path.GetFileName(context.AfterPath),
            patch_file_name = Path.GetFileName(outputPath),
            patch_path = outputPath,
            patch_id = context.Plan.Patch.PatchId,
            base_package_fingerprint = context.Plan.Patch.BasePackageFingerprint,
            result_package_fingerprint = context.Plan.Patch.ResultPackageFingerprint,
            operation_count = context.Plan.Patch.OperationCount,
            payload_count = context.Plan.Patch.PayloadCount,
            payload_bytes = context.Plan.Patch.PayloadBytes,
            artifact_bytes = artifact.Bytes,
            artifact_sha256 = artifact.Sha256,
            created = true,
            overwritten = false,
            reversible = true,
            entry_payload_exact = true,
            zip_container_byte_exact = false,
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

    private static Task<object> InspectPackagePatchAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackagePatchAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = ParsePackagePatchPageRequest(arguments, allowSchemaView: false);
        if (request.View == "risks")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Artifact inspection supports summary or operations; risk requires an exact base package"
            );
        }
        var path = ResolvePackagePatchPath(arguments, "patch_path", mustExist: true);
        var patch = ReadPackagePatch(path, cancellationToken);
        VerifyOptionalPatchId(arguments, patch.PatchId);
        var page = PackagePatchOperationPage(patch, request);
        return new
        {
            patch_file_name = Path.GetFileName(path),
            patch_id = patch.PatchId,
            format = OpcPackagePatch.Format,
            format_version = OpcPackagePatch.FormatVersion,
            base_package_fingerprint = patch.BasePackageFingerprint,
            result_package_fingerprint = patch.ResultPackageFingerprint,
            operation_count = patch.OperationCount,
            operation_counts = new
            {
                added = patch.AddedEntryCount,
                replaced = patch.ReplacedEntryCount,
                deleted = patch.DeletedEntryCount,
            },
            payload_count = patch.PayloadCount,
            payload_bytes = patch.PayloadBytes,
            artifact_bytes = new FileInfo(path).Length,
            artifact_sha256 = HashFile(path),
            no_op = patch.IsNoOp,
            reversible = true,
            entry_payload_exact = true,
            zip_container_byte_exact = false,
            view = request.View,
            filtered_item_count = page.FilteredCount,
            offset = page.Offset,
            returned_item_count = page.Items.Length,
            next_offset = page.NextOffset,
            items = page.Items,
            hashes_included = request.IncludeHashes,
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
    });

    private static Task<object> PlanPackagePatchApplyAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackagePatchAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = ParsePackagePatchPageRequest(arguments, allowSchemaView: true);
        var context = BuildPackagePatchApplyPlan(arguments, cancellationToken);
        return PackagePatchApplyPlanResponse(context, request, started);
    });

    private static Task<object> ApplyPackagePatchAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackagePatchAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var expectedApplyPlanId = RequiredApplyPlanId(arguments);
        var context = BuildPackagePatchApplyPlan(arguments, cancellationToken);
        if (!string.Equals(
                context.ApplyPlanId,
                expectedApplyPlanId,
                StringComparison.Ordinal
            ))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "The current package and patch do not reproduce the reviewed apply-plan ID"
            );
        }

        var policy = ParsePackagePatchPolicy(arguments);
        var blocks = PackagePatchBlockCodes(context, policy);
        if (blocks.Count != 0)
        {
            throw new NativeToolException(
                "PATCH_POLICY_BLOCKED",
                "The patch requires authorization or failed a non-overridable safety check",
                new
                {
                    patch_id = context.Patch.PatchId,
                    apply_plan_id = context.ApplyPlanId,
                    block_codes = blocks,
                }
            );
        }

        if (context.Patch.IsNoOp)
        {
            return new
            {
                file_name = Path.GetFileName(context.PackagePath),
                patch_file_name = Path.GetFileName(context.PatchPath),
                patch_id = context.Patch.PatchId,
                apply_plan_id = context.ApplyPlanId,
                applied = false,
                no_op = true,
                package_fingerprint = context.BasePackage.Fingerprint,
                backup_path = (string?)null,
                changed_entry_names = Array.Empty<string>(),
                word_opened = false,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var mutation = context.Patch.CreateMutation(
            context.BasePackage,
            cancellationToken
        );
        var result = new OpcAtomicPackageWriter().Write(
            context.PackagePath,
            mutation,
            new OpcAtomicWriteOptions
            {
                ExpectedDestinationFingerprint = context.BasePackage.Fingerprint,
                ExpectedResultFingerprint = context.Patch.ResultPackageFingerprint,
                AllowStructuralErrors = !context.CandidatePackage.IsStructurallyValid,
                KeepBackup = arguments.Boolean("keep_backup", true),
            }
        );
        return new
        {
            file_name = Path.GetFileName(context.PackagePath),
            patch_file_name = Path.GetFileName(context.PatchPath),
            patch_id = context.Patch.PatchId,
            apply_plan_id = context.ApplyPlanId,
            applied = true,
            no_op = false,
            previous_package_fingerprint = context.BasePackage.Fingerprint,
            package_fingerprint = result.Fingerprint,
            predicted_package_fingerprint = context.Patch.ResultPackageFingerprint,
            backup_path = result.BackupPath,
            changed_entry_names = result.ChangedEntryNames,
            diagnostic_count = result.Diagnostics.Count,
            digital_signatures_may_be_invalidated =
                context.Plan.RiskAssessment.DigitalSignaturesPresent,
            explicit_authorizations = ExplicitAuthorizationNames(policy),
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static PackagePatchPlanContext BuildPackagePatchPlan(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var beforePath = ResolveComparedPackagePath(arguments, "before_path");
        var afterPath = ResolveComparedPackagePath(arguments, "after_path");
        var reader = new OpcPackageReader();
        var projector = new WordSemanticProjector();
        var beforePackage = reader.Read(beforePath, cancellationToken);
        VerifyOptionalFingerprint(
            beforePackage.Fingerprint,
            OptionalFingerprint(arguments, "expected_before_fingerprint"),
            "before"
        );
        var beforeDocument = projector.Project(beforePackage, cancellationToken);
        var afterPackage = reader.Read(afterPath, cancellationToken);
        VerifyOptionalFingerprint(
            afterPackage.Fingerprint,
            OptionalFingerprint(arguments, "expected_after_fingerprint"),
            "after"
        );
        var afterDocument = projector.Project(afterPackage, cancellationToken);
        var plan = new WordPackagePatchPlanner().Plan(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument,
            cancellationToken
        );
        return new PackagePatchPlanContext(
            beforePath,
            afterPath,
            beforePackage,
            afterPackage,
            plan
        );
    }

    private static PackagePatchApplyContext BuildPackagePatchApplyPlan(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packagePath = ResolveInspectablePackagePath(arguments);
        var patchPath = ResolvePackagePatchPath(arguments, "patch_path", mustExist: true);
        var expectedFingerprint = RequiredSha256(
            arguments,
            "expected_package_fingerprint"
        );
        var expectedPatchId = RequiredPackagePatchId(arguments, "expected_patch_id");
        var patch = ReadPackagePatch(patchPath, cancellationToken);
        if (!string.Equals(patch.PatchId, expectedPatchId, StringComparison.Ordinal))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "The patch artifact does not match expected_patch_id"
            );
        }

        var reader = new OpcPackageReader();
        var package = reader.Read(packagePath, cancellationToken);
        if (!string.Equals(
                package.Fingerprint,
                expectedFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The destination package does not match expected_package_fingerprint"
            );
        }
        var document = new WordSemanticProjector().Project(
            package,
            cancellationToken
        );
        var plan = new WordPackagePatchPlanner().PlanApply(
            package,
            document,
            patch,
            out var candidate,
            cancellationToken
        );
        var validation = patch.IsNoOp
            ? CandidateSchemaValidation.NotPerformed("no_changes")
            : ValidatePackageCandidate(
                package,
                new OpcPackageMutationBuilder(candidate),
                cancellationToken
            );
        var formatHardBlocks = PackagePatchFormatHardBlockCodes(
            packagePath,
            candidate
        );
        var applyPlanId = ComputeApplyPlanId(
            plan,
            validation,
            packagePath,
            formatHardBlocks
        );
        return new PackagePatchApplyContext(
            packagePath,
            patchPath,
            package,
            candidate,
            patch,
            plan,
            validation,
            formatHardBlocks,
            applyPlanId
        );
    }

    private static object PackagePatchPlanResponse(
        PackagePatchPlanContext context,
        PackagePatchPageRequest request,
        long started
    )
    {
        var page = PackagePatchPlanPage(context.Plan, request, schema: null);
        var defaultDecision = context.Plan.Evaluate();
        return new
        {
            before_file_name = Path.GetFileName(context.BeforePath),
            after_file_name = Path.GetFileName(context.AfterPath),
            patch_id = context.Plan.Patch.PatchId,
            base_package_fingerprint = context.Plan.Patch.BasePackageFingerprint,
            result_package_fingerprint = context.Plan.Patch.ResultPackageFingerprint,
            operation_count = context.Plan.Patch.OperationCount,
            operation_counts = new
            {
                added = context.Plan.Patch.AddedEntryCount,
                replaced = context.Plan.Patch.ReplacedEntryCount,
                deleted = context.Plan.Patch.DeletedEntryCount,
            },
            payload_count = context.Plan.Patch.PayloadCount,
            payload_bytes = context.Plan.Patch.PayloadBytes,
            no_op = context.Plan.Patch.IsNoOp,
            semantic = SemanticPatchSummary(context.Plan.SemanticDiff),
            risk = PackagePatchRiskSummary(context.Plan.RiskAssessment),
            default_policy = new
            {
                can_apply = defaultDecision.CanApply,
                block_codes = defaultDecision.BlockCodes,
            },
            required_authorizations = RequiredAuthorizationNames(
                context.Plan.RiskAssessment,
                schemaHasNewErrors: false,
                hasChanges: !context.Plan.Patch.IsNoOp
            ),
            view = request.View,
            filtered_item_count = page.FilteredCount,
            offset = page.Offset,
            returned_item_count = page.Items.Length,
            next_offset = page.NextOffset,
            items = page.Items,
            hashes_included = request.IncludeHashes,
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

    private static object PackagePatchApplyPlanResponse(
        PackagePatchApplyContext context,
        PackagePatchPageRequest request,
        long started
    )
    {
        var page = PackagePatchPlanPage(context.Plan, request, context.Validation);
        var defaultBlocks = PackagePatchBlockCodes(
            context,
            new WordPackagePatchApplyPolicy()
        );
        var hardBlocks = PackagePatchHardBlockCodes(context);
        return new
        {
            file_name = Path.GetFileName(context.PackagePath),
            patch_file_name = Path.GetFileName(context.PatchPath),
            patch_id = context.Patch.PatchId,
            apply_plan_id = context.ApplyPlanId,
            base_package_fingerprint = context.Patch.BasePackageFingerprint,
            result_package_fingerprint = context.Patch.ResultPackageFingerprint,
            operation_count = context.Patch.OperationCount,
            no_op = context.Patch.IsNoOp,
            semantic = SemanticPatchSummary(context.Plan.SemanticDiff),
            risk = PackagePatchRiskSummary(context.Plan.RiskAssessment),
            openxml_schema_validation = SchemaValidationSummary(context.Validation),
            default_policy = new
            {
                can_apply = defaultBlocks.Count == 0,
                block_codes = defaultBlocks,
            },
            hard_block_codes = hardBlocks,
            required_authorizations = RequiredAuthorizationNames(
                context.Plan.RiskAssessment,
                SchemaValidationHasNewErrors(context.Validation),
                hasChanges: !context.Patch.IsNoOp
            ),
            view = request.View,
            filtered_item_count = page.FilteredCount,
            offset = page.Offset,
            returned_item_count = page.Items.Length,
            next_offset = page.NextOffset,
            items = page.Items,
            hashes_included = request.IncludeHashes,
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

    private static object SemanticPatchSummary(WordSemanticDiffResult diff) => new
    {
        diff_id = diff.DiffId,
        package_equivalent = diff.PackageEquivalent,
        semantically_equivalent = diff.SemanticallyEquivalent,
        matching_complete = diff.MatchingComplete,
        package_entry_difference_count = diff.EntryDifferences.Count,
        semantic_difference_count = diff.SemanticDifferences.Count,
        unclassified_projected_entry_count = diff.UnclassifiedProjectedEntryCount,
        change_counts = new
        {
            added = diff.AddedNodeCount,
            removed = diff.RemovedNodeCount,
            moved = diff.MovedNodeCount,
            text_changed = diff.TextChangedNodeCount,
            properties_changed = diff.PropertiesChangedNodeCount,
            structure_changed = diff.StructureChangedNodeCount,
            unmodeled_markup_changed = diff.UnmodeledMarkupChangedNodeCount,
        },
    };

    private static object PackagePatchRiskSummary(
        WordPackagePatchRiskAssessment risk
    ) => new
    {
        item_count = risk.Items.Count,
        block_item_count = risk.Items.Count(item =>
            item.Severity == WordPackagePatchRiskSeverity.Block
        ),
        review_item_count = risk.Items.Count(item =>
            item.Severity == WordPackagePatchRiskSeverity.Review
        ),
        digital_signatures_present = risk.DigitalSignaturesPresent,
        digital_signature_material_changed = risk.DigitalSignatureMaterialChanged,
        macro_operation_count = risk.MacroOperationCount,
        embedded_object_operation_count = risk.EmbeddedObjectOperationCount,
        activex_operation_count = risk.ActiveXOperationCount,
        external_relationship_added_count = risk.ExternalRelationshipAddedCount,
        external_relationship_removed_count = risk.ExternalRelationshipRemovedCount,
        opaque_binary_operation_count = risk.OpaqueBinaryOperationCount,
        custom_xml_operation_count = risk.CustomXmlOperationCount,
        infrastructure_operation_count = risk.InfrastructureOperationCount,
        baseline_structural_error_count = risk.BaselineStructuralErrorCount,
        candidate_structural_error_count = risk.CandidateStructuralErrorCount,
        new_structural_error_count = risk.NewStructuralErrorCount,
    };

    private static object SchemaValidationSummary(
        CandidateSchemaValidation validation
    ) => new
    {
        performed = validation.Performed,
        candidate_valid = validation.CandidateValid,
        no_new_errors = validation.NoNewErrors,
        new_error_count = validation.ErrorCount,
        baseline_error_count = validation.BaselineErrorCount,
        candidate_error_count = validation.CandidateErrorCount,
        errors_truncated = validation.ErrorsTruncated,
        not_performed_reason = validation.NotPerformedReason,
    };

    private static PatchPage PackagePatchPlanPage(
        WordPackagePatchPlan plan,
        PackagePatchPageRequest request,
        CandidateSchemaValidation? schema
    ) => request.View switch
    {
        "operations" => PackagePatchOperationPage(plan.Patch, request),
        "risks" => PagePackagePatchItems(
            plan.RiskAssessment.Items,
            request,
            item => (object)new
            {
                code = item.Code,
                severity = ToSnakeCase(item.Severity.ToString()),
                message = BoundForResponse(item.Message, 512),
                affected_operation_count = item.AffectedOperationCount,
            }
        ),
        "schema_errors" when schema is not null => PagePackagePatchItems(
            schema.Issues,
            request,
            ValidationIssueItem
        ),
        _ => PatchPage.Empty,
    };

    private static PatchPage PackagePatchOperationPage(
        OpcPackagePatch patch,
        PackagePatchPageRequest request
    ) => request.View == "operations"
        ? PagePackagePatchItems(
            patch.Operations,
            request,
            operation => (object)new
            {
                operation_id = operation.OperationId,
                kind = ToSnakeCase(operation.Kind.ToString()),
                entry_name = BoundForResponse(operation.EntryName, 512),
                part_uri = BoundForResponse(operation.PartUri, 512),
                before_content_type = BoundForResponse(
                    operation.BeforeContentType,
                    256
                ),
                after_content_type = BoundForResponse(
                    operation.AfterContentType,
                    256
                ),
                before_bytes = operation.BeforeBytes,
                after_bytes = operation.AfterBytes,
                before_sha256 = request.IncludeHashes
                    ? operation.BeforeSha256
                    : null,
                after_sha256 = request.IncludeHashes
                    ? operation.AfterSha256
                    : null,
                is_infrastructure = operation.IsInfrastructure,
            }
        )
        : PatchPage.Empty;

    private static PatchPage PagePackagePatchItems<T>(
        IReadOnlyList<T> source,
        PackagePatchPageRequest request,
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
        return new PatchPage(source.Count, request.Offset, nextOffset, items);
    }

    private static PackagePatchPageRequest ParsePackagePatchPageRequest(
        JsonElement arguments,
        bool allowSchemaView
    )
    {
        var view = arguments.String("view", "summary");
        if (
            view is not ("summary" or "operations" or "risks")
            && !(allowSchemaView && view == "schema_errors")
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                allowSchemaView
                    ? "view must be summary, operations, risks, or schema_errors"
                    : "view must be summary, operations, or risks"
            );
        }
        var offsetValue = arguments.NullableInt64("offset") ?? 0;
        var maxItemsValue = arguments.NullableInt64("max_items") ?? 50;
        if (offsetValue is < 0 or > int.MaxValue)
        {
            throw new NativeToolException("INVALID_INPUT", "offset is out of range");
        }
        if (maxItemsValue is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 200"
            );
        }
        return new PackagePatchPageRequest(
            view,
            (int)offsetValue,
            (int)maxItemsValue,
            arguments.Boolean("include_hashes", false)
        );
    }

    private static WordPackagePatchApplyPolicy ParsePackagePatchPolicy(
        JsonElement arguments
    ) => new()
    {
        AllowDigitalSignatureInvalidation = arguments.Boolean(
            "allow_digital_signature_invalidation",
            false
        ),
        AllowActiveContentChanges = arguments.Boolean(
            "allow_active_content_changes",
            false
        ),
        AllowExternalRelationshipChanges = arguments.Boolean(
            "allow_external_relationship_changes",
            false
        ),
        AllowOpaqueBinaryChanges = arguments.Boolean(
            "allow_opaque_binary_changes",
            false
        ),
        AllowNewStructuralErrors = arguments.Boolean(
            "allow_new_structural_errors",
            false
        ),
    };

    private static IReadOnlyList<string> PackagePatchBlockCodes(
        PackagePatchApplyContext context,
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
        blocks.AddRange(PackagePatchHardBlockCodes(context));
        return blocks.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> PackagePatchHardBlockCodes(
        PackagePatchApplyContext context
    )
    {
        var validation = context.Validation;
        if (
            !validation.Performed
            && validation.NotPerformedReason == "no_changes"
        )
        {
            return context.FormatHardBlockCodes;
        }
        var blocks = new List<string>(context.FormatHardBlockCodes);
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
        return blocks;
    }

    private static IReadOnlyList<string> PackagePatchFormatHardBlockCodes(
        string destinationPath,
        OpcPackageSnapshot candidate
    )
    {
        var expectedContentType = Path.GetExtension(destinationPath).ToLowerInvariant()
            switch
            {
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                ".docm" => "application/vnd.ms-word.document.macroEnabled.main+xml",
                ".dotx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml",
                ".dotm" => "application/vnd.ms-word.template.macroEnabledTemplate.main+xml",
                _ => null,
            };
        var officeDocumentPart = candidate.Relationships.FirstOrDefault(relationship =>
            relationship.SourcePartUri == "/"
            && relationship.Type.EndsWith("/officeDocument", StringComparison.Ordinal)
        )?.ResolvedTargetPartUri;
        var actualContentType = officeDocumentPart is not null
            && candidate.Parts.TryGetValue(officeDocumentPart, out var part)
                ? part.ContentType
                : null;
        return expectedContentType is not null
            && string.Equals(
                expectedContentType,
                actualContentType,
                StringComparison.OrdinalIgnoreCase
            )
                ? Array.Empty<string>()
                : ["result_package_type_does_not_match_destination_extension"];
    }

    private static bool SchemaValidationHasNewErrors(
        CandidateSchemaValidation validation
    ) => validation.Performed
        && !validation.ErrorsTruncated
        && validation.ErrorCount != 0
        && !validation.Issues.Any(issue =>
            issue.Id == "OPEN_XML_PACKAGE_OPEN_FAILED"
        );

    private static string[] RequiredAuthorizationNames(
        WordPackagePatchRiskAssessment risk,
        bool schemaHasNewErrors,
        bool hasChanges
    )
    {
        var names = new List<string>();
        if (hasChanges && risk.DigitalSignaturesPresent)
        {
            names.Add("allow_digital_signature_invalidation");
        }
        if (risk.ActiveContentChanged)
        {
            names.Add("allow_active_content_changes");
        }
        if (risk.ExternalRelationshipsChanged)
        {
            names.Add("allow_external_relationship_changes");
        }
        if (risk.OpaqueBinaryChanged)
        {
            names.Add("allow_opaque_binary_changes");
        }
        if (risk.NewStructuralErrorCount != 0 || schemaHasNewErrors)
        {
            names.Add("allow_new_structural_errors");
        }
        return names.ToArray();
    }

    private static string[] ExplicitAuthorizationNames(
        WordPackagePatchApplyPolicy policy
    )
    {
        var names = new List<string>();
        if (policy.AllowDigitalSignatureInvalidation)
        {
            names.Add("allow_digital_signature_invalidation");
        }
        if (policy.AllowActiveContentChanges)
        {
            names.Add("allow_active_content_changes");
        }
        if (policy.AllowExternalRelationshipChanges)
        {
            names.Add("allow_external_relationship_changes");
        }
        if (policy.AllowOpaqueBinaryChanges)
        {
            names.Add("allow_opaque_binary_changes");
        }
        if (policy.AllowNewStructuralErrors)
        {
            names.Add("allow_new_structural_errors");
        }
        return names.ToArray();
    }

    private static string ComputeApplyPlanId(
        WordPackagePatchPlan plan,
        CandidateSchemaValidation validation,
        string destinationPath,
        IReadOnlyList<string> formatHardBlocks
    )
    {
        var fields = new List<string>
        {
            "wordtoolkit-package-patch-apply-plan-v1",
            plan.Patch.PatchId,
            plan.Patch.BasePackageFingerprint,
            plan.Patch.ResultPackageFingerprint,
            plan.SemanticDiff.DiffId,
            Path.GetFullPath(destinationPath).ToUpperInvariant(),
            validation.Performed ? "1" : "0",
            validation.NoNewErrors ? "1" : "0",
            validation.ErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validation.BaselineErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validation.CandidateErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validation.ErrorsTruncated ? "1" : "0",
            validation.NotPerformedReason ?? string.Empty,
        };
        fields.AddRange(formatHardBlocks.Order(StringComparer.Ordinal));
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
        return "wtapply_" + Convert.ToBase64String(digest.AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ResolvePackagePatchPath(
        JsonElement arguments,
        string argumentName,
        bool mustExist
    )
    {
        var rawPath = arguments.String(argumentName);
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} must be a non-empty string"
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
                $"{argumentName} is not a valid filesystem path"
            );
        }
        if (!string.Equals(
                Path.GetExtension(path),
                ".wtpatch",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} must use the .wtpatch extension"
            );
        }
        if (mustExist)
        {
            if (!File.Exists(path))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The requested patch artifact does not exist"
                );
            }
        }
        else
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The patch output directory does not exist"
                );
            }
            if (File.Exists(path))
            {
                throw new NativeToolException(
                    "ALREADY_EXISTS",
                    "The patch output path already exists; artifacts are never overwritten"
                );
            }
        }
        return path;
    }

    private static PackagePatchArtifactResult WritePackagePatchArtifact(
        string destinationPath,
        OpcPackagePatch patch,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "The patch output path has no parent directory"
            );
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.wordtoolkit-{Guid.NewGuid():N}.tmp"
        );
        try
        {
            using (
                var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.WriteThrough
                )
            )
            {
                new OpcPackagePatchCodec().Write(output, patch, cancellationToken);
                output.Flush(flushToDisk: true);
            }
            using (var verify = File.OpenRead(temporaryPath))
            {
                var decoded = new OpcPackagePatchCodec().Read(
                    verify,
                    cancellationToken
                );
                if (!string.Equals(
                        decoded.PatchId,
                        patch.PatchId,
                        StringComparison.Ordinal
                    ))
                {
                    throw new OpcPackagePatchResultMismatchException(
                        "The persisted artifact does not match the reviewed patch ID."
                    );
                }
            }
            try
            {
                File.Move(temporaryPath, destinationPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                throw new NativeToolException(
                    "ALREADY_EXISTS",
                    "The patch output path was created concurrently; it was not overwritten"
                );
            }
            return new PackagePatchArtifactResult(
                new FileInfo(destinationPath).Length,
                HashFile(destinationPath)
            );
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static OpcPackagePatch ReadPackagePatch(
        string path,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            return new OpcPackagePatchCodec().Read(stream, cancellationToken);
        }
        catch (OpcPackagePatchException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or JsonException
                or NotSupportedException
        )
        {
            throw new OpcPackagePatchFormatException(
                "The file is not a valid WordToolkit patch artifact."
            );
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequiredPackagePatchId(
        JsonElement arguments,
        string name
    )
    {
        _ = arguments.Required(name);
        var value = arguments.String(name);
        if (
            value.Length is < 16 or > 96
            || !value.StartsWith("wtpatch_", StringComparison.Ordinal)
            || value[8..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} is not a valid WordToolkit patch ID"
            );
        }
        return value;
    }

    private static string RequiredApplyPlanId(JsonElement arguments)
    {
        _ = arguments.Required("expected_apply_plan_id");
        var value = arguments.String("expected_apply_plan_id");
        if (
            value.Length is < 16 or > 96
            || !value.StartsWith("wtapply_", StringComparison.Ordinal)
            || value[8..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_apply_plan_id is not a valid patch apply-plan ID"
            );
        }
        return value;
    }

    private static void VerifyOptionalPatchId(
        JsonElement arguments,
        string actual
    )
    {
        if (!arguments.TryGetProperty("expected_patch_id", out _))
        {
            return;
        }
        var expected = RequiredPackagePatchId(arguments, "expected_patch_id");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "The artifact does not match expected_patch_id"
            );
        }
    }

    private static Task<object> ExecutePackagePatchAction(Func<object> action)
    {
        try
        {
            return Task.FromResult(action());
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (OpcPackagePatchLimitException exception)
        {
            throw new NativeToolException(
                "PATCH_LIMIT",
                BoundForResponse(exception.Message, 512) ?? "Patch safety limit exceeded"
            );
        }
        catch (OpcPackagePatchPreconditionException exception)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                BoundForResponse(exception.Message, 512) ?? "Patch base mismatch"
            );
        }
        catch (OpcPackagePatchResultMismatchException exception)
        {
            throw new NativeToolException(
                "RESULT_MISMATCH",
                BoundForResponse(exception.Message, 512) ?? "Patch result mismatch"
            );
        }
        catch (OpcPackagePatchException exception)
        {
            throw new NativeToolException(
                "INVALID_PATCH",
                BoundForResponse(exception.Message, 512) ?? "Invalid patch artifact"
            );
        }
        catch (WordSemanticDiffLimitException exception)
        {
            throw new NativeToolException(
                "DIFF_LIMIT",
                BoundForResponse(exception.Message, 512) ?? "Semantic diff limit exceeded"
            );
        }
        catch (WordSemanticPreconditionException exception)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                BoundForResponse(exception.Message, 512) ?? "Semantic precondition failed"
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
                "A package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageConcurrencyException exception)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The destination package changed during the atomic write",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageResultMismatchException exception)
        {
            throw new NativeToolException(
                "RESULT_MISMATCH",
                "The written package does not match the reviewed patch result",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageValidationException exception)
        {
            throw new NativeToolException(
                "VALIDATION_FAILED",
                "The candidate package failed OPC structural validation",
                new
                {
                    diagnostics = exception.Diagnostics.Take(20).Select(diagnostic => new
                    {
                        code = diagnostic.Code,
                        severity = ToSnakeCase(diagnostic.Severity.ToString()),
                        message = BoundForResponse(diagnostic.Message, 512),
                        part_uri = BoundForResponse(diagnostic.PartUri, 512),
                    }).ToArray(),
                }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "A package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                BoundForResponse(exception.Message, 512) ?? "Invalid patch request"
            );
        }
        catch (InvalidDataException exception)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "A Word file is not a readable OPC ZIP package",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "A package or patch cannot be read or written with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "A package or patch could not be read or written",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private sealed record PackagePatchPageRequest(
        string View,
        int Offset,
        int MaxItems,
        bool IncludeHashes
    );

    private sealed record PatchPage(
        int FilteredCount,
        int Offset,
        int? NextOffset,
        object[] Items
    )
    {
        internal static PatchPage Empty { get; } = new(
            0,
            0,
            null,
            Array.Empty<object>()
        );
    }

    private sealed record PackagePatchPlanContext(
        string BeforePath,
        string AfterPath,
        OpcPackageSnapshot BeforePackage,
        OpcPackageSnapshot AfterPackage,
        WordPackagePatchPlan Plan
    );

    private sealed record PackagePatchApplyContext(
        string PackagePath,
        string PatchPath,
        OpcPackageSnapshot BasePackage,
        OpcPackageSnapshot CandidatePackage,
        OpcPackagePatch Patch,
        WordPackagePatchPlan Plan,
        CandidateSchemaValidation Validation,
        IReadOnlyList<string> FormatHardBlockCodes,
        string ApplyPlanId
    );

    private sealed record PackagePatchArtifactResult(long Bytes, string Sha256);
}
