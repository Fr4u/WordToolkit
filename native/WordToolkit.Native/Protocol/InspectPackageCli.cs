using System.Globalization;
using System.Text.Json;
using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

internal static class InspectPackageCli
{
    private const string Usage =
        "usage: wordtoolkit-native inspect-package <path> [--include-details] [--max-items <1..200>] [--format json]";

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
        if (arguments.Count == 0 || arguments[0].StartsWith("--", StringComparison.Ordinal))
        {
            WriteUsageError(error);
            return 64;
        }

        var path = arguments[0];
        var includeDetails = false;
        long maxItems = InspectWordPackageContract.DefaultMaxItems;
        for (var index = 1; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument == "--include-details")
            {
                includeDetails = true;
                continue;
            }
            if (
                argument == "--max-items"
                && TryValue(arguments, ref index, out var maximum)
                && long.TryParse(
                    maximum,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out maxItems
                )
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

            WriteUsageError(error);
            return 64;
        }

        try
        {
            var result = new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(path, includeDetails, maxItems)
            );
            output.WriteLine(WordToolkitOperationJson.Serialize(result, indented: true));
            return 0;
        }
        catch (WordToolkitOperationException exception)
        {
            WriteError(error, WordToolkitOperationError.FromException(exception));
            return ExitCode(exception.Code);
        }
        catch (Exception)
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "INTERNAL_ERROR",
                    "The package inspection operation failed",
                    Reason: null,
                    Retryable: false
                )
            );
            return 70;
        }
    }

    internal static int ExitCode(string errorCode)
    {
        return errorCode switch
        {
            "INVALID_INPUT" => 64,
            "NOT_FOUND" => 66,
            "INVALID_PACKAGE"
            or "PACKAGE_LIMIT"
            or "DOCUMENT_ENCRYPTED"
            or "ENCRYPTION_CONTAINER_INVALID" => 65,
            "ACCESS_DENIED" => 77,
            "IO_ERROR" => 74,
            "SOURCE_CHANGED" => 75,
            _ => 70,
        };
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

    private static void WriteError(TextWriter error, WordToolkitOperationError operationError)
    {
        error.WriteLine(
            WordToolkitOperationJson.Serialize(
                new WordToolkitOperationErrorEnvelope(false, operationError)
            )
        );
    }

    private static void WriteUsageError(TextWriter error)
    {
        WriteError(
            error,
            new WordToolkitOperationError(
                "INVALID_INPUT",
                "Invalid inspect-package arguments",
                Usage,
                Retryable: false
            )
        );
    }
}
