using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordRollbackAcceptanceTests
{
    [Fact]
    public async Task RealWordSystemNotesStabilizeRollbackSnapshot()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_ROLLBACK_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is not null)
            {
                return ownedApplication;
            }
            if (!launchIfMissing)
            {
                throw new InvalidOperationException(
                    "The real-Word regression requires its dedicated application instance."
                );
            }
            ownedApplication = CreateOwnedWordApplication();
            return ownedApplication;
        }

        await using var host = new WordComHost(CreateApplication, shutdownTimeout: TimeSpan.FromSeconds(15));
        var service = new WordLiveService(host);
        string? documentName = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            await host.InvokeAsync(
                application =>
                {
                    dynamic document = application.Documents.Add(Visible: false);
                    documentName = (string)document.Name;
                    return true;
                },
                launchIfMissing: true
            );

            using var connectArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                document_name = documentName,
                activate = false,
            }));
            using var connectedJson = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync(
                    "connect_live_word_document",
                    connectArguments.RootElement,
                    CancellationToken.None
                ),
                JsonDefaults.Compact
            ));
            var documentId = connectedJson.RootElement.GetProperty("live_document_id").GetString()!;
            var version = connectedJson.RootElement.GetProperty("live_version").GetInt64();

            async Task<JsonElement> InsertAsync(string kind, string mark, long expectedVersion)
            {
                using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    live_document_id = documentId,
                    kind,
                    text = kind == "footnote" ? "Footnote F" : "Endnote E",
                    custom_mark = mark,
                    target = "document_end",
                    expected_version = expectedVersion,
                    activate = false,
                }));
                using var result = JsonDocument.Parse(JsonSerializer.Serialize(
                    await service.CallAsync("insert_live_word_note", arguments.RootElement, CancellationToken.None),
                    JsonDefaults.Compact
                ));
                return result.RootElement.Clone();
            }

            var footnote = await InsertAsync("footnote", "F", version);
            version = footnote.GetProperty("live_version").GetInt64();
            var endnote = await InsertAsync("endnote", "E", version);
            version = endnote.GetProperty("live_version").GetInt64();
            Assert.Equal(2, version);
            Assert.Equal(1, endnote.GetProperty("document").GetProperty("footnote_count").GetInt32());
            Assert.Equal(1, endnote.GetProperty("document").GetProperty("endnote_count").GetInt32());
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            Exception? cleanupFailure = null;
            if (documentName is not null)
            {
                try
                {
                    await host.InvokeAsync(
                        application =>
                        {
                            foreach (dynamic document in application.Documents)
                            {
                                if ((string)document.Name == documentName)
                                {
                                    document.Close(0);
                                    break;
                                }
                            }
                            return true;
                        },
                        launchIfMissing: false
                    );
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
            }
            try
            {
                await host.InvokeAsync(
                    application =>
                    {
                        application.Quit(0);
                        return true;
                    },
                    launchIfMissing: false
                );
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }
            if (primary is not null)
            {
                primary.Throw();
            }
            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }

    private static object CreateOwnedWordApplication()
    {
        var wordType = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
            ?? throw new InvalidOperationException("Microsoft Word ProgID is unavailable.");
        return Activator.CreateInstance(wordType)
            ?? throw new InvalidOperationException("Microsoft Word application could not be created.");
    }

    [Fact]
    public async Task WordUndoThatLeavesOoxmlDriftFailsClosed()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_ROLLBACK_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is not null) return ownedApplication;
            if (!launchIfMissing) throw new InvalidOperationException("The real-Word regression requires its dedicated application instance.");
            ownedApplication = CreateOwnedWordApplication();
            return ownedApplication;
        }
        await using var host = new WordComHost(CreateApplication, shutdownTimeout: TimeSpan.FromSeconds(15));
        var service = new WordLiveService(host);
        ExceptionDispatchInfo? primary = null;
        try { await host.InvokeAsync(
            application =>
            {
                dynamic document = application.Documents.Add(Visible: false);
                try
                {
                    var record = new LiveDocumentRecord
                    {
                        Id = "real-word-rollback-proof",
                        Name = (string)document.Name,
                        FullName = (string)document.FullName,
                        WindowHwnd = 0,
                        Version = 0,
                    };
                    var baseline = WordLiveService.CaptureLiveRollbackSnapshot(
                        (object)document,
                        0
                    );
                    dynamic undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: real rollback proof");
                    var undoStarted = true;
                    document.Range(0, 0).InsertBefore("contamination\n");

                    var error = Assert.Throws<NativeToolException>(
                        () =>
                            service.RollbackPreparedOperationsOrThrow(
                                (object)document,
                                (object)undoRecord,
                                ref undoStarted,
                                mutationAttempted: true,
                                baseline,
                                record,
                                new NativeToolException(
                                    "INVALID_INPUT",
                                    "synthetic failure"
                                )
                            )
                    );

                    Assert.Equal("ROLLBACK_FAILED", error.ErrorCode);
                    var restored = WordLiveService.CaptureLiveRollbackSnapshot(
                        (object)document,
                        0
                    );
                    var differences = baseline.Differences(restored);
                    Assert.Contains("document_word_open_xml_sha256", differences);
                    Assert.Contains("story_graph_sha256", differences);
                    Assert.Equal(baseline.ContentTextSha256, restored.ContentTextSha256);
                    Assert.Equal(baseline.ParagraphCount, restored.ParagraphCount);
                    Assert.Equal(baseline.EquationCount, restored.EquationCount);
                    Assert.Equal(0, record.Version);
                    return true;
                }
                finally
                {
                    document.Close(0);
                }
            }, launchIfMissing: true);
        } catch (Exception exception) { primary = ExceptionDispatchInfo.Capture(exception); }
        finally
        {
            try { await host.InvokeAsync(application => { application.Quit(0); return true; }, launchIfMissing: false); }
            catch (Exception cleanup) { primary ??= ExceptionDispatchInfo.Capture(cleanup); }
            primary?.Throw();
        }
    }

    [Fact]
    public async Task FlatOpcBaselineRemovesVisibleResidueButDoesNotFakePackageIdentity()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_ROLLBACK_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is not null) return ownedApplication;
            if (!launchIfMissing) throw new InvalidOperationException("The real-Word regression requires its dedicated application instance.");
            ownedApplication = CreateOwnedWordApplication();
            return ownedApplication;
        }
        await using var host = new WordComHost(CreateApplication, shutdownTimeout: TimeSpan.FromSeconds(15));
        ExceptionDispatchInfo? primary = null;
        try { await host.InvokeAsync(
            application =>
            {
                dynamic document = application.Documents.Add(Visible: false);
                try
                {
                    document.Content.Text = "Przed\nEquation: x^2 + y^2 = z^2\nPo\n";
                    dynamic equationParagraph = document.Paragraphs.Item(2).Range;
                    dynamic equationRange = document.Range(
                        (int)equationParagraph.Start + 10,
                        (int)equationParagraph.End - 1
                    );
                    dynamic added = document.OMaths.Add(equationRange);
                    added.OMaths.Item(1).BuildUp();
                    var baseline = WordLiveService.CaptureLiveRollbackSnapshot(
                        (object)document,
                        0
                    );
                    var flatOpc = (string)document.WordOpenXML;

                    document.Content.Text = "skażenie\nresztka\nresztka\n";
                    Assert.Equal(0, (int)document.OMaths.Count);

                    WordLiveService.RestoreLiveMainStoryFromFlatOpc(
                        application,
                        document,
                        flatOpc
                    );
                    var restored = WordLiveService.CaptureLiveRollbackSnapshot(
                        (object)document,
                        0
                    );

                    Assert.Equal(baseline.ContentTextSha256, restored.ContentTextSha256);
                    Assert.Equal(baseline.ParagraphCount, restored.ParagraphCount);
                    Assert.Equal(baseline.EquationCount, restored.EquationCount);
                    Assert.Equal(
                        new[] { "document_semantic_word_open_xml_sha256" },
                        baseline.RecoveryDifferences(restored)
                    );
                    return true;
                }
                finally
                {
                    document.Close(0);
                }
            }, launchIfMissing: true); }
        catch (Exception exception) { primary = ExceptionDispatchInfo.Capture(exception); }
        finally
        {
            try { await host.InvokeAsync(application => { application.Quit(0); return true; }, launchIfMissing: false); }
            catch (Exception cleanup) { primary ??= ExceptionDispatchInfo.Capture(cleanup); }
            primary?.Throw();
        }
    }
}
