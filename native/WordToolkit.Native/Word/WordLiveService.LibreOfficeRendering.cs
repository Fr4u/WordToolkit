using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Rendering;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Rendering;

namespace WordToolkit.Native.Word;

internal static class LibreOfficeRenderWordPackageContract
{
    public const string OperationName = "render_ooxml_libreoffice_artifacts";
    public const string Contract =
        "wordtoolkit.render_ooxml_libreoffice_artifacts/1.0";
    public const int DefaultTimeoutMilliseconds = 60_000;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LibreOfficeRenderWordPackageRequest
{
    public required string LocalPath { get; init; }
    public required string ExpectedPackageFingerprint { get; init; }
    public required string OutputDirectory { get; init; }
    public required string ArtifactStem { get; init; }
    [JsonPropertyName("libreoffice_executable_path")]
    public required string LibreOfficeExecutablePath { get; init; }
    [JsonPropertyName("expected_libreoffice_executable_sha256")]
    public required string ExpectedLibreOfficeExecutableSha256 { get; init; }
    public required string JavaExecutablePath { get; init; }
    public required string ExpectedJavaExecutableSha256 { get; init; }
    [JsonPropertyName("libreoffice_jar_path")]
    public required string LibreOfficeJarPath { get; init; }
    [JsonPropertyName("expected_libreoffice_jar_sha256")]
    public required string ExpectedLibreOfficeJarSha256 { get; init; }
    public FixedRenderOutputKind Output { get; init; } = FixedRenderOutputKind.Pdf;
    public int FirstPage { get; init; } = 1;
    public int? LastPage { get; init; }
    public int Dpi { get; init; } = 144;
    public bool PdfA1b { get; init; }
    public bool ExportBookmarks { get; init; } = true;
    public int TimeoutMilliseconds { get; init; } =
        LibreOfficeRenderWordPackageContract.DefaultTimeoutMilliseconds;
    [JsonPropertyName("pdfinfo_path")]
    public string? PdfInfoPath { get; init; }
    public string? RasterizerPath { get; init; }
    public FixedRenderRasterizerKind RasterizerKind { get; init; } =
        FixedRenderRasterizerKind.PdfToPpm;
}

internal sealed partial class WordLiveService
{
    private async Task<object> RenderPackageLibreOfficeArtifactsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        LibreOfficeRenderWordPackageRequest request;
        try
        {
            request = WordToolkitOperationJson.Deserialize<LibreOfficeRenderWordPackageRequest>(
                arguments.GetRawText()
            );
        }
        catch (JsonException)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "LibreOffice render arguments are invalid or contain unsupported fields"
            );
        }

        var paths = ValidateLibreOfficeRenderRequest(request);
        var sourceSha256Before = Sha256File(paths.Input, cancellationToken);
        OpcPackageSnapshot package;
        try
        {
            package = new OpcPackageReader().Read(paths.Input, cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException
        )
        {
            throw new NativeToolException(
                exception is UnauthorizedAccessException ? "ACCESS_DENIED" : "INVALID_PACKAGE",
                "The LibreOffice render source could not be read as an OPC package"
            );
        }
        if (!package.IsStructurallyValid)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The LibreOffice render source failed structural OPC validation"
            );
        }
        if (!string.Equals(
                package.Fingerprint,
                request.ExpectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The render source does not match expected_package_fingerprint"
            );
        }

        var execution = ResolveLibreOfficeRenderIntent(request, package.Fingerprint);
        InspectLibreOfficeBackendResult version;
        try
        {
            version = await new InspectLibreOfficeBackendOperation(
                    _libreOfficeBackendProbeProvider
                )
                .ExecuteAsync(
                    new InspectLibreOfficeBackendRequest(
                        paths.LibreOfficeExecutable,
                        request.ExpectedLibreOfficeExecutableSha256,
                        Math.Min(
                            request.TimeoutMilliseconds,
                            LibreOfficeBackendProbeContract.MaximumTimeoutMilliseconds
                        )
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (WordToolkitOperationException exception)
        {
            throw MapOperationFailure(exception);
        }

        var stagingDirectory = Path.Combine(
            paths.OutputDirectory,
            $".wordtoolkit-libreoffice-render-{Guid.NewGuid():N}"
        );
        try
        {
            Directory.CreateDirectory(stagingDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "LibreOffice render staging cannot be created"
            );
        }
        catch (IOException)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "LibreOffice render staging cannot be created",
                retryable: true
            );
        }
        var stagingPdfPath = Path.Combine(stagingDirectory, "source.pdf");
        PopplerRasterizationStagingResult? raster = null;
        PopplerPdfInspection? inspection = null;
        var stagingCleaned = false;
        try
        {
            LibreOfficeUnoRenderObservation uno;
            try
            {
                uno = await _libreOfficeUnoRenderProvider
                    .RenderAsync(
                        new LibreOfficeUnoRenderProviderRequest(
                            paths.LibreOfficeExecutable,
                            request.ExpectedLibreOfficeExecutableSha256,
                            paths.JavaExecutable,
                            request.ExpectedJavaExecutableSha256,
                            paths.LibreOfficeJar,
                            request.ExpectedLibreOfficeJarSha256,
                            paths.Input,
                            sourceSha256Before,
                            stagingPdfPath,
                            InputFilterName(paths.Input),
                            request.FirstPage,
                            request.LastPage,
                            request.PdfA1b,
                            request.ExportBookmarks,
                            request.TimeoutMilliseconds
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (WordToolkitOperationException exception)
            {
                throw MapOperationFailure(exception);
            }
            ValidatePdfArtifact(stagingPdfPath);

            if (request.Output is not FixedRenderOutputKind.Pdf)
            {
                var poppler = CreateLibreOfficePopplerBackend(request, stagingDirectory);
                inspection = await poppler.InspectAsync(stagingPdfPath, cancellationToken);
                raster = await poppler.RasterizeAsync(
                    stagingPdfPath,
                    firstPage: 1,
                    lastPage: inspection.PageCount,
                    request.Dpi,
                    cancellationToken
                );
                ValidateLibreOfficeRasterGeometry(inspection, raster);
            }

            var prepared = PrepareLibreOfficeRenderArtifacts(
                request,
                paths,
                stagingPdfPath,
                version,
                uno,
                inspection,
                raster,
                execution,
                package.Fingerprint,
                sourceSha256Before
            );

            var sourceSha256BeforePublication = Sha256File(paths.Input, cancellationToken);
            if (!string.Equals(
                    sourceSha256Before,
                    sourceSha256BeforePublication,
                    StringComparison.Ordinal
                ))
            {
                throw new NativeToolException(
                    "VERSION_CONFLICT",
                    "The source package changed before artifact publication; no artifact was published"
                );
            }

            CleanupLibreOfficeRenderStaging(raster, stagingDirectory);
            stagingCleaned = true;

            var published = new TransactionalRenderArtifactPublisher().PublishCreateNew(
                prepared.Publications,
                cancellationToken
            );
            var artifactResults = published.Select(
                descriptor =>
                {
                    var metadata = prepared.Metadata.Single(item =>
                        item.ArtifactId == descriptor.ArtifactId
                    );
                    return new
                    {
                        artifact_id = descriptor.ArtifactId,
                        output_file_name = Path.GetFileName(descriptor.OutputPath),
                        output_path = descriptor.OutputPath,
                        format = descriptor.Format,
                        media_type = descriptor.MediaType,
                        bytes = descriptor.Bytes,
                        sha256 = descriptor.Sha256,
                        state = ToSnakeCase(descriptor.State.ToString()),
                        source_page_number = metadata.SourcePageNumber,
                        pixel_width = metadata.PixelWidth,
                        pixel_height = metadata.PixelHeight,
                    };
                }
            ).ToArray();
            var pageGeometries = inspection?.PageGeometries
                .OrderBy(item => item.PageNumber)
                .Select(item => new
                {
                    exported_page_number = item.PageNumber,
                    left_points = item.MediaBox.LeftPoints,
                    bottom_points = item.MediaBox.BottomPoints,
                    right_points = item.MediaBox.RightPoints,
                    top_points = item.MediaBox.TopPoints,
                    width_points = item.MediaBox.RightPoints
                        - item.MediaBox.LeftPoints,
                    height_points = item.MediaBox.TopPoints
                        - item.MediaBox.BottomPoints,
                })
                .ToArray() ?? [];

            return new
            {
                operation_contract = LibreOfficeRenderWordPackageContract.Contract,
                package_fingerprint = package.Fingerprint,
                source_sha256 = sourceSha256Before,
                source_file_name = Path.GetFileName(paths.Input),
                source_mutated = false,
                output_created = true,
                output = ToSnakeCase(request.Output.ToString()),
                requested_first_page = request.FirstPage,
                requested_last_page = request.LastPage,
                exported_page_count = inspection?.PageCount,
                page_geometry_count = pageGeometries.Length,
                page_geometries = pageGeometries,
                dpi = request.Output is FixedRenderOutputKind.Pdf
                    ? (int?)null
                    : request.Dpi,
                backend = new
                {
                    primary = "libreoffice_writer_pdf",
                    product = version.Identity.Product,
                    version = version.Identity.Version,
                    version_banner = version.Identity.VersionBanner,
                    executable = uno.LibreOfficeExecutable,
                    java = uno.JavaExecutable,
                    libreoffice_jar = uno.LibreOfficeJar,
                    embedded_helper = uno.EmbeddedHelper,
                    uno_provider_contract = uno.ProviderContract,
                    rasterizer = raster?.Provenance.Backend,
                    pdfinfo_version = raster?.Provenance.PdfInfo.Version,
                    rasterizer_version = raster?.Provenance.Rasterizer.Version,
                    pdf_geometry_inspected = inspection is not null,
                },
                execution = new
                {
                    source_kind = ToSnakeCase(execution.Intent.Source.Kind.ToString()),
                    target_kind = ToSnakeCase(execution.Intent.Target.Kind.ToString()),
                    output_format = execution.Intent.Output.Format,
                    fidelity = ToSnakeCase(execution.Intent.Fidelity.RequiredLevel.ToString()),
                    resolution_count = execution.Resolutions.Length,
                    all_resolved = execution.Resolutions.All(item =>
                        item.State == RenderResolutionState.Resolved
                    ),
                    silent_fallback = false,
                },
                fidelity = new
                {
                    class_name = "libreoffice_writer_fixed_layout",
                    paginated = true,
                    exact_text_metrics_against_microsoft_word = false,
                    microsoft_word_layout_claimed = false,
                    pixel_equivalence_claimed = false,
                    png_is_derived_from_exact_pdf = raster is not null,
                },
                artifacts = artifactResults,
                artifact_count = artifactResults.Length,
                policy = uno.DocumentPolicy,
                cleanup = uno.Cleanup,
                safety = new
                {
                    source_opened_read_only = uno.DocumentPolicy.ReadOnlyVerified,
                    source_hash_verified_after_close = uno.SourceHashStable,
                    macro_never_execute_requested =
                        uno.DocumentPolicy.MacroNeverExecuteRequested,
                    macro_prevention_behaviorally_verified =
                        uno.DocumentPolicy.MacroPreventionBehaviorallyVerified,
                    update_no_update_requested =
                        uno.DocumentPolicy.UpdateNoUpdateRequested,
                    external_update_prevention_behaviorally_verified =
                        uno.DocumentPolicy.ExternalUpdatePreventionBehaviorallyVerified,
                    active_content_execution_prevented = false,
                    external_resource_loading_prevented = false,
                    raw_xml_returned = false,
                    document_text_returned = false,
                    microsoft_word_opened = false,
                    silent_backend_fallback = false,
                    public_artifacts_published_after_private_cleanup = true,
                },
                limitations = uno.Limitations,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    runtime = "dotnet-native",
                    python_used = false,
                    persistent_com_sta = false,
                    com_attachments = 0,
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            };
        }
        catch (PopplerPdfBackendException exception)
        {
            throw MapPopplerFailure(exception);
        }
        catch (WordToolkitOperationException exception)
        {
            throw MapOperationFailure(exception);
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "LibreOffice render staging could not be read or removed"
            );
        }
        catch (IOException)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "LibreOffice render staging could not be read or removed",
                retryable: true
            );
        }
        finally
        {
            if (!stagingCleaned)
            {
                var cleanupFailed = false;
                if (raster is not null)
                {
                    cleanupFailed |= !TryDeleteRenderDirectory(raster.StagingDirectory);
                }
                cleanupFailed |= !TryDeleteRenderDirectory(stagingDirectory);
                if (cleanupFailed)
                {
                    throw new NativeToolException(
                        "ROLLBACK_FAILED",
                        "LibreOffice render staging cleanup could not be proven",
                        new { public_artifact_state = "unchanged" }
                    );
                }
            }
        }
    }

    private static LibreOfficeRenderPaths ValidateLibreOfficeRenderRequest(
        LibreOfficeRenderWordPackageRequest request
    )
    {
        if (new[]
            {
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.OutputDirectory,
                request.ArtifactStem,
                request.LibreOfficeExecutablePath,
                request.ExpectedLibreOfficeExecutableSha256,
                request.JavaExecutablePath,
                request.ExpectedJavaExecutableSha256,
                request.LibreOfficeJarPath,
                request.ExpectedLibreOfficeJarSha256,
            }.Any(string.IsNullOrWhiteSpace))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "The source, output and exact LibreOffice/Java identities are required"
            );
        }
        if (new[]
            {
                request.LocalPath,
                request.OutputDirectory,
                request.LibreOfficeExecutablePath,
                request.JavaExecutablePath,
                request.LibreOfficeJarPath,
            }.Any(path => path.Length > FixedRenderWordPackageContract.MaximumLocalPathCharacters))
        {
            throw new NativeToolException("LIMIT_EXCEEDED", "A render path is too long");
        }
        if (!IsSha256(request.ExpectedPackageFingerprint)
            || !IsSha256(request.ExpectedLibreOfficeExecutableSha256)
            || !IsSha256(request.ExpectedJavaExecutableSha256)
            || !IsSha256(request.ExpectedLibreOfficeJarSha256))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Every expected fingerprint or SHA-256 must contain 64 hexadecimal characters"
            );
        }
        if (request.ArtifactStem.Length
                > FixedRenderWordPackageContract.MaximumArtifactStemCharacters
            || request.ArtifactStem is "." or ".."
            || request.ArtifactStem.StartsWith(".", StringComparison.Ordinal)
            || request.ArtifactStem.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "artifact_stem accepts only ASCII letters, digits, hyphen and underscore"
            );
        }
        if (request.FirstPage < 1
            || request.LastPage is < 1
            || request.LastPage < request.FirstPage
            || (request.FirstPage != 1 && request.LastPage is null))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "A non-default first page requires a valid bounded last page"
            );
        }
        if (request.LastPage is > FixedRenderWordPackageContract.MaximumPages
            || request.Dpi is < 1 or > FixedRenderWordPackageContract.MaximumDpi
            || request.TimeoutMilliseconds
                is < LibreOfficeUnoRenderContract.MinimumTimeoutMilliseconds
                    or > LibreOfficeUnoRenderContract.MaximumTimeoutMilliseconds)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "The page, DPI or timeout limit was exceeded"
            );
        }

        var input = ResolveLibreOfficeRenderPath(request.LocalPath, "local_path", file: true);
        var outputDirectory = ResolveLibreOfficeRenderPath(
            request.OutputDirectory,
            "output_directory",
            file: false
        );
        var libreOffice = ResolveLibreOfficeRenderPath(
            request.LibreOfficeExecutablePath,
            "libreoffice_executable_path",
            file: true
        );
        var java = ResolveLibreOfficeRenderPath(
            request.JavaExecutablePath,
            "java_executable_path",
            file: true
        );
        var libreOfficeJar = ResolveLibreOfficeRenderPath(
            request.LibreOfficeJarPath,
            "libreoffice_jar_path",
            file: true
        );
        if (!InspectWordPackageContract.IsSupportedFileName(input))
        {
            throw new NativeToolException(
                "UNSUPPORTED_FORMAT",
                "LibreOffice rendering accepts DOCX, DOCM, DOTX, or DOTM files"
            );
        }
        if (!Path.GetExtension(libreOfficeJar).Equals(
                ".jar",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "libreoffice_jar_path must identify a JAR file"
            );
        }
        return new LibreOfficeRenderPaths(
            input,
            outputDirectory,
            libreOffice,
            java,
            libreOfficeJar
        );
    }

    private static string ResolveLibreOfficeRenderPath(
        string value,
        string argumentName,
        bool file
    )
    {
        if (!Path.IsPathFullyQualified(value)
            || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} must be an explicit absolute local path"
            );
        }
        string path;
        try
        {
            path = Path.GetFullPath(value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} is not a valid path"
            );
        }
        RejectNonLocalRenderPath(path);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root)
                    && new DriveInfo(root).DriveType == DriveType.Network)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        $"{argumentName} cannot use a mapped network drive"
                    );
                }
            }
            catch (NativeToolException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException
            )
            {
                throw new NativeToolException(
                    "ACCESS_DENIED",
                    $"{argumentName} drive could not be inspected"
                );
            }
        }
        if (file ? !File.Exists(path) : !Directory.Exists(path))
        {
            throw new NativeToolException(
                file ? "NOT_FOUND" : "OUTPUT_DIRECTORY_NOT_FOUND",
                $"{argumentName} does not exist"
            );
        }
        EnsureLibreOfficeRenderPathHasNoLinks(path, argumentName);
        return path;
    }

    private static void EnsureLibreOfficeRenderPathHasNoLinks(
        string path,
        string argumentName
    )
    {
        FileSystemInfo? current = File.Exists(path)
            ? new FileInfo(path)
            : new DirectoryInfo(path);
        while (current is not null)
        {
            try
            {
                current.Refresh();
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0
                    || current.LinkTarget is not null)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        $"{argumentName} cannot traverse symbolic or reparse paths"
                    );
                }
            }
            catch (NativeToolException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException
            )
            {
                throw new NativeToolException(
                    "ACCESS_DENIED",
                    $"{argumentName} could not be inspected"
                );
            }
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null,
            };
        }
    }

    private static string InputFilterName(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".docx" or ".docm" => "Office Open XML Text",
            ".dotx" or ".dotm" => "Office Open XML Text Template",
            _ => throw new NativeToolException(
                "UNSUPPORTED_FORMAT",
                "The source extension has no supported Writer input filter"
            ),
        };

    private static PopplerPdfBackend CreateLibreOfficePopplerBackend(
        LibreOfficeRenderWordPackageRequest request,
        string stagingDirectory
    )
    {
        var pdfInfo = ResolveExplicitExecutable(
            request.PdfInfoPath,
            "WORDTOOLKIT_PDFINFO_PATH",
            "pdfinfo_path"
        );
        var rasterizer = ResolveExplicitExecutable(
            request.RasterizerPath,
            "WORDTOOLKIT_PDF_RASTERIZER_PATH",
            "rasterizer_path"
        );
        return new PopplerPdfBackend(
            new PopplerPdfBackendOptions(
                pdfInfo,
                rasterizer,
                request.RasterizerKind == FixedRenderRasterizerKind.PdfToPpm
                    ? PopplerRasterizerKind.PdfToPpm
                    : PopplerRasterizerKind.PdfToCairo,
                stagingDirectory,
                FixedRenderWordPackageContract.ProcessTimeout,
                FixedRenderWordPackageContract.MaximumPages,
                FixedRenderWordPackageContract.MaximumDpi,
                FixedRenderWordPackageContract.MaximumInputPdfBytes,
                FixedRenderWordPackageContract.MaximumRasterBytes
            )
        );
    }

    private static ResolvedRenderExecutionIntent ResolveLibreOfficeRenderIntent(
        LibreOfficeRenderWordPackageRequest request,
        string packageFingerprint
    )
    {
        var target = request.FirstPage == 1 && request.LastPage is null
            ? new RenderTargetIntent(RenderTargetKind.WholeDocument)
            : new RenderTargetIntent(
                RenderTargetKind.PageRange,
                $"{request.FirstPage}:{request.LastPage}"
            );
        var outputArtifacts = request.Output switch
        {
            FixedRenderOutputKind.Pdf => new[]
            {
                new RenderArtifactIntent("pdf", "application/pdf"),
                new RenderArtifactIntent("manifest", "application/json"),
            },
            FixedRenderOutputKind.PngPages =>
            [
                new RenderArtifactIntent("png_page_set", "image/png"),
                new RenderArtifactIntent("manifest", "application/json"),
            ],
            FixedRenderOutputKind.PdfAndPngPages =>
            [
                new RenderArtifactIntent("pdf", "application/pdf"),
                new RenderArtifactIntent("png_page_set", "image/png"),
                new RenderArtifactIntent("manifest", "application/json"),
            ],
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "The LibreOffice render output kind is unsupported"
            ),
        };
        var capabilities = new List<string>
        {
            "pagination",
            "source_read_only",
            "transactional_publication",
            "isolated_user_profile",
        };
        if (request.Output is not FixedRenderOutputKind.Pdf)
        {
            capabilities.Add("page_raster");
        }
        var intent = new RenderExecutionIntent(
            new RenderSourceIntent(
                RenderSourceKind.SavedWordPackage,
                packageFingerprint,
                packageFingerprint
            ),
            target,
            new RenderOutputIntent(ToSnakeCase(request.Output.ToString()), outputArtifacts),
            new RenderFidelityIntent(
                RenderFidelityLevel.LayoutApproximate,
                allowApproximation: false,
                capabilities
            )
        );
        var profile = new RenderBackendProfile(
            "libreoffice-writer-pdf",
            "1.0",
            [RenderSourceKind.SavedWordPackage],
            [RenderTargetKind.WholeDocument, RenderTargetKind.PageRange],
            [
                new RenderBackendOutput(
                    ToSnakeCase(request.Output.ToString()),
                    RenderOutputCardinality.ArtifactBundle,
                    outputArtifacts.Select(item =>
                        new RenderBackendArtifact(item.ArtifactKind, item.MediaType)
                    )
                ),
            ],
            RenderFidelityLevel.LayoutApproximate,
            capabilities.Select(capability =>
                new RenderBackendCapability(capability, RenderResolutionState.Resolved)
            ),
            loadsExternalResources: true,
            executesActiveContent: true
        );
        try
        {
            return RenderExecutionIntentValidator.ValidateAndResolve(intent, profile);
        }
        catch (WordToolkitOperationException exception)
        {
            throw MapOperationFailure(exception);
        }
    }

    private static void ValidateLibreOfficeRasterGeometry(
        PopplerPdfInspection inspection,
        PopplerRasterizationStagingResult raster
    )
    {
        if (inspection.PageGeometries.Count != raster.Pages.Count)
        {
            throw new NativeToolException(
                "RENDER_VALIDATION_FAILED",
                "PDF geometry and PNG page counts do not agree"
            );
        }
        var geometryByPage = inspection.PageGeometries.ToDictionary(item =>
            item.PageNumber
        );
        foreach (var page in raster.Pages)
        {
            if (!geometryByPage.TryGetValue(page.PageNumber, out var geometry))
            {
                throw new NativeToolException(
                    "RENDER_VALIDATION_FAILED",
                    "A raster page has no matching PDF MediaBox"
                );
            }
            var expectedWidth =
                (geometry.MediaBox.RightPoints - geometry.MediaBox.LeftPoints)
                * raster.Dpi
                / 72d;
            var expectedHeight =
                (geometry.MediaBox.TopPoints - geometry.MediaBox.BottomPoints)
                * raster.Dpi
                / 72d;
            if (Math.Abs(page.PixelWidth - expectedWidth) > 1.01
                || Math.Abs(page.PixelHeight - expectedHeight) > 1.01)
            {
                throw new NativeToolException(
                    "RENDER_VALIDATION_FAILED",
                    "PNG dimensions do not match the PDF MediaBox at the requested DPI",
                    new { page_number = page.PageNumber }
                );
            }
        }
    }

    private static PreparedLibreOfficeRenderArtifacts PrepareLibreOfficeRenderArtifacts(
        LibreOfficeRenderWordPackageRequest request,
        LibreOfficeRenderPaths paths,
        string stagingPdfPath,
        InspectLibreOfficeBackendResult version,
        LibreOfficeUnoRenderObservation uno,
        PopplerPdfInspection? inspection,
        PopplerRasterizationStagingResult? raster,
        ResolvedRenderExecutionIntent execution,
        string packageFingerprint,
        string sourceSha256
    )
    {
        var publications = new List<RenderArtifactPublication>();
        var metadata = new List<PreparedLibreOfficeRenderMetadata>();
        var manifestArtifacts = new List<object>();
        if (request.Output is FixedRenderOutputKind.Pdf or FixedRenderOutputKind.PdfAndPngPages)
        {
            AddArtifact(
                "pdf",
                Path.Combine(paths.OutputDirectory, request.ArtifactStem + ".pdf"),
                "pdf",
                "application/pdf",
                File.ReadAllBytes(stagingPdfPath),
                null,
                null,
                null
            );
        }
        if (raster is not null)
        {
            foreach (var page in raster.Pages.OrderBy(item => item.PageNumber))
            {
                AddArtifact(
                    $"page_{page.PageNumber:D4}",
                    Path.Combine(
                        paths.OutputDirectory,
                        $"{request.ArtifactStem}-page-{page.PageNumber:D4}.png"
                    ),
                    "png",
                    "image/png",
                    File.ReadAllBytes(page.StagingPath),
                    page.PageNumber,
                    page.PixelWidth,
                    page.PixelHeight
                );
            }
        }

        var manifest = new
        {
            operation_contract = LibreOfficeRenderWordPackageContract.Contract,
            package_fingerprint = packageFingerprint,
            source_sha256 = sourceSha256,
            source_file_name = Path.GetFileName(paths.Input),
            primary_backend = "libreoffice_writer_pdf",
            backend_identity = version.Identity,
            uno,
            requested_first_page = request.FirstPage,
            requested_last_page = request.LastPage,
            pdf_page_count = inspection?.PageCount,
            page_geometries = inspection?.PageGeometries,
            dpi = raster?.Dpi,
            derived_raster_backend = raster?.Provenance.Backend,
            execution,
            artifacts = manifestArtifacts,
            fidelity = new
            {
                class_name = "libreoffice_writer_fixed_layout",
                microsoft_word_layout_claimed = false,
                pixel_equivalence_claimed = false,
            },
            source_mutated = false,
            macro_prevention_behaviorally_verified = false,
            external_update_prevention_behaviorally_verified = false,
            silent_backend_fallback = false,
            public_artifacts_published_after_private_cleanup = true,
        };
        var manifestBytes = Encoding.UTF8.GetBytes(
            WordToolkitOperationJson.Serialize(manifest, indented: true) + "\n"
        );
        publications.Add(
            new RenderArtifactPublication(
                "manifest",
                Path.Combine(
                    paths.OutputDirectory,
                    request.ArtifactStem + ".render.json"
                ),
                "json",
                "application/json",
                manifestBytes,
                bytes =>
                    bytes.Span.Length > 1 && bytes.Span[0] == (byte)'{'
                        ? RenderArtifactValidationResult.Valid
                        : RenderArtifactValidationResult.Invalid(
                            "The LibreOffice render manifest is not a JSON object"
                        )
            )
        );
        metadata.Add(new PreparedLibreOfficeRenderMetadata("manifest", null, null, null));
        return new PreparedLibreOfficeRenderArtifacts(publications, metadata);

        void AddArtifact(
            string artifactId,
            string outputPath,
            string format,
            string mediaType,
            byte[] bytes,
            int? sourcePageNumber,
            int? pixelWidth,
            int? pixelHeight
        )
        {
            publications.Add(
                new RenderArtifactPublication(
                    artifactId,
                    outputPath,
                    format,
                    mediaType,
                    bytes,
                    format == "pdf" ? ValidatePdfBytes : ValidatePngBytes
                )
            );
            metadata.Add(
                new PreparedLibreOfficeRenderMetadata(
                    artifactId,
                    sourcePageNumber,
                    pixelWidth,
                    pixelHeight
                )
            );
            manifestArtifacts.Add(
                new
                {
                    artifact_id = artifactId,
                    file_name = Path.GetFileName(outputPath),
                    format,
                    media_type = mediaType,
                    bytes = bytes.LongLength,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    source_page_number = sourcePageNumber,
                    pixel_width = pixelWidth,
                    pixel_height = pixelHeight,
                }
            );
        }
    }

    private static void CleanupLibreOfficeRenderStaging(
        PopplerRasterizationStagingResult? raster,
        string stagingDirectory
    )
    {
        var cleanupFailed = false;
        if (raster is not null)
        {
            cleanupFailed |= !TryDeleteRenderDirectory(raster.StagingDirectory);
        }
        cleanupFailed |= !TryDeleteRenderDirectory(stagingDirectory);
        if (cleanupFailed)
        {
            throw new NativeToolException(
                "CLEANUP_FAILED",
                "LibreOffice private render staging cleanup failed before publication",
                new { public_artifact_state = "unchanged" }
            );
        }
    }

    private static NativeToolException MapOperationFailure(
        WordToolkitOperationException exception
    ) => new(exception.Code, exception.Message, exception.Details, exception.Retryable);

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private sealed record LibreOfficeRenderPaths(
        string Input,
        string OutputDirectory,
        string LibreOfficeExecutable,
        string JavaExecutable,
        string LibreOfficeJar
    );

    private sealed record PreparedLibreOfficeRenderMetadata(
        string ArtifactId,
        int? SourcePageNumber,
        int? PixelWidth,
        int? PixelHeight
    );

    private sealed record PreparedLibreOfficeRenderArtifacts(
        IReadOnlyList<RenderArtifactPublication> Publications,
        IReadOnlyList<PreparedLibreOfficeRenderMetadata> Metadata
    );
}
