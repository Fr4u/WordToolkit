using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordToolkit.Engine.Extensions;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Ocr;

internal sealed record OcrProviderTrustModelBinding(
    string Language,
    string FileName,
    string Sha256
);

internal sealed record OcrProviderTrustRuntimeFileBinding(
    string FileName,
    string Sha256
);

internal sealed record OcrProviderTrustBinding(
    string Contract,
    string ProviderId,
    string PublisherId,
    string PublisherKeyId,
    string ProviderVersion,
    string ExecutableFileName,
    string ExecutableSha256,
    string RuntimeSetSha256,
    IReadOnlyList<OcrProviderTrustRuntimeFileBinding> RuntimeFiles,
    string ModelSetSha256,
    IReadOnlyList<OcrProviderTrustModelBinding> Models,
    string ManifestSha256,
    string TrustStoreSha256
);

internal sealed class OcrProviderTrustSnapshot : IDisposable
{
    private OcrProviderResourceLease? _resourceLease;

    internal OcrProviderTrustSnapshot(
        OcrProviderTrustBinding binding,
        string manifestPath,
        string trustStorePath,
        DateTimeOffset expiresAtUtc,
        OcrProviderResourceLease resourceLease
    )
    {
        Binding = binding;
        ManifestPath = manifestPath;
        TrustStorePath = trustStorePath;
        ExpiresAtUtc = expiresAtUtc;
        _resourceLease = resourceLease;
    }

    internal OcrProviderTrustBinding Binding { get; }

    internal string ManifestPath { get; }

    internal string TrustStorePath { get; }

    internal DateTimeOffset ExpiresAtUtc { get; }

    internal void VerifyDirectorySet() => _resourceLease?.VerifyDirectorySet();

    public void Dispose() => Interlocked.Exchange(ref _resourceLease, null)?.Dispose();
}

internal interface IOcrProviderTrustPolicy
{
    OcrProviderTrustSnapshot Authorize(
        string executablePath,
        string modelDirectory,
        IReadOnlyList<string> languages,
        CancellationToken cancellationToken
    );

    void Revalidate(
        OcrProviderTrustSnapshot snapshot,
        string executablePath,
        string modelDirectory,
        IReadOnlyList<string> languages,
        CancellationToken cancellationToken
    );
}

internal sealed partial class OcrProviderTrustPolicy : IOcrProviderTrustPolicy
{
    internal const string ManifestEnvironmentVariable =
        "WORDTOOLKIT_OCR_PROVIDER_MANIFEST_PATH";
    internal const string TrustStoreEnvironmentVariable =
        "WORDTOOLKIT_OCR_TRUST_STORE_PATH";
    internal const string BindingContract = "wordtoolkit.ocr-provider-trust-binding/1.0";
    internal const string ManifestContract = "wordtoolkit.ocr-provider-manifest/1.0";
    internal const string TrustStoreContract = "wordtoolkit.ocr-trust-store/1.0";
    internal const string SignatureAlgorithm = "ecdsa-p256-sha256-p1363";
    private const int MaximumManifestBytes = 256 * 1024;
    private const int MaximumTrustStoreBytes = 1024 * 1024;
    private const int MaximumModels = 64;
    private const int MaximumRuntimeFiles = 512;
    private static readonly TimeSpan MaximumManifestLifetime = TimeSpan.FromDays(366);
    private readonly string _manifestPath;
    private readonly string _trustStorePath;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string, DriveType> _driveTypeResolver;

    internal OcrProviderTrustPolicy(
        string manifestPath,
        string trustStorePath,
        Func<DateTimeOffset>? utcNow = null,
        Func<string, DriveType>? driveTypeResolver = null
    )
    {
        _manifestPath = ResolveConfigurationFile(manifestPath);
        _trustStorePath = ResolveConfigurationFile(trustStorePath);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _driveTypeResolver = driveTypeResolver ?? (root => new DriveInfo(root).DriveType);
    }

    internal static OcrProviderTrustPolicy FromEnvironment()
    {
        var manifestPath = Environment.GetEnvironmentVariable(ManifestEnvironmentVariable);
        var trustStorePath = Environment.GetEnvironmentVariable(TrustStoreEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(trustStorePath))
        {
            throw Error(
                "OCR_PROVIDER_TRUST_NOT_CONFIGURED",
                "Local OCR requires a signed provider manifest and a host-owned trust store."
            );
        }
        return new OcrProviderTrustPolicy(manifestPath, trustStorePath);
    }

    public OcrProviderTrustSnapshot Authorize(
        string executablePath,
        string modelDirectory,
        IReadOnlyList<string> languages,
        CancellationToken cancellationToken
    )
    {
        using var pairLock = OcrProviderTrustPairCoordinator.Acquire(_manifestPath, _trustStorePath);
        OcrProviderTrustPairCoordinator.Recover(_manifestPath, _trustStorePath);
        return AuthorizeCore(executablePath, modelDirectory, languages, cancellationToken);
    }

