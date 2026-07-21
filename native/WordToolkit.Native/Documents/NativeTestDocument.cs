using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WordToolkit.Native.Documents;

internal static class NativeTestDocument
{
    public static object Create(string path)
    {
        var output = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(output), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The native test document must use the .docx extension");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
        {
            throw new IOException($"Refusing to overwrite an existing file: {output}");
        }

        using (var package = WordprocessingDocument.Create(
            output,
            WordprocessingDocumentType.Document
        ))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new Document(
                new Body(
                    Paragraph(
                        "WordToolkit Native — test szybkości",
                        "Title"
                    ),
                    Paragraph(
                        "Ten dokument został utworzony przez natywny kod .NET. "
                        + "Nie uruchomiono Pythona, uv ani środowiska wirtualnego.",
                        "Normal"
                    ),
                    Paragraph(
                        "Po otwarciu dokumentu WordToolkit.Native połączy się z widocznym "
                        + "Wordem przez Running Object Table i wstawi wynik w jednej transakcji COM.",
                        "Normal"
                    ),
                    new SectionProperties(
                        new PageSize { Width = 11_906U, Height = 16_838U },
                        new PageMargin
                        {
                            Top = 1_440,
                            Right = 1_440U,
                            Bottom = 1_440,
                            Left = 1_440U,
                            Header = 720U,
                            Footer = 720U,
                            Gutter = 0U,
                        }
                    )
                )
            );
            main.Document.Save();
        }

        var issues = new List<string>();
        using (var package = WordprocessingDocument.Open(output, false))
        {
            var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
            issues.AddRange(
                validator.Validate(package)
                    .Take(100)
                    .Select(error => $"{error.Id}: {error.Description}")
            );
        }
        if (issues.Count > 0)
        {
            File.Delete(output);
            throw new InvalidDataException(
                $"Native test DOCX failed validation: {string.Join("; ", issues)}"
            );
        }
        return new
        {
            created = true,
            path = output,
            bytes = new FileInfo(output).Length,
            valid = true,
            validation_errors = 0,
            runtime = "dotnet-native",
            python_used = false,
        };
    }

    private static Paragraph Paragraph(string text, string style)
    {
        return new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = style }),
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })
        );
    }
}
