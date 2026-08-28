using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordIsolatedEquationPreflightAcceptanceTests
{
    [Fact]
    public async Task DedicatedWorkerBuildsEquationsAndLeavesNoWordProcess()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_ISOLATED_PREFLIGHT_TEST"
            ) != "1"
        )
        {
            return;
        }
        var baseline = WordProcessIds();
        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "equations": [
                {"value":"x^2+1","input_format":"latex","display":true},
                {"value":"\\int_0^1 x^2 \\,\\mathrm{d}x","input_format":"latex","display":true}
              ],
              "per_equation_timeout_seconds": 20,
              "total_timeout_seconds": 60
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        var root = json.RootElement;
        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Equal(2, root.GetProperty("equation_count").GetInt32());
        Assert.True(
            root.GetProperty("isolation")
                .GetProperty("dedicated_word_process_verified")
                .GetBoolean()
        );
        Assert.True(
            root.GetProperty("isolation")
                .GetProperty("worker_cleanup_verified")
                .GetBoolean()
        );
        Assert.False(host.ApplicationOwnedByRuntime);
        await AssertWordProcessSetReturnsToAsync(baseline);
    }

    [Fact]
    public async Task HungEquationReturnsExactIndexBeforeOuterToolTimeoutAndCleansWorker()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_ISOLATED_PREFLIGHT_TEST"
            ) != "1"
        )
        {
            return;
        }
        var baseline = WordProcessIds();
        var oldMode = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_INTERNAL_EQUATION_PREFLIGHT_TEST_MODE"
        );
        var oldIndex = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_INTERNAL_EQUATION_PREFLIGHT_HANG_INDEX"
        );
        Environment.SetEnvironmentVariable(
            "WORDTOOLKIT_INTERNAL_EQUATION_PREFLIGHT_TEST_MODE",
            "1"
        );
        Environment.SetEnvironmentVariable(
            "WORDTOOLKIT_INTERNAL_EQUATION_PREFLIGHT_HANG_INDEX",
            "1"
        );
        try
        {
            await using var host = new WordComHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                """
                {
                  "equations": [
                    {"value":"x+1","input_format":"latex"},
                    {"value":"y+1","input_format":"latex"}
                  ],
                  "per_equation_timeout_seconds": 5,
                  "total_timeout_seconds": 20
                }
                """
            );
            var started = Stopwatch.StartNew();
            var error = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "preflight_live_word_equations",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );
            started.Stop();
            Assert.Equal("EQUATION_PREFLIGHT_TIMEOUT", error.ErrorCode);
            Assert.InRange(started.Elapsed, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(18));
            using var details = JsonDocument.Parse(
                JsonSerializer.Serialize(error.Details, JsonDefaults.Compact)
            );
            Assert.Equal(1, details.RootElement.GetProperty("equation_index").GetInt32());
            Assert.Equal(1, details.RootElement.GetProperty("completed_count").GetInt32());
            Assert.True(
                details.RootElement.GetProperty("worker_process_terminated").GetBoolean()
            );
            Assert.True(
                details.RootElement
                    .GetProperty("dedicated_word_process_terminated")
                    .GetBoolean()
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "WORDTOOLKIT_INTERNAL_EQUATION_PREFLIGHT_TEST_MODE",
                oldMode
            );
            Environment.SetEnvironmentVariable(
                "WORDTOOLKIT_INTERNAL_EQUATION_PREFLIGHT_HANG_INDEX",
                oldIndex
            );
        }
        await AssertWordProcessSetReturnsToAsync(baseline);
    }

    [Fact]
    public async Task DedicatedWorkerReturnsValidInvalidValidInOneOrderedResponse()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_ISOLATED_PREFLIGHT_TEST"
            ) != "1"
        )
        {
            return;
        }
        var baseline = WordProcessIds();
        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "equations":[
                {"value":"x+1","input_format":"latex"},
                {"value":"\\unsupportedcommand{x}","input_format":"latex"},
                {"value":"y+1","input_format":"latex"}
              ],
              "per_equation_timeout_seconds":20,
              "total_timeout_seconds":60
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        var root = json.RootElement;
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.Equal(3, root.GetProperty("equation_count").GetInt32());
        Assert.Equal(2, root.GetProperty("valid_count").GetInt32());
        Assert.Equal(1, root.GetProperty("invalid_count").GetInt32());
        var items = root.GetProperty("equations");
        Assert.Equal(new[] { 0, 1, 2 }, items.EnumerateArray()
            .Select(item => item.GetProperty("index").GetInt32()).ToArray());
        Assert.True(items[0].GetProperty("valid").GetBoolean());
        Assert.False(items[1].GetProperty("valid").GetBoolean());
        Assert.True(items[2].GetProperty("valid").GetBoolean());
        Assert.StartsWith("weq_", items[1].GetProperty("equation_id").GetString());
        Assert.Equal(
            "USE_SUPPORTED_LATEX_OR_UNICODEMATH",
            items[1].GetProperty("suggestion_code").GetString()
        );
        await AssertWordProcessSetReturnsToAsync(baseline);
    }

    [Fact]
    public async Task MaxwellVectorAccentAndImplicitMultiplicationCorpusPassesNativeReadback()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_ISOLATED_PREFLIGHT_TEST"
            ) != "1"
        )
        {
            return;
        }
        var baseline = WordProcessIds();
        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {"equations":[
              {"value":"\\widehat{\\mathbf{k}}","input_format":"latex"},
              {"value":"\\hat{\\mathbf{k}}","input_format":"latex"},
              {"value":"\\mathbf{E}(\\mathbf{r},t)","input_format":"latex"},
              {"value":"\\mathbf{k}\\times\\mathbf{E}","input_format":"latex"},
              {"value":"\\mu_0\\varepsilon_0","input_format":"latex"},
              {"value":"\\mu_0\\varepsilon_0\\frac{\\partial^2\\mathbf{E}}{\\partial t^2}","input_format":"latex"},
              {"value":"\\nabla^2\\mathbf{B}-\\mu_0\\varepsilon_0\\frac{\\partial^2\\mathbf{B}}{\\partial t^2}=0","input_format":"latex"}
            ],"per_equation_timeout_seconds":20,"total_timeout_seconds":120}
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(7, json.RootElement.GetProperty("valid_count").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("invalid_count").GetInt32());
        Assert.All(
            json.RootElement.GetProperty("equations").EnumerateArray(),
            item =>
            {
                Assert.True(item.GetProperty("native_execution_verified").GetBoolean());
                Assert.StartsWith("weq_", item.GetProperty("equation_id").GetString());
            }
        );
        await AssertWordProcessSetReturnsToAsync(baseline);
    }

    private static HashSet<int> WordProcessIds() => Process.GetProcessesByName("WINWORD")
        .Select(process =>
        {
            using (process)
            {
                return process.Id;
            }
        })
        .ToHashSet();

    private static async Task AssertWordProcessSetReturnsToAsync(HashSet<int> expected)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (WordProcessIds().IsSubsetOf(expected))
            {
                return;
            }
            await Task.Delay(200);
        }
        Assert.Equal(
            Array.Empty<int>(),
            WordProcessIds().Except(expected).Order()
        );
    }
}
