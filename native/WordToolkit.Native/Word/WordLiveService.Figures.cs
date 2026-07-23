using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageFiguresAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateFigureInspectionArguments(arguments);
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (
            view is not "summary"
                and not "figures"
                and not "representations"
                and not "captions"
                and not "associations"
                and not "resources"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, figures, representations, captions, associations, resources, or issues"
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
        var figureId = BoundedOptionalArgument(arguments, "figure_id", 128);
        var captionId = BoundedOptionalArgument(arguments, "caption_id", 128);
        var objectKind = BoundedOptionalArgument(arguments, "object_kind", 32);
        var supportedObjectKinds = Enum.GetValues<WordFigureObjectKind>()
            .Select(value => ToSnakeCase(value.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        if (objectKind is not null && !supportedObjectKinds.Contains(objectKind))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "object_kind must be picture, chart, diagram, shape, ink, content_part, embedded_object, or unknown"
            );
        }
        var includeText = arguments.Boolean("include_text", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeTargets = arguments.Boolean("include_relationship_targets", false);
        var includeIssues = arguments.Boolean("include_issues", false);

        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var graph = new WordFigureCaptionGraphBuilder().Build(
                package,
                cancellationToken
            );
            if (figureId is not null && !graph.TryGetFigure(figureId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The figure does not exist in this package fingerprint"
                );
            }
            if (captionId is not null && !graph.TryGetCaption(captionId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The caption does not exist in this package fingerprint"
                );
            }

            var captionFigureIds = captionId is null
                ? null
                : graph.Associations.Where(item => item.CaptionId == captionId)
                    .Select(item => item.FigureId)
                    .ToHashSet(StringComparer.Ordinal);
            var figures = FilterFigures(
                graph,
                figureId,
                captionFigureIds,
                objectKind
            ).ToArray();
            var figureIds = figures.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var associatedCaptionIds = graph.Associations.Where(item =>
                figureIds.Contains(item.FigureId)
            )
                .Select(item => item.CaptionId)
                .ToHashSet(StringComparer.Ordinal);
            var captions = FilterCaptions(
                graph,
                captionId,
                associatedCaptionIds,
                restrictToFigures: figureId is not null || objectKind is not null
            ).ToArray();
            var captionIds = captions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var associations = graph.Associations.Where(item =>
                figureIds.Contains(item.FigureId) && captionIds.Contains(item.CaptionId)
            ).ToArray();
            var issues = FilterFigureIssues(graph, figureIds, captionIds).ToArray();
            var inspection = FigureInspectionItems(
                view,
                detail,
                figures,
                captions,
                associations,
                issues,
                includeText,
                includeSource,
                includeTargets,
                (int)offset,
                (int)maximum
            );
            var consumed = (long)offset + inspection.Items.Length;
            var resources = figures.SelectMany(item => item.Resources).ToArray();
            var representations = figures.SelectMany(item => item.Representations).ToArray();
            var issuePage = includeIssues && view != "issues"
                ? issues.Take(20).Select(item => FigureIssueItem(item, includeSource)).ToArray()
                : null;

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                figure_count = figures.Length,
                representation_count = representations.Length,
                caption_count = captions.Length,
                selected_association_count = associations.Count(item =>
                    item.Status == WordFigureCaptionAssociationStatus.Selected
                ),
                ambiguous_association_count = associations.Count(item =>
                    item.Status == WordFigureCaptionAssociationStatus.Ambiguous
                ),
                resource_count = resources.Length,
                external_resource_count = resources.Count(item => item.IsExternal),
                unresolved_resource_count = resources.Count(item => !item.IsResolved),
                missing_alt_text_count = representations.Count(item =>
                    !item.Accessibility.HasAlternativeText
                    && !item.Accessibility.Decorative
                ),
                issue_count = issues.Length,
                issues_truncated_at_source = graph.IssuesTruncated,
                association_policy =
                    "evidence_scored_same_story_and_container_unique_best_only",
                execution_policy =
                    "parse_package_only_never_open_word_decode_binary_resources_follow_external_targets_or_execute_active_content",
                word_opened = false,
                binary_resources_decoded = false,
                external_targets_followed = false,
                active_content_executed = false,
                text_included = includeText,
                source_included = includeSource,
                relationship_targets_included = includeTargets,
                view,
                detail,
                figure_id = figureId,
                caption_id = captionId,
                object_kind = objectKind,
                matched_item_count = inspection.MatchedCount,
                offset,
                returned_item_count = inspection.Items.Length,
                next_offset = consumed < inspection.MatchedCount
                    ? (int)consumed
                    : (int?)null,
                items = inspection.Items,
                issues = issuePage,
                issues_truncated = issuePage is not null && issues.Length > issuePage.Length,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    parsed_xml_bytes = graph.ParsedXmlBytes,
                    parsed_xml_elements = graph.ParsedXmlElements,
                },
            });
        }
        catch (WordFigureLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The figure/caption graph exceeds a bounded safety limit",
                new { reason_code = "figure_graph_limit" }
            );
        }
        catch (WordFigureProjectionException)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word figure/caption graph",
                new { reason_code = "figure_projection_failed" }
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
                "The Word package could not be read",
                new { reason_code = "io_read_failed" }
            );
        }
    }

    private static void ValidateFigureInspectionArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException("INVALID_INPUT", "arguments must be an object");
        }
        var allowed = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal)
        {
            ["local_path"] = JsonValueKind.String,
            ["view"] = JsonValueKind.String,
            ["detail"] = JsonValueKind.String,
            ["figure_id"] = JsonValueKind.String,
            ["caption_id"] = JsonValueKind.String,
            ["object_kind"] = JsonValueKind.String,
            ["offset"] = JsonValueKind.Number,
            ["max_items"] = JsonValueKind.Number,
            ["include_text"] = JsonValueKind.True,
            ["include_source"] = JsonValueKind.True,
            ["include_relationship_targets"] = JsonValueKind.True,
            ["include_issues"] = JsonValueKind.True,
        };
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowed.TryGetValue(property.Name, out var expected))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "inspect_ooxml_figures received an unknown argument"
                );
            }
            var validKind = expected == JsonValueKind.True
                ? property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                : property.Value.ValueKind == expected;
            if (!validKind)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{property.Name} has the wrong JSON type"
                );
            }
            if (
                expected == JsonValueKind.Number
                && !property.Value.TryGetInt64(out _)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{property.Name} must be an integer"
                );
            }
        }
        ValidateStableFigureId(arguments, "figure_id", "wdfig_");
        ValidateStableFigureId(arguments, "caption_id", "wdfc_");
    }

    private static void ValidateStableFigureId(
        JsonElement arguments,
        string propertyName,
        string prefix
    )
    {
        if (!arguments.TryGetProperty(propertyName, out var value))
        {
            return;
        }
        var candidate = value.GetString()!;
        if (!Regex.IsMatch(
            candidate,
            $@"\A{Regex.Escape(prefix)}[A-Za-z0-9_-]{{20}}\z",
            RegexOptions.CultureInvariant
        ))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{propertyName} is not a valid stable figure graph identifier"
            );
        }
    }

    private static IEnumerable<WordFigureDefinition> FilterFigures(
        WordFigureCaptionGraph graph,
        string? figureId,
        IReadOnlySet<string>? captionFigureIds,
        string? objectKind
    ) => graph.Figures.Where(figure =>
        (figureId is null || figure.Id == figureId)
        && (captionFigureIds is null || captionFigureIds.Contains(figure.Id))
        && (
            objectKind is null
            || string.Equals(
                ToSnakeCase(figure.ObjectKind.ToString()),
                objectKind,
                StringComparison.Ordinal
            )
        )
    );

    private static IEnumerable<WordCaptionDefinition> FilterCaptions(
        WordFigureCaptionGraph graph,
        string? captionId,
        IReadOnlySet<string> associatedCaptionIds,
        bool restrictToFigures
    ) => graph.Captions.Where(caption =>
        (captionId is null || caption.Id == captionId)
        && (
            !restrictToFigures
            || associatedCaptionIds.Contains(caption.Id)
        )
    );

    private static IEnumerable<WordFigureIssue> FilterFigureIssues(
        WordFigureCaptionGraph graph,
        IReadOnlySet<string> figureIds,
        IReadOnlySet<string> captionIds
    ) => graph.Issues.Where(issue =>
        (issue.FigureId is null || figureIds.Contains(issue.FigureId))
        && (issue.CaptionId is null || captionIds.Contains(issue.CaptionId))
    );

    private static FigureInspectionPage FigureInspectionItems(
        string view,
        string detail,
        IReadOnlyList<WordFigureDefinition> figures,
        IReadOnlyList<WordCaptionDefinition> captions,
        IReadOnlyList<WordFigureCaptionAssociation> associations,
        IReadOnlyList<WordFigureIssue> issues,
        bool includeText,
        bool includeSource,
        bool includeTargets,
        int offset,
        int maximum
    )
    {
        IEnumerable<object> items;
        int matchedCount;
        switch (view)
        {
            case "summary":
                var summary = figures.GroupBy(item => item.ObjectKind)
                    .OrderBy(group => group.Key)
                    .Select(group => (object)new
                    {
                        object_kind = ToSnakeCase(group.Key.ToString()),
                        figure_count = group.Count(),
                        representation_count = group.Sum(item => item.Representations.Count),
                        resource_count = group.Sum(item => item.Resources.Count),
                        captioned_count = group.Count(item => item.SelectedCaptionId is not null),
                    })
                    .ToArray();
                items = summary;
                matchedCount = summary.Length;
                break;
            case "figures":
                items = figures.Select(item => FigureItem(item, detail, includeText, includeSource));
                matchedCount = figures.Count;
                break;
            case "representations":
                items = figures.SelectMany(figure => figure.Representations.Select(item =>
                    RepresentationItem(figure, item, detail, includeText, includeSource)
                ));
                matchedCount = figures.Sum(item => item.Representations.Count);
                break;
            case "captions":
                items = captions.Select(item => CaptionItem(item, includeText, includeSource));
                matchedCount = captions.Count;
                break;
            case "associations":
                items = associations.Select(AssociationItem);
                matchedCount = associations.Count;
                break;
            case "resources":
                items = figures.SelectMany(figure => figure.Resources.Select(item =>
                    ResourceItem(figure, item, detail, includeSource, includeTargets)
                ));
                matchedCount = figures.Sum(item => item.Resources.Count);
                break;
            case "issues":
                items = issues.Select(item => FigureIssueItem(item, includeSource));
                matchedCount = issues.Count;
                break;
            default:
                throw new UnreachableException();
        }
        return new FigureInspectionPage(
            items.Skip(offset).Take(maximum).ToArray(),
            matchedCount
        );
    }

    private static object FigureItem(
        WordFigureDefinition figure,
        string detail,
        bool includeText,
        bool includeSource
    )
    {
        var primary = figure.PrimaryRepresentationId is { } primaryId
            ? figure.Representations.FirstOrDefault(item => item.Id == primaryId)
            : null;
        return new
        {
            figure_id = figure.Id,
            object_kind = ToSnakeCase(figure.ObjectKind.ToString()),
            story_kind = ToSnakeCase(figure.StoryKind.ToString()),
            representation_count = figure.Representations.Count,
            resource_count = figure.Resources.Count,
            selected_caption_id = figure.SelectedCaptionId,
            in_deleted_content = figure.IsInDeletedContent,
            alternate_content = figure.AlternateContentGroupId is not null,
            representation_selection_basis = ToSnakeCase(
                figure.RepresentationSelectionBasis.ToString()
            ),
            primary_representation_id = detail == "declared"
                ? figure.PrimaryRepresentationId
                : null,
            title_present = primary is null
                ? (bool?)null
                : primary.Accessibility.Title is not null,
            description_present = primary is null
                ? (bool?)null
                : primary.Accessibility.Description is not null,
            decorative = primary?.Accessibility.Decorative,
            declared_title_representation_count = figure.Representations.Count(item =>
                item.Accessibility.Title is not null
            ),
            declared_description_representation_count = figure.Representations.Count(item =>
                item.Accessibility.Description is not null
            ),
            declared_decorative_representation_count = figure.Representations.Count(item =>
                item.Accessibility.Decorative
            ),
            title = includeText
                ? BoundForResponse(primary?.Accessibility.Title, 1_024)
                : null,
            description = includeText
                ? BoundForResponse(primary?.Accessibility.Description, 2_048)
                : null,
            text_redacted = !includeText && figure.Representations.Any(item =>
                item.Accessibility.Title is not null || item.Accessibility.Description is not null
            ),
            part_uri = includeSource ? BoundForResponse(figure.PartUri, 512) : null,
            paragraph_node_id = includeSource ? figure.ParagraphNodeId.Value : null,
            story_root_node_id = includeSource ? figure.StoryRootNodeId.Value : null,
            container_node_id = includeSource ? figure.ContainerNodeId.Value : null,
            source_element_ordinal = includeSource ? figure.SourceElementOrdinal : (int?)null,
        };
    }

    private static object RepresentationItem(
        WordFigureDefinition figure,
        WordFigureRepresentationDefinition representation,
        string detail,
        bool includeText,
        bool includeSource
    ) => new
    {
        figure_id = figure.Id,
        representation_id = representation.Id,
        representation_kind = ToSnakeCase(representation.Kind.ToString()),
        object_kind = ToSnakeCase(representation.ObjectKind.ToString()),
        in_deleted_content = representation.IsInDeletedContent,
        alternate_content_branch = representation.AlternateContentBranch,
        graphic_data_uri = detail == "declared" && includeSource
            ? BoundForResponse(representation.GraphicDataUri, 512)
            : null,
        width_emu = detail == "declared" ? representation.Placement.WidthEmu : null,
        height_emu = detail == "declared" ? representation.Placement.HeightEmu : null,
        wrap_kind = detail == "declared" ? representation.Placement.WrapKind : null,
        behind_document = detail == "declared"
            ? representation.Placement.BehindDocument
            : null,
        non_visual_drawing_id = detail == "declared" && includeSource
            ? representation.NonVisualDrawingId
            : null,
        name = includeText
            ? BoundForResponse(representation.Accessibility.Name, 1_024)
            : null,
        title = includeText
            ? BoundForResponse(representation.Accessibility.Title, 1_024)
            : null,
        description = includeText
            ? BoundForResponse(representation.Accessibility.Description, 2_048)
            : null,
        text_redacted = !includeText
            && (
                representation.Accessibility.Name is not null
                || representation.Accessibility.Title is not null
                || representation.Accessibility.Description is not null
            ),
        decorative = representation.Accessibility.Decorative,
        hidden = detail == "declared" ? representation.Accessibility.Hidden : null,
        resource_count = representation.Resources.Count,
        unmodeled_payload_element_count = detail == "declared"
            ? representation.UnmodeledPayloadElements.Count
            : (int?)null,
        part_uri = includeSource ? BoundForResponse(representation.PartUri, 512) : null,
        semantic_node_id = includeSource ? representation.SemanticNodeId.Value : null,
        source_path = includeSource ? BoundForResponse(representation.SourcePath, 2_048) : null,
        source_element_ordinal = includeSource
            ? representation.SourceElementOrdinal
            : (int?)null,
    };

    private static object CaptionItem(
        WordCaptionDefinition caption,
        bool includeText,
        bool includeSource
    ) => new
    {
        caption_id = caption.Id,
        caption_kind = ToSnakeCase(caption.Kind.ToString()),
        primary_label = includeText ? BoundForResponse(caption.PrimaryLabel, 256) : null,
        label_present = caption.PrimaryLabel is not null,
        caption_style_evidence = caption.HasCaptionStyleEvidence,
        in_deleted_content = caption.IsInDeletedContent,
        sequence_field_count = caption.SequenceFieldIds.Count,
        text_character_count = caption.TextCharacterCount,
        sequence_result_character_count = caption.SequenceResultCharacterCount,
        text = includeText ? BoundForResponse(caption.Text, 2_048) : null,
        sequence_result_text = includeText
            ? BoundForResponse(caption.SequenceResultText, 1_024)
            : null,
        text_redacted = !includeText
            && (caption.TextCharacterCount != 0 || caption.SequenceResultCharacterCount != 0),
        selected_figure_id = caption.SelectedFigureId,
        story_kind = ToSnakeCase(caption.StoryKind.ToString()),
        part_uri = includeSource ? BoundForResponse(caption.PartUri, 512) : null,
        paragraph_node_id = includeSource ? caption.ParagraphNodeId.Value : null,
        paragraph_style_id = includeSource
            ? BoundForResponse(caption.ParagraphStyleId, 512)
            : null,
        paragraph_style_name = includeText
            ? BoundForResponse(caption.ParagraphStyleName, 512)
            : null,
        source_path = includeSource ? BoundForResponse(caption.SourcePath, 2_048) : null,
        source_element_ordinal = includeSource ? caption.SourceElementOrdinal : (int?)null,
    };

    private static object AssociationItem(WordFigureCaptionAssociation association) => new
    {
        association_id = association.Id,
        figure_id = association.FigureId,
        caption_id = association.CaptionId,
        status = ToSnakeCase(association.Status.ToString()),
        confidence = ToSnakeCase(association.Confidence.ToString()),
        direction = ToSnakeCase(association.Direction.ToString()),
        paragraph_distance = association.ParagraphDistance,
        score = association.Score,
        same_container = association.SameContainer,
        sequence_evidence = association.HasSequenceEvidence,
        caption_style_evidence = association.HasCaptionStyleEvidence,
        label_compatible = association.LabelCompatible,
    };

    private static object ResourceItem(
        WordFigureDefinition figure,
        WordFigureResourceDefinition resource,
        string detail,
        bool includeSource,
        bool includeTargets
    ) => new
    {
        figure_id = figure.Id,
        resource_id = resource.Id,
        role = ToSnakeCase(resource.Role.ToString()),
        target_mode = resource.TargetMode is null
            ? null
            : ToSnakeCase(resource.TargetMode.Value.ToString()),
        resolved = resource.IsResolved,
        external = resource.IsExternal,
        target_byte_length = resource.TargetByteLength,
        target_content_type = detail == "declared" && includeSource
            ? BoundForResponse(resource.TargetContentType, 512)
            : null,
        target_sha256 = detail == "declared" && includeSource
            ? resource.TargetSha256
            : null,
        relationship_id = includeSource
            ? BoundForResponse(resource.RelationshipId, 128)
            : null,
        relationship_type = detail == "declared" && includeSource
            ? BoundForResponse(resource.RelationshipType, 512)
            : null,
        target = includeTargets ? BoundForResponse(resource.Target, 2_048) : null,
        target_part_uri = includeTargets
            ? BoundForResponse(resource.TargetPartUri, 512)
            : null,
        target_redacted = !includeTargets
            && (resource.Target is not null || resource.TargetPartUri is not null),
        source_element_ordinal = includeSource
            ? resource.SourceElementOrdinal
            : (int?)null,
    };

    private static object FigureIssueItem(
        WordFigureIssue issue,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        figure_id = issue.FigureId,
        caption_id = issue.CaptionId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        relationship_id = includeSource
            ? BoundForResponse(issue.RelationshipId, 128)
            : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private sealed record FigureInspectionPage(object[] Items, int MatchedCount);
}
