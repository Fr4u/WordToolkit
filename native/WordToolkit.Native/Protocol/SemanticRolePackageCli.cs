using System.Text;
using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

internal static class SemanticRolePackageCli
{
    private const string Usage =
        "usage: wordtoolkit-native semantic-role-package --request <request.json|-> [--format json]";

    public static int Run(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error
    )
    {
        try
        {
            var requestPath = Parse(args);
            var json = ReadRequest(requestPath, input);
            var request = SemanticRoleOperationJson.ParseInspectRequest(json);
            var result = new SemanticRoleWordPackageOperation().Inspect(request);
            output.WriteLine(WordToolkitOperationJson.Serialize(result));
            return 0;
        }
        catch (WordToolkitOperationException exception)
        {
            error.WriteLine(WordToolkitOperationJson.Serialize(new
            {
                error = new
                {
                    code = exception.Code,
                    message = exception.Message,
                    reason = exception.Reason,
                    retryable = exception.Retryable,
                },
            }));
            return exception.Code == "INVALID_INPUT" ? 64 : 1;
        }
        catch (Exception exception)
        {
            error.WriteLine(WordToolkitOperationJson.Serialize(new
            {
                error = new
                {
                    code = "INTERNAL_ERROR",
                    message = "Semantic-role inspection failed",
                    reason = exception.GetType().Name,
                    retryable = false,
                },
            }));
            return 1;
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
                    throw Invalid($"Invalid semantic-role-package arguments. {Usage}");
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
                > SemanticRoleWordPackageContract.MaximumRequestJsonCharacters * 4L)
            {
                throw Invalid(
                    $"Request JSON cannot exceed {SemanticRoleWordPackageContract.MaximumRequestJsonCharacters} characters"
                );
            }
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
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
                "The semantic-role request file cannot be read",
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
                > SemanticRoleWordPackageContract.MaximumRequestJsonCharacters)
            {
                throw Invalid(
                    $"Request JSON cannot exceed {SemanticRoleWordPackageContract.MaximumRequestJsonCharacters} characters"
                );
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private static WordToolkitOperationException Invalid(string message) =>
        new("INVALID_INPUT", message);
}
