using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageRelationshipsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteRelationshipRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = RelationshipRepairOperationJson.ParseInspectRequest(
            arguments.GetRawText()
        );
        var result = new RelationshipRepairWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Inspect(request, cancellationToken);
        return AddRelationshipRepairRuntime(result, started);
    });

    private static Task<object> PlanPackageRelationshipRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteRelationshipRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = RelationshipRepairOperationJson.ParsePlanRequest(
            arguments.GetRawText()
        );
        var result = new RelationshipRepairWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Plan(request, cancellationToken);
        return AddRelationshipRepairRuntime(result, started);
    });

    private static Task<object> ApplyPackageRelationshipRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteRelationshipRepairAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = RelationshipRepairOperationJson.ParseApplyRequest(
            arguments.GetRawText()
        );
        var result = new RelationshipRepairWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Apply(request, cancellationToken);
        return AddRelationshipRepairRuntime(result, started);
    });

    private static JsonObject AddRelationshipRepairRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is RelationshipRepairApplyResult && !response.ContainsKey("backup_path"))
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

    private static Task<object> ExecuteRelationshipRepairAction(Func<object> action)
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
