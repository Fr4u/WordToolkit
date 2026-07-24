using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly HashSet<string> FlatOpcPackageArguments = new(
        ["local_path", "output_path", "direction"],
        StringComparer.Ordinal
    );

    private static Task<object> ConvertFlatOpcPackageAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "convert_ooxml_flat_opc arguments must be an object"
            );
        }
        foreach (var property in arguments.EnumerateObject())
        {
            if (!FlatOpcPackageArguments.Contains(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "convert_ooxml_flat_opc received an unsupported argument",
                    new { field = property.Name }
                );
            }
        }

        _ = arguments.Required("direction");
        if (
            !FlatOpcWordPackageContract.TryParse(
                arguments.String("direction"),
                out var direction
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "direction must be 'to_flat_opc' or 'from_flat_opc'"
            );
        }

        try
        {
            var result = new FlatOpcWordPackageOperation().Execute(
                new FlatOpcWordPackageRequest(
                    arguments.String("local_path"),
                    arguments.String("output_path"),
                    direction
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
                exception.Reason is null ? null : new { reason = exception.Reason },
                exception.Retryable
            );
        }
    }
}
