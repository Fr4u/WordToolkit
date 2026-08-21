using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageOcrCandidatesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteOcrAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = OcrOperationJson.ParseInspectRequest(arguments.GetRawText());
        var result = new OcrWordPackageOperation(
            NativeExtensionHost.Registry
        ).Inspect(request, cancellationToken);
        return AddOcrRuntime(result, started);
    });

    private static Task<object> RunPackageOcrAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteOcrAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = OcrOperationJson.ParseRecognizeRequest(arguments.GetRawText());
        var result = new OcrWordPackageOperation(
            NativeExtensionHost.Registry
        ).Recognize(request, cancellationToken);
        return AddOcrRuntime(result, started);
    });

    private static JsonObject AddOcrRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        response["runtime"] = "dotnet-native";
        response["python_used"] = false;
        response["performance"] = new JsonObject
        {
            ["total_ms"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        };
        return response;
    }

    private static Task<object> ExecuteOcrAction(Func<object> action)
    {
        try
        {
            return Task.FromResult(action());
        }
        catch (WordToolkitOperationException exception)
        {
            var details = exception.Details
                ?? (exception.Reason is null ? null : new { reason = exception.Reason });
            throw new NativeToolException(
                exception.Code,
                exception.Message,
                details,
                exception.Retryable
            );
        }
    }
}
