using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LiveDrawingLayoutTests
{
    [Fact]
    public void PublishesAClosedVersionedTokenLeanActionContract()
    {
        var tool = ToolCatalog
            .LoadNativeWordTools()
            .InspectAction("inspect_live_word_drawing_layout")["tool"]!
            .AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.Equal(
            "read_connected_document_layout_and_optionally_repaginate",
            tool["permissions"]!["microsoft_word"]!.GetValue<string>()
        );
        Assert.Equal(
            100,
            tool["inputSchema"]!["properties"]!["limit"]!["maximum"]!.GetValue<int>()
        );
        Assert.False(
            tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>()
        );
        Assert.NotNull(tool["outputSchema"]);
        Assert.True(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.True(tool["annotations"]!["openWorldHint"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ProjectsExecutedFloatingInlineGroupAndSmartArtLayoutWithoutRawCom()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    limit = 10,
                    include_group_items = true,
                    include_smartart_nodes = true,
                    include_text = true,
                    max_text_chars = 160,
                    include_screen_pixels = true,
                    repaginate = true,
                }
            )
        );

        var result = await service.CallAsync(
            "inspect_live_word_drawing_layout",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.inspect_live_word_drawing_layout/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal("microsoft_word_object_model", data.GetProperty("layout_source").GetString());
        Assert.True(data.GetProperty("repagination").GetProperty("performed").GetBoolean());
        Assert.Equal(1, host.Application.ActiveDocument.RepaginateCount);
        Assert.Equal(3, data.GetProperty("scan").GetProperty("root_objects_scanned").GetInt32());
        Assert.Equal(3, data.GetProperty("scan").GetProperty("returned_count").GetInt32());
        Assert.True(data.GetProperty("scan").GetProperty("total_count_exact").GetBoolean());
        Assert.False(data.GetProperty("scan").GetProperty("response_truncated").GetBoolean());

        var items = data.GetProperty("items").EnumerateArray().ToArray();
        var group = Assert.Single(
            items,
            item => item.GetProperty("object_kind").GetString() == "group"
        );
        Assert.Equal(72, group.GetProperty("page_relative_bounds_points").GetProperty("x").GetDouble());
        var groupProjection = group.GetProperty("group");
        Assert.Equal("group_local", groupProjection.GetProperty("coordinate_space").GetString());
        Assert.Equal(1, groupProjection.GetProperty("returned_member_count").GetInt32());
        Assert.Equal(
            "picture",
            groupProjection.GetProperty("members")[0].GetProperty("object_kind").GetString()
        );

        var smartArt = Assert.Single(
            items,
            item => item.GetProperty("object_kind").GetString() == "smartart"
        );
        var smartArtProjection = smartArt.GetProperty("smartart");
        Assert.Equal(1, smartArtProjection.GetProperty("total_node_count").GetInt32());
        var node = smartArtProjection.GetProperty("nodes")[0];
        Assert.Equal("Etap A", node.GetProperty("text").GetString());
        Assert.Equal(1, node.GetProperty("rendered_shape_count").GetInt32());
        Assert.Equal(
            "smartart_layout",
            node.GetProperty("rendered_shapes")[0].GetProperty("coordinate_space").GetString()
        );

        var inline = Assert.Single(
            items,
            item => item.GetProperty("collection_kind").GetString() == "inline"
        );
        Assert.Equal("text_flow", inline.GetProperty("flow").GetProperty("coordinate_space").GetString());
        Assert.True(
            inline.GetProperty("visible_page_position_points").GetProperty("viewport_dependent").GetBoolean()
        );
        Assert.Equal(
            "active_window_screen",
            inline.GetProperty("viewport_bounds_pixels").GetProperty("coordinate_space").GetString()
        );
        Assert.False(
            data.GetProperty("geometry_contract").GetProperty("screen_pixels_are_page_geometry").GetBoolean()
        );
        Assert.False(data.GetProperty("disclosure").GetProperty("raw_xml_returned").GetBoolean());
        Assert.False(data.GetProperty("disclosure").GetProperty("raw_com_objects_returned").GetBoolean());
        Assert.Equal(0, data.GetProperty("diagnostics").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task RejectsAnUnboundedViewportRequestBeforeCallingWord()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var callsAfterConnect = host.CallCount;
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    limit = 11,
                    include_screen_pixels = true,
                }
            )
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "inspect_live_word_drawing_layout",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("LIMIT_EXCEEDED", error.ErrorCode);
        Assert.Equal(callsAfterConnect, host.CallCount);
    }

    [Fact]
    public async Task DefaultProjectionDoesNotReadOrReturnSensitiveDrawingText()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    limit = 10,
                    include_group_items = true,
                    include_smartart_nodes = true,
                    include_text = false,
                    include_screen_pixels = false,
                }
            )
        );

        var result = await service.CallAsync(
            "inspect_live_word_drawing_layout",
            arguments.RootElement,
            CancellationToken.None
        );
        var raw = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        using var json = JsonDocument.Parse(raw);

        Assert.Equal(0, host.Application.SensitiveReads.Count);
        Assert.DoesNotContain("Group 1", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Etap A", raw, StringComparison.Ordinal);
        Assert.False(
            json.RootElement
                .GetProperty("disclosure")
                .GetProperty("sensitive_text_requested")
                .GetBoolean()
        );
        Assert.Equal(
            0,
            json.RootElement
                .GetProperty("disclosure")
                .GetProperty("sensitive_text_fields_returned")
                .GetInt32()
        );
    }

    [Fact]
    public void PublishesGuardedSmartArtTextEditContracts()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var prepare = catalog
            .InspectAction("prepare_live_word_smartart_text_edits")["tool"]!
            .AsObject();
        var apply = catalog
            .InspectAction("apply_live_word_smartart_text_edits")["tool"]!
            .AsObject();

        Assert.Equal("1.0", prepare["operationVersion"]!.GetValue<string>());
        Assert.Equal("1.0", apply["operationVersion"]!.GetValue<string>());
        Assert.True(prepare["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(apply["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.Equal(
            32,
            apply["inputSchema"]!["properties"]!["edits"]!["maxItems"]!.GetValue<int>()
        );
        Assert.False(
            apply["inputSchema"]!["additionalProperties"]!.GetValue<bool>()
        );
        Assert.NotNull(prepare["outputSchema"]);
        Assert.NotNull(apply["outputSchema"]);
    }

    [Fact]
    public async Task AppliesOneTokenVerifiedSmartArtTextEditAndRepaginatesOnce()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var (token, version) = await PrepareSmartArtNodeAsync(service, documentId);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    edits = new[]
                    {
                        new
                        {
                            smartart_node_token = token,
                            replacement_text = "Etap zweryfikowany",
                        },
                    },
                }
            )
        );

        var result = await service.CallAsync(
            "apply_live_word_smartart_text_edits",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.apply_live_word_smartart_text_edits/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.True(data.GetProperty("mutated").GetBoolean());
        Assert.Equal(1, data.GetProperty("changed_count").GetInt32());
        Assert.Equal(version + 1, data.GetProperty("live_version").GetInt64());
        Assert.Equal("Etap zweryfikowany", host.Application.ActiveDocument.SmartArtTextRange.TextValue);
        Assert.Equal(1, host.Application.ActiveDocument.RepaginateCount);
        Assert.True(host.Application.ScreenUpdating);
        Assert.Equal(1, host.Application.UndoRecord.StartCount);
        Assert.Equal(1, host.Application.UndoRecord.EndCount);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
    }

    [Fact]
    public async Task RejectsAStaleSmartArtContextBeforeStartingUndo()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var (token, version) = await PrepareSmartArtNodeAsync(service, documentId);
        host.Application.ActiveDocument.SmartArtTextRange.ForceText("Zmiana zewnętrzna");
        using var arguments = SmartArtApplyArguments(
            documentId,
            version,
            token,
            "Nowy tekst"
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "apply_live_word_smartart_text_edits",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VERSION_CONFLICT", error.ErrorCode);
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
        Assert.Equal("Zmiana zewnętrzna", host.Application.ActiveDocument.SmartArtTextRange.TextValue);
    }

    [Fact]
    public async Task RollsBackWhenWordDoesNotPreserveExactSmartArtText()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var (token, version) = await PrepareSmartArtNodeAsync(service, documentId);
        host.Application.ActiveDocument.SmartArtTextRange.WriteTransform = text => text + "!";
        using var arguments = SmartArtApplyArguments(
            documentId,
            version,
            token,
            "Tekst żądany"
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "apply_live_word_smartart_text_edits",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VALIDATION_FAILED", error.ErrorCode);
        Assert.Equal("Etap A", host.Application.ActiveDocument.SmartArtTextRange.TextValue);
        Assert.Equal(1, host.Application.ActiveDocument.UndoCount);
        Assert.Equal(1, host.Application.UndoRecord.StartCount);
        Assert.Equal(1, host.Application.UndoRecord.EndCount);
        Assert.True(host.Application.ScreenUpdating);
    }

    [Fact]
    public async Task StableSmartArtNoOpDoesNotCreateUndoRepaginateOrAdvanceVersion()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var (token, version) = await PrepareSmartArtNodeAsync(service, documentId);
        using var arguments = SmartArtApplyArguments(documentId, version, token, "Etap A");

        var result = await service.CallAsync(
            "apply_live_word_smartart_text_edits",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        var data = json.RootElement;

        Assert.False(data.GetProperty("mutated").GetBoolean());
        Assert.Equal(0, data.GetProperty("changed_count").GetInt32());
        Assert.Equal(version, data.GetProperty("live_version").GetInt64());
        Assert.Equal(0, host.Application.UndoRecord.StartCount);
        Assert.Equal(0, host.Application.ActiveDocument.RepaginateCount);
    }

    private static async Task<(string Token, long Version)> PrepareSmartArtNodeAsync(
        WordLiveService service,
        string documentId
    )
    {
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    story_type = "main_text",
                    story_link_index = 0,
                    collection_kind = "floating",
                    source_index = 2,
                    include_text = false,
                }
            )
        );
        var result = await service.CallAsync(
            "prepare_live_word_smartart_text_edits",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        var data = json.RootElement;
        Assert.True(
            data.GetProperty("disclosure")
                .GetProperty("sensitive_text_read_for_guarding")
                .GetBoolean()
        );
        Assert.False(
            data.GetProperty("disclosure").GetProperty("sensitive_text_returned").GetBoolean()
        );
        Assert.False(data.GetProperty("nodes")[0].TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String);
        return (
            data.GetProperty("nodes")[0].GetProperty("smartart_node_token").GetString()!,
            data.GetProperty("live_version").GetInt64()
        );
    }

    private static JsonDocument SmartArtApplyArguments(
        string documentId,
        long version,
        string token,
        string replacementText
    ) =>
        JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    edits = new[]
                    {
                        new
                        {
                            smartart_node_token = token,
                            replacement_text = replacementText,
                        },
                    },
                }
            )
        );

    private static async Task<string> ConnectAsync(WordLiveService service)
    {
        using var arguments = JsonDocument.Parse("""{"use_active":true,"activate":true}""");
        var connected = await service.CallAsync(
            "connect_live_word_document",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(connected, JsonDefaults.Compact)
        );
        return json.RootElement.GetProperty("live_document_id").GetString()!;
    }
}

