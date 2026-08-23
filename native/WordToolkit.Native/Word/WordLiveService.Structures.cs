using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly IReadOnlyDictionary<int, string> WordStoryTypes =
        new Dictionary<int, string>
        {
            [1] = "main_text",
            [2] = "footnotes",
            [3] = "endnotes",
            [4] = "comments",
            [5] = "text_frames",
            [6] = "even_page_headers",
            [7] = "primary_headers",
            [8] = "even_page_footers",
            [9] = "primary_footers",
            [10] = "first_page_headers",
            [11] = "first_page_footers",
            [12] = "footnote_separator",
            [13] = "footnote_continuation_separator",
            [14] = "footnote_continuation_notice",
            [15] = "endnote_separator",
            [16] = "endnote_continuation_separator",
            [17] = "endnote_continuation_notice",
        };

    private static readonly string[] WordStructureNames =
    [
        "paragraphs",
        "sections",
        "styles",
        "tables",
        "equations",
        "fields",
        "form_fields",
        "bookmarks",
        "hyperlinks",
        "comments",
        "revisions",
        "content_controls",
        "inline_shapes",
        "floating_shapes",
        "footnotes",
        "endnotes",
        "lists",
        "list_paragraphs",
        "subdocuments",
        "variables",
        "tables_of_contents",
        "tables_of_figures",
        "tables_of_authorities",
    ];

    private static readonly HashSet<string> EditableStructureNames = new(
        [
            "paragraphs",
            "styles",
            "tables",
            "equations",
            "fields",
            "bookmarks",
            "comments",
            "revisions",
            "lists",
            "list_paragraphs",
        ],
        StringComparer.Ordinal
    );

    private readonly ConcurrentDictionary<string, long> _structureMapObservations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _structureInspectionObservations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _equationLearning =
        new(StringComparer.Ordinal);

    private async Task<object> MapStructuresAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var includeHistograms = arguments.Boolean("include_type_histograms", false);
        var adaptive = arguments.Boolean("adaptive_type_histograms", true);
        var maxTypeItems = (int)(arguments.NullableInt64("max_type_items") ?? 2_000);
        if (maxTypeItems is < 1 or > 10_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_type_items must be between 1 and 10,000"
            );
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                var structures = new Dictionary<string, int?>(StringComparer.Ordinal);
                foreach (var name in WordStructureNames)
                {
                    structures[name] = StructureCount(document, name);
                    _structureMapObservations.AddOrUpdate(name, 1, (_, count) => count + 1);
                }

                var stories = new List<object>();
                foreach (var pair in WordStoryTypes)
                {
                    var ranges = new List<object>();
                    var readError = "";
                    try
                    {
                        dynamic? current = document.StoryRanges.Item(pair.Key);
                        var linked = 0;
                        while (current is not null && linked < 1_000)
                        {
                            ranges.Add(
                                new
                                {
                                    link_index = linked,
                                    start = SafeInt(() => (int)current.Start, -1),
                                    end = SafeInt(() => (int)current.End, -1),
                                    characters = SafeInt(
                                        () => Math.Max(0, (int)current.End - (int)current.Start),
                                        0
                                    ),
                                    paragraphs = SafeInt(() => (int)current.Paragraphs.Count, 0),
                                    tables = SafeInt(() => (int)current.Tables.Count, 0),
                                    fields = SafeInt(() => (int)current.Fields.Count, 0),
                                    equations = SafeInt(() => (int)current.OMaths.Count, 0),
                                }
                            );
                            linked++;
                            dynamic? next = null;
                            try
                            {
                                next = current.NextStoryRange;
                            }
                            catch
                            {
                                next = null;
                            }
                            current = next;
                        }
                    }
                    catch (Exception exception)
                    {
                        readError = exception.GetType().Name;
                    }
                    stories.Add(
                        new
                        {
                            story_type = pair.Key,
                            name = pair.Value,
                            present = ranges.Count > 0,
                            linked_range_count = ranges.Count,
                            ranges,
                            read_error = readError,
                        }
                    );
                }

                var histograms = new Dictionary<string, object>(StringComparer.Ordinal);
                if (includeHistograms)
                {
                    foreach (
                        var specification in new[]
                        {
                            ("equation_types", "equations", "Type"),
                            ("field_types", "fields", "Type"),
                            ("form_field_types", "form_fields", "Type"),
                            ("content_control_types", "content_controls", "Type"),
                            ("revision_types", "revisions", "Type"),
                            ("inline_shape_types", "inline_shapes", "Type"),
                            ("floating_shape_types", "floating_shapes", "Type"),
                            ("style_types", "styles", "Type"),
                            ("list_types", "lists", "Range.ListFormat.ListType"),
                        }
                    )
                    {
                        histograms[specification.Item1] = StructureTypeHistogram(
                            document,
                            specification.Item2,
                            specification.Item3,
                            maxTypeItems
                        );
                    }
                }

                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    structures,
                    stories,
                    type_histograms = histograms,
                    type_histograms_requested = includeHistograms,
                    adaptive_type_histograms = adaptive,
                    editable_structures = WordStructureNames
                        .Where(EditableStructureNames.Contains)
                        .ToArray(),
                    inspectable_structures = WordStructureNames,
                    content_returned = false,
                    structure_learning = new
                    {
                        observation_recorded = true,
                        document_content_stored = false,
                        document_counts_stored = false,
                        property_values_stored = false,
                    },
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            WordComReplaySafety.ReplaySafe,
            cancellationToken
        );
    }

    private async Task<object> InspectStructureItemsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var structure = arguments.String("structure");
        if (!WordStructureNames.Contains(structure, StringComparer.Ordinal))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "structure is not a supported native Word collection",
                new { supported = WordStructureNames }
            );
        }
        var offset = (int)(arguments.NullableInt64("offset") ?? 0);
        var limit = (int)(arguments.NullableInt64("limit") ?? 50);
        var includeText = arguments.Boolean("include_text", false);
        var maxTextChars = (int)(arguments.NullableInt64("max_text_chars") ?? 500);
        var adaptive = arguments.Boolean("adaptive_property_probing", true);
        if (offset is < 0 or > 1_000_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 1,000,000"
            );
        }
        if (limit is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "limit must be between 1 and 200"
            );
        }
        if (maxTextChars is < 1 or > 2_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_text_chars must be between 1 and 2,000"
            );
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                dynamic? collection = StructureCollection(document, structure);
                if (collection is null)
                {
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        structure,
                        available = false,
                        total_count = 0,
                        offset,
                        limit,
                        returned_count = 0,
                        truncated = false,
                        items = Array.Empty<object>(),
                        text_content_returned = false,
                        external_addresses_returned = false,
                        field_codes_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }

                var total = SafeInt(() => (int)collection.Count, 0);
                var first = Math.Min(total, offset) + 1;
                var last = Math.Min(total, offset + limit);
                var items = new List<object>();
                for (var index = first; index <= last; index++)
                {
                    try
                    {
                        dynamic item = collection.Item(index);
                        items.Add(
                            StructureItemPayload(
                                structure,
                                item,
                                index,
                                includeText,
                                maxTextChars
                            )
                        );
                    }
                    catch (Exception exception)
                    {
                        items.Add(
                            new
                            {
                                item_index = index,
                                properties = new Dictionary<string, object?>(),
                                read_error = exception.GetType().Name,
                            }
                        );
                    }
                }
                _structureInspectionObservations.AddOrUpdate(
                    structure,
                    1,
                    (_, count) => count + 1
                );

                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    structure,
                    available = true,
                    total_count = total,
                    offset,
                    limit,
                    returned_count = items.Count,
                    truncated = offset + items.Count < total,
                    items,
                    text_content_returned = includeText,
                    external_addresses_returned = false,
                    field_codes_returned = false,
                    property_learning = new
                    {
                        adaptive,
                        observation_recorded = true,
                        property_values_stored = false,
                    },
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            WordComReplaySafety.ReplaySafe,
            cancellationToken
        );
    }

    private async Task<object> DiagnoseLayoutAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var maxParagraphs = (int)(arguments.NullableInt64("max_paragraphs") ?? 10_000);
        var maxIssues = (int)(arguments.NullableInt64("max_issues") ?? 500);
        var keepThreshold = (int)(
            arguments.NullableInt64("keep_with_next_threshold") ?? 5
        );
        var longHeading = (int)(arguments.NullableInt64("long_heading_chars") ?? 100);
        var longKeepTogether = (int)(
            arguments.NullableInt64("long_keep_together_chars") ?? 1_200
        );
        if (maxParagraphs is < 1 or > 25_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_paragraphs must be between 1 and 25,000"
            );
        }
        if (maxIssues is < 1 or > 2_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_issues must be between 1 and 2,000"
            );
        }
        if (keepThreshold is < 2 or > 100)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "keep_with_next_threshold must be between 2 and 100"
            );
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                var total = SafeInt(() => (int)document.Paragraphs.Count, 0);
                var scanned = Math.Min(total, maxParagraphs);
                var issues = new List<object>();
                var keepChainStart = 0;
                var keepChainLength = 0;
                var emptyRunStart = 0;
                var emptyRunLength = 0;
                var headingCount = 0;

                void AddIssue(
                    string code,
                    string severity,
                    int paragraph,
                    int start,
                    int end,
                    object details
                )
                {
                    if (issues.Count >= maxIssues)
                    {
                        return;
                    }
                    issues.Add(
                        new
                        {
                            code,
                            severity,
                            paragraph,
                            range = new { start, end },
                            details,
                        }
                    );
                }

                for (var index = 1; index <= scanned; index++)
                {
                    dynamic paragraph = document.Paragraphs.Item(index);
                    dynamic range = paragraph.Range;
                    dynamic format = paragraph.Format;
                    var start = SafeInt(() => (int)range.Start, -1);
                    var end = SafeInt(() => (int)range.End, -1);
                    var raw = SafeString(() => (string?)range.Text);
                    var visibleLength = raw.Trim('\r', '\n', '\a', '\v').Length;
                    // Range.Style is a VARIANT and Word commonly returns a Style RCW,
                    // not a scalar string. Resolve its public name through the shared
                    // COM-safe reader so diagnostics never publish System.__ComObject.
                    var style = ReadStyleIdentity(range);
                    var outline = SafeInt(() => (int)paragraph.OutlineLevel, 10);
                    var heading = outline is >= 1 and <= 9;
                    if (heading)
                    {
                        headingCount++;
                    }

                    var keepWithNext = WordPropertyTrue(
                        SafeInt(() => (int)format.KeepWithNext, 0)
                    );
                    if (keepWithNext)
                    {
                        if (keepChainLength == 0)
                        {
                            keepChainStart = index;
                        }
                        keepChainLength++;
                    }
                    else
                    {
                        if (keepChainLength >= keepThreshold)
                        {
                            AddIssue(
                                "long_keep_with_next_chain",
                                "warning",
                                keepChainStart,
                                start,
                                end,
                                new { paragraph_count = keepChainLength }
                            );
                        }
                        keepChainLength = 0;
                    }

                    if (visibleLength == 0)
                    {
                        if (emptyRunLength == 0)
                        {
                            emptyRunStart = index;
                        }
                        emptyRunLength++;
                    }
                    else
                    {
                        if (emptyRunLength >= 3)
                        {
                            AddIssue(
                                "empty_paragraph_run",
                                "info",
                                emptyRunStart,
                                start,
                                end,
                                new { paragraph_count = emptyRunLength }
                            );
                        }
                        emptyRunLength = 0;
                    }

                    if (heading && visibleLength > longHeading)
                    {
                        AddIssue(
                            "long_heading",
                            "warning",
                            index,
                            start,
                            end,
                            new { style, character_count = visibleLength }
                        );
                    }
                    if (
                        !heading
                        && WordPropertyTrue(SafeInt(() => (int)format.PageBreakBefore, 0))
                    )
                    {
                        AddIssue(
                            "body_page_break_before",
                            "warning",
                            index,
                            start,
                            end,
                            new { style }
                        );
                    }
                    if (
                        WordPropertyTrue(SafeInt(() => (int)format.KeepTogether, 0))
                        && visibleLength > longKeepTogether
                    )
                    {
                        AddIssue(
                            "oversized_keep_together",
                            "warning",
                            index,
                            start,
                            end,
                            new { style, character_count = visibleLength }
                        );
                    }
                    if (!WordPropertyTrue(SafeInt(() => (int)format.WidowControl, WordTrue)))
                    {
                        AddIssue(
                            "widow_control_disabled",
                            "info",
                            index,
                            start,
                            end,
                            new { style }
                        );
                    }
                    if (raw.Contains('\f'))
                    {
                        AddIssue(
                            "manual_page_break",
                            "info",
                            index,
                            start,
                            end,
                            new { count = raw.Count(character => character == '\f') }
                        );
                    }
                }

                if (keepChainLength >= keepThreshold)
                {
                    AddIssue(
                        "long_keep_with_next_chain",
                        "warning",
                        keepChainStart,
                        -1,
                        -1,
                        new { paragraph_count = keepChainLength }
                    );
                }
                if (emptyRunLength >= 3)
                {
                    AddIssue(
                        "empty_paragraph_run",
                        "info",
                        emptyRunStart,
                        -1,
                        -1,
                        new { paragraph_count = emptyRunLength }
                    );
                }
                if (scanned >= 20 && headingCount > scanned / 2)
                {
                    AddIssue(
                        "heading_style_overuse",
                        "warning",
                        1,
                        -1,
                        -1,
                        new { heading_paragraphs = headingCount, scanned_paragraphs = scanned }
                    );
                }

                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    scanned_paragraphs = scanned,
                    total_paragraphs = total,
                    truncated = scanned < total || issues.Count >= maxIssues,
                    issue_count = issues.Count,
                    issues,
                    document_text_returned = false,
                    rules = new[]
                    {
                        "long_keep_with_next_chain",
                        "long_heading",
                        "body_page_break_before",
                        "oversized_keep_together",
                        "widow_control_disabled",
                        "empty_paragraph_run",
                        "manual_page_break",
                        "heading_style_overuse",
                    },
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            WordComReplaySafety.ReplaySafe,
            cancellationToken
        );
    }

    private Task<object> InspectEquationLearning()
    {
        var snapshot = _equationLearning
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return Task.FromResult<object>(
            new
            {
                observation_count = snapshot.Values.Sum(),
                outcomes = snapshot,
                content_stored = false,
                formula_text_stored = false,
                document_text_stored = false,
                path_exposed = false,
                runtime = "dotnet-native",
            }
        );
    }

    private Task<object> InspectStructureLearning()
    {
        var map = _structureMapObservations
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var inspection = _structureInspectionObservations
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return Task.FromResult<object>(
            new
            {
                observation_count = map.Values.Sum(),
                inspection_observation_count = inspection.Values.Sum(),
                map_observations = map,
                inspection_observations = inspection,
                adaptive_rescan_policy = "presence observations 1, 2, 4, 8, 16, ...",
                content_stored = false,
                document_counts_stored = false,
                property_values_stored = false,
                path_exposed = false,
                runtime = "dotnet-native",
            }
        );
    }

    private static dynamic? StructureCollection(dynamic document, string structure)
    {
        try
        {
            return structure switch
            {
                "paragraphs" => document.Paragraphs,
                "sections" => document.Sections,
                "styles" => document.Styles,
                "tables" => document.Tables,
                "equations" => document.OMaths,
                "fields" => document.Fields,
                "form_fields" => document.FormFields,
                "bookmarks" => document.Bookmarks,
                "hyperlinks" => document.Hyperlinks,
                "comments" => document.Comments,
                "revisions" => document.Revisions,
                "content_controls" => document.ContentControls,
                "inline_shapes" => document.InlineShapes,
                "floating_shapes" => document.Shapes,
                "footnotes" => document.Footnotes,
                "endnotes" => document.Endnotes,
                "lists" => document.Lists,
                "list_paragraphs" => document.ListParagraphs,
                "subdocuments" => document.Subdocuments,
                "variables" => document.Variables,
                "tables_of_contents" => document.TablesOfContents,
                "tables_of_figures" => document.TablesOfFigures,
                "tables_of_authorities" => document.TablesOfAuthorities,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static int? StructureCount(dynamic document, string structure)
    {
        dynamic? collection = StructureCollection(document, structure);
        if (collection is null)
        {
            return null;
        }
        try
        {
            return (int)collection.Count;
        }
        catch
        {
            return null;
        }
    }

    private static object StructureTypeHistogram(
        dynamic document,
        string structure,
        string propertyPath,
        int maxItems
    )
    {
        dynamic? collection = StructureCollection(document, structure);
        if (collection is null)
        {
            return new
            {
                available = false,
                scanned = 0,
                truncated = false,
                read_errors = 0,
                types = new Dictionary<string, int>(),
            };
        }
        var total = SafeInt(() => (int)collection.Count, 0);
        var scanned = Math.Min(total, maxItems);
        var errors = 0;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var index = 1; index <= scanned; index++)
        {
            try
            {
                dynamic item = collection.Item(index);
                object? value = ReadPropertyPath(item, propertyPath);
                string key = Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
                counts[key] = counts.TryGetValue(key, out int currentCount)
                    ? currentCount + 1
                    : 1;
            }
            catch
            {
                errors++;
            }
        }
        return new
        {
            available = true,
            total,
            scanned,
            truncated = scanned < total,
            read_errors = errors,
            types = counts,
        };
    }

    private static object StructureItemPayload(
        string structure,
        dynamic item,
        int index,
        bool includeText,
        int maxTextChars
    )
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        dynamic? textRange = null;

        void Range(string name, Func<dynamic> getRange)
        {
            try
            {
                dynamic range = getRange();
                properties[name] = new
                {
                    start = (int)range.Start,
                    end = (int)range.End,
                };
                textRange ??= range;
            }
            catch
            {
                // One unsupported property must not destroy the page.
            }
        }

        void Integer(string name, Func<int> read)
        {
            try
            {
                properties[name] = read();
            }
            catch
            {
                // Optional native metadata.
            }
        }

        void Number(string name, Func<double> read)
        {
            try
            {
                properties[name] = Math.Round(read(), 3);
            }
            catch
            {
                // Optional native metadata.
            }
        }

        void Boolean(string name, Func<bool> read)
        {
            try
            {
                properties[name] = read();
            }
            catch
            {
                // Optional native metadata.
            }
        }

        void Text(string name, Func<object?> read, int limit = 512)
        {
            try
            {
                var raw = read();
                // Never serialize an unresolved COM wrapper as its runtime type
                // name (for example, System.__ComObject).  Such values are not
                // client-usable scalar metadata, so omit the optional field.
                if (raw is not null && Marshal.IsComObject(raw))
                {
                    return;
                }
                var value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
                properties[name] = value[..Math.Min(value.Length, limit)];
            }
            catch
            {
                // Optional native metadata.
            }
        }

        void StyleText(string name, Func<object?> read)
        {
            object? raw = null;
            try
            {
                raw = read();
                var value = ReadStyleValueIdentity(raw);
                if (value.Length > 0)
                {
                    properties[name] = value[..Math.Min(value.Length, 512)];
                }
            }
            catch
            {
                // Optional native metadata.
            }
            finally
            {
                FinalReleaseBatchComObject(raw);
            }
        }

        switch (structure)
        {
            case "paragraphs":
                Range("range", () => item.Range);
                StyleText("style", () => item.Style);
                Integer("outline_level", () => (int)item.OutlineLevel);
                break;
            case "sections":
                Range("range", () => item.Range);
                Integer("index", () => (int)item.Index);
                Integer("start_type", () => (int)item.Start);
                break;
            case "styles":
                Text("name_local", () => item.NameLocal);
                Integer("type", () => (int)item.Type);
                Boolean("built_in", () => (bool)item.BuiltIn);
                Boolean("in_use", () => (bool)item.InUse);
                Boolean("automatically_update", () => (bool)item.AutomaticallyUpdate);
                break;
            case "tables":
                Range("range", () => item.Range);
                Integer("row_count", () => (int)item.Rows.Count);
                Integer("column_count", () => (int)item.Columns.Count);
                Integer("nesting_level", () => (int)item.NestingLevel);
                StyleText("style", () => item.Style);
                Boolean("allow_autofit", () => (bool)item.AllowAutoFit);
                break;
            case "equations":
                Range("range", () => item.Range);
                Integer("type", () => (int)item.Type);
                break;
            case "fields":
                Range("result_range", () => item.Result);
                Integer("type", () => (int)item.Type);
                Boolean("locked", () => (bool)item.Locked);
                textRange = item.Result;
                break;
            case "form_fields":
                Range("range", () => item.Range);
                Integer("type", () => (int)item.Type);
                Text("name", () => (string?)item.Name);
                Boolean("enabled", () => (bool)item.Enabled);
                break;
            case "bookmarks":
                Range("range", () => item.Range);
                Text("name", () => (string?)item.Name);
                break;
            case "hyperlinks":
                Range("range", () => item.Range);
                Integer("type", () => (int)item.Type);
                Text("name", () => (string?)item.Name);
                Boolean("has_external_address", () => !string.IsNullOrEmpty((string?)item.Address));
                Boolean("has_internal_target", () => !string.IsNullOrEmpty((string?)item.SubAddress));
                break;
            case "comments":
                Range("scope_range", () => item.Scope);
                Range("comment_range", () => item.Range);
                Text("author", () => (string?)item.Author);
                Text("initials", () => (string?)item.Initial);
                Text("date", () => Convert.ToString(item.Date, CultureInfo.InvariantCulture));
                textRange = item.Range;
                break;
            case "revisions":
                Range("range", () => item.Range);
                Integer("type", () => (int)item.Type);
                Text("author", () => (string?)item.Author);
                Text("date", () => Convert.ToString(item.Date, CultureInfo.InvariantCulture));
                break;
            case "content_controls":
                Range("range", () => item.Range);
                Integer("id", () => (int)item.ID);
                Integer("type", () => (int)item.Type);
                Text("title", () => (string?)item.Title);
                Text("tag", () => (string?)item.Tag);
                Boolean("lock_contents", () => (bool)item.LockContents);
                Boolean("lock_control", () => (bool)item.LockContentControl);
                Boolean("showing_placeholder_text", () => (bool)item.ShowingPlaceholderText);
                break;
            case "inline_shapes":
                Range("range", () => item.Range);
                Integer("type", () => (int)item.Type);
                Number("width_points", () => (double)item.Width);
                Number("height_points", () => (double)item.Height);
                Text("alternative_text", () => (string?)item.AlternativeText, 2_000);
                Text("title", () => (string?)item.Title);
                Boolean("has_chart", () => (bool)item.HasChart);
                Boolean("has_smart_art", () => (bool)item.HasSmartArt);
                break;
            case "floating_shapes":
                Range("anchor_range", () => item.Anchor);
                Integer("type", () => (int)item.Type);
                Text("name", () => (string?)item.Name);
                Number("width_points", () => (double)item.Width);
                Number("height_points", () => (double)item.Height);
                Text("alternative_text", () => (string?)item.AlternativeText, 2_000);
                Text("title", () => (string?)item.Title);
                Boolean("lock_anchor", () => WordPropertyTrue((int)item.LockAnchor));
                Boolean("visible", () => WordPropertyTrue((int)item.Visible));
                break;
            case "footnotes":
            case "endnotes":
                Range("range", () => item.Range);
                Range("reference_range", () => item.Reference);
                Integer("index", () => (int)item.Index);
                break;
            case "lists":
                Range("range", () => item.Range);
                Integer("list_type", () => (int)item.Range.ListFormat.ListType);
                break;
            case "list_paragraphs":
                Range("range", () => item.Range);
                Integer("list_type", () => (int)item.Range.ListFormat.ListType);
                StyleText("style", () => item.Style);
                break;
            case "subdocuments":
                Range("range", () => item.Range);
                Boolean("locked", () => (bool)item.Locked);
                break;
            case "variables":
                Text("name", () => (string?)item.Name);
                if (includeText)
                {
                    Text("value_preview", () => (string?)item.Value, maxTextChars);
                }
                break;
            case "tables_of_contents":
                Range("range", () => item.Range);
                Boolean("use_heading_styles", () => (bool)item.UseHeadingStyles);
                Integer("upper_heading_level", () => (int)item.UpperHeadingLevel);
                Integer("lower_heading_level", () => (int)item.LowerHeadingLevel);
                break;
            case "tables_of_figures":
                Range("range", () => item.Range);
                Text("caption_label", () => (string?)item.Caption);
                break;
            case "tables_of_authorities":
                Range("range", () => item.Range);
                Integer("category", () => (int)item.Category);
                break;
        }

        string? preview = null;
        var truncated = false;
        if (includeText && textRange is not null)
        {
            var raw = SafeString(() => (string?)textRange.Text);
            var cleaned = CleanWordPreview(raw);
            truncated = cleaned.Length > maxTextChars;
            preview = cleaned[..Math.Min(cleaned.Length, maxTextChars)];
        }
        return new
        {
            item_index = index,
            properties,
            text_preview = preview,
            text_truncated = truncated,
            read_error = "",
        };
    }

    private static object? ReadPropertyPath(dynamic value, string path)
    {
        dynamic current = value;
        foreach (var segment in path.Split('.'))
        {
            current = current.GetType().InvokeMember(
                segment,
                System.Reflection.BindingFlags.GetProperty,
                null,
                current,
                null,
                CultureInfo.InvariantCulture
            );
        }
        return current;
    }

    private static bool WordPropertyTrue(int value)
    {
        return value is not 0 and not 9_999_999;
    }

    private static int SafeInt(Func<int> read, int fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private static string CleanWordPreview(string value)
    {
        return value
            .Replace('\r', '\n')
            .Replace('\a', ' ')
            .Replace('\v', '\n')
            .Replace("\0", "", StringComparison.Ordinal);
    }
}
