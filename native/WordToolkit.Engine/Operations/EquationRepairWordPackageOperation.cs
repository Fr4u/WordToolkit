using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Inspects and applies only fingerprint-bound removal of canonically identical,
/// schema-invalid duplicate OfficeMath properties and property containers.
/// </summary>
public sealed class EquationRepairWordPackageOperation
{
    private static readonly string[] SupportedIssueCodes =
    [
        "MATH_PARAGRAPH_PROPERTIES_DUPLICATE",
        "MATH_PROPERTIES_DUPLICATE",
        "MATH_RUN_PROPERTIES_DUPLICATE",
        "MATH_SETTINGS_DUPLICATE",
        "MATH_PROPERTY_DUPLICATE",
    ];

    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer = new();
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public EquationRepairWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public EquationRepairInspectionResult Inspect(
        EquationRepairInspectionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateCommon(request.LocalPath, request.ExpectedPackageFingerprint);
            if (request.MaxItems is < 1
                or > EquationRepairWordPackageContract.MaximumReturnedItems)
            {
                throw Invalid(
                    $"max_items must be between 1 and {EquationRepairWordPackageContract.MaximumReturnedItems}"
                );
            }
            var path = ResolvePath(request.LocalPath);
            var package = ReadExpected(path, request.ExpectedPackageFingerprint, cancellationToken);
            ValidateWordPackage(path, package, cancellationToken);
            var catalog = new WordEquationRepairPlanner().Inspect(package, cancellationToken);
            ValidatePublicBounds(catalog);
            var candidatePage = catalog.Candidates.Take(request.MaxItems).ToArray();
            var issuePage = request.IncludeIssues
                ? catalog.EquationGraph.Issues.Take(request.MaxItems).ToArray()
                : Array.Empty<WordEquationIssue>();
            var observed = catalog.EquationGraph.Issues
                .Select(issue => issue.Code)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            return new EquationRepairInspectionResult(
                EquationRepairWordPackageContract.InspectContract,
                Path.GetFileName(path),
                package.Fingerprint,
                catalog.AnalysisExecutionComplete,
                catalog.RepairCoverageComplete,
                catalog.EquationGraph.IssuesTruncated,
                catalog.EquationGraph.Equations.Count,
                catalog.EquationGraph.MalformedEquationCount,
                catalog.EquationGraph.UnsupportedEquationCount,
                catalog.EquationGraph.Issues.Count,
                catalog.EquationGraph.Issues.Count(issue =>
                    issue.Severity == WordEquationIssueSeverity.Error
                ),
                catalog.EquationGraph.Issues.Count(issue =>
                    issue.Severity == WordEquationIssueSeverity.Warning
                ),
                catalog.Candidates.Count,
                catalog.Candidates.Count(candidate =>
                    candidate.Kind
                        == WordEquationRepairKind.RemoveRedundantDuplicatePropertyContainer
                ),
                catalog.Candidates.Count(candidate =>
                    candidate.Kind == WordEquationRepairKind.RemoveRedundantDuplicateProperty
                ),
                candidatePage.Length,
                candidatePage.Length < catalog.Candidates.Count,
                candidatePage.Select(candidate => ProjectCandidate(
                    candidate,
                    request.IncludeSource
                )).ToArray(),
                issuePage.Length,
                request.IncludeIssues
                    && (catalog.EquationGraph.IssuesTruncated
                        || issuePage.Length < catalog.EquationGraph.Issues.Count),
                request.IncludeIssues
                    ? issuePage.Select(issue => ProjectIssue(
                        issue,
                        catalog.Candidates,
                        request.IncludeSource
                    )).ToArray()
                    : null,
                SupportedIssueCodes,
                observed.Where(code => !SupportedIssueCodes.Contains(code, StringComparer.Ordinal))
                    .OrderBy(code => code, StringComparer.Ordinal)
                    .ToArray(),
                SensitiveEquationTextReturned: false,
                RawOmmlReturned: false,
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

    public EquationRepairPlanResult Plan(
        EquationRepairPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return ProjectPlan(BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.Commands,
                cancellationToken
            ), request.IncludeDetails);
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

    public EquationRepairApplyResult Apply(
        EquationRepairApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid OfficeMath repair plan ID");
            }
            var context = BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.Commands,
                cancellationToken
            );
            if (!string.Equals(context.Plan.PlanId, request.ExpectedPlanId, StringComparison.Ordinal))
            {
                throw new WordToolkitOperationException(
                    "PLAN_MISMATCH",
                    "The request does not reproduce the reviewed OfficeMath repair plan ID"
                );
            }
            if (context.HasDigitalSignatures)
            {
                throw new WordToolkitOperationException(
                    "SIGNED_PACKAGE",
                    "OfficeMath repair is blocked because the package contains digital signatures"
                );
            }
            if (!context.Validation.Performed)
            {
                throw new WordToolkitOperationException(
                    "VALIDATOR_REQUIRED",
                    "Applying OfficeMath repair requires a candidate package schema validator"
                );
            }
            if (!context.Validation.NoNewErrors)
            {
                throw ValidationFailure(
                    context.Validation,
                    "The exact OfficeMath repair candidate introduces Microsoft Open XML schema errors"
                );
            }
            if (!SchemaErrorsReduced(context.Validation))
            {
                throw ValidationFailure(
                    context.Validation,
                    "The exact OfficeMath repair candidate does not reduce Microsoft Open XML schema errors"
                );
            }

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
            return new EquationRepairApplyResult(
                EquationRepairWordPackageContract.ApplyContract,
                Path.GetFileName(context.Path),
                context.Plan.PlanId,
                Applied: true,
                context.Plan.Candidates.Count,
                context.Plan.Candidates.Sum(candidate => candidate.RemovedElementCount),
                context.Plan.Validation.RemovedElementCount,
                context.Package.Fingerprint,
                result.Fingerprint,
                context.Plan.ResultPackageFingerprint,
                result.BackupPath,
                result.ChangedEntryNames,
                result.Diagnostics.Count,
                context.Validation.NoNewErrors,
                SchemaErrorsReduced(context.Validation),
                SensitiveEquationTextReturned: false,
                RawOmmlReturned: false,
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
            throw MapFailure(exception, request?.LocalPath);
        }
    }

