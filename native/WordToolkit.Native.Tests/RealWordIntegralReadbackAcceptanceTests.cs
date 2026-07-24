using System.Text.Json;
using System.Xml.Linq;
using WordToolkit.Native.Equations;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordIntegralReadbackAcceptanceTests
{
    private const string FullIntegralDerivation =
        @"\begin{aligned}
I&=\int x^3e^{2x}\sin(3x)\,dx\\
&=\operatorname{Im}\int x^3e^{(2+3i)x}\,dx,\quad \lambda=2+3i\\
J_3&=\int x^3e^{\lambda x}\,dx=\frac{x^3e^{\lambda x}}{\lambda}-\frac{3}{\lambda}J_2\\
J_2&=\int x^2e^{\lambda x}\,dx=\frac{x^2e^{\lambda x}}{\lambda}-\frac{2}{\lambda}J_1\\
J_1&=\int xe^{\lambda x}\,dx=\frac{xe^{\lambda x}}{\lambda}-\frac{1}{\lambda}J_0\\
J_0&=\int e^{\lambda x}\,dx=\frac{e^{\lambda x}}{\lambda}\\
J_3&=e^{\lambda x}\left(\frac{x^3}{\lambda}-\frac{3x^2}{\lambda^2}+\frac{6x}{\lambda^3}-\frac{6}{\lambda^4}\right)\\
I&=\operatorname{Im}\left[e^{(2+3i)x}\left(\frac{x^3}{2+3i}-\frac{3x^2}{(2+3i)^2}+\frac{6x}{(2+3i)^3}-\frac{6}{(2+3i)^4}\right)\right]+C
\end{aligned}";

    [Fact]
    public async Task FullComplexIntegralDerivationSurvivesNativeWordBuildUpAndReadback()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_EQUATION_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-integral-readback-{Guid.NewGuid():N}.docx"
        );
        string? documentId = null;
        var version = 0;
        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        try
        {
            await Call(service, "start_word_application", new { visible = true });
            var plan = LatexToUnicodeMath.ConvertPlan(FullIntegralDerivation);
            var canonical = await host.InvokeAsync(
                application =>
                {
                    dynamic diagnosticDocument = application.Documents.Add();
                    try
                    {
                        dynamic range = diagnosticDocument.Range(0, 0);
                        range.Text = plan.BuildLinear;
                        dynamic added = diagnosticDocument.OMaths.Add(range);
                        dynamic equation = added.OMaths.Item(1);
                        equation.BuildUp();
                        var xml = (string?)equation.Range.WordOpenXML ?? "";
                        XNamespace math =
                            "http://schemas.openxmlformats.org/officeDocument/2006/math";
                        var root = XDocument.Parse(xml).Descendants(math + "oMath").Single();
                        var actual = MathMarkupToUnicodeMath.Convert(
                            root.ToString(SaveOptions.DisableFormatting),
                            "omml"
                        );
                        return (
                            Expected: EquationReadbackVerifier.CanonicalizeForTesting(plan.Linear),
                            Actual: EquationReadbackVerifier.CanonicalizeForTesting(actual)
                        );
                    }
                    finally
                    {
                        diagnosticDocument.Close(0);
                    }
                },
                launchIfMissing: true
            );
            Assert.Equal(canonical.Expected, canonical.Actual);
            using (var created = await Call(
                service,
                "create_live_word_document",
                new { output_path = path, activate = true }
            ))
            {
                documentId = created.RootElement.GetProperty("live_document_id").GetString();
                version = created.RootElement.GetProperty("live_version").GetInt32();
            }

            JsonDocument applied;
            try
            {
                applied = await Call(
                    service,
                    "apply_live_word_operations",
                    new
                    {
                        live_document_id = documentId,
                        expected_version = version,
                        operations = new[]
                        {
                            new
                            {
                                type = "equation",
                                value = FullIntegralDerivation,
                                input_format = "latex",
                                display = true,
                                verify_readback = true,
                            },
                        },
                    }
                );
            }
            catch (WordToolkit.Native.Protocol.NativeToolException exception)
            {
                throw new InvalidOperationException(
                    exception.Message + ": " + JsonSerializer.Serialize(exception.Details),
                    exception
                );
            }
            using (applied)
            {
                var operation = applied.RootElement.GetProperty("operations")[0]
                    .GetProperty("equation");
                Assert.True(operation.GetProperty("native_verified").GetBoolean());
                Assert.True(operation.GetProperty("readback_verified").GetBoolean());
                var readback = operation.GetProperty("readback");
                Assert.Equal(6, readback.GetProperty("nary_count").GetInt32());
                Assert.Equal(6, readback.GetProperty("differential_count").GetInt32());
                Assert.True(
                    readback.GetProperty("differential_placement_verified").GetBoolean()
                );
                Assert.Equal(
                    readback.GetProperty("expected_contract_sha256").GetString(),
                    readback.GetProperty("actual_contract_sha256").GetString()
                );
                version = applied.RootElement.GetProperty("live_version").GetInt32();
            }
        }
        finally
        {
            if (documentId is not null)
            {
                try
                {
                    using var _ = await Call(
                        service,
                        "close_live_word_document",
                        new
                        {
                            live_document_id = documentId,
                            expected_version = version,
                            save_changes = "discard",
                        }
                    );
                }
                catch
                {
                    // Preserve the acceptance failure; the COM host is still disposed.
                }
            }
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task<JsonDocument> Call(
        WordLiveService service,
        string action,
        object arguments
    )
    {
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var result = await service.CallAsync(
            action,
            input.RootElement,
            CancellationToken.None
        );
        return JsonDocument.Parse(JsonSerializer.Serialize(result));
    }
}
