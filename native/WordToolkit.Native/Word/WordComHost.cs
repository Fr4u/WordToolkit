using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed class WordComHost : IWordComHost
{
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _thread;
    private object? _application;
    private bool _disposed;

    public WordComHost()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "WordToolkit.Native COM STA",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public async Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
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
            launchIfMissing
        );
        _queue.Add(item, cancellationToken);
        var result = await completion.Task.WaitAsync(cancellationToken);
        return (T)result!;
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
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Completion.TrySetCanceled(item.CancellationToken);
                    continue;
                }
                Execute(item);
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
        try
        {
            var application = GetApplication(item.LaunchIfMissing);
            item.Completion.TrySetResult(item.Operation(application));
        }
        catch (COMException exception) when (IsDisconnected(exception))
        {
            ResetApplication();
            try
            {
                var application = GetApplication(item.LaunchIfMissing);
                item.Completion.TrySetResult(item.Operation(application));
            }
            catch (Exception retryException)
            {
                item.Completion.TrySetException(MapException(retryException));
            }
        }
        catch (Exception exception)
        {
            item.Completion.TrySetException(MapException(exception));
        }
    }

    private dynamic GetApplication(bool launchIfMissing)
    {
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

    private sealed record WorkItem(
        Func<dynamic, object?> Operation,
        TaskCompletionSource<object?> Completion,
        CancellationToken CancellationToken,
        bool LaunchIfMissing
    );

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