    private OcrProviderTrustSnapshot AuthorizeCore(
        string executablePath,
        string modelDirectory,
        IReadOnlyList<string> languages,
        CancellationToken cancellationToken
    )
    {
        ValidateConfigurationFiles();
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        ArgumentNullException.ThrowIfNull(languages);
        cancellationToken.ThrowIfCancellationRequested();

        var manifestFile = ReadConfigurationFile(
            _manifestPath,
            MaximumManifestBytes,
            cancellationToken
        );
        var trustStoreFile = ReadConfigurationFile(
            _trustStorePath,
            MaximumTrustStoreBytes,
            cancellationToken
        );
        var manifest = ParseManifest(manifestFile.Bytes);
        var trustStore = ParseTrustStore(trustStoreFile.Bytes);
        ValidateManifestWindow(manifest);
        VerifyManifestSignature(manifest, trustStore);

        if (
            !string.Equals(
                manifest.ProviderId,
                TesseractCliOcrProvider.ExtensionId,
                StringComparison.Ordinal
            )
            || !string.Equals(
                manifest.InterfaceContract,
                WordOcrProviderContract.InterfaceContract,
                StringComparison.Ordinal
            )
            || !string.Equals(
                manifest.InterfaceVersion,
                WordOcrProviderContract.InterfaceVersion,
                StringComparison.Ordinal
            )
        )
        {
            throw Error(
                "OCR_PROVIDER_MANIFEST_INCOMPATIBLE",
                "The signed OCR provider manifest targets an unsupported provider contract."
            );
        }
        if (
            !string.Equals(
                Path.GetFileName(executablePath),
                manifest.ExecutableFileName,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            )
        )
        {
            throw IdentityMismatch();
        }

        var runtimeFiles = manifest.RuntimeFiles
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new OcrProviderTrustRuntimeFileBinding(
                item.FileName,
                item.Sha256
            ))
            .ToArray();

