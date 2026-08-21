using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Publishing;

namespace WordToolkit.Engine.Rendering;

public enum RenderSourceKind
{
    SavedWordPackage,
    SemanticDocument,
    LiveWordDocument,
}

public enum RenderTargetKind
{
    WholeDocument,
    Story,
    SemanticNode,
    Page,
    PageRange,
    Selection,
}

public enum RenderFidelityLevel
{
    SemanticPreview,
    LayoutApproximate,
    LayoutExact,
}

public enum RenderResolutionState
{
    Resolved,
    Approximated,
    Unsupported,
    Ambiguous,
}

public sealed record RenderSourceIntent(
    RenderSourceKind Kind,
    string SourceIdentity,
    string? ExpectedVersion = null
);

public sealed record RenderTargetIntent(RenderTargetKind Kind, string? Selector = null);

public sealed record RenderArtifactIntent(string ArtifactKind, string MediaType);

public sealed record RenderOutputIntent(
    string Format,
    ImmutableArray<RenderArtifactIntent> Artifacts
)
{
    public RenderOutputIntent(string format, IEnumerable<RenderArtifactIntent> artifacts)
        : this(format, artifacts.ToImmutableArray()) { }
}

public sealed record RenderFidelityIntent(
    RenderFidelityLevel RequiredLevel,
    bool AllowApproximation,
    ImmutableArray<string> RequiredCapabilities
)
{
    public RenderFidelityIntent(
        RenderFidelityLevel requiredLevel,
        bool allowApproximation = false,
        IEnumerable<string>? requiredCapabilities = null
    )
        : this(
            requiredLevel,
            allowApproximation,
            requiredCapabilities?.ToImmutableArray() ?? []
        )
    { }
}

public sealed record RenderExecutionIntent(
    RenderSourceIntent Source,
    RenderTargetIntent Target,
    RenderOutputIntent Output,
    RenderFidelityIntent Fidelity
);

public sealed record RenderBackendCapability(
    string Capability,
    RenderResolutionState State,
    string? Explanation = null
);

public enum RenderOutputCardinality
{
    SingleArtifact,
    ArtifactBundle,
}

public sealed record RenderBackendArtifact(string ArtifactKind, string MediaType);

public sealed record RenderBackendOutput(
    string Format,
    RenderOutputCardinality Cardinality,
    ImmutableArray<RenderBackendArtifact> Artifacts,
    RenderResolutionState State = RenderResolutionState.Resolved,
    string? Explanation = null
)
{
    public RenderBackendOutput(
        string format,
        RenderOutputCardinality cardinality,
        IEnumerable<RenderBackendArtifact> artifacts,
        RenderResolutionState state = RenderResolutionState.Resolved,
        string? explanation = null
    )
        : this(format, cardinality, artifacts.ToImmutableArray(), state, explanation) { }
}

public sealed record RenderBackendProfile(
    string Backend,
    string BackendVersion,
    ImmutableArray<RenderSourceKind> SourceKinds,
    ImmutableArray<RenderTargetKind> TargetKinds,
    ImmutableArray<RenderBackendOutput> Outputs,
    RenderFidelityLevel MaximumFidelity,
    ImmutableArray<RenderBackendCapability> Capabilities,
    bool LoadsExternalResources,
    bool ExecutesActiveContent
)
{
    public RenderBackendProfile(
        string backend,
        string backendVersion,
        IEnumerable<RenderSourceKind> sourceKinds,
        IEnumerable<RenderTargetKind> targetKinds,
        IEnumerable<RenderBackendOutput> outputs,
        RenderFidelityLevel maximumFidelity,
        IEnumerable<RenderBackendCapability>? capabilities = null,
        bool loadsExternalResources = false,
        bool executesActiveContent = false
    )
        : this(
            backend,
            backendVersion,
            sourceKinds.ToImmutableArray(),
            targetKinds.ToImmutableArray(),
            outputs.ToImmutableArray(),
            maximumFidelity,
            capabilities?.ToImmutableArray() ?? [],
            loadsExternalResources,
            executesActiveContent
        )
    { }
}

