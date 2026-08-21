using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WordToolkit.Native.Rendering;

internal enum PopplerRasterizerKind
{
    PdfToPpm,
    PdfToCairo,
}

internal enum PopplerPdfBackendError
{
    InvalidConfiguration,
    InputNotFound,
    InputLimitExceeded,
    ProcessFailed,
    ProcessTimedOut,
    MalformedOutput,
    PageLimitExceeded,
    DpiLimitExceeded,
    OutputLimitExceeded,
    PartialRasterization,
    StagingCleanupFailed,
}

internal sealed class PopplerPdfBackendException : Exception
{
    internal PopplerPdfBackendException(
        PopplerPdfBackendError error,
        string message,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        Error = error;
    }

    internal PopplerPdfBackendError Error { get; }
}

internal sealed record PopplerPdfBackendOptions(
    string PdfInfoExecutablePath,
    string RasterizerExecutablePath,
    PopplerRasterizerKind RasterizerKind,
    string StagingRootDirectory,
    TimeSpan ProcessTimeout,
    int MaximumPages,
    int MaximumDpi,
    long MaximumInputBytes,
    long MaximumOutputBytes,
    int MaximumProcessOutputCharacters = 256 * 1024
);

internal sealed record PopplerToolProvenance(
    string Component,
    string Version,
    string ExecutablePath
);

internal sealed record PopplerBackendProvenance(
    string Backend,
    PopplerToolProvenance PdfInfo,
    PopplerToolProvenance Rasterizer
);

internal sealed record PdfMediaBox(
    double LeftPoints,
    double BottomPoints,
    double RightPoints,
    double TopPoints
);

internal sealed record PdfPageGeometry(int PageNumber, PdfMediaBox MediaBox);

internal sealed record PopplerPdfInspection(
    int PageCount,
    IReadOnlyList<PdfPageGeometry> PageGeometries,
    PopplerBackendProvenance Provenance
);

internal sealed record PopplerRasterizedPage(
    int PageNumber,
    string StagingPath,
    long ByteLength,
    int PixelWidth,
    int PixelHeight,
    string Sha256
);

internal sealed record PopplerRasterizationStagingResult(
    string StagingDirectory,
    bool IsStagingOnly,
    int Dpi,
    IReadOnlyList<PopplerRasterizedPage> Pages,
    PopplerBackendProvenance Provenance
);

internal sealed record ExternalProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    int MaximumOutputCharacters
);

internal sealed record ExternalProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated = false,
    bool StandardErrorTruncated = false,
    bool TimedOut = false
);

internal interface IExternalProcessRunner
{
    Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!Path.IsPathFullyQualified(request.ExecutablePath))
        {
            throw new ArgumentException(
                "The executable path must be fully qualified.",
                nameof(request)
            );
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(request.ExecutablePath)!,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The external process did not start.");
        }

        var stdoutTask = ReadBoundedAsync(
            process.StandardOutput,
            request.MaximumOutputCharacters
        );
        var stderrTask = ReadBoundedAsync(
            process.StandardError,
            request.MaximumOutputCharacters
        );
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token
        );

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            var timedOutOutput = await stdoutTask.ConfigureAwait(false);
            var timedOutError = await stderrTask.ConfigureAwait(false);
            return new ExternalProcessResult(
                process.HasExited ? process.ExitCode : -1,
                timedOutOutput.Text,
                timedOutError.Text,
                timedOutOutput.Truncated,
                timedOutError.Truncated,
                TimedOut: true
            );
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ExternalProcessResult(
            process.ExitCode,
            stdout.Text,
            stderr.Text,
            stdout.Truncated,
            stderr.Truncated
        );
    }

    private static async Task<BoundedText> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters
    )
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            var remaining = maximumCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
            if (read > remaining)
            {
                truncated = true;
            }
        }
        return new BoundedText(builder.ToString(), truncated);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and Kill.
        }
    }

    private sealed record BoundedText(string Text, bool Truncated);
}

