using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

public static class NumberingRepairWordPackageContract
{
    public const string PlanOperationName = "plan_ooxml_numbering_repair";
    public const string ApplyOperationName = "apply_ooxml_numbering_repair";
    public const string PlanContract =
        "wordtoolkit.plan_ooxml_numbering_repair/1.0";
    public const string ApplyContract =
        "wordtoolkit.apply_ooxml_numbering_repair/1.0";
    public const int MaximumAffectedParagraphs = 10_000;
    public const int MaximumChangedParts = 16;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 64 * 1024;
}

public sealed record NumberingRepairPlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string TargetParagraphNodeId,
    int ExpectedNumberId,
    int ExpectedLevelIndex,
    int StartValue,
    bool IncludeDetails = false
);

public sealed record NumberingRepairApplyRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string ExpectedPlanId,
    string TargetParagraphNodeId,
    int ExpectedNumberId,
    int ExpectedLevelIndex,
    int StartValue,
    bool KeepBackup = true,
    string? ProtectedEditAuthorization = null
);

public sealed record NumberingRepairEditPolicyBlockDetails(
    string PlanId,
    IReadOnlyList<string> BlockCodes
);

public sealed record NumberingRepairParagraphDetail(
    string ParagraphNodeId,
    int LevelIndex,
    long? BeforeCounterValue,
    string BeforeCounterStatus,
    bool DirectNumberingMaterialized
);

public sealed record NumberingRepairChangedPart(
    string PartUri,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes,
    long ByteDelta
);

public sealed record NumberingRepairPlanResult(
    string OperationContract,
    string FileName,
    string RepairKind,
    string Scope,
    string PlanId,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    string TargetParagraphNodeId,
    string StoryKind,
    int SourceNumberId,
    int NewNumberId,
    int AbstractNumberId,
    int LevelIndex,
    int StartValue,
    long? TargetCounterBefore,
    string TargetCounterStatusBefore,
    long? TargetCounterAfter,
    string TargetCounterStatusAfter,
    int AffectedParagraphCount,
    bool AffectedParagraphDetailsTruncated,
    int DirectNumberingMaterializedCount,
    int ChangedPartCount,
    long TotalXmlByteDelta,
    bool HasChanges,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordNumberingSequenceRepairValidation EngineValidation,
    WordPackageCandidateValidationReport CandidateValidation,
    IReadOnlyList<string> CompatibilityRules,
    IReadOnlyList<NumberingRepairParagraphDetail>? AffectedParagraphs,
    IReadOnlyList<NumberingRepairChangedPart>? ChangedParts,
    bool ParagraphTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened,
    WordPackageProtectionRiskAssessment Protection,
    string? ProtectionAuthorizationId,
    IReadOnlyList<string> RequiredAuthorizations
);

public sealed record NumberingRepairApplyResult(
    string OperationContract,
    string FileName,
    string RepairKind,
    string Scope,
    string PlanId,
    bool Applied,
    bool NoOp,
    int? AffectedParagraphCount,
    int? SourceNumberId,
    int? NewNumberId,
    string? PreviousPackageFingerprint,
    string PackageFingerprint,
    string? PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int? DiagnosticCount,
    bool MicrosoftSchemaValid,
    bool MicrosoftSchemaNoNewErrors,
    bool ParagraphTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened,
    IReadOnlyList<string> ExplicitAuthorizations
);
