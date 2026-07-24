using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int WordFieldTableOfAuthorities = 73;
    private const int WordFieldTableOfAuthoritiesEntry = 74;
    private const int AuthorityCitationTextLimit = 4_096;
    private const int TableOfAuthoritiesLimit = 10_000;
    private static readonly Regex AuthorityCategoryPattern = new(
        "(?:^|\\s)\\\\c\\s+(?<category>\\d{1,2})(?=\\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private async Task<object> MarkAuthorityCitationAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for authority-citation marking"
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
        var shortCitationInput = arguments.String("short_citation");
        var longCitationInput = arguments.String("long_citation");
        ValidateAuthorityCitationText(shortCitationInput, "short_citation", allowEmpty: true);
        ValidateAuthorityCitationText(longCitationInput, "long_citation", allowEmpty: true);
        var categoryValue = arguments.NullableInt64("category") ?? 1;
        if (categoryValue is < 1 or > 16)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "category must be between 1 and 16"
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
                        requireNonEmpty: true
                    )
                    : ResolveVerifiedSelectionRange(
                        (object)application,
                        (object)document,
                        record,
                        selectionToken,
                        requireNonEmpty: true
                    );
                var selectedText = (string?)range.Text ?? "";
                ValidateAuthorityCitationText(selectedText, "selected citation", allowEmpty: false);
                var shortCitation = shortCitationInput.Length > 0
                    ? shortCitationInput
                    : selectedText;
                var longCitation = longCitationInput.Length > 0
                    ? longCitationInput
                    : shortCitation;
                ValidateAuthorityCitationText(shortCitation, "short_citation", allowEmpty: false);
                ValidateAuthorityCitationText(longCitation, "long_citation", allowEmpty: false);
                var fieldsBefore = (int)document.Fields.Count;
                if (fieldsBefore >= CaptionFieldScanLimit)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        $"The live document already has the maximum supported {CaptionFieldScanLimit} fields"
                    );
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
                    undoRecord.StartCustomRecord("WordToolkit: mark authority citation");
                    undoStarted = true;
                    dynamic field = document.TablesOfAuthorities.MarkCitation(
                        range,
                        shortCitation,
                        longCitation,
                        "",
                        (int)categoryValue
                    );
                    var fieldsAfter = (int)document.Fields.Count;
                    dynamic codeRange = field.Code.Duplicate;
                    var codeStart = (int)codeRange.Start;
                    var codeEnd = (int)codeRange.End;
                    var fieldIndex = FindExactAuthorityEntryFieldIndex(
                        document,
                        codeStart,
                        codeEnd,
                        (int)categoryValue
                    );
                    if (
                        fieldsAfter != fieldsBefore + 1
                        || (int)field.Type != WordFieldTableOfAuthoritiesEntry
                        || codeEnd <= codeStart
                        || fieldIndex < 1
                    )
                    {
                        throw new NativeToolException(
                            "VALIDATION_FAILED",
                            "Word did not create one readable native table-of-authorities entry field",
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
                        operation_contract = "wordtoolkit.mark_live_word_authority_citation/1.0",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        category = categoryValue,
                        target_source = rangeToken.Length > 0 ? "range_token" : "selection_token",
                        short_citation_source = shortCitationInput.Length > 0
                            ? "explicit"
                            : "target_text",
                        long_citation_source = longCitationInput.Length > 0
                            ? "explicit"
                            : shortCitationInput.Length > 0
                                ? "short_citation"
                                : "target_text",
                        short_citation_length = shortCitation.Length,
                        long_citation_length = longCitation.Length,
                        field_count_before = fieldsBefore,
                        field_count_after = fieldsAfter,
                        authority_entry_field_index = fieldIndex,
                        native_verified = true,
                        citation_text_returned = false,
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

    private async Task<object> InsertTableOfAuthoritiesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for table-of-authorities insertion"
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
        var categoryValue = arguments.NullableInt64("category") ?? 1;
        if (categoryValue is < 0 or > 16)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "category must be between 0 and 16; zero includes all categories"
            );
        }
        var passim = arguments.Boolean("passim", false);
        var keepEntryFormatting = arguments.Boolean("keep_entry_formatting", true);
        var entrySeparator = AuthoritySeparator(
            arguments.String("entry_separator", "\t"),
            "entry_separator"
        );
        var pageRangeSeparator = AuthoritySeparator(
            arguments.String("page_range_separator", "–"),
            "page_range_separator"
        );
        var includeCategoryHeader = arguments.Boolean("include_category_header", true);
        var pageNumberSeparator = AuthoritySeparator(
            arguments.String("page_number_separator", ", "),
            "page_number_separator"
        );
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
                var matchingCitationCount = CountAuthorityEntryFields(
                    document,
                    (int)categoryValue
                );
                if (matchingCitationCount == 0)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The live document has no matching native authority citations for this category"
                    );
                }
                var tablesBefore = (int)document.TablesOfAuthorities.Count;
                if (tablesBefore >= TableOfAuthoritiesLimit)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        $"The live document already has the maximum supported {TableOfAuthoritiesLimit} tables of authorities"
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
                    undoRecord.StartCustomRecord("WordToolkit: insert native table of authorities");
                    undoStarted = true;
                    dynamic tableOfAuthorities = document.TablesOfAuthorities.Add(
                        insertion,
                        (int)categoryValue,
                        Type.Missing,
                        passim,
                        keepEntryFormatting,
                        Type.Missing,
                        Type.Missing,
                        entrySeparator,
                        pageRangeSeparator,
                        includeCategoryHeader,
                        pageNumberSeparator
                    );
                    tableOfAuthorities.TabLeader = tabLeaderValue;
                    var repaginationPerformed = false;
                    if (repaginate)
                    {
                        document.Repaginate();
                        repaginationPerformed = true;
                    }
                    if (update)
                    {
                        tableOfAuthorities.Update();
                    }
                    var tablesAfter = (int)document.TablesOfAuthorities.Count;
                    dynamic insertedRange = tableOfAuthorities.Range.Duplicate;
                    var insertedStart = (int)insertedRange.Start;
                    var insertedEnd = (int)insertedRange.End;
                    var insertedFieldCount = (int)insertedRange.Fields.Count;
                    var insertedIndex = FindExactTableOfAuthoritiesIndex(
                        document,
                        insertedStart,
                        insertedEnd
                    );
                    var nativeOptionsVerified =
                        string.Equals(
                            (string?)tableOfAuthorities.EntrySeparator ?? "",
                            entrySeparator,
                            StringComparison.Ordinal
                        )
                        && string.Equals(
                            (string?)tableOfAuthorities.PageRangeSeparator ?? "",
                            pageRangeSeparator,
                            StringComparison.Ordinal
                        )
                        && string.Equals(
                            (string?)tableOfAuthorities.PageNumberSeparator ?? "",
                            pageNumberSeparator,
                            StringComparison.Ordinal
                        )
                        && (bool)tableOfAuthorities.Passim == passim
                        && (bool)tableOfAuthorities.KeepEntryFormatting == keepEntryFormatting
                        && (bool)tableOfAuthorities.IncludeCategoryHeader == includeCategoryHeader
                        && (int)tableOfAuthorities.TabLeader == tabLeaderValue;
                    if (
                        tablesAfter != tablesBefore + 1
                        || insertedIndex < 1
                        || insertedFieldCount < 1
                        || insertedEnd <= insertedStart
                        || !nativeOptionsVerified
                    )
                    {
                        throw new NativeToolException(
                            "VALIDATION_FAILED",
                            "Word did not create one readable native table-of-authorities field",
                            new
                            {
                                table_count_before = tablesBefore,
                                table_count_after = tablesAfter,
                                exact_range_matches = insertedIndex < 1 ? 0 : 1,
                                inserted_field_count = insertedFieldCount,
                                native_options_verified = nativeOptionsVerified,
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
                        operation_contract = "wordtoolkit.insert_live_word_table_of_authorities/1.1",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        target,
                        category = categoryValue,
                        matching_citation_count = matchingCitationCount,
                        table_of_authorities_count_before = tablesBefore,
                        table_of_authorities_count_after = tablesAfter,
                        table_of_authorities_index = insertedIndex,
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
                            passim,
                            keep_entry_formatting = keepEntryFormatting,
                            entry_separator_length = entrySeparator.Length,
                            page_range_separator_length = pageRangeSeparator.Length,
                            include_category_header = includeCategoryHeader,
                            page_number_separator_length = pageNumberSeparator.Length,
                            tab_leader = tabLeader,
                        },
                        native_verified = true,
                        separator_values_returned = false,
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

    private static void ValidateAuthorityCitationText(
        string value,
        string name,
        bool allowEmpty
    )
    {
        if ((!allowEmpty && value.Length == 0) || value.Length > AuthorityCitationTextLimit)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must contain between {(allowEmpty ? 0 : 1)} and {AuthorityCitationTextLimit} characters"
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

    private static string AuthoritySeparator(string value, string name)
    {
        if (value.Length > 5 || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must contain at most five single-line characters"
            );
        }
        return value;
    }

    private static int AuthorityTabLeader(string value) =>
        value switch
        {
            "spaces" => 0,
            "dots" => 1,
            "dashes" => 2,
            "lines" => 3,
            "heavy" => 4,
            "middle_dot" => 5,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "tab_leader must be spaces, dots, dashes, lines, heavy, or middle_dot"
            ),
        };

    private static int FindExactAuthorityEntryFieldIndex(
        dynamic document,
        int expectedStart,
        int expectedEnd,
        int expectedCategory
    )
    {
        var count = (int)document.Fields.Count;
        var match = 0;
        for (var index = 1; index <= count; index++)
        {
            dynamic field = document.Fields.Item(index);
            if ((int)field.Type != WordFieldTableOfAuthoritiesEntry)
            {
                continue;
            }
            dynamic code = field.Code.Duplicate;
            if ((int)code.Start != expectedStart || (int)code.End != expectedEnd)
            {
                continue;
            }
            if (AuthorityEntryCategory((string?)code.Text ?? "") != expectedCategory || match != 0)
            {
                return 0;
            }
            match = index;
        }
        return match;
    }

    private static int CountAuthorityEntryFields(dynamic document, int category)
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
            if ((int)field.Type != WordFieldTableOfAuthoritiesEntry)
            {
                continue;
            }
            var code = (string?)field.Code.Text ?? "";
            if (code.Length > 16_384)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    "One authority-entry field instruction exceeds 16,384 characters"
                );
            }
            if (category == 0 || AuthorityEntryCategory(code) == category)
            {
                matches++;
            }
        }
        return matches;
    }

    private static int AuthorityEntryCategory(string code)
    {
        var match = AuthorityCategoryPattern.Match(code);
        return match.Success && int.TryParse(match.Groups["category"].Value, out var category)
            ? category
            : 1;
    }

    private static int FindExactTableOfAuthoritiesIndex(
        dynamic document,
        int expectedStart,
        int expectedEnd
    )
    {
        var count = (int)document.TablesOfAuthorities.Count;
        var match = 0;
        for (var index = 1; index <= count; index++)
        {
            dynamic table = document.TablesOfAuthorities.Item(index);
            dynamic range = table.Range.Duplicate;
            if ((int)range.Start != expectedStart || (int)range.End != expectedEnd)
            {
                continue;
            }
            if (
                (int)range.Fields.Count < 1
                || (int)range.Fields.Item(1).Type != WordFieldTableOfAuthorities
                || match != 0
            )
            {
                return 0;
            }
            match = index;
        }
        return match;
    }
}
