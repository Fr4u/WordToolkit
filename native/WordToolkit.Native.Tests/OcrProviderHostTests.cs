using System.Security.Cryptography;
using System.Text.Json;
using WordToolkit.Engine.Extensions;
using WordToolkit.Native.Ocr;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class OcrProviderHostTests
{
    [Fact]
    public void ClosedProtocolRoundTripsBoundRequestSuccessAndError()
    {
        var request = Request();
        var identity = OcrProviderHostIdentityResolver.Current();
        var requestId = OcrProviderHostProtocol.NewRequestId();
        var json = OcrProviderHostProtocol.SerializeRequest(
            request,
            requestId,
            identity
        );
        var parsed = OcrProviderHostProtocol.ParseRequest(json);

        Assert.Equal(OcrProviderHostProtocol.Contract, parsed.Protocol);
        Assert.Equal(requestId, parsed.RequestId);
        Assert.Equal(request.ImageBytes.ToArray(), parsed.ImageBytes);
        Assert.Equal(identity.ExecutableSha256, parsed.HostExecutableSha256);
        Assert.Equal(identity.AssemblySha256, parsed.HostAssemblySha256);

        var result = Result();
        var success = OcrProviderHostProtocol.ParseResponse(
            OcrProviderHostProtocol.SerializeSuccess(requestId, result),
            requestId
        );
        Assert.True(success.Ok);
        Assert.Equal("recognized", success.Result!.Text);
        Assert.Null(success.ErrorCode);

        var failure = OcrProviderHostProtocol.ParseResponse(
            OcrProviderHostProtocol.SerializeError(
                requestId,
                "OCR_PROVIDER_UNAVAILABLE",
                retryable: false
            ),
            requestId
        );
        Assert.False(failure.Ok);
        Assert.Equal("OCR_PROVIDER_UNAVAILABLE", failure.ErrorCode);
        Assert.Null(failure.Result);
    }

    [Fact]
    public void ClosedProtocolRejectsUnknownDuplicateUnboundAndUntypedPayloads()
    {
        var requestId = OcrProviderHostProtocol.NewRequestId();
        var requestJson = OcrProviderHostProtocol.SerializeRequest(
            Request(),
            requestId,
            OcrProviderHostIdentityResolver.Current()
        );
        var unknown = requestJson[..^1] + ",\"surprise\":true}";
        Assert.Equal(
            "EXTENSION_PROTOCOL_VIOLATION",
            Assert.Throws<WordToolkitExtensionException>(() =>
                OcrProviderHostProtocol.ParseRequest(unknown)
            ).Code
        );
        var duplicate = requestJson.Replace(
            "\"protocol\":",
            "\"protocol\":\"wordtoolkit.ocr-provider-host/1.0\",\"protocol\":",
            StringComparison.Ordinal
        );
        Assert.Equal(
            "EXTENSION_PROTOCOL_VIOLATION",
            Assert.Throws<WordToolkitExtensionException>(() =>
                OcrProviderHostProtocol.ParseRequest(duplicate)
            ).Code
        );

        var success = OcrProviderHostProtocol.SerializeSuccess(requestId, Result());
        Assert.Equal(
            "EXTENSION_PROTOCOL_VIOLATION",
            Assert.Throws<WordToolkitExtensionException>(() =>
                OcrProviderHostProtocol.ParseResponse(
                    success,
                    OcrProviderHostProtocol.NewRequestId()
                )
            ).Code
        );
        using var successJson = JsonDocument.Parse(success);
        var result = successJson.RootElement.GetProperty("result").GetRawText();
        var hostileResult = result[..^1] + ",\"implementation_type\":\"evil\"}";
        var hostile = $$"""
            {"protocol":"{{OcrProviderHostProtocol.Contract}}","request_id":"{{requestId}}","ok":true,"result":{{hostileResult}}}
            """;
        Assert.Equal(
            "EXTENSION_PROTOCOL_VIOLATION",
            Assert.Throws<WordToolkitExtensionException>(() =>
                OcrProviderHostProtocol.ParseResponse(hostile, requestId)
            ).Code
        );
    }

    [Fact]
    public async Task InternalHostUsesTypedProviderAndPublishesNoImplementationDetails()
    {
        var requestId = OcrProviderHostProtocol.NewRequestId();
        var input = OcrProviderHostProtocol.SerializeRequest(
            Request(),
            requestId,
            OcrProviderHostIdentityResolver.Current()
        );
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await OcrProviderHostCli.RunAsync(
            [],
            new StringReader(input),
            output,
            error,
            new FixedProvider()
        );

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        var response = OcrProviderHostProtocol.ParseResponse(
            output.ToString().Trim(),
            requestId
        );
        Assert.True(response.Ok);
        Assert.Equal("recognized", response.Result!.Text);
        Assert.DoesNotContain(
            nameof(FixedProvider),
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("C:\\", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProxyBindsHardProcessLimitsBeforeDelegating()
    {
        var client = new RecordingClient();
        var provider = new ProcessBoundaryTesseractOcrProvider(client);

        Assert.Equal("recognized", provider.Recognize(Request()).Text);
        Assert.Equal(
            ProcessBoundaryTesseractOcrProvider.MaximumProcessMemoryBytes,
            client.MaximumProcessMemoryBytes
        );
        Assert.Equal(
            ProcessBoundaryTesseractOcrProvider.MaximumActiveProcesses,
            client.MaximumActiveProcesses
        );
        Assert.Equal(7000, client.TimeoutMilliseconds);
    }

    [Fact]
    public void RealProcessBoundaryReturnsTypedProviderFailureWithoutLeakingPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotnet) || !File.Exists(dotnet))
        {
            return;
        }
        var assembly = typeof(WordToolkit.Native.Program).Assembly.Location;
        var client = new OcrProviderProcessHostClient(
            new OcrProviderHostCommand(dotnet, assembly, PassAssemblyAsArgument: true)
        );
        var missing = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-missing-" + Guid.NewGuid().ToString("N"),
            "tesseract.exe"
        );
        var request = Request() with
        {
            Configuration = new WordOcrProviderConfiguration(
                missing,
                Path.GetDirectoryName(missing)
            ),
        };

        var exception = Assert.Throws<WordToolkitExtensionException>(() =>
            client.Invoke(
                request,
                512L * 1024 * 1024,
                3,
                10_000,
                CancellationToken.None
            )
        );
        Assert.Equal("OCR_PROVIDER_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain(missing, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TesseractCliOcrProvider", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JobObjectTerminatesAProviderHostBlockedOnItsIpcChannel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotnet) || !File.Exists(dotnet))
        {
            return;
        }
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = dotnet,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(WordToolkit.Native.Program).Assembly.Location);
        start.ArgumentList.Add("--internal-ocr-provider-host");
        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("The provider host did not start.");
        using var job = WindowsJobObject.Create(512L * 1024 * 1024, 2);
        job.Attach(process);
        Assert.False(process.WaitForExit(100));

        job.Terminate();

        Assert.True(process.WaitForExit(5000));
        Assert.True(process.HasExited);
    }

    [Fact]
    public void BenchmarkContractIsContentFreeAndRejectsUnknownArguments()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-ocr-boundary-benchmark-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var image = Path.Combine(directory, "image.png");
            var executable = Path.Combine(directory, "provider.exe");
            var models = Path.Combine(directory, "models");
            Directory.CreateDirectory(models);
            File.WriteAllBytes(image, [1, 2, 3, 4]);
            File.WriteAllBytes(executable, [5, 6, 7, 8]);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = OcrProviderBoundaryBenchmarkCli.Run(
                [
                    "--image", image,
                    "--tesseract", executable,
                    "--models", models,
                    "--samples", "3",
                    "--format", "json",
                ],
                output,
                error,
                new FixedProvider(),
                new FixedProvider()
            );

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                "wordtoolkit.benchmark.ocr_process_boundary/1.0",
                json.RootElement.GetProperty("operation_contract").GetString()
            );
            Assert.True(json.RootElement.GetProperty("stable_typed_results").GetBoolean());
            Assert.False(json.RootElement.GetProperty("recognized_text_returned").GetBoolean());
            Assert.DoesNotContain(directory, output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("recognized\"", output.ToString(), StringComparison.Ordinal);

            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            exitCode = OcrProviderBoundaryBenchmarkCli.Run(
                ["--unknown", "value"],
                output,
                error,
                new FixedProvider(),
                new FixedProvider()
            );
            Assert.Equal(64, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("INVALID_INPUT", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WordOcrProviderRequest Request()
    {
        byte[] image = [1, 2, 3, 4];
        return new WordOcrProviderRequest(
            image,
            "image/png",
            Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(),
            ["eng"],
            WordOcrLayoutHint.Automatic,
            2000,
            1024,
            new WordOcrProviderConfiguration(
                Path.Combine(Path.GetTempPath(), "tesseract.exe"),
                Path.GetTempPath()
            )
        );
    }

    private static WordOcrProviderResult Result() => new(
        10,
        20,
        "recognized",
        [
            new WordOcrProviderLine(
                "recognized",
                0.9,
                new WordOcrPixelBox(0, 0, 10, 20),
                [
                    new WordOcrProviderWord(
                        "recognized",
                        0.9,
                        new WordOcrPixelBox(0, 0, 10, 20)
                    ),
                ]
            ),
        ],
        [],
        new WordOcrProviderProvenance(
            "fixed",
            "1.0",
            new string('a', 64),
            new string('b', 64),
            ["eng"],
            "normalized_0_to_1",
            NetworkUsed: false,
            DeterministicForBoundInputs: true
        )
    );

    private sealed class FixedProvider : IWordOcrProvider
    {
        public WordOcrProviderResult Recognize(
            WordOcrProviderRequest request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal([1, 2, 3, 4], request.ImageBytes.ToArray());
            return Result();
        }
    }

    private sealed class RecordingClient : IOcrProviderHostClient
    {
        internal long MaximumProcessMemoryBytes { get; private set; }
        internal uint MaximumActiveProcesses { get; private set; }
        internal int TimeoutMilliseconds { get; private set; }

        public WordOcrProviderResult Invoke(
            WordOcrProviderRequest request,
            long maximumProcessMemoryBytes,
            uint maximumActiveProcesses,
            int timeoutMilliseconds,
            CancellationToken cancellationToken
        )
        {
            MaximumProcessMemoryBytes = maximumProcessMemoryBytes;
            MaximumActiveProcesses = maximumActiveProcesses;
            TimeoutMilliseconds = timeoutMilliseconds;
            return Result();
        }
    }
}
