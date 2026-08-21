using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private Task<object> InspectObservabilityAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        RequireObject(arguments, "observability arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (
                property.Name
                    is not (
                        "view"
                        or "offset"
                        or "limit"
                        or "include_correlation"
                        or "include_record_hashes"
                    )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown observability argument",
                    new { argument = property.Name }
                );
            }
        }
        var view = ReadObservabilityString(arguments, "view", "summary");
        var offset = ReadObservabilityInt(arguments, "offset", 0);
        var limit = ReadObservabilityInt(
            arguments,
            "limit",
            InspectObservabilityContract.DefaultPageSize
        );
        var includeCorrelation = ReadObservabilityBoolean(
            arguments,
            "include_correlation",
            defaultValue: false
        );
        var includeRecordHashes = ReadObservabilityBoolean(
            arguments,
            "include_record_hashes",
            defaultValue: false
        );
        try
        {
            object result = new InspectObservabilityOperation(_observability).Execute(
                new InspectObservabilityRequest(
                    view,
                    offset,
                    limit,
                    includeCorrelation,
                    includeRecordHashes
                ),
                cancellationToken
            );
            return Task.FromResult(result);
        }
        catch (WordToolkitOperationException exception)
        {
            throw new NativeToolException(exception.Code, exception.Message);
        }
    }

    private static string ReadObservabilityString(
        JsonElement arguments,
        string name,
        string defaultValue
    )
    {
        if (!arguments.TryGetProperty(name, out var node))
        {
            return defaultValue;
        }
        if (node.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(node.GetString()))
        {
            throw new NativeToolException("INVALID_INPUT", $"{name} must be a non-empty string");
        }
        return node.GetString()!;
    }

    private static int ReadObservabilityInt(
        JsonElement arguments,
        string name,
        int defaultValue
    )
    {
        if (!arguments.TryGetProperty(name, out var node))
        {
            return defaultValue;
        }
        if (node.ValueKind != JsonValueKind.Number || !node.TryGetInt32(out var value))
        {
            throw new NativeToolException("INVALID_INPUT", $"{name} must be an integer");
        }
        return value;
    }

    private static bool ReadObservabilityBoolean(
        JsonElement arguments,
        string name,
        bool defaultValue
    )
    {
        if (!arguments.TryGetProperty(name, out var node))
        {
            return defaultValue;
        }
        if (node.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new NativeToolException("INVALID_INPUT", $"{name} must be a Boolean");
        }
        return node.GetBoolean();
    }
}
