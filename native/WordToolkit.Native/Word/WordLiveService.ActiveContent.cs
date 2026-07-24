using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private Task<object> InspectPackageActiveContentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (
            view is not "summary"
                and not "declarations"
                and not "controls"
                and not "payloads"
                and not "relationships"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, declarations, controls, payloads, relationships, or issues"
            );
        }

        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_items") ?? 30;
        if (offset is < 0 or > int.MaxValue)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 2147483647"
            );
        }
        if (maximum is < 1 or > 100)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 100"
            );
        }

        var declarationId = BoundedOptionalArgument(arguments, "declaration_id", 128);
        var controlId = BoundedOptionalArgument(arguments, "control_id", 128);
        var payloadId = BoundedOptionalArgument(arguments, "payload_id", 128);
        var declarationKind = ParseActiveContentDeclarationKind(
            BoundedOptionalArgument(arguments, "declaration_kind", 128)
        );
        var payloadKind = ParseActiveContentPayloadKind(
            BoundedOptionalArgument(arguments, "payload_kind", 128)
        );
        var relationshipRole = ParseActiveContentRelationshipRole(
            BoundedOptionalArgument(arguments, "relationship_role", 128)
        );
        var includeNames = arguments.Boolean("include_names", false);
        var includeTargets = arguments.Boolean("include_targets", false);
        var includeHashes = arguments.Boolean("include_hashes", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", true);

        try
        {
            var resourceLease = _operationResourceLeaseFactory()
                ?? throw new InvalidOperationException(
                    "The active-content resource-lease factory returned null."
                );
            var package = new OpcPackageReader(
                OpcPackageLimits.Default,
                resourceLease
            ).Read(path, cancellationToken);
            var graph = new WordActiveContentGraphBuilder(null, resourceLease).Build(
                package,
                cancellationToken
            );
            var operationUsage = resourceLease.Snapshot();

            if (declarationId is not null && !graph.TryGetDeclaration(declarationId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The active-content declaration does not exist in this package fingerprint"
                );
            }
            if (payloadId is not null && !graph.TryGetPayload(payloadId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The active-content payload does not exist in this package fingerprint"
                );
            }
            if (controlId is not null && !graph.Controls.Any(item => item.Id == controlId))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The ActiveX control does not exist in this package fingerprint"
                );
            }

            var selected = SelectActiveContent(
                graph,
                declarationId,
                controlId,
                payloadId,
                declarationKind,
                payloadKind,
                relationshipRole
            );
            var page = ActiveContentInspectionItems(
                view,
                selected,
                includeNames,
                includeTargets,
                includeHashes,
                includeSource,
                (int)offset,
                (int)maximum
            );
            var consumed = (long)offset + page.Items.Length;
            var issuePage = includeIssues && view != "issues"
                ? selected.Issues.Take(20)
                    .Select(issue => ActiveContentIssueItem(issue, includeSource))
                    .ToArray()
                : null;

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_document_macro_enabled = graph.MainDocumentMacroEnabled,
                declaration_count = selected.Declarations.Length,
                control_count = selected.Controls.Length,
                payload_count = selected.Payloads.Length,
                potentially_executable_payload_count = selected.Payloads.Count(item =>
                    item.IsPotentiallyExecutable
                ),
                relationship_count = selected.Relationships.Length,
                external_relationship_count = selected.Relationships.Count(item =>
                    item.TargetMode == OpcRelationshipTargetMode.External
                ),
                unresolved_relationship_count = selected.Relationships.Count(item =>
                    !item.IsResolved
                ),
                issue_count = selected.Issues.Length,
                issues_truncated_at_source = graph.IssuesTruncated,
                execution_policy =
                    "metadata_only_never_execute_macros_decode_binary_open_embedded_packages_validate_signatures_or_follow_external_targets",
                word_opened = false,
                package_mutated = false,
                macros_executed = false,
                binary_payloads_decoded = graph.BinaryPayloadsDecoded,
                embedded_packages_opened = graph.EmbeddedPackagesOpened,
                cryptographic_signature_validation_performed = graph
                    .CryptographicSignatureValidationPerformed,
                external_targets_followed = false,
                raw_xml_included = false,
                field_codes_included = false,
                activex_licenses_included = false,
                names_included = includeNames,
                targets_included = includeTargets,
                hashes_included = includeHashes,
                source_included = includeSource,
                view,
                declaration_id = declarationId,
                control_id = controlId,
                payload_id = payloadId,
                declaration_kind = declarationKind is null
                    ? null
                    : ToSnakeCase(declarationKind.Value.ToString()),
                payload_kind = payloadKind is null
                    ? null
                    : ToSnakeCase(payloadKind.Value.ToString()),
                relationship_role = relationshipRole is null
                    ? null
                    : ToSnakeCase(relationshipRole.Value.ToString()),
                matched_item_count = page.MatchedCount,
                offset,
                returned_item_count = page.Items.Length,
                next_offset = consumed < page.MatchedCount
                    ? (int)consumed
                    : (int?)null,
                items = page.Items,
                issues = issuePage,
                issues_truncated = issuePage is not null
                    && selected.Issues.Length > issuePage.Length,
                operation_budget = new
                {
                    model = "wop1",
                    used = operationUsage.AccountedBytes,
                    maximum = operationUsage.MaximumAccountedBytes,
                },
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordOperationResourceLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The active-content inspection exceeded its operation resource budget",
                new
                {
                    reason = "The operation resource budget was exhausted",
                    operation_budget = new
                    {
                        model = "wop1",
                        used = exception.AccountedBytes,
                        maximum = exception.MaximumAccountedBytes,
                        attempted = exception.AttemptedBytes,
                        stage = ToSnakeCase(exception.Stage.ToString()),
                    },
                }
            );
        }
        catch (WordActiveContentLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The active-content graph exceeds a bounded safety limit",
                new { reason_code = "active_content_graph_limit" }
            );
        }
        catch (WordActiveContentProjectionException)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into an active-content graph",
                new { reason_code = "active_content_projection_failed" }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
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
                "The Word package cannot be read with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The active-content graph could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static ActiveContentSelection SelectActiveContent(
        WordActiveContentGraph graph,
        string? declarationId,
        string? controlId,
        string? payloadId,
        WordActiveContentDeclarationKind? declarationKind,
        WordActiveContentPayloadKind? payloadKind,
        WordActiveContentRelationshipRole? relationshipRole
    )
    {
        var declarationConstraint = declarationId is not null || declarationKind is not null;
        var controlConstraint = controlId is not null;
        var payloadConstraint = payloadId is not null || payloadKind is not null;
        var declarationCandidates = graph.Declarations.Where(item =>
            (declarationId is null || item.Id == declarationId)
            && (declarationKind is null || item.Kind == declarationKind)
        ).ToArray();
        var controlCandidates = graph.Controls.Where(item =>
            controlId is null || item.Id == controlId
        ).ToArray();
        var payloadCandidates = graph.Payloads.Where(item =>
            (payloadId is null || item.Id == payloadId)
            && (payloadKind is null || item.Kind == payloadKind)
        ).ToArray();
        var declarationRelationshipIds = declarationCandidates
            .Where(item => item.RelationshipNodeId is not null)
            .Select(item => item.RelationshipNodeId!)
            .ToHashSet(StringComparer.Ordinal);
        var controlRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in controlCandidates)
        {
            if (control.BinaryRelationshipNodeId is { } binaryRelationshipId)
            {
                controlRelationshipIds.Add(binaryRelationshipId);
            }
            foreach (var relationship in graph.Relationships.Where(item =>
                item.SourcePartUri == control.PartUri
                || item.TargetPartUri == control.PartUri
            ))
            {
                controlRelationshipIds.Add(relationship.Id);
            }
        }
        var payloadCandidateIds = payloadCandidates.Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        var relationships = graph.Relationships.Where(item =>
            (!payloadConstraint
                || item.PayloadId is not null
                    && payloadCandidateIds.Contains(item.PayloadId))
            && (!declarationConstraint
                || declarationRelationshipIds.Contains(item.Id))
            && (!controlConstraint || controlRelationshipIds.Contains(item.Id))
            && (relationshipRole is null || item.Role == relationshipRole)
        ).ToArray();
        var relationshipIds = relationships.Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var relatedPayloadIds = relationships.Where(item => item.PayloadId is not null)
            .Select(item => item.PayloadId!)
            .ToHashSet(StringComparer.Ordinal);

        var declarations = declarationCandidates.Where(item =>
            (!controlConstraint
                || controlCandidates.Any(control =>
                    control.DeclarationIds.Contains(item.Id)
                ))
            && (!payloadConstraint && relationshipRole is null
                || item.RelationshipNodeId is not null
                    && relationshipIds.Contains(item.RelationshipNodeId))
        ).ToArray();
        var declarationCandidateIds = declarationCandidates.Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var controls = controlCandidates.Where(item =>
            (!declarationConstraint
                || item.DeclarationIds.Any(declarationCandidateIds.Contains))
            && (!payloadConstraint
                || item.BinaryPayloadId is not null
                    && payloadCandidateIds.Contains(item.BinaryPayloadId)
                || payloadCandidates.Any(payload => payload.PartUri == item.PartUri))
            && (relationshipRole is null
                || controlRelationshipIds.Overlaps(relationshipIds))
        ).ToArray();
        var payloads = payloadCandidates.Where(item =>
            (!declarationConstraint && !controlConstraint && relationshipRole is null
                || relatedPayloadIds.Contains(item.Id)
                || controls.Any(control => control.PartUri == item.PartUri))
        ).ToArray();

        var selectedIds = declarations.Select(item => item.Id)
            .Concat(controls.Select(item => item.Id))
            .Concat(payloads.Select(item => item.Id))
            .Concat(relationships.Select(item => item.Id))
            .ToHashSet(StringComparer.Ordinal);
        var selectedRelationshipNames = relationships.Select(item => item.RelationshipId)
            .ToHashSet(StringComparer.Ordinal);
        var hasFilters = declarationId is not null
            || controlId is not null
            || payloadId is not null
            || declarationKind is not null
            || payloadKind is not null
            || relationshipRole is not null;
        var issues = graph.Issues.Where(issue =>
            !hasFilters
            || issue.SubjectId is not null && selectedIds.Contains(issue.SubjectId)
            || issue.RelationshipId is not null
                && selectedRelationshipNames.Contains(issue.RelationshipId)
        ).ToArray();
        return new ActiveContentSelection(
            declarations,
            controls,
            payloads,
            relationships,
            issues
        );
    }

    private static ActiveContentInspectionPage ActiveContentInspectionItems(
        string view,
        ActiveContentSelection selected,
        bool includeNames,
        bool includeTargets,
        bool includeHashes,
        bool includeSource,
        int offset,
        int maximum
    )
    {
        IEnumerable<object> items;
        int matchedCount;
        switch (view)
        {
            case "summary":
                var payloadKinds = selected.Payloads.GroupBy(item => item.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => (object)new
                    {
                        category = "payload_kind",
                        name = ToSnakeCase(group.Key.ToString()),
                        count = group.Count(),
                    });
                var declarationKinds = selected.Declarations.GroupBy(item => item.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => (object)new
                    {
                        category = "declaration_kind",
                        name = ToSnakeCase(group.Key.ToString()),
                        count = group.Count(),
                    });
                var summary = payloadKinds.Concat(declarationKinds).ToArray();
                items = summary;
                matchedCount = summary.Length;
                break;
            case "declarations":
                items = selected.Declarations.Select(item => ActiveContentDeclarationItem(
                    item,
                    includeNames,
                    includeSource
                ));
                matchedCount = selected.Declarations.Length;
                break;
            case "controls":
                items = selected.Controls.Select(item => ActiveContentControlItem(
                    item,
                    includeNames,
                    includeSource
                ));
                matchedCount = selected.Controls.Length;
                break;
            case "payloads":
                items = selected.Payloads.Select(item => ActiveContentPayloadItem(
                    item,
                    includeHashes,
                    includeSource
                ));
                matchedCount = selected.Payloads.Length;
                break;
            case "relationships":
                items = selected.Relationships.Select(item =>
                    ActiveContentRelationshipItem(item, includeTargets, includeSource)
                );
                matchedCount = selected.Relationships.Length;
                break;
            case "issues":
                items = selected.Issues.Select(item =>
                    ActiveContentIssueItem(item, includeSource)
                );
                matchedCount = selected.Issues.Length;
                break;
            default:
                throw new UnreachableException();
        }
        return new ActiveContentInspectionPage(
            items.Skip(offset).Take(maximum).ToArray(),
            matchedCount
        );
    }

    private static object ActiveContentDeclarationItem(
        WordActiveContentDeclaration item,
        bool includeNames,
        bool includeSource
    ) => new
    {
        declaration_id = item.Id,
        kind = ToSnakeCase(item.Kind.ToString()),
        relationship_node_id = item.RelationshipNodeId,
        resolved = item.IsResolved,
        has_field_codes = item.HasFieldCodes,
        field_code_character_count = item.FieldCodeCharacters,
        program_id = includeNames ? BoundForResponse(item.ProgramId, 128) : null,
        object_type = includeNames ? BoundForResponse(item.ObjectType, 128) : null,
        draw_aspect = includeNames ? BoundForResponse(item.DrawAspect, 128) : null,
        update_mode = includeNames ? BoundForResponse(item.UpdateMode, 128) : null,
        shape_id = includeNames ? BoundForResponse(item.ShapeId, 128) : null,
        object_id = includeNames ? BoundForResponse(item.ObjectId, 128) : null,
        control_name = includeNames ? BoundForResponse(item.ControlName, 512) : null,
        link_type = includeNames ? BoundForResponse(item.LinkType, 128) : null,
        server_format = includeNames ? BoundForResponse(item.ServerFormat, 128) : null,
        locked_field = includeNames ? BoundForResponse(item.LockedField, 128) : null,
        relationship_id = includeSource
            ? BoundForResponse(item.RelationshipId, 128)
            : null,
        part_uri = includeSource ? BoundForResponse(item.SourcePartUri, 512) : null,
        source_element_ordinal = includeSource
            ? item.SourceElementOrdinal
            : (int?)null,
    };

    private static object ActiveContentControlItem(
        WordActiveXControlDefinition item,
        bool includeNames,
        bool includeSource
    ) => new
    {
        control_id = item.Id,
        binary_payload_id = item.BinaryPayloadId,
        declaration_count = item.DeclarationIds.Count,
        declaration_ids = item.DeclarationIds.Take(100).ToArray(),
        declaration_ids_truncated = item.DeclarationIds.Count > 100,
        property_count = item.PropertyCount,
        has_license = item.HasLicense,
        license_character_count = item.LicenseCharacters,
        resolved = item.IsResolved,
        class_id = includeNames ? BoundForResponse(item.ClassId, 128) : null,
        persistence = includeNames ? BoundForResponse(item.Persistence, 128) : null,
        binary_relationship_id = includeSource
            ? BoundForResponse(item.BinaryRelationshipId, 128)
            : null,
        binary_relationship_node_id = item.BinaryRelationshipNodeId,
        part_uri = includeSource ? BoundForResponse(item.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? item.SourceElementOrdinal
            : (int?)null,
    };

    private static object ActiveContentPayloadItem(
        WordActiveContentPayload item,
        bool includeHashes,
        bool includeSource
    ) => new
    {
        payload_id = item.Id,
        kind = ToSnakeCase(item.Kind.ToString()),
        uncompressed_length = item.UncompressedLength,
        incoming_relationship_count = item.IncomingRelationshipCount,
        package_reachable = item.IsPackageReachable,
        is_xml = item.IsXml,
        potentially_executable = item.IsPotentiallyExecutable,
        container_family = item.ContainerFamily,
        sha256 = includeHashes ? item.Sha256 : null,
        content_type = includeSource
            ? BoundForResponse(item.ContentType, 512)
            : null,
        part_uri = includeSource ? BoundForResponse(item.PartUri, 512) : null,
    };

    private static object ActiveContentRelationshipItem(
        WordActiveContentRelationship item,
        bool includeTargets,
        bool includeSource
    ) => new
    {
        relationship_node_id = item.Id,
        role = ToSnakeCase(item.Role.ToString()),
        target_mode = ToSnakeCase(item.TargetMode.ToString()),
        payload_id = item.PayloadId,
        resolved = item.IsResolved,
        target = includeTargets ? BoundForResponse(item.Target, 4_096) : null,
        target_part_uri = includeTargets
            ? BoundForResponse(item.TargetPartUri, 512)
            : null,
        relationship_id = includeSource
            ? BoundForResponse(item.RelationshipId, 128)
            : null,
        relationship_type = includeSource
            ? BoundForResponse(item.RelationshipType, 512)
            : null,
        source_part_uri = includeSource
            ? BoundForResponse(item.SourcePartUri, 512)
            : null,
    };

    private static object ActiveContentIssueItem(
        WordActiveContentIssue item,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(item.Code, 128),
        severity = ToSnakeCase(item.Severity.ToString()),
        message = BoundForResponse(item.Message, 512),
        subject_id = item.SubjectId,
        relationship_id = includeSource
            ? BoundForResponse(item.RelationshipId, 128)
            : null,
        part_uri = includeSource ? BoundForResponse(item.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? item.SourceElementOrdinal
            : null,
    };

    private static WordActiveContentDeclarationKind? ParseActiveContentDeclarationKind(
        string? value
    ) => ParseActiveContentEnum<WordActiveContentDeclarationKind>(
        value,
        "declaration_kind"
    );

    private static WordActiveContentPayloadKind? ParseActiveContentPayloadKind(
        string? value
    ) => ParseActiveContentEnum<WordActiveContentPayloadKind>(value, "payload_kind");

    private static WordActiveContentRelationshipRole? ParseActiveContentRelationshipRole(
        string? value
    ) => ParseActiveContentEnum<WordActiveContentRelationshipRole>(
        value,
        "relationship_role"
    );

    private static T? ParseActiveContentEnum<T>(string? value, string argumentName)
        where T : struct, Enum
    {
        if (value is null)
        {
            return null;
        }
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (ToSnakeCase(candidate.ToString()) == value)
            {
                return candidate;
            }
        }
        throw new NativeToolException(
            "INVALID_INPUT",
            $"{argumentName} is not a supported active-content value"
        );
    }

    private sealed record ActiveContentSelection(
        WordActiveContentDeclaration[] Declarations,
        WordActiveXControlDefinition[] Controls,
        WordActiveContentPayload[] Payloads,
        WordActiveContentRelationship[] Relationships,
        WordActiveContentIssue[] Issues
    );

    private sealed record ActiveContentInspectionPage(
        object[] Items,
        int MatchedCount
    );
}
