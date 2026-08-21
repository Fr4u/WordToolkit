using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WordToolkit.Native.Ocr;

internal sealed class WindowsAppContainerProcess : IDisposable
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint StartfUseStdHandles = 0x00000100;
    private const nuint ProcThreadAttributeHandleList = 0x00020002;
    private const nuint ProcThreadAttributeSecurityCapabilities = 0x00020009;

    private readonly Process _process;
    private SafeFileHandle? _processHandle;
    private SafeFileHandle? _threadHandle;
    private bool _resumed;

    private WindowsAppContainerProcess(
        Process process,
        SafeFileHandle processHandle,
        SafeFileHandle threadHandle,
        SafeFileHandle standardInput,
        SafeFileHandle standardOutput,
        SafeFileHandle standardError
    )
    {
        _process = process;
        _processHandle = processHandle;
        _threadHandle = threadHandle;
        StandardInput = new StreamWriter(
            new FileStream(standardInput, FileAccess.Write, 4096, isAsync: false),
            new UTF8Encoding(false),
            4096,
            leaveOpen: false
        );
        StandardOutput = new StreamReader(
            new FileStream(standardOutput, FileAccess.Read, 4096, isAsync: false),
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            4096,
            leaveOpen: false
        );
        StandardError = new StreamReader(
            new FileStream(standardError, FileAccess.Read, 4096, isAsync: false),
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            4096,
            leaveOpen: false
        );
    }

    internal StreamWriter StandardInput { get; }

    internal StreamReader StandardOutput { get; }

    internal StreamReader StandardError { get; }

    internal IntPtr ProcessHandle => _processHandle?.DangerousGetHandle() ?? IntPtr.Zero;

    internal bool HasExited => _process.HasExited;

    internal int ExitCode => _process.ExitCode;

    internal static WindowsAppContainerProcess LaunchSuspended(
        OcrProviderHostCommand command,
        WindowsAppContainerProfile profile,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory
    )
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("AppContainer requires Windows.");
        }

        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        CreatePipe(out var stdinRead, out var stdinWrite, ref attributes, 0);
        CreatePipe(out var stdoutRead, out var stdoutWrite, ref attributes, 0);
        CreatePipe(out var stderrRead, out var stderrWrite, ref attributes, 0);
        var launched = false;
        try
        {
            MakeNonInheritable(stdinWrite);
            MakeNonInheritable(stdoutRead);
            MakeNonInheritable(stderrRead);

            var sidBytes = new byte[profile.Sid.BinaryLength];
            profile.Sid.GetBinaryForm(sidBytes, 0);
            var sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
            var securityCapabilitiesPointer = IntPtr.Zero;
            var handlesPointer = IntPtr.Zero;
            var attributeList = IntPtr.Zero;
            var environmentPointer = IntPtr.Zero;
            try
            {
                Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);
                var securityCapabilities = new SecurityCapabilities
                {
                    AppContainerSid = sidPointer,
                    Capabilities = IntPtr.Zero,
                    CapabilityCount = 0,
                    Reserved = 0,
                };
                securityCapabilitiesPointer = Marshal.AllocHGlobal(
                    Marshal.SizeOf<SecurityCapabilities>()
                );
                Marshal.StructureToPtr(
                    securityCapabilities,
                    securityCapabilitiesPointer,
                    fDeleteOld: false
                );

                var inheritedHandles = new[]
                {
                    stdinRead.DangerousGetHandle(),
                    stdoutWrite.DangerousGetHandle(),
                    stderrWrite.DangerousGetHandle(),
                };
                handlesPointer = Marshal.AllocHGlobal(IntPtr.Size * inheritedHandles.Length);
                Marshal.Copy(inheritedHandles, 0, handlesPointer, inheritedHandles.Length);

                nuint attributeListSize = 0;
                _ = InitializeProcThreadAttributeList(
                    IntPtr.Zero,
                    2,
                    0,
                    ref attributeListSize
                );
                if (attributeListSize == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                attributeList = Marshal.AllocHGlobal(checked((nint)attributeListSize));
                if (!InitializeProcThreadAttributeList(
                    attributeList,
                    2,
                    0,
                    ref attributeListSize
                ))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributeSecurityCapabilities,
                    securityCapabilitiesPointer,
                    (nuint)Marshal.SizeOf<SecurityCapabilities>(),
                    IntPtr.Zero,
                    IntPtr.Zero
                ))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributeHandleList,
                    handlesPointer,
                    (nuint)(IntPtr.Size * inheritedHandles.Length),
                    IntPtr.Zero,
                    IntPtr.Zero
                ))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var startupInfo = new StartupInfoEx
                {
                    StartupInfo = new StartupInfo
                    {
                        Size = Marshal.SizeOf<StartupInfoEx>(),
                        Flags = StartfUseStdHandles,
                        StandardInput = stdinRead.DangerousGetHandle(),
                        StandardOutput = stdoutWrite.DangerousGetHandle(),
                        StandardError = stderrWrite.DangerousGetHandle(),
                    },
                    AttributeList = attributeList,
                };
                environmentPointer = AllocateEnvironment(environment);
                var commandLine = new StringBuilder(BuildCommandLine(command));
                if (!CreateProcess(
                    command.ExecutablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: true,
                    CreateNoWindow
                        | CreateSuspended
                        | CreateUnicodeEnvironment
                        | ExtendedStartupInfoPresent,
                    environmentPointer,
                    Path.GetFullPath(workingDirectory),
                    ref startupInfo,
                    out var processInformation
                ))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var processHandle = new SafeFileHandle(
                    processInformation.Process,
                    ownsHandle: true
                );
                var threadHandle = new SafeFileHandle(
                    processInformation.Thread,
                    ownsHandle: true
                );
                try
                {
                    var process = Process.GetProcessById(checked((int)processInformation.ProcessId));
                    var result = new WindowsAppContainerProcess(
                        process,
                        processHandle,
                        threadHandle,
                        stdinWrite,
                        stdoutRead,
                        stderrRead
                    );
                    stdinRead.Dispose();
                    stdoutWrite.Dispose();
                    stderrWrite.Dispose();
                    launched = true;
                    return result;
                }
                catch
                {
                    _ = TerminateProcess(processHandle, 70);
                    processHandle.Dispose();
                    threadHandle.Dispose();
                    throw;
                }
            }
            finally
            {
                if (attributeList != IntPtr.Zero)
                {
                    DeleteProcThreadAttributeList(attributeList);
                    Marshal.FreeHGlobal(attributeList);
                }
                if (environmentPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(environmentPointer);
                }
                if (handlesPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(handlesPointer);
                }
                if (securityCapabilitiesPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(securityCapabilitiesPointer);
                }
                Marshal.FreeHGlobal(sidPointer);
            }
        }
        finally
        {
            if (!launched)
            {
                stdinRead.Dispose();
                stdinWrite.Dispose();
                stdoutRead.Dispose();
                stdoutWrite.Dispose();
                stderrRead.Dispose();
                stderrWrite.Dispose();
            }
        }
    }

    internal void Resume()
    {
        if (_resumed)
        {
            throw new InvalidOperationException("The AppContainer process is already running.");
        }
        var thread = _threadHandle
            ?? throw new ObjectDisposedException(nameof(WindowsAppContainerProcess));
        if (ResumeThread(thread) == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _resumed = true;
        thread.Dispose();
        _threadHandle = null;
    }

    internal Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    internal void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The enclosing Job Object remains the final process-tree boundary.
        }
    }

    public void Dispose()
    {
        StandardInput.Dispose();
        StandardOutput.Dispose();
        StandardError.Dispose();
        _threadHandle?.Dispose();
        _processHandle?.Dispose();
        _threadHandle = null;
        _processHandle = null;
        _process.Dispose();
    }

    private static void MakeNonInheritable(SafeFileHandle handle)
    {
        if (!SetHandleInformation(handle, HandleFlagInherit, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static IntPtr AllocateEnvironment(IReadOnlyDictionary<string, string> environment)
    {
        var entries = environment
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                if (
                    string.IsNullOrWhiteSpace(item.Key)
                    || item.Key.Contains('=')
                    || item.Key.Contains('\0')
                    || item.Value.Contains('\0')
                )
                {
                    throw new ArgumentException("The AppContainer environment is invalid.");
                }
                return item.Key + "=" + item.Value;
            });
        var block = string.Join('\0', entries) + "\0\0";
        return Marshal.StringToHGlobalUni(block);
    }

    private static string BuildCommandLine(OcrProviderHostCommand command)
    {
        var arguments = new List<string> { command.ExecutablePath };
        if (command.PassAssemblyAsArgument)
        {
            arguments.Add(command.AssemblyIdentityPath);
        }
        arguments.Add(command.InternalArgument);
        return string.Join(' ', arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }

        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(character);
        }
        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        internal IntPtr AppContainerSid;
        internal IntPtr Capabilities;
        internal uint CapabilityCount;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal int Size;
        internal string? Reserved;
        internal string? Desktop;
        internal string? Title;
        internal int X;
        internal int Y;
        internal int XSize;
        internal int YSize;
        internal int XCountChars;
        internal int YCountChars;
        internal int FillAttribute;
        internal uint Flags;
        internal short ShowWindow;
        internal short ReservedSize;
        internal IntPtr ReservedPointer;
        internal IntPtr StandardInput;
        internal IntPtr StandardOutput;
        internal IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        internal StartupInfo StartupInfo;
        internal IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal IntPtr Process;
        internal IntPtr Thread;
        internal uint ProcessId;
        internal uint ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        ref SecurityAttributes pipeAttributes,
        uint size
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(
        SafeFileHandle handle,
        uint mask,
        uint flags
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        uint flags,
        ref nuint size
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        nuint attribute,
        IntPtr value,
        nuint size,
        IntPtr previousValue,
        IntPtr returnSize
    );

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeFileHandle thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
}
