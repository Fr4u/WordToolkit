using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WordToolkit.Engine.Operations;

public static class LibreOfficeBackendProbeOperationJson
{
    public static InspectLibreOfficeBackendRequest ParseRequest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > LibreOfficeBackendProbeContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {LibreOfficeBackendProbeContract.MaximumRequestJsonCharacters} characters"
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
                    MaxDepth = 8,
                }
            );
            return ParseRequest(document.RootElement);
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Invalid("Request JSON is malformed or exceeds the depth limit", exception);
        }
    }

    public static InspectLibreOfficeBackendRequest ParseRequest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Request must be an object");
        }
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not (
                "executable_path"
                or "expected_executable_sha256"
                or "timeout_milliseconds"
            ))
            {
                throw Invalid($"Request contains unsupported field '{property.Name}'");
            }
            if (!fields.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"Request contains duplicate field '{property.Name}'");
            }
        }
        if (!fields.TryGetValue("executable_path", out var pathNode)
            || pathNode.ValueKind != JsonValueKind.String)
        {
            throw Invalid("executable_path is required and must be a string");
        }
        string? expectedHash = null;
        if (fields.TryGetValue("expected_executable_sha256", out var hashNode))
        {
            if (hashNode.ValueKind != JsonValueKind.String)
            {
                throw Invalid("expected_executable_sha256 must be a string");
            }
            expectedHash = hashNode.GetString();
        }
        var timeout = LibreOfficeBackendProbeContract.DefaultTimeoutMilliseconds;
        if (fields.TryGetValue("timeout_milliseconds", out var timeoutNode)
            && (timeoutNode.ValueKind != JsonValueKind.Number
                || !timeoutNode.TryGetInt32(out timeout)))
        {
            throw Invalid("timeout_milliseconds must be a 32-bit integer");
        }
        return new InspectLibreOfficeBackendRequest(
            pathNode.GetString() ?? string.Empty,
            expectedHash,
            timeout
        );
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}

public sealed class InspectLibreOfficeBackendOperation
{
    private static readonly Regex VersionValuePattern = new(
        "^[0-9][0-9A-Za-z.+-]{1,63}$",
        RegexOptions.CultureInvariant
    );
    private static readonly IReadOnlyList<string> Limitations = Array.AsReadOnly(
        new[]
        {
            "version_probe_only",
            "not_a_process_sandbox",
            "no_uno_connection_proof",
            "no_writer_component_proof",
            "no_document_load_policy_proof",
            "no_macro_execution_policy_proof",
            "no_external_update_policy_proof",
            "no_rendering_proof",
            "no_word_fidelity_claim",
            "no_vendor_signature_or_authenticity_proof",
            "no_atomic_executable_handle_binding",
        }
    );
    private readonly ILibreOfficeBackendProbeProvider _provider;

