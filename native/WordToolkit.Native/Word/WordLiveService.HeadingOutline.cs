using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageHeadingOutlineAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteHeadingOutlineAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = HeadingOutlineOperationJson.ParseInspectRequest(
            arguments.GetRawText()
        );
        var result = new HeadingOutlineWordPackageOperation().Inspect(
            request,
            cancellationToken
        );
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        response["runtime"] = "dotnet-native";
        response["python_used"] = false;
        response["performance"] = new JsonObject
        {
            ["total_ms"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        };
        return response;
    });

    private static Task<object> ExecuteHeadingOutlineAction(Func<object> action)
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
