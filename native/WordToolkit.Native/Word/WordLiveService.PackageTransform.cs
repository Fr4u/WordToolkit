using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly HashSet<string> TransformPackageArguments = new(
        ["local_path", "output_path", "operation", "find_text", "replace_text"],
        StringComparer.Ordinal
    );

    private static Task<object> TransformPackageAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "transform_ooxml_package arguments must be an object"
            );
        }
        foreach (var property in arguments.EnumerateObject())
        {
            if (!TransformPackageArguments.Contains(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "transform_ooxml_package received an unsupported argument",
                    new { field = property.Name }
                );
            }
        }

        _ = arguments.Required("operation");
        var operationName = arguments.String("operation");
        if (!TransformWordPackageContract.TryParse(operationName, out var kind))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "operation is not a supported package transform"
            );
        }
        var findText = OptionalString(arguments, "find_text");
        var replaceText = OptionalString(arguments, "replace_text");
        if (
            kind == WordPackageTransformKind.ReplaceFirstTextOccurrence
            && (findText is null || replaceText is null)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "find_text and replace_text are required for text replacement"
            );
        }
        if (
            kind != WordPackageTransformKind.ReplaceFirstTextOccurrence
            && (findText is not null || replaceText is not null)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "find_text and replace_text are only valid for text replacement"
            );
        }

        try
        {
            var result = new TransformWordPackageOperation().Execute(
                new TransformWordPackageRequest(
                    arguments.String("local_path"),
                    arguments.String("output_path"),
                    kind,
                    findText,
                    replaceText
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
