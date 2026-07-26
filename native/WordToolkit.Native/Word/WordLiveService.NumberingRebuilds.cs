using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageNumberingRebuildCandidatesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteNumberingRebuildAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = NumberingRebuildOperationJson.ParseInspectRequest(
            arguments.GetRawText()
        );
        var result = new NumberingRebuildWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Inspect(request, cancellationToken);
        return AddNumberingRebuildRuntime(result, started);
    });

    private static Task<object> PlanPackageNumberingRebuildAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteNumberingRebuildAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = NumberingRebuildOperationJson.ParsePlanRequest(
            arguments.GetRawText()
        );
        var result = new NumberingRebuildWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Plan(request, cancellationToken);
        return AddNumberingRebuildRuntime(result, started);
    });

    private static Task<object> ApplyPackageNumberingRebuildAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteNumberingRebuildAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = NumberingRebuildOperationJson.ParseApplyRequest(
            arguments.GetRawText()
        );
        var result = new NumberingRebuildWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Apply(request, cancellationToken);
        return AddNumberingRebuildRuntime(result, started);
    });

    private static JsonObject AddNumberingRebuildRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is NumberingRebuildApplyResult && !response.ContainsKey("backup_path"))
        {
            response["backup_path"] = null;
        }
        response["runtime"] = "dotnet-native";
        response["python_used"] = false;
        response["performance"] = new JsonObject
        {
            ["total_ms"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        };
        return response;
    }

    private static Task<object> ExecuteNumberingRebuildAction(Func<object> action)
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
