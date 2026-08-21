using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WordToolkit.Engine.Operations;

namespace WordToolkit.LibreOffice;

internal sealed record LibreOfficeProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    int MaximumOutputCharacters
);

internal sealed record LibreOfficeProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated = false,
    bool StandardErrorTruncated = false,
    bool TimedOut = false
);

internal interface ILibreOfficeProcessRunner
{
    Task<LibreOfficeProcessResult> RunAsync(
        LibreOfficeProcessRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed class LibreOfficeProcessRunner : ILibreOfficeProcessRunner
{
    public async Task<LibreOfficeProcessResult> RunAsync(
        LibreOfficeProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(request.ExecutablePath)!,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The external process did not start.");
        }
        process.StandardInput.Close();
        var stdoutTask = ReadBoundedAsync(
            process.StandardOutput,
            request.MaximumOutputCharacters
        );
        var stderrTask = ReadBoundedAsync(
            process.StandardError,
            request.MaximumOutputCharacters
        );
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token
        );

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested
        )
        {
            TryKill(process);
            var timedOutOutput = await AwaitReaderAfterTerminationAsync(stdoutTask)
                .ConfigureAwait(false);
            var timedOutError = await AwaitReaderAfterTerminationAsync(stderrTask)
                .ConfigureAwait(false);
            return new LibreOfficeProcessResult(
                process.HasExited ? process.ExitCode : -1,
                timedOutOutput.Text,
                timedOutError.Text,
                timedOutOutput.Truncated,
                timedOutError.Truncated,
                TimedOut: true
            );
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new LibreOfficeProcessResult(
            process.ExitCode,
            stdout.Text,
            stderr.Text,
            stdout.Truncated,
            stderr.Truncated
        );
    }

    private static async Task<BoundedText> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters
    )
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            var remaining = maximumCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
            if (read > remaining)
            {
                truncated = true;
            }
        }
        return new BoundedText(builder.ToString(), truncated);
    }

    private static async Task<BoundedText> AwaitReaderAfterTerminationAsync(
        Task<BoundedText> readerTask
    )
    {
        var completed = await Task.WhenAny(readerTask, Task.Delay(5_000))
            .ConfigureAwait(false);
        return completed == readerTask
            ? await readerTask.ConfigureAwait(false)
            : new BoundedText(string.Empty, Truncated: true);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or Win32Exception
                or NotSupportedException
        )
        {
            // The timeout result remains unqualified; the caller never treats it as success.
        }
    }

    private sealed record BoundedText(string Text, bool Truncated);
}

public sealed class LibreOfficeBackendProbeProvider : ILibreOfficeBackendProbeProvider
{
    private static readonly Regex VersionPattern = new(
        @"(?im)^\s*(LibreOffice(?:Dev)?)\s+([0-9]+(?:\.[0-9]+){1,3}(?:[.+-][0-9A-Za-z.+-]+)?)\b[^\r\n]*$",
        RegexOptions.CultureInvariant
    );
    private readonly ILibreOfficeProcessRunner _processRunner;

    public LibreOfficeBackendProbeProvider()
        : this(null)
    { }

    internal LibreOfficeBackendProbeProvider(ILibreOfficeProcessRunner? processRunner)
    {
        _processRunner = processRunner ?? new LibreOfficeProcessRunner();
    }

