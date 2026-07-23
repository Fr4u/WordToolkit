using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> PlanPackageSemanticEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteStylePackageAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = StyleEditOperationJson.ParsePlanRequest(
            arguments.GetRawText()
        );
        var result = new StyleWordPackageOperation(
            new MicrosoftOpenXmlPackageValidator()
        ).Plan(request, cancellationToken);
        return AddSemanticEditRuntime(result, started);
    });

    private static Task<object> ApplyPackageSemanticEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteStylePackageAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = StyleEditOperationJson.ParseApplyRequest(
            arguments.GetRawText()
        );
        var result = new StyleWordPackageOperation(
            new MicrosoftOpenXmlPackageValidator()
        ).Apply(request, cancellationToken);
        return AddSemanticEditRuntime(result, started);
    });

    private static JsonObject AddSemanticEditRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is StyleEditApplyResult && !response.ContainsKey("backup_path"))
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

    private static Task<object> ExecuteStylePackageAction(Func<object> action)
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
