using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

internal static class ExtensionCatalogCli
{
    private const string Usage =
        "usage: wordtoolkit-native extensions [--query <text>] [--offset <n>] [--limit <1..32>] [--format json]";

    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error
    )
    {
        if (arguments.Count == 1 && arguments[0] is "--help" or "-h")
        {
            output.WriteLine(Usage);
            return 0;
        }

        string? query = null;
        var offset = 0;
        var limit = InspectExtensionCatalogContract.DefaultPageSize;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (
                argument == "--query"
                && TryValue(arguments, ref index, out query)
            )
            {
                continue;
            }
            if (
                argument == "--offset"
                && TryValue(arguments, ref index, out var offsetValue)
                && int.TryParse(offsetValue, out offset)
            )
            {
                continue;
            }
            if (
                argument == "--limit"
                && TryValue(arguments, ref index, out var limitValue)
                && int.TryParse(limitValue, out limit)
            )
            {
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
            return UsageError(error);
        }

        try
        {
            var result = new InspectExtensionCatalogOperation(
                NativeExtensionHost.Registry
            ).Execute(new InspectExtensionCatalogRequest(query, offset, limit));
            output.WriteLine(WordToolkitOperationJson.Serialize(result, indented: true));
            return 0;
        }
        catch (WordToolkitOperationException exception)
        {
            error.WriteLine(
                WordToolkitOperationJson.Serialize(
                    new WordToolkitOperationErrorEnvelope(
                        Ok: false,
                        WordToolkitOperationError.FromException(exception)
                    ),
                    indented: true
                )
            );
            return exception.Code == "INVALID_INPUT" ? 64 : 70;
        }
    }

    private static bool TryValue(
        IReadOnlyList<string> arguments,
        ref int index,
        out string? value
    )
    {
        if (index + 1 >= arguments.Count)
        {
            value = null;
            return false;
        }
        value = arguments[++index];
        return true;
    }

    private static int UsageError(TextWriter error)
    {
        error.WriteLine(Usage);
        return 64;
    }
}
