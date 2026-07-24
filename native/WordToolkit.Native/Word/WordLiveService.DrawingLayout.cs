using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int DrawingRootScanLimit = 10_000;
    private const int DrawingStoryLinkLimit = 1_000;
    private const int DrawingDiagnosticLimit = 100;
    private const int DrawingTextBudgetChars = 4_096;
    private const int DrawingGroupItemLimit = 128;
    private const int DrawingGroupDepthLimit = 16;
    private const int DrawingSmartArtNodeLimit = 128;
    private const int DrawingSmartArtShapeLimit = 256;

    private static readonly HashSet<string> DrawingObjectKinds = new(
        ["all", "floating", "inline", "smartart", "group", "picture", "chart", "ole", "canvas", "other"],
        StringComparer.Ordinal
    );

    private async Task<object> InspectDrawingLayoutAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var objectKind = arguments.String("object_kind");
        objectKind = objectKind.Length == 0 ? "all" : objectKind;
        var storyType = arguments.String("story_type");
        storyType = storyType.Length == 0 ? "all" : storyType;
        var offset = (int)(arguments.NullableInt64("offset") ?? 0);
        var limit = (int)(arguments.NullableInt64("limit") ?? 25);
        var repaginate = arguments.Boolean("repaginate", true);
        var includeGroupItems = arguments.Boolean("include_group_items", false);
        var includeSmartArtNodes = arguments.Boolean("include_smartart_nodes", false);
        var includeText = arguments.Boolean("include_text", false);
        var maxTextChars = (int)(arguments.NullableInt64("max_text_chars") ?? 160);
        var includeScreenPixels = arguments.Boolean("include_screen_pixels", false);

        if (!DrawingObjectKinds.Contains(objectKind))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "object_kind is not a supported drawing layout filter",
                new { supported = DrawingObjectKinds.Order(StringComparer.Ordinal).ToArray() }
            );
        }
        if (
            storyType != "all"
            && !WordStoryTypes.Values.Contains(storyType, StringComparer.Ordinal)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "story_type is not a supported Word story",
                new
                {
                    supported = new[] { "all" }
                        .Concat(WordStoryTypes.Values.Order(StringComparer.Ordinal))
                        .ToArray(),
                }
            );
        }
        if (offset is < 0 or > 1_000_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 1,000,000"
            );
        }
        if (limit is < 1 or > 100)
        {
            throw new NativeToolException("INVALID_INPUT", "limit must be between 1 and 100");
        }
        if (maxTextChars is < 1 or > 512)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_text_chars must be between 1 and 512"
            );
        }
        if (includeScreenPixels && limit > 10)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "include_screen_pixels requires limit at or below 10 because Word must resolve each object against the active viewport",
                new { requested_limit = limit, screen_pixel_limit = 10 }
            );
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                var diagnostics = new DrawingLayoutDiagnostics(DrawingDiagnosticLimit);
                var textBudget = new DrawingTextBudget(
                    includeText ? DrawingTextBudgetChars : 0
                );
                var nestedBudget = new DrawingNestedBudget();
                var items = new List<object>();
                var kindCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var collectionCounts = new SortedDictionary<string, int>(
                    StringComparer.Ordinal
                );
                var scannedRoots = 0;
                var matchedRoots = 0;
                var rootLimitReached = false;
                var storyLinksTruncated = false;
                var repaginationPerformed = false;

                if (repaginate)
                {
                    try
                    {
                        document.Repaginate();
                        repaginationPerformed = true;
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add(
                            "repagination_failed",
                            "document",
                            exception
                        );
                    }
                }

                void Increment(SortedDictionary<string, int> counts, string key)
                {
                    counts[key] = counts.TryGetValue(key, out var current)
                        ? current + 1
                        : 1;
                }

                void ScanCollection(
                    object collectionObject,
                    string collectionKind,
                    string currentStoryType,
                    int storyLinkIndex,
                    bool unavailableIsNormal
                )
                {
                    if (rootLimitReached)
                    {
                        return;
                    }

                    dynamic collection = collectionObject;
                    int count;
                    try
                    {
                        count = Math.Max(0, (int)collection.Count);
                    }
                    catch (Exception exception)
                    {
                        if (!unavailableIsNormal)
                        {
                            diagnostics.Add(
                                "collection_count_unavailable",
                                $"{currentStoryType}:{storyLinkIndex}:{collectionKind}",
                                exception
                            );
                        }
                        return;
                    }

                    for (var sourceIndex = 1; sourceIndex <= count; sourceIndex++)
                    {
                        if (scannedRoots >= DrawingRootScanLimit)
                        {
                            rootLimitReached = true;
                            diagnostics.Add(
                                "root_scan_limit_reached",
                                "document",
                                details: new { limit = DrawingRootScanLimit }
                            );
                            return;
                        }

                        dynamic item;
                        try
                        {
                            item = collection.Item(sourceIndex);
                        }
                        catch (Exception exception)
                        {
                            scannedRoots++;
                            diagnostics.Add(
                                "drawing_item_unavailable",
                                $"{currentStoryType}:{storyLinkIndex}:{collectionKind}:{sourceIndex}",
                                exception
                            );
                            continue;
                        }

                        scannedRoots++;
                        var resolvedKind = ClassifyLiveDrawing(item, collectionKind);
                        Increment(kindCounts, resolvedKind);
                        Increment(collectionCounts, collectionKind);
                        if (!DrawingKindMatches(objectKind, collectionKind, resolvedKind))
                        {
                            continue;
                        }

                        var matchedIndex = matchedRoots;
                        matchedRoots++;
                        if (matchedIndex < offset || items.Count >= limit)
                        {
                            continue;
                        }

                        var objectId = $"wdlo_{scannedRoots.ToString("D6", CultureInfo.InvariantCulture)}";
                        items.Add(
                            BuildLiveDrawingPayload(
                                application,
                                item,
                                objectId,
                                collectionKind,
                                resolvedKind,
                                currentStoryType,
                                storyLinkIndex,
                                sourceIndex,
                                includeGroupItems,
                                includeSmartArtNodes,
                                includeText,
                                maxTextChars,
                                includeScreenPixels,
                                diagnostics,
                                textBudget,
                                nestedBudget
                            )
                        );
                    }
                }

                void TryScanCollection(
                    Func<object> read,
                    string collectionKind,
                    string currentStoryType,
                    int storyLinkIndex,
                    bool unavailableIsNormal
                )
                {
                    if (rootLimitReached)
                    {
                        return;
                    }
                    try
                    {
                        ScanCollection(
                            read(),
                            collectionKind,
                            currentStoryType,
                            storyLinkIndex,
                            unavailableIsNormal
                        );
                    }
                    catch (Exception exception)
                    {
                        if (!unavailableIsNormal)
                        {
                            diagnostics.Add(
                                "collection_unavailable",
                                $"{currentStoryType}:{storyLinkIndex}:{collectionKind}",
                                exception
                            );
                        }
                    }
                }

                if (storyType is "all" or "main_text")
                {
                    TryScanCollection(
                        () => document.Shapes,
                        "floating",
                        "main_text",
                        0,
                        unavailableIsNormal: false
                    );
                    TryScanCollection(
                        () => document.InlineShapes,
                        "inline",
                        "main_text",
                        0,
                        unavailableIsNormal: false
                    );
                }

                foreach (var story in WordStoryTypes.Where(pair => pair.Key != MainTextStory))
                {
                    if (rootLimitReached)
                    {
                        break;
                    }
                    if (storyType != "all" && story.Value != storyType)
                    {
                        continue;
                    }

                    dynamic? current;
                    try
                    {
                        current = document.StoryRanges.Item(story.Key);
                    }
                    catch
                    {
                        continue;
                    }

                    var linkIndex = 0;
                    while (
                        current is not null
                        && linkIndex < DrawingStoryLinkLimit
                        && !rootLimitReached
                    )
                    {
                        var range = current;
                        TryScanCollection(
                            () => range.ShapeRange,
                            "floating",
                            story.Value,
                            linkIndex,
                            unavailableIsNormal: true
                        );
                        TryScanCollection(
                            () => range.InlineShapes,
                            "inline",
                            story.Value,
                            linkIndex,
                            unavailableIsNormal: true
                        );
                        linkIndex++;
                        try
                        {
                            current = range.NextStoryRange;
                        }
                        catch
                        {
                            current = null;
                        }
                    }
                    if (current is not null)
                    {
                        storyLinksTruncated = true;
                        diagnostics.Add(
                            "story_link_limit_reached",
                            story.Value,
                            details: new { limit = DrawingStoryLinkLimit }
                        );
                    }
                }

                var totalCountExact = !rootLimitReached && !storyLinksTruncated;
                var moreMatchedItems = matchedRoots > offset + items.Count;
                int? nextOffset = moreMatchedItems ? offset + items.Count : null;
                return new
                {
                    operation_contract = "wordtoolkit.inspect_live_word_drawing_layout/1.0",
                    live_document_id = record.Id,
                    live_version = record.Version,
                    layout_source = "microsoft_word_object_model",
                    repagination = new
                    {
                        requested = repaginate,
                        performed = repaginationPerformed,
                        cached_layout_may_have_been_used = !repaginationPerformed,
                        document_content_edit_requested = false,
                        layout_cache_may_change = repaginate,
                    },
                    geometry_contract = new
                    {
                        document_units = "points",
                        page_relative_bounds = "returned_only_when_both_position_references_are_page_and_left_top_are_numeric_offsets",
                        group_member_coordinate_space = "group_local",
                        smartart_shape_coordinate_space = "smartart_layout",
                        screen_units = includeScreenPixels ? "pixels" : "not_requested",
                        screen_pixels_are_viewport_dependent = true,
                        screen_pixels_are_page_geometry = false,
                        word_layout_execution_claim = "object_model_properties_only",
                    },
                    scan = new
                    {
                        object_kind = objectKind,
                        story_type = storyType,
                        root_scan_limit = DrawingRootScanLimit,
                        root_objects_scanned = scannedRoots,
                        matched_objects_scanned = matchedRoots,
                        total_count_exact = totalCountExact,
                        offset,
                        limit,
                        returned_count = items.Count,
                        next_offset = nextOffset,
                        response_truncated = rootLimitReached
                            || storyLinksTruncated
                            || moreMatchedItems,
                    },
                    counts = new
                    {
                        by_object_kind = kindCounts,
                        by_collection_kind = collectionCounts,
                        nested_group_items_returned = nestedBudget.GroupItems,
                        smartart_nodes_returned = nestedBudget.SmartArtNodes,
                        smartart_shapes_returned = nestedBudget.SmartArtShapes,
                    },
                    items,
                    diagnostics = new
                    {
                        count = diagnostics.TotalCount,
                        returned_count = diagnostics.Items.Count,
                        truncated = diagnostics.Truncated,
                        items = diagnostics.Items,
                    },
                    disclosure = new
                    {
                        sensitive_text_requested = includeText,
                        sensitive_text_fields_returned = textBudget.FieldsReturned,
                        text_character_budget = includeText ? DrawingTextBudgetChars : 0,
                        text_characters_returned = textBudget.Consumed,
                        text_budget_exhausted = textBudget.Exhausted,
                        screen_pixels_requested = includeScreenPixels,
                        raw_xml_returned = false,
                        raw_com_objects_returned = false,
                        external_content_fetched = false,
                        document_content_is_untrusted = true,
                    },
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            WordComReplaySafety.ReplaySafe,
            cancellationToken
        );
    }

    private static bool DrawingKindMatches(
        string requestedKind,
        string collectionKind,
        string resolvedKind
    ) =>
        requestedKind == "all"
        || requestedKind == resolvedKind
        || requestedKind == collectionKind;

    private static string ClassifyLiveDrawing(dynamic item, string collectionKind)
    {
        var nativeType = ReadDrawingInt(() => item.Type);
        var hasSmartArt = ReadDrawingOfficeBoolean(() => item.HasSmartArt);
        var hasChart = ReadDrawingOfficeBoolean(() => item.HasChart);
        if (hasSmartArt == true || nativeType == (collectionKind == "inline" ? 15 : 24))
        {
            return "smartart";
        }
        if (hasChart == true || nativeType == (collectionKind == "inline" ? 12 : 3))
        {
            return "chart";
        }
        if (collectionKind == "inline")
        {
            return nativeType switch
            {
                1 or 2 or 5 => "ole",
                3 or 4 or 7 or 8 or 9 or 19 or 20 => "picture",
                14 => "canvas",
                _ => "other",
            };
        }
        return nativeType switch
        {
            6 => "group",
            7 or 10 or 12 => "ole",
            11 or 13 or 28 or 29 or 30 or 31 => "picture",
            20 => "canvas",
            _ => "other",
        };
    }

    private static Dictionary<string, object?> BuildLiveDrawingPayload(
        dynamic application,
        dynamic item,
        string objectId,
        string collectionKind,
        string resolvedKind,
        string storyType,
        int storyLinkIndex,
        int sourceIndex,
        bool includeGroupItems,
        bool includeSmartArtNodes,
        bool includeText,
        int maxTextChars,
        bool includeScreenPixels,
        DrawingLayoutDiagnostics diagnostics,
        DrawingTextBudget textBudget,
        DrawingNestedBudget nestedBudget
    )
    {
        var scope = objectId;
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["object_id"] = objectId,
            ["identity_scope"] = "connected_document_traversal",
            ["collection_kind"] = collectionKind,
            ["object_kind"] = resolvedKind,
            ["story_type"] = storyType,
            ["story_link_index"] = storyLinkIndex,
            ["source_index"] = sourceIndex,
        };

        var nativeType = ReadDrawingInt(() => item.Type, diagnostics, "native_type", scope);
        if (nativeType is not null)
        {
            payload["native_type"] = nativeType;
            payload["native_type_name"] = collectionKind == "inline"
                ? InlineShapeTypeName(nativeType.Value)
                : FloatingShapeTypeName(nativeType.Value);
        }

        dynamic? range = null;
        try
        {
            range = collectionKind == "inline" ? item.Range : item.Anchor;
            payload[collectionKind == "inline" ? "range" : "anchor_range"] =
                BuildDrawingRangePayload(range, diagnostics, scope);
        }
        catch (Exception exception)
        {
            diagnostics.Add("range_unavailable", scope, exception);
        }

        var pageNumber = ReadDrawingRangeInformation(
            range,
            3,
            diagnostics,
            "page_number",
            scope
        );
        var sectionNumber = ReadDrawingRangeInformation(
            range,
            2,
            diagnostics,
            "section_number",
            scope
        );
        if (pageNumber is > 0)
        {
            payload["page_number"] = pageNumber;
        }
        if (sectionNumber is > 0)
        {
            payload["section_number"] = sectionNumber;
        }

        var width = ReadDrawingDouble(() => item.Width, diagnostics, "width", scope);
        var height = ReadDrawingDouble(() => item.Height, diagnostics, "height", scope);
        payload["size_points"] = new
        {
            width = RoundedDrawingNumber(width),
            height = RoundedDrawingNumber(height),
        };

        if (includeText)
        {
            if (collectionKind == "floating")
            {
                AddDrawingText(
                    payload,
                    "name",
                    () => item.Name,
                    maxTextChars,
                    diagnostics,
                    textBudget,
                    scope
                );
            }
            AddDrawingText(
                payload,
                "title",
                () => item.Title,
                maxTextChars,
                diagnostics,
                textBudget,
                scope
            );
            AddDrawingText(
                payload,
                "alternative_text",
                () => item.AlternativeText,
                maxTextChars,
                diagnostics,
                textBudget,
                scope
            );
        }

        if (collectionKind == "floating")
        {
            AddFloatingDrawingLayout(item, payload, width, height, diagnostics, scope);
        }
        else
        {
            AddInlineDrawingLayout(range, payload, diagnostics, scope);
        }

        if (includeScreenPixels)
        {
            var screenTarget = collectionKind == "inline" ? range : item;
            payload["viewport_bounds_pixels"] = ReadDrawingViewportBounds(
                application,
                screenTarget,
                diagnostics,
                scope
            );
        }

        if (includeGroupItems && resolvedKind == "group")
        {
            payload["group"] = BuildDrawingGroupProjection(
                item,
                objectId,
                includeSmartArtNodes,
                includeText,
                maxTextChars,
                diagnostics,
                textBudget,
                nestedBudget
            );
        }
        if (includeSmartArtNodes && resolvedKind == "smartart")
        {
            payload["smartart"] = BuildSmartArtProjection(
                item,
                objectId,
                includeText,
                maxTextChars,
                diagnostics,
                textBudget,
                nestedBudget
            );
        }
        return payload;
    }

    private static void AddFloatingDrawingLayout(
        dynamic item,
        Dictionary<string, object?> payload,
        double? width,
        double? height,
        DrawingLayoutDiagnostics diagnostics,
        string scope
    )
    {
        var left = ReadDrawingDouble(() => item.Left, diagnostics, "left", scope);
        var top = ReadDrawingDouble(() => item.Top, diagnostics, "top", scope);
        var horizontalReference = ReadDrawingInt(
            () => item.RelativeHorizontalPosition,
            diagnostics,
            "relative_horizontal_position",
            scope
        );
        var verticalReference = ReadDrawingInt(
            () => item.RelativeVerticalPosition,
            diagnostics,
            "relative_vertical_position",
            scope
        );
        var position = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["left"] = DrawingPositionValue(left),
            ["top"] = DrawingPositionValue(top),
            ["horizontal_reference"] = RelativeHorizontalPositionName(horizontalReference),
            ["horizontal_reference_value"] = horizontalReference,
            ["vertical_reference"] = RelativeVerticalPositionName(verticalReference),
            ["vertical_reference_value"] = verticalReference,
        };
        var leftRelative = ReadDrawingDouble(() => item.LeftRelative);
        var topRelative = ReadDrawingDouble(() => item.TopRelative);
        if (DrawingRelativePercentIsDefined(leftRelative))
        {
            position["left_relative_percent"] = RoundedDrawingNumber(leftRelative);
        }
        if (DrawingRelativePercentIsDefined(topRelative))
        {
            position["top_relative_percent"] = RoundedDrawingNumber(topRelative);
        }
        payload["position"] = position;

        var rotation = ReadDrawingDouble(() => item.Rotation);
        if (rotation is not null)
        {
            payload["rotation_degrees"] = RoundedDrawingNumber(rotation);
        }
        var zOrder = ReadDrawingInt(() => item.ZOrderPosition);
        if (zOrder is not null)
        {
            payload["z_order_position"] = zOrder;
        }
        var lockAnchor = ReadDrawingOfficeBoolean(() => item.LockAnchor);
        if (lockAnchor is not null)
        {
            payload["lock_anchor"] = lockAnchor;
        }
        var layoutInCell = ReadDrawingOfficeBoolean(() => item.LayoutInCell);
        if (layoutInCell is not null)
        {
            payload["layout_in_cell"] = layoutInCell;
        }
        var visible = ReadDrawingOfficeBoolean(() => item.Visible);
        if (visible is not null)
        {
            payload["visible"] = visible;
        }
        var horizontalFlip = ReadDrawingOfficeBoolean(() => item.HorizontalFlip);
        var verticalFlip = ReadDrawingOfficeBoolean(() => item.VerticalFlip);
        if (horizontalFlip is not null || verticalFlip is not null)
        {
            payload["flip"] = new
            {
                horizontal = horizontalFlip,
                vertical = verticalFlip,
            };
        }

        try
        {
            dynamic wrap = item.WrapFormat;
            var wrapType = ReadDrawingInt(() => wrap.Type);
            var wrapSide = ReadDrawingInt(() => wrap.Side);
            payload["wrap"] = new
            {
                type = WrapTypeName(wrapType),
                type_value = wrapType,
                side = WrapSideName(wrapSide),
                side_value = wrapSide,
                distance_left_points = RoundedDrawingNumber(
                    ReadDrawingDouble(() => wrap.DistanceLeft)
                ),
                distance_right_points = RoundedDrawingNumber(
                    ReadDrawingDouble(() => wrap.DistanceRight)
                ),
                distance_top_points = RoundedDrawingNumber(
                    ReadDrawingDouble(() => wrap.DistanceTop)
                ),
                distance_bottom_points = RoundedDrawingNumber(
                    ReadDrawingDouble(() => wrap.DistanceBottom)
                ),
            };
        }
        catch (Exception exception)
        {
            diagnostics.Add("wrap_unavailable", scope, exception);
        }

        if (
            horizontalReference == 1
            && verticalReference == 1
            && DrawingPositionIsNumeric(left)
            && DrawingPositionIsNumeric(top)
            && width is not null
            && height is not null
        )
        {
            payload["page_relative_bounds_points"] = new
            {
                x = RoundedDrawingNumber(left),
                y = RoundedDrawingNumber(top),
                width = RoundedDrawingNumber(width),
                height = RoundedDrawingNumber(height),
                coordinate_space = "page_reference_box",
            };
        }
    }

    private static void AddInlineDrawingLayout(
        dynamic? range,
        Dictionary<string, object?> payload,
        DrawingLayoutDiagnostics diagnostics,
        string scope
    )
    {
        payload["flow"] = new { mode = "inline_character", coordinate_space = "text_flow" };
        var x = ReadDrawingRangeInformation(
            range,
            5,
            diagnostics,
            "visible_page_x",
            scope
        );
        var y = ReadDrawingRangeInformation(
            range,
            6,
            diagnostics,
            "visible_page_y",
            scope
        );
        if (x is >= 0 && y is >= 0)
        {
            payload["visible_page_position_points"] = new
            {
                x,
                y,
                viewport_dependent = true,
                unavailable_when_offscreen = true,
            };
        }
    }

    private static object BuildDrawingGroupProjection(
        dynamic root,
        string rootObjectId,
        bool includeSmartArtNodes,
        bool includeText,
        int maxTextChars,
        DrawingLayoutDiagnostics diagnostics,
        DrawingTextBudget textBudget,
        DrawingNestedBudget nestedBudget
    )
    {
        var members = new List<object>();
        var depthTruncated = false;
        var itemLimitReached = false;

        void Walk(dynamic parent, string parentId, string path, int depth)
        {
            if (depth > DrawingGroupDepthLimit)
            {
                depthTruncated = true;
                return;
            }
            dynamic groupItems;
            int count;
            try
            {
                groupItems = parent.GroupItems;
                count = Math.Max(0, (int)groupItems.Count);
            }
            catch (Exception exception)
            {
                diagnostics.Add("group_items_unavailable", parentId, exception);
                return;
            }
            for (var index = 1; index <= count; index++)
            {
                if (nestedBudget.GroupItems >= DrawingGroupItemLimit)
                {
                    itemLimitReached = true;
                    return;
                }
                dynamic child;
                try
                {
                    child = groupItems.Item(index);
                }
                catch (Exception exception)
                {
                    diagnostics.Add("group_item_unavailable", $"{parentId}:{index}", exception);
                    continue;
                }
                nestedBudget.GroupItems++;
                var childPath = path.Length == 0 ? index.ToString(CultureInfo.InvariantCulture) : $"{path}.{index}";
                var childId = $"{rootObjectId}_g{childPath.Replace('.', '_')}";
                var kind = ClassifyLiveDrawing(child, "floating");
                var childPayload = BuildNestedShapePayload(
                    child,
                    childId,
                    parentId,
                    childPath,
                    kind,
                    "group_local",
                    includeText,
                    maxTextChars,
                    diagnostics,
                    textBudget
                );
                if (includeSmartArtNodes && kind == "smartart")
                {
                    childPayload["smartart"] = BuildSmartArtProjection(
                        child,
                        childId,
                        includeText,
                        maxTextChars,
                        diagnostics,
                        textBudget,
                        nestedBudget
                    );
                }
                members.Add(childPayload);
                if (kind == "group")
                {
                    Walk(child, childId, childPath, depth + 1);
                }
            }
        }

        Walk(root, rootObjectId, "", 1);
        return new
        {
            coordinate_space = "group_local",
            member_limit = DrawingGroupItemLimit,
            depth_limit = DrawingGroupDepthLimit,
            returned_member_count = members.Count,
            truncated = itemLimitReached || depthTruncated,
            item_limit_reached = itemLimitReached,
            depth_limit_reached = depthTruncated,
            members,
        };
    }

    private static Dictionary<string, object?> BuildNestedShapePayload(
        dynamic item,
        string objectId,
        string parentObjectId,
        string path,
        string kind,
        string coordinateSpace,
        bool includeText,
        int maxTextChars,
        DrawingLayoutDiagnostics diagnostics,
        DrawingTextBudget textBudget
    )
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["object_id"] = objectId,
            ["parent_object_id"] = parentObjectId,
            ["path"] = path,
            ["object_kind"] = kind,
            ["coordinate_space"] = coordinateSpace,
        };
        var nativeType = ReadDrawingInt(() => item.Type);
        payload["native_type"] = nativeType;
        payload["native_type_name"] = nativeType is null
            ? "unknown"
            : FloatingShapeTypeName(nativeType.Value);
        payload["bounds"] = new
        {
            left = DrawingPositionValue(ReadDrawingDouble(() => item.Left)),
            top = DrawingPositionValue(ReadDrawingDouble(() => item.Top)),
            width_points = RoundedDrawingNumber(ReadDrawingDouble(() => item.Width)),
            height_points = RoundedDrawingNumber(ReadDrawingDouble(() => item.Height)),
            rotation_degrees = RoundedDrawingNumber(ReadDrawingDouble(() => item.Rotation)),
        };
        if (includeText)
        {
            AddDrawingText(payload, "name", () => item.Name, maxTextChars, diagnostics, textBudget, objectId);
            AddDrawingText(payload, "title", () => item.Title, maxTextChars, diagnostics, textBudget, objectId);
            AddDrawingText(payload, "alternative_text", () => item.AlternativeText, maxTextChars, diagnostics, textBudget, objectId);
        }
        return payload;
    }

    private static object BuildSmartArtProjection(
        dynamic root,
        string rootObjectId,
        bool includeText,
        int maxTextChars,
        DrawingLayoutDiagnostics diagnostics,
        DrawingTextBudget textBudget,
        DrawingNestedBudget nestedBudget
    )
    {
        dynamic smartArt;
        try
        {
            smartArt = root.SmartArt;
        }
        catch (Exception exception)
        {
            diagnostics.Add("smartart_unavailable", rootObjectId, exception);
            return new
            {
                available = false,
                node_limit = DrawingSmartArtNodeLimit,
                shape_limit = DrawingSmartArtShapeLimit,
                nodes = Array.Empty<object>(),
            };
        }

        var nodes = new List<object>();
        var nodeLimitReached = false;
        var shapeLimitReached = false;
        var totalNodes = 0;
        dynamic allNodes;
        try
        {
            allNodes = smartArt.AllNodes;
            totalNodes = Math.Max(0, (int)allNodes.Count);
        }
        catch (Exception exception)
        {
            diagnostics.Add("smartart_nodes_unavailable", rootObjectId, exception);
            return new
            {
                available = true,
                node_limit = DrawingSmartArtNodeLimit,
                shape_limit = DrawingSmartArtShapeLimit,
                total_node_count = 0,
                nodes,
            };
        }

        for (var index = 1; index <= totalNodes; index++)
        {
            if (nestedBudget.SmartArtNodes >= DrawingSmartArtNodeLimit)
            {
                nodeLimitReached = true;
                break;
            }
            dynamic node;
            try
            {
                node = allNodes.Item(index);
            }
            catch (Exception exception)
            {
                diagnostics.Add("smartart_node_unavailable", $"{rootObjectId}:{index}", exception);
                continue;
            }
            nestedBudget.SmartArtNodes++;
            var nodeId = $"{rootObjectId}_sa{index.ToString("D3", CultureInfo.InvariantCulture)}";
            var nodePayload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["node_id"] = nodeId,
                ["node_index"] = index,
                ["level"] = ReadDrawingInt(() => node.Level),
                ["hidden"] = ReadDrawingOfficeBoolean(() => node.Hidden),
                ["native_type"] = ReadDrawingInt(() => node.Type),
                ["child_count"] = ReadDrawingInt(() => node.Nodes.Count) ?? 0,
            };
            if (includeText)
            {
                AddDrawingText(
                    nodePayload,
                    "text",
                    () => node.TextFrame2.TextRange.Text,
                    maxTextChars,
                    diagnostics,
                    textBudget,
                    nodeId
                );
            }

            var renderedShapes = new List<object>();
            var declaredShapeCount = 0;
            try
            {
                dynamic shapes = node.Shapes;
                declaredShapeCount = Math.Max(0, (int)shapes.Count);
                for (var shapeIndex = 1; shapeIndex <= declaredShapeCount; shapeIndex++)
                {
                    if (nestedBudget.SmartArtShapes >= DrawingSmartArtShapeLimit)
                    {
                        shapeLimitReached = true;
                        break;
                    }
                    dynamic shape = shapes.Item(shapeIndex);
                    nestedBudget.SmartArtShapes++;
                    renderedShapes.Add(
                        new
                        {
                            shape_index = shapeIndex,
                            native_type = ReadDrawingInt(() => shape.Type),
                            coordinate_space = "smartart_layout",
                            left = DrawingPositionValue(ReadDrawingDouble(() => shape.Left)),
                            top = DrawingPositionValue(ReadDrawingDouble(() => shape.Top)),
                            width_points = RoundedDrawingNumber(ReadDrawingDouble(() => shape.Width)),
                            height_points = RoundedDrawingNumber(ReadDrawingDouble(() => shape.Height)),
                            rotation_degrees = RoundedDrawingNumber(ReadDrawingDouble(() => shape.Rotation)),
                        }
                    );
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add("smartart_node_shapes_unavailable", nodeId, exception);
            }
            nodePayload["rendered_shape_count"] = declaredShapeCount;
            nodePayload["rendered_shapes"] = renderedShapes;
            nodePayload["rendered_shapes_truncated"] = renderedShapes.Count < declaredShapeCount;
            nodes.Add(nodePayload);
        }

        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = true,
            ["node_limit"] = DrawingSmartArtNodeLimit,
            ["shape_limit"] = DrawingSmartArtShapeLimit,
            ["total_node_count"] = totalNodes,
            ["returned_node_count"] = nodes.Count,
            ["nodes_truncated"] = nodeLimitReached || nodes.Count < totalNodes,
            ["shapes_truncated"] = shapeLimitReached,
            ["nodes"] = nodes,
        };
        AddDrawingOptionalIdentifier(metadata, "layout_id", () => smartArt.Layout.Id);
        AddDrawingOptionalIdentifier(metadata, "quick_style_id", () => smartArt.QuickStyle.Id);
        AddDrawingOptionalIdentifier(metadata, "color_id", () => smartArt.Color.Id);
        return metadata;
    }

    private static object BuildDrawingRangePayload(
        dynamic range,
        DrawingLayoutDiagnostics diagnostics,
        string scope
    ) =>
        new
        {
            start = ReadDrawingInt(() => range.Start, diagnostics, "range_start", scope),
            end = ReadDrawingInt(() => range.End, diagnostics, "range_end", scope),
            story_type_value = ReadDrawingInt(() => range.StoryType),
        };

    private static object? ReadDrawingViewportBounds(
        dynamic application,
        dynamic? target,
        DrawingLayoutDiagnostics diagnostics,
        string scope
    )
    {
        if (target is null)
        {
            diagnostics.Add("viewport_target_unavailable", scope);
            return null;
        }
        try
        {
            var left = 0;
            var top = 0;
            var width = 0;
            var height = 0;
            application.ActiveWindow.GetPoint(ref left, ref top, ref width, ref height, target);
            return new
            {
                left,
                top,
                width,
                height,
                coordinate_space = "active_window_screen",
                viewport_dependent = true,
                page_geometry = false,
            };
        }
        catch (Exception exception)
        {
            diagnostics.Add("viewport_bounds_unavailable", scope, exception);
            return null;
        }
    }

    private static int? ReadDrawingRangeInformation(
        dynamic? range,
        int code,
        DrawingLayoutDiagnostics diagnostics,
        string property,
        string scope
    )
    {
        if (range is null)
        {
            return null;
        }
        try
        {
            return Convert.ToInt32(range.Information[code], CultureInfo.InvariantCulture);
        }
        catch
        {
            try
            {
                return Convert.ToInt32(range.get_Information(code), CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                diagnostics.Add($"{property}_unavailable", scope, exception);
                return null;
            }
        }
    }

    private static int? ReadDrawingInt(
        Func<object?> read,
        DrawingLayoutDiagnostics? diagnostics = null,
        string property = "",
        string scope = ""
    )
    {
        try
        {
            var value = read();
            return value is null
                ? null
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception)
        {
            diagnostics?.Add($"{property}_unavailable", scope, exception);
            return null;
        }
    }

    private static double? ReadDrawingDouble(
        Func<object?> read,
        DrawingLayoutDiagnostics? diagnostics = null,
        string property = "",
        string scope = ""
    )
    {
        try
        {
            var value = read();
            if (value is null)
            {
                return null;
            }
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(number) ? number : null;
        }
        catch (Exception exception)
        {
            diagnostics?.Add($"{property}_unavailable", scope, exception);
            return null;
        }
    }

    private static bool? ReadDrawingOfficeBoolean(Func<object?> read)
    {
        try
        {
            var value = read();
            if (value is bool boolean)
            {
                return boolean;
            }
            if (value is null)
            {
                return null;
            }
            var integer = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return integer is not 0 and not 9_999_999;
        }
        catch
        {
            return null;
        }
    }

    private static void AddDrawingText(
        Dictionary<string, object?> payload,
        string property,
        Func<object?> read,
        int maxTextChars,
        DrawingLayoutDiagnostics diagnostics,
        DrawingTextBudget textBudget,
        string scope
    )
    {
        if (!textBudget.HasCapacity)
        {
            payload[$"{property}_omitted_due_to_budget"] = true;
            return;
        }
        try
        {
            var raw = Convert.ToString(read(), CultureInfo.InvariantCulture) ?? "";
            var projection = textBudget.Take(CleanWordPreview(raw), maxTextChars);
            payload[property] = projection.Text;
            payload[$"{property}_truncated"] = projection.Truncated;
        }
        catch (Exception exception)
        {
            diagnostics.Add($"{property}_unavailable", scope, exception);
        }
    }

    private static void AddDrawingOptionalIdentifier(
        Dictionary<string, object?> payload,
        string property,
        Func<object?> read
    )
    {
        try
        {
            var value = Convert.ToString(read(), CultureInfo.InvariantCulture) ?? "";
            if (value.Length > 0)
            {
                payload[property] = value[..Math.Min(value.Length, 256)];
                payload[$"{property}_truncated"] = value.Length > 256;
            }
        }
        catch
        {
            // Layout, color, and quick-style identifiers are optional across Word versions.
        }
    }

    private static object? RoundedDrawingNumber(double? value) =>
        value is null ? null : Math.Round(value.Value, 3);

    private static bool DrawingRelativePercentIsDefined(double? value) =>
        value is not null && Math.Abs(value.Value + 999_999d) > 0.5d;

    private static bool DrawingPositionIsNumeric(double? value) =>
        value is not null && DrawingPositionAlignment(value.Value) is null;

    private static object? DrawingPositionValue(double? value)
    {
        if (value is null)
        {
            return null;
        }
        var alignment = DrawingPositionAlignment(value.Value);
        return alignment is null
            ? new { kind = "offset_points", value = Math.Round(value.Value, 3) }
            : new { kind = "alignment", value = alignment };
    }

    private static string? DrawingPositionAlignment(double value)
    {
        foreach (
            var candidate in new[]
            {
                (-999_999d, "top"),
                (-999_998d, "left"),
                (-999_997d, "bottom"),
                (-999_996d, "right"),
                (-999_995d, "center"),
                (-999_994d, "inside"),
                (-999_993d, "outside"),
            }
        )
        {
            if (Math.Abs(value - candidate.Item1) <= 0.5d)
            {
                return candidate.Item2;
            }
        }
        return null;
    }

    private static string RelativeHorizontalPositionName(int? value) =>
        value switch
        {
            0 => "margin",
            1 => "page",
            2 => "column",
            3 => "character",
            4 => "left_margin_area",
            5 => "right_margin_area",
            6 => "inner_margin_area",
            7 => "outer_margin_area",
            _ => "unknown",
        };

    private static string RelativeVerticalPositionName(int? value) =>
        value switch
        {
            0 => "margin",
            1 => "page",
            2 => "paragraph",
            3 => "line",
            4 => "top_margin_area",
            5 => "bottom_margin_area",
            6 => "inner_margin_area",
            7 => "outer_margin_area",
            _ => "unknown",
        };

    private static string WrapTypeName(int? value) =>
        value switch
        {
            0 => "square",
            1 => "tight",
            2 => "through",
            3 => "in_front_of_text",
            4 => "top_and_bottom",
            5 => "behind_text",
            7 => "inline",
            _ => "unknown",
        };

    private static string WrapSideName(int? value) =>
        value switch
        {
            0 => "both",
            1 => "left",
            2 => "right",
            3 => "largest",
            _ => "unknown",
        };

    private static string InlineShapeTypeName(int value) =>
        value switch
        {
            1 => "embedded_ole",
            2 => "linked_ole",
            3 => "picture",
            4 => "linked_picture",
            5 => "ole_control",
            6 => "horizontal_line",
            7 => "picture_horizontal_line",
            8 => "linked_horizontal_line",
            9 => "picture_bullet",
            10 => "script_anchor",
            11 => "owa_control",
            12 => "chart",
            13 => "diagram",
            14 => "locked_canvas",
            15 => "smartart",
            16 => "web_video",
            19 => "three_d_model",
            20 => "linked_three_d_model",
            _ => "unknown",
        };

    private static string FloatingShapeTypeName(int value) =>
        value switch
        {
            -2 => "mixed",
            1 => "auto_shape",
            2 => "callout",
            3 => "chart",
            4 => "comment",
            5 => "freeform",
            6 => "group",
            7 => "embedded_ole",
            8 => "form_control",
            9 => "line",
            10 => "linked_ole",
            11 => "linked_picture",
            12 => "ole_control",
            13 => "picture",
            14 => "placeholder",
            15 => "text_effect",
            16 => "media",
            17 => "text_box",
            18 => "script_anchor",
            19 => "table",
            20 => "canvas",
            21 => "diagram",
            22 => "ink",
            23 => "ink_comment",
            24 => "smartart",
            25 => "slicer",
            26 => "web_video",
            27 => "content_app",
            28 => "graphic",
            29 => "linked_graphic",
            30 => "three_d_model",
            31 => "linked_three_d_model",
            _ => "unknown",
        };

    private sealed class DrawingLayoutDiagnostics
    {
        private readonly int _limit;

        public DrawingLayoutDiagnostics(int limit)
        {
            _limit = limit;
        }

        public int TotalCount { get; private set; }
        public bool Truncated => TotalCount > Items.Count;
        public List<object> Items { get; } = [];

        public void Add(
            string code,
            string scope,
            Exception? exception = null,
            object? details = null
        )
        {
            TotalCount++;
            if (Items.Count >= _limit)
            {
                return;
            }
            Items.Add(
                new
                {
                    code,
                    scope,
                    error_type = exception?.GetType().Name ?? "",
                    details,
                }
            );
        }
    }

    private sealed class DrawingTextBudget
    {
        private readonly int _limit;

        public DrawingTextBudget(int limit)
        {
            _limit = limit;
        }

        public int Consumed { get; private set; }
        public int FieldsReturned { get; private set; }
        public bool HasCapacity => Consumed < _limit;
        public bool Exhausted => Consumed >= _limit && _limit > 0;

        public (string Text, bool Truncated) Take(string value, int fieldLimit)
        {
            var remaining = Math.Max(0, _limit - Consumed);
            var length = Math.Min(value.Length, Math.Min(fieldLimit, remaining));
            var text = value[..length];
            Consumed += length;
            FieldsReturned++;
            return (text, value.Length > length);
        }
    }

    private sealed class DrawingNestedBudget
    {
        public int GroupItems { get; set; }
        public int SmartArtNodes { get; set; }
        public int SmartArtShapes { get; set; }
    }
}
