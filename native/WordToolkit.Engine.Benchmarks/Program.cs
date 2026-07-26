using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Rendering;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

var options = Arguments.Parse(args);
var report = options.Scenario switch
{
    "graph" => RunGraph(options),
    "bibliography" => RunBibliography(options),
    "mail-merge" => RunMailMerge(options),
    "bindings" => RunBindings(options),
    "tables" => RunTables(options),
    "figures" => RunFigures(options),
    "mce" => RunMce(options),
    "patch" => RunPatch(options),
    "semantic-html" => RunSemanticHtml(options),
    "semantic-svg" => RunSemanticSvg(options),
    _ => throw new ArgumentException(
        "scenario must be 'graph', 'bibliography', 'mail-merge', 'bindings', 'tables', 'figures', 'mce', 'patch', 'semantic-html', or 'semantic-svg'"
    ),
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
});
Console.WriteLine(json);
if (options.Output is not null)
{
    var path = Path.GetFullPath(options.Output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
}

static object RunGraph(Arguments options)
{
    if (options.TargetNodes is < 100 or > 999_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "graph target must be between 100 and 999000 nodes"
        );
    }
    var paragraphCount = Math.Max(1, (options.TargetNodes - 8) / 3);
    var maxXmlElements = checked(
        (int)Math.Min(int.MaxValue, Math.Max(1_000_000L, options.TargetNodes * 2L))
    );
    var path = TemporaryPath("graph", ".docx");
    try
    {
        var generation = Measure(() => WriteGraphPackage(path, paragraphCount));
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? package = null;
        WordSemanticDocument? semantic = null;
        WordDependencyGraph? graph = null;
        var resourceLease = new WordOperationResourceLease(
            checked((long)options.OperationBudgetMiB * 1024 * 1024)
        );
        var read = Measure(() =>
            package = new OpcPackageReader(
                OpcPackageLimits.Default,
                resourceLease
            ).Read(path)
        );
        var project = Measure(() =>
            semantic = new WordSemanticProjector(
                new WordSemanticProjectionOptions
                {
                    MaxXmlCharacters = 256L * 1024 * 1024,
                    MaxXmlElements = maxXmlElements,
                    MaxTextCharacters = 64L * 1024 * 1024,
                },
                resourceLease
            ).Project(package!)
        );
        var build = Measure(() =>
            graph = new WordDependencyGraphBuilder(
                new WordDependencyGraphOptions
                {
                    MaxAccountedBytes = checked((long)options.GraphBudgetMiB * 1024 * 1024),
                },
                resourceLease
            ).Build(package!, semantic!)
        );
        var final = MemorySnapshot.Capture();
        GC.KeepAlive(graph);
        return CommonReport(
            "dependency_graph",
            new
            {
                requested_nodes = options.TargetNodes,
                paragraphs = paragraphCount,
                package_bytes = new FileInfo(path).Length,
                package_parts = package!.Entries.Count,
                semantic_nodes = semantic!.NodeCount,
                dependency_nodes = graph!.Nodes.Count,
                dependency_edges = graph.Edges.Count,
                issues = graph.Issues.Count,
                resource_usage = new
                {
                    accounting_model = graph.ResourceUsage.AccountingModel,
                    accounted_bytes = graph.ResourceUsage.AccountedBytes,
                    maximum_accounted_bytes = graph.ResourceUsage.MaximumAccountedBytes,
                    adjacency_index_bytes = graph.ResourceUsage.AdjacencyIndexBytes,
                },
                operation_resource_usage = new
                {
                    accounting_model = graph.OperationResourceUsage!.AccountingModel,
                    accounted_bytes = graph.OperationResourceUsage.AccountedBytes,
                    maximum_accounted_bytes = graph.OperationResourceUsage.MaximumAccountedBytes,
                    stages = graph.OperationResourceUsage.Stages.Select(item => new
                    {
                        stage = item.Stage.ToString(),
                        accounted_bytes = item.AccountedBytes,
                    }),
                },
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    semantic_projection = project.TotalMilliseconds,
                    dependency_build = build.TotalMilliseconds,
                    measured_total = read.TotalMilliseconds
                        + project.TotalMilliseconds
                        + build.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
    }
}

static object RunBibliography(Arguments options)
{
    if (options.TargetNodes is < 100 or > 100_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "bibliography target must be between 100 and 100000 sources"
        );
    }
    const int repetitionCount = 7;
    var path = TemporaryPath("bibliography", ".docx");
    try
    {
        var generation = Measure(() => WriteBibliographyPackage(path, options.TargetNodes));
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? package = null;
        WordSemanticDocument? semantic = null;
        WordReferenceGraph? references = null;
        var read = Measure(() => package = new OpcPackageReader().Read(path));
        var project = Measure(() =>
            semantic = new WordSemanticProjector().Project(package!)
        );
        var referenceBuild = Measure(() =>
            references = new WordReferenceGraphBuilder().Build(package!, semantic!)
        );
        WordBibliographyGraph? graph = null;
        WordOperationResourceUsage? operationUsage = null;
        var timings = new double[repetitionCount];
        var allocatedBytes = new long[repetitionCount];
        for (var index = 0; index < repetitionCount; index++)
        {
            var resourceLease = new WordOperationResourceLease(
                checked((long)options.OperationBudgetMiB * 1024 * 1024)
            );
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            timings[index] = Measure(() =>
            {
                graph = new WordBibliographyGraphBuilder(null, resourceLease).Build(
                    package!
                );
            }).TotalMilliseconds;
            allocatedBytes[index] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            operationUsage = resourceLease.Snapshot();
        }
        Collect();
        var final = MemorySnapshot.Capture();
        var measuredGraph = graph ?? throw new InvalidOperationException(
            "Bibliography benchmark did not produce a graph."
        );
        var measuredUsage = operationUsage ?? throw new InvalidOperationException(
            "Bibliography benchmark did not record resource usage."
        );
        var orderedTimings = timings.Order().ToArray();
        var orderedAllocatedBytes = allocatedBytes.Order().ToArray();
        var citationEdges = references!.Edges.Where(edge =>
            edge.TargetKind == WordReferenceTargetKind.Citation
        ).ToArray();
        var resolvedCitations = citationEdges.Count(edge =>
            measuredGraph.TryResolveCitationTag(edge.TargetKey, out _)
        );
        GC.KeepAlive(measuredGraph);
        return CommonReport(
            "bibliography_graph",
            new
            {
                requested_sources = options.TargetNodes,
                repetitions = repetitionCount,
                fixture = "synthetic_unique_book_sources_with_one_person_and_one_matching_citation_each",
                package_bytes = new FileInfo(path).Length,
                package_parts = package!.Entries.Count,
                semantic_nodes = semantic!.NodeCount,
                sources = measuredGraph.Sources.Count,
                contributors = measuredGraph.Sources.Sum(source =>
                    source.Contributors.Count
                ),
                people = measuredGraph.Sources.Sum(source =>
                    source.Contributors.Sum(contributor => contributor.People.Count)
                ),
                citation_fields = citationEdges.Length,
                resolved_citations = resolvedCitations,
                unresolved_citations = citationEdges.Length - resolvedCitations,
                issues = measuredGraph.Issues.Count,
                package_fingerprint_preserved = package.Fingerprint
                    == measuredGraph.PackageFingerprint,
                execution_policy =
                    "parse_package_only_never_open_word_evaluate_fields_execute_xslt_or_follow_external_targets",
                operation_resource_usage = new
                {
                    scope = "bibliography_graph_only_package_semantic_reference_setup_excluded",
                    accounting_model = measuredUsage.AccountingModel,
                    accounted_bytes = measuredUsage.AccountedBytes,
                    maximum_accounted_bytes = measuredUsage.MaximumAccountedBytes,
                    stages = measuredUsage.Stages.Select(item => new
                    {
                        stage = item.Stage.ToString(),
                        accounted_bytes = item.AccountedBytes,
                    }),
                },
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    semantic_projection = project.TotalMilliseconds,
                    reference_build = referenceBuild.TotalMilliseconds,
                    bibliography_build_samples = timings,
                    bibliography_build_median = orderedTimings[repetitionCount / 2],
                    bibliography_build_p95 = orderedTimings[^1],
                    measured_setup_total = read.TotalMilliseconds
                        + project.TotalMilliseconds
                        + referenceBuild.TotalMilliseconds,
                },
                bibliography_build_allocated_bytes = new
                {
                    samples = allocatedBytes,
                    median = orderedAllocatedBytes[repetitionCount / 2],
                    p95 = orderedAllocatedBytes[^1],
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
    }
}

static object RunMailMerge(Arguments options)
{
    if (options.TargetNodes is < 100 or > 250_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "mail-merge target must be between 100 and 250000 recipients"
        );
    }
    const int repetitionCount = 7;
    const int mappingCount = 30;
    var path = TemporaryPath("mail-merge", ".docx");
    try
    {
        var generation = Measure(() => WriteMailMergePackage(
            path,
            options.TargetNodes,
            mappingCount
        ));
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? package = null;
        WordSemanticDocument? semantic = null;
        WordSettingsGraph? settings = null;
        WordReferenceGraph? references = null;
        var read = Measure(() => package = new OpcPackageReader().Read(path));
        var project = Measure(() =>
            semantic = new WordSemanticProjector().Project(package!)
        );
        var settingsBuild = Measure(() =>
            settings = new WordSettingsGraphBuilder().Build(package!, semantic!)
        );
        var referenceBuild = Measure(() =>
            references = new WordReferenceGraphBuilder().Build(package!, semantic!)
        );
        WordMailMergeGraph? graph = null;
        WordOperationResourceUsage? operationUsage = null;
        var timings = new double[repetitionCount];
        var allocatedBytes = new long[repetitionCount];
        for (var index = 0; index < repetitionCount; index++)
        {
            var resourceLease = new WordOperationResourceLease(
                checked((long)options.OperationBudgetMiB * 1024 * 1024)
            );
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            timings[index] = Measure(() =>
            {
                graph = new WordMailMergeGraphBuilder(null, resourceLease).Build(
                    package!,
                    semantic!,
                    settings!,
                    references!
                );
            }).TotalMilliseconds;
            allocatedBytes[index] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            operationUsage = resourceLease.Snapshot();
        }
        var sourceColumns = Enumerable.Range(0, mappingCount)
            .Select(index => new WordMailMergeSourceColumn(
                $"Column{index:D2}",
                index % 2 == 0
                    ? WordMailMergeSourceDataKind.Text
                    : WordMailMergeSourceDataKind.Number
            ))
            .ToArray();
        WordMailMergeSchemaBindingPlan? schemaPlan = null;
        var schemaPlanTimings = new double[repetitionCount];
        var schemaPlanAllocatedBytes = new long[repetitionCount];
        var schemaPlanner = new WordMailMergeSchemaPlanner();
        for (var index = 0; index < repetitionCount; index++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            schemaPlanTimings[index] = Measure(() =>
            {
                schemaPlan = schemaPlanner.Plan(graph!, sourceColumns);
            }).TotalMilliseconds;
            schemaPlanAllocatedBytes[index] = GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;
        }
        Collect();
        var final = MemorySnapshot.Capture();
        var measuredGraph = graph ?? throw new InvalidOperationException(
            "Mail-merge benchmark did not produce a graph."
        );
        var measuredUsage = operationUsage ?? throw new InvalidOperationException(
            "Mail-merge benchmark did not record resource usage."
        );
        var orderedTimings = timings.Order().ToArray();
        var orderedAllocatedBytes = allocatedBytes.Order().ToArray();
        var measuredSchemaPlan = schemaPlan ?? throw new InvalidOperationException(
            "Mail-merge benchmark did not produce a schema-binding plan."
        );
        var orderedSchemaPlanTimings = schemaPlanTimings.Order().ToArray();
        var orderedSchemaPlanAllocatedBytes = schemaPlanAllocatedBytes.Order().ToArray();
        GC.KeepAlive(measuredGraph);
        GC.KeepAlive(measuredSchemaPlan);
        return CommonReport(
            "mail_merge_graph",
            new
            {
                requested_recipients = options.TargetNodes,
                repetitions = repetitionCount,
                fixture = "synthetic_saved_odso_with_30_bound_fields_and_unique_recipient_tags",
                recipient_projection =
                    "bounded_streaming_xml_reader_with_source_ordinals_and_constant_space_stable_ids",
                package_bytes = new FileInfo(path).Length,
                package_parts = package!.Entries.Count,
                semantic_nodes = semantic!.NodeCount,
                mappings = measuredGraph.Mappings.Count,
                recipients = measuredGraph.Recipients.Count,
                included_recipients = measuredGraph.Recipients.Count(item => item.IsIncluded),
                fields = measuredGraph.Fields.Count,
                resolved_fields = measuredGraph.Fields.Count(field =>
                    field.BindingStatus is WordMailMergeFieldBindingStatus.ResolvedBySourceColumnName
                        or WordMailMergeFieldBindingStatus.ResolvedByWordPredefinedName
                ),
                issues = measuredGraph.Issues.Count,
                schema_plan = new
                {
                    source_columns = measuredSchemaPlan.SourceColumns.Count,
                    bindings = measuredSchemaPlan.Bindings.Count,
                    issues = measuredSchemaPlan.Issues.Count,
                    can_bind_schema = measuredSchemaPlan.CanBindSchema,
                    execution_supported = measuredSchemaPlan.ExecutionSupported,
                    contains_record_values = measuredSchemaPlan.ContainsRecordValues,
                    unused_source_columns = measuredSchemaPlan.UnusedSourceColumnCount,
                    plan_id_stable = measuredSchemaPlan.PlanId
                        == schemaPlanner.Plan(measuredGraph, sourceColumns).PlanId,
                },
                package_fingerprint_preserved = package.Fingerprint
                    == measuredGraph.PackageFingerprint,
                execution_policy =
                    "parse_saved_package_only_never_open_word_execute_mail_merge_open_data_source_run_query_or_follow_external_targets",
                operation_resource_usage = new
                {
                    scope = "mail_merge_graph_only_package_semantic_settings_reference_setup_excluded",
                    accounting_model = measuredUsage.AccountingModel,
                    accounted_bytes = measuredUsage.AccountedBytes,
                    maximum_accounted_bytes = measuredUsage.MaximumAccountedBytes,
                    stages = measuredUsage.Stages.Select(item => new
                    {
                        stage = item.Stage.ToString(),
                        accounted_bytes = item.AccountedBytes,
                    }),
                },
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    semantic_projection = project.TotalMilliseconds,
                    settings_build = settingsBuild.TotalMilliseconds,
                    reference_build = referenceBuild.TotalMilliseconds,
                    mail_merge_build_samples = timings,
                    mail_merge_build_median = orderedTimings[repetitionCount / 2],
                    mail_merge_build_p95 = orderedTimings[^1],
                    schema_plan_samples = schemaPlanTimings,
                    schema_plan_median = orderedSchemaPlanTimings[repetitionCount / 2],
                    schema_plan_p95 = orderedSchemaPlanTimings[^1],
                    measured_setup_total = read.TotalMilliseconds
                        + project.TotalMilliseconds
                        + settingsBuild.TotalMilliseconds
                        + referenceBuild.TotalMilliseconds,
                },
                mail_merge_build_allocated_bytes = new
                {
                    samples = allocatedBytes,
                    median = orderedAllocatedBytes[repetitionCount / 2],
                    p95 = orderedAllocatedBytes[^1],
                },
                schema_plan_allocated_bytes = new
                {
                    samples = schemaPlanAllocatedBytes,
                    median = orderedSchemaPlanAllocatedBytes[repetitionCount / 2],
                    p95 = orderedSchemaPlanAllocatedBytes[^1],
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
    }
}

static object RunFigures(Arguments options)
{
    if (options.TargetNodes is < 10 or > 100_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "figure target must be between 10 and the engine ceiling of 100000 logical figures"
        );
    }
    const int repetitionCount = 7;
    var maxXmlElements = checked(
        (int)Math.Min(int.MaxValue, Math.Max(1_000_000L, options.TargetNodes * 45L))
    );
    var path = TemporaryPath("figures", ".docx");
    try
    {
        var generation = Measure(() => WriteFigurePackage(path, options.TargetNodes));
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? package = null;
        WordSemanticDocument? semantic = null;
        WordReferenceGraph? references = null;
        WordStyleGraph? styles = null;
        var read = Measure(() => package = new OpcPackageReader().Read(path));
        var project = Measure(() =>
            semantic = new WordSemanticProjector(
                new WordSemanticProjectionOptions
                {
                    MaxXmlCharacters = 256L * 1024 * 1024,
                    MaxXmlElements = maxXmlElements,
                    MaxTextCharacters = 64L * 1024 * 1024,
                }
            ).Project(package!)
        );
        var referenceBuild = Measure(() =>
            references = new WordReferenceGraphBuilder().Build(package!, semantic!)
        );
        var styleBuild = Measure(() =>
            styles = new WordStyleGraphBuilder().Build(package!, semantic!)
        );
        WordFigureCaptionGraph? graph = null;
        var timings = new double[repetitionCount];
        for (var index = 0; index < repetitionCount; index++)
        {
            timings[index] = Measure(() =>
            {
                graph = new WordFigureCaptionGraphBuilder().Build(
                    package!,
                    semantic!,
                    references!,
                    styles!
                );
            }).TotalMilliseconds;
        }
        Collect();
        var final = MemorySnapshot.Capture();
        var orderedTimings = timings.Order().ToArray();
        var measuredGraph = graph ?? throw new InvalidOperationException(
            "Figure benchmark did not produce a graph."
        );
        return CommonReport(
            "figure_caption_graph",
            new
            {
                requested_figures = options.TargetNodes,
                configured_figure_ceiling = WordFigureCaptionGraphOptions.Default.MaxFigures,
                configured_caption_ceiling = WordFigureCaptionGraphOptions.Default.MaxCaptions,
                configured_association_ceiling = WordFigureCaptionGraphOptions.Default.MaxAssociations,
                package_bytes = new FileInfo(path).Length,
                package_parts = package!.Entries.Count,
                semantic_nodes = semantic!.NodeCount,
                reference_fields = references!.Fields.Count,
                figures = measuredGraph.Figures.Count,
                representations = measuredGraph.Figures.Sum(item => item.Representations.Count),
                resources = measuredGraph.Figures.Sum(item => item.Resources.Count),
                captions = measuredGraph.Captions.Count,
                associations = measuredGraph.Associations.Count,
                selected_associations = measuredGraph.Associations.Count(item =>
                    item.Status == WordFigureCaptionAssociationStatus.Selected
                ),
                issues = measuredGraph.Issues.Count,
                parsed_xml_bytes = measuredGraph.ParsedXmlBytes,
                parsed_xml_elements = measuredGraph.ParsedXmlElements,
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    semantic_projection = project.TotalMilliseconds,
                    reference_build = referenceBuild.TotalMilliseconds,
                    style_build = styleBuild.TotalMilliseconds,
                    figure_build_samples = timings,
                    figure_build_median = orderedTimings[repetitionCount / 2],
                    figure_build_p95 = orderedTimings[^1],
                    measured_setup_total = read.TotalMilliseconds
                        + project.TotalMilliseconds
                        + referenceBuild.TotalMilliseconds
                        + styleBuild.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
    }
}

static object RunSemanticHtml(Arguments options)
{
    if (options.TargetNodes is < 100 or > 300_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "semantic HTML target must be between 100 and 300000 semantic nodes"
        );
    }
    var paragraphCount = Math.Max(1, (options.TargetNodes - 12) / 3);
    var path = TemporaryPath("semantic-html", ".docx");
    var fullOutput = TemporaryPath("semantic-html-full", ".html");
    var targetOutput = TemporaryPath("semantic-html-target", ".html");
    var targetRepeatOutput = TemporaryPath("semantic-html-target-repeat", ".html");
    try
    {
        var generation = Measure(() =>
            WriteSemanticHtmlPackage(path, paragraphCount)
        );
        var package = new OpcPackageReader().Read(path);
        var semantic = new WordSemanticProjector().Project(package);
        var target = semantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Table
        );
        Collect();
        var baseline = MemorySnapshot.Capture();
        var operation = new SemanticHtmlWordPackageOperation();
        SemanticHtmlWordPackageResult? full = null;
        SemanticHtmlWordPackageResult? selected = null;
        SemanticHtmlWordPackageResult? selectedRepeat = null;
        var fullRender = Measure(() =>
            full = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    path,
                    fullOutput,
                    package.Fingerprint
                )
            )
        );
        var selectedRender = Measure(() =>
            selected = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    path,
                    targetOutput,
                    package.Fingerprint,
                    TargetNodeId: target.Id.Value
                )
            )
        );
        var selectedRepeatRender = Measure(() =>
            selectedRepeat = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    path,
                    targetRepeatOutput,
                    package.Fingerprint,
                    TargetNodeId: target.Id.Value
                )
            )
        );
        var final = MemorySnapshot.Capture();
        return CommonReport(
            "semantic_html_subtree",
            new
            {
                requested_semantic_nodes = options.TargetNodes,
                paragraphs = paragraphCount,
                projected_nodes = semantic.NodeCount,
                package_bytes = new FileInfo(path).Length,
                target_kind = target.Kind.ToString(),
                target_subtree_nodes = target.DescendantsAndSelf().Count(),
                full_artifact_bytes = full!.ArtifactBytes,
                selected_artifact_bytes = selected!.ArtifactBytes,
                selected_to_full_artifact_ratio = Math.Round(
                    (double)selected.ArtifactBytes / full.ArtifactBytes,
                    6
                ),
                full_rendered_nodes = full.RenderedNodeCount,
                selected_rendered_nodes = selected.RenderedNodeCount,
                selection_applied = selected.SelectionApplied,
                source_mutated = full.SourceMutated || selected.SourceMutated,
                word_opened = full.WordOpened || selected.WordOpened,
                deterministic_target_sha256 = selected.ArtifactSha256,
                target_repeat_sha256 = selectedRepeat!.ArtifactSha256,
                target_artifact_hashes_equal = string.Equals(
                    selected.ArtifactSha256,
                    selectedRepeat.ArtifactSha256,
                    StringComparison.Ordinal
                ),
                target_artifact_bytes_equal = File.ReadAllBytes(targetOutput)
                    .SequenceEqual(File.ReadAllBytes(targetRepeatOutput)),
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    full_render = fullRender.TotalMilliseconds,
                    selected_render = selectedRender.TotalMilliseconds,
                    selected_repeat_render = selectedRepeatRender.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
        File.Delete(fullOutput);
        File.Delete(targetOutput);
        File.Delete(targetRepeatOutput);
    }
}

