using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int TableOfContentsLimit = 10_000;

    private async Task<object> InsertTableOfContentsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for table-of-contents insertion"
            );
        var target = arguments.String("target", "document_start");
        if (target is not ("cursor" or "document_start" or "document_end"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "target must be cursor, document_start, or document_end"
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
        var upperHeadingLevelValue = arguments.NullableInt64("upper_heading_level") ?? 1;
        var lowerHeadingLevelValue = arguments.NullableInt64("lower_heading_level") ?? 3;
        if (
            upperHeadingLevelValue is < 1 or > 9
            || lowerHeadingLevelValue is < 1 or > 9
            || lowerHeadingLevelValue < upperHeadingLevelValue
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "heading levels must be between 1 and 9 and lower_heading_level must not be less than upper_heading_level"
            );
        }
        var useHeadingStyles = arguments.Boolean("use_heading_styles", true);
        var useOutlineLevels = arguments.Boolean("use_outline_levels", false);
        if (!useHeadingStyles && !useOutlineLevels)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "At least one of use_heading_styles or use_outline_levels must be true"
            );
        }
        var includePageNumbers = arguments.Boolean("include_page_numbers", true);
        var rightAlignPageNumbers = arguments.Boolean("right_align_page_numbers", true);
        var useHyperlinks = arguments.Boolean("use_hyperlinks", true);
        var hidePageNumbersInWeb = arguments.Boolean("hide_page_numbers_in_web", true);
        var repaginate = arguments.Boolean("repaginate", true);
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
                var tablesBefore = (int)document.TablesOfContents.Count;
                if (tablesBefore >= TableOfContentsLimit)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        $"The live document already has the maximum supported {TableOfContentsLimit} tables of contents"
                    );
                }
                dynamic insertion = target == "document_start"
                    ? document.Range(0, 0)
                    : ResolveInsertionRange(
                        (object)application,
                        (object)document,
                        record,
                        target,
                        selectionToken,
                        replaceSelection: false
                    );
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                    undoRecord.StartCustomRecord("WordToolkit: insert native table of contents");
                    undoStarted = true;
                    dynamic tableOfContents = document.TablesOfContents.Add(
                        insertion,
                        useHeadingStyles,
                        (int)upperHeadingLevelValue,
                        (int)lowerHeadingLevelValue,
                        false,
                        "",
                        rightAlignPageNumbers,
                        includePageNumbers,
                        "",
                        useHyperlinks,
                        hidePageNumbersInWeb,
                        useOutlineLevels
                    );
                    var repaginationPerformed = false;
                    if (repaginate)
                    {
                        document.Repaginate();
                        repaginationPerformed = true;
                    }
                    if (update)
                    {
                        tableOfContents.Update();
                    }
                    var tablesAfter = (int)document.TablesOfContents.Count;
                    dynamic insertedRange = tableOfContents.Range.Duplicate;
                    var insertedStart = (int)insertedRange.Start;
                    var insertedEnd = (int)insertedRange.End;
                    var insertedFieldCount = (int)insertedRange.Fields.Count;
                    var insertedIndex = FindExactTableOfContentsIndex(
                        document,
                        insertedStart,
                        insertedEnd
                    );
                    if (
                        tablesAfter != tablesBefore + 1
                        || insertedIndex < 1
                        || insertedFieldCount < 1
                        || insertedEnd <= insertedStart
                    )
                    {
                        throw new NativeToolException(
                            "VALIDATION_FAILED",
                            "Word did not create one readable native table-of-contents field",
                            new
                            {
                                table_count_before = tablesBefore,
                                table_count_after = tablesAfter,
                                exact_range_matches = insertedIndex < 1 ? 0 : 1,
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
                        operation_contract = "wordtoolkit.insert_live_word_table_of_contents/1.0",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        target,
                        table_of_contents_count_before = tablesBefore,
                        table_of_contents_count_after = tablesAfter,
                        table_of_contents_index = insertedIndex,
                        inserted_range = new { start = insertedStart, end = insertedEnd },
                        inserted_field_count = insertedFieldCount,
                        updated = update,
                        repagination = new
                        {
                            requested = repaginate,
                            performed = repaginationPerformed,
                        },
                        options = new
                        {
                            upper_heading_level = upperHeadingLevelValue,
                            lower_heading_level = lowerHeadingLevelValue,
                            use_heading_styles = useHeadingStyles,
                            use_outline_levels = useOutlineLevels,
                            include_page_numbers = includePageNumbers,
                            right_align_page_numbers = rightAlignPageNumbers,
                            use_hyperlinks = useHyperlinks,
                            hide_page_numbers_in_web = hidePageNumbersInWeb,
                        },
                        native_verified = true,
                        raw_field_code_returned = false,
                        result_text_returned = false,
                        raw_com_objects_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        undoRecord is not null,
                        rollbackSnapshot,
                        record,
                        exception
                    );
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

    private static int FindExactTableOfContentsIndex(
        dynamic document,
        int expectedStart,
        int expectedEnd
    )
    {
        var count = (int)document.TablesOfContents.Count;
        var match = 0;
        for (var index = 1; index <= count; index++)
        {
            dynamic range = document.TablesOfContents.Item(index).Range.Duplicate;
            if ((int)range.Start != expectedStart || (int)range.End != expectedEnd)
            {
                continue;
            }
            if ((int)range.Fields.Count < 1 || match != 0)
            {
                return 0;
            }
            match = index;
        }
        return match;
    }
}
