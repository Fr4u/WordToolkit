using System.Security.Cryptography;
using WordToolkit.Engine.Operations;
using Xunit;

namespace WordToolkit.LibreOffice.Tests;

public sealed class LibreOfficeUnoRenderProviderTests
{
    [Fact]
    public async Task RejectsInvalidHashesBeforeTouchingTheBackend()
    {
        var request = EmptyRequest() with
        {
            ExpectedLibreOfficeExecutableSha256 = "not-a-hash",
        };

        var error = await Assert.ThrowsAsync<WordToolkitOperationException>(() =>
            new LibreOfficeUnoRenderProvider().RenderAsync(request)
        );

        Assert.Equal("INVALID_INPUT", error.Code);
    }

    [Fact]
    public async Task RejectsOpenEndedNonDefaultPageRange()
    {
        var request = EmptyRequest() with { FirstPage = 2, LastPage = null };

        var error = await Assert.ThrowsAsync<WordToolkitOperationException>(() =>
            new LibreOfficeUnoRenderProvider().RenderAsync(request)
        );

        Assert.Equal("INVALID_INPUT", error.Code);
    }

    [Fact]
    public async Task RejectsInputFilterThatDoesNotMatchPackageKind()
    {
        var request = EmptyRequest() with
        {
            InputFilterName = "Office Open XML Text Template",
        };

        var error = await Assert.ThrowsAsync<WordToolkitOperationException>(() =>
            new LibreOfficeUnoRenderProvider().RenderAsync(request)
        );

        Assert.Equal("INVALID_INPUT", error.Code);
    }

    [Fact]
    public async Task RealUnoWriterPdfExportRunsOnlyWhenExplicitlyConfigured()
    {
        var office = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_PATH"
        );
        var java = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_JAVA_PATH"
        );
        var libreOfficeJar = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_JAR_PATH"
        );
        var source = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_SOURCE_PATH"
        );
        if (new[] { office, java, libreOfficeJar, source }
            .Any(string.IsNullOrWhiteSpace))
        {
            return;
        }

        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-libreoffice-uno-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, "render.pdf");
        try
        {
            var observation = await new LibreOfficeUnoRenderProvider().RenderAsync(
                new LibreOfficeUnoRenderProviderRequest(
                    office!,
                    RequiredEnvironmentHash("WORDTOOLKIT_TEST_LIBREOFFICE_UNO_SHA256"),
                    java!,
                    RequiredEnvironmentHash(
                        "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_JAVA_SHA256"
                    ),
                    libreOfficeJar!,
                    RequiredEnvironmentHash(
                        "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_JAR_SHA256"
                    ),
                    source!,
                    Sha256(source!),
                    output,
                    "Office Open XML Text",
                    FirstPage: 1,
                    LastPage: null,
                    PdfA1b: false,
                    ExportBookmarks: true,
                    TimeoutMilliseconds: 60_000
                )
            );

            Assert.Equal(LibreOfficeUnoRenderContract.ProviderContract, observation.ProviderContract);
            Assert.True(observation.SourceHashStable);
            Assert.True(observation.DocumentPolicy.ReadOnlyVerified);
            Assert.True(observation.DocumentPolicy.MacroNeverExecuteRequested);
            Assert.False(observation.DocumentPolicy.MacroPreventionBehaviorallyVerified);
            Assert.True(observation.DocumentPolicy.UpdateNoUpdateRequested);
            Assert.False(
                observation.DocumentPolicy.ExternalUpdatePreventionBehaviorallyVerified
            );
            Assert.True(observation.Export.UnoConnectionVerified);
            Assert.True(observation.Export.WriterComponentVerified);
            Assert.True(observation.Export.WriterPdfExportVerified);
            Assert.True(observation.Cleanup.DocumentClosed);
            Assert.True(observation.Cleanup.DesktopTerminated);
            Assert.True(observation.Cleanup.HelperExited);
            Assert.True(observation.Cleanup.LibreOfficeExited);
            Assert.False(observation.Cleanup.ProcessTreeKillRequired);
            Assert.True(observation.Cleanup.PrivateProfileDeleted);
            Assert.True(observation.Cleanup.PrivateWorkspaceDeleted);
            Assert.True(File.Exists(output));
            Assert.Equal(observation.Export.PdfSha256, Sha256(output));
            using var stream = File.OpenRead(output);
            var signature = new byte[5];
            Assert.Equal(5, stream.Read(signature));
            Assert.True(signature.AsSpan().SequenceEqual("%PDF-"u8));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static LibreOfficeUnoRenderProviderRequest EmptyRequest() => new(
        Path.Combine(Path.GetTempPath(), "soffice"),
        new string('0', 64),
        Path.Combine(Path.GetTempPath(), "java"),
        new string('0', 64),
        Path.Combine(Path.GetTempPath(), "classes", "libreoffice.jar"),
        new string('0', 64),
        Path.Combine(Path.GetTempPath(), "source.docx"),
        new string('0', 64),
        Path.Combine(Path.GetTempPath(), "output.pdf"),
        "Office Open XML Text",
        FirstPage: 1,
        LastPage: null,
        PdfA1b: false,
        ExportBookmarks: true,
        TimeoutMilliseconds: 60_000
    );

    private static string RequiredEnvironmentHash(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"{name} is required");
        return value!;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
