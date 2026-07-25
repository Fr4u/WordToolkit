using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

internal static class RelationshipRepairPackageCli
{
    private const string Usage =
        "usage: wordtoolkit-native relationship-repair-package --mode <inspect|plan|apply> --request <request.json|-> [--format json]";

    public static int Run(
        IReadOnlyList<string> arguments,
        TextReader input,
        TextWriter output,
        TextWriter error
    )
    {
        if (arguments.Count == 1 && arguments[0] is "--help" or "-h")
        {
            output.WriteLine(Usage);
            return 0;
        }
        string? mode = null;
        string? requestSource = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument == "--mode"
                && TryValue(arguments, ref index, out var candidateMode)
                && mode is null
                && candidateMode is "inspect" or "plan" or "apply")
            {
                mode = candidateMode;
                continue;
            }
            if (argument == "--request"
                && TryValue(arguments, ref index, out var candidateSource)
                && requestSource is null)
            {
                requestSource = candidateSource;
                continue;
            }
            if (argument == "--format"
                && TryValue(arguments, ref index, out var format)
                && string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            return UsageError(error);
        }
        if (mode is null || string.IsNullOrWhiteSpace(requestSource))
        {
            return UsageError(error);
        }

        try
        {
            var json = requestSource == "-"
                ? ReadBounded(input)
                : ReadRequestFile(requestSource);
            var operation = new RelationshipRepairWordPackageOperation(
                NativeExtensionHost.CandidateValidator
            );
            object result = mode switch
            {
                "inspect" => operation.Inspect(
                    RelationshipRepairOperationJson.ParseInspectRequest(json)
                ),
                "plan" => operation.Plan(
                    RelationshipRepairOperationJson.ParsePlanRequest(json)
                ),
                _ => operation.Apply(
                    RelationshipRepairOperationJson.ParseApplyRequest(json)
                ),
            };
            output.WriteLine(WordToolkitOperationJson.Serialize(result, indented: true));
            return 0;
        }
        catch (WordToolkitOperationException exception)
        {
            WriteError(error, WordToolkitOperationError.FromException(exception));
            return ExitCode(exception.Code);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException
        )
        {
            WriteError(error, new WordToolkitOperationError(
                "NOT_FOUND",
                "The requested relationship repair JSON file does not exist",
                null,
                Retryable: false
            ));
            return 66;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            WriteError(error, new WordToolkitOperationError(
                "INVALID_INPUT",
                "The relationship repair JSON path is invalid",
                null,
                Retryable: false
            ));
            return 64;
        }
        catch (UnauthorizedAccessException)
        {
            WriteError(error, new WordToolkitOperationError(
                "ACCESS_DENIED",
                "The relationship repair JSON file cannot be read",
                null,
                Retryable: false
            ));
            return 77;
        }
        catch (IOException)
        {
            WriteError(error, new WordToolkitOperationError(
                "IO_ERROR",
                "The relationship repair JSON could not be read",
                null,
                Retryable: true
            ));
            return 74;
        }
        catch (Exception)
        {
            WriteError(error, new WordToolkitOperationError(
                "INTERNAL_ERROR",
                "The relationship repair operation failed",
                null,
                Retryable: false
            ));
            return 70;
        }
    }

    private static string ReadRequestFile(string requestPath)
    {
        var path = Path.GetFullPath(requestPath);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length
            > RelationshipRepairWordPackageContract.MaximumRequestJsonCharacters * 4L)
        {
            throw new WordToolkitOperationException(
                "INVALID_INPUT",
                $"Request JSON cannot exceed {RelationshipRepairWordPackageContract.MaximumRequestJsonCharacters} characters"
            );
        }
        using var reader = new StreamReader(
            stream,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false
        );
        return ReadBounded(reader);
    }

    private static string ReadBounded(TextReader reader)
    {
        var buffer = new char[8_192];
        var result = new System.Text.StringBuilder();
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            if (result.Length + read
                > RelationshipRepairWordPackageContract.MaximumRequestJsonCharacters)
            {
                throw new WordToolkitOperationException(
                    "INVALID_INPUT",
                    $"Request JSON cannot exceed {RelationshipRepairWordPackageContract.MaximumRequestJsonCharacters} characters"
                );
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private static int ExitCode(string code) => code switch
    {
        "INVALID_INPUT" => 64,
        "NOT_FOUND" => 66,
        "INVALID_PACKAGE" or "INVALID_WORD_PACKAGE" or "PACKAGE_LIMIT" => 65,
        "VERSION_CONFLICT" or "PLAN_MISMATCH" or "TRANSACTION_LIMIT" => 75,
        "ACCESS_DENIED" => 77,
        "IO_ERROR" => 74,
        "SIGNED_PACKAGE" or "VALIDATOR_REQUIRED" or "OOXML_SCHEMA_INVALID"
            or "VALIDATION_FAILED" or "UNSAFE_REPAIR" or "RECOVERY_REQUIRED"
            or "EXTERNAL_RELATIONSHIP_AUTHORIZATION_REQUIRED" => 78,
        _ => 70,
    };

    private static bool TryValue(
        IReadOnlyList<string> arguments,
        ref int index,
        out string value
    )
    {
        if (index + 1 >= arguments.Count)
        {
            value = string.Empty;
            return false;
        }
        value = arguments[++index];
        return true;
    }

    private static int UsageError(TextWriter error)
    {
        WriteError(error, new WordToolkitOperationError(
            "INVALID_INPUT",
            "Invalid relationship-repair-package arguments",
            Usage,
            Retryable: false
        ));
        return 64;
    }

    private static void WriteError(
        TextWriter error,
        WordToolkitOperationError operationError
    ) => error.WriteLine(WordToolkitOperationJson.Serialize(
        new WordToolkitOperationErrorEnvelope(false, operationError)
    ));
}
