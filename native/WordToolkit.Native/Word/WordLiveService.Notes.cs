using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageNotesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteNoteAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = NoteOperationJson.ParseInspectRequest(arguments.GetRawText());
        var result = new NoteWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Inspect(request, cancellationToken);
        return AddNoteRuntime(result, started);
    });

    private static Task<object> PlanPackageNoteRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteNoteAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = NoteOperationJson.ParsePlanRequest(arguments.GetRawText());
        var result = new NoteWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Plan(request, cancellationToken);
        return AddNoteRuntime(result, started);
    });

    private static Task<object> ApplyPackageNoteRepairAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteNoteAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var request = NoteOperationJson.ParseApplyRequest(arguments.GetRawText());
        var result = new NoteWordPackageOperation(
            NativeExtensionHost.CandidateValidator
        ).Apply(request, cancellationToken);
        return AddNoteRuntime(result, started);
    });

    private static JsonObject AddNoteRuntime<T>(T result, long started)
    {
        var response = WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
        if (result is NoteRepairApplyResult && !response.ContainsKey("backup_path"))
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

    private static Task<object> ExecuteNoteAction(Func<object> action)
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
