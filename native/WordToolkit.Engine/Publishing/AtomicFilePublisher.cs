using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WordToolkit.Engine.Publishing;

/// <summary>Publishes a staged file as a new directory entry without overwriting an existing file.</summary>
public static class AtomicFilePublisher
{
    public static void PublishCreateNew(string stagedPath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(stagedPath) || string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Staged and destination paths are required.");

        var staged = Path.GetFullPath(stagedPath);
        var destination = Path.GetFullPath(destinationPath);
        if (!File.Exists(staged))
            throw new FileNotFoundException("The staged file does not exist.", staged);
        var stagedDirectory = Path.GetDirectoryName(staged)!;
        var destinationDirectory = Path.GetDirectoryName(destination)!;
        if (!string.Equals(stagedDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Atomic create-new publication requires staged and destination files in the same directory.");

        int error;
        if (OperatingSystem.IsWindows())
        {
            if (CreateHardLinkWindows(destination, staged, IntPtr.Zero)) return;
            error = Marshal.GetLastWin32Error();
        }
        else
        {
            if (CreateHardLinkUnix(staged, destination) == 0) return;
            error = Marshal.GetLastWin32Error();
        }
        throw new IOException("Atomic create-new file publication failed.", new Win32Exception(error));
    }

    public static bool IsAlreadyExists(IOException exception)
        => (exception.InnerException as Win32Exception)?.NativeErrorCode is 17 or 80 or 183;

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string newFileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix(string existingPath, string newPath);
}
