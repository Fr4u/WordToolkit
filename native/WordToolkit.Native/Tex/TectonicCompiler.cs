using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace WordToolkit.Native.Tex;

internal sealed record TectonicDiagnostic(
    string Severity,
    string Message,
    int? Line = null
);

internal sealed record TectonicCompilationResult(
    bool Succeeded,
    IReadOnlyList<TectonicDiagnostic> Diagnostics,
    byte[]? PdfBytes,
    string? PdfSha256,
    long PdfBytesLength,
    string ProviderVersion,
    string ProviderSha256,
    bool OnlyCachedResources,
    bool NetworkRequested
);

internal sealed class TectonicCleanupException(string message, Exception? inner = null)
    : Exception(message, inner)
{ }

internal sealed class TectonicCompiler
{
    public const int MaxSourceCharacters = 100_000;
    public const long MaxOutputBytes = 50 * 1024 * 1024;
    public const int MaxOutputFiles = 128;
    private const long MaxExecutableBytes = 256L * 1024 * 1024;
    private const int MaxDiagnosticCharacters = 4_096;

    private static readonly string[] PreservedEnvironmentVariables =
    [
        "SystemRoot",
        "WINDIR",
        "USERPROFILE",
        "APPDATA",
        "LOCALAPPDATA",
        "HOME",
        "TECTONIC_CACHE_DIR",
        "FONTCONFIG_FILE",
        "FONTCONFIG_PATH",
    ];

    public async Task<TectonicCompilationResult> CompileAsync(
        string executablePath,
        string source,
        TimeSpan timeout,
        string? expectedSha256 = null,
        bool allowNetworkResourceFetch = false,
        CancellationToken cancellationToken = default
    )
    {
        if (
            string.IsNullOrWhiteSpace(source)
            || source.Length > MaxSourceCharacters
            || source.IndexOf('\0') >= 0
        )
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromSeconds(120))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        if (expectedSha256 is not null && !IsSha256(expectedSha256))
        {
            throw new ArgumentException(
                "Expected Tectonic SHA-256 must contain exactly 64 hexadecimal characters.",
                nameof(expectedSha256)
            );
        }
        var resolvedExecutable = ValidateExecutablePath(executablePath);
        var onlyCachedResources = !allowNetworkResourceFetch;

        var root = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit_tex_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            await using var executableLease = new FileStream(
                resolvedExecutable,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            var providerHash = await Sha256StreamAsync(
                executableLease,
                cancellationToken
            );
            if (
                expectedSha256 is not null
                && !string.Equals(
                    providerHash,
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidOperationException(
                    "Tectonic executable SHA-256 does not match the expected value."
                );
            }

            var version = await ReadVersionAsync(
                resolvedExecutable,
                root,
                timeout,
                cancellationToken
            );
            var texPath = Path.Combine(root, "input.tex");
            var outputDirectory = Path.Combine(root, "out");
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(
                texPath,
                source,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            );

            var startInfo = CreateStartInfo(resolvedExecutable, root);
            startInfo.ArgumentList.Add("--untrusted");
            if (onlyCachedResources)
            {
                startInfo.ArgumentList.Add("--only-cached");
            }
            startInfo.ArgumentList.Add("--outdir");
            startInfo.ArgumentList.Add(outputDirectory);
            startInfo.ArgumentList.Add(texPath);

            using var process = new Process { StartInfo = startInfo };
            StartProcess(process);
            process.StandardInput.Close();
            var stdoutTask = ReadBoundedAsync(process.StandardOutput);
            var stderrTask = ReadBoundedAsync(process.StandardError);
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeoutCancellation.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process);
                await AwaitTerminationAndDrainAsync(process, stdoutTask, stderrTask);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                return Failure(
                    "Tectonic compilation timed out.",
                    version,
                    providerHash,
                    onlyCachedResources
                );
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            var stdout = SanitizeProviderDiagnostics(stdoutTask.Result, root);
            var stderr = SanitizeProviderDiagnostics(stderrTask.Result, root);
            var files = EnumerateOutputFiles(outputDirectory);
            if (files.Length > MaxOutputFiles)
            {
                return Failure(
                    "Tectonic output file limit was exceeded.",
                    version,
                    providerHash,
                    onlyCachedResources
                );
            }
            long totalOutputBytes = 0;
            foreach (var file in files)
            {
                totalOutputBytes = checked(totalOutputBytes + new FileInfo(file).Length);
                if (totalOutputBytes > MaxOutputBytes)
                {
                    return Failure(
                        "Tectonic output size limit was exceeded.",
                        version,
                        providerHash,
                        onlyCachedResources
                    );
                }
            }

            var pdfPath = files.SingleOrDefault(file =>
                string.Equals(
                    Path.GetExtension(file),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (process.ExitCode != 0 || pdfPath is null)
            {
                var diagnostic = CombineDiagnostics(stdout, stderr);
                return Failure(
                    diagnostic.Length == 0
                        ? "Tectonic exited without producing one PDF."
                        : diagnostic,
                    version,
                    providerHash,
                    onlyCachedResources
                );
            }

            var pdfBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
            if (!IsBoundedPdf(pdfBytes))
            {
                return Failure(
                    "Tectonic did not produce a structurally recognizable bounded PDF.",
                    version,
                    providerHash,
                    onlyCachedResources
                );
            }

            var currentProviderHash = await Sha256FileAsync(
                resolvedExecutable,
                cancellationToken
            );
            if (!string.Equals(currentProviderHash, providerHash, StringComparison.Ordinal))
            {
                return Failure(
                    "Tectonic executable changed during compilation.",
                    version,
                    providerHash,
                    onlyCachedResources
                );
            }

            var pdfHash = Convert.ToHexString(SHA256.HashData(pdfBytes))
                .ToLowerInvariant();
            return new TectonicCompilationResult(
                true,
                ParseDiagnostics(CombineDiagnostics(stdout, stderr), succeeded: true),
                pdfBytes,
                pdfHash,
                pdfBytes.LongLength,
                version,
                providerHash,
                onlyCachedResources,
                NetworkRequested: allowNetworkResourceFetch
            );
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
                if (Directory.Exists(root))
                {
                    throw new IOException("Temporary TeX directory still exists.");
                }
            }
            catch (Exception exception)
            {
                throw new TectonicCleanupException(
                    "Tectonic temporary-file cleanup could not be proven.",
                    exception
                );
            }
        }
    }

