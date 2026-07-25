using System.Buffers.Binary;
using System.Security.Cryptography;
using WordToolkit.Native.Rendering;

namespace WordToolkit.Native.Tests;

public sealed class PopplerPdfBackendTests
{
    [Fact]
    public async Task InspectionReturnsOnlyPageGeometryAndVersionedProvenance()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner();
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);

        var result = await backend.InspectAsync(fixture.PdfPath);

        Assert.Equal(2, result.PageCount);
        Assert.Equal("poppler", result.Provenance.Backend);
        Assert.Equal("24.08.0", result.Provenance.PdfInfo.Version);
        Assert.Equal("24.08.0", result.Provenance.Rasterizer.Version);
        Assert.Equal(fixture.PdfInfoPath, result.Provenance.PdfInfo.ExecutablePath);
        Assert.Collection(
            result.PageGeometries,
            first =>
            {
                Assert.Equal(1, first.PageNumber);
                Assert.Equal(612, first.MediaBox.RightPoints);
                Assert.Equal(792, first.MediaBox.TopPoints);
            },
            second =>
            {
                Assert.Equal(2, second.PageNumber);
                Assert.Equal(595.28, second.MediaBox.RightPoints);
                Assert.Equal(841.89, second.MediaBox.TopPoints);
            }
        );
        Assert.All(runner.Requests, request => Assert.True(Path.IsPathFullyQualified(request.ExecutablePath)));
    }

    [Fact]
    public async Task RasterizationReturnsCompleteStagingArtifactsWithHashesAndDimensions()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner(writeRasterPages: true);
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);

        var result = await backend.RasterizeAsync(fixture.PdfPath, 1, 2, 144);

        Assert.True(result.IsStagingOnly);
        Assert.StartsWith(
            Path.GetFullPath(fixture.StagingRoot),
            Path.GetFullPath(result.StagingDirectory),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Collection(
            result.Pages,
            first => AssertPage(first, 1, 800, 1200),
            second => AssertPage(second, 2, 900, 1300)
        );
        Assert.All(result.Pages, page => Assert.StartsWith(result.StagingDirectory, page.StagingPath));

        Directory.Delete(result.StagingDirectory, recursive: true);
    }

    [Fact]
    public async Task PdfToCairoUsesCairoProvenanceAndAnArgumentList()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner(
            writeRasterPages: true,
            rasterizerComponent: "pdftocairo"
        );
        var backend = new PopplerPdfBackend(
            fixture.CreateOptions() with { RasterizerKind = PopplerRasterizerKind.PdfToCairo },
            runner
        );

        var result = await backend.RasterizeAsync(fixture.PdfPath, 2, 2, 300);

        Assert.Equal("pdftocairo", result.Provenance.Rasterizer.Component);
        Assert.Contains(
            runner.Requests,
            request =>
                request.ExecutablePath == fixture.RasterizerPath
                && request.Arguments.Contains("-png")
                && request.Arguments.Contains("300")
                && request.Arguments.Contains("2")
        );
        Directory.Delete(result.StagingDirectory, recursive: true);
    }

    [Fact]
    public async Task TimeoutIsReportedWithoutPublishingAStagingDirectory()
    {
        using var fixture = new Fixture();
        var runner = new RecordingProcessRunner((_, _) =>
            Task.FromResult(new ExternalProcessResult(-1, "", "secret", TimedOut: true))
        );
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);

        var exception = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            backend.RasterizeAsync(fixture.PdfPath, 1, 1, 96)
        );

        Assert.Equal(PopplerPdfBackendError.ProcessTimedOut, exception.Error);
        Assert.False(Directory.Exists(fixture.StagingRoot));
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var fixture = new Fixture();
        var runner = new RecordingProcessRunner(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            backend.InspectAsync(fixture.PdfPath, cancellation.Token)
        );
    }

    [Fact]
    public async Task MalformedPdfInfoOutputFailsClosed()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner(
            pdfInfoOutput: "Pages: banana\nMediaBox: 0 0 612 792"
        );
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);

        var exception = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            backend.InspectAsync(fixture.PdfPath)
        );

        Assert.Equal(PopplerPdfBackendError.MalformedOutput, exception.Error);
    }

    [Fact]
    public async Task GenericMediaBoxCannotBeInventedForEveryPage()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner(
            pdfInfoOutput: "Pages: 2\nMediaBox: 0 0 612 792\n"
        );
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);

        var exception = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            backend.InspectAsync(fixture.PdfPath)
        );

        Assert.Equal(PopplerPdfBackendError.MalformedOutput, exception.Error);
    }

    [Fact]
    public async Task PartialRasterizationDeletesAllOperationStaging()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner(writeRasterPages: true, omitLastRasterPage: true);
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);

        var exception = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            backend.RasterizeAsync(fixture.PdfPath, 1, 2, 96)
        );

        Assert.Equal(PopplerPdfBackendError.PartialRasterization, exception.Error);
        Assert.Empty(Directory.EnumerateDirectories(fixture.StagingRoot));
    }

    [Fact]
    public async Task OutputByteLimitDeletesAllOperationStaging()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner(writeRasterPages: true);
        var options = fixture.CreateOptions() with { MaximumOutputBytes = 40 };
        var backend = new PopplerPdfBackend(options, runner);

        var exception = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            backend.RasterizeAsync(fixture.PdfPath, 1, 2, 96)
        );

        Assert.Equal(PopplerPdfBackendError.OutputLimitExceeded, exception.Error);
        Assert.Empty(Directory.EnumerateDirectories(fixture.StagingRoot));
    }

    [Fact]
    public async Task UnexpectedRasterizerArtifactFailsClosedAndIsDeleted()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner(
            writeRasterPages: true,
            writeUnexpectedArtifact: true
        );
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);

        var exception = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            backend.RasterizeAsync(fixture.PdfPath, 1, 2, 96)
        );

        Assert.Equal(PopplerPdfBackendError.PartialRasterization, exception.Error);
        Assert.Empty(Directory.EnumerateDirectories(fixture.StagingRoot));
    }

    [Fact]
    public async Task PageDpiAndInputLimitsFailBeforeRasterizerExecution()
    {
        using var fixture = new Fixture();
        var runner = fixture.CreateSuccessfulRunner();

        var dpiBackend = new PopplerPdfBackend(
            fixture.CreateOptions() with { MaximumDpi = 150 },
            runner
        );
        var dpiException = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            dpiBackend.RasterizeAsync(fixture.PdfPath, 1, 1, 151)
        );
        Assert.Equal(PopplerPdfBackendError.DpiLimitExceeded, dpiException.Error);

        var pageBackend = new PopplerPdfBackend(
            fixture.CreateOptions() with { MaximumPages = 1 },
            runner
        );
        var pageException = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            pageBackend.InspectAsync(fixture.PdfPath)
        );
        Assert.Equal(PopplerPdfBackendError.PageLimitExceeded, pageException.Error);

        var inputBackend = new PopplerPdfBackend(
            fixture.CreateOptions() with { MaximumInputBytes = 1 },
            runner
        );
        var inputException = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            inputBackend.InspectAsync(fixture.PdfPath)
        );
        Assert.Equal(PopplerPdfBackendError.InputLimitExceeded, inputException.Error);
    }

    [Fact]
    public async Task ProcessFailureRedactsStderrContent()
    {
        using var fixture = new Fixture();
        const string secret = "CONFIDENTIAL DOCUMENT CONTENT C:\\private\\document.pdf";
        var runner = new RecordingProcessRunner((_, _) =>
            Task.FromResult(new ExternalProcessResult(7, "", secret))
        );
        var backend = new PopplerPdfBackend(fixture.CreateOptions(), runner);

        var exception = await Assert.ThrowsAsync<PopplerPdfBackendException>(() =>
            backend.InspectAsync(fixture.PdfPath)
        );

        Assert.Equal(PopplerPdfBackendError.ProcessFailed, exception.Error);
        Assert.DoesNotContain("CONFIDENTIAL", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("document.pdf", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stderr redacted", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sha256=", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeExecutableAndStagingPathsAreRejected()
    {
        using var fixture = new Fixture();

        var exception = Assert.Throws<PopplerPdfBackendException>(() =>
            new PopplerPdfBackend(
                fixture.CreateOptions() with
                {
                    PdfInfoExecutablePath = "pdfinfo.exe",
                    RasterizerExecutablePath = "pdftoppm.exe",
                    StagingRootDirectory = "staging",
                }
            )
        );

        Assert.Equal(PopplerPdfBackendError.InvalidConfiguration, exception.Error);
    }

    private static void AssertPage(
        PopplerRasterizedPage page,
        int expectedPage,
        int expectedWidth,
        int expectedHeight
    )
    {
        Assert.Equal(expectedPage, page.PageNumber);
        Assert.Equal(expectedWidth, page.PixelWidth);
        Assert.Equal(expectedHeight, page.PixelHeight);
        Assert.True(page.ByteLength >= 24);
        Assert.Equal(64, page.Sha256.Length);
        using var stream = File.OpenRead(page.StagingPath);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            page.Sha256
        );
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"wordtoolkit-poppler-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            PdfPath = Path.Combine(Root, "input.pdf");
            File.WriteAllBytes(PdfPath, "%PDF-1.7\n"u8.ToArray());
            PdfInfoPath = Path.Combine(Root, "tools", "pdfinfo.exe");
            RasterizerPath = Path.Combine(Root, "tools", "pdftoppm.exe");
            StagingRoot = Path.Combine(Root, "private-staging");
        }

        internal string Root { get; }

        internal string PdfPath { get; }

        internal string PdfInfoPath { get; }

        internal string RasterizerPath { get; }

        internal string StagingRoot { get; }

        internal PopplerPdfBackendOptions CreateOptions() =>
            new(
                PdfInfoPath,
                RasterizerPath,
                PopplerRasterizerKind.PdfToPpm,
                StagingRoot,
                TimeSpan.FromSeconds(5),
                MaximumPages: 10,
                MaximumDpi: 600,
                MaximumInputBytes: 1024 * 1024,
                MaximumOutputBytes: 1024 * 1024
            );

        internal RecordingProcessRunner CreateSuccessfulRunner(
            string? pdfInfoOutput = null,
            bool writeRasterPages = false,
            bool omitLastRasterPage = false,
            bool writeUnexpectedArtifact = false,
            string rasterizerComponent = "pdftoppm"
        ) =>
            new((request, _) =>
            {
                if (request.Arguments.SequenceEqual(["-v"]))
                {
                    var component = request.ExecutablePath == PdfInfoPath
                        ? "pdfinfo"
                        : rasterizerComponent;
                    return Task.FromResult(
                        new ExternalProcessResult(
                            0,
                            "",
                            $"{component} version 24.08.0\nCopyright Poppler"
                        )
                    );
                }
                if (request.ExecutablePath == PdfInfoPath)
                {
                    return Task.FromResult(
                        new ExternalProcessResult(
                            0,
                            pdfInfoOutput
                                ?? "Pages: 2\nPage 1 MediaBox: 0 0 612 792\nPage 2 MediaBox: 0 0 595.28 841.89\n",
                            ""
                        )
                    );
                }
                if (writeRasterPages)
                {
                    var firstPage = ArgumentValue(request.Arguments, "-f");
                    var lastPage = ArgumentValue(request.Arguments, "-l");
                    var prefix = request.Arguments[^1];
                    var effectiveLastPage = omitLastRasterPage ? lastPage - 1 : lastPage;
                    for (var page = firstPage; page <= effectiveLastPage; page++)
                    {
                        WritePng(
                            $"{prefix}-{page}.png",
                            page == 1 ? 800 : 900,
                            page == 1 ? 1200 : 1300
                        );
                    }
                    if (writeUnexpectedArtifact)
                    {
                        File.WriteAllText(
                            Path.Combine(Path.GetDirectoryName(prefix)!, "untrusted.txt"),
                            "must never be published"
                        );
                    }
                }
                return Task.FromResult(new ExternalProcessResult(0, "", ""));
            });

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static int ArgumentValue(IReadOnlyList<string> arguments, string name)
        {
            var index = arguments.ToList().IndexOf(name);
            return int.Parse(arguments[index + 1], System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void WritePng(string path, int width, int height)
        {
            Span<byte> header = stackalloc byte[24];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header);
            BinaryPrimitives.WriteUInt32BigEndian(header[8..12], 13);
            "IHDR"u8.CopyTo(header[12..16]);
            BinaryPrimitives.WriteInt32BigEndian(header[16..20], width);
            BinaryPrimitives.WriteInt32BigEndian(header[20..24], height);
            File.WriteAllBytes(path, header.ToArray());
        }
    }

    private sealed class RecordingProcessRunner(
        Func<ExternalProcessRequest, CancellationToken, Task<ExternalProcessResult>> callback
    ) : IExternalProcessRunner
    {
        internal List<ExternalProcessRequest> Requests { get; } = [];

        public Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return callback(request, cancellationToken);
        }
    }
}
