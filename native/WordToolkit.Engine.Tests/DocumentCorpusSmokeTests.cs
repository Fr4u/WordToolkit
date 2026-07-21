using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Tests;

public sealed class DocumentCorpusSmokeTests
{
    [Fact]
    public void ParsesEveryXmlPartInBundledMultiProducerDocxCorpusWithoutRewritingBytes()
    {
        var root = FindRepositoryRoot();
        var documentPaths = new[]
        {
            Path.Combine(root, "examples"),
            Path.Combine(root, "tests", "upstream", "fixtures"),
            Path.Combine(root, "tests", "upstream", "fuzz", "corpus"),
        }
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.docx", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(
            documentPaths.Length >= 40,
            $"Expected at least 40 corpus documents, found {documentPaths.Length}."
        );

        var reader = new OpcPackageReader();
        var parsedXmlParts = 0;
        foreach (var path in documentPaths)
        {
            var package = reader.Read(path);
            _ = new WordSemanticProjector().Project(package);
            foreach (var part in package.Parts.Values.Where(part => IsXml(part.ContentType)))
            {
                var source = LosslessXmlDocument.Parse(part.Entry.Content);
                Assert.Equal(
                    part.Entry.Content.ToArray(),
                    source.ApplyPatches(Array.Empty<XmlSourcePatch>())
                );
                parsedXmlParts++;
            }
        }

        Assert.True(
            parsedXmlParts >= 200,
            $"Expected at least 200 XML parts, parsed {parsedXmlParts}."
        );
    }

    private static bool IsXml(string? contentType) =>
        contentType is not null
        && (
            contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase)
        );

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the WordToolkit repository root.");
    }
}
