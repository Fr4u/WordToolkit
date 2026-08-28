using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal static class EquationPreflightProcessRunner
{
    internal const int DefaultPerEquationTimeoutSeconds = 20;
    internal const int DefaultTotalTimeoutSeconds = 120;
    private const int StartupTimeoutSeconds = 30;
    private const int CleanupTimeoutSeconds = 15;
    private const int MaximumEventCharacters = 2 * 1024 * 1024;
    private const int MaximumStderrCharacters = 8 * 1024;

    internal static async Task<object> RunAsync(
        JsonElement arguments,
        int equationCount,
        int perEquationTimeoutSeconds,
        int totalTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        var executable = ResolveExecutablePath();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.ArgumentList.Add("--internal-equation-preflight-worker");
        using var process = Process.Start(startInfo)
            ?? throw new NativeToolException(
                "EQUATION_PREFLIGHT_WORKER_FAILED",
                "WordToolkit could not start the isolated equation preflight worker",
                retryable: true
            );
        var stderrTask = ReadBoundedStderrAsync(process.StandardError);
        try
        {
            await process.StandardInput.WriteAsync(arguments.GetRawText());
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }
        catch (Exception exception)
        {
            TryTerminate(process);
            _ = await stderrTask;
            throw new NativeToolException(
                "EQUATION_PREFLIGHT_WORKER_FAILED",
                "WordToolkit could not send the request to the isolated equation worker",
                new
                {
                    stage = "write_worker_request",
                    exception_type = exception.GetType().Name,
                    target_document_mutated = false,
                    raw_document_content_returned = false,
                },
                retryable: true
            );
        }

        var started = Stopwatch.GetTimestamp();
        var equationStarted = started;
        var stageStarted = started;
        var currentIndex = -1;
        var resultItems = new JsonObject?[equationCount];
        var processedCount = 0;
        var validCount = 0;
        var invalidCount = 0;
        var conversionInvalidCount = 0;
        var workerReady = false;
        var workerFinished = false;
        var cleanupVerified = false;
        var ownedWordProcessId = 0;
        var ownedWordStartUtcTicks = 0L;
        string? workerFailureCode = null;
        var workerFailureRetryable = false;
        var workerFailureIndex = -1;
        try
        {
            while (!workerFinished)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var totalElapsed = Stopwatch.GetElapsedTime(started);
                if (totalElapsed >= TimeSpan.FromSeconds(totalTimeoutSeconds))
                {
                    throw TimeoutFailure(
                        "total_timeout",
                        currentIndex,
                        processedCount,
                        totalElapsed,
                        perEquationTimeoutSeconds,
                        totalTimeoutSeconds
                    );
                }
                var stageLimit = currentIndex >= 0
                    ? TimeSpan.FromSeconds(perEquationTimeoutSeconds)
                    : workerReady
                        ? TimeSpan.FromSeconds(CleanupTimeoutSeconds)
                        : TimeSpan.FromSeconds(StartupTimeoutSeconds);
                var stageElapsed = Stopwatch.GetElapsedTime(stageStarted);
                if (stageElapsed >= stageLimit)
                {
                    throw TimeoutFailure(
                        currentIndex >= 0 ? "equation_timeout" : workerReady
                            ? "cleanup_timeout"
                            : "startup_timeout",
                        currentIndex,
                        processedCount,
                        currentIndex >= 0
                            ? Stopwatch.GetElapsedTime(equationStarted)
                            : stageElapsed,
                        perEquationTimeoutSeconds,
                        totalTimeoutSeconds
                    );
                }

                var remainingStage = stageLimit - stageElapsed;
                var remainingTotal = TimeSpan.FromSeconds(totalTimeoutSeconds) - totalElapsed;
                var delay = remainingStage < remainingTotal ? remainingStage : remainingTotal;
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                var lineTask = process.StandardOutput.ReadLineAsync(
                    waitCancellation.Token
                ).AsTask();
                var delayTask = Task.Delay(delay, cancellationToken);
                var winner = await Task.WhenAny(lineTask, delayTask);
                if (winner != lineTask)
                {
                    waitCancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw TimeoutFailure(
                        currentIndex >= 0 ? "equation_timeout" : workerReady
                            ? "cleanup_timeout"
                            : "startup_timeout",
                        currentIndex,
                        processedCount,
                        currentIndex >= 0
                            ? Stopwatch.GetElapsedTime(equationStarted)
                            : Stopwatch.GetElapsedTime(stageStarted),
                        perEquationTimeoutSeconds,
                        totalTimeoutSeconds
                    );
                }
                var line = await lineTask;
                if (line is null)
                {
                    break;
                }
                if (line.Length is < 2 or > MaximumEventCharacters)
                {
                    throw new NativeToolException(
                        "EQUATION_PREFLIGHT_WORKER_FAILED",
                        "The isolated equation preflight worker returned an invalid event",
                        new
                        {
                            stage = "read_worker_event",
                            event_characters = line.Length,
                            equation_index = currentIndex >= 0 ? (int?)currentIndex : null,
                            completed_count = processedCount,
                            target_document_mutated = false,
                            raw_document_content_returned = false,
                        }
                    );
                }
                using var eventDocument = JsonDocument.Parse(line);
                var root = eventDocument.RootElement;
                var eventName = root.TryGetProperty("event", out var eventNode)
                    && eventNode.ValueKind == JsonValueKind.String
                        ? eventNode.GetString() ?? ""
                        : "";
                switch (eventName)
                {
                    case "ready":
                        ownedWordProcessId = root.GetProperty("word_process_id").GetInt32();
                        ownedWordStartUtcTicks = root
                            .GetProperty("word_process_start_utc_ticks")
                            .GetInt64();
                        VerifyOwnedWordProcess(
                            ownedWordProcessId,
                            ownedWordStartUtcTicks
                        );
                        workerReady = true;
                        stageStarted = Stopwatch.GetTimestamp();
                        await ToolProgressContext.ReportAsync(
                            $"Native equation preflight worker ready: 0/{equationCount} equations"
                        );
                        break;
                    case "equation_started":
                        currentIndex = root.GetProperty("equation_index").GetInt32();
                        if (currentIndex != processedCount)
                        {
                            throw InvalidSequence(currentIndex, processedCount);
                        }
                        equationStarted = Stopwatch.GetTimestamp();
                        stageStarted = equationStarted;
                        await ToolProgressContext.ReportAsync(
                            $"Validating native equation {currentIndex + 1}/{equationCount}"
                        );
                        break;
                    case "equation_completed":
                        var completedIndex = root.GetProperty("equation_index").GetInt32();
                        if (
                            completedIndex != currentIndex
                            || !root.TryGetProperty("result", out var result)
                            || result.ValueKind != JsonValueKind.Object
                        )
                        {
                            throw InvalidSequence(completedIndex, processedCount);
                        }
                        resultItems[completedIndex] = JsonNode.Parse(
                            result.GetRawText()
                        )!.AsObject();
                        processedCount++;
                        validCount++;
                        await ToolProgressContext.ReportAsync(
                            $"Validated native equation {processedCount}/{equationCount}"
                        );
                        currentIndex = -1;
                        stageStarted = Stopwatch.GetTimestamp();
                        break;
                    case "equation_failed":
                        workerFailureIndex = root.GetProperty("equation_index").GetInt32();
                        workerFailureCode = root.GetProperty("error_code").GetString()
                            ?? "EQUATION_INVALID";
                        workerFailureRetryable = root.TryGetProperty(
                            "retryable",
                            out var retryable
                        ) && retryable.ValueKind == JsonValueKind.True;
                        var failureStage = root.TryGetProperty("stage", out var stage)
                            && stage.ValueKind == JsonValueKind.String
                                ? stage.GetString() ?? "build_verified_native_equation"
                                : "build_verified_native_equation";
                        var conversionValid = root.TryGetProperty(
                            "conversion_valid",
                            out var conversionValidNode
                        )
                            ? conversionValidNode.ValueKind == JsonValueKind.True
                            : failureStage != "conversion";
                        currentIndex = -1;
                        resultItems[workerFailureIndex] = new JsonObject
                        {
                            ["index"] = workerFailureIndex,
                            ["equation_id"] = root.TryGetProperty("equation_id", out var equationId)
                                ? equationId.GetString() : $"eq-{workerFailureIndex:D4}",
                            ["valid"] = false,
                            ["conversion_valid"] = conversionValid,
                            ["native_execution_verified"] = false,
                            ["error_code"] = workerFailureCode,
                            ["stage"] = failureStage,
                            ["suggestion_code"] = root.TryGetProperty("suggestion_code", out var suggestion)
                                ? suggestion.GetString() : "RUN_CONVERSION_ONLY_FOR_SYNTAX_DIAGNOSIS",
                            ["diagnostic"] = root.TryGetProperty("diagnostic", out var diagnostic)
                                && diagnostic.ValueKind == JsonValueKind.Object
                                    ? JsonNode.Parse(diagnostic.GetRawText())
                                    : null,
                            ["expected_semantic_sha256"] = root.TryGetProperty(
                                "expected_semantic_sha256",
                                out var expectedSemantic
                            ) && expectedSemantic.ValueKind == JsonValueKind.String
                                ? expectedSemantic.GetString()
                                : null,
                            ["actual_semantic_sha256"] = root.TryGetProperty(
                                "actual_semantic_sha256",
                                out var actualSemantic
                            ) && actualSemantic.ValueKind == JsonValueKind.String
                                ? actualSemantic.GetString()
                                : null,
                            ["hresult"] = root.TryGetProperty("hresult", out var hresult)
                                && hresult.ValueKind == JsonValueKind.Number
                                && hresult.TryGetInt32(out var parsedHresult)
                                    ? parsedHresult
                                    : null,
                            ["native_exception_type"] = root.TryGetProperty(
                                "native_exception_type",
                                out var nativeExceptionType
                            ) && nativeExceptionType.ValueKind == JsonValueKind.String
                                ? nativeExceptionType.GetString()
                                : root.TryGetProperty(
                                    "exception_type",
                                    out var workerExceptionType
                                ) && workerExceptionType.ValueKind == JsonValueKind.String
                                    ? workerExceptionType.GetString()
                                    : null,
                        };
                        processedCount++;
                        invalidCount++;
                        if (!conversionValid)
                        {
                            conversionInvalidCount++;
                        }
                        workerFailureCode = null;
                        stageStarted = Stopwatch.GetTimestamp();
                        await ToolProgressContext.ReportAsync(
                            $"Native equation {workerFailureIndex + 1}/{equationCount} failed; continuing with remaining equations"
                        );
                        break;
                    case "fatal":
                        workerFailureCode = root.TryGetProperty(
                            "error_code",
                            out var fatalCode
                        ) && fatalCode.ValueKind == JsonValueKind.String
                            ? fatalCode.GetString() ?? "EQUATION_PREFLIGHT_WORKER_FAILED"
                            : "EQUATION_PREFLIGHT_WORKER_FAILED";
                        workerFailureRetryable = false;
                        workerFailureIndex = root.TryGetProperty(
                            "equation_index",
                            out var fatalIndex
                        ) && fatalIndex.TryGetInt32(out var parsedFatalIndex)
                            ? parsedFatalIndex
                            : currentIndex;
                        stageStarted = Stopwatch.GetTimestamp();
                        break;
                    case "finished":
                        workerFinished = true;
                        cleanupVerified = root.TryGetProperty(
                            "cleanup_verified",
                            out var cleanup
                        ) && cleanup.ValueKind == JsonValueKind.True;
                        break;
                    default:
                        throw new NativeToolException(
                            "EQUATION_PREFLIGHT_WORKER_FAILED",
                            "The isolated equation preflight worker returned an unknown event",
                            new
                            {
                                stage = "parse_worker_event",
                                equation_index = currentIndex >= 0 ? (int?)currentIndex : null,
                                completed_count = processedCount,
                                target_document_mutated = false,
                                raw_document_content_returned = false,
                            }
                        );
                }
            }

            if (!workerFinished)
            {
                throw new NativeToolException(
                    "EQUATION_PREFLIGHT_WORKER_FAILED",
                    "The isolated equation preflight worker exited before a final result",
                    new
                    {
                        stage = "worker_exit",
                        equation_index = currentIndex >= 0 ? (int?)currentIndex : null,
                        completed_count = processedCount,
                        target_document_mutated = false,
                        raw_document_content_returned = false,
                    },
                    retryable: true
                );
            }
            if (!cleanupVerified)
            {
                throw new NativeToolException(
                    "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                    "WordToolkit could not prove cleanup of the isolated equation preflight worker",
                    new
                    {
                        worker_process_created = true,
                        dedicated_word_process_created = workerReady,
                        completed_count = processedCount,
                        target_document_mutated = false,
                        raw_document_content_returned = false,
                    }
                );
            }
            if (workerFailureCode is not null)
            {
                throw new NativeToolException(
                    workerFailureCode,
                    "The isolated equation preflight worker failed before completing the requested batch",
                    new
                    {
                        equation_index = workerFailureIndex >= 0
                            ? (int?)workerFailureIndex
                            : null,
                        completed_count = processedCount,
                        stage = "equation_worker_infrastructure",
                        target_document_mutated = false,
                        raw_document_content_returned = false,
                    },
                    workerFailureRetryable
                );
            }
            if (
                processedCount != equationCount
                || resultItems.Any(item => item is null)
            )
            {
                throw InvalidSequence(currentIndex, processedCount);
            }

            await WaitForExitAsync(process, TimeSpan.FromSeconds(5));
            if (
                !await WaitForOwnedWordExitAsync(
                    ownedWordProcessId,
                    ownedWordStartUtcTicks,
                    TimeSpan.FromSeconds(5)
                )
            )
            {
                throw new NativeToolException(
                    "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                    "The dedicated Word preflight process remained after successful worker cleanup",
                    new
                    {
                        worker_process_exited = process.HasExited,
                        dedicated_word_process_exited = false,
                        target_document_mutated = false,
                        raw_document_content_returned = false,
                    }
                );
            }
            return new
            {
                valid = invalidCount == 0,
                valid_count = validCount,
                invalid_count = invalidCount,
                conversion_valid = conversionInvalidCount == 0,
                native_execution_verified = invalidCount == 0,
                validation_mode = "native",
                equation_count = equationCount,
                equations = resultItems.Select(item => item!).ToArray(),
                mutated_connected_document = false,
                isolation = new
                {
                    worker_process_created = true,
                    dedicated_word_process_created = true,
                    dedicated_word_process_verified = true,
                    per_equation_scratch_documents = true,
                    worker_cleanup_verified = true,
                    worker_process_exited = process.HasExited,
                    per_equation_timeout_seconds = perEquationTimeoutSeconds,
                    total_timeout_seconds = totalTimeoutSeconds,
                },
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Math.Round(
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        3
                    ),
                },
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var terminated = TerminateIsolatedWorker(
                process,
                ownedWordProcessId,
                ownedWordStartUtcTicks
            );
            if (!cleanupVerified && !terminated.WordCleanupVerified)
            {
                throw new NativeToolException(
                    "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                    "Cancellation could not prove cleanup of the dedicated Word preflight process",
                    new
                    {
                        equation_index = currentIndex >= 0 ? (int?)currentIndex : null,
                        completed_count = processedCount,
                        worker_process_terminated = terminated.WorkerTerminated,
                        dedicated_word_process_terminated = terminated.WordTerminated,
                        target_document_mutated = false,
                        raw_document_content_returned = false,
                    }
                );
            }
            throw;
        }
        catch (NativeToolException exception)
            when (exception.ErrorCode == "EQUATION_PREFLIGHT_TIMEOUT")
        {
            var terminated = TerminateIsolatedWorker(
                process,
                ownedWordProcessId,
                ownedWordStartUtcTicks
            );
            if (!terminated.WordCleanupVerified)
            {
                throw new NativeToolException(
                    "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                    "A timed-out native equation worker could not be cleaned up safely",
                    new
                    {
                        original_error_code = exception.ErrorCode,
                        equation_index = currentIndex >= 0 ? (int?)currentIndex : null,
                        completed_count = processedCount,
                        worker_process_terminated = terminated.WorkerTerminated,
                        dedicated_word_process_terminated = terminated.WordTerminated,
                        target_document_mutated = false,
                        raw_document_content_returned = false,
                    }
                );
            }
            throw new NativeToolException(
                exception.ErrorCode,
                exception.Message,
                new
                {
                    equation_index = currentIndex >= 0 ? (int?)currentIndex : null,
                    completed_count = processedCount,
                    stage = currentIndex >= 0 ? "build_verified_native_equation" : "worker_cleanup",
                    elapsed_milliseconds = Math.Max(
                        0L,
                        (long)Stopwatch.GetElapsedTime(
                            currentIndex >= 0 ? equationStarted : stageStarted
                        ).TotalMilliseconds
                    ),
                    per_equation_timeout_seconds = perEquationTimeoutSeconds,
                    total_timeout_seconds = totalTimeoutSeconds,
                    worker_process_terminated = terminated.WorkerTerminated,
                    dedicated_word_process_terminated = terminated.WordTerminated,
                    target_document_mutated = false,
                    raw_document_content_returned = false,
                },
                retryable: true
            );
        }
        catch (Exception)
        {
            var terminated = TerminateIsolatedWorker(
                process,
                ownedWordProcessId,
                ownedWordStartUtcTicks
            );
            if (!cleanupVerified && !terminated.WordCleanupVerified)
            {
                throw new NativeToolException(
                    "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                    "WordToolkit could not prove cleanup after an isolated equation worker failure",
                    new
                    {
                        equation_index = currentIndex >= 0 ? (int?)currentIndex : null,
                        completed_count = processedCount,
                        worker_process_terminated = terminated.WorkerTerminated,
                        dedicated_word_process_terminated = terminated.WordTerminated,
                        target_document_mutated = false,
                        raw_document_content_returned = false,
                    }
                );
            }
            throw;
        }
        finally
        {
            try
            {
                _ = await stderrTask;
            }
            catch
            {
                // Stderr is diagnostic only and must not replace the worker result.
            }
        }
    }

    private static string ResolveExecutablePath()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var executable = Path.Combine(
            Path.GetDirectoryName(assemblyPath) ?? "",
            "wordtoolkit-native.exe"
        );
        if (!File.Exists(executable))
        {
            throw new NativeToolException(
                "EQUATION_PREFLIGHT_WORKER_FAILED",
                "The isolated equation preflight executable is unavailable",
                new { worker_executable_present = false },
                retryable: false
            );
        }
        return executable;
    }

    private static NativeToolException TimeoutFailure(
        string stage,
        int currentIndex,
        int completedCount,
        TimeSpan elapsed,
        int perEquationTimeoutSeconds,
        int totalTimeoutSeconds
    ) => new(
        "EQUATION_PREFLIGHT_TIMEOUT",
        "Native equation preflight exceeded its isolated execution limit",
        new
        {
            equation_index = currentIndex >= 0 ? (int?)currentIndex : null,
            completed_count = completedCount,
            stage,
            elapsed_milliseconds = Math.Max(0L, (long)elapsed.TotalMilliseconds),
            per_equation_timeout_seconds = perEquationTimeoutSeconds,
            total_timeout_seconds = totalTimeoutSeconds,
            target_document_mutated = false,
            raw_document_content_returned = false,
        },
        retryable: true
    );

    private static NativeToolException InvalidSequence(int index, int completed) => new(
        "EQUATION_PREFLIGHT_WORKER_FAILED",
        "The isolated equation preflight worker returned an inconsistent sequence",
        new
        {
            equation_index = index >= 0 ? (int?)index : null,
            completed_count = completed,
            target_document_mutated = false,
            raw_document_content_returned = false,
        }
    );

    private static void VerifyOwnedWordProcess(int processId, long startUtcTicks)
    {
        using var process = Process.GetProcessById(processId);
        if (
            !string.Equals(process.ProcessName, "WINWORD", StringComparison.OrdinalIgnoreCase)
            || process.StartTime.ToUniversalTime().Ticks != startUtcTicks
        )
        {
            throw new NativeToolException(
                "EQUATION_PREFLIGHT_WORKER_FAILED",
                "The equation preflight worker could not prove ownership of its Word process",
                new { dedicated_word_process_verified = false }
            );
        }
    }

    private static TerminationResult TerminateIsolatedWorker(
        Process worker,
        int wordProcessId,
        long wordStartUtcTicks
    )
    {
        var workerTerminated = TryTerminate(worker);
        var wordTerminated = wordProcessId > 0
            && TryTerminateOwnedWord(wordProcessId, wordStartUtcTicks);
        return new TerminationResult(
            workerTerminated,
            wordTerminated,
            wordTerminated
        );
    }

    private static bool TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryTerminateOwnedWord(int processId, long startUtcTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (
                !string.Equals(process.ProcessName, "WINWORD", StringComparison.OrdinalIgnoreCase)
                || process.StartTime.ToUniversalTime().Ticks != startUtcTicks
            )
            {
                return true;
            }
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForOwnedWordExitAsync(
        int processId,
        long startUtcTicks,
        TimeSpan timeout
    )
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            if (!OwnedWordProcessStillExists(processId, startUtcTicks))
            {
                return true;
            }
            await Task.Delay(100);
        }
        return !OwnedWordProcessStillExists(processId, startUtcTicks);
    }

    private static bool OwnedWordProcessStillExists(int processId, long startUtcTicks)
    {
        if (processId <= 0)
        {
            return false;
        }
        try
        {
            using var process = Process.GetProcessById(processId);
            return string.Equals(
                    process.ProcessName,
                    "WINWORD",
                    StringComparison.OrdinalIgnoreCase
                )
                && process.StartTime.ToUniversalTime().Ticks == startUtcTicks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            if (!TryTerminate(process))
            {
                throw new NativeToolException(
                    "EQUATION_PREFLIGHT_WORKER_FAILED",
                    "The isolated equation preflight worker did not exit after cleanup"
                );
            }
        }
    }

    private static async Task<string> ReadBoundedStderrAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }
            if (builder.Length < MaximumStderrCharacters)
            {
                builder.Append(
                    buffer,
                    0,
                    Math.Min(read, MaximumStderrCharacters - builder.Length)
                );
            }
        }
        return builder.ToString();
    }

    private sealed record TerminationResult(
        bool WorkerTerminated,
        bool WordTerminated,
        bool WordCleanupVerified
    );
}
