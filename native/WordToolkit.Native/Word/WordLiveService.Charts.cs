using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly HashSet<string> SupportedChartTypes =
    [
        "area3DChart",
        "areaChart",
        "bar3DChart",
        "barChart",
        "bubbleChart",
        "doughnutChart",
        "line3DChart",
        "lineChart",
        "ofPieChart",
        "pie3DChart",
        "pieChart",
        "radarChart",
        "scatterChart",
        "stockChart",
        "surface3DChart",
        "surfaceChart",
    ];

    private static Task<object> InspectPackageChartsAsync(
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
                and not "charts"
                and not "series"
                and not "axes"
                and not "relationships"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, charts, series, axes, relationships, or issues"
            );
        }

        var detail = arguments.String("detail", "summary");
        if (detail is not "summary" and not "declared")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail must be summary or declared"
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

        var chartId = BoundedOptionalArgument(arguments, "chart_id", 128);
        var chartType = BoundedOptionalArgument(arguments, "chart_type", 32);
        if (chartType is not null && !SupportedChartTypes.Contains(chartType))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "chart_type is not a supported classic DrawingML chart type"
            );
        }
        var includeSensitive = arguments.Boolean("include_sensitive", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", true);

        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var graph = new WordChartGraphBuilder().Build(package, cancellationToken);
            if (chartId is not null && !graph.TryGetChart(chartId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The chart does not exist in this package fingerprint"
                );
            }

            var matching = ChartInspectionItems(
                graph,
                view,
                detail,
                chartId,
                chartType,
                includeSensitive,
                includeSource,
                (int)offset,
                (int)maximum
            );
            var page = matching.Items;
            var consumed = (long)offset + page.Length;
            var issuePage = includeIssues && view != "issues"
                ? FilterChartIssues(graph, chartId, chartType)
                    .Take(20)
                    .Select(issue => ChartIssueItem(issue, includeSource))
                    .ToArray()
                : null;
            var selectedCharts = FilterCharts(graph, chartId, chartType).ToArray();
            var allSources = selectedCharts.SelectMany(chart => chart.Series)
                .SelectMany(series => series.DataSources)
                .ToArray();
            var relatedParts = selectedCharts.SelectMany(chart => chart.RelatedParts)
                .ToArray();
            var references = graph.References.Where(reference =>
                selectedCharts.Any(chart => chart.PartUri == reference.TargetPartUri)
            ).ToArray();
            var selectedIssues = FilterChartIssues(graph, chartId, chartType).ToArray();

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                chart_reference_count = references.Length,
                unresolved_reference_count = references.Count(reference => !reference.IsResolved),
                chart_count = selectedCharts.Length,
                plot_count = selectedCharts.Sum(chart => chart.Plots.Count),
                series_count = selectedCharts.Sum(chart => chart.Series.Count),
                axis_count = selectedCharts.Sum(chart => chart.Axes.Count),
                data_source_count = allSources.Length,
                cached_point_count = allSources.Sum(source => (long)source.ActualPointCount),
                embedded_package_count = relatedParts.Count(part =>
                    part.Kind == WordChartRelatedPartKind.EmbeddedPackage
                ),
                external_relationship_count = references.Count(reference =>
                    reference.TargetMode == OpcRelationshipTargetMode.External
                ) + relatedParts.Count(part =>
                    part.TargetMode == OpcRelationshipTargetMode.External
                ),
                extended_chart_part_count = graph.UnsupportedExtendedChartPartUris.Count,
                issue_count = selectedIssues.Length,
                issues_truncated_at_source = graph.IssuesTruncated,
                execution_policy =
                    "parse_only_never_open_embedded_packages_or_follow_external_targets",
                word_opened = false,
                embedded_packages_opened = false,
                external_targets_followed = false,
                sensitive_values_included = includeSensitive,
                source_included = includeSource,
                view,
                detail,
                chart_id = chartId,
                chart_type = chartType,
                matched_item_count = matching.MatchedCount,
                offset,
                returned_item_count = page.Length,
                next_offset = consumed < matching.MatchedCount
                    ? (int)consumed
                    : (int?)null,
                items = page,
                issues = issuePage,
                issues_truncated = issuePage is not null
                    && selectedIssues.Length > issuePage.Length,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordChartLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The chart graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordChartProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word chart graph",
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
                "The Word package could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static IReadOnlyList<WordChartDefinition> FilterCharts(
        WordChartGraph graph,
        string? chartId,
        string? chartType
    ) => graph.Charts.Where(chart =>
        (chartId is null || chart.Id == chartId)
        && (chartType is null || chart.Plots.Any(plot => plot.Type == chartType))
    ).ToArray();

    private static IReadOnlyList<WordChartIssue> FilterChartIssues(
        WordChartGraph graph,
        string? chartId,
        string? chartType
    )
    {
        var selected = FilterCharts(graph, chartId, chartType);
        if (chartId is null && chartType is null)
        {
            return graph.Issues;
        }
        var partUris = selected.Select(chart => chart.PartUri).ToHashSet(StringComparer.Ordinal);
        return graph.Issues.Where(issue =>
            issue.ChartPartUri is null || partUris.Contains(issue.ChartPartUri)
        ).ToArray();
    }

    private static ChartInspectionPage ChartInspectionItems(
        WordChartGraph graph,
        string view,
        string detail,
        string? chartId,
        string? chartType,
        bool includeSensitive,
        bool includeSource,
        int offset,
        int maximum
    )
    {
        var charts = FilterCharts(graph, chartId, chartType);
        IEnumerable<object> items;
        int matchedCount;
        switch (view)
        {
            case "summary":
                var summary = charts.SelectMany(chart => chart.Plots)
                .GroupBy(plot => plot.Type, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (object)new
                {
                    chart_type = group.Key,
                    plot_count = group.Count(),
                    series_count = group.Sum(plot => plot.SeriesIds.Count),
                }).ToArray();
                items = summary;
                matchedCount = summary.Length;
                break;
            case "charts":
                items = charts.Select(chart => ChartItem(
                    chart,
                    detail,
                    includeSensitive,
                    includeSource
                ));
                matchedCount = charts.Count;
                break;
            case "series":
                items = charts.SelectMany(chart => chart.Series.Select(series =>
                    SeriesItem(chart, series, detail, includeSensitive, includeSource)
                ));
                matchedCount = charts.Sum(chart => chart.Series.Count);
                break;
            case "axes":
                items = charts.SelectMany(chart => chart.Axes.Select(axis =>
                    AxisItem(chart, axis, detail, includeSource)
                ));
                matchedCount = charts.Sum(chart => chart.Axes.Count);
                break;
            case "relationships":
                items = RelationshipItems(graph, charts, detail, includeSource);
                var chartPartUris = charts.Select(chart => chart.PartUri)
                    .ToHashSet(StringComparer.Ordinal);
                matchedCount = graph.References.Count(reference =>
                    reference.TargetPartUri is not null
                    && chartPartUris.Contains(reference.TargetPartUri)
                ) + charts.Sum(chart => chart.RelatedParts.Count);
                break;
            case "issues":
                var issues = FilterChartIssues(graph, chartId, chartType);
                items = issues.Select(issue => ChartIssueItem(issue, includeSource));
                matchedCount = issues.Count;
                break;
            default:
                throw new UnreachableException();
        }
        return new ChartInspectionPage(
            items.Skip(offset).Take(maximum).ToArray(),
            matchedCount
        );
    }

    private static object ChartItem(
        WordChartDefinition chart,
        string detail,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        chart_id = chart.Id,
        package_reachable = chart.IsPackageReachable,
        incoming_reference_count = chart.IncomingReferenceCount,
        has_title = chart.HasTitle,
        title_character_count = chart.TitleText?.Length ?? 0,
        title_text = includeSensitive ? BoundForResponse(chart.TitleText, 1_024) : null,
        title_text_redacted = chart.TitleText is not null && !includeSensitive,
        title_text_truncated_at_source = chart.TitleTextTruncated,
        plot_types = chart.Plots.Select(plot => plot.Type).Distinct().Order().ToArray(),
        plot_count = chart.Plots.Count,
        series_count = chart.Series.Count,
        axis_count = chart.Axes.Count,
        data_source_count = chart.Series.Sum(series => series.DataSources.Count),
        cached_point_count = chart.Series.SelectMany(series => series.DataSources)
            .Sum(source => (long)source.ActualPointCount),
        external_data_count = chart.ExternalData.Count,
        related_part_count = chart.RelatedParts.Count,
        auto_title_deleted = detail == "declared" ? chart.AutoTitleDeleted : null,
        plot_visible_only = detail == "declared" ? chart.PlotVisibleOnly : null,
        display_blanks_as = detail == "declared"
            ? BoundForResponse(chart.DisplayBlanksAs, 64)
            : null,
        unmodeled_root_element_count = detail == "declared"
            ? chart.UnmodeledRootElements.Count
            : (int?)null,
        unmodeled_plot_area_element_count = detail == "declared"
            ? chart.UnmodeledPlotAreaElements.Count
            : (int?)null,
        part_uri = includeSource ? BoundForResponse(chart.PartUri, 512) : null,
        content_type = includeSource ? BoundForResponse(chart.ContentType, 512) : null,
        namespace_uri = includeSource ? BoundForResponse(chart.NamespaceUri, 512) : null,
        source_element_ordinal = includeSource
            ? chart.SourceElementOrdinal
            : (int?)null,
    };

    private static object SeriesItem(
        WordChartDefinition chart,
        WordChartSeriesDefinition series,
        string detail,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        series_id = series.Id,
        chart_id = chart.Id,
        chart_type = series.ChartType,
        index = series.Index,
        order = series.Order,
        data_source_count = series.DataSources.Count,
        sources = series.DataSources.Select(source => new
        {
            role = source.Role,
            kind = ToSnakeCase(source.Kind.ToString()),
            formula_present = source.Formula is not null,
            formula_character_count = source.Formula?.Length ?? 0,
            formula = includeSensitive ? BoundForResponse(source.Formula, 4_096) : null,
            formula_redacted = source.Formula is not null && !includeSensitive,
            cache_present = source.CachePresent,
            cache_level_count = source.CacheLevelCount,
            declared_point_count = source.DeclaredPointCount,
            cached_point_count = source.ActualPointCount,
            distinct_point_index_count = source.DistinctPointIndexCount,
            maximum_point_index = source.MaximumPointIndex,
            declared_count_matches = source.DeclaredCountMatches,
            duplicate_point_indexes = source.HasDuplicatePointIndexes,
            format_code_present = source.FormatCode is not null,
            format_code_character_count = source.FormatCode?.Length ?? 0,
            format_code = detail == "declared" && includeSensitive
                ? BoundForResponse(source.FormatCode, 256)
                : null,
            format_code_redacted = source.FormatCode is not null
                && !(detail == "declared" && includeSensitive),
            unmodeled_element_count = detail == "declared"
                ? source.UnmodeledElements.Count
                : (int?)null,
            source_element_ordinal = includeSource
                ? source.SourceElementOrdinal
                : (int?)null,
        }).ToArray(),
        unmodeled_element_count = detail == "declared"
            ? series.UnmodeledElements.Count
            : (int?)null,
        chart_part_uri = includeSource ? BoundForResponse(chart.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? series.SourceElementOrdinal
            : (int?)null,
    };

    private static object AxisItem(
        WordChartDefinition chart,
        WordChartAxisDefinition axis,
        string detail,
        bool includeSource
    ) => new
    {
        chart_id = chart.Id,
        axis_id = axis.AxisId,
        kind = axis.Kind,
        position = BoundForResponse(axis.Position, 64),
        cross_axis_id = axis.CrossAxisId,
        deleted = axis.Deleted,
        unmodeled_element_count = detail == "declared"
            ? axis.UnmodeledElements.Count
            : (int?)null,
        chart_part_uri = includeSource ? BoundForResponse(chart.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? axis.SourceElementOrdinal
            : (int?)null,
    };

    private static IEnumerable<object> RelationshipItems(
        WordChartGraph graph,
        IReadOnlyList<WordChartDefinition> charts,
        string detail,
        bool includeSource
    )
    {
        var partUris = charts.Select(chart => chart.PartUri).ToHashSet(StringComparer.Ordinal);
        var chartIdsByPartUri = charts.ToDictionary(
            chart => chart.PartUri,
            chart => chart.Id,
            StringComparer.Ordinal
        );
        var incoming = graph.References.Where(reference =>
            reference.TargetPartUri is not null && partUris.Contains(reference.TargetPartUri)
        ).Select(reference => (object)new
        {
            relationship_kind = "chart_reference",
            chart_id = chartIdsByPartUri[reference.TargetPartUri!],
            related_part_kind = (string?)null,
            target_mode = ToSnakeCase(reference.TargetMode.ToString()),
            resolved = reference.IsResolved,
            used_by_external_data = (bool?)null,
            relationship_id = includeSource
                ? BoundForResponse(reference.RelationshipId, 128)
                : null,
            relationship_type = detail == "declared" && includeSource
                ? BoundForResponse(reference.RelationshipType, 512)
                : null,
            source_part_uri = includeSource
                ? BoundForResponse(reference.SourcePartUri, 512)
                : null,
            target = includeSource ? BoundForResponse(reference.Target, 2_048) : null,
            target_part_uri = includeSource
                ? BoundForResponse(reference.TargetPartUri, 512)
                : null,
            target_content_type = (string?)null,
        });
        var outgoing = charts.SelectMany(chart => chart.RelatedParts.Select(part =>
        {
            var externalData = chart.ExternalData.FirstOrDefault(value =>
                value.RelationshipId == part.RelationshipId
            );
            return (object)new
            {
                relationship_kind = "chart_related_part",
                chart_id = chart.Id,
                related_part_kind = ToSnakeCase(part.Kind.ToString()),
                target_mode = ToSnakeCase(part.TargetMode.ToString()),
                resolved = part.IsResolved,
                used_by_external_data = externalData is not null,
                auto_update = detail == "declared" ? externalData?.AutoUpdate : null,
                relationship_id = includeSource
                    ? BoundForResponse(part.RelationshipId, 128)
                    : null,
                relationship_type = detail == "declared" && includeSource
                    ? BoundForResponse(part.RelationshipType, 512)
                    : null,
                source_part_uri = includeSource
                    ? BoundForResponse(chart.PartUri, 512)
                    : null,
                target = includeSource ? BoundForResponse(part.Target, 2_048) : null,
                target_part_uri = includeSource
                    ? BoundForResponse(part.TargetPartUri, 512)
                    : null,
                target_content_type = includeSource
                    ? BoundForResponse(part.TargetContentType, 512)
                    : null,
            };
        }));
        return incoming.Concat(outgoing);
    }

    private static object ChartIssueItem(WordChartIssue issue, bool includeSource) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        series_id = issue.SeriesId,
        chart_part_uri = includeSource
            ? BoundForResponse(issue.ChartPartUri, 512)
            : null,
        relationship_id = includeSource
            ? BoundForResponse(issue.RelationshipId, 128)
            : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private sealed record ChartInspectionPage(
        object[] Items,
        int MatchedCount
    );
}
