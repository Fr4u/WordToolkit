using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int MaxDiagramItemProjectionCharacters = 10 * 1_024;
    private static readonly byte[] DiagramFingerprintKey = RandomNumberGenerator.GetBytes(32);

    private Task<object> InspectPackageDiagramsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDiagramArguments(arguments);
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (
            view is not "summary"
                and not "diagrams"
                and not "points"
                and not "connections"
                and not "parts"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, diagrams, points, connections, parts, or issues"
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
        if (maximum is < 1 or > 50)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 50"
            );
        }

        var diagramId = BoundedOptionalArgument(arguments, "diagram_id", 128);
        var pointType = BoundedOptionalArgument(arguments, "point_type", 128);
        var includeKeys = arguments.Boolean("include_keys", false);
        var includeHashes = arguments.Boolean("include_hashes", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", false);
        var resourceLease = _operationResourceLeaseFactory();

        try
        {
            var package = new OpcPackageReader(null, resourceLease).Read(
                path,
                cancellationToken
            );
            var graph = new WordDiagramGraphBuilder(null, resourceLease).Build(
                package,
                cancellationToken
            );
            if (diagramId is not null && !graph.TryGetDiagram(diagramId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "diagram_id does not identify a SmartArt diagram in this package fingerprint"
                );
            }

            var selectedDiagrams = FilterDiagrams(graph, diagramId, pointType);
            var selectedPoints = selectedDiagrams.SelectMany(item => item.Points)
                .Where(item => pointType is null || item.PointType == pointType)
                .ToArray();
            var selectedPointKeys = selectedPoints.Select(item =>
                (item.DiagramId, item.ModelId)
            ).ToHashSet();
            var selectedConnections = selectedDiagrams.SelectMany(item => item.Connections)
                .Where(item =>
                    pointType is null
                    || selectedPointKeys.Contains((item.DiagramId, item.SourceModelId))
                    || selectedPointKeys.Contains((item.DiagramId, item.DestinationModelId))
                )
                .ToArray();
            var selectedIssues = FilterDiagramIssues(
                graph,
                selectedDiagrams,
                diagramId is not null || pointType is not null
            )
                .ToArray();
            var inspection = DiagramInspectionItems(
                graph,
                view,
                selectedDiagrams,
                selectedPoints,
                selectedConnections,
                selectedIssues,
                includeKeys,
                includeHashes,
                includeSource
            );
            var page = PageDiagramItems(
                inspection.Items,
                inspection.MatchedCount,
                (int)offset,
                (int)maximum,
                cancellationToken
            );
            var issueItems = includeIssues && view != "issues"
                ? selectedIssues.Take(10)
                    .Select(issue => DiagramIssueItem(
                        issue,
                        includeKeys,
                        includeHashes,
                        includeSource
                    ))
                    .ToArray()
                : null;
            var partReferences = selectedDiagrams.SelectMany(item => item.PartReferences)
                .ToArray();
            var operationUsage = resourceLease.Snapshot();

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                diagram_part_count = graph.Parts.Count,
                diagram_count = selectedDiagrams.Length,
                point_count = selectedPoints.Length,
                connection_count = selectedConnections.Length,
                part_reference_count = partReferences.Length,
                unresolved_part_reference_count = partReferences.Count(item => !item.IsResolved),
                invalid_point_count = selectedPoints.Count(item => !item.IsStructurallyValid),
                invalid_connection_count = selectedConnections.Count(item =>
                    !item.IsStructurallyValid
                ),
                text_character_count = selectedPoints.Sum(item => (long)item.TextCharacterCount),
                persisted_drawing_part_count = selectedDiagrams.Sum(item =>
                    item.PersistedDrawingPartCount
                ),
                issue_count = selectedIssues.Length,
                issues_truncated_at_source = graph.IssuesTruncated,
                execution_policy =
                    "metadata_only_never_open_word_render_layout_return_point_text_or_raw_xml",
                word_opened = false,
                package_mutated = false,
                layout_executed = false,
                text_values_returned = false,
                raw_xml_included = false,
                keys_included = includeKeys,
                hashes_included = includeHashes,
                source_included = includeSource,
                view,
                diagram_id = diagramId,
                point_type = pointType,
                matched_item_count = inspection.MatchedCount,
                offset,
                returned_item_count = page.Items.Count,
                next_offset = page.NextOffset,
                response_truncated = page.WasTruncated ? true : (bool?)null,
                items = page.Items,
                issues = issueItems,
                issues_truncated = issueItems is not null
                    && selectedIssues.Length > issueItems.Length
                        ? true
                        : (bool?)null,
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
                "The SmartArt inspection exceeded its operation resource budget",
                new
                {
                    reason_code = "operation_resource_budget_exhausted",
                    operation_budget = new
                    {
                        model = "wop1",
                        used = exception.AccountedBytes,
                        maximum = exception.MaximumAccountedBytes,
                    },
                }
            );
        }
        catch (WordDiagramLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The SmartArt graph exceeds a bounded safety limit",
                new { reason_code = "diagram_graph_limit" }
            );
        }
        catch (WordDiagramProjectionException)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a SmartArt graph",
                new { reason_code = "diagram_projection_failed" }
            );
        }
        catch (OpcPackageLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason_code = "opc_package_limit" }
            );
        }
        catch (InvalidDataException)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                new { reason_code = "invalid_opc_package" }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions"
            );
        }
        catch (IOException)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The SmartArt graph could not be read",
                new { reason_code = "diagram_io_failed" }
            );
        }
    }

    private static WordDiagramDefinition[] FilterDiagrams(
        WordDiagramGraph graph,
        string? diagramId,
        string? pointType
    ) => graph.Diagrams.Where(item =>
        (diagramId is null || item.Id == diagramId)
        && (pointType is null || item.Points.Any(point => point.PointType == pointType))
    ).ToArray();

    private static IEnumerable<WordDiagramIssue> FilterDiagramIssues(
        WordDiagramGraph graph,
        IReadOnlyList<WordDiagramDefinition> selectedDiagrams,
        bool filtered
    )
    {
        if (!filtered)
        {
            return graph.Issues;
        }
        var ids = selectedDiagrams.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return graph.Issues.Where(issue =>
            issue.DiagramId is null || ids.Contains(issue.DiagramId)
        );
    }

    private static DiagramInspection DiagramInspectionItems(
        WordDiagramGraph graph,
        string view,
        IReadOnlyList<WordDiagramDefinition> diagrams,
        IReadOnlyList<WordDiagramPoint> points,
        IReadOnlyList<WordDiagramConnection> connections,
        IReadOnlyList<WordDiagramIssue> issues,
        bool includeKeys,
        bool includeHashes,
        bool includeSource
    )
    {
        IEnumerable<object> items;
        int matchedCount;
        switch (view)
        {
            case "summary":
                var summary = diagrams.SelectMany(item => item.PartReferences)
                    .GroupBy(item => item.Kind)
                    .OrderBy(item => item.Key)
                    .Select(item => (object)new
                    {
                        part_kind = ToSnakeCase(item.Key.ToString()),
                        reference_count = item.Count(),
                        resolved_count = item.Count(reference => reference.IsResolved),
                        unresolved_count = item.Count(reference => !reference.IsResolved),
                    })
                    .ToArray();
                items = summary;
                matchedCount = summary.Length;
                break;
            case "diagrams":
                items = diagrams.Select(item => DiagramItem(
                    item,
                    includeKeys,
                    includeHashes,
                    includeSource
                ));
                matchedCount = diagrams.Count;
                break;
            case "points":
                items = points.Select(item => PointItem(
                    item,
                    includeKeys,
                    includeHashes,
                    includeSource
                ));
                matchedCount = points.Count;
                break;
            case "connections":
                items = connections.Select(item => ConnectionItem(
                    item,
                    includeKeys,
                    includeHashes,
                    includeSource
                ));
                matchedCount = connections.Count;
                break;
            case "parts":
                var partsByUri = graph.Parts.ToDictionary(
                    item => item.PartUri,
                    StringComparer.Ordinal
                );
                items = diagrams.SelectMany(diagram => diagram.PartReferences.Select(reference =>
                    DiagramPartItem(
                        diagram,
                        reference,
                        reference.TargetPartUri is not null
                            && partsByUri.TryGetValue(reference.TargetPartUri, out var part)
                                ? part
                                : null,
                        includeHashes,
                        includeSource
                    )
                ));
                matchedCount = diagrams.Sum(item => item.PartReferences.Count);
                break;
            case "issues":
                items = issues.Select(issue => DiagramIssueItem(
                    issue,
                    includeKeys,
                    includeHashes,
                    includeSource
                ));
                matchedCount = issues.Count;
                break;
            default:
                throw new UnreachableException();
        }
        return new DiagramInspection(items, matchedCount);
    }

    private static object DiagramItem(
        WordDiagramDefinition diagram,
        bool includeKeys,
        bool includeHashes,
        bool includeSource
    ) => new
    {
        diagram_id = diagram.Id,
        package_reachable = diagram.IsPackageReachable,
        required_parts_resolved = diagram.RequiredPartsResolved,
        point_count = diagram.Points.Count,
        connection_count = diagram.Connections.Count,
        part_reference_count = diagram.PartReferences.Count,
        persisted_drawing_part_count = diagram.PersistedDrawingPartCount,
        layout_unique_id = includeKeys ? BoundForResponse(diagram.LayoutUniqueId, 512) : null,
        layout_unique_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(diagram.LayoutUniqueId)
            : null,
        quick_style_unique_id = includeKeys
            ? BoundForResponse(diagram.QuickStyleUniqueId, 512)
            : null,
        quick_style_unique_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(diagram.QuickStyleUniqueId)
            : null,
        colors_unique_id = includeKeys ? BoundForResponse(diagram.ColorsUniqueId, 512) : null,
        colors_unique_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(diagram.ColorsUniqueId)
            : null,
        source_part_uri = includeSource
            ? BoundForResponse(diagram.SourcePartUri, 512)
            : null,
        source_element_ordinal = includeSource
            ? diagram.SourceElementOrdinal
            : (int?)null,
    };

    private static object PointItem(
        WordDiagramPoint point,
        bool includeKeys,
        bool includeHashes,
        bool includeSource
    ) => new
    {
        point_id = point.Id,
        diagram_id = point.DiagramId,
        point_type = BoundForResponse(point.PointType, 128),
        model_id = includeKeys ? BoundForResponse(point.ModelId, 512) : null,
        model_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(point.ModelId)
            : null,
        model_id_unique = point.IsModelIdUnique,
        structurally_valid = point.IsStructurallyValid,
        has_text = point.HasText,
        text_character_count = point.TextCharacterCount,
        placeholder = point.IsPlaceholder,
        layout_type_id = includeKeys ? BoundForResponse(point.LayoutTypeId, 512) : null,
        layout_type_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(point.LayoutTypeId)
            : null,
        quick_style_type_id = includeKeys
            ? BoundForResponse(point.QuickStyleTypeId, 512)
            : null,
        quick_style_type_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(point.QuickStyleTypeId)
            : null,
        color_style_type_id = includeKeys
            ? BoundForResponse(point.ColorStyleTypeId, 512)
            : null,
        color_style_type_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(point.ColorStyleTypeId)
            : null,
        presentation_association_id = includeKeys
            ? BoundForResponse(point.PresentationAssociationId, 512)
            : null,
        presentation_association_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(point.PresentationAssociationId)
            : null,
        presentation_name = includeKeys
            ? BoundForResponse(point.PresentationName, 512)
            : null,
        presentation_name_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(point.PresentationName)
            : null,
        presentation_style_label = includeKeys
            ? BoundForResponse(point.PresentationStyleLabel, 512)
            : null,
        presentation_style_label_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(point.PresentationStyleLabel)
            : null,
        part_uri = includeSource ? BoundForResponse(point.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? point.SourceElementOrdinal
            : (int?)null,
    };

    private static object ConnectionItem(
        WordDiagramConnection connection,
        bool includeKeys,
        bool includeHashes,
        bool includeSource
    ) => new
    {
        connection_id = connection.Id,
        diagram_id = connection.DiagramId,
        connection_type = BoundForResponse(connection.ConnectionType, 128),
        model_id = includeKeys ? BoundForResponse(connection.ModelId, 512) : null,
        model_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(connection.ModelId)
            : null,
        source_model_id = includeKeys
            ? BoundForResponse(connection.SourceModelId, 512)
            : null,
        source_model_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(connection.SourceModelId)
            : null,
        destination_model_id = includeKeys
            ? BoundForResponse(connection.DestinationModelId, 512)
            : null,
        destination_model_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(connection.DestinationModelId)
            : null,
        source_order = connection.SourceOrder,
        destination_order = connection.DestinationOrder,
        model_id_unique = connection.IsModelIdUnique,
        source_resolved = connection.SourceResolved,
        destination_resolved = connection.DestinationResolved,
        structurally_valid = connection.IsStructurallyValid,
        part_uri = includeSource ? BoundForResponse(connection.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? connection.SourceElementOrdinal
            : (int?)null,
    };

    private static object DiagramPartItem(
        WordDiagramDefinition diagram,
        WordDiagramPartReference reference,
        WordDiagramPart? part,
        bool includeHashes,
        bool includeSource
    ) => new
    {
        diagram_id = diagram.Id,
        part_kind = ToSnakeCase(reference.Kind.ToString()),
        target_mode = ToSnakeCase(reference.TargetMode.ToString()),
        resolved = reference.IsResolved,
        package_reachable = part?.IsPackageReachable,
        source_sha256 = includeHashes ? part?.SourceSha256 : null,
        relationship_id = includeSource
            ? BoundForResponse(reference.RelationshipId, 128)
            : null,
        relationship_type = includeSource
            ? BoundForResponse(reference.RelationshipType, 512)
            : null,
        target = includeSource ? BoundForResponse(reference.Target, 2_048) : null,
        target_part_uri = includeSource
            ? BoundForResponse(reference.TargetPartUri, 512)
            : null,
        content_type = includeSource ? BoundForResponse(part?.ContentType, 512) : null,
    };

    private static object DiagramIssueItem(
        WordDiagramIssue issue,
        bool includeKeys,
        bool includeHashes,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        diagram_id = issue.DiagramId,
        connection_id = issue.ConnectionId,
        point_model_id = includeKeys ? BoundForResponse(issue.PointId, 512) : null,
        point_model_id_fingerprint = includeHashes
            ? FingerprintDiagramIdentifier(issue.PointId)
            : null,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        relationship_id = includeSource
            ? BoundForResponse(issue.RelationshipId, 128)
            : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static DiagramPage PageDiagramItems(
        IEnumerable<object> items,
        int matchedCount,
        int offset,
        int maximum,
        CancellationToken cancellationToken
    )
    {
        var candidates = items.Skip(offset).Take(maximum).ToArray();
        var page = new List<object>(candidates.Length);
        var characters = 0;
        foreach (var item in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemCharacters = JsonSerializer.Serialize(item, JsonDefaults.Compact).Length;
            if (
                page.Count > 0
                && characters > MaxDiagramItemProjectionCharacters - itemCharacters
            )
            {
                break;
            }
            page.Add(item);
            characters = checked(characters + itemCharacters);
        }
        var next = checked(offset + page.Count);
        var requestedEnd = Math.Min((long)matchedCount, (long)offset + maximum);
        return new DiagramPage(
            page,
            next < matchedCount ? next : null,
            next < requestedEnd
        );
    }

    private static string? FingerprintDiagramIdentifier(string? value) =>
        value is null
            ? null
            : Convert.ToHexString(
                HMACSHA256.HashData(
                    DiagramFingerprintKey,
                    Encoding.UTF8.GetBytes(value)
                )
            ).ToLowerInvariant()[..16];

    private static void ValidateDiagramArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException("INVALID_INPUT", "arguments must be an object");
        }
        var allowed = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal)
        {
            ["local_path"] = JsonValueKind.String,
            ["view"] = JsonValueKind.String,
            ["diagram_id"] = JsonValueKind.String,
            ["point_type"] = JsonValueKind.String,
            ["offset"] = JsonValueKind.Number,
            ["max_items"] = JsonValueKind.Number,
            ["include_keys"] = JsonValueKind.True,
            ["include_hashes"] = JsonValueKind.True,
            ["include_source"] = JsonValueKind.True,
            ["include_issues"] = JsonValueKind.True,
        };
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowed.TryGetValue(property.Name, out var expected))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"unsupported argument: {property.Name}"
                );
            }
            if (
                expected == JsonValueKind.True
                    ? property.Value.ValueKind is not JsonValueKind.True
                        and not JsonValueKind.False
                    : property.Value.ValueKind != expected
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{property.Name} has the wrong JSON type"
                );
            }
        }
    }

    private sealed record DiagramInspection(IEnumerable<object> Items, int MatchedCount);

    private sealed record DiagramPage(
        IReadOnlyList<object> Items,
        int? NextOffset,
        bool WasTruncated
    );
}