public sealed record RenderBackendProvenance(
    string Backend,
    string BackendVersion,
    string EngineVersion,
    string ProfileSha256
);

public sealed record RenderIntentResolution(
    string Requirement,
    RenderResolutionState State,
    string? Explanation = null
);

public sealed record ResolvedRenderExecutionIntent(
    RenderExecutionIntent Intent,
    RenderBackendProfile Backend,
    ImmutableArray<RenderIntentResolution> Resolutions
);

public sealed record RenderArtifactDescriptor(
    string ArtifactId,
    string OutputPath,
    string Format,
    string MediaType,
    long Bytes,
    string Sha256,
    RenderResolutionState State
);

public sealed record RenderArtifactManifest(
    ResolvedRenderExecutionIntent Execution,
    RenderBackendProvenance Provenance,
    ImmutableArray<RenderArtifactDescriptor> Artifacts
);

public static class RenderExecutionIntentValidator
{
    public static ResolvedRenderExecutionIntent ValidateAndResolve(
        RenderExecutionIntent intent,
        RenderBackendProfile backend
    )
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(backend);
        ValidateIntent(intent);
        ValidateProfile(backend);

        var resolutions = ImmutableArray.CreateBuilder<RenderIntentResolution>();
        AddMembershipResolution(
            resolutions,
            $"source:{intent.Source.Kind}",
            backend.SourceKinds.Contains(intent.Source.Kind)
        );
        AddMembershipResolution(
            resolutions,
            $"target:{intent.Target.Kind}",
            backend.TargetKinds.Contains(intent.Target.Kind)
        );
        var requiredCardinality = intent.Output.Artifacts.Length == 1
            ? RenderOutputCardinality.SingleArtifact
            : RenderOutputCardinality.ArtifactBundle;
        var outputMatches = backend
            .Outputs.Where(item =>
                string.Equals(item.Format, intent.Output.Format, StringComparison.OrdinalIgnoreCase)
                && item.Cardinality == requiredCardinality
                && OutputArtifactsMatch(intent.Output.Artifacts, item.Artifacts)
            )
            .ToArray();
        resolutions.Add(
            outputMatches.Length switch
            {
                0 => new RenderIntentResolution(
                    $"output:{intent.Output.Format}",
                    RenderResolutionState.Unsupported,
                    "The backend does not declare the requested format and media type."
                ),
                > 1 => new RenderIntentResolution(
                    $"output:{intent.Output.Format}",
                    RenderResolutionState.Ambiguous,
                    "The backend declares the requested output more than once."
                ),
                _ => new RenderIntentResolution(
                    $"output:{intent.Output.Format}",
                    outputMatches[0].State,
                    outputMatches[0].Explanation
                ),
            }
        );

        var fidelityState = backend.MaximumFidelity >= intent.Fidelity.RequiredLevel
            ? RenderResolutionState.Resolved
            : RenderResolutionState.Approximated;
        resolutions.Add(
            new RenderIntentResolution(
                $"fidelity:{intent.Fidelity.RequiredLevel}",
                fidelityState,
                fidelityState == RenderResolutionState.Approximated
                    ? $"Backend maximum fidelity is {backend.MaximumFidelity}."
                    : null
            )
        );

