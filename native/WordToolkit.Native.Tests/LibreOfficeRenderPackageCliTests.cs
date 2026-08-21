using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class LibreOfficeRenderPackageCliTests
{
    [Fact]
    public async Task DispatchesBoundedJsonToTheLibreOfficeRenderOperation()
    {
        using var input = new StringReader("{\"local_path\":\"C:/source.docx\"}");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var invoked = false;

        var exit = await LibreOfficeRenderPackageCli.RunAsync(
            ["--request", "-", "--format", "json"],
            input,
            output,
            error,
            (arguments, _) =>
            {
                invoked = true;
                Assert.Equal(
                    "C:/source.docx",
                    arguments.GetProperty("local_path").GetString()
                );
                return Task.FromResult<object>(
                    new
                    {
                        operation_contract =
                            "wordtoolkit.render_ooxml_libreoffice_artifacts/1.0",
                    }
                );
            }
        );

        Assert.Equal(0, exit);
        Assert.True(invoked);
        Assert.Equal(string.Empty, error.ToString());
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            "wordtoolkit.render_ooxml_libreoffice_artifacts/1.0",
            result.RootElement.GetProperty("operation_contract").GetString()
        );
    }

    [Fact]
    public async Task HelpDoesNotCreateAnExecutor()
    {
        var invoked = false;
        using var output = new StringWriter();

        var exit = await LibreOfficeRenderPackageCli.RunAsync(
            ["--help"],
            TextReader.Null,
            output,
            TextWriter.Null,
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<object>(new { });
            }
        );

        Assert.Equal(0, exit);
        Assert.False(invoked);
        Assert.Contains(
            "libreoffice-render-package",
            output.ToString(),
            StringComparison.Ordinal
        );
    }
}
