using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

public static class TemplateStyleAlignmentWordPackageContract
{
    public const string InspectOperationName = "inspect_ooxml_template_style_alignment";
    public const string PlanOperationName = "plan_ooxml_template_style_alignment";
    public const string ApplyOperationName = "apply_ooxml_template_style_alignment";
    public const string InspectContract =
        "wordtoolkit.inspect_ooxml_template_style_alignment/1.0";
    public const string PlanContract =
        "wordtoolkit.plan_ooxml_template_style_alignment/1.0";
    public const string ApplyContract =
        "wordtoolkit.apply_ooxml_template_style_alignment/1.0";
    public const int MaximumReturnedItems = 200;
    public const int MaximumCommands = 64;
    public const int MaximumPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 256 * 1024;
}

public sealed record TemplateStyleAlignmentInspectRequest(
    string TargetPath,
    string TemplatePath,
    string ExpectedTargetPackageFingerprint,
    string ExpectedTemplatePackageFingerprint,
    int MaxItems = 50,
    bool IncludeIssues = true,
    bool IncludeDependencies = false
);

public sealed record TemplateStyleAlignmentCommandRequest(
    string CandidateId,
    string ExpectedCandidateFingerprint
);

public sealed record TemplateStyleAlignmentPlanRequest(
    string TargetPath,
    string TemplatePath,
    string ExpectedTargetPackageFingerprint,
    string ExpectedTemplatePackageFingerprint,
    IReadOnlyList<TemplateStyleAlignmentCommandRequest> Commands,
    bool IncludeDetails = false
);

public sealed record TemplateStyleAlignmentApplyRequest(
    string TargetPath,
    string TemplatePath,
    string ExpectedTargetPackageFingerprint,
    string ExpectedTemplatePackageFingerprint,
    string ExpectedPlanId,
    IReadOnlyList<TemplateStyleAlignmentCommandRequest> Commands,
    bool KeepBackup = true
);

public sealed record TemplateStyleAlignmentInspectionCandidate(
    string Id,
    string Fingerprint,
    string StyleId,
    string StyleType,
    string AlignmentAction,
    int DependencyStyleCount,
    IReadOnlyList<string>? DependencyStyleIds,
    int AddedStyleCount,
    int ReplacedStyleCount,
    int AlreadyAlignedStyleCount,
    bool ThemeContextVerified,
    bool NumberingDependenciesVerified,
    bool StylesWithEffectsMirrored
);

public sealed record TemplateStyleAlignmentInspectionIssue(
    string Code,
    string Severity,
    string? StyleId
);

public sealed record TemplateStyleAlignmentInspectionResult(
    string OperationContract,
    string TargetFileName,
    string TemplateFileName,
    string TargetPackageFingerprint,
    string TemplatePackageFingerprint,
    bool AnalysisExecutionComplete,
    bool AlignmentCoverageComplete,
    bool CanPlan,
    bool StylesWithEffectsSymmetric,
    int CandidateCount,
    int AddCandidateCount,
    int ReplaceCandidateCount,
    int DependencyClosureCandidateCount,
    int AlreadyAlignedStyleCount,
    int IssueCount,
    int ErrorCount,
    int WarningCount,
    int ReturnedCandidateCount,
    bool CandidatesTruncated,
    IReadOnlyList<TemplateStyleAlignmentInspectionCandidate> Candidates,
    int ReturnedIssueCount,
    bool IssuesTruncated,
    IReadOnlyList<TemplateStyleAlignmentInspectionIssue>? Issues,
    bool LocalizedNameMatchingUsed,
    bool TemplateAttached,
    bool TemplateMutationPerformed,
    bool DocumentTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record TemplateStyleAlignmentPlanResult(
    string OperationContract,
    string TargetFileName,
    string TemplateFileName,
    string PlanId,
    string TargetPackageFingerprint,
    string TemplatePackageFingerprint,
    string ResultPackageFingerprint,
    int CommandCount,
    int CandidateCount,
    int AlignedStyleCount,
    int AddedStyleCount,
    int ReplacedStyleCount,
    int ChangedPartCount,
    long TotalByteDelta,
    bool HasChanges,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordTemplateStyleAlignmentValidation EngineValidation,
    WordPackageCandidateValidationReport CandidateValidation,
    IReadOnlyList<string> SafetyRules,
    IReadOnlyList<TemplateStyleAlignmentInspectionCandidate>? Candidates,
    IReadOnlyList<string>? AlignedStyleIds,
    IReadOnlyList<WordTemplateStyleAlignmentPartChange>? ChangedParts,
    bool LocalizedNameMatchingUsed,
    bool TemplateAttached,
    bool TemplateMutationPerformed,
    bool DocumentTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record TemplateStyleAlignmentApplyResult(
    string OperationContract,
    string TargetFileName,
    string TemplateFileName,
    string PlanId,
    bool Applied,
    int CandidateCount,
    int AlignedStyleCount,
    int AddedStyleCount,
    int ReplacedStyleCount,
    string PreviousTargetPackageFingerprint,
    string TemplatePackageFingerprint,
    string PackageFingerprint,
    string PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int DiagnosticCount,
    bool MicrosoftSchemaValid,
    bool MicrosoftSchemaNoNewErrors,
    bool LocalizedNameMatchingUsed,
    bool TemplateAttached,
    bool TemplateMutationPerformed,
    bool DocumentTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);
