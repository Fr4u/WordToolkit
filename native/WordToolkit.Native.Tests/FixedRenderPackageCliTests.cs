using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class FixedRenderPackageCliTests
{
    [Fact]
    public async Task RunsTypedFixedRenderRequestThroughCli()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-fixed-render-cli-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.docx");
            var outputDirectory = Path.Combine(directory, "output");
            Directory.CreateDirectory(outputDirectory);
            WordFixedRenderServiceTests.CreatePackage(source);
            var fingerprint = new OpcPackageReader().Read(source).Fingerprint;
            var request = JsonSerializer.Serialize(
                new
                {
                    local_path = source,
                    expected_package_fingerprint = fingerprint,
                    output_directory = outputDirectory,
                    artifact_stem = "cli_proof",
                    output = "pdf",
                }
            );
            using var input = new StringReader(request);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = await FixedRenderPackageCli.RunAsync(
                ["--request", "-", "--format", "json"],
                input,
                output,
                error,
                hostFactory: () => new FixedFormatFakeHost(pageCount: 2)
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                "wordtoolkit.render_ooxml_fixed_artifacts/1.0",
                result.RootElement.GetProperty("operation_contract").GetString()
            );
            Assert.Equal(2, result.RootElement.GetProperty("artifact_count").GetInt32());
            Assert.True(File.Exists(Path.Combine(outputDirectory, "cli_proof.pdf")));
            Assert.True(
                File.Exists(Path.Combine(outputDirectory, "cli_proof.render.json"))
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HelpDoesNotStartWord()
    {
        var factoryCalled = false;
        using var output = new StringWriter();
        var exit = await FixedRenderPackageCli.RunAsync(
            ["--help"],
            TextReader.Null,
            output,
            TextWriter.Null,
            hostFactory: () =>
            {
                factoryCalled = true;
                return new FixedFormatFakeHost(pageCount: 1);
            }
        );

        Assert.Equal(0, exit);
        Assert.False(factoryCalled);
        Assert.Contains("fixed-render-package", output.ToString(), StringComparison.Ordinal);
    }
}
