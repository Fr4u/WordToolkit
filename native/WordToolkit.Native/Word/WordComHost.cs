using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed class WordComHost : IWordComHost
{
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly object _submissionGate = new();
    private readonly Thread _thread;
    private readonly Func<bool, object>? _applicationFactory;
    private readonly Action? _beforeCancellationObservation;
    private readonly Action? _afterWorkItemCompleted;
    private object? _application;
    private int _abandonedExecutionCount;
    private int _unknownOutcome;
    private bool _disposed;

    public WordComHost() : this(applicationFactory: null) { }

    internal WordComHost(
        Func<bool, object>? applicationFactory,
        Action? beforeCancellationObservation = null,
        Action? afterWorkItemCompleted = null
    )
    {
        _applicationFactory = applicationFactory;
        _beforeCancellationObservation = beforeCancellationObservation;
        _afterWorkItemCompleted = afterWorkItemCompleted;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "WordToolkit.Native COM STA",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    ) =>
        InvokeAsync(
            operation,
            WordComReplaySafety.NonReplayable,
            cancellationToken,
            launchIfMissing
        );

    public async Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        WordComReplaySafety replaySafety,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var item = new WorkItem(
            application => operation(application),
            completion,
            cancellationToken,
            launchIfMissing,
            replaySafety
        );
        using var cancellationRegistration = cancellationToken.Register(
            () => ObserveCancellation(item)
        );
        lock (_submissionGate)
        {
            if (Volatile.Read(ref _abandonedExecutionCount) != 0)
            {
                throw RecoveryRequired();
            }
            if (
                replaySafety == WordComReplaySafety.NonReplayable
                && Volatile.Read(ref _unknownOutcome) != 0
            )
            {
                throw UnknownOutcomeRecoveryRequired();
            }
            _queue.Add(item, cancellationToken);
        }
        try
        {
            var result = await completion
                .Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return (T)result!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _beforeCancellationObservation?.Invoke();
            var observation = ObserveCancellation(item);
            if (observation == CancellationObservation.AbandonedExecution)
            {
                Console.Error.WriteLine(
                    "WordToolkit.Native cancelled an active COM request; "
                        + "restart only the WordToolkit runtime if Word does not return."
                );
            }
            if (
                observation != CancellationObservation.BeforeStart
                && item.ReplaySafety == WordComReplaySafety.NonReplayable
            )
            {
                throw OutcomeUnknown(
                    "The client stopped waiting after the Microsoft Word operation began",
                    reason: "cancellation_requested_after_start"
                );
            }
            throw;
        }
        finally
        {
            item.MarkClientObservationCompleted();
        }
    }

    private CancellationObservation ObserveCancellation(WorkItem item)
    {
        lock (_submissionGate)
        {
            var observation = item.ObserveCancellation(
                () => Interlocked.Increment(ref _abandonedExecutionCount)
            );
            if (
                observation != CancellationObservation.BeforeStart
                && item.ReplaySafety == WordComReplaySafety.NonReplayable
            )
            {
                Volatile.Write(ref _unknownOutcome, 1);
            }
            return observation;
        }
    }

    private void Run()
    {
        var initialized = NativeMethods.CoInitializeEx(
            IntPtr.Zero,
            NativeMethods.COINIT_APARTMENTTHREADED
        );
        if (initialized < 0 && initialized != NativeMethods.RPC_E_CHANGED_MODE)
        {
            FailPending(new COMException("COM STA initialization failed", initialized));
            return;
        }
        OleMessageFilter.Register();
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (item.CancellationToken.IsCancellationRequested)
                    {
                        item.Completion.TrySetCanceled(item.CancellationToken);
                        continue;
                    }
                    if (
                        item.ReplaySafety == WordComReplaySafety.NonReplayable
                        && Volatile.Read(ref _unknownOutcome) != 0
                    )
                    {
                        item.Completion.TrySetException(UnknownOutcomeRecoveryRequired());
                        continue;
                    }
                    Execute(item);
                }
                finally
                {
                    item.WaitForClientObservation();
                }
            }
        }
        finally
        {
            ResetApplication();
            OleMessageFilter.Revoke();
            if (initialized >= 0)
            {
                NativeMethods.CoUninitialize();
            }
        }
    }

    private void Execute(WorkItem item)
    {
        item.MarkStarted();
        try
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
                return;
            }
            var application = GetApplication(item.LaunchIfMissing);
            CompleteSuccessfulOperation(item, item.Operation(application));
        }
        catch (COMException exception) when (IsDisconnected(exception))
        {
            ResetApplication();
            if (
                item.ReplaySafety == WordComReplaySafety.ReplaySafe
                && item.CanReplayAfterDisconnect()
            )
            {
                try
                {
                    var application = GetApplication(item.LaunchIfMissing);
                    CompleteSuccessfulOperation(item, item.Operation(application));
                }
                catch (Exception retryException)
                {
                    item.Completion.TrySetException(MapException(retryException));
                }
            }
            else if (item.ReplaySafety == WordComReplaySafety.NonReplayable)
            {
                MarkUnknownOutcome();
                item.Completion.TrySetException(
                    OutcomeUnknown(
                        "Microsoft Word disconnected before confirming a non-replayable operation",
                        reason: "com_disconnected_during_non_replayable_operation",
                        exception: exception
                    )
                );
            }
            else
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
            }
        }
        catch (Exception exception)
        {
            item.Completion.TrySetException(MapException(exception));
        }
        finally
        {
            var abandoned = item.MarkCompletedAndWasAbandoned();
            _afterWorkItemCompleted?.Invoke();
            if (abandoned)
            {
                ResetApplication();
                Interlocked.Decrement(ref _abandonedExecutionCount);
            }
        }
    }

    private void CompleteSuccessfulOperation(WorkItem item, object? result)
    {
        if (!item.CancellationToken.IsCancellationRequested)
        {
            item.Completion.TrySetResult(result);
            return;
        }
        if (item.ReplaySafety == WordComReplaySafety.NonReplayable)
        {
            MarkUnknownOutcome();
            item.Completion.TrySetException(
                OutcomeUnknown(
                    "The client cancelled before Microsoft Word confirmed the operation result",
                    reason: "cancellation_requested_before_completion"
                )
            );
            return;
        }
        item.Completion.TrySetCanceled(item.CancellationToken);
    }

    private static NativeToolException RecoveryRequired()
    {
        return new NativeToolException(
            "WORD_HOST_RECOVERY_REQUIRED",
            "A cancelled Microsoft Word COM call has not returned",
            new
            {
                recovery = "restart_wordtoolkit_runtime",
                terminate_word_process = false,
                reconnect_and_reinspect = true,
            },
            retryable: true
        );
    }

    private static NativeToolException UnknownOutcomeRecoveryRequired()
    {
        return new NativeToolException(
            "WORD_HOST_RECOVERY_REQUIRED",
            "A previous Microsoft Word operation has an unknown outcome",
            new
            {
                outcome_unknown = true,
                recovery = "restart_wordtoolkit_runtime_then_reconnect_and_reinspect",
                terminate_word_process = false,
                reconnect_and_reinspect = true,
            },
            retryable: false
        );
    }

    private static NativeToolException OutcomeUnknown(
        string message,
        string reason,
        COMException? exception = null
    )
    {
        return new NativeToolException(
            "WORD_OPERATION_OUTCOME_UNKNOWN",
            message,
            new
            {
                outcome_unknown = true,
                reason,
                hresult = exception is null ? null : $"0x{exception.HResult:X8}",
                automatic_replay = false,
                recovery = "restart_wordtoolkit_runtime_then_reconnect_and_reinspect",
                terminate_word_process = false,
            },
            retryable: false
        );
    }

    private void MarkUnknownOutcome()
    {
        lock (_submissionGate)
        {
            Volatile.Write(ref _unknownOutcome, 1);
        }
    }

    private dynamic GetApplication(bool launchIfMissing)
    {
        if (_applicationFactory is not null)
        {
            _application = _applicationFactory(launchIfMissing);
            return _application;
        }
        if (_application is not null)
        {
            try
            {
                dynamic cached = _application;
                _ = (int)cached.Documents.Count;
                return cached;
            }
            catch (COMException exception) when (IsDisconnected(exception))
            {
                ResetApplication();
            }
        }

        var clsidResult = NativeMethods.CLSIDFromProgID("Word.Application", out var clsid);
        if (clsidResult < 0)
        {
            Marshal.ThrowExceptionForHR(clsidResult);
        }
        var activeResult = NativeMethods.GetActiveObject(
            ref clsid,
            IntPtr.Zero,
            out var application
        );
        if (activeResult < 0 || application is null)
        {
            if (!launchIfMissing)
            {
                throw new NativeToolException(
                    "LIVE_WORD_UNAVAILABLE",
                    "Microsoft Word is not running or has no automation-visible instance",
                    new { hresult = $"0x{activeResult:X8}" },
                    retryable: true
                );
            }
            var wordType = Type.GetTypeFromCLSID(clsid, throwOnError: true)
                ?? throw new NativeToolException(
                    "LIVE_WORD_UNAVAILABLE",
                    "Microsoft Word is not installed"
                );
            application = Activator.CreateInstance(wordType)
                ?? throw new NativeToolException(
                    "LIVE_WORD_UNAVAILABLE",
                    "Microsoft Word could not be started",
                    retryable: true
                );
        }
        _application = application;
        return application;
    }

    private static Exception MapException(Exception exception)
    {
        if (exception is NativeToolException)
        {
            return exception;
        }
        if (exception is COMException comException)
        {
            return new NativeToolException(
                "EXTERNAL_TOOL_FAILED",
                "Microsoft Word rejected the native operation",
                new
                {
                    exception = comException.GetType().Name,
                    hresult = $"0x{comException.HResult:X8}",
                },
                retryable: true
            );
        }
        return exception;
    }

    private static bool IsDisconnected(COMException exception)
    {
        return exception.HResult is
            unchecked((int)0x80010108) or
            unchecked((int)0x800706BA) or
            unchecked((int)0x800401E3);
    }

    private void ResetApplication()
    {
        if (_application is null)
        {
            return;
        }
        try
        {
            if (Marshal.IsComObject(_application))
            {
                Marshal.FinalReleaseComObject(_application);
            }
        }
        catch
        {
            // Word may already have destroyed the proxy.
        }
        _application = null;
    }

    private void FailPending(Exception exception)
    {
        while (_queue.TryTake(out var item))
        {
            item.Completion.TrySetException(exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The native Word COM thread did not stop");
        }
        _queue.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class WorkItem
    {
        private readonly object _stateGate = new();
        private readonly TaskCompletionSource<bool> _clientObservation = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private WorkState _state;
        private bool _abandoned;

        public WorkItem(
            Func<dynamic, object?> operation,
            TaskCompletionSource<object?> completion,
            CancellationToken cancellationToken,
            bool launchIfMissing,
            WordComReplaySafety replaySafety
        )
        {
            Operation = operation;
            Completion = completion;
            CancellationToken = cancellationToken;
            LaunchIfMissing = launchIfMissing;
            ReplaySafety = replaySafety;
        }

        public Func<dynamic, object?> Operation { get; }

        public TaskCompletionSource<object?> Completion { get; }

        public CancellationToken CancellationToken { get; }

        public bool LaunchIfMissing { get; }

        public WordComReplaySafety ReplaySafety { get; }

        public void MarkStarted()
        {
            lock (_stateGate)
            {
                _state = WorkState.Executing;
            }
        }

        public CancellationObservation ObserveCancellation(Action acquireRecovery)
        {
            lock (_stateGate)
            {
                if (_state == WorkState.Queued)
                {
                    return CancellationObservation.BeforeStart;
                }
                if (_state == WorkState.Completed)
                {
                    return CancellationObservation.CompletedAfterStart;
                }
                if (!_abandoned)
                {
                    acquireRecovery();
                    _abandoned = true;
                }
                return CancellationObservation.AbandonedExecution;
            }
        }

        public bool CanReplayAfterDisconnect()
        {
            lock (_stateGate)
            {
                return _state == WorkState.Executing
                    && !_abandoned
                    && !CancellationToken.IsCancellationRequested;
            }
        }

        public bool MarkCompletedAndWasAbandoned()
        {
            lock (_stateGate)
            {
                _state = WorkState.Completed;
                return _abandoned;
            }
        }

        public void MarkClientObservationCompleted() => _clientObservation.TrySetResult(true);

        public void WaitForClientObservation() => _clientObservation.Task.GetAwaiter().GetResult();

        private enum WorkState
        {
            Queued,
            Executing,
            Completed,
        }
    }

    private enum CancellationObservation
    {
        BeforeStart,
        AbandonedExecution,
        CompletedAfterStart,
    }

    private static class NativeMethods
    {
        internal const uint COINIT_APARTMENTTHREADED = 0x2;
        internal const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

        [DllImport("ole32.dll")]
        internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        internal static extern void CoUninitialize();

        [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CLSIDFromProgID(string progId, out Guid clsid);

        [DllImport("oleaut32.dll", PreserveSig = true)]
        internal static extern int GetActiveObject(
            ref Guid clsid,
            IntPtr reserved,
            [MarshalAs(UnmanagedType.Interface)] out object? application
        );
    }
}