internal sealed class DrawingLayoutFakeHost : IWordComHost
{
    public DrawingLayoutFakeApplication Application { get; } = new();
    public int CallCount { get; private set; }

    public Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult(operation(Application));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class DrawingLayoutFakeApplication
{
    public DrawingLayoutFakeApplication()
    {
        ActiveDocument = new DrawingLayoutFakeDocument(this, SensitiveReads);
        Documents = new DrawingLayoutFakeDocuments(ActiveDocument);
        UndoRecord = new DrawingLayoutFakeUndoRecord(this);
    }

    public DrawingLayoutFakeSensitiveReads SensitiveReads { get; } = new();
    public DrawingLayoutFakeDocument ActiveDocument { get; set; }
    public DrawingLayoutFakeDocuments Documents { get; }
    public DrawingLayoutFakeWindow ActiveWindow { get; } = new();
    public DrawingLayoutFakeUndoRecord UndoRecord { get; }
    public bool ScreenUpdating { get; set; } = true;
}

public sealed class DrawingLayoutFakeDocuments
{
    private readonly DrawingLayoutFakeDocument _document;

    public DrawingLayoutFakeDocuments(DrawingLayoutFakeDocument document)
    {
        _document = document;
    }

    public int Count => 1;

    public DrawingLayoutFakeDocument Item(int index) =>
        index == 1 ? _document : throw new IndexOutOfRangeException();
}

public sealed class DrawingLayoutFakeDocument
{
    private readonly DrawingLayoutFakeApplication _application;
    private string[]? _undoSmartArtTexts;

