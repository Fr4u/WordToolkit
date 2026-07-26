using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

public static class EquationParagraphRewriteWordPackageContract
{
    public const string InspectOperationName =
        "inspect_ooxml_equation_paragraph_rewrites";
    public const string PlanOperationName =
        "plan_ooxml_equation_paragraph_rewrites";
    public const string ApplyOperationName =
        "apply_ooxml_equation_paragraph_rewrites";
    public const string InspectContract =
        "wordtoolkit.inspect_ooxml_equation_paragraph_rewrites/1.0";
    public const string PlanContract =
        "wordtoolkit.plan_ooxml_equation_paragraph_rewrites/1.0";
    public const string ApplyContract =
        "wordtoolkit.apply_ooxml_equation_paragraph_rewrites/1.0";
    public const int MaximumCommands = 64;
    public const int MaximumTextSlotsPerCommand = 129;
    public const int MaximumTextCharactersPerSlot = 1_000_000;
    public const int MaximumTotalReplacementCharacters = 4 * 1024 * 1024;
    public const int MaximumTextNodeOperations = 4_096;
    public const int MaximumChangedParts = 16;
    public const int MaximumInspectItems = 100;
    public const int MaximumInspectTextCharacters = 64 * 1024;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 8 * 1024 * 1024;
}

public sealed record EquationParagraphRewriteInspectRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string? ParagraphNodeId = null,
    int Offset = 0,
    int MaxItems = 25,
    bool IncludeText = false
);

public sealed record RewriteEquationParagraphTextCommand(
    string CandidateId,
    string ExpectedCandidateFingerprint,
    IReadOnlyList<string> ReplacementTextSlots
);

public sealed record EquationParagraphRewritePlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<RewriteEquationParagraphTextCommand> Commands,
    bool IncludeDetails = false
);

public sealed record EquationParagraphRewriteApplyRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string ExpectedPlanId,
    IReadOnlyList<RewriteEquationParagraphTextCommand> Commands,
    bool KeepBackup = true
);

public sealed record EquationParagraphRewriteSlotInspection(
    int SlotIndex,
    int CharacterCount,
    int TextNodeCount,
    string TextSha256,
    bool CanRewrite,
    string? Text
);

public sealed record EquationParagraphRewriteCandidateInspection(
    string CandidateId,
    string CandidateFingerprint,
    string ParagraphNodeId,
    string StoryKind,
    int EquationAnchorCount,
    int InlineEquationAnchorCount,
    int DisplayEquationAnchorCount,
    int TextSlotCount,
    int EditableTextSlotCount,
    int TextNodeCount,
    int TextCharacterCount,
    bool CanRewrite,
    IReadOnlyList<string> BlockedReasons,
    IReadOnlyList<EquationParagraphRewriteSlotInspection>? TextSlots
);

public sealed record EquationParagraphRewriteInspectResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    int TotalCandidateCount,
    int RewritableCandidateCount,
    int Offset,
    int ReturnedCount,
    int? NextOffset,
    IReadOnlyList<EquationParagraphRewriteCandidateInspection> Candidates,
    bool TextIncluded,
    int ReturnedTextCharacters,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record EquationParagraphRewriteDetail(
    int CommandIndex,
    string CandidateId,
    string ParagraphNodeId,
    string StoryKind,
    int EquationAnchorCount,
    int TextSlotCount,
    int ChangedTextSlotCount,
    int TextNodeOperationCount,
    int BeforeCharacters,
    int AfterCharacters,
    string BeforeTextSlotsSha256,
    string AfterTextSlotsSha256
);

public sealed record EquationParagraphRewriteChangedPart(
    string PartUri,
    int BeforeBytes,
    int AfterBytes,
    long ByteDelta
);

public sealed record EquationParagraphRewritePlanResult(
    string OperationContract,
    string FileName,
    string PlanId,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    int SubmittedCommandCount,
    int ParagraphCount,
    int EquationAnchorCount,
    int TextSlotCount,
    int ChangedTextSlotCount,
    int TextNodeOperationCount,
    int ChangedTextNodeOperationCount,
    int ChangedPartCount,
    long TotalXmlByteDelta,
    bool HasChanges,
    bool ExactEquationBytesPreserved,
    bool ParagraphStructurePreserved,
    bool ExactInverseVerified,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordPackageCandidateValidationReport CandidateValidation,
    IReadOnlyList<EquationParagraphRewriteDetail>? ParagraphRewrites,
    IReadOnlyList<EquationParagraphRewriteChangedPart>? ChangedParts,
    bool RawTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record EquationParagraphRewriteApplyResult(
    string OperationContract,
    string FileName,
    string PlanId,
    bool Applied,
    bool NoOp,
    int ParagraphCount,
    int EquationAnchorCount,
    int TextNodeOperationCount,
    string PreviousPackageFingerprint,
    string PackageFingerprint,
    string PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int DiagnosticCount,
    bool MicrosoftSchemaValid,
    bool MicrosoftSchemaNoNewErrors,
    bool ExactEquationBytesPreserved,
    bool ParagraphStructurePreserved,
    bool ExactInverseVerified,
    bool RawTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);
