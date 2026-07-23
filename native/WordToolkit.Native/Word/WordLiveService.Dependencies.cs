using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int MaxDependencyTraversalEdges = 20_000;

    private static Task<object> InspectPackageDependenciesAsync(
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
                and not "nodes"
                and not "edges"
                and not "unresolved"
                and not "impact"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, nodes, edges, unresolved, impact, or issues"
            );
        }
        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_items") ?? 30;
        var maximumDepth = arguments.NullableInt64("max_depth") ?? 1;
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
        if (maximumDepth is < 1 or > 4)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_depth must be between 1 and 4"
            );
        }
        var includeKeys = arguments.Boolean("include_keys", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", false);
        var nodeId = BoundedOptionalArgument(arguments, "node_id", 128);
        if (view == "impact" && nodeId is null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "node_id is required for the impact view"
            );
        }
        var direction = arguments.String("direction", "both");
        if (direction is not "incoming" and not "outgoing" and not "both")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "direction must be incoming, outgoing, or both"
            );
        }
        var nodeKind = ParseDependencyNodeKind(
            BoundedOptionalArgument(arguments, "node_kind", 128)
        );
        var edgeKind = ParseDependencyEdgeKind(
            BoundedOptionalArgument(arguments, "edge_kind", 128)
        );

        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordDependencyGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            if (nodeId is not null && !graph.TryGetNode(nodeId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The dependency node does not exist in this package fingerprint"
                );
            }
            var matching = DependencyItems(
                graph,
                view,
                nodeId,
                nodeKind,
                edgeKind,
                direction,
                (int)maximumDepth,
                (int)offset,
                (int)maximum,
                includeKeys,
                includeSource,
                cancellationToken
            );
            var page = matching.Items;
            var consumed = (long)offset + page.Count;
            var issuePage = includeIssues && view != "issues"
                ? graph.Issues.Take(40)
                    .Select(issue => DependencyIssueItem(issue, includeSource))
                    .ToArray()
                : null;
            var unresolvedEdges = graph.Edges.Count(edge => !edge.IsResolved);
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = includeSource
                    ? BoundForResponse(graph.MainPartUri, 512)
                    : null,
                node_count = graph.Nodes.Count,
                edge_count = graph.Edges.Count,
                resolved_edge_count = graph.Edges.Count - unresolvedEdges,
                unresolved_edge_count = unresolvedEdges,
                external_edge_count = graph.Edges.Count(edge => edge.IsExternal),
                package_unreachable_part_count = graph.Nodes.Count(node =>
                    node.Kind == WordDependencyNodeKind.Part
                    && node.IsResolved
                    && !node.IsPackageReachable
                ),
                source_diagnostics = new
                {
                    package = graph.PackageDiagnosticCount,
                    styles = graph.StyleIssueCount,
                    numbering = graph.NumberingIssueCount,
                    references = graph.ReferenceIssueCount,
                    unbound_section_stories = graph.UnboundSectionStoryCount,
                    charts = graph.ChartIssueCount,
                    figures_and_captions = graph.FigureIssueCount,
                    content_controls = graph.ContentControlIssueCount,
                    tables = graph.TableIssueCount,
                },
                coverage = new
                {
                    package_relationships = graph.Coverage.PackageRelationships,
                    semantic_containment = graph.Coverage.SemanticContainment,
                    styles = graph.Coverage.Styles,
                    numbering = graph.Coverage.Numbering,
                    references = graph.Coverage.References,
                    sections = graph.Coverage.Sections,
                    charts = graph.Coverage.Charts,
                    figures_and_captions = graph.Coverage.FiguresAndCaptions,
                    content_controls_and_custom_xml = graph.Coverage
                        .ContentControlsAndCustomXml,
                    tables_and_cell_topology = graph.Coverage
                        .TablesAndCellTopology,
                    explicitly_unmodeled_domains = graph.Coverage
                        .ExplicitlyUnmodeledDomains,
                },
                execution_policy =
                    "parse_only_never_execute_fields_or_follow_external_targets",
                word_opened = false,
                external_targets_followed = false,
                view,
                keys_included = includeKeys,
                source_included = includeSource,
                node_id = nodeId,
                direction = view == "impact" ? direction : null,
                max_depth = view == "impact" ? maximumDepth : (long?)null,
                matched_item_count = matching.MatchedCount,
                offset,
                returned_item_count = page.Count,
                next_offset = consumed < matching.MatchedCount
                    ? (int)consumed
                    : (int?)null,
                items = page,
                issue_count = graph.Issues.Count,
                issues = issuePage,
                issues_truncated = issuePage is not null
                    && graph.Issues.Count > issuePage.Length,
                byte_budget = new
                {
                    model = "wdg1",
                    used = graph.ResourceUsage.AccountedBytes,
                    maximum = graph.ResourceUsage.MaximumAccountedBytes,
                },
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordDependencyLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The dependency graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordDependencyProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word dependency graph",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (Exception exception) when (
            exception is WordStyleLimitException
                or WordNumberingLimitException
                or WordReferenceLimitException
                or WordSectionLimitException
                or WordFigureLimitException
                or WordContentControlLimitException
        )
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "A typed dependency source graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (Exception exception) when (
            exception is WordStyleProjectionException
                or WordNumberingProjectionException
                or WordNumberingResolutionException
                or WordReferenceProjectionException
                or WordSectionProjectionException
                or WordFigureProjectionException
                or WordContentControlProjectionException
        )
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "A typed Word dependency source graph could not be resolved",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
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
                "The Word dependency graph could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static DependencyItemPage DependencyItems(
        WordDependencyGraph graph,
        string view,
        string? nodeId,
        WordDependencyNodeKind? nodeKind,
        WordDependencyEdgeKind? edgeKind,
        string direction,
        int maximumDepth,
        int offset,
        int maximum,
        bool includeKeys,
        bool includeSource,
        CancellationToken cancellationToken
    )
    {
        if (view == "summary")
        {
            var counts = new Dictionary<
                WordDependencyEdgeKind,
                (int Total, int Resolved, int Unresolved, int External)
            >();
            foreach (var edge in graph.Edges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                counts.TryGetValue(edge.Kind, out var count);
                counts[edge.Kind] = (
                    count.Total + 1,
                    count.Resolved + (edge.IsResolved ? 1 : 0),
                    count.Unresolved + (edge.IsResolved ? 0 : 1),
                    count.External + (edge.IsExternal ? 1 : 0)
                );
            }
            return PageDependencyItems(
                counts.OrderBy(item => item.Key),
                offset,
                maximum,
                item => (object)new
                {
                    edge_kind = ToSnakeCase(item.Key.ToString()),
                    count = item.Value.Total,
                    resolved_count = item.Value.Resolved,
                    unresolved_count = item.Value.Unresolved,
                    external_count = item.Value.External,
                },
                cancellationToken
            );
        }
        if (view == "nodes")
        {
            return PageDependencyItems(
                graph.Nodes.Where(node =>
                    (nodeId is null || node.Id == nodeId)
                    && (nodeKind is null || node.Kind == nodeKind)
                ),
                offset,
                maximum,
                node => DependencyNodeItem(
                    graph,
                    node,
                    includeKeys,
                    includeSource
                ),
                cancellationToken
            );
        }
        if (view == "issues")
        {
            return PageDependencyItems(
                graph.Issues,
                offset,
                maximum,
                issue => DependencyIssueItem(issue, includeSource),
                cancellationToken
            );
        }

        IEnumerable<WordDependencyEdge> edges;
        if (view == "impact")
        {
            edges = TraverseDependencyImpact(
                graph,
                nodeId!,
                direction,
                maximumDepth,
                cancellationToken
            );
        }
        else
        {
            edges = graph.Edges;
            if (nodeId is not null)
            {
                edges = direction switch
                {
                    "incoming" => edges.Where(edge => edge.TargetNodeId == nodeId),
                    "outgoing" => edges.Where(edge => edge.SourceNodeId == nodeId),
                    _ => edges.Where(edge =>
                        edge.SourceNodeId == nodeId || edge.TargetNodeId == nodeId
                    ),
                };
            }
        }
        if (view == "unresolved")
        {
            edges = edges.Where(edge => !edge.IsResolved);
        }
        if (edgeKind is not null)
        {
            edges = edges.Where(edge => edge.Kind == edgeKind);
        }
        return PageDependencyItems(
            edges,
            offset,
            maximum,
            edge => DependencyEdgeItem(edge, includeSource),
            cancellationToken
        );
    }

    private static DependencyItemPage PageDependencyItems<T>(
        IEnumerable<T> source,
        int offset,
        int maximum,
        Func<T, object> projector,
        CancellationToken cancellationToken
    )
    {
        var items = new List<object>(maximum);
        var matchedCount = 0;
        foreach (var item in source)
        {
            if ((matchedCount & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (matchedCount >= offset && items.Count < maximum)
            {
                items.Add(projector(item));
            }
            matchedCount++;
        }
        return new DependencyItemPage(items, matchedCount);
    }

    private static IReadOnlyList<WordDependencyEdge> TraverseDependencyImpact(
        WordDependencyGraph graph,
        string startNodeId,
        string direction,
        int maximumDepth,
        CancellationToken cancellationToken
    )
    {
        var visitedNodes = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
        var visitedEdges = new Dictionary<string, WordDependencyEdge>(StringComparer.Ordinal);
        var frontier = new Queue<(string NodeId, int Depth)>();
        frontier.Enqueue((startNodeId, 0));
        while (frontier.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Depth >= maximumDepth)
            {
                continue;
            }
            if (direction is "incoming" or "both")
            {
                VisitDependencyEdges(
                    graph.IncomingView(current.NodeId),
                    current.NodeId,
                    current.Depth + 1,
                    visitedNodes,
                    visitedEdges,
                    frontier,
                    cancellationToken
                );
            }
            if (direction is "outgoing" or "both")
            {
                VisitDependencyEdges(
                    graph.OutgoingView(current.NodeId),
                    current.NodeId,
                    current.Depth + 1,
                    visitedNodes,
                    visitedEdges,
                    frontier,
                    cancellationToken
                );
            }
        }
        return visitedEdges.Values
            .OrderBy(edge => edge.Kind)
            .ThenBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void VisitDependencyEdges(
        WordDependencyEdgeCollection adjacent,
        string currentNodeId,
        int nextDepth,
        HashSet<string> visitedNodes,
        Dictionary<string, WordDependencyEdge> visitedEdges,
        Queue<(string NodeId, int Depth)> frontier,
        CancellationToken cancellationToken
    )
    {
        var scanned = 0;
        foreach (var edge in adjacent)
        {
            if ((scanned++ & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (
                !visitedEdges.ContainsKey(edge.Id)
                && visitedEdges.Count >= MaxDependencyTraversalEdges
            )
            {
                throw new WordDependencyLimitException(
                    $"Impact traversal exceeds the {MaxDependencyTraversalEdges}-edge limit."
                );
            }
            visitedEdges.TryAdd(edge.Id, edge);
            var nextNodeId = edge.SourceNodeId == currentNodeId
                ? edge.TargetNodeId
                : edge.SourceNodeId;
            if (visitedNodes.Add(nextNodeId))
            {
                frontier.Enqueue((nextNodeId, nextDepth));
            }
        }
    }

    private static object DependencyNodeItem(
        WordDependencyGraph graph,
        WordDependencyNode node,
        bool includeKeys,
        bool includeSource
    ) => new
    {
        node_id = node.Id,
        kind = ToSnakeCase(node.Kind.ToString()),
        key = includeKeys ? BoundForResponse(node.Key, 4_096) : null,
        key_redacted = includeKeys ? (bool?)null : true,
        key_character_count = node.Key.Length,
        key_fingerprint = FingerprintSensitiveValue(node.Key),
        resolved = node.IsResolved,
        external = node.IsExternal,
        package_reachable = node.IsPackageReachable,
        semantic_kind = node.SemanticKind is null
            ? null
            : ToSnakeCase(node.SemanticKind.Value.ToString()),
        incoming_edge_count = graph.IncomingView(node.Id).Count,
        outgoing_edge_count = graph.OutgoingView(node.Id).Count,
        part_uri = includeSource ? BoundForResponse(node.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? node.SourceElementOrdinal
            : null,
        semantic_node_id = includeSource ? node.SemanticNodeId?.Value : null,
    };

    private static object DependencyEdgeItem(
        WordDependencyEdge edge,
        bool includeSource
    ) => new
    {
        edge_id = edge.Id,
        kind = ToSnakeCase(edge.Kind.ToString()),
        source_node_id = edge.SourceNodeId,
        target_node_id = edge.TargetNodeId,
        resolved = edge.IsResolved,
        external = edge.IsExternal,
        qualifier = BoundForResponse(edge.Qualifier, 256),
        relationship_id = includeSource
            ? BoundForResponse(edge.RelationshipId, 128)
            : null,
        relationship_type = includeSource
            ? BoundForResponse(edge.RelationshipType, 512)
            : null,
        part_uri = includeSource ? BoundForResponse(edge.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? edge.SourceElementOrdinal
            : null,
    };

    private static object DependencyIssueItem(
        WordDependencyIssue issue,
        bool includeSource
    ) => new
    {
        code = issue.Code,
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        node_id = issue.NodeId,
        edge_id = issue.EdgeId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static WordDependencyNodeKind? ParseDependencyNodeKind(string? value)
    {
        if (value is null)
        {
            return null;
        }
        foreach (var candidate in Enum.GetValues<WordDependencyNodeKind>())
        {
            if (ToSnakeCase(candidate.ToString()) == value)
            {
                return candidate;
            }
        }
        throw new NativeToolException(
            "INVALID_INPUT",
            "node_kind is not a supported dependency node kind"
        );
    }

    private static WordDependencyEdgeKind? ParseDependencyEdgeKind(string? value)
    {
        if (value is null)
        {
            return null;
        }
        foreach (var candidate in Enum.GetValues<WordDependencyEdgeKind>())
        {
            if (ToSnakeCase(candidate.ToString()) == value)
            {
                return candidate;
            }
        }
        throw new NativeToolException(
            "INVALID_INPUT",
            "edge_kind is not a supported dependency edge kind"
        );
    }

    private sealed record DependencyItemPage(
        IReadOnlyList<object> Items,
        int MatchedCount
    );
}
