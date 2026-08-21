using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int WordFormulaFieldType = 34;
    private const char WordFieldMarker = '\uE000';
    private static readonly Regex BookmarkNamePattern = new(
        "^[A-Za-z][A-Za-z0-9_]{0,39}$",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex SequenceNamePattern = new(
        "^[A-Za-z][A-Za-z0-9_]{0,30}$",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex NumericFormatPattern = new(
        "^[0#.,%$€£¥()\\-+ ]+$",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex DateFormatPattern = new(
        "^[A-Za-z0-9\\s.,:/\\-]+$",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex FormulaCharactersPattern = new(
        "^[0-9A-Za-z_+\\-*/^%(),.<>=\\s]+$",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex FormulaWordsPattern = new(
        "[A-Za-z_]+",
        RegexOptions.CultureInvariant
    );
    private static readonly HashSet<string> SafeFormulaWords = new(
        [
            "ABS",
            "AND",
            "AVERAGE",
            "COUNT",
            "FALSE",
            "IF",
            "INT",
            "MAX",
            "MIN",
            "MOD",
            "NOT",
            "OR",
            "PRODUCT",
            "ROUND",
            "SIGN",
            "SUM",
            "TRUE",
        ],
        StringComparer.Ordinal
    );
    private static readonly IReadOnlyDictionary<string, int> SafeFieldTypes =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["page"] = 33,
            ["num_pages"] = 26,
            ["section"] = 65,
            ["section_pages"] = 66,
            ["date"] = 31,
            ["time"] = 32,
            ["create_date"] = 21,
            ["save_date"] = 22,
            ["print_date"] = 23,
            ["file_name"] = 29,
            ["author"] = 17,
            ["title"] = 15,
            ["subject"] = 16,
            ["word_count"] = 27,
            ["character_count"] = 28,
            ["sequence"] = 12,
            ["reference"] = 3,
            ["formula"] = WordFormulaFieldType,
        };
    private static readonly HashSet<string> DateFieldKinds = new(
        ["date", "time", "create_date", "save_date", "print_date"],
        StringComparer.Ordinal
    );

    private object PreflightTableFormulas(JsonElement arguments)
    {
        var formulas = arguments.RequiredArray("formulas");
        if (formulas.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "formulas must contain between 1 and 200 items"
            );
        }
        var results = new List<object>();
        var targets = new HashSet<(int Row, int Column)>();
        var invalid = 0;
        var index = 0;
        foreach (var item in formulas.EnumerateArray())
        {
            try
            {
                var prepared = PrepareTableFormula(item, index);
                if (!targets.Add((prepared.Row, prepared.Column)))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "A table formula batch cannot target the same cell twice",
                        new { formula = index }
                    );
                }
                results.Add(
                    new
                    {
                        index,
                        valid = true,
                        row = prepared.Row,
                        column = prepared.Column,
                        function = prepared.Function,
                        source = prepared.Directions.Length > 0
                            ? "directions"
                            : "cell_range",
                        directions = prepared.Directions,
                        has_numeric_format = prepared.NumericFormat.Length > 0,
                        replace_existing = prepared.ReplaceExisting,
                        field_type = WordFormulaFieldType,
                        rules = new[]
                        {
                            "typed_table_formula",
                            "no_raw_field_code",
                            "bounded_table_coordinates",
                            "native_formula_field_verification",
                            "locale_aware_formula_separators",
                        },
                    }
                );
            }
            catch (NativeToolException exception)
            {
                invalid++;
                results.Add(
                    new
                    {
                        index,
                        valid = false,
                        error = new
                        {
                            code = exception.ErrorCode,
                            message = exception.Message,
                            details = exception.Details,
                        },
                    }
                );
            }
            index++;
        }
        return new
        {
            valid = invalid == 0,
            formula_count = results.Count,
            valid_count = results.Count - invalid,
            invalid_count = invalid,
            formulas = results,
            raw_field_codes_accepted = false,
            mutated_word = false,
            content_returned = false,
        };
    }

    private async Task<object> InsertTableFormulasAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var tableIndex = (int)(
            arguments.NullableInt64("table_index")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "table_index is required"
            )
        );
        if (tableIndex is < 1 or > 10_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "table_index must be between 1 and 10,000"
            );
        }
        var formulas = PrepareTableFormulas(arguments.RequiredArray("formulas"));
        var activate = arguments.Boolean("activate", true);
        var optimize = arguments.Boolean("optimize_screen_updates", true);
        var forceUpdate = arguments.Boolean("force_update", false);
        var expectedVersion = arguments.NullableInt64("expected_version");
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                if (activate)
                {
                    document.Activate();
                }
                var tableCount = (int)document.Tables.Count;
                if (tableIndex > tableCount)
                {
                    throw new NativeToolException(
                        "DOCUMENT_NOT_FOUND",
                        "The requested table does not exist in the live document",
                        new { table_index = tableIndex, table_count = tableCount }
                    );
                }
                dynamic table = document.Tables.Item(tableIndex);
                int rowCount;
                int columnCount;
                try
                {
                    rowCount = (int)table.Rows.Count;
                    columnCount = (int)table.Columns.Count;
                }
                catch
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The requested table is not a uniform rectangular table",
                        new { table_index = tableIndex }
                    );
                }

                var cells = new List<(dynamic Cell, int ExistingFields, bool Clear)>();
                var removedFields = 0;
                for (var index = 0; index < formulas.Count; index++)
                {
                    var formula = formulas[index];
                    if (formula.Row > rowCount || formula.Column > columnCount)
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "A formula destination is outside the live table",
                            new
                            {
                                formula = index,
                                row = formula.Row,
                                column = formula.Column,
                                table_rows = rowCount,
                                table_columns = columnCount,
                            }
                        );
                    }
                    if (
                        formula.RangeEnd is not null
                        && (
                            formula.RangeEnd.Value.Row > rowCount
                            || formula.RangeEnd.Value.Column > columnCount
                        )
                    )
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "A formula source range is outside the live table",
                            new { formula = index }
                        );
                    }
                    dynamic cell = table.Cell(formula.Row, formula.Column);
                    dynamic cellRange = cell.Range.Duplicate;
                    var existingFields = (int)cellRange.Fields.Count;
                    var hasContent = CellVisibleText((object)cellRange).Length > 0;
                    if (
                        (hasContent || existingFields > 0)
                        && !formula.ReplaceExisting
                    )
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "A formula destination is not empty; set replace_existing=true explicitly",
                            new
                            {
                                formula = index,
                                row = formula.Row,
                                column = formula.Column,
                            }
                        );
                    }
                    var clear = hasContent || existingFields > 0;
                    cells.Add((cell, existingFields, clear));
                    if (clear)
                    {
                        removedFields += existingFields;
                    }
                }

                var separators = WordFormulaLocale((object)application);
                var before = (int)document.Fields.Count;
                var created = new List<(dynamic Field, dynamic Result, bool Updated)>();
                var clearAssignments = 0;
                var originalScreenUpdating = (bool?)null;
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
                dynamic? undoRecord = null;
                var undoStarted = false;
                try
                {
                    if (optimize)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord(
                        "WordToolkit: insert native table formulas"
                    );
                    undoStarted = true;
                    for (var index = 0; index < formulas.Count; index++)
                    {
                        var formula = formulas[index];
                        var cell = cells[index].Cell;
                        if (cells[index].Clear)
                        {
                            dynamic contentRange = cell.Range.Duplicate;
                            contentRange.End = Math.Max(
                                (int)contentRange.Start,
                                (int)contentRange.End - 1
                            );
                            contentRange.Text = "";
                            clearAssignments++;
                        }
                        var expression = LocalizeFormulaExpression(
                            formula.Expression.TrimStart('='),
                            separators.List,
                            separators.Decimal
                        );
                        var numericFormat = LocalizeNumericFormat(
                            formula.NumericFormat,
                            separators.Decimal,
                            separators.Thousands
                        );
                        var fieldText = numericFormat.Length == 0
                            ? expression
                            : $"{expression} \\# \"{numericFormat}\"";
                        dynamic insertion = cell.Range.Duplicate;
                        insertion.End = insertion.Start;
                        dynamic field = document.Fields.Add(
                            insertion,
                            WordFormulaFieldType,
                            fieldText,
                            true
                        );
                        if ((int)cell.Range.Fields.Count != 1)
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                "Word did not create exactly one field in a formula cell",
                                new { formula = index }
                            );
                        }
                        if ((int)field.Type != WordFormulaFieldType)
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                "Word created an unexpected table formula field type",
                                new { formula = index }
                            );
                        }
                        dynamic resultRange = field.Result.Duplicate;
                        if ((int)resultRange.End <= (int)resultRange.Start)
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                "Word did not calculate the native table formula",
                                new { formula = index }
                            );
                        }
                        var updated = false;
                        if (forceUpdate)
                        {
                            updated = (bool)field.Update();
                            if (!updated)
                            {
                                throw new NativeToolException(
                                    "EXTERNAL_TOOL_FAILED",
                                    "Word could not update the table formula",
                                    new { formula = index }
                                );
                            }
                        }
                        created.Add((field, resultRange, updated));
                    }
                    var after = (int)document.Fields.Count;
                    var expectedAfter = before - removedFields + formulas.Count;
                    if (after != expectedAfter)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word did not create the expected number of formula fields",
                            new { before, after, expected = expectedAfter }
                        );
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version += formulas.Count;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);

                    var results = formulas
                        .Select(
                            (formula, index) =>
                                new
                                {
                                    index,
                                    row = formula.Row,
                                    column = formula.Column,
                                    function = formula.Function,
                                    source = formula.Directions.Length > 0
                                        ? "directions"
                                        : "cell_range",
                                    directions = formula.Directions,
                                    field_type = WordFormulaFieldType,
                                    calculated_on_insert = true,
                                    updated = created[index].Updated,
                                    replaced_existing = cells[index].Clear,
                                    range = new
                                    {
                                        start = (int)created[index].Result.Start,
                                        end = (int)created[index].Result.End,
                                    },
                                    native_verified = true,
                                }
                        )
                        .ToArray();
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        table = new
                        {
                            index = tableIndex,
                            rows = rowCount,
                            columns = columnCount,
                        },
                        formulas = results,
                        formula_count = formulas.Count,
                        field_count_before = before,
                        field_count_after = after,
                        performance = new
                        {
                            runtime = "dotnet-native",
                            python_used = false,
                            persistent_com_sta = true,
                            com_attachments = 1,
                            table_lookups = 1,
                            field_add_calls = formulas.Count,
                            field_update_calls = forceUpdate ? formulas.Count : 0,
                            cell_clear_assignments = clearAssignments,
                            undo_transactions = 1,
                            screen_updates_suspended = optimize,
                            calculation_mode = forceUpdate
                                ? "on_insert_and_explicit_update"
                                : "on_insert",
                            total_ms = Math.Round(
                                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                                3
                            ),
                        },
                        raw_field_codes_accepted = false,
                        content_returned = false,
                        document = DocumentInfo(application, document),
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
            cancellationToken
        );
    }

    private async Task<object> UpdateTableFieldsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var tableIndex = (int)(
            arguments.NullableInt64("table_index")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "table_index is required"
            )
        );
        if (tableIndex is < 1 or > 10_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "table_index must be between 1 and 10,000"
            );
        }
        var activate = arguments.Boolean("activate", true);
        var optimize = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var tableCount = (int)document.Tables.Count;
                if (tableIndex > tableCount)
                {
                    throw new NativeToolException(
                        "DOCUMENT_NOT_FOUND",
                        "The requested table does not exist",
                        new { table_index = tableIndex, table_count = tableCount }
                    );
                }
                dynamic table = document.Tables.Item(tableIndex);
                dynamic fields = table.Range.Fields;
                var beforeCount = Math.Max(0, (int)fields.Count);
                if (beforeCount > 5_000)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        "A table field refresh is limited to 5,000 fields"
                    );
                }
                var beforeTypes = FieldTypeHistogram((object)fields, beforeCount);
                if (beforeCount == 0)
                {
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        table = new
                        {
                            index = tableIndex,
                            field_count = 0,
                            field_type_histogram = beforeTypes,
                        },
                        updated = false,
                        no_op = true,
                        field_codes_returned = false,
                        field_results_returned = false,
                        content_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }

                var originalScreenUpdating = (bool?)null;
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
                dynamic? undoRecord = null;
                var undoStarted = false;
                try
                {
                    if (optimize)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord(
                        "WordToolkit: update native table fields"
                    );
                    undoStarted = true;
                    var updateResult = (int)fields.Update();
                    if (updateResult != 0)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word reported an error while updating table fields",
                            new { reported_first_error_index = updateResult }
                        );
                    }
                    var afterCount = Math.Max(0, (int)fields.Count);
                    var afterTypes = FieldTypeHistogram((object)fields, afterCount);
                    if (
                        afterCount != beforeCount
                        || !DictionaryEqual(beforeTypes, afterTypes)
                    )
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word changed the native field structure during recalculation"
                        );
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version++;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);
                    if (activate)
                    {
                        document.Activate();
                    }
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        table = new
                        {
                            index = tableIndex,
                            field_count = afterCount,
                            field_type_histogram = afterTypes,
                        },
                        updated = true,
                        no_op = false,
                        native_verified = true,
                        word_update_result = updateResult,
                        field_codes_returned = false,
                        field_results_returned = false,
                        content_returned = false,
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
            cancellationToken
        );
    }

    private object PreflightBookmarks(JsonElement arguments)
    {
        var bookmarks = arguments.RequiredArray("bookmarks");
        if (bookmarks.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "bookmarks must contain between 1 and 200 items"
            );
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<object>();
        var invalid = 0;
        var index = 0;
        foreach (var item in bookmarks.EnumerateArray())
        {
            try
            {
                var prepared = PrepareBookmark(item, index);
                if (!names.Add(prepared.Name))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "Bookmark names must be unique without regard to capitalization"
                    );
                }
                results.Add(
                    new
                    {
                        index,
                        valid = true,
                        name = prepared.Name,
                        rules = new[]
                        {
                            "native_bookmark",
                            "case_insensitive_unique_name",
                            "bounded_bookmark_range",
                            "native_range_verification",
                        },
                    }
                );
            }
            catch (NativeToolException exception)
            {
                invalid++;
                results.Add(
                    new
                    {
                        index,
                        valid = false,
                        error = new
                        {
                            code = exception.ErrorCode,
                            message = exception.Message,
                            details = exception.Details,
                        },
                    }
                );
            }
            index++;
        }
        return new
        {
            valid = invalid == 0,
            bookmark_count = results.Count,
            valid_count = results.Count - invalid,
            invalid_count = invalid,
            bookmarks = results,
            word_attached = false,
            mutated_word = false,
            content_returned = false,
        };
    }

    private async Task<object> InsertBookmarksAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var bookmarks = PrepareBookmarks(arguments.RequiredArray("bookmarks"));
        var target = arguments.String("target", "document_end");
        var selectionToken = arguments.String("selection_token");
        var replaceSelection = arguments.Boolean("replace_selection", false);
        var activate = arguments.Boolean("activate", true);
        var optimize = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var collisions = bookmarks
                    .Where(bookmark => (bool)document.Bookmarks.Exists(bookmark.Name))
                    .Select(bookmark => bookmark.Name)
                    .ToArray();
                if (collisions.Length > 0)
                {
                    throw new NativeToolException(
                        "VERSION_CONFLICT",
                        "A requested bookmark already exists in the live document",
                        new { names = collisions }
                    );
                }
                dynamic insertion = ResolveInsertionRange(
                    application,
                    document,
                    record,
                    target,
                    selectionToken,
                    replaceSelection
                );
                if (activate)
                {
                    document.Activate();
                }
                var start = (int)insertion.Start;
                var batch = BookmarkBatchPayload((object)document, start, bookmarks);
                var before = (int)document.Bookmarks.Count;
                var created = new Dictionary<int, dynamic>();
                var originalScreenUpdating = (bool?)null;
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
                dynamic? undoRecord = null;
                var undoStarted = false;
                try
                {
                    if (optimize)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord(
                        "WordToolkit: insert native bookmarks"
                    );
                    undoStarted = true;
                    insertion.Text = batch.Payload;
                    for (var index = 0; index < bookmarks.Count; index++)
                    {
                        var bookmark = bookmarks[index];
                        var relative = batch.Ranges[index];
                        dynamic range = document.Range(
                            start + relative.Start,
                            start + relative.End
                        );
                        if (bookmark.Style.Length > 0)
                        {
                            range.Style = bookmark.Style;
                        }
                        if (bookmark.Formatting is not null)
                        {
                            ApplyFormatting(range, bookmark.Formatting.Value);
                        }
                        document.Bookmarks.Add(bookmark.Name, range);
                        if (!(bool)document.Bookmarks.Exists(bookmark.Name))
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                "Word did not create a requested bookmark",
                                new { bookmark = index }
                            );
                        }
                        dynamic native = document.Bookmarks.Item(bookmark.Name);
                        dynamic actual = native.Range.Duplicate;
                        if (
                            !string.Equals(
                                (string)native.Name,
                                bookmark.Name,
                                StringComparison.OrdinalIgnoreCase
                            )
                            || (int)actual.Start != (int)range.Start
                            || (int)actual.End != (int)range.End
                        )
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                "Word changed a requested bookmark range",
                                new { bookmark = index }
                            );
                        }
                        created[index] = native;
                    }
                    var after = (int)document.Bookmarks.Count;
                    if (after != before + bookmarks.Count)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word did not create the expected number of bookmarks",
                            new { before, after, expected = bookmarks.Count }
                        );
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version += bookmarks.Count;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);
                    var results = bookmarks
                        .Select(
                            (bookmark, index) =>
                            {
                                dynamic range = created[index].Range.Duplicate;
                                return new
                                {
                                    index,
                                    name = bookmark.Name,
                                    range = new
                                    {
                                        start = (int)range.Start,
                                        end = (int)range.End,
                                    },
                                    native_verified = true,
                                };
                            }
                        )
                        .ToArray();
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        bookmarks = results,
                        bookmark_count_before = before,
                        bookmark_count_after = after,
                        performance = new
                        {
                            runtime = "dotnet-native",
                            python_used = false,
                            persistent_com_sta = true,
                            com_attachments = 1,
                            text_assignments = 1,
                            bookmark_add_calls = bookmarks.Count,
                            undo_transactions = 1,
                            screen_updates_suspended = optimize,
                            total_ms = Math.Round(
                                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                                3
                            ),
                        },
                        content_returned = false,
                        document = DocumentInfo(application, document),
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
            cancellationToken
        );
    }

    private object PreflightFields(JsonElement arguments)
    {
        var fields = arguments.RequiredArray("fields");
        if (fields.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "fields must contain between 1 and 200 items"
            );
        }
        var results = new List<object>();
        var invalid = 0;
        var index = 0;
        foreach (var item in fields.EnumerateArray())
        {
            try
            {
                var prepared = PrepareField(item, index);
                results.Add(
                    new
                    {
                        index,
                        valid = true,
                        kind = prepared.Kind,
                        field_type = prepared.FieldType,
                        preserve_formatting = prepared.PreserveFormatting,
                        update = prepared.Update,
                        as_new_paragraph = prepared.AsNewParagraph,
                        rules = prepared.Rules,
                    }
                );
            }
            catch (NativeToolException exception)
            {
                invalid++;
                results.Add(
                    new
                    {
                        index,
                        valid = false,
                        error = new
                        {
                            code = exception.ErrorCode,
                            message = exception.Message,
                            details = exception.Details,
                        },
                    }
                );
            }
            index++;
        }
        return new
        {
            valid = invalid == 0,
            field_count = results.Count,
            valid_count = results.Count - invalid,
            invalid_count = invalid,
            fields = results,
            raw_field_codes_accepted = false,
            mutated_word = false,
            content_returned = false,
        };
    }

    private async Task<object> InsertFieldsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var fields = PrepareFields(arguments.RequiredArray("fields"));
        var target = arguments.String("target", "document_end");
        var selectionToken = arguments.String("selection_token");
        var replaceSelection = arguments.Boolean("replace_selection", false);
        var activate = arguments.Boolean("activate", true);
        var optimize = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                for (var index = 0; index < fields.Count; index++)
                {
                    if (
                        fields[index].Bookmark.Length > 0
                        && !(bool)document.Bookmarks.Exists(fields[index].Bookmark)
                    )
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "A reference bookmark does not exist",
                            new { field = index, bookmark = fields[index].Bookmark }
                        );
                    }
                }
                dynamic insertion = ResolveInsertionRange(
                    application,
                    document,
                    record,
                    target,
                    selectionToken,
                    replaceSelection
                );
                if (activate)
                {
                    document.Activate();
                }
                var start = (int)insertion.Start;
                var batch = FieldBatchPayload((object)document, start, fields);
                var before = (int)document.Fields.Count;
                var created = new Dictionary<int, (dynamic Field, bool Updated)>();
                var originalScreenUpdating = (bool?)null;
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
                dynamic? undoRecord = null;
                var undoStarted = false;
                try
                {
                    if (optimize)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord(
                        "WordToolkit: insert safe native fields"
                    );
                    undoStarted = true;
                    insertion.Text = batch.Payload;
                    for (var index = fields.Count - 1; index >= 0; index--)
                    {
                        var field = fields[index];
                        var marker = batch.Markers[index];
                        dynamic range = document.Range(
                            start + marker.Start,
                            start + marker.End
                        );
                        var text = LocalizedFieldText(application, field);
                        dynamic native = document.Fields.Add(
                            range,
                            field.FieldType,
                            text,
                            field.PreserveFormatting
                        );
                        if ((int)native.Type != field.FieldType)
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                "Word created an unexpected native field type",
                                new { field = index }
                            );
                        }
                        var updated = false;
                        if (field.Update)
                        {
                            updated = (bool)native.Update();
                            if (!updated)
                            {
                                throw new NativeToolException(
                                    "EXTERNAL_TOOL_FAILED",
                                    "Word could not update the native field result",
                                    new { field = index, kind = field.Kind }
                                );
                            }
                        }
                        created[index] = (native, updated);
                    }
                    var after = (int)document.Fields.Count;
                    if (after != before + fields.Count)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word did not create the expected number of fields",
                            new { before, after, expected = fields.Count }
                        );
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version += fields.Count;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);
                    var results = fields
                        .Select(
                            (field, index) =>
                            {
                                dynamic range = created[index].Field.Result.Duplicate;
                                return new
                                {
                                    index,
                                    kind = field.Kind,
                                    field_type = field.FieldType,
                                    updated = created[index].Updated,
                                    preserve_formatting = field.PreserveFormatting,
                                    range = new
                                    {
                                        start = (int)range.Start,
                                        end = (int)range.End,
                                    },
                                    native_verified = true,
                                };
                            }
                        )
                        .ToArray();
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        fields = results,
                        field_count_before = before,
                        field_count_after = after,
                        performance = new
                        {
                            runtime = "dotnet-native",
                            python_used = false,
                            persistent_com_sta = true,
                            com_attachments = 1,
                            text_assignments = 1,
                            field_add_calls = fields.Count,
                            undo_transactions = 1,
                            screen_updates_suspended = optimize,
                            total_ms = Math.Round(
                                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                                3
                            ),
                        },
                        raw_field_codes_accepted = false,
                        content_returned = false,
                        document = DocumentInfo(application, document),
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
            cancellationToken
        );
    }

    private static List<PreparedTableFormula> PrepareTableFormulas(JsonElement formulas)
    {
        if (formulas.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException("INVALID_INPUT", "formulas must be an array");
        }
        if (formulas.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "formulas must contain between 1 and 200 items"
            );
        }
        var prepared = formulas
            .EnumerateArray()
            .Select((item, index) => PrepareTableFormula(item, index))
            .ToList();
        if (
            prepared.Select(item => (item.Row, item.Column)).Distinct().Count()
            != prepared.Count
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "A table formula batch cannot target the same cell twice"
            );
        }
        return prepared;
    }

    private static PreparedTableFormula PrepareTableFormula(
        JsonElement item,
        int index
    )
    {
        RequireObject(item, "Each table formula must be an object");
        EnsureAllowedProperties(
            item,
            [
                "row",
                "column",
                "function",
                "directions",
                "cell_range",
                "numeric_format",
                "replace_existing",
            ],
            "table formula",
            index
        );
        var row = Coordinate(item, "row", 200, index);
        var column = Coordinate(item, "column", 50, index);
        var function = item.String("function");
        var functionName = function switch
        {
            "sum" => "SUM",
            "average" => "AVERAGE",
            "count" => "COUNT",
            "max" => "MAX",
            "min" => "MIN",
            "product" => "PRODUCT",
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "Unsupported table formula function",
                new { formula = index }
            ),
        };
        var hasDirections = item.TryGetProperty("directions", out var directionsNode);
        var hasRange = item.TryGetProperty("cell_range", out var rangeNode);
        if (hasDirections == hasRange)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Provide exactly one of directions or cell_range",
                new { formula = index }
            );
        }
        string[] directions = [];
        (int Row, int Column)? rangeStart = null;
        (int Row, int Column)? rangeEnd = null;
        string operands;
        if (hasDirections)
        {
            if (
                directionsNode.ValueKind != JsonValueKind.Array
                || directionsNode.GetArrayLength() is < 1 or > 2
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "directions must contain one or two values",
                    new { formula = index }
                );
            }
            directions = directionsNode
                .EnumerateArray()
                .Select(
                    value =>
                        value.ValueKind == JsonValueKind.String
                            ? value.GetString() ?? ""
                            : throw new NativeToolException(
                                "INVALID_INPUT",
                                "Every direction must be a string"
                            )
                )
                .ToArray();
            if (directions.Distinct(StringComparer.Ordinal).Count() != directions.Length)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "directions must be unique"
                );
            }
            operands = string.Join(
                ",",
                directions.Select(
                    direction =>
                        direction switch
                        {
                            "above" => "ABOVE",
                            "below" => "BELOW",
                            "left" => "LEFT",
                            "right" => "RIGHT",
                            _ => throw new NativeToolException(
                                "INVALID_INPUT",
                                "Unsupported table formula direction"
                            ),
                        }
                )
            );
        }
        else
        {
            RequireObject(rangeNode, "cell_range must be an object");
            EnsureExactProperties(rangeNode, ["start", "end"], "cell_range");
            var startNode = rangeNode.Required("start");
            var endNode = rangeNode.Required("end");
            RequireObject(startNode, "cell_range.start must be an object");
            RequireObject(endNode, "cell_range.end must be an object");
            EnsureExactProperties(startNode, ["row", "column"], "cell_range.start");
            EnsureExactProperties(endNode, ["row", "column"], "cell_range.end");
            rangeStart = (
                Coordinate(startNode, "row", 200, index),
                Coordinate(startNode, "column", 50, index)
            );
            rangeEnd = (
                Coordinate(endNode, "row", 200, index),
                Coordinate(endNode, "column", 50, index)
            );
            if (
                rangeStart.Value.Row > rangeEnd.Value.Row
                || rangeStart.Value.Column > rangeEnd.Value.Column
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "cell_range start must not follow its end"
                );
            }
            if (
                rangeStart.Value.Row <= row
                && row <= rangeEnd.Value.Row
                && rangeStart.Value.Column <= column
                && column <= rangeEnd.Value.Column
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "cell_range cannot contain the destination cell"
                );
            }
            var startName =
                $"{TableColumnName(rangeStart.Value.Column)}{rangeStart.Value.Row}";
            var endName =
                $"{TableColumnName(rangeEnd.Value.Column)}{rangeEnd.Value.Row}";
            operands = startName == endName ? startName : $"{startName}:{endName}";
        }
        var numericFormat = item.String("numeric_format");
        if (
            numericFormat.Length > 64
            || (
                numericFormat.Length > 0
                && !NumericFormatPattern.IsMatch(numericFormat)
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "numeric_format contains unsupported characters"
            );
        }
        var replaceExisting = item.Boolean("replace_existing", false);
        return new PreparedTableFormula(
            row,
            column,
            function,
            directions,
            rangeStart,
            rangeEnd,
            numericFormat,
            replaceExisting,
            $"={functionName}({operands})"
        );
    }

    private static List<PreparedBookmark> PrepareBookmarks(JsonElement bookmarks)
    {
        if (bookmarks.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "bookmarks must contain between 1 and 200 items"
            );
        }
        var prepared = bookmarks
            .EnumerateArray()
            .Select((item, index) => PrepareBookmark(item, index))
            .ToList();
        if (
            prepared
                .Select(item => item.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            != prepared.Count
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Bookmark names must be unique without regard to capitalization"
            );
        }
        if (
            prepared.Sum(
                item =>
                    item.Text.Length + item.PrefixText.Length + item.SuffixText.Length
            ) > 500_000
        )
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Bookmark payload exceeds 500,000 characters"
            );
        }
        return prepared;
    }

    private static PreparedBookmark PrepareBookmark(JsonElement item, int index)
    {
        RequireObject(item, "Each bookmark must be an object");
        EnsureAllowedProperties(
            item,
            [
                "name",
                "text",
                "prefix_text",
                "suffix_text",
                "as_new_paragraph",
                "style",
                "formatting",
            ],
            "bookmark",
            index
        );
        var name = item.String("name");
        if (!BookmarkNamePattern.IsMatch(name))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Bookmark name must start with an ASCII letter and contain at most 40 ASCII letters, digits or underscores",
                new { bookmark = index }
            );
        }
        var text = NormalizeBoundedWordText(item.String("text"), "text", index, 100_000);
        if (text.Length == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Bookmark text must not be empty",
                new { bookmark = index }
            );
        }
        var prefix = NormalizeBoundedWordText(
            item.String("prefix_text"),
            "prefix_text",
            index,
            100_000
        );
        var suffix = NormalizeBoundedWordText(
            item.String("suffix_text"),
            "suffix_text",
            index,
            100_000
        );
        var style = item.String("style");
        if (style.Length > 128)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Bookmark style exceeds 128 characters"
            );
        }
        JsonElement? formatting = null;
        if (item.TryGetProperty("formatting", out var node))
        {
            if (node.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "formatting must be an object or null"
                );
            }
            if (node.ValueKind == JsonValueKind.Object)
            {
                formatting = node.Clone();
            }
        }
        return new PreparedBookmark(
            name,
            text,
            prefix,
            suffix,
            item.Boolean("as_new_paragraph", false),
            style,
            formatting
        );
    }

    private static List<PreparedField> PrepareFields(JsonElement fields)
    {
        if (fields.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "fields must contain between 1 and 200 items"
            );
        }
        var prepared = fields
            .EnumerateArray()
            .Select((item, index) => PrepareField(item, index))
            .ToList();
        if (
            prepared.Sum(
                item => item.PrefixText.Length + item.SuffixText.Length
            ) > 500_000
        )
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Field surrounding text exceeds 500,000 characters"
            );
        }
        return prepared;
    }

    private static PreparedField PrepareField(JsonElement item, int index)
    {
        RequireObject(item, "Each field must be an object");
        var kind = item.String("kind");
        if (!SafeFieldTypes.TryGetValue(kind, out var fieldType))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Unsupported live field kind",
                new { field = index, allowed = SafeFieldTypes.Keys.Order().ToArray() }
            );
        }
        var allowed = new HashSet<string>(
            [
                "kind",
                "preserve_formatting",
                "update",
                "prefix_text",
                "suffix_text",
                "as_new_paragraph",
            ],
            StringComparer.Ordinal
        );
        if (kind == "formula")
        {
            allowed.UnionWith(["expression", "numeric_format"]);
        }
        else if (DateFieldKinds.Contains(kind))
        {
            allowed.Add("date_format");
        }
        else if (kind == "sequence")
        {
            allowed.UnionWith(["identifier", "restart_at"]);
        }
        else if (kind == "reference")
        {
            allowed.UnionWith(["bookmark", "hyperlink"]);
        }
        else if (kind == "file_name")
        {
            allowed.Add("include_path");
        }
        EnsureAllowedProperties(item, allowed, "field", index);

        var prefix = NormalizeBoundedWordText(
            item.String("prefix_text"),
            "prefix_text",
            index,
            50_000
        );
        var suffix = NormalizeBoundedWordText(
            item.String("suffix_text"),
            "suffix_text",
            index,
            50_000
        );
        var preserve = item.Boolean("preserve_formatting", true);
        var update = item.Boolean("update", true);
        var newParagraph = item.Boolean("as_new_paragraph", false);
        var text = "";
        var bookmark = "";
        var formulaExpression = "";
        var numericFormat = "";
        var rules = new List<string>
        {
            "safe_field_kind",
            "no_raw_field_code",
            "native_field_type_verification",
        };

        if (kind == "formula")
        {
            var expression = item.String("expression").Trim();
            if (expression.StartsWith('='))
            {
                expression = expression[1..].Trim();
            }
            if (
                expression.Length is < 1 or > 1_000
                || !FormulaCharactersPattern.IsMatch(expression)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Formula expression contains unsupported characters or length",
                    new { field = index }
                );
            }
            var words = FormulaWordsPattern
                .Matches(expression)
                .Select(match => match.Value.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var unsupported = words
                .Where(word => !SafeFormulaWords.Contains(word))
                .ToArray();
            if (unsupported.Length > 0)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Formula contains unsupported names",
                    new { field = index, names = unsupported }
                );
            }
            ValidateFormulaParentheses(expression, index);
            numericFormat = item.String("numeric_format");
            if (
                numericFormat.Length > 64
                || (
                    numericFormat.Length > 0
                    && !NumericFormatPattern.IsMatch(numericFormat)
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "numeric_format contains unsupported characters"
                );
            }
            formulaExpression = expression;
            text = expression;
            rules.Add("restricted_formula_grammar");
            rules.Add("locale_aware_formula_separators");
        }
        else if (DateFieldKinds.Contains(kind))
        {
            var format = item.String("date_format");
            if (
                format.Length > 64
                || (format.Length > 0 && !DateFormatPattern.IsMatch(format))
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "date_format contains unsupported characters"
                );
            }
            if (format.Length > 0)
            {
                text = $"\\@ \"{format}\"";
            }
        }
        else if (kind == "sequence")
        {
            var identifier = item.String("identifier");
            if (!SequenceNamePattern.IsMatch(identifier))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "sequence identifier is invalid"
                );
            }
            text = $"{identifier} \\* ARABIC";
            var restart = item.NullableInt64("restart_at");
            if (restart is not null)
            {
                if (restart is < 1 or > 1_000_000)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "restart_at must be between 1 and 1,000,000"
                    );
                }
                text += $" \\r {restart.Value}";
            }
        }
        else if (kind == "reference")
        {
            bookmark = item.String("bookmark");
            if (!BookmarkNamePattern.IsMatch(bookmark))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "reference bookmark name is invalid"
                );
            }
            text = bookmark;
            if (item.Boolean("hyperlink", true))
            {
                text += " \\h";
            }
            rules.Add("bookmark_must_exist_live");
        }
        else if (kind == "file_name" && item.Boolean("include_path", false))
        {
            text = "\\p";
        }

        return new PreparedField(
            kind,
            fieldType,
            text,
            preserve,
            update,
            prefix,
            suffix,
            newParagraph,
            bookmark,
            formulaExpression,
            numericFormat,
            rules.ToArray()
        );
    }

    private static (
        string Payload,
        List<(int Start, int End)> Ranges
    ) BookmarkBatchPayload(
        object documentObject,
        int start,
        IReadOnlyList<PreparedBookmark> bookmarks
    )
    {
        dynamic document = documentObject;
        var payload = new StringBuilder();
        var ranges = new List<(int Start, int End)>();
        var previous = start > 0
            ? SafeString(() => (string?)document.Range(start - 1, start).Text)
            : "";
        foreach (var bookmark in bookmarks)
        {
            if (
                bookmark.AsNewParagraph
                && !(
                    (
                        payload.Length == 0
                        && (start == 0 || previous == "\r")
                    )
                    || (payload.Length > 0 && payload[^1] == '\r')
                )
            )
            {
                payload.Append('\r');
            }
            payload.Append(bookmark.PrefixText);
            var rangeStart = payload.Length;
            payload.Append(bookmark.Text);
            ranges.Add((rangeStart, payload.Length));
            payload.Append(bookmark.SuffixText);
            if (bookmark.AsNewParagraph && payload[^1] != '\r')
            {
                payload.Append('\r');
            }
        }
        return (payload.ToString(), ranges);
    }

    private static (
        string Payload,
        List<(int Start, int End)> Markers
    ) FieldBatchPayload(
        object documentObject,
        int start,
        IReadOnlyList<PreparedField> fields
    )
    {
        dynamic document = documentObject;
        var payload = new StringBuilder();
        var markers = new List<(int Start, int End)>();
        var previous = start > 0
            ? SafeString(() => (string?)document.Range(start - 1, start).Text)
            : "";
        foreach (var field in fields)
        {
            if (
                field.AsNewParagraph
                && !(
                    (
                        payload.Length == 0
                        && (start == 0 || previous == "\r")
                    )
                    || (payload.Length > 0 && payload[^1] == '\r')
                )
            )
            {
                payload.Append('\r');
            }
            payload.Append(field.PrefixText);
            var markerStart = payload.Length;
            payload.Append(WordFieldMarker);
            markers.Add((markerStart, markerStart + 1));
            payload.Append(field.SuffixText);
            if (field.AsNewParagraph && payload[^1] != '\r')
            {
                payload.Append('\r');
            }
        }
        return (payload.ToString(), markers);
    }

    private static string LocalizedFieldText(dynamic application, PreparedField field)
    {
        if (field.Kind != "formula")
        {
            return field.FieldText;
        }
        var separators = WordFormulaLocale((object)application);
        var expression = LocalizeFormulaExpression(
            field.FormulaExpression,
            separators.List,
            separators.Decimal
        );
        if (field.NumericFormat.Length == 0)
        {
            return expression;
        }
        var numericFormat = LocalizeNumericFormat(
            field.NumericFormat,
            separators.Decimal,
            separators.Thousands
        );
        return $"{expression} \\# \"{numericFormat}\"";
    }

    private static (string List, string Decimal, string Thousands) WordFormulaLocale(
        object applicationObject
    )
    {
        dynamic application = applicationObject;
        return (
            WordInternationalCharacter((object)application, 17, "list"),
            WordInternationalCharacter((object)application, 18, "decimal"),
            WordInternationalCharacter((object)application, 19, "thousands")
        );
    }

    private static string WordInternationalCharacter(
        object applicationObject,
        int index,
        string name
    )
    {
        dynamic application = applicationObject;
        string value;
        try
        {
            value = Convert.ToString(
                    application.International(index),
                    CultureInfo.InvariantCulture
                )
                ?? "";
        }
        catch
        {
            throw new NativeToolException(
                "EXTERNAL_TOOL_FAILED",
                "Word did not expose a required locale separator",
                new { separator = name }
            );
        }
        if (
            value.Length != 1
            || value[0] is '"' or '\\' or '\r' or '\n'
        )
        {
            throw new NativeToolException(
                "EXTERNAL_TOOL_FAILED",
                "Word returned an invalid locale separator",
                new { separator = name }
            );
        }
        return value;
    }

    private static string LocalizeFormulaExpression(
        string expression,
        string listSeparator,
        string decimalSeparator
    )
    {
        var localized = expression.Replace(",", listSeparator, StringComparison.Ordinal);
        if (decimalSeparator != ".")
        {
            localized = Regex.Replace(
                localized,
                "(?<=\\d)\\.(?=\\d)",
                decimalSeparator,
                RegexOptions.CultureInvariant
            );
        }
        return localized;
    }

    private static string LocalizeNumericFormat(
        string format,
        string decimalSeparator,
        string thousandsSeparator
    )
    {
        if (format.Length == 0)
        {
            return "";
        }
        const string placeholder = "\uE001";
        return format
            .Replace(",", placeholder, StringComparison.Ordinal)
            .Replace(".", decimalSeparator, StringComparison.Ordinal)
            .Replace(placeholder, thousandsSeparator, StringComparison.Ordinal);
    }

    private static string CellVisibleText(object rangeObject)
    {
        dynamic range = rangeObject;
        var text = SafeString(() => (string?)range.Text);
        return text.EndsWith("\r\a", StringComparison.Ordinal)
            ? text[..^2]
            : text.TrimEnd('\r', '\a');
    }

    private static SortedDictionary<string, int> FieldTypeHistogram(
        object fieldsObject,
        int count
    )
    {
        dynamic fields = fieldsObject;
        var histogram = new SortedDictionary<string, int>(
            Comparer<string>.Create(
                (left, right) =>
                    int.Parse(left, CultureInfo.InvariantCulture)
                        .CompareTo(int.Parse(right, CultureInfo.InvariantCulture))
            )
        );
        for (var index = 1; index <= count; index++)
        {
            var type = ((int)fields.Item(index).Type).ToString(
                CultureInfo.InvariantCulture
            );
            histogram[type] = histogram.TryGetValue(type, out var value)
                ? value + 1
                : 1;
        }
        return histogram;
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right
    )
    {
        return left.Count == right.Count
            && left.All(
                pair =>
                    right.TryGetValue(pair.Key, out var value)
                    && value == pair.Value
            );
    }

    private static int Coordinate(
        JsonElement item,
        string name,
        int maximum,
        int index
    )
    {
        var value = item.NullableInt64(name);
        if (value is null || value < 1 || value > maximum)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be between 1 and {maximum}",
                new { formula = index }
            );
        }
        return (int)value.Value;
    }

    private static string TableColumnName(int column)
    {
        var builder = new StringBuilder();
        var current = column;
        while (current > 0)
        {
            current--;
            builder.Insert(0, (char)('A' + (current % 26)));
            current /= 26;
        }
        return builder.ToString();
    }

    private static string NormalizeBoundedWordText(
        string value,
        string name,
        int index,
        int limit
    )
    {
        if (value.Length > limit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"{name} exceeds {limit:N0} characters",
                new { item = index }
            );
        }
        if (
            value.Any(
                character =>
                    character is '\0' or '\a' or '\x13' or '\x14' or '\x15'
                    || character == WordFieldMarker
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} contains a reserved Word control character",
                new { item = index }
            );
        }
        return NormalizeWordText(value);
    }

    private static void ValidateFormulaParentheses(string expression, int index)
    {
        var depth = 0;
        var maximum = 0;
        foreach (var character in expression)
        {
            if (character == '(')
            {
                depth++;
                maximum = Math.Max(maximum, depth);
            }
            else if (character == ')')
            {
                depth--;
                if (depth < 0)
                {
                    break;
                }
            }
        }
        if (depth != 0 || maximum > 32)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Formula parentheses are unbalanced or nested too deeply",
                new { field = index, maximum_depth = maximum }
            );
        }
    }

    private static void RequireObject(JsonElement item, string message)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException("INVALID_INPUT", message);
        }
    }

    private static void EnsureAllowedProperties(
        JsonElement item,
        IEnumerable<string> allowed,
        string itemKind,
        int index
    )
    {
        var allow = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = item
            .EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !allow.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"Unsupported {itemKind} arguments",
                new { item = index, arguments = unknown }
            );
        }
    }

    private static void EnsureExactProperties(
        JsonElement item,
        IEnumerable<string> expected,
        string name
    )
    {
        var actual = item
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var wanted = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(wanted, StringComparer.Ordinal))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} contains an invalid property set"
            );
        }
    }

    private sealed record PreparedTableFormula(
        int Row,
        int Column,
        string Function,
        string[] Directions,
        (int Row, int Column)? RangeStart,
        (int Row, int Column)? RangeEnd,
        string NumericFormat,
        bool ReplaceExisting,
        string Expression
    );

    private sealed record PreparedBookmark(
        string Name,
        string Text,
        string PrefixText,
        string SuffixText,
        bool AsNewParagraph,
        string Style,
        JsonElement? Formatting
    );

    private sealed record PreparedField(
        string Kind,
        int FieldType,
        string FieldText,
        bool PreserveFormatting,
        bool Update,
        string PrefixText,
        string SuffixText,
        bool AsNewParagraph,
        string Bookmark,
        string FormulaExpression,
        string NumericFormat,
        string[] Rules
    );
}
