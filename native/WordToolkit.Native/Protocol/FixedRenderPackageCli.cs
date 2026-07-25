using System.Text.Json;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Protocol;

internal static class FixedRenderPackageCli
{
    private const int MaximumRequestCharacters = 256 * 1024;
    private const string Usage =
        "usage: wordtoolkit-native fixed-render-package --request <request.json|-> [--format json]";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextReader input,
        TextWriter output,
        TextWriter error,
        Func<IWordComHost>? hostFactory = null,
        CancellationToken cancellationToken = default
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
            using var document = JsonDocument.Parse(json);
            await using var host = hostFactory?.Invoke() ?? new WordComHost();
            var service = new WordLiveService(host);
            var result = await service.CallAsync(
                FixedRenderWordPackageContract.OperationName,
                document.RootElement,
                cancellationToken
            );
            output.WriteLine(
                JsonSerializer.Serialize(result, JsonDefaults.Indented)
            );
            return 0;
        }
        catch (NativeToolException exception)
        {
            WriteError(
                error,
                new WordToolkit.Engine.Operations.WordToolkitOperationError(
                    exception.ErrorCode,
                    exception.Message,
                    null,
                    exception.Retryable,
                    exception.Details
                )
            );
            return ExitCode(exception.ErrorCode);
        }
        catch (JsonException)
        {
            WriteError(
                error,
                new WordToolkit.Engine.Operations.WordToolkitOperationError(
                    "INVALID_INPUT",
                    "Fixed render request JSON is invalid",
                    null,
                    Retryable: false
                )
            );
            return 64;
        }
        catch (OperationCanceledException)
        {
            WriteError(
                error,
                new WordToolkit.Engine.Operations.WordToolkitOperationError(
                    "CANCELLED",
                    "Fixed rendering was cancelled",
                    null,
                    Retryable: true
                )
            );
            return 75;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException
        )
        {
            WriteError(
                error,
                new WordToolkit.Engine.Operations.WordToolkitOperationError(
                    "NOT_FOUND",
                    "The fixed render request file does not exist",
                    null,
                    Retryable: false
                )
            );
            return 66;
        }
        catch (UnauthorizedAccessException)
        {
            WriteError(
                error,
                new WordToolkit.Engine.Operations.WordToolkitOperationError(
                    "ACCESS_DENIED",
                    "The fixed render request file cannot be read",
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
                new WordToolkit.Engine.Operations.WordToolkitOperationError(
                    "IO_ERROR",
                    "The fixed render request could not be read",
                    null,
                    Retryable: true
                )
            );
            return 74;
        }
    }

    private static string ReadRequestFile(string requestPath)
    {
        var path = Path.GetFullPath(requestPath);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumRequestCharacters * 4L)
        {
            throw new NativeToolException(
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
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Request JSON cannot exceed {MaximumRequestCharacters} characters"
                );
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

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

    private static int ExitCode(string code) => code switch
    {
        "INVALID_INPUT" or "UNSUPPORTED_FORMAT" => 64,
        "NOT_FOUND" or "DOCUMENT_NOT_FOUND" => 66,
        "INVALID_PACKAGE" => 65,
        "VERSION_CONFLICT"
            or "OUTPUT_EXISTS"
            or "RENDER_BACKEND_UNAVAILABLE"
            or "RENDER_VALIDATION_FAILED" => 75,
        "ACCESS_DENIED" or "AUTH_FORBIDDEN" => 77,
        "IO_ERROR" => 74,
        "ROLLBACK_FAILED" => 70,
        _ => 70,
    };

    private static int UsageError(TextWriter error)
    {
        WriteError(
            error,
            new WordToolkit.Engine.Operations.WordToolkitOperationError(
                "INVALID_INPUT",
                "Invalid fixed-render-package arguments",
                Usage,
                Retryable: false
            )
        );
        return 64;
    }

    private static void WriteError(
        TextWriter error,
        WordToolkit.Engine.Operations.WordToolkitOperationError operationError
    ) => error.WriteLine(
        WordToolkit.Engine.Operations.WordToolkitOperationJson.Serialize(
            new WordToolkit.Engine.Operations.WordToolkitOperationErrorEnvelope(
                false,
                operationError
            )
        )
    );
}
