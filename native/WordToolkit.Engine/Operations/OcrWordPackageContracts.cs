using WordToolkit.Engine.Extensions;

namespace WordToolkit.Engine.Operations;

public static class OcrWordPackageContract
{
    public const string InspectOperationName = "inspect_ooxml_ocr_candidates";
    public const string RecognizeOperationName = "run_ooxml_ocr";
    public const string InspectContract = "wordtoolkit.inspect_ooxml_ocr_candidates/1.0";
    public const string RecognizeContract = "wordtoolkit.run_ooxml_ocr/1.0";
    public const string DefaultProviderCapabilityId = "wordtoolkit.ocr.tesseract.cli";
    public const int DefaultMaxItems = 30;
    public const int MaximumMaxItems = 100;
    public const int MaximumSelectedCandidates = 8;
    public const int DefaultTimeoutMilliseconds = 30_000;
    public const int MaximumTimeoutMilliseconds = 120_000;
    public const int DefaultProviderOutputCharacters = 1_000_000;
    public const int MaximumProviderOutputCharacters = 4_000_000;
    public const int DefaultReturnedTextCharacters = 32_768;
    public const int MaximumReturnedTextCharacters = 131_072;
    public const int DefaultReturnedLines = 50;
    public const int MaximumReturnedLines = 200;
    public const int DefaultReturnedWordsPerLine = 50;
    public const int MaximumReturnedWordsPerLine = 200;
    public const int MaximumRequestJsonCharacters = 64 * 1024;
    public const int MaximumLocalPathCharacters = 32_767;
}

public sealed record OcrCandidateInspectionRequest(
    string LocalPath,
    string View = "candidates",
    string? ExpectedPackageFingerprint = null,
    string? CandidateId = null,
    int Offset = 0,
    int MaxItems = OcrWordPackageContract.DefaultMaxItems,
    bool IncludeHashes = false,
    bool IncludeSource = false
);

public sealed record OcrCandidateInspectionItem(
    string CandidateId,
    string? DeclaredContentType,
    string? DetectedContentType,
    string MediaFamily,
    long ByteLength,
    bool SignatureValid,
    bool Eligible,
    string? RejectionCode,
    int FigureCount,
    int ResourceCount,
    IReadOnlyList<string> StoryKinds,
    string? ImageSha256,
    string? SourcePartUri
);

public sealed record OcrCandidateInspectionIssue(
    string Code,
    string Severity,
    string Message,
    string? CandidateId,
    string? SourcePartUri
);

public sealed record OcrCandidateInspectionDisclosure(
    bool ImageBytesReturned,
    bool ImageHashesReturned,
    bool SourceReturned,
    bool ExternalRelationshipsFollowed,
    bool ProviderInvoked,
    bool NetworkUsed,
    bool MutationPerformed,
    bool WordOpened,
    bool DocumentContentIsUntrusted
);

public sealed record OcrCandidateInspectionResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    string View,
    int CandidateCount,
    int EligibleCandidateCount,
    int RasterCandidateCount,
    int VectorCandidateCount,
    int UnsupportedCandidateCount,
    int IssueCount,
    bool AnalysisExecutionComplete,
    bool CandidateCoverageComplete,
    int MatchedCandidateCount,
    int Offset,
    int ReturnedItemCount,
    int? NextOffset,
    IReadOnlyList<OcrCandidateInspectionItem> Items,
    int ReturnedIssueCount,
    bool IssuesTruncated,
    IReadOnlyList<OcrCandidateInspectionIssue> Issues,
    OcrCandidateInspectionDisclosure Disclosure
);

public sealed record OcrRecognitionRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<string> CandidateIds,
    bool SelectAllEligible = false,
    string ProviderCapabilityId = OcrWordPackageContract.DefaultProviderCapabilityId,
    string PrivacyMode = "local_only",
    IReadOnlyList<string>? Languages = null,
    WordOcrLayoutHint LayoutHint = WordOcrLayoutHint.Automatic,
    string? ProviderExecutablePath = null,
    string? ProviderModelDirectory = null,
    int TimeoutMilliseconds = OcrWordPackageContract.DefaultTimeoutMilliseconds,
    int ProviderOutputCharacters = OcrWordPackageContract.DefaultProviderOutputCharacters,
    string Detail = "summary",
    bool IncludeText = false,
    bool IncludeHashes = false,
    int MaxReturnedTextCharacters = OcrWordPackageContract.DefaultReturnedTextCharacters,
    int MaxReturnedLines = OcrWordPackageContract.DefaultReturnedLines,
    int MaxReturnedWordsPerLine = OcrWordPackageContract.DefaultReturnedWordsPerLine,
    double? MinimumMeanConfidence = null
);

public sealed record OcrRecognitionWord(
    string? Text,
    int TextCharacterCount,
    double? Confidence,
    OcrPixelBox Bounds
);

public sealed record OcrRecognitionLine(
    string? Text,
    int TextCharacterCount,
    bool TextTruncated,
    double? Confidence,
    OcrPixelBox Bounds,
    IReadOnlyList<OcrRecognitionWord> Words,
    int ReturnedWordCount,
    bool WordsTruncated
);

public sealed record OcrPixelBox(
    int Left,
    int Top,
    int Width,
    int Height
);

public sealed record OcrConfidenceSummary(
    string Scale,
    double? Minimum,
    double? Mean,
    double? Maximum,
    double? RequiredMinimumMean,
    bool MeetsRequiredMinimum
);

public sealed record OcrRecognitionProvenance(
    string ProviderCapabilityId,
    string ExtensionId,
    string ExtensionVersion,
    string ProviderName,
    string ProviderVersion,
    string ProviderBinarySha256,
    string ModelSetSha256,
    IReadOnlyList<string> EffectiveLanguages,
    string LayoutHint,
    string PrivacyMode,
    bool NetworkUsed,
    bool DeterministicForBoundInputs
);

public sealed record OcrRecognitionItem(
    string ResultId,
    string CandidateId,
    string? SourceImageSha256,
    int ImageWidthPixels,
    int ImageHeightPixels,
    int TextCharacterCount,
    string? Text,
    bool TextTruncated,
    string? TextSha256,
    int LineCount,
    int WordCount,
    int ReturnedLineCount,
    bool LinesTruncated,
    OcrConfidenceSummary Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<OcrRecognitionLine> Lines,
    OcrRecognitionProvenance Provenance
);

public sealed record OcrRecognitionDisclosure(
    bool SourceFingerprintVerified,
    bool SourceFileHashReverified,
    bool ImageBytesReturned,
    bool TextReturned,
    bool GeometryReturned,
    bool RawProviderOutputReturned,
    bool RawXmlReturned,
    bool ExternalRelationshipsFollowed,
    bool NetworkUsed,
    bool MutationPerformed,
    bool WordOpened,
    bool DocumentContentIsUntrusted
);

public sealed record OcrRecognitionResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    string Detail,
    int SelectedCandidateCount,
    int RecognizedCandidateCount,
    int TotalLineCount,
    int TotalWordCount,
    IReadOnlyList<OcrRecognitionItem> Results,
    OcrRecognitionDisclosure Disclosure
);
