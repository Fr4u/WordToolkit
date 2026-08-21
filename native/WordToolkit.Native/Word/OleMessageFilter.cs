using System.Runtime.InteropServices;

namespace WordToolkit.Native.Word;

[ComImport]
[Guid("00000016-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleMessageFilter
{
    [PreserveSig]
    int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo);

    [PreserveSig]
    int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType);

    [PreserveSig]
    int MessagePending(IntPtr taskCallee, int tickCount, int pendingType);
}

internal sealed class OleMessageFilter : IOleMessageFilter
{
    private const int ServerCallRetryLater = 2;
    internal const int DefaultRetryDelayMilliseconds = 100;
    internal const uint DefaultRetryBudgetMilliseconds = 30_000;
    private readonly uint _retryBudgetMilliseconds;
    private readonly int _retryDelayMilliseconds;

    public OleMessageFilter(
        uint retryBudgetMilliseconds = DefaultRetryBudgetMilliseconds,
        int retryDelayMilliseconds = DefaultRetryDelayMilliseconds
    )
    {
        if (retryBudgetMilliseconds == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryBudgetMilliseconds));
        }
        if (retryDelayMilliseconds < 100)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelayMilliseconds));
        }
        _retryBudgetMilliseconds = retryBudgetMilliseconds;
        _retryDelayMilliseconds = retryDelayMilliseconds;
    }

    public static void Register()
    {
        NativeMethods.CoRegisterMessageFilter(new OleMessageFilter(), out _);
    }

    public static void Revoke()
    {
        NativeMethods.CoRegisterMessageFilter(null, out _);
    }

    public int HandleInComingCall(
        int callType,
        IntPtr taskCaller,
        int tickCount,
        IntPtr interfaceInfo
    ) => 0;

    public int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType)
    {
        if (rejectType != ServerCallRetryLater)
        {
            return -1;
        }
        var elapsedMilliseconds = unchecked((uint)tickCount);
        if (elapsedMilliseconds >= _retryBudgetMilliseconds)
        {
            return -1;
        }
        var remainingMilliseconds = _retryBudgetMilliseconds - elapsedMilliseconds;
        return remainingMilliseconds < (uint)_retryDelayMilliseconds
            ? -1
            : _retryDelayMilliseconds;
    }

    public int MessagePending(IntPtr taskCallee, int tickCount, int pendingType) => 2;

    private static class NativeMethods
    {
        [DllImport("ole32.dll")]
        internal static extern int CoRegisterMessageFilter(
            IOleMessageFilter? newFilter,
            out IOleMessageFilter? oldFilter
        );
    }
}
