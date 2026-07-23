using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

internal static class TransformPackageCli
{
    private const string Usage =
        "usage: wordtoolkit-native transform-package <input> <output> --operation <replace_first_text_occurrence|accept_all_tracked_changes|reject_all_tracked_changes> [--find-text <text> --replace-text <text>] [--format json]";

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
        string? operation = null;
        string? findText = null;
        string? replaceText = null;
        for (var index = 2; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument == "--operation" && TryValue(arguments, ref index, out operation))
            {
                continue;
            }
            if (argument == "--find-text" && TryValue(arguments, ref index, out findText))
            {
                continue;
            }
            if (
                argument == "--replace-text"
                && TryValue(arguments, ref index, out replaceText)
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

        if (!TransformWordPackageContract.TryParse(operation, out var kind))
        {
            return UsageError(error);
        }

        try
        {
            var result = new TransformWordPackageOperation().Execute(
                new TransformWordPackageRequest(
                    inputPath,
                    outputPath,
                    kind,
                    findText,
                    replaceText
                )
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
                    "The Word package transform failed",
                    Reason: null,
                    Retryable: false
                )
            );
            return 70;
        }
    }

    private static int ExitCode(string code) => code switch
    {
        "INVALID_INPUT" => 64,
        "NOT_FOUND" => 66,
        "INVALID_PACKAGE" or "INVALID_WORD_PACKAGE" or "PACKAGE_LIMIT" => 65,
        "TEXT_NOT_FOUND" => 69,
        "VERSION_CONFLICT" => 73,
        "IO_ERROR" => 74,
        "ACCESS_DENIED" => 77,
        "SIGNED_PACKAGE" or "UNSUPPORTED_DOCUMENT" => 78,
        "VALIDATION_FAILED" or "RESULT_MISMATCH" => 70,
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
                "Invalid transform-package arguments",
                Usage,
                Retryable: false
            )
        );
        return 64;
    }

    private static void WriteError(
        TextWriter error,
        WordToolkitOperationError operationError
    ) => error.WriteLine(
        WordToolkitOperationJson.Serialize(
            new WordToolkitOperationErrorEnvelope(false, operationError)
        )
    );
}