static object RunSemanticSvg(Arguments options)
{
    if (options.TargetNodes is < 100 or > 300_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "semantic SVG target must be between 100 and 300000 semantic nodes"
        );
    }
    const int repetitionCount = 7;
    var paragraphCount = Math.Max(1, (options.TargetNodes - 12) / 3);
    var path = TemporaryPath("semantic-svg", ".docx");
    var outputs = Enumerable.Range(0, repetitionCount)
        .Select(index => TemporaryPath($"semantic-svg-{index}", ".svg"))
        .ToArray();
    try
    {
        var generation = Measure(() =>
            WriteSemanticHtmlPackage(path, paragraphCount)
        );
        var sourceBefore = File.ReadAllBytes(path);
        var package = new OpcPackageReader().Read(path);
        var semantic = new WordSemanticProjector().Project(package);
        var target = semantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Table
        );
        Collect();
        var baseline = MemorySnapshot.Capture();
        var operation = new SemanticSvgWordPackageOperation();
        var results = new SemanticSvgWordPackageResult[repetitionCount];
        var timings = new double[repetitionCount];
        for (var index = 0; index < repetitionCount; index++)
        {
            var current = index;
            timings[index] = Measure(() =>
            {
                results[current] = operation.Execute(
                    new SemanticSvgWordPackageRequest(
                        path,
                        outputs[current],
                        package.Fingerprint,
                        target.Id.Value
                    )
                );
            }).TotalMilliseconds;
        }
        var final = MemorySnapshot.Capture();
        var orderedTimings = timings.Order().ToArray();
        var firstBytes = File.ReadAllBytes(outputs[0]);
        return CommonReport(
            "semantic_svg_subtree",
            new
            {
                requested_semantic_nodes = options.TargetNodes,
                paragraphs = paragraphCount,
                projected_nodes = semantic.NodeCount,
                package_bytes = new FileInfo(path).Length,
                target_kind = target.Kind.ToString(),
                target_subtree_nodes = target.DescendantsAndSelf().Count(),
                artifact_bytes = results[0].ArtifactBytes,
                viewport_width_px = results[0].ViewportWidthPx,
                viewport_height_px = results[0].ViewportHeightPx,
                rendered_nodes = results[0].RenderedNodeCount,
                warning_count = results[0].Warnings.Count,
                paginated = results[0].Paginated,
                exact_text_metrics = results[0].ExactTextMetrics,
                pixel_equivalence_claimed = results[0].PixelEquivalenceClaimed,
                external_resources_loaded = results[0].ExternalResourcesLoaded,
                active_content_executed = results[0].ActiveContentExecuted,
                source_mutated = results.Any(result => result.SourceMutated),
                source_bytes_equal = sourceBefore.SequenceEqual(File.ReadAllBytes(path)),
                word_opened = results.Any(result => result.WordOpened),
                repeat_count = repetitionCount,
                deterministic_sha256 = results[0].ArtifactSha256,
                artifact_hashes_equal = results.All(result =>
                    string.Equals(
                        result.ArtifactSha256,
                        results[0].ArtifactSha256,
                        StringComparison.Ordinal
                    )
                ),
                artifact_bytes_equal = outputs.Skip(1).All(output =>
                    firstBytes.SequenceEqual(File.ReadAllBytes(output))
                ),
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    samples = timings,
                    median = orderedTimings[repetitionCount / 2],
                    p95 = orderedTimings[^1],
                    minimum = orderedTimings[0],
                    maximum = orderedTimings[^1],
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
        foreach (var output in outputs)
        {
            File.Delete(output);
        }
    }
}

