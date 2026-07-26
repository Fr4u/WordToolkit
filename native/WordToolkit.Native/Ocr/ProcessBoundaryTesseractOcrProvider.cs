using System.Diagnostics;
using System.Reflection;
using System.Text;
using WordToolkit.Engine.Extensions;

namespace WordToolkit.Native.Ocr;

internal sealed class ProcessBoundaryTesseractOcrProvider
    : IWordOcrProvider,
        IWordToolkitProcessBoundaryProxy
{
    internal const long MaximumProcessMemoryBytes = 1024L * 1024 * 1024;
    internal const uint MaximumActiveProcesses = 3;
    private const int StartupAndShutdownMilliseconds = 5000;
    private readonly IOcrProviderHostClient _client;

    internal ProcessBoundaryTesseractOcrProvider()
        : this(new OcrProviderProcessHostClient()) { }

    internal ProcessBoundaryTesseractOcrProvider(IOcrProviderHostClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public WordOcrProviderResult Recognize(
        WordOcrProviderRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return _client.Invoke(
            request,
            MaximumProcessMemoryBytes,
            MaximumActiveProcesses,
            checked(request.TimeoutMilliseconds + StartupAndShutdownMilliseconds),
            cancellationToken
        );
    }
}

internal interface IOcrProviderHostClient
{
    WordOcrProviderResult Invoke(
        WordOcrProviderRequest request,
        long maximumProcessMemoryBytes,
        uint maximumActiveProcesses,
        int timeoutMilliseconds,
        CancellationToken cancellationToken
    );
}

internal sealed class OcrProviderProcessHostClient : IOcrProviderHostClient
{
    private readonly OcrProviderHostCommand _command;

    internal OcrProviderProcessHostClient()
        : this(OcrProviderHostCommand.Current()) { }

    internal OcrProviderProcessHostClient(OcrProviderHostCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _command = command;
    }

    public WordOcrProviderResult Invoke(
        WordOcrProviderRequest request,
        long maximumProcessMemoryBytes,
        uint maximumActiveProcesses,
        int timeoutMilliseconds,
        CancellationToken cancellationToken
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Error(
                "EXTENSION_ISOLATION_UNAVAILABLE",
                "The OCR provider process boundary requires Windows Job Object enforcement."
            );
        }
        if (
            maximumProcessMemoryBytes < 1
            || maximumActiveProcesses < 1
            || timeoutMilliseconds < 1
        )
        {
            throw new ArgumentOutOfRangeException(nameof(maximumProcessMemoryBytes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var identity = OcrProviderHostIdentityResolver.ForPaths(
            _command.ExecutablePath,
            _command.AssemblyIdentityPath,
            cancellationToken
        );
        var requestId = OcrProviderHostProtocol.NewRequestId();
        var requestJson = OcrProviderHostProtocol.SerializeRequest(
            request,
            requestId,
            identity
        );
        using var process = new Process { StartInfo = StartInfo(_command) };
        using var job = WindowsJobObject.Create(
            maximumProcessMemoryBytes,
            maximumActiveProcesses
        );
        var processStarted = false;
        try
        {
            if (!process.Start())
            {
                throw Error(
                    "EXTENSION_START_FAILED",
                    "The OCR process host could not be started."
                );
            }
            processStarted = true;
            try
            {
                job.Attach(process);
            }
            catch (Exception exception)
            {
                TryKill(process, job);
                throw Error(
                    "EXTENSION_ISOLATION_FAILED",
                    "The OCR process host could not be attached to its Job Object boundary.",
                    innerException: exception
                );
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeout.CancelAfter(timeoutMilliseconds);
            var stdout = ReadBoundedAsync(
                process.StandardOutput,
                OcrProviderHostProtocol.MaximumResponseCharacters,
                timeout.Token
            );
            var stderr = ReadBoundedAsync(
                process.StandardError,
                OcrProviderHostProtocol.MaximumDiagnosticCharacters,
                timeout.Token
            );
            try
            {
                process.StandardInput.WriteAsync(
                    requestJson.AsMemory(),
                    timeout.Token
                ).GetAwaiter().GetResult();
                process.StandardInput.FlushAsync(timeout.Token).GetAwaiter().GetResult();
                process.StandardInput.Close();
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            )
            {
                TryKill(process, job);
                throw Error(
                    "EXTENSION_TIMEOUT",
                    "The OCR process host exceeded its hard timeout and its process tree was terminated.",
                    retryable: true
                );
            }
            catch (OperationCanceledException)
            {
                TryKill(process, job);
                throw;
            }
            catch (WordToolkitExtensionException)
            {
                TryKill(process, job);
                throw;
            }
            catch (Exception exception)
            {
                TryKill(process, job);
                throw Error(
                    "EXTENSION_EXECUTION_FAILED",
                    "The OCR process host failed before publishing a valid response.",
                    innerException: exception
                );
            }

            var response = OcrProviderHostProtocol.ParseResponse(
                stdout.Result.Trim(),
                requestId
            );
            if (response.Ok && process.ExitCode != 0)
            {
                throw Error(
                    "EXTENSION_PROTOCOL_VIOLATION",
                    "The OCR process host returned success with a failing exit code."
                );
            }
            if (!response.Ok)
            {
                if (process.ExitCode == 0)
                {
                    throw Error(
                        "EXTENSION_PROTOCOL_VIOLATION",
                        "The OCR process host returned an error with a successful exit code."
                    );
                }
                throw Error(
                    response.ErrorCode ?? "EXTENSION_EXECUTION_FAILED",
                    "The isolated OCR provider rejected the request without publishing implementation details.",
                    response.Retryable
                );
            }
            if (response.Result is null)
            {
                throw Error(
                    "EXTENSION_PROTOCOL_VIOLATION",
                    "The OCR process host omitted its typed result."
                );
            }

            var after = OcrProviderHostIdentityResolver.ForPaths(
                _command.ExecutablePath,
                _command.AssemblyIdentityPath,
                cancellationToken
            );
            if (after != identity)
            {
                throw Error(
                    "EXTENSION_IDENTITY_MISMATCH",
                    "The OCR process-host executable identity changed during invocation.",
                    retryable: true
                );
            }
            return response.Result;
        }
        finally
        {
            if (processStarted && !process.HasExited)
            {
                TryKill(process, job);
            }
        }
    }

    private static ProcessStartInfo StartInfo(OcrProviderHostCommand command)
    {
        var start = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        if (command.PassAssemblyAsArgument)
        {
            start.ArgumentList.Add(command.AssemblyIdentityPath);
        }
        start.ArgumentList.Add("--internal-ocr-provider-host");
        MinimizeEnvironment(start);
        return start;
    }

    private static void MinimizeEnvironment(ProcessStartInfo start)
    {
        var inherited = start.Environment.ToArray();
        start.Environment.Clear();
        foreach (var name in new[]
        {
            "SystemRoot",
            "WINDIR",
            "TEMP",
            "TMP",
            "DOTNET_ROOT",
            "DOTNET_ROOT(x86)",
        })
        {
            var value = inherited.FirstOrDefault(pair =>
                string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)
            ).Value;
            if (!string.IsNullOrEmpty(value))
            {
                start.Environment[name] = value;
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken
    )
    {
        var result = new StringBuilder(Math.Min(maximumCharacters, 64 * 1024));
        var buffer = new char[16 * 1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (result.Length > maximumCharacters - read)
            {
                throw Error(
                    "EXTENSION_LIMIT_EXCEEDED",
                    "The OCR process host exceeded its bounded output channel."
                );
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private static void TryKill(Process process, WindowsJobObject job)
    {
        job.Terminate();
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Job close remains the final process-tree kill boundary.
        }
    }

    private static WordToolkitExtensionException Error(
        string code,
        string message,
        bool retryable = false,
        Exception? innerException = null
    ) => new(code, message, retryable, innerException);
}

internal sealed record OcrProviderHostCommand(
    string ExecutablePath,
    string AssemblyIdentityPath,
    bool PassAssemblyAsArgument
)
{
    internal static OcrProviderHostCommand Current()
    {
        var executablePath = Environment.ProcessPath;
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_IDENTITY_UNAVAILABLE",
                "The OCR process host executable identity is unavailable."
            );
        }
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (
            string.Equals(
                executableName,
                "wordtoolkit-native",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new OcrProviderHostCommand(
                executablePath,
                assemblyPath,
                PassAssemblyAsArgument: false
            );
        }
        if (string.Equals(executableName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return new OcrProviderHostCommand(
                executablePath,
                assemblyPath,
                PassAssemblyAsArgument: true
            );
        }
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (
            !string.IsNullOrWhiteSpace(dotnetHostPath)
            && Path.IsPathFullyQualified(dotnetHostPath)
            && File.Exists(dotnetHostPath)
        )
        {
            return new OcrProviderHostCommand(
                Path.GetFullPath(dotnetHostPath),
                assemblyPath,
                PassAssemblyAsArgument: true
            );
        }
        throw new WordToolkitExtensionException(
            "EXTENSION_ISOLATION_UNAVAILABLE",
            "The current host is not the WordToolkit executable; an explicit process-host command is required."
        );
    }
}
