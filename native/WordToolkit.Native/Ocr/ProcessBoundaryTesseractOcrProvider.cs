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
        var boundRequest = BindProviderConfiguration(request);
        var identity = OcrProviderHostIdentityResolver.ForPaths(
            _command.ExecutablePath,
            _command.AssemblyIdentityPath,
            cancellationToken
        );
        var requestId = OcrProviderHostProtocol.NewRequestId();
        var requestJson = OcrProviderHostProtocol.SerializeRequest(
            boundRequest,
            requestId,
            identity
        );
        using var job = WindowsJobObject.Create(
            maximumProcessMemoryBytes,
            maximumActiveProcesses
        );
        using var process = StartIsolatedProcess(boundRequest);
        try
        {
            try
            {
                job.Attach(process.ProcessHandle);
                process.Resume();
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
            if (!process.HasExited)
            {
                TryKill(process, job);
            }
        }
    }

    private WindowsAppContainerProcess StartIsolatedProcess(
        WordOcrProviderRequest request
    )
    {
        WindowsAppContainerProfile profile;
        try
        {
            profile = WindowsAppContainerProfile.CreateOrOpenOcrProviderProfile();
        }
        catch (Exception exception)
        {
            throw Error(
                "EXTENSION_SANDBOX_PROFILE_FAILED",
                "The OCR AppContainer profile could not be created or opened.",
                innerException: exception
            );
        }

        try
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetDirectoryName(_command.ExecutablePath)!,
                Path.GetDirectoryName(_command.AssemblyIdentityPath)!,
                Path.GetDirectoryName(request.Configuration.ExecutablePath!)!,
                request.Configuration.ModelDirectory!,
            };
            foreach (var directory in directories)
            {
                profile.GrantReadExecuteToDirectory(directory);
            }
        }
        catch (Exception exception)
        {
            throw Error(
                "EXTENSION_SANDBOX_BROKER_FAILED",
                "The OCR AppContainer read-only resource grants could not be established.",
                innerException: exception
            );
        }

        try
        {
            var workingDirectory = Path.Combine(profile.FolderPath, "Temp");
            return WindowsAppContainerProcess.LaunchSuspended(
                _command,
                profile,
                AppContainerEnvironment(profile, _command),
                workingDirectory
            );
        }
        catch (Exception exception)
        {
            throw Error(
                "EXTENSION_START_FAILED",
                "The OCR process host could not be started inside its AppContainer boundary.",
                innerException: exception
            );
        }
    }

    private static WordOcrProviderRequest BindProviderConfiguration(
        WordOcrProviderRequest request
    )
    {
        var executable = ResolveProviderPath(
            request.Configuration.ExecutablePath,
            TesseractCliOcrProvider.ExecutableEnvironmentVariable,
            expectDirectory: false
        );
        var models = ResolveProviderPath(
            request.Configuration.ModelDirectory,
            TesseractCliOcrProvider.ModelDirectoryEnvironmentVariable,
            expectDirectory: true
        );
        return request with
        {
            Configuration = new WordOcrProviderConfiguration(executable, models),
        };
    }

    private static string ResolveProviderPath(
        string? configured,
        string environmentVariable,
        bool expectDirectory
    )
    {
        var value = string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable(environmentVariable)
            : configured;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "The OCR provider requires explicit absolute executable and model paths."
            );
        }
        var fullPath = Path.GetFullPath(value);
        if (expectDirectory ? !Directory.Exists(fullPath) : !File.Exists(fullPath))
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "The configured OCR provider resource is unavailable."
            );
        }
        VerifyNoReparsePoints(fullPath);
        return fullPath;
    }

    private static void VerifyNoReparsePoints(string path)
    {
        var root = Path.GetPathRoot(path);
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw Error(
                    "OCR_PROVIDER_UNAVAILABLE",
                    "OCR provider paths cannot contain symbolic links or reparse points."
                );
            }
            if (string.Equals(
                Path.TrimEndingDirectorySeparator(current),
                Path.TrimEndingDirectorySeparator(root ?? current),
                StringComparison.OrdinalIgnoreCase
            ))
            {
                break;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> AppContainerEnvironment(
        WindowsAppContainerProfile profile,
        OcrProviderHostCommand command
    )
    {
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot")
            ?? Environment.GetEnvironmentVariable("WINDIR")
            ?? throw Error(
                "EXTENSION_ISOLATION_FAILED",
                "The Windows system directory is unavailable for the OCR AppContainer."
            );
        var temporaryDirectory = Path.Combine(profile.FolderPath, "Temp");
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = systemRoot,
            ["WINDIR"] = systemRoot,
            ["LOCALAPPDATA"] = profile.FolderPath,
            ["TEMP"] = temporaryDirectory,
            ["TMP"] = temporaryDirectory,
        };
        if (command.PassAssemblyAsArgument)
        {
            environment["DOTNET_ROOT"] = Path.GetDirectoryName(command.ExecutablePath)!;
        }
        return environment;
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

    private static void TryKill(WindowsAppContainerProcess process, WindowsJobObject job)
    {
        job.Terminate();
        if (!process.HasExited)
        {
            process.Kill();
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
    bool PassAssemblyAsArgument,
    string InternalArgument = "--internal-ocr-provider-host"
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
