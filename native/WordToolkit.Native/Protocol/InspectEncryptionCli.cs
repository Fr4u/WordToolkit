using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

internal static class InspectEncryptionCli
{
    private const string Usage =
        "usage: wordtoolkit-native inspect-encryption <path> [--format json]";

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
            return UsageError(error);
        }
        if (
            arguments.Count != 1
            && !(
                arguments.Count == 3
                && arguments[1] == "--format"
                && string.Equals(arguments[2], "json", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return UsageError(error);
        }

        try
        {
            var result = new InspectOoxmlEncryptionOperation().Execute(
                new InspectOoxmlEncryptionRequest(arguments[0])
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
                    "The OOXML encryption inspection operation failed",
                    Reason: null,
                    Retryable: false
                )
            );
            return 70;
        }
    }

    internal static int ExitCode(string code) =>
        code switch
        {
            "INVALID_INPUT" => 64,
            "ENCRYPTION_INSPECTION_LIMIT" => 65,
            "NOT_FOUND" => 66,
            "IO_ERROR" => 74,
            "SOURCE_CHANGED" => 75,
            "ACCESS_DENIED" => 77,
            _ => 70,
        };

    private static int UsageError(TextWriter error)
    {
        WriteError(
            error,
            new WordToolkitOperationError(
                "INVALID_INPUT",
                "Invalid inspect-encryption arguments",
                Usage,
                Retryable: false
            )
        );
        return 64;
    }

    private static void WriteError(TextWriter error, WordToolkitOperationError operationError)
    {
        error.WriteLine(
            WordToolkitOperationJson.Serialize(
                new WordToolkitOperationErrorEnvelope(false, operationError)
            )
        );
    }
}
