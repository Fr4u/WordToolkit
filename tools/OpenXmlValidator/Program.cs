using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: wordtoolkit-openxml-validator <document.docx>");
    return 64;
}

var issues = new List<object>();
try
{
    using var document = WordprocessingDocument.Open(args[0], false);
    var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
    foreach (var error in validator.Validate(document).Take(500))
    {
        issues.Add(new
        {
            id = error.Id,
            description = error.Description,
            errorType = error.ErrorType.ToString(),
            part = error.Part?.Uri.ToString(),
            path = error.Path?.XPath,
            node = error.Node?.LocalName,
        });
    }
    var payload = new { valid = issues.Count == 0, errors = issues.Count, issues };
    Console.WriteLine(JsonSerializer.Serialize(payload));
    return issues.Count == 0 ? 0 : 2;
}
catch (Exception exception)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        valid = false,
        errors = 1,
        issues = new[] { new { id = "OPEN_FAILED", description = exception.GetType().Name } },
    }));
    return 3;
}