    private PlanContext BuildContext(
        string localPath,
        string expectedPackageFingerprint,
        IReadOnlyList<EquationRepairCommandRequest> commands,
        CancellationToken cancellationToken
    )
    {
        ValidateCommon(localPath, expectedPackageFingerprint);
        var parsedCommands = ParseCommands(commands);
        var path = ResolvePath(localPath);
        var package = ReadExpected(path, expectedPackageFingerprint, cancellationToken);
        ValidateWordPackage(path, package, cancellationToken);
        var plan = new WordEquationRepairPlanner().Plan(
            package,
            parsedCommands,
            cancellationToken
        );
        var candidate = MaterializeCandidate(package, plan, cancellationToken);
        var validation = ValidateExactCandidate(package, candidate, cancellationToken);
        return new PlanContext(
            path,
            package,
            candidate,
            plan,
            WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package),
            validation
        );
    }

    private OpcPackageSnapshot MaterializeCandidate(
        OpcPackageSnapshot package,
        WordEquationRepairPlan plan,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream();
        _serializer.Write(stream, plan.CreateMutation(package));
        stream.Position = 0;
        var candidate = _reader.Read(stream, cancellationToken);
        if (!candidate.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "The exact OfficeMath repair candidate has structural OPC errors"
            );
        }
        if (!string.Equals(
                candidate.Fingerprint,
                plan.ResultPackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "The exact OfficeMath repair candidate does not match the planned result fingerprint"
            );
        }
        return candidate;
    }

    private WordPackageCandidateValidationReport ValidateExactCandidate(
        OpcPackageSnapshot package,
        OpcPackageSnapshot candidate,
        CancellationToken cancellationToken
    )
    {
        if (_candidateValidator is null)
        {
            return WordPackageCandidateValidationReport.NotPerformed(
                "schema_validator_unavailable"
            );
        }
        using var baselineStream = new MemoryStream();
        _serializer.Write(baselineStream, new OpcPackageMutationBuilder(package));
        using var candidateStream = new MemoryStream();
        _serializer.Write(candidateStream, new OpcPackageMutationBuilder(candidate));
        baselineStream.Position = 0;
        candidateStream.Position = 0;
        try
        {
            return BoundValidation(_candidateValidator.Validate(
                baselineStream,
                candidateStream,
                cancellationToken
            ));
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

    private static EquationRepairPlanResult ProjectPlan(
        PlanContext context,
        bool includeDetails
    )
    {
        var blocked = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blocked.Add("digital_signature_present");
        }
        if (!context.Plan.Validation.Passed)
        {
            blocked.Add("engine_validation_failed");
        }
        if (!context.Validation.Performed)
        {
            blocked.Add("schema_validator_unavailable");
        }
        else
        {
            if (!context.Validation.NoNewErrors)
            {
                blocked.Add("microsoft_schema_validation_failed");
            }
            if (!SchemaErrorsReduced(context.Validation))
            {
                blocked.Add("microsoft_schema_errors_not_reduced");
            }
        }
        return new EquationRepairPlanResult(
            EquationRepairWordPackageContract.PlanContract,
            Path.GetFileName(context.Path),
            context.Plan.PlanId,
            context.Plan.BasePackageFingerprint,
            context.Plan.ResultPackageFingerprint,
            context.Plan.Candidates.Count,
            context.Plan.Candidates.Count,
            context.Plan.Candidates.Sum(candidate => candidate.RemovedElementCount),
            context.Plan.Validation.RemovedElementCount,
            context.Plan.ChangedParts.Count,
            context.Plan.ChangedParts.Sum(part => (long)part.AfterBytes - part.BeforeBytes),
            context.Plan.HasChanges,
            CanApply: blocked.Count == 0,
            ApplyBlocked: blocked.Count != 0,
            ApplyBlockedReasons: blocked,
            context.Plan.Validation,
            includeDetails
                ? context.Validation
                : context.Validation with
                {
                    ErrorsTruncated = context.Validation.ErrorsTruncated
                        || context.Validation.Issues.Count != 0,
                    Issues = Array.Empty<WordPackageValidationIssue>(),
                },
            SchemaErrorsReduced(context.Validation),
            context.Plan.SafetyRules,
            includeDetails
                ? context.Plan.Candidates.Select(candidate =>
                    ProjectCandidate(candidate, includeSource: true)
                ).ToArray()
                : null,
            includeDetails ? context.Plan.ChangedParts : null,
            SensitiveEquationTextReturned: false,
            RawOmmlReturned: false,
            MutationPerformed: false,
            WordOpened: false
        );
    }

    private static IReadOnlyList<WordEquationRepairCommand> ParseCommands(
        IReadOnlyList<EquationRepairCommandRequest> commands
    )
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count is < 1 or > EquationRepairWordPackageContract.MaximumCommands)
        {
            throw Invalid(
                $"commands must contain between 1 and {EquationRepairWordPackageContract.MaximumCommands} items"
            );
        }
        return commands.Select(command =>
        {
            ArgumentNullException.ThrowIfNull(command);
            var kind = command.RepairKind switch
            {
                "remove_redundant_duplicate_property_container" =>
                    WordEquationRepairKind.RemoveRedundantDuplicatePropertyContainer,
                "remove_redundant_duplicate_property" =>
                    WordEquationRepairKind.RemoveRedundantDuplicateProperty,
                _ => throw Invalid(
                    "repair_kind must be remove_redundant_duplicate_property_container or remove_redundant_duplicate_property"
                ),
            };
            return new WordEquationRepairCommand(
                kind,
                command.CandidateId,
                command.ExpectedCandidateFingerprint
            );
        }).ToArray();
    }

    private static EquationRepairInspectionCandidate ProjectCandidate(
        WordEquationRepairCandidate candidate,
        bool includeSource
    ) => new(
        candidate.Id,
        candidate.Fingerprint,
        RepairKindName(candidate.Kind),
        candidate.IssueCode,
        candidate.RemovedElementCount,
        candidate.RemovedXmlElementCount,
        candidate.ParentElementName,
        candidate.DuplicateElementName,
        candidate.EquationId,
        candidate.NodeId,
        includeSource ? candidate.PartUri : null,
        includeSource ? candidate.ParentElementOrdinal : null,
        includeSource ? candidate.RetainedElementOrdinal : null
    );

    private static EquationRepairInspectionIssue ProjectIssue(
        WordEquationIssue issue,
        IReadOnlyList<WordEquationRepairCandidate> candidates,
        bool includeSource
    ) => new(
        issue.Code,
        SnakeCase(issue.Severity),
        issue.EquationId,
        issue.NodeId,
        candidates.Any(candidate =>
            string.Equals(candidate.IssueCode, issue.Code, StringComparison.Ordinal)
            && string.Equals(candidate.PartUri, issue.PartUri, StringComparison.Ordinal)
            && issue.SourceElementOrdinal is { } ordinal
            && (ordinal == candidate.ParentElementOrdinal
                || candidate.RemovedElementOrdinals.Contains(ordinal))
        ),
        includeSource ? issue.PartUri : null,
        includeSource ? issue.SourceElementOrdinal : null
    );

    private OpcPackageSnapshot ReadExpected(
        string path,
        string expectedFingerprint,
        CancellationToken cancellationToken
    )
    {
        var package = _reader.Read(path, cancellationToken);
        if (!string.Equals(
                package.Fingerprint,
                expectedFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "Saved package changed after OfficeMath repair inspection"
            );
        }
        return package;
    }

    private static void ValidateWordPackage(
        string path,
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        if (!package.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The input package has structural OPC errors"
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
    }

    private static void ValidateCommon(string localPath, string expectedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(localPath)
            || localPath.Length > EquationRepairWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(localPath))
        {
            throw Invalid(
                "OfficeMath repair operations accept DOCX, DOCM, DOTX, or DOTM files"
            );
        }
        if (expectedFingerprint.Length != 64 || !expectedFingerprint.All(Uri.IsHexDigit))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
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

    private static void ValidatePublicBounds(WordEquationRepairCatalog catalog)
    {
        foreach (var candidate in catalog.Candidates)
        {
            RequireBound(candidate.Id, 128, "OfficeMath candidate ID");
            RequireBound(candidate.Fingerprint, 64, "OfficeMath candidate fingerprint");
            RequireBound(candidate.IssueCode, 128, "OfficeMath candidate issue code");
            RequireBound(candidate.PartUri, 2_048, "OfficeMath candidate part URI");
            RequireBound(candidate.ParentElementName, 128, "OfficeMath parent element name");
            RequireBound(candidate.DuplicateElementName, 128, "OfficeMath duplicate element name");
        }
        foreach (var issue in catalog.EquationGraph.Issues)
        {
            RequireBound(issue.Code, 128, "OfficeMath issue code");
            if (issue.PartUri is not null)
            {
                RequireBound(issue.PartUri, 2_048, "OfficeMath issue part URI");
            }
        }
    }

    private static void RequireBound(string value, int maximum, string description)
    {
        if (value.Length > maximum)
        {
            throw new WordEquationLimitException(
                $"The {description} exceeds {maximum} characters."
            );
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

    private static bool SchemaErrorsReduced(WordPackageCandidateValidationReport report) =>
        report.Performed
        && report.NoNewErrors
        && report.CandidateErrorCount < report.BaselineErrorCount;

    private static WordToolkitOperationException ValidationFailure(
        WordPackageCandidateValidationReport report,
        string message
    )
    {
        var issues = report.Issues.Take(20).ToArray();
        return new WordToolkitOperationException(
            "OOXML_SCHEMA_INVALID",
            message,
            details: new WordPackageValidationFailureDetails(
                report.ErrorCount,
                report.BaselineErrorCount,
                report.CandidateErrorCount,
                report.ErrorsTruncated || report.Issues.Count > issues.Length,
                issues
            )
        );
    }

    private static bool IsPlanId(string value) => value is not null
        && value.Length is >= 16 and <= 128
        && value.StartsWith("werplan_", StringComparison.Ordinal)
        && value[8..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
        );

    private static string RepairKindName(WordEquationRepairKind kind) => kind switch
    {
        WordEquationRepairKind.RemoveRedundantDuplicatePropertyContainer =>
            "remove_redundant_duplicate_property_container",
        WordEquationRepairKind.RemoveRedundantDuplicateProperty =>
            "remove_redundant_duplicate_property",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

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
        WordEquationLimitException or WordSemanticLimitException =>
            new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Word package OfficeMath projection exceeds a bounded safety limit",
                SafeReason(exception.Message, localPath),
                innerException: exception
            ),
        WordSemanticPreconditionException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            SafeReason(conflict.Message, localPath)
                ?? "OfficeMath repair precondition failed",
            innerException: conflict
        ),
        WordSemanticEditException edit => new WordToolkitOperationException(
            "UNSAFE_REPAIR",
            SafeReason(edit.Message, localPath) ?? "OfficeMath repair is unsafe",
            innerException: edit
        ),
        WordEquationProjectionException or WordSemanticProjectionException =>
            new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected safely for OfficeMath repair",
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
            "Candidate package does not match the reviewed OfficeMath repair plan",
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
            "The OfficeMath package could not be read or written",
            SafeReason(io.Message, localPath),
            retryable: true,
            innerException: io
        ),
        ArgumentException argument => Invalid(
            SafeReason(argument.Message, localPath) ?? "Invalid OfficeMath repair request",
            argument
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The OfficeMath package operation failed",
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
        OpcPackageSnapshot Candidate,
        WordEquationRepairPlan Plan,
        bool HasDigitalSignatures,
        WordPackageCandidateValidationReport Validation
    );
}
