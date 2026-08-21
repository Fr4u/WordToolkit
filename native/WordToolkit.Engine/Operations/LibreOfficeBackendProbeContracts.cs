namespace WordToolkit.Engine.Operations;

public static class LibreOfficeBackendProbeContract
{
    public const string OperationName = "inspect_libreoffice_backend";
    public const string Contract = "wordtoolkit.inspect_libreoffice_backend/1.0";
    public const int DefaultTimeoutMilliseconds = 10_000;
    public const int MinimumTimeoutMilliseconds = 1_000;
    public const int MaximumTimeoutMilliseconds = 30_000;
    public const int MaximumRequestJsonCharacters = 16 * 1024;
    public const int MaximumExecutablePathCharacters = 32_767;
    public const long MaximumExecutableBytes = 512L * 1024 * 1024;
    public const int MaximumProcessOutputCharacters = 8 * 1024;
}

public sealed record InspectLibreOfficeBackendRequest(
    string ExecutablePath,
    string? ExpectedExecutableSha256 = null,
    int TimeoutMilliseconds = LibreOfficeBackendProbeContract.DefaultTimeoutMilliseconds
);

public sealed record LibreOfficeBackendProbeProviderRequest(
    string ExecutablePath,
    string? ExpectedExecutableSha256,
    int TimeoutMilliseconds,
    long MaximumExecutableBytes,
    int MaximumProcessOutputCharacters
);

public sealed record LibreOfficeBackendProbeObservation(
    string Product,
    string Version,
    string VersionBanner,
    string ExecutableFileName,
    long ExecutableBytes,
    string ExecutableSha256,
    bool ExecutableHashStable,
    string OperatingSystem,
    string OperatingSystemArchitecture,
    string ProcessArchitecture
);

public interface ILibreOfficeBackendProbeProvider
{
    Task<LibreOfficeBackendProbeObservation> ProbeAsync(
        LibreOfficeBackendProbeProviderRequest request,
        CancellationToken cancellationToken = default
    );
}

public sealed record LibreOfficeBackendIdentity(
    string Product,
    string Version,
    string VersionBanner,
    string ExecutableFileName,
    long ExecutableBytes,
    string ExecutableSha256,
    bool ExecutableHashStable,
    bool ExpectedExecutableHashEnforced
);

public sealed record LibreOfficeBackendHost(
    string OperatingSystem,
    string OperatingSystemArchitecture,
    string ProcessArchitecture
);

public sealed record LibreOfficeBackendCapabilities(
    bool VersionProbeVerified,
    bool UnoConnectionVerified,
    bool WriterComponentVerified,
    bool WriterPdfExportVerified,
    bool DocumentLoadPolicyVerified,
    bool MacroExecutionPrevented,
    bool ExternalUpdatesPrevented,
    bool RenderingVerified,
    bool WordFidelityClaimed
);

public sealed record LibreOfficeBackendProbeSecurity(
    bool ReadsDocument,
    bool ReturnsDocumentContent,
    bool OpensMicrosoftWord,
    bool DocumentArgumentsSupplied,
    bool ProfileCreatedByWordToolkit,
    bool PathSearchUsed,
    bool NetworkRequested,
    bool NetworkIsolationEnforced,
    bool StdinClosed,
    bool ProcessTreeTerminationOnTimeout,
    bool ExecutablePathReturned,
    bool EnvironmentValuesReturned,
    bool ArgumentsFixed
);

public sealed record LibreOfficeBackendProbePerformance(double TotalMilliseconds);

public sealed record InspectLibreOfficeBackendResult(
    string OperationContract,
    bool Available,
    LibreOfficeBackendIdentity Identity,
    LibreOfficeBackendHost Host,
    LibreOfficeBackendCapabilities Capabilities,
    LibreOfficeBackendProbeSecurity Security,
    IReadOnlyList<string> Limitations,
    string Runtime,
    bool PythonUsed,
    LibreOfficeBackendProbePerformance Performance
);
