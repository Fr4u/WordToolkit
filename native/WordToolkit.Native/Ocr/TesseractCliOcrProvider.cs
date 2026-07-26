using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Extensions;

namespace WordToolkit.Native.Ocr;

internal sealed class TesseractCliOcrProvider : IWordOcrProvider
{
    internal const string DefaultCapabilityId = "wordtoolkit.ocr.tesseract.cli";
    internal const string ExtensionId = "wordtoolkit.tesseract-cli";
    internal const string ExecutableEnvironmentVariable = "WORDTOOLKIT_TESSERACT_PATH";
    internal const string ModelDirectoryEnvironmentVariable = "WORDTOOLKIT_TESSDATA_DIR";
    private const int MaximumRows = 250_000;
    private const int MaximumWords = 100_000;
    private const int MaximumLines = 20_000;
    private static readonly IReadOnlySet<string> SupportedContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/png",
            "image/jpeg",
            "image/gif",
            "image/bmp",
            "image/tiff",
            "image/webp",
        };

    public WordOcrProviderResult Recognize(
        WordOcrProviderRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var callerCancellationToken = cancellationToken;
        using var totalTimeout = new CancellationTokenSource(
            request.TimeoutMilliseconds
        );
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            totalTimeout.Token
        );
        cancellationToken = linked.Token;
        var started = Stopwatch.GetTimestamp();
        var stage = "REQUEST";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SupportedContentTypes.Contains(request.ContentType))
            {
                throw Error(
                    "OCR_FORMAT_UNSUPPORTED",
                    "The configured OCR provider does not support this image content type."
                );
            }
            var imageHash = Sha256(request.ImageBytes.Span);
            if (!string.Equals(
                imageHash,
                request.ImageSha256,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                throw Error(
                    "OCR_INPUT_CHANGED",
                    "The OCR image bytes do not match the bound source hash."
                );
            }

            stage = "PATH";
            var executablePath = ResolveExecutable(request.Configuration.ExecutablePath);
            var modelDirectory = ResolveModelDirectory(request.Configuration.ModelDirectory);
            stage = "BINARY_HASH";
            var executableHash = HashFile(
                executablePath,
                128L * 1024 * 1024,
                cancellationToken
            );
            stage = "MODEL_HASH";
            var modelHashes = ValidateModels(
                modelDirectory,
                request.Languages,
                cancellationToken
            );
            var modelSetHash = ModelSetHash(modelHashes);
            stage = "VERSION_PROBE";
            var version = ProbeVersion(
                executablePath,
                RemainingTimeout(started, request.TimeoutMilliseconds, cancellationToken),
                cancellationToken
            );
            stage = "LANGUAGE_PROBE";
            var availableLanguages = ProbeLanguages(
                executablePath,
                modelDirectory,
                RemainingTimeout(started, request.TimeoutMilliseconds, cancellationToken),
                cancellationToken
            );
            foreach (var language in request.Languages)
            {
                if (!availableLanguages.Contains(language))
                {
                    throw Error(
                        "OCR_LANGUAGE_UNAVAILABLE",
                        "A requested OCR language is not available in the bound model directory."
                    );
                }
            }

            var arguments = new List<string>
        {
            "stdin",
            "stdout",
            "--tessdata-dir",
            modelDirectory,
            "-l",
            string.Join('+', request.Languages),
            "--psm",
            PageSegmentationMode(request.LayoutHint).ToString(CultureInfo.InvariantCulture),
            "-c",
            "tessedit_create_tsv=1",
        };
            stage = "RECOGNITION";
            var processResult = Run(
                executablePath,
                arguments,
                request.ImageBytes,
                RemainingTimeout(started, request.TimeoutMilliseconds, cancellationToken),
                request.MaximumOutputCharacters,
                cancellationToken
            );
            if (processResult.TimedOut)
            {
                throw Error(
                    "OCR_PROVIDER_TIMEOUT",
                    "The OCR provider exceeded the configured execution timeout.",
                    retryable: true
                );
            }
            if (processResult.ExitCode != 0)
            {
                throw Error(
                    "OCR_PROVIDER_FAILED",
                    "The OCR provider rejected the image or model configuration."
                );
            }
            if (processResult.StandardOutputTruncated)
            {
                throw Error(
                    "OCR_PROVIDER_OUTPUT_LIMIT",
                    "The OCR provider output exceeded the configured character limit."
                );
            }
            stage = "POST_VERIFICATION";
            var executableHashAfter = HashFile(
                executablePath,
                128L * 1024 * 1024,
                cancellationToken
            );
            var modelSetHashAfter = ModelSetHash(
                ValidateModels(modelDirectory, request.Languages, cancellationToken)
            );
            if (!string.Equals(executableHash, executableHashAfter, StringComparison.Ordinal)
                || !string.Equals(modelSetHash, modelSetHashAfter, StringComparison.Ordinal))
            {
                throw Error(
                    "OCR_PROVIDER_CHANGED",
                    "The OCR provider binary or language model changed during recognition.",
                    retryable: true
                );
            }

            var parsed = ParseTsv(processResult.StandardOutput);
            var warnings = processResult.StandardError.Length == 0
                ? Array.Empty<string>()
                : ["provider_diagnostics_redacted"];
            return new WordOcrProviderResult(
                parsed.Width,
                parsed.Height,
                parsed.Text,
                parsed.Lines,
                warnings,
                new WordOcrProviderProvenance(
                    "tesseract-cli",
                    version,
                    executableHash,
                    modelSetHash,
                    request.Languages.ToArray(),
                    "normalized_0_to_1_from_tesseract_tsv_0_to_100",
                    NetworkUsed: false,
                    DeterministicForBoundInputs: false
                )
            );
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Error(
                $"OCR_PROVIDER_{stage}_ACCESS_DENIED",
                "The OCR AppContainer could not read an explicitly bound provider resource.",
                innerException: exception
            );
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw Error(
                $"OCR_PROVIDER_{stage}_START_FAILED",
                "The OCR AppContainer could not start an explicitly bound provider process.",
                innerException: exception
            );
        }
        catch (OperationCanceledException) when (
            totalTimeout.IsCancellationRequested
            && !callerCancellationToken.IsCancellationRequested
        )
        {
            throw Error(
                "OCR_PROVIDER_TIMEOUT",
                "The OCR provider exceeded the configured end-to-end timeout.",
                retryable: true
            );
        }
    }

    private static void ValidateRequest(WordOcrProviderRequest request)
    {
        if (request.ImageBytes.IsEmpty || request.ImageBytes.Length > 32 * 1024 * 1024)
        {
            throw Error(
                "OCR_INPUT_LIMIT",
                "OCR image bytes must be between 1 byte and 32 MiB."
            );
        }
        if (string.IsNullOrWhiteSpace(request.ContentType)
            || request.ContentType.Length > 128)
        {
            throw Error("OCR_INVALID_INPUT", "OCR content type is invalid.");
        }
        if (!IsSha256(request.ImageSha256))
        {
            throw Error("OCR_INVALID_INPUT", "OCR image hash is invalid.");
        }
        if (request.Languages is null || request.Languages.Count is < 1 or > 4)
        {
            throw Error(
                "OCR_INVALID_INPUT",
                "OCR requires between one and four language identifiers."
            );
        }
        foreach (var language in request.Languages)
        {
            if (!IsLanguageIdentifier(language))
            {
                throw Error(
                    "OCR_INVALID_INPUT",
                    "OCR language identifiers must be bounded safe model names."
                );
            }
        }
        if (request.Languages.Distinct(StringComparer.Ordinal).Count()
            != request.Languages.Count)
        {
            throw Error("OCR_INVALID_INPUT", "OCR language identifiers must be unique.");
        }
        if (request.TimeoutMilliseconds is < 1_000 or > 120_000)
        {
            throw Error(
                "OCR_INVALID_INPUT",
                "OCR timeout must be between 1000 and 120000 milliseconds."
            );
        }
        if (request.MaximumOutputCharacters is < 1_024 or > 4_000_000)
        {
            throw Error(
                "OCR_INVALID_INPUT",
                "OCR output limit must be between 1024 and 4000000 characters."
            );
        }
        if (request.Configuration is null)
        {
            throw Error("OCR_INVALID_INPUT", "OCR provider configuration is required.");
        }
    }

    private static string ResolveExecutable(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable)
            : configured;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                $"Configure an absolute Tesseract executable path or set {ExecutableEnvironmentVariable}."
            );
        }
        var path = ResolveExistingPath(value, expectDirectory: false);
        if (Path.GetExtension(path) is { Length: > 0 } extension
            && OperatingSystem.IsWindows()
            && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "The configured Windows OCR executable must be an .exe file."
            );
        }
        return path;
    }

    private static string ResolveModelDirectory(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable(ModelDirectoryEnvironmentVariable)
            : configured;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                $"Configure an absolute OCR model directory or set {ModelDirectoryEnvironmentVariable}."
            );
        }
        return ResolveExistingPath(value, expectDirectory: true);
    }

    private static string ResolveExistingPath(string value, bool expectDirectory)
    {
        if (value.Length > 32_767 || !Path.IsPathFullyQualified(value))
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "OCR provider paths must be absolute and bounded."
            );
        }
        var path = Path.GetFullPath(value);
        RejectNetworkPath(path);
        var exists = expectDirectory ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "A configured OCR provider path does not exist."
            );
        }
        EnsureNoReparsePoints(path);
        return path;
    }

    private static void RejectNetworkPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "OCR provider paths must resolve to the local filesystem."
            );
        }
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrWhiteSpace(root)
                && new DriveInfo(root).DriveType == DriveType.Network)
            {
                throw Error(
                    "OCR_PROVIDER_UNAVAILABLE",
                    "OCR provider paths must resolve to the local filesystem."
                );
            }
        }
        catch (WordToolkitExtensionException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException
        )
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "The OCR provider filesystem root could not be verified."
            );
        }
    }

    private static void EnsureNoReparsePoints(string path)
    {
        var root = Path.GetPathRoot(path);
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (UnauthorizedAccessException) when (
                !string.Equals(current, path, StringComparison.OrdinalIgnoreCase)
            )
            {
                // The trusted process-boundary parent already verified every ancestor
                // before granting the AppContainer the exact leaf resource. The child
                // must not require metadata access outside that read-only grant.
                break;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Error(
                    "OCR_PROVIDER_UNAVAILABLE",
                    "OCR provider paths cannot contain symbolic links or reparse points."
                );
            }
            if (string.Equals(
                Path.TrimEndingDirectorySeparator(current),
                Path.TrimEndingDirectorySeparator(root ?? current),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            ))
            {
                break;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> ValidateModels(
        string modelDirectory,
        IReadOnlyList<string> languages,
        CancellationToken cancellationToken
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var language in languages.OrderBy(item => item, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(modelDirectory, language + ".traineddata");
            var fullPath = Path.GetFullPath(path);
            if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                Path.TrimEndingDirectorySeparator(modelDirectory),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            ))
            {
                throw Error("OCR_MODEL_INVALID", "OCR model path escaped its directory.");
            }
            if (!File.Exists(fullPath)
                || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw Error(
                    "OCR_LANGUAGE_UNAVAILABLE",
                    "A requested OCR language model is missing or unsafe."
                );
            }
            var info = new FileInfo(fullPath);
            totalBytes = checked(totalBytes + info.Length);
            if (info.Length is < 1 or > 64L * 1024 * 1024
                || totalBytes > 128L * 1024 * 1024)
            {
                throw Error("OCR_MODEL_LIMIT", "OCR model bytes exceed the allowed limit.");
            }
            result.Add(language, HashFile(
                fullPath,
                64L * 1024 * 1024,
                cancellationToken
            ));
        }
        return result;
    }

    private static string ModelSetHash(IReadOnlyDictionary<string, string> hashes)
    {
        var canonical = new StringBuilder();
        foreach (var pair in hashes.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            canonical.Append(pair.Key.Length.ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(pair.Key);
            canonical.Append(pair.Value);
        }
        return Sha256(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    private static string ProbeVersion(
        string executablePath,
        int timeoutMilliseconds,
        CancellationToken cancellationToken
    )
    {
        var result = Run(
            executablePath,
            ["--version"],
            ReadOnlyMemory<byte>.Empty,
            timeoutMilliseconds,
            8_192,
            cancellationToken
        );
        if (result.TimedOut)
        {
            throw Error(
                "OCR_PROVIDER_TIMEOUT",
                "The OCR provider exceeded the configured end-to-end timeout.",
                retryable: true
            );
        }
        if (result.ExitCode != 0 || result.StandardOutputTruncated)
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "The OCR provider version probe failed."
            );
        }
        var line = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (line is null || !line.StartsWith("tesseract ", StringComparison.OrdinalIgnoreCase))
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "The configured executable did not identify as Tesseract."
            );
        }
        var version = line["tesseract ".Length..].Trim();
        if (version.Length is < 1 or > 128 || ContainsUnsafeText(version))
        {
            throw Error("OCR_PROVIDER_UNAVAILABLE", "OCR provider version is invalid.");
        }
        return version;
    }

    private static IReadOnlySet<string> ProbeLanguages(
        string executablePath,
        string modelDirectory,
        int timeoutMilliseconds,
        CancellationToken cancellationToken
    )
    {
        var result = Run(
            executablePath,
            ["--tessdata-dir", modelDirectory, "--list-langs"],
            ReadOnlyMemory<byte>.Empty,
            timeoutMilliseconds,
            32_768,
            cancellationToken
        );
        if (result.TimedOut)
        {
            throw Error(
                "OCR_PROVIDER_TIMEOUT",
                "The OCR provider exceeded the configured end-to-end timeout.",
                retryable: true
            );
        }
        if (result.ExitCode != 0 || result.StandardOutputTruncated)
        {
            throw Error(
                "OCR_PROVIDER_UNAVAILABLE",
                "The OCR provider language probe failed."
            );
        }
        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsLanguageIdentifier)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static ParsedTsv ParseTsv(string tsv)
    {
        var rows = tsv.Split('\n');
        if (rows.Length is < 2 or > MaximumRows + 1)
        {
            throw Error("OCR_OUTPUT_INVALID", "OCR TSV row count is invalid.");
        }
        var header = rows[0].TrimEnd('\r').Split('\t');
        string[] expected =
        [
            "level", "page_num", "block_num", "par_num", "line_num", "word_num",
            "left", "top", "width", "height", "conf", "text",
        ];
        if (!header.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw Error("OCR_OUTPUT_INVALID", "OCR TSV header is unsupported.");
        }

        var width = 0;
        var height = 0;
        var accumulators = new Dictionary<LineKey, LineAccumulator>();
        var order = new List<LineKey>();
        var wordCount = 0;
        for (var rowIndex = 1; rowIndex < rows.Length; rowIndex++)
        {
            var raw = rows[rowIndex].TrimEnd('\r');
            if (raw.Length == 0)
            {
                continue;
            }
            var cells = raw.Split('\t', 12, StringSplitOptions.None);
            if (cells.Length != 12)
            {
                throw Error("OCR_OUTPUT_INVALID", "OCR TSV row shape is invalid.");
            }
            var level = ParseInt(cells[0]);
            var page = ParseInt(cells[1]);
            var block = ParseInt(cells[2]);
            var paragraph = ParseInt(cells[3]);
            var line = ParseInt(cells[4]);
            var word = ParseInt(cells[5]);
            var left = ParseInt(cells[6]);
            var top = ParseInt(cells[7]);
            var boxWidth = ParseInt(cells[8]);
            var boxHeight = ParseInt(cells[9]);
            if (level is < 1 or > 5 || page < 1 || block < 0 || paragraph < 0
                || line < 0 || word < 0 || left < 0 || top < 0
                || boxWidth < 0 || boxHeight < 0)
            {
                throw Error("OCR_OUTPUT_INVALID", "OCR TSV contains invalid numeric values.");
            }
            if (level == 1)
            {
                if (width != 0 || height != 0 || boxWidth <= 0 || boxHeight <= 0)
                {
                    throw Error("OCR_OUTPUT_INVALID", "OCR TSV page geometry is invalid.");
                }
                width = boxWidth;
                height = boxHeight;
                continue;
            }
            if (level != 5)
            {
                continue;
            }
            if (width <= 0 || height <= 0 || boxWidth <= 0 || boxHeight <= 0
                || (long)left + boxWidth > width
                || (long)top + boxHeight > height)
            {
                throw Error("OCR_OUTPUT_INVALID", "OCR word geometry escapes the image bounds.");
            }
            var text = cells[11];
            if (text.Length == 0)
            {
                continue;
            }
            if (text.Length > 8_192 || ContainsUnsafeText(text))
            {
                throw Error("OCR_OUTPUT_INVALID", "OCR word text is unsafe or oversized.");
            }
            if (++wordCount > MaximumWords)
            {
                throw Error("OCR_OUTPUT_LIMIT", "OCR word count exceeds the allowed limit.");
            }
            var confidence = ParseConfidence(cells[10]);
            var key = new LineKey(page, block, paragraph, line);
            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                if (accumulators.Count >= MaximumLines)
                {
                    throw Error("OCR_OUTPUT_LIMIT", "OCR line count exceeds the allowed limit.");
                }
                accumulator = new LineAccumulator();
                accumulators.Add(key, accumulator);
                order.Add(key);
            }
            accumulator.Add(new WordOcrProviderWord(
                text,
                confidence,
                new WordOcrPixelBox(left, top, boxWidth, boxHeight)
            ));
        }
        if (width <= 0 || height <= 0)
        {
            throw Error("OCR_OUTPUT_INVALID", "OCR TSV does not contain page geometry.");
        }
        var lines = order.Select(key => accumulators[key].Build()).ToArray();
        return new ParsedTsv(
            width,
            height,
            string.Join('\n', lines.Select(item => item.Text)),
            lines
        );
    }

    private static int ParseInt(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
        {
            throw Error("OCR_OUTPUT_INVALID", "OCR TSV contains an invalid integer.");
        }
        return result;
    }

    private static double? ParseConfidence(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            || !double.IsFinite(result) || result < -1 || result > 100)
        {
            throw Error("OCR_OUTPUT_INVALID", "OCR TSV confidence is invalid.");
        }
        return result < 0 ? null : result / 100d;
    }

    private static ProcessResult Run(
        string executablePath,
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte> standardInput,
        int timeoutMilliseconds,
        int maximumOutputCharacters,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
        };
        startInfo.Environment["OMP_THREAD_LIMIT"] = "1";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw Error("OCR_PROVIDER_UNAVAILABLE", "The OCR provider process did not start.");
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, maximumOutputCharacters);
        var stderrTask = ReadBoundedAsync(process.StandardError, 32_768);
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token
        );
        try
        {
            if (!standardInput.IsEmpty)
            {
                process.StandardInput.BaseStream.WriteAsync(
                    standardInput,
                    linked.Token
                ).AsTask().GetAwaiter().GetResult();
            }
            process.StandardInput.Close();
            process.WaitForExitAsync(linked.Token).GetAwaiter().GetResult();
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            return new ProcessResult(
                process.ExitCode,
                stdout.Text,
                stderr.Text,
                stdout.Truncated,
                stderr.Truncated,
                TimedOut: false
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Terminate(process);
            var timedOutOut = stdoutTask.GetAwaiter().GetResult();
            var timedOutErr = stderrTask.GetAwaiter().GetResult();
            return new ProcessResult(
                process.HasExited ? process.ExitCode : -1,
                timedOutOut.Text,
                timedOutErr.Text,
                timedOutOut.Truncated,
                timedOutErr.Truncated,
                TimedOut: true
            );
        }
        catch
        {
            Terminate(process);
            throw;
        }
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

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5_000))
                {
                    throw Error(
                        "OCR_PROVIDER_TERMINATION_FAILED",
                        "The OCR provider process could not be terminated safely."
                    );
                }
            }
        }
        catch (InvalidOperationException)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            { }
            throw Error(
                "OCR_PROVIDER_TERMINATION_FAILED",
                "The OCR provider process termination state could not be verified."
            );
        }
        catch (WordToolkitExtensionException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or NotSupportedException
        )
        {
            throw Error(
                "OCR_PROVIDER_TERMINATION_FAILED",
                "The OCR provider process could not be terminated safely."
            );
        }
    }

    private static string HashFile(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        RejectNetworkPath(path);
        EnsureNoReparsePoints(path);
        var info = new FileInfo(path);
        if (info.Length is < 1 || info.Length > maximumBytes)
        {
            throw Error("OCR_PROVIDER_UNAVAILABLE", "OCR provider file size is invalid.");
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
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
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static int RemainingTimeout(
        long started,
        int totalMilliseconds,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var elapsed = (int)Math.Ceiling(
            Stopwatch.GetElapsedTime(started).TotalMilliseconds
        );
        var remaining = totalMilliseconds - elapsed;
        if (remaining <= 0)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        return remaining;
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(
        SHA256.HashData(bytes)
    ).ToLowerInvariant();

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiHexDigit(character));

    private static bool IsLanguageIdentifier(string? value) => value is { Length: >= 2 and <= 32 }
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-');

    private static bool ContainsUnsafeText(string value) => value.Any(character =>
        character == '\0'
        || (char.IsControl(character) && character is not '\t' and not '\r' and not '\n')
    );

    private static int PageSegmentationMode(WordOcrLayoutHint hint) => hint switch
    {
        WordOcrLayoutHint.Automatic => 3,
        WordOcrLayoutHint.SingleBlock => 6,
        WordOcrLayoutHint.SparseText => 11,
        WordOcrLayoutHint.SingleLine => 7,
        WordOcrLayoutHint.SingleWord => 8,
        _ => throw Error("OCR_INVALID_INPUT", "OCR layout hint is invalid."),
    };

    private static WordToolkitExtensionException Error(
        string code,
        string message,
        bool retryable = false,
        Exception? innerException = null
    ) => new(code, message, retryable, innerException);

    private sealed record BoundedText(string Text, bool Truncated);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool StandardOutputTruncated,
        bool StandardErrorTruncated,
        bool TimedOut
    );

    private sealed record ParsedTsv(
        int Width,
        int Height,
        string Text,
        IReadOnlyList<WordOcrProviderLine> Lines
    );

    private readonly record struct LineKey(
        int Page,
        int Block,
        int Paragraph,
        int Line
    );

    private sealed class LineAccumulator
    {
        private readonly List<WordOcrProviderWord> _words = [];

        public void Add(WordOcrProviderWord word) => _words.Add(word);

        public WordOcrProviderLine Build()
        {
            var left = _words.Min(item => item.Bounds.Left);
            var top = _words.Min(item => item.Bounds.Top);
            var right = _words.Max(item => item.Bounds.Left + item.Bounds.Width);
            var bottom = _words.Max(item => item.Bounds.Top + item.Bounds.Height);
            var confidences = _words
                .Where(item => item.Confidence.HasValue)
                .Select(item => item.Confidence!.Value)
                .ToArray();
            return new WordOcrProviderLine(
                string.Join(' ', _words.Select(item => item.Text)),
                confidences.Length == 0 ? null : confidences.Average(),
                new WordOcrPixelBox(left, top, right - left, bottom - top),
                _words.ToArray()
            );
        }
    }
}
