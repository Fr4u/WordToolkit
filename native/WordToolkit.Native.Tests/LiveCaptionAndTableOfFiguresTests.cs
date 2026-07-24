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

        Assert.Equal("1.0", caption["operationVersion"]!.GetValue<string>());
        Assert.Equal("1.0", table["operationVersion"]!.GetValue<string>());
        Assert.False(caption["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(table["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(caption["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(table["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.NotNull(caption["permissions"]);
        Assert.NotNull(caption["reversibility"]);
        Assert.NotNull(caption["outputSchema"]);
        Assert.NotNull(table["permissions"]);
        Assert.NotNull(table["reversibility"]);
        Assert.NotNull(table["outputSchema"]);
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
    private int _undoTableOfFiguresCount;
    private const string Body = "Document body with one selected object and enough context for hashing.\r";

    public CaptionFakeDocument(CaptionFakeApplication application)
    {
        _application = application;
        Fields = new CaptionFakeFields();
        TablesOfFigures = new CaptionFakeTablesOfFigures(this);
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
    public CaptionFakeTablesOfFigures TablesOfFigures { get; }
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

    public CaptionFakeRange Range(int start, int end) =>
        new(this, start, end, Body[start..Math.Min(end, Body.Length)]);

    public void Activate() => _application.ActiveDocument = this;

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
        _undoTableOfFiguresCount = TablesOfFigures.Count;
    }

    public bool Undo(int count)
    {
        if (count != 1)
        {
            return false;
        }
        Fields.Trim(_undoFieldCount);
        TablesOfFigures.Trim(_undoTableOfFiguresCount);
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

public sealed class CaptionFakeTablesOfFigures
{
    private readonly CaptionFakeDocument _document;
    private readonly List<CaptionFakeTableOfFigures> _items = [];

    public CaptionFakeTablesOfFigures(CaptionFakeDocument document) => _document = document;
    public int Count => _items.Count;
    public string LastCaption { get; private set; } = "";
    public CaptionFakeTableOfFigures Item(int index) => _items[index - 1];

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

    public void Trim(int count) => _items.RemoveRange(count, _items.Count - count);
}

public sealed class CaptionFakeTableOfFigures
{
    public CaptionFakeTableOfFigures(int start, int end)
    {
        Range = new CaptionFakeTableOfFiguresRange(start, end);
    }

    public CaptionFakeTableOfFiguresRange Range { get; }
    public bool Updated { get; private set; }
    public void Update() => Updated = true;
}

public sealed class CaptionFakeTableOfFiguresRange
{
    public CaptionFakeTableOfFiguresRange(int start, int end)
    {
        Start = start;
        End = end;
    }

    public int Start { get; }
    public int End { get; }
    public CaptionFakeCountCollection Fields { get; } = new(1);
    public CaptionFakeTableOfFiguresRange Duplicate => this;
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
