using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageTemplateStyleAlignmentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteTemplateStyleAlignmentAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = TemplateStyleAlignmentOperationJson.ParseInspectRequest(
            arguments.GetRawText()
        );
        var result = new TemplateStyleAlignmentWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Inspect(request, cancellationToken);
        return AddTemplateStyleAlignmentRuntime(result, started);
    });

    private static Task<object> PlanPackageTemplateStyleAlignmentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteTemplateStyleAlignmentAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = TemplateStyleAlignmentOperationJson.ParsePlanRequest(
            arguments.GetRawText()
        );
        var result = new TemplateStyleAlignmentWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Plan(request, cancellationToken);
        return AddTemplateStyleAlignmentRuntime(result, started);
    });

    private static Task<object> ApplyPackageTemplateStyleAlignmentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteTemplateStyleAlignmentAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = TemplateStyleAlignmentOperationJson.ParseApplyRequest(
            arguments.GetRawText()
        );
        var result = new TemplateStyleAlignmentWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Apply(request, cancellationToken);
        return AddTemplateStyleAlignmentRuntime(result, started);
    });

    private static JsonObject AddTemplateStyleAlignmentRuntime<T>(
        T result,
        long started
    )
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is TemplateStyleAlignmentApplyResult
            && !response.ContainsKey("backup_path"))
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

    private static Task<object> ExecuteTemplateStyleAlignmentAction(
        Func<object> action
    )
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