    public DrawingLayoutFakeDocument(
        DrawingLayoutFakeApplication application,
        DrawingLayoutFakeSensitiveReads sensitiveReads
    )
    {
        _application = application;
        var group = DrawingLayoutFakeShape.Group(sensitiveReads);
        var smartArt = DrawingLayoutFakeShape.SmartArtRoot(sensitiveReads);
        Shapes = new DrawingLayoutFakeCollection<DrawingLayoutFakeShape>(group, smartArt);
        InlineShapes = new DrawingLayoutFakeCollection<DrawingLayoutFakeInlineShape>(
            new DrawingLayoutFakeInlineShape(sensitiveReads)
        );
    }

    public string Name => "Drawing-layout.docx";
    public string FullName => @"C:\Fixtures\Drawing-layout.docx";
    public string Path => @"C:\Fixtures";
    public bool Saved => true;
    public bool ReadOnly => false;
    public int CompatibilityMode => 15;
    public int ProtectionType => -1;
    public DrawingLayoutFakeCollection<DrawingLayoutFakeShape> Shapes { get; }
    public DrawingLayoutFakeCollection<DrawingLayoutFakeInlineShape> InlineShapes { get; }
    public DrawingLayoutFakeCountCollection Paragraphs { get; } = new(3);
    public DrawingLayoutFakeCountCollection OMaths { get; } = new(0);
    public DrawingLayoutFakeCountCollection Tables { get; } = new(0);
    public DrawingLayoutFakeCountCollection Fields { get; } = new(0);
    public DrawingLayoutFakeCountCollection Bookmarks { get; } = new(0);
    public DrawingLayoutFakeCountCollection Comments { get; } = new(0);
    public DrawingLayoutFakeCountCollection Footnotes { get; } = new(0);
    public DrawingLayoutFakeCountCollection Endnotes { get; } = new(0);
    public DrawingLayoutFakeCountCollection Sections { get; } = new(1);
    public DrawingLayoutFakeStoryRanges StoryRanges { get; } = new();
    public DrawingLayoutFakeRange Content => Range(0, 1);
    public string WordOpenXML
    {
        get
        {
            var smartArt = Shapes.Item(2).SmartArt!;
            return string.Join(
                "|",
                Enumerable.Range(1, smartArt.AllNodes.Count)
                    .Select(
                        index => smartArt.AllNodes.Item(index).TextFrame2.TextRange.TextValue
                    )
            );
        }
    }
    public int RepaginateCount { get; private set; }
    public int UndoCount { get; private set; }
    public DrawingLayoutFakeTextRange SmartArtTextRange =>
        Shapes.Item(2).SmartArt!.AllNodes.Item(1).TextFrame2.TextRange;

