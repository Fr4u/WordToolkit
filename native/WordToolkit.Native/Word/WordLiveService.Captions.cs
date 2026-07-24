using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int WordCaptionFigure = -1;
    private const int WordCaptionTable = -2;
    private const int WordCaptionEquation = -3;
    private const int WordCaptionPositionAbove = 0;
    private const int WordCaptionPositionBelow = 1;
    private const int WordFieldSequence = 12;
    private const int CaptionFieldScanLimit = 50_000;
    private const int CaptionLabelScanLimit = 1_024;
    private const int ReferenceTableUpdateLimit = 128;
    private const int CaptionTitleLimit = 4_096;
    private const int CustomCaptionLabelLimit = 256;
    private static readonly Regex SequenceFieldLabelPattern = new(
        "^\\s*SEQ\\s+(?:\"(?<quoted>[^\"]+)\"|(?<plain>[^\\s\\\\]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private async Task<object> InsertCaptionAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for native caption insertion"
            );
        var selectionToken = arguments.String("selection_token");
        if (selectionToken.Length is < 1 or > 128)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "A fresh bounded selection_token is required"
            );
        }
        var captionKind = arguments.String("caption_kind", "figure");
        var customLabel = arguments.String("custom_label");
        ValidateCaptionKind(captionKind, customLabel);
        var title = arguments.String("title");
        ValidateSingleLineCaptionValue(title, CaptionTitleLimit, "title");
        var separator = arguments.String("separator", "space");
        var titleSuffix = BuildCaptionTitleSuffix(title, separator);
        var requestedPosition = arguments.String("position", "automatic");
        if (requestedPosition is not ("automatic" or "above" or "below"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "position must be automatic, above, or below"
            );
        }
        var excludeLabel = arguments.Boolean("exclude_label", false);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                dynamic targetRange = ResolveVerifiedSelectionRange(
                    (object)application,
                    (object)document,
                    record,
                    selectionToken,
                    requireNonEmpty: false
                );
                var label = ResolveCaptionLabel(application, captionKind, customLabel);
                var resolvedPosition = ResolveCaptionPosition(
                    label,
                    requestedPosition
                );
                var sequenceCountBefore = CountSequenceFields(document, label.Name);
                var allFieldsBefore = CheckedFieldCount(document);
                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: insert native caption");
                    undoStarted = true;
                    targetRange.InsertCaption(
                        label.InsertValue,
                        titleSuffix,
                        Type.Missing,
                        resolvedPosition.Value,
                        excludeLabel
                    );
                    var sequenceCountAfter = CountSequenceFields(document, label.Name);
                    var allFieldsAfter = CheckedFieldCount(document);
                    if (
                        sequenceCountAfter != sequenceCountBefore + 1
                        || allFieldsAfter < allFieldsBefore + 1
                    )
                    {
                        throw new NativeToolException(
                            "VALIDATION_FAILED",
                            "Word did not create exactly one native caption sequence field",
                            new
                            {
                                sequence_count_before = sequenceCountBefore,
                                sequence_count_after = sequenceCountAfter,
                                field_count_before = allFieldsBefore,
                                field_count_after = allFieldsAfter,
                            }
                        );
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version++;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);
                    return new
                    {
                        operation_contract = "wordtoolkit.insert_live_word_caption/1.0",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        caption_kind = captionKind,
                        custom_label_used = label.Custom,
                        position = resolvedPosition.Name,
                        exclude_label = excludeLabel,
                        title_length = title.Length,
                        sequence_field_count_before = sequenceCountBefore,
                        sequence_field_count_after = sequenceCountAfter,
                        native_verified = true,
                        raw_field_code_returned = false,
                        raw_com_objects_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                catch
                {
                    Rollback(document, undoRecord, ref undoStarted);
                    throw;
                }
                finally
                {
                    if (originalScreenUpdating is not null)
                    {
                        application.ScreenUpdating = originalScreenUpdating.Value;
                    }
                }
            },
            WordComReplaySafety.NonReplayable,
            cancellationToken
        );
    }

    private async Task<object> InsertTableOfFiguresAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for table-of-figures insertion"
            );
        var target = arguments.String("target", "document_end");
        if (target is not ("cursor" or "document_end"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "target must be cursor or document_end"
            );
        }
        var selectionToken = arguments.String("selection_token");
        if (target == "cursor" && selectionToken.Length is < 1 or > 128)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "A fresh bounded selection_token is required for target=cursor"
            );
        }
        var captionKind = arguments.String("caption_kind", "figure");
        var customLabel = arguments.String("custom_label");
        ValidateCaptionKind(captionKind, customLabel);
        var includeLabel = arguments.Boolean("include_label", true);
        var includePageNumbers = arguments.Boolean("include_page_numbers", true);
        var rightAlignPageNumbers = arguments.Boolean("right_align_page_numbers", true);
        var useHyperlinks = arguments.Boolean("use_hyperlinks", true);
        var hidePageNumbersInWeb = arguments.Boolean("hide_page_numbers_in_web", true);
        var update = arguments.Boolean("update", true);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var label = ResolveCaptionLabel(application, captionKind, customLabel);
                var matchingCaptionCount = CountSequenceFields(document, label.Name);
                if (matchingCaptionCount == 0)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The live document has no matching native captions for this table of figures"
                    );
                }
                dynamic insertion = ResolveInsertionRange(
                    (object)application,
                    (object)document,
                    record,
                    target,
                    selectionToken,
                    replaceSelection: false
                );
                var tablesBefore = (int)document.TablesOfFigures.Count;
                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: insert native table of figures");
                    undoStarted = true;
                    dynamic tableOfFigures = document.TablesOfFigures.Add(
                        insertion,
                        label.Name,
                        includeLabel,
                        false,
                        1,
                        9,
                        false,
                        "",
                        rightAlignPageNumbers,
                        includePageNumbers,
                        "",
                        useHyperlinks,
                        hidePageNumbersInWeb
                    );
                    if (update)
                    {
                        tableOfFigures.Update();
                    }
                    var tablesAfter = (int)document.TablesOfFigures.Count;
                    dynamic insertedRange = tableOfFigures.Range.Duplicate;
                    var insertedFieldCount = (int)insertedRange.Fields.Count;
                    if (
                        tablesAfter != tablesBefore + 1
                        || insertedFieldCount < 1
                        || (int)insertedRange.End <= (int)insertedRange.Start
                    )
                    {
                        throw new NativeToolException(
                            "VALIDATION_FAILED",
                            "Word did not create one readable native table-of-figures field",
                            new
                            {
                                table_count_before = tablesBefore,
                                table_count_after = tablesAfter,
                                inserted_field_count = insertedFieldCount,
                            }
                        );
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version++;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);
                    return new
                    {
                        operation_contract = "wordtoolkit.insert_live_word_table_of_figures/1.0",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        caption_kind = captionKind,
                        custom_label_used = label.Custom,
                        matching_caption_count = matchingCaptionCount,
                        table_of_figures_count_before = tablesBefore,
                        table_of_figures_count_after = tablesAfter,
                        inserted_range = new
                        {
                            start = (int)insertedRange.Start,
                            end = (int)insertedRange.End,
                        },
                        inserted_field_count = insertedFieldCount,
                        updated = update,
                        options = new
                        {
                            include_label = includeLabel,
                            include_page_numbers = includePageNumbers,
                            right_align_page_numbers = rightAlignPageNumbers,
                            use_hyperlinks = useHyperlinks,
                            hide_page_numbers_in_web = hidePageNumbersInWeb,
                        },
                        native_verified = true,
                        raw_field_code_returned = false,
                        raw_com_objects_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                catch
                {
                    Rollback(document, undoRecord, ref undoStarted);
                    throw;
                }
                finally
                {
                    if (originalScreenUpdating is not null)
                    {
                        application.ScreenUpdating = originalScreenUpdating.Value;
                    }
                }
            },
            WordComReplaySafety.NonReplayable,
            cancellationToken
        );
    }

    private async Task<object> UpdateReferenceTablesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for reference-table updates"
            );
        var kind = arguments.String("kind", "all");
        if (
            kind is not (
                "all"
                or "table_of_contents"
                or "table_of_figures"
                or "table_of_authorities"
                or "index"
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "kind must be all, table_of_contents, table_of_figures, table_of_authorities, or index"
            );
        }
        var indexValue = arguments.NullableInt64("index");
        int? requestedIndex = null;
        if (indexValue is not null)
        {
            if (indexValue.Value is < 1 or > 10_000)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "index must be between 1 and 10,000"
                );
            }
            if (kind == "all")
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "index requires one exact reference-table kind"
                );
            }
            requestedIndex = (int)indexValue.Value;
        }
        var repaginate = arguments.Boolean("repaginate", true);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var countsBefore = CaptureReferenceTableCounts((object)document);
                var targets = ResolveReferenceTableTargets(
                    (object)document,
                    kind,
                    requestedIndex,
                    countsBefore
                );
                foreach (var target in targets)
                {
                    ValidateReferenceTableObject(target.Native, target.Kind, target.Index);
                }

                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: update native reference tables");
                    undoStarted = true;
                    var repaginationPerformed = false;
                    if (repaginate)
                    {
                        document.Repaginate();
                        repaginationPerformed = true;
                    }
                    foreach (var target in targets)
                    {
                        dynamic native = target.Native;
                        native.Update();
                    }

                    var countsAfter = CaptureReferenceTableCounts((object)document);
                    if (countsAfter != countsBefore)
                    {
                        throw new NativeToolException(
                            "VALIDATION_FAILED",
                            "Word changed a reference-table collection during update",
                            new
                            {
                                before = ReferenceTableCountsPayload(countsBefore),
                                after = ReferenceTableCountsPayload(countsAfter),
                            }
                        );
                    }
                    foreach (var target in targets)
                    {
                        dynamic collection = ResolveReferenceTableCollection(
                            (object)document,
                            target.Kind
                        );
                        dynamic native = collection.Item(target.Index);
                        ValidateReferenceTableObject(native, target.Kind, target.Index);
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version++;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);
                    return new
                    {
                        operation_contract = "wordtoolkit.update_live_word_reference_tables/1.1",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        requested_kind = kind,
                        requested_index = requestedIndex,
                        updated_count = targets.Count,
                        updated_counts = new
                        {
                            tables_of_contents = targets.Count(item =>
                                item.Kind == "table_of_contents"
                            ),
                            tables_of_figures = targets.Count(item =>
                                item.Kind == "table_of_figures"
                            ),
                            tables_of_authorities = targets.Count(item =>
                                item.Kind == "table_of_authorities"
                            ),
                            indexes = targets.Count(item => item.Kind == "index"),
                        },
                        counts_before = ReferenceTableCountsPayload(countsBefore),
                        counts_after = ReferenceTableCountsPayload(countsAfter),
                        repagination = new
                        {
                            requested = repaginate,
                            performed = repaginationPerformed,
                        },
                        ranges_and_fields_verified = true,
                        native_verified = true,
                        raw_field_code_returned = false,
                        result_text_returned = false,
                        raw_com_objects_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                catch
                {
                    Rollback(document, undoRecord, ref undoStarted);
                    throw;
                }
                finally
                {
                    if (originalScreenUpdating is not null)
                    {
                        application.ScreenUpdating = originalScreenUpdating.Value;
                    }
                }
            },
            WordComReplaySafety.NonReplayable,
            cancellationToken
        );
    }

    private static ReferenceTableCounts CaptureReferenceTableCounts(object documentObject)
    {
        dynamic document = documentObject;
        return new ReferenceTableCounts(
            (int)document.TablesOfContents.Count,
            (int)document.TablesOfFigures.Count,
            (int)document.TablesOfAuthorities.Count,
            (int)document.Indexes.Count
        );
    }

    private static object ReferenceTableCountsPayload(ReferenceTableCounts counts) =>
        new
        {
            tables_of_contents = counts.TablesOfContents,
            tables_of_figures = counts.TablesOfFigures,
            tables_of_authorities = counts.TablesOfAuthorities,
            indexes = counts.Indexes,
        };

    private static List<ReferenceTableTarget> ResolveReferenceTableTargets(
        object documentObject,
        string requestedKind,
        int? requestedIndex,
        ReferenceTableCounts counts
    )
    {
        dynamic document = documentObject;
        var kinds = requestedKind == "all"
            ? new[]
            {
                "table_of_contents",
                "table_of_figures",
                "table_of_authorities",
                "index",
            }
            : new[] { requestedKind };
        var requestedCount = requestedIndex is not null
            ? 1L
            : kinds.Sum(kind => (long)counts.ForKind(kind));
        if (requestedCount == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "The live document has no matching reference tables to update"
            );
        }
        if (requestedCount > ReferenceTableUpdateLimit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"One reference-table update is limited to {ReferenceTableUpdateLimit} objects"
            );
        }

        var targets = new List<ReferenceTableTarget>((int)requestedCount);
        foreach (var kind in kinds)
        {
            dynamic collection = ResolveReferenceTableCollection(
                (object)document,
                kind
            );
            var count = counts.ForKind(kind);
            if (requestedIndex is not null)
            {
                if (requestedIndex.Value > count)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The requested reference-table index does not exist"
                    );
                }
                targets.Add(
                    new ReferenceTableTarget(
                        kind,
                        requestedIndex.Value,
                        (object)collection.Item(requestedIndex.Value)
                    )
                );
                continue;
            }
            for (var index = 1; index <= count; index++)
            {
                targets.Add(
                    new ReferenceTableTarget(kind, index, (object)collection.Item(index))
                );
            }
        }
        return targets;
    }

    private static object ResolveReferenceTableCollection(
        object documentObject,
        string kind
    )
    {
        dynamic document = documentObject;
        return kind switch
        {
            "table_of_contents" => (object)document.TablesOfContents,
            "table_of_figures" => (object)document.TablesOfFigures,
            "table_of_authorities" => (object)document.TablesOfAuthorities,
            "index" => (object)document.Indexes,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "Unsupported reference-table kind"
            ),
        };
    }

    private static void ValidateReferenceTableObject(
        object nativeObject,
        string kind,
        int index
    )
    {
        dynamic native = nativeObject;
        dynamic range = native.Range.Duplicate;
        var start = (int)range.Start;
        var end = (int)range.End;
        var fieldCount = (int)range.Fields.Count;
        if (start < 0 || end <= start || fieldCount < 1)
        {
            throw new NativeToolException(
                "VALIDATION_FAILED",
                "A native reference table has no readable field range",
                new { kind, index, start, end, field_count = fieldCount }
            );
        }
    }

    private static CaptionLabelResolution ResolveCaptionLabel(
        dynamic application,
        string captionKind,
        string customLabel
    )
    {
        dynamic labels = application.CaptionLabels;
        if (captionKind != "custom")
        {
            var builtInId = captionKind switch
            {
                "figure" => WordCaptionFigure,
                "table" => WordCaptionTable,
                "equation" => WordCaptionEquation,
                _ => throw new NativeToolException(
                    "INVALID_INPUT",
                    "caption_kind must be figure, table, equation, or custom"
                ),
            };
            dynamic native = labels.Item(builtInId);
            var name = Convert.ToString(native.Name, CultureInfo.InvariantCulture) ?? "";
            if (name.Length == 0)
            {
                throw new NativeToolException(
                    "EXTERNAL_TOOL_FAILED",
                    "Word did not resolve the requested built-in caption label"
                );
            }
            return new CaptionLabelResolution(builtInId, name, native, false);
        }

        dynamic? matched = null;
        var matchCount = 0;
        var count = (int)labels.Count;
        if (count > CaptionLabelScanLimit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"Caption label inspection is limited to {CaptionLabelScanLimit} labels"
            );
        }
        for (var index = 1; index <= count; index++)
        {
            dynamic candidate = labels.Item(index);
            var name = Convert.ToString(candidate.Name, CultureInfo.InvariantCulture) ?? "";
            if (!string.Equals(name, customLabel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            matched = candidate;
            matchCount++;
        }
        if (matchCount != 1 || matched is null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "The requested custom caption label does not resolve to exactly one existing Word label"
            );
        }
        var resolvedName = Convert.ToString(matched.Name, CultureInfo.InvariantCulture) ?? "";
        return new CaptionLabelResolution(resolvedName, resolvedName, matched, true);
    }

    private static CaptionPositionResolution ResolveCaptionPosition(
        CaptionLabelResolution label,
        string requestedPosition
    )
    {
        var value = requestedPosition switch
        {
            "above" => WordCaptionPositionAbove,
            "below" => WordCaptionPositionBelow,
            _ => (int)label.Native.Position,
        };
        if (value is not (WordCaptionPositionAbove or WordCaptionPositionBelow))
        {
            throw new NativeToolException(
                "EXTERNAL_TOOL_FAILED",
                "Word returned an unsupported caption position"
            );
        }
        return new CaptionPositionResolution(
            value,
            value == WordCaptionPositionAbove ? "above" : "below"
        );
    }

    private static int CountSequenceFields(dynamic document, string labelName)
    {
        dynamic fields = document.Fields;
        var count = CheckedFieldCount(document);
        var matches = 0;
        for (var index = 1; index <= count; index++)
        {
            dynamic field = fields.Item(index);
            if ((int)field.Type != WordFieldSequence)
            {
                continue;
            }
            var code = Convert.ToString(field.Code.Text, CultureInfo.InvariantCulture) ?? "";
            var match = SequenceFieldLabelPattern.Match(code);
            if (!match.Success)
            {
                continue;
            }
            var parsedLabel = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["plain"].Value;
            if (string.Equals(parsedLabel, labelName, StringComparison.OrdinalIgnoreCase))
            {
                matches++;
            }
        }
        return matches;
    }

    private static int CheckedFieldCount(dynamic document)
    {
        var count = (int)document.Fields.Count;
        if (count > CaptionFieldScanLimit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"Caption field inspection is limited to {CaptionFieldScanLimit} main-story fields"
            );
        }
        return count;
    }

    private static void ValidateCaptionKind(string captionKind, string customLabel)
    {
        if (captionKind is not ("figure" or "table" or "equation" or "custom"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "caption_kind must be figure, table, equation, or custom"
            );
        }
        if (captionKind == "custom")
        {
            if (customLabel.Length is < 1 or > CustomCaptionLabelLimit)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"custom_label must contain between 1 and {CustomCaptionLabelLimit} characters"
                );
            }
            ValidateSingleLineCaptionValue(
                customLabel,
                CustomCaptionLabelLimit,
                "custom_label"
            );
        }
        else if (customLabel.Length > 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "custom_label is accepted only when caption_kind=custom"
            );
        }
    }

    private static void ValidateSingleLineCaptionValue(
        string value,
        int limit,
        string parameterName
    )
    {
        if (value.Length > limit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"{parameterName} exceeds the supported {limit}-character limit"
            );
        }
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{parameterName} must be a single-line value without NUL"
            );
        }
    }

    private static string BuildCaptionTitleSuffix(string title, string separator)
    {
        if (separator is not ("space" or "colon" or "dash" or "em_dash" or "none"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "separator must be space, colon, dash, em_dash, or none"
            );
        }
        if (title.Length == 0)
        {
            return "";
        }
        return separator switch
        {
            "space" => $" {title}",
            "colon" => $": {title}",
            "dash" => $" - {title}",
            "em_dash" => $" — {title}",
            _ => title,
        };
    }

    private sealed record CaptionLabelResolution(
        object InsertValue,
        string Name,
        dynamic Native,
        bool Custom
    );

    private sealed record CaptionPositionResolution(int Value, string Name);

    private sealed record ReferenceTableTarget(string Kind, int Index, object Native);

    private sealed record ReferenceTableCounts(
        int TablesOfContents,
        int TablesOfFigures,
        int TablesOfAuthorities,
        int Indexes
    )
    {
        public int ForKind(string kind) =>
            kind switch
            {
                "table_of_contents" => TablesOfContents,
                "table_of_figures" => TablesOfFigures,
                "table_of_authorities" => TablesOfAuthorities,
                "index" => Indexes,
                _ => 0,
            };
    }
}
