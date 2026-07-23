using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Rendering;

namespace WordToolkit.Native.Protocol;

internal static class RenderPackageCli
{
    private const int MaximumRequestCharacters = 256 * 1024;
    private const string Usage =
        "usage: wordtoolkit-native render-package --request <request.json|-> [--format json]";

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

        string? requestSource = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (
                argument == "--request"
                && TryValue(arguments, ref index, out var candidate)
                && requestSource is null
            )
            {
                requestSource = candidate;
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
        if (string.IsNullOrWhiteSpace(requestSource))
        {
            return UsageError(error);
        }

        try
        {
            var json = requestSource == "-"
                ? ReadBounded(input)
                : ReadRequestFile(requestSource);
            var result = new SemanticHtmlWordPackageOperation().Execute(
                SemanticHtmlWordPackageJson.ParseRequest(json)
            );
            output.WriteLine(WordToolkitOperationJson.Serialize(result, indented: true));
            return 0;
        }
        catch (WordToolkitOperationException exception)
        {
            WriteError(error, WordToolkitOperationError.FromException(exception));
            return ExitCode(exception.Code);
        }
        catch (JsonException)
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "INVALID_INPUT",
                    "Request JSON is invalid or contains unsupported fields",
                    null,
                    Retryable: false
                )
            );
            return 64;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException
        )
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "NOT_FOUND",
                    "The requested render JSON file does not exist",
                    null,
                    Retryable: false
                )
            );
            return 66;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "INVALID_INPUT",
                    "The render JSON path is invalid",
                    null,
                    Retryable: false
                )
            );
            return 64;
        }
        catch (UnauthorizedAccessException)
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "ACCESS_DENIED",
                    "The render JSON file cannot be read with current permissions",
                    null,
                    Retryable: false
                )
            );
            return 77;
        }
        catch (IOException)
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "IO_ERROR",
                    "The render JSON could not be read",
                    null,
                    Retryable: true
                )
            );
            return 74;
        }
        catch (Exception)
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "INTERNAL_ERROR",
                    "The semantic HTML render operation failed",
                    null,
                    Retryable: false
                )
            );
            return 70;
        }
    }

    private static string ReadRequestFile(string requestPath)
    {
        var path = Path.GetFullPath(requestPath);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumRequestCharacters * 4L)
        {
            throw new WordToolkitOperationException(
                "INVALID_INPUT",
                $"Request JSON cannot exceed {MaximumRequestCharacters} characters"
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
            if (result.Length + read > MaximumRequestCharacters)
            {
                throw new WordToolkitOperationException(
                    "INVALID_INPUT",
                    $"Request JSON cannot exceed {MaximumRequestCharacters} characters"
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
        "VERSION_CONFLICT" or "OUTPUT_EXISTS" => 75,
        "ACCESS_DENIED" => 77,
        "IO_ERROR" => 74,
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
        WriteError(
            error,
            new WordToolkitOperationError(
                "INVALID_INPUT",
                "Invalid render-package arguments",
                Usage,
                Retryable: false
            )
        );
        return 64;
    }

    private static void WriteError(
        TextWriter error,
        WordToolkitOperationError operationError
    ) =>
        error.WriteLine(
            WordToolkitOperationJson.Serialize(
                new WordToolkitOperationErrorEnvelope(false, operationError)
            )
        );
}
