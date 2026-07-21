using System.Diagnostics;
using System.Text.Json;

namespace WordToolkit.Native.Word;

internal sealed record NativeBenchmarkResult(
    bool Passed,
    string Runtime,
    bool PythonUsed,
    string Document,
    int Characters,
    int Operations,
    double ConnectMs,
    double ApplyWallMs,
    object? ReportedPerformance,
    double UndoMs,
    bool Undone
);

internal static class NativeBenchmark
{
    public static async Task<NativeBenchmarkResult> RunAsync(WordLiveService service)
    {
        var connectArguments = JsonDocument.Parse(
            """{"use_active":true,"activate":true}"""
        ).RootElement.Clone();
        var connectStarted = Stopwatch.GetTimestamp();
        var connected = await service.CallAsync(
            "connect_live_word_document",
            connectArguments,
            CancellationToken.None
        );
        var connectMs = Stopwatch.GetElapsedTime(connectStarted).TotalMilliseconds;
        var serialized = JsonSerializer.SerializeToElement(connected);
        var documentId = serialized.GetProperty("live_document_id").GetString()!;
        var document = serialized.GetProperty("document").GetProperty("name").GetString()!;
        var chunk = (
            "Natywny benchmark WordToolkit — szybka ścieżka generacja → Word. "
            + string.Concat(Enumerable.Repeat("0123456789 ", 38))
        ).Trim();
        var operations = Enumerable.Range(1, 100)
            .Select(index => new
            {
                type = "text",
                text = $"[{index:000}] {chunk}",
                as_new_paragraph = true,
                style = "",
            })
            .ToArray();
        var applyArguments = JsonSerializer.SerializeToElement(
            new
            {
                live_document_id = documentId,
                operations,
                activate = true,
                expected_version = 0,
                optimize_screen_updates = true,
            }
        );
        var applyStarted = Stopwatch.GetTimestamp();
        var applied = await service.CallAsync(
            "apply_live_word_operations",
            applyArguments,
            CancellationToken.None
        );
        var applyMs = Stopwatch.GetElapsedTime(applyStarted).TotalMilliseconds;
        var appliedElement = JsonSerializer.SerializeToElement(applied);
        var version = appliedElement.GetProperty("live_version").GetInt64();
        var performance = JsonSerializer.Deserialize<object>(
            appliedElement.GetProperty("performance").GetRawText()
        );
        var undoStarted = Stopwatch.GetTimestamp();
        var undone = await service.UndoBenchmarkTransactionAsync(documentId, version);
        var undoMs = Stopwatch.GetElapsedTime(undoStarted).TotalMilliseconds;
        var disconnectArguments = JsonSerializer.SerializeToElement(
            new { live_document_id = documentId }
        );
        _ = await service.CallAsync(
            "disconnect_live_word_document",
            disconnectArguments,
            CancellationToken.None
        );
        return new NativeBenchmarkResult(
            Passed: undone,
            Runtime: ".NET native",
            PythonUsed: false,
            Document: document,
            Characters: operations.Sum(operation => operation.text.Length),
            Operations: operations.Length,
            ConnectMs: Math.Round(connectMs, 3),
            ApplyWallMs: Math.Round(applyMs, 3),
            ReportedPerformance: performance,
            UndoMs: Math.Round(undoMs, 3),
            Undone: undone
        );
    }
}
