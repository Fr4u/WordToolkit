using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using WordToolkit.Engine.Extensions;
using WordToolkit.Native.Ocr;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class OcrProviderHostTests
{
    [Fact]
    public void AppContainerProfileCanBeCreatedOrOpenedWithAStablePrivateIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var profile = WindowsAppContainerProfile.CreateOrOpenOcrProviderProfile();

        Assert.Equal(WindowsAppContainerProfile.OcrProviderProfileName, profile.Name);
        Assert.StartsWith("S-1-15-2-", profile.SidValue, StringComparison.Ordinal);
        Assert.True(Directory.Exists(profile.FolderPath));
        Assert.True(Directory.Exists(Path.Combine(profile.FolderPath, "Temp")));
    }

    [Fact]
    public async Task AppContainerLauncherStartsTheNativeHostWithAnAppContainerToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var command = OcrProviderHostCommand.Current() with
        {
            InternalArgument = "--internal-appcontainer-probe",
        };
        var profile = WindowsAppContainerProfile.CreateOrOpenOcrProviderProfile();
        foreach (var directory in new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetDirectoryName(command.ExecutablePath)!,
            Path.GetDirectoryName(command.AssemblyIdentityPath)!,
        })
        {
            profile.GrantReadExecuteToDirectory(directory);
        }
        var temporaryDirectory = Path.Combine(profile.FolderPath, "Temp");
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot")!;
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = systemRoot,
            ["WINDIR"] = systemRoot,
            ["LOCALAPPDATA"] = profile.FolderPath,
            ["TEMP"] = temporaryDirectory,
            ["TMP"] = temporaryDirectory,
        };
        if (command.PassAssemblyAsArgument)
        {
            environment["DOTNET_ROOT"] = Path.GetDirectoryName(command.ExecutablePath)!;
        }

        using var process = WindowsAppContainerProcess.LaunchSuspended(
            command,
            profile,
            environment,
            temporaryDirectory
        );
        using var job = WindowsJobObject.Create(512L * 1024 * 1024, 2);
        job.Attach(process.ProcessHandle);
        process.Resume();
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);

        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, process.ExitCode);
        using var result = JsonDocument.Parse(stdout);
        Assert.Equal(
            "wordtoolkit.internal.appcontainer-probe/1.0",
            result.RootElement.GetProperty("contract").GetString()
        );
        Assert.True(result.RootElement.GetProperty("is_app_container").GetBoolean());
    }

    [Fact]
    public async Task AppContainerDeniesNetworkAndUnbrokeredFilesAndKeepsBrokerReadOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-appcontainer-test-" + Guid.NewGuid().ToString("N")
        );
        var unbrokeredDirectory = Path.Combine(directory, "unbrokered");
        var brokeredDirectory = Path.Combine(directory, "brokered");
        Directory.CreateDirectory(unbrokeredDirectory);
        Directory.CreateDirectory(brokeredDirectory);
        var unbrokeredRead = Path.Combine(unbrokeredDirectory, "read.bin");
        var unbrokeredWrite = Path.Combine(unbrokeredDirectory, "write.bin");
        var brokeredRead = Path.Combine(brokeredDirectory, "read.bin");
        var brokeredWrite = Path.Combine(brokeredDirectory, "write.bin");
        File.WriteAllBytes(unbrokeredRead, [1]);
        File.WriteAllBytes(brokeredRead, [2]);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var command = OcrProviderHostCommand.Current() with
            {
                InternalArgument = "--internal-appcontainer-probe",
            };
            var profile = WindowsAppContainerProfile.CreateOrOpenOcrProviderProfile();
            foreach (var readableDirectory in new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetDirectoryName(command.ExecutablePath)!,
                Path.GetDirectoryName(command.AssemblyIdentityPath)!,
                brokeredDirectory,
            })
            {
                profile.GrantReadExecuteToDirectory(readableDirectory);
            }
            var temporaryDirectory = Path.Combine(profile.FolderPath, "Temp");
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot")!;
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SystemRoot"] = systemRoot,
                ["WINDIR"] = systemRoot,
                ["LOCALAPPDATA"] = profile.FolderPath,
                ["TEMP"] = temporaryDirectory,
                ["TMP"] = temporaryDirectory,
            };
            if (command.PassAssemblyAsArgument)
            {
                environment["DOTNET_ROOT"] = Path.GetDirectoryName(command.ExecutablePath)!;
            }
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var request = JsonSerializer.Serialize(new
            {
                contract = "wordtoolkit.internal.appcontainer-probe-request/1.0",
                unbrokered_read_path = unbrokeredRead,
                unbrokered_write_path = unbrokeredWrite,
                brokered_read_path = brokeredRead,
                brokered_write_path = brokeredWrite,
                loopback_port = port,
            }, JsonDefaults.Compact);

            using var process = WindowsAppContainerProcess.LaunchSuspended(
                command,
                profile,
                environment,
                temporaryDirectory
            );
            using var job = WindowsJobObject.Create(512L * 1024 * 1024, 2);
            job.Attach(process.ProcessHandle);
            process.Resume();
            await process.StandardInput.WriteAsync(request);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);

            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, process.ExitCode);
            using var result = JsonDocument.Parse(stdout);
            var root = result.RootElement;
            Assert.True(root.GetProperty("is_app_container").GetBoolean());
            Assert.False(root.GetProperty("unbrokered_read_succeeded").GetBoolean());
            Assert.False(root.GetProperty("unbrokered_write_succeeded").GetBoolean());
            Assert.True(root.GetProperty("brokered_read_succeeded").GetBoolean());
            Assert.False(root.GetProperty("brokered_write_succeeded").GetBoolean());
            Assert.False(root.GetProperty("loopback_connect_succeeded").GetBoolean());
            Assert.False(listener.Pending());
            Assert.False(File.Exists(unbrokeredWrite));
            Assert.False(File.Exists(brokeredWrite));
        }
        finally
        {
            listener.Stop();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ClosedProtocolRoundTripsBoundRequestSuccessAndError()
    {
        var request = Request();
        var identity = OcrProviderHostIdentityResolver.Current();
        var requestId = OcrProviderHostProtocol.NewRequestId();
        var json = OcrProviderHostProtocol.SerializeRequest(
            request,
            requestId,
            identity,
            Binding()
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
            OcrProviderHostIdentityResolver.Current(),
            Binding()
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
            OcrProviderHostIdentityResolver.Current(),
            Binding()
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
            var boundary = json.RootElement.GetProperty("process_boundary");
            Assert.True(boundary.GetProperty("app_container_enforced").GetBoolean());
            Assert.True(boundary.GetProperty("network_isolation_enforced").GetBoolean());
            Assert.True(boundary.GetProperty("filesystem_brokered").GetBoolean());
            Assert.True(boundary.GetProperty("signed_provider_manifest_required").GetBoolean());
            Assert.True(boundary.GetProperty("complete_top_level_runtime_bound").GetBoolean());
            Assert.True(boundary.GetProperty("provider_resources_session_pinned").GetBoolean());
            Assert.False(boundary.GetProperty("ai_request_trust_material_required").GetBoolean());
            Assert.True(boundary.GetProperty("sandbox_claimed").GetBoolean());
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

    private static OcrProviderTrustBinding Binding()
    {
        var models = new[]
        {
            new OcrProviderTrustModelBinding("eng", "eng.traineddata", new string('b', 64)),
        };
        var runtimeFiles = new[]
        {
            new OcrProviderTrustRuntimeFileBinding("tesseract.exe", new string('a', 64)),
        };
        return new OcrProviderTrustBinding(
            OcrProviderTrustPolicy.BindingContract,
            TesseractCliOcrProvider.ExtensionId,
            "wordtoolkit.project",
            "release-2026",
            "1.0.0",
            "tesseract.exe",
            new string('a', 64),
            OcrProviderTrustPolicy.RuntimeSetHash(runtimeFiles),
            runtimeFiles,
            OcrProviderTrustPolicy.ModelSetHash(models),
            models,
            new string('c', 64),
            new string('d', 64)
        );
    }

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
