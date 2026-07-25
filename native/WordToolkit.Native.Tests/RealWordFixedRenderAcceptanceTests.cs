using System.Security.Cryptography;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordFixedRenderAcceptanceTests
{
    [Fact]
    public async Task WordPdfAndPopplerPageRemainSourceBoundAndGeometricallyConsistent()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_FIXED_RENDER_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        var pdfInfoPath = RequiredExecutable("WORDTOOLKIT_PDFINFO_PATH");
        var rasterizerPath = RequiredExecutable("WORDTOOLKIT_PDF_RASTERIZER_PATH");
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-real-fixed-render-{Guid.NewGuid():N}"
        );
        var outputDirectory = Path.Combine(directory, "output");
        Directory.CreateDirectory(outputDirectory);
        var source = Path.Combine(directory, "source.docx");
        WordFixedRenderServiceTests.CreatePackage(source);
        var sourceBefore = SHA256.HashData(File.ReadAllBytes(source));
        try
        {
            var fingerprint = new OpcPackageReader().Read(source).Fingerprint;
            await using var host = new WordComHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        local_path = source,
                        expected_package_fingerprint = fingerprint,
                        output_directory = outputDirectory,
                        artifact_stem = "word_oracle",
                        output = "pdf_and_png_pages",
                        first_page = 1,
                        last_page = 1,
                        dpi = 96,
                        pdfinfo_path = pdfInfoPath,
                        rasterizer_path = rasterizerPath,
                        rasterizer_kind = "pdf_to_ppm",
                    }
                )
            );

            var raw = await service.CallAsync(
                "render_ooxml_fixed_artifacts",
                arguments.RootElement,
                CancellationToken.None
            );
            using var resultDocument = JsonDocument.Parse(
                JsonSerializer.Serialize(raw, JsonDefaults.Compact)
            );
            var result = resultDocument.RootElement;

            Assert.Equal(fingerprint, result.GetProperty("package_fingerprint").GetString());
            Assert.False(result.GetProperty("source_mutated").GetBoolean());
            Assert.Equal(1, result.GetProperty("exported_page_count").GetInt32());
            Assert.Equal(1, result.GetProperty("page_geometry_count").GetInt32());
            Assert.True(
                result.GetProperty("backend")
                    .GetProperty("pdf_geometry_inspected")
                    .GetBoolean()
            );
            Assert.True(
                result.GetProperty("execution")
                    .GetProperty("all_resolved")
                    .GetBoolean()
            );
            Assert.False(
                result.GetProperty("execution")
                    .GetProperty("silent_fallback")
                    .GetBoolean()
            );

            var geometry = result.GetProperty("page_geometries")[0];
            var png = result.GetProperty("artifacts")
                .EnumerateArray()
                .Single(item => item.GetProperty("format").GetString() == "png");
            var expectedWidth = geometry.GetProperty("width_points").GetDouble() * 96 / 72;
            var expectedHeight = geometry.GetProperty("height_points").GetDouble() * 96 / 72;
            Assert.InRange(
                Math.Abs(png.GetProperty("pixel_width").GetInt32() - expectedWidth),
                0,
                1.01
            );
            Assert.InRange(
                Math.Abs(png.GetProperty("pixel_height").GetInt32() - expectedHeight),
                0,
                1.01
            );

            Assert.Equal(sourceBefore, SHA256.HashData(File.ReadAllBytes(source)));
            Assert.True(
                File.ReadAllBytes(Path.Combine(outputDirectory, "word_oracle.pdf"))
                    .AsSpan()
                    .StartsWith("%PDF-"u8)
            );
            Assert.True(
                File.ReadAllBytes(
                        Path.Combine(outputDirectory, "word_oracle-page-0001.png")
                    )
                    .AsSpan()
                    .StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            );
            Assert.True(File.Exists(Path.Combine(outputDirectory, "word_oracle.render.json")));
            Assert.Empty(
                Directory.EnumerateDirectories(
                    outputDirectory,
                    ".wordtoolkit-fixed-render-*"
                )
            );

            Console.WriteLine(
                $"Qualified fixed rendering against Word {result.GetProperty("backend").GetProperty("word_version").GetString()} "
                    + $"build {result.GetProperty("backend").GetProperty("word_build").GetString()}."
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string RequiredExecutable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        Assert.False(
            string.IsNullOrWhiteSpace(value),
            $"{variableName} must name an explicit Poppler executable when the real-Word test is enabled."
        );
        var path = Path.GetFullPath(value!);
        Assert.True(File.Exists(path), $"Configured executable does not exist: {path}");
        return path;
    }
}
