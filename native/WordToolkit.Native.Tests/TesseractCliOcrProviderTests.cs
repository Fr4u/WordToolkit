using System.Security.Cryptography;
using WordToolkit.Engine.Extensions;
using WordToolkit.Native.Ocr;

namespace WordToolkit.Native.Tests;

public sealed class TesseractCliOcrProviderTests
{
    private static readonly byte[] TestPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9WlSAAAAAASUVORK5CYII="
    );

    [Fact]
    public void RejectsUnsafeLanguagesBeforeResolvingAnyExecutable()
    {
        var request = Request(
            languages: ["../eng"],
            executablePath: "relative.exe",
            modelDirectory: "relative-models"
        );

        var exception = Assert.Throws<WordToolkitExtensionException>(() =>
            new TesseractCliOcrProvider().Recognize(request)
        );

        Assert.Equal("OCR_INVALID_INPUT", exception.Code);
    }

    [Fact]
    public void RefusesPathLookupAndRequiresAbsoluteBoundProviderPaths()
    {
        var request = Request(
            languages: ["eng"],
            executablePath: "tesseract.exe",
            modelDirectory: "tessdata"
        );

        var exception = Assert.Throws<WordToolkitExtensionException>(() =>
            new TesseractCliOcrProvider().Recognize(request)
        );

        Assert.Equal("OCR_PROVIDER_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("tesseract.exe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalProviderRefusesUncExecutableAndModelRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var request = Request(
            languages: ["eng"],
            executablePath: @"\\server\share\tesseract.exe",
            modelDirectory: @"\\server\share\tessdata"
        );

        var exception = Assert.Throws<WordToolkitExtensionException>(() =>
            new TesseractCliOcrProvider().Recognize(request)
        );

        Assert.Equal("OCR_PROVIDER_UNAVAILABLE", exception.Code);
        Assert.Contains("local filesystem", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsChangedImageBytesBeforeSpawningTheProvider()
    {
        var request = Request(
            languages: ["eng"],
            executablePath: Path.GetFullPath("missing-tesseract.exe"),
            modelDirectory: Path.GetFullPath("missing-tessdata"),
            imageSha256: new string('0', 64)
        );

        var exception = Assert.Throws<WordToolkitExtensionException>(() =>
            new TesseractCliOcrProvider().Recognize(request)
        );

        Assert.Equal("OCR_INPUT_CHANGED", exception.Code);
    }

    private static WordOcrProviderRequest Request(
        IReadOnlyList<string> languages,
        string executablePath,
        string modelDirectory,
        string? imageSha256 = null
    ) => new(
        TestPng,
        "image/png",
        imageSha256 ?? Convert.ToHexString(SHA256.HashData(TestPng)).ToLowerInvariant(),
        languages,
        WordOcrLayoutHint.Automatic,
        30_000,
        1_000_000,
        new WordOcrProviderConfiguration(executablePath, modelDirectory)
    );
}
