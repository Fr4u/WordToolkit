using System.Reflection;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LiveOperationRollbackTests
{
    [Fact]
    public async Task ExactRollbackPreservesOriginalErrorAndLiveHandle()
    {
        await using var host = new RollbackFakeHost(RollbackBehavior.RestoreExact);
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var error = await ApplyFailingBatchAsync(service, documentId);

        Assert.True(
            error.ErrorCode == "EQUATION_INVALID",
            JsonSerializer.Serialize(error.Details, JsonDefaults.Compact)
        );
        Assert.Equal("\r", host.Application.ActiveDocument.RawText);
        Assert.Equal(1, host.Application.ActiveDocument.Paragraphs.Count);
        Assert.Equal(0, host.Application.ActiveDocument.OMaths.Count);
        Assert.Equal(1, host.Application.ActiveDocument.UndoCount);

        using var inspectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var result = await service.CallAsync(
            "inspect_live_word_document",
            inspectArguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        Assert.Equal(0, resultJson.RootElement.GetProperty("live_version").GetInt64());
    }

    [Fact]
    public async Task IndependentBaselineRecoveryRepairsTargetWhenUndoReturnsFalse()
    {
        await using var host = new RollbackFakeHost(
            RollbackBehavior.ReturnFalse,
            failIndependentRestore: false
        );
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        host.Application.ActiveDocument.Saved = false;

        var error = await ApplyFailingBatchAsync(service, documentId);

        Assert.True(
            error.ErrorCode == "EQUATION_INVALID",
            JsonSerializer.Serialize(error.Details, JsonDefaults.Compact)
        );
        Assert.Equal("\r", host.Application.ActiveDocument.RawText);
        Assert.Equal(1, host.Application.ActiveDocument.Paragraphs.Count);
        Assert.Equal(0, host.Application.ActiveDocument.OMaths.Count);
        Assert.Equal(1, host.Application.ActiveDocument.UndoCount);
        Assert.Equal(2, host.Application.ActiveDocument.FormattedTextAssignments);
        Assert.Equal(2, host.Application.Documents.OpenCount);
        Assert.False(host.Application.ActiveDocument.Saved);

        using var inspectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var result = await service.CallAsync(
            "inspect_live_word_document",
            inspectArguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        Assert.Equal(0, resultJson.RootElement.GetProperty("live_version").GetInt64());
    }

    [Fact]
    public void SemanticRollbackHashIgnoresOnlyWordRsidSessionMetadata()
    {
        const string before =
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"urn:w14\"><w:body><w:p w:rsidR=\"00000001\" w14:paraId=\"A\"><w:r><w:t>tekst</w:t></w:r></w:p></w:body><w:rsids><w:rsidRoot w:val=\"00000001\"/><w:rsid w:val=\"00000002\"/></w:rsids></w:document>";
        const string onlySessionIdsChanged =
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"urn:w14\"><w:body><w:p w:rsidR=\"FFFFFFFF\" w14:paraId=\"A\"><w:r><w:t>tekst</w:t></w:r></w:p></w:body><w:rsids><w:rsidRoot w:val=\"AAAAAAAA\"/><w:rsid w:val=\"BBBBBBBB\"/></w:rsids></w:document>";
        const string stableIdentityChanged =
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"urn:w14\"><w:body><w:p w:rsidR=\"FFFFFFFF\" w14:paraId=\"B\"><w:r><w:t>tekst</w:t></w:r></w:p></w:body><w:rsids><w:rsidRoot w:val=\"AAAAAAAA\"/></w:rsids></w:document>";
        const string contentChanged =
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"urn:w14\"><w:body><w:p w:rsidR=\"FFFFFFFF\" w14:paraId=\"A\"><w:r><w:t>skażenie</w:t></w:r></w:p></w:body><w:rsids/></w:document>";

        var baseline = WordLiveService.RollbackSemanticSha256(before);

        Assert.Equal(
            baseline,
            WordLiveService.RollbackSemanticSha256(onlySessionIdsChanged)
        );
        Assert.NotEqual(
            baseline,
            WordLiveService.RollbackSemanticSha256(stableIdentityChanged)
        );
        Assert.NotEqual(baseline, WordLiveService.RollbackSemanticSha256(contentChanged));
    }

    [Fact]
    public void SemanticRollbackHashIgnoresSystemNoteParaIdsOnly()
    {
        const string before =
            "<w:pkg xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" "
            + "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w:part>"
            + "<w:footnotes><w:footnote w:id=\"-1\"><w:p w14:paraId=\"A\"><w:r><w:t>sep</w:t>"
            + "</w:r></w:p></w:footnote><w:footnote w:id=\"1\"><w:p w14:paraId=\"B\"><w:r>"
            + "<w:t>regular</w:t></w:r></w:p></w:footnote></w:footnotes><w:endnotes>"
            + "<w:endnote w:id=\"0\"><w:p w14:paraId=\"C\"><w:r><w:t>endsep</w:t></w:r>"
            + "</w:p></w:endnote></w:endnotes><w:document><w:body><w:p w14:paraId=\"D\"><w:r>"
            + "<w:t>main</w:t></w:r></w:p></w:body></w:document></w:part></w:pkg>";
        var baseline = WordLiveService.RollbackSemanticSha256(before);
        var systemChanged = before.Replace("paraId=\"A\"", "paraId=\"AA\"")
            .Replace("paraId=\"C\"", "paraId=\"CC\"");

        Assert.Equal(baseline, WordLiveService.RollbackSemanticSha256(systemChanged));
        Assert.NotEqual(baseline, WordLiveService.RollbackSemanticSha256(
            before.Replace("paraId=\"B\"", "paraId=\"BB\"")));
        Assert.NotEqual(baseline, WordLiveService.RollbackSemanticSha256(
            before.Replace("paraId=\"D\"", "paraId=\"DD\"")));
        Assert.NotEqual(baseline, WordLiveService.RollbackSemanticSha256(
            before.Replace("endsep", "changed")));
    }

    [Theory]
    [InlineData(RollbackBehavior.LeaveContaminated, true, false)]
    [InlineData(RollbackBehavior.RestoreTextOnly, true, false)]
    [InlineData(RollbackBehavior.RestoreVisibleStateOnly, true, false)]
    [InlineData(RollbackBehavior.ReturnFalse, false, false)]
    [InlineData(RollbackBehavior.Throw, null, true)]
    public async Task UnprovenRollbackReturnsDedicatedErrorAndQuarantinesHandle(
        RollbackBehavior behavior,
        bool? expectedUndoResult,
        bool expectedUndoFailure
    )
    {
        await using var host = new RollbackFakeHost(behavior);
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var error = await ApplyFailingBatchAsync(service, documentId);

        Assert.Equal("ROLLBACK_FAILED", error.ErrorCode);
        using var detailsJson = JsonDocument.Parse(
            JsonSerializer.Serialize(error.Details, JsonDefaults.Compact)
        );
        var details = detailsJson.RootElement;
        Assert.Equal(
            "EQUATION_INVALID",
            details.GetProperty("original_error_code").GetString()
        );
        Assert.True(details.GetProperty("handle_invalidated").GetBoolean());
        Assert.True(details.GetProperty("document_quarantined").GetBoolean());
        Assert.True(details.GetProperty("requires_explicit_disconnect").GetBoolean());
        Assert.Equal(expectedUndoFailure, details.GetProperty("undo_failed").GetBoolean());
        if (expectedUndoResult is null)
        {
            Assert.False(details.TryGetProperty("undo_returned", out _));
        }
        else
        {
            Assert.Equal(
                expectedUndoResult.Value,
                details.GetProperty("undo_returned").GetBoolean()
            );
        }
        Assert.False(details.GetProperty("raw_document_content_returned").GetBoolean());

        using var inspectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var inspectError = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "inspect_live_word_document",
                    inspectArguments.RootElement,
                    CancellationToken.None
                )
        );
        Assert.Equal("LIVE_DOCUMENT_QUARANTINED", inspectError.ErrorCode);

        using var connectArguments = JsonDocument.Parse("""{"use_active":true}""");
        var reconnectError = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "connect_live_word_document",
                    connectArguments.RootElement,
                    CancellationToken.None
                )
        );
        Assert.Equal("LIVE_DOCUMENT_QUARANTINED", reconnectError.ErrorCode);

        using var disconnectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var disconnectResult = await service.CallAsync(
            "disconnect_live_word_document",
            disconnectArguments.RootElement,
            CancellationToken.None
        );
        using var disconnectJson = JsonDocument.Parse(
            JsonSerializer.Serialize(disconnectResult, JsonDefaults.Compact)
        );
        Assert.True(
            disconnectJson.RootElement.GetProperty("quarantine_cleared").GetBoolean()
        );

        var newDocumentId = await ConnectAsync(service);
        Assert.NotEqual(documentId, newDocumentId);
    }

    [Fact]
    public async Task FailedUndoRecordClosureDoesNotUndoAnUnrelatedHistoryEntry()
    {
        await using var host = new RollbackFakeHost(RollbackBehavior.EndRecordThrows);
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var error = await ApplyFailingBatchAsync(service, documentId);

        Assert.Equal("ROLLBACK_FAILED", error.ErrorCode);
        using var detailsJson = JsonDocument.Parse(
            JsonSerializer.Serialize(error.Details, JsonDefaults.Compact)
        );
        var details = detailsJson.RootElement;
        Assert.True(details.GetProperty("undo_record_end_failed").GetBoolean());
        Assert.False(details.GetProperty("undo_attempted").GetBoolean());
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
    }

    [Fact]
    public async Task EquationFailureInIsolatedStageNeverTouchesTargetDocument()
    {
        await using var host = new RollbackFakeHost(
            RollbackBehavior.RestoreExact,
            failStagingEquation: true
        );
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var error = await ApplyFailingBatchAsync(service, documentId);

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
        Assert.Equal("\r", host.Application.ActiveDocument.RawText);
        Assert.Equal(1, host.Application.ActiveDocument.Paragraphs.Count);
        Assert.Equal(0, host.Application.ActiveDocument.OMaths.Count);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
        Assert.Equal(0, host.Application.ActiveDocument.FormattedTextAssignments);

        using var inspectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var result = await service.CallAsync(
            "inspect_live_word_document",
            inspectArguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        Assert.Equal(0, resultJson.RootElement.GetProperty("live_version").GetInt64());
    }

    [Fact]
    public async Task SuccessfulMixedBatchPublishesOnceWithoutBuildingMathInTarget()
    {
        await using var host = new RollbackFakeHost(
            RollbackBehavior.RestoreExact,
            failTargetPublication: false
        );
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = 0,
                    operations = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = "pierwszy akapit",
                            as_new_paragraph = true,
                        },
                        new
                        {
                            type = "equation",
                            value = "x",
                            input_format = "unicodemath",
                            display = true,
                        },
                        new
                        {
                            type = "text",
                            text = "koniec",
                            as_new_paragraph = true,
                        },
                    },
                }
            )
        );

        var result = await service.CallAsync(
            "apply_live_word_operations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );

        Assert.Equal(3, resultJson.RootElement.GetProperty("live_version").GetInt64());
        Assert.Equal(1, host.Application.ActiveDocument.FormattedTextAssignments);
        Assert.Equal(0, host.Application.ActiveDocument.OMaths.AddCalls);
        Assert.Equal(1, host.Application.ActiveDocument.OMaths.Count);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
        Assert.NotNull(host.Application.Documents.LastStagingDocument);
        Assert.True(host.Application.Documents.LastStagingDocument!.Closed);
    }

    [Fact]
    public async Task PublicationFailureBeforeMutationPreservesOriginalErrorWithoutUndo()
    {
        await using var host = new RollbackFakeHost(
            RollbackBehavior.RestoreExact,
            failTargetPublication: false,
            failPublicationBeforeMutation: true
        );
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var error = await ApplyFailingBatchAsync(service, documentId);

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
        Assert.Equal("\r", host.Application.ActiveDocument.RawText);
        Assert.Equal(0, host.Application.ActiveDocument.OMaths.Count);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
        Assert.Equal(1, host.Application.ActiveDocument.FormattedTextAssignments);
        using var inspectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var result = await service.CallAsync(
            "inspect_live_word_document",
            inspectArguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        Assert.Equal(0, resultJson.RootElement.GetProperty("live_version").GetInt64());
    }

    [Fact]
    public async Task HiddenTargetDriftDuringStagingQuarantinesWithoutUndoingUserHistory()
    {
        await using var host = new RollbackFakeHost(
            RollbackBehavior.RestoreExact,
            driftTargetDuringStaging: true
        );
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var error = await ApplyFailingBatchAsync(service, documentId);

        Assert.Equal("STAGING_TARGET_DRIFT", error.ErrorCode);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
        Assert.Equal(0, host.Application.ActiveDocument.FormattedTextAssignments);
        using var inspectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var inspectError = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "inspect_live_word_document",
                    inspectArguments.RootElement,
                    CancellationToken.None
                )
        );
        Assert.Equal("LIVE_DOCUMENT_QUARANTINED", inspectError.ErrorCode);
    }

    [Fact]
    public async Task FailedStagingOpenDeletesFlatOpcArtifactAndLeavesTargetUntouched()
    {
        await using var host = new RollbackFakeHost(
            RollbackBehavior.RestoreExact,
            failStagingOpen: true
        );
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);

        var error = await ApplyFailingBatchAsync(service, documentId);

        Assert.Equal("STAGING_FAILED", error.ErrorCode);
        Assert.NotNull(host.Application.Documents.LastStagingPath);
        Assert.False(File.Exists(host.Application.Documents.LastStagingPath));
        Assert.Equal("\r", host.Application.ActiveDocument.RawText);
        Assert.Equal(0, host.Application.ActiveDocument.OMaths.Count);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
        Assert.Equal(0, host.Application.ActiveDocument.FormattedTextAssignments);
    }

    [Fact]
    public async Task SupplementalStateMismatchFailsClosedAfterExactDocumentUndo()
    {
        await using var host = new RollbackFakeHost(RollbackBehavior.RestoreExact);
        var service = new WordLiveService(host);
        var document = host.Application.ActiveDocument;
        var record = new LiveDocumentRecord
        {
            Id = "rollback-supplemental",
            Name = document.Name,
            FullName = document.FullName,
            WindowHwnd = host.Application.ActiveWindow.Hwnd,
            Version = 0,
        };
        var baseline = WordLiveService.CaptureLiveRollbackSnapshot(document, 0);
        var undoStarted = true;
        host.Application.UndoRecord.StartCustomRecord("WordToolkit: supplemental rollback test");
        document.Content.Text = "mutated\r";

        var error = Assert.Throws<NativeToolException>(
            () =>
                service.RollbackPreparedOperationsOrThrow(
                    document,
                    host.Application.UndoRecord,
                    ref undoStarted,
                    mutationAttempted: true,
                    baseline,
                    record,
                    new NativeToolException("INVALID_INPUT", "synthetic failure"),
                    supplementalBaseline: "before",
                    supplementalStateReader: () => "after",
                    supplementalDifferenceName: "hidden_state_sha256"
                )
        );

        Assert.Equal("ROLLBACK_FAILED", error.ErrorCode);
        using var detailsJson = JsonDocument.Parse(
            JsonSerializer.Serialize(error.Details, JsonDefaults.Compact)
        );
        Assert.Contains(
            "hidden_state_sha256",
            detailsJson.RootElement
                .GetProperty("differences")
                .EnumerateArray()
                .Select(item => item.GetString())
        );
        Assert.Equal("\r", document.RawText);
        Assert.Equal(1, document.UndoCount);
    }

    [Fact]
    public void LegacySilentRollbackEntryPointDoesNotExist()
    {
        var method = typeof(WordLiveService).GetMethod(
            "Rollback",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.Null(method);
    }

    private static async Task<string> ConnectAsync(WordLiveService service)
    {
        using var arguments = JsonDocument.Parse("""{"use_active":true}""");
        var result = await service.CallAsync(
            "connect_live_word_document",
            arguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        return resultJson.RootElement.GetProperty("live_document_id").GetString()!;
    }

    private static async Task<NativeToolException> ApplyFailingBatchAsync(
        WordLiveService service,
        string documentId
    )
    {
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = 0,
                    operations = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = "pierwszy akapit",
                            as_new_paragraph = true,
                        },
                        new
                        {
                            type = "equation",
                            value = "x",
                            input_format = "unicodemath",
                            display = true,
                        },
                    },
                }
            )
        );
        return await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "apply_live_word_operations",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );
    }
}

