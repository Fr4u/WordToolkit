using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LiveWordFeatureBehaviorProbeTests
{
    [Fact]
    public void PublishesAnExplicitNonReadOnlyScratchDocumentContract()
    {
        var tool = ToolCatalog
            .LoadNativeWordTools()
            .InspectAction("probe_live_word_feature_behaviors")["tool"]!
            .AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.Equal(
            "create_test_and_discard_invisible_unsaved_scratch_documents_without_mutating_connected_document_content",
            tool["permissions"]!["microsoft_word"]!.GetValue<string>()
        );
        Assert.True(
            tool["inputSchema"]!["properties"]!["confirm_scratch_documents"]!["const"]!
                .GetValue<bool>()
        );
        Assert.False(tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(tool["annotations"]!["destructiveHint"]!.GetValue<bool>());
        Assert.True(tool["annotations"]!["idempotentHint"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExecutesFourBehaviorsInSeparateScratchDocumentsAndRestoresWord()
    {
        await using var host = new FeatureBehaviorFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var originalDocument = host.Application.TargetDocument;
        var originalWindow = host.Application.ActiveWindow;

        var result = await ProbeAsync(service, documentId);
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.probe_live_word_feature_behaviors/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(0, data.GetProperty("live_version").GetInt64());
        Assert.Equal(4, data.GetProperty("summary").GetProperty("passed").GetInt32());
        Assert.Equal(0, data.GetProperty("summary").GetProperty("failed").GetInt32());
        Assert.All(
            data.GetProperty("probes").EnumerateObject(),
            item =>
            {
                Assert.Equal("passed", item.Value.GetProperty("status").GetString());
                Assert.True(item.Value.GetProperty("behavior_verified").GetBoolean());
                Assert.True(item.Value.GetProperty("scratch_document_closed").GetBoolean());
            }
        );
        Assert.Equal(
            4,
            data.GetProperty("isolation").GetProperty("scratch_documents_created").GetInt32()
        );
        Assert.Equal(
            4,
            data.GetProperty("isolation").GetProperty("scratch_documents_closed").GetInt32()
        );
        Assert.Same(originalDocument, host.Application.ActiveDocument);
        Assert.Same(originalWindow, host.Application.ActiveWindow);
        Assert.Equal(1, host.Application.Documents.Count);
        Assert.Equal(4, host.Application.Documents.CreatedCount);
        Assert.Equal(4, host.Application.Documents.ClosedCount);
        Assert.Equal(0, originalDocument.MutationCount);
    }

    [Fact]
    public async Task ReportsFeatureFailureAndUnavailableLayoutWithoutLeakingOrStoppingCleanup()
    {
        await using var host = new FeatureBehaviorFakeHost();
        host.Application.FailOMathBuild = true;
        host.Application.SmartArtLayoutsAvailable = false;
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var result = await ProbeAsync(service, documentId);
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        var data = json.RootElement;

        Assert.Equal(
            "failed",
            data.GetProperty("probes").GetProperty("native_omath").GetProperty("status")
                .GetString()
        );
        Assert.Equal(
            "NATIVE_OMATH_BEHAVIOR_FAILED",
            data.GetProperty("probes").GetProperty("native_omath")
                .GetProperty("issue_code").GetString()
        );
        Assert.Equal(
            "unavailable",
            data.GetProperty("probes").GetProperty("smartart").GetProperty("status")
                .GetString()
        );
        Assert.Equal(
            "SMARTART_LAYOUT_UNAVAILABLE",
            data.GetProperty("probes").GetProperty("smartart")
                .GetProperty("issue_code").GetString()
        );
        Assert.Equal(2, data.GetProperty("summary").GetProperty("passed").GetInt32());
        Assert.Equal(1, data.GetProperty("summary").GetProperty("failed").GetInt32());
        Assert.Equal(1, data.GetProperty("summary").GetProperty("unavailable").GetInt32());
        Assert.DoesNotContain(
            "InvalidOperationException",
            json.RootElement.GetRawText(),
            StringComparison.Ordinal
        );
        Assert.Equal(1, host.Application.Documents.Count);
        Assert.Equal(4, host.Application.Documents.ClosedCount);
        Assert.Equal(0, host.Application.TargetDocument.MutationCount);
    }

    [Fact]
    public async Task CleanupFailureReturnsDedicatedErrorAndQuarantinesTheHandle()
    {
        await using var host = new FeatureBehaviorFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        host.Application.FailNextScratchClose = true;

        var exception = await Assert.ThrowsAsync<NativeToolException>(
            () => ProbeAsync(service, documentId)
        );

        Assert.Equal("TEMPORARY_DOCUMENT_CLEANUP_FAILED", exception.ErrorCode);
        Assert.Equal(0, host.Application.TargetDocument.MutationCount);
        using var inspectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var quarantine = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "inspect_live_word_document",
                    inspectArguments.RootElement,
                    CancellationToken.None
                )
        );
        Assert.Equal("LIVE_DOCUMENT_QUARANTINED", quarantine.ErrorCode);
        Assert.Contains(
            "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
            JsonSerializer.Serialize(quarantine.Details, JsonDefaults.Compact),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task UndoRecordClosureFailureIsAStateCleanupFailureNotAFeatureResult()
    {
        await using var host = new FeatureBehaviorFakeHost();
        host.Application.FailUndoRecordEnd = true;
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var exception = await Assert.ThrowsAsync<NativeToolException>(
            () => ProbeAsync(service, documentId)
        );

        Assert.Equal("TEMPORARY_DOCUMENT_CLEANUP_FAILED", exception.ErrorCode);
        Assert.Equal(4, host.Application.Documents.CreatedCount);
        Assert.Equal(4, host.Application.Documents.ClosedCount);
        Assert.Equal(1, host.Application.Documents.Count);
    }

    [Fact]
    public async Task RejectsMissingConfirmationAndUnknownArgumentsBeforeWord()
    {
        await using var host = new FeatureBehaviorFakeHost();
        var service = new WordLiveService(host);
        using var missingConfirmation = JsonDocument.Parse(
            """{"live_document_id":"unused","confirm_scratch_documents":false}"""
        );
        using var unknown = JsonDocument.Parse(
            """{"live_document_id":"unused","confirm_scratch_documents":true,"save_path":"secret.docx"}"""
        );

        Assert.Equal(
            "AUTH_FORBIDDEN",
            (await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "probe_live_word_feature_behaviors",
                    missingConfirmation.RootElement,
                    CancellationToken.None
                )
            )).ErrorCode
        );
        Assert.Equal(
            "INVALID_INPUT",
            (await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "probe_live_word_feature_behaviors",
                    unknown.RootElement,
                    CancellationToken.None
                )
            )).ErrorCode
        );
        Assert.Equal(0, host.CallCount);
    }

    private static async Task<object> ProbeAsync(
        WordLiveService service,
        string documentId
    )
    {
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    confirm_scratch_documents = true,
                }
            )
        );
        return await service.CallAsync(
            "probe_live_word_feature_behaviors",
            arguments.RootElement,
            CancellationToken.None
        );
    }

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

