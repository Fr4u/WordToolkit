using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Aligns fingerprint-bound style-ID dependency closures from one Word package into
/// another without attaching or mutating the template.
/// </summary>
public sealed class TemplateStyleAlignmentWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer = new();
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public TemplateStyleAlignmentWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public TemplateStyleAlignmentInspectionResult Inspect(
        TemplateStyleAlignmentInspectRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.MaxItems is < 1
                or > TemplateStyleAlignmentWordPackageContract.MaximumReturnedItems)
            {
                throw Invalid($"max_items must be between 1 and {TemplateStyleAlignmentWordPackageContract.MaximumReturnedItems}");
            }
            var input = ReadInputs(
                request.TargetPath,
                request.TemplatePath,
                request.ExpectedTargetPackageFingerprint,
                request.ExpectedTemplatePackageFingerprint,
                cancellationToken
            );
            var catalog = new WordTemplateStyleAlignmentPlanner().Inspect(
                input.Target,
                input.Template,
                cancellationToken
            );
            ValidatePublicBounds(catalog);
            var candidatePage = catalog.Candidates.Take(request.MaxItems).ToArray();
            var issuePage = request.IncludeIssues
                ? catalog.Issues.Take(request.MaxItems).ToArray()
                : Array.Empty<WordTemplateStyleAlignmentIssue>();
            return new TemplateStyleAlignmentInspectionResult(
                TemplateStyleAlignmentWordPackageContract.InspectContract,
                Path.GetFileName(input.TargetPath),
                Path.GetFileName(input.TemplatePath),
                input.Target.Fingerprint,
                input.Template.Fingerprint,
                catalog.AnalysisExecutionComplete,
                catalog.AlignmentCoverageComplete,
                catalog.CanPlan,
                catalog.StylesWithEffectsSymmetric,
                catalog.Candidates.Count,
                catalog.Candidates.Count(candidate =>
                    candidate.Action == WordTemplateStyleAlignmentAction.AddStyle
                ),
                catalog.Candidates.Count(candidate =>
                    candidate.Action == WordTemplateStyleAlignmentAction.ReplaceStyle
                ),
                catalog.Candidates.Count(candidate =>
                    candidate.Action == WordTemplateStyleAlignmentAction.AlignDependencyClosure
                ),
                catalog.AlreadyAlignedStyleCount,
                catalog.Issues.Count,
                catalog.Issues.Count(issue => issue.Severity == WordStyleIssueSeverity.Error),
                catalog.Issues.Count(issue => issue.Severity == WordStyleIssueSeverity.Warning),
                candidatePage.Length,
                candidatePage.Length < catalog.Candidates.Count,
                candidatePage.Select(candidate => ProjectCandidate(
                    candidate,
                    request.IncludeDependencies
                )).ToArray(),
                issuePage.Length,
                request.IncludeIssues && issuePage.Length < catalog.Issues.Count,
                request.IncludeIssues
                    ? issuePage.Select(ProjectIssue).ToArray()
                    : null,
                LocalizedNameMatchingUsed: false,
                TemplateAttached: false,
                TemplateMutationPerformed: false,
                DocumentTextReturned: false,
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
            throw MapFailure(exception, request?.TargetPath, request?.TemplatePath);
        }
    }

    public TemplateStyleAlignmentPlanResult Plan(
        TemplateStyleAlignmentPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return ProjectPlan(BuildContext(
                request.TargetPath,
                request.TemplatePath,
                request.ExpectedTargetPackageFingerprint,
                request.ExpectedTemplatePackageFingerprint,
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
            throw MapFailure(exception, request?.TargetPath, request?.TemplatePath);
        }
    }

    public TemplateStyleAlignmentApplyResult Apply(
        TemplateStyleAlignmentApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid template style-alignment plan ID");
            }
            var context = BuildContext(
                request.TargetPath,
                request.TemplatePath,
                request.ExpectedTargetPackageFingerprint,
                request.ExpectedTemplatePackageFingerprint,
                request.Commands,
                cancellationToken
            );
            if (!string.Equals(context.Plan.PlanId, request.ExpectedPlanId, StringComparison.Ordinal))
            {
                throw new WordToolkitOperationException(
                    "PLAN_MISMATCH",
                    "The request does not reproduce the reviewed template style-alignment plan ID"
                );
            }
            var protectionBlocks = ProtectionBlockCodes(context, request.ProtectedEditAuthorization);
            if (protectionBlocks.Count != 0)
            {
                throw new WordToolkitOperationException(
                    "EDIT_POLICY_BLOCKED",
                    "Template style alignment is blocked by document protection or permission metadata",
                    details: new TemplateStyleAlignmentEditPolicyBlockDetails(context.Plan.PlanId, protectionBlocks)
                );
            }
            if (context.TargetHasDigitalSignatures)
            {
                throw new WordToolkitOperationException(
                    "SIGNED_PACKAGE",
                    "Template style alignment is blocked because the target package contains digital signatures"
                );
            }
            if (!context.Validation.Performed)
            {
                throw new WordToolkitOperationException(
                    "VALIDATOR_REQUIRED",
                    "Applying template style alignment requires a candidate package schema validator"
                );
            }
            if (!context.Validation.NoNewErrors)
            {
                throw ValidationFailure(
                    context.Validation,
                    "The exact template style-alignment candidate introduces Microsoft Open XML schema errors"
                );
            }
            if (!context.Plan.HasChanges)
            {
                return new TemplateStyleAlignmentApplyResult(
                    TemplateStyleAlignmentWordPackageContract.ApplyContract,
                    Path.GetFileName(context.TargetPath), Path.GetFileName(context.TemplatePath),
                    context.Plan.PlanId, Applied: false, NoOp: true,
                    context.Plan.Candidates.Count,
                    context.Plan.AlignedStyleIds.Count,
                    context.Plan.Validation.AddedStyleCount,
                    context.Plan.Validation.ReplacedStyleCount,
                    context.Target.Fingerprint, context.Template.Fingerprint,
                    context.Target.Fingerprint, context.Target.Fingerprint, null,
                    Array.Empty<string>(), 0, context.Validation.CandidateValid,
                    context.Validation.NoNewErrors, false, false, false, false, false, false, false,
                    Array.Empty<string>()
                );
            }
            var currentTemplate = _reader.Read(context.TemplatePath, cancellationToken);
            if (!string.Equals(
                    currentTemplate.Fingerprint,
                    context.Template.Fingerprint,
                    StringComparison.Ordinal
                ))
            {
                throw new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The template package changed immediately before target publication",
                    retryable: true
                );
            }
            var result = _writer.Write(
                context.TargetPath,
                context.Plan.CreateMutation(context.Target),
                new OpcAtomicWriteOptions
                {
                    ExpectedDestinationFingerprint = context.Target.Fingerprint,
                    ExpectedResultFingerprint = context.Plan.ResultPackageFingerprint,
                    KeepBackup = request.KeepBackup,
                }
            );
            return new TemplateStyleAlignmentApplyResult(
                TemplateStyleAlignmentWordPackageContract.ApplyContract,
                Path.GetFileName(context.TargetPath),
                Path.GetFileName(context.TemplatePath),
                context.Plan.PlanId,
                Applied: true,
                NoOp: false,
                context.Plan.Candidates.Count,
                context.Plan.AlignedStyleIds.Count,
                context.Plan.Validation.AddedStyleCount,
                context.Plan.Validation.ReplacedStyleCount,
                context.Target.Fingerprint,
                context.Template.Fingerprint,
                result.Fingerprint,
                context.Plan.ResultPackageFingerprint,
                result.BackupPath,
                result.ChangedEntryNames,
                result.Diagnostics.Count,
                context.Validation.CandidateValid,
                context.Validation.NoNewErrors,
                LocalizedNameMatchingUsed: false,
                TemplateAttached: false,
                TemplateMutationPerformed: false,
                DocumentTextReturned: false,
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
            throw MapFailure(exception, request?.TargetPath, request?.TemplatePath);
        }
    }

    private PlanContext BuildContext(
        string targetPath,
        string templatePath,
        string targetFingerprint,
        string templateFingerprint,
        IReadOnlyList<TemplateStyleAlignmentCommandRequest> commands,
        CancellationToken cancellationToken
    )
    {
        var input = ReadInputs(
            targetPath,
            templatePath,
            targetFingerprint,
            templateFingerprint,
            cancellationToken
        );
        var parsedCommands = ParseCommands(commands);
        var plan = new WordTemplateStyleAlignmentPlanner().Plan(
            input.Target,
            input.Template,
            parsedCommands,
            cancellationToken
        );
        var candidate = MaterializeCandidate(input.Target, plan, cancellationToken);
        var validation = ValidateExactCandidate(input.Target, candidate, cancellationToken);
        var projector = new WordSemanticProjector();
        var targetSemantic = projector.Project(input.Target, cancellationToken);
        var candidateSemantic = projector.Project(candidate, cancellationToken);
        return new PlanContext(
            input.TargetPath,
            input.TemplatePath,
            input.Target,
            input.Template,
            candidate,
            plan,
            WordPackagePatchRiskAnalyzer.HasDigitalSignatures(input.Target),
            validation,
            WordPackagePatchRiskAnalyzer.AssessProtection(
                input.Target, targetSemantic, candidate, candidateSemantic,
                plan.HasChanges, cancellationToken
            )
        );
    }

    private InputContext ReadInputs(
        string targetPath,
        string templatePath,
        string targetFingerprint,
        string templateFingerprint,
        CancellationToken cancellationToken
    )
    {
        ValidateCommon(targetPath, "target_path", targetFingerprint,
            "expected_target_package_fingerprint");
        ValidateCommon(templatePath, "template_path", templateFingerprint,
            "expected_template_package_fingerprint");
        var target = ResolvePath(targetPath, "target");
        var template = ResolvePath(templatePath, "template");
        if (string.Equals(target, template, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("target_path and template_path must identify different files");
        }
        var targetPackage = ReadExpected(
            target,
            targetFingerprint,
            "target",
            cancellationToken
        );
        var templatePackage = ReadExpected(
            template,
            templateFingerprint,
            "template",
            cancellationToken
        );
        ValidateWordPackage(target, targetPackage, "target", cancellationToken);
        ValidateWordPackage(template, templatePackage, "template", cancellationToken);
        return new InputContext(target, template, targetPackage, templatePackage);
    }

    private OpcPackageSnapshot MaterializeCandidate(
        OpcPackageSnapshot target,
        WordTemplateStyleAlignmentPlan plan,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream();
        _serializer.Write(stream, plan.CreateMutation(target));
        stream.Position = 0;
        var candidate = _reader.Read(stream, cancellationToken);
        if (!candidate.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "The exact template style-alignment candidate has structural OPC errors"
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
                "The exact template style-alignment candidate does not match the planned result fingerprint"
            );
        }
        return candidate;
    }

    private WordPackageCandidateValidationReport ValidateExactCandidate(
        OpcPackageSnapshot target,
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
        using var baseline = new MemoryStream();
        _serializer.Write(baseline, new OpcPackageMutationBuilder(target));
        using var changed = new MemoryStream();
        _serializer.Write(changed, new OpcPackageMutationBuilder(candidate));
        baseline.Position = 0;
        changed.Position = 0;
        try
        {
            return BoundValidation(_candidateValidator.Validate(
                baseline,
                changed,
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
                "Template style-alignment candidate schema validation failed",
                innerException: exception
            );
        }
    }

    private static TemplateStyleAlignmentPlanResult ProjectPlan(
        PlanContext context,
        bool includeDetails
    )
    {
        var blocked = new List<string>();
        if (context.TargetHasDigitalSignatures)
        {
            blocked.Add("target_digital_signature_present");
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
        if (context.Plan.HasChanges && context.Protection.HasMalformedProtectionMetadata)
            blocked.Add("protection_metadata_malformed");
        else if (context.Plan.HasChanges && context.Protection.AuthorizationRequired)
            blocked.Add("protected_document_edit_not_authorized");
        var requiredAuthorizations = context.Plan.HasChanges
            && context.Protection.AuthorizationRequired
            && !context.Protection.HasMalformedProtectionMetadata
            ? (IReadOnlyList<string>)["protected_edit_authorization"] : Array.Empty<string>();
        return new TemplateStyleAlignmentPlanResult(
            TemplateStyleAlignmentWordPackageContract.PlanContract,
            Path.GetFileName(context.TargetPath),
            Path.GetFileName(context.TemplatePath),
            context.Plan.PlanId,
            context.Target.Fingerprint,
            context.Template.Fingerprint,
            context.Plan.ResultPackageFingerprint,
            context.Plan.Candidates.Count,
            context.Plan.Candidates.Count,
            context.Plan.AlignedStyleIds.Count,
            context.Plan.Validation.AddedStyleCount,
            context.Plan.Validation.ReplacedStyleCount,
            context.Plan.ChangedParts.Count,
            context.Plan.ChangedParts.Sum(part => (long)part.AfterBytes - part.BeforeBytes),
            context.Plan.HasChanges,
            CanApply: blocked.Count == 0,
            ApplyBlocked: blocked.Count != 0,
            blocked,
            context.Plan.Validation,
            ProjectValidation(context.Validation, includeDetails),
            context.Plan.SafetyRules,
            includeDetails
                ? context.Plan.Candidates.Select(candidate => ProjectCandidate(
                    candidate,
                    includeDependencies: true
                )).ToArray()
                : null,
            includeDetails ? context.Plan.AlignedStyleIds : null,
            includeDetails ? context.Plan.ChangedParts : null,
            LocalizedNameMatchingUsed: false,
            TemplateAttached: false,
            TemplateMutationPerformed: false,
            DocumentTextReturned: false,
            RawXmlReturned: false,
            MutationPerformed: false,
            WordOpened: false,
            Protection: context.Protection,
            ProtectionAuthorizationId: requiredAuthorizations.Count == 0 ? null : context.Plan.PlanId,
            RequiredAuthorizations: requiredAuthorizations
        );
    }

    private static IReadOnlyList<WordTemplateStyleAlignmentCommand> ParseCommands(
        IReadOnlyList<TemplateStyleAlignmentCommandRequest> commands
    )
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count is < 1
            or > TemplateStyleAlignmentWordPackageContract.MaximumCommands)
        {
            throw Invalid($"commands must contain between 1 and {TemplateStyleAlignmentWordPackageContract.MaximumCommands} items");
        }
        return commands.Select(command =>
        {
            ArgumentNullException.ThrowIfNull(command);
            return new WordTemplateStyleAlignmentCommand(
                command.CandidateId,
                command.ExpectedCandidateFingerprint
            );
        }).ToArray();
    }

    private static TemplateStyleAlignmentInspectionCandidate ProjectCandidate(
        WordTemplateStyleAlignmentCandidate candidate,
        bool includeDependencies
    ) => new(
        candidate.Id,
        candidate.Fingerprint,
        candidate.StyleId,
        SnakeCase(candidate.StyleType),
        SnakeCase(candidate.Action),
        candidate.DependencyStyleIds.Count,
        includeDependencies ? candidate.DependencyStyleIds : null,
        candidate.AddedStyleCount,
        candidate.ReplacedStyleCount,
        candidate.AlreadyAlignedStyleCount,
        candidate.ThemeContextVerified,
        candidate.NumberingDependenciesVerified,
        candidate.StylesWithEffectsMirrored
    );

    private static TemplateStyleAlignmentInspectionIssue ProjectIssue(
        WordTemplateStyleAlignmentIssue issue
    ) => new(issue.Code, SnakeCase(issue.Severity), issue.StyleId);

    private OpcPackageSnapshot ReadExpected(
        string path,
        string expectedFingerprint,
        string role,
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
                $"The {role} package changed after template style-alignment inspection",
                retryable: true
            );
        }
        return package;
    }

    private static void ValidateWordPackage(
        string path,
        OpcPackageSnapshot package,
        string role,
        CancellationToken cancellationToken
    )
    {
        if (!package.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "INVALID_PACKAGE",
                $"The {role} package has structural OPC errors"
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
                $"The {role} filename extension does not match its Word main-part content type"
            );
        }
    }

    private static void ValidateCommon(
        string path,
        string pathField,
        string fingerprint,
        string fingerprintField
    )
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > TemplateStyleAlignmentWordPackageContract.MaximumPathCharacters)
        {
            throw Invalid($"{pathField} must be a non-empty bounded path");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(path))
        {
            throw Invalid($"{pathField} must identify DOCX, DOCM, DOTX, or DOTM");
        }
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))
        {
            throw Invalid($"{fingerprintField} must contain exactly 64 hexadecimal characters");
        }
    }

    private static string ResolvePath(string path, string role)
    {
        try
        {
            var resolved = Path.GetFullPath(path);
            if (!File.Exists(resolved))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    $"The requested {role} Word package does not exist"
                );
            }
            return resolved;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw Invalid($"{role} path is not a valid filesystem path", exception);
        }
    }

    private static bool IsPlanId(string value) => value is not null
        && value.Length is >= 16 and <= 128
        && value.StartsWith("wtsaplan_", StringComparison.Ordinal)
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

    private static void ValidatePublicBounds(WordTemplateStyleAlignmentCatalog catalog)
    {
        foreach (var candidate in catalog.Candidates)
        {
            RequireBound(candidate.Id, 128, "candidate ID");
            RequireBound(candidate.Fingerprint, 64, "candidate fingerprint");
            RequireBound(candidate.StyleId, 253, "style ID");
            foreach (var dependency in candidate.DependencyStyleIds)
            {
                RequireBound(dependency, 253, "dependency style ID");
            }
        }
        foreach (var issue in catalog.Issues)
        {
            RequireBound(issue.Code, 128, "issue code");
            if (issue.StyleId is not null)
            {
                RequireBound(issue.StyleId, 253, "issue style ID");
            }
        }
    }

    private static void RequireBound(string value, int maximum, string description)
    {
        if (value.Length > maximum)
        {
            throw new WordSemanticTransactionLimitException(
                $"Template style-alignment {description} exceeds {maximum} characters."
            );
        }
    }

    private static WordPackageCandidateValidationReport ProjectValidation(
        WordPackageCandidateValidationReport report,
        bool includeDetails
    ) => includeDetails
        ? report
        : report with
        {
            ErrorsTruncated = report.ErrorsTruncated || report.Issues.Count != 0,
            Issues = Array.Empty<WordPackageValidationIssue>(),
        };

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
                report.CandidateValid || report.NoNewErrors || report.ErrorCount != 0
                || report.BaselineErrorCount != 0 || report.CandidateErrorCount != 0
                || report.ErrorsTruncated || report.Issues.Count != 0
                || string.IsNullOrWhiteSpace(report.NotPerformedReason)
            )
            || report.Performed && !report.ErrorsTruncated
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

    private static string? Bound(string? value, int maximum) =>
        value is null || value.Length <= maximum ? value : value[..maximum] + "…";

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

    private static string? SafeReason(string? message, params string?[] paths)
    {
        if (message is null)
        {
            return null;
        }
        var safe = paths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Aggregate(message, (current, path) => current.Replace(
                path!,
                "<redacted>",
                StringComparison.OrdinalIgnoreCase
            ));
        return Bound(safe, 512);
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? targetPath,
        string? templatePath
    ) => exception switch
    {
        WordSemanticTransactionLimitException or WordSemanticLimitException =>
            new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Template style alignment exceeds a bounded safety limit",
                SafeReason(exception.Message, targetPath, templatePath),
                innerException: exception
            ),
        WordSemanticPreconditionException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            SafeReason(conflict.Message, targetPath, templatePath)
                ?? "Template style-alignment precondition failed",
            retryable: true,
            innerException: conflict
        ),
        WordSemanticEditException edit => new WordToolkitOperationException(
            "UNSAFE_ALIGNMENT",
            SafeReason(edit.Message, targetPath, templatePath)
                ?? "Template style alignment is unsafe",
            innerException: edit
        ),
        WordStyleProjectionException or WordNumberingProjectionException
            or WordSemanticProjectionException => new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "Target or template cannot be projected safely for style alignment",
                SafeReason(exception.Message, targetPath, templatePath),
                innerException: exception
            ),
        OpcPackageConcurrencyException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            "Target package changed during atomic publication",
            SafeReason(conflict.Message, targetPath),
            retryable: true,
            innerException: conflict
        ),
        OpcPackageResultMismatchException mismatch => new WordToolkitOperationException(
            "RESULT_MISMATCH",
            "Target package does not match the reviewed template style-alignment plan",
            SafeReason(mismatch.Message, targetPath),
            innerException: mismatch
        ),
        OpcPackageValidationException validation => new WordToolkitOperationException(
            "VALIDATION_FAILED",
            "Template style-alignment candidate failed structural validation",
            SafeReason(validation.Message, targetPath),
            innerException: validation
        ),
        OpcPackageRecoveryException recovery => new WordToolkitOperationException(
            "RECOVERY_REQUIRED",
            "Atomic target commit detected a concurrent change and requires recovery inspection",
            innerException: recovery
        ),
        OpcPackageLimitException limit => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            "Target or template exceeds a bounded OPC safety limit",
            SafeReason(limit.Message, targetPath, templatePath),
            innerException: limit
        ),
        InvalidDataException invalid => new WordToolkitOperationException(
            "INVALID_PACKAGE",
            "Target or template is not a readable OPC ZIP package",
            innerException: invalid
        ),
        FileNotFoundException or DirectoryNotFoundException =>
            new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested target or template package does not exist",
                innerException: exception
            ),
        UnauthorizedAccessException => new WordToolkitOperationException(
            "ACCESS_DENIED",
            "Target or template cannot be read or the target cannot be written",
            innerException: exception
        ),
        IOException io => new WordToolkitOperationException(
            "IO_ERROR",
            "Template style alignment could not read or publish the package",
            SafeReason(io.Message, targetPath, templatePath),
            retryable: true,
            innerException: io
        ),
        ArgumentException argument => Invalid(
            SafeReason(argument.Message, targetPath, templatePath)
                ?? "Invalid template style-alignment request",
            argument
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "Template style-alignment operation failed",
            innerException: exception
        ),
    };

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? exception = null
    ) => new("INVALID_INPUT", message, innerException: exception);

    private sealed record InputContext(
        string TargetPath,
        string TemplatePath,
        OpcPackageSnapshot Target,
        OpcPackageSnapshot Template
    );

    private sealed record PlanContext(
        string TargetPath,
        string TemplatePath,
        OpcPackageSnapshot Target,
        OpcPackageSnapshot Template,
        OpcPackageSnapshot Candidate,
        WordTemplateStyleAlignmentPlan Plan,
        bool TargetHasDigitalSignatures,
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