public enum RollbackBehavior
{
    RestoreExact,
    LeaveContaminated,
    RestoreTextOnly,
    ReturnFalse,
    Throw,
    EndRecordThrows,
    RestoreVisibleStateOnly,
}

internal sealed class RollbackFakeHost : IWordComHost
{
    public RollbackFakeHost(
        RollbackBehavior rollbackBehavior,
        bool failStagingEquation = false,
        bool failTargetPublication = true,
        bool failPublicationBeforeMutation = false,
        bool driftTargetDuringStaging = false,
        bool failStagingOpen = false,
        bool failIndependentRestore = true
    )
    {
        Application = new RollbackFakeApplication(
            rollbackBehavior,
            failStagingEquation,
            failTargetPublication,
            failPublicationBeforeMutation,
            driftTargetDuringStaging,
            failStagingOpen,
            failIndependentRestore
        );
    }

    public RollbackFakeApplication Application { get; }

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

public sealed class RollbackFakeApplication
{
    public RollbackFakeApplication(
        RollbackBehavior rollbackBehavior,
        bool failStagingEquation,
        bool failTargetPublication,
        bool failPublicationBeforeMutation,
        bool driftTargetDuringStaging,
        bool failStagingOpen,
        bool failIndependentRestore
    )
    {
        FailStagingEquation = failStagingEquation;
        DriftTargetDuringStaging = driftTargetDuringStaging;
        FailStagingOpen = failStagingOpen;
        FailIndependentRestore = failIndependentRestore;
        ActiveDocument = new RollbackFakeDocument(
            this,
            rollbackBehavior,
            failEquationBuild: failTargetPublication,
            failPublicationBeforeMutation,
            "Rollback.docx"
        );
        Documents = new RollbackFakeDocuments(ActiveDocument);
        UndoRecord = new RollbackFakeUndoRecord(ActiveDocument);
    }

