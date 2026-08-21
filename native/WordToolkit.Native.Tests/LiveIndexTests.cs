using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LiveIndexTests
{
    [Fact]
    public void PublishesClosedVersionedNativeIndexContracts()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var mark = catalog.InspectAction("mark_live_word_index_entry")["tool"]!.AsObject();
        var insert = catalog.InspectAction("insert_live_word_index")["tool"]!.AsObject();

        Assert.Equal("1.0", mark["operationVersion"]!.GetValue<string>());
        Assert.Equal("1.0", insert["operationVersion"]!.GetValue<string>());
        Assert.False(mark["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.True(mark["annotations"]!["destructiveHint"]!.GetValue<bool>());
        Assert.False(insert["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(insert["outputSchema"]!["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public async Task MarksOneHierarchicalNativeIndexEntryWithoutReturningItsText()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        var selectionToken = await SelectionTokenAsync(service, documentId);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    selection_token = selectionToken,
                    main_entry = "Poufna analiza",
                    subentries = new[] { "Równania", "Całki" },
                    bold_page_number = true,
                    italic_page_number = true,
                }
            )
        );

        var result = await service.CallAsync(
            "mark_live_word_index_entry",
            arguments.RootElement,
            CancellationToken.None
        );
        var raw = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        using var json = JsonDocument.Parse(raw);
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.mark_live_word_index_entry/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(version + 1, data.GetProperty("live_version").GetInt64());
        Assert.Equal(2, data.GetProperty("subentry_count").GetInt32());
        Assert.True(data.GetProperty("bold_page_number").GetBoolean());
        Assert.True(data.GetProperty("italic_page_number").GetBoolean());
        Assert.True(data.GetProperty("native_verified").GetBoolean());
        Assert.False(data.GetProperty("entry_text_returned").GetBoolean());
        Assert.DoesNotContain("Poufna", raw, StringComparison.Ordinal);
        Assert.Equal(4, host.Application.ActiveDocument.Fields.Item(1).Type);
        Assert.Contains("Poufna analiza:Równania:Całki", host.Application.ActiveDocument.Fields.Item(1).Code.Text);
        Assert.Equal(1, host.Application.UndoRecord.StartCount);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
    }

    [Fact]
    public async Task MarksCrossReferenceAndKeepsItsTextPrivate()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        var selectionToken = await SelectionTokenAsync(service, documentId);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    selection_token = selectionToken,
                    main_entry = "Pochodne",
                    cross_reference = "Zobacz Rachunek różniczkowy",
                }
            )
        );

        var result = await service.CallAsync(
            "mark_live_word_index_entry",
            arguments.RootElement,
            CancellationToken.None
        );
        var raw = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        using var json = JsonDocument.Parse(raw);

        Assert.True(json.RootElement.GetProperty("cross_reference").GetBoolean());
        Assert.False(
            json.RootElement.GetProperty("cross_reference_text_returned").GetBoolean()
        );
        Assert.DoesNotContain("Rachunek", raw, StringComparison.Ordinal);
        Assert.Contains("\\t", host.Application.ActiveDocument.Fields.Item(1).Code.Text);
    }

    [Fact]
    public async Task MarksBookmarkBackedPageRangeOnlyWhenBookmarkExists()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.Bookmarks.Seed("ZakresCalek");
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        var selectionToken = await SelectionTokenAsync(service, documentId);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    selection_token = selectionToken,
                    main_entry = "Całki",
                    bookmark_name = "ZakresCalek",
                }
            )
        );

        var result = await service.CallAsync(
            "mark_live_word_index_entry",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));

        Assert.True(json.RootElement.GetProperty("bookmark_page_range").GetBoolean());
        Assert.False(json.RootElement.GetProperty("bookmark_name_returned").GetBoolean());
        Assert.Contains("\\r ZakresCalek", host.Application.ActiveDocument.Fields.Item(1).Code.Text);
    }

    [Fact]
    public async Task RollsBackWhenWordDoesNotAddTheMarkedIndexField()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.Indexes.SuppressMarkedEntry = true;
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        var selectionToken = await SelectionTokenAsync(service, documentId);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    selection_token = selectionToken,
                }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "mark_live_word_index_entry",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VALIDATION_FAILED", error.ErrorCode);
        Assert.Equal(0, host.Application.ActiveDocument.Fields.Count);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
    }

    [Fact]
    public async Task InsertsUpdatesAndReacquiresOneNativeIndex()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.Fields.Add(" XE \"Analiza\" ", 4);
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    target = "document_end",
                    heading_separator = "uppercase_letter",
                    right_align_page_numbers = true,
                    index_type = "run_in",
                    number_of_columns = 2,
                    separate_accented_letter_headings = true,
                    tab_leader = "dashes",
                }
            )
        );

        var result = await service.CallAsync(
            "insert_live_word_index",
            arguments.RootElement,
            CancellationToken.None
        );
        var raw = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        using var json = JsonDocument.Parse(raw);
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.insert_live_word_index/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(1, data.GetProperty("index_entry_count").GetInt32());
        Assert.Equal(1, data.GetProperty("index_count_after").GetInt32());
        Assert.Equal(1, data.GetProperty("index_collection_index").GetInt32());
        Assert.True(data.GetProperty("native_verified").GetBoolean());
        Assert.False(data.GetProperty("result_text_returned").GetBoolean());
        var native = host.Application.ActiveDocument.Indexes.Item(1);
        Assert.Equal(4, native.HeadingSeparator);
        Assert.True(native.RightAlignPageNumbers);
        Assert.Equal(1, native.Type);
        Assert.Equal(2, native.NumberOfColumns);
        Assert.True(native.AccentedLetters);
        Assert.Equal(2, native.TabLeader);
        Assert.Equal(1, native.UpdateCount);
        Assert.Equal(1, host.Application.ActiveDocument.RepaginateCount);
    }

    [Fact]
    public async Task RejectsIndexWithoutEntriesBeforeUndo()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new { live_document_id = documentId, expected_version = version }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_index",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
    }

    [Fact]
    public async Task RollsBackIndexWhenNativeOptionReadbackMismatches()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.Fields.Add(" XE \"Analiza\" ", 4);
        host.Application.ActiveDocument.Indexes.SuppressTabLeaderChange = true;
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new { live_document_id = documentId, expected_version = version }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_index",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VALIDATION_FAILED", error.ErrorCode);
        Assert.Equal(0, host.Application.ActiveDocument.Indexes.Count);
        Assert.Equal(1, host.Application.ActiveDocument.UndoCount);
    }

    [Fact]
    public async Task ReferenceTableUpdateTargetsOneNativeIndex()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.Indexes.Seed(1);
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    kind = "index",
                    index = 1,
                    repaginate = false,
                }
            )
        );

        var result = await service.CallAsync(
            "update_live_word_reference_tables",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));

        Assert.Equal(
            "wordtoolkit.update_live_word_reference_tables/1.1",
            json.RootElement.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(
            1,
            json.RootElement.GetProperty("updated_counts").GetProperty("indexes").GetInt32()
        );
        Assert.Equal(1, host.Application.ActiveDocument.Indexes.Item(1).UpdateCount);
    }

    private static async Task<(string DocumentId, long Version)> ConnectAsync(
        WordLiveService service
    )
    {
        using var arguments = JsonDocument.Parse("{}");
        var result = await service.CallAsync(
            "connect_live_word_document",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        return (
            json.RootElement.GetProperty("live_document_id").GetString()!,
            json.RootElement.GetProperty("live_version").GetInt64()
        );
    }

    private static async Task<string> SelectionTokenAsync(
        WordLiveService service,
        string documentId
    )
    {
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var result = await service.CallAsync(
            "get_live_word_selection",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        return json.RootElement
            .GetProperty("selection")
            .GetProperty("selection_token")
            .GetString()!;
    }
}
