using System.Text.Json;
using System.Text.Json.Serialization;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Native.Protocol;

internal static class QueryPackageCli
{
    private const int MaximumRequestCharacters = 256 * 1_024;
    private const string Usage =
        "usage: wordtoolkit-native query-package --request <query.json|-> [--format json]";

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

            WriteUsageError(error);
            return 64;
        }

        if (string.IsNullOrWhiteSpace(requestSource))
        {
            WriteUsageError(error);
            return 64;
        }

        try
        {
            var requestJson = requestSource == "-"
                ? ReadBounded(input)
                : ReadRequestFile(requestSource);
            var cliRequest = WordToolkitOperationJson.Deserialize<QueryPackageCliRequest>(
                requestJson
            );
            var result = new QueryWordPackageOperation().Execute(
                new QueryWordPackageRequest(
                    cliRequest.LocalPath,
                    cliRequest.ToSemanticQuery(),
                    cliRequest.ExpectedPackageFingerprint,
                    cliRequest.IncludeSensitiveProperties
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
        catch (JsonException)
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "INVALID_INPUT",
                    "Request JSON is invalid or contains unsupported fields",
                    Reason: null,
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
                    "The requested query JSON file does not exist",
                    Reason: null,
                    Retryable: false
                )
            );
            return 66;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            WriteError(
                error,
                new WordToolkitOperationError(
                    "INVALID_INPUT",
                    "The query JSON path is invalid",
                    Reason: null,
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
                    "The query JSON file cannot be read with current permissions",
                    Reason: null,
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
                    "The query JSON could not be read",
                    Reason: null,
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
                    "The semantic query operation failed",
                    Reason: null,
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

    internal static int ExitCode(string errorCode) =>
        errorCode switch
        {
            "INVALID_INPUT" => 64,
            "NOT_FOUND" => 66,
            "INVALID_PACKAGE" or "INVALID_WORD_PACKAGE" or "PACKAGE_LIMIT" => 65,
            "VERSION_CONFLICT" or "TARGET_NOT_FOUND" or "SOURCE_CHANGED" => 75,
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
                "Invalid query-package arguments",
                Usage,
                Retryable: false
            )
        );
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record QueryPackageCliRequest
    {
        public required string LocalPath { get; init; }

        public string? ExpectedPackageFingerprint { get; init; }

        public IReadOnlyCollection<WordSemanticNodeKind>? Kinds { get; init; }

        public string? Text { get; init; }

        public WordSemanticTextMatchMode TextMatch { get; init; } =
            WordSemanticTextMatchMode.Contains;

        public WordSemanticTextScope TextScope { get; init; } =
            WordSemanticTextScope.Node;

        public bool CaseSensitive { get; init; }

        public IReadOnlyDictionary<string, string>? PropertyEquals { get; init; }

        public QueryPackageRelatedPredicate? Ancestor { get; init; }

        public QueryPackageRelatedPredicate? Descendant { get; init; }

        public string? WithinNodeId { get; init; }

        public string? SourcePartUri { get; init; }

        public int Offset { get; init; }

        public int MaxResults { get; init; } = 80;

        public int TextPreviewChars { get; init; } = 160;

        public bool IncludeProperties { get; init; }

        public bool IncludeSensitiveProperties { get; init; }

        public bool IncludeSource { get; init; }

        public WordSemanticQuery ToSemanticQuery() =>
            new()
            {
                Kinds = Kinds,
                Text = Text,
                TextMatch = TextMatch,
                TextScope = TextScope,
                CaseSensitive = CaseSensitive,
                PropertyEquals = PropertyEquals,
                Ancestor = Ancestor?.ToSemanticPredicate(),
                Descendant = Descendant?.ToSemanticPredicate(),
                WithinNodeId = WithinNodeId is null
                    ? null
                    : new SemanticNodeId(WithinNodeId),
                SourcePartUri = SourcePartUri,
                Offset = Offset,
                Limit = MaxResults,
                TextPreviewCharacters = TextPreviewChars,
                IncludeProperties = IncludeProperties,
                IncludeSource = IncludeSource,
            };
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record QueryPackageRelatedPredicate
    {
        public IReadOnlyCollection<WordSemanticNodeKind>? Kinds { get; init; }

        public IReadOnlyDictionary<string, string>? PropertyEquals { get; init; }

        public WordSemanticRelatedNodePredicate ToSemanticPredicate() =>
            new()
            {
                Kinds = Kinds,
                PropertyEquals = PropertyEquals,
            };
    }
}