    public RollbackFakeDocument ActiveDocument { get; set; }
    public RollbackFakeDocuments Documents { get; }
    public RollbackFakeUndoRecord UndoRecord { get; }
    public RollbackFakeWindow ActiveWindow { get; } = new();
    public RollbackFakeOptions Options { get; } = new();
    public int AutomationSecurity { get; set; } = 1;
    public bool ScreenUpdating { get; set; } = true;
    public bool FailStagingEquation { get; }
    public bool DriftTargetDuringStaging { get; }
    public bool FailStagingOpen { get; }
    public bool FailIndependentRestore { get; }
}

public sealed class RollbackFakeOptions
{
    public bool UpdateLinksAtOpen { get; set; } = true;
}

public sealed class RollbackFakeWindow
{
    public int Hwnd => 9001;
}

public sealed class RollbackFakeDocuments
{
    private readonly RollbackFakeDocument _document;

    public RollbackFakeDocuments(RollbackFakeDocument document) => _document = document;

    public int Count => 1;
    public RollbackFakeDocument? LastStagingDocument { get; private set; }
    public string? LastStagingPath { get; private set; }
    public int OpenCount { get; private set; }

    public RollbackFakeDocument Item(int index) =>
        index == 1 ? _document : throw new IndexOutOfRangeException();

