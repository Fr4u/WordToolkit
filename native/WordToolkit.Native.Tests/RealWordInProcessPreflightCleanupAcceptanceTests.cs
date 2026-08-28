using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordInProcessPreflightCleanupAcceptanceTests
{
    [Fact]
    public async Task RepeatedInProcessPreflightReleasesScratchDocumentsAndComReferences()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_PREFLIGHT_RCW_TEST"
            ) != "1"
        )
        {
            return;
        }
        var baseline = Process.GetProcessesByName("WINWORD")
            .Select(process =>
            {
                using (process) return process.Id;
            })
            .ToHashSet();
        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is null)
            {
                if (!launchIfMissing)
                {
                    throw new InvalidOperationException("Dedicated Word was not created.");
                }
                var type = Type.GetTypeFromProgID("Word.Application", throwOnError: true)!;
                ownedApplication = Activator.CreateInstance(type)!;
            }
            return ownedApplication;
        }

        await using (var host = new WordComHost(CreateApplication))
        {
            var service = new WordLiveService(host);
            using var startArguments = JsonDocument.Parse("{\"visible\":false}");
            _ = await service.CallAsync(
                "start_word_application",
                startArguments.RootElement,
                CancellationToken.None
            );
            using var arguments = JsonDocument.Parse(
                """
                {
                  "equations": [
                    {"value":"\\frac{x}{y}","input_format":"latex","verify_readback":true},
                    {"value":"\\phantom{z}","input_format":"latex","verify_readback":true},
                    {"value":"_(a)^(b)x","input_format":"unicodemath","verify_readback":true}
                  ]
                }
                """
            );
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var result = await service.PreflightEquationsInProcessAsync(
                    arguments.RootElement,
                    CancellationToken.None
                );
                using var json = JsonDocument.Parse(
                    JsonSerializer.Serialize(result, JsonDefaults.Compact)
                );
                Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
                Assert.True(
                    json.RootElement.GetProperty("isolation")
                        .GetProperty("scratch_document_closed").GetBoolean()
                );
                Assert.Equal(
                    0,
                    await host.InvokeAsync(
                        application => (int)application.Documents.Count,
                        launchIfMissing: false
                    )
                );
            }
            await host.InvokeAsync(
                application =>
                {
                    application.Quit(0);
                    return true;
                },
                launchIfMissing: false
            );
        }
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var after = Process.GetProcessesByName("WINWORD")
                .Select(process =>
                {
                    using (process) return process.Id;
                })
                .ToHashSet();
            if (after.IsSubsetOf(baseline))
            {
                return;
            }
            await Task.Delay(250);
        }
        Assert.Empty(
            Process.GetProcessesByName("WINWORD")
                .Select(process =>
                {
                    using (process) return process.Id;
                })
                .Except(baseline)
        );
    }
}
