using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> AnalyzePackageDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecuteDocumentAnalysisAction(() =>
    {
        var request = DocumentAnalysisOperationJson.ParseRequest(arguments.GetRawText());
        var result = new DocumentAnalysisWordPackageOperation().Analyze(
            request,
            cancellationToken
        );
        return WordToolkitOperationJson.SerializeToNode(result)
            as JsonObject ?? new JsonObject();
    });

    private static Task<object> ExecuteDocumentAnalysisAction(Func<object> action)
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