internal sealed partial class PopplerPdfBackend
{
    private const string BackendName = "poppler";
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly Regex VersionPattern = new(
        @"\b(pdfinfo|pdftoppm|pdftocairo)\s+version\s+([0-9][0-9A-Za-z.+-]*)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );
    private static readonly Regex PagesPattern = new(
        @"(?m)^Pages:\s*([0-9]+)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );
    private static readonly Regex PageMediaBoxPattern = new(
        @"(?m)^Page\s+([0-9]+)\s+MediaBox:\s*" + BoxNumberPattern + @"\s+" + BoxNumberPattern + @"\s+" + BoxNumberPattern + @"\s+" + BoxNumberPattern + @"\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );
    private static readonly Regex GenericMediaBoxPattern = new(
        @"(?m)^MediaBox:\s*" + BoxNumberPattern + @"\s+" + BoxNumberPattern + @"\s+" + BoxNumberPattern + @"\s+" + BoxNumberPattern + @"\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );
    private static readonly Regex RasterPageFilePattern = new(
        @"^page-([0-9]+)\.png$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );
    private const string BoxNumberPattern = @"([-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))";

    private readonly PopplerPdfBackendOptions _options;
    private readonly IExternalProcessRunner _processRunner;

    internal PopplerPdfBackend(
        PopplerPdfBackendOptions options,
        IExternalProcessRunner? processRunner = null
    )
    {
        ValidateOptions(options);
        _options = options with
        {
            PdfInfoExecutablePath = Path.GetFullPath(options.PdfInfoExecutablePath),
            RasterizerExecutablePath = Path.GetFullPath(options.RasterizerExecutablePath),
            StagingRootDirectory = Path.GetFullPath(options.StagingRootDirectory),
        };
        _processRunner = processRunner ?? new ExternalProcessRunner();
    }

    internal async Task<PopplerBackendProvenance> GetCapabilityAsync(
        CancellationToken cancellationToken = default
    )
    {
        var pdfInfo = await ProbeToolAsync(
                _options.PdfInfoExecutablePath,
                "pdfinfo",
                cancellationToken
            )
            .ConfigureAwait(false);
        var rasterizerComponent = _options.RasterizerKind switch
        {
            PopplerRasterizerKind.PdfToPpm => "pdftoppm",
            PopplerRasterizerKind.PdfToCairo => "pdftocairo",
            _ => throw new PopplerPdfBackendException(
                PopplerPdfBackendError.InvalidConfiguration,
                "The Poppler rasterizer kind is invalid."
            ),
        };
        var rasterizer = await ProbeToolAsync(
                _options.RasterizerExecutablePath,
                rasterizerComponent,
                cancellationToken
            )
            .ConfigureAwait(false);
        return new PopplerBackendProvenance(BackendName, pdfInfo, rasterizer);
    }

    internal async Task<PopplerPdfInspection> InspectAsync(
        string pdfPath,
        CancellationToken cancellationToken = default
    )
    {
        var fullPdfPath = ValidateInput(pdfPath);
        var provenance = await GetCapabilityAsync(cancellationToken).ConfigureAwait(false);
        var process = await RunCheckedAsync(
                _options.PdfInfoExecutablePath,
                ["-box", fullPdfPath],
                "pdfinfo",
                cancellationToken
            )
            .ConfigureAwait(false);

        var pageMatch = PagesPattern.Match(process.StandardOutput);
        if (!pageMatch.Success
            || !int.TryParse(
                pageMatch.Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var pageCount
            )
            || pageCount < 1)
        {
            throw Malformed("pdfinfo did not return a valid page count.");
        }
        if (pageCount > _options.MaximumPages)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.PageLimitExceeded,
                "The PDF page count exceeds the configured limit."
            );
        }

        var detailed = await RunCheckedAsync(
                _options.PdfInfoExecutablePath,
                [
                    "-f",
                    "1",
                    "-l",
                    pageCount.ToString(CultureInfo.InvariantCulture),
                    "-box",
                    fullPdfPath,
                ],
                "pdfinfo",
                cancellationToken
            )
            .ConfigureAwait(false);
        var geometries = ParseMediaBoxes(detailed.StandardOutput, pageCount);
        return new PopplerPdfInspection(pageCount, geometries, provenance);
    }

    internal async Task<PopplerRasterizationStagingResult> RasterizeAsync(
        string pdfPath,
        int firstPage,
        int lastPage,
        int dpi,
        CancellationToken cancellationToken = default
    )
    {
        if (dpi < 1 || dpi > _options.MaximumDpi)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.DpiLimitExceeded,
                "The requested DPI is outside the configured limit."
            );
        }
        if (firstPage < 1 || lastPage < firstPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstPage),
                "The requested page range is invalid."
            );
        }

