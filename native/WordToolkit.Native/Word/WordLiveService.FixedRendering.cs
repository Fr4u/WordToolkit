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

internal static class FixedRenderWordPackageContract
{
    public const string OperationName = "render_ooxml_fixed_artifacts";
    public const string Contract = "wordtoolkit.render_ooxml_fixed_artifacts/1.0";
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumArtifactStemCharacters = 128;
    public const int MaximumPages = 500;
    public const int MaximumDpi = 600;
    public const long MaximumInputPdfBytes = 512L * 1024 * 1024;
    public const long MaximumRasterBytes = 512L * 1024 * 1024;
    public static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(120);
}

internal enum FixedRenderOutputKind
{
    Pdf,
    PngPages,
    PdfAndPngPages,
}

internal enum FixedRenderRasterizerKind
{
    PdfToPpm,
    PdfToCairo,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record FixedRenderWordPackageRequest
{
    public required string LocalPath { get; init; }
    public required string ExpectedPackageFingerprint { get; init; }
    public required string OutputDirectory { get; init; }
    public required string ArtifactStem { get; init; }
    public FixedRenderOutputKind Output { get; init; } = FixedRenderOutputKind.Pdf;
    public int FirstPage { get; init; } = 1;
    public int? LastPage { get; init; }
    public int Dpi { get; init; } = 144;
    public bool IncludeMarkup { get; init; }
    public string OptimizeFor { get; init; } = "print";
    public bool IncludeDocumentProperties { get; init; } = true;
    public string Bookmarks { get; init; } = "headings";
    public bool PdfA { get; init; }
    [JsonPropertyName("pdfinfo_path")]
    public string? PdfInfoPath { get; init; }
    public string? RasterizerPath { get; init; }
    public FixedRenderRasterizerKind RasterizerKind { get; init; } =
        FixedRenderRasterizerKind.PdfToPpm;
}

internal sealed partial class WordLiveService
{
    private async Task<object> RenderPackageFixedArtifactsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        FixedRenderWordPackageRequest request;
        try
        {
            request = WordToolkitOperationJson.Deserialize<FixedRenderWordPackageRequest>(
                arguments.GetRawText()
            );
        }
        catch (JsonException)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Fixed render arguments are invalid or contain unsupported fields"
            );
        }

        var paths = ValidateFixedRenderRequest(request);
        var sourceSha256Before = Sha256File(paths.Input, cancellationToken);
        OpcPackageSnapshot package;
        try
        {
            package = new OpcPackageReader().Read(paths.Input, cancellationToken);
        }
        catch (OpcPackageSourceChangedException)
        {
            throw new NativeToolException(
                "SOURCE_CHANGED",
                "The fixed render source changed while its stable snapshot was captured",
                new
                {
                    recommended_action =
                        "wait_for_save_or_connect_live_document_then_export_live_word_artifacts",
                },
                retryable: true
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The fixed render source cannot be read with the current filesystem access"
            );
        }
        catch (IOException)
        {
            throw new NativeToolException(
                "SOURCE_CHANGED",
                "The fixed render source is busy or cannot be snapshotted safely; use the connected live-document artifact export when it is open in Word",
                new
                {
                    reason = "source_busy_or_exclusive_share",
                    recommended_action = "export_live_word_artifacts",
                },
                retryable: true
            );
        }
        catch (InvalidDataException)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The fixed render source could not be read as an OPC package"
            );
        }
        if (!package.IsStructurallyValid)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The fixed render source failed structural OPC validation"
            );
        }
        if (
            !string.Equals(
                package.Fingerprint,
                request.ExpectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The render source does not match expected_package_fingerprint"
            );
        }
        var execution = ResolveFixedRenderIntent(request, package.Fingerprint);

        var stagingDirectory = Path.Combine(
            paths.OutputDirectory,
            $".wordtoolkit-fixed-render-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(stagingDirectory);
        var stagingPdfPath = Path.Combine(stagingDirectory, "source.pdf");
        PopplerRasterizationStagingResult? raster = null;
        PopplerPdfInspection? inspection = null;
        Exception? primaryFailure = null;
        try
        {
            var word = await _host.InvokeAsync<WordFixedFormatExportObservation>(
                application =>
                    WordFixedFormatExporter.ExportSavedPackage(
                        (object)application,
                        paths.Input,
                        stagingPdfPath,
                        new WordFixedFormatExportOptions(
                            request.FirstPage,
                            request.LastPage,
                            request.IncludeMarkup,
                            OptimizeForPrint: request.OptimizeFor == "print",
                            request.IncludeDocumentProperties,
                            request.Bookmarks,
                            request.PdfA
                        )
                    ),
                WordComReplaySafety.NonReplayable,
                cancellationToken,
                launchIfMissing: true
            );
            ValidatePdfArtifact(stagingPdfPath);

            var sourceSha256AfterWord = Sha256File(paths.Input, cancellationToken);
            if (!string.Equals(sourceSha256Before, sourceSha256AfterWord, StringComparison.Ordinal))
            {
                throw new NativeToolException(
                    "VERSION_CONFLICT",
                    "The source package changed while Word was rendering; no artifact was published",
                    new
                    {
                        package_fingerprint_before = package.Fingerprint,
                        source_mutation_attributed = false,
                    }
                );
            }

            if (request.Output is not FixedRenderOutputKind.Pdf)
            {
                var poppler = CreatePopplerBackend(request, stagingDirectory);
                inspection = await poppler.InspectAsync(
                    stagingPdfPath,
                    cancellationToken
                );
                if (inspection.PageCount != word.ExportedPageCount)
                {
                    throw new NativeToolException(
                        "RENDER_VALIDATION_FAILED",
                        "Word and the PDF inspector disagree on the exported page count",
                        new
                        {
                            word_page_count = word.ExportedPageCount,
                            pdf_page_count = inspection.PageCount,
                        }
                    );
                }
                raster = await poppler.RasterizeAsync(
                    stagingPdfPath,
                    firstPage: 1,
                    lastPage: inspection.PageCount,
                    request.Dpi,
                    cancellationToken
                );
                ValidateRasterGeometry(word, inspection, raster);
            }

            var equationRenderQa = EquationRenderQa.Analyze(
                raster,
                EquationRenderQa.ScanPackage(paths.Input)
            );
            var prepared = PrepareFixedRenderArtifacts(
                request,
                paths,
                stagingPdfPath,
                word,
                inspection,
                raster,
                execution,
                package.Fingerprint,
                sourceSha256Before,
                equationRenderQa
            );
            var publisher = new TransactionalRenderArtifactPublisher();
            var published = publisher.PublishCreateNew(
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
                    source_page_number = checked(
                        word.ExportedFirstPage + item.PageNumber - 1
                    ),
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
            var warnings = FixedRenderWarnings(request, inspection is not null);

            return new
            {
                operation_contract = FixedRenderWordPackageContract.Contract,
                package_fingerprint = package.Fingerprint,
                source_sha256 = sourceSha256Before,
                source_file_name = Path.GetFileName(paths.Input),
                source_mutated = false,
                output_created = true,
                output = ToSnakeCase(request.Output.ToString()),
                requested_first_page = request.FirstPage,
                requested_last_page = request.LastPage,
                exported_first_page = word.ExportedFirstPage,
                exported_last_page = word.ExportedLastPage,
                exported_page_count = word.ExportedPageCount,
                page_geometry_count = pageGeometries.Length,
                page_geometries = pageGeometries,
                equation_render_qa = equationRenderQa,
                warnings,
                dpi = request.Output is FixedRenderOutputKind.Pdf ? (int?)null : request.Dpi,
                backend = new
                {
                    primary = "microsoft_word_pdf",
                    word_version = word.ApplicationVersion,
                    word_build = word.ApplicationBuild,
                    compatibility_mode = word.CompatibilityMode,
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
                    fidelity = ToSnakeCase(
                        execution.Intent.Fidelity.RequiredLevel.ToString()
                    ),
                    resolution_count = execution.Resolutions.Length,
                    all_resolved = execution.Resolutions.All(item =>
                        item.State == RenderResolutionState.Resolved
                    ),
                    silent_fallback = false,
                },
                fidelity = new
                {
                    class_name = "word_authoritative_fixed_layout",
                    paginated = true,
                    exact_text_metrics = true,
                    pixel_equivalence_claimed = false,
                    png_is_derived_from_exact_pdf = raster is not null,
                },
                artifacts = artifactResults,
                artifact_count = artifactResults.Length,
                safety = new
                {
                    source_opened_read_only = word.ReadOnly,
                    source_hash_verified_after_close = true,
                    macros_forced_disabled = word.MacrosForcedDisabled,
                    link_updates_disabled = word.LinkUpdatesDisabled,
                    active_content_executed = false,
                    external_resources_loaded = false,
                    added_to_recent_files = word.AddedToRecentFiles,
                    opened_visible = word.OpenedVisible,
                    raw_xml_returned = false,
                    document_text_returned = false,
                    silent_backend_fallback = false,
                },
                runtime = "dotnet-native",
                python_used = false,
                performance = Performance(started),
            };
        }
        catch (PopplerPdfBackendException exception)
        {
            primaryFailure = MapPopplerFailure(exception);
            throw primaryFailure;
        }
        catch (WordToolkitOperationException exception)
        {
            primaryFailure = new NativeToolException(
                exception.Code,
                exception.Message,
                exception.Details,
                exception.Retryable
            );
            throw primaryFailure;
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            var cleanupFailed = false;
            if (raster is not null)
            {
                cleanupFailed |= !TryDeleteRenderDirectory(raster.StagingDirectory);
            }
            cleanupFailed |= !TryDeleteRenderDirectory(stagingDirectory);
            if (cleanupFailed && !cancellationToken.IsCancellationRequested)
            {
                if (primaryFailure is not null)
                {
                    throw new NativeToolException(
                        "ROLLBACK_FAILED",
                        "Fixed render failed and private staging cleanup could not be proven",
                        new
                        {
                            original_error_code = primaryFailure
                                is NativeToolException native
                                    ? native.ErrorCode
                                    : "EXTERNAL_TOOL_FAILED",
                            original_error_message = primaryFailure.Message[..Math.Min(
                                primaryFailure.Message.Length,
                                256
                            )],
                            cleanup_failed = true,
                            public_artifact_state = "unchanged_or_committed",
                            raw_document_content_returned = false,
                        }
                    );
                }
                throw new NativeToolException(
                    "ROLLBACK_FAILED",
                    "Fixed render staging cleanup could not be proven",
                    new { public_artifact_state = "unchanged_or_committed" }
                );
            }
        }
    }

    private static FixedRenderPaths ValidateFixedRenderRequest(
        FixedRenderWordPackageRequest request
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.LocalPath)
            || string.IsNullOrWhiteSpace(request.ExpectedPackageFingerprint)
            || string.IsNullOrWhiteSpace(request.OutputDirectory)
            || string.IsNullOrWhiteSpace(request.ArtifactStem)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "local_path, expected_package_fingerprint, output_directory and artifact_stem are required"
            );
        }
        if (
            request.LocalPath.Length > FixedRenderWordPackageContract.MaximumLocalPathCharacters
            || request.OutputDirectory.Length
                > FixedRenderWordPackageContract.MaximumLocalPathCharacters
        )
        {
            throw new NativeToolException("LIMIT_EXCEEDED", "A render path is too long");
        }
        if (
            request.ExpectedPackageFingerprint.Length != 64
            || !request.ExpectedPackageFingerprint.All(Uri.IsHexDigit)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (
            request.ArtifactStem.Length
                > FixedRenderWordPackageContract.MaximumArtifactStemCharacters
            || request.ArtifactStem is "." or ".."
            || request.ArtifactStem.StartsWith(".", StringComparison.Ordinal)
            || request.ArtifactStem.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "artifact_stem accepts only ASCII letters, digits, hyphen and underscore"
            );
        }
        if (request.FirstPage < 1 || request.LastPage < request.FirstPage)
        {
            throw new NativeToolException("INVALID_INPUT", "The page interval is invalid");
        }
        if (
            request.LastPage is > FixedRenderWordPackageContract.MaximumPages
            || request.Dpi is < 1 or > FixedRenderWordPackageContract.MaximumDpi
        )
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "The page or DPI limit was exceeded"
            );
        }
        if (request.OptimizeFor is not ("print" or "screen"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "optimize_for must be print or screen"
            );
        }
        if (request.Bookmarks is not ("none" or "headings" or "bookmarks"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "bookmarks must be none, headings, or bookmarks"
            );
        }

        var input = Path.GetFullPath(request.LocalPath);
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        RejectNonLocalRenderPath(input);
        RejectNonLocalRenderPath(outputDirectory);
        if (!InspectWordPackageContract.IsSupportedFileName(input))
        {
            throw new NativeToolException(
                "UNSUPPORTED_FORMAT",
                "Fixed rendering accepts DOCX, DOCM, DOTX, or DOTM files"
            );
        }
        if (!File.Exists(input))
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The fixed render source does not exist"
            );
        }
        if (!Directory.Exists(outputDirectory))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                "output_directory must already exist"
            );
        }
        return new FixedRenderPaths(input, outputDirectory);
    }

    private static PreparedFixedRenderArtifacts PrepareFixedRenderArtifacts(
        FixedRenderWordPackageRequest request,
        FixedRenderPaths paths,
        string stagingPdfPath,
        WordFixedFormatExportObservation word,
        PopplerPdfInspection? inspection,
        PopplerRasterizationStagingResult? raster,
        ResolvedRenderExecutionIntent execution,
        string packageFingerprint,
        string sourceSha256,
        object equationRenderQa
    )
    {
        var publications = new List<RenderArtifactPublication>();
        var metadata = new List<PreparedFixedRenderMetadata>();
        var sidecarItems = new List<object>();
        if (request.Output is FixedRenderOutputKind.Pdf or FixedRenderOutputKind.PdfAndPngPages)
        {
            AddPreparedArtifact(
                "pdf",
                Path.Combine(paths.OutputDirectory, request.ArtifactStem + ".pdf"),
                "pdf",
                "application/pdf",
                File.ReadAllBytes(stagingPdfPath),
                sourcePageNumber: null,
                pixelWidth: null,
                pixelHeight: null
            );
        }
        if (raster is not null)
        {
            foreach (var page in raster.Pages.OrderBy(item => item.PageNumber))
            {
                var sourcePage = checked(word.ExportedFirstPage + page.PageNumber - 1);
                AddPreparedArtifact(
                    $"page_{sourcePage:D4}",
                    Path.Combine(
                        paths.OutputDirectory,
                        $"{request.ArtifactStem}-page-{sourcePage:D4}.png"
                    ),
                    "png",
                    "image/png",
                    File.ReadAllBytes(page.StagingPath),
                    sourcePage,
                    page.PixelWidth,
                    page.PixelHeight
                );
            }
        }

        var manifestPath = Path.Combine(
            paths.OutputDirectory,
            request.ArtifactStem + ".render.json"
        );
        var manifestPayload = new
        {
            operation_contract = FixedRenderWordPackageContract.Contract,
            package_fingerprint = packageFingerprint,
            source_sha256 = sourceSha256,
            source_file_name = Path.GetFileName(paths.Input),
            primary_backend = "microsoft_word_pdf",
            word_version = word.ApplicationVersion,
            word_build = word.ApplicationBuild,
            compatibility_mode = word.CompatibilityMode,
            exported_first_page = word.ExportedFirstPage,
            exported_last_page = word.ExportedLastPage,
            exported_page_count = word.ExportedPageCount,
            pdf_page_count = inspection?.PageCount,
            page_geometries = inspection?.PageGeometries
                .OrderBy(item => item.PageNumber)
                .Select(item => new
                {
                    source_page_number = checked(
                        word.ExportedFirstPage + item.PageNumber - 1
                    ),
                    left_points = item.MediaBox.LeftPoints,
                    bottom_points = item.MediaBox.BottomPoints,
                    right_points = item.MediaBox.RightPoints,
                    top_points = item.MediaBox.TopPoints,
                })
                .ToArray() ?? [],
            dpi = raster?.Dpi,
            derived_raster_backend = raster?.Provenance.Backend,
            execution,
            equation_render_qa = equationRenderQa,
            artifacts = sidecarItems,
            source_mutated = false,
            active_content_executed = false,
            external_resources_loaded = false,
            silent_backend_fallback = false,
            warnings = FixedRenderWarnings(request, inspection is not null),
        };
        var manifestBytes = Encoding.UTF8.GetBytes(
            WordToolkitOperationJson.Serialize(manifestPayload, indented: true) + "\n"
        );
        publications.Add(
            new RenderArtifactPublication(
                "manifest",
                manifestPath,
                "json",
                "application/json",
                manifestBytes,
                bytes =>
                    bytes.Span.Length > 1 && bytes.Span[0] == (byte)'{'
                        ? RenderArtifactValidationResult.Valid
                        : RenderArtifactValidationResult.Invalid(
                            "The render manifest is not a JSON object"
                        )
            )
        );
        metadata.Add(new PreparedFixedRenderMetadata("manifest", null, null, null));
        return new PreparedFixedRenderArtifacts(publications, metadata);

        void AddPreparedArtifact(
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
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            publications.Add(
                new RenderArtifactPublication(
                    artifactId,
                    outputPath,
                    format,
                    mediaType,
                    bytes,
                    format == "pdf"
                        ? ValidatePdfBytes
                        : format == "png"
                            ? ValidatePngBytes
                            : null
                )
            );
            metadata.Add(
                new PreparedFixedRenderMetadata(
                    artifactId,
                    sourcePageNumber,
                    pixelWidth,
                    pixelHeight
                )
            );
            sidecarItems.Add(
                new
                {
                    artifact_id = artifactId,
                    file_name = Path.GetFileName(outputPath),
                    format,
                    media_type = mediaType,
                    bytes = bytes.LongLength,
                    sha256,
                    source_page_number = sourcePageNumber,
                    pixel_width = pixelWidth,
                    pixel_height = pixelHeight,
                }
            );
        }
    }

    private static string[] FixedRenderWarnings(
        FixedRenderWordPackageRequest request,
        bool geometryInspected
    )
    {
        var warnings = new List<string>
        {
            "subjective_visual_review_required",
            "word_layout_is_not_pixel_equivalence",
        };
        if (request.Output is FixedRenderOutputKind.Pdf && !geometryInspected)
        {
            warnings.Add("pdf_geometry_not_inspected");
        }
        return warnings.ToArray();
    }

    private static PopplerPdfBackend CreatePopplerBackend(
        FixedRenderWordPackageRequest request,
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

    private static ResolvedRenderExecutionIntent ResolveFixedRenderIntent(
        FixedRenderWordPackageRequest request,
        string packageFingerprint
    )
    {
        var target = request.FirstPage == 1 && request.LastPage is null
            ? new RenderTargetIntent(RenderTargetKind.WholeDocument)
            : new RenderTargetIntent(
                RenderTargetKind.PageRange,
                $"{request.FirstPage}:{request.LastPage?.ToString() ?? "end"}"
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
                "The fixed render output kind is unsupported"
            ),
        };
        var outputFormat = ToSnakeCase(request.Output.ToString());
        var capabilities = new List<string>
        {
            "pagination",
            "source_read_only",
            "transactional_publication",
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
            new RenderOutputIntent(outputFormat, outputArtifacts),
            new RenderFidelityIntent(
                RenderFidelityLevel.LayoutExact,
                allowApproximation: false,
                capabilities
            )
        );
        var profileArtifacts = outputArtifacts.Select(item =>
            new RenderBackendArtifact(item.ArtifactKind, item.MediaType)
        );
        var profile = new RenderBackendProfile(
            "microsoft-word-fixed-format",
            "1.0",
            [RenderSourceKind.SavedWordPackage],
            [RenderTargetKind.WholeDocument, RenderTargetKind.PageRange],
            [
                new RenderBackendOutput(
                    outputFormat,
                    RenderOutputCardinality.ArtifactBundle,
                    profileArtifacts
                ),
            ],
            RenderFidelityLevel.LayoutExact,
            capabilities.Select(capability =>
                new RenderBackendCapability(
                    capability,
                    RenderResolutionState.Resolved
                )
            ),
            loadsExternalResources: false,
            executesActiveContent: false
        );
        try
        {
            return RenderExecutionIntentValidator.ValidateAndResolve(intent, profile);
        }
        catch (WordToolkitOperationException exception)
        {
            throw new NativeToolException(
                exception.Code,
                exception.Message,
                exception.Details,
                exception.Retryable
            );
        }
    }

    private static void ValidateRasterGeometry(
        WordFixedFormatExportObservation word,
        PopplerPdfInspection inspection,
        PopplerRasterizationStagingResult raster
    )
    {
        if (
            inspection.PageGeometries.Count != raster.Pages.Count
            || raster.Pages.Count != word.ExportedPageCount
        )
        {
            throw new NativeToolException(
                "RENDER_VALIDATION_FAILED",
                "PDF geometry, PNG pages and Word export counts do not agree"
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
            var widthPoints = geometry.MediaBox.RightPoints
                - geometry.MediaBox.LeftPoints;
            var heightPoints = geometry.MediaBox.TopPoints
                - geometry.MediaBox.BottomPoints;
            var expectedWidth = widthPoints * raster.Dpi / 72d;
            var expectedHeight = heightPoints * raster.Dpi / 72d;
            if (
                Math.Abs(page.PixelWidth - expectedWidth) > 1.01
                || Math.Abs(page.PixelHeight - expectedHeight) > 1.01
            )
            {
                throw new NativeToolException(
                    "RENDER_VALIDATION_FAILED",
                    "PNG dimensions do not match the PDF MediaBox at the requested DPI",
                    new { page_number = page.PageNumber }
                );
            }
        }
    }

    private static string ResolveExplicitExecutable(
        string? requestValue,
        string environmentVariable,
        string argumentName
    )
    {
        var configured = string.IsNullOrWhiteSpace(requestValue)
            ? Environment.GetEnvironmentVariable(environmentVariable)
            : requestValue;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new NativeToolException(
                "RENDER_BACKEND_UNAVAILABLE",
                $"{argumentName} or {environmentVariable} is required; PATH fallback is disabled"
            );
        }
        var path = Path.GetFullPath(configured);
        RejectNonLocalRenderPath(path);
        if (!File.Exists(path))
        {
            throw new NativeToolException(
                "RENDER_BACKEND_UNAVAILABLE",
                $"The configured {argumentName} executable does not exist"
            );
        }
        return path;
    }

    private static void RejectNonLocalRenderPath(string path)
    {
        var candidate = path.TrimStart();
        if (
            candidate.StartsWith(@"\\", StringComparison.Ordinal)
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith(@"\??\", StringComparison.Ordinal)
            || candidate.StartsWith("/??/", StringComparison.Ordinal)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Fixed render paths must be local and cannot use UNC or device namespaces"
            );
        }
    }

    private static string Sha256File(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
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

    private static void ValidatePdfArtifact(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 5)
        {
            throw new NativeToolException(
                "RENDER_VALIDATION_FAILED",
                "Word did not create a non-empty PDF artifact"
            );
        }
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[5];
        if (stream.Read(signature) != signature.Length || !signature.SequenceEqual("%PDF-"u8))
        {
            throw new NativeToolException(
                "RENDER_VALIDATION_FAILED",
                "Word output does not have a PDF signature"
            );
        }
    }

    private static RenderArtifactValidationResult ValidatePdfBytes(
        ReadOnlyMemory<byte> bytes
    ) => bytes.Span.StartsWith("%PDF-"u8)
        ? RenderArtifactValidationResult.Valid
        : RenderArtifactValidationResult.Invalid("The staged PDF signature is invalid");

    private static RenderArtifactValidationResult ValidatePngBytes(
        ReadOnlyMemory<byte> bytes
    ) => bytes.Span.Length >= 8
        && bytes.Span[..8].SequenceEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }
        )
        ? RenderArtifactValidationResult.Valid
        : RenderArtifactValidationResult.Invalid("The staged PNG signature is invalid");

    private static NativeToolException MapPopplerFailure(
        PopplerPdfBackendException exception
    ) => new(
        exception.Error switch
        {
            PopplerPdfBackendError.InvalidConfiguration
                or PopplerPdfBackendError.InputNotFound => "RENDER_BACKEND_UNAVAILABLE",
            PopplerPdfBackendError.ProcessTimedOut => "TIMEOUT",
            PopplerPdfBackendError.PageLimitExceeded
                or PopplerPdfBackendError.DpiLimitExceeded
                or PopplerPdfBackendError.InputLimitExceeded
                or PopplerPdfBackendError.OutputLimitExceeded => "LIMIT_EXCEEDED",
            PopplerPdfBackendError.StagingCleanupFailed => "ROLLBACK_FAILED",
            _ => "RENDER_VALIDATION_FAILED",
        },
        exception.Message
    );

    private static bool TryDeleteRenderDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            return !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private sealed record FixedRenderPaths(string Input, string OutputDirectory);

    private sealed record PreparedFixedRenderMetadata(
        string ArtifactId,
        int? SourcePageNumber,
        int? PixelWidth,
        int? PixelHeight
    );

    private sealed record PreparedFixedRenderArtifacts(
        IReadOnlyList<RenderArtifactPublication> Publications,
        IReadOnlyList<PreparedFixedRenderMetadata> Metadata
    );
}
