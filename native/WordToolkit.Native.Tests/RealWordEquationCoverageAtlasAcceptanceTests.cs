using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordEquationCoverageAtlasAcceptanceTests
{
    private sealed record AtlasCase(string Family, string Value, string InputFormat);

    [Fact]
    public async Task BroadEquationAtlasBuildsUpReadsBackAndRendersInRealWord()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_EQUATION_ATLAS_TEST"
            ) != "1"
        )
        {
            return;
        }

        var baseline = WordProcessIds();
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-equation-atlas-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directory);
        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is null)
            {
                if (!launchIfMissing)
                {
                    throw new InvalidOperationException(
                        "Dedicated Microsoft Word was not created."
                    );
                }
                var type = Type.GetTypeFromProgID(
                        "Word.Application",
                        throwOnError: true
                    )
                    ?? throw new InvalidOperationException(
                        "Microsoft Word is unavailable."
                    );
                ownedApplication = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException(
                        "Could not create a dedicated Microsoft Word instance."
                    );
            }
            return ownedApplication;
        }

        ExceptionDispatchInfo? primaryFailure = null;
        Exception? cleanupFailure = null;
        await using (
            var host = new WordComHost(
                CreateApplication,
                shutdownTimeout: TimeSpan.FromSeconds(15)
            )
        )
        {
            var service = new WordLiveService(host);
            try
            {
                var corpus = Corpus();
                var equations = corpus
                    .Select(item => new
                    {
                        value = item.Value,
                        input_format = item.InputFormat,
                        display = true,
                        verify_readback = true,
                    })
                    .ToArray();

                using var preflight = await Call(
                    service,
                    "preflight_live_word_equations",
                    new
                    {
                        equations,
                        per_equation_timeout_seconds = 20,
                        total_timeout_seconds = 150,
                    }
                );
                var preflightRoot = preflight.RootElement;
                var items = preflightRoot.GetProperty("equations");
                var failures = items.EnumerateArray()
                    .Where(item => !item.GetProperty("valid").GetBoolean())
                    .Select(item =>
                    {
                        var index = item.GetProperty("index").GetInt32();
                        var errorCode = item.TryGetProperty("error_code", out var code)
                            ? code.GetString()
                            : "unknown";
                        var stage = item.TryGetProperty("stage", out var stageNode)
                            ? stageNode.GetString()
                            : "unknown";
                        var suggestion = item.TryGetProperty(
                            "suggestion_code",
                            out var suggestionNode
                        )
                            ? suggestionNode.GetString()
                            : "none";
                        var linear = item.TryGetProperty("word_linear", out var linearNode)
                            ? linearNode.GetString()
                            : "unavailable";
                        var diagnostic = item.TryGetProperty(
                            "diagnostic",
                            out var diagnosticNode
                        )
                            ? diagnosticNode.GetRawText()
                            : "unavailable";
                        var semantic = item.TryGetProperty(
                            "actual_semantic_sha256",
                            out var semanticNode
                        )
                            ? semanticNode.GetString()
                            : "unavailable";
                        var expectedSemantic = item.TryGetProperty(
                            "expected_semantic_sha256",
                            out var expectedSemanticNode
                        )
                            ? expectedSemanticNode.GetString()
                            : "unavailable";
                        var hresult = item.TryGetProperty("hresult", out var hresultNode)
                            ? hresultNode.GetRawText()
                            : "unavailable";
                        var exceptionType = item.TryGetProperty(
                            "native_exception_type",
                            out var exceptionTypeNode
                        )
                            ? exceptionTypeNode.GetString()
                            : "unavailable";
                        return $"{index}:{corpus[index].Family}:{errorCode}:{stage}:{suggestion}:linear={linear}:diagnostic={diagnostic}:expected_semantic={expectedSemantic}:actual_semantic={semantic}:hresult={hresult}:exception={exceptionType}";
                    })
                    .ToArray();
                Assert.True(
                    preflightRoot.GetProperty("valid").GetBoolean(),
                    string.Join(Environment.NewLine, failures)
                );
                Assert.Equal(
                    corpus.Count,
                    preflightRoot.GetProperty("equation_count").GetInt32()
                );
                Assert.Equal(
                    corpus.Count,
                    preflightRoot.GetProperty("valid_count").GetInt32()
                );
                Assert.Equal(
                    0,
                    preflightRoot.GetProperty("invalid_count").GetInt32()
                );
                Assert.Equal(corpus.Count, items.GetArrayLength());
                for (var index = 0; index < corpus.Count; index++)
                {
                    var family = corpus[index].Family;
                    var item = items[index];
                    Assert.Equal(index, item.GetProperty("index").GetInt32());
                    Assert.True(
                        item.GetProperty("valid").GetBoolean(),
                        $"Equation family '{family}' failed native preflight."
                    );
                    Assert.True(
                        item.GetProperty("native_execution_verified").GetBoolean(),
                        $"Equation family '{family}' did not build as native OfficeMath."
                    );
                    Assert.True(
                        item.GetProperty("native_readback_verified").GetBoolean(),
                        $"Equation family '{family}' did not pass exact native readback."
                    );
                    Assert.StartsWith(
                        "weq_",
                        item.GetProperty("equation_id").GetString()
                    );
                }

                var outputPath = Path.Combine(directory, "atlas.docx");
                var pdfInfoPath = Environment.GetEnvironmentVariable(
                    "WORDTOOLKIT_TEST_PDFINFO_PATH"
                );
                var rasterizerPath = Environment.GetEnvironmentVariable(
                    "WORDTOOLKIT_TEST_PDF_RASTERIZER_PATH"
                );
                var request = new Dictionary<string, object?>
                {
                    ["output_path"] = outputPath,
                    ["equations"] = equations,
                    ["idempotency_key"] = "equation-coverage-atlas",
                    ["visible"] = false,
                    ["keep_open"] = false,
                    ["render_output_directory"] = directory,
                    ["artifact_stem"] = "atlas",
                    ["render_output"] = string.IsNullOrWhiteSpace(pdfInfoPath)
                        || string.IsNullOrWhiteSpace(rasterizerPath)
                            ? "pdf"
                            : "pdf_and_png_pages",
                    ["per_equation_timeout_seconds"] = 20,
                    ["total_timeout_seconds"] = 150,
                };
                if (
                    !string.IsNullOrWhiteSpace(pdfInfoPath)
                    && !string.IsNullOrWhiteSpace(rasterizerPath)
                )
                {
                    request["pdfinfo_path"] = pdfInfoPath;
                    request["rasterizer_path"] = rasterizerPath;
                    request["rasterizer_kind"] = "pdf_to_ppm";
                }

                using var created = await Call(
                    service,
                    "create_live_word_equation_document",
                    request
                );
                var root = created.RootElement;
                Assert.True(root.GetProperty("workflow_complete").GetBoolean());
                Assert.True(root.GetProperty("published").GetBoolean());
                Assert.True(root.GetProperty("saved").GetBoolean());
                Assert.True(root.GetProperty("rendered").GetBoolean());
                Assert.True(root.GetProperty("package_inspected").GetBoolean());
                Assert.False(root.GetProperty("live_document_open").GetBoolean());
                Assert.Equal(
                    corpus.Count,
                    root.GetProperty("equation_count").GetInt32()
                );
                Assert.True(
                    root.GetProperty("validation").GetProperty("valid").GetBoolean()
                );
                var qa = root.GetProperty("render")
                    .GetProperty("equation_render_qa");
                Assert.True(qa.GetProperty("source_check_performed").GetBoolean());
                Assert.Equal(
                    0,
                    qa.GetProperty("raw_control_syntax_count").GetInt32()
                );
                if (request["render_output"] as string == "pdf_and_png_pages")
                {
                    Assert.True(qa.GetProperty("raster_check_performed").GetBoolean());
                    Assert.NotEmpty(
                        Directory.EnumerateFiles(directory, "atlas-page-*.png")
                    );
                }
                Assert.True(File.Exists(outputPath));
                Assert.True(File.Exists(Path.Combine(directory, "atlas.pdf")));
                Assert.True(
                    File.Exists(Path.Combine(directory, "atlas.render.json"))
                );
                Assert.Equal(
                    0,
                    await host.InvokeAsync(
                        application => (int)application.Documents.Count,
                        launchIfMissing: false
                    )
                );
            }
            catch (Exception exception)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                try
                {
                    if (ownedApplication is not null)
                    {
                        await host.InvokeAsync(
                            application =>
                            {
                                while ((int)application.Documents.Count > 0)
                                {
                                    application.Documents.Item(1).Close(0);
                                }
                                application.Quit(0);
                                return true;
                            },
                            launchIfMissing: false
                        );
                    }
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
            }
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception)
        {
            cleanupFailure ??= exception;
        }
        primaryFailure?.Throw();
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
        await AssertWordProcessSetReturnsToAsync(baseline);
    }

    private static IReadOnlyList<AtlasCase> Corpus() =>
    [
        new("latex.arithmetic_relations", @"a+b=c,\quad x<y\le z", "latex"),
        new("latex.scripts", @"x^2+y_1+z_i^j", "latex"),
        new("latex.fraction_square_root", @"\frac{a+b}{\sqrt{x}}", "latex"),
        new("latex.indexed_radicals", @"\sqrt[3]{x}+\qdrt{y}", "latex"),
        new("latex.delimiters", @"\left\langle\frac{x}{y}\right\rangle", "latex"),
        new("latex.set_logic", @"A\subseteq B\land x\in A", "latex"),
        new("latex.arrows", @"A\implies B\Leftrightarrow C", "latex"),
        new("latex.functions", @"\sin x+\log y+\exp z", "latex"),
        new("latex.limit", @"\lim_{x\to0}\frac{\sin x}{x}", "latex"),
        new("latex.sum_product", @"\sum_{i=1}^n i+\prod_{j=1}^m j", "latex"),
        new("latex.integral", @"\int_0^1 t^2\,\mathrm{d}t", "latex"),
        new("latex.multiple_integral", @"\iint_D f(x,y)\,\mathrm{d}x\,\mathrm{d}y", "latex"),
        new("latex.total_derivative", @"\dv{f}{x}", "latex"),
        new("latex.partial_derivative", @"\pdv{u}{t}", "latex"),
        new("latex.accents", @"\hat{x}+\bar{y}+\vec{z}+\ddot{q}", "latex"),
        new("latex.math_alphabets", @"\mathbf{A}+\mathbb{R}+\mathcal{F}+\mathfrak{g}", "latex"),
        new("latex.matrix", @"\begin{pmatrix}a&b\\c&d\end{pmatrix}", "latex"),
        new("latex.determinant", @"\begin{vmatrix}a&b\\c&d\end{vmatrix}", "latex"),
        new("latex.cases", @"f(x)=\begin{cases}x^2&x>0\\0&x\le0\end{cases}", "latex"),
        new("latex.aligned", @"\begin{aligned}x&=a+b\\y&=c+d\end{aligned}", "latex"),
        new("latex.smallmatrix", @"\begin{smallmatrix}1&0\\0&1\end{smallmatrix}", "latex"),
        new("latex.binomial", @"\dbinom{n}{k}", "latex"),
        new("latex.boxed", @"\boxed{x^2+y^2=1}", "latex"),
        new("latex.dirac", @"i\hbar\gamma^\mu\partial_\mu\psi=mc\psi", "latex"),
        new("latex.braket", @"\matrixel{\phi}{H}{\psi}", "latex"),
        new("latex.tensor_products", @"A\otimes B\oplus C", "latex"),
        new("latex.text_units", @"v=3{,}0\,\mathrm{m/s}", "latex"),
        new("latex.fourfold_integral", @"\iiiint_V f\,\mathrm{d}x\,\mathrm{d}y\,\mathrm{d}z\,\mathrm{d}w", "latex"),
        new("latex.root_of", @"\root 5\of{x}", "latex"),
        new("latex.over_under_braces", @"\overbrace{a+b}^{n}+\underbrace{c+d}_{m}", "latex"),
        new("latex.over_under_scripts", @"\overset{!}{=}+\underset{i}{x}", "latex"),
        new("latex.substack", @"\sum_{\substack{i=1\\j=2}}^n a_{ij}", "latex"),
        new("latex.split", @"\begin{split}a&=b\\c&=d\end{split}", "latex"),
        new("latex.multline", @"\begin{multline}a+b\\c+d\end{multline}", "latex"),
        new("latex.middle_delimiter", @"\left\langle a\middle|b\right\rangle", "latex"),
        new("latex.extended_accents", @"\acute{x}+\grave{y}+\dddot{z}+\underbar{q}", "latex"),
        new("latex.phantom", @"\phantom{x}", "latex"),
        new("latex.hphantom", @"\hphantom{x}", "latex"),
        new("latex.vphantom", @"\vphantom{x}", "latex"),
        new("latex.smash", @"\smash{x}", "latex"),
        new("latex.hsmash", @"\hsmash{x}", "latex"),
        new("latex.asmash", @"\asmash{x}", "latex"),
        new("latex.dsmash", @"\dsmash{x}", "latex"),
        new("latex.extended_relations", @"a\preceq b\nRightarrow c\nsim d", "latex"),
        new("latex.harpoons", @"A\leftrightharpoons B\rightleftharpoons C", "latex"),
        new("unicodemath.basic", "a+b\u2260c", "unicodemath"),
        new("unicodemath.scripts", "x\u00B2+y\u2081", "unicodemath"),
        new("unicodemath.matrix", "■(a&b@c&d)", "unicodemath"),
        new("unicodemath.equation_array", "█(x&=1@y&=2)", "unicodemath"),
        new("unicodemath.prescript", "_(a)^(b)x", "unicodemath"),
        new("unicodemath.overbrace", "\u23DE(a+b)", "unicodemath"),
        new(
            "mathml.fraction",
            "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mfrac><mi>a</mi><mi>b</mi></mfrac></math>",
            "mathml"
        ),
        new(
            "mathml.matrix",
            "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mtable><mtr><mtd><mi>a</mi></mtd><mtd><mi>b</mi></mtd></mtr><mtr><mtd><mi>c</mi></mtd><mtd><mi>d</mi></mtd></mtr></mtable></math>",
            "mathml"
        ),
        new(
            "mathml.multiscripts",
            "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mmultiscripts><mi>T</mi><mi>i</mi><mi>j</mi><mprescripts/><mi>k</mi><none/></mmultiscripts></math>",
            "mathml"
        ),
        new(
            "mathml.maction",
            "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><maction selection=\"2\"><mi>x</mi><mfrac><mi>a</mi><mi>b</mi></mfrac></maction></math>",
            "mathml"
        ),
        new(
            "omml.fraction",
            "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath>",
            "omml"
        ),
        new(
            "omml.radical",
            "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:rad><m:deg><m:r><m:t>3</m:t></m:r></m:deg><m:e><m:r><m:t>x</m:t></m:r></m:e></m:rad></m:oMath>",
            "omml"
        ),
        new(
            "omml.prescript",
            "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:sPre><m:sub><m:r><m:t>a</m:t></m:r></m:sub><m:sup><m:r><m:t>b</m:t></m:r></m:sup><m:e><m:r><m:t>x</m:t></m:r></m:e></m:sPre></m:oMath>",
            "omml"
        ),
        new(
            "omml.group_character",
            "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:groupChr><m:groupChrPr><m:chr m:val=\"\u23DE\"/><m:pos m:val=\"top\"/></m:groupChrPr><m:e><m:r><m:t>a+b</m:t></m:r></m:e></m:groupChr></m:oMath>",
            "omml"
        ),
        new(
            "omml.direct_complex",
            "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:sPre><m:sub><m:r><m:t>a</m:t></m:r></m:sub><m:sup><m:r><m:t>b</m:t></m:r></m:sup><m:e><m:groupChr><m:groupChrPr><m:chr m:val=\"⏞\"/><m:pos m:val=\"top\"/></m:groupChrPr><m:e><m:phant><m:e><m:r><m:t>x+y</m:t></m:r></m:e></m:phant></m:e></m:groupChr></m:e></m:sPre></m:oMath>",
            "omml"
        ),
    ];

    private static HashSet<int> WordProcessIds() =>
        Process.GetProcessesByName("WINWORD")
            .Select(process =>
            {
                using (process)
                {
                    return process.Id;
                }
            })
            .ToHashSet();

    private static async Task AssertWordProcessSetReturnsToAsync(
        HashSet<int> expected
    )
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (WordProcessIds().IsSubsetOf(expected))
            {
                return;
            }
            await Task.Delay(250);
        }
        Assert.Equal(
            Array.Empty<int>(),
            WordProcessIds().Except(expected).Order()
        );
    }

    private static async Task<JsonDocument> Call(
        WordLiveService service,
        string action,
        object arguments
    ) =>
        JsonDocument.Parse(
            JsonSerializer.Serialize(
                await service.CallAsync(
                    action,
                    JsonSerializer.SerializeToElement(arguments),
                    CancellationToken.None
                ),
                JsonDefaults.Compact
            )
        );
}
