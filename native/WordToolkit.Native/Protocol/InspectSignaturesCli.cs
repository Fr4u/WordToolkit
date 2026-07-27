using System.Globalization;
using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

internal static class InspectSignaturesCli
{
    private const string Usage =
        "usage: wordtoolkit-native inspect-signatures <path> [--view summary|signatures|references|issues] [--signature-id <id>] [--offset <n>] [--limit <1..100>] [--include-source] [--include-certificate-hash] [--format json]";

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

        var path = arguments[0];
        var view = "summary";
        string? signatureId = null;
        var offset = 0;
        var limit = InspectOoxmlSignaturesContract.DefaultLimit;
        var includeSource = false;
        var includeCertificateHash = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (!seen.Add(option))
            {
                return UsageError(error);
            }
            switch (option)
            {
                case "--view":
                    if (!TryReadValue(arguments, ref index, out view))
                    {
                        return UsageError(error);
                    }
                    break;
                case "--signature-id":
                    if (!TryReadValue(arguments, ref index, out signatureId))
                    {
                        return UsageError(error);
                    }
                    break;
                case "--offset":
                    if (!TryReadInt(arguments, ref index, out offset))
                    {
                        return UsageError(error);
                    }
                    break;
                case "--limit":
                    if (!TryReadInt(arguments, ref index, out limit))
                    {
                        return UsageError(error);
                    }
                    break;
                case "--include-source":
                    includeSource = true;
                    break;
                case "--include-certificate-hash":
                    includeCertificateHash = true;
                    break;
                case "--format":
                    if (
                        !TryReadValue(arguments, ref index, out var format)
                        || !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return UsageError(error);
                    }
                    break;
                default:
                    return UsageError(error);
            }
        }

        try
        {
            var result = new InspectOoxmlSignaturesOperation().Execute(
                new InspectOoxmlSignaturesRequest(
                    path,
                    view,
                    signatureId,
                    offset,
                    limit,
                    includeSource,
                    includeCertificateHash
                )
            );
            output.WriteLine(WordToolkitOperationJson.Serialize(result, indented: true));
            return 0;
        }
        catch (WordToolkitOperationException exception)
        {
            WriteError(error, WordToolkitOperationError.FromException(exception));
            return exception.Code switch
            {
                "INVALID_INPUT" => 64,
                "SIGNATURE_INSPECTION_LIMIT" or "PACKAGE_LIMIT" => 65,
                "NOT_FOUND" => 66,
                "ACCESS_DENIED" => 77,
                "PACKAGE_INVALID" => 74,
                _ => 70,
            };
        }
        catch (Exception)
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "INTERNAL_ERROR",
                    "The OOXML digital-signature inspection operation failed",
                    Reason: null,
                    Retryable: false
                )
            );
            return 70;
        }
    }

    private static bool TryReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        out string value
    )
    {
        value = string.Empty;
        if (index + 1 >= arguments.Count
            || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }
        value = arguments[++index];
        return value.Length > 0;
    }

    private static bool TryReadInt(
        IReadOnlyList<string> arguments,
        ref int index,
        out int value
    )
    {
        value = 0;
        return TryReadValue(arguments, ref index, out var raw)
            && int.TryParse(
                raw,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value
            );
    }

    private static int UsageError(TextWriter error)
    {
        WriteError(
            error,
            new WordToolkitOperationError(
                "INVALID_INPUT",
                "Invalid inspect-signatures arguments",
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
