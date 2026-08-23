using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

public static class EquationRepairWordPackageContract
{
    public const string InspectOperationName = "inspect_ooxml_equation_repairs";
    public const string PlanOperationName = "plan_ooxml_equation_repair";
    public const string ApplyOperationName = "apply_ooxml_equation_repair";
    public const string InspectContract = "wordtoolkit.inspect_ooxml_equation_repairs/1.0";
    public const string PlanContract = "wordtoolkit.plan_ooxml_equation_repair/1.0";
    public const string ApplyContract = "wordtoolkit.apply_ooxml_equation_repair/1.0";
    public const int MaximumReturnedItems = 200;
    public const int MaximumCommands = 32;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 128 * 1024;
}

public sealed record EquationRepairInspectionRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    bool IncludeSource = false,
    bool IncludeIssues = true,
    int MaxItems = 50
);

public sealed record EquationRepairCommandRequest(
    string RepairKind,
    string CandidateId,
    string ExpectedCandidateFingerprint
);

public sealed record EquationRepairPlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<EquationRepairCommandRequest> Commands,
    bool IncludeDetails = false
);

public sealed record EquationRepairApplyRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string ExpectedPlanId,
    IReadOnlyList<EquationRepairCommandRequest> Commands,
    bool KeepBackup = true,
    string? ProtectedEditAuthorization = null
);

public sealed record EquationRepairEditPolicyBlockDetails(
    string PlanId,
    IReadOnlyList<string> BlockCodes
);

public sealed record EquationRepairInspectionCandidate(
    string Id,
    string Fingerprint,
    string RepairKind,
    string IssueCode,
    int RemovedGroupMemberCount,
    int RemovedXmlElementCount,
    string ParentElementName,
    string DuplicateElementName,
    string? EquationId,
    string? NodeId,
    string? PartUri,
    int? ParentElementOrdinal,
    int? RetainedElementOrdinal
);

public sealed record EquationRepairInspectionIssue(
    string Code,
    string Severity,
    string? EquationId,
    string? NodeId,
    bool RepairCandidate,
    string? PartUri,
    int? SourceElementOrdinal
);

public sealed record EquationRepairInspectionResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    bool AnalysisExecutionComplete,
    bool RepairCoverageComplete,
    bool IssuesTruncated,
    int EquationCount,
    int MalformedEquationCount,
    int UnsupportedEquationCount,
    int IssueCount,
    int ErrorCount,
    int WarningCount,
    int CandidateCount,
    int DuplicatePropertyContainerCandidateCount,
    int DuplicatePropertyCandidateCount,
    int ReturnedCandidateCount,
    bool CandidatesTruncated,
    IReadOnlyList<EquationRepairInspectionCandidate> Candidates,
    int ReturnedIssueCount,
    bool ReturnedIssuesTruncated,
    IReadOnlyList<EquationRepairInspectionIssue>? Issues,
    IReadOnlyList<string> SupportedRepairIssueCodes,
    IReadOnlyList<string> ExplicitlyUnsupportedIssueCodes,
    bool SensitiveEquationTextReturned,
    bool RawOmmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record EquationRepairPlanResult(
    string OperationContract,
    string FileName,
    string PlanId,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    int CommandCount,
    int CandidateCount,
    int RemovedGroupMemberCount,
    int RemovedXmlElementCount,
    int ChangedPartCount,
    long TotalByteDelta,
    bool HasChanges,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordEquationRepairValidation EngineValidation,
    WordPackageCandidateValidationReport CandidateValidation,
    bool MicrosoftSchemaErrorsReduced,
    IReadOnlyList<string> SafetyRules,
    IReadOnlyList<EquationRepairInspectionCandidate>? Candidates,
    IReadOnlyList<WordEquationRepairPartChange>? ChangedParts,
    bool SensitiveEquationTextReturned,
    bool RawOmmlReturned,
    bool MutationPerformed,
    bool WordOpened,
    WordPackageProtectionRiskAssessment Protection,
    string? ProtectionAuthorizationId,
    IReadOnlyList<string> RequiredAuthorizations
);

public sealed record EquationRepairApplyResult(
    string OperationContract,
    string FileName,
    string PlanId,
    bool Applied,
    int CandidateCount,
    int RemovedGroupMemberCount,
    int RemovedXmlElementCount,
    string PreviousPackageFingerprint,
    string PackageFingerprint,
    string PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int DiagnosticCount,
    bool MicrosoftSchemaNoNewErrors,
    bool MicrosoftSchemaErrorsReduced,
    bool SensitiveEquationTextReturned,
    bool RawOmmlReturned,
    bool MutationPerformed,
    bool WordOpened,
    IReadOnlyList<string> ExplicitAuthorizations
);