        var manifestModels = manifest.Models.ToDictionary(
            item => item.Language,
            StringComparer.Ordinal
        );
        var selected = new List<OcrProviderTrustModelBinding>(languages.Count);
        var selectedLanguages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in languages.OrderBy(item => item, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!selectedLanguages.Add(language) || !manifestModels.TryGetValue(language, out var model))
            {
                throw Error(
                    "OCR_PROVIDER_MODEL_NOT_TRUSTED",
                    "A requested OCR language model is not authorized by the signed provider manifest."
                );
            }
            var expectedFileName = language + ".traineddata";
            if (!string.Equals(model.FileName, expectedFileName, StringComparison.Ordinal))
            {
                throw Error(
                    "OCR_PROVIDER_MANIFEST_INVALID",
                    "The signed OCR provider manifest contains an invalid model binding."
                );
            }
            var modelPath = Path.GetFullPath(Path.Combine(modelDirectory, model.FileName));
            if (
                !string.Equals(
                    Path.GetDirectoryName(modelPath),
                    Path.TrimEndingDirectorySeparator(modelDirectory),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal
                )
                || !File.Exists(modelPath)
                || (File.GetAttributes(modelPath) & FileAttributes.ReparsePoint) != 0
            )
            {
                throw IdentityMismatch();
            }
            selected.Add(new OcrProviderTrustModelBinding(
                model.Language,
                model.FileName,
                model.Sha256
            ));
        }
        if (selected.Count == 0)
        {
            throw Error(
                "OCR_PROVIDER_MODEL_NOT_TRUSTED",
                "At least one signed OCR language model is required."
            );
        }

        var binding = new OcrProviderTrustBinding(
            BindingContract,
            manifest.ProviderId,
            manifest.PublisherId,
            manifest.KeyId,
            manifest.ProviderVersion,
            manifest.ExecutableFileName,
            manifest.ExecutableSha256,
            RuntimeSetHash(runtimeFiles),
            runtimeFiles,
            ModelSetHash(selected),
            selected.AsReadOnly(),
            manifestFile.Sha256,
            trustStoreFile.Sha256
        );
        var resourceLease = OcrProviderResourceLease.Acquire(
            executablePath,
            modelDirectory,
            binding
        );
        return new OcrProviderTrustSnapshot(
            binding,
            _manifestPath,
            _trustStorePath,
            manifest.ExpiresAtUtc,
            resourceLease
        );
    }

    public void Revalidate(
        OcrProviderTrustSnapshot snapshot,
        string executablePath,
        string modelDirectory,
        IReadOnlyList<string> languages,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using var current = Authorize(
            executablePath,
            modelDirectory,
            languages,
            cancellationToken
        );
        if (
            !string.Equals(snapshot.ManifestPath, current.ManifestPath, StringComparison.Ordinal)
            || !string.Equals(snapshot.TrustStorePath, current.TrustStorePath, StringComparison.Ordinal)
            || !BindingEquals(snapshot.Binding, current.Binding)
        )
        {
            throw Error(
                "OCR_PROVIDER_TRUST_CHANGED",
                "The signed OCR provider identity or host trust policy changed during recognition.",
                retryable: true
            );
        }
    }

    internal static void ValidateBinding(OcrProviderTrustBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (
            !string.Equals(binding.Contract, BindingContract, StringComparison.Ordinal)
            || !IdentifierPattern().IsMatch(binding.ProviderId)
            || !IdentifierPattern().IsMatch(binding.PublisherId)
            || !IdentifierPattern().IsMatch(binding.PublisherKeyId)
            || !SafeScalar(binding.ProviderVersion, 128)
            || !LeafFileName(binding.ExecutableFileName)
            || !Sha256Pattern().IsMatch(binding.ExecutableSha256)
            || !Sha256Pattern().IsMatch(binding.RuntimeSetSha256)
            || !Sha256Pattern().IsMatch(binding.ModelSetSha256)
            || !Sha256Pattern().IsMatch(binding.ManifestSha256)
            || !Sha256Pattern().IsMatch(binding.TrustStoreSha256)
            || binding.Models.Count is < 1 or > MaximumModels
            || binding.RuntimeFiles.Count is < 1 or > MaximumRuntimeFiles
        )
        {
            throw Error(
                "EXTENSION_PROTOCOL_VIOLATION",
                "The OCR process-host trust binding is invalid."
            );
        }
        var runtimeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in binding.RuntimeFiles)
        {
            if (
                !LeafFileName(file.FileName)
                || !runtimeNames.Add(file.FileName)
                || !Sha256Pattern().IsMatch(file.Sha256)
            )
            {
                throw Error(
                    "EXTENSION_PROTOCOL_VIOLATION",
                    "The OCR process-host runtime trust binding is invalid."
                );
            }
        }
        var boundExecutable = binding.RuntimeFiles.SingleOrDefault(file =>
            string.Equals(
                file.FileName,
                binding.ExecutableFileName,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (boundExecutable is null
            || !FixedEquals(boundExecutable.Sha256, binding.ExecutableSha256))
        {
            throw Error(
                "EXTENSION_PROTOCOL_VIOLATION",
                "The OCR process-host executable trust binding is inconsistent."
            );
        }
        if (!FixedEquals(RuntimeSetHash(binding.RuntimeFiles), binding.RuntimeSetSha256))
        {
            throw Error(
                "EXTENSION_PROTOCOL_VIOLATION",
                "The OCR process-host runtime-set trust binding is inconsistent."
            );
        }
        var languages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in binding.Models)
        {
            if (
                !LanguagePattern().IsMatch(model.Language)
                || !languages.Add(model.Language)
                || !LeafFileName(model.FileName)
                || !string.Equals(
                    model.FileName,
                    model.Language + ".traineddata",
                    StringComparison.Ordinal
                )
                || !Sha256Pattern().IsMatch(model.Sha256)
            )
            {
                throw Error(
                    "EXTENSION_PROTOCOL_VIOLATION",
                    "The OCR process-host model trust binding is invalid."
                );
            }
        }
        if (!FixedEquals(ModelSetHash(binding.Models), binding.ModelSetSha256))
        {
            throw Error(
                "EXTENSION_PROTOCOL_VIOLATION",
                "The OCR process-host model-set trust binding is inconsistent."
            );
        }
    }

    internal static OcrProviderManifest CreateSignedManifest(
        string publisherId,
        string keyId,
        string providerVersion,
        string executableFileName,
        string executableSha256,
        IReadOnlyList<OcrProviderManifestRuntimeFile> runtimeFiles,
        IReadOnlyList<OcrProviderManifestModel> models,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        ECDsa signer
    )
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(runtimeFiles);
        ArgumentNullException.ThrowIfNull(signer);
        var unsigned = new OcrProviderManifest(
            ManifestContract,
            TesseractCliOcrProvider.ExtensionId,
            publisherId,
            keyId,
            WordOcrProviderContract.InterfaceContract,
            WordOcrProviderContract.InterfaceVersion,
            providerVersion,
            executableFileName,
            executableSha256,
            runtimeFiles.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToArray(),
            models.OrderBy(item => item.Language, StringComparer.Ordinal).ToArray(),
            issuedAtUtc.ToUniversalTime(),
            expiresAtUtc.ToUniversalTime(),
            SignatureAlgorithm,
            ""
        );
        ValidateManifestShape(unsigned);
        if (signer.KeySize != 256)
        {
            throw Error(
                "OCR_PROVIDER_MANIFEST_INVALID",
                "OCR provider manifests require an ECDSA P-256 signing key."
            );
        }
        var signature = signer.SignData(
            CanonicalManifestPayload(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );
        return unsigned with { Signature = Convert.ToBase64String(signature) };
    }

    internal static byte[] SerializeManifest(OcrProviderManifest manifest)
    {
        ValidateManifestShape(manifest);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteManifest(writer, manifest, includeSignature: true);
        }
        return stream.ToArray();
    }

    internal static byte[] SerializeTrustStore(OcrProviderTrustStore trustStore)
    {
        ValidateTrustStoreShape(trustStore);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", trustStore.Schema);
            writer.WriteStartArray("keys");
            foreach (var key in trustStore.Keys.OrderBy(item => item.PublisherId, StringComparer.Ordinal)
                         .ThenBy(item => item.KeyId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("publisher_id", key.PublisherId);
                writer.WriteString("key_id", key.KeyId);
                writer.WriteString("algorithm", key.Algorithm);
                writer.WriteString("subject_public_key_info_base64", key.SubjectPublicKeyInfoBase64);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private void ValidateManifestWindow(OcrProviderManifest manifest)
    {
        var now = _utcNow().ToUniversalTime();
        if (
            manifest.IssuedAtUtc > now
            || manifest.ExpiresAtUtc <= now
            || manifest.ExpiresAtUtc <= manifest.IssuedAtUtc
            || manifest.ExpiresAtUtc - manifest.IssuedAtUtc > MaximumManifestLifetime
        )
        {
            throw Error(
                "OCR_PROVIDER_MANIFEST_EXPIRED",
                "The signed OCR provider manifest is not valid at the current UTC time."
            );
        }
    }

    private static void VerifyManifestSignature(
        OcrProviderManifest manifest,
        OcrProviderTrustStore trustStore
    )
    {
        var key = trustStore.Keys.SingleOrDefault(item =>
            string.Equals(item.PublisherId, manifest.PublisherId, StringComparison.Ordinal)
            && string.Equals(item.KeyId, manifest.KeyId, StringComparison.Ordinal)
        ) ?? throw Error(
            "OCR_PROVIDER_PUBLISHER_NOT_TRUSTED",
            "The OCR provider publisher key is not present in the host trust store."
        );

        byte[] subjectPublicKeyInfo;
        byte[] signature;
        try
        {
            subjectPublicKeyInfo = DecodeCanonicalBase64(key.SubjectPublicKeyInfoBase64);
            signature = DecodeCanonicalBase64(manifest.Signature);
        }
        catch (FormatException exception)
        {
            throw Error(
                "OCR_PROVIDER_MANIFEST_INVALID",
                "The OCR provider trust material contains invalid canonical base64.",
                innerException: exception
            );
        }
        if (signature.Length != 64)
        {
            throw Error(
                "OCR_PROVIDER_MANIFEST_INVALID",
                "The OCR provider manifest signature has an invalid size."
            );
        }

        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            if (bytesRead != subjectPublicKeyInfo.Length || verifier.KeySize != 256)
            {
                throw new CryptographicException("Unexpected public-key shape.");
            }
            var parameters = verifier.ExportParameters(includePrivateParameters: false);
            if (!string.Equals(
                parameters.Curve.Oid.Value,
                ECCurve.NamedCurves.nistP256.Oid.Value,
                StringComparison.Ordinal
            ))
            {
                throw new CryptographicException("Unexpected elliptic curve.");
            }
            if (!verifier.VerifyData(
                CanonicalManifestPayload(manifest),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation
            ))
            {
                throw Error(
                    "OCR_PROVIDER_SIGNATURE_INVALID",
                    "The OCR provider manifest signature is invalid."
                );
            }
        }
        catch (WordToolkitExtensionException)
        {
            throw;
        }
        catch (CryptographicException exception)
        {
            throw Error(
                "OCR_PROVIDER_TRUST_STORE_INVALID",
                "The host OCR trust store contains an invalid publisher key.",
                innerException: exception
            );
        }
    }

    private static OcrProviderManifest ParseManifest(byte[] bytes)
    {
        using var document = ParseJson(bytes, MaximumManifestBytes, "provider manifest");
        var root = RequireObject(document.RootElement, "provider manifest");
        RequireOnly(root,
            "schema", "provider_id", "publisher_id", "key_id",
            "interface_contract", "interface_version", "provider_version",
            "executable_file_name", "executable_sha256", "models",
            "runtime_files",
            "issued_at_utc", "expires_at_utc", "signature_algorithm", "signature"
        );
        var runtimeElement = Required(root, "runtime_files");
        if (runtimeElement.ValueKind != JsonValueKind.Array
            || runtimeElement.GetArrayLength() is < 1 or > MaximumRuntimeFiles)
        {
            throw ManifestInvalid("The signed OCR provider manifest has an invalid runtime file list.");
        }
        var runtimeFiles = new List<OcrProviderManifestRuntimeFile>();
        foreach (var item in runtimeElement.EnumerateArray())
        {
            var file = RequireObject(item, "provider runtime file");
            RequireOnly(file, "file_name", "sha256");
            runtimeFiles.Add(new OcrProviderManifestRuntimeFile(
                RequiredString(file, "file_name", 128),
                RequiredSha256(file, "sha256")
            ));
        }
        var modelsElement = Required(root, "models");
        if (modelsElement.ValueKind != JsonValueKind.Array
            || modelsElement.GetArrayLength() is < 1 or > MaximumModels)
        {
            throw ManifestInvalid("The signed OCR provider manifest has an invalid model list.");
        }
        var models = new List<OcrProviderManifestModel>();
        foreach (var item in modelsElement.EnumerateArray())
        {
            var model = RequireObject(item, "provider model");
            RequireOnly(model, "language", "file_name", "sha256");
            models.Add(new OcrProviderManifestModel(
                RequiredString(model, "language", 32),
                RequiredString(model, "file_name", 128),
                RequiredSha256(model, "sha256")
            ));
        }
        var manifest = new OcrProviderManifest(
            RequiredString(root, "schema", 128),
            RequiredString(root, "provider_id", 128),
            RequiredString(root, "publisher_id", 128),
            RequiredString(root, "key_id", 128),
            RequiredString(root, "interface_contract", 128),
            RequiredString(root, "interface_version", 32),
            RequiredString(root, "provider_version", 128),
            RequiredString(root, "executable_file_name", 128),
            RequiredSha256(root, "executable_sha256"),
            runtimeFiles.AsReadOnly(),
            models.AsReadOnly(),
            RequiredUtc(root, "issued_at_utc"),
            RequiredUtc(root, "expires_at_utc"),
            RequiredString(root, "signature_algorithm", 64),
            RequiredString(root, "signature", 256)
        );
        ValidateManifestShape(manifest);
        return manifest;
    }

    private static OcrProviderTrustStore ParseTrustStore(byte[] bytes)
    {
        using var document = ParseJson(bytes, MaximumTrustStoreBytes, "trust store");
        var root = RequireObject(document.RootElement, "trust store");
        RequireOnly(root, "schema", "keys");
        var keysElement = Required(root, "keys");
        if (keysElement.ValueKind != JsonValueKind.Array
            || keysElement.GetArrayLength() is < 1 or > 128)
        {
            throw TrustStoreInvalid("The host OCR trust store has an invalid key list.");
        }
        var keys = new List<OcrProviderTrustedKey>();
        foreach (var item in keysElement.EnumerateArray())
        {
            var key = RequireObject(item, "trusted key");
            RequireOnly(key, "publisher_id", "key_id", "algorithm", "subject_public_key_info_base64");
            keys.Add(new OcrProviderTrustedKey(
                RequiredString(key, "publisher_id", 128),
                RequiredString(key, "key_id", 128),
                RequiredString(key, "algorithm", 64),
                RequiredString(key, "subject_public_key_info_base64", 2048)
            ));
        }
        var store = new OcrProviderTrustStore(
            RequiredString(root, "schema", 128),
            keys.AsReadOnly()
        );
        ValidateTrustStoreShape(store);
        return store;
    }

    private static void ValidateManifestShape(OcrProviderManifest manifest)
    {
        if (
            !string.Equals(manifest.Schema, ManifestContract, StringComparison.Ordinal)
            || !IdentifierPattern().IsMatch(manifest.ProviderId)
            || !IdentifierPattern().IsMatch(manifest.PublisherId)
            || !IdentifierPattern().IsMatch(manifest.KeyId)
            || !IdentifierPattern().IsMatch(manifest.InterfaceContract)
            || !VersionPattern().IsMatch(manifest.InterfaceVersion)
            || !SafeScalar(manifest.ProviderVersion, 128)
            || !LeafFileName(manifest.ExecutableFileName)
            || !Sha256Pattern().IsMatch(manifest.ExecutableSha256)
            || !string.Equals(manifest.SignatureAlgorithm, SignatureAlgorithm, StringComparison.Ordinal)
            || manifest.Models.Count is < 1 or > MaximumModels
            || manifest.RuntimeFiles.Count is < 1 or > MaximumRuntimeFiles
            || manifest.ExpiresAtUtc <= manifest.IssuedAtUtc
            || manifest.ExpiresAtUtc - manifest.IssuedAtUtc > MaximumManifestLifetime
        )
        {
            throw ManifestInvalid("The signed OCR provider manifest is invalid.");
        }
        var runtimeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.RuntimeFiles)
        {
            if (
                !LeafFileName(file.FileName)
                || !runtimeNames.Add(file.FileName)
                || !Sha256Pattern().IsMatch(file.Sha256)
            )
            {
                throw ManifestInvalid("The signed OCR provider manifest contains an invalid runtime file binding.");
            }
        }
        var executableEntry = manifest.RuntimeFiles.SingleOrDefault(file =>
            string.Equals(file.FileName, manifest.ExecutableFileName, StringComparison.OrdinalIgnoreCase)
        );
        if (executableEntry is null || !FixedEquals(executableEntry.Sha256, manifest.ExecutableSha256))
        {
            throw ManifestInvalid("The signed OCR provider manifest does not bind its executable inside the runtime set.");
        }
        var languages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in manifest.Models)
        {
            if (
                !LanguagePattern().IsMatch(model.Language)
                || !languages.Add(model.Language)
                || !LeafFileName(model.FileName)
                || !string.Equals(model.FileName, model.Language + ".traineddata", StringComparison.Ordinal)
                || !Sha256Pattern().IsMatch(model.Sha256)
            )
            {
                throw ManifestInvalid("The signed OCR provider manifest contains an invalid model binding.");
            }
        }
        if (manifest.Signature.Length > 0)
        {
            try
            {
                if (DecodeCanonicalBase64(manifest.Signature).Length != 64)
                {
                    throw new FormatException();
                }
            }
            catch (FormatException exception)
            {
                throw ManifestInvalid("The signed OCR provider manifest contains an invalid signature.", exception);
            }
        }
    }

    private static void ValidateTrustStoreShape(OcrProviderTrustStore trustStore)
    {
        if (!string.Equals(trustStore.Schema, TrustStoreContract, StringComparison.Ordinal)
            || trustStore.Keys.Count is < 1 or > 128)
        {
            throw TrustStoreInvalid("The host OCR trust store is invalid.");
        }
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in trustStore.Keys)
        {
            if (
                !IdentifierPattern().IsMatch(key.PublisherId)
                || !IdentifierPattern().IsMatch(key.KeyId)
                || !string.Equals(key.Algorithm, SignatureAlgorithm, StringComparison.Ordinal)
                || !identities.Add(key.PublisherId + "\0" + key.KeyId)
            )
            {
                throw TrustStoreInvalid("The host OCR trust store contains an invalid or duplicate key.");
            }
            try
            {
                _ = DecodeCanonicalBase64(key.SubjectPublicKeyInfoBase64);
            }
            catch (FormatException exception)
            {
                throw TrustStoreInvalid("The host OCR trust store contains invalid canonical base64.", exception);
            }
        }
    }

    private static byte[] CanonicalManifestPayload(OcrProviderManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteManifest(writer, manifest, includeSignature: false);
        }
        return stream.ToArray();
    }

    private static void WriteManifest(
        Utf8JsonWriter writer,
        OcrProviderManifest manifest,
        bool includeSignature
    )
    {
        writer.WriteStartObject();
        writer.WriteString("schema", manifest.Schema);
        writer.WriteString("provider_id", manifest.ProviderId);
        writer.WriteString("publisher_id", manifest.PublisherId);
        writer.WriteString("key_id", manifest.KeyId);
        writer.WriteString("interface_contract", manifest.InterfaceContract);
        writer.WriteString("interface_version", manifest.InterfaceVersion);
        writer.WriteString("provider_version", manifest.ProviderVersion);
        writer.WriteString("executable_file_name", manifest.ExecutableFileName);
        writer.WriteString("executable_sha256", manifest.ExecutableSha256);
        writer.WriteStartArray("runtime_files");
        foreach (var file in manifest.RuntimeFiles.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteStartObject();
            writer.WriteString("file_name", file.FileName);
            writer.WriteString("sha256", file.Sha256);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("models");
        foreach (var model in manifest.Models.OrderBy(item => item.Language, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("language", model.Language);
            writer.WriteString("file_name", model.FileName);
            writer.WriteString("sha256", model.Sha256);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteString("issued_at_utc", FormatUtc(manifest.IssuedAtUtc));
        writer.WriteString("expires_at_utc", FormatUtc(manifest.ExpiresAtUtc));
        writer.WriteString("signature_algorithm", manifest.SignatureAlgorithm);
        if (includeSignature)
        {
            writer.WriteString("signature", manifest.Signature);
        }
        writer.WriteEndObject();
    }

    internal static string ModelSetHash(IReadOnlyList<OcrProviderTrustModelBinding> models)
    {
        var canonical = new StringBuilder();
        foreach (var model in models.OrderBy(item => item.Language, StringComparer.Ordinal))
        {
            canonical.Append(model.Language.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(model.Language);
            canonical.Append(model.Sha256);
        }
        return Sha256(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    internal static string RuntimeSetHash(
        IReadOnlyList<OcrProviderTrustRuntimeFileBinding> files
    )
    {
        var canonical = new StringBuilder();
        foreach (var file in files.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append(file.FileName.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(file.FileName.ToLowerInvariant());
            canonical.Append(file.Sha256);
        }
        return Sha256(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    internal static IReadOnlyList<OcrProviderTrustRuntimeFileBinding> HashRuntimeFiles(
        string executablePath,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(executablePath)
            ?? throw IdentityMismatch();
        var paths = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length is < 1 or > MaximumRuntimeFiles)
        {
            throw Error(
                "OCR_PROVIDER_RUNTIME_LIMIT",
                "The OCR provider runtime file set exceeds its closed limit."
            );
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<OcrProviderTrustRuntimeFileBinding>(paths.Length);
        long totalBytes = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw IdentityMismatch();
            }
            var name = Path.GetFileName(path);
            if (!LeafFileName(name) || !names.Add(name))
            {
                throw IdentityMismatch();
            }
            var length = new FileInfo(path).Length;
            totalBytes = checked(totalBytes + length);
            if (length is < 1 or > 128L * 1024 * 1024
                || totalBytes > 512L * 1024 * 1024)
            {
                throw Error(
                    "OCR_PROVIDER_RUNTIME_LIMIT",
                    "The OCR provider runtime bytes exceed their closed limit."
                );
            }
            result.Add(new OcrProviderTrustRuntimeFileBinding(
                name,
                HashFile(path, 128L * 1024 * 1024, cancellationToken)
            ));
        }
        return result.AsReadOnly();
    }

    private static bool BindingEquals(
        OcrProviderTrustBinding left,
        OcrProviderTrustBinding right
    ) =>
        string.Equals(left.Contract, right.Contract, StringComparison.Ordinal)
        && string.Equals(left.ProviderId, right.ProviderId, StringComparison.Ordinal)
        && string.Equals(left.PublisherId, right.PublisherId, StringComparison.Ordinal)
        && string.Equals(left.PublisherKeyId, right.PublisherKeyId, StringComparison.Ordinal)
        && string.Equals(left.ProviderVersion, right.ProviderVersion, StringComparison.Ordinal)
        && string.Equals(left.ExecutableFileName, right.ExecutableFileName, StringComparison.Ordinal)
        && FixedEquals(left.ExecutableSha256, right.ExecutableSha256)
        && FixedEquals(left.RuntimeSetSha256, right.RuntimeSetSha256)
        && FixedEquals(left.ModelSetSha256, right.ModelSetSha256)
        && FixedEquals(left.ManifestSha256, right.ManifestSha256)
        && FixedEquals(left.TrustStoreSha256, right.TrustStoreSha256)
        && left.RuntimeFiles.Count == right.RuntimeFiles.Count
        && left.RuntimeFiles.Zip(right.RuntimeFiles).All(pair => pair.First == pair.Second)
        && left.Models.Count == right.Models.Count
        && left.Models.Zip(right.Models).All(pair => pair.First == pair.Second);

    private static string ResolveConfigurationFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw Error(
                "OCR_PROVIDER_TRUST_NOT_CONFIGURED",
                "OCR provider trust files require explicit absolute local paths."
            );
        }
        var fullPath = Path.GetFullPath(path);
        return fullPath;
    }

    private void ValidateConfigurationFiles()
    {
        foreach (var path in new[] { _manifestPath, _trustStorePath })
        {
            if (path.StartsWith("\\\\", StringComparison.Ordinal)
                || path.StartsWith("\\\\?\\", StringComparison.Ordinal)
                || !File.Exists(path))
                throw Error("OCR_PROVIDER_TRUST_NOT_CONFIGURED", "An OCR provider trust file is unavailable or not local.");
            var root = Path.GetPathRoot(path);
            if (root is null || _driveTypeResolver(root) == DriveType.Network)
                throw Error("OCR_PROVIDER_TRUST_NOT_CONFIGURED", "An OCR provider trust file is unavailable or not local.");
            VerifyNoReparsePoints(path);
        }
    }

    private static FileSnapshot ReadConfigurationFile(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken
    )
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 2 || info.Length > maximumBytes)
        {
            throw Error(
                "OCR_PROVIDER_TRUST_INVALID",
                "An OCR provider trust file is missing or exceeds its limit."
            );
        }
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan
        );
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        return new FileSnapshot(bytes, Sha256(bytes));
    }

    internal static string HashFile(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 1 || info.Length > maximumBytes)
        {
            throw IdentityMismatch();
        }
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan
        );
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static JsonDocument ParseJson(byte[] bytes, int maximumBytes, string description)
    {
        if (bytes.Length > maximumBytes)
        {
            throw Error("OCR_PROVIDER_TRUST_INVALID", "An OCR provider trust file exceeds its limit.");
        }
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw Error(
                "OCR_PROVIDER_TRUST_INVALID",
                $"The OCR provider {description} is not strict UTF-8 JSON.",
                innerException: exception
            );
        }
    }

    private static JsonElement RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Error("OCR_PROVIDER_TRUST_INVALID", $"The OCR provider {description} must be an object.");
        }
        return value;
    }

    private static void RequireOnly(JsonElement value, params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !allowedSet.Contains(property.Name))
            {
                throw Error(
                    "OCR_PROVIDER_TRUST_INVALID",
                    "An OCR provider trust object contains an unknown or duplicate field."
                );
            }
        }
        if (seen.Count != allowedSet.Count || allowedSet.Any(name => !seen.Contains(name)))
        {
            throw Error(
                "OCR_PROVIDER_TRUST_INVALID",
                "An OCR provider trust object is missing a required field."
            );
        }
    }

    private static JsonElement Required(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw Error("OCR_PROVIDER_TRUST_INVALID", "An OCR provider trust field is missing.");
        }
        return value;
    }

    private static string RequiredString(JsonElement root, string name, int maximumLength)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Error("OCR_PROVIDER_TRUST_INVALID", "An OCR provider trust field has the wrong type.");
        }
        var text = value.GetString();
        if (string.IsNullOrEmpty(text) || text.Length > maximumLength || !SafeScalar(text, maximumLength))
        {
            throw Error("OCR_PROVIDER_TRUST_INVALID", "An OCR provider trust field has an invalid value.");
        }
        return text;
    }

    private static string RequiredSha256(JsonElement root, string name)
    {
        var value = RequiredString(root, name, 64);
        if (!Sha256Pattern().IsMatch(value))
        {
            throw Error("OCR_PROVIDER_TRUST_INVALID", "An OCR provider trust hash is invalid.");
        }
        return value;
    }

    private static DateTimeOffset RequiredUtc(JsonElement root, string name)
    {
        var text = RequiredString(root, name, 64);
        if (!DateTimeOffset.TryParseExact(
            text,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var value
        ) || !string.Equals(text, FormatUtc(value), StringComparison.Ordinal))
        {
            throw Error("OCR_PROVIDER_TRUST_INVALID", "An OCR provider trust timestamp is invalid.");
        }
        return value;
    }

    private static string FormatUtc(DateTimeOffset value) => value.ToUniversalTime()
        .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] DecodeCanonicalBase64(string value)
    {
        var bytes = Convert.FromBase64String(value);
        if (!string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal))
        {
            throw new FormatException("Non-canonical base64.");
        }
        return bytes;
    }

    private static bool LeafFileName(string value) => SafeScalar(value, 128)
        && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)
        && value is not "." and not ".."
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool SafeScalar(string? value, int maximumLength) => value is { Length: > 0 }
        && value.Length <= maximumLength
        && value.All(character => !char.IsControl(character) && !char.IsSurrogate(character));

    private static bool FixedEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right)
        );
    }

    private static void VerifyNoReparsePoints(string path)
    {
        var root = Path.GetPathRoot(path);
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw Error(
                    "OCR_PROVIDER_TRUST_NOT_CONFIGURED",
                    "OCR provider trust paths cannot contain symbolic links or reparse points."
                );
            }
            if (string.Equals(
                Path.TrimEndingDirectorySeparator(current),
                Path.TrimEndingDirectorySeparator(root ?? current),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            ))
            {
                break;
            }
        }
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(
        SHA256.HashData(bytes)
    ).ToLowerInvariant();

    private static WordToolkitExtensionException IdentityMismatch() => Error(
        "OCR_PROVIDER_IDENTITY_MISMATCH",
        "The OCR provider binary or model does not match its signed manifest."
    );

    private static WordToolkitExtensionException ManifestInvalid(
        string message,
        Exception? innerException = null
    ) => Error("OCR_PROVIDER_MANIFEST_INVALID", message, innerException: innerException);

    private static WordToolkitExtensionException TrustStoreInvalid(
        string message,
        Exception? innerException = null
    ) => Error("OCR_PROVIDER_TRUST_STORE_INVALID", message, innerException: innerException);

    private static WordToolkitExtensionException Error(
        string code,
        string message,
        bool retryable = false,
        Exception? innerException = null
    ) => new(code, message, retryable, innerException);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,127})$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{2,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();

    private sealed record FileSnapshot(byte[] Bytes, string Sha256);
}

internal sealed record OcrProviderManifestModel(
    string Language,
    string FileName,
    string Sha256
);

internal sealed record OcrProviderManifestRuntimeFile(
    string FileName,
    string Sha256
);

internal sealed record OcrProviderManifest(
    string Schema,
    string ProviderId,
    string PublisherId,
    string KeyId,
    string InterfaceContract,
    string InterfaceVersion,
    string ProviderVersion,
    string ExecutableFileName,
    string ExecutableSha256,
    IReadOnlyList<OcrProviderManifestRuntimeFile> RuntimeFiles,
    IReadOnlyList<OcrProviderManifestModel> Models,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string SignatureAlgorithm,
    string Signature
);

internal sealed record OcrProviderTrustedKey(
    string PublisherId,
    string KeyId,
    string Algorithm,
    string SubjectPublicKeyInfoBase64
);

internal sealed record OcrProviderTrustStore(
    string Schema,
    IReadOnlyList<OcrProviderTrustedKey> Keys
);
