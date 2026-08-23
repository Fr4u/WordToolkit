using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

public static class StyleWordPackageContract
{
    public const string PlanOperationName = "plan_ooxml_semantic_edits";
    public const string ApplyOperationName = "apply_ooxml_semantic_edits";
    public const string PlanContract =
        "wordtoolkit.plan_ooxml_semantic_edits/1.0";
    public const string ApplyContract =
        "wordtoolkit.apply_ooxml_semantic_edits/1.0";
    public const int MaximumCommands = 200;
    public const int MaximumSelectorCommands = 16;
    public const int MaximumStyleTextCharacters = 253;
    public const int MaximumSelectorMatches = 200;
    public const int MaximumChangedParts = 200;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRequestJsonCharacters = 256 * 1_024;
}

public abstract record StyleEditCommand(string Type);

public sealed record CreateStyleEditCommand(
    string StyleId,
    string Name,
    WordStyleType StyleType,
    string? BasedOnStyleId = null,
    string? NextStyleId = null,
    bool? QuickFormat = null,
    int? UiPriority = null
) : StyleEditCommand("create_style");

public sealed record CloneStyleEditCommand(
    string SourceStyleId,
    string StyleId,
    string Name
) : StyleEditCommand("clone_style");

public sealed record ConsolidateStyleEditCommand(
    string SourceStyleId,
    string TargetStyleId
) : StyleEditCommand("consolidate_style");

public sealed record DeleteUnusedStyleEditCommand(
    string StyleId
) : StyleEditCommand("delete_unused_style");

public sealed record RenameStyleEditCommand(
    string StyleId,
    string Name
) : StyleEditCommand("rename_style");

public sealed record SetStyleEditCommand(
    string NodeId,
    string StyleId,
    string? ExpectedStyleId = null,
    bool RequireNoExplicitStyle = false
) : StyleEditCommand("set_style");

public sealed record SetStyleWhereEditCommand(
    StyleEditSelector Selector,
    string StyleId,
    int MaxMatches,
    string? ExpectedStyleId = null,
    bool RequireNoExplicitStyle = false
) : StyleEditCommand("set_style_where");

public sealed record StyleEditSelector
{
    public required WordSemanticNodeKind Kind { get; init; }

    public string? Text { get; init; }

    public WordSemanticTextMatchMode TextMatch { get; init; } =
        WordSemanticTextMatchMode.Contains;

    public WordSemanticTextScope TextScope { get; init; } =
        WordSemanticTextScope.Node;

    public bool CaseSensitive { get; init; }

    public IReadOnlyDictionary<string, string>? PropertyEquals { get; init; }

    public StyleEditRelatedPredicate? Ancestor { get; init; }

    public StyleEditRelatedPredicate? Descendant { get; init; }

    public string? WithinNodeId { get; init; }

    public string? SourcePartUri { get; init; }
}

public sealed record StyleEditRelatedPredicate
{
    public WordSemanticNodeKind? Kind { get; init; }

    public IReadOnlyDictionary<string, string>? PropertyEquals { get; init; }
}

public sealed record StyleEditPlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<StyleEditCommand> Commands,
    bool IncludeDetails = false
);

public sealed record StyleEditApplyRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    string ExpectedPlanId,
    IReadOnlyList<StyleEditCommand> Commands,
    bool KeepBackup = true,
    string? ProtectedEditAuthorization = null
);

public sealed record StyleEditPolicyBlockDetails(
    string PlanId,
    IReadOnlyList<string> BlockCodes
);

public sealed record StyleEditSelectorResolution(
    int CommandIndex,
    int MatchedNodeCount,
    int ScannedNodeCount,
    string CandidateSeed
);

public sealed record StyleEditOperationDetail(
    int Index,
    string Kind,
    string NodeId,
    string? PropertyName,
    string? BeforeValue,
    string? AfterValue,
    string SourcePartUri,
    int SourceElementOrdinal,
    int XmlByteDelta,
    bool HasChange
);

public sealed record StyleDefinitionOperationDetail(
    int Index,
    string Kind,
    string StyleId,
    string? SourceStyleId,
    string StyleType,
    string SourcePartUri,
    int SourceElementOrdinal,
    int ReferenceUpdateCount,
    int XmlByteDelta,
    bool HasChange
);

public sealed record StyleEditChangedPart(
    string PartUri,
    int BeforeBytes,
    int AfterBytes,
    long ByteDelta
);

public sealed record StyleEditPlanResult(
    string OperationContract,
    string FileName,
    string PlanId,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    int SubmittedCommandCount,
    int SelectorCommandCount,
    int SelectorMatchCount,
    int StyleDefinitionCount,
    int StyleConsolidationCount,
    int StyleDeletionCount,
    int StyleRenameCount,
    int StyleReferenceUpdateCount,
    int StyleAssignmentCount,
    int OperationCount,
    int ChangedOperationCount,
    int ChangedPartCount,
    long TotalXmlByteDelta,
    bool HasChanges,
    bool CanApply,
    bool ApplyBlocked,
    IReadOnlyList<string> ApplyBlockedReasons,
    WordPackageCandidateValidationReport CandidateValidation,
    IReadOnlyList<StyleEditOperationDetail>? Operations,
    IReadOnlyList<StyleDefinitionOperationDetail>? StyleDefinitionOperations,
    IReadOnlyList<StyleEditChangedPart>? ChangedParts,
    IReadOnlyList<StyleEditSelectorResolution>? SelectorResolutions,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened,
    WordPackageProtectionRiskAssessment Protection,
    string? ProtectionAuthorizationId,
    IReadOnlyList<string> RequiredAuthorizations
);

public sealed record StyleEditApplyResult(
    string OperationContract,
    string FileName,
    string PlanId,
    bool Applied,
    bool NoOp,
    int? OperationCount,
    string? PreviousPackageFingerprint,
    string PackageFingerprint,
    string? PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int? DiagnosticCount,
    bool MicrosoftSchemaValid,
    bool MicrosoftSchemaNoNewErrors,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened,
    IReadOnlyList<string> ExplicitAuthorizations
);