    public InspectLibreOfficeBackendOperation(ILibreOfficeBackendProbeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public async Task<InspectLibreOfficeBackendResult> ExecuteAsync(
        InspectLibreOfficeBackendRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = Validate(request);
        var started = Stopwatch.GetTimestamp();
        var observation = await _provider.ProbeAsync(
                new LibreOfficeBackendProbeProviderRequest(
                    normalized.ExecutablePath,
                    normalized.ExpectedExecutableSha256,
                    normalized.TimeoutMilliseconds,
                    LibreOfficeBackendProbeContract.MaximumExecutableBytes,
                    LibreOfficeBackendProbeContract.MaximumProcessOutputCharacters
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
        ValidateObservation(observation, normalized.ExpectedExecutableSha256);
        return new InspectLibreOfficeBackendResult(
            LibreOfficeBackendProbeContract.Contract,
            Available: true,
            new LibreOfficeBackendIdentity(
                observation.Product,
                observation.Version,
                observation.VersionBanner,
                observation.ExecutableFileName,
                observation.ExecutableBytes,
                observation.ExecutableSha256,
                observation.ExecutableHashStable,
                ExpectedExecutableHashEnforced: normalized.ExpectedExecutableSha256 is not null
            ),
            new LibreOfficeBackendHost(
                observation.OperatingSystem,
                observation.OperatingSystemArchitecture,
                observation.ProcessArchitecture
            ),
            new LibreOfficeBackendCapabilities(
                VersionProbeVerified: true,
                UnoConnectionVerified: false,
                WriterComponentVerified: false,
                WriterPdfExportVerified: false,
                DocumentLoadPolicyVerified: false,
                MacroExecutionPrevented: false,
                ExternalUpdatesPrevented: false,
                RenderingVerified: false,
                WordFidelityClaimed: false
            ),
            new LibreOfficeBackendProbeSecurity(
                ReadsDocument: false,
                ReturnsDocumentContent: false,
                OpensMicrosoftWord: false,
                DocumentArgumentsSupplied: false,
                ProfileCreatedByWordToolkit: false,
                PathSearchUsed: false,
                NetworkRequested: false,
                NetworkIsolationEnforced: false,
                StdinClosed: true,
                ProcessTreeTerminationOnTimeout: true,
                ExecutablePathReturned: false,
                EnvironmentValuesReturned: false,
                ArgumentsFixed: true
            ),
            Limitations,
            Runtime: "dotnet-native",
            PythonUsed: false,
            new LibreOfficeBackendProbePerformance(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds
            )
        );
    }

    private static InspectLibreOfficeBackendRequest Validate(
        InspectLibreOfficeBackendRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.ExecutablePath)
            || request.ExecutablePath.Length
                > LibreOfficeBackendProbeContract.MaximumExecutablePathCharacters
            || request.ExecutablePath.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw Invalid("executable_path must be a bounded non-empty local path");
        }
        if (!Path.IsPathFullyQualified(request.ExecutablePath))
        {
            throw Invalid("executable_path must be an absolute path; PATH search is forbidden");
        }
        string? expectedHash = null;
        if (request.ExpectedExecutableSha256 is not null)
        {
            if (!IsSha256(request.ExpectedExecutableSha256))
            {
                throw Invalid("expected_executable_sha256 must be exactly 64 hexadecimal characters");
            }
            expectedHash = request.ExpectedExecutableSha256.ToLowerInvariant();
        }
        if (request.TimeoutMilliseconds
                is < LibreOfficeBackendProbeContract.MinimumTimeoutMilliseconds
                or > LibreOfficeBackendProbeContract.MaximumTimeoutMilliseconds)
        {
            throw Invalid(
                $"timeout_milliseconds must be between {LibreOfficeBackendProbeContract.MinimumTimeoutMilliseconds} and {LibreOfficeBackendProbeContract.MaximumTimeoutMilliseconds}"
            );
        }
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(request.ExecutablePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_INPUT",
                "executable_path is not a valid absolute path",
                innerException: exception
            );
        }
        return request with
        {
            ExecutablePath = fullPath,
            ExpectedExecutableSha256 = expectedHash,
        };
    }

    private static void ValidateObservation(
        LibreOfficeBackendProbeObservation observation,
        string? expectedHash
    )
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Product is not ("LibreOffice" or "LibreOfficeDev")
            || !VersionValuePattern.IsMatch(observation.Version)
            || string.IsNullOrWhiteSpace(observation.VersionBanner)
            || observation.VersionBanner.Length > 256
            || string.IsNullOrWhiteSpace(observation.ExecutableFileName)
            || observation.ExecutableFileName.Length > 512
            || observation.ExecutableFileName.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\r', '\n', '\0']
            ) >= 0
            || observation.ExecutableBytes < 1
            || observation.ExecutableBytes
                > LibreOfficeBackendProbeContract.MaximumExecutableBytes
            || !IsSha256(observation.ExecutableSha256)
            || !observation.ExecutableHashStable
            || observation.OperatingSystem is not ("windows" or "linux" or "macos" or "other")
            || !IsBoundedIdentity(observation.OperatingSystemArchitecture, 32)
            || !IsBoundedIdentity(observation.ProcessArchitecture, 32))
        {
            throw new WordToolkitOperationException(
                "INVALID_BACKEND",
                "The LibreOffice probe provider returned incomplete identity evidence"
            );
        }
        if (expectedHash is not null
            && !string.Equals(
                expectedHash,
                observation.ExecutableSha256,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
                "EXECUTABLE_MISMATCH",
                "The LibreOffice executable does not match the expected SHA-256"
            );
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsBoundedIdentity(string value, int maximumCharacters) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumCharacters
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-');

    private static WordToolkitOperationException Invalid(string message) =>
        new("INVALID_INPUT", message);
}