    public RollbackFakeDocument Add(bool Visible = false)
    {
        var staging = new RollbackFakeDocument(
            _document.Application,
            RollbackBehavior.RestoreExact,
            failEquationBuild: _document.Application.FailStagingEquation,
            failPublicationBeforeMutation: false,
            "Rollback-stage.docx"
        );
        _document.Application.ActiveDocument = staging;
        LastStagingDocument = staging;
        return staging;
    }

    public RollbackFakeDocument Open(
        string FileName,
        bool ConfirmConversions = false,
        bool ReadOnly = false,
        bool AddToRecentFiles = false,
        bool Revert = false,
        bool Visible = false,
        bool OpenAndRepair = false,
        bool NoEncodingDialog = true
    )
    {
        OpenCount++;
        LastStagingPath = FileName;
        if (_document.Application.FailStagingOpen)
        {
            throw new NativeToolException(
                "STAGING_FAILED",
                "Fake Word could not open the Flat OPC staging artifact"
            );
        }
        var staging = new RollbackFakeDocument(
            _document.Application,
            RollbackBehavior.RestoreExact,
            failEquationBuild: _document.Application.FailStagingEquation,
            failPublicationBeforeMutation: false,
            Path.GetFileName(FileName)
        );
        staging.IsRecoverySource = OpenCount > 1;
        _document.Application.ActiveDocument = staging;
        LastStagingDocument = staging;
        if (_document.Application.DriftTargetDuringStaging)
        {
            _document.InjectHiddenStagingDrift();
        }
        return staging;
    }
}

public sealed class RollbackFakeDocument
{
    private readonly RollbackFakeApplication _application;
    private readonly RollbackBehavior _rollbackBehavior;
    private readonly bool _failEquationBuild;
    private readonly bool _failPublicationBeforeMutation;
    private readonly string _name;
    private string _rawText = "\r";
    private string _undoText = "\r";
    private int _undoEquationCount;
    private bool _undoSaved = true;
    private bool _hiddenOpenXmlResidue;
    private bool _undoHiddenOpenXmlResidue;

