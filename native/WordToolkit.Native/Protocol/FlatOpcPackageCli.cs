using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

internal static class FlatOpcPackageCli
{
    private const string Usage =
        "usage: wordtoolkit-native flat-opc-package <input> <output> --direction <to_flat_opc|from_flat_opc> [--format json]";

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
        if (arguments.Count < 4)
        {
            return UsageError(error);
        }

        var inputPath = arguments[0];
        var outputPath = arguments[1];
        string? directionName = null;
        for (var index = 2; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (
                argument == "--direction"
                && TryValue(arguments, ref index, out directionName)
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
        if (!FlatOpcWordPackageContract.TryParse(directionName, out var direction))
        {
            return UsageError(error);
        }

        try
        {
            var result = new FlatOpcWordPackageOperation().Execute(
                new FlatOpcWordPackageRequest(inputPath, outputPath, direction)
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
                    "The Flat OPC package conversion failed",
                    Reason: null,
                    Retryable: false
                )
            );
            return 70;
        }
    }

    internal static int ExitCode(string code) => code switch
    {
        "INVALID_INPUT" => 64,
        "NOT_FOUND" => 66,
        "INVALID_PACKAGE" or "INVALID_WORD_PACKAGE" or "PACKAGE_LIMIT" => 65,
        "VERSION_CONFLICT" => 73,
        "SOURCE_CHANGED" => 75,
        "IO_ERROR" => 74,
        "ACCESS_DENIED" => 77,
        "SIGNED_PACKAGE" or "UNSUPPORTED_DOCUMENT" => 78,
        "RESULT_MISMATCH" or "VALIDATION_FAILED" => 70,
        _ => 70,
    };

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
        WriteError(
            error,
            new WordToolkitOperationError(
                "INVALID_INPUT",
                "Invalid flat-opc-package arguments",
                Usage,
                Retryable: false
            )
        );
        return 64;
    }

    private static void WriteError(TextWriter error, WordToolkitOperationError value) =>
        error.WriteLine(
            WordToolkitOperationJson.Serialize(
                new { ok = false, error = value },
                indented: true
            )
        );
}
