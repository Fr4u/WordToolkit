using WordToolkit.Native.Tex;

namespace WordToolkit.Native.Tests;

public sealed class TectonicCompilerTests
{
    [Fact]
    public async Task RejectsNonAbsoluteExecutable()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TectonicCompiler().CompileAsync(
                "tectonic.exe",
                "x",
                TimeSpan.FromSeconds(1)
            )
        );
    }

    [Fact]
    public async Task RejectsOversizedSource()
    {
        var path = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                new TectonicCompiler().CompileAsync(
                    Path.GetFullPath(path),
                    new string('x', 100_001),
                    TimeSpan.FromSeconds(1)
                )
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsEmptySource()
    {
        var path = Path.GetFullPath(Path.GetTempFileName());
        try
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                new TectonicCompiler().CompileAsync(
                    path,
                    " \n",
                    TimeSpan.FromSeconds(1)
                )
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsExpectedProviderHashMismatch()
    {
        var path = Path.GetFullPath(Path.GetTempFileName());
        try
        {
            await File.WriteAllTextAsync(path, "not-tectonic");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new TectonicCompiler().CompileAsync(
                    path,
                    "x",
                    TimeSpan.FromSeconds(1),
                    new string('0', 64)
                )
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CompilesCompleteTexWhenOptedIn()
    {
        var path = Environment.GetEnvironmentVariable("WORDTOOLKIT_TEST_TECTONIC_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        var source =
            "\\documentclass{article}\n"
            + "\\usepackage{amsmath}\n"
            + "\\newcommand{\\R}{R}\n"
            + "\\begin{document}\n"
            + "$\\int_0^1 x^2\\,dx$ and "
            + "$A=\\begin{matrix}1&2\\\\3&4\\end{matrix}$ over $\\R$.\n"
            + "\\end{document}\n";
        var result = await new TectonicCompiler().CompileAsync(
            Path.GetFullPath(path),
            source,
            TimeSpan.FromSeconds(30)
        );
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(x => x.Message)));
        Assert.NotNull(result.PdfBytes);
        Assert.True(
            result.PdfBytes!.Length > 4
            && result.PdfBytes.AsSpan(0, 4).SequenceEqual("%PDF"u8)
        );
        Assert.Equal(result.PdfBytes.Length, result.PdfBytesLength);
        Assert.Equal(64, result.PdfSha256!.Length);
        Assert.Contains("Tectonic", result.ProviderVersion, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.OnlyCachedResources);
        Assert.False(result.NetworkRequested);
        Assert.DoesNotContain(
            "wordtoolkit_tex_",
            string.Join("\n", result.Diagnostics.Select(item => item.Message)),
            StringComparison.OrdinalIgnoreCase
        );
    }
}