    public async Task<LibreOfficeBackendProbeObservation> ProbeAsync(
        LibreOfficeBackendProbeProviderRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProviderRequest(request);
        var path = ResolveExecutable(request.ExecutablePath, request.MaximumExecutableBytes);
        var before = ReadIdentity(path, request.MaximumExecutableBytes, cancellationToken);
        if (request.ExpectedExecutableSha256 is not null
            && !string.Equals(
                request.ExpectedExecutableSha256,
                before.Sha256,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw Error(
                "EXECUTABLE_MISMATCH",
                "The LibreOffice executable does not match the expected SHA-256"
            );
        }

        LibreOfficeProcessResult process;
        try
        {
            process = await _processRunner.RunAsync(
                    new LibreOfficeProcessRequest(
                        path,
                        ["--version"],
                        TimeSpan.FromMilliseconds(request.TimeoutMilliseconds),
                        request.MaximumProcessOutputCharacters
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            throw Error("NOT_FOUND", "The LibreOffice executable could not be started", exception);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            throw Error("ACCESS_DENIED", "Access to the LibreOffice executable was denied", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Error("ACCESS_DENIED", "Access to the LibreOffice executable was denied", exception);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException
        )
        {
            throw Error(
                "BACKEND_UNAVAILABLE",
                "The LibreOffice version process could not be started",
                exception,
                retryable: true
            );
        }

        if (process.TimedOut)
        {
            throw Error(
                "BACKEND_TIMEOUT",
                "The LibreOffice version probe exceeded the configured timeout",
                retryable: true
            );
        }
        if (process.StandardOutputTruncated || process.StandardErrorTruncated)
        {
            throw Error(
                "OUTPUT_LIMIT",
                "The LibreOffice version output exceeded the configured limit"
            );
        }
        if (process.ExitCode != 0)
        {
            throw Error(
                "BACKEND_UNAVAILABLE",
                "The LibreOffice version process returned a non-zero exit code",
                details: new { exit_code = process.ExitCode },
                retryable: true
            );
        }

        var combined = string.Concat(process.StandardOutput, "\n", process.StandardError);
        var match = VersionPattern.Match(combined);
        if (!match.Success)
        {
            throw Error(
                "INVALID_BACKEND",
                "The executable did not return a recognizable LibreOffice version banner"
            );
        }

        ExecutableIdentity after;
        try
        {
            after = ReadIdentity(path, request.MaximumExecutableBytes, cancellationToken);
        }
        catch (WordToolkitOperationException exception)
        {
            throw Error(
                "EXECUTABLE_DRIFT",
                "The LibreOffice executable became unavailable during the probe",
                exception,
                retryable: true
            );
        }
        if (before.Bytes != after.Bytes
            || !string.Equals(before.Sha256, after.Sha256, StringComparison.Ordinal))
        {
            throw Error(
                "EXECUTABLE_DRIFT",
                "The LibreOffice executable changed during the probe",
                retryable: true
            );
        }

        return new LibreOfficeBackendProbeObservation(
            match.Groups[1].Value,
            match.Groups[2].Value,
            string.Concat(match.Groups[1].Value, " ", match.Groups[2].Value),
            Path.GetFileName(path),
            before.Bytes,
            before.Sha256,
            ExecutableHashStable: true,
            HostOperatingSystem(),
            ArchitectureName(RuntimeInformation.OSArchitecture),
            ArchitectureName(RuntimeInformation.ProcessArchitecture)
        );
    }

    private static void ValidateProviderRequest(
        LibreOfficeBackendProbeProviderRequest request
    )
    {
        if (request.TimeoutMilliseconds
                is < LibreOfficeBackendProbeContract.MinimumTimeoutMilliseconds
                or > LibreOfficeBackendProbeContract.MaximumTimeoutMilliseconds
            || request.MaximumExecutableBytes < 1
            || request.MaximumExecutableBytes
                > LibreOfficeBackendProbeContract.MaximumExecutableBytes
            || request.MaximumProcessOutputCharacters < 1_024
            || request.MaximumProcessOutputCharacters
                > LibreOfficeBackendProbeContract.MaximumProcessOutputCharacters
            || (request.ExpectedExecutableSha256 is not null
                && (request.ExpectedExecutableSha256.Length != 64
                    || !request.ExpectedExecutableSha256.All(Uri.IsHexDigit))))
        {
            throw Error("INVALID_INPUT", "The LibreOffice probe provider request is invalid");
        }
    }

    private static string ResolveExecutable(string configured, long maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(configured)
            || !Path.IsPathFullyQualified(configured)
            || configured.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw Error(
                "INVALID_INPUT",
                "The LibreOffice executable path must be explicit, absolute and bounded"
            );
        }
        if (System.OperatingSystem.IsWindows()
            && (configured.StartsWith(@"\\?\", StringComparison.Ordinal)
                || configured.StartsWith(@"\\.\", StringComparison.Ordinal)
                || configured.StartsWith(@"\\", StringComparison.Ordinal)))
        {
            throw Error(
                "INVALID_INPUT",
                "Device-namespace and network executable paths are forbidden"
            );
        }

        string path;
        try
        {
            path = Path.GetFullPath(configured);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw Error("INVALID_INPUT", "The LibreOffice executable path is invalid", exception);
        }
        if (System.OperatingSystem.IsWindows())
        {
            if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw Error(
                    "INVALID_INPUT",
                    "The Windows LibreOffice executable must be an .exe file"
                );
            }
            try
            {
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrWhiteSpace(root)
                    && new DriveInfo(root).DriveType == DriveType.Network)
                {
                    throw Error(
                        "INVALID_INPUT",
                        "Mapped-network executable paths are forbidden"
                    );
                }
            }
            catch (WordToolkitOperationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Error("ACCESS_DENIED", "The executable drive could not be inspected", exception);
            }
        }
        if (!File.Exists(path))
        {
            throw Error("NOT_FOUND", "The configured LibreOffice executable does not exist");
        }
        EnsureNoReparsePoints(path);
        var length = FileLength(path);
        if (length < 1 || length > maximumBytes)
        {
            throw Error(
                "INVALID_INPUT",
                "The LibreOffice executable size is outside the configured limit"
            );
        }
        return path;
    }

    private static void EnsureNoReparsePoints(string path)
    {
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null)
        {
            try
            {
                current.Refresh();
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw Error(
                        "INVALID_INPUT",
                        "Reparse-point executable paths are forbidden"
                    );
                }
            }
            catch (WordToolkitOperationException)
            {
                throw;
            }
            catch (FileNotFoundException exception)
            {
                throw Error("NOT_FOUND", "The LibreOffice executable path changed", exception);
            }
            catch (DirectoryNotFoundException exception)
            {
                throw Error("NOT_FOUND", "The LibreOffice executable path changed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw Error("ACCESS_DENIED", "The executable path could not be inspected", exception);
            }
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null,
            };
        }
    }

    private static ExecutableIdentity ReadIdentity(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        var bytes = FileLength(path);
        if (bytes < 1 || bytes > maximumBytes)
        {
            throw Error(
                "INVALID_INPUT",
                "The LibreOffice executable size is outside the configured limit"
            );
        }
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                FileOptions.SequentialScan
            );
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }
                hash.AppendData(buffer, 0, read);
            }
            return new ExecutableIdentity(
                bytes,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw Error("NOT_FOUND", "The LibreOffice executable does not exist", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw Error("NOT_FOUND", "The LibreOffice executable directory does not exist", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Error("ACCESS_DENIED", "The LibreOffice executable cannot be read", exception);
        }
        catch (IOException exception)
        {
            throw Error("IO_ERROR", "The LibreOffice executable could not be hashed", exception, true);
        }
    }

    private static long FileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (FileNotFoundException exception)
        {
            throw Error("NOT_FOUND", "The LibreOffice executable does not exist", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw Error("NOT_FOUND", "The LibreOffice executable directory does not exist", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Error("ACCESS_DENIED", "The LibreOffice executable cannot be inspected", exception);
        }
        catch (IOException exception)
        {
            throw Error("IO_ERROR", "The LibreOffice executable could not be inspected", exception, true);
        }
    }

    private static string HostOperatingSystem() =>
        System.OperatingSystem.IsWindows() ? "windows"
        : System.OperatingSystem.IsLinux() ? "linux"
        : System.OperatingSystem.IsMacOS() ? "macos"
        : "other";

    private static string ArchitectureName(Architecture architecture) =>
        architecture.ToString().ToLowerInvariant();

    private static WordToolkitOperationException Error(
        string code,
        string message,
        Exception? innerException = null,
        bool retryable = false,
        object? details = null
    ) => new(
        code,
        message,
        retryable: retryable,
        innerException: innerException,
        details: details
    );

    private sealed record ExecutableIdentity(long Bytes, string Sha256);
}
