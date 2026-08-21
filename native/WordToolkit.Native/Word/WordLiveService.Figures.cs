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
        var includeGeometry = arguments.Boolean("include_geometry", false);
        var includeShapeDetails = arguments.Boolean("include_shape_details", false);
        if (includeGeometry && (view != "representations" || detail != "declared"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "include_geometry requires view=representations and detail=declared"
            );
        }
        if (includeGeometry && maximum > 2)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "include_geometry requires max_items between 1 and 2"
            );
        }
        if (includeShapeDetails && (view != "representations" || detail != "declared"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "include_shape_details requires view=representations and detail=declared"
            );
        }
        if (includeShapeDetails && maximum > 2)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "include_shape_details requires max_items between 1 and 2"
            );
        }

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
                includeGeometry,
                includeShapeDetails,
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
                geometry_included = includeGeometry,
                shape_details_included = includeShapeDetails,
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
            ["include_geometry"] = JsonValueKind.True,
            ["include_shape_details"] = JsonValueKind.True,
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
        bool includeGeometry,
        bool includeShapeDetails,
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
                    RepresentationItem(
                        figure,
                        item,
                        detail,
                        includeText,
                        includeSource,
                        includeGeometry,
                        includeShapeDetails
                    )
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
        bool includeSource,
        bool includeGeometry,
        bool includeShapeDetails
    )
    {
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["figure_id"] = figure.Id,
            ["representation_id"] = representation.Id,
            ["representation_kind"] = ToSnakeCase(representation.Kind.ToString()),
            ["object_kind"] = ToSnakeCase(representation.ObjectKind.ToString()),
            ["in_deleted_content"] = representation.IsInDeletedContent,
            ["text_redacted"] = !includeText
                && (
                    representation.Accessibility.Name is not null
                    || representation.Accessibility.Title is not null
                    || representation.Accessibility.Description is not null
                ),
            ["decorative"] = representation.Accessibility.Decorative,
            ["resource_count"] = representation.Resources.Count,
        };
        AddNonNull(item, "alternate_content_branch", representation.AlternateContentBranch);
        if (detail == "declared")
        {
            AddNonNull(item, "width_emu", representation.Placement.WidthEmu);
            AddNonNull(item, "height_emu", representation.Placement.HeightEmu);
            AddNonNull(item, "wrap_kind", representation.Placement.WrapKind);
            AddNonNull(item, "behind_document", representation.Placement.BehindDocument);
            item["placement"] = FigurePlacementItem(
                representation.Placement,
                includeGeometry,
                includeSource
            );
            if (representation.ShapeModel is { } shapeModel)
            {
                item["shape_node_count"] = shapeModel.NodeCount;
                item["shape_group_count"] = shapeModel.GroupCount;
                item["shape_path_count"] = shapeModel.PathCount;
                item["shape_path_command_count"] = shapeModel.PathCommandCount;
                item["shape_effect_count"] = shapeModel.EffectCount;
                item["shape_text_character_count"] = shapeModel.TextCharacterCount;
                if (includeShapeDetails)
                {
                    item["shape_model"] = ShapeModelItem(
                        shapeModel,
                        includeText,
                        includeSource,
                        includeGeometry
                    );
                }
            }
            AddNonNull(item, "hidden", representation.Accessibility.Hidden);
            item["unmodeled_payload_element_count"] =
                representation.UnmodeledPayloadElements.Count;
            if (includeSource)
            {
                AddNonNull(
                    item,
                    "graphic_data_uri",
                    BoundForResponse(representation.GraphicDataUri, 512)
                );
                AddNonNull(
                    item,
                    "non_visual_drawing_id",
                    representation.NonVisualDrawingId
                );
            }
        }
        if (includeText)
        {
            AddNonNull(
                item,
                "name",
                BoundForResponse(representation.Accessibility.Name, 1_024)
            );
            AddNonNull(
                item,
                "title",
                BoundForResponse(representation.Accessibility.Title, 1_024)
            );
            AddNonNull(
                item,
                "description",
                BoundForResponse(representation.Accessibility.Description, 2_048)
            );
        }
        if (includeSource)
        {
            item["part_uri"] = BoundForResponse(representation.PartUri, 512);
            item["semantic_node_id"] = representation.SemanticNodeId.Value;
            item["source_path"] = BoundForResponse(representation.SourcePath, 2_048);
            item["source_element_ordinal"] = representation.SourceElementOrdinal;
        }
        return item;
    }

    private static object FigurePlacementItem(
        WordFigurePlacementDefinition placement,
        bool includeGeometry,
        bool includeSource
    )
    {
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = ToSnakeCase(placement.Kind.ToString()),
            ["declared_only_not_rendered_geometry"] = true,
        };
        AddNonNull(item, "width_emu", placement.WidthEmu);
        AddNonNull(item, "height_emu", placement.HeightEmu);
        AddNonNull(item, "distance_top_emu", placement.DistanceTopEmu);
        AddNonNull(item, "distance_bottom_emu", placement.DistanceBottomEmu);
        AddNonNull(item, "distance_left_emu", placement.DistanceLeftEmu);
        AddNonNull(item, "distance_right_emu", placement.DistanceRightEmu);
        AddNonNull(item, "relative_height_order", placement.RelativeHeight);
        AddNonNull(item, "behind_document", placement.BehindDocument);
        AddNonNull(item, "layout_in_cell", placement.LayoutInCell);
        AddNonNull(item, "allow_overlap", placement.AllowOverlap);
        AddNonNull(item, "use_simple_position", placement.UseSimplePosition);
        AddNonNull(item, "locked", placement.Locked);
        AddNonNull(item, "simple_position", PointItem(placement.SimplePosition));
        AddNonNull(item, "effect_extent", EffectExtentItem(placement.EffectExtent));
        AddNonNull(item, "horizontal_position", PositionItem(placement.HorizontalPosition));
        AddNonNull(item, "vertical_position", PositionItem(placement.VerticalPosition));
        AddNonNull(item, "relative_width", RelativeSizeItem(placement.RelativeWidthSize));
        AddNonNull(item, "relative_height", RelativeSizeItem(placement.RelativeHeightSize));
        AddNonNull(item, "wrap", WrapItem(placement.Wrap, includeGeometry));
        AddNonNull(item, "vml", VmlPlacementItem(placement.Vml, includeGeometry, includeSource));
        return item;
    }

    private static object? PointItem(WordFigurePointDefinition? point)
    {
        if (point is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddNonNull(item, "x_emu", point.XEmu);
        AddNonNull(item, "y_emu", point.YEmu);
        return item;
    }

    private static object? EffectExtentItem(WordFigureEffectExtentDefinition? extent)
    {
        if (extent is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddNonNull(item, "left_emu", extent.LeftEmu);
        AddNonNull(item, "top_emu", extent.TopEmu);
        AddNonNull(item, "right_emu", extent.RightEmu);
        AddNonNull(item, "bottom_emu", extent.BottomEmu);
        return item;
    }

    private static object? PositionItem(WordFigurePositionDefinition? position)
    {
        if (position is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddNonNull(item, "relative_from", position.RelativeFrom);
        AddNonNull(item, "alignment", position.Alignment);
        AddNonNull(item, "offset_emu", position.OffsetEmu);
        return item;
    }

    private static object? RelativeSizeItem(WordFigureRelativeSizeDefinition? size)
    {
        if (size is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddNonNull(item, "relative_from", size.RelativeFrom);
        AddNonNull(
            item,
            "percentage_thousandths_of_percent",
            size.PercentageThousandthsOfPercent
        );
        return item;
    }

    private static object? WrapItem(WordFigureWrapDefinition? wrap, bool includeGeometry)
    {
        if (wrap is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = wrap.Kind,
            ["polygon_line_point_count"] = wrap.PolygonLinePoints.Count,
        };
        AddNonNull(item, "text_side", wrap.TextSide);
        AddNonNull(item, "distance_top_emu", wrap.DistanceTopEmu);
        AddNonNull(item, "distance_bottom_emu", wrap.DistanceBottomEmu);
        AddNonNull(item, "distance_left_emu", wrap.DistanceLeftEmu);
        AddNonNull(item, "distance_right_emu", wrap.DistanceRightEmu);
        AddNonNull(item, "polygon_edited", wrap.PolygonEdited);
        if (includeGeometry)
        {
            AddNonNull(item, "polygon_start", PointItem(wrap.PolygonStart));
            item["polygon_line_points"] = wrap.PolygonLinePoints.Take(128)
                .Select(PointItem)
                .ToArray();
            item["polygon_line_points_truncated"] = wrap.PolygonLinePoints.Count > 128;
        }
        return item;
    }

    private static object? VmlPlacementItem(
        WordVmlPlacementDefinition? vml,
        bool includeGeometry,
        bool includeSource
    )
    {
        if (vml is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["wrap_coordinate_count"] = vml.WrapCoordinates.Count,
        };
        AddNonNull(item, "position_mode", vml.PositionMode);
        AddNonNull(item, "left", LengthItem(vml.Left, includeSource));
        AddNonNull(item, "top", LengthItem(vml.Top, includeSource));
        AddNonNull(item, "margin_left", LengthItem(vml.MarginLeft, includeSource));
        AddNonNull(item, "margin_top", LengthItem(vml.MarginTop, includeSource));
        AddNonNull(item, "margin_right", LengthItem(vml.MarginRight, includeSource));
        AddNonNull(item, "margin_bottom", LengthItem(vml.MarginBottom, includeSource));
        AddNonNull(item, "width", LengthItem(vml.Width, includeSource));
        AddNonNull(item, "height", LengthItem(vml.Height, includeSource));
        AddNonNull(item, "z_index", vml.ZIndex);
        AddNonNull(item, "horizontal_position", vml.HorizontalPosition);
        AddNonNull(item, "horizontal_relative_from", vml.HorizontalRelativeFrom);
        AddNonNull(item, "vertical_position", vml.VerticalPosition);
        AddNonNull(item, "vertical_relative_from", vml.VerticalRelativeFrom);
        AddNonNull(item, "left_percentage_tenths", vml.LeftPercentageTenths);
        AddNonNull(item, "top_percentage_tenths", vml.TopPercentageTenths);
        AddNonNull(item, "wrap_mode", vml.WrapMode);
        AddNonNull(item, "wrap_edited", vml.WrapEdited);
        AddNonNull(item, "wrap_distance_top", LengthItem(vml.WrapDistanceTop, includeSource));
        AddNonNull(item, "wrap_distance_bottom", LengthItem(vml.WrapDistanceBottom, includeSource));
        AddNonNull(item, "wrap_distance_left", LengthItem(vml.WrapDistanceLeft, includeSource));
        AddNonNull(item, "wrap_distance_right", LengthItem(vml.WrapDistanceRight, includeSource));
        AddNonNull(item, "visibility", vml.Visibility);
        if (vml.SourceTruncated)
        {
            item["source_truncated"] = true;
        }
        if (includeGeometry)
        {
            item["wrap_coordinates"] = vml.WrapCoordinates.Take(128)
                .Select(point => new { x = point.X, y = point.Y })
                .ToArray();
            item["wrap_coordinates_truncated"] = vml.WrapCoordinates.Count > 128;
        }
        return item;
    }

    private static object? LengthItem(WordFigureLengthDefinition? length, bool includeSource)
    {
        if (length is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source_unit_recognized"] = length.Emu is not null,
        };
        AddNonNull(item, "emu", length.Emu);
        if (includeSource)
        {
            item["lexical_value"] = length.LexicalValue;
        }
        return item;
    }

    private static object ShapeModelItem(
        WordFigureShapeModelDefinition model,
        bool includeText,
        bool includeSource,
        bool includeGeometry
    )
    {
        var budget = new ShapeResponseBudget();
        var nodes = new List<object>();
        foreach (var node in FlattenShapeNodes(model.Roots))
        {
            if (budget.NodesRemaining == 0)
            {
                budget.NodesTruncated = true;
                break;
            }
            budget.NodesRemaining--;
            nodes.Add(ShapeNodeItem(
                node,
                includeText,
                includeSource,
                includeGeometry,
                budget
            ));
        }
        return new
        {
            declared_only_not_rendered_geometry = true,
            root_count = model.Roots.Count,
            node_count = model.NodeCount,
            group_count = model.GroupCount,
            shape_count = model.ShapeCount,
            picture_count = model.PictureCount,
            path_count = model.PathCount,
            path_command_count = model.PathCommandCount,
            path_point_count = model.PathPointCount,
            effect_count = model.EffectCount,
            text_character_count = model.TextCharacterCount,
            returned_node_count = nodes.Count,
            returned_path_count = budget.PathsReturned,
            returned_path_command_count = budget.CommandsReturned,
            returned_path_point_count = budget.PointsReturned,
            returned_effect_count = budget.EffectsReturned,
            nodes_truncated = budget.NodesTruncated || nodes.Count < model.NodeCount,
            paths_truncated = includeGeometry && budget.PathsReturned < model.PathCount,
            path_commands_truncated = includeGeometry
                && budget.CommandsReturned < model.PathCommandCount,
            path_points_truncated = includeGeometry
                && budget.PointsReturned < model.PathPointCount,
            effects_truncated = budget.EffectsReturned < model.EffectCount,
            text_truncated_at_response = budget.TextTruncated,
            formula_values_truncated = budget.FormulasTruncated,
            nodes,
        };
    }

    private static object ShapeNodeItem(
        WordFigureShapeNodeDefinition node,
        bool includeText,
        bool includeSource,
        bool includeGeometry,
        ShapeResponseBudget budget
    )
    {
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["shape_node_id"] = node.Id,
            ["kind"] = ToSnakeCase(node.Kind.ToString()),
            ["depth"] = node.Depth,
            ["child_count"] = node.Children.Count,
            ["name_present"] = node.Name is not null,
            ["name_truncated_at_source"] = node.NameTruncated,
        };
        AddNonNull(item, "parent_shape_node_id", node.ParentId);
        if (includeText)
        {
            AddNonNull(item, "name", BoundForResponse(node.Name, 1_024));
        }
        if (includeSource)
        {
            item["source_element_ordinal"] = node.SourceElementOrdinal;
        }
        AddNonNull(item, "transform", ShapeTransformItem(node.Transform));
        AddNonNull(
            item,
            "geometry",
            ShapeGeometryItem(node.Geometry, includeSource, includeGeometry, budget)
        );
        AddNonNull(item, "fill_kind", node.FillKind);
        AddNonNull(item, "line", ShapeLineItem(node.Line));
        AddNonNull(item, "effects", ShapeEffectsItem(node.Effects, budget));
        AddNonNull(
            item,
            "text_flow",
            ShapeTextFlowItem(node.TextFlow, includeText, budget)
        );
        return item;
    }

    private static object? ShapeTransformItem(WordFigureShapeTransformDefinition? transform)
    {
        if (transform is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddNonNull(item, "offset_x_emu", transform.OffsetXEmu);
        AddNonNull(item, "offset_y_emu", transform.OffsetYEmu);
        AddNonNull(item, "width_emu", transform.WidthEmu);
        AddNonNull(item, "height_emu", transform.HeightEmu);
        AddNonNull(item, "child_offset_x_emu", transform.ChildOffsetXEmu);
        AddNonNull(item, "child_offset_y_emu", transform.ChildOffsetYEmu);
        AddNonNull(item, "child_width_emu", transform.ChildWidthEmu);
        AddNonNull(item, "child_height_emu", transform.ChildHeightEmu);
        AddNonNull(
            item,
            "rotation_sixty_thousandths_of_degree",
            transform.RotationSixtyThousandthsOfDegree
        );
        AddNonNull(item, "flip_horizontal", transform.FlipHorizontal);
        AddNonNull(item, "flip_vertical", transform.FlipVertical);
        return item;
    }

    private static object? ShapeGeometryItem(
        WordFigureShapeGeometryDefinition? geometry,
        bool includeSource,
        bool includeGeometry,
        ShapeResponseBudget budget
    )
    {
        if (geometry is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = ToSnakeCase(geometry.Kind.ToString()),
            ["preset_recognized"] = geometry.PresetRecognized,
            ["adjustment_count"] = geometry.AdjustmentCount,
            ["guide_count"] = geometry.GuideCount,
            ["handle_count"] = geometry.HandleCount,
            ["connection_site_count"] = geometry.ConnectionSiteCount,
            ["has_text_rectangle"] = geometry.HasTextRectangle,
            ["path_count"] = geometry.Paths.Count,
            ["vml_path_character_count"] = geometry.VmlPathCharacterCount,
        };
        AddNonNull(item, "preset", geometry.Preset);
        if (includeSource)
        {
            AddNonNull(item, "vml_path_sha256", geometry.VmlPathSha256);
        }
        if (includeGeometry && geometry.Paths.Count != 0)
        {
            var paths = new List<object>();
            foreach (var path in geometry.Paths)
            {
                if (budget.PathsRemaining == 0)
                {
                    break;
                }
                budget.PathsRemaining--;
                budget.PathsReturned++;
                paths.Add(ShapePathItem(path, budget));
            }
            item["paths"] = paths;
            item["paths_truncated"] = paths.Count < geometry.Paths.Count;
        }
        return item;
    }

    private static object ShapePathItem(
        WordFigureShapePathDefinition path,
        ShapeResponseBudget budget
    )
    {
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["command_count"] = path.Commands.Count,
        };
        AddNonNull(item, "width", path.Width);
        AddNonNull(item, "height", path.Height);
        AddNonNull(item, "fill_mode", path.FillMode);
        AddNonNull(item, "stroke", path.Stroke);
        AddNonNull(item, "extrusion_allowed", path.ExtrusionAllowed);
        var commands = new List<object>();
        foreach (var command in path.Commands)
        {
            if (budget.CommandsRemaining == 0)
            {
                break;
            }
            budget.CommandsRemaining--;
            budget.CommandsReturned++;
            commands.Add(ShapePathCommandItem(command, budget));
        }
        item["commands"] = commands;
        item["commands_truncated"] = commands.Count < path.Commands.Count;
        return item;
    }

    private static object ShapePathCommandItem(
        WordFigureShapePathCommandDefinition command,
        ShapeResponseBudget budget
    )
    {
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = command.Kind,
            ["point_count"] = command.Points.Count,
        };
        AddNonNull(item, "width_radius", ShapeFormulaForResponse(command.WidthRadius, budget));
        AddNonNull(item, "height_radius", ShapeFormulaForResponse(command.HeightRadius, budget));
        AddNonNull(item, "start_angle", ShapeFormulaForResponse(command.StartAngle, budget));
        AddNonNull(item, "sweep_angle", ShapeFormulaForResponse(command.SweepAngle, budget));
        var points = new List<object>();
        foreach (var point in command.Points)
        {
            if (budget.PointsRemaining == 0)
            {
                break;
            }
            budget.PointsRemaining--;
            budget.PointsReturned++;
            var pointItem = new Dictionary<string, object?>(StringComparer.Ordinal);
            AddNonNull(pointItem, "x", ShapeFormulaForResponse(point.X, budget));
            AddNonNull(pointItem, "y", ShapeFormulaForResponse(point.Y, budget));
            points.Add(pointItem);
        }
        item["points"] = points;
        item["points_truncated"] = points.Count < command.Points.Count;
        return item;
    }

    private static object? ShapeLineItem(WordFigureShapeLineDefinition? line)
    {
        if (line is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddNonNull(item, "width_emu", line.WidthEmu);
        AddNonNull(item, "cap", line.Cap);
        AddNonNull(item, "compound", line.Compound);
        AddNonNull(item, "alignment", line.Alignment);
        AddNonNull(item, "fill_kind", line.FillKind);
        AddNonNull(item, "dash", line.Dash);
        AddNonNull(item, "join", line.Join);
        AddNonNull(item, "head_end", line.HeadEnd);
        AddNonNull(item, "tail_end", line.TailEnd);
        return item;
    }

    private static object? ShapeEffectsItem(
        WordFigureShapeEffectsDefinition? effects,
        ShapeResponseBudget budget
    )
    {
        if (effects is null)
        {
            return null;
        }
        var maximum = Math.Min(effects.EffectKinds.Count, budget.EffectsRemaining);
        var kinds = effects.EffectKinds.Take(maximum).ToArray();
        budget.EffectsRemaining -= maximum;
        budget.EffectsReturned += maximum;
        return new
        {
            has_effect_list = effects.HasEffectList,
            has_effect_dag = effects.HasEffectDag,
            effect_count = effects.EffectKinds.Count,
            effect_kinds = kinds,
            effect_kinds_truncated = maximum < effects.EffectKinds.Count,
        };
    }

    private static object? ShapeTextFlowItem(
        WordFigureShapeTextFlowDefinition? text,
        bool includeText,
        ShapeResponseBudget budget
    )
    {
        if (text is null)
        {
            return null;
        }
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["has_text_box_content"] = text.HasTextBoxContent,
            ["paragraph_count"] = text.ParagraphCount,
            ["run_count"] = text.RunCount,
            ["character_count"] = text.CharacterCount,
            ["text_redacted"] = !includeText && text.CharacterCount != 0,
            ["text_truncated_at_source"] = text.TextTruncated,
        };
        if (includeText && text.Text is { } value)
        {
            var take = Math.Min(value.Length, budget.TextCharactersRemaining);
            item["text"] = value[..take];
            budget.TextCharactersRemaining -= take;
            if (take < value.Length)
            {
                budget.TextTruncated = true;
            }
        }
        AddNonNull(item, "anchor", text.Anchor);
        AddNonNull(item, "vertical_flow", text.VerticalFlow);
        AddNonNull(item, "wrap", text.Wrap);
        AddNonNull(item, "left_inset_emu", text.LeftInsetEmu);
        AddNonNull(item, "top_inset_emu", text.TopInsetEmu);
        AddNonNull(item, "right_inset_emu", text.RightInsetEmu);
        AddNonNull(item, "bottom_inset_emu", text.BottomInsetEmu);
        AddNonNull(item, "column_count", text.ColumnCount);
        AddNonNull(item, "column_spacing_emu", text.ColumnSpacingEmu);
        AddNonNull(item, "right_to_left_columns", text.RightToLeftColumns);
        AddNonNull(item, "from_word_art", text.FromWordArt);
        AddNonNull(item, "anchor_center", text.AnchorCenter);
        AddNonNull(item, "force_antialias", text.ForceAntiAlias);
        AddNonNull(item, "upright", text.Upright);
        AddNonNull(item, "compatible_line_spacing", text.CompatibleLineSpacing);
        AddNonNull(
            item,
            "rotation_sixty_thousandths_of_degree",
            text.RotationSixtyThousandthsOfDegree
        );
        AddNonNull(item, "auto_fit_kind", text.AutoFitKind);
        AddNonNull(item, "linked_text_box_id", text.LinkedTextBoxId);
        AddNonNull(item, "linked_text_box_sequence", text.LinkedTextBoxSequence);
        return item;
    }

    private static string? ShapeFormulaForResponse(
        string? value,
        ShapeResponseBudget budget
    )
    {
        if (value is null)
        {
            return null;
        }
        if (value.Length > budget.FormulaCharactersRemaining)
        {
            budget.FormulasTruncated = true;
            return null;
        }
        budget.FormulaCharactersRemaining -= value.Length;
        return value;
    }

    private static IEnumerable<WordFigureShapeNodeDefinition> FlattenShapeNodes(
        IReadOnlyList<WordFigureShapeNodeDefinition> roots
    )
    {
        var stack = new Stack<WordFigureShapeNodeDefinition>();
        for (var index = roots.Count - 1; index >= 0; index--)
        {
            stack.Push(roots[index]);
        }
        while (stack.Count != 0)
        {
            var current = stack.Pop();
            yield return current;
            for (var index = current.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(current.Children[index]);
            }
        }
    }

    private sealed class ShapeResponseBudget
    {
        public int NodesRemaining { get; set; } = 64;
        public int PathsRemaining { get; set; } = 64;
        public int CommandsRemaining { get; set; } = 128;
        public int PointsRemaining { get; set; } = 256;
        public int EffectsRemaining { get; set; } = 128;
        public int TextCharactersRemaining { get; set; } = 4_096;
        public int FormulaCharactersRemaining { get; set; } = 4_096;
        public int PathsReturned { get; set; }
        public int CommandsReturned { get; set; }
        public int PointsReturned { get; set; }
        public int EffectsReturned { get; set; }
        public bool NodesTruncated { get; set; }
        public bool TextTruncated { get; set; }
        public bool FormulasTruncated { get; set; }
    }

    private static void AddNonNull(
        IDictionary<string, object?> item,
        string key,
        object? value
    )
    {
        if (value is not null)
        {
            item[key] = value;
        }
    }

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