static object RunPatch(Arguments options)
{
    if (options.PayloadMiB is < 1 or > 200)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.PayloadMiB),
            "patch payload input must be between 1 and 200 MiB per package"
        );
    }
    if (options.Parts is < 1 or > 2_000)
    {
        throw new ArgumentOutOfRangeException(nameof(options.Parts));
    }
    var beforePath = TemporaryPath("patch-before", ".docx");
    var afterPath = TemporaryPath("patch-after", ".docx");
    try
    {
        var bytesPerPackage = checked((long)options.PayloadMiB * 1024 * 1024);
        var expectedPatchPayloadBytes = checked(bytesPerPackage * 2);
        var patchLimits = expectedPatchPayloadBytes <= OpcPackagePatchLimits.Default.MaxPayloadBytes
            ? OpcPackagePatchLimits.Default
            : new OpcPackagePatchLimits
            {
                MaxPayloadBytes = checked(expectedPatchPayloadBytes + 1024 * 1024),
                MaxPayloadBytesPerBlob = OpcPackagePatchLimits.Default.MaxPayloadBytesPerBlob,
                MaxManifestBytes = OpcPackagePatchLimits.Default.MaxManifestBytes,
                MaxCompressionRatio = OpcPackagePatchLimits.Default.MaxCompressionRatio,
            };
        var generation = Measure(() =>
        {
            WritePatchPackage(beforePath, bytesPerPackage, options.Parts, variant: 1);
            WritePatchPackage(afterPath, bytesPerPackage, options.Parts, variant: 2);
        });
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? before = null;
        OpcPackageSnapshot? after = null;
        OpcPackagePatch? patch = null;
        OpcPackagePatch? decoded = null;
        MemoryStream? artifact = null;
        var read = Measure(() =>
        {
            var reader = new OpcPackageReader();
            before = reader.Read(beforePath);
            after = reader.Read(afterPath);
        });
        var create = Measure(() =>
            patch = new OpcPackagePatchBuilder(patchLimits).Create(before!, after!)
        );
        var write = Measure(() =>
        {
            artifact = new MemoryStream();
            new OpcPackagePatchCodec(patchLimits).Write(artifact, patch!);
        });
        var decode = Measure(() =>
        {
            artifact!.Position = 0;
            decoded = new OpcPackagePatchCodec(patchLimits).Read(artifact);
        });
        var final = MemorySnapshot.Capture();
        GC.KeepAlive(decoded);
        return CommonReport(
            "wtpatch_materialization",
            new
            {
                source_payload_mib_per_package = options.PayloadMiB,
                changed_parts = options.Parts,
                configured_max_patch_payload_bytes = patchLimits.MaxPayloadBytes,
                before_package_bytes = new FileInfo(beforePath).Length,
                after_package_bytes = new FileInfo(afterPath).Length,
                patch_operations = patch!.OperationCount,
                patch_payloads = patch.PayloadCount,
                patch_payload_bytes = patch.PayloadBytes,
                artifact_bytes = artifact!.Length,
                decoded_payload_bytes = decoded!.PayloadBytes,
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    patch_create = create.TotalMilliseconds,
                    patch_write = write.TotalMilliseconds,
                    patch_read = decode.TotalMilliseconds,
                    measured_total = read.TotalMilliseconds
                        + create.TotalMilliseconds
                        + write.TotalMilliseconds
                        + decode.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(beforePath);
        File.Delete(afterPath);
    }
}

static object RunBindings(Arguments options)
{
    if (options.TargetNodes is < 100 or > 100_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "binding target must be between 100 and the engine ceiling of 100000 controls"
        );
    }
    var path = TemporaryPath("bindings", ".docx");
    try
    {
        var generation = Measure(() => WriteBindingPackage(path, options.TargetNodes));
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? package = null;
        WordSemanticDocument? semantic = null;
        WordContentControlBindingGraph? graph = null;
        var read = Measure(() => package = new OpcPackageReader().Read(path));
        var project = Measure(() =>
            semantic = new WordSemanticProjector(
                new WordSemanticProjectionOptions
                {
                    MaxXmlCharacters = 256L * 1024 * 1024,
                    MaxXmlElements = 1_000_000,
                    MaxTextCharacters = 64L * 1024 * 1024,
                }
            ).Project(package!)
        );
        var bindingOptions = new WordContentControlBindingGraphOptions
        {
            // This scale fixture deliberately spends metadata on one distinct positional
            // XPath per control. Keep the production control/binding ceilings intact while
            // making the independent metadata budget explicit in the report.
            MaxMetadataCharacters = 64L * 1024 * 1024,
        };
        var build = Measure(() =>
            graph = new WordContentControlBindingGraphBuilder(bindingOptions).Build(
                package!,
                semantic!
            )
        );
        var final = MemorySnapshot.Capture();
        GC.KeepAlive(graph);
        return CommonReport(
            "content_control_binding_graph",
            new
            {
                requested_controls_and_bindings = options.TargetNodes,
                configured_control_ceiling = bindingOptions.MaxControls,
                configured_binding_ceiling = bindingOptions.MaxBindings,
                configured_target_ceiling = bindingOptions.MaxTargets,
                configured_metadata_character_ceiling = bindingOptions.MaxMetadataCharacters,
                production_metadata_character_ceiling = WordContentControlBindingGraphOptions.Default.MaxMetadataCharacters,
                package_bytes = new FileInfo(path).Length,
                package_parts = package!.Entries.Count,
                semantic_nodes = semantic!.NodeCount,
                controls = graph!.Controls.Count,
                stores = graph.Stores.Count,
                bindings = graph.Bindings.Count,
                targets = graph.Targets.Count,
                repeating_sections = graph.RepeatingSections.Count,
                issues = graph.Issues.Count,
                all_bindings_resolved = graph.Bindings.All(binding =>
                    binding.Status == WordBindingResolutionStatus.Resolved
                ),
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    semantic_projection = project.TotalMilliseconds,
                    binding_graph_build = build.TotalMilliseconds,
                    measured_total = read.TotalMilliseconds
                        + project.TotalMilliseconds
                        + build.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
    }
}

static object RunTables(Arguments options)
{
    if (options.TargetNodes is < 100 or > 1_000_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "table target must be between 100 and 1000000 physical cells"
        );
    }
    const int columns = 20;
    var rows = (options.TargetNodes + columns - 1) / columns;
    var path = TemporaryPath("tables", ".docx");
    try
    {
        var generation = Measure(() =>
            WriteTablePackage(path, options.TargetNodes, columns)
        );
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? package = null;
        WordSemanticDocument? semantic = null;
        WordTableGraph? graph = null;
        var read = Measure(() => package = new OpcPackageReader().Read(path));
        var project = Measure(() =>
            semantic = new WordSemanticProjector(
                new WordSemanticProjectionOptions
                {
                    MaxXmlCharacters = 256L * 1024 * 1024,
                    MaxXmlElements = 2_000_000,
                    MaxTextCharacters = 64L * 1024 * 1024,
                }
            ).Project(package!)
        );
        var build = Measure(() =>
            graph = new WordTableGraphBuilder().Build(package!, semantic!)
        );
        var final = MemorySnapshot.Capture();
        GC.KeepAlive(graph);
        return CommonReport(
            "table_graph",
            new
            {
                requested_physical_cells = options.TargetNodes,
                configured_cell_ceiling = WordTableGraphOptions.Default.MaxCells,
                columns,
                generated_rows = rows,
                package_bytes = new FileInfo(path).Length,
                package_parts = package!.Entries.Count,
                semantic_nodes = semantic!.NodeCount,
                tables = graph!.Tables.Count,
                rows = graph.Rows.Count,
                cells = graph.Cells.Count,
                vertical_merges = graph.VerticalMerges.Count,
                issues = graph.Issues.Count,
                parsed_xml_bytes = graph.ParsedXmlBytes,
                parsed_xml_elements = graph.ParsedXmlElements,
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    semantic_projection = project.TotalMilliseconds,
                    table_graph_build = build.TotalMilliseconds,
                    measured_total = read.TotalMilliseconds
                        + project.TotalMilliseconds
                        + build.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
    }
}

static object RunMce(Arguments options)
{
    if (options.TargetNodes is < 100 or > 999_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "MCE target must be between 100 and 999000 XML elements"
        );
    }
    var paragraphCount = MceParagraphCountForTarget(options.TargetNodes);
    const int maxElements = 1_000_000;
    var path = TemporaryPath("mce", ".docx");
    try
    {
        var generation = Measure(() => WriteMcePackage(path, paragraphCount));
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? package = null;
        WordMarkupCompatibilityGraph? graph = null;
        var read = Measure(() => package = new OpcPackageReader().Read(path));
        var build = Measure(() =>
            graph = new WordMarkupCompatibilityGraphBuilder(
                new WordMarkupCompatibilityGraphOptions
                {
                    MaxElementsPerPart = maxElements,
                    MaxTotalElements = maxElements,
                    MaxAffectedElements = maxElements,
                }
            ).Build(package!)
        );
        var final = MemorySnapshot.Capture();
        GC.KeepAlive(graph);
        return CommonReport(
            "markup_compatibility_graph",
            new
            {
                requested_xml_element_ceiling = options.TargetNodes,
                paragraphs = paragraphCount,
                package_bytes = new FileInfo(path).Length,
                package_parts = package!.Entries.Count,
                parsed_xml_parts = graph!.Parts.Count(part => part.Parsed),
                parsed_xml_bytes = graph.ParsedXmlBytes,
                parsed_xml_elements = graph.ParsedElementCount,
                namespaces = graph.Namespaces.Count,
                rules = graph.Rules.Count,
                alternate_content = graph.AlternateContent.Count,
                affected_elements = graph.AffectedElements.Count,
                output_affecting_elements = graph.AffectedElements.Count(item =>
                    item.AffectsOutput
                ),
                must_understand_mismatches = graph.MustUnderstandMismatches.Count,
                issues = graph.Issues.Count,
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    mce_build = build.TotalMilliseconds,
                    measured_total = read.TotalMilliseconds + build.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
    }
}

static int MceParagraphCountForTarget(int targetElements)
{
    var low = 1;
    var high = Math.Max(1, targetElements / 3);
    while (low < high)
    {
        var candidate = low + ((high - low + 1) / 2);
        if (MceElementCount(candidate) <= targetElements)
        {
            low = candidate;
        }
        else
        {
            high = candidate - 1;
        }
    }
    return low;
}

static long MceElementCount(int paragraphs) =>
    8L
    + (3L * paragraphs)
    + ((paragraphs + 19L) / 20L)
    + (2L * ((paragraphs + 99L) / 100L));

static object CommonReport(string scenario, object measurements)
{
    return new
    {
        schema = "wordtoolkit-engine-benchmark-v1",
        scenario,
        utc = DateTimeOffset.UtcNow,
        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        operating_system = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        engine_version = typeof(WordDependencyGraph).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion,
        processor_count = Environment.ProcessorCount,
        server_gc = System.Runtime.GCSettings.IsServerGC,
        measurements,
    };
}

static object MemoryReport(MemorySnapshot baseline, MemorySnapshot final)
{
    return new
    {
        baseline_managed_bytes = baseline.ManagedBytes,
        final_managed_bytes = final.ManagedBytes,
        retained_managed_delta_bytes = final.ManagedBytes - baseline.ManagedBytes,
        allocated_delta_bytes = final.AllocatedBytes - baseline.AllocatedBytes,
        baseline_working_set_bytes = baseline.WorkingSetBytes,
        final_working_set_bytes = final.WorkingSetBytes,
        process_peak_working_set_bytes = final.PeakWorkingSetBytes,
    };
}

static TimeSpan Measure(Action action)
{
    var watch = Stopwatch.StartNew();
    action();
    watch.Stop();
    return watch.Elapsed;
}

static void Collect()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static string TemporaryPath(string stem, string extension)
{
    return Path.Combine(
        Path.GetTempPath(),
        $"wordtoolkit-{stem}-{Guid.NewGuid():N}{extension}"
    );
}

static void WriteGraphPackage(string path, int paragraphCount)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    var entry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
    writer.Write(PackageFixture.DocumentStart);
    for (var index = 0; index < paragraphCount; index++)
    {
        writer.Write("<w:p w14:paraId=\"");
        writer.Write((index + 1).ToString("X8"));
        writer.Write("\"><w:r><w:t>Item ");
        writer.Write(index);
        writer.Write("</w:t></w:r></w:p>");
    }
    writer.Write(PackageFixture.DocumentEnd);
}

static void WriteBibliographyPackage(string path, int sourceCount)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    WriteTextEntry(
        archive,
        "word/_rels/document.xml.rels",
        PackageFixture.BibliographyDocumentRelationships
    );

    var documentEntry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using (var stream = documentEntry.Open())
    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024))
    {
        writer.Write(PackageFixture.DocumentStart);
        for (var index = 1; index <= sourceCount; index++)
        {
            writer.Write("<w:p><w:fldSimple w:instr=\" CITATION Source");
            writer.Write(index.ToString("D8"));
            writer.Write(" \"><w:r><w:t>[");
            writer.Write(index);
            writer.Write("]</w:t></w:r></w:fldSimple></w:p>");
        }
        writer.Write(PackageFixture.DocumentEnd);
    }

    var sourceEntry = archive.CreateEntry("customXml/item1.xml", CompressionLevel.Fastest);
    using var sourceStream = sourceEntry.Open();
    using var sourceWriter = new StreamWriter(
        sourceStream,
        new UTF8Encoding(false),
        64 * 1024
    );
    sourceWriter.Write("<b:Sources xmlns:b=\"http://schemas.openxmlformats.org/officeDocument/2006/bibliography\" StyleName=\"APA\" Version=\"6\">");
    for (var index = 1; index <= sourceCount; index++)
    {
        sourceWriter.Write("<b:Source><b:Tag>Source");
        sourceWriter.Write(index.ToString("D8"));
        sourceWriter.Write("</b:Tag><b:SourceType>Book</b:SourceType><b:LCID>1033</b:LCID><b:Author><b:NameList><b:Person><b:Last>Author");
        sourceWriter.Write(index.ToString("D8"));
        sourceWriter.Write("</b:Last><b:First>Benchmark</b:First></b:Person></b:NameList></b:Author><b:Title>Synthetic source ");
        sourceWriter.Write(index);
        sourceWriter.Write("</b:Title><b:Year>2026</b:Year></b:Source>");
    }
    sourceWriter.Write("</b:Sources>");
}

