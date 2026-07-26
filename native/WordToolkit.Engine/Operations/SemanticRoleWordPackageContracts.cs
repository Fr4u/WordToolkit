using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class SemanticRoleWordPackageContract
{
    public const string OperationName = "inspect_ooxml_semantic_roles";
    public const string Contract = "wordtoolkit.inspect_ooxml_semantic_roles/1.0";
    public const int DefaultMaxItems = 30;
    public const int MaximumMaxItems = 100;
    public const int MaximumPreviewCharacters = 512;
    public const int MaximumReturnedIssues = 100;
    public const int MaximumRequestJsonCharacters = 64 * 1024;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumRoles = 10;
}

public sealed record SemanticRoleInspectionRequest(
    string LocalPath,
    string View,
    string StoryKind,
    string? ExpectedPackageFingerprint,
    IReadOnlyList<WordSemanticRoleKind> Roles,
    string MinimumEvidence,
    bool UsableOnly,
    string? CandidateId,
    string? ParagraphNodeId,
    string? Classification,
    int Offset,
    int MaxItems,
    bool IncludeEvidence,
    bool IncludeStyles,
    bool IncludeDeclarations,
    bool IncludeHashes,
    bool IncludeSource,
    bool IncludeSensitive,
    int TextPreviewCharacters
)
{
    public static SemanticRoleInspectionRequest Default(string localPath) => new(
        localPath,
        View: "candidates",
        StoryKind: "main",
        ExpectedPackageFingerprint: null,
        Roles: [WordSemanticRoleKind.Theorem],
        MinimumEvidence: "any",
        UsableOnly: true,
        CandidateId: null,
        ParagraphNodeId: null,
        Classification: null,
        Offset: 0,
        MaxItems: SemanticRoleWordPackageContract.DefaultMaxItems,
        IncludeEvidence: false,
        IncludeStyles: false,
        IncludeDeclarations: false,
        IncludeHashes: false,
        IncludeSource: false,
        IncludeSensitive: false,
        TextPreviewCharacters: 0
    );
}

public sealed record SemanticRoleInspectionEvidence(
    string EvidenceId,
    string Kind,
    string Role,
    bool AuthorDeclared,
    string? ContentControlId,
    string? StyleId,
    string? ValueFingerprint
);

public sealed record SemanticRoleInspectionItem(
    string CandidateId,
    string CandidateFingerprint,
    string ParagraphNodeId,
    string? Role,
    string Classification,
    string StoryKind,
    int SourceOrder,
    int? ParagraphCharacterCount,
    int? LabelCharacterCount,
    string? ParagraphTextFingerprint,
    bool ViewAmbiguous,
    bool UsableAsSemanticRole,
    int EvidenceCount,
    IReadOnlyList<SemanticRoleInspectionEvidence>? Evidence,
    string? TextPreview,
    bool TextPreviewTruncated,
    string? SourcePartUri,
    int? SourceElementOrdinal
);

public sealed record SemanticRoleInspectionIssue(
    string Code,
    string Severity,
    string Message,
    string? ParagraphNodeId,
    string? StoryKind,
    int? SourceOrder,
    string? CandidateId
);

public sealed record SemanticRoleInspectionSummary(
    string Role,
    int CandidateCount,
    int UsableCandidateCount,
    int DeclaredCount,
    int StyleConventionCount,
    int LexicalCandidateCount,
    int ConflictEvidenceCount
);

public sealed record SemanticRoleInspectionDisclosure(
    bool TextReturned,
    bool EvidenceReturned,
    bool StylesReturned,
    bool DeclarationsReturned,
    bool HashesReturned,
    bool SourceReturned,
    bool RawXmlReturned,
    bool CustomXmlValuesReturned,
    bool ExternalRelationshipsFollowed,
    bool MutationPerformed,
    bool WordOpened,
    bool DocumentContentIsUntrusted
);

public sealed record SemanticRoleInspectionResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    string MainPartUri,
    string Profile,
    string View,
    string StoryKind,
    IReadOnlyList<string> RequestedRoles,
    string MinimumEvidence,
    bool UsableOnly,
    int ExaminedParagraphCount,
    int EligibleParagraphCount,
    int AmbiguousParagraphCount,
    int CandidateCount,
    int UsableCandidateCount,
    int ConflictCount,
    int IssueCount,
    bool AnalysisExecutionComplete,
    bool SemanticRoleCoverageComplete,
    bool SemanticCompletenessClaimed,
    bool StylesWithEffectsPresent,
    IReadOnlyList<string> CoverageOmissions,
    IReadOnlyList<SemanticRoleInspectionSummary> RoleSummaries,
    int MatchedCandidateCount,
    int Offset,
    int ReturnedItemCount,
    int? NextOffset,
    IReadOnlyList<SemanticRoleInspectionItem> Items,
    int MatchedIssueCount,
    int ReturnedIssueCount,
    bool IssuePageTruncated,
    IReadOnlyList<SemanticRoleInspectionIssue> Issues,
    SemanticRoleInspectionDisclosure Disclosure
);
