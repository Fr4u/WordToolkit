using System.Text.Json.Serialization;

namespace WordToolkit.Engine.Validation;

public sealed record WordPackageValidationIssue(
    string? Id,
    string ErrorType,
    string? PartUri,
    string? Path,
    string? Node
);

public sealed record WordPackageCandidateValidationReport(
    bool Performed,
    [property: JsonPropertyName("valid")]
    bool CandidateValid,
    bool NoNewErrors,
    int ErrorCount,
    int BaselineErrorCount,
    int CandidateErrorCount,
    bool ErrorsTruncated,
    string? NotPerformedReason,
    IReadOnlyList<WordPackageValidationIssue> Issues
)
{
    public static WordPackageCandidateValidationReport NotPerformed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new WordPackageCandidateValidationReport(
            Performed: false,
            CandidateValid: false,
            NoNewErrors: false,
            ErrorCount: 0,
            BaselineErrorCount: 0,
            CandidateErrorCount: 0,
            ErrorsTruncated: false,
            NotPerformedReason: reason,
            Issues: Array.Empty<WordPackageValidationIssue>()
        );
    }
}

public sealed record WordPackageValidationFailureDetails(
    int ErrorCount,
    int BaselineErrorCount,
    int CandidateErrorCount,
    bool ErrorsTruncated,
    IReadOnlyList<WordPackageValidationIssue> Issues
);

/// <summary>
/// Validates an exact candidate Word package against its exact baseline.
/// Implementations must not execute active document content or resolve external resources.
/// </summary>
public interface IWordPackageCandidateValidator
{
    WordPackageCandidateValidationReport Validate(
        Stream baselinePackage,
        Stream candidatePackage,
        CancellationToken cancellationToken = default
    );
}