    public DrawingLayoutFakeRange Range(int start, int end) =>
        new(start, end, page: 1, section: 1);

    public void Activate()
    {
        _application.ActiveDocument = this;
    }

    public void Repaginate()
    {
        RepaginateCount++;
    }

    public void BeginUndoSnapshot()
    {
        var smartArt = Shapes.Item(2).SmartArt!;
        _undoSmartArtTexts = Enumerable.Range(1, smartArt.AllNodes.Count)
            .Select(index => smartArt.AllNodes.Item(index).TextFrame2.TextRange.TextValue)
            .ToArray();
    }

    public bool Undo(int count)
    {
        if (count != 1 || _undoSmartArtTexts is null)
        {
            return false;
        }
        var smartArt = Shapes.Item(2).SmartArt!;
        for (var index = 1; index <= _undoSmartArtTexts.Length; index++)
        {
            smartArt.AllNodes.Item(index).TextFrame2.TextRange.ForceText(
                _undoSmartArtTexts[index - 1]
            );
        }
        UndoCount++;
        _undoSmartArtTexts = null;
        return true;
    }
}

public sealed class DrawingLayoutFakeUndoRecord
{
    private readonly DrawingLayoutFakeApplication _application;

    public DrawingLayoutFakeUndoRecord(DrawingLayoutFakeApplication application)
    {
        _application = application;
    }

    public int StartCount { get; private set; }
    public int EndCount { get; private set; }

    public void StartCustomRecord(string name)
    {
        StartCount++;
        _application.ActiveDocument.BeginUndoSnapshot();
    }

