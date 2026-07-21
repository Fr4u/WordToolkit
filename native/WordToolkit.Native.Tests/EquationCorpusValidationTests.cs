using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Native.Tests;

public sealed class EquationCorpusValidationTests
{
    [Theory]
    [InlineData("examples/generated/WordToolkit-equations.docx")]
    [InlineData("examples/generated/WordToolkit-showcase.docx")]
    [InlineData("examples/advanced/WordToolkit-advanced-torture-test.docx")]
    public void TrackedEquationDocumentsAreSchemaValidAndProjectable(
        string relativePath
    )
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)
        );
        using (var document = WordprocessingDocument.Open(path, false))
        {
            var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365)
                .Validate(document)
                .ToArray();
            Assert.True(
                errors.Length == 0,
                string.Join(
                    Environment.NewLine,
                    errors.Take(20).Select(error => error.Description)
                )
            );
        }

        var package = new OpcPackageReader().Read(path);
        var semantic = new WordSemanticProjector().Project(package);
        var graph = new WordEquationGraphBuilder().Build(package, semantic);

        Assert.NotEmpty(graph.Equations);
        Assert.Equal(
            graph.Equations.Sum(equation => equation.NodeCount),
            graph.NodeCount
        );
    }

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
        throw new DirectoryNotFoundException(
            "Could not locate the WordToolkit repository root."
        );
    }
}
