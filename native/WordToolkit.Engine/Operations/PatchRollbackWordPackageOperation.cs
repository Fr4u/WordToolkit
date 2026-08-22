using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Plans and atomically applies the exact reverse of one portable WordToolkit patch.
/// Direct .NET, JSON CLI and MCP callers share this implementation. The operation never
/// opens Word, follows external relationships, or returns package payloads or XML.
/// </summary>
public sealed class PatchRollbackWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer;
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public PatchRollbackWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? packageLimits = null
    )
    {
        _reader = new OpcPackageReader(packageLimits);
        _serializer = new OpcPackageSerializer();
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public PatchRollbackPlanResult Plan(
        PatchRollbackPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Patch rollback plan request is required");
            }
            var context = BuildContext(
                request.LocalPath,
                request.PatchPath,
                request.ExpectedPackageFingerprint,
                request.ExpectedPatchId,
                cancellationToken
            );
            return ProjectPlan(context, request);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception, request?.LocalPath, request?.PatchPath);
        }
    }

    public PatchRollbackApplyResult Apply(
        PatchRollbackApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Patch rollback apply request is required");
            }
            if (!IsPlanId(request.ExpectedRollbackPlanId, "wtrollback_"))
            {
                throw Invalid(
                    "expected_rollback_plan_id is not a valid patch rollback-plan ID"
                );
            }

            var context = BuildContext(
                request.LocalPath,
                request.PatchPath,
                request.ExpectedPackageFingerprint,
                request.ExpectedPatchId,
                cancellationToken
            );
            if (!string.Equals(
                    context.RollbackPlanId,
                    request.ExpectedRollbackPlanId,
                    StringComparison.Ordinal
                ))
            {
                throw new WordToolkitOperationException(
                    "PLAN_MISMATCH",
                    "The current package and patch do not reproduce the reviewed rollback-plan ID"
                );
            }

            var policy = Policy(request, context.RollbackPlanId);
            var blocks = BlockCodes(context, policy);
            if (blocks.Count != 0)
            {
                throw new WordToolkitOperationException(
                    "PATCH_POLICY_BLOCKED",
                    "The rollback requires authorization or failed a non-overridable safety check",
                    details: new PatchRollbackPolicyBlockDetails(
                        context.SourcePatchId,
                        context.ReversePatch.PatchId,
                        context.RollbackPlanId,
                        blocks
                    )
                );
            }

            var authorizations = ExplicitAuthorizationNames(policy);
            if (context.ReversePatch.IsNoOp)
            {
                return new PatchRollbackApplyResult(
                    PatchRollbackWordPackageContract.ApplyContract,
                    Path.GetFileName(context.LocalPath),
                    Path.GetFileName(context.PatchPath),
                    context.SourcePatchId,
                    context.ReversePatch.PatchId,
                    context.RollbackPlanId,
                    RolledBack: false,
                    NoOp: true,
                    PreviousPackageFingerprint: null,
                    PackageFingerprint: context.CurrentPackage.Fingerprint,
                    PredictedPackageFingerprint: null,
                    BackupPath: null,
                    ChangedEntryNames: Array.Empty<string>(),
                    DiagnosticCount: null,
                    DigitalSignaturesMayBeInvalidated: null,
                    authorizations,
                    RawPayloadsReturned: false,
                    RawXmlReturned: false,
                    MutationPerformed: false,
                    WordOpened: false
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
            var mutation = context.ReversePatch.CreateMutation(
                context.CurrentPackage,
                cancellationToken
            );
            var write = _writer.Write(
                context.LocalPath,
                mutation,
                new OpcAtomicWriteOptions
                {
                    ExpectedDestinationFingerprint = context.CurrentPackage.Fingerprint,
                    ExpectedResultFingerprint =
                        context.ReversePatch.ResultPackageFingerprint,
                    AllowStructuralErrors = !context.CandidatePackage.IsStructurallyValid,
                    KeepBackup = request.KeepBackup,
                }
            );
            return new PatchRollbackApplyResult(
                PatchRollbackWordPackageContract.ApplyContract,
                Path.GetFileName(context.LocalPath),
                Path.GetFileName(context.PatchPath),
                context.SourcePatchId,
                context.ReversePatch.PatchId,
                context.RollbackPlanId,
                RolledBack: true,
                NoOp: false,
                PreviousPackageFingerprint: context.CurrentPackage.Fingerprint,
                PackageFingerprint: write.Fingerprint,
                PredictedPackageFingerprint:
                    context.ReversePatch.ResultPackageFingerprint,
                BackupPath: write.BackupPath,
                ChangedEntryNames: write.ChangedEntryNames,
                DiagnosticCount: write.Diagnostics.Count,
                DigitalSignaturesMayBeInvalidated:
                    context.Plan.RiskAssessment.DigitalSignaturesPresent,
                authorizations,
                RawPayloadsReturned: false,
                RawXmlReturned: false,
                MutationPerformed: true,
                WordOpened: false
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception, request?.LocalPath, request?.PatchPath);
        }
    }

    private PlanContext BuildContext(
        string localPath,
        string patchPath,
        string expectedPackageFingerprint,
        string expectedPatchId,
        CancellationToken cancellationToken
    )
    {
        ValidateIdentityRequest(
            localPath,
            patchPath,
            expectedPackageFingerprint,
            expectedPatchId
        );
        var resolvedLocalPath = ResolvePackagePath(localPath);
        var resolvedPatchPath = ResolvePatchPath(patchPath);
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePatch = ReadPatch(resolvedPatchPath, cancellationToken);
        if (!string.Equals(
                sourcePatch.PatchId,
                expectedPatchId,
                StringComparison.Ordinal
            ))
        {
            throw new WordToolkitOperationException(
                "PLAN_MISMATCH",
                "The patch artifact does not match expected_patch_id"
            );
        }
        var reversePatch = sourcePatch.Reverse();

        var package = _reader.Read(resolvedLocalPath, cancellationToken);
        if (!string.Equals(
                package.Fingerprint,
                expectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
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
            reversePatch,
            out var candidate,
            cancellationToken
        );
        var validation = reversePatch.IsNoOp
            ? WordPackageCandidateValidationReport.NotPerformed("no_changes")
            : ValidateCandidate(package, candidate, cancellationToken);
        var formatHardBlocks = FormatHardBlockCodes(
            resolvedLocalPath,
            candidate
        );
        var rollbackPlanId = CreateRollbackPlanId(
            plan,
            validation,
            resolvedLocalPath,
            formatHardBlocks
        );
        return new PlanContext(
            resolvedLocalPath,
            resolvedPatchPath,
            package,
            candidate,
            reversePatch,
            sourcePatch.PatchId,
            plan,
            validation,
            formatHardBlocks,
            rollbackPlanId
        );
    }

    private WordPackageCandidateValidationReport ValidateCandidate(
        OpcPackageSnapshot baselinePackage,
        OpcPackageSnapshot candidatePackage,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_candidateValidator is null)
        {
            return WordPackageCandidateValidationReport.NotPerformed(
                "schema_validator_unavailable"
            );
        }

        using var baseline = new MemoryStream();
        _serializer.Write(
            baseline,
            new OpcPackageMutationBuilder(baselinePackage)
        );
        using var candidate = new MemoryStream();
        _serializer.Write(
            candidate,
            new OpcPackageMutationBuilder(candidatePackage)
        );
        baseline.Position = 0;
        candidate.Position = 0;
        try
        {
            return BoundValidation(
                _candidateValidator.Validate(
                    baseline,
                    candidate,
                    cancellationToken
                )
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "Candidate package schema validation failed",
                innerException: exception
            );
        }
    }

    private static PatchRollbackPlanResult ProjectPlan(
        PlanContext context,
        PatchRollbackPlanRequest request
    )
    {
        ValidatePage(request.View, request.Offset, request.MaxItems);
        var page = Page(context, request);
        var defaultBlocks = BlockCodes(
            context,
            new WordPackagePatchApplyPolicy()
        );
        var hardBlocks = HardBlockCodes(context);
        return new PatchRollbackPlanResult(
            PatchRollbackWordPackageContract.PlanContract,
            Path.GetFileName(context.LocalPath),
            Path.GetFileName(context.PatchPath),
            context.SourcePatchId,
            context.ReversePatch.PatchId,
            context.RollbackPlanId,
            context.ReversePatch.BasePackageFingerprint,
            context.ReversePatch.ResultPackageFingerprint,
            context.ReversePatch.OperationCount,
            context.ReversePatch.IsNoOp,
            SemanticSummary(context.Plan.SemanticDiff),
            RiskSummary(context.Plan.RiskAssessment),
            ValidationSummary(context.Validation),
            new PatchRollbackDefaultPolicy(
                defaultBlocks.Count == 0,
                defaultBlocks
            ),
            hardBlocks,
            RequiredAuthorizationNames(
                context.Plan.RiskAssessment,
                SchemaValidationHasNewErrors(context.Validation),
                hasChanges: !context.ReversePatch.IsNoOp
            ),
            context.Plan.RiskAssessment.Protection.AuthorizationRequired
                && !context.Plan.RiskAssessment.Protection.HasMalformedPermissionMetadata
                    ? context.RollbackPlanId
                    : null,
            request.View,
            page.FilteredCount,
            request.Offset,
            page.Items.Count,
            page.NextOffset,
            page.Items,
            request.IncludeHashes,
            RawPayloadsReturned: false,
            RawXmlReturned: false,
            MutationPerformed: false,
            WordOpened: false
        );
    }

    private static PageResult Page(
        PlanContext context,
        PatchRollbackPlanRequest request
    )
    {
        IReadOnlyList<PatchRollbackPageItem> source = request.View switch
        {
            PatchRollbackView.Operations => context.ReversePatch.Operations.Select(
                operation => new PatchRollbackPageItem(
                    OperationId: operation.OperationId,
                    Kind: SnakeCase(operation.Kind.ToString()),
                    EntryName: Bound(operation.EntryName, 512),
                    PartUri: Bound(operation.PartUri, 512),
                    BeforeContentType: Bound(operation.BeforeContentType, 256),
                    AfterContentType: Bound(operation.AfterContentType, 256),
                    BeforeBytes: operation.BeforeBytes,
                    AfterBytes: operation.AfterBytes,
                    BeforeSha256: request.IncludeHashes
                        ? operation.BeforeSha256
                        : null,
                    AfterSha256: request.IncludeHashes
                        ? operation.AfterSha256
                        : null,
                    IsInfrastructure: operation.IsInfrastructure
                )
            ).ToArray(),
            PatchRollbackView.Risks => context.Plan.RiskAssessment.Items.Select(
                item => new PatchRollbackPageItem(
                    Code: item.Code,
                    Severity: SnakeCase(item.Severity.ToString()),
                    Message: Bound(item.Message, 512),
                    AffectedOperationCount: item.AffectedOperationCount
                )
            ).ToArray(),
            PatchRollbackView.SchemaErrors => context.Validation.Issues.Select(
                issue => new PatchRollbackPageItem(
                    Id: Bound(issue.Id, 128),
                    ErrorType: Bound(issue.ErrorType, 64),
                    PartUri: Bound(issue.PartUri, 512),
                    Path: Bound(issue.Path, 512),
                    Node: Bound(issue.Node, 128)
                )
            ).ToArray(),
            _ => Array.Empty<PatchRollbackPageItem>(),
        };
        var items = source.Skip(request.Offset).Take(request.MaxItems).ToArray();
        var nextOffset = request.Offset + items.Length < source.Count
            ? request.Offset + items.Length
            : (int?)null;
        return new PageResult(source.Count, nextOffset, items);
    }

    private static PatchRollbackSemanticSummary SemanticSummary(
        WordSemanticDiffResult diff
    ) => new(
        diff.DiffId,
        diff.PackageEquivalent,
        diff.SemanticallyEquivalent,
        diff.MatchingComplete,
        diff.EntryDifferences.Count,
        diff.SemanticDifferences.Count,
        diff.UnclassifiedProjectedEntryCount,
        new PatchRollbackChangeCounts(
            diff.AddedNodeCount,
            diff.RemovedNodeCount,
            diff.MovedNodeCount,
            diff.TextChangedNodeCount,
            diff.PropertiesChangedNodeCount,
            diff.StructureChangedNodeCount,
            diff.UnmodeledMarkupChangedNodeCount
        )
    );

    private static PatchRollbackRiskSummary RiskSummary(
        WordPackagePatchRiskAssessment risk
    ) => new(
        risk.Items.Count,
        risk.Items.Count(item =>
            item.Severity == WordPackagePatchRiskSeverity.Block
        ),
        risk.Items.Count(item =>
            item.Severity == WordPackagePatchRiskSeverity.Review
        ),
        risk.DigitalSignaturesPresent,
        risk.DigitalSignatureMaterialChanged,
        risk.MacroOperationCount,
        risk.EmbeddedObjectOperationCount,
        risk.ActiveXOperationCount,
        risk.ExternalRelationshipAddedCount,
        risk.ExternalRelationshipRemovedCount,
        risk.OpaqueBinaryOperationCount,
        risk.CustomXmlOperationCount,
        risk.InfrastructureOperationCount,
        risk.BaselineStructuralErrorCount,
        risk.CandidateStructuralErrorCount,
        risk.NewStructuralErrorCount,
        new PatchRollbackProtectionRiskSummary(
            risk.Protection.BaseDocumentProtectionEnforced,
            risk.Protection.BaseDocumentProtectionEditMode,
            risk.Protection.ResultDocumentProtectionEnforced,
            risk.Protection.ResultDocumentProtectionEditMode,
            risk.Protection.DocumentProtectionMetadataChanged,
            risk.Protection.BasePermissionRangeCount,
            risk.Protection.ResultPermissionRangeCount,
            risk.Protection.MalformedPermissionRangeCount,
            risk.Protection.PermissionIssuesTruncated,
            risk.Protection.PermissionIssueCodes,
            risk.Protection.AuthorizationRequired
        )
    );

    private static PatchRollbackSchemaValidationSummary ValidationSummary(
        WordPackageCandidateValidationReport validation
    ) => new(
        validation.Performed,
        validation.CandidateValid,
        validation.NoNewErrors,
        validation.ErrorCount,
        validation.BaselineErrorCount,
        validation.CandidateErrorCount,
        validation.ErrorsTruncated,
        validation.NotPerformedReason
    );

    private static WordPackagePatchApplyPolicy Policy(
        PatchRollbackApplyRequest request,
        string expectedProtectionAuthorization
    ) => new()
    {
        AllowDigitalSignatureInvalidation =
            request.AllowDigitalSignatureInvalidation,
        AllowActiveContentChanges = request.AllowActiveContentChanges,
        AllowExternalRelationshipChanges =
            request.AllowExternalRelationshipChanges,
        AllowOpaqueBinaryChanges = request.AllowOpaqueBinaryChanges,
        AllowNewStructuralErrors = request.AllowNewStructuralErrors,
        AllowProtectedDocumentEdit = string.Equals(
            request.ProtectedEditAuthorization,
            expectedProtectionAuthorization,
            StringComparison.Ordinal
        ),
    };

    private static IReadOnlyList<string> BlockCodes(
        PlanContext context,
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
        blocks.AddRange(HardBlockCodes(context));
        return blocks.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> HardBlockCodes(PlanContext context)
    {
        if (
            !context.Validation.Performed
            && context.Validation.NotPerformedReason == "no_changes"
        )
        {
            return context.FormatHardBlockCodes;
        }
        var blocks = new List<string>(context.FormatHardBlockCodes);
        if (context.Plan.RiskAssessment.Protection.HasMalformedPermissionMetadata)
        {
            blocks.Add("protection_metadata_malformed");
        }
        if (!context.Validation.Performed)
        {
            blocks.Add("openxml_validation_not_performed");
        }
        if (context.Validation.ErrorsTruncated)
        {
            blocks.Add("openxml_validation_limit_exceeded");
        }
        if (context.Validation.Issues.Any(issue =>
                issue.Id == "OPEN_XML_PACKAGE_OPEN_FAILED"
            ))
        {
            blocks.Add("candidate_not_openable_by_openxml_sdk");
        }
        return blocks.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> FormatHardBlockCodes(
        string destinationPath,
        OpcPackageSnapshot candidate
    )
    {
        var officeDocumentPart = candidate.Relationships.FirstOrDefault(
            relationship => relationship.SourcePartUri == "/"
                && WordPackageConformance.IsOfficeDocumentRelationshipType(
                    relationship.Type
                )
        )?.ResolvedTargetPartUri;
        var contentType = officeDocumentPart is not null
            && candidate.Parts.TryGetValue(officeDocumentPart, out var part)
                ? part.ContentType
                : null;
        return WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
            destinationPath,
            contentType
        )
            ? Array.Empty<string>()
            : ["result_package_type_does_not_match_destination_extension"];
    }

    private static bool SchemaValidationHasNewErrors(
        WordPackageCandidateValidationReport validation
    ) => validation.Performed
        && !validation.ErrorsTruncated
        && validation.ErrorCount != 0
        && !validation.Issues.Any(issue =>
            issue.Id == "OPEN_XML_PACKAGE_OPEN_FAILED"
        );

    private static IReadOnlyList<string> RequiredAuthorizationNames(
        WordPackagePatchRiskAssessment risk,
        bool schemaHasNewErrors,
        bool hasChanges
    )
    {
        var result = new List<string>();
        if (hasChanges && risk.DigitalSignaturesPresent)
        {
            result.Add("allow_digital_signature_invalidation");
        }
        if (risk.ActiveContentChanged)
        {
            result.Add("allow_active_content_changes");
        }
        if (risk.ExternalRelationshipsChanged)
        {
            result.Add("allow_external_relationship_changes");
        }
        if (risk.OpaqueBinaryChanged)
        {
            result.Add("allow_opaque_binary_changes");
        }
        if (risk.NewStructuralErrorCount != 0 || schemaHasNewErrors)
        {
            result.Add("allow_new_structural_errors");
        }
        if (
            hasChanges
            && risk.Protection.AuthorizationRequired
            && !risk.Protection.HasMalformedPermissionMetadata
        )
        {
            result.Add("protected_edit_authorization");
        }
        return result;
    }

    private static IReadOnlyList<string> ExplicitAuthorizationNames(
        WordPackagePatchApplyPolicy policy
    )
    {
        var result = new List<string>();
        if (policy.AllowDigitalSignatureInvalidation)
        {
            result.Add("allow_digital_signature_invalidation");
        }
        if (policy.AllowActiveContentChanges)
        {
            result.Add("allow_active_content_changes");
        }
        if (policy.AllowExternalRelationshipChanges)
        {
            result.Add("allow_external_relationship_changes");
        }
        if (policy.AllowOpaqueBinaryChanges)
        {
            result.Add("allow_opaque_binary_changes");
        }
        if (policy.AllowNewStructuralErrors)
        {
            result.Add("allow_new_structural_errors");
        }
        if (policy.AllowProtectedDocumentEdit)
        {
            result.Add("protected_edit_authorization");
        }
        return result;
    }

    private static string CreateRollbackPlanId(
        WordPackagePatchPlan plan,
        WordPackageCandidateValidationReport validation,
        string destinationPath,
        IReadOnlyList<string> formatHardBlocks
    )
    {
        var fields = new List<string>
        {
            "wordtoolkit-package-patch-rollback-plan-v1",
            plan.Patch.PatchId,
            plan.Patch.BasePackageFingerprint,
            plan.Patch.ResultPackageFingerprint,
            plan.SemanticDiff.DiffId,
            NormalizeDestinationBindingPath(destinationPath),
            validation.Performed ? "1" : "0",
            validation.NoNewErrors ? "1" : "0",
            validation.ErrorCount.ToString(CultureInfo.InvariantCulture),
            validation.BaselineErrorCount.ToString(CultureInfo.InvariantCulture),
            validation.CandidateErrorCount.ToString(CultureInfo.InvariantCulture),
            validation.ErrorsTruncated ? "1" : "0",
            validation.NotPerformedReason ?? string.Empty,
        };
        fields.AddRange(formatHardBlocks.Order(StringComparer.Ordinal));
        fields.AddRange(validation.Issues
            .OrderBy(ValidationIssueKey, StringComparer.Ordinal)
            .Select(ValidationIssueKey));
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\u001f', fields))
        );
        return "wtrollback_" + Convert.ToBase64String(digest.AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ValidationIssueKey(WordPackageValidationIssue issue) =>
        string.Join(
            '\u001e',
            issue.Id ?? string.Empty,
            issue.ErrorType,
            issue.PartUri ?? string.Empty,
            issue.Path ?? string.Empty,
            issue.Node ?? string.Empty
        );

    private static string NormalizeDestinationBindingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return OperatingSystem.IsWindows()
            ? fullPath.ToUpperInvariant()
            : fullPath;
    }

    private static OpcPackagePatch ReadPatch(
        string path,
        CancellationToken cancellationToken
    )
        => new OpcPackagePatchCodec().ReadFromPath(path, cancellationToken);

    private static void ValidateIdentityRequest(
        string localPath,
        string patchPath,
        string expectedPackageFingerprint,
        string expectedPatchId
    )
    {
        ValidatePath(localPath, "local_path");
        ValidatePath(patchPath, "patch_path");
        if (!IsSha256(expectedPackageFingerprint))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (!IsPlanId(expectedPatchId, "wtpatch_"))
        {
            throw Invalid("expected_patch_id is not a valid WordToolkit patch ID");
        }
    }

    private static void ValidatePage(
        PatchRollbackView view,
        int offset,
        int maxItems
    )
    {
        if (!Enum.IsDefined(view))
        {
            throw Invalid("view is not supported");
        }
        if (offset < 0)
        {
            throw Invalid("offset must be non-negative");
        }
        if (maxItems is < 1 or > PatchRollbackWordPackageContract.MaximumPageItems)
        {
            throw Invalid(
                $"max_items must be between 1 and {PatchRollbackWordPackageContract.MaximumPageItems}"
            );
        }
    }

    private static void ValidatePath(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw Invalid($"{name} must be a non-empty string");
        }
        if (path.Length > PatchRollbackWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid(
                $"{name} cannot exceed {PatchRollbackWordPackageContract.MaximumLocalPathCharacters} characters"
            );
        }
    }

    private static string ResolvePackagePath(string value)
    {
        var path = ResolveExistingPath(value, "Word package");
        if (!InspectWordPackageContract.IsSupportedFileName(path))
        {
            throw Invalid("local_path must use DOCX, DOCM, DOTX, or DOTM");
        }
        return path;
    }

    private static string ResolvePatchPath(string value)
    {
        var path = ResolveExistingPath(value, "patch artifact");
        if (!string.Equals(
                Path.GetExtension(path),
                ".wtpatch",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw Invalid("patch_path must use the .wtpatch extension");
        }
        return path;
    }

    private static string ResolveExistingPath(string value, string label)
    {
        try
        {
            var path = Path.GetFullPath(value);
            if (!File.Exists(path))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    $"The requested {label} does not exist"
                );
            }
            return path;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw Invalid($"{label} path is invalid", exception);
        }
    }

    private static WordPackageCandidateValidationReport BoundValidation(
        WordPackageCandidateValidationReport report
    )
    {
        ArgumentNullException.ThrowIfNull(report);
        if (
            report.ErrorCount < 0
            || report.BaselineErrorCount < 0
            || report.CandidateErrorCount < 0
            || report.Issues is null
            || report.Issues.Count > 200
            || report.ErrorCount < report.Issues.Count
            || report.NoNewErrors && report.ErrorCount != 0
            || report.CandidateValid && report.CandidateErrorCount != 0
            || report.Performed && report.NotPerformedReason is not null
            || !report.Performed
                && (
                    report.CandidateValid
                    || report.NoNewErrors
                    || report.ErrorCount != 0
                    || report.BaselineErrorCount != 0
                    || report.CandidateErrorCount != 0
                    || report.ErrorsTruncated
                    || report.Issues.Count != 0
                    || string.IsNullOrWhiteSpace(report.NotPerformedReason)
                )
            || report.Performed
                && !report.ErrorsTruncated
                && report.ErrorCount != report.Issues.Count
        )
        {
            throw new InvalidOperationException(
                "Candidate validator returned an invalid or unbounded report."
            );
        }
        return report with
        {
            NotPerformedReason = Bound(report.NotPerformedReason, 128),
            Issues = report.Issues.Select(issue =>
                new WordPackageValidationIssue(
                    Bound(issue.Id, 128),
                    Bound(issue.ErrorType, 64) ?? "Unknown",
                    Bound(issue.PartUri, 512),
                    Bound(issue.Path, 512),
                    Bound(issue.Node, 128)
                )
            ).ToArray(),
        };
    }

    private static bool IsSha256(string value) => value is not null
        && value.Length == 64
        && value.All(Uri.IsHexDigit);

    private static bool IsPlanId(string value, string prefix) => value is not null
        && value.Length is >= 16
            and <= PatchRollbackWordPackageContract.MaximumPatchIdCharacters
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && value[prefix.Length..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
        );

    private static string SnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index != 0)
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static string? Bound(string? value, int maximum) =>
        value is null || value.Length <= maximum
            ? value
            : value[..maximum] + "…";

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath,
        string? patchPath
    ) => exception switch
    {
        OpcPackageSourceChangedException changed =>
            new WordToolkitOperationException(
                "SOURCE_CHANGED",
                "The patch artifact changed while a stable snapshot was being captured",
                retryable: true,
                innerException: changed
            ),
        OpcPackagePatchLimitException limit => new WordToolkitOperationException(
            "PATCH_LIMIT",
            SafeReason(limit.Message, localPath, patchPath)
                ?? "Patch safety limit exceeded",
            innerException: limit
        ),
        OpcPackagePatchPreconditionException conflict =>
            new WordToolkitOperationException(
                "VERSION_CONFLICT",
                SafeReason(conflict.Message, localPath, patchPath)
                    ?? "Patch base mismatch",
                innerException: conflict
            ),
        OpcPackagePatchResultMismatchException mismatch =>
            new WordToolkitOperationException(
                "RESULT_MISMATCH",
                SafeReason(mismatch.Message, localPath, patchPath)
                    ?? "Patch result mismatch",
                innerException: mismatch
            ),
        OpcPackagePatchException patch => new WordToolkitOperationException(
            "INVALID_PATCH",
            SafeReason(patch.Message, localPath, patchPath)
                ?? "Invalid patch artifact",
            innerException: patch
        ),
        WordSemanticDiffLimitException limit => new WordToolkitOperationException(
            "DIFF_LIMIT",
            SafeReason(limit.Message, localPath, patchPath)
                ?? "Semantic diff limit exceeded",
            innerException: limit
        ),
        WordSemanticDiffPreconditionException conflict =>
            new WordToolkitOperationException(
                "VERSION_CONFLICT",
                SafeReason(conflict.Message, localPath, patchPath)
                    ?? "Semantic diff precondition failed",
                innerException: conflict
            ),
        WordSemanticPreconditionException conflict =>
            new WordToolkitOperationException(
                "VERSION_CONFLICT",
                SafeReason(conflict.Message, localPath, patchPath)
                    ?? "Semantic precondition failed",
                innerException: conflict
            ),
        WordSemanticLimitException limit => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            "Semantic projection exceeds a bounded safety limit",
            SafeReason(limit.Message, localPath, patchPath),
            innerException: limit
        ),
        WordSemanticProjectionException projection =>
            new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                SafeReason(projection.Message, localPath, patchPath),
                innerException: projection
            ),
        OpcPackageRecoveryException recovery => new WordToolkitOperationException(
            "RECOVERY_REQUIRED",
            "Atomic rollback detected a concurrent change and automatic recovery did not finish",
            retryable: false,
            innerException: recovery,
            details: new WordToolkitRecoveryDetails(
                recovery.RecoveryPaths.Select(path =>
                    Path.GetFileName(path) ?? string.Empty
                ).Where(name => name.Length != 0).ToArray()
            )
        ),
        OpcPackageConcurrencyException conflict =>
            new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "The destination package changed during the atomic rollback",
                SafeReason(conflict.Message, localPath, patchPath),
                retryable: true,
                innerException: conflict
            ),
        OpcPackageResultMismatchException mismatch =>
            new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "The written package does not match the reviewed rollback result",
                SafeReason(mismatch.Message, localPath, patchPath),
                innerException: mismatch
            ),
        OpcPackageValidationException validation =>
            new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "The rollback candidate failed OPC structural validation",
                SafeReason(validation.Message, localPath, patchPath),
                innerException: validation
            ),
        OpcPackageLimitException limit => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            "The package exceeds a bounded safety limit",
            SafeReason(limit.Message, localPath, patchPath),
            innerException: limit
        ),
        InvalidDataException invalid => new WordToolkitOperationException(
            "INVALID_PACKAGE",
            "A Word file or patch is not a readable bounded package",
            innerException: invalid
        ),
        FileNotFoundException missing => new WordToolkitOperationException(
            "NOT_FOUND",
            "The requested Word package or patch does not exist",
            innerException: missing
        ),
        DirectoryNotFoundException missing => new WordToolkitOperationException(
            "NOT_FOUND",
            "The requested Word package or patch does not exist",
            innerException: missing
        ),
        UnauthorizedAccessException denied => new WordToolkitOperationException(
            "ACCESS_DENIED",
            "The Word package or patch cannot be read or written with current permissions",
            innerException: denied
        ),
        ArgumentException invalid => Invalid("Invalid patch rollback request", invalid),
        IOException io => new WordToolkitOperationException(
            "IO_ERROR",
            "The Word package or patch could not be read or written",
            SafeReason(io.Message, localPath, patchPath),
            retryable: true,
            innerException: io
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The patch rollback operation failed",
            innerException: exception
        ),
    };

    private static string? SafeReason(
        string? message,
        params string?[] sensitivePaths
    )
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }
        var result = message;
        foreach (var value in sensitivePaths.Where(value =>
                     !string.IsNullOrWhiteSpace(value)
                 ))
        {
            try
            {
                result = result.Replace(
                    Path.GetFullPath(value!),
                    "<redacted>",
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException
            )
            {
            }
            result = result.Replace(
                value!,
                "<redacted>",
                StringComparison.OrdinalIgnoreCase
            );
        }
        return Bound(result, 512);
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);

    private sealed record PlanContext(
        string LocalPath,
        string PatchPath,
        OpcPackageSnapshot CurrentPackage,
        OpcPackageSnapshot CandidatePackage,
        OpcPackagePatch ReversePatch,
        string SourcePatchId,
        WordPackagePatchPlan Plan,
        WordPackageCandidateValidationReport Validation,
        IReadOnlyList<string> FormatHardBlockCodes,
        string RollbackPlanId
    );

    private sealed record PageResult(
        int FilteredCount,
        int? NextOffset,
        IReadOnlyList<PatchRollbackPageItem> Items
    );
}