internal sealed class FeatureBehaviorFakeHost : IWordComHost
{
    public FeatureBehaviorFakeApplication Application { get; } = new();
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

public sealed class FeatureBehaviorFakeApplication
{
    private readonly FeatureBehaviorFakeUndoRecord _undoRecord;
    private bool _smartArtLayoutsAvailable = true;

    public FeatureBehaviorFakeApplication()
    {
        TargetDocument = new FeatureBehaviorFakeTargetDocument(this);
        Documents = new FeatureBehaviorFakeDocuments(this, TargetDocument);
        ActiveDocument = TargetDocument;
        ActiveWindow = TargetDocument.Window;
        _undoRecord = new FeatureBehaviorFakeUndoRecord(this);
    }

    public FeatureBehaviorFakeTargetDocument TargetDocument { get; }
    public FeatureBehaviorFakeDocuments Documents { get; }
    public object ActiveDocument { get; private set; }
    public FeatureBehaviorFakeWindow ActiveWindow { get; private set; }
    public FeatureBehaviorFakeUndoRecord UndoRecord => _undoRecord;
    public FeatureBehaviorFakeSmartArtLayouts SmartArtLayouts =>
        new(_smartArtLayoutsAvailable ? 1 : 0);
    public bool FailOMathBuild { get; set; }
    public bool FailNextScratchClose { get; set; }
    public bool FailUndoRecordEnd { get; set; }
    public bool SmartArtLayoutsAvailable
    {
        get => _smartArtLayoutsAvailable;
        set => _smartArtLayoutsAvailable = value;
    }

    public void SetActive(object document, FeatureBehaviorFakeWindow window)
    {
        ActiveDocument = document;
        ActiveWindow = window;
    }
}

public sealed class FeatureBehaviorFakeDocuments
{
    private readonly FeatureBehaviorFakeApplication _application;
    private readonly List<object> _documents;

    public FeatureBehaviorFakeDocuments(
        FeatureBehaviorFakeApplication application,
        FeatureBehaviorFakeTargetDocument target
    )
    {
        _application = application;
        _documents = [target];
    }

    public int Count => _documents.Count;
    public int CreatedCount { get; private set; }
    public int ClosedCount { get; private set; }

    public object Item(int index) =>
        index >= 1 && index <= _documents.Count
            ? _documents[index - 1]
            : throw new IndexOutOfRangeException();