    public RollbackFakeDocument(
        RollbackFakeApplication application,
        RollbackBehavior rollbackBehavior,
        bool failEquationBuild,
        bool failPublicationBeforeMutation,
        string name
    )
    {
        _application = application;
        _rollbackBehavior = rollbackBehavior;
        _failEquationBuild = failEquationBuild;
        _failPublicationBeforeMutation = failPublicationBeforeMutation;
        _name = name;
        OMaths = new RollbackFakeDocumentEquations(this);
    }

    public string Name => _name;
    public string FullName => @"C:\Fixtures\" + _name;
    public string Path => @"C:\Fixtures";
    public bool Saved { get; set; } = true;
    public bool ReadOnly => false;
    public bool Final => false;
    public bool TrackRevisions { get; set; }
    public int CompatibilityMode => 15;
    public int ProtectionType => -1;
    public string RawText => _rawText;
    public string WordOpenXML
    {
        get
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(_rawText));
            return $"<document hidden=\"{_hiddenOpenXmlResidue}\" equations=\"{OMaths.Count}\">{encoded}</document>";
        }
    }
    public RollbackFakeRange Content => Range(0, _rawText.Length);
    public RollbackFakeCountCollection Paragraphs => new(CountParagraphs(_rawText));
    public RollbackFakeDocumentEquations OMaths { get; }
    public RollbackFakeCountCollection Tables { get; } = new(0);
    public RollbackFakeCountCollection Fields { get; } = new(0);
    public RollbackFakeCountCollection Bookmarks { get; } = new(0);
    public RollbackFakeCountCollection InlineShapes { get; } = new(0);
    public RollbackFakeCountCollection Shapes { get; } = new(0);
    public RollbackFakeCountCollection Comments { get; } = new(0);
    public RollbackFakeCountCollection Footnotes { get; } = new(0);
    public RollbackFakeCountCollection Endnotes { get; } = new(0);
    public RollbackFakeCountCollection Sections { get; } = new(1);
    public int UndoCount { get; private set; }
    public int FormattedTextAssignments { get; private set; }
    public bool Closed { get; private set; }
    public RollbackBehavior RollbackBehavior => _rollbackBehavior;
    public bool FailEquationBuild => _failEquationBuild;
    public RollbackFakeApplication Application => _application;
    internal bool HiddenOpenXmlResidue => _hiddenOpenXmlResidue;
    internal bool IsRecoverySource { get; set; }

    public RollbackFakeRange Range(int start, int end) => new(this, start, end);

    public void Activate() => _application.ActiveDocument = this;

    public void Close(int saveChanges) => Closed = true;

    public void CaptureUndoSnapshot()
    {
        _undoText = _rawText;
        _undoEquationCount = OMaths.Count;
        _undoSaved = Saved;
        _undoHiddenOpenXmlResidue = _hiddenOpenXmlResidue;
    }

    public bool Undo(int count)
    {
        Assert.Equal(1, count);
        UndoCount++;
        switch (_rollbackBehavior)
        {
            case RollbackBehavior.RestoreExact:
                RestoreText();
                OMaths.Count = _undoEquationCount;
                Saved = _undoSaved;
                _hiddenOpenXmlResidue = _undoHiddenOpenXmlResidue;
                return true;
            case RollbackBehavior.RestoreTextOnly:
                RestoreText();
                Saved = _undoSaved;
                return true;
            case RollbackBehavior.LeaveContaminated:
                return true;
            case RollbackBehavior.ReturnFalse:
                return false;
            case RollbackBehavior.Throw:
                throw new InvalidOperationException("Fake Word Undo failed");
            case RollbackBehavior.EndRecordThrows:
                throw new InvalidOperationException(
                    "Undo must not be attempted after EndCustomRecord failed"
                );
            case RollbackBehavior.RestoreVisibleStateOnly:
                RestoreText();
                OMaths.Count = _undoEquationCount;
                Saved = _undoSaved;
                return true;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal string Read(int start, int end)
    {
        var safeStart = Math.Clamp(start, 0, _rawText.Length);
        var safeEnd = Math.Clamp(end, safeStart, _rawText.Length);
        return _rawText[safeStart..safeEnd];
    }

    internal void Replace(int start, int end, string value)
    {
        _rawText = _rawText[..start] + value + _rawText[end..];
        Saved = false;
        _hiddenOpenXmlResidue = true;
    }

    internal void MarkEquationCreated()
    {
        Saved = false;
        _hiddenOpenXmlResidue = true;
    }

    internal void PublishFormattedText(
        int start,
        int end,
        RollbackFakeRange source
    )
    {
        FormattedTextAssignments++;
        if (source.Document.IsRecoverySource && _application.FailIndependentRestore)
        {
            throw new NativeToolException(
                "ROLLBACK_FAILED",
                "Fake Word rejected the independent baseline restoration"
            );
        }
        if (_failPublicationBeforeMutation)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Fake Word rejected the staged publication before mutation"
            );
        }
        Replace(start, end, source.Text);
        OMaths.ReplaceRangeFrom(start, end, source);
        _hiddenOpenXmlResidue = source.Document.HiddenOpenXmlResidue;
        Saved = source.Document.Saved;
        if (_failEquationBuild && !source.Document.IsRecoverySource)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Fake Word rejected the staged publication after partial mutation"
            );
        }
    }

    internal void InjectHiddenStagingDrift() => _hiddenOpenXmlResidue = true;

    private void RestoreText() => _rawText = _undoText;

    private static int CountParagraphs(string value) =>
        Math.Max(1, value.Count(character => character == '\r'));
}

