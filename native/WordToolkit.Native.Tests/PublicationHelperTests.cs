using WordToolkit.Engine.Operations;
using WordToolkit.LibreOffice;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class PublicationHelperTests
{
    [Fact]
    public void WordPublication_CreateNew_AndOverwrite()
    {
        using var t = new TempFiles();
        File.WriteAllText(t.Staged, "new");
        WordLiveService.PublishStagedPdf(t.Staged, t.Output, false);
        Assert.Equal("new", File.ReadAllText(t.Output));
        File.Delete(t.Staged);
        File.WriteAllText(t.Staged, "replacement");
        WordLiveService.PublishStagedPdf(t.Staged, t.Output, true);
        Assert.Equal("replacement", File.ReadAllText(t.Output));
    }

    [Fact]
    public void WordPublication_Competitor_IsConflictAndCleansStaged()
    {
        using var t = new TempFiles();
        File.WriteAllText(t.Staged, "staged");
        WordToolkit.Native.Protocol.NativeToolException? error = null;
        try { WordLiveService.PublishStagedPdf(t.Staged, t.Output, false, (_, output) => File.WriteAllText(output, "competitor")); }
        catch (WordToolkit.Native.Protocol.NativeToolException ex) { error = ex; }
        Assert.Equal("VERSION_CONFLICT", error?.ErrorCode);
        Assert.Equal("competitor", File.ReadAllText(t.Output));
        File.Delete(t.Staged);
    }

    [Fact]
    public void LibreOfficePublication_NoClobberMapsOnlyAlreadyExists()
    {
        using var t = new TempFiles();
        File.WriteAllText(t.Staged, "staged");
        LibreOfficeUnoRenderProvider.PublishStagedPdfNoClobber(t.Staged, t.Output);
        Assert.Equal("staged", File.ReadAllText(t.Output));
        File.Delete(t.Staged);
        File.WriteAllText(t.Staged, "other");
        var ex = Assert.Throws<WordToolkitOperationException>(() => LibreOfficeUnoRenderProvider.PublishStagedPdfNoClobber(t.Staged, t.Output));
        Assert.Equal("OUTPUT_EXISTS", ex.Code);
        Assert.Equal("staged", File.ReadAllText(t.Output));
        Assert.Throws<FileNotFoundException>(() => LibreOfficeUnoRenderProvider.PublishStagedPdfNoClobber(Path.Combine(t.Root, "missing.tmp"), Path.Combine(t.Root, "x.pdf")));
    }

    private sealed class TempFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "wt-pub-" + Guid.NewGuid().ToString("N"));
        public string Staged => Path.Combine(Root, "staged.tmp");
        public string Output => Path.Combine(Root, "output.pdf");
        public TempFiles() => Directory.CreateDirectory(Root);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
