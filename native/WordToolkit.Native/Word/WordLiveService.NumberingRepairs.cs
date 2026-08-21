using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> PlanPackageNumberingRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteNumberingRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = NumberingRepairOperationJson.ParsePlanRequest(
            arguments.GetRawText()
        );
        var result = new NumberingRepairWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Plan(request, cancellationToken);
        return AddNumberingRepairRuntime(result, started);
    });

    private static Task<object> ApplyPackageNumberingRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteNumberingRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = NumberingRepairOperationJson.ParseApplyRequest(
            arguments.GetRawText()
        );
        var result = new NumberingRepairWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Apply(request, cancellationToken);
        return AddNumberingRepairRuntime(result, started);
    });

    private static JsonObject AddNumberingRepairRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is NumberingRepairApplyResult && !response.ContainsKey("backup_path"))
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

    private static Task<object> ExecuteNumberingRepairAction(Func<object> action)
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
