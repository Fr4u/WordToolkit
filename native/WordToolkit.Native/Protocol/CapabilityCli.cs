using System.Text.Json;

namespace WordToolkit.Native.Protocol;

internal static class CapabilityCli
{
    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error
    )
    {
        string? query = null;
        var offset = 0;
        var limit = ToolCatalog.DefaultCapabilityPageSize;
        var schemaView = false;
        var hasManifestSelector = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument == "--query" && TryValue(arguments, ref index, out var queryValue))
            {
                query = queryValue;
                hasManifestSelector = true;
                continue;
            }
            if (
                argument == "--offset"
                && TryValue(arguments, ref index, out var offsetValue)
                && int.TryParse(offsetValue, out offset)
            )
            {
                hasManifestSelector = true;
                continue;
            }
            if (
                argument == "--limit"
                && TryValue(arguments, ref index, out var limitValue)
                && int.TryParse(limitValue, out limit)
            )
            {
                hasManifestSelector = true;
                continue;
            }
            if (argument == "--schema")
            {
                schemaView = true;
                continue;
            }
            if (
                argument == "--format"
                && TryValue(arguments, ref index, out var format)
                && string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            error.WriteLine(
                "usage: wordtoolkit-native capabilities [--schema | [--query <text>] [--offset <n>] [--limit <n>]] [--format json]"
            );
            return 64;
        }

        try
        {
            if (schemaView && hasManifestSelector)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "--schema cannot be combined with --query, --offset or --limit"
                );
            }
            var catalog = ToolCatalog.LoadNativeWordTools();
            var result = schemaView
                ? catalog.GetCapabilitySchema()
                : catalog.GetCapabilities(query, offset, limit);
            output.WriteLine(result.ToJsonString(JsonDefaults.Indented));
            return 0;
        }
        catch (NativeToolException exception) when (exception.ErrorCode == "INVALID_INPUT")
        {
            error.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        error = new
                        {
                            code = exception.ErrorCode,
                            message = exception.Message,
                        },
                    },
                    JsonDefaults.Compact
                )
            );
            return 64;
        }
    }

    private static bool TryValue(
        IReadOnlyList<string> arguments,
        ref int index,
        out string value
    )
    {
        if (index + 1 >= arguments.Count)
        {
            value = "";
            return false;
        }
        value = arguments[++index];
        return true;
    }
}
