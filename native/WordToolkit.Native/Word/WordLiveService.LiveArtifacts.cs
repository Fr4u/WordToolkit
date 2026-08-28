using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WordToolkit.Engine.Rendering;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Rendering;

namespace WordToolkit.Native.Word;

internal static class LiveWordArtifactContract
{
    public const string Contract = "wordtoolkit.export_live_word_artifacts/1.0";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LiveWordArtifactRequest
{
    public required string LiveDocumentId { get; init; }
    public required long ExpectedVersion { get; init; }
    public required string OutputDirectory { get; init; }
    public required string ArtifactStem { get; init; }
    public FixedRenderOutputKind Output { get; init; } = FixedRenderOutputKind.Pdf;
    public int Dpi { get; init; } = 144;
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
    private async Task<object> ExportLiveWordArtifactsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        LiveWordArtifactRequest request;
        try
        {
            request = WordToolkit.Engine.Operations.WordToolkitOperationJson
                .Deserialize<LiveWordArtifactRequest>(arguments.GetRawText());
        }
        catch (JsonException)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Live artifact arguments are invalid or contain unsupported fields"
            );
        }
        var record = Record(request.LiveDocumentId);
        CheckVersion(record, request.ExpectedVersion);
        var outputDirectory = ValidateLiveArtifactRequest(request);
        var stagingDirectory = Path.Combine(
            outputDirectory,
            $".wordtoolkit-live-render-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(stagingDirectory);
        var stagingPdfPath = Path.Combine(stagingDirectory, "source.pdf");
        PopplerRasterizationStagingResult? raster = null;
        PopplerPdfInspection? inspection = null;
        Exception? primaryFailure = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var baseline = await _host.InvokeAsync<LiveArtifactBaseline>(
                application =>
                {
                    CheckVersion(record, request.ExpectedVersion);
                    dynamic document = ResolveDocument(application, record);
                    var snapshot = CaptureLiveRollbackSnapshot(document, record.Version);
                    var equationRenderSource = EquationRenderQa.ScanLiveDocument(document);
                    document.ExportAsFixedFormat(
                        stagingPdfPath,
                        WordExportFormatPdf,
                        false,
                        request.OptimizeFor == "print" ? 0 : 1,
                        0,
                        1,
                        1,
                        0,
                        request.IncludeDocumentProperties,
                        true,
                        request.Bookmarks switch
                        {
                            "none" => 0,
                            "headings" => 1,
                            "bookmarks" => 2,
                            _ => 0,
                        },
                        true,
                        true,
                        request.PdfA
                    );
                    if (
                        !File.Exists(stagingPdfPath)
                        || new FileInfo(stagingPdfPath).Length == 0
                    )
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word did not create a non-empty live-document PDF"
                        );
                    }
                    CheckVersion(record, request.ExpectedVersion);
                    EnsureTargetUnchangedBeforePublication(document, snapshot, record);
                    return new LiveArtifactBaseline(
                        snapshot,
                        Convert.ToString(application.Version) ?? "",
                        Convert.ToString(application.Build) ?? "",
                        DocumentCompatibilityMode(document),
                        equationRenderSource
                    );
                },
                WordComReplaySafety.ReplaySafe,
                cancellationToken
            );
            ValidatePdfArtifact(stagingPdfPath);

            if (request.Output is not FixedRenderOutputKind.Pdf)
            {
                var backendRequest = new FixedRenderWordPackageRequest
                {
                    LocalPath = record.FullName,
                    ExpectedPackageFingerprint =
                        baseline.Snapshot.DocumentSemanticWordOpenXmlSha256,
                    OutputDirectory = outputDirectory,
                    ArtifactStem = request.ArtifactStem,
                    Output = request.Output,
                    Dpi = request.Dpi,
                    PdfInfoPath = request.PdfInfoPath,
                    RasterizerPath = request.RasterizerPath,
                    RasterizerKind = request.RasterizerKind,
                };
                var poppler = CreatePopplerBackend(backendRequest, stagingDirectory);
                inspection = await poppler.InspectAsync(stagingPdfPath, cancellationToken);
                raster = await poppler.RasterizeAsync(
                    stagingPdfPath,
                    firstPage: 1,
                    lastPage: inspection.PageCount,
                    request.Dpi,
                    cancellationToken
                );
                ValidateLiveRasterGeometry(inspection, raster);
            }

            var equationRenderQa = EquationRenderQa.Analyze(
                raster,
                baseline.EquationRenderSource
            );
            var prepared = PrepareLiveArtifactPublications(
                request,
                outputDirectory,
                stagingPdfPath,
                baseline,
                record.Id,
                record.Version,
                inspection,
                raster,
                equationRenderQa
            );
            var publisher = new TransactionalRenderArtifactPublisher();
            var published = publisher.PublishCreateNew(
                prepared.Publications,
                cancellationToken
            );
            var artifacts = published.Select(descriptor =>
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
            }).ToArray();
            var pageGeometries = inspection?.PageGeometries.Select(geometry => new
            {
                source_page_number = geometry.PageNumber,
                left_points = geometry.MediaBox.LeftPoints,
                bottom_points = geometry.MediaBox.BottomPoints,
                right_points = geometry.MediaBox.RightPoints,
                top_points = geometry.MediaBox.TopPoints,
                width_points = geometry.MediaBox.RightPoints - geometry.MediaBox.LeftPoints,
                height_points = geometry.MediaBox.TopPoints - geometry.MediaBox.BottomPoints,
            }).ToArray() ?? [];

            return new
            {
                operation_contract = LiveWordArtifactContract.Contract,
                live_document_id = record.Id,
                live_version = record.Version,
                source_state_sha256 =
                    baseline.Snapshot.DocumentSemanticWordOpenXmlSha256,
                source_saved = baseline.Snapshot.Saved,
                source_included_unsaved_changes = !baseline.Snapshot.Saved,
                source_mutated = false,
                document_reopened = false,
                document_saved = false,
                output = ToSnakeCase(request.Output.ToString()),
                page_geometry_count = pageGeometries.Length,
                page_geometries = pageGeometries,
                equation_render_qa = equationRenderQa,
                warnings = LiveArtifactWarnings(request, inspection is not null),
                backend = new
                {
                    primary = "microsoft_word_pdf",
                    word_version = baseline.WordVersion,
                    word_build = baseline.WordBuild,
                    compatibility_mode = baseline.CompatibilityMode,
                    pdf_geometry_inspected = inspection is not null,
                    rasterizer = raster?.Provenance.Backend,
                    pdfinfo_version = raster?.Provenance.PdfInfo.Version,
                    rasterizer_version = raster?.Provenance.Rasterizer.Version,
                },
                artifacts,
                artifact_count = artifacts.Length,
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
                        "Live artifact export failed and private staging cleanup could not be proven",
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
                    "Live render staging cleanup could not be proven",
                    new { public_artifact_state = "unchanged_or_committed" }
                );
            }
        }
    }

    private static string ValidateLiveArtifactRequest(LiveWordArtifactRequest request)
    {
        if (request.ExpectedVersion < 0)
        {
            throw new NativeToolException("INVALID_INPUT", "expected_version cannot be negative");
        }
        if (
            string.IsNullOrWhiteSpace(request.OutputDirectory)
            || string.IsNullOrWhiteSpace(request.ArtifactStem)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "output_directory and artifact_stem are required"
            );
        }
        if (
            request.ArtifactStem.Length > FixedRenderWordPackageContract.MaximumArtifactStemCharacters
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
        if (request.Dpi is < 1 or > FixedRenderWordPackageContract.MaximumDpi)
        {
            throw new NativeToolException("LIMIT_EXCEEDED", "dpi is outside the render limit");
        }
        if (request.OptimizeFor is not ("print" or "screen"))
        {
            throw new NativeToolException("INVALID_INPUT", "optimize_for must be print or screen");
        }
        if (request.Bookmarks is not ("none" or "headings" or "bookmarks"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "bookmarks must be none, headings, or bookmarks"
            );
        }
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        RejectNonLocalRenderPath(outputDirectory);
        if (!Directory.Exists(outputDirectory))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                "output_directory must already exist"
            );
        }
        return outputDirectory;
    }

    private static LivePreparedArtifacts PrepareLiveArtifactPublications(
        LiveWordArtifactRequest request,
        string outputDirectory,
        string stagingPdfPath,
        LiveArtifactBaseline baseline,
        string liveDocumentId,
        long liveVersion,
        PopplerPdfInspection? inspection,
        PopplerRasterizationStagingResult? raster,
        object equationRenderQa
    )
    {
        var publications = new List<RenderArtifactPublication>();
        var metadata = new List<PreparedFixedRenderMetadata>();
        var manifestArtifacts = new List<object>();

        if (request.Output is FixedRenderOutputKind.Pdf or FixedRenderOutputKind.PdfAndPngPages)
        {
            AddArtifact(
                "pdf",
                Path.Combine(outputDirectory, request.ArtifactStem + ".pdf"),
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
            foreach (var page in raster.Pages.OrderBy(page => page.PageNumber))
            {
                AddArtifact(
                    $"page_{page.PageNumber:D4}",
                    Path.Combine(
                        outputDirectory,
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

        var manifestPath = Path.Combine(
            outputDirectory,
            request.ArtifactStem + ".render.json"
        );
        var manifest = new
        {
            operation_contract = LiveWordArtifactContract.Contract,
            live_document_id = liveDocumentId,
            live_version = liveVersion,
            source_kind = "connected_live_word_document",
            source_state_sha256 = baseline.Snapshot.DocumentSemanticWordOpenXmlSha256,
            source_saved = baseline.Snapshot.Saved,
            source_included_unsaved_changes = !baseline.Snapshot.Saved,
            source_mutated = false,
            document_reopened = false,
            document_saved = false,
            primary_backend = "microsoft_word_pdf",
            word_version = baseline.WordVersion,
            word_build = baseline.WordBuild,
            compatibility_mode = baseline.CompatibilityMode,
            derived_raster_backend = raster?.Provenance.Backend,
            page_count = inspection?.PageCount,
            dpi = raster?.Dpi,
            artifacts = manifestArtifacts,
            equation_render_qa = equationRenderQa,
            warnings = LiveArtifactWarnings(request, inspection is not null),
        };
        var manifestBytes = Encoding.UTF8.GetBytes(
            WordToolkit.Engine.Operations.WordToolkitOperationJson.Serialize(
                manifest,
                indented: true
            ) + "\n"
        );
        publications.Add(
            new RenderArtifactPublication(
                "manifest",
                manifestPath,
                "json",
                "application/json",
                manifestBytes,
                bytes => bytes.Span.Length > 1 && bytes.Span[0] == (byte)'{'
                    ? RenderArtifactValidationResult.Valid
                    : RenderArtifactValidationResult.Invalid(
                        "The live render manifest is not a JSON object"
                    )
            )
        );
        metadata.Add(new PreparedFixedRenderMetadata("manifest", null, null, null));
        return new LivePreparedArtifacts(publications, metadata);

        void AddArtifact(
            string artifactId,
            string outputPath,
            string format,
            string mediaType,
            byte[] bytes,
            int? pageNumber,
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
                    format == "pdf" ? ValidatePdfBytes : ValidatePngBytes
                )
            );
            metadata.Add(
                new PreparedFixedRenderMetadata(
                    artifactId,
                    pageNumber,
                    pixelWidth,
                    pixelHeight
                )
            );
            manifestArtifacts.Add(new
            {
                artifact_id = artifactId,
                file_name = Path.GetFileName(outputPath),
                format,
                media_type = mediaType,
                bytes = bytes.LongLength,
                sha256,
                source_page_number = pageNumber,
                pixel_width = pixelWidth,
                pixel_height = pixelHeight,
            });
        }
    }

    private static void ValidateLiveRasterGeometry(
        PopplerPdfInspection inspection,
        PopplerRasterizationStagingResult raster
    )
    {
        if (
            inspection.PageGeometries.Count != raster.Pages.Count
            || raster.Pages.Count != inspection.PageCount
        )
        {
            throw new NativeToolException(
                "RENDER_VALIDATION_FAILED",
                "PDF geometry and live-document PNG page counts do not agree"
            );
        }
        var geometries = inspection.PageGeometries.ToDictionary(item => item.PageNumber);
        foreach (var page in raster.Pages)
        {
            if (!geometries.TryGetValue(page.PageNumber, out var geometry))
            {
                throw new NativeToolException(
                    "RENDER_VALIDATION_FAILED",
                    "A live-document raster page has no matching PDF MediaBox"
                );
            }
            var expectedWidth = (
                geometry.MediaBox.RightPoints - geometry.MediaBox.LeftPoints
            ) * raster.Dpi / 72d;
            var expectedHeight = (
                geometry.MediaBox.TopPoints - geometry.MediaBox.BottomPoints
            ) * raster.Dpi / 72d;
            if (
                Math.Abs(page.PixelWidth - expectedWidth) > 1.01
                || Math.Abs(page.PixelHeight - expectedHeight) > 1.01
            )
            {
                throw new NativeToolException(
                    "RENDER_VALIDATION_FAILED",
                    "Live-document PNG dimensions do not match the PDF MediaBox",
                    new { page_number = page.PageNumber }
                );
            }
        }
    }

    private static string[] LiveArtifactWarnings(
        LiveWordArtifactRequest request,
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

    private sealed record LivePreparedArtifacts(
        IReadOnlyList<RenderArtifactPublication> Publications,
        IReadOnlyList<PreparedFixedRenderMetadata> Metadata
    );

    internal sealed record LiveArtifactBaseline(
        LiveRollbackSnapshot Snapshot,
        string WordVersion,
        string WordBuild,
        int CompatibilityMode,
        EquationRenderSourceScan EquationRenderSource
    );
}
