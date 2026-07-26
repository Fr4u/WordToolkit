using System.Text.Json;
using System.Text.RegularExpressions;
using WordToolkit.Engine.Extensions;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Ocr;

internal sealed record OcrProviderHostIdentity(
    string ExecutableSha256,
    string AssemblySha256
);

internal sealed record OcrProviderHostRequest(
    string Protocol,
    string RequestId,
    byte[] ImageBytes,
    string ContentType,
    string ImageSha256,
    IReadOnlyList<string> Languages,
    WordOcrLayoutHint LayoutHint,
    int TimeoutMilliseconds,
    int MaximumOutputCharacters,
    WordOcrProviderConfiguration Configuration,
    string HostExecutableSha256,
    string HostAssemblySha256,
    OcrProviderTrustBinding TrustBinding
)
{
    internal WordOcrProviderRequest ToProviderRequest() => new(
        ImageBytes,
        ContentType,
        ImageSha256,
        Languages,
        LayoutHint,
        TimeoutMilliseconds,
        MaximumOutputCharacters,
        Configuration
    );
}

internal sealed record OcrProviderHostResponse(
    bool Ok,
    WordOcrProviderResult? Result,
    string? ErrorCode,
    bool Retryable
);

internal static partial class OcrProviderHostProtocol
{
    internal const string Contract = "wordtoolkit.ocr-provider-host/1.0";
    internal const int MaximumRequestCharacters = 48 * 1024 * 1024;
    internal const int MaximumResponseCharacters = 8 * 1024 * 1024;
    internal const int MaximumDiagnosticCharacters = 4096;

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RequestIdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodePattern();