    public FeatureBehaviorFakeScratchDocument Add(bool Visible)
    {
        Assert.False(Visible);
        CreatedCount++;
        var scratch = new FeatureBehaviorFakeScratchDocument(
            _application,
            this,
            CreatedCount
        );
        _documents.Add(scratch);
        scratch.Activate();
        return scratch;
    }

    public void Close(FeatureBehaviorFakeScratchDocument scratch)
    {
        if (_application.FailNextScratchClose)
        {
            _application.FailNextScratchClose = false;
            throw new InvalidOperationException("Synthetic scratch close failure");
        }
        if (_documents.Remove(scratch))
        {
            ClosedCount++;
        }
    }
}

public sealed class FeatureBehaviorFakeTargetDocument
{
    private readonly FeatureBehaviorFakeApplication _application;

    public FeatureBehaviorFakeTargetDocument(FeatureBehaviorFakeApplication application)
    {
        _application = application;
        Window = new FeatureBehaviorFakeWindow(application, this, 9101);
    }

    public string Name => "Feature-behavior.docx";
    public string FullName => @"C:\Fixtures\Feature-behavior.docx";
    public string Path => @"C:\Fixtures";
    public bool Saved => true;
    public bool ReadOnly => false;
    public int CompatibilityMode => 15;
    public int ProtectionType => -1;
    public FeatureBehaviorFakeCountCollection Paragraphs { get; } = new(1);
    public FeatureBehaviorFakeCountCollection OMaths { get; } = new(0);
    public FeatureBehaviorFakeCountCollection Tables { get; } = new(0);
    public FeatureBehaviorFakeCountCollection Fields { get; } = new(0);
    public FeatureBehaviorFakeCountCollection Bookmarks { get; } = new(0);
    public FeatureBehaviorFakeCountCollection InlineShapes { get; } = new(0);
    public FeatureBehaviorFakeCountCollection Shapes { get; } = new(0);
    public FeatureBehaviorFakeCountCollection Comments { get; } = new(0);
    public FeatureBehaviorFakeCountCollection Footnotes { get; } = new(0);
    public FeatureBehaviorFakeCountCollection Endnotes { get; } = new(0);
    public FeatureBehaviorFakeCountCollection Sections { get; } = new(1);
    public FeatureBehaviorFakeWindow Window { get; }
    public int MutationCount { get; private set; }

    public void Activate() => _application.SetActive(this, Window);
}

public sealed class FeatureBehaviorFakeScratchDocument
{
    private readonly FeatureBehaviorFakeApplication _application;
    private readonly FeatureBehaviorFakeDocuments _documents;
    private string _text = "\r";
    private string? _undoText;

    public FeatureBehaviorFakeScratchDocument(
        FeatureBehaviorFakeApplication application,
        FeatureBehaviorFakeDocuments documents,
        int index
    )
    {
        _application = application;
        _documents = documents;
        Name = $"Document{index}";
        FullName = Name;
        Window = new FeatureBehaviorFakeWindow(application, this, 9200 + index);
        OMaths = new FeatureBehaviorFakeOMaths(this, application);
        ContentControls = new FeatureBehaviorFakeContentControls();
        Shapes = new FeatureBehaviorFakeShapes();
    }

    public string Name { get; }
    public string FullName { get; }
    public FeatureBehaviorFakeWindow Window { get; }
    public FeatureBehaviorFakeOMaths OMaths { get; }
    public FeatureBehaviorFakeContentControls ContentControls { get; }
    public FeatureBehaviorFakeShapes Shapes { get; }
    public FeatureBehaviorFakeRange Content => new(this, 0, _text.Length, wholeDocument: true);

    public FeatureBehaviorFakeRange Range(int start, int end) =>
        new(this, start, end, wholeDocument: false);

    public void Activate() => _application.SetActive(this, Window);

    public void Close(int saveChanges)
    {
        Assert.Equal(0, saveChanges);
        _documents.Close(this);
    }

    public void BeginUndoSnapshot() => _undoText = _text;

    public bool Undo(int count)
    {
        if (count != 1 || _undoText is null)
        {
            return false;
        }
        _text = _undoText;
        _undoText = null;
        return true;
    }

    public string ReadText() => _text;

    public void WriteRange(int start, int end, string value)
    {
        if (start < 0 || end < start || end > _text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        _text = _text[..start] + value + _text[end..];
    }
}

public sealed class FeatureBehaviorFakeWindow
{
    private readonly FeatureBehaviorFakeApplication _application;
    private readonly object _document;

    public FeatureBehaviorFakeWindow(
        FeatureBehaviorFakeApplication application,
        object document,
        int hwnd
    )
    {
        _application = application;
        _document = document;
        Hwnd = hwnd;
    }

