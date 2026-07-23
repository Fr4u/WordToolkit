using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Validation;

namespace WordToolkit.OpenXmlSdk;

/// <summary>
/// Applies the Microsoft Open XML SDK validator to a baseline and candidate package and
/// reports only errors introduced by the candidate. Document content remains local and inert.
/// </summary>
public sealed class MicrosoftOpenXmlPackageValidator : IWordPackageCandidateValidator
{
    public const int MaximumValidationErrors = 500;

    public WordPackageCandidateValidationReport Validate(
        Stream baselinePackage,
        Stream candidatePackage,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(baselinePackage);
        ArgumentNullException.ThrowIfNull(candidatePackage);
        RequireReadableSeekable(baselinePackage, nameof(baselinePackage));
        RequireReadableSeekable(candidatePackage, nameof(candidatePackage));
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var baseline = ValidateStream(baselinePackage, cancellationToken);
            var candidate = ValidateStream(candidatePackage, cancellationToken);
            if (
                baseline.Length > MaximumValidationErrors
                || candidate.Length > MaximumValidationErrors
            )
            {
                return new WordPackageCandidateValidationReport(
                    Performed: true,
                    CandidateValid: false,
                    NoNewErrors: false,
                    ErrorCount: 1,
                    BaselineErrorCount: Math.Min(
                        baseline.Length,
                        MaximumValidationErrors
                    ),
                    CandidateErrorCount: Math.Min(
                        candidate.Length,
                        MaximumValidationErrors
                    ),
                    ErrorsTruncated: true,
                    NotPerformedReason: null,
                    Issues:
                    [
                        new WordPackageValidationIssue(
                            "OPEN_XML_VALIDATION_LIMIT_EXCEEDED",
                            "Limit",
                            null,
                            null,
                            null
                        ),
                    ]
                );
            }

            var baselineCounts = baseline
                .GroupBy(IssueKey)
                .ToDictionary(group => group.Key, group => group.Count());
            var newErrors = new List<WordPackageValidationIssue>();
            foreach (var issue in candidate)
            {
                var key = IssueKey(issue);
                if (baselineCounts.TryGetValue(key, out var count) && count > 0)
                {
                    baselineCounts[key] = count - 1;
                }
                else
                {
                    newErrors.Add(issue);
                }
            }

            return new WordPackageCandidateValidationReport(
                Performed: true,
                CandidateValid: candidate.Length == 0,
                NoNewErrors: newErrors.Count == 0,
                ErrorCount: newErrors.Count,
                BaselineErrorCount: baseline.Length,
                CandidateErrorCount: candidate.Length,
                ErrorsTruncated: newErrors.Count > 200,
                NotPerformedReason: null,
                Issues: newErrors.Take(200).ToArray()
            );
        }
        catch (OpenXmlPackageException exception)
        {
            return OpenFailed(exception.GetType().Name);
        }
        catch (InvalidDataException exception)
        {
            return OpenFailed(exception.GetType().Name);
        }
    }

    private static WordPackageValidationIssue[] ValidateStream(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        var result = new List<WordPackageValidationIssue>(
            MaximumValidationErrors + 1
        );
        foreach (var error in validator.Validate(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new WordPackageValidationIssue(
                error.Id,
                error.ErrorType.ToString(),
                error.Part?.Uri.ToString(),
                error.Path?.XPath,
                error.Node?.LocalName
            ));
            if (result.Count > MaximumValidationErrors)
            {
                break;
            }
        }
        return result.ToArray();
    }

    private static string IssueKey(WordPackageValidationIssue issue) =>
        string.Join(
            '\u001f',
            issue.Id ?? string.Empty,
            issue.ErrorType,
            issue.PartUri ?? string.Empty,
            issue.Path ?? string.Empty,
            issue.Node ?? string.Empty
        );

    private static WordPackageCandidateValidationReport OpenFailed(
        string exceptionType
    ) => new(
        Performed: true,
        CandidateValid: false,
        NoNewErrors: false,
        ErrorCount: 1,
        BaselineErrorCount: 0,
        CandidateErrorCount: 1,
        ErrorsTruncated: false,
        NotPerformedReason: null,
        Issues:
        [
            new WordPackageValidationIssue(
                "OPEN_XML_PACKAGE_OPEN_FAILED",
                exceptionType,
                null,
                null,
                null
            ),
        ]
    );

    private static void RequireReadableSeekable(Stream stream, string name)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "Package validation streams must be readable and seekable.",
                name
            );
        }
    }
}
