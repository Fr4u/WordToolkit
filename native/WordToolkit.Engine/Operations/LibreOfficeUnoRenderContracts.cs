namespace WordToolkit.Engine.Operations;

public static class LibreOfficeUnoRenderContract
{
    public const string ProviderContract = "wordtoolkit.libreoffice_uno_render_provider/1.0";
    public const int MinimumTimeoutMilliseconds = 5_000;
    public const int MaximumTimeoutMilliseconds = 120_000;
    public const int MaximumPathCharacters = 32_767;
    public const long MaximumExecutableBytes = 512L * 1024 * 1024;
    public const long MaximumJavaArchiveBytes = 256L * 1024 * 1024;
    public const long MaximumSourceBytes = 512L * 1024 * 1024;
    public const long MaximumPdfBytes = 512L * 1024 * 1024;
    public const int MaximumProcessOutputCharacters = 8 * 1024;
    public const int MaximumPages = 10_000;
}

public sealed record LibreOfficeUnoRenderProviderRequest(
    string LibreOfficeExecutablePath,
    string ExpectedLibreOfficeExecutableSha256,
    string JavaExecutablePath,
    string ExpectedJavaExecutableSha256,
    string LibreOfficeJarPath,
    string ExpectedLibreOfficeJarSha256,
    string HelperClasspathPath,
    string ExpectedHelperClasspathSha256,
    string SourcePath,
    string ExpectedSourceSha256,
    string OutputPdfPath,
    string InputFilterName,
    int FirstPage,
    int? LastPage,
    bool PdfA1b,
    bool ExportBookmarks,
    int TimeoutMilliseconds,
    long MaximumExecutableBytes = LibreOfficeUnoRenderContract.MaximumExecutableBytes,
    long MaximumJavaArchiveBytes = LibreOfficeUnoRenderContract.MaximumJavaArchiveBytes,
    long MaximumSourceBytes = LibreOfficeUnoRenderContract.MaximumSourceBytes,
    long MaximumPdfBytes = LibreOfficeUnoRenderContract.MaximumPdfBytes,
    int MaximumProcessOutputCharacters =
        LibreOfficeUnoRenderContract.MaximumProcessOutputCharacters
);

public sealed record LibreOfficeUnoBinaryIdentity(
    string FileName,
    long Bytes,
    string Sha256,
    bool ExpectedSha256Enforced,
    bool HashStable
);

public sealed record LibreOfficeUnoDocumentPolicyEvidence(
    bool HiddenRequested,
    bool ReadOnlyRequested,
    bool ReadOnlyVerified,
    bool PickListDisabledRequested,
    bool RepairDisabledRequested,
    bool MacroNeverExecuteRequested,
    bool MacroPreventionBehaviorallyVerified,
    bool UpdateNoUpdateRequested,
    bool ExternalUpdatePreventionBehaviorallyVerified,
    string InputFilterName,
    bool InputFilterExplicit
);

public sealed record LibreOfficeUnoExportEvidence(
    bool UnoConnectionVerified,
    bool WriterComponentVerified,
    bool WriterPdfExportVerified,
    bool PdfFilterExplicit,
    bool OverwriteDisabled,
    bool SourceLocationPreserved,
    int FirstPage,
    int? LastPage,
    bool PdfA1bRequested,
    bool ExportBookmarksRequested,
    long PdfBytes,
    string PdfSha256
);

public sealed record LibreOfficeUnoCleanupEvidence(
    bool DocumentClosed,
    bool DesktopTerminated,
    bool HelperExited,
    bool LibreOfficeExited,
    bool ProcessTreeKillRequired,
    bool PrivateProfileDeleted,
    bool PrivateWorkspaceDeleted
);

public sealed record LibreOfficeUnoRenderObservation(
    string ProviderContract,
    LibreOfficeUnoBinaryIdentity LibreOfficeExecutable,
    LibreOfficeUnoBinaryIdentity JavaExecutable,
    LibreOfficeUnoBinaryIdentity LibreOfficeJar,
    LibreOfficeUnoBinaryIdentity HelperClasspath,
    string SourceSha256,
    bool SourceHashStable,
    LibreOfficeUnoDocumentPolicyEvidence DocumentPolicy,
    LibreOfficeUnoExportEvidence Export,
    LibreOfficeUnoCleanupEvidence Cleanup,
    string OperatingSystem,
    string OperatingSystemArchitecture,
    string ProcessArchitecture,
    IReadOnlyList<string> Limitations
);

public interface ILibreOfficeUnoRenderProvider
{
    Task<LibreOfficeUnoRenderObservation> RenderAsync(
        LibreOfficeUnoRenderProviderRequest request,
        CancellationToken cancellationToken = default
    );
}
