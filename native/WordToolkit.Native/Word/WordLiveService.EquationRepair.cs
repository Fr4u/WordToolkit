using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageEquationRepairsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteEquationRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = EquationRepairOperationJson.ParseInspectRequest(
            arguments.GetRawText()
        );
        var result = new EquationRepairWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Inspect(request, cancellationToken);
        return AddEquationRepairRuntime(result, started);
    });

    private static Task<object> PlanPackageEquationRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteEquationRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = EquationRepairOperationJson.ParsePlanRequest(
            arguments.GetRawText()
        );
        var result = new EquationRepairWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Plan(request, cancellationToken);
        return AddEquationRepairRuntime(result, started);
    });

    private static Task<object> ApplyPackageEquationRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteEquationRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = EquationRepairOperationJson.ParseApplyRequest(
            arguments.GetRawText()
        );
        var result = new EquationRepairWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Apply(request, cancellationToken);
        return AddEquationRepairRuntime(result, started);
    });

    private static JsonObject AddEquationRepairRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is EquationRepairApplyResult && !response.ContainsKey("backup_path"))
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

    private static Task<object> ExecuteEquationRepairAction(Func<object> action)
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
