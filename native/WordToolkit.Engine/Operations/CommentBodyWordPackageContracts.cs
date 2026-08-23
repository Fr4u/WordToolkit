using WordToolkit.Engine.Validation;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class CommentBodyWordPackageContract
{
    public const string PlanOperationName = "plan_ooxml_comment_body_edits";
    public const string ApplyOperationName = "apply_ooxml_comment_body_edits";
    public const string PlanContract =
        "wordtoolkit.plan_ooxml_comment_body_edits/1.0";
    public const string ApplyContract =
        "wordtoolkit.apply_ooxml_comment_body_edits/1.0";
    public const int MaximumCommands = 200;
    public const int MaximumTextNodeOperations = 2_000;
    public const int MaximumTextCharactersPerField = 64 * 1024;
    public const int MaximumTotalReplacementCharacters = 4 * 1024 * 1024;
    public const int MaximumChangedParts = 8;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 8 * 1024 * 1024;
}

public sealed record ReplaceCommentBodyTextCommand(
    string CommentId,
    string FindText,
    string ReplacementText,
    int ExpectedMatchCount = 1,
    bool CaseSensitive = true,
    string? ExpectedBodySha256 = null
);

public sealed record CommentBodyEditPlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<ReplaceCommentBodyTextCommand> Commands,
    bool IncludeDetails = false
);

public sealed record CommentBodyEditApplyRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string ExpectedPlanId,
    IReadOnlyList<ReplaceCommentBodyTextCommand> Commands,
    bool KeepBackup = true,
    string? ProtectedEditAuthorization = null
);

public sealed record CommentBodyEditPolicyBlockDetails(
    string PlanId,
    IReadOnlyList<string> BlockCodes
);

public sealed record CommentBodyEditDetail(
    int CommandIndex,
    string CommentId,
    int MatchedOccurrenceCount,
    int TextNodeCount,
    int ChangedTextNodeCount,
    int BeforeCharacters,
    int AfterCharacters,
    string BeforeBodySha256,
    string AfterBodySha256,
    string SourcePartUri
);

public sealed record CommentBodyEditChangedPart(
    string PartUri,
    int BeforeBytes,
    int AfterBytes,
    long ByteDelta
);

public sealed record CommentBodyEditPlanResult(
    string OperationContract,
    string FileName,
    string PlanId,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    int SubmittedCommandCount,
    int CommentCount,
    int MatchedOccurrenceCount,
    int TextNodeOperationCount,
    int ChangedTextNodeOperationCount,
    int ChangedPartCount,
    long TotalXmlByteDelta,
    bool HasChanges,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordPackageProtectionRiskAssessment Protection,
    string? ProtectionAuthorizationId,
    IReadOnlyList<string> RequiredAuthorizations,
    WordPackageCandidateValidationReport CandidateValidation,
    IReadOnlyList<CommentBodyEditDetail>? CommentEdits,
    IReadOnlyList<CommentBodyEditChangedPart>? ChangedParts,
    bool RawTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record CommentBodyEditApplyResult(
    string OperationContract,
    string FileName,
    string PlanId,
    bool Applied,
    bool NoOp,
    int? CommentCount,
    int? TextNodeOperationCount,
    string? PreviousPackageFingerprint,
    string PackageFingerprint,
    string? PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int? DiagnosticCount,
    bool MicrosoftSchemaValid,
    bool MicrosoftSchemaNoNewErrors,
    IReadOnlyList<string> ExplicitAuthorizations,
    bool RawTextReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);