        foreach (var required in intent.Fidelity.RequiredCapabilities)
        {
            var matches = backend
                .Capabilities.Where(item =>
                    string.Equals(item.Capability, required, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
            resolutions.Add(
                matches.Length switch
                {
                    0 => new RenderIntentResolution(
                        $"capability:{required}",
                        RenderResolutionState.Unsupported,
                        "The backend does not declare the required capability."
                    ),
                    > 1 => new RenderIntentResolution(
                        $"capability:{required}",
                        RenderResolutionState.Ambiguous,
                        "The backend declares the capability more than once."
                    ),
                    _ => new RenderIntentResolution(
                        $"capability:{required}",
                        matches[0].State,
                        matches[0].Explanation
                    ),
                }
            );
        }

        var result = resolutions.ToImmutable();
        var ambiguous = result.Where(item => item.State == RenderResolutionState.Ambiguous).ToArray();
        if (ambiguous.Length != 0)
        {
            throw ResolutionFailure("RENDER_INTENT_AMBIGUOUS", ambiguous);
        }
        var unsupported = result.Where(item => item.State == RenderResolutionState.Unsupported).ToArray();
        if (unsupported.Length != 0)
        {
            throw ResolutionFailure("RENDER_INTENT_UNSUPPORTED", unsupported);
        }
        var approximated = result.Where(item => item.State == RenderResolutionState.Approximated).ToArray();
        if (approximated.Length != 0 && !intent.Fidelity.AllowApproximation)
        {
            throw ResolutionFailure("RENDER_APPROXIMATION_NOT_ALLOWED", approximated);
        }

        return new ResolvedRenderExecutionIntent(intent, backend, result);
    }

    private static void ValidateIntent(RenderExecutionIntent intent)
    {
        if (
            string.IsNullOrWhiteSpace(intent.Source.SourceIdentity)
            || string.IsNullOrWhiteSpace(intent.Output.Format)
            || intent.Output.Artifacts.IsDefaultOrEmpty
            || intent.Output.Artifacts.Any(item =>
                string.IsNullOrWhiteSpace(item.ArtifactKind)
                || string.IsNullOrWhiteSpace(item.MediaType)
            )
            || intent.Fidelity.RequiredCapabilities.Any(string.IsNullOrWhiteSpace)
            || intent.Fidelity.RequiredCapabilities.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != intent.Fidelity.RequiredCapabilities.Length
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_RENDER_INTENT",
                "Render intent contains an empty required field."
            );
        }
        if (
            intent.Target.Kind != RenderTargetKind.WholeDocument
            && string.IsNullOrWhiteSpace(intent.Target.Selector)
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_RENDER_INTENT",
                "The selected render target requires an explicit selector."
            );
        }
    }

    private static void ValidateProfile(RenderBackendProfile backend)
    {
        if (
            string.IsNullOrWhiteSpace(backend.Backend)
            || string.IsNullOrWhiteSpace(backend.BackendVersion)
            || backend.SourceKinds.IsDefaultOrEmpty
            || backend.TargetKinds.IsDefaultOrEmpty
            || backend.Outputs.IsDefaultOrEmpty
            || backend.Outputs.Any(item =>
                string.IsNullOrWhiteSpace(item.Format)
                || item.Artifacts.IsDefaultOrEmpty
                || (
                    item.Cardinality == RenderOutputCardinality.SingleArtifact
                    && item.Artifacts.Length != 1
                )
                || (
                    item.Cardinality == RenderOutputCardinality.ArtifactBundle
                    && item.Artifacts.Length < 2
                )
                || item.Artifacts.Any(artifact =>
                    string.IsNullOrWhiteSpace(artifact.ArtifactKind)
                    || string.IsNullOrWhiteSpace(artifact.MediaType)
                )
            )
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_RENDER_BACKEND_PROFILE",
                "Render backend profile is incomplete."
            );
        }
    }

    private static void AddMembershipResolution(
        ImmutableArray<RenderIntentResolution>.Builder resolutions,
        string requirement,
        bool supported
    ) =>
        resolutions.Add(
            new RenderIntentResolution(
                requirement,
                supported
                    ? RenderResolutionState.Resolved
                    : RenderResolutionState.Unsupported
            )
        );

    private static bool OutputArtifactsMatch(
        ImmutableArray<RenderArtifactIntent> requested,
        ImmutableArray<RenderBackendArtifact> available
    )
    {
        if (requested.Length != available.Length)
        {
            return false;
        }
        var remaining = available.ToList();
        foreach (var artifact in requested)
        {
            var index = remaining.FindIndex(item =>
                string.Equals(
                    item.ArtifactKind,
                    artifact.ArtifactKind,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    item.MediaType,
                    artifact.MediaType,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (index < 0)
            {
                return false;
            }
            remaining.RemoveAt(index);
        }
        return true;
    }

    private static WordToolkitOperationException ResolutionFailure(
        string code,
        IReadOnlyList<RenderIntentResolution> failures
    ) =>
        new(
            code,
            "The render intent cannot be executed by the selected backend without an undeclared fallback.",
            details: failures
        );
}

public sealed record RenderArtifactValidationResult(bool IsValid, string? Message = null)
{
    public static RenderArtifactValidationResult Valid { get; } = new(true);

    public static RenderArtifactValidationResult Invalid(string message) => new(false, message);
}

public delegate RenderArtifactValidationResult RenderArtifactValidator(
    ReadOnlyMemory<byte> artifact
);

public sealed record RenderArtifactPublication(
    string ArtifactId,
    string OutputPath,
    string Format,
    string MediaType,
    ReadOnlyMemory<byte> Bytes,
    RenderArtifactValidator? Validator = null
);

public sealed record RenderPublicationRollbackDetails(
    string OriginalFailure,
    ImmutableArray<string> UnverifiedPaths
);

public sealed class TransactionalRenderArtifactPublisher
{
    private readonly IRenderArtifactPublicationFileSystem _fileSystem;

    public TransactionalRenderArtifactPublisher()
        : this(PhysicalRenderArtifactPublicationFileSystem.Instance) { }

    internal TransactionalRenderArtifactPublisher(
        IRenderArtifactPublicationFileSystem fileSystem
    ) => _fileSystem = fileSystem;

    public ImmutableArray<RenderArtifactDescriptor> PublishCreateNew(
        IEnumerable<RenderArtifactPublication> artifacts,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var batch = artifacts.ToImmutableArray();
        var normalized = ValidateBatch(batch);
        cancellationToken.ThrowIfCancellationRequested();

        var staged = new List<StagedArtifact>(batch.Length);
        var published = new List<string>(batch.Length);
        Exception? failure = null;
        try
        {
            for (var index = 0; index < batch.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var temporaryPath = CreateTemporaryPath(normalized[index]);
                var stagedArtifact = new StagedArtifact(
                    batch[index],
                    normalized[index],
                    temporaryPath
                );
                staged.Add(stagedArtifact);
                _fileSystem.WriteCreateNew(temporaryPath, batch[index].Bytes);
            }

            foreach (var item in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stagedBytes = _fileSystem.ReadAllBytes(item.TemporaryPath);
                if (!stagedBytes.AsSpan().SequenceEqual(item.Publication.Bytes.Span))
                {
                    throw new WordToolkitOperationException(
                        "RENDER_ARTIFACT_INVALID",
                        "A staged render artifact does not match its input bytes."
                    );
                }
                item.ValidatedBytes = stagedBytes;
                var validation = item.Publication.Validator?.Invoke(stagedBytes)
                    ?? RenderArtifactValidationResult.Valid;
                if (!validation.IsValid)
                {
                    throw new WordToolkitOperationException(
                        "RENDER_ARTIFACT_INVALID",
                        validation.Message ?? "A staged render artifact failed validation."
                    );
                }
            }

            foreach (var item in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _fileSystem.PublishNoClobber(item.TemporaryPath, item.OutputPath);
                }
                catch (RenderOutputExistsException exception)
                {
                    throw new WordToolkitOperationException(
                        "OUTPUT_EXISTS",
                        "A render artifact output already exists.",
                        innerException: exception
                    );
                }
                catch (Exception)
                {
                    try
                    {
                        if (_fileSystem.FileExists(item.OutputPath))
                        {
                            published.Add(item.OutputPath);
                        }
                    }
                    catch (Exception)
                    {
                        // The native OUTPUT_EXISTS case has a dedicated exception.
                        // Any other unverifiable publication must enter rollback.
                        published.Add(item.OutputPath);
                    }
                    throw;
                }
                published.Add(item.OutputPath);
                cancellationToken.ThrowIfCancellationRequested();
                _fileSystem.DeleteFile(item.TemporaryPath);
                item.StagingDeleted = true;
            }

            return staged
                .Select(item =>
                    new RenderArtifactDescriptor(
                        item.Publication.ArtifactId,
                        item.OutputPath,
                        item.Publication.Format,
                        item.Publication.MediaType,
                        item.Publication.Bytes.Length,
                        Convert.ToHexString(SHA256.HashData(item.ValidatedBytes!)).ToLowerInvariant(),
                        RenderResolutionState.Resolved
                    )
                )
                .ToImmutableArray();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var unverified = RollBack(published, staged);
        if (unverified.Length != 0)
        {
            throw new WordToolkitOperationException(
                "ROLLBACK_FAILED",
                "Render artifact transaction failed and cleanup could not be proven.",
                innerException: failure,
                details: new RenderPublicationRollbackDetails(
                    FailureCode(failure!),
                    unverified
                )
            );
        }

        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure!).Throw();
        throw new InvalidOperationException("Unreachable transaction state.");
    }

    private ImmutableArray<string> ValidateBatch(
        ImmutableArray<RenderArtifactPublication> batch
    )
    {
        if (batch.IsDefaultOrEmpty)
        {
            throw new WordToolkitOperationException(
                "INVALID_RENDER_PUBLICATION",
                "At least one render artifact is required."
            );
        }

        var paths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        );
        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        var normalized = ImmutableArray.CreateBuilder<string>(batch.Length);
        foreach (var artifact in batch)
        {
            if (
                string.IsNullOrWhiteSpace(artifact.ArtifactId)
                || string.IsNullOrWhiteSpace(artifact.OutputPath)
                || string.IsNullOrWhiteSpace(artifact.Format)
                || string.IsNullOrWhiteSpace(artifact.MediaType)
            )
            {
                throw new WordToolkitOperationException(
                    "INVALID_RENDER_PUBLICATION",
                    "Render artifact publication contains an empty required field."
                );
            }
            if (!artifactIds.Add(artifact.ArtifactId))
            {
                throw new WordToolkitOperationException(
                    "DUPLICATE_ARTIFACT_ID",
                    "A render artifact transaction contains the same artifact id more than once."
                );
            }
            if (!SemanticRenderPathPolicy.IsAllowedLocalPath(artifact.OutputPath))
            {
                throw new WordToolkitOperationException(
                    "INVALID_RENDER_PUBLICATION",
                    "Render artifact output must be a local path."
                );
            }
            var suppliedOutputPath = Path.GetFullPath(artifact.OutputPath);
            var outputFileName = Path.GetFileName(suppliedOutputPath);
            if (
                string.IsNullOrWhiteSpace(outputFileName)
                || outputFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || _fileSystem.DirectoryExists(suppliedOutputPath)
            )
            {
                throw new WordToolkitOperationException(
                    "INVALID_RENDER_PUBLICATION",
                    "A render artifact output must identify a valid file path."
                );
            }
            var suppliedDirectory = Path.GetDirectoryName(suppliedOutputPath);
            if (
                suppliedDirectory is null
                || !_fileSystem.DirectoryExists(suppliedDirectory)
            )
            {
                throw new WordToolkitOperationException(
                    "OUTPUT_DIRECTORY_NOT_FOUND",
                    "A render artifact output directory does not exist."
                );
            }
            if (_fileSystem.ContainsReparsePoint(suppliedDirectory))
            {
                throw new WordToolkitOperationException(
                    "OUTPUT_PATH_ALIAS_REJECTED",
                    "Render artifact output paths cannot traverse symbolic links or reparse points."
                );
            }
            var outputPath = Path.Combine(
                _fileSystem.CanonicalizeDirectory(suppliedDirectory),
                outputFileName
            );
            if (!paths.Add(outputPath))
            {
                throw new WordToolkitOperationException(
                    "DUPLICATE_OUTPUT_PATH",
                    "A render artifact transaction contains the same output path more than once."
                );
            }
            if (_fileSystem.FileExists(outputPath))
            {
                throw new WordToolkitOperationException(
                    "OUTPUT_EXISTS",
                    "A render artifact output already exists."
                );
            }
            normalized.Add(outputPath);
        }
        return normalized.ToImmutable();
    }

    private string CreateTemporaryPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath)!;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = Path.Combine(
                directory,
                $".wordtoolkit-render-transaction-{Guid.NewGuid():N}.tmp"
            );
            if (!_fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException("A private render staging path could not be allocated.");
    }

    private ImmutableArray<string> RollBack(
        IReadOnlyList<string> published,
        IReadOnlyList<StagedArtifact> staged
    )
    {
        var candidates = published
            .Reverse()
            .Concat(staged.Where(item => !item.StagingDeleted).Select(item => item.TemporaryPath))
            .Distinct(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            )
            .ToArray();
        foreach (var path in candidates)
        {
            try
            {
                _fileSystem.DeleteFile(path);
            }
            catch (Exception)
            {
                // Verification below determines whether cleanup actually completed.
            }
        }
        var unverified = ImmutableArray.CreateBuilder<string>();
        foreach (var path in candidates)
        {
            try
            {
                if (_fileSystem.FileExists(path))
                {
                    unverified.Add(path);
                }
            }
            catch (Exception)
            {
                unverified.Add(path);
            }
        }
        return unverified.ToImmutable();
    }

    private static string FailureCode(Exception failure) =>
        failure is WordToolkitOperationException operation ? operation.Code : failure.GetType().Name;

    private sealed class StagedArtifact(
        RenderArtifactPublication publication,
        string outputPath,
        string temporaryPath
    )
    {
        public RenderArtifactPublication Publication { get; } = publication;
        public string OutputPath { get; } = outputPath;
        public string TemporaryPath { get; } = temporaryPath;
        public bool StagingDeleted { get; set; }
        public byte[]? ValidatedBytes { get; set; }
    }
}

internal interface IRenderArtifactPublicationFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    bool ContainsReparsePoint(string directoryPath);
    string CanonicalizeDirectory(string directoryPath);
    void WriteCreateNew(string path, ReadOnlyMemory<byte> bytes);
    byte[] ReadAllBytes(string path);
    void PublishNoClobber(string temporaryPath, string outputPath);
    void DeleteFile(string path);
}

internal sealed class PhysicalRenderArtifactPublicationFileSystem
    : IRenderArtifactPublicationFileSystem
{
    public static PhysicalRenderArtifactPublicationFileSystem Instance { get; } = new();

    private PhysicalRenderArtifactPublicationFileSystem() { }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool ContainsReparsePoint(string directoryPath)
    {
        DirectoryInfo? current = new(directoryPath);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    public string CanonicalizeDirectory(string directoryPath)
    {
        var fullPath = Path.GetFullPath(directoryPath);
        if (!OperatingSystem.IsWindows())
        {
            return fullPath;
        }
        var required = GetLongPathNameWindows(fullPath, null, 0);
        if (required == 0)
        {
            throw new IOException(
                "Render output directory could not be canonicalized.",
                new Win32Exception(Marshal.GetLastWin32Error())
            );
        }
        var buffer = new char[required];
        var written = GetLongPathNameWindows(fullPath, buffer, buffer.Length);
        if (written == 0 || written >= buffer.Length)
        {
            throw new IOException(
                "Render output directory could not be canonicalized.",
                new Win32Exception(Marshal.GetLastWin32Error())
            );
        }
        return new string(buffer, 0, written);
    }

    public void WriteCreateNew(string path, ReadOnlyMemory<byte> bytes)
    {
        using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.WriteThrough
        );
        output.Write(bytes.Span);
        output.Flush(flushToDisk: true);
    }

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void PublishNoClobber(string temporaryPath, string outputPath)
    {
        try
        {
            AtomicFilePublisher.PublishCreateNew(temporaryPath, outputPath);
        }
        catch (IOException exception) when (AtomicFilePublisher.IsAlreadyExists(exception))
        {
            throw new RenderOutputExistsException(
                "A render artifact output already exists.",
                exception.InnerException ?? exception
            );
        }
    }

    public void DeleteFile(string path) => File.Delete(path);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetLongPathNameW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern int GetLongPathNameWindows(
        string shortPath,
        [Out] char[]? longPath,
        int bufferLength
    );
}

internal sealed class RenderOutputExistsException(string message, Exception innerException)
    : IOException(message, innerException);
