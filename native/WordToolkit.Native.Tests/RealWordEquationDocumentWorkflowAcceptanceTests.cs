using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordEquationDocumentWorkflowAcceptanceTests
{
    [Fact]
    public async Task InvalidPreflightCreatesNoDocumentOrArtifact()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_EQUATION_DOCUMENT_TEST"
            ) != "1"
        )
        {
            return;
        }
        var baseline = Process.GetProcessesByName("WINWORD").Select(process =>
        {
            using (process) return process.Id;
        }).ToHashSet();
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-equation-document-invalid-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directory);
        try
        {
            await using var host = new WordComHost();
            var service = new WordLiveService(host);
            var outputPath = Path.Combine(directory, "invalid.docx");
            using var result = await Call(
                service,
                "create_live_word_equation_document",
                new
                {
                    output_path = outputPath,
                    equations = new[]
                    {
                        new { value = "x+1", input_format = "latex" },
                        new { value = "\\unsupportedcommand{x}", input_format = "latex" },
                    },
                }
            );
            Assert.False(result.RootElement.GetProperty("workflow_complete").GetBoolean());
            Assert.False(result.RootElement.GetProperty("created").GetBoolean());
            Assert.Equal(
                1,
                result.RootElement.GetProperty("preflight")
                    .GetProperty("invalid_count").GetInt32()
            );
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var after = Process.GetProcessesByName("WINWORD").Select(process =>
            {
                using (process) return process.Id;
            }).ToHashSet();
            if (after.IsSubsetOf(baseline)) return;
            await Task.Delay(200);
        }
        Assert.DoesNotContain(
            Process.GetProcessesByName("WINWORD").Select(process =>
            {
                using (process) return process.Id;
            }),
            id => !baseline.Contains(id)
        );
    }

    [Fact]
    public async Task CreatesPreflightsPublishesSavesRendersValidatesAndInspects()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_EQUATION_DOCUMENT_TEST"
            ) != "1"
        )
        {
            return;
        }
        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is null)
            {
                if (!launchIfMissing)
                {
                    throw new InvalidOperationException("Dedicated Word was not created.");
                }
                var type = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
                    ?? throw new InvalidOperationException("Microsoft Word is unavailable.");
                ownedApplication = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException("Could not create Word.");
            }
            return ownedApplication;
        }

        await using var host = new WordComHost(
            CreateApplication,
            shutdownTimeout: TimeSpan.FromSeconds(15)
        );
        var service = new WordLiveService(host);
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-equation-document-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directory);
        ExceptionDispatchInfo? primaryFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            var outputPath = Path.Combine(directory, "maxwell.docx");
            var pdfInfoPath = Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_TEST_PDFINFO_PATH"
            );
            var rasterizerPath = Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_TEST_PDF_RASTERIZER_PATH"
            );
            var request = new Dictionary<string, object?>
            {
                ["output_path"] = outputPath,
                ["equations"] = new[]
                {
                    new { value = "\\nabla\\cdot\\mathbf{E}=0", input_format = "latex" },
                    new { value = "\\nabla\\times\\mathbf{E}=-\\frac{\\partial\\mathbf{B}}{\\partial t}", input_format = "latex" },
                    new { value = "c=\\frac{1}{\\sqrt{\\mu_0\\varepsilon_0}}", input_format = "latex" },
                },
                ["idempotency_key"] = "equation-document-acceptance",
                ["visible"] = false,
                ["keep_open"] = false,
                ["render_output_directory"] = directory,
                ["artifact_stem"] = "maxwell",
                ["render_output"] = string.IsNullOrWhiteSpace(pdfInfoPath)
                    || string.IsNullOrWhiteSpace(rasterizerPath)
                        ? "pdf"
                        : "pdf_and_png_pages",
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
            using var result = await Call(
                service,
                "create_live_word_equation_document",
                request
            );
            var root = result.RootElement;
            Assert.True(root.GetProperty("workflow_complete").GetBoolean());
            Assert.True(root.GetProperty("published").GetBoolean());
            Assert.True(root.GetProperty("saved").GetBoolean());
            Assert.True(root.GetProperty("rendered").GetBoolean());
            Assert.True(root.GetProperty("package_inspected").GetBoolean());
            Assert.False(root.GetProperty("live_document_open").GetBoolean());
            Assert.Equal(3, root.GetProperty("equation_count").GetInt32());
            Assert.True(
                root.GetProperty("validation").GetProperty("valid").GetBoolean()
            );
            Assert.StartsWith("wlop_", root.GetProperty("operation_id").GetString());
            Assert.True(File.Exists(outputPath));
            Assert.True(File.Exists(Path.Combine(directory, "maxwell.pdf")));
            Assert.True(File.Exists(Path.Combine(directory, "maxwell.render.json")));
            var qa = root.GetProperty("render").GetProperty("equation_render_qa");
            Assert.True(qa.GetProperty("source_check_performed").GetBoolean());
            Assert.Equal(0, qa.GetProperty("raw_control_syntax_count").GetInt32());
            if (request["render_output"] as string == "pdf_and_png_pages")
            {
                Assert.True(qa.GetProperty("raster_check_performed").GetBoolean());
                Assert.True(File.Exists(Path.Combine(directory, "maxwell-page-0001.png")));
            }
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
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }
        }
        primaryFailure?.Throw();
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private static async Task<JsonDocument> Call(
        WordLiveService service,
        string action,
        object arguments
    ) => JsonDocument.Parse(
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