    internal static string NewRequestId() => Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)
        )
        .ToLowerInvariant();

    internal static string SerializeRequest(
        WordOcrProviderRequest request,
        string requestId,
        OcrProviderHostIdentity identity,
        OcrProviderTrustBinding trustBinding
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(trustBinding);
        OcrProviderTrustPolicy.ValidateBinding(trustBinding);
        var value = new OcrProviderHostRequest(
            Contract,
            requestId,
            request.ImageBytes.ToArray(),
            request.ContentType,
            request.ImageSha256,
            request.Languages,
            request.LayoutHint,
            request.TimeoutMilliseconds,
            request.MaximumOutputCharacters,
            request.Configuration,
            identity.ExecutableSha256,
            identity.AssemblySha256,
            trustBinding
        );
        var json = WordToolkitOperationJson.Serialize(value);
        if (json.Length > MaximumRequestCharacters)
        {
            throw Error(
                "EXTENSION_LIMIT_EXCEEDED",
                "The OCR process-host request exceeds its closed IPC limit."
            );
        }
        return json;
    }

    internal static OcrProviderHostRequest ParseRequest(string json)
    {
        using var document = Parse(json, MaximumRequestCharacters);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "protocol",
            "request_id",
            "image_bytes",
            "content_type",
            "image_sha256",
            "languages",
            "layout_hint",
            "timeout_milliseconds",
            "maximum_output_characters",
            "configuration",
            "host_executable_sha256",
            "host_assembly_sha256",
            "trust_binding"
        );
        var protocol = RequiredString(root, "protocol", 128);
        if (!string.Equals(protocol, Contract, StringComparison.Ordinal))
        {
            throw Invalid("The OCR process-host protocol is unsupported.");
        }
        var requestId = RequiredString(root, "request_id", 32);
        if (!RequestIdPattern().IsMatch(requestId))
        {
            throw Invalid("The OCR process-host request identity is invalid.");
        }
        var configurationObject = Object(
            Required(root, "configuration"),
            "configuration"
        );
        RequireOnly(configurationObject, "executable_path", "model_directory");
        var request = new OcrProviderHostRequest(
            protocol,
            requestId,
            RequiredBytes(root, "image_bytes", 32 * 1024 * 1024),
            RequiredString(root, "content_type", 128),
            RequiredSha256(root, "image_sha256"),
            RequiredStrings(root, "languages", 4, 128),
            RequiredLayoutHint(root, "layout_hint"),
            RequiredInt32(root, "timeout_milliseconds"),
            RequiredInt32(root, "maximum_output_characters"),
            new WordOcrProviderConfiguration(
                OptionalString(configurationObject, "executable_path", 32_767),
                OptionalString(configurationObject, "model_directory", 32_767)
            ),
            RequiredSha256(root, "host_executable_sha256"),
            RequiredSha256(root, "host_assembly_sha256"),
            ParseTrustBinding(Required(root, "trust_binding"))
        );
        OcrProviderTrustPolicy.ValidateBinding(request.TrustBinding);
        _ = request.ToProviderRequest();
        return request;
    }

    private static OcrProviderTrustBinding ParseTrustBinding(JsonElement value)
    {
        var binding = Object(value, "trust_binding");
        RequireOnly(
            binding,
            "contract",
            "provider_id",
            "publisher_id",
            "publisher_key_id",
            "provider_version",
            "executable_file_name",
            "executable_sha256",
            "runtime_set_sha256",
            "runtime_files",
            "model_set_sha256",
            "models",
            "manifest_sha256",
            "trust_store_sha256"
        );
        var runtimeFiles = new List<OcrProviderTrustRuntimeFileBinding>();
        foreach (var runtimeValue in RequiredArray(binding, "runtime_files", 512))
        {
            var file = Object(runtimeValue, "trust_binding runtime file");
            RequireOnly(file, "file_name", "sha256");
            runtimeFiles.Add(new OcrProviderTrustRuntimeFileBinding(
                RequiredString(file, "file_name", 128),
                RequiredSha256(file, "sha256")
            ));
        }
        var models = new List<OcrProviderTrustModelBinding>();
        foreach (var valueModel in RequiredArray(binding, "models", 64))
        {
            var model = Object(valueModel, "trust_binding model");
            RequireOnly(model, "language", "file_name", "sha256");
            models.Add(new OcrProviderTrustModelBinding(
                RequiredString(model, "language", 32),
                RequiredString(model, "file_name", 128),
                RequiredSha256(model, "sha256")
            ));
        }
        return new OcrProviderTrustBinding(
            RequiredString(binding, "contract", 128),
            RequiredString(binding, "provider_id", 128),
            RequiredString(binding, "publisher_id", 128),
            RequiredString(binding, "publisher_key_id", 128),
            RequiredString(binding, "provider_version", 128),
            RequiredString(binding, "executable_file_name", 128),
            RequiredSha256(binding, "executable_sha256"),
            RequiredSha256(binding, "runtime_set_sha256"),
            runtimeFiles.AsReadOnly(),
            RequiredSha256(binding, "model_set_sha256"),
            models.AsReadOnly(),
            RequiredSha256(binding, "manifest_sha256"),
            RequiredSha256(binding, "trust_store_sha256")
        );
    }

    internal static string SerializeSuccess(
        string requestId,
        WordOcrProviderResult result
    ) => BoundedSerialize(new
    {
        protocol = Contract,
        request_id = requestId,
        ok = true,
        result,
    });

    internal static string SerializeError(
        string requestId,
        string errorCode,
        bool retryable
    )
    {
        if (!RequestIdPattern().IsMatch(requestId))
        {
            requestId = new string('0', 32);
        }
        if (!ErrorCodePattern().IsMatch(errorCode))
        {
            errorCode = "EXTENSION_EXECUTION_FAILED";
            retryable = false;
        }
        return BoundedSerialize(new
        {
            protocol = Contract,
            request_id = requestId,
            ok = false,
            error = new { code = errorCode, retryable },
        });
    }

    internal static OcrProviderHostResponse ParseResponse(
        string json,
        string expectedRequestId
    )
    {
        using var document = Parse(json, MaximumResponseCharacters);
        var root = Object(document.RootElement, "response");
        RequireOnly(root, "protocol", "request_id", "ok", "result", "error");
        if (
            !string.Equals(
                RequiredString(root, "protocol", 128),
                Contract,
                StringComparison.Ordinal
            )
            || !string.Equals(
                RequiredString(root, "request_id", 32),
                expectedRequestId,
                StringComparison.Ordinal
            )
        )
        {
            throw Error(
                "EXTENSION_PROTOCOL_VIOLATION",
                "The OCR process host returned an unbound response."
            );
        }
        var ok = RequiredBoolean(root, "ok");
        if (ok)
        {
            if (root.ContainsKey("error") || !root.TryGetValue("result", out var result))
            {
                throw ProtocolViolation();
            }
            ValidateResultShape(result);
            WordOcrProviderResult parsed;
            try
            {
                parsed = WordToolkitOperationJson.Deserialize<WordOcrProviderResult>(
                    result.GetRawText()
                );
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw Error(
                    "EXTENSION_PROTOCOL_VIOLATION",
                    "The OCR process host returned an invalid typed result.",
                    innerException: exception
                );
            }
            return new OcrProviderHostResponse(true, parsed, null, false);
        }

        if (root.ContainsKey("result") || !root.TryGetValue("error", out var errorValue))
        {
            throw ProtocolViolation();
        }
        var error = Object(errorValue, "error");
        RequireOnly(error, "code", "retryable");
        var code = RequiredString(error, "code", 64);
        if (!ErrorCodePattern().IsMatch(code))
        {
            throw ProtocolViolation();
        }
        return new OcrProviderHostResponse(
            false,
            null,
            code,
            RequiredBoolean(error, "retryable")
        );
    }

    private static void ValidateResultShape(JsonElement value)
    {
        var result = Object(value, "result");
        RequireOnly(
            result,
            "image_width_pixels",
            "image_height_pixels",
            "text",
            "lines",
            "warnings",
            "provenance"
        );
        _ = RequiredInt32(result, "image_width_pixels");
        _ = RequiredInt32(result, "image_height_pixels");
        _ = RequiredString(result, "text", 4_000_000);
        _ = RequiredStrings(result, "warnings", 64, 512);
        var lines = RequiredArray(result, "lines", 20_000);
        foreach (var lineValue in lines)
        {
            var line = Object(lineValue, "line");
            RequireOnly(line, "text", "confidence", "bounds", "words");
            _ = RequiredString(line, "text", 1_000_000);
            OptionalFiniteNumber(line, "confidence");
            ValidateBounds(Required(line, "bounds"));
            foreach (var wordValue in RequiredArray(line, "words", 100_000))
            {
                var word = Object(wordValue, "word");
                RequireOnly(word, "text", "confidence", "bounds");
                _ = RequiredString(word, "text", 65_536);
                OptionalFiniteNumber(word, "confidence");
                ValidateBounds(Required(word, "bounds"));
            }
        }
        var provenance = Object(Required(result, "provenance"), "provenance");
        RequireOnly(
            provenance,
            "provider_name",
            "provider_version",
            "provider_binary_sha256",
            "model_set_sha256",
            "effective_languages",
            "confidence_scale",
            "network_used",
            "deterministic_for_bound_inputs"
        );
        _ = RequiredString(provenance, "provider_name", 128);
        _ = RequiredString(provenance, "provider_version", 512);
        _ = RequiredSha256(provenance, "provider_binary_sha256");
        _ = RequiredSha256(provenance, "model_set_sha256");
        _ = RequiredStrings(provenance, "effective_languages", 4, 128);
        _ = RequiredString(provenance, "confidence_scale", 256);
        _ = RequiredBoolean(provenance, "network_used");
        _ = RequiredBoolean(provenance, "deterministic_for_bound_inputs");
    }

    private static void ValidateBounds(JsonElement value)
    {
        var bounds = Object(value, "bounds");
        RequireOnly(bounds, "left", "top", "width", "height");
        _ = RequiredInt32(bounds, "left");
        _ = RequiredInt32(bounds, "top");
        _ = RequiredInt32(bounds, "width");
        _ = RequiredInt32(bounds, "height");
    }

    private static string BoundedSerialize(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonDefaults.Compact);
        if (json.Length > MaximumResponseCharacters)
        {
            throw Error(
                "EXTENSION_LIMIT_EXCEEDED",
                "The OCR process-host response exceeds its closed IPC limit."
            );
        }
        return json;
    }

    private static JsonDocument Parse(string json, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > maximumCharacters)
        {
            throw Invalid("The OCR process-host JSON is empty or exceeds its limit.");
        }
        try
        {
            return JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                }
            );
        }
        catch (JsonException exception)
        {
            throw Invalid("The OCR process-host JSON is malformed.", exception);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Object(
        JsonElement value,
        string name
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"The OCR process-host {name} must be an object.");
        }
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Invalid(
                    $"The OCR process-host {name} contains a duplicate field."
                );
            }
        }
        return result;
    }

    private static void RequireOnly(
        IReadOnlyDictionary<string, JsonElement> value,
        params string[] allowed
    )
    {
        foreach (var property in value.Keys)
        {
            if (!allowed.Contains(property, StringComparer.Ordinal))
            {
                throw Invalid(
                    "The OCR process-host payload contains an unsupported field."
                );
            }
        }
    }

    private static JsonElement Required(
        IReadOnlyDictionary<string, JsonElement> value,
        string property
    ) => value.TryGetValue(property, out var result)
        ? result
        : throw Invalid("The OCR process-host payload is missing a required field.");

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> value,
        string property,
        int maximumCharacters
    )
    {
        var element = Required(value, property);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid("The OCR process-host payload contains an invalid string.");
        }
        var result = element.GetString() ?? string.Empty;
        if (result.Length > maximumCharacters)
        {
            throw Invalid("The OCR process-host string exceeds its limit.");
        }
        return result;
    }

    private static string? OptionalString(
        IReadOnlyDictionary<string, JsonElement> value,
        string property,
        int maximumCharacters
    )
    {
        if (!value.TryGetValue(property, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid("The OCR process-host payload contains an invalid optional string.");
        }
        var result = element.GetString();
        if (result?.Length > maximumCharacters)
        {
            throw Invalid("The OCR process-host string exceeds its limit.");
        }
        return result;
    }

    private static string RequiredSha256(
        IReadOnlyDictionary<string, JsonElement> value,
        string property
    )
    {
        var result = RequiredString(value, property, 64);
        if (
            result.Length != 64
            || result.Any(character => !Uri.IsHexDigit(character))
        )
        {
            throw Invalid("The OCR process-host hash is invalid.");
        }
        return result.ToLowerInvariant();
    }

    private static byte[] RequiredBytes(
        IReadOnlyDictionary<string, JsonElement> value,
        string property,
        int maximumBytes
    )
    {
        var element = Required(value, property);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid("The OCR process-host byte payload is invalid.");
        }
        byte[] result;
        try
        {
            result = element.GetBytesFromBase64();
        }
        catch (FormatException exception)
        {
            throw Invalid("The OCR process-host byte payload is malformed.", exception);
        }
        if (result.Length is < 1 || result.Length > maximumBytes)
        {
            throw Invalid("The OCR process-host byte payload exceeds its limit.");
        }
        return result;
    }

    private static IReadOnlyList<string> RequiredStrings(
        IReadOnlyDictionary<string, JsonElement> value,
        string property,
        int maximumItems,
        int maximumCharacters
    )
    {
        var array = RequiredArray(value, property, maximumItems);
        return Array.AsReadOnly(array
            .Select(item =>
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw Invalid("The OCR process-host string array is invalid.");
                }
                var text = item.GetString() ?? string.Empty;
                if (text.Length > maximumCharacters)
                {
                    throw Invalid("The OCR process-host string exceeds its limit.");
                }
                return text;
            })
            .ToArray());
    }

    private static JsonElement[] RequiredArray(
        IReadOnlyDictionary<string, JsonElement> value,
        string property,
        int maximumItems
    )
    {
        var element = Required(value, property);
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("The OCR process-host payload contains an invalid array.");
        }
        var result = element.EnumerateArray().ToArray();
        if (result.Length > maximumItems)
        {
            throw Invalid("The OCR process-host array exceeds its limit.");
        }
        return result;
    }

    private static int RequiredInt32(
        IReadOnlyDictionary<string, JsonElement> value,
        string property
    )
    {
        var element = Required(value, property);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var result))
        {
            throw Invalid("The OCR process-host payload contains an invalid integer.");
        }
        return result;
    }

    private static bool RequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> value,
        string property
    )
    {
        var element = Required(value, property);
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid("The OCR process-host payload contains an invalid boolean."),
        };
    }

    private static WordOcrLayoutHint RequiredLayoutHint(
        IReadOnlyDictionary<string, JsonElement> value,
        string property
    ) => RequiredString(value, property, 32) switch
    {
        "automatic" => WordOcrLayoutHint.Automatic,
        "single_block" => WordOcrLayoutHint.SingleBlock,
        "sparse_text" => WordOcrLayoutHint.SparseText,
        "single_line" => WordOcrLayoutHint.SingleLine,
        "single_word" => WordOcrLayoutHint.SingleWord,
        _ => throw Invalid("The OCR process-host layout hint is unsupported."),
    };

    private static void OptionalFiniteNumber(
        IReadOnlyDictionary<string, JsonElement> value,
        string property
    )
    {
        if (!value.TryGetValue(property, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return;
        }
        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var number)
            || !double.IsFinite(number)
        )
        {
            throw Invalid("The OCR process-host payload contains an invalid number.");
        }
    }

    private static WordToolkitExtensionException ProtocolViolation() => Error(
        "EXTENSION_PROTOCOL_VIOLATION",
        "The OCR process host returned a response that violates its closed contract."
    );

    private static WordToolkitExtensionException Invalid(
        string message,
        Exception? innerException = null
    ) => Error("EXTENSION_PROTOCOL_VIOLATION", message, innerException: innerException);

    private static WordToolkitExtensionException Error(
        string code,
        string message,
        bool retryable = false,
        Exception? innerException = null
    ) => new(code, message, retryable, innerException);
}
