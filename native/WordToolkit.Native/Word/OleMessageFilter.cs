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
        return rejectType == ServerCallRetryLater ? 100 : -1;
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
