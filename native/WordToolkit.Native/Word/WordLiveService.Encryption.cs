using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectEncryptionAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        RequireObject(arguments, "OOXML encryption inspection arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name != "local_path")
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown OOXML encryption inspection argument",
                    new { argument = property.Name }
                );
            }
        }
        try
        {
            var result = new InspectOoxmlEncryptionOperation().Execute(
                new InspectOoxmlEncryptionRequest(arguments.String("local_path")),
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
            throw new NativeToolException(
                exception.Code,
                exception.Message,
                retryable: exception.Retryable
            );
        }
    }
}
