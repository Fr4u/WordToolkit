using System.Globalization;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Plans and atomically applies one source-linked numbering sequence restart. Direct
/// .NET, CLI and MCP callers share this implementation.
/// </summary>
public sealed class NumberingRepairWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer = new();
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public NumberingRepairWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public NumberingRepairPlanResult Plan(
        NumberingRepairPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var context = BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.TargetParagraphNodeId,
                request.ExpectedNumberId,
                request.ExpectedLevelIndex,
                request.StartValue,
                cancellationToken
            );
            return ProjectPlan(context, request.IncludeDetails);
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
            throw MapFailure(exception, request?.LocalPath);
        }
    }

    public NumberingRepairApplyResult Apply(
        NumberingRepairApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid numbering repair plan ID");
            }
            var context = BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.TargetParagraphNodeId,
                request.ExpectedNumberId,
                request.ExpectedLevelIndex,
                request.StartValue,
                cancellationToken
            );
            if (!string.Equals(
                    context.Plan.PlanId,
                    request.ExpectedPlanId,
                    StringComparison.Ordinal
                ))
            {
                throw new WordToolkitOperationException(
                    "PLAN_MISMATCH",
                    "The request does not reproduce the reviewed numbering repair plan ID"
                );
            }
            var protectionBlocks = ProtectionBlockCodes(context, request.ProtectedEditAuthorization);
            if (protectionBlocks.Count != 0)
            {
                throw new WordToolkitOperationException(
                    "EDIT_POLICY_BLOCKED",
                    "Numbering repair is blocked by document protection or permission metadata",
                    details: new NumberingRepairEditPolicyBlockDetails(context.Plan.PlanId, protectionBlocks)
                );
            }
            if (context.HasDigitalSignatures)
            {
                throw new WordToolkitOperationException(
                    "SIGNED_PACKAGE",
                    "Numbering repair is blocked because the package contains digital signatures"
                );
            }
            if (!context.Validation.Performed)
            {
                throw new WordToolkitOperationException(
                    "VALIDATOR_REQUIRED",
                    "Applying numbering repair requires a candidate package schema validator"
                );
            }
            if (!context.Validation.NoNewErrors)
            {
                var issues = context.Validation.Issues.Take(20).ToArray();
                throw new WordToolkitOperationException(
                    "OOXML_SCHEMA_INVALID",
                    "The exact numbering repair candidate introduces Microsoft Open XML schema errors",
                    details: new WordPackageValidationFailureDetails(
                        context.Validation.ErrorCount,
                        context.Validation.BaselineErrorCount,
                        context.Validation.CandidateErrorCount,
                        context.Validation.ErrorsTruncated
                            || context.Validation.Issues.Count > issues.Length,
                        issues
                    )
                );
            }

            if (!context.Plan.HasChanges)
            {
                return new NumberingRepairApplyResult(
                    NumberingRepairWordPackageContract.ApplyContract,
                    Path.GetFileName(context.Path), "restart_numbering_sequence",
                    "remaining_instance_in_story", context.Plan.PlanId,
                    false, true, 0, context.Plan.SourceNumberId, context.Plan.NewNumberId,
                    context.Package.Fingerprint, context.Package.Fingerprint,
                    context.Plan.ResultPackageFingerprint, null, Array.Empty<string>(), 0,
                    context.Validation.CandidateValid, context.Validation.NoNewErrors,
                    false, false, false, false, Array.Empty<string>());
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = _writer.Write(
                context.Path,
                context.Plan.CreateMutation(context.Package),
                new OpcAtomicWriteOptions
                {
                    ExpectedDestinationFingerprint = context.Package.Fingerprint,
                    ExpectedResultFingerprint = context.Plan.ResultPackageFingerprint,
                    KeepBackup = request.KeepBackup,
                }
            );
            return new NumberingRepairApplyResult(
                NumberingRepairWordPackageContract.ApplyContract,
                Path.GetFileName(context.Path),
                "restart_numbering_sequence",
                "remaining_instance_in_story",
                context.Plan.PlanId,
                Applied: true,
                NoOp: false,
                AffectedParagraphCount: context.Plan.AffectedParagraphs.Count,
                SourceNumberId: context.Plan.SourceNumberId,
                NewNumberId: context.Plan.NewNumberId,
                PreviousPackageFingerprint: context.Package.Fingerprint,
                PackageFingerprint: result.Fingerprint,
                PredictedPackageFingerprint: context.Plan.ResultPackageFingerprint,
                BackupPath: result.BackupPath,
                ChangedEntryNames: result.ChangedEntryNames,
                DiagnosticCount: result.Diagnostics.Count,
                MicrosoftSchemaValid: context.Validation.CandidateValid,
                MicrosoftSchemaNoNewErrors: context.Validation.NoNewErrors,
                ParagraphTextReturned: false,
                RawXmlReturned: false,
                MutationPerformed: true,
                WordOpened: false,
                ExplicitAuthorizations: context.Protection.AuthorizationRequired
                    ? ["protected_edit_authorization"] : Array.Empty<string>()
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
            throw MapFailure(exception, request?.LocalPath);
        }
    }

    private PlanContext BuildContext(
        string localPath,
        string expectedFingerprint,
        string targetParagraphNodeId,
        int expectedNumberId,
        int expectedLevelIndex,
        int startValue,
        CancellationToken cancellationToken
    )
    {
        ValidateRequest(
            localPath,
            expectedFingerprint,
            targetParagraphNodeId,
            expectedNumberId,
            expectedLevelIndex,
            startValue
        );
        var path = ResolvePath(localPath);
        cancellationToken.ThrowIfCancellationRequested();
        var package = _reader.Read(path, cancellationToken);
        if (!package.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The input package has structural OPC errors"
            );
        }
        if (!string.Equals(
                package.Fingerprint,
                expectedFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "Saved package changed before the numbering repair plan was built"
            );
        }
        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        if (!package.Parts.TryGetValue(semantic.MainPartUri, out var mainPart)
            || !WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
                path,
                mainPart.ContentType
            ))
        {
            throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The filename extension does not match the Word main-part content type"
            );
        }
        var plan = new WordNumberingSequenceRepairPlanner(
            new WordNumberingSequenceRepairOptions
            {
                MaxAffectedParagraphs = NumberingRepairWordPackageContract
                    .MaximumAffectedParagraphs,
                MaxChangedParts = NumberingRepairWordPackageContract.MaximumChangedParts,
            }
        ).PlanRestart(
            package,
            semantic,
            new WordNumberingSequenceRestartCommand(
                new SemanticNodeId(targetParagraphNodeId),
                expectedNumberId,
                expectedLevelIndex,
                startValue
            ),
            cancellationToken
        );
        var validation = ValidateExactCandidate(package, plan, cancellationToken);
        using var candidateStream = new MemoryStream();
        _serializer.Write(candidateStream, plan.CreateMutation(package));
        candidateStream.Position = 0;
        var candidate = _reader.Read(candidateStream, cancellationToken);
        var projector = new WordSemanticProjector();
        var projectedSemantic = projector.Project(package, cancellationToken);
        var candidateSemantic = projector.Project(candidate, cancellationToken);
        return new PlanContext(
            path,
            package,
            plan,
            WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package),
            validation,
            WordPackagePatchRiskAnalyzer.AssessProtection(
                package, projectedSemantic, candidate, candidateSemantic, plan.HasChanges, cancellationToken)
        );
    }

    private WordPackageCandidateValidationReport ValidateExactCandidate(
        OpcPackageSnapshot package,
        WordNumberingSequenceRepairPlan plan,
        CancellationToken cancellationToken
    )
    {
        using var baseline = new MemoryStream();
        _serializer.Write(baseline, new OpcPackageMutationBuilder(package));
        using var candidate = new MemoryStream();
        _serializer.Write(candidate, plan.CreateMutation(package));
        candidate.Position = 0;
        var candidateSnapshot = _reader.Read(candidate, cancellationToken);
        if (!candidateSnapshot.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "The exact numbering repair candidate has structural OPC errors"
            );
        }
        if (!string.Equals(
                candidateSnapshot.Fingerprint,
                plan.ResultPackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "The exact candidate package does not match the planned result fingerprint"
            );
        }
        if (_candidateValidator is null)
        {
            return WordPackageCandidateValidationReport.NotPerformed(
                "schema_validator_unavailable"
            );
        }
        baseline.Position = 0;
        candidate.Position = 0;
        try
        {
            return BoundValidation(
                _candidateValidator.Validate(baseline, candidate, cancellationToken)
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

    private static NumberingRepairPlanResult ProjectPlan(
        PlanContext context,
        bool includeDetails
    )
    {
        var blocked = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blocked.Add("digital_signature_present");
        }
        if (context.Plan.HasChanges && context.Protection.HasMalformedProtectionMetadata)
            blocked.Add("protection_metadata_malformed");
        else if (context.Plan.HasChanges && context.Protection.AuthorizationRequired)
            blocked.Add("protected_document_edit_not_authorized");
        if (!context.Plan.Validation.Passed)
        {
            blocked.Add("engine_validation_failed");
        }
        if (!context.Validation.Performed)
        {
            blocked.Add("schema_validator_unavailable");
        }
        else if (!context.Validation.NoNewErrors)
        {
            blocked.Add("microsoft_schema_validation_failed");
        }
        return new NumberingRepairPlanResult(
            NumberingRepairWordPackageContract.PlanContract,
            Path.GetFileName(context.Path),
            "restart_numbering_sequence",
            "remaining_instance_in_story",
            context.Plan.PlanId,
            context.Plan.BasePackageFingerprint,
            context.Plan.ResultPackageFingerprint,
            context.Plan.TargetParagraphNodeId.Value,
            SnakeCase(context.Plan.StoryKind),
            context.Plan.SourceNumberId,
            context.Plan.NewNumberId,
            context.Plan.AbstractNumberId,
            context.Plan.LevelIndex,
            context.Plan.StartValue,
            context.Plan.TargetCounterBefore,
            SnakeCase(context.Plan.TargetCounterStatusBefore),
            context.Plan.TargetCounterAfter,
            SnakeCase(context.Plan.TargetCounterStatusAfter),
            context.Plan.AffectedParagraphs.Count,
            includeDetails && context.Plan.AffectedParagraphs.Count > 200,
            context.Plan.DirectNumberingMaterializedCount,
            context.Plan.ChangedParts.Count,
            context.Plan.ChangedParts.Sum(part =>
                (long)part.AfterBytes - part.BeforeBytes
            ),
            context.Plan.HasChanges,
            CanApply: blocked.Count == 0,
            ApplyBlocked: blocked.Count != 0,
            ApplyBlockedReasons: blocked,
            EngineValidation: context.Plan.Validation,
            CandidateValidation: includeDetails
                ? context.Validation
                : context.Validation with
                {
                    ErrorsTruncated = context.Validation.ErrorsTruncated
                        || context.Validation.Issues.Count != 0,
                    Issues = Array.Empty<WordPackageValidationIssue>(),
                },
            CompatibilityRules: context.Plan.CompatibilityRules,
            AffectedParagraphs: includeDetails
                ? context.Plan.AffectedParagraphs.Take(200).Select(item =>
                    new NumberingRepairParagraphDetail(
                        item.ParagraphNodeId.Value,
                        item.LevelIndex,
                        item.BeforeCounterValue,
                        SnakeCase(item.BeforeCounterStatus),
                        item.DirectNumberingMaterialized
                    )
                ).ToArray()
                : null,
            ChangedParts: includeDetails
                ? context.Plan.ChangedParts.Select(part =>
                    new NumberingRepairChangedPart(
                        part.PartUri,
                        part.BeforeSha256,
                        part.AfterSha256,
                        part.BeforeBytes,
                        part.AfterBytes,
                        (long)part.AfterBytes - part.BeforeBytes
                    )
                ).ToArray()
                : null,
            ParagraphTextReturned: false,
            RawXmlReturned: false,
            MutationPerformed: false,
            WordOpened: false,
            Protection: context.Protection,
            ProtectionAuthorizationId: context.Plan.HasChanges
                && context.Protection.AuthorizationRequired
                && !context.Protection.HasMalformedProtectionMetadata
                    ? context.Plan.PlanId
                    : null,
            RequiredAuthorizations: context.Plan.HasChanges
                && context.Protection.AuthorizationRequired
                && !context.Protection.HasMalformedProtectionMetadata
                    ? ["protected_edit_authorization"]
                    : Array.Empty<string>()
        );
    }

    private static void ValidateRequest(
        string localPath,
        string expectedFingerprint,
        string targetParagraphNodeId,
        int expectedNumberId,
        int expectedLevelIndex,
        int startValue
    )
    {
        if (string.IsNullOrWhiteSpace(localPath)
            || localPath.Length > NumberingRepairWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(localPath))
        {
            throw Invalid("Numbering repair accepts DOCX, DOCM, DOTX, or DOTM files");
        }
        if (expectedFingerprint is null
            || expectedFingerprint.Length != 64
            || !expectedFingerprint.All(Uri.IsHexDigit))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (string.IsNullOrWhiteSpace(targetParagraphNodeId)
            || targetParagraphNodeId.Length is < 5 or > 128
            || !targetParagraphNodeId.StartsWith("wdn_", StringComparison.Ordinal)
            || targetParagraphNodeId[4..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            ))
        {
            throw Invalid("target_paragraph_node_id is not a valid semantic node ID");
        }
        if (expectedNumberId <= 0)
        {
            throw Invalid("expected_number_id must be between 1 and 2147483647");
        }
        if (expectedLevelIndex is < 0 or > 8)
        {
            throw Invalid("expected_level_index must be between 0 and 8");
        }
        if (startValue < 0)
        {
            throw Invalid("start_value must be between 0 and 2147483647");
        }
    }

    private static string ResolvePath(string localPath)
    {
        try
        {
            var path = Path.GetFullPath(localPath);
            if (!File.Exists(path))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    "The requested Word package does not exist"
                );
            }
            return path;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw Invalid("local_path is not a valid filesystem path", exception);
        }
    }

    private static WordPackageCandidateValidationReport BoundValidation(
        WordPackageCandidateValidationReport report
    )
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.ErrorCount < 0
            || report.BaselineErrorCount < 0
            || report.CandidateErrorCount < 0
            || report.Issues is null
            || report.Issues.Count > 200
            || report.ErrorCount < report.Issues.Count
            || report.NoNewErrors && report.ErrorCount != 0
            || report.CandidateValid && report.CandidateErrorCount != 0
            || report.Performed && report.NotPerformedReason is not null
            || !report.Performed && (
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
                && report.ErrorCount != report.Issues.Count)
        {
            throw new InvalidOperationException(
                "Candidate validator returned an invalid or unbounded report."
            );
        }
        return report with
        {
            NotPerformedReason = Bound(report.NotPerformedReason, 128),
            Issues = report.Issues.Select(issue => new WordPackageValidationIssue(
                Bound(issue.Id, 128),
                Bound(issue.ErrorType, 64) ?? "Unknown",
                Bound(issue.PartUri, 512),
                Bound(issue.Path, 512),
                Bound(issue.Node, 128)
            )).ToArray(),
        };
    }

    private static bool IsPlanId(string value) => value is not null
        && value.Length is >= 16 and <= 128
        && value.StartsWith("wnrplan_", StringComparison.Ordinal)
        && value[8..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
        );

    private static string SnakeCase<T>(T value) where T : struct, Enum
    {
        var source = value.ToString();
        var result = new StringBuilder(source.Length + 8);
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (char.IsUpper(character) && index != 0)
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static string? Bound(string? value, int maximum) =>
        value is null || value.Length <= maximum ? value : value[..maximum] + "…";

    private static string? SafeReason(string? message, string? localPath)
    {
        if (message is null)
        {
            return null;
        }
        var safe = message;
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            safe = safe.Replace(localPath, "<redacted>", StringComparison.OrdinalIgnoreCase);
        }
        return Bound(safe, 512);
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath
    ) => exception switch
    {
        WordSemanticTransactionLimitException limit => new WordToolkitOperationException(
            "TRANSACTION_LIMIT",
            SafeReason(limit.Message, localPath) ?? "Numbering repair limit exceeded",
            innerException: limit
        ),
        WordListSequenceLimitException limit => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            SafeReason(limit.Message, localPath) ?? "Numbering sequence limit exceeded",
            innerException: limit
        ),
        WordSemanticPreconditionException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            SafeReason(conflict.Message, localPath) ?? "Numbering repair precondition failed",
            innerException: conflict
        ),
        WordSemanticEditException edit => new WordToolkitOperationException(
            "UNSAFE_REPAIR",
            SafeReason(edit.Message, localPath) ?? "Numbering repair is unsafe",
            innerException: edit
        ),
        WordSemanticLimitException or WordStyleLimitException
            or WordNumberingLimitException => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Word package projection exceeds a bounded safety limit",
                SafeReason(exception.Message, localPath),
                innerException: exception
            ),
        WordSemanticProjectionException or WordStyleProjectionException
            or WordNumberingProjectionException or WordListSequenceProjectionException =>
                new WordToolkitOperationException(
                    "INVALID_WORD_PACKAGE",
                    "The package cannot be projected safely for numbering repair",
                    SafeReason(exception.Message, localPath),
                    innerException: exception
                ),
        OpcPackageConcurrencyException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            "Destination package changed during the atomic write",
            SafeReason(conflict.Message, localPath),
            retryable: true,
            innerException: conflict
        ),
        OpcPackageResultMismatchException mismatch => new WordToolkitOperationException(
            "RESULT_MISMATCH",
            "Candidate package does not match the reviewed numbering repair plan",
            SafeReason(mismatch.Message, localPath),
            innerException: mismatch
        ),
        OpcPackageValidationException validation => new WordToolkitOperationException(
            "VALIDATION_FAILED",
            "Candidate package failed structural validation",
            SafeReason(validation.Message, localPath),
            innerException: validation
        ),
        OpcPackageRecoveryException recovery => new WordToolkitOperationException(
            "RECOVERY_REQUIRED",
            "Atomic commit detected a concurrent change and recovery requires inspection",
            retryable: false,
            innerException: recovery
        ),
        OpcPackageLimitException limit => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            "The package exceeds a bounded OPC safety limit",
            SafeReason(limit.Message, localPath),
            innerException: limit
        ),
        InvalidDataException invalid => new WordToolkitOperationException(
            "INVALID_PACKAGE",
            "The file is not a readable OPC ZIP package",
            innerException: invalid
        ),
        FileNotFoundException or DirectoryNotFoundException => new WordToolkitOperationException(
            "NOT_FOUND",
            "The requested Word package does not exist",
            innerException: exception
        ),
        UnauthorizedAccessException => new WordToolkitOperationException(
            "ACCESS_DENIED",
            "The Word package cannot be read or written with current permissions",
            innerException: exception
        ),
        IOException io => new WordToolkitOperationException(
            "IO_ERROR",
            "The numbering repair package could not be read or written",
            SafeReason(io.Message, localPath),
            retryable: true,
            innerException: io
        ),
        ArgumentException argument => Invalid(
            SafeReason(argument.Message, localPath) ?? "Invalid numbering repair request",
            argument
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The numbering repair operation failed",
            innerException: exception
        ),
    };

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);

    private sealed record PlanContext(
        string Path,
        OpcPackageSnapshot Package,
        WordNumberingSequenceRepairPlan Plan,
        bool HasDigitalSignatures,
        WordPackageCandidateValidationReport Validation,
        WordPackageProtectionRiskAssessment Protection
    );

    private static IReadOnlyList<string> ProtectionBlockCodes(PlanContext context, string? authorization)
    {
        if (!context.Plan.HasChanges) return Array.Empty<string>();
        if (context.Protection.HasMalformedProtectionMetadata) return ["protection_metadata_malformed"];
        if (context.Protection.AuthorizationRequired
            && !string.Equals(
                authorization,
                context.Plan.PlanId,
                StringComparison.Ordinal
            ))
            return ["protected_document_edit_not_authorized"];
        return Array.Empty<string>();
    }
}