    public void EndCustomRecord()
    {
        EndCount++;
    }
}

public sealed class DrawingLayoutFakeStoryRanges
{
    public object Item(int storyType) => throw new InvalidOperationException();
}

public sealed class DrawingLayoutFakeWindow
{
    public int Hwnd => 7301;

    public void GetPoint(
        ref int left,
        ref int top,
        ref int width,
        ref int height,
        object target
    )
    {
        left = 100;
        top = 200;
        width = 300;
        height = 150;
    }
}

public sealed class DrawingLayoutFakeCountCollection
{
    public DrawingLayoutFakeCountCollection(int count)
    {
        Count = count;
    }

    public int Count { get; }
}

public sealed class DrawingLayoutFakeCollection<T>
{
    private readonly T[] _items;

    public DrawingLayoutFakeCollection(params T[] items)
    {
        _items = items;
    }

    public int Count => _items.Length;

    public T Item(int index) =>
        index >= 1 && index <= _items.Length
            ? _items[index - 1]
            : throw new IndexOutOfRangeException();
}

public sealed class DrawingLayoutFakeRange
{
    private readonly IReadOnlyDictionary<int, int> _information;

    public DrawingLayoutFakeRange(
        int start,
        int end,
        int page,
        int section,
        int x = -1,
        int y = -1
    )
    {
        Start = start;
        End = end;
        _information = new Dictionary<int, int>
        {
            [2] = section,
            [3] = page,
            [5] = x,
            [6] = y,
        };
    }

    public int Start { get; }
    public int End { get; }
    public int StoryType => 1;
    public string Text => "\r";
    public string WordOpenXML => $"<range start=\"{Start}\" end=\"{End}\" />";

    public int get_Information(int code) => _information[code];
}

public sealed class DrawingLayoutFakeWrapFormat
{
    public int Type => 0;
    public int Side => 0;
    public double DistanceLeft => 6;
    public double DistanceRight => 6;
    public double DistanceTop => 3;
    public double DistanceBottom => 3;
}

public sealed class DrawingLayoutFakeShape
{
    private readonly string _name;
    private readonly DrawingLayoutFakeSensitiveReads _sensitiveReads;

    private DrawingLayoutFakeShape(
        int type,
        string name,
        double left,
        double top,
        DrawingLayoutFakeSensitiveReads sensitiveReads
    )
    {
        Type = type;
        _name = name;
        Left = left;
        Top = top;
        _sensitiveReads = sensitiveReads;
    }

    public static DrawingLayoutFakeShape Group(DrawingLayoutFakeSensitiveReads sensitiveReads)
    {
        var shape = new DrawingLayoutFakeShape(6, "Group 1", 72, 144, sensitiveReads);
        shape.GroupItems = new DrawingLayoutFakeCollection<DrawingLayoutFakeShape>(
            new DrawingLayoutFakeShape(13, "Picture 1", 5, 10, sensitiveReads)
            {
                Width = 20,
                Height = 15,
            }
        );
        return shape;
    }

    public static DrawingLayoutFakeShape SmartArtRoot(
        DrawingLayoutFakeSensitiveReads sensitiveReads
    ) =>
        new(24, "SmartArt 1", 100, 250, sensitiveReads)
        {
            Width = 240,
            Height = 120,
            SmartArt = new DrawingLayoutFakeSmartArt(sensitiveReads),
        };

    public int Type { get; }
    public int ID => Type == 24 ? 202 : 101;
    public string Name
    {
        get
        {
            _sensitiveReads.Count++;
            return _name;
        }
    }
    public string Title
    {
        get
        {
            _sensitiveReads.Count++;
            return $"Title {_name}";
        }
    }
    public string AlternativeText
    {
        get
        {
            _sensitiveReads.Count++;
            return $"Alt {_name}";
        }
    }
    public DrawingLayoutFakeRange Anchor { get; } = new(5, 6, 2, 1);
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 60;
    public double Left { get; }
    public double Top { get; }
    public int RelativeHorizontalPosition => 1;
    public int RelativeVerticalPosition => 1;
    public double LeftRelative => -999999;
    public double TopRelative => -999999;
    public double Rotation => 15;
    public int ZOrderPosition => 2;
    public int LockAnchor => -1;
    public int LayoutInCell => -1;
    public int Visible => -1;
    public int HorizontalFlip => 0;
    public int VerticalFlip => 0;
    public bool HasSmartArt => Type == 24;
    public bool HasChart => false;
    public DrawingLayoutFakeWrapFormat WrapFormat { get; } = new();
    public DrawingLayoutFakeCollection<DrawingLayoutFakeShape> GroupItems { get; private set; } = new();
    public DrawingLayoutFakeSmartArt? SmartArt { get; private set; }
}

public sealed class DrawingLayoutFakeInlineShape
{
    private readonly DrawingLayoutFakeSensitiveReads _sensitiveReads;

