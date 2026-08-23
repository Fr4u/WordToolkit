using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

public static class RelationshipRepairWordPackageContract
{
    public const string InspectOperationName = "inspect_ooxml_relationships";
    public const string PlanOperationName = "plan_ooxml_relationship_repair";
    public const string ApplyOperationName = "apply_ooxml_relationship_repair";
    public const string InspectContract = "wordtoolkit.inspect_ooxml_relationships/1.0";
    public const string PlanContract = "wordtoolkit.plan_ooxml_relationship_repair/1.0";
    public const string ApplyContract = "wordtoolkit.apply_ooxml_relationship_repair/1.0";
    public const int MaximumCommands = 100;
    public const int MaximumChangedEntries = 32;
    public const int MaximumReturnedItems = 200;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 128 * 1024;
}

public sealed record RelationshipInspectionRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    bool IncludeAll = false,
    bool IncludeDetails = false,
    int MaxItems = 50
);

public sealed record RelationshipRepairCommandRequest(
    string Kind,
    string? SourcePartUri,
    string? RelationshipId,
    string? ExpectedRelationshipFingerprint,
    string? RelationshipPartUri,
    string? ExpectedEntrySha256
);

public sealed record RelationshipRepairPlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<RelationshipRepairCommandRequest> Commands,
    bool IncludeDetails = false
);

public sealed record RelationshipRepairApplyRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string ExpectedPlanId,
    IReadOnlyList<RelationshipRepairCommandRequest> Commands,
    bool AllowExternalRelationshipRemoval = false,
    bool KeepBackup = true,
    string? ProtectedEditAuthorization = null
);

public sealed record RelationshipRepairEditPolicyBlockDetails(
    string PlanId,
    IReadOnlyList<string> BlockCodes
);

public sealed record RelationshipInspectionItem(
    string Id,
    string Fingerprint,
    string SourcePartUri,
    string RelationshipPartUri,
    string RelationshipId,
    string RelationshipTypeName,
    string TargetMode,
    string? ResolvedTargetPartUri,
    bool HasTargetFragment,
    string Status,
    int MarkupReferenceCount,
    bool MarkupReferencesTruncated,
    bool MarkupRemovalCandidate,
    IReadOnlyList<WordRelationshipMarkupReference>? MarkupReferences
);

public sealed record RelationshipInspectionOrphanPart(
    string Id,
    string RelationshipPartUri,
    string SourcePartUri,
    string EntrySha256,
    int ParsedRelationshipCount
);

public sealed record RelationshipInspectionResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    int RelationshipCount,
    int MarkupRemovalCandidateCount,
    int OrphanRelationshipPartCount,
    IReadOnlyDictionary<string, int> StatusCounts,
    int ReturnedRelationshipCount,
    bool RelationshipsTruncated,
    IReadOnlyList<RelationshipInspectionItem> Relationships,
    int ReturnedOrphanPartCount,
    bool OrphanPartsTruncated,
    IReadOnlyList<RelationshipInspectionOrphanPart> OrphanRelationshipParts,
    bool ExternalTargetsReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record RelationshipRepairPlanResult(
    string OperationContract,
    string FileName,
    string RepairKind,
    string PlanId,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    int CommandCount,
    int RemovedRelationshipCount,
    int ChangedEntryCount,
    long TotalByteDelta,
    bool RequiresExternalRelationshipAuthorization,
    bool HasChanges,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordPackageProtectionRiskAssessment Protection,
    string? ProtectionAuthorizationId,
    IReadOnlyList<string> RequiredAuthorizations,
    WordRelationshipRepairValidation EngineValidation,
    WordPackageCandidateValidationReport CandidateValidation,
    IReadOnlyList<string> SafetyRules,
    IReadOnlyList<WordRelationshipRepairAction>? Actions,
    IReadOnlyList<WordRelationshipRepairEntryChange>? ChangedEntries,
    bool ExternalTargetsReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record RelationshipRepairApplyResult(
    string OperationContract,
    string FileName,
    string RepairKind,
    string PlanId,
    bool Applied,
    int CommandCount,
    int RemovedRelationshipCount,
    string PreviousPackageFingerprint,
    string PackageFingerprint,
    string PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int DiagnosticCount,
    bool MicrosoftSchemaValid,
    bool MicrosoftSchemaNoNewErrors,
    IReadOnlyList<string> ExplicitAuthorizations,
    bool ExternalTargetsReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);
