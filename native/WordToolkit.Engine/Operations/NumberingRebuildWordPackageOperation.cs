using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Inspects, plans, and atomically applies semantic numbering reconstruction. The
/// caller supplies typed list semantics and package-bound paragraph candidates;
/// raw OOXML is never accepted or returned.
/// </summary>
public sealed class NumberingRebuildWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer = new();
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public NumberingRebuildWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public NumberingRebuildInspectResult Inspect(
        NumberingRebuildInspectRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ParagraphNodeIds is null
                || request.ParagraphNodeIds.Count is < 1
                    or > NumberingRebuildWordPackageContract.MaximumInspectionItems)
            {
                throw Invalid(
                    $"paragraph_node_ids must contain between 1 and {NumberingRebuildWordPackageContract.MaximumInspectionItems} items"
                );
            }
            var context = ReadContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                cancellationToken
            );
            var nodeIds = request.ParagraphNodeIds.Select(value =>
            {
                ValidateNodeId(value, "paragraph_node_ids");
                return new SemanticNodeId(value);
            }).ToArray();
            if (nodeIds.Distinct().Count() != nodeIds.Length)
            {
                throw Invalid("paragraph_node_ids must be unique");
            }
            var candidates = new WordNumberingRebuildCandidateInspector(
                RebuildOptions()
            ).Inspect(
                context.Package,
                context.Semantic,
                nodeIds,
                cancellationToken
            );
            return new NumberingRebuildInspectResult(
                NumberingRebuildWordPackageContract.InspectContract,
                Path.GetFileName(context.Path),
                context.Package.Fingerprint,
                candidates.Count,
                candidates.Select(candidate => new NumberingRebuildCandidateDetail(
                    candidate.ParagraphNodeId.Value,
                    candidate.Fingerprint,
                    SnakeCase(candidate.StoryKind),
                    candidate.SourcePartUri,
                    candidate.SourcePath,
                    candidate.SourceOrder,
                    candidate.CurrentNumberId,
                    candidate.CurrentLevelIndex,
                    candidate.CanRebuild,
                    candidate.BlockedReasons
                )).ToArray(),
                ParagraphTextReturned: false,
                RawXmlReturned: false,
                MutationPerformed: false,
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
            throw MapFailure(exception, request?.LocalPath);
        }
    }

    public NumberingRebuildPlanResult Plan(
        NumberingRebuildPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var context = BuildPlanContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.Commands,
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

    public NumberingRebuildApplyResult Apply(
        NumberingRebuildApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid numbering rebuild plan ID");
            }
            var context = BuildPlanContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.Commands,
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
                    "The request does not reproduce the reviewed numbering rebuild plan ID"
                );
            }
            var protectionBlocks = ProtectionBlockCodes(
                context,
                request.ProtectedEditAuthorization
            );
            if (protectionBlocks.Count != 0)
            {
                throw new WordToolkitOperationException(
                    "EDIT_POLICY_BLOCKED",
                    "Numbering rebuild is blocked by document protection or permission metadata",
                    details: new NumberingRebuildEditPolicyBlockDetails(
                        context.Plan.PlanId,
                        protectionBlocks
                    )
                );
            }
            if (!_candidateValidatorAvailable(context.Validation))
            {
                throw new WordToolkitOperationException(
                    "VALIDATOR_REQUIRED",
                    "Applying numbering reconstruction requires a candidate package schema validator"
                );
            }
            if (!context.Validation.NoNewErrors)
            {
                var issues = context.Validation.Issues.Take(20).ToArray();
                throw new WordToolkitOperationException(
                    "OOXML_SCHEMA_INVALID",
                    "The exact numbering reconstruction candidate introduces Microsoft Open XML schema errors",
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
                return new NumberingRebuildApplyResult(
                    NumberingRebuildWordPackageContract.ApplyContract,
                    Path.GetFileName(context.Path),
                    "semantic_numbering_reconstruction",
                    context.Plan.PlanId,
                    Applied: false,
                    NoOp: true,
                    CommandCount: context.Plan.Commands.Count,
                    TargetCount: context.Plan.TargetCount,
                    PreviousPackageFingerprint: context.Package.Fingerprint,
                    PackageFingerprint: context.Package.Fingerprint,
                    PredictedPackageFingerprint: context.Plan.ResultPackageFingerprint,
                    BackupPath: null,
                    ChangedEntryNames: Array.Empty<string>(),
                    DiagnosticCount: 0,
                    MicrosoftSchemaValid: context.Validation.CandidateValid,
                    MicrosoftSchemaNoNewErrors: context.Validation.NoNewErrors,
                    ParagraphTextReturned: false,
                    RawXmlReturned: false,
                    MutationPerformed: false,
                    WordOpened: false,
                    ExplicitAuthorizations: Array.Empty<string>()
                );
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
            return new NumberingRebuildApplyResult(
                NumberingRebuildWordPackageContract.ApplyContract,
                Path.GetFileName(context.Path),
                "semantic_numbering_reconstruction",
                context.Plan.PlanId,
                Applied: true,
                NoOp: false,
                CommandCount: context.Plan.Commands.Count,
                TargetCount: context.Plan.TargetCount,
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
                    ? ["protected_edit_authorization"]
                    : Array.Empty<string>()
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

    private PlanContext BuildPlanContext(
        string localPath,
        string expectedFingerprint,
        IReadOnlyList<WordNumberingRebuildCommand> commands,
        CancellationToken cancellationToken
    )
    {
        var context = ReadContext(localPath, expectedFingerprint, cancellationToken);
        if (WordPackagePatchRiskAnalyzer.HasDigitalSignatures(context.Package))
        {
            throw new WordToolkitOperationException(
                "SIGNED_PACKAGE",
                "Numbering rebuild is blocked because the package contains digital signatures"
            );
        }
        var plan = new WordNumberingRebuildPlanner(RebuildOptions()).Plan(
            context.Package,
            context.Semantic,
            commands,
            cancellationToken
        );
        var validation = ValidateExactCandidate(
            context.Package,
            plan,
            cancellationToken
        );
        using var stream = new MemoryStream();
        _serializer.Write(stream, plan.CreateMutation(context.Package));
        stream.Position = 0;
        var candidate = _reader.Read(stream, cancellationToken);
        var projector = new WordSemanticProjector();
        var candidateSemantic = projector.Project(candidate, cancellationToken);
        return new PlanContext(
            context.Path,
            context.Package,
            plan,
            validation,
            WordPackagePatchRiskAnalyzer.AssessProtection(
                context.Package,
                context.Semantic,
                candidate,
                candidateSemantic,
                plan.HasChanges,
                cancellationToken
            )
        );
    }

    private ReadContextResult ReadContext(
        string localPath,
        string expectedFingerprint,
        CancellationToken cancellationToken
    )
    {
        ValidatePathAndFingerprint(localPath, expectedFingerprint);
        var path = ResolvePath(localPath);
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
                "Saved package changed before the numbering rebuild operation"
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
        return new ReadContextResult(path, package, semantic);
    }

    private WordPackageCandidateValidationReport ValidateExactCandidate(
        OpcPackageSnapshot package,
        WordNumberingRebuildPlan plan,
        CancellationToken cancellationToken
    )
    {
        using var baseline = new MemoryStream();
        _serializer.Write(baseline, new OpcPackageMutationBuilder(package));
        using var candidate = new MemoryStream();
        _serializer.Write(candidate, plan.CreateMutation(package));
        candidate.Position = 0;
        var candidateSnapshot = _reader.Read(candidate, cancellationToken);
        if (!candidateSnapshot.IsStructurallyValid
            || !string.Equals(
                candidateSnapshot.Fingerprint,
                plan.ResultPackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "The exact candidate package does not match the reviewed numbering rebuild plan"
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

    private static NumberingRebuildPlanResult ProjectPlan(
        PlanContext context,
        bool includeDetails
    )
    {
        var blocked = new List<string>();
        if (!context.Plan.Validation.Passed)
        {
            blocked.Add("engine_validation_failed");
        }
        if (context.Plan.HasChanges && context.Protection.HasMalformedProtectionMetadata)
            blocked.Add("protection_metadata_malformed");
        else if (context.Plan.HasChanges && context.Protection.AuthorizationRequired)
            blocked.Add("protected_document_edit_not_authorized");
        if (!context.Validation.Performed)
        {
            blocked.Add("schema_validator_unavailable");
        }
        else if (!context.Validation.NoNewErrors)
        {
            blocked.Add("microsoft_schema_validation_failed");
        }
        var detailsTruncated = includeDetails && context.Plan.TargetCount > 200;
        return new NumberingRebuildPlanResult(
            NumberingRebuildWordPackageContract.PlanContract,
            Path.GetFileName(context.Path),
            "semantic_numbering_reconstruction",
            context.Plan.PlanId,
            context.Plan.BasePackageFingerprint,
            context.Plan.ResultPackageFingerprint,
            context.Plan.NumberingPartUri,
            context.Plan.NumberingPartCreated,
            context.Plan.Commands.Count,
            context.Plan.TargetCount,
            context.Plan.ChangedEntries.Count,
            context.Plan.ChangedEntries.Sum(entry =>
                (long)entry.AfterBytes - entry.BeforeBytes
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
            Commands: context.Plan.Commands.Select(command =>
                new NumberingRebuildCommandDetail(
                    command.CommandId,
                    command.AbstractNumberId,
                    command.NumberId,
                    command.NamespaceId,
                    command.TemplateCode,
                    SnakeCase(command.MultiLevelKind),
                    command.RestartAfterSectionBreak,
                    command.LevelCount,
                    command.TargetCount,
                    includeDetails
                        ? command.Targets.Take(200).Select(target =>
                            new NumberingRebuildTargetDetail(
                                target.ParagraphNodeId.Value,
                                target.CandidateFingerprint,
                                SnakeCase(target.StoryKind),
                                target.SourcePartUri,
                                target.SourcePath,
                                target.SourceOrder,
                                target.LevelIndex,
                                target.PreviousNumberId,
                                target.PreviousLevelIndex,
                                target.CounterValue,
                                SnakeCase(target.CounterStatus),
                                target.Label,
                                SnakeCase(target.LabelStatus),
                                target.DirectNumberingMaterialized
                            )
                        ).ToArray()
                        : null
                )
            ).ToArray(),
            ChangedEntries: includeDetails
                ? context.Plan.ChangedEntries.Select(entry =>
                    new NumberingRebuildChangedEntryDetail(
                        entry.EntryName,
                        entry.PartUri,
                        SnakeCase(entry.Kind),
                        entry.BeforeSha256,
                        entry.AfterSha256,
                        entry.BeforeBytes,
                        entry.AfterBytes,
                        (long)entry.AfterBytes - entry.BeforeBytes
                    )
                ).ToArray()
                : null,
            DetailsTruncated: detailsTruncated,
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

    private static WordNumberingRebuildOptions RebuildOptions() => new()
    {
        MaxCommands = NumberingRebuildWordPackageContract.MaximumCommands,
        MaxTargets = NumberingRebuildWordPackageContract.MaximumTargets,
        MaxChangedEntries = NumberingRebuildWordPackageContract.MaximumChangedEntries,
        MaxCandidateInspectionItems =
            NumberingRebuildWordPackageContract.MaximumInspectionItems,
    };

    private static void ValidatePathAndFingerprint(
        string localPath,
        string expectedFingerprint
    )
    {
        if (string.IsNullOrWhiteSpace(localPath)
            || localPath.Length > NumberingRebuildWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(localPath))
        {
            throw Invalid("Numbering reconstruction accepts DOCX, DOCM, DOTX, or DOTM files");
        }
        if (expectedFingerprint is null
            || expectedFingerprint.Length != 64
            || !expectedFingerprint.All(Uri.IsHexDigit))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
    }

    private static void ValidateNodeId(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length is < 5 or > 128
            || !value.StartsWith("wdn_", StringComparison.Ordinal)
            || value[4..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            ))
        {
            throw Invalid($"{field} contains an invalid semantic paragraph node ID");
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

    private static bool _candidateValidatorAvailable(
        WordPackageCandidateValidationReport validation
    ) => validation.Performed;

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
        && value.StartsWith("wnrbplan_", StringComparison.Ordinal)
        && value[9..].All(character =>
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
            SafeReason(limit.Message, localPath) ?? "Numbering rebuild limit exceeded",
            innerException: limit
        ),
        WordSemanticPreconditionException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            SafeReason(conflict.Message, localPath) ?? "Numbering rebuild precondition failed",
            innerException: conflict
        ),
        WordSemanticEditException edit => new WordToolkitOperationException(
            "UNSAFE_REBUILD",
            SafeReason(edit.Message, localPath) ?? "Numbering reconstruction is unsafe",
            innerException: edit
        ),
        WordSemanticLimitException or WordStyleLimitException
            or WordNumberingLimitException or WordListSequenceLimitException =>
                new WordToolkitOperationException(
                    "PACKAGE_LIMIT",
                    "Word package projection exceeds a bounded safety limit",
                    SafeReason(exception.Message, localPath),
                    innerException: exception
                ),
        WordSemanticProjectionException or WordStyleProjectionException
            or WordNumberingProjectionException or WordListSequenceProjectionException =>
                new WordToolkitOperationException(
                    "INVALID_WORD_PACKAGE",
                    "The package cannot be projected safely for numbering reconstruction",
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
            "Candidate package does not match the reviewed numbering rebuild plan",
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
            "Atomic commit recovery requires inspection",
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
            "The numbering rebuild package could not be read or written",
            SafeReason(io.Message, localPath),
            retryable: true,
            innerException: io
        ),
        ArgumentException argument => Invalid(
            SafeReason(argument.Message, localPath) ?? "Invalid numbering rebuild request",
            argument
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The numbering rebuild operation failed",
            innerException: exception
        ),
    };

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);

    private sealed record ReadContextResult(
        string Path,
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic
    );

    private sealed record PlanContext(
        string Path,
        OpcPackageSnapshot Package,
        WordNumberingRebuildPlan Plan,
        WordPackageCandidateValidationReport Validation,
        WordPackageProtectionRiskAssessment Protection
    );

    private static IReadOnlyList<string> ProtectionBlockCodes(
        PlanContext context,
        string? authorization
    )
    {
        if (!context.Plan.HasChanges)
        {
            return Array.Empty<string>();
        }
        if (context.Protection.HasMalformedProtectionMetadata)
        {
            return ["protection_metadata_malformed"];
        }
        if (context.Protection.AuthorizationRequired
            && !string.Equals(
                authorization,
                context.Plan.PlanId,
                StringComparison.Ordinal
            ))
        {
            return ["protected_document_edit_not_authorized"];
        }
        return Array.Empty<string>();
    }
}