public sealed class RollbackFakeRange
{
    private readonly RollbackFakeDocument _document;

    public RollbackFakeRange(RollbackFakeDocument document, int start, int end)
    {
        _document = document;
        Start = start;
        End = end;
    }

    public int Start { get; private set; }
    public int End { get; private set; }
    internal RollbackFakeDocument Document => _document;
    public string Text
    {
        get => _document.Read(Start, End);
        set
        {
            _document.Replace(Start, End, value);
            End = Start + value.Length;
        }
    }
    public string WordOpenXML
    {
        get
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Text));
            return $"<range start=\"{Start}\" end=\"{End}\" hidden=\"{_document.HiddenOpenXmlResidue}\">{encoded}</range>";
        }
    }
    public RollbackFakeRange FormattedText
    {
        get => this;
        set
        {
            _document.PublishFormattedText(Start, End, value);
            End = Start + value.Text.Length;
        }
    }
    public RollbackFakeRangeEquations OMaths =>
        _document.OMaths.ForRange(Start, End);
    public object? Style { get; set; }
}

public sealed class RollbackFakeDocumentEquations
{
    private readonly RollbackFakeDocument _document;
    private readonly List<RollbackFakeEquation> _equations = [];

    public RollbackFakeDocumentEquations(RollbackFakeDocument document) =>
        _document = document;

