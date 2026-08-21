using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class MailMergeSchemaPlanWordPackageContract
{
    public const string OperationName = "plan_ooxml_mail_merge_schema_binding";
    public const string Contract = "wordtoolkit.plan_ooxml_mail_merge_schema_binding/1.0";
    public const int MaximumRequestJsonCharacters = 4 * 1_024 * 1_024;
    public const int MaximumLocalPathCharacters = 32_767;
}

public sealed record MailMergeSchemaPlanRequest(
    string LocalPath,
    string ExpectedPackageFingerprint,
    IReadOnlyList<WordMailMergeSourceColumn> SourceColumns
);

public sealed record MailMergeSchemaPlanDisclosure(
    bool RecordValuesAccepted,
    bool RecordValuesReturned,
    bool WordOpened,
    bool MailMergeExecuted,
    bool DataSourcesOpened,
    bool QueriesExecuted,
    bool ExternalTargetsFollowed,
    bool MutationPerformed,
    bool DocumentContentIsUntrusted
);

public sealed record MailMergeSchemaPlanResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    string SourceSchemaFingerprint,
    string PlanId,
    string? ConfigurationId,
    string? MainDocumentType,
    string? Destination,
    IReadOnlyList<WordMailMergeSourceColumn> SourceColumns,
    IReadOnlyList<WordMailMergeSchemaBinding> Bindings,
    IReadOnlyList<WordMailMergeSchemaPlanIssue> Issues,
    IReadOnlyList<string> SchemaBlockedReasons,
    IReadOnlyList<string> ExecutionBlockedReasons,
    int UnusedSourceColumnCount,
    bool CanBindSchema,
    bool ExecutionSupported,
    bool ExternalSourceIgnored,
    bool SensitiveConnectionMetadataIgnored,
    bool ContainsRecordValues,
    MailMergeSchemaPlanDisclosure Disclosure,
    WordOperationResourceUsage OperationBudget
);
