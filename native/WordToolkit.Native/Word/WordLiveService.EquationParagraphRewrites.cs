using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageEquationParagraphRewritesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteEquationParagraphRewriteAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = EquationParagraphRewriteOperationJson.ParseInspectRequest(
            arguments.GetRawText()
        );
        var result = new EquationParagraphRewriteWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Inspect(request, cancellationToken);
        return AddEquationParagraphRewriteRuntime(result, started);
    });

    private static Task<object> PlanPackageEquationParagraphRewritesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteEquationParagraphRewriteAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = EquationParagraphRewriteOperationJson.ParsePlanRequest(
            arguments.GetRawText()
        );
        var result = new EquationParagraphRewriteWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Plan(request, cancellationToken);
        return AddEquationParagraphRewriteRuntime(result, started);
    });

    private static Task<object> ApplyPackageEquationParagraphRewritesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteEquationParagraphRewriteAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = EquationParagraphRewriteOperationJson.ParseApplyRequest(
            arguments.GetRawText()
        );
        var result = new EquationParagraphRewriteWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Apply(request, cancellationToken);
        return AddEquationParagraphRewriteRuntime(result, started);
    });

    private static JsonObject AddEquationParagraphRewriteRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is EquationParagraphRewriteApplyResult
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

    private static Task<object> ExecuteEquationParagraphRewriteAction(Func<object> action)
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
