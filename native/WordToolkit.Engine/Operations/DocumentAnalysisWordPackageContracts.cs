namespace WordToolkit.Engine.Operations;

public static class DocumentAnalysisWordPackageContract
{
    public const string OperationName = "analyze_ooxml_document";
    public const string Contract = "wordtoolkit.analyze_ooxml_document/1.0";
    public const int DefaultMaxSignals = 12;
    public const int MaximumMaxSignals = 32;
    public const int MaximumRequestJsonCharacters = 64 * 1024;
    public const int MaximumLocalPathCharacters = 32_767;
}

public sealed record DocumentAnalysisRequest(
    string LocalPath,
    string? ExpectedPackageFingerprint = null,
    int MaxSignals = DocumentAnalysisWordPackageContract.DefaultMaxSignals
);

public sealed record DocumentAnalysisCount(string Kind, int Count);

public sealed record DocumentAnalysisPackageSummary(
    long Bytes,
    int EntryCount,
    int PartCount,
    int RelationshipCount,
    int ExternalRelationshipCount,
    int UnreachablePartCount,
    int PackageDiagnosticCount,
    bool StructurallyValid
);

public sealed record DocumentAnalysisSemanticSummary(
    int ProjectedPartCount,
    int SemanticNodeCount,
    int ProjectionWarningCount,
    IReadOnlyList<DocumentAnalysisCount> ObjectCounts
);

public sealed record DocumentAnalysisDependencySummary(
    int NodeCount,
    int EdgeCount,
    int IssueCount,
    int UnresolvedNodeCount,
    int UnresolvedEdgeCount,
    int ExternalNodeCount,
    int UnreachablePartNodeCount,
    IReadOnlyList<DocumentAnalysisCount> DiagnosticDomains
);

public sealed record DocumentAnalysisQualitySummary(
    int FindingCount,
    int InfoCount,
    int WarningCount,
    int ErrorCount,
    int FatalCount,
    int ImplementedFixCandidateCount,
    int ReviewRequiredCandidateCount,
    bool FindingPageTruncated,
    bool ExecutionComplete,
    bool DocumentCoverageComplete,
    IReadOnlyList<DocumentAnalysisCount> FindingCategories,
    IReadOnlyList<DocumentAnalysisOpportunity> Opportunities
);

public sealed record DocumentAnalysisOpportunity(
    string RepairKind,
    string Safety,
    bool Implemented,
    int CandidateCount
);

public sealed record DocumentAnalysisSafetySummary(
    int ExternalRelationshipCount,
    int ActiveContentDeclarationCount,
    int ActiveContentPayloadCount,
    int ActiveXControlCount,
    int UnresolvedActiveContentNodeCount,
    bool ActiveContentPresent,
    bool BinaryPayloadsDecoded,
    bool EmbeddedPackagesOpened,
    bool ExternalTargetsFollowed,
    bool CryptographicSignatureValidationPerformed
);

public sealed record DocumentAnalysisCompatibilitySummary(
    int ParsedXmlPartCount,
    int ParsedElementCount,
    int NamespaceCount,
    int RuleCount,
    int AlternateContentCount,
    int MustUnderstandMismatchCount,
    int IssueCount,
    bool IssuesTruncated,
    string ApplicationConfiguration
);

public enum DocumentAnalysisSignalSeverity
{
    Info,
    Warning,
    Error,
    Critical,
}

public sealed record DocumentAnalysisSignal(
    string Code,
    DocumentAnalysisSignalSeverity Severity,
    string Domain,
    int EvidenceCount,
    string NextAction,
    bool BlocksAutomaticMutation
);

public sealed record DocumentAnalysisCoverage(
    bool AnalysisExecutionComplete,
    bool DocumentCoverageComplete,
    bool SemanticCompletenessClaimed,
    bool OperationBudgetCoverageComplete,
    IReadOnlyList<string> ExplicitlyUnmodeledDomains,
    IReadOnlyList<string> Omissions
);

public sealed record DocumentAnalysisDisclosure(
    bool DocumentTextReturned,
    bool RawXmlReturned,
    bool SourceLocationsReturned,
    bool ExternalRelationshipTargetsReturned,
    bool ExternalRelationshipsFollowed,
    bool ActiveContentExecuted,
    bool MutationPerformed,
    bool WordOpened,
    bool DocumentContentIsUntrusted
);

public sealed record DocumentAnalysisOperationBudget(
    string Model,
    long Used,
    long Maximum
);

public sealed record DocumentAnalysisResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    string MainPartUri,
    DocumentAnalysisPackageSummary Package,
    DocumentAnalysisSemanticSummary Semantic,
    DocumentAnalysisDependencySummary Dependencies,
    DocumentAnalysisQualitySummary Quality,
    DocumentAnalysisSafetySummary Safety,
    DocumentAnalysisCompatibilitySummary Compatibility,
    int SignalCount,
    int ReturnedSignalCount,
    bool SignalsTruncated,
    IReadOnlyList<DocumentAnalysisSignal> Signals,
    DocumentAnalysisCoverage Coverage,
    DocumentAnalysisDisclosure Disclosure,
    DocumentAnalysisOperationBudget OperationBudget
);