    private static async Task<string> ReadVersionAsync(
        string executablePath,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var startInfo = CreateStartInfo(executablePath, workingDirectory);
        startInfo.ArgumentList.Add("--version");
        using var process = new Process { StartInfo = startInfo };
        StartProcess(process);
        process.StandardInput.Close();
        var stdoutTask = ReadBoundedAsync(process.StandardOutput);
        var stderrTask = ReadBoundedAsync(process.StandardError);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await AwaitTerminationAndDrainAsync(process, stdoutTask, stderrTask);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            throw new TimeoutException("Tectonic version probe timed out.");
        }
        await Task.WhenAll(stdoutTask, stderrTask);
        var version = stdoutTask.Result.Trim();
        if (
            process.ExitCode != 0
            || version.Length is < 1 or > 256
            || !version.StartsWith("Tectonic ", StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                "Tectonic version probe returned an invalid identity."
            );
        }
        return version;
    }

    private static ProcessStartInfo CreateStartInfo(
        string executablePath,
        string workingDirectory
    )
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
        };
        var inherited = PreservedEnvironmentVariables
            .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
            .Where(item => !string.IsNullOrEmpty(item.Value))
            .ToArray();
        startInfo.Environment.Clear();
        foreach (var item in inherited)
        {
            startInfo.Environment[item.Name] = item.Value!;
        }
        startInfo.Environment["TEMP"] = workingDirectory;
        startInfo.Environment["TMP"] = workingDirectory;
        if (!OperatingSystem.IsWindows())
        {
            startInfo.Environment["TMPDIR"] = workingDirectory;
        }
        return startInfo;
    }

    private static void StartProcess(Process process)
    {
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Tectonic process did not start.");
            }
        }
        catch (Exception exception)
            when (exception is Win32Exception or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Tectonic process could not be started.",
                exception
            );
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[1_024];
        var output = new StringBuilder(MaxDiagnosticCharacters);
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (count == 0)
            {
                break;
            }
            if (output.Length < MaxDiagnosticCharacters)
            {
                var retained = Math.Min(
                    count,
                    MaxDiagnosticCharacters - output.Length
                );
                output.Append(buffer, 0, retained);
            }
        }
        return output.ToString();
    }

    private static async Task AwaitTerminationAndDrainAsync(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask
    )
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // The caller already requested process-tree termination.
        }
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Bounded readers retain no document source and expose no path.
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Termination uncertainty is reflected by the failed/aborted action.
        }
    }

    private static string[] EnumerateOutputFiles(string outputDirectory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
            MaxRecursionDepth = 16,
        };
        return Directory.EnumerateFiles(outputDirectory, "*", options)
            .Take(MaxOutputFiles + 1)
            .ToArray();
    }

    private static bool IsBoundedPdf(byte[] bytes)
    {
        if (
            bytes.LongLength is < 8 or > MaxOutputBytes
            || !bytes.AsSpan().StartsWith("%PDF-"u8)
        )
        {
            return false;
        }
        var tailLength = Math.Min(bytes.Length, 4_096);
        return bytes.AsSpan(bytes.Length - tailLength, tailLength)
            .IndexOf("%%EOF"u8) >= 0;
    }

    private static IReadOnlyList<TectonicDiagnostic> ParseDiagnostics(
        string text,
        bool succeeded
    ) => string.IsNullOrWhiteSpace(text)
        ? Array.Empty<TectonicDiagnostic>()
        :
        [
            new TectonicDiagnostic(
                succeeded ? "warning" : "error",
                text.Length > MaxDiagnosticCharacters
                    ? text[..MaxDiagnosticCharacters]
                    : text
            ),
        ];

    private static string CombineDiagnostics(string stdout, string stderr)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { stdout.Trim(), stderr.Trim() }.Where(value => value.Length > 0)
        );
        return combined.Length > MaxDiagnosticCharacters
            ? combined[..MaxDiagnosticCharacters]
            : combined;
    }

    private static string SanitizeProviderDiagnostics(string text, string privateRoot)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }
        var normalized = text
            .Replace(privateRoot, "<private-temp>", StringComparison.OrdinalIgnoreCase)
            .Replace(
                privateRoot.Replace('\\', '/'),
                "<private-temp>",
                StringComparison.OrdinalIgnoreCase
            );
        var output = new StringBuilder(MaxDiagnosticCharacters);
        foreach (
            var line in normalized.Split(
                ["\r\n", "\n", "\r"],
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            var trimmed = line.Trim();
            if (
                !trimmed.StartsWith("note:", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("warning:", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith(
                    "Fontconfig error:",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }
            if (output.Length > 0)
            {
                output.AppendLine();
            }
            var remaining = MaxDiagnosticCharacters - output.Length;
            if (remaining <= 0)
            {
                break;
            }
            output.Append(trimmed, 0, Math.Min(trimmed.Length, remaining));
        }
        return output.ToString();
    }

    private static TectonicCompilationResult Failure(
        string message,
        string providerVersion,
        string providerSha256,
        bool onlyCachedResources
    ) => new(
        false,
        [new TectonicDiagnostic("error", message)],
        null,
        null,
        0,
        providerVersion,
        providerSha256,
        onlyCachedResources,
        NetworkRequested: !onlyCachedResources
    );

    private static string ValidateExecutablePath(string value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > 32_767
            || value.IndexOfAny(['\r', '\n', '\0']) >= 0
            || !Path.IsPathFullyQualified(value)
        )
        {
            throw new ArgumentException(
                "Tectonic executable must be an explicit absolute local path.",
                nameof(value)
            );
        }
        var path = Path.GetFullPath(value);
        if (
            OperatingSystem.IsWindows()
            && (
                path.StartsWith("\\\\", StringComparison.Ordinal)
                || path.StartsWith("\\\\?\\", StringComparison.Ordinal)
                || path.StartsWith("\\\\.\\", StringComparison.Ordinal)
            )
        )
        {
            throw new ArgumentException(
                "Tectonic executable cannot use a UNC or device path.",
                nameof(value)
            );
        }
        if (!File.Exists(path))
        {
            throw new ArgumentException(
                "Tectonic executable does not exist.",
                nameof(value)
            );
        }
        if (OperatingSystem.IsWindows())
        {
            var root = Path.GetPathRoot(path);
            if (
                !string.IsNullOrEmpty(root)
                && new DriveInfo(root).DriveType == DriveType.Network
            )
            {
                throw new ArgumentException(
                    "Tectonic executable cannot use a mapped network drive.",
                    nameof(value)
                );
            }
        }
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null)
        {
            current.Refresh();
            if (
                (current.Attributes & FileAttributes.ReparsePoint) != 0
                || current.LinkTarget is not null
            )
            {
                throw new ArgumentException(
                    "Tectonic executable cannot traverse symbolic or reparse paths.",
                    nameof(value)
                );
            }
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null,
            };
        }
        var length = new FileInfo(path).Length;
        if (length is <= 0 or > MaxExecutableBytes)
        {
            throw new ArgumentException(
                "Tectonic executable size is invalid.",
                nameof(value)
            );
        }
        return path;
    }

    private static async Task<string> Sha256FileAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        return await Sha256StreamAsync(stream, cancellationToken);
    }

    private static async Task<string> Sha256StreamAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}