    public int Hwnd { get; }

    public void Activate() => _application.SetActive(_document, this);
}

public sealed class FeatureBehaviorFakeRange
{
    private readonly FeatureBehaviorFakeScratchDocument _document;
    private readonly bool _wholeDocument;
    private string? _detachedText;

    public FeatureBehaviorFakeRange(
        FeatureBehaviorFakeScratchDocument document,
        int start,
        int end,
        bool wholeDocument
    )
    {
        _document = document;
        Start = start;
        End = end;
        _wholeDocument = wholeDocument;
        OMaths = new FeatureBehaviorFakeRangeOMaths(document);
    }

    public int Start { get; }
    public int End { get; }
    public FeatureBehaviorFakeRangeOMaths OMaths { get; }
    public string Text
    {
        get => _wholeDocument ? _document.ReadText() : _detachedText ?? "";
        set
        {
            _detachedText = value;
            _document.WriteRange(Start, End, value);
        }
    }
}

public sealed class FeatureBehaviorFakeOMaths
{
    private readonly FeatureBehaviorFakeScratchDocument _document;
    private readonly FeatureBehaviorFakeApplication _application;
    private int _count;

    public FeatureBehaviorFakeOMaths(
        FeatureBehaviorFakeScratchDocument document,
        FeatureBehaviorFakeApplication application
    )
    {
        _document = document;
        _application = application;
    }

    public int Count => _count;

    public FeatureBehaviorFakeRange Add(FeatureBehaviorFakeRange range)
    {
        _count++;
        range.OMaths.Attach(new FeatureBehaviorFakeOMath(_application));
        return range;
    }
}

public sealed class FeatureBehaviorFakeRangeOMaths
{
    private readonly FeatureBehaviorFakeScratchDocument _document;
    private FeatureBehaviorFakeOMath? _equation;

    public FeatureBehaviorFakeRangeOMaths(FeatureBehaviorFakeScratchDocument document)
    {
        _document = document;
    }

    public int Count => _equation is null ? 0 : 1;

    public void Attach(FeatureBehaviorFakeOMath equation) => _equation = equation;

    public FeatureBehaviorFakeOMath Item(int index) =>
        index == 1 && _equation is not null
            ? _equation
            : throw new IndexOutOfRangeException();
}

public sealed class FeatureBehaviorFakeOMath
{
    private readonly FeatureBehaviorFakeApplication _application;

    public FeatureBehaviorFakeOMath(FeatureBehaviorFakeApplication application)
    {
        _application = application;
    }

    public void BuildUp()
    {
        if (_application.FailOMathBuild)
        {
            throw new InvalidOperationException("Synthetic OMath BuildUp failure");
        }
    }
}

public sealed class FeatureBehaviorFakeContentControls
{
    private int _count;
    public int Count => _count;

    public object Add(int type, FeatureBehaviorFakeRange range)
    {
        Assert.Equal(0, type);
        _ = range;
        _count++;
        return new object();
    }
}

public sealed class FeatureBehaviorFakeShapes
{
    private int _count;
    public int Count => _count;

    public FeatureBehaviorFakeShape AddSmartArt(
        object layout,
        object left,
        object top,
        object width,
        object height,
        FeatureBehaviorFakeRange anchor
    )
    {
        _ = layout;
        _ = left;
        _ = top;
        _ = width;
        _ = height;
        _ = anchor;
        _count++;
        return new FeatureBehaviorFakeShape();
    }
}

public sealed class FeatureBehaviorFakeShape
{
    public int HasSmartArt => -1;
}

public sealed class FeatureBehaviorFakeSmartArtLayouts
{
    private readonly int _count;

    public FeatureBehaviorFakeSmartArtLayouts(int count) => _count = count;

    public int Count => _count;

    public object Item(int index) =>
        index >= 1 && index <= _count ? new object() : throw new IndexOutOfRangeException();
}

public sealed class FeatureBehaviorFakeUndoRecord
{
    private readonly FeatureBehaviorFakeApplication _application;

    public FeatureBehaviorFakeUndoRecord(FeatureBehaviorFakeApplication application)
    {
        _application = application;
    }

    public void StartCustomRecord(string name)
    {
        Assert.Equal("WordToolkit feature probe", name);
        ((dynamic)_application.ActiveDocument).BeginUndoSnapshot();
    }

    public void EndCustomRecord()
    {
        if (_application.FailUndoRecordEnd)
        {
            throw new InvalidOperationException("Synthetic UndoRecord closure failure");
        }
    }
}

public sealed class FeatureBehaviorFakeCountCollection
{
    public FeatureBehaviorFakeCountCollection(int count) => Count = count;
    public int Count { get; }
}
