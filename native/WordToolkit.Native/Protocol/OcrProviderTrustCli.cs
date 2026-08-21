using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Extensions;
using WordToolkit.Native.Ocr;

namespace WordToolkit.Native.Protocol;

internal static class OcrProviderTrustCli
{
    internal sealed record RunOptions(OcrProviderTrustPairHooks? Hooks, Func<string, DriveType> DriveTypeResolver)
    {
        internal static RunOptions Default => new(null, root => new DriveInfo(root).DriveType);
    }
    private const int MaximumRequestCharacters = 256 * 1024;
    private static readonly string Usage =
        "usage: wordtoolkit-native ocr-provider-trust --mode <keygen|issue|verify> --request <request.json|-> [--format json]";

    internal static int Run(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error
    )
        => Run(args, input, output, error, RunOptions.Default);

    internal static int Run(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        OcrProviderTrustPairHooks? hooks
    ) => Run(args, input, output, error, new RunOptions(hooks, root => new DriveInfo(root).DriveType));

    internal static int Run(string[] args, TextReader input, TextWriter output, TextWriter error, RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            var parsed = ParseOptions(args);
            var json = parsed.RequestSource == "-"
                ? ReadBounded(input)
                : ReadRequestFile(parsed.RequestSource, options.DriveTypeResolver);
            object result = parsed.Mode switch
            {
                "keygen" => Keygen(ParseKeygenRequest(json), options),
                "issue" => Issue(ParseIssueRequest(json), options),
                "verify" => Verify(ParseVerifyRequest(json), options),
                _ => throw Invalid("The OCR provider trust mode is invalid."),
            };
            output.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Indented));
            return 0;
        }
        catch (WordToolkitExtensionException exception)
        {
            error.WriteLine($"{exception.Code}: {exception.Message}");
            return exception.Code is "OCR_PROVIDER_TRUST_INVALID" or "OCR_PROVIDER_MANIFEST_INVALID"
                ? 64
                : 2;
        }
        catch (OcrProviderTrustPathValidationException exception)
        {
            error.WriteLine($"OCR_PROVIDER_TRUST_INVALID: {exception.Message}");
            return 64;
        }
        catch (OutputAlreadyExistsException exception)
        {
            error.WriteLine($"OCR_PROVIDER_TRUST_OUTPUT_EXISTS: {exception.Message}");
            return 2;
        }
        catch (Exception)
        {
            error.WriteLine("OCR_PROVIDER_TRUST_FAILED: The OCR provider trust operation failed.");
            return 2;
        }
    }

    private static object Keygen(KeygenRequest request, RunOptions options)
    {
        var privateKeyOutput = NewPemOutput(request.PrivateKeyOutputPath, options.DriveTypeResolver);
        var trustStoreOutput = NewJsonOutput(request.TrustStoreOutputPath, options.DriveTypeResolver);
        if (string.Equals(privateKeyOutput, trustStoreOutput, PathComparison())
            || !string.Equals(
                Path.GetDirectoryName(privateKeyOutput),
                Path.GetDirectoryName(trustStoreOutput),
                PathComparison()
            ))
        {
            throw Invalid("Private-key and trust-store outputs must be different files in one directory.");
        }
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trustStore = new OcrProviderTrustStore(
            OcrProviderTrustPolicy.TrustStoreContract,
            [
                new OcrProviderTrustedKey(
                    request.PublisherId,
                    request.KeyId,
                    OcrProviderTrustPolicy.SignatureAlgorithm,
                    Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
                ),
            ]
        );
        var privateKeyBytes = Encoding.ASCII.GetBytes(key.ExportPkcs8PrivateKeyPem());
        var trustStoreBytes = OcrProviderTrustPolicy.SerializeTrustStore(trustStore);
        try
        {
            PublishPair(
                privateKeyOutput,
                privateKeyBytes,
                trustStoreOutput,
                trustStoreBytes
            , options.Hooks, ValidateKeygenPairBytes);
            using var verifier = LoadPrivateKey(privateKeyOutput);
            if (!verifier.ExportSubjectPublicKeyInfo().SequenceEqual(key.ExportSubjectPublicKeyInfo()))
            {
                throw Invalid("The generated OCR provider signing key failed readback verification.");
            }
            return new
            {
                operation_contract = "wordtoolkit.ocr-provider-trust/1.0",
                mode = "keygen",
                publisher_id = request.PublisherId,
                publisher_key_id = request.KeyId,
                signature_algorithm = OcrProviderTrustPolicy.SignatureAlgorithm,
                trust_store_sha256 = Sha256(trustStoreBytes),
                private_key_created = true,
                private_key_returned = false,
                paths_returned = false,
            };
        }
        catch { throw; }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    private static object Issue(IssueRequest request, RunOptions options)
    {
        var executablePath = ExistingFile(request.ExecutablePath, 128L * 1024 * 1024, options.DriveTypeResolver);
        var modelDirectory = ExistingDirectory(request.ModelDirectory, options.DriveTypeResolver);
        var privateKeyPath = ExistingFile(request.PrivateKeyPkcs8PemPath, 64 * 1024, options.DriveTypeResolver);
        var manifestOutput = NewJsonOutput(request.ManifestOutputPath, options.DriveTypeResolver);
        var trustStoreOutput = NewJsonOutput(request.TrustStoreOutputPath, options.DriveTypeResolver);
        if (string.Equals(manifestOutput, trustStoreOutput, PathComparison()))
        {
            throw Invalid("Manifest and trust-store outputs must be different files.");
        }
        if (!string.Equals(
            Path.GetDirectoryName(manifestOutput),
            Path.GetDirectoryName(trustStoreOutput),
            PathComparison()
        ))
        {
            throw Invalid("Manifest and trust-store outputs must share one directory.");
        }
        var runtimeDirectory = Path.GetDirectoryName(executablePath)!;
        if (
            string.Equals(
                runtimeDirectory,
                Path.GetDirectoryName(manifestOutput),
                PathComparison()
            )
            || string.Equals(
                runtimeDirectory,
                Path.GetDirectoryName(privateKeyPath),
                PathComparison()
            )
        )
        {
            throw Invalid(
                "Provider trust files and private signing keys must remain outside the provider runtime directory."
            );
        }

        using var signer = LoadPrivateKey(privateKeyPath);
        var executableSha256 = OcrProviderTrustPolicy.HashFile(
            executablePath,
            128L * 1024 * 1024,
            CancellationToken.None
        );
        var runtimeFiles = OcrProviderTrustPolicy.HashRuntimeFiles(
            executablePath,
            CancellationToken.None
        );
        var manifestModels = new List<OcrProviderManifestModel>();
        foreach (var language in request.Languages.OrderBy(item => item, StringComparer.Ordinal))
        {
            var fileName = language + ".traineddata";
            var modelPath = Path.GetFullPath(Path.Combine(modelDirectory, fileName));
            if (
                !string.Equals(
                    Path.GetDirectoryName(modelPath),
                    Path.TrimEndingDirectorySeparator(modelDirectory),
                    PathComparison()
                )
                || !File.Exists(modelPath)
                || (File.GetAttributes(modelPath) & FileAttributes.ReparsePoint) != 0
            )
            {
                throw Invalid("A requested OCR language model is missing or unsafe.");
            }
            manifestModels.Add(new OcrProviderManifestModel(
                language,
                fileName,
                OcrProviderTrustPolicy.HashFile(
                    modelPath,
                    64L * 1024 * 1024,
                    CancellationToken.None
                )
            ));
        }
        var manifest = OcrProviderTrustPolicy.CreateSignedManifest(
            request.PublisherId,
            request.KeyId,
            request.ProviderVersion,
            Path.GetFileName(executablePath),
            executableSha256,
            runtimeFiles.Select(item => new OcrProviderManifestRuntimeFile(
                item.FileName,
                item.Sha256
            )).ToArray(),
            manifestModels,
            request.IssuedAtUtc,
            request.ExpiresAtUtc,
            signer
        );
        OcrProviderTrustPolicy.ValidateManifestWindowForPublication(
            manifest,
            DateTimeOffset.UtcNow
        );
        var trustStore = new OcrProviderTrustStore(
            OcrProviderTrustPolicy.TrustStoreContract,
            [
                new OcrProviderTrustedKey(
                    request.PublisherId,
                    request.KeyId,
                    OcrProviderTrustPolicy.SignatureAlgorithm,
                    Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())
                ),
            ]
        );
        var manifestBytes = OcrProviderTrustPolicy.SerializeManifest(manifest);
        var trustStoreBytes = OcrProviderTrustPolicy.SerializeTrustStore(trustStore);
        PublishPair(
            manifestOutput,
            manifestBytes,
            trustStoreOutput,
            trustStoreBytes,
            options.Hooks, OcrProviderTrustPolicy.ValidatePublishedPairBytes
        );

        try
        {
            var policy = new OcrProviderTrustPolicy(manifestOutput, trustStoreOutput, driveTypeResolver: options.DriveTypeResolver);
            using var snapshot = policy.Authorize(
                executablePath,
                modelDirectory,
                request.Languages,
                CancellationToken.None
            );
            return Result(snapshot.Binding, request.Languages.Count, issue: true);
        }
        catch { throw; }
    }

    private static object Verify(VerifyRequest request, RunOptions options)
    {
        var policy = new OcrProviderTrustPolicy(
            request.ManifestPath,
            request.TrustStorePath,
            driveTypeResolver: options.DriveTypeResolver
        );
        using var snapshot = policy.Authorize(
            request.ExecutablePath,
            request.ModelDirectory,
            request.Languages,
            CancellationToken.None
        );
        policy.Revalidate(
            snapshot,
            request.ExecutablePath,
            request.ModelDirectory,
            request.Languages,
            CancellationToken.None
        );
        return Result(snapshot.Binding, request.Languages.Count, issue: false);
    }

    private static object Result(
        OcrProviderTrustBinding binding,
        int languageCount,
        bool issue
    ) => new
    {
        operation_contract = "wordtoolkit.ocr-provider-trust/1.0",
        mode = issue ? "issue" : "verify",
        provider_id = binding.ProviderId,
        publisher_id = binding.PublisherId,
        publisher_key_id = binding.PublisherKeyId,
        provider_version = binding.ProviderVersion,
        signature_algorithm = OcrProviderTrustPolicy.SignatureAlgorithm,
        signature_verified = true,
        resource_hashes_verified = true,
        manifest_sha256 = binding.ManifestSha256,
        trust_store_sha256 = binding.TrustStoreSha256,
        provider_binary_sha256 = binding.ExecutableSha256,
        model_set_sha256 = binding.ModelSetSha256,
        runtime_set_sha256 = binding.RuntimeSetSha256,
        runtime_file_count = binding.RuntimeFiles.Count,
        language_count = languageCount,
        paths_returned = false,
        private_key_returned = false,
    };

    private static IssueRequest ParseIssueRequest(string json)
    {
        using var document = Parse(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "executable_path",
            "model_directory",
            "languages",
            "publisher_id",
            "key_id",
            "provider_version",
            "private_key_pkcs8_pem_path",
            "manifest_output_path",
            "trust_store_output_path",
            "issued_at_utc",
            "expires_at_utc"
        );
        return new IssueRequest(
            String(root, "executable_path", 32_767),
            String(root, "model_directory", 32_767),
            Languages(root),
            String(root, "publisher_id", 128),
            String(root, "key_id", 128),
            String(root, "provider_version", 128),
            String(root, "private_key_pkcs8_pem_path", 32_767),
            String(root, "manifest_output_path", 32_767),
            String(root, "trust_store_output_path", 32_767),
            Utc(root, "issued_at_utc"),
            Utc(root, "expires_at_utc")
        );
    }

    private static KeygenRequest ParseKeygenRequest(string json)
    {
        using var document = Parse(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "publisher_id",
            "key_id",
            "private_key_output_path",
            "trust_store_output_path"
        );
        return new KeygenRequest(
            String(root, "publisher_id", 128),
            String(root, "key_id", 128),
            String(root, "private_key_output_path", 32_767),
            String(root, "trust_store_output_path", 32_767)
        );
    }

    private static VerifyRequest ParseVerifyRequest(string json)
    {
        using var document = Parse(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "executable_path",
            "model_directory",
            "languages",
            "manifest_path",
            "trust_store_path"
        );
        return new VerifyRequest(
            String(root, "executable_path", 32_767),
            String(root, "model_directory", 32_767),
            Languages(root),
            String(root, "manifest_path", 32_767),
            String(root, "trust_store_path", 32_767)
        );
    }

    private static Options ParseOptions(string[] args)
    {
        string? mode = null;
        string? request = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option))
            {
                throw Invalid("Duplicate OCR provider trust option.");
            }
            switch (option)
            {
                case "--mode" when index + 1 < args.Length:
                    mode = args[++index];
                    break;
                case "--request" when index + 1 < args.Length:
                    request = args[++index];
                    break;
                case "--format" when index + 1 < args.Length:
                    if (!string.Equals(args[++index], "json", StringComparison.Ordinal))
                    {
                        throw Invalid("Only JSON output is supported.");
                    }
                    break;
                default:
                    throw Invalid(Usage);
            }
        }
        if (mode is not ("keygen" or "issue" or "verify") || string.IsNullOrWhiteSpace(request))
        {
            throw Invalid(Usage);
        }
        return new Options(mode, request);
    }

    private static JsonDocument Parse(string json)
    {
        if (json.Length is < 2 or > MaximumRequestCharacters)
        {
            throw Invalid("The OCR provider trust request exceeds its limit.");
        }
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        }
        catch (JsonException exception)
        {
            throw Invalid("The OCR provider trust request is not strict JSON.", exception);
        }
    }

    private static JsonElement Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("The OCR provider trust request must be an object.");
        }
        return value;
    }

    private static void RequireOnly(JsonElement root, params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name))
            {
                throw Invalid("The OCR provider trust request contains an unknown or duplicate field.");
            }
        }
        if (seen.Count != allowed.Count)
        {
            throw Invalid("The OCR provider trust request is missing a required field.");
        }
    }

    private static string String(JsonElement root, string name, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw Invalid("An OCR provider trust request field has the wrong type.");
        }
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength
            || text.Any(character => char.IsControl(character) || char.IsSurrogate(character)))
        {
            throw Invalid("An OCR provider trust request field is invalid.");
        }
        return text;
    }

    private static IReadOnlyList<string> Languages(JsonElement root)
    {
        if (!root.TryGetProperty("languages", out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < 1 or > 4)
        {
            throw Invalid("OCR provider trust requires one to four languages.");
        }
        var languages = value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .ToArray();
        if (languages.Any(item => item is null || item.Length is < 2 or > 32
            || item.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('_' or '-')))
            || languages.Distinct(StringComparer.Ordinal).Count() != languages.Length)
        {
            throw Invalid("OCR provider trust language identifiers are invalid or duplicate.");
        }
        return languages!;
    }

    private static DateTimeOffset Utc(JsonElement root, string name)
    {
        var text = String(root, name, 64);
        if (!DateTimeOffset.TryParseExact(
            text,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var value
        ))
        {
            throw Invalid("OCR provider trust timestamps require canonical UTC seconds.");
        }
        return value;
    }

    private static ECDsa LoadPrivateKey(string path)
    {
        string pem;
        try
        {
            pem = File.ReadAllText(path, new UTF8Encoding(false, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw Invalid("The OCR provider signing key could not be read safely.", exception);
        }
        try
        {
            var signer = ECDsa.Create();
            signer.ImportFromPem(pem);
            if (signer.KeySize != 256 || !string.Equals(
                signer.ExportParameters(false).Curve.Oid.Value,
                ECCurve.NamedCurves.nistP256.Oid.Value,
                StringComparison.Ordinal
            ))
            {
                signer.Dispose();
                throw new CryptographicException();
            }
            return signer;
        }
        catch (CryptographicException exception)
        {
            throw Invalid("The OCR provider signing key must be unencrypted PKCS#8 ECDSA P-256 PEM.", exception);
        }
    }

    private static string ExistingFile(string path, long maximumBytes, Func<string, DriveType> resolver)
    {
        var fullPath = ExistingLocalPath(path, expectDirectory: false, resolver);
        var length = new FileInfo(fullPath).Length;
        if (length is < 1 || length > maximumBytes)
        {
            throw Invalid("An OCR provider trust input file exceeds its limit.");
        }
        return fullPath;
    }

    private static string ExistingDirectory(string path, Func<string, DriveType> resolver) => ExistingLocalPath(
        path,
        expectDirectory: true,
        resolver
    );

    private static string ExistingLocalPath(string path, bool expectDirectory, Func<string, DriveType> resolver)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw Invalid("OCR provider trust paths must be absolute.");
        }
        var fullPath = Path.GetFullPath(path);
        try { OcrProviderTrustPairCoordinator.ValidateNoReparsePoints(fullPath); }
        catch (OcrProviderTrustPathValidationException) { throw; }
        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal)
            || fullPath.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || (expectDirectory ? !Directory.Exists(fullPath) : !File.Exists(fullPath)))
        {
            throw Invalid("An OCR provider trust path is unavailable or not local.");
        }
        var driveRoot = Path.GetPathRoot(fullPath);
        if (driveRoot is null || resolver(driveRoot) == DriveType.Network)
            throw Invalid("An OCR provider trust path is unavailable or not local.");
        return fullPath;
    }

    private static string NewJsonOutput(string path, Func<string, DriveType> driveTypeResolver)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw Invalid("OCR provider trust outputs require absolute paths.");
        }
        var fullPath = Path.GetFullPath(path);
        try { OcrProviderTrustPairCoordinator.ValidateNoReparsePoints(fullPath); }
        catch (OcrProviderTrustPathValidationException) { throw; }
        var outputRoot = Path.GetPathRoot(fullPath);
        if (outputRoot is null || driveTypeResolver(outputRoot) == DriveType.Network)
            throw Invalid("OCR provider trust outputs must be local files (not local network paths).");
        if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(Path.GetDirectoryName(fullPath)))
        {
            throw Invalid("OCR provider trust outputs must be new JSON files in an existing directory.");
        }
        if (File.Exists(fullPath))
            throw new OutputAlreadyExistsException();
        return fullPath;
    }

    private static string NewPemOutput(string path, Func<string, DriveType> driveTypeResolver)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw Invalid("OCR provider private-key outputs require absolute paths.");
        }
        var fullPath = Path.GetFullPath(path);
        try { OcrProviderTrustPairCoordinator.ValidateNoReparsePoints(fullPath); }
        catch (OcrProviderTrustPathValidationException) { throw; }
        var outputRoot = Path.GetPathRoot(fullPath);
        if (outputRoot is null || driveTypeResolver(outputRoot) == DriveType.Network)
            throw Invalid("OCR provider private-key outputs must be local files (not local network paths).");
        if (!string.Equals(Path.GetExtension(fullPath), ".pem", StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(Path.GetDirectoryName(fullPath)))
        {
            throw Invalid("OCR provider private-key outputs must be new PEM files in an existing directory.");
        }
        if (File.Exists(fullPath))
            throw new OutputAlreadyExistsException();
        return fullPath;
    }

    private static void PublishPair(
        string manifestPath,
        byte[] manifestBytes,
        string trustStorePath,
        byte[] trustStoreBytes,
        OcrProviderTrustPairHooks? hooks = null,
        Func<byte[], byte[], bool>? validator = null
    )
    {
        try
        {
            OcrProviderTrustPairCoordinator.ValidateNoReparsePoints(manifestPath);
            OcrProviderTrustPairCoordinator.ValidateNoReparsePoints(trustStorePath);
        }
        catch (OcrProviderTrustPathValidationException) { throw; }
        using var pairLock = OcrProviderTrustPairCoordinator.Acquire(manifestPath, trustStorePath);
        hooks?.AfterLockAcquired?.Invoke();
        using var directoryLease = OcrProviderTrustPairCoordinator.AcquireStableOutputDirectories(
            manifestPath,
            trustStorePath
        );
        hooks?.AfterDirectoriesLeased?.Invoke();
        try
        {
            OcrProviderTrustPairCoordinator.ValidateNoReparsePoints(manifestPath);
            OcrProviderTrustPairCoordinator.ValidateNoReparsePoints(trustStorePath);
        }
        catch (OcrProviderTrustPathValidationException) { throw; }
        OcrProviderTrustPairCoordinator.Recover(manifestPath, trustStorePath, validator);
        if (File.Exists(manifestPath) || File.Exists(trustStorePath))
            throw new OutputAlreadyExistsException();
        try
        {
            hooks?.BeforeJournalWrite?.Invoke();
            OcrProviderTrustPairCoordinator.WriteJournal(manifestPath, trustStorePath, trustStoreBytes, Guid.NewGuid().ToString("N"), "manifest_store", manifestBytes);
            hooks?.BeforeSecondaryPublish?.Invoke();
            directoryLease.PublishCreateNew(trustStorePath, trustStoreBytes);
            hooks?.AfterSecondaryPublish?.Invoke();
            directoryLease.PublishCreateNew(manifestPath, manifestBytes);
            OcrProviderTrustPairCoordinator.DeleteJournal(manifestPath, trustStorePath);
        }
        catch
        {
            try { OcrProviderTrustPairCoordinator.Recover(manifestPath, trustStorePath); } catch { }
            throw;
        }
    }

    private static bool ValidateKeygenPairBytes(byte[] privatePem, byte[] storeBytes)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(Encoding.ASCII.GetString(privatePem));
            var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            return Encoding.UTF8.GetString(storeBytes).Contains(publicKey, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static string ReadRequestFile(string path, Func<string, DriveType> resolver)
    {
        var fullPath = ExistingFile(path, MaximumRequestCharacters, resolver);
        return File.ReadAllText(fullPath, new UTF8Encoding(false, true));
    }

    private static string ReadBounded(TextReader input)
    {
        var buffer = new char[16 * 1024];
        var result = new StringBuilder();
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            if (result.Length > MaximumRequestCharacters - read)
            {
                throw Invalid("The OCR provider trust request exceeds its limit.");
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static WordToolkitExtensionException Invalid(
        string message,
        Exception? innerException = null
    ) => new("OCR_PROVIDER_TRUST_INVALID", message, innerException: innerException);

    private sealed class OutputAlreadyExistsException()
        : IOException("The OCR provider trust output already exists; existing files are never overwritten.");

    private static string Sha256(ReadOnlySpan<byte> value) => Convert.ToHexString(
        SHA256.HashData(value)
    ).ToLowerInvariant();

    private sealed record Options(string Mode, string RequestSource);

    private sealed record KeygenRequest(
        string PublisherId,
        string KeyId,
        string PrivateKeyOutputPath,
        string TrustStoreOutputPath
    );

    private sealed record IssueRequest(
        string ExecutablePath,
        string ModelDirectory,
        IReadOnlyList<string> Languages,
        string PublisherId,
        string KeyId,
        string ProviderVersion,
        string PrivateKeyPkcs8PemPath,
        string ManifestOutputPath,
        string TrustStoreOutputPath,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc
    );

    private sealed record VerifyRequest(
        string ExecutablePath,
        string ModelDirectory,
        IReadOnlyList<string> Languages,
        string ManifestPath,
        string TrustStorePath
    );
}
