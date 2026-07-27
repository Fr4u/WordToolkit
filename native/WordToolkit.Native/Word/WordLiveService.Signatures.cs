using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectSignaturesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        RequireObject(arguments, "OOXML signature inspection arguments");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "local_path",
            "view",
            "signature_id",
            "offset",
            "limit",
            "include_source",
            "include_certificate_hash",
        };
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown OOXML signature inspection argument",
                    new { argument = property.Name }
                );
            }
        }
        var offset = arguments.NullableInt64("offset") ?? 0;
        var limit = arguments.NullableInt64("limit")
            ?? InspectOoxmlSignaturesContract.DefaultLimit;
        if (offset is < 0 or > int.MaxValue || limit is < 1 or > int.MaxValue)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "OOXML signature paging is outside the integer contract"
            );
        }
        try
        {
            var result = new InspectOoxmlSignaturesOperation().Execute(
                new InspectOoxmlSignaturesRequest(
                    arguments.String("local_path"),
                    arguments.String("view", "summary"),
                    BoundedOptionalArgument(arguments, "signature_id", 96),
                    (int)offset,
                    (int)limit,
                    arguments.Boolean("include_source", false),
                    arguments.Boolean("include_certificate_hash", false)
                ),
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
