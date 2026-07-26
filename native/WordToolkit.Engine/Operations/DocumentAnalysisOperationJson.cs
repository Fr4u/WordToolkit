using System.Text.Json;

namespace WordToolkit.Engine.Operations;

public static class DocumentAnalysisOperationJson
{
    public static DocumentAnalysisRequest ParseRequest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > DocumentAnalysisWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {DocumentAnalysisWordPackageContract.MaximumRequestJsonCharacters} characters"
            );
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                }
            );
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Request must be an object");
            }

            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!properties.TryAdd(property.Name, property.Value))
                {
                    throw Invalid($"Request contains duplicate field '{property.Name}'");
                }
                if (property.Name is not (
                    "local_path" or "expected_package_fingerprint" or "max_signals"
                ))
                {
                    throw Invalid($"Request contains unsupported field '{property.Name}'");
                }
            }

            if (!properties.TryGetValue("local_path", out var path)
                || path.ValueKind != JsonValueKind.String)
            {
                throw Invalid("local_path must be a string");
            }

            string? fingerprint = null;
            if (properties.TryGetValue("expected_package_fingerprint", out var fingerprintNode))
            {
                if (fingerprintNode.ValueKind != JsonValueKind.String)
                {
                    throw Invalid("expected_package_fingerprint must be a string");
                }
                fingerprint = fingerprintNode.GetString();
            }

            var maxSignals = DocumentAnalysisWordPackageContract.DefaultMaxSignals;
            if (properties.TryGetValue("max_signals", out var maximumNode))
            {
                if (maximumNode.ValueKind != JsonValueKind.Number
                    || !maximumNode.TryGetInt32(out maxSignals))
                {
                    throw Invalid("max_signals must be a 32-bit integer");
                }
            }

            return new DocumentAnalysisRequest(
                path.GetString() ?? string.Empty,
                fingerprint,
                maxSignals
            );
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Invalid(
                "Request JSON is malformed or exceeds the depth limit",
                exception
            );
        }
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
