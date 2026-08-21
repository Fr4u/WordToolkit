using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealOcrAcceptanceTests
{
    [Fact]
    public async Task LocalTesseractRecognizesAnEmbeddedScanWithBoundProvenance()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_OCR_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var tesseract = RequiredProviderPath(
            "WORDTOOLKIT_TESSERACT_PATH",
            @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            expectDirectory: false
        );
        var models = RequiredProviderPath(
            "WORDTOOLKIT_TESSDATA_DIR",
            @"C:\Program Files\Tesseract-OCR\tessdata",
            expectDirectory: true
        );
        var magick = RequiredProviderPath(
            "WORDTOOLKIT_IMAGEMAGICK_PATH",
            @"C:\Program Files\ImageMagick-7.1.2-Q16-HDRI\magick.exe",
            expectDirectory: false
        );
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-real-ocr",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var imagePath = Path.Combine(directory, "offline-ocr.png");
        var packagePath = Path.Combine(directory, "offline-ocr.docx");
        var wordBefore = WordProcessCount();
        try
        {
            CreateScan(magick, imagePath);
            File.WriteAllBytes(
                packagePath,
                OcrPackageCliTests.BuildPackage(File.ReadAllBytes(imagePath))
            );
            var sourceHash = HashFile(packagePath);
            var operation = new OcrWordPackageOperation(NativeExtensionHost.Registry);
            var inspected = operation.Inspect(new OcrCandidateInspectionRequest(
                packagePath,
                IncludeHashes: true,
                IncludeSource: true
            ));
            var candidate = Assert.Single(inspected.Items);
            Assert.True(candidate.Eligible);
            Assert.Equal("/word/media/image1.png", candidate.SourcePartUri);
            Assert.NotNull(candidate.ImageSha256);

            var arguments = new JsonObject
            {
                ["local_path"] = packagePath,
                ["expected_package_fingerprint"] = inspected.PackageFingerprint,
                ["candidate_ids"] = new JsonArray(candidate.CandidateId),
                ["privacy_mode"] = "local_only",
                ["languages"] = new JsonArray("eng"),
                ["layout_hint"] = "single_block",
                ["provider_executable_path"] = tesseract,
                ["provider_model_directory"] = models,
                ["detail"] = "words",
                ["include_text"] = true,
                ["include_hashes"] = true,
                ["minimum_mean_confidence"] = 0.7,
            };
            var rpc = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "execute_wordtoolkit_action",
                    ["arguments"] = new JsonObject
                    {
                        ["action"] = OcrWordPackageContract.RecognizeOperationName,
                        ["arguments"] = arguments,
                        ["response_mode"] = "full",
                    },
                },
            };
            var output = new StringWriter();
            var host = new NoInvokeHost();
            var catalog = ToolCatalog.LoadNativeWordTools();
            var server = new McpServer(
                new StringReader(rpc.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
                output,
                catalog,
                new WordLiveService(host)
            );

            await server.RunAsync();

            var response = JsonNode.Parse(output.ToString().Trim())!.AsObject();
            var structured = response["result"]!["structuredContent"]!;
            Assert.True(
                structured["ok"]!.GetValue<bool>(),
                structured.ToJsonString(JsonDefaults.Indented)
            );
            var schema = catalog.InspectAction(
                OcrWordPackageContract.RecognizeOperationName
            )["tool"]!["outputSchema"]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            Assert.True(structured["ok"]!.GetValue<bool>());
            var data = structured["data"]!.AsObject();
            var result = Assert.Single(data["results"]!.AsArray())!.AsObject();
            var text = result["text"]!.GetValue<string>();
            Assert.Contains("WORDTOOLKIT", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OFFLINE", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OCR", text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(candidate.ImageSha256, result["source_image_sha256"]!.GetValue<string>());
            Assert.Equal(64, result["text_sha256"]!.GetValue<string>().Length);
            Assert.True(result["confidence"]!["mean"]!.GetValue<double>() >= 0.7);
            Assert.True(result["returned_line_count"]!.GetValue<int>() > 0);
            Assert.True(result["word_count"]!.GetValue<int>() >= 3);
            var provenance = result["provenance"]!.AsObject();
            Assert.Equal("tesseract-cli", provenance["provider_name"]!.GetValue<string>());
            Assert.Equal(64, provenance["provider_binary_sha256"]!.GetValue<string>().Length);
            Assert.Equal(64, provenance["model_set_sha256"]!.GetValue<string>().Length);
            Assert.False(provenance["network_used"]!.GetValue<bool>());
            Assert.Equal("local_only", provenance["privacy_mode"]!.GetValue<string>());
            Assert.True(data["disclosure"]!["source_file_hash_reverified"]!.GetValue<bool>());
            Assert.False(data["disclosure"]!["image_bytes_returned"]!.GetValue<bool>());
            Assert.False(data["disclosure"]!["raw_provider_output_returned"]!.GetValue<bool>());
            Assert.False(data["disclosure"]!["word_opened"]!.GetValue<bool>());
            Assert.Equal(sourceHash, HashFile(packagePath));
            Assert.Equal(0, host.InvocationCount);
            Assert.Equal(wordBefore, WordProcessCount());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreateScan(string magick, string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = magick,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-size", "1800x400", "canvas:white", "-font", "Arial", "-pointsize", "104",
            "-fill", "black", "-gravity", "center", "-annotate", "+0+0",
            "WORDTOOLKIT OFFLINE OCR", outputPath,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ImageMagick did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "ImageMagick timed out.");
        Assert.True(
            process.ExitCode == 0,
            $"ImageMagick failed: {stdout} {stderr}"
        );
        Assert.True(new FileInfo(outputPath).Length > 0);
    }

    private static string RequiredProviderPath(
        string environmentVariable,
        string fallback,
        bool expectDirectory
    )
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        var path = string.IsNullOrWhiteSpace(value) ? fallback : value;
        Assert.True(
            expectDirectory ? Directory.Exists(path) : File.Exists(path),
            $"Configure {environmentVariable} with an existing absolute path."
        );
        return Path.GetFullPath(path);
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static int WordProcessCount()
    {
        var processes = Process.GetProcessesByName("WINWORD");
        try
        {
            return processes.Length;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public int InvocationCount { get; private set; }

        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        )
        {
            InvocationCount++;
            throw new Xunit.Sdk.XunitException("Offline OCR must not invoke Microsoft Word.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