        var selectedPageCount = checked(lastPage - firstPage + 1);
        if (selectedPageCount > _options.MaximumPages)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.PageLimitExceeded,
                "The requested page range exceeds the configured limit."
            );
        }

        var fullPdfPath = ValidateInput(pdfPath);
        var inspection = await InspectAsync(fullPdfPath, cancellationToken)
            .ConfigureAwait(false);
        if (lastPage > inspection.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastPage),
                "The requested page range exceeds the PDF page count."
            );
        }

        Directory.CreateDirectory(_options.StagingRootDirectory);
        var stagingDirectory = Path.Combine(
            _options.StagingRootDirectory,
            $".wordtoolkit-poppler-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(stagingDirectory);
        TryHideDirectory(stagingDirectory);

        try
        {
            var outputPrefix = Path.Combine(stagingDirectory, "page");
            await RunCheckedAsync(
                    _options.RasterizerExecutablePath,
                    [
                        "-png",
                        "-r",
                        dpi.ToString(CultureInfo.InvariantCulture),
                        "-f",
                        firstPage.ToString(CultureInfo.InvariantCulture),
                        "-l",
                        lastPage.ToString(CultureInfo.InvariantCulture),
                        fullPdfPath,
                        outputPrefix,
                    ],
                    _options.RasterizerKind == PopplerRasterizerKind.PdfToPpm
                        ? "pdftoppm"
                        : "pdftocairo",
                    cancellationToken
                )
                .ConfigureAwait(false);

            var pages = InspectRasterizedPages(
                stagingDirectory,
                firstPage,
                lastPage
            );
            return new PopplerRasterizationStagingResult(
                stagingDirectory,
                IsStagingOnly: true,
                dpi,
                pages,
                inspection.Provenance
            );
        }
        catch
        {
            if (!TryDeleteDirectory(stagingDirectory))
            {
                throw new PopplerPdfBackendException(
                    PopplerPdfBackendError.StagingCleanupFailed,
                    "The failed raster operation left untrusted data in its private staging directory."
                );
            }
            throw;
        }
    }

    private async Task<PopplerToolProvenance> ProbeToolAsync(
        string executablePath,
        string component,
        CancellationToken cancellationToken
    )
    {
        var result = await RunCheckedAsync(
                executablePath,
                ["-v"],
                component,
                cancellationToken
            )
            .ConfigureAwait(false);
        var versionText = string.Concat(result.StandardOutput, "\n", result.StandardError);
        var match = VersionPattern.Match(versionText);
        if (!match.Success
            || !string.Equals(
                match.Groups[1].Value,
                component,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw Malformed($"{component} did not return a parseable version.");
        }
        return new PopplerToolProvenance(component, match.Groups[2].Value, executablePath);
    }

    private async Task<ExternalProcessResult> RunCheckedAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string component,
        CancellationToken cancellationToken
    )
    {
        ExternalProcessResult result;
        try
        {
            result = await _processRunner
                .RunAsync(
                    new ExternalProcessRequest(
                        executablePath,
                        arguments,
                        _options.ProcessTimeout,
                        _options.MaximumProcessOutputCharacters
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.ProcessFailed,
                $"{component} could not be started.",
                exception
            );
        }

        if (result.TimedOut)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.ProcessTimedOut,
                $"{component} exceeded the configured timeout."
            );
        }
        if (result.ExitCode != 0)
        {
            var diagnostic = SanitizeStandardError(
                result.StandardError,
                result.StandardErrorTruncated
            );
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.ProcessFailed,
                $"{component} failed with exit code {result.ExitCode}; {diagnostic}."
            );
        }
        if (result.StandardOutputTruncated || result.StandardErrorTruncated)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.OutputLimitExceeded,
                $"{component} exceeded the configured diagnostic output limit."
            );
        }
        return result;
    }

    private IReadOnlyList<PopplerRasterizedPage> InspectRasterizedPages(
        string stagingDirectory,
        int firstPage,
        int lastPage
    )
    {
        var byPage = new Dictionary<int, string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(stagingDirectory))
        {
            var match = RasterPageFilePattern.Match(Path.GetFileName(path));
            if (!File.Exists(path)
                || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                || !match.Success
                || !int.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var pageNumber
                )
                || pageNumber < firstPage
                || pageNumber > lastPage
                || !byPage.TryAdd(pageNumber, path))
            {
                throw new PopplerPdfBackendException(
                    PopplerPdfBackendError.PartialRasterization,
                    "The rasterizer produced an unexpected page artifact."
                );
            }
        }

        var expectedCount = lastPage - firstPage + 1;
        if (byPage.Count != expectedCount
            || Enumerable.Range(firstPage, expectedCount).Any(page => !byPage.ContainsKey(page)))
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.PartialRasterization,
                "The rasterizer did not produce the complete requested page range."
            );
        }

        long totalBytes = 0;
        var result = new List<PopplerRasterizedPage>(expectedCount);
        foreach (var pageNumber in Enumerable.Range(firstPage, expectedCount))
        {
            var path = byPage[pageNumber];
            var fileInfo = new FileInfo(path);
            totalBytes = checked(totalBytes + fileInfo.Length);
            if (totalBytes > _options.MaximumOutputBytes)
            {
                throw new PopplerPdfBackendException(
                    PopplerPdfBackendError.OutputLimitExceeded,
                    "The rasterized output exceeds the configured byte limit."
                );
            }

            var (width, height) = ReadPngDimensions(path);
            using var stream = File.OpenRead(path);
            var digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            result.Add(
                new PopplerRasterizedPage(
                    pageNumber,
                    path,
                    fileInfo.Length,
                    width,
                    height,
                    digest
                )
            );
        }
        return result;
    }

    private static IReadOnlyList<PdfPageGeometry> ParseMediaBoxes(
        string output,
        int pageCount
    )
    {
        var geometries = new SortedDictionary<int, PdfPageGeometry>();
        foreach (Match match in PageMediaBoxPattern.Matches(output))
        {
            if (!int.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var pageNumber
                )
                || pageNumber < 1
                || pageNumber > pageCount
                || geometries.ContainsKey(pageNumber))
            {
                throw Malformed("pdfinfo returned an invalid page MediaBox entry.");
            }
            geometries.Add(pageNumber, new PdfPageGeometry(pageNumber, ParseBox(match, 2)));
        }

        if (geometries.Count == 0)
        {
            var genericMatch = GenericMediaBoxPattern.Match(output);
            if (!genericMatch.Success || pageCount != 1)
            {
                throw Malformed(
                    "pdfinfo did not return one unambiguous MediaBox per page."
                );
            }
            geometries.Add(1, new PdfPageGeometry(1, ParseBox(genericMatch, 1)));
        }
        if (geometries.Count != pageCount)
        {
            throw Malformed("pdfinfo did not return one MediaBox per page.");
        }
        return geometries.Values.ToArray();
    }

    private static PdfMediaBox ParseBox(Match match, int firstGroup)
    {
        var coordinates = new double[4];
        for (var index = 0; index < coordinates.Length; index++)
        {
            if (!double.TryParse(
                    match.Groups[firstGroup + index].Value,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out coordinates[index]
                )
                || !double.IsFinite(coordinates[index]))
            {
                throw Malformed("pdfinfo returned a malformed MediaBox coordinate.");
            }
        }
        if (coordinates[2] <= coordinates[0] || coordinates[3] <= coordinates[1])
        {
            throw Malformed("pdfinfo returned a non-positive MediaBox.");
        }
        return new PdfMediaBox(
            coordinates[0],
            coordinates[1],
            coordinates[2],
            coordinates[3]
        );
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) != header.Length
            || !header[..8].SequenceEqual(PngSignature)
            || !header[12..16].SequenceEqual("IHDR"u8))
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.MalformedOutput,
                "The rasterizer produced a malformed PNG artifact."
            );
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        if (width < 1 || height < 1)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.MalformedOutput,
                "The rasterizer produced invalid PNG dimensions."
            );
        }
        return (width, height);
    }

    private string ValidateInput(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !Path.IsPathFullyQualified(pdfPath))
        {
            throw new ArgumentException("The PDF path must be fully qualified.", nameof(pdfPath));
        }
        var fullPath = Path.GetFullPath(pdfPath);
        if (!File.Exists(fullPath))
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.InputNotFound,
                "The PDF input does not exist."
            );
        }
        if (new FileInfo(fullPath).Length > _options.MaximumInputBytes)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.InputLimitExceeded,
                "The PDF input exceeds the configured byte limit."
            );
        }
        return fullPath;
    }

    private static void ValidateOptions(PopplerPdfBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IsExplicitAbsolutePath(options.PdfInfoExecutablePath)
            || !IsExplicitAbsolutePath(options.RasterizerExecutablePath)
            || !IsExplicitAbsolutePath(options.StagingRootDirectory)
            || options.ProcessTimeout <= TimeSpan.Zero
            || options.MaximumPages < 1
            || options.MaximumDpi < 1
            || options.MaximumInputBytes < 1
            || options.MaximumOutputBytes < 1
            || options.MaximumProcessOutputCharacters < 1024)
        {
            throw new PopplerPdfBackendException(
                PopplerPdfBackendError.InvalidConfiguration,
                "The Poppler backend configuration is invalid."
            );
        }
    }

    private static bool IsExplicitAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && path.IndexOfAny(['\r', '\n', '\0']) < 0;

    private static string SanitizeStandardError(string standardError, bool truncated)
    {
        var bytes = Encoding.UTF8.GetBytes(standardError);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return $"stderr redacted (sha256={digest}, chars={standardError.Length}, truncated={truncated.ToString().ToLowerInvariant()})";
    }

    private static PopplerPdfBackendException Malformed(string message) =>
        new(PopplerPdfBackendError.MalformedOutput, message);

    private static void TryHideDirectory(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (UnauthorizedAccessException)
        {
            // The randomized staging directory remains private to this operation.
        }
        catch (IOException)
        {
            // Hidden is a defense-in-depth hint, not a publication boundary.
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
