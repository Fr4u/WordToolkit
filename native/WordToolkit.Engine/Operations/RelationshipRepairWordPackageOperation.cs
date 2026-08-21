using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Inspects, plans, and atomically applies bounded relationship repairs. The public
/// projection never returns external target values or raw XML.
/// </summary>
public sealed class RelationshipRepairWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer = new();
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public RelationshipRepairWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public RelationshipInspectionResult Inspect(
        RelationshipInspectionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateCommon(request.LocalPath, request.ExpectedPackageFingerprint);
            if (request.MaxItems is < 1 or > RelationshipRepairWordPackageContract.MaximumReturnedItems)
            {
                throw Invalid(
                    $"max_items must be between 1 and {RelationshipRepairWordPackageContract.MaximumReturnedItems}"
                );
            }
            var path = ResolvePath(request.LocalPath);
            var package = ReadExpected(path, request.ExpectedPackageFingerprint, cancellationToken);
            ValidateWordPackage(path, package, cancellationToken);
            var graph = new WordRelationshipUsageGraphBuilder().Build(
                package,
                cancellationToken
            );
            ValidatePublicGraphBounds(graph);
            var selected = (request.IncludeAll
                    ? graph.Relationships
                    : graph.Relationships.Where(item => item.MarkupRemovalCandidate))
                .Take(request.MaxItems)
                .ToArray();
            var orphans = graph.OrphanRelationshipParts.Take(request.MaxItems).ToArray();
            var statusCounts = graph.Relationships
                .GroupBy(item => SnakeCase(item.Status), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            return new RelationshipInspectionResult(
                RelationshipRepairWordPackageContract.InspectContract,
                Path.GetFileName(path),
                package.Fingerprint,
                graph.Relationships.Count,
                graph.MarkupRemovalCandidateCount,
                graph.OrphanRelationshipParts.Count,
                statusCounts,
                selected.Length,
                selected.Length < (request.IncludeAll
                    ? graph.Relationships.Count
                    : graph.MarkupRemovalCandidateCount),
                selected.Select(item => new RelationshipInspectionItem(
                    item.Id,
                    item.Fingerprint,
                    item.SourcePartUri,
                    item.RelationshipPartUri,
                    item.RelationshipId,
                    RelationshipTypeName(item.RelationshipType),
                    SnakeCase(item.TargetMode),
                    item.ResolvedTargetPartUri,
                    item.TargetFragment is not null,
                    SnakeCase(item.Status),
                    item.MarkupReferenceCount,
                    item.MarkupReferencesTruncated,
                    item.MarkupRemovalCandidate,
                    request.IncludeDetails ? item.MarkupReferences : null
                )).ToArray(),
                orphans.Length,
                orphans.Length < graph.OrphanRelationshipParts.Count,
                orphans.Select(item => new RelationshipInspectionOrphanPart(
                    item.Id,
                    item.RelationshipPartUri,
                    item.SourcePartUri,
                    item.EntrySha256,
                    item.ParsedRelationshipCount
                )).ToArray(),
                ExternalTargetsReturned: false,
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

    public RelationshipRepairPlanResult Plan(
        RelationshipRepairPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var context = BuildContext(
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

    public RelationshipRepairApplyResult Apply(
        RelationshipRepairApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid relationship repair plan ID");
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
                    "The request does not reproduce the reviewed relationship repair plan ID"
                );
            }
            if (context.HasDigitalSignatures)
            {
                throw new WordToolkitOperationException(
                    "SIGNED_PACKAGE",
                    "Relationship repair is blocked because the package contains digital signatures"
                );
            }
            if (context.RequiresExternalAuthorization
                && !request.AllowExternalRelationshipRemoval)
            {
                throw new WordToolkitOperationException(
                    "EXTERNAL_RELATIONSHIP_AUTHORIZATION_REQUIRED",
                    "The reviewed plan removes an external relationship and requires explicit authorization"
                );
            }
            if (!context.Validation.Performed)
            {
                throw new WordToolkitOperationException(
                    "VALIDATOR_REQUIRED",
                    "Applying relationship repair requires a candidate package schema validator"
                );
            }
            if (!context.Validation.NoNewErrors)
            {
                var issues = context.Validation.Issues.Take(20).ToArray();
                throw new WordToolkitOperationException(
                    "OOXML_SCHEMA_INVALID",
                    "The exact relationship repair candidate introduces Microsoft Open XML schema errors",
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

            cancellationToken.ThrowIfCancellationRequested();
            var result = _writer.Write(
                context.Path,
                context.Plan.CreateMutation(context.Package),
                new OpcAtomicWriteOptions
                {
                    ExpectedDestinationFingerprint = context.Package.Fingerprint,
                    ExpectedResultFingerprint = context.Plan.ResultPackageFingerprint,
                    AllowStructuralErrors = !context.Candidate.IsStructurallyValid,
                    KeepBackup = request.KeepBackup,
                }
            );
            return new RelationshipRepairApplyResult(
                RelationshipRepairWordPackageContract.ApplyContract,
                Path.GetFileName(context.Path),
                "remove_proven_dead_relationships",
                context.Plan.PlanId,
                Applied: true,
                CommandCount: context.Plan.Actions.Count,
                RemovedRelationshipCount: context.Plan.Actions.Sum(item => item.RemovedRelationshipCount),
                PreviousPackageFingerprint: context.Package.Fingerprint,
                PackageFingerprint: result.Fingerprint,
                PredictedPackageFingerprint: context.Plan.ResultPackageFingerprint,
                BackupPath: result.BackupPath,
                ChangedEntryNames: result.ChangedEntryNames,
                DiagnosticCount: result.Diagnostics.Count,
                MicrosoftSchemaValid: context.Validation.CandidateValid,
                MicrosoftSchemaNoNewErrors: context.Validation.NoNewErrors,
                ExternalTargetsReturned: false,
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
            throw MapFailure(exception, request?.LocalPath);
        }
    }

    private PlanContext BuildContext(
        string localPath,
        string expectedFingerprint,
        IReadOnlyList<RelationshipRepairCommandRequest> commandRequests,
        CancellationToken cancellationToken
    )
    {
        ValidateCommon(localPath, expectedFingerprint);
        ArgumentNullException.ThrowIfNull(commandRequests);
        if (commandRequests.Count is 0 or > RelationshipRepairWordPackageContract.MaximumCommands)
        {
            throw Invalid(
                $"commands must contain between 1 and {RelationshipRepairWordPackageContract.MaximumCommands} items"
            );
        }
        var commands = commandRequests.Select(ParseCommand).ToArray();
        var path = ResolvePath(localPath);
        var package = ReadExpected(path, expectedFingerprint, cancellationToken);
        ValidateWordPackage(path, package, cancellationToken);
        var plan = new WordRelationshipRepairPlanner(
            new WordRelationshipRepairOptions
            {
                MaxCommands = RelationshipRepairWordPackageContract.MaximumCommands,
                MaxChangedEntries = RelationshipRepairWordPackageContract.MaximumChangedEntries,
            }
        ).Plan(package, commands, cancellationToken);
        var candidate = MaterializeCandidate(package, plan, cancellationToken);
        var validation = ValidateExactCandidate(package, candidate, plan, cancellationToken);
        var requiresExternal = RequiresExternalAuthorization(package, plan.Actions);
        return new PlanContext(
            path,
            package,
            candidate,
            plan,
            WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package),
            requiresExternal,
            validation
        );
    }

    private OpcPackageSnapshot MaterializeCandidate(
        OpcPackageSnapshot package,
        WordRelationshipRepairPlan plan,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var candidate = new MemoryStream();
        _serializer.Write(candidate, plan.CreateMutation(package));
        candidate.Position = 0;
        var snapshot = _reader.Read(candidate, cancellationToken);
        if (!string.Equals(
                snapshot.Fingerprint,
                plan.ResultPackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "The exact candidate package does not match the relationship repair plan"
            );
        }
        return snapshot;
    }

    private WordPackageCandidateValidationReport ValidateExactCandidate(
        OpcPackageSnapshot package,
        OpcPackageSnapshot candidateSnapshot,
        WordRelationshipRepairPlan plan,
        CancellationToken cancellationToken
    )
    {
        if (!plan.Validation.Passed)
        {
            throw new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "The relationship repair engine rejected its exact candidate"
            );
        }
        if (_candidateValidator is null)
        {
            return WordPackageCandidateValidationReport.NotPerformed(
                "schema_validator_unavailable"
            );
        }
        using var baseline = new MemoryStream();
        _serializer.Write(baseline, new OpcPackageMutationBuilder(package));
        using var candidate = new MemoryStream();
        _serializer.Write(candidate, new OpcPackageMutationBuilder(candidateSnapshot));
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

    private static RelationshipRepairPlanResult ProjectPlan(
        PlanContext context,
        bool includeDetails
    )
    {
        var blocked = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blocked.Add("digital_signature_present");
        }
        if (context.RequiresExternalAuthorization)
        {
            blocked.Add("external_relationship_removal_requires_apply_authorization");
        }
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
        return new RelationshipRepairPlanResult(
            RelationshipRepairWordPackageContract.PlanContract,
            Path.GetFileName(context.Path),
            "remove_proven_dead_relationships",
            context.Plan.PlanId,
            context.Plan.BasePackageFingerprint,
            context.Plan.ResultPackageFingerprint,
            context.Plan.Actions.Count,
            context.Plan.Actions.Sum(item => item.RemovedRelationshipCount),
            context.Plan.ChangedEntries.Count,
            context.Plan.ChangedEntries.Sum(item => (long)item.AfterBytes - item.BeforeBytes),
            context.RequiresExternalAuthorization,
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
            SafetyRules: context.Plan.SafetyRules,
            Actions: includeDetails ? context.Plan.Actions : null,
            ChangedEntries: includeDetails ? context.Plan.ChangedEntries : null,
            ExternalTargetsReturned: false,
            RawXmlReturned: false,
            MutationPerformed: false,
            WordOpened: false
        );
    }

    private static WordRelationshipRepairCommand ParseCommand(
        RelationshipRepairCommandRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Kind switch
        {
            "remove_unreferenced_relationship" => ParseRelationshipCommand(request),
            "remove_orphan_relationship_part" => ParseOrphanCommand(request),
            _ => throw Invalid(
                "command kind must be remove_unreferenced_relationship or remove_orphan_relationship_part"
            ),
        };
    }

    private static RemoveUnreferencedRelationshipCommand ParseRelationshipCommand(
        RelationshipRepairCommandRequest request
    )
    {
        if (request.RelationshipPartUri is not null || request.ExpectedEntrySha256 is not null)
        {
            throw Invalid(
                "remove_unreferenced_relationship cannot contain orphan-part fields"
            );
        }
        ValidatePartUri(request.SourcePartUri, "source_part_uri");
        if (string.IsNullOrWhiteSpace(request.RelationshipId)
            || request.RelationshipId.Length > 255)
        {
            throw Invalid("relationship_id must be a non-empty bounded string");
        }
        ValidateSha256(
            request.ExpectedRelationshipFingerprint,
            "expected_relationship_fingerprint"
        );
        return new RemoveUnreferencedRelationshipCommand(
            request.SourcePartUri!,
            request.RelationshipId,
            request.ExpectedRelationshipFingerprint!
        );
    }

    private static RemoveOrphanRelationshipPartCommand ParseOrphanCommand(
        RelationshipRepairCommandRequest request
    )
    {
        if (request.SourcePartUri is not null
            || request.RelationshipId is not null
            || request.ExpectedRelationshipFingerprint is not null)
        {
            throw Invalid(
                "remove_orphan_relationship_part cannot contain relationship fields"
            );
        }
        ValidatePartUri(request.RelationshipPartUri, "relationship_part_uri");
        ValidateSha256(request.ExpectedEntrySha256, "expected_entry_sha256");
        return new RemoveOrphanRelationshipPartCommand(
            request.RelationshipPartUri!,
            request.ExpectedEntrySha256!
        );
    }

    private static bool RequiresExternalAuthorization(
        OpcPackageSnapshot package,
        IReadOnlyList<WordRelationshipRepairAction> actions
    ) => actions.Any(action => package.Relationships.Any(relationship =>
        string.Equals(
            relationship.RelationshipPartUri,
            action.RelationshipPartUri,
            StringComparison.Ordinal
        )
        && (action.RelationshipId is null
            || string.Equals(relationship.Id, action.RelationshipId, StringComparison.Ordinal))
        && relationship.TargetMode == OpcRelationshipTargetMode.External
    ));

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
                "Saved package changed after relationship inspection"
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
            || localPath.Length > RelationshipRepairWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(localPath))
        {
            throw Invalid("Relationship operations accept DOCX, DOCM, DOTX, or DOTM files");
        }
        ValidateSha256(expectedFingerprint, "expected_package_fingerprint");
    }

    private static void ValidatePartUri(string? value, string property)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 2_048
            || !value.StartsWith("/", StringComparison.Ordinal)
            || value.Contains("\\", StringComparison.Ordinal)
            || value.Contains('\0'))
        {
            throw Invalid($"{property} must be a bounded absolute OPC part URI");
        }
    }

    private static void ValidateSha256(string? value, string property)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw Invalid($"{property} must be exactly 64 hexadecimal characters");
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

    private static string RelationshipTypeName(string type)
    {
        var end = type.Length;
        while (end > 0 && type[end - 1] == '/')
        {
            end--;
        }
        var slash = type.LastIndexOf('/', end - 1);
        return type[(slash + 1)..end];
    }

    private static void ValidatePublicGraphBounds(WordRelationshipUsageGraph graph)
    {
        foreach (var item in graph.Relationships)
        {
            RequireBound(item.SourcePartUri, 2_048, "relationship source part URI");
            RequireBound(item.RelationshipPartUri, 2_048, "relationship part URI");
            RequireBound(item.RelationshipId, 255, "relationship ID");
            RequireBound(RelationshipTypeName(item.RelationshipType), 512, "relationship type name");
            if (item.ResolvedTargetPartUri is not null)
            {
                RequireBound(item.ResolvedTargetPartUri, 2_048, "resolved target part URI");
            }
            foreach (var reference in item.MarkupReferences)
            {
                RequireBound(reference.AttributeName, 512, "relationship reference attribute name");
            }
        }
        foreach (var orphan in graph.OrphanRelationshipParts)
        {
            RequireBound(orphan.RelationshipPartUri, 2_048, "orphan relationship part URI");
            RequireBound(orphan.SourcePartUri, 2_048, "orphan relationship source URI");
        }
    }

    private static void RequireBound(string value, int maximum, string description)
    {
        if (value.Length > maximum)
        {
            throw new WordRelationshipUsageLimitException(
                $"The {description} exceeds {maximum} characters."
            );
        }
    }

    private static bool IsPlanId(string value) => value is not null
        && value.Length is >= 16 and <= 128
        && value.StartsWith("wrrplan_", StringComparison.Ordinal)
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
            SafeReason(limit.Message, localPath) ?? "Relationship repair limit exceeded",
            innerException: limit
        ),
        WordRelationshipUsageLimitException or WordSemanticLimitException =>
            new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Word package relationship projection exceeds a bounded safety limit",
                SafeReason(exception.Message, localPath),
                innerException: exception
            ),
        WordSemanticPreconditionException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            SafeReason(conflict.Message, localPath) ?? "Relationship repair precondition failed",
            innerException: conflict
        ),
        WordSemanticEditException edit => new WordToolkitOperationException(
            "UNSAFE_REPAIR",
            SafeReason(edit.Message, localPath) ?? "Relationship repair is unsafe",
            innerException: edit
        ),
        WordSemanticProjectionException projection => new WordToolkitOperationException(
            "INVALID_WORD_PACKAGE",
            "The package cannot be projected safely for relationship repair",
            SafeReason(projection.Message, localPath),
            innerException: projection
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
            "Candidate package does not match the reviewed relationship repair plan",
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
            "The relationship repair package could not be read or written",
            SafeReason(io.Message, localPath),
            retryable: true,
            innerException: io
        ),
        ArgumentException argument => Invalid(
            SafeReason(argument.Message, localPath) ?? "Invalid relationship repair request",
            argument
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The relationship repair operation failed",
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
        WordRelationshipRepairPlan Plan,
        bool HasDigitalSignatures,
        bool RequiresExternalAuthorization,
        WordPackageCandidateValidationReport Validation
    );
}
