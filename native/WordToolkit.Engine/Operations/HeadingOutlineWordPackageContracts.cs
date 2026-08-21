using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class HeadingOutlineWordPackageContract
{
    public const string OperationName = "inspect_ooxml_heading_outline";
    public const string Contract = "wordtoolkit.inspect_ooxml_heading_outline/1.0";
    public const int DefaultMaxItems = 30;
    public const int MaximumMaxItems = 100;
    public const int MaximumPreviewCharacters = 512;
    public const int MaximumReturnedIssues = 100;
    public const int MaximumRequestJsonCharacters = 64 * 1024;
    public const int MaximumLocalPathCharacters = 32_767;
}

public sealed record HeadingOutlineInspectionRequest(
    string LocalPath,
    string View = "headings",
    string StoryKind = "main",
    string? ExpectedPackageFingerprint = null,
    string? ParagraphNodeId = null,
    int? MinimumLevel = null,
    int? MaximumLevel = null,
    bool HierarchyOnly = true,
    int Offset = 0,
    int MaxItems = HeadingOutlineWordPackageContract.DefaultMaxItems,
    bool IncludeStyles = false,
    bool IncludeSource = false,
    bool IncludeSensitive = false,
    int TextPreviewCharacters = 0
);

public sealed record HeadingOutlineInspectionItem(
    string ParagraphNodeId,
    string? ParentHeadingParagraphNodeId,
    string? PreviousHeadingParagraphNodeId,
    string? NextHeadingParagraphNodeId,
    int HeadingIndex,
    int Level,
    string LevelSource,
    string StoryKind,
    int SourceOrder,
    int ChildHeadingCount,
    int DescendantHeadingCount,
    int? TitleCharacterCount,
    bool TitleIsEmpty,
    bool HierarchyEligible,
    bool ViewAmbiguous,
    string? ParagraphStyleId,
    string? LevelSourceStyleId,
    string? TextPreview,
    bool TextPreviewTruncated,
    string? SourcePartUri,
    int? SourceElementOrdinal
);

public sealed record HeadingOutlineInspectionIssue(
    string Code,
    string Severity,
    string Message,
    string? ParagraphNodeId,
    string? StoryKind,
    int? Level,
    int? PreviousLevel,
    string? SourcePartUri,
    int? SourceElementOrdinal
);

public sealed record HeadingOutlineInspectionDisclosure(
    bool TextReturned,
    bool StylesReturned,
    bool SourceReturned,
    bool RawXmlReturned,
    bool ExternalRelationshipsFollowed,
    bool MutationPerformed,
    bool WordOpened,
    bool DocumentContentIsUntrusted
);

public sealed record HeadingOutlineInspectionResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    string MainPartUri,
    string View,
    string StoryKind,
    int ExaminedParagraphCount,
    int ResolvedParagraphCount,
    int BodyTextParagraphCount,
    int UnresolvedParagraphCount,
    int HeadingCount,
    int HierarchyHeadingCount,
    int RootHeadingCount,
    int SkippedHeadingCount,
    int StoryCount,
    int IssueCount,
    bool AnalysisExecutionComplete,
    bool OutlineCoverageComplete,
    bool StylesWithEffectsPresent,
    IReadOnlyList<string> CoverageOmissions,
    int MatchedHeadingCount,
    int Offset,
    int ReturnedItemCount,
    int? NextOffset,
    IReadOnlyList<HeadingOutlineInspectionItem> Items,
    int MatchedIssueCount,
    int ReturnedIssueCount,
    bool IssuePageTruncated,
    IReadOnlyList<HeadingOutlineInspectionIssue> Issues,
    HeadingOutlineInspectionDisclosure Disclosure
);
