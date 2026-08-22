using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class PackageCliExitCodeTests
{
    [Fact]
    public void SnapshotSourceChangesUseRetryableTemporaryFailureExitCode()
    {
        Assert.Equal(75, DocumentAnalysisPackageCli.ExitCode("SOURCE_CHANGED"));
        Assert.Equal(75, FlatOpcPackageCli.ExitCode("SOURCE_CHANGED"));
        Assert.Equal(75, HeadingOutlinePackageCli.ExitCode("SOURCE_CHANGED"));
        Assert.Equal(75, OcrPackageCli.ExitCode("SOURCE_CHANGED"));
        Assert.Equal(75, PatchRollbackPackageCli.ExitCode("SOURCE_CHANGED"));
        Assert.Equal(75, QueryPackageCli.ExitCode("SOURCE_CHANGED"));
    }
}
