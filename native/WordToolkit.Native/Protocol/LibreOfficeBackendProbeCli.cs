using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.LibreOffice;

namespace WordToolkit.Native.Protocol;

internal static class LibreOfficeBackendProbeCli
{
    private const string Usage =
        "usage: wordtoolkit-native libreoffice-backend --request <request.json|-> [--format json]";

    internal static async Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        ILibreOfficeBackendProbeProvider? provider = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var requestPath = Parse(args);
            var json = ReadRequest(requestPath, input);
            var request = LibreOfficeBackendProbeOperationJson.ParseRequest(json);
            var result = await new InspectLibreOfficeBackendOperation(
                    provider ?? new LibreOfficeBackendProbeProvider()
                )
                .ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            output.WriteLine(WordToolkitOperationJson.Serialize(result));
            return 0;
        }
        catch (WordToolkitOperationException exception)
        {
            error.WriteLine(
                WordToolkitOperationJson.Serialize(
                    new WordToolkitOperationErrorEnvelope(
                        false,
                        WordToolkitOperationError.FromException(exception)
                    )
                )
            );
            return ExitCode(exception.Code);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine(
                WordToolkitOperationJson.Serialize(
                    new WordToolkitOperationErrorEnvelope(
                        false,
                        new WordToolkitOperationError(
                            "CANCELLED",
                            "The LibreOffice backend probe was cancelled",
                            null,
                            true
                        )
                    )
                )
            );
            return 75;
        }
        catch (Exception exception)
        {
            error.WriteLine(
                WordToolkitOperationJson.Serialize(
                    new WordToolkitOperationErrorEnvelope(
                        false,
                        new WordToolkitOperationError(
                            "INTERNAL_ERROR",
                            "The LibreOffice backend probe failed",
                            exception.GetType().Name,
                            false
                        )
                    )
                )
            );
            return 70;
        }
    }

    private static string Parse(string[] args)
    {
        string? request = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--request" when index + 1 < args.Length:
                    if (request is not null)
                    {
                        throw Invalid("--request can be supplied only once");
                    }
                    request = args[++index];
                    break;
                case "--format" when index + 1 < args.Length:
                    if (!string.Equals(args[++index], "json", StringComparison.Ordinal))
                    {
                        throw Invalid("--format must be json");
                    }
                    break;
                case "--help":
                case "-h":
                    throw Invalid(Usage);
                default:
                    throw Invalid($"Invalid libreoffice-backend arguments. {Usage}");
            }
        }
        return string.IsNullOrWhiteSpace(request)
            ? throw Invalid($"--request is required. {Usage}")
            : request;
    }

    private static string ReadRequest(string requestPath, TextReader input)
    {
        if (requestPath == "-")
        {
            return ReadBounded(input);
        }
        try
        {
            var fullPath = Path.GetFullPath(requestPath);
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            if (stream.Length
                > LibreOfficeBackendProbeContract.MaximumRequestJsonCharacters * 4L)
            {
                throw Invalid(
                    $"Request JSON cannot exceed {LibreOfficeBackendProbeContract.MaximumRequestJsonCharacters} characters"
                );
            }
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true
            );
            return ReadBounded(reader);
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_INPUT",
                "The LibreOffice backend request file cannot be read",
                innerException: exception
            );
        }
    }

    private static string ReadBounded(TextReader reader)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            if (result.Length + read
                > LibreOfficeBackendProbeContract.MaximumRequestJsonCharacters)
            {
                throw Invalid(
                    $"Request JSON cannot exceed {LibreOfficeBackendProbeContract.MaximumRequestJsonCharacters} characters"
                );
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private static int ExitCode(string code) => code switch
    {
        "INVALID_INPUT" => 64,
        "OUTPUT_LIMIT" => 65,
        "NOT_FOUND" => 66,
        "BACKEND_UNAVAILABLE" or "INVALID_BACKEND" => 69,
        "IO_ERROR" => 74,
        "BACKEND_TIMEOUT" or "EXECUTABLE_DRIFT" => 75,
        "ACCESS_DENIED" => 77,
        "EXECUTABLE_MISMATCH" => 78,
        _ => 70,
    };

    private static WordToolkitOperationException Invalid(string message) =>
        new("INVALID_INPUT", message);
}