    public DrawingLayoutFakeInlineShape(DrawingLayoutFakeSensitiveReads sensitiveReads)
    {
        _sensitiveReads = sensitiveReads;
    }

    public int Type => 3;
    public string Title
    {
        get
        {
            _sensitiveReads.Count++;
            return "Inline picture";
        }
    }
    public string AlternativeText
    {
        get
        {
            _sensitiveReads.Count++;
            return "Inline alternative";
        }
    }
    public DrawingLayoutFakeRange Range { get; } = new(20, 21, 3, 1, 50, 80);
    public double Width => 90;
    public double Height => 45;
    public bool HasSmartArt => false;
    public bool HasChart => false;
}

public sealed class DrawingLayoutFakeSmartArt
{
    public DrawingLayoutFakeSmartArt(DrawingLayoutFakeSensitiveReads sensitiveReads)
    {
        AllNodes = new DrawingLayoutFakeCollection<DrawingLayoutFakeSmartArtNode>(
            new DrawingLayoutFakeSmartArtNode(sensitiveReads)
        );
    }

    public DrawingLayoutFakeCollection<DrawingLayoutFakeSmartArtNode> AllNodes { get; }
    public DrawingLayoutFakeIdentifier Layout { get; } = new("layout-1");
    public DrawingLayoutFakeIdentifier QuickStyle { get; } = new("style-1");
    public DrawingLayoutFakeIdentifier Color { get; } = new("color-1");
}

public sealed class DrawingLayoutFakeSmartArtNode
{
    public DrawingLayoutFakeSmartArtNode(DrawingLayoutFakeSensitiveReads sensitiveReads)
    {
        TextFrame2 = new DrawingLayoutFakeTextFrame("Etap A", sensitiveReads);
    }

    public int Level => 1;
    public int Hidden => 0;
    public int Type => 1;
    public DrawingLayoutFakeCountCollection Nodes { get; } = new(0);
    public DrawingLayoutFakeTextFrame TextFrame2 { get; }
    public DrawingLayoutFakeCollection<DrawingLayoutFakeSmartArtRenderedShape> Shapes { get; } = new(
        new DrawingLayoutFakeSmartArtRenderedShape()
    );
}

public sealed class DrawingLayoutFakeSmartArtRenderedShape
{
    public int Type => 1;
    public double Left => 4;
    public double Top => 8;
    public double Width => 120;
    public double Height => 40;
    public double Rotation => 0;
}

public sealed class DrawingLayoutFakeTextFrame
{
    public DrawingLayoutFakeTextFrame(
        string text,
        DrawingLayoutFakeSensitiveReads sensitiveReads
    )
    {
        TextRange = new DrawingLayoutFakeTextRange(text, sensitiveReads);
    }

    public DrawingLayoutFakeTextRange TextRange { get; }
}

public sealed class DrawingLayoutFakeTextRange
{
    private string _text;
    private readonly DrawingLayoutFakeSensitiveReads _sensitiveReads;

    public DrawingLayoutFakeTextRange(
        string text,
        DrawingLayoutFakeSensitiveReads sensitiveReads
    )
    {
        _text = text;
        _sensitiveReads = sensitiveReads;
    }

    public string Text
    {
        get
        {
            _sensitiveReads.Count++;
            return _text;
        }
        set
        {
            _text = WriteTransform is null ? value : WriteTransform(value);
        }
    }

    public string TextValue => _text;
    public Func<string, string>? WriteTransform { get; set; }

    public void ForceText(string value)
    {
        _text = value;
    }
}

public sealed class DrawingLayoutFakeSensitiveReads
{
    public int Count { get; set; }
}

public sealed class DrawingLayoutFakeIdentifier
{
    public DrawingLayoutFakeIdentifier(string id)
    {
        Id = id;
    }

    public string Id { get; }
}
