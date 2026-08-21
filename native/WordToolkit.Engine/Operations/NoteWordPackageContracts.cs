using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

public static class NoteWordPackageContract
{
    public const string InspectOperationName = "inspect_ooxml_notes";
    public const string PlanOperationName = "plan_ooxml_note_repair";
    public const string ApplyOperationName = "apply_ooxml_note_repair";
    public const string InspectContract = "wordtoolkit.inspect_ooxml_notes/1.0";
    public const string PlanContract = "wordtoolkit.plan_ooxml_note_repair/1.0";
    public const string ApplyContract = "wordtoolkit.apply_ooxml_note_repair/1.0";
    public const int MaximumReturnedItems = 200;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 64 * 1024;
}

public sealed record NoteInspectionRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    bool IncludeAll = false,
    bool IncludeDetails = false,
    int MaxItems = 50
);

public sealed record NoteRepairPlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string RepairKind,
    string DefinitionId,
    string ExpectedDefinitionFingerprint,
    bool IncludeDetails = false
);

public sealed record NoteRepairApplyRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string ExpectedPlanId,
    string RepairKind,
    string DefinitionId,
    string ExpectedDefinitionFingerprint,
    bool KeepBackup = true
);

public sealed record NoteInspectionDefinition(
    string Id,
    string Fingerprint,
    string Kind,
    string DefinitionType,
    int? OoxmlId,
    string PartUri,
    int ReferenceCount,
    int SpecialReferenceCount,
    int ParagraphCount,
    int TextCharacterCount,
    bool HasReferenceMark,
    bool HasComplexContent,
    bool IsOrphan,
    bool EmptyOrphanRemovalCandidate,
    bool RedundantDuplicateRemovalCandidate
);

public sealed record NoteInspectionReference(
    string Id,
    string Kind,
    int? OoxmlId,
    string PartUri,
    bool CustomMarkFollows,
    bool CustomMarkValueValid,
    bool NestedInsideNoteStory,
    string ResolutionStatus
);

public sealed record NoteInspectionSpecialReference(
    string Id,
    string Kind,
    int? OoxmlId,
    string PartUri,
    string ResolutionStatus
);

public sealed record NoteInspectionPolicy(
    string Id,
    string Kind,
    string Scope,
    int? SectionIndex,
    string? Position,
    string? NumberFormat,
    int? NumberStart,
    string? NumberRestart,
    bool ValuesValid
);

public sealed record NoteInspectionIssue(
    string Id,
    string Code,
    string Severity,
    string? Kind,
    string? SubjectId,
    string? PartUri,
    bool RepairCandidate
);

public sealed record NoteInspectionResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    bool AnalysisExecutionComplete,
    bool DocumentCoverageComplete,
    bool IssuesTruncated,
    int DefinitionCount,
    int ReferenceCount,
    int SpecialReferenceCount,
    int NumberingPolicyCount,
    int IssueCount,
    int ErrorCount,
    int WarningCount,
    int EmptyOrphanRemovalCandidateCount,
    int RedundantDuplicateRemovalCandidateCount,
    int ReturnedDefinitionCount,
    bool DefinitionsTruncated,
    IReadOnlyList<NoteInspectionDefinition> Definitions,
    int ReturnedIssueCount,
    bool ReturnedIssuesTruncated,
    IReadOnlyList<NoteInspectionIssue> Issues,
    bool? ReferenceDetailsTruncated,
    bool? SpecialReferenceDetailsTruncated,
    bool? NumberingPolicyDetailsTruncated,
    IReadOnlyList<NoteInspectionReference>? References,
    IReadOnlyList<NoteInspectionSpecialReference>? SpecialReferences,
    IReadOnlyList<NoteInspectionPolicy>? NumberingPolicies,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record NoteRepairPlanResult(
    string OperationContract,
    string FileName,
    string RepairKind,
    string PlanId,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    string DefinitionId,
    string DefinitionFingerprint,
    string NoteKind,
    int? OoxmlId,
    string PartUri,
    int ChangedPartCount,
    long TotalByteDelta,
    bool HasChanges,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordNoteRepairValidation EngineValidation,
    WordPackageCandidateValidationReport CandidateValidation,
    IReadOnlyList<string> SafetyRules,
    IReadOnlyList<WordNoteRepairPartChange>? ChangedParts,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record NoteRepairApplyResult(
    string OperationContract,
    string FileName,
    string RepairKind,
    string PlanId,
    bool Applied,
    string DefinitionId,
    string PreviousPackageFingerprint,
    string PackageFingerprint,
    string PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int DiagnosticCount,
    bool MicrosoftSchemaValid,
    bool MicrosoftSchemaNoNewErrors,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);
