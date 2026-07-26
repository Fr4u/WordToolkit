using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WordToolkit.Native.Ocr;

internal sealed record WindowsAppContainerProfile(
    string Name,
    SecurityIdentifier Sid,
    string SidValue,
    string FolderPath
)
{
    internal const string OcrProviderProfileName = "WordToolkit.OcrProviderHost.v1";
    private const int ErrorAlreadyExistsHResult = unchecked((int)0x800700B7);

    internal static WindowsAppContainerProfile CreateOrOpenOcrProviderProfile()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("AppContainer requires Windows.");
        }

        IntPtr sidPointer = IntPtr.Zero;
        var result = CreateAppContainerProfile(
            OcrProviderProfileName,
            "WordToolkit OCR provider host",
            "Network-denied AppContainer for the WordToolkit OCR provider process tree.",
            IntPtr.Zero,
            0,
            out sidPointer
        );
        if (result == ErrorAlreadyExistsHResult)
        {
            result = DeriveAppContainerSidFromAppContainerName(
                OcrProviderProfileName,
                out sidPointer
            );
        }
        if (result < 0 || sidPointer == IntPtr.Zero)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        try
        {
            var sid = new SecurityIdentifier(sidPointer);
            if (!ConvertSidToStringSid(sidPointer, out var sidStringPointer))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            string sidValue;
            try
            {
                sidValue = Marshal.PtrToStringUni(sidStringPointer)
                    ?? throw new InvalidOperationException("AppContainer SID conversion failed.");
            }
            finally
            {
                _ = LocalFree(sidStringPointer);
            }

            result = GetAppContainerFolderPath(sidValue, out var folderPointer);
            if (result < 0 || folderPointer == IntPtr.Zero)
            {
                Marshal.ThrowExceptionForHR(result);
            }
            string folderPath;
            try
            {
                folderPath = Marshal.PtrToStringUni(folderPointer)
                    ?? throw new InvalidOperationException("AppContainer folder lookup failed.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(folderPointer);
            }

            var fullFolderPath = Path.GetFullPath(folderPath);
            Directory.CreateDirectory(fullFolderPath);
            Directory.CreateDirectory(Path.Combine(fullFolderPath, "Temp"));
            return new WindowsAppContainerProfile(
                OcrProviderProfileName,
                sid,
                sidValue,
                fullFolderPath
            );
        }
        finally
        {
            _ = FreeSid(sidPointer);
        }
    }

    internal void GrantReadExecuteToDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var target = new DirectoryInfo(Path.GetFullPath(path));
        if (!target.Exists)
        {
            throw new DirectoryNotFoundException("The AppContainer read grant target is unavailable.");
        }

        GrantTarget(target);
    }

    private void GrantTarget(DirectoryInfo directory)
    {
        var security = directory.GetAccessControl(AccessControlSections.Access);
        security.SetAccessRule(new FileSystemAccessRule(
            Sid,
            FileSystemRights.ReadAndExecute
                | FileSystemRights.ReadAttributes
                | FileSystemRights.ReadExtendedAttributes
                | FileSystemRights.ReadPermissions
                | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow
        ));
        directory.SetAccessControl(security);
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateAppContainerProfile(
        string appContainerName,
        string displayName,
        string description,
        IntPtr capabilities,
        uint capabilityCount,
        out IntPtr appContainerSid
    );

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(
        string appContainerName,
        out IntPtr appContainerSid
    );

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int GetAppContainerFolderPath(
        string appContainerSid,
        out IntPtr path
    );

    [DllImport(
        "advapi32.dll",
        EntryPoint = "ConvertSidToStringSidW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr sid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
