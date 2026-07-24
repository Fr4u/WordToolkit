using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LiveCaptionAndTableOfFiguresTests
{
    [Fact]
    public void PublishesClosedVersionedCaptionContracts()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var caption = catalog.InspectAction("insert_live_word_caption")["tool"]!.AsObject();
        var table = catalog
            .InspectAction("insert_live_word_table_of_figures")["tool"]!
            .AsObject();
        var contents = catalog
            .InspectAction("insert_live_word_table_of_contents")["tool"]!
            .AsObject();
        var update = catalog
            .InspectAction("update_live_word_reference_tables")["tool"]!
            .AsObject();

        Assert.Equal("1.0", caption["operationVersion"]!.GetValue<string>());
        Assert.Equal("1.0", table["operationVersion"]!.GetValue<string>());
        Assert.Equal("1.0", contents["operationVersion"]!.GetValue<string>());
        Assert.Equal("1.0", update["operationVersion"]!.GetValue<string>());
        Assert.False(caption["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(table["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(contents["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(update["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(caption["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(table["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(contents["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(update["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.NotNull(caption["permissions"]);
        Assert.NotNull(caption["reversibility"]);
        Assert.NotNull(caption["outputSchema"]);
        Assert.NotNull(table["permissions"]);
        Assert.NotNull(table["reversibility"]);
        Assert.NotNull(table["outputSchema"]);
        Assert.NotNull(contents["permissions"]);
        Assert.NotNull(contents["reversibility"]);
        Assert.NotNull(contents["outputSchema"]);
        Assert.NotNull(update["permissions"]);
        Assert.NotNull(update["reversibility"]);
        Assert.NotNull(update["outputSchema"]);
    }

    [Fact]
    public async Task InsertsOneLocalizedNativeCaptionWithoutReturningItsText()
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
                    caption_kind = "figure",
                    title = "Sekretny podpis kontrolny",
                    separator = "colon",
                    position = "automatic",
                }
            )
        );

        var result = await service.CallAsync(
            "insert_live_word_caption",
            arguments.RootElement,
            CancellationToken.None
        );
        var raw = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        using var json = JsonDocument.Parse(raw);
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.insert_live_word_caption/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(version + 1, data.GetProperty("live_version").GetInt64());
        Assert.Equal("below", data.GetProperty("position").GetString());
        Assert.Equal(1, data.GetProperty("sequence_field_count_after").GetInt32());
        Assert.True(data.GetProperty("native_verified").GetBoolean());
        Assert.DoesNotContain("Sekretny podpis kontrolny", raw, StringComparison.Ordinal);
        Assert.Equal("Figure", host.Application.ActiveDocument.LastCaptionLabel);
        Assert.Equal(": Sekretny podpis kontrolny", host.Application.ActiveDocument.LastCaptionTitle);
        Assert.Equal(1, host.Application.UndoRecord.StartCount);
        Assert.Equal(1, host.Application.UndoRecord.EndCount);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
        Assert.True(host.Application.ScreenUpdating);
    }

    [Fact]
    public async Task RollsBackWhenWordDoesNotCreateExactlyOneCaptionField()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.SuppressCaptionField = true;
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        var selectionToken = await SelectionTokenAsync(service, documentId);
        using var arguments = CaptionArguments(documentId, version, selectionToken);

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_caption",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VALIDATION_FAILED", error.ErrorCode);
        Assert.Equal(0, host.Application.ActiveDocument.Fields.Count);
        Assert.Equal(1, host.Application.ActiveDocument.UndoCount);
        Assert.True(host.Application.ScreenUpdating);
    }

    [Fact]
    public async Task UsesOneExactExistingCustomCaptionLabel()
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
                    caption_kind = "custom",
                    custom_label = "Diagram",
                    title = "Przebieg procesu",
                }
            )
        );

        var result = await service.CallAsync(
            "insert_live_word_caption",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));

        Assert.True(json.RootElement.GetProperty("custom_label_used").GetBoolean());
        Assert.Equal("Diagram", host.Application.ActiveDocument.LastCaptionLabel);
        Assert.Equal(1, host.Application.ActiveDocument.Fields.Count);
    }

    [Fact]
    public async Task RejectsAnUnboundedCustomLabelCollectionBeforeUndo()
    {
        await using var host = new CaptionFakeHost();
        host.Application.CaptionLabels.ReportedCount = 1_025;
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
                    caption_kind = "custom",
                    custom_label = "Diagram",
                }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_caption",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("LIMIT_EXCEEDED", error.ErrorCode);
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
    }

    [Fact]
    public async Task InsertsAndUpdatesOneNativeTableOfFigures()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.Fields.Add(" SEQ Figure \\* ARABIC ");
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    caption_kind = "figure",
                    target = "document_end",
                }
            )
        );

        var result = await service.CallAsync(
            "insert_live_word_table_of_figures",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.insert_live_word_table_of_figures/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(1, data.GetProperty("matching_caption_count").GetInt32());
        Assert.Equal(1, data.GetProperty("table_of_figures_count_after").GetInt32());
        Assert.True(data.GetProperty("updated").GetBoolean());
        Assert.True(host.Application.ActiveDocument.TablesOfFigures.Item(1).Updated);
        Assert.Equal("Figure", host.Application.ActiveDocument.TablesOfFigures.LastCaption);
        Assert.Equal(1, host.Application.UndoRecord.StartCount);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
    }

    [Fact]
    public async Task RejectsTableOfFiguresWithoutMatchingCaptionsBeforeUndo()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    caption_kind = "table",
                }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_table_of_figures",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
        Assert.Equal(0, host.Application.ActiveDocument.TablesOfFigures.Count);
    }

    [Fact]
    public async Task InsertsUpdatesAndReacquiresOneNativeTableOfContentsWithoutReturningText()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    target = "document_start",
                    upper_heading_level = 2,
                    lower_heading_level = 4,
                    use_heading_styles = true,
                    use_outline_levels = true,
                }
            )
        );

        var result = await service.CallAsync(
            "insert_live_word_table_of_contents",
            arguments.RootElement,
            CancellationToken.None
        );
        var raw = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        using var json = JsonDocument.Parse(raw);
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.insert_live_word_table_of_contents/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(version + 1, data.GetProperty("live_version").GetInt64());
        Assert.Equal(0, data.GetProperty("table_of_contents_count_before").GetInt32());
        Assert.Equal(1, data.GetProperty("table_of_contents_count_after").GetInt32());
        Assert.Equal(1, data.GetProperty("table_of_contents_index").GetInt32());
        Assert.Equal(0, data.GetProperty("inserted_range").GetProperty("start").GetInt32());
        Assert.True(data.GetProperty("native_verified").GetBoolean());
        Assert.False(data.GetProperty("raw_field_code_returned").GetBoolean());
        Assert.False(data.GetProperty("result_text_returned").GetBoolean());
        Assert.DoesNotContain("TOC ", raw, StringComparison.Ordinal);
        Assert.Equal(0, host.Application.ActiveDocument.TablesOfContents.LastRangeStart);
        Assert.Equal(2, host.Application.ActiveDocument.TablesOfContents.LastUpperHeadingLevel);
        Assert.Equal(4, host.Application.ActiveDocument.TablesOfContents.LastLowerHeadingLevel);
        Assert.True(host.Application.ActiveDocument.TablesOfContents.LastUseOutlineLevels);
        Assert.Equal(1, host.Application.ActiveDocument.TablesOfContents.Item(1).UpdateCount);
        Assert.Equal(1, host.Application.ActiveDocument.RepaginateCount);
        Assert.Equal(1, host.Application.UndoRecord.StartCount);
        Assert.Equal(1, host.Application.UndoRecord.EndCount);
        Assert.True(host.Application.ScreenUpdating);
    }

    [Fact]
    public async Task RejectsInvalidTableOfContentsSourceConfigurationBeforeUndo()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    use_heading_styles = false,
                    use_outline_levels = false,
                }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_table_of_contents",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
        Assert.Equal(0, host.Application.ActiveDocument.TablesOfContents.Count);
    }

    [Fact]
    public async Task RejectsInvertedTableOfContentsHeadingLevelsBeforeUndo()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    upper_heading_level = 5,
                    lower_heading_level = 2,
                }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_table_of_contents",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
        Assert.Equal(0, host.Application.ActiveDocument.TablesOfContents.Count);
    }

    [Fact]
    public async Task RollsBackTableOfContentsWhenNativeFieldReadbackIsMissing()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.TablesOfContents.SuppressAddedField = true;
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    target = "document_end",
                    repaginate = false,
                }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_table_of_contents",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VALIDATION_FAILED", error.ErrorCode);
        Assert.Equal(0, host.Application.ActiveDocument.TablesOfContents.Count);
        Assert.Equal(1, host.Application.ActiveDocument.UndoCount);
        Assert.Equal(0, host.Application.ActiveDocument.RepaginateCount);
        Assert.True(host.Application.ScreenUpdating);
    }

    [Fact]
    public async Task UpdatesEveryNativeReferenceTableWithoutReturningResultText()
    {
        await using var host = new CaptionFakeHost();
        var document = host.Application.ActiveDocument;
        document.TablesOfContents.Seed(1);
        document.TablesOfFigures.Seed(1);
        document.TablesOfAuthorities.Seed(1);
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = ReferenceTableArguments(documentId, version);

        var result = await service.CallAsync(
            "update_live_word_reference_tables",
            arguments.RootElement,
            CancellationToken.None
        );
        var raw = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        using var json = JsonDocument.Parse(raw);
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.update_live_word_reference_tables/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(version + 1, data.GetProperty("live_version").GetInt64());
        Assert.Equal(3, data.GetProperty("updated_count").GetInt32());
        Assert.Equal(
            1,
            data.GetProperty("updated_counts").GetProperty("tables_of_contents").GetInt32()
        );
        Assert.Equal(
            1,
            data.GetProperty("updated_counts").GetProperty("tables_of_figures").GetInt32()
        );
        Assert.Equal(
            1,
            data.GetProperty("updated_counts").GetProperty("tables_of_authorities").GetInt32()
        );
        Assert.Equal(
            1,
            data.GetProperty("counts_before").GetProperty("tables_of_contents").GetInt32()
        );
        Assert.True(data.GetProperty("ranges_and_fields_verified").GetBoolean());
        Assert.False(data.GetProperty("raw_field_code_returned").GetBoolean());
        Assert.False(data.GetProperty("result_text_returned").GetBoolean());
        Assert.Equal(1, document.TablesOfContents.Item(1).UpdateCount);
        Assert.Equal(1, document.TablesOfFigures.Item(1).UpdateCount);
        Assert.Equal(1, document.TablesOfAuthorities.Item(1).UpdateCount);
        Assert.Equal(1, document.RepaginateCount);
        Assert.Equal(1, host.Application.UndoRecord.StartCount);
        Assert.Equal(1, host.Application.UndoRecord.EndCount);
        Assert.True(host.Application.ScreenUpdating);
    }

    [Fact]
    public async Task UpdatesOnlyOneExactReferenceTableIndexWithoutRepagination()
    {
        await using var host = new CaptionFakeHost();
        var document = host.Application.ActiveDocument;
        document.TablesOfFigures.Seed(2);
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = ReferenceTableArguments(
            documentId,
            version,
            kind: "table_of_figures",
            index: 2,
            repaginate: false
        );

        var result = await service.CallAsync(
            "update_live_word_reference_tables",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));

        Assert.Equal(1, json.RootElement.GetProperty("updated_count").GetInt32());
        Assert.Equal(0, document.TablesOfFigures.Item(1).UpdateCount);
        Assert.Equal(1, document.TablesOfFigures.Item(2).UpdateCount);
        Assert.Equal(0, document.RepaginateCount);
        Assert.False(
            json.RootElement.GetProperty("repagination").GetProperty("performed").GetBoolean()
        );
    }

    [Fact]
    public async Task RejectsMissingReferenceTablesBeforeUndo()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = ReferenceTableArguments(documentId, version);

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "update_live_word_reference_tables",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
    }

    [Fact]
    public async Task RejectsMoreThanOneHundredTwentyEightReferenceTablesBeforeUndo()
    {
        await using var host = new CaptionFakeHost();
        host.Application.ActiveDocument.TablesOfContents.Seed(129);
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = ReferenceTableArguments(documentId, version);

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "update_live_word_reference_tables",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("LIMIT_EXCEEDED", error.ErrorCode);
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
    }

    [Fact]
    public async Task RollsBackEveryReferenceTableWhenReadbackBecomesInvalid()
    {
        await using var host = new CaptionFakeHost();
        var document = host.Application.ActiveDocument;
        document.TablesOfContents.Seed(1);
        document.TablesOfFigures.Seed(1);
        document.TablesOfFigures.Item(1).InvalidateRangeOnUpdate = true;
        var service = new WordLiveService(host);
        var (documentId, version) = await ConnectAsync(service);
        using var arguments = ReferenceTableArguments(documentId, version);

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "update_live_word_reference_tables",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VALIDATION_FAILED", error.ErrorCode);
        Assert.Equal(0, document.TablesOfContents.Item(1).UpdateCount);
        Assert.Equal(0, document.TablesOfFigures.Item(1).UpdateCount);
        Assert.True(document.TablesOfFigures.Item(1).Range.End > 0);
        Assert.Equal(1, document.UndoCount);
        Assert.True(host.Application.ScreenUpdating);
    }

    private static JsonDocument CaptionArguments(
        string documentId,
        long version,
        string selectionToken
    ) =>
        JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    selection_token = selectionToken,
                }
            )
        );

    private static JsonDocument ReferenceTableArguments(
        string documentId,
        long version,
        string kind = "all",
        int? index = null,
        bool repaginate = true
    ) =>
        JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    kind,
                    index,
                    repaginate,
                }
            )
        );

    private static async Task<(string DocumentId, long Version)> ConnectAsync(
        WordLiveService service
    )
    {
        using var arguments = JsonDocument.Parse("""{"use_active":true}""");
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

internal sealed class CaptionFakeHost : IWordComHost
{
    public CaptionFakeApplication Application { get; } = new();

    public Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(operation(Application));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class CaptionFakeApplication
{
    public CaptionFakeApplication()
    {
        ActiveDocument = new CaptionFakeDocument(this);
        Documents = new CaptionFakeDocuments(ActiveDocument);
        Selection = new CaptionFakeSelection(ActiveDocument.Range(8, 16));
        UndoRecord = new CaptionFakeUndoRecord(ActiveDocument);
        CaptionLabels = new CaptionFakeCaptionLabels();
    }

    public CaptionFakeDocument ActiveDocument { get; set; }
    public CaptionFakeDocuments Documents { get; }
    public CaptionFakeSelection Selection { get; }
    public CaptionFakeWindow ActiveWindow { get; } = new();
    public CaptionFakeUndoRecord UndoRecord { get; }
    public CaptionFakeCaptionLabels CaptionLabels { get; }
    public bool ScreenUpdating { get; set; } = true;
}

public sealed class CaptionFakeDocuments
{
    private readonly CaptionFakeDocument _document;

    public CaptionFakeDocuments(CaptionFakeDocument document) => _document = document;
    public int Count => 1;
    public CaptionFakeDocument Item(int index) =>
        index == 1 ? _document : throw new IndexOutOfRangeException();
}

public sealed class CaptionFakeDocument
{
    private readonly CaptionFakeApplication _application;
    private int _undoFieldCount;
    private const string Body = "Document body with one selected object and enough context for hashing.\r";

    public CaptionFakeDocument(CaptionFakeApplication application)
    {
        _application = application;
        Fields = new CaptionFakeFields();
        TablesOfContents = new CaptionFakeReferenceTables(this);
        TablesOfFigures = new CaptionFakeTablesOfFigures(this);
        TablesOfAuthorities = new CaptionFakeReferenceTables();
    }

    public string Name => "Captions.docx";
    public string FullName => @"C:\Fixtures\Captions.docx";
    public string Path => @"C:\Fixtures";
    public bool Saved => true;
    public bool ReadOnly => false;
    public bool Final => false;
    public int CompatibilityMode => 15;
    public int ProtectionType => -1;
    public CaptionFakeRange Content => Range(0, Body.Length);
    public CaptionFakeFields Fields { get; }
    public CaptionFakeReferenceTables TablesOfContents { get; }
    public CaptionFakeTablesOfFigures TablesOfFigures { get; }
    public CaptionFakeReferenceTables TablesOfAuthorities { get; }
    public CaptionFakeCountCollection Paragraphs { get; } = new(2);
    public CaptionFakeCountCollection OMaths { get; } = new(0);
    public CaptionFakeCountCollection Tables { get; } = new(1);
    public CaptionFakeCountCollection Bookmarks { get; } = new(0);
    public CaptionFakeCountCollection InlineShapes { get; } = new(1);
    public CaptionFakeCountCollection Shapes { get; } = new(0);
    public CaptionFakeCountCollection Comments { get; } = new(0);
    public CaptionFakeCountCollection Footnotes { get; } = new(0);
    public CaptionFakeCountCollection Endnotes { get; } = new(0);
    public CaptionFakeCountCollection Sections { get; } = new(1);
    public bool SuppressCaptionField { get; set; }
    public string LastCaptionLabel { get; private set; } = "";
    public string LastCaptionTitle { get; private set; } = "";
    public int UndoCount { get; private set; }
    public int RepaginateCount { get; private set; }

    public CaptionFakeRange Range(int start, int end) =>
        new(this, start, end, Body[start..Math.Min(end, Body.Length)]);

    public void Activate() => _application.ActiveDocument = this;

    public void Repaginate() => RepaginateCount++;

    public void InsertCaption(object label, string title)
    {
        LastCaptionLabel = label switch
        {
            -1 => "Figure",
            -2 => "Table",
            -3 => "Equation",
            _ => Convert.ToString(label) ?? "",
        };
        LastCaptionTitle = title;
        if (!SuppressCaptionField)
        {
            Fields.Add($" SEQ {LastCaptionLabel} \\* ARABIC ");
        }
    }

    public void BeginUndoSnapshot()
    {
        _undoFieldCount = Fields.Count;
        TablesOfContents.CaptureUndoSnapshot();
        TablesOfFigures.CaptureUndoSnapshot();
        TablesOfAuthorities.CaptureUndoSnapshot();
    }

    public bool Undo(int count)
    {
        if (count != 1)
        {
            return false;
        }
        Fields.Trim(_undoFieldCount);
        TablesOfContents.RestoreUndoSnapshot();
        TablesOfFigures.RestoreUndoSnapshot();
        TablesOfAuthorities.RestoreUndoSnapshot();
        UndoCount++;
        return true;
    }
}

public sealed class CaptionFakeRange
{
    private readonly CaptionFakeDocument _document;

    public CaptionFakeRange(CaptionFakeDocument document, int start, int end, string text)
    {
        _document = document;
        Start = start;
        End = end;
        Text = text;
        Fields = new CaptionFakeCountCollection(0);
    }

    public int Start { get; private set; }
    public int End { get; private set; }
    public int StoryType => 1;
    public string Text { get; }
    public CaptionFakeCountCollection Fields { get; }
    public CaptionFakeRange Duplicate => this;

    public void SetRange(int start, int end)
    {
        Start = start;
        End = end;
    }

    public void InsertCaption(
        object label,
        string title,
        object titleAutoText,
        int position,
        bool excludeLabel
    ) => _document.InsertCaption(label, title);
}

public sealed class CaptionFakeSelection
{
    public CaptionFakeSelection(CaptionFakeRange range) => Range = range;
    public CaptionFakeRange Range { get; }
    public int Type => 2;
}

public sealed class CaptionFakeWindow
{
    public int Hwnd => 4411;
}

public sealed class CaptionFakeCaptionLabels
{
    private readonly CaptionFakeCaptionLabel[] _labels =
    [
        new("Figure", 1),
        new("Table", 0),
        new("Equation", 1),
        new("Diagram", 1),
    ];

    public int ReportedCount { get; set; } = 4;
    public int Count => ReportedCount;

    public CaptionFakeCaptionLabel Item(int index) =>
        index switch
        {
            -1 => _labels[0],
            -2 => _labels[1],
            -3 => _labels[2],
            >= 1 and <= 4 => _labels[index - 1],
            _ => throw new IndexOutOfRangeException(),
        };
}

public sealed class CaptionFakeCaptionLabel
{
    public CaptionFakeCaptionLabel(string name, int position)
    {
        Name = name;
        Position = position;
    }

    public string Name { get; }
    public int Position { get; }
}

public sealed class CaptionFakeFields
{
    private readonly List<CaptionFakeField> _items = [];
    public int Count => _items.Count;
    public CaptionFakeField Item(int index) => _items[index - 1];
    public void Add(string code) => _items.Add(new CaptionFakeField(code));
    public void Trim(int count) => _items.RemoveRange(count, _items.Count - count);
}

public sealed class CaptionFakeField
{
    public CaptionFakeField(string code) => Code = new CaptionFakeFieldCode(code);
    public int Type => 12;
    public CaptionFakeFieldCode Code { get; }
}

public sealed class CaptionFakeFieldCode
{
    public CaptionFakeFieldCode(string text) => Text = text;
    public string Text { get; }
}

public sealed class CaptionFakeReferenceTables
{
    private readonly CaptionFakeDocument? _document;
    private readonly List<CaptionFakeReferenceTable> _items = [];
    private int _undoCount;

    public CaptionFakeReferenceTables(CaptionFakeDocument? document = null) =>
        _document = document;

    public int Count => _items.Count;
    public CaptionFakeReferenceTable Item(int index) => _items[index - 1];
    public bool SuppressAddedField { get; set; }
    public int LastRangeStart { get; private set; } = -1;
    public bool LastUseHeadingStyles { get; private set; }
    public int LastUpperHeadingLevel { get; private set; }
    public int LastLowerHeadingLevel { get; private set; }
    public bool LastUseOutlineLevels { get; private set; }

    public CaptionFakeReferenceTable Add(
        CaptionFakeRange range,
        bool useHeadingStyles,
        int upperHeadingLevel,
        int lowerHeadingLevel,
        bool useFields,
        string tableId,
        bool rightAlignPageNumbers,
        bool includePageNumbers,
        string addedStyles,
        bool useHyperlinks,
        bool hidePageNumbersInWeb,
        bool useOutlineLevels
    )
    {
        _ = _document ?? throw new InvalidOperationException("Add is unavailable");
        LastRangeStart = range.Start;
        LastUseHeadingStyles = useHeadingStyles;
        LastUpperHeadingLevel = upperHeadingLevel;
        LastLowerHeadingLevel = lowerHeadingLevel;
        LastUseOutlineLevels = useOutlineLevels;
        var table = new CaptionFakeReferenceTable(
            range.Start,
            range.Start + 20,
            SuppressAddedField ? 0 : 1
        );
        _items.Add(table);
        return table;
    }

    public void Seed(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var start = 20 + (_items.Count * 20);
            _items.Add(new CaptionFakeReferenceTable(start, start + 12));
        }
    }

    public void CaptureUndoSnapshot()
    {
        _undoCount = _items.Count;
        foreach (var item in _items)
        {
            item.CaptureUndoSnapshot();
        }
    }

    public void RestoreUndoSnapshot()
    {
        if (_items.Count > _undoCount)
        {
            _items.RemoveRange(_undoCount, _items.Count - _undoCount);
        }
        foreach (var item in _items)
        {
            item.RestoreUndoSnapshot();
        }
    }
}

public sealed class CaptionFakeTablesOfFigures
{
    private readonly CaptionFakeDocument _document;
    private readonly List<CaptionFakeTableOfFigures> _items = [];
    private int _undoCount;

    public CaptionFakeTablesOfFigures(CaptionFakeDocument document) => _document = document;
    public int Count => _items.Count;
    public string LastCaption { get; private set; } = "";
    public CaptionFakeTableOfFigures Item(int index) => _items[index - 1];

    public void Seed(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var start = 40 + (_items.Count * 20);
            _items.Add(new CaptionFakeTableOfFigures(start, start + 12));
        }
    }

    public CaptionFakeTableOfFigures Add(
        CaptionFakeRange range,
        string caption,
        bool includeLabel,
        bool useHeadingStyles,
        int upperHeadingLevel,
        int lowerHeadingLevel,
        bool useFields,
        string tableId,
        bool rightAlignPageNumbers,
        bool includePageNumbers,
        string addedStyles,
        bool useHyperlinks,
        bool hidePageNumbersInWeb
    )
    {
        LastCaption = caption;
        var table = new CaptionFakeTableOfFigures(_document.Content.End, _document.Content.End + 20);
        _items.Add(table);
        return table;
    }

    public void CaptureUndoSnapshot()
    {
        _undoCount = _items.Count;
        foreach (var item in _items)
        {
            item.CaptureUndoSnapshot();
        }
    }

    public void RestoreUndoSnapshot()
    {
        if (_items.Count > _undoCount)
        {
            _items.RemoveRange(_undoCount, _items.Count - _undoCount);
        }
        foreach (var item in _items)
        {
            item.RestoreUndoSnapshot();
        }
    }
}

