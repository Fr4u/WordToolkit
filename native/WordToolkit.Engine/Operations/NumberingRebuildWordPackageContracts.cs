using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

public static class NumberingRebuildWordPackageContract
{
    public const string InspectOperationName = "inspect_ooxml_numbering_rebuild_candidates";
    public const string PlanOperationName = "plan_ooxml_numbering_rebuild";
    public const string ApplyOperationName = "apply_ooxml_numbering_rebuild";
    public const string InspectContract =
        "wordtoolkit.inspect_ooxml_numbering_rebuild_candidates/1.0";
    public const string PlanContract = "wordtoolkit.plan_ooxml_numbering_rebuild/1.0";
    public const string ApplyContract = "wordtoolkit.apply_ooxml_numbering_rebuild/1.0";
    public const int MaximumCommands = 32;
    public const int MaximumTargets = 10_000;
    public const int MaximumInspectionItems = 100;
    public const int MaximumChangedEntries = 64;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 4 * 1024 * 1024;
}

public sealed record NumberingRebuildInspectRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<string> ParagraphNodeIds
);

public sealed record NumberingRebuildPlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<WordNumberingRebuildCommand> Commands,
    bool IncludeDetails = false
);

public sealed record NumberingRebuildApplyRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string ExpectedPlanId,
    IReadOnlyList<WordNumberingRebuildCommand> Commands,
    bool KeepBackup = true,
    string? ProtectedEditAuthorization = null
);

public sealed record NumberingRebuildEditPolicyBlockDetails(string PlanId, IReadOnlyList<string> BlockCodes);

public sealed record NumberingRebuildCandidateDetail(
    string ParagraphNodeId,
    string CandidateFingerprint,
    string StoryKind,
    string SourcePartUri,
    string SourcePath,
    int SourceOrder,
    int? CurrentNumberId,
    int? CurrentLevelIndex,
    bool CanRebuild,
    IReadOnlyList<string> BlockedReasons
);

public sealed record NumberingRebuildInspectResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    int CandidateCount,
    IReadOnlyList<NumberingRebuildCandidateDetail> Candidates,
    bool ParagraphTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record NumberingRebuildTargetDetail(
    string ParagraphNodeId,
    string CandidateFingerprint,
    string StoryKind,
    string SourcePartUri,
    string SourcePath,
    int SourceOrder,
    int LevelIndex,
    int? PreviousNumberId,
    int? PreviousLevelIndex,
    long? CounterValue,
    string CounterStatus,
    string? Label,
    string LabelStatus,
    bool DirectNumberingMaterialized
);

public sealed record NumberingRebuildCommandDetail(
    string CommandId,
    int AbstractNumberId,
    int NumberId,
    string NamespaceId,
    string TemplateCode,
    string MultiLevelKind,
    bool RestartAfterSectionBreak,
    int LevelCount,
    int TargetCount,
    IReadOnlyList<NumberingRebuildTargetDetail>? Targets
);

public sealed record NumberingRebuildChangedEntryDetail(
    string EntryName,
    string? PartUri,
    string ChangeKind,
    string? BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes,
    long ByteDelta
);

public sealed record NumberingRebuildPlanResult(
    string OperationContract,
    string FileName,
    string RebuildKind,
    string PlanId,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    string NumberingPartUri,
    bool NumberingPartCreated,
    int CommandCount,
    int TargetCount,
    int ChangedEntryCount,
    long TotalXmlByteDelta,
    bool HasChanges,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordNumberingRebuildValidation EngineValidation,
    WordPackageCandidateValidationReport CandidateValidation,
    IReadOnlyList<string> CompatibilityRules,
    IReadOnlyList<NumberingRebuildCommandDetail> Commands,
    IReadOnlyList<NumberingRebuildChangedEntryDetail>? ChangedEntries,
    bool DetailsTruncated,
    bool ParagraphTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened,
    WordPackageProtectionRiskAssessment Protection,
    string? ProtectionAuthorizationId,
    IReadOnlyList<string> RequiredAuthorizations
);

public sealed record NumberingRebuildApplyResult(
    string OperationContract,
    string FileName,
    string RebuildKind,
    string PlanId,
    bool Applied,
    bool NoOp,
    int CommandCount,
    int TargetCount,
    string PreviousPackageFingerprint,
    string PackageFingerprint,
    string PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int DiagnosticCount,
    bool MicrosoftSchemaValid,
    bool MicrosoftSchemaNoNewErrors,
    bool ParagraphTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened,
    IReadOnlyList<string> ExplicitAuthorizations
);
