using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Rendering;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> RenderPackageSemanticHtmlAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            var request = SemanticHtmlWordPackageJson.ParseRequest(
                arguments.GetRawText()
            );
            var result = new SemanticHtmlWordPackageOperation().Execute(
                request,
                cancellationToken
            );
            var response = WordToolkitOperationJson.SerializeToNode(result)
                as JsonObject ?? new JsonObject();
            response["runtime"] = "dotnet-native";
            response["python_used"] = false;
            response["performance"] = new JsonObject
            {
                ["total_ms"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            };
            return Task.FromResult<object>(response);
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
        catch (JsonException)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Semantic HTML render arguments are invalid or contain unsupported fields",
                details: null,
                retryable: false
            );
        }
    }

    private static Task<object> RenderPackageSemanticSvgAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            var request = SemanticSvgWordPackageJson.ParseRequest(arguments.GetRawText());
            var result = new SemanticSvgWordPackageOperation().Execute(
                request,
                cancellationToken
            );
            var response = WordToolkitOperationJson.SerializeToNode(result)
                as JsonObject ?? new JsonObject();
            response["runtime"] = "dotnet-native";
            response["python_used"] = false;
            response["performance"] = new JsonObject
            {
                ["total_ms"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            };
            return Task.FromResult<object>(response);
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
        catch (JsonException)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Semantic SVG render arguments are invalid or contain unsupported fields",
                details: null,
                retryable: false
            );
        }
    }
}
