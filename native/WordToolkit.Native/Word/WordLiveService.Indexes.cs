using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int WordFieldIndex = 8;
    private const int WordFieldIndexEntry = 4;
    private const int IndexEntryTextLimit = 4_096;
    private const int IndexSubentryLimit = 8;
    private const int IndexCollectionLimit = 10_000;

    private async Task<object> MarkIndexEntryAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for index-entry marking"
            );
        var selectionToken = arguments.String("selection_token");
        var rangeToken = arguments.String("range_token");
        if ((selectionToken.Length == 0) == (rangeToken.Length == 0))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Provide exactly one fresh selection_token or range_token"
            );
        }
        if (selectionToken.Length > 128 || rangeToken.Length > 128)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "selection_token and range_token are bounded to 128 characters"
            );
        }
        var mainEntryInput = arguments.String("main_entry");
        ValidateIndexEntrySegment(mainEntryInput, "main_entry", allowEmpty: true);
        var subentries = ParseIndexSubentries(arguments);
        var crossReference = arguments.String("cross_reference");
        ValidateIndexEntryText(crossReference, "cross_reference", allowEmpty: true);
        var bookmarkName = arguments.String("bookmark_name");
        ValidateIndexBookmarkName(bookmarkName);
        var boldPageNumber = arguments.Boolean("bold_page_number", false);
        var italicPageNumber = arguments.Boolean("italic_page_number", false);
        if (
            crossReference.Length > 0
            && (bookmarkName.Length > 0 || boldPageNumber || italicPageNumber)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "cross_reference cannot be combined with bookmark_name or page-number formatting"
            );
        }
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                dynamic range = rangeToken.Length > 0
                    ? ResolveVerifiedRange(
                        (object)document,
                        record,
                        rangeToken,
                        requireNonEmpty: false
                    )
                    : ResolveVerifiedSelectionRange(
                        (object)application,
                        (object)document,
                        record,
                        selectionToken,
                        requireNonEmpty: false
                    );
                var targetText = (string?)range.Text ?? "";
                var mainEntry = mainEntryInput.Length > 0 ? mainEntryInput : targetText;
                ValidateIndexEntrySegment(mainEntry, "main_entry", allowEmpty: false);
                var entry = ComposeIndexEntry(mainEntry, subentries);
                if (bookmarkName.Length > 0 && !(bool)document.Bookmarks.Exists(bookmarkName))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "bookmark_name does not identify an existing native Word bookmark"
                    );
                }
                var fieldsBefore = (int)document.Fields.Count;
                if (fieldsBefore >= CaptionFieldScanLimit)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        $"The live document already has the maximum supported {CaptionFieldScanLimit} fields"
                    );
                }
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
                    undoRecord.StartCustomRecord("WordToolkit: mark native index entry");
                    undoStarted = true;
                    dynamic field = document.Indexes.MarkEntry(
                        range,
                        entry,
                        Type.Missing,
                        crossReference.Length > 0 ? crossReference : Type.Missing,
                        Type.Missing,
                        bookmarkName.Length > 0 ? bookmarkName : Type.Missing,
                        boldPageNumber,
                        italicPageNumber,
                        Type.Missing
                    );
                    var fieldsAfter = (int)document.Fields.Count;
                    dynamic codeRange = field.Code.Duplicate;
                    var codeStart = (int)codeRange.Start;
                    var codeEnd = (int)codeRange.End;
                    var fieldIndex = FindExactIndexEntryFieldIndex(
                        document,
                        codeStart,
                        codeEnd,
                        entry,
                        crossReference,
                        bookmarkName,
                        boldPageNumber,
                        italicPageNumber
                    );
                    if (
                        fieldsAfter != fieldsBefore + 1
                        || (int)field.Type != WordFieldIndexEntry
                        || codeEnd <= codeStart
                        || fieldIndex < 1
                    )
                    {
                        throw new NativeToolException(
                            "VALIDATION_FAILED",
                            "Word did not create one readable native index-entry field with the requested options",
                            new
                            {
                                field_count_before = fieldsBefore,
                                field_count_after = fieldsAfter,
                                returned_field_type = (int)field.Type,
                                exact_field_match = fieldIndex > 0,
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
                        operation_contract = "wordtoolkit.mark_live_word_index_entry/1.0",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        target_source = rangeToken.Length > 0 ? "range_token" : "selection_token",
                        main_entry_source = mainEntryInput.Length > 0 ? "explicit" : "target_text",
                        main_entry_length = mainEntry.Length,
                        subentry_count = subentries.Count,
                        entry_length = entry.Length,
                        cross_reference = crossReference.Length > 0,
                        bookmark_page_range = bookmarkName.Length > 0,
                        bold_page_number = boldPageNumber,
                        italic_page_number = italicPageNumber,
                        field_count_before = fieldsBefore,
                        field_count_after = fieldsAfter,
                        index_entry_field_index = fieldIndex,
                        native_verified = true,
                        entry_text_returned = false,
                        bookmark_name_returned = false,
                        cross_reference_text_returned = false,
                        raw_field_code_returned = false,
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

    private async Task<object> InsertIndexAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for index insertion"
            );
        var target = arguments.String("target", "document_end");
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
        var headingSeparator = arguments.String("heading_separator", "letter");
        var headingSeparatorValue = IndexHeadingSeparator(headingSeparator);
        var rightAlignPageNumbers = arguments.Boolean("right_align_page_numbers", true);
        var indexType = arguments.String("index_type", "indented");
        var indexTypeValue = IndexType(indexType);
        var numberOfColumnsValue = arguments.NullableInt64("number_of_columns") ?? 1;
        if (numberOfColumnsValue is < 0 or > 4)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "number_of_columns must be between 0 and 4; zero inherits the section column count"
            );
        }
        var accentedLetters = arguments.Boolean("separate_accented_letter_headings", false);
        var tabLeader = arguments.String("tab_leader", "dots");
        var tabLeaderValue = AuthorityTabLeader(tabLeader);
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
                var indexEntryCount = CountIndexEntryFields(document);
                if (indexEntryCount == 0)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The live document has no complete native XE index entries"
                    );
                }
                var indexesBefore = (int)document.Indexes.Count;
                if (indexesBefore >= IndexCollectionLimit)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        $"The live document already has the maximum supported {IndexCollectionLimit} indexes"
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
                    undoRecord.StartCustomRecord("WordToolkit: insert native index");
                    undoStarted = true;
                    dynamic index = document.Indexes.Add(
                        insertion,
                        headingSeparatorValue,
                        rightAlignPageNumbers,
                        indexTypeValue,
                        (int)numberOfColumnsValue,
                        accentedLetters,
                        Type.Missing,
                        Type.Missing
                    );
                    var repaginationPerformed = false;
                    if (repaginate)
                    {
                        document.Repaginate();
                        repaginationPerformed = true;
                    }
                    if (update)
                    {
                        index.Update();
                    }
                    // Word can normalize or ignore some optional Add arguments and can
                    // reapply language defaults while it builds the index. The returned
                    // Index properties are the authoritative final read/write surface, so
                    // apply every requested presentation option after the optional update
                    // and verify the exact values immediately afterward.
                    index.HeadingSeparator = headingSeparatorValue;
                    index.RightAlignPageNumbers = rightAlignPageNumbers;
                    index.Type = indexTypeValue;
                    index.NumberOfColumns = (int)numberOfColumnsValue;
                    index.AccentedLetters = accentedLetters;
                    index.TabLeader = tabLeaderValue;
                    var indexesAfter = (int)document.Indexes.Count;
                    dynamic insertedRange = index.Range.Duplicate;
                    var insertedStart = (int)insertedRange.Start;
                    var insertedEnd = (int)insertedRange.End;
                    var insertedFieldCount = (int)insertedRange.Fields.Count;
                    var insertedIndex = FindExactIndex(
                        document,
                        insertedStart,
                        insertedEnd
                    );
                    var actualHeadingSeparator = (int)index.HeadingSeparator;
                    var actualRightAlignPageNumbers = (bool)index.RightAlignPageNumbers;
                    var actualIndexType = (int)index.Type;
                    var actualNumberOfColumns = (int)index.NumberOfColumns;
                    var actualAccentedLetters = (bool)index.AccentedLetters;
                    var actualTabLeader = (int)index.TabLeader;
                    var nativeOptionsVerified =
                        actualHeadingSeparator == headingSeparatorValue
                        && actualRightAlignPageNumbers == rightAlignPageNumbers
                        && actualIndexType == indexTypeValue
                        && actualNumberOfColumns == (int)numberOfColumnsValue
                        && actualAccentedLetters == accentedLetters
                        && actualTabLeader == tabLeaderValue;
                    if (
                        indexesAfter != indexesBefore + 1
                        || insertedIndex < 1
                        || insertedFieldCount < 1
                        || insertedEnd <= insertedStart
                        || !nativeOptionsVerified
                    )
                    {
                        throw new NativeToolException(
                            "VALIDATION_FAILED",
                            "Word did not create one readable native index with the requested options",
                            new
                            {
                                index_count_before = indexesBefore,
                                index_count_after = indexesAfter,
                                exact_range_matches = insertedIndex < 1 ? 0 : 1,
                                inserted_field_count = insertedFieldCount,
                                native_options_verified = nativeOptionsVerified,
                                requested_options = new
                                {
                                    heading_separator = headingSeparatorValue,
                                    right_align_page_numbers = rightAlignPageNumbers,
                                    index_type = indexTypeValue,
                                    number_of_columns = numberOfColumnsValue,
                                    accented_letters = accentedLetters,
                                    tab_leader = tabLeaderValue,
                                },
                                actual_options = new
                                {
                                    heading_separator = actualHeadingSeparator,
                                    right_align_page_numbers = actualRightAlignPageNumbers,
                                    index_type = actualIndexType,
                                    number_of_columns = actualNumberOfColumns,
                                    accented_letters = actualAccentedLetters,
                                    tab_leader = actualTabLeader,
                                },
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
                        operation_contract = "wordtoolkit.insert_live_word_index/1.0",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        target,
                        index_entry_count = indexEntryCount,
                        index_count_before = indexesBefore,
                        index_count_after = indexesAfter,
                        index_collection_index = insertedIndex,
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
                            heading_separator = headingSeparator,
                            right_align_page_numbers = rightAlignPageNumbers,
                            index_type = indexType,
                            number_of_columns = numberOfColumnsValue,
                            separate_accented_letter_headings = accentedLetters,
                            tab_leader = tabLeader,
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

    private static List<string> ParseIndexSubentries(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("subentries", out var value))
        {
            return [];
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException("INVALID_INPUT", "subentries must be an array");
        }
        if (value.GetArrayLength() > IndexSubentryLimit)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"subentries is limited to {IndexSubentryLimit} levels"
            );
        }
        var result = new List<string>(value.GetArrayLength());
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"subentries[{index}] must be a string"
                );
            }
            var segment = item.GetString() ?? "";
            ValidateIndexEntrySegment(segment, $"subentries[{index}]", allowEmpty: false);
            result.Add(segment);
            index++;
        }
        return result;
    }

    private static string ComposeIndexEntry(string mainEntry, IReadOnlyList<string> subentries)
    {
        var entry = subentries.Count == 0
            ? mainEntry
            : string.Join(':', new[] { mainEntry }.Concat(subentries));
        ValidateIndexEntryText(entry, "composed index entry", allowEmpty: false);
        return entry;
    }

    private static void ValidateIndexEntrySegment(string value, string name, bool allowEmpty)
    {
        ValidateIndexEntryText(value, name, allowEmpty);
        if (value.Contains(':', StringComparison.Ordinal))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} cannot contain ':'; provide hierarchy through subentries"
            );
        }
    }

    private static void ValidateIndexEntryText(string value, string name, bool allowEmpty)
    {
        if ((!allowEmpty && value.Length == 0) || value.Length > IndexEntryTextLimit)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must contain between {(allowEmpty ? 0 : 1)} and {IndexEntryTextLimit} characters"
            );
        }
        if (value.IndexOfAny(['\r', '\n', '\0', '\a']) >= 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be single-line text without NUL"
            );
        }
    }

    private static void ValidateIndexBookmarkName(string value)
    {
        if (value.Length > 40 || value.IndexOfAny(['\r', '\n', '\0', '\a']) >= 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "bookmark_name must contain at most 40 single-line characters"
            );
        }
    }

    private static int IndexHeadingSeparator(string value) =>
        value switch
        {
            "none" => 0,
            "blank_line" => 1,
            "letter" => 2,
            "lowercase_letter" => 3,
            "uppercase_letter" => 4,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "heading_separator must be none, blank_line, letter, lowercase_letter, or uppercase_letter"
            ),
        };

    private static int IndexType(string value) =>
        value switch
        {
            "indented" => 0,
            "run_in" => 1,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "index_type must be indented or run_in"
            ),
        };

    private static int CountIndexEntryFields(dynamic document)
    {
        var count = (int)document.Fields.Count;
        if (count > CaptionFieldScanLimit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"Field scanning is capped at {CaptionFieldScanLimit} fields"
            );
        }
        var matches = 0;
        for (var index = 1; index <= count; index++)
        {
            dynamic field = document.Fields.Item(index);
            if ((int)field.Type != WordFieldIndexEntry)
            {
                continue;
            }
            var code = (string?)field.Code.Text ?? "";
            if (code.Length > 16_384)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    "One index-entry field instruction exceeds 16,384 characters"
                );
            }
            if (TryParseIndexEntryInstruction(code, out _))
            {
                matches++;
            }
        }
        return matches;
    }

    private static int FindExactIndexEntryFieldIndex(
        dynamic document,
        int expectedStart,
        int expectedEnd,
        string expectedEntry,
        string expectedCrossReference,
        string expectedBookmarkName,
        bool expectedBold,
        bool expectedItalic
    )
    {
        var count = (int)document.Fields.Count;
        var match = 0;
        for (var index = 1; index <= count; index++)
        {
            dynamic field = document.Fields.Item(index);
            if ((int)field.Type != WordFieldIndexEntry)
            {
                continue;
            }
            dynamic code = field.Code.Duplicate;
            if ((int)code.Start != expectedStart || (int)code.End != expectedEnd)
            {
                continue;
            }
            if (
                !TryParseIndexEntryInstruction((string?)code.Text ?? "", out var instruction)
                || !string.Equals(instruction.Entry, expectedEntry, StringComparison.Ordinal)
                || !string.Equals(
                    instruction.CrossReference,
                    expectedCrossReference,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    instruction.BookmarkName,
                    expectedBookmarkName,
                    StringComparison.Ordinal
                )
                || instruction.BoldPageNumber != expectedBold
                || instruction.ItalicPageNumber != expectedItalic
                || match != 0
            )
            {
                return 0;
            }
            match = index;
        }
        return match;
    }

    private static bool TryParseIndexEntryInstruction(
        string code,
        out ParsedIndexEntryInstruction instruction
    )
    {
        instruction = new ParsedIndexEntryInstruction("", "", "", false, false);
        if (!TryTokenizeFieldInstruction(code, out var tokens) || tokens.Count < 2)
        {
            return false;
        }
        if (!string.Equals(tokens[0], "XE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var entry = tokens[1];
        if (entry.Length == 0)
        {
            return false;
        }
        var crossReference = "";
        var bookmarkName = "";
        var bold = false;
        var italic = false;
        for (var index = 2; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (string.Equals(token, "\\b", StringComparison.OrdinalIgnoreCase))
            {
                if (bold)
                {
                    return false;
                }
                bold = true;
                continue;
            }
            if (string.Equals(token, "\\i", StringComparison.OrdinalIgnoreCase))
            {
                if (italic)
                {
                    return false;
                }
                italic = true;
                continue;
            }
            if (
                string.Equals(token, "\\t", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "\\r", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (index + 1 >= tokens.Count || tokens[index + 1].StartsWith('\\'))
                {
                    return false;
                }
                var operand = tokens[++index];
                if (string.Equals(token, "\\t", StringComparison.OrdinalIgnoreCase))
                {
                    if (crossReference.Length > 0)
                    {
                        return false;
                    }
                    crossReference = operand;
                }
                else
                {
                    if (bookmarkName.Length > 0)
                    {
                        return false;
                    }
                    bookmarkName = operand;
                }
                continue;
            }
            return false;
        }
        instruction = new ParsedIndexEntryInstruction(
            entry,
            crossReference,
            bookmarkName,
            bold,
            italic
        );
        return true;
    }

    private static bool TryTokenizeFieldInstruction(string value, out List<string> tokens)
    {
        tokens = [];
        var index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }
            if (index >= value.Length)
            {
                break;
            }
            if (value[index] == '"')
            {
                index++;
                var decoded = new StringBuilder();
                var closed = false;
                while (index < value.Length)
                {
                    if (
                        value[index] == '"'
                        && index + 1 < value.Length
                        && value[index + 1] == '"'
                    )
                    {
                        decoded.Append('"');
                        index += 2;
                        continue;
                    }
                    if (value[index] == '"')
                    {
                        index++;
                        closed = true;
                        break;
                    }
                    decoded.Append(value[index++]);
                }
                if (!closed)
                {
                    tokens = [];
                    return false;
                }
                tokens.Add(decoded.ToString());
                continue;
            }
            var start = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]))
            {
                index++;
            }
            tokens.Add(value[start..index]);
        }
        return tokens.Count > 0;
    }

    private static int FindExactIndex(dynamic document, int expectedStart, int expectedEnd)
    {
        var count = (int)document.Indexes.Count;
        var match = 0;
        for (var index = 1; index <= count; index++)
        {
            dynamic native = document.Indexes.Item(index);
            dynamic range = native.Range.Duplicate;
            if ((int)range.Start != expectedStart || (int)range.End != expectedEnd)
            {
                continue;
            }
            if (
                (int)range.Fields.Count < 1
                || (int)range.Fields.Item(1).Type != WordFieldIndex
                || match != 0
            )
            {
                return 0;
            }
            match = index;
        }
        return match;
    }

    private sealed record ParsedIndexEntryInstruction(
        string Entry,
        string CrossReference,
        string BookmarkName,
        bool BoldPageNumber,
        bool ItalicPageNumber
    );
}
