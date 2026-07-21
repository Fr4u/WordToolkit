using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Native.Documents;

namespace WordToolkit.Native.Tests;

public sealed class NativeTestDocumentTests
{
    [Fact]
    public void CreatesValidDocxWithoutOverwriting()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wordtoolkit-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "native-test.docx");
        try
        {
            _ = NativeTestDocument.Create(path);
            Assert.True(File.Exists(path));
            using var package = WordprocessingDocument.Open(path, false);
            Assert.Empty(new OpenXmlValidator().Validate(package));
            Assert.Throws<IOException>(() => NativeTestDocument.Create(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
