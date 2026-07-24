using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Tests;

public sealed class FlatOpcWordPackageOperationTests
{
    [Fact]
    public void DirectOperationExportsAndImportsCreateNewVerifiedArtifacts()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "source.docx");
            var flat = Path.Combine(directory, "transport.xml");
            var output = Path.Combine(directory, "roundtrip.docx");
            using (var source = FlatOpcPackageCodecTests.BuildWordPackage())
            using (var file = File.Create(input))
            {
                source.CopyTo(file);
            }
            var inputHash = Hash(input);
            var operation = new FlatOpcWordPackageOperation();

            var exported = operation.Execute(
                new FlatOpcWordPackageRequest(
                    input,
                    flat,
                    FlatOpcConversionDirection.ToFlatOpc
                )
            );
            var imported = operation.Execute(
                new FlatOpcWordPackageRequest(
                    flat,
                    output,
                    FlatOpcConversionDirection.FromFlatOpc
                )
            );

            Assert.Equal("wordtoolkit.convert_ooxml_flat_opc/1.0", exported.OperationContract);
            Assert.Equal("to_flat_opc", exported.Direction);
            Assert.Equal("from_flat_opc", imported.Direction);
            Assert.True(exported.PackageSemanticallyEquivalent);
            Assert.True(imported.PackageSemanticallyEquivalent);
            Assert.True(exported.StructurallyValid);
            Assert.True(imported.StructurallyValid);
            Assert.False(exported.RawXmlReturned);
            Assert.False(exported.WordOpened);
            Assert.Equal(inputHash, Hash(input));
            Assert.Equal(exported.OutputSha256, Hash(flat));
            Assert.Equal(imported.OutputSha256, Hash(output));
            Assert.Equal(
                imported.ResultPackageFingerprint,
                new OpcPackageReader().Read(output).Fingerprint
            );
            Assert.Equal(4, exported.PartCount);
            Assert.Equal(3, exported.XmlPartCount);
            Assert.Equal(1, exported.BinaryPartCount);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directory).Select(Path.GetFileName),
                name => name!.Contains(".wordtoolkit-", StringComparison.Ordinal)
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BundledAdvancedDocumentRoundTripsDespiteXmlDeclarationNormalization()
    {
        var root = FindRepositoryRoot();
        var input = Path.Combine(
            root,
            "examples",
            "advanced",
            "WordToolkit-advanced-seed.docx"
        );
        Assert.True(File.Exists(input), $"Missing bundled corpus document: {input}");
        var directory = TemporaryDirectory();
        try
        {
            var flat = Path.Combine(directory, "transport.xml");
            var output = Path.Combine(directory, "roundtrip.docx");
            var operation = new FlatOpcWordPackageOperation();

            var exported = operation.Execute(
                new FlatOpcWordPackageRequest(
                    input,
                    flat,
                    FlatOpcConversionDirection.ToFlatOpc
                )
            );
            var imported = operation.Execute(
                new FlatOpcWordPackageRequest(
                    flat,
                    output,
                    FlatOpcConversionDirection.FromFlatOpc
                )
            );

            Assert.True(exported.PackageSemanticallyEquivalent);
            Assert.True(imported.PackageSemanticallyEquivalent);
            Assert.True(new OpcPackageReader().Read(output).IsStructurallyValid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidFlatOpcNeverPublishesOrLeavesTransactionFiles()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "broken.xml");
            var output = Path.Combine(directory, "broken.docx");
            File.WriteAllText(
                input,
                "<!DOCTYPE pkg:package [<!ENTITY x 'poison'>]>"
                    + "<pkg:package xmlns:pkg='http://schemas.microsoft.com/office/2006/xmlPackage'>&x;</pkg:package>",
                new UTF8Encoding(false)
            );

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                new FlatOpcWordPackageOperation().Execute(
                    new FlatOpcWordPackageRequest(
                        input,
                        output,
                        FlatOpcConversionDirection.FromFlatOpc
                    )
                )
            );

            Assert.Equal("INVALID_PACKAGE", exception.Code);
            Assert.False(File.Exists(output));
            Assert.Equal(
                ["broken.xml"],
                Directory.EnumerateFiles(directory)
                    .Select(path => Path.GetFileName(path)!)
                    .ToArray()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ImportRequiresExtensionMatchingMainPartContentType()
    {
        var directory = TemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(directory, "source.docx");
            var flatPath = Path.Combine(directory, "source.xml");
            var wrongOutput = Path.Combine(directory, "wrong.docm");
            using (var source = FlatOpcPackageCodecTests.BuildWordPackage())
            using (var file = File.Create(packagePath))
            {
                source.CopyTo(file);
            }
            var operation = new FlatOpcWordPackageOperation();
            _ = operation.Execute(
                new FlatOpcWordPackageRequest(
                    packagePath,
                    flatPath,
                    FlatOpcConversionDirection.ToFlatOpc
                )
            );

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Execute(
                    new FlatOpcWordPackageRequest(
                        flatPath,
                        wrongOutput,
                        FlatOpcConversionDirection.FromFlatOpc
                    )
                )
            );

            Assert.Equal("INVALID_INPUT", exception.Code);
            Assert.False(File.Exists(wrongOutput));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directory).Select(Path.GetFileName),
                name => name!.Contains(".wordtoolkit-", StringComparison.Ordinal)
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExistingDestinationIsNeverOverwritten()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "source.docx");
            var output = Path.Combine(directory, "existing.xml");
            using (var source = FlatOpcPackageCodecTests.BuildWordPackage())
            using (var file = File.Create(input))
            {
                source.CopyTo(file);
            }
            File.WriteAllText(output, "sentinel", new UTF8Encoding(false));

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                new FlatOpcWordPackageOperation().Execute(
                    new FlatOpcWordPackageRequest(
                        input,
                        output,
                        FlatOpcConversionDirection.ToFlatOpc
                    )
                )
            );

            Assert.Equal("VERSION_CONFLICT", exception.Code);
            Assert.Equal("sentinel", File.ReadAllText(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsWrongDirectionExtensionsBeforeCreatingLock()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "source.docx");
            var output = Path.Combine(directory, "wrong.flatopc");
            using (var source = FlatOpcPackageCodecTests.BuildWordPackage())
            using (var file = File.Create(input))
            {
                source.CopyTo(file);
            }

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                new FlatOpcWordPackageOperation().Execute(
                    new FlatOpcWordPackageRequest(
                        input,
                        output,
                        FlatOpcConversionDirection.ToFlatOpc
                    )
                )
            );

            Assert.Equal("INVALID_INPUT", exception.Code);
            Assert.False(File.Exists(output));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directory).Select(Path.GetFileName),
                name => name!.EndsWith(".wordtoolkit.lock", StringComparison.Ordinal)
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-flatopc-operation-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