static void WriteMailMergePackage(string path, int recipientCount, int mappingCount)
{
    string[] predefinedNames =
    [
        "Unique", "CourtesyTitle", "FirstName", "MiddleName", "LastName", "Suffix",
        "Nickname", "JobTitle", "Company", "Address1", "Address2", "City", "State",
        "PostalCode", "CountryorRegion", "BusinessPhone", "BusinessFax", "HomePhone",
        "HomeFax", "EmailAddress", "WebPage", "SpouseCourtesyTitle",
        "SpouseFirstName", "SpouseMiddleName", "SpouseLastName", "SpouseNickname",
        "RubyFirstName", "RubyLastName", "Address3", "Department",
    ];
    if (mappingCount > predefinedNames.Length)
    {
        throw new ArgumentOutOfRangeException(nameof(mappingCount));
    }

    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(
        archive,
        "[Content_Types].xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
          <Override PartName="/word/recipients.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.mailMergeRecipientData+xml"/>
        </Types>
        """
    );
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    WriteTextEntry(
        archive,
        "word/_rels/document.xml.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdSettings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
        </Relationships>
        """
    );
    WriteTextEntry(
        archive,
        "word/_rels/settings.xml.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdRecipients" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/recipientData" Target="recipients.xml"/>
        </Relationships>
        """
    );

    var documentEntry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using (var stream = documentEntry.Open())
    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024))
    {
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
        for (var index = 0; index < mappingCount; index++)
        {
            writer.Write("<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> MERGEFIELD \"Column");
            writer.Write(index.ToString("D2"));
            writer.Write("\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>Value</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        }
        writer.Write(PackageFixture.DocumentEnd);
    }

    var settingsEntry = archive.CreateEntry("word/settings.xml", CompressionLevel.Fastest);
    using (var stream = settingsEntry.Open())
    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024))
    {
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:mailMerge><w:mainDocumentType w:val=\"formLetters\"/><w:dataType w:val=\"database\"/><w:destination w:val=\"newDocument\"/><w:odso><w:fHdr w:val=\"1\"/>");
        for (var index = 0; index < mappingCount; index++)
        {
            writer.Write("<w:fieldMapData><w:type w:val=\"dbColumn\"/><w:name w:val=\"Column");
            writer.Write(index.ToString("D2"));
            writer.Write("\"/><w:mappedName w:val=\"");
            writer.Write(predefinedNames[index]);
            writer.Write("\"/><w:column w:val=\"");
            writer.Write(index);
            writer.Write("\"/></w:fieldMapData>");
        }
        writer.Write("<w:recipientData r:id=\"rIdRecipients\"/></w:odso></w:mailMerge></w:settings>");
    }

    var recipientEntry = archive.CreateEntry("word/recipients.xml", CompressionLevel.Fastest);
    using var recipientStream = recipientEntry.Open();
    using var recipientWriter = new StreamWriter(
        recipientStream,
        new UTF8Encoding(false),
        64 * 1024
    );
    recipientWriter.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:recipients xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
    for (var index = 0; index < recipientCount; index++)
    {
        recipientWriter.Write("<w:recipientData><w:active w:val=\"1\"/><w:column w:val=\"");
        recipientWriter.Write(index);
        recipientWriter.Write("\"/><w:uniqueTag w:val=\"recipient-");
        recipientWriter.Write(index.ToString("D8"));
        recipientWriter.Write("\"/></w:recipientData>");
    }
    recipientWriter.Write("</w:recipients>");
}

static void WriteFigurePackage(string path, int figureCount)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    var relationshipsEntry = archive.CreateEntry(
        "word/_rels/document.xml.rels",
        CompressionLevel.Fastest
    );
    using (var relationshipsStream = relationshipsEntry.Open())
    using (var relationshipsWriter = new StreamWriter(
        relationshipsStream,
        new UTF8Encoding(false),
        64 * 1024
    ))
    {
        relationshipsWriter.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var index = 0; index < figureCount; index++)
        {
            relationshipsWriter.Write("<Relationship Id=\"rIdImage");
            relationshipsWriter.Write(index + 1);
            relationshipsWriter.Write("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/figure.bin\"/>");
        }
        relationshipsWriter.Write("</Relationships>");
    }
    WriteTextEntry(archive, "word/media/figure.bin", "shared-opaque-benchmark-resource");
    var entry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
    writer.Write(PackageFixture.FigureDocumentStart);
    for (var index = 0; index < figureCount; index++)
    {
        writer.Write("<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"914400\" cy=\"457200\"/><wp:docPr id=\"");
        writer.Write(index + 1);
        writer.Write("\" name=\"Figure ");
        writer.Write(index + 1);
        writer.Write("\" descr=\"Synthetic benchmark figure\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"figure.bin\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"rIdImage");
        writer.Write(index + 1);
        writer.Write("\"/></pic:blipFill><pic:spPr/></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
        writer.Write("<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> SEQ Figure \\* ARABIC </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>");
        writer.Write(index + 1);
        writer.Write("</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
    }
    writer.Write(PackageFixture.DocumentEnd);
}

static void WriteSemanticHtmlPackage(string path, int paragraphCount)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    var entry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
    writer.Write(PackageFixture.DocumentStart);
    for (var index = 0; index < paragraphCount; index++)
    {
        writer.Write("<w:p><w:r><w:t>Unselected paragraph ");
        writer.Write(index);
        writer.Write("</w:t></w:r></w:p>");
    }
    writer.Write("<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Selected table sentinel</w:t></w:r></w:p></w:tc></w:tr></w:tbl>");
    writer.Write(PackageFixture.DocumentEnd);
}

static void WritePatchPackage(
    string path,
    long payloadBytes,
    int parts,
    int variant
)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    WriteTextEntry(
        archive,
        "word/document.xml",
        PackageFixture.DocumentStart + PackageFixture.DocumentEnd
    );
    var basePartBytes = payloadBytes / parts;
    var remainder = payloadBytes % parts;
    var buffer = new byte[64 * 1024];
    for (var part = 0; part < parts; part++)
    {
        var remaining = basePartBytes + (part < remainder ? 1 : 0);
        var entry = archive.CreateEntry(
            $"word/media/benchmark-{part:D5}.bin",
            CompressionLevel.NoCompression
        );
        using var stream = entry.Open();
        long offset = 0;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            FillDeterministic(buffer.AsSpan(0, count), part, offset, variant);
            stream.Write(buffer, 0, count);
            remaining -= count;
            offset += count;
        }
    }
}

static void WriteBindingPackage(string path, int controlCount)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.BindingContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    WriteTextEntry(
        archive,
        "word/_rels/document.xml.rels",
        PackageFixture.BindingDocumentRelationships
    );
    WriteTextEntry(
        archive,
        "customXml/_rels/item1.xml.rels",
        PackageFixture.BindingStoreRelationships
    );
    WriteTextEntry(archive, "customXml/itemProps1.xml", PackageFixture.BindingStoreProperties);

    var documentEntry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using (var stream = documentEntry.Open())
    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024))
    {
        writer.Write(PackageFixture.BindingDocumentStart);
        for (var index = 1; index <= controlCount; index++)
        {
            writer.Write("<w:sdt><w:sdtPr><w:id w:val=\"");
            writer.Write(index);
            writer.Write("\"/><w:text/><w:dataBinding w:storeItemID=\"");
            writer.Write(PackageFixture.BindingStoreItemId);
            writer.Write("\" w:xpath=\"/b:root[1]/b:item[");
            writer.Write(index);
            writer.Write("]\" w:prefixMappings=\"xmlns:b='urn:wordtoolkit:benchmark'\"/>");
            writer.Write("</w:sdtPr><w:sdtContent><w:p/></w:sdtContent></w:sdt>");
        }
        writer.Write(PackageFixture.DocumentEnd);
    }

    var storeEntry = archive.CreateEntry("customXml/item1.xml", CompressionLevel.Fastest);
    using var storeStream = storeEntry.Open();
    using var storeWriter = new StreamWriter(storeStream, new UTF8Encoding(false), 64 * 1024);
    storeWriter.Write("<b:root xmlns:b=\"urn:wordtoolkit:benchmark\">");
    for (var index = 1; index <= controlCount; index++)
    {
        storeWriter.Write("<b:item id=\"");
        storeWriter.Write(index);
        storeWriter.Write("\"/>");
    }
    storeWriter.Write("</b:root>");
}

static void WriteTablePackage(string path, int cellCount, int columns)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    var entry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
    writer.Write(PackageFixture.DocumentStart);
    writer.Write("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>");
    for (var column = 0; column < columns; column++)
    {
        writer.Write("<w:gridCol w:w=\"500\"/>");
    }
    writer.Write("</w:tblGrid>");
    var written = 0;
    var row = 0;
    while (written < cellCount)
    {
        writer.Write(row == 0 ? "<w:tr><w:trPr><w:tblHeader/></w:trPr>" : "<w:tr>");
        for (var column = 0; column < columns && written < cellCount; column++)
        {
            writer.Write("<w:tc>");
            if (column == 0)
            {
                writer.Write(row % 5 == 0
                    ? "<w:tcPr><w:vMerge w:val=\"restart\"/></w:tcPr>"
                    : "<w:tcPr><w:vMerge/></w:tcPr>");
            }
            writer.Write("<w:p/></w:tc>");
            written++;
        }
        writer.Write("</w:tr>");
        row++;
    }
    writer.Write("</w:tbl>");
    writer.Write(PackageFixture.DocumentEnd);
}

static void WriteMcePackage(string path, int paragraphCount)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    var entry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
    writer.Write(PackageFixture.MceDocumentStart);
    for (var index = 0; index < paragraphCount; index++)
    {
        writer.Write("<w:p><w:r><w:t>Item ");
        writer.Write(index);
        writer.Write("</w:t></w:r></w:p>");
        if (index % 20 == 0)
        {
            writer.Write("<w14:future w14:value=\"opaque\"/>");
        }
        if (index % 100 == 0)
        {
            writer.Write("<w14:unwrap><w:p/></w14:unwrap>");
        }
    }
    writer.Write(PackageFixture.MceDocumentEnd);
}

static void FillDeterministic(Span<byte> destination, int part, long offset, int variant)
{
    var state = unchecked((uint)(part * 2654435761U) ^ (uint)offset ^ (uint)variant);
    for (var index = 0; index < destination.Length; index++)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        destination[index] = (byte)state;
    }
}

static void WriteTextEntry(ZipArchive archive, string name, string value)
{
    var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
    writer.Write(value);
}

internal sealed record Arguments(
    string Scenario,
    int TargetNodes,
    int PayloadMiB,
    int Parts,
    int GraphBudgetMiB,
    int OperationBudgetMiB,
    string? Output
)
{
    public static Arguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException(
                "usage: graph --target-nodes N [--graph-budget-mib N] [--operation-budget-mib N] | bibliography --target-nodes N [--operation-budget-mib N] | mail-merge --target-nodes N [--operation-budget-mib N] | bindings --target-nodes N | tables --target-nodes N | figures --target-nodes N | mce --target-nodes N | semantic-html --target-nodes N | semantic-svg --target-nodes N | patch --payload-mib N [--parts N]"
            );
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"invalid argument at position {index}");
            }
            values.Add(args[index][2..], args[index + 1]);
        }
        return new Arguments(
            args[0],
            IntValue(values, "target-nodes", 10_000),
            IntValue(values, "payload-mib", 16),
            IntValue(values, "parts", 16),
            IntValue(values, "graph-budget-mib", 128),
            IntValue(values, "operation-budget-mib", 640),
            values.GetValueOrDefault("output")
        );
    }

    private static int IntValue(
        IReadOnlyDictionary<string, string> values,
        string name,
        int fallback
    )
    {
        return values.TryGetValue(name, out var value)
            && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }
}

internal readonly record struct MemorySnapshot(
    long ManagedBytes,
    long AllocatedBytes,
    long WorkingSetBytes,
    long PeakWorkingSetBytes
)
{
    public static MemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new MemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            GC.GetTotalAllocatedBytes(precise: false),
            process.WorkingSet64,
            process.PeakWorkingSet64
        );
    }
}

internal static class PackageFixture
{
    internal const string BindingStoreItemId =
        "{11111111-2222-3333-4444-555555555555}";

    internal const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="bin" ContentType="application/octet-stream"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    internal const string RootRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    internal const string BindingContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/customXml/itemProps1.xml" ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
        </Types>
        """;

    internal const string BindingDocumentRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdCustom" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>
        </Relationships>
        """;

    internal const string BibliographyDocumentRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdSources" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>
        </Relationships>
        """;

    internal const string BindingStoreRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdProps" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" Target="itemProps1.xml"/>
        </Relationships>
        """;

    internal const string BindingStoreProperties = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <ds:datastoreItem ds:itemID="{11111111-2222-3333-4444-555555555555}"
          xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml">
          <ds:schemaRefs><ds:schemaRef ds:uri="urn:wordtoolkit:benchmark"/></ds:schemaRefs>
        </ds:datastoreItem>
        """;

    internal const string DocumentStart = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"><w:body>
        """;

    internal const string BindingDocumentStart = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
        """;

    internal const string DocumentEnd = "<w:sectPr/></w:body></w:document>";

    internal const string FigureDocumentStart = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document
          xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
          xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
          xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body>
        """;

    internal const string MceDocumentStart = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
          xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
          xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
          mc:Ignorable="w14" mc:ProcessContent="w14:unwrap" mc:MustUnderstand="w14"><w:body>
        """;

    internal const string MceDocumentEnd = """
        <mc:AlternateContent>
          <mc:Choice Requires="w14"><w:p/></mc:Choice>
          <mc:Fallback><w:p/></mc:Fallback>
        </mc:AlternateContent>
        <w:sectPr/></w:body></w:document>
        """;
}
