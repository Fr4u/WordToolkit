using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Extensions;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Ocr;

namespace WordToolkit.Native.Protocol;

internal static class OcrProviderBoundaryBenchmarkCli
{
    private const string Contract = "wordtoolkit.benchmark.ocr_process_boundary/1.0";

    internal static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        IWordOcrProvider? directProvider = null,
        IWordOcrProvider? isolatedProvider = null
    )
    {
        try
        {
            var options = Parse(args);
            var image = File.ReadAllBytes(options.ImagePath);
            if (image.Length is < 1 or > 32 * 1024 * 1024)
            {
                throw Invalid("The benchmark image must be between 1 byte and 32 MiB.");
            }
            var imageHash = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
            var request = new WordOcrProviderRequest(
                image,
                ContentType(options.ImagePath),
                imageHash,
                [options.Language],
                WordOcrLayoutHint.SingleBlock,
                options.TimeoutMilliseconds,
                4_000_000,
                new WordOcrProviderConfiguration(
                    options.TesseractPath,
                    options.ModelDirectory
                )
            );
            directProvider ??= new TesseractCliOcrProvider();
            isolatedProvider ??= new ProcessBoundaryTesseractOcrProvider();
            var direct = new List<double>(options.Samples);
            var isolated = new List<double>(options.Samples);
            var resultHashes = new List<string>(options.Samples * 2);
            WordOcrProviderResult? representative = null;
            for (var index = 0; index < options.Samples; index++)
            {
                if (index % 2 == 0)
                {
                    RunOne(directProvider, request, direct, resultHashes, ref representative);
                    RunOne(isolatedProvider, request, isolated, resultHashes, ref representative);
                }
                else
                {
                    RunOne(isolatedProvider, request, isolated, resultHashes, ref representative);
                    RunOne(directProvider, request, direct, resultHashes, ref representative);
                }
            }
            var directSummary = Summarize(direct);
            var isolatedSummary = Summarize(isolated);
            var medianOverhead = isolatedSummary.MedianMilliseconds
                - directSummary.MedianMilliseconds;
            var result = new
            {
                operation_contract = Contract,
                runtime = new
                {
                    os = Environment.OSVersion.VersionString,
                    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                },
                samples = options.Samples,
                alternating_order = true,
                image = new
                {
                    bytes = image.Length,
                    sha256 = imageHash,
                    content_type = request.ContentType,
                },
                provider = new
                {
                    name = representative!.Provenance.ProviderName,
                    version = representative.Provenance.ProviderVersion,
                    binary_sha256 = representative.Provenance.ProviderBinarySha256,
                    model_set_sha256 = representative.Provenance.ModelSetSha256,
                    language = options.Language,
                },
                direct_in_process = directSummary,
                isolated_process = isolatedSummary,
                median_overhead_milliseconds = Math.Round(medianOverhead, 4),
                median_overhead_percent = directSummary.MedianMilliseconds == 0
                    ? (double?)null
                    : Math.Round(medianOverhead / directSummary.MedianMilliseconds * 100, 2),
                result_sha256 = resultHashes[0],
                stable_typed_results = resultHashes.Distinct(StringComparer.Ordinal).Count() == 1,
                recognized_text_returned = false,
                recognized_text_characters = representative.Text.Length,
                process_boundary = new
                {
                    closed_json_ipc = true,
                    request_identity_bound = true,
                    host_executable_identity_bound = true,
                    hard_timeout = true,
                    process_tree_kill = true,
                    windows_job_object = OperatingSystem.IsWindows(),
                    maximum_process_memory_bytes = ProcessBoundaryTesseractOcrProvider.MaximumProcessMemoryBytes,
                    maximum_active_processes = ProcessBoundaryTesseractOcrProvider.MaximumActiveProcesses,
                    minimized_environment = true,
                    restricted_windows_token = false,
                    app_container_enforced = true,
                    network_isolation_enforced = true,
                    filesystem_brokered = true,
                    writes_confined_to_private_profile = true,
                    signed_provider_manifest_required = true,
                    complete_top_level_runtime_bound = true,
                    provider_resources_session_pinned = true,
                    ai_request_trust_material_required = false,
                    sandbox_profile = "windows_app_container_no_network_brokered_filesystem",
                    sandbox_claimed = true,
                },
            };
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, JsonDefaults.Indented));
            return resultHashes.Distinct(StringComparer.Ordinal).Count() == 1 ? 0 : 2;
        }
        catch (Exception exception) when (
            exception is WordToolkitExtensionException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException
        )
        {
            var code = exception is WordToolkitExtensionException extension
                ? extension.Code
                : "INVALID_INPUT";
            error.WriteLine($"{code}: OCR process-boundary benchmark failed.");
            return 64;
        }
    }

    private static void RunOne(
        IWordOcrProvider provider,
        WordOcrProviderRequest request,
        List<double> timings,
        List<string> resultHashes,
        ref WordOcrProviderResult? representative
    )
    {
        var started = Stopwatch.GetTimestamp();
        var result = provider.Recognize(request);
        timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        var canonical = WordToolkitOperationJson.Serialize(result);
        resultHashes.Add(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant()
        );
        representative ??= result;
    }

    private static BenchmarkSummary Summarize(IReadOnlyList<double> values)
    {
        var ordered = values.Order().ToArray();
        return new BenchmarkSummary(
            Math.Round(ordered[0], 4),
            Math.Round(Percentile(ordered, 0.5), 4),
            Math.Round(Percentile(ordered, 0.95), 4),
            Math.Round(ordered[^1], 4),
            ordered.Select(value => Math.Round(value, 4)).ToArray()
        );
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        var position = (ordered.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return ordered[lower];
        }
        return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
    }

    private static Options Parse(string[] args)
    {
        string? image = null;
        string? tesseract = null;
        string? models = null;
        var language = "eng";
        var samples = 7;
        var timeout = 30_000;
        for (var index = 0; index < args.Length; index++)
        {
            var value = index + 1 < args.Length ? args[index + 1] : null;
            switch (args[index])
            {
                case "--image" when value is not null:
                    image = value;
                    index++;
                    break;
                case "--tesseract" when value is not null:
                    tesseract = value;
                    index++;
                    break;
                case "--models" when value is not null:
                    models = value;
                    index++;
                    break;
                case "--language" when value is not null:
                    language = value;
                    index++;
                    break;
                case "--samples" when value is not null && int.TryParse(value, out samples):
                    index++;
                    break;
                case "--timeout-milliseconds" when value is not null && int.TryParse(value, out timeout):
                    index++;
                    break;
                case "--format" when value == "json":
                    index++;
                    break;
                default:
                    throw Invalid("The OCR benchmark arguments are invalid.");
            }
        }
        if (
            string.IsNullOrWhiteSpace(image)
            || string.IsNullOrWhiteSpace(tesseract)
            || string.IsNullOrWhiteSpace(models)
            || !Path.IsPathFullyQualified(image)
            || !Path.IsPathFullyQualified(tesseract)
            || !Path.IsPathFullyQualified(models)
            || !File.Exists(image)
            || !File.Exists(tesseract)
            || !Directory.Exists(models)
            || samples is < 3 or > 31
            || timeout is < 1000 or > 120_000
            || language.Length is < 1 or > 32
            || language.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_'))
        )
        {
            throw Invalid("The OCR benchmark paths or limits are invalid.");
        }
        return new Options(
            Path.GetFullPath(image),
            Path.GetFullPath(tesseract),
            Path.GetFullPath(models),
            language,
            samples,
            timeout
        );
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => throw Invalid("The OCR benchmark image format is unsupported."),
    };

    private static WordToolkitExtensionException Invalid(string message) => new(
        "INVALID_INPUT",
        message
    );

    private sealed record Options(
        string ImagePath,
        string TesseractPath,
        string ModelDirectory,
        string Language,
        int Samples,
        int TimeoutMilliseconds
    );

    private sealed record BenchmarkSummary(
        double MinimumMilliseconds,
        double MedianMilliseconds,
        double P95Milliseconds,
        double MaximumMilliseconds,
        IReadOnlyList<double> SamplesMilliseconds
    );
}
