using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> PlanPackageCommentBodyEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteCommentBodyPackageAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = CommentBodyEditOperationJson.ParsePlanRequest(
            arguments.GetRawText()
        );
        var result = new CommentBodyWordPackageOperation(
            new MicrosoftOpenXmlPackageValidator()
        ).Plan(request, cancellationToken);
        return AddCommentBodyEditRuntime(result, started);
    });

    private static Task<object> ApplyPackageCommentBodyEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteCommentBodyPackageAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = CommentBodyEditOperationJson.ParseApplyRequest(
            arguments.GetRawText()
        );
        var result = new CommentBodyWordPackageOperation(
            new MicrosoftOpenXmlPackageValidator()
        ).Apply(request, cancellationToken);
        return AddCommentBodyEditRuntime(result, started);
    });

    private static JsonObject AddCommentBodyEditRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is CommentBodyEditApplyResult && !response.ContainsKey("backup_path"))
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

    private static Task<object> ExecuteCommentBodyPackageAction(Func<object> action)
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
