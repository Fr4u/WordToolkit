using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Protocol;

internal static class EquationPreflightWorkerCli
{
    private const int WordDoNotSaveChanges = 0;
    private const int OfficeAutomationSecurityForceDisable = 3;
    private const int MaximumRequestCharacters = 8 * 1024 * 1024;

    public static async Task<int> RunAsync(
        TextReader input,
        TextWriter output,
        TextWriter error
    )
    {
        var requestText = await input.ReadToEndAsync();
        if (requestText.Length is < 2 or > MaximumRequestCharacters)
        {
            await WriteEventAsync(
                output,
                new JsonObject
                {
                    ["event"] = "fatal",
                    ["error_code"] = "INVALID_INPUT",
                    ["stage"] = "read_request",
                }
            );
            return 64;
        }

        JsonDocument? request = null;
        WordComHost? host = null;
        object? dedicatedApplication = null;
        Exception? cleanupFailure = null;
        var success = false;
        var failedIndex = -1;
        var workerStarted = Stopwatch.GetTimestamp();
        try
        {
            request = JsonDocument.Parse(requestText);
            if (
                request.RootElement.ValueKind != JsonValueKind.Object
                || !request.RootElement.TryGetProperty("equations", out var equations)
                || equations.ValueKind != JsonValueKind.Array
                || equations.GetArrayLength() is < 1 or > 200
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "The isolated equation worker requires between 1 and 200 equations"
                );
            }

            object CreateDedicatedApplication(bool _)
            {
                if (dedicatedApplication is not null)
                {
                    return dedicatedApplication;
                }
                var type = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
                    ?? throw new NativeToolException(
                        "LIVE_WORD_UNAVAILABLE",
                        "Microsoft Word is not installed"
                    );
                dedicatedApplication = Activator.CreateInstance(type)
                    ?? throw new NativeToolException(
                        "LIVE_WORD_UNAVAILABLE",
                        "Microsoft Word could not be started"
                    );
                dynamic application = dedicatedApplication;
                application.Visible = false;
                application.DisplayAlerts = 0;
                application.AutomationSecurity = OfficeAutomationSecurityForceDisable;
                application.Options.UpdateLinksAtOpen = false;
                return dedicatedApplication;
            }

            host = new WordComHost(
                CreateDedicatedApplication,
                shutdownTimeout: TimeSpan.FromSeconds(15)
            );
            var owned = await host.InvokeAsync(
                application => ProbeOwnedWordProcess(application),
                CancellationToken.None,
                launchIfMissing: true
            );
            await WriteEventAsync(
                output,
                new JsonObject
                {
                    ["event"] = "ready",
                    ["word_process_id"] = owned.ProcessId,
                    ["word_process_start_utc_ticks"] = owned.StartUtcTicks,
                    ["equation_count"] = equations.GetArrayLength(),
                }
            );

            var service = new WordLiveService(host);
            var testHangIndex = InternalTestHangIndex();
            var index = 0;
            foreach (var equation in equations.EnumerateArray())
            {
                failedIndex = index;
                await WriteEventAsync(
                    output,
                    new JsonObject
                    {
                        ["event"] = "equation_started",
                        ["equation_index"] = index,
                    }
                );
                var itemStarted = Stopwatch.GetTimestamp();
                try
                {
                    if (testHangIndex == index)
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan);
                    }
                    var itemArguments = new JsonObject
                    {
                        ["validation_mode"] = "native",
                        ["equations"] = new JsonArray(
                            JsonNode.Parse(equation.GetRawText())
                        ),
                    };
                    using var itemDocument = JsonDocument.Parse(
                        itemArguments.ToJsonString(JsonDefaults.Compact)
                    );
                    var result = await service.PreflightEquationsInProcessAsync(
                        itemDocument.RootElement,
                        CancellationToken.None
                    );
                    var resultNode = JsonSerializer.SerializeToNode(
                        result,
                        JsonDefaults.Compact
                    )?.AsObject() ?? throw new InvalidOperationException(
                        "Equation preflight result was empty"
                    );
                    var resultItems = resultNode["equations"]?.AsArray()
                        ?? throw new InvalidOperationException(
                            "Equation preflight result did not contain items"
                        );
                    if (resultItems.Count != 1 || resultItems[0] is not JsonObject item)
                    {
                        throw new InvalidOperationException(
                            "Equation preflight result contained an unexpected item count"
                        );
                    }
                    var globalItem = item.DeepClone().AsObject();
                    globalItem["index"] = index;
                    globalItem["equation_id"] = EquationPreflightIdentity.FromInput(equation);
                    await WriteEventAsync(
                        output,
                        new JsonObject
                        {
                            ["event"] = "equation_completed",
                            ["equation_index"] = index,
                            ["equation_id"] = EquationPreflightIdentity.FromInput(equation),
                            ["elapsed_milliseconds"] = Math.Max(
                                0L,
                                (long)Stopwatch.GetElapsedTime(itemStarted).TotalMilliseconds
                            ),
                            ["result"] = globalItem,
                        }
                    );
                }
                catch (Exception exception)
                {
                    var code = exception is NativeToolException native
                        ? native.ErrorCode
                        : "INTERNAL_ERROR";
                    if (code is not (
                        "EQUATION_INVALID"
                        or "INVALID_INPUT"
                        or "LIMIT_EXCEEDED"
                        or "UNSUPPORTED_FORMAT"
                    ))
                    {
                        await WriteEventAsync(
                            output,
                            new JsonObject
                            {
                                ["event"] = "fatal",
                                ["error_code"] = code,
                                ["stage"] = "equation_worker_infrastructure",
                                ["equation_index"] = index,
                                ["exception_type"] = exception.GetType().Name,
                            }
                        );
                        throw;
                    }
                    var safeDetails = WordLiveService.ProjectSafeErrorDetails(exception);
                    var safeDetailsNode = safeDetails is null
                        ? null
                        : JsonSerializer.SerializeToNode(
                            safeDetails,
                            JsonDefaults.Compact
                        ) as JsonObject;
                    var diagnosticNode = safeDetails is null
                        ? null
                        : safeDetailsNode?["diagnostic"];
                    var suggestionCode = SuggestionCode(
                        code,
                        exception.Message,
                        diagnosticNode as JsonObject
                    );
                    await WriteEventAsync(
                        output,
                        new JsonObject
                        {
                            ["event"] = "equation_failed",
                            ["equation_index"] = index,
                            ["equation_id"] = EquationPreflightIdentity.FromInput(equation),
                            ["elapsed_milliseconds"] = Math.Max(
                                0L,
                                (long)Stopwatch.GetElapsedTime(itemStarted).TotalMilliseconds
                            ),
                            ["error_code"] = code,
                            ["retryable"] = exception is NativeToolException retryable
                                && retryable.Retryable,
                            ["exception_type"] = exception.GetType().Name,
                            ["stage"] = "build_verified_native_equation",
                            ["suggestion_code"] = suggestionCode,
                            ["diagnostic"] = diagnosticNode?.DeepClone(),
                            ["expected_semantic_sha256"] = safeDetailsNode?
                                ["expected_semantic_sha256"]?.DeepClone(),
                            ["actual_semantic_sha256"] = safeDetailsNode?
                                ["actual_semantic_sha256"]?.DeepClone(),
                            ["hresult"] = safeDetailsNode?["hresult"]?.DeepClone(),
                            ["native_exception_type"] = safeDetailsNode?
                                ["exception_type"]?.DeepClone(),
                        }
                    );
                    // Equation-local failures are data, not worker-fatal errors. Continue
                    // validating the remaining scratch documents; infrastructure failures
                    // still surface through fatal/cleanup events.
                }
                index++;
            }
            success = true;
        }
        catch (Exception exception)
        {
            if (failedIndex < 0)
            {
                var code = exception is NativeToolException native
                    ? native.ErrorCode
                    : "INTERNAL_ERROR";
                await WriteEventAsync(
                    output,
                    new JsonObject
                    {
                        ["event"] = "fatal",
                        ["error_code"] = code,
                        ["stage"] = "worker_setup",
                        ["exception_type"] = exception.GetType().Name,
                    }
                );
            }
            await error.WriteLineAsync(
                $"Equation preflight worker failed ({exception.GetType().Name})"
            );
        }
        finally
        {
            if (host is not null)
            {
                try
                {
                    await host.InvokeAsync(
                        application =>
                        {
                            while ((int)application.Documents.Count > 0)
                            {
                                dynamic? document = null;
                                try
                                {
                                    document = application.Documents.Item(1);
                                    document.Close(WordDoNotSaveChanges);
                                }
                                finally
                                {
                                    FinalRelease(document);
                                }
                            }
                            application.Quit(WordDoNotSaveChanges);
                            return true;
                        },
                        CancellationToken.None,
                        launchIfMissing: false
                    );
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
                try
                {
                    await host.DisposeAsync();
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                }
            }
            dedicatedApplication = null;
            request?.Dispose();
        }

        var cleanupVerified = cleanupFailure is null;
        await WriteEventAsync(
            output,
            new JsonObject
            {
                ["event"] = "finished",
                ["success"] = success && cleanupVerified,
                ["cleanup_verified"] = cleanupVerified,
                ["failed_equation_index"] = failedIndex >= 0 && !success
                    ? failedIndex
                    : null,
                ["total_milliseconds"] = Math.Max(
                    0L,
                    (long)Stopwatch.GetElapsedTime(workerStarted).TotalMilliseconds
                ),
            }
        );
        return success && cleanupVerified ? 0 : 2;
    }

    private static string SuggestionCode(
        string errorCode,
        string message,
        JsonObject? diagnostic
    )
    {
        if (
            message.Contains("Unsupported LaTeX command", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unsupported command", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "USE_SUPPORTED_LATEX_OR_UNICODEMATH";
        }
        var mismatch = diagnostic?["mismatch_kind"]?.GetValue<string>() ?? "";
        var expectedToken = diagnostic?["expected_token_kind"]?.GetValue<string>() ?? "";
        var actualToken = diagnostic?["actual_token_kind"]?.GetValue<string>() ?? "";
        if (
            mismatch == "canonical_structure"
            && (expectedToken == "operator" || actualToken == "operator")
        )
        {
            return "TRY_EXPLICIT_MULTIPLICATION_WITH_CDOT";
        }
        if (
            mismatch == "canonical_structure"
            && (expectedToken == "letter" || actualToken == "letter")
        )
        {
            return "SIMPLIFY_STYLED_ACCENT_OR_VECTOR_APPLICATION";
        }
        return errorCode == "EQUATION_INVALID"
            ? "SIMPLIFY_OR_TRY_UNICODEMATH"
            : "RUN_CONVERSION_ONLY_FOR_SYNTAX_DIAGNOSIS";
    }

    private static OwnedWordProcess ProbeOwnedWordProcess(dynamic application)
    {
        dynamic? document = null;
        dynamic? window = null;
        try
        {
            document = application.Documents.Add(Visible: false);
            window = document.ActiveWindow;
            var hwnd = new IntPtr(Convert.ToInt64(window.Hwnd));
            if (
                hwnd == IntPtr.Zero
                || GetWindowThreadProcessId(hwnd, out var processId) == 0
                || processId == 0
            )
            {
                throw new NativeToolException(
                    "LIVE_WORD_UNAVAILABLE",
                    "The isolated worker could not bind its dedicated Word process"
                );
            }
            using var process = Process.GetProcessById(checked((int)processId));
            if (!string.Equals(process.ProcessName, "WINWORD", StringComparison.OrdinalIgnoreCase))
            {
                throw new NativeToolException(
                    "LIVE_WORD_UNAVAILABLE",
                    "The isolated worker bound an unexpected process"
                );
            }
            return new OwnedWordProcess(
                checked((int)processId),
                process.StartTime.ToUniversalTime().Ticks
            );
        }
        finally
        {
            if (document is not null)
            {
                try
                {
                    document.Close(WordDoNotSaveChanges);
                }
                catch
                {
                    // The worker-wide cleanup remains authoritative.
                }
            }
            FinalRelease(window);
            FinalRelease(document);
        }
    }

    private static async Task WriteEventAsync(TextWriter output, JsonObject value)
    {
        await output.WriteLineAsync(value.ToJsonString(JsonDefaults.Compact));
        await output.FlushAsync();
    }

    private static int InternalTestHangIndex()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_INTERNAL_EQUATION_PREFLIGHT_TEST_MODE"
            ) != "1"
        )
        {
            return -1;
        }
        return int.TryParse(
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_INTERNAL_EQUATION_PREFLIGHT_HANG_INDEX"
            ),
            out var index
        ) && index is >= 0 and < 200
            ? index
            : -1;
    }

    private static void FinalRelease(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
        {
            return;
        }
        try
        {
            Marshal.FinalReleaseComObject(value);
        }
        catch (InvalidComObjectException)
        {
            // Already released by another owned RCW.
        }
    }

    private sealed record OwnedWordProcess(int ProcessId, long StartUtcTicks);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId
    );
}
