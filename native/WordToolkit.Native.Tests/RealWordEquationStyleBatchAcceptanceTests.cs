using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordEquationStyleBatchAcceptanceTests
{
    [Fact]
    public async Task DedicatedWordBatchHandlesFortyNineOperationsAndPreservesFormattedEquationStyles()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_EQUATION_STYLE_BATCH_TEST"),
                "1", StringComparison.Ordinal))
        {
            return;
        }

        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is null)
            {
                if (!launchIfMissing)
                    throw new InvalidOperationException("Dedicated Word application was not created.");
                var wordType = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
                    ?? throw new InvalidOperationException("Microsoft Word ProgID is unavailable.");
                ownedApplication = Activator.CreateInstance(wordType)
                    ?? throw new InvalidOperationException("Could not create dedicated Word application.");
            }
            return ownedApplication;
        }

        // Create the dedicated instance outside the host; every COM request below must
        // use launchIfMissing:false and therefore can never attach/launch another Word.
        ownedApplication = CreateApplication(true);
        await using var host = new WordComHost(CreateApplication, shutdownTimeout: TimeSpan.FromSeconds(15));
        var service = new WordLiveService(host);
        string? documentId = null;
        string? documentName = null;
        ExceptionDispatchInfo? primary = null;
        Exception? cleanupFailure = null;
        try
        {
            documentName = await host.InvokeAsync(
                application => (string)application.Documents.Add(Visible: false).Name,
                launchIfMissing: false);
            using var connected = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync("connect_live_word_document",
                    JsonDocument.Parse(JsonSerializer.Serialize(new { document_name = documentName, activate = false })).RootElement,
                    CancellationToken.None), JsonDefaults.Compact));
            documentId = connected.RootElement.GetProperty("live_document_id").GetString();
            var version = connected.RootElement.GetProperty("live_version").GetInt64();
            var equationValues = new[]
            {
                @"\begin{matrix}1&2\\3&4\end{matrix}",
                @"\begin{pmatrix}a&b\\c&d\end{pmatrix}",
                @"\begin{bmatrix}3{,}14&2\\1&0{,}5\end{bmatrix}",
                @"\begin{vmatrix}p&q\\r&s\end{vmatrix}",
                @"\begin{Vmatrix}u&v\\w&z\end{Vmatrix}",
                @"\begin{matrix}\frac{1}{2}&\sqrt{x}\\y^2&z_1\end{matrix}",
                @"\mathbf{x+\boldsymbol{y}}",
                @"\boldsymbol{\frac{\alpha+\beta}{\gamma}}",
            };
            var operations = new List<object>(49);
            var equationOffset = 0;
            for (var operationIndex = 1; operationIndex <= 49; operationIndex++)
            {
                if (operationIndex % 6 == 0)
                {
                    operations.Add(new
                    {
                        type = "equation",
                        value = equationValues[equationOffset++],
                        input_format = "latex",
                        display = true,
                        verify_readback = true,
                    });
                }
                else
                {
                    operations.Add(new
                    {
                        type = "text",
                        text = $"Batch paragraph {operationIndex}",
                        as_new_paragraph = true,
                    });
                }
            }
            Assert.Equal(8, equationOffset);
            var applyStopwatch = Stopwatch.StartNew();
            using var applied = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync("apply_live_word_operations",
                    JsonDocument.Parse(JsonSerializer.Serialize(new
                    {
                        live_document_id = documentId,
                        expected_version = version,
                        operations,
                    })).RootElement, CancellationToken.None), JsonDefaults.Compact));
            applyStopwatch.Stop();

            var root = applied.RootElement;
            Assert.Equal(49, root.GetProperty("operation_count").GetInt32());
            Assert.Equal(8, root.GetProperty("equation_operation_count").GetInt32());
            Assert.Equal(8, root.GetProperty("document").GetProperty("equation_count").GetInt32());
            var equations = root.GetProperty("operations").EnumerateArray()
                .Where(item => item.GetProperty("type").GetString() == "equation")
                .Select(item => item.GetProperty("equation"))
                .ToArray();
            Assert.Equal(8, equations.Length);
            Assert.All(equations, e =>
            {
                Assert.True(e.GetProperty("native_verified").GetBoolean());
                Assert.True(e.GetProperty("readback_verified").GetBoolean());
            });
            Assert.All(equations.Take(6), equation =>
                Assert.False(equation.GetProperty("native_style_verified").GetBoolean()));
            Assert.All(equations.Skip(6), equation =>
                Assert.True(equation.GetProperty("native_style_verified").GetBoolean()));
            foreach (var equation in equations.Skip(6))
            {
                var formatting = equation.GetProperty("formatting");
                Assert.True(formatting.GetProperty("region_count").GetInt32() > 0);
                Assert.True(formatting.GetProperty("styled_run_count").GetInt32() > 0);
                Assert.Equal(
                    formatting.GetProperty("expected_contract_sha256").GetString(),
                    formatting.GetProperty("actual_contract_sha256").GetString());
            }
            var complexity = root.GetProperty("performance").GetProperty("complexity");
            Assert.Equal(49, complexity.GetProperty("operation_count").GetInt32());
            Assert.Equal(8, complexity.GetProperty("equation_count").GetInt32());
            Assert.Equal(2, complexity.GetProperty("styled_equation_count").GetInt32());
            Assert.Equal(
                61,
                complexity.GetProperty("estimated_staging_content_com_calls").GetInt32()
            );
            Assert.Equal(
                2,
                complexity.GetProperty("batch_boundary_equation_count_reads").GetInt32()
            );
            Assert.True(
                applyStopwatch.Elapsed < TimeSpan.FromSeconds(120),
                $"The 49-operation batch exceeded the 120-second acceptance ceiling: {applyStopwatch.Elapsed}."
            );
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            try
            {
                if (documentName is not null)
                    await host.InvokeAsync(application => { foreach (dynamic d in application.Documents) if ((string)d.Name == documentName) { d.Close(0); break; } return true; }, launchIfMissing: false);
            }
            catch (Exception exception) { cleanupFailure = exception; }
            try
            {
                await host.InvokeAsync(application => { application.Quit(0); return true; }, launchIfMissing: false);
            }
            catch (Exception exception) { cleanupFailure ??= exception; }
            if (primary is not null) primary.Throw();
            if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }
}
