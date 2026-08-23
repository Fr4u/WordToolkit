namespace WordToolkit.Engine.Operations;

public static class PatchRollbackWordPackageContract
{
    public const string PlanOperationName = "plan_ooxml_patch_rollback";
    public const string ApplyOperationName = "apply_ooxml_patch_rollback";
    public const string PlanContract =
        "wordtoolkit.plan_ooxml_patch_rollback/1.0";
    public const string ApplyContract =
        "wordtoolkit.apply_ooxml_patch_rollback/1.0";
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumPatchIdCharacters = 96;
    public const int MaximumPageItems = 200;
    public const int MaximumRequestJsonCharacters = 1 * 1024 * 1024;
}

public enum PatchRollbackView
{
    Summary,
    Operations,
    Risks,
    SchemaErrors,
}

public sealed record PatchRollbackPlanRequest(
    string LocalPath,
    string PatchPath,
    string ExpectedPackageFingerprint,
    string ExpectedPatchId,
    PatchRollbackView View = PatchRollbackView.Summary,
    int Offset = 0,
    int MaxItems = 50,
    bool IncludeHashes = false
);

public sealed record PatchRollbackApplyRequest(
    string LocalPath,
    string PatchPath,
    string ExpectedPackageFingerprint,
    string ExpectedPatchId,
    string ExpectedRollbackPlanId,
    bool AllowDigitalSignatureInvalidation = false,
    bool AllowActiveContentChanges = false,
    bool AllowExternalRelationshipChanges = false,
    bool AllowOpaqueBinaryChanges = false,
    bool AllowNewStructuralErrors = false,
    bool KeepBackup = true,
    string? ProtectedEditAuthorization = null
);

public sealed record PatchRollbackChangeCounts(
    int Added,
    int Removed,
    int Moved,
    int TextChanged,
    int PropertiesChanged,
    int StructureChanged,
    int UnmodeledMarkupChanged
);

public sealed record PatchRollbackSemanticSummary(
    string DiffId,
    bool PackageEquivalent,
    bool SemanticallyEquivalent,
    bool MatchingComplete,
    int PackageEntryDifferenceCount,
    int SemanticDifferenceCount,
    int UnclassifiedProjectedEntryCount,
    PatchRollbackChangeCounts ChangeCounts
);

public sealed record PatchRollbackRiskSummary(
    int ItemCount,
    int BlockItemCount,
    int ReviewItemCount,
    bool DigitalSignaturesPresent,
    bool DigitalSignatureMaterialChanged,
    int MacroOperationCount,
    int EmbeddedObjectOperationCount,
    int ActivexOperationCount,
    int ExternalRelationshipAddedCount,
    int ExternalRelationshipRemovedCount,
    int OpaqueBinaryOperationCount,
    int CustomXmlOperationCount,
    int InfrastructureOperationCount,
    int BaselineStructuralErrorCount,
    int CandidateStructuralErrorCount,
    int NewStructuralErrorCount,
    PatchRollbackProtectionRiskSummary Protection
);

public sealed record PatchRollbackProtectionRiskSummary(
    bool BaseDocumentProtectionEnforced,
    string? BaseDocumentProtectionEditMode,
    bool ResultDocumentProtectionEnforced,
    string? ResultDocumentProtectionEditMode,
    bool DocumentProtectionMetadataChanged,
    bool UnmodeledDocumentProtectionMetadata,
    int BasePermissionRangeCount,
    int ResultPermissionRangeCount,
    int MalformedPermissionRangeCount,
    bool PermissionIssuesTruncated,
    IReadOnlyList<string> PermissionIssueCodes,
    bool AuthorizationRequired
);

public sealed record PatchRollbackSchemaValidationSummary(
    bool Performed,
    bool CandidateValid,
    bool NoNewErrors,
    int NewErrorCount,
    int BaselineErrorCount,
    int CandidateErrorCount,
    bool ErrorsTruncated,
    string? NotPerformedReason
);

public sealed record PatchRollbackDefaultPolicy(
    bool CanRollback,
    IReadOnlyList<string> BlockCodes
);

/// <summary>
/// One bounded item from the selected rollback-plan view. Null fields belong to other
/// views and are omitted by the shared operation JSON serializer.
/// </summary>
public sealed record PatchRollbackPageItem(
    string? OperationId = null,
    string? Kind = null,
    string? EntryName = null,
    string? PartUri = null,
    string? BeforeContentType = null,
    string? AfterContentType = null,
    long? BeforeBytes = null,
    long? AfterBytes = null,
    string? BeforeSha256 = null,
    string? AfterSha256 = null,
    bool? IsInfrastructure = null,
    string? Code = null,
    string? Severity = null,
    string? Message = null,
    int? AffectedOperationCount = null,
    string? Id = null,
    string? ErrorType = null,
    string? Path = null,
    string? Node = null
);

public sealed record PatchRollbackPlanResult(
    string OperationContract,
    string FileName,
    string PatchFileName,
    string SourcePatchId,
    string ReversePatchId,
    string RollbackPlanId,
    string CurrentPackageFingerprint,
    string RestoredPackageFingerprint,
    int OperationCount,
    bool NoOp,
    PatchRollbackSemanticSummary Semantic,
    PatchRollbackRiskSummary Risk,
    PatchRollbackSchemaValidationSummary OpenxmlSchemaValidation,
    PatchRollbackDefaultPolicy DefaultPolicy,
    IReadOnlyList<string> HardBlockCodes,
    IReadOnlyList<string> RequiredAuthorizations,
    string? ProtectionAuthorizationId,
    PatchRollbackView View,
    int FilteredItemCount,
    int Offset,
    int ReturnedItemCount,
    int? NextOffset,
    IReadOnlyList<PatchRollbackPageItem> Items,
    bool HashesIncluded,
    bool RawPayloadsReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record PatchRollbackApplyResult(
    string OperationContract,
    string FileName,
    string PatchFileName,
    string SourcePatchId,
    string ReversePatchId,
    string RollbackPlanId,
    bool RolledBack,
    bool NoOp,
    string? PreviousPackageFingerprint,
    string PackageFingerprint,
    string? PredictedPackageFingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    int? DiagnosticCount,
    bool? DigitalSignaturesMayBeInvalidated,
    IReadOnlyList<string> ExplicitAuthorizations,
    bool RawPayloadsReturned,
    bool RawXmlReturned,
    bool MutationPerformed,
    bool WordOpened
);

public sealed record PatchRollbackPolicyBlockDetails(
    string SourcePatchId,
    string ReversePatchId,
    string RollbackPlanId,
    IReadOnlyList<string> BlockCodes
);
