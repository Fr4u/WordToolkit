using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly HashSet<string> InspectExtensionArguments = new(
        ["query", "offset", "limit"],
        StringComparer.Ordinal
    );

    private static Task<object> InspectExtensionsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "inspect_wordtoolkit_extensions arguments must be an object"
            );
        }
        foreach (var property in arguments.EnumerateObject())
        {
            if (!InspectExtensionArguments.Contains(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "inspect_wordtoolkit_extensions received an unsupported argument",
                    new { field = property.Name }
                );
            }
        }

        string? query = null;
        if (arguments.TryGetProperty("query", out var queryNode))
        {
            if (queryNode.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "query must be a string"
                );
            }
            query = queryNode.GetString();
        }
        var offset = OptionalInteger(arguments, "offset", 0);
        var limit = OptionalInteger(
            arguments,
            "limit",
            InspectExtensionCatalogContract.DefaultPageSize
        );
        try
        {
            var result = new InspectExtensionCatalogOperation(
                NativeExtensionHost.Registry
            ).Execute(
                new InspectExtensionCatalogRequest(query, offset, limit),
                cancellationToken
            );
            return Task.FromResult<object>(
                WordToolkitOperationJson.SerializeToNode(result)!
            );
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

    private static int OptionalInteger(
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
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be an integer"
            );
        }
        return value;
    }
}