    public int Count
    {
        get => _equations.Count;
        set
        {
            while (_equations.Count > value)
            {
                _equations.RemoveAt(_equations.Count - 1);
            }
            while (_equations.Count < value)
            {
                _equations.Add(
                    new RollbackFakeEquation(_document, _document.Range(0, 0))
                );
            }
        }
    }
    public int AddCalls { get; private set; }

    public RollbackFakeAddedEquationRange Add(RollbackFakeRange range)
    {
        AddCalls++;
        _document.MarkEquationCreated();
        var equation = new RollbackFakeEquation(_document, range);
        _equations.Add(equation);
        return new RollbackFakeAddedEquationRange(equation);
    }

    public RollbackFakeRangeEquations ForRange(int start, int end) =>
        new(
            _equations
                .Where(
                    equation =>
                        equation.Range.Start >= start && equation.Range.End <= end
                )
                .ToArray()
        );

    public void ReplaceRangeFrom(int start, int end, RollbackFakeRange source)
    {
        _equations.RemoveAll(
            equation => equation.Range.Start >= start && equation.Range.End <= end
        );
        foreach (
            var sourceEquation in source.OMaths.Items
        )
        {
            var relativeStart = sourceEquation.Range.Start - source.Start;
            var relativeEnd = sourceEquation.Range.End - source.Start;
            _equations.Add(
                new RollbackFakeEquation(
                    _document,
                    _document.Range(start + relativeStart, start + relativeEnd)
                )
                {
                    Type = sourceEquation.Type,
                }
            );
        }
        _equations.Sort(
            static (left, right) => left.Range.Start.CompareTo(right.Range.Start)
        );
    }
}

