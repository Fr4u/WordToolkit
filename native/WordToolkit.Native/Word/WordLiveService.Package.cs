using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly HashSet<string> InspectPackageArguments = new(
        ["local_path", "include_details", "max_items"],
        StringComparer.Ordinal
    );

    private static Task<object> InspectPackageAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "inspect_ooxml_package arguments must be an object"
            );
        }
        foreach (var property in arguments.EnumerateObject())
        {
            if (!InspectPackageArguments.Contains(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "inspect_ooxml_package received an unsupported argument",
                    new { field = property.Name }
                );
            }
        }
        try
        {
            var result = new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(
                    arguments.String("local_path"),
                    arguments.Boolean("include_details", false),
                    arguments.NullableInt64("max_items")
                        ?? InspectWordPackageContract.DefaultMaxItems
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

    private static string ResolveInspectablePackagePath(JsonElement arguments)
    {
        var rawPath = arguments.String("local_path");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "local_path must be a non-empty string"
            );
        }

        string path;
        try
        {
            path = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "local_path is not a valid filesystem path"
            );
        }

        if (!File.Exists(path))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                "The requested Word package does not exist"
            );
        }

        if (!InspectWordPackageContract.IsSupportedFileName(path))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Package inspection accepts DOCX, DOCM, DOTX, or DOTM files"
            );
        }

        return path;
    }

    private static string? BoundForResponse(string? value, int maxCharacters)
    {
        if (value is null || value.Length <= maxCharacters)
        {
            return value;
        }

        return value[..maxCharacters] + "…";
    }
}
