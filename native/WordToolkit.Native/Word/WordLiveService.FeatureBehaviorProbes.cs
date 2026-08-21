using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> ProbeFeatureBehaviorsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        RequireObject(arguments, "live Word feature behavior probe arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name is not ("live_document_id" or "confirm_scratch_documents"))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown live Word feature behavior probe argument",
                    new { argument = property.Name }
                );
            }
        }
        if (!arguments.Boolean("confirm_scratch_documents", false))
        {
            throw new NativeToolException(
                "AUTH_FORBIDDEN",
                "probe_live_word_feature_behaviors requires confirm_scratch_documents=true"
            );
        }

        var record = Record(arguments.String("live_document_id"));
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                _ = ResolveDocument(application, record);
                object originalActiveDocument = application.ActiveDocument;
                object originalActiveWindow = application.ActiveWindow;
                var initialDocumentCount = (int)application.Documents.Count;

                var probes = new Dictionary<string, FeatureBehaviorProbeResult>(
                    StringComparer.Ordinal
                )
                {
                    ["native_omath"] = RunFeatureBehaviorProbe(
                        (object)application,
                        originalActiveDocument,
                        originalActiveWindow,
                        record,
                        "native_omath",
                        "NATIVE_OMATH_BEHAVIOR_FAILED",
                        ProbeNativeOMathBehavior
                    ),
                    ["content_controls"] = RunFeatureBehaviorProbe(
                        (object)application,
                        originalActiveDocument,
                        originalActiveWindow,
                        record,
                        "content_controls",
                        "CONTENT_CONTROLS_BEHAVIOR_FAILED",
                        ProbeContentControlBehavior
                    ),
                    ["smartart"] = RunFeatureBehaviorProbe(
                        (object)application,
                        originalActiveDocument,
                        originalActiveWindow,
                        record,
                        "smartart",
                        "SMARTART_BEHAVIOR_FAILED",
                        ProbeSmartArtBehavior
                    ),
                    ["undo_record"] = RunFeatureBehaviorProbe(
                        (object)application,
                        originalActiveDocument,
                        originalActiveWindow,
                        record,
                        "undo_record",
                        "UNDO_RECORD_BEHAVIOR_FAILED",
                        ProbeUndoRecordBehavior
                    ),
                };

                var finalDocumentCount = (int)application.Documents.Count;
                var activeDocumentRestored = SameWordComIdentity(
                    (object)application.ActiveDocument,
                    originalActiveDocument
                );
                var activeWindowRestored = SameWordComIdentity(
                    (object)application.ActiveWindow,
                    originalActiveWindow
                );
                if (
                    finalDocumentCount != initialDocumentCount
                    || !activeDocumentRestored
                    || !activeWindowRestored
                )
                {
                    QuarantineLiveDocument(record, "TEMPORARY_DOCUMENT_CLEANUP_FAILED");
                    throw new NativeToolException(
                        "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                        "WordToolkit could not prove restoration of Word after the isolated behavior probes; the live handle was quarantined",
                        new
                        {
                            phase = "final_isolation_verification",
                            document_count_restored = finalDocumentCount
                                == initialDocumentCount,
                            active_document_restored = activeDocumentRestored,
                            active_window_restored = activeWindowRestored,
                            connected_document_content_mutated = false,
                            live_handle_quarantined = true,
                            requires_explicit_disconnect = true,
                        }
                    );
                }

                return new
                {
                    operation_contract = "wordtoolkit.probe_live_word_feature_behaviors/1.0",
                    live_document_id = record.Id,
                    live_version = record.Version,
                    backend = "microsoft_word_com",
                    probes,
                    summary = new
                    {
                        passed = probes.Values.Count(item => item.Status == "passed"),
                        unavailable = probes.Values.Count(item => item.Status == "unavailable"),
                        failed = probes.Values.Count(item => item.Status == "failed"),
                    },
                    isolation = new
                    {
                        scratch_document_per_probe = true,
                        scratch_documents_created = probes.Values.Count(
                            item => item.ScratchDocumentCreated
                        ),
                        scratch_documents_closed = probes.Values.Count(
                            item => item.ScratchDocumentClosed
                        ),
                        document_count_restored = true,
                        active_document_restored = true,
                        active_window_restored = true,
                        connected_document_content_mutated = false,
                        connected_document_package_identity_verified = false,
                        word_may_update_volatile_view_state = true,
                    },
                    security = new
                    {
                        reads_connected_document_content = false,
                        reads_scratch_document_content = true,
                        returns_document_content = false,
                        returns_paths = false,
                        returns_user_or_license_identity = false,
                        opens_word = false,
                        creates_unsaved_scratch_documents = true,
                        saves_scratch_documents = false,
                        uses_network = false,
                    },
                    runtime = "dotnet-native",
                    python_used = false,
                    performance = Performance(started),
                };
            },
            WordComReplaySafety.NonReplayable,
            cancellationToken
        );
    }

    private FeatureBehaviorProbeResult RunFeatureBehaviorProbe(
        object applicationObject,
        object originalActiveDocument,
        object originalActiveWindow,
        LiveDocumentRecord record,
        string feature,
        string failureCode,
        Func<object, object, FeatureBehaviorProbeOutcome> probe
    )
    {
        dynamic application = applicationObject;
        var started = Stopwatch.GetTimestamp();
        var baselineDocumentCount = (int)application.Documents.Count;
        object? scratchDocument = null;
        var scratchDocumentCreated = false;
        var scratchDocumentClosed = false;
        FeatureBehaviorProbeOutcome outcome;
        Exception? probeFailure = null;

        try
        {
            scratchDocument = (object)application.Documents.Add(Visible: false);
            scratchDocumentCreated = true;
            ((dynamic)scratchDocument).Activate();
            if (!SameWordComIdentity((object)application.ActiveDocument, scratchDocument))
            {
                throw new InvalidOperationException(
                    "The isolated scratch document did not become active"
                );
            }
            outcome = probe(applicationObject, scratchDocument);
        }
        catch (Exception exception)
        {
            probeFailure = exception;
            outcome = new FeatureBehaviorProbeOutcome("failed", false, failureCode);
        }

        Exception? closeFailure = null;
        Exception? activeDocumentRestoreFailure = null;
        Exception? activeWindowRestoreFailure = null;
        Exception? documentCountReadFailure = null;
        Exception? activeDocumentReadFailure = null;
        Exception? activeWindowReadFailure = null;
        var documentCountRestored = false;
        var activeDocumentRestored = false;
        var activeWindowRestored = false;

        if (scratchDocument is not null)
        {
            try
            {
                ((dynamic)scratchDocument).Close(WordDoNotSaveChanges);
                scratchDocumentClosed = true;
            }
            catch (Exception exception)
            {
                closeFailure = exception;
            }
        }
        try
        {
            ((dynamic)originalActiveDocument).Activate();
        }
        catch (Exception exception)
        {
            activeDocumentRestoreFailure = exception;
        }
        try
        {
            ((dynamic)originalActiveWindow).Activate();
        }
        catch (Exception exception)
        {
            activeWindowRestoreFailure = exception;
        }
        try
        {
            documentCountRestored = (int)application.Documents.Count
                == baselineDocumentCount;
        }
        catch (Exception exception)
        {
            documentCountReadFailure = exception;
        }
        try
        {
            activeDocumentRestored = SameWordComIdentity(
                (object)application.ActiveDocument,
                originalActiveDocument
            );
        }
        catch (Exception exception)
        {
            activeDocumentReadFailure = exception;
        }
        try
        {
            activeWindowRestored = SameWordComIdentity(
                (object)application.ActiveWindow,
                originalActiveWindow
            );
        }
        catch (Exception exception)
        {
            activeWindowReadFailure = exception;
        }

        var probeReportedCleanupFailure =
            probeFailure is NativeToolException nativeFailure
            && nativeFailure.ErrorCode == "TEMPORARY_DOCUMENT_CLEANUP_FAILED";
        if (
            probeReportedCleanupFailure
            || closeFailure is not null
            || activeDocumentRestoreFailure is not null
            || activeWindowRestoreFailure is not null
            || documentCountReadFailure is not null
            || activeDocumentReadFailure is not null
            || activeWindowReadFailure is not null
            || !documentCountRestored
            || !activeDocumentRestored
            || !activeWindowRestored
        )
        {
            QuarantineLiveDocument(record, "TEMPORARY_DOCUMENT_CLEANUP_FAILED");
            throw new NativeToolException(
                "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                "WordToolkit could not prove closure of an isolated behavior-probe document and restoration of the prior Word state; the live handle was quarantined",
                new
                {
                    feature,
                    original_error_code = probeFailure is NativeToolException originalNative
                        ? originalNative.ErrorCode
                        : probeFailure is null
                            ? null
                            : failureCode,
                    scratch_document_created = scratchDocumentCreated,
                    scratch_document_closed = scratchDocumentClosed,
                    close_failed = closeFailure is not null,
                    document_count_restored = documentCountRestored,
                    active_document_restored = activeDocumentRestored,
                    active_window_restored = activeWindowRestored,
                    connected_document_content_mutated = false,
                    live_handle_quarantined = true,
                    requires_explicit_disconnect = true,
                }
            );
        }

        return new FeatureBehaviorProbeResult(
            outcome.Status,
            outcome.BehaviorVerified,
            outcome.IssueCode,
            scratchDocumentCreated,
            scratchDocumentClosed,
            true,
            true,
            ElapsedMilliseconds(started)
        );
    }

    private static FeatureBehaviorProbeOutcome ProbeNativeOMathBehavior(
        object applicationObject,
        object scratchDocumentObject
    )
    {
        _ = applicationObject;
        dynamic document = scratchDocumentObject;
        var before = (int)document.OMaths.Count;
        dynamic range = document.Range(0, 0);
        range.Text = "x^2";
        dynamic addedRange = document.OMaths.Add(range);
        if (addedRange is null || (int)addedRange.OMaths.Count != 1)
        {
            throw new InvalidOperationException("Word did not return one native equation");
        }
        addedRange.OMaths.Item(1).BuildUp();
        if ((int)document.OMaths.Count != before + 1)
        {
            throw new InvalidOperationException("Word did not retain one built-up equation");
        }
        return FeatureBehaviorProbeOutcome.Passed;
    }

    private static FeatureBehaviorProbeOutcome ProbeContentControlBehavior(
        object applicationObject,
        object scratchDocumentObject
    )
    {
        _ = applicationObject;
        dynamic document = scratchDocumentObject;
        var before = (int)document.ContentControls.Count;
        dynamic range = document.Range(0, 0);
        dynamic contentControl = document.ContentControls.Add(0, range);
        if (contentControl is null || (int)document.ContentControls.Count != before + 1)
        {
            throw new InvalidOperationException("Word did not retain one content control");
        }
        return FeatureBehaviorProbeOutcome.Passed;
    }

    private static FeatureBehaviorProbeOutcome ProbeSmartArtBehavior(
        object applicationObject,
        object scratchDocumentObject
    )
    {
        dynamic application = applicationObject;
        dynamic document = scratchDocumentObject;
        dynamic layouts = application.SmartArtLayouts;
        if (layouts is null || (int)layouts.Count < 1)
        {
            return new FeatureBehaviorProbeOutcome(
                "unavailable",
                false,
                "SMARTART_LAYOUT_UNAVAILABLE"
            );
        }
        var before = (int)document.Shapes.Count;
        dynamic anchor = document.Range(0, 0);
        dynamic shape = document.Shapes.AddSmartArt(
            layouts.Item(1),
            Type.Missing,
            Type.Missing,
            Type.Missing,
            Type.Missing,
            anchor
        );
        if (
            shape is null
            || Convert.ToInt32(shape.HasSmartArt) == 0
            || (int)document.Shapes.Count != before + 1
        )
        {
            throw new InvalidOperationException("Word did not retain one SmartArt shape");
        }
        return FeatureBehaviorProbeOutcome.Passed;
    }

    private static FeatureBehaviorProbeOutcome ProbeUndoRecordBehavior(
        object applicationObject,
        object scratchDocumentObject
    )
    {
        dynamic application = applicationObject;
        dynamic document = scratchDocumentObject;
        var beforeText = (string?)document.Content.Text ?? "";
        dynamic undoRecord = application.UndoRecord;
        var undoStarted = false;
        try
        {
            undoRecord.StartCustomRecord("WordToolkit feature probe");
            undoStarted = true;
            dynamic range = document.Range(0, 0);
            range.Text = "wordtoolkit-probe";
        }
        finally
        {
            if (undoStarted)
            {
                try
                {
                    undoRecord.EndCustomRecord();
                }
                catch
                {
                    throw new NativeToolException(
                        "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                        "Word did not close the temporary custom Undo record"
                    );
                }
            }
        }
        if (!(bool)document.Undo(1))
        {
            throw new InvalidOperationException("Word rejected the temporary Undo operation");
        }
        var afterText = (string?)document.Content.Text ?? "";
        if (!string.Equals(beforeText, afterText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Word did not restore the scratch text");
        }
        return FeatureBehaviorProbeOutcome.Passed;
    }

    private static bool SameWordComIdentity(object left, object right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        if (!Marshal.IsComObject(left) || !Marshal.IsComObject(right))
        {
            return false;
        }

        nint leftIdentity = 0;
        nint rightIdentity = 0;
        try
        {
            leftIdentity = Marshal.GetIUnknownForObject(left);
            rightIdentity = Marshal.GetIUnknownForObject(right);
            return leftIdentity == rightIdentity;
        }
        finally
        {
            if (rightIdentity != 0)
            {
                Marshal.Release(rightIdentity);
            }
            if (leftIdentity != 0)
            {
                Marshal.Release(leftIdentity);
            }
        }
    }

    private static long ElapsedMilliseconds(long started)
    {
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return Math.Max(0, (long)Math.Ceiling(elapsed));
    }

    private sealed record FeatureBehaviorProbeResult(
        string Status,
        bool BehaviorVerified,
        string IssueCode,
        bool ScratchDocumentCreated,
        bool ScratchDocumentClosed,
        bool ActiveDocumentRestored,
        bool ActiveWindowRestored,
        long DurationMs
    );

    private sealed record FeatureBehaviorProbeOutcome(
        string Status,
        bool BehaviorVerified,
        string IssueCode
    )
    {
        public static FeatureBehaviorProbeOutcome Passed { get; } =
            new("passed", true, "NONE");
    }
}