public class CaptionFakeReferenceTable
{
    private int _undoStart;
    private int _undoEnd;
    private int _undoUpdateCount;

    public CaptionFakeReferenceTable(int start, int end, int fieldCount = 1)
    {
        Range = new CaptionFakeTableOfFiguresRange(start, end, fieldCount);
    }

    public CaptionFakeTableOfFiguresRange Range { get; }
    public bool Updated => UpdateCount > 0;
    public int UpdateCount { get; private set; }
    public bool InvalidateRangeOnUpdate { get; set; }

    public void Update()
    {
        UpdateCount++;
        if (InvalidateRangeOnUpdate)
        {
            Range.Invalidate();
        }
    }

    public void CaptureUndoSnapshot()
    {
        _undoStart = Range.Start;
        _undoEnd = Range.End;
        _undoUpdateCount = UpdateCount;
    }

    public void RestoreUndoSnapshot()
    {
        Range.Restore(_undoStart, _undoEnd);
        UpdateCount = _undoUpdateCount;
    }
}

public sealed class CaptionFakeTableOfFigures : CaptionFakeReferenceTable
{
    public CaptionFakeTableOfFigures(int start, int end)
        : base(start, end) { }
}

public sealed class CaptionFakeTableOfFiguresRange
{
    public CaptionFakeTableOfFiguresRange(int start, int end, int fieldCount = 1)
    {
        Start = start;
        End = end;
        Fields = new CaptionFakeCountCollection(fieldCount);
    }

    public int Start { get; private set; }
    public int End { get; private set; }
    public CaptionFakeCountCollection Fields { get; }
    public CaptionFakeTableOfFiguresRange Duplicate => this;

    public void Invalidate() => End = Start;

    public void Restore(int start, int end)
    {
        Start = start;
        End = end;
    }
}

public sealed class CaptionFakeUndoRecord
{
    private readonly CaptionFakeDocument _document;
    public CaptionFakeUndoRecord(CaptionFakeDocument document) => _document = document;
    public int StartCount { get; private set; }
    public int EndCount { get; private set; }

    public void StartCustomRecord(string name)
    {
        StartCount++;
        _document.BeginUndoSnapshot();
    }

    public void EndCustomRecord() => EndCount++;
}

public sealed class CaptionFakeCountCollection
{
    public CaptionFakeCountCollection(int count) => Count = count;
    public int Count { get; }
}
