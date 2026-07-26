namespace WordToolkit.Engine.Extensions;

public enum WordToolkitExtensionKind
{
    PackageFormatAdapter,
    StorageAdapter,
    TypedPartAdapter,
    SemanticProjector,
    Validator,
    LinterRulePack,
    RepairRulePack,
    CommandHandler,
    Renderer,
    Converter,
    OcrProvider,
    IndexProvider,
    PolicyProvider,
    TelemetrySink,
}

public enum WordToolkitExtensionTrust
{
    BuiltIn,
    TrustedPublisher,
}

public enum WordToolkitExtensionIsolation
{
    TrustedInProcess,
    OutOfProcess,
}

public enum WordToolkitExtensionTimeoutEnforcement
{
    Cooperative,
    ProcessBoundary,
}

[Flags]
public enum WordToolkitExtensionPermission
{
    None = 0,
    ReadPackage = 1 << 0,
    ReadDocumentContent = 1 << 1,
    MutatePackage = 1 << 2,
    ReadSensitiveMetadata = 1 << 3,
    FilesystemRead = 1 << 4,
    FilesystemWrite = 1 << 5,
    Network = 1 << 6,
    SpawnProcess = 1 << 7,
    LiveWord = 1 << 8,
    Credentials = 1 << 9,
}

public sealed record WordToolkitExtensionResourceLimits(
    long MaxInputBytes,
    long MaxOutputBytes,
    int MaxConcurrentInvocations,
    int TimeoutMilliseconds,
    long? MaxProcessMemoryBytes = null
)
{
    public static WordToolkitExtensionResourceLimits ConservativeDefault { get; } =
        new(
            MaxInputBytes: 256L * 1024 * 1024,
            MaxOutputBytes: 16L * 1024 * 1024,
            MaxConcurrentInvocations: 2,
            TimeoutMilliseconds: 120_000,
            MaxProcessMemoryBytes: null
        );
}

/// <summary>
/// Marks a host-owned proxy whose capability implementation crosses a real process
/// boundary before invoking provider code. The registry never discovers or loads a
/// provider assembly merely because an implementation carries this marker.
/// </summary>
public interface IWordToolkitProcessBoundaryProxy;

public sealed record WordToolkitExtensionDescriptor(
    string ExtensionId,
    string DisplayName,
    string Publisher,
    string ExtensionVersion,
    string EngineContractVersion,
    WordToolkitExtensionTrust Trust,
    WordToolkitExtensionIsolation Isolation
);

public sealed record WordToolkitExtensionCapabilityDescriptor(
    string CapabilityId,
    WordToolkitExtensionKind Kind,
    string InterfaceContract,
    string InterfaceVersion,
    WordToolkitExtensionPermission Permissions,
    WordToolkitExtensionResourceLimits ResourceLimits,
    WordToolkitExtensionTimeoutEnforcement TimeoutEnforcement,
    bool Deterministic,
    bool Idempotent,
    bool ReturnsDocumentContent
);

public sealed record WordToolkitExtensionInterfaceSupport(
    string InterfaceContract,
    string MaximumVersion,
    WordToolkitExtensionKind Kind
);

public sealed class WordToolkitExtensionException : Exception
{
    public WordToolkitExtensionException(
        string code,
        string message,
        bool retryable = false,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }

    public bool Retryable { get; }
}