public sealed class RollbackFakeRangeEquations
{
    public RollbackFakeRangeEquations(IReadOnlyList<RollbackFakeEquation> items) =>
        Items = items;

    public IReadOnlyList<RollbackFakeEquation> Items { get; }
    public int Count => Items.Count;

    public RollbackFakeEquation Item(int index) =>
        index >= 1 && index <= Items.Count
            ? Items[index - 1]
            : throw new IndexOutOfRangeException();
}

public sealed class RollbackFakeAddedEquationRange
{
    public RollbackFakeAddedEquationRange(RollbackFakeEquation equation)
    {
        OMaths = new RollbackFakeAddedEquations(equation);
    }

    public RollbackFakeAddedEquations OMaths { get; }
}

public sealed class RollbackFakeAddedEquations
{
    private readonly RollbackFakeEquation _equation;

    public RollbackFakeAddedEquations(RollbackFakeEquation equation) =>
        _equation = equation;

    public RollbackFakeEquation Item(int index) =>
        index == 1 ? _equation : throw new IndexOutOfRangeException();
}

public sealed class RollbackFakeEquation
{
    private readonly RollbackFakeDocument _document;

    public RollbackFakeEquation(RollbackFakeDocument document, RollbackFakeRange range)
    {
        _document = document;
        Range = range;
    }

    public RollbackFakeRange Range { get; }
    public int Type { get; set; }

    public void BuildUp()
    {
        if (!_document.FailEquationBuild)
        {
            return;
        }
        throw new NativeToolException(
            "EQUATION_INVALID",
            "Fake Word rejected the native equation after partial mutation"
        );
    }
}

public sealed class RollbackFakeUndoRecord
{
    private readonly RollbackFakeDocument _document;

    public RollbackFakeUndoRecord(RollbackFakeDocument document) => _document = document;

    public void StartCustomRecord(string name) => _document.CaptureUndoSnapshot();

    public void EndCustomRecord()
    {
        if (_document.RollbackBehavior == RollbackBehavior.EndRecordThrows)
        {
            throw new InvalidOperationException("Fake EndCustomRecord failed");
        }
    }
}

public sealed class RollbackFakeCountCollection
{
    public RollbackFakeCountCollection(int count) => Count = count;

    public int Count { get; }
}
