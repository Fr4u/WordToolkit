using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Inspects the source-linked footnote/endnote graph and applies only reviewed,
/// fingerprint-bound definition removals that survive semantic and schema validation.
/// </summary>
public sealed class NoteWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer = new();
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public NoteWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public NoteInspectionResult Inspect(
        NoteInspectionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateCommon(request.LocalPath, request.ExpectedPackageFingerprint);
            if (request.MaxItems is < 1 or > NoteWordPackageContract.MaximumReturnedItems)
            {
                throw Invalid(
                    $"max_items must be between 1 and {NoteWordPackageContract.MaximumReturnedItems}"
                );
            }
            var path = ResolvePath(request.LocalPath);
            var package = ReadExpected(path, request.ExpectedPackageFingerprint, cancellationToken);
            ValidateWordPackage(path, package, cancellationToken);
            var graph = new WordNoteGraphBuilder().Build(
                package,
                cancellationToken: cancellationToken
            );
            ValidatePublicGraphBounds(graph);
            var selectable = request.IncludeAll
                ? graph.Definitions
                : graph.Definitions.Where(definition =>
                    definition.EmptyOrphanRemovalCandidate
                    || definition.RedundantDuplicateRemovalCandidate
                ).ToArray();
            var definitions = selectable.Take(request.MaxItems).ToArray();
            var issues = graph.Issues.Take(request.MaxItems).ToArray();
            return new NoteInspectionResult(
                NoteWordPackageContract.InspectContract,
                Path.GetFileName(path),
                package.Fingerprint,
                graph.AnalysisExecutionComplete,
                graph.DocumentCoverageComplete,
                graph.IssuesTruncated,
                graph.Definitions.Count,
                graph.References.Count,
                graph.SpecialReferences.Count,
                graph.NumberingPolicies.Count,
                graph.Issues.Count,
                graph.Issues.Count(issue => issue.Severity == WordNoteIssueSeverity.Error),
                graph.Issues.Count(issue => issue.Severity == WordNoteIssueSeverity.Warning),
                graph.Definitions.Count(definition => definition.EmptyOrphanRemovalCandidate),
                graph.Definitions.Count(definition => definition.RedundantDuplicateRemovalCandidate),
                definitions.Length,
                definitions.Length < selectable.Count,
                definitions.Select(ProjectDefinition).ToArray(),
                issues.Length,
                graph.IssuesTruncated || issues.Length < graph.Issues.Count,
                issues.Select(ProjectIssue).ToArray(),
                request.IncludeDetails
                    ? graph.References.Count > request.MaxItems
                    : null,
                request.IncludeDetails
                    ? graph.SpecialReferences.Count > request.MaxItems
                    : null,
                request.IncludeDetails
                    ? graph.NumberingPolicies.Count > request.MaxItems
                    : null,
                request.IncludeDetails
                    ? graph.References.Take(request.MaxItems).Select(reference =>
                        new NoteInspectionReference(
                            reference.Id,
                            SnakeCase(reference.Kind),
                            reference.OoxmlId,
                            reference.PartUri,
                            reference.CustomMarkFollows,
                            reference.CustomMarkValueValid,
                            reference.NestedInsideNoteStory,
                            reference.ResolutionStatus
                        )
                    ).ToArray()
                    : null,
                request.IncludeDetails
                    ? graph.SpecialReferences.Take(request.MaxItems).Select(reference =>
                        new NoteInspectionSpecialReference(
                            reference.Id,
                            SnakeCase(reference.Kind),
                            reference.OoxmlId,
                            reference.PartUri,
                            reference.ResolutionStatus
                        )
                    ).ToArray()
                    : null,
                request.IncludeDetails
                    ? graph.NumberingPolicies.Take(request.MaxItems).Select(policy =>
                        new NoteInspectionPolicy(
                            policy.Id,
                            SnakeCase(policy.Kind),
                            policy.Scope,
                            policy.SectionIndex,
                            policy.Position,
                            policy.NumberFormat,
                            policy.NumberStart,
                            policy.NumberRestart,
                            policy.ValuesValid
                        )
                    ).ToArray()
                    : null,
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

    public NoteRepairPlanResult Plan(
        NoteRepairPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return ProjectPlan(BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.RepairKind,
                request.DefinitionId,
                request.ExpectedDefinitionFingerprint,
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

    public NoteRepairApplyResult Apply(
        NoteRepairApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid note repair plan ID");
            }
            var context = BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.RepairKind,
                request.DefinitionId,
                request.ExpectedDefinitionFingerprint,
                cancellationToken
            );
            if (!string.Equals(context.Plan.PlanId, request.ExpectedPlanId, StringComparison.Ordinal))
            {
                throw new WordToolkitOperationException(
                    "PLAN_MISMATCH",
                    "The request does not reproduce the reviewed note repair plan ID"
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
                    "The reviewed note repair is blocked by Word editing protection",
                    details: new NoteRepairEditPolicyBlockDetails(
                        context.Plan.PlanId,
                        protectionBlocks
                    )
                );
            }
            if (context.HasDigitalSignatures)
            {
                throw new WordToolkitOperationException(
                    "SIGNED_PACKAGE",
                    "Note repair is blocked because the package contains digital signatures"
                );
            }
            if (!context.Validation.Performed)
            {
                throw new WordToolkitOperationException(
                    "VALIDATOR_REQUIRED",
                    "Applying note repair requires a candidate package schema validator"
                );
            }
            if (!context.Validation.NoNewErrors)
            {
                var issues = context.Validation.Issues.Take(20).ToArray();
                throw new WordToolkitOperationException(
                    "OOXML_SCHEMA_INVALID",
                    "The exact note repair candidate introduces Microsoft Open XML schema errors",
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
                throw new WordToolkitOperationException(
                    "NO_CHANGES",
                    "The reviewed note repair does not contain a package mutation"
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
            return new NoteRepairApplyResult(
                NoteWordPackageContract.ApplyContract,
                Path.GetFileName(context.Path),
                RepairKindName(context.Plan.RepairKind),
                context.Plan.PlanId,
                Applied: true,
                context.Plan.TargetDefinition.Id,
                context.Package.Fingerprint,
                result.Fingerprint,
                context.Plan.ResultPackageFingerprint,
                result.BackupPath,
                result.ChangedEntryNames,
                result.Diagnostics.Count,
                context.Validation.CandidateValid,
                context.Validation.NoNewErrors,
                context.Protection.AuthorizationRequired
                    ? ["protected_edit_authorization"]
                    : Array.Empty<string>(),
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
        string expectedPackageFingerprint,
        string repairKind,
        string definitionId,
        string expectedDefinitionFingerprint,
        CancellationToken cancellationToken
    )
    {
        ValidateCommon(localPath, expectedPackageFingerprint);
        var command = ParseCommand(repairKind, definitionId, expectedDefinitionFingerprint);
        var path = ResolvePath(localPath);
        var package = ReadExpected(path, expectedPackageFingerprint, cancellationToken);
        ValidateWordPackage(path, package, cancellationToken);
        var plan = new WordNoteRepairPlanner().Plan(package, command, cancellationToken);
        var candidate = MaterializeCandidate(package, plan, cancellationToken);
        var validation = ValidateExactCandidate(package, candidate, cancellationToken);
        var projector = new WordSemanticProjector();
        var semantic = projector.Project(package, cancellationToken);
        var candidateSemantic = projector.Project(candidate, cancellationToken);
        var protection = WordPackagePatchRiskAnalyzer.AssessProtection(
            package,
            semantic,
            candidate,
            candidateSemantic,
            plan.HasChanges,
            cancellationToken
        );
        return new PlanContext(
            path,
            package,
            candidate,
            plan,
            WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package),
            validation,
            protection
        );
    }

    private OpcPackageSnapshot MaterializeCandidate(
        OpcPackageSnapshot package,
        WordNoteRepairPlan plan,
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
                "The exact note repair candidate has structural OPC errors"
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
                "The exact note repair candidate does not match the planned result fingerprint"
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

    private static NoteRepairPlanResult ProjectPlan(PlanContext context, bool includeDetails)
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
        else if (!context.Validation.NoNewErrors)
        {
            blocked.Add("microsoft_schema_validation_failed");
        }
        blocked.AddRange(ProtectionBlockCodes(context, null));
        var requiredAuthorizations = context.Plan.HasChanges
            && context.Protection.AuthorizationRequired
            && !context.Protection.HasMalformedProtectionMetadata
                ? new[] { "protected_edit_authorization" }
                : Array.Empty<string>();
        return new NoteRepairPlanResult(
            NoteWordPackageContract.PlanContract,
            Path.GetFileName(context.Path),
            RepairKindName(context.Plan.RepairKind),
            context.Plan.PlanId,
            context.Plan.BasePackageFingerprint,
            context.Plan.ResultPackageFingerprint,
            context.Plan.TargetDefinition.Id,
            context.Plan.TargetDefinition.Fingerprint,
            SnakeCase(context.Plan.TargetDefinition.Kind),
            context.Plan.TargetDefinition.OoxmlId,
            context.Plan.TargetDefinition.PartUri,
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
            SafetyRules: context.Plan.SafetyRules,
            Protection: context.Protection,
            ProtectionAuthorizationId: requiredAuthorizations.Length == 0
                ? null
                : context.Plan.PlanId,
            RequiredAuthorizations: requiredAuthorizations,
            ChangedParts: includeDetails ? context.Plan.ChangedParts : null,
            RawXmlReturned: false,
            MutationPerformed: false,
            WordOpened: false
        );
    }

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
            && !string.Equals(authorization, context.Plan.PlanId, StringComparison.Ordinal))
        {
            return ["protected_document_edit_not_authorized"];
        }
        return Array.Empty<string>();
    }

    private static WordNoteRepairCommand ParseCommand(
        string repairKind,
        string definitionId,
        string expectedDefinitionFingerprint
    )
    {
        var kind = repairKind switch
        {
            "remove_empty_orphan_definition" => WordNoteRepairKind.RemoveEmptyOrphanDefinition,
            "remove_redundant_duplicate_definition" =>
                WordNoteRepairKind.RemoveRedundantDuplicateDefinition,
            _ => throw Invalid(
                "repair_kind must be remove_empty_orphan_definition or remove_redundant_duplicate_definition"
            ),
        };
        return new WordNoteRepairCommand(kind, definitionId, expectedDefinitionFingerprint);
    }

    private static NoteInspectionDefinition ProjectDefinition(WordNoteDefinition definition) => new(
        definition.Id,
        definition.Fingerprint,
        SnakeCase(definition.Kind),
        SnakeCase(definition.DefinitionType),
        definition.OoxmlId,
        definition.PartUri,
        definition.ReferenceCount,
        definition.SpecialReferenceCount,
        definition.ParagraphCount,
        definition.TextCharacterCount,
        definition.HasReferenceMark,
        definition.HasComplexContent,
        definition.IsOrphan,
        definition.EmptyOrphanRemovalCandidate,
        definition.RedundantDuplicateRemovalCandidate
    );

    private static NoteInspectionIssue ProjectIssue(WordNoteIssue issue) => new(
        issue.Id,
        issue.Code,
        SnakeCase(issue.Severity),
        issue.Kind is null ? null : SnakeCase(issue.Kind.Value),
        issue.SubjectId,
        issue.PartUri,
        issue.RepairCandidate
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
                "Saved package changed after note inspection"
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
            || localPath.Length > NoteWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(localPath))
        {
            throw Invalid("Note operations accept DOCX, DOCM, DOTX, or DOTM files");
        }
        ValidateSha256(expectedFingerprint, "expected_package_fingerprint");
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

    private static void ValidatePublicGraphBounds(WordNoteGraph graph)
    {
        foreach (var definition in graph.Definitions)
        {
            RequireBound(definition.Id, 128, "note definition ID");
            RequireBound(definition.Fingerprint, 64, "note definition fingerprint");
            RequireBound(definition.PartUri, 2_048, "note part URI");
        }
        foreach (var issue in graph.Issues)
        {
            RequireBound(issue.Id, 128, "note issue ID");
            RequireBound(issue.Code, 128, "note issue code");
            if (issue.PartUri is not null)
            {
                RequireBound(issue.PartUri, 2_048, "note issue part URI");
            }
        }
        foreach (var reference in graph.References)
        {
            RequireBound(reference.Id, 128, "note reference ID");
            RequireBound(reference.PartUri, 2_048, "note reference part URI");
            RequireBound(reference.ResolutionStatus, 64, "note reference status");
        }
        foreach (var reference in graph.SpecialReferences)
        {
            RequireBound(reference.Id, 128, "special-note reference ID");
            RequireBound(reference.PartUri, 2_048, "special-note reference part URI");
            RequireBound(reference.ResolutionStatus, 64, "special-note reference status");
        }
        foreach (var policy in graph.NumberingPolicies)
        {
            RequireBound(policy.Id, 128, "note policy ID");
            RequireBound(policy.PartUri, 2_048, "note policy part URI");
            RequireOptionalBound(policy.Position, 128, "note position value");
            RequireOptionalBound(policy.NumberFormat, 128, "note number-format value");
            RequireOptionalBound(policy.RawNumberStart, 128, "note number-start value");
            RequireOptionalBound(policy.NumberRestart, 128, "note restart value");
            foreach (var duplicate in policy.DuplicateProperties)
            {
                RequireBound(duplicate, 128, "duplicate note property name");
            }
        }
    }

    private static void RequireBound(string value, int maximum, string description)
    {
        if (value.Length > maximum)
        {
            throw new WordNoteLimitException(
                $"The {description} exceeds {maximum} characters."
            );
        }
    }

    private static void RequireOptionalBound(
        string? value,
        int maximum,
        string description
    )
    {
        if (value is not null)
        {
            RequireBound(value, maximum, description);
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

    private static string RepairKindName(WordNoteRepairKind kind) => kind switch
    {
        WordNoteRepairKind.RemoveEmptyOrphanDefinition =>
            "remove_empty_orphan_definition",
        WordNoteRepairKind.RemoveRedundantDuplicateDefinition =>
            "remove_redundant_duplicate_definition",
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
        WordNoteLimitException or WordSemanticLimitException =>
            new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Word package note projection exceeds a bounded safety limit",
                SafeReason(exception.Message, localPath),
                innerException: exception
            ),
        WordSemanticPreconditionException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            SafeReason(conflict.Message, localPath) ?? "Note repair precondition failed",
            innerException: conflict
        ),
        WordSemanticEditException edit => new WordToolkitOperationException(
            "UNSAFE_REPAIR",
            SafeReason(edit.Message, localPath) ?? "Note repair is unsafe",
            innerException: edit
        ),
        WordNoteProjectionException or WordSemanticProjectionException =>
            new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected safely for note operations",
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
            "Candidate package does not match the reviewed note repair plan",
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
            "The note package could not be read or written",
            SafeReason(io.Message, localPath),
            retryable: true,
            innerException: io
        ),
        ArgumentException argument => Invalid(
            SafeReason(argument.Message, localPath) ?? "Invalid note operation request",
            argument
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The note package operation failed",
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
        WordNoteRepairPlan Plan,
        bool HasDigitalSignatures,
        WordPackageCandidateValidationReport Validation,
        WordPackageProtectionRiskAssessment Protection
    );
}
