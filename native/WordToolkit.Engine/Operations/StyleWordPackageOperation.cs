using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Plans and atomically applies bounded, source-preserving semantic style edits to one
/// saved Word package. MCP, CLI and direct .NET callers share this implementation.
/// </summary>
public sealed class StyleWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer;
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public StyleWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _serializer = new OpcPackageSerializer();
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public StyleEditPlanResult Plan(
        StyleEditPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Style edit plan request is required");
            }
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

    public StyleEditApplyResult Apply(
        StyleEditApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Style edit apply request is required");
            }
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid semantic edit plan ID");
            }

            var context = BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.Commands,
                cancellationToken
            );
            if (!string.Equals(
                    context.PlanId,
                    request.ExpectedPlanId,
                    StringComparison.Ordinal
                ))
            {
                throw new WordToolkitOperationException(
                    "PLAN_MISMATCH",
                    "Commands do not reproduce the reviewed semantic edit plan ID"
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
                    "Semantic style editing is blocked by document protection or permission metadata",
                    details: new StyleEditPolicyBlockDetails(context.PlanId, protectionBlocks)
                );
            }
            if (context.HasDigitalSignatures)
            {
                throw new WordToolkitOperationException(
                    "SIGNED_PACKAGE",
                    "Direct OOXML editing is blocked because the package contains digital signatures"
                );
            }
            if (!context.Validation.Performed)
            {
                throw new WordToolkitOperationException(
                    "VALIDATOR_REQUIRED",
                    "Applying semantic style edits requires a candidate package schema validator"
                );
            }
            if (!context.Validation.NoNewErrors)
            {
                var issues = context.Validation.Issues.Take(20).ToArray();
                throw new WordToolkitOperationException(
                    "OOXML_SCHEMA_INVALID",
                    "The exact candidate package introduces Microsoft Open XML schema errors",
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
                return new StyleEditApplyResult(
                    StyleWordPackageContract.ApplyContract,
                    Path.GetFileName(context.Path),
                    context.PlanId,
                    Applied: false,
                    NoOp: true,
                    OperationCount: null,
                    PreviousPackageFingerprint: null,
                    PackageFingerprint: context.Package.Fingerprint,
                    PredictedPackageFingerprint: null,
                    BackupPath: null,
                    ChangedEntryNames: Array.Empty<string>(),
                    DiagnosticCount: null,
                    MicrosoftSchemaValid: context.Validation.CandidateValid,
                    MicrosoftSchemaNoNewErrors: context.Validation.NoNewErrors,
                    RawXmlReturned: false,
                    MutationPerformed: false,
                    WordOpened: false,
                    ExplicitAuthorizations: context.Protection.AuthorizationRequired
                        ? ["protected_edit_authorization"]
                        : Array.Empty<string>()
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
            return new StyleEditApplyResult(
                StyleWordPackageContract.ApplyContract,
                Path.GetFileName(context.Path),
                context.PlanId,
                Applied: true,
                NoOp: false,
                OperationCount: context.Plan.OperationCount,
                PreviousPackageFingerprint: context.Package.Fingerprint,
                PackageFingerprint: result.Fingerprint,
                PredictedPackageFingerprint: context.Plan.ResultPackageFingerprint,
                BackupPath: result.BackupPath,
                ChangedEntryNames: result.ChangedEntryNames,
                DiagnosticCount: result.Diagnostics.Count,
                MicrosoftSchemaValid: context.Validation.CandidateValid,
                MicrosoftSchemaNoNewErrors: context.Validation.NoNewErrors,
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

    private PlanContext BuildContext(
        string localPath,
        string expectedPackageFingerprint,
        IReadOnlyList<StyleEditCommand> commands,
        CancellationToken cancellationToken
    )
    {
        ValidateRequest(localPath, expectedPackageFingerprint, commands);
        var commandSnapshot = SnapshotCommands(commands);
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
                expectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "Saved package changed before the semantic edit plan was built"
            );
        }

        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        if (
            !package.Parts.TryGetValue(semantic.MainPartUri, out var mainPart)
            || !WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
                path,
                mainPart.ContentType
            )
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The filename extension does not match the Word main-part content type"
            );
        }

        var parsed = ResolveCommands(commandSnapshot, semantic, cancellationToken);
        var plan = new WordSemanticTransactionPlanner(
            new WordSemanticTransactionOptions
            {
                MaxCommands = StyleWordPackageContract.MaximumCommands,
            }
        ).PlanStyleEdits(
            package,
            semantic,
            parsed.DefinitionCommands,
            parsed.AssignmentCommands,
            cancellationToken
        );
        if (plan.ChangedPartCount > StyleWordPackageContract.MaximumChangedParts)
        {
            throw new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                $"Semantic style edits may change at most {StyleWordPackageContract.MaximumChangedParts} package parts"
            );
        }

        var validation = ValidateExactCandidate(package, plan, cancellationToken);
        var projector = new WordSemanticProjector();
        var semanticForProtection = projector.Project(package, cancellationToken);
        using var protectionCandidateStream = new MemoryStream();
        _serializer.Write(protectionCandidateStream, plan.CreateMutation(package));
        protectionCandidateStream.Position = 0;
        var protectionCandidate = _reader.Read(protectionCandidateStream, cancellationToken);
        var candidateSemanticForProtection = projector.Project(protectionCandidate, cancellationToken);
        return new PlanContext(
            path,
            package,
            plan,
            CreatePlanId(plan.PlanId, parsed.IntentFields),
            commandSnapshot.Count,
            parsed.SelectorResolutions,
            WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package),
            validation,
            WordPackagePatchRiskAnalyzer.AssessProtection(
                package,
                semanticForProtection,
                protectionCandidate,
                candidateSemanticForProtection,
                plan.HasChanges,
                cancellationToken
            )
        );
    }

    private WordPackageCandidateValidationReport ValidateExactCandidate(
        OpcPackageSnapshot package,
        WordSemanticTransactionPlan plan,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
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
                "The exact candidate package has structural OPC errors"
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
        _ = new WordSemanticProjector().Project(candidateSnapshot, cancellationToken);

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

    private static StyleEditPlanResult ProjectPlan(
        PlanContext context,
        bool includeDetails
    )
    {
        var blockedReasons = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blockedReasons.Add("digital_signature_present");
        }
        if (!context.Validation.Performed)
        {
            blockedReasons.Add("schema_validator_unavailable");
        }
        else if (!context.Validation.NoNewErrors)
        {
            blockedReasons.Add("microsoft_schema_validation_failed");
        }
        if (context.Plan.HasChanges && context.Protection.HasMalformedProtectionMetadata)
        {
            blockedReasons.Add("protection_metadata_malformed");
        }
        else if (context.Plan.HasChanges && context.Protection.AuthorizationRequired)
        {
            blockedReasons.Add("protected_document_edit_not_authorized");
        }
        var requiredAuthorizations = context.Plan.HasChanges
            && context.Protection.AuthorizationRequired
            && !context.Protection.HasMalformedProtectionMetadata
            ? (IReadOnlyList<string>)["protected_edit_authorization"]
            : Array.Empty<string>();

        return new StyleEditPlanResult(
            StyleWordPackageContract.PlanContract,
            Path.GetFileName(context.Path),
            context.PlanId,
            context.Plan.BasePackageFingerprint,
            context.Plan.ResultPackageFingerprint,
            context.SubmittedCommandCount,
            context.SelectorResolutions.Count,
            context.SelectorResolutions.Sum(item => item.MatchedNodeCount),
            context.Plan.DefinitionOperations.Count,
            context.Plan.DefinitionOperations.Count(operation =>
                operation.Kind == "consolidate_style"
            ),
            context.Plan.DefinitionOperations.Count(operation =>
                operation.Kind == "delete_unused_style"
            ),
            context.Plan.DefinitionOperations.Count(operation =>
                operation.Kind == "rename_style"
            ),
            context.Plan.DefinitionOperations.Sum(operation =>
                operation.ReferenceUpdateCount
            ),
            context.Plan.Operations.Count,
            context.Plan.OperationCount,
            context.Plan.ChangedOperationCount,
            context.Plan.ChangedPartCount,
            context.Plan.TotalXmlByteDelta,
            context.Plan.HasChanges,
            CanApply: blockedReasons.Count == 0,
            ApplyBlocked: blockedReasons.Count != 0,
            ApplyBlockedReasons: blockedReasons,
            CandidateValidation: ProjectValidation(
                context.Validation,
                includeDetails
            ),
            Operations: includeDetails
                ? context.Plan.Operations.Select(operation =>
                    new StyleEditOperationDetail(
                        operation.Index,
                        operation.Kind,
                        operation.NodeId.Value,
                        operation.PropertyName,
                        Bound(operation.BeforeValue, 253),
                        Bound(operation.AfterValue, 253),
                        Bound(operation.SourcePartUri, 512)!,
                        operation.SourceElementOrdinal,
                        operation.XmlByteDelta,
                        operation.HasChange
                    )
                ).ToArray()
                : null,
            StyleDefinitionOperations: includeDetails
                ? context.Plan.DefinitionOperations.Select(operation =>
                    new StyleDefinitionOperationDetail(
                        operation.Index,
                        operation.Kind,
                        Bound(operation.StyleId, 253)!,
                        Bound(operation.SourceStyleId, 253),
                        operation.StyleType.ToString().ToLowerInvariant(),
                        Bound(operation.SourcePartUri, 512)!,
                        operation.SourceElementOrdinal,
                        operation.ReferenceUpdateCount,
                        operation.XmlByteDelta,
                        operation.HasChange
                    )
                ).ToArray()
                : null,
            ChangedParts: includeDetails
                ? context.Plan.ChangedParts.Select(part =>
                    new StyleEditChangedPart(
                        Bound(part.PartUri, 512)!,
                        part.BeforeBytes,
                        part.AfterBytes,
                        (long)part.AfterBytes - part.BeforeBytes
                    )
                ).ToArray()
                : null,
            SelectorResolutions: includeDetails
                ? context.SelectorResolutions
                : null,
            RawXmlReturned: false,
            MutationPerformed: false,
            WordOpened: false,
            Protection: context.Protection,
            ProtectionAuthorizationId: requiredAuthorizations.Count == 0 ? null : context.PlanId,
            RequiredAuthorizations: requiredAuthorizations
        );
    }

    private static ResolvedCommands ResolveCommands(
        IReadOnlyList<StyleEditCommand> commands,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken
    )
    {
        var definitions = new List<WordStyleDefinitionCommand>(commands.Count);
        var assignments = new List<WordStyleAssignmentCommand>(commands.Count);
        var intentFields = new List<string>(checked(commands.Count * 16));
        var selectors = new List<StyleEditSelectorResolution>();

        for (var index = 0; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = commands[index]
                ?? throw Invalid($"commands[{index}] cannot be null");
            switch (command)
            {
                case CreateStyleEditCommand create:
                    AddCreate(create, index, definitions, intentFields);
                    break;
                case CloneStyleEditCommand clone:
                    AddClone(clone, index, definitions, intentFields);
                    break;
                case ConsolidateStyleEditCommand consolidate:
                    AddConsolidate(consolidate, index, definitions, intentFields);
                    break;
                case DeleteUnusedStyleEditCommand delete:
                    AddDelete(delete, index, definitions, intentFields);
                    break;
                case RenameStyleEditCommand rename:
                    AddRename(rename, index, definitions, intentFields);
                    break;
                case SetStyleEditCommand set:
                    AddSet(set, index, assignments, intentFields);
                    break;
                case SetStyleWhereEditCommand selected:
                    if (selectors.Count >= StyleWordPackageContract.MaximumSelectorCommands)
                    {
                        throw new WordToolkitOperationException(
                            "TRANSACTION_LIMIT",
                            $"At most {StyleWordPackageContract.MaximumSelectorCommands} selector commands are allowed"
                        );
                    }
                    AddSelected(
                        selected,
                        index,
                        semanticDocument,
                        assignments,
                        intentFields,
                        selectors,
                        cancellationToken
                    );
                    break;
                default:
                    throw Invalid($"commands[{index}] has an unsupported command type");
            }

            if (
                definitions.Count + assignments.Count
                > StyleWordPackageContract.MaximumCommands
            )
            {
                throw new WordToolkitOperationException(
                    "TRANSACTION_LIMIT",
                    $"Resolved semantic edits exceed the {StyleWordPackageContract.MaximumCommands}-operation transaction limit"
                );
            }
        }
        return new ResolvedCommands(
            definitions,
            assignments,
            intentFields,
            selectors
        );
    }

    private static WordPackageCandidateValidationReport ProjectValidation(
        WordPackageCandidateValidationReport report,
        bool includeDetails
    ) => includeDetails
        ? report
        : report with
        {
            ErrorsTruncated = report.ErrorsTruncated || report.Issues.Count > 0,
            Issues = Array.Empty<WordPackageValidationIssue>(),
        };

    private static void AddCreate(
        CreateStyleEditCommand command,
        int index,
        ICollection<WordStyleDefinitionCommand> definitions,
        ICollection<string> intent
    )
    {
        var styleId = StyleText(command.StyleId, "style_id");
        var name = StyleText(command.Name, "name");
        if (!Enum.IsDefined(command.StyleType))
        {
            throw Invalid("style_type is not supported");
        }
        var basedOn = OptionalStyleText(command.BasedOnStyleId, "based_on_style_id");
        var next = OptionalStyleText(command.NextStyleId, "next_style_id");
        if (command.UiPriority is < 0)
        {
            throw Invalid("ui_priority must be between 0 and 2147483647");
        }
        definitions.Add(
            new WordStyleCreateCommand(
                styleId,
                name,
                command.StyleType,
                basedOn,
                next,
                command.QuickFormat,
                command.UiPriority
            )
        );
        AddStyleDefinitionIntent(
            intent,
            index,
            "create_style",
            styleId,
            name,
            command.StyleType.ToString(),
            basedOn,
            next,
            command.QuickFormat,
            command.UiPriority,
            null
        );
    }

    private static void AddClone(
        CloneStyleEditCommand command,
        int index,
        ICollection<WordStyleDefinitionCommand> definitions,
        ICollection<string> intent
    )
    {
        var source = StyleText(command.SourceStyleId, "source_style_id");
        var styleId = StyleText(command.StyleId, "style_id");
        var name = StyleText(command.Name, "name");
        definitions.Add(new WordStyleCloneCommand(source, styleId, name));
        AddStyleDefinitionIntent(
            intent,
            index,
            "clone_style",
            styleId,
            name,
            null,
            null,
            null,
            null,
            null,
            source
        );
    }

    private static void AddConsolidate(
        ConsolidateStyleEditCommand command,
        int index,
        ICollection<WordStyleDefinitionCommand> definitions,
        ICollection<string> intent
    )
    {
        var source = StyleText(command.SourceStyleId, "source_style_id");
        var target = StyleText(command.TargetStyleId, "target_style_id");
        definitions.Add(new WordStyleConsolidateCommand(source, target));
        intent.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        intent.Add("consolidate_style");
        intent.Add(source);
        intent.Add(target);
    }

    private static void AddDelete(
        DeleteUnusedStyleEditCommand command,
        int index,
        ICollection<WordStyleDefinitionCommand> definitions,
        ICollection<string> intent
    )
    {
        var styleId = StyleText(command.StyleId, "style_id");
        definitions.Add(new WordStyleDeleteUnusedCommand(styleId));
        intent.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        intent.Add("delete_unused_style");
        intent.Add(styleId);
    }

    private static void AddRename(
        RenameStyleEditCommand command,
        int index,
        ICollection<WordStyleDefinitionCommand> definitions,
        ICollection<string> intent
    )
    {
        var styleId = StyleText(command.StyleId, "style_id");
        var name = StyleText(command.Name, "name");
        definitions.Add(new WordStyleRenameCommand(styleId, name));
        intent.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        intent.Add("rename_style");
        intent.Add(styleId);
        intent.Add(name);
    }

    private static void AddSet(
        SetStyleEditCommand command,
        int index,
        ICollection<WordStyleAssignmentCommand> assignments,
        ICollection<string> intent
    )
    {
        ValidateNodeId(command.NodeId, "node_id");
        var style = ValidateAssignment(
            command.StyleId,
            command.ExpectedStyleId,
            command.RequireNoExplicitStyle
        );
        assignments.Add(
            new WordStyleAssignmentCommand(
                new SemanticNodeId(command.NodeId),
                style.StyleId,
                style.ExpectedStyleId,
                style.RequireNoExplicitStyle
            )
        );
        AddAssignmentIntent(intent, index, "set_style", command.NodeId, style);
    }

    private static void AddSelected(
        SetStyleWhereEditCommand command,
        int index,
        WordSemanticDocument semanticDocument,
        ICollection<WordStyleAssignmentCommand> assignments,
        ICollection<string> intent,
        ICollection<StyleEditSelectorResolution> selectors,
        CancellationToken cancellationToken
    )
    {
        if (command.Selector is null)
        {
            throw Invalid("selector is required");
        }
        if (command.MaxMatches is < 1 or > StyleWordPackageContract.MaximumSelectorMatches)
        {
            throw Invalid(
                $"max_matches must be between 1 and {StyleWordPackageContract.MaximumSelectorMatches}"
            );
        }
        var style = ValidateAssignment(
            command.StyleId,
            command.ExpectedStyleId,
            command.RequireNoExplicitStyle
        );
        var query = SelectorQuery(command.Selector);
        WordSemanticQueryResult result;
        try
        {
            result = new WordSemanticQueryEngine().Query(
                semanticDocument,
                query,
                cancellationToken
            );
        }
        catch (KeyNotFoundException exception)
        {
            throw new WordToolkitOperationException(
                "UNSAFE_EDIT",
                Bound(exception.Message, 512) ?? "Selector scope does not exist",
                innerException: exception
            );
        }
        catch (ArgumentException exception)
        {
            throw Invalid(
                Bound(exception.Message, 512) ?? "Selector is invalid",
                exception
            );
        }
        if (result.MatchedNodeCount == 0)
        {
            throw new WordToolkitOperationException(
                "EMPTY_SELECTION",
                "set_style_where selector matched no semantic nodes"
            );
        }
        if (result.MatchedNodeCount > command.MaxMatches)
        {
            throw new WordToolkitOperationException(
                "SELECTION_LIMIT",
                "set_style_where selector exceeded max_matches"
            );
        }
        foreach (var match in result.Matches)
        {
            assignments.Add(
                new WordStyleAssignmentCommand(
                    match.NodeId,
                    style.StyleId,
                    style.ExpectedStyleId,
                    style.RequireNoExplicitStyle
                )
            );
        }
        selectors.Add(
            new StyleEditSelectorResolution(
                index,
                result.MatchedNodeCount,
                result.ScannedNodeCount,
                result.CandidateSeed
            )
        );
        AddAssignmentIntent(intent, index, "set_style_where", null, style);
        AddQueryIntent(intent, query, command.MaxMatches);
    }

    private static WordSemanticQuery SelectorQuery(StyleEditSelector selector)
    {
        if (selector.Kind is not WordSemanticNodeKind.Paragraph
            and not WordSemanticNodeKind.Run
            and not WordSemanticNodeKind.Table)
        {
            throw Invalid("selector.kind must be paragraph, run, or table");
        }
        if (!Enum.IsDefined(selector.TextMatch))
        {
            throw Invalid("selector.text_match is not supported");
        }
        if (!Enum.IsDefined(selector.TextScope))
        {
            throw Invalid("selector.text_scope is not supported");
        }
        var properties = SnapshotProperties(
            selector.PropertyEquals,
            "selector.property_equals"
        );
        if (properties is { Count: 0 })
        {
            throw Invalid("selector.property_equals cannot be empty");
        }
        if (selector.WithinNodeId is { } within)
        {
            ValidateNodeId(within, "selector.within_node_id");
        }
        var query = new WordSemanticQuery
        {
            Kinds = [selector.Kind],
            Text = selector.Text,
            TextMatch = selector.TextMatch,
            TextScope = selector.TextScope,
            CaseSensitive = selector.CaseSensitive,
            PropertyEquals = properties,
            Ancestor = Related(selector.Ancestor, "selector.ancestor"),
            Descendant = Related(selector.Descendant, "selector.descendant"),
            WithinNodeId = selector.WithinNodeId is null
                ? null
                : new SemanticNodeId(selector.WithinNodeId),
            SourcePartUri = selector.SourcePartUri,
            Offset = 0,
            Limit = StyleWordPackageContract.MaximumSelectorMatches,
            TextPreviewCharacters = 0,
            IncludeProperties = false,
            IncludeSource = false,
        };
        try
        {
            query.Validate();
        }
        catch (ArgumentException exception)
        {
            throw Invalid(
                Bound(exception.Message, 512) ?? "Selector is invalid",
                exception
            );
        }
        return query;
    }

    private static WordSemanticRelatedNodePredicate? Related(
        StyleEditRelatedPredicate? predicate,
        string name
    )
    {
        if (predicate is null)
        {
            return null;
        }
        var properties = SnapshotProperties(
            predicate.PropertyEquals,
            $"{name}.property_equals"
        );
        if (predicate.Kind is null && properties is null)
        {
            throw Invalid($"{name} must contain kind or property_equals");
        }
        if (predicate.Kind is { } kind && !Enum.IsDefined(kind))
        {
            throw Invalid($"{name}.kind is not supported");
        }
        if (properties is { Count: 0 })
        {
            throw Invalid($"{name}.property_equals cannot be empty");
        }
        return new WordSemanticRelatedNodePredicate
        {
            Kinds = predicate.Kind is null ? null : [predicate.Kind.Value],
            PropertyEquals = properties,
        };
    }

    private static IReadOnlyList<StyleEditCommand> SnapshotCommands(
        IReadOnlyList<StyleEditCommand> commands
    )
    {
        try
        {
            var snapshot = commands.ToArray();
            if (
                snapshot.Length != commands.Count
                || snapshot.Length is < 1 or > StyleWordPackageContract.MaximumCommands
            )
            {
                throw Invalid("commands changed while the request was being read");
            }
            return snapshot;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            throw Invalid("commands changed while the request was being read", exception);
        }
    }

    private static IReadOnlyDictionary<string, string>? SnapshotProperties(
        IReadOnlyDictionary<string, string>? source,
        string name
    )
    {
        if (source is null)
        {
            return null;
        }
        if (source.Count > 16)
        {
            throw Invalid($"{name} cannot contain more than 16 entries");
        }
        try
        {
            return new Dictionary<string, string>(source, StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException
        )
        {
            throw Invalid($"{name} changed or contains duplicate keys", exception);
        }
    }

    private static AssignmentInput ValidateAssignment(
        string styleId,
        string? expectedStyleId,
        bool requireNoExplicitStyle
    )
    {
        var style = StyleText(styleId, "style_id");
        var expected = OptionalStyleText(expectedStyleId, "expected_style_id");
        if (requireNoExplicitStyle && expected is not null)
        {
            throw Invalid("Use expected_style_id or require_no_explicit_style, never both");
        }
        return new AssignmentInput(style, expected, requireNoExplicitStyle);
    }

    private static string StyleText(string value, string field)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > StyleWordPackageContract.MaximumStyleTextCharacters
        )
        {
            throw Invalid(
                $"{field} must contain between 1 and {StyleWordPackageContract.MaximumStyleTextCharacters} characters"
            );
        }
        return value;
    }

    private static string? OptionalStyleText(string? value, string field) =>
        value is null ? null : StyleText(value, field);

    private static void ValidateRequest(
        string localPath,
        string expectedPackageFingerprint,
        IReadOnlyList<StyleEditCommand> commands
    )
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw Invalid("local_path must be a non-empty string");
        }
        if (localPath.Length > StyleWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid(
                $"local_path cannot exceed {StyleWordPackageContract.MaximumLocalPathCharacters} characters"
            );
        }
        if (!InspectWordPackageContract.IsSupportedFileName(localPath))
        {
            throw Invalid("Style edits accept DOCX, DOCM, DOTX, or DOTM files");
        }
        if (!IsSha256(expectedPackageFingerprint))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (
            commands is null
            || commands.Count is < 1 or > StyleWordPackageContract.MaximumCommands
        )
        {
            throw Invalid(
                $"commands must contain between 1 and {StyleWordPackageContract.MaximumCommands} semantic edits"
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
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
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

    private static void AddStyleDefinitionIntent(
        ICollection<string> fields,
        int commandIndex,
        string type,
        string styleId,
        string name,
        string? styleType,
        string? basedOnStyleId,
        string? nextStyleId,
        bool? quickFormat,
        int? uiPriority,
        string? sourceStyleId
    )
    {
        fields.Add(commandIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fields.Add(type);
        fields.Add(styleId);
        fields.Add(name);
        AddNullableIntent(fields, styleType);
        AddNullableIntent(fields, basedOnStyleId);
        AddNullableIntent(fields, nextStyleId);
        AddNullableIntent(fields, quickFormat?.ToString());
        AddNullableIntent(
            fields,
            uiPriority?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        AddNullableIntent(fields, sourceStyleId);
    }

    private static void AddAssignmentIntent(
        ICollection<string> fields,
        int commandIndex,
        string type,
        string? nodeId,
        AssignmentInput style
    )
    {
        fields.Add(commandIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fields.Add(type);
        AddNullableIntent(fields, nodeId);
        fields.Add(style.StyleId);
        AddNullableIntent(fields, style.ExpectedStyleId);
        fields.Add(style.RequireNoExplicitStyle ? "1" : "0");
    }

    private static void AddQueryIntent(
        ICollection<string> fields,
        WordSemanticQuery query,
        int maxMatches
    )
    {
        fields.Add(query.Kinds!.Single().ToString());
        AddNullableIntent(fields, query.Text);
        fields.Add(query.TextMatch.ToString());
        fields.Add(query.TextScope.ToString());
        fields.Add(query.CaseSensitive ? "1" : "0");
        AddPropertyIntent(fields, query.PropertyEquals);
        AddRelatedIntent(fields, query.Ancestor);
        AddRelatedIntent(fields, query.Descendant);
        AddNullableIntent(fields, query.WithinNodeId?.Value);
        AddNullableIntent(fields, query.SourcePartUri);
        fields.Add(maxMatches.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AddRelatedIntent(
        ICollection<string> fields,
        WordSemanticRelatedNodePredicate? predicate
    )
    {
        if (predicate is null)
        {
            fields.Add("related:null");
            return;
        }
        fields.Add("related:value");
        AddNullableIntent(fields, predicate.Kinds?.Single().ToString());
        AddPropertyIntent(fields, predicate.PropertyEquals);
    }

    private static void AddPropertyIntent(
        ICollection<string> fields,
        IReadOnlyDictionary<string, string>? properties
    )
    {
        if (properties is null)
        {
            fields.Add("properties:null");
            return;
        }
        fields.Add("properties:value");
        fields.Add(properties.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (name, value) in properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            fields.Add(name);
            fields.Add(value);
        }
    }

    private static void AddNullableIntent(ICollection<string> fields, string? value)
    {
        fields.Add(value is null ? "nullable:null" : "nullable:value");
        if (value is not null)
        {
            fields.Add(value);
        }
    }

    private static string CreatePlanId(
        string enginePlanId,
        IReadOnlyList<string> intentFields
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendIntentHashField(hash, "wordtoolkit-semantic-edit-intent-v1");
        AppendIntentHashField(hash, enginePlanId);
        foreach (var field in intentFields)
        {
            AppendIntentHashField(hash, field);
        }
        var digest = hash.GetHashAndReset();
        return "wseplan_" + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AppendIntentHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void ValidateNodeId(string? nodeId, string field)
    {
        if (
            string.IsNullOrWhiteSpace(nodeId)
            || nodeId.Length is < 5 or > 128
            || !nodeId.StartsWith("wdn_", StringComparison.Ordinal)
            || nodeId[4..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw Invalid($"{field} is not a valid semantic node ID");
        }
    }

    private static bool IsPlanId(string value) =>
        value is not null
        && value.Length is >= 12 and <= 128
        && value.StartsWith("wseplan_", StringComparison.Ordinal)
        && value[8..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
        );

    private static bool IsSha256(string value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string? Bound(string? value, int maximum)
    {
        if (value is null || value.Length <= maximum)
        {
            return value;
        }
        return value[..maximum] + "…";
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath
    ) =>
        exception switch
        {
            WordSemanticTransactionLimitException limit =>
                new WordToolkitOperationException(
                    "TRANSACTION_LIMIT",
                    SafeReason(limit.Message, localPath)
                        ?? "Semantic transaction limit exceeded",
                    innerException: limit
                ),
            WordSemanticPreconditionException conflict =>
                new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    SafeReason(conflict.Message, localPath)
                        ?? "Semantic precondition failed",
                    innerException: conflict
                ),
            WordSemanticEditException edit => new WordToolkitOperationException(
                "UNSAFE_EDIT",
                SafeReason(edit.Message, localPath) ?? "Semantic style edit is unsafe",
                innerException: edit
            ),
            WordSemanticLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                SafeReason(limit.Message, localPath),
                innerException: limit
            ),
            WordStyleLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Style projection exceeds a bounded safety limit",
                SafeReason(limit.Message, localPath),
                innerException: limit
            ),
            WordSemanticProjectionException projection =>
                new WordToolkitOperationException(
                    "INVALID_WORD_PACKAGE",
                    "The package cannot be projected as a Word semantic document",
                    SafeReason(projection.Message, localPath),
                    innerException: projection
                ),
            WordStyleProjectionException projection =>
                new WordToolkitOperationException(
                    "INVALID_WORD_PACKAGE",
                    "The package style graph is invalid",
                    SafeReason(projection.Message, localPath),
                    innerException: projection
                ),
            OpcPackageConcurrencyException conflict =>
                new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "Destination package changed during the atomic write",
                    SafeReason(conflict.Message, localPath),
                    retryable: true,
                    innerException: conflict
                ),
            OpcPackageResultMismatchException mismatch =>
                new WordToolkitOperationException(
                    "RESULT_MISMATCH",
                    "Candidate package does not match the reviewed semantic plan",
                    SafeReason(mismatch.Message, localPath),
                    innerException: mismatch
                ),
            OpcPackageValidationException validation =>
                new WordToolkitOperationException(
                    "VALIDATION_FAILED",
                    "Candidate package failed structural validation",
                    SafeReason(validation.Message, localPath),
                    innerException: validation
                ),
            OpcPackageRecoveryException recovery =>
                new WordToolkitOperationException(
                    "RECOVERY_REQUIRED",
                    "Atomic commit detected a concurrent change and automatic recovery did not finish",
                    retryable: false,
                    innerException: recovery,
                    details: BuildRecoveryDetails(recovery)
                ),
            OpcPackageLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                SafeReason(limit.Message, localPath),
                innerException: limit
            ),
            InvalidDataException invalid => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                innerException: invalid
            ),
            FileNotFoundException missing => new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested Word package does not exist",
                innerException: missing
            ),
            DirectoryNotFoundException missing => new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested Word package does not exist",
                innerException: missing
            ),
            UnauthorizedAccessException denied => new WordToolkitOperationException(
                "ACCESS_DENIED",
                "The Word package cannot be read or written with current permissions",
                innerException: denied
            ),
            ArgumentException invalid => Invalid(
                "Invalid semantic style edit",
                invalid
            ),
            IOException io => new WordToolkitOperationException(
                "IO_ERROR",
                "The Word package could not be read or written",
                retryable: true,
                innerException: io
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The semantic style operation failed",
                innerException: exception
            ),
        };

    internal static WordToolkitRecoveryDetails? BuildRecoveryDetails(
        OpcPackageRecoveryException recovery
    )
    {
        var names = recovery.RecoveryPaths
            .Where(File.Exists)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return names.Length == 0
            ? null
            : new WordToolkitRecoveryDetails(names!);
    }

    private static string? SafeReason(string? message, string? localPath)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }
        var safe = message;
        if (localPath is not null)
        {
            try
            {
                safe = safe.Replace(
                    Path.GetFullPath(localPath),
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
            safe = safe.Replace(
                localPath,
                "<redacted>",
                StringComparison.OrdinalIgnoreCase
            );
        }
        return Bound(safe, 512);
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);

    private sealed record PlanContext(
        string Path,
        OpcPackageSnapshot Package,
        WordSemanticTransactionPlan Plan,
        string PlanId,
        int SubmittedCommandCount,
        IReadOnlyList<StyleEditSelectorResolution> SelectorResolutions,
        bool HasDigitalSignatures,
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
            && !string.Equals(authorization, context.PlanId, StringComparison.Ordinal))
        {
            return ["protected_document_edit_not_authorized"];
        }
        return Array.Empty<string>();
    }

    private sealed record ResolvedCommands(
        IReadOnlyList<WordStyleDefinitionCommand> DefinitionCommands,
        IReadOnlyList<WordStyleAssignmentCommand> AssignmentCommands,
        IReadOnlyList<string> IntentFields,
        IReadOnlyList<StyleEditSelectorResolution> SelectorResolutions
    );

    private sealed record AssignmentInput(
        string StyleId,
        string? ExpectedStyleId,
        bool RequireNoExplicitStyle
    );
}
