using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordToolkit.Engine.Semantics;

public enum WordMailMergeSourceDataKind
{
    Unspecified,
    Text,
    Number,
    DateTime,
    Boolean,
    Binary,
}

public enum WordMailMergeSchemaBindingStatus
{
    ResolvedExact,
    ResolvedCaseInsensitive,
    Missing,
    Ambiguous,
    NotApplicable,
}

public sealed record WordMailMergeSourceColumn(
    string Name,
    WordMailMergeSourceDataKind DataKind = WordMailMergeSourceDataKind.Unspecified
);

public sealed record WordMailMergeSchemaBinding(
    string FieldId,
    string FieldType,
    string? TargetName,
    string? MappingId,
    string? RequiredColumnName,
    int? SourceColumnOrdinal,
    string? SourceColumnName,
    WordMailMergeSourceDataKind? SourceDataKind,
    WordMailMergeSchemaBindingStatus Status,
    bool FieldComplete,
    bool FieldInDeletedContent,
    bool ExecutionBlocking
);

public sealed record WordMailMergeSchemaPlanIssue(
    string Code,
    WordMailMergeIssueSeverity Severity,
    string Message,
    string? FieldId = null,
    int? SourceColumnOrdinal = null
);

public sealed class WordMailMergeSchemaBindingPlan
{
    internal WordMailMergeSchemaBindingPlan(
        string packageFingerprint,
        string sourceSchemaFingerprint,
        string planId,
        string? configurationId,
        string? mainDocumentType,
        string? destination,
        IReadOnlyList<WordMailMergeSourceColumn> sourceColumns,
        IReadOnlyList<WordMailMergeSchemaBinding> bindings,
        IReadOnlyList<WordMailMergeSchemaPlanIssue> issues,
        IReadOnlyList<string> schemaBlockedReasons,
        IReadOnlyList<string> executionBlockedReasons,
        int unusedSourceColumnCount,
        bool externalSourceIgnored,
        bool sensitiveConnectionMetadataIgnored
    )
    {
        PackageFingerprint = packageFingerprint;
        SourceSchemaFingerprint = sourceSchemaFingerprint;
        PlanId = planId;
        ConfigurationId = configurationId;
        MainDocumentType = mainDocumentType;
        Destination = destination;
        SourceColumns = new ReadOnlyCollection<WordMailMergeSourceColumn>(
            sourceColumns.ToArray()
        );
        Bindings = new ReadOnlyCollection<WordMailMergeSchemaBinding>(bindings.ToArray());
        Issues = new ReadOnlyCollection<WordMailMergeSchemaPlanIssue>(issues.ToArray());
        SchemaBlockedReasons = new ReadOnlyCollection<string>(schemaBlockedReasons.ToArray());
        ExecutionBlockedReasons = new ReadOnlyCollection<string>(
            executionBlockedReasons.ToArray()
        );
        UnusedSourceColumnCount = unusedSourceColumnCount;
        ExternalSourceIgnored = externalSourceIgnored;
        SensitiveConnectionMetadataIgnored = sensitiveConnectionMetadataIgnored;
    }

    public string PackageFingerprint { get; }

    public string SourceSchemaFingerprint { get; }

    public string PlanId { get; }

    public string? ConfigurationId { get; }

    public string? MainDocumentType { get; }

    public string? Destination { get; }

    public IReadOnlyList<WordMailMergeSourceColumn> SourceColumns { get; }

    public IReadOnlyList<WordMailMergeSchemaBinding> Bindings { get; }

    public IReadOnlyList<WordMailMergeSchemaPlanIssue> Issues { get; }

    public IReadOnlyList<string> SchemaBlockedReasons { get; }

    public IReadOnlyList<string> ExecutionBlockedReasons { get; }

    public int UnusedSourceColumnCount { get; }

    public bool ExternalSourceIgnored { get; }

    public bool SensitiveConnectionMetadataIgnored { get; }

    public bool CanBindSchema => SchemaBlockedReasons.Count == 0;

    public bool ExecutionSupported => false;

    public bool ContainsRecordValues => false;
}

public sealed record WordMailMergeSchemaPlannerOptions
{
    public static WordMailMergeSchemaPlannerOptions Default { get; } = new();

    public int MaxSourceColumns { get; init; } = 4_096;

    public int MaxColumnNameCharacters { get; init; } = 512;

    public long MaxTotalColumnNameCharacters { get; init; } = 1_048_576;

    public int MaxBindings { get; init; } = 250_000;

    internal void Validate()
    {
        if (
            MaxSourceColumns <= 0
            || MaxColumnNameCharacters <= 0
            || MaxTotalColumnNameCharacters <= 0
            || MaxBindings <= 0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(WordMailMergeSchemaPlannerOptions),
                "All mail-merge schema-planner limits must be positive."
            );
        }
    }
}

public class WordMailMergeSchemaPlanException : Exception
{
    public WordMailMergeSchemaPlanException(string message)
        : base(message) { }

    public WordMailMergeSchemaPlanException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordMailMergeSchemaPlanLimitException
    : WordMailMergeSchemaPlanException
{
    public WordMailMergeSchemaPlanLimitException(string message)
        : base(message) { }
}

public sealed class WordMailMergeSchemaPlanner
{
    public const string Contract = "wordtoolkit.mail_merge_schema_binding_plan/1.0";

    private readonly WordMailMergeSchemaPlannerOptions _options;

    public WordMailMergeSchemaPlanner(WordMailMergeSchemaPlannerOptions? options = null)
    {
        _options = options ?? WordMailMergeSchemaPlannerOptions.Default;
        _options.Validate();
    }

    public WordMailMergeSchemaBindingPlan Plan(
        WordMailMergeGraph graph,
        IReadOnlyList<WordMailMergeSourceColumn> sourceColumns,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sourceColumns);
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceColumns.Count > _options.MaxSourceColumns)
        {
            throw new WordMailMergeSchemaPlanLimitException(
                $"Mail-merge source schema exceeds {_options.MaxSourceColumns} columns."
            );
        }
        if (graph.Fields.Count > _options.MaxBindings)
        {
            throw new WordMailMergeSchemaPlanLimitException(
                $"Mail-merge schema plan exceeds {_options.MaxBindings} field bindings."
            );
        }

        var columns = ValidateAndFreezeColumns(sourceColumns, cancellationToken);
        var schemaFingerprint = SourceSchemaFingerprint(columns);
        var exact = new Dictionary<string, int>(StringComparer.Ordinal);
        var insensitive = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<WordMailMergeSchemaPlanIssue>();
        var schemaBlocked = new HashSet<string>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < columns.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = columns[ordinal];
            if (!exact.TryAdd(column.Name, ordinal))
            {
                schemaBlocked.Add("source_schema_duplicate_column");
                issues.Add(new WordMailMergeSchemaPlanIssue(
                    "MAIL_MERGE_SCHEMA_COLUMN_DUPLICATE",
                    WordMailMergeIssueSeverity.Error,
                    "The source schema contains a duplicate column name.",
                    SourceColumnOrdinal: ordinal
                ));
            }
            if (!insensitive.TryGetValue(column.Name, out var ordinals))
            {
                ordinals = [];
                insensitive.Add(column.Name, ordinals);
            }
            ordinals.Add(ordinal);
        }
        foreach (var pair in insensitive.Where(item => item.Value.Count > 1))
        {
            schemaBlocked.Add("source_schema_case_collision");
            foreach (var ordinal in pair.Value)
            {
                issues.Add(new WordMailMergeSchemaPlanIssue(
                    "MAIL_MERGE_SCHEMA_COLUMN_CASE_COLLISION",
                    WordMailMergeIssueSeverity.Error,
                    "The source schema contains column names that differ only by case.",
                    SourceColumnOrdinal: ordinal
                ));
            }
        }

        var bindings = new List<WordMailMergeSchemaBinding>(graph.Fields.Count);
        var usedColumns = new HashSet<int>();
        foreach (var field in graph.Fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bindings.Add(BindField(
                graph,
                field,
                columns,
                exact,
                insensitive,
                usedColumns,
                issues,
                schemaBlocked
            ));
        }
        if (graph.Configuration is null)
        {
            schemaBlocked.Add("mail_merge_configuration_missing");
            issues.Add(new WordMailMergeSchemaPlanIssue(
                "MAIL_MERGE_SCHEMA_CONFIGURATION_MISSING",
                WordMailMergeIssueSeverity.Error,
                "The package has mail-merge fields but no saved mail-merge configuration."
            ));
        }
        if (graph.Issues.Any(issue => issue.Severity == WordMailMergeIssueSeverity.Error))
        {
            schemaBlocked.Add("mail_merge_graph_errors");
        }

        var orderedSchemaBlocked = schemaBlocked.Order(StringComparer.Ordinal).ToArray();
        var executionBlocked = orderedSchemaBlocked
            .Append("execution_backend_not_implemented")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var planId = PlanId(
            graph.PackageFingerprint,
            schemaFingerprint,
            bindings,
            orderedSchemaBlocked
        );
        return new WordMailMergeSchemaBindingPlan(
            graph.PackageFingerprint,
            schemaFingerprint,
            planId,
            graph.Configuration?.Id,
            graph.Configuration?.MainDocumentType,
            graph.Configuration?.Destination,
            columns,
            bindings,
            issues.OrderBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.FieldId, StringComparer.Ordinal)
                .ThenBy(item => item.SourceColumnOrdinal)
                .ToArray(),
            orderedSchemaBlocked,
            executionBlocked,
            columns.Length - usedColumns.Count,
            graph.Configuration?.HasExternalDataSource == true,
            graph.Configuration?.HasSensitiveConnectionMetadata == true
        );
    }

    private WordMailMergeSourceColumn[] ValidateAndFreezeColumns(
        IReadOnlyList<WordMailMergeSourceColumn> columns,
        CancellationToken cancellationToken
    )
    {
        var result = new WordMailMergeSourceColumn[columns.Count];
        long totalCharacters = 0;
        for (var ordinal = 0; ordinal < columns.Count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = columns[ordinal]
                ?? throw new WordMailMergeSchemaPlanException(
                    "Mail-merge source schema contains a null column."
                );
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                throw new WordMailMergeSchemaPlanException(
                    "Mail-merge source column names must not be empty or whitespace."
                );
            }
            if (column.Name.Length > _options.MaxColumnNameCharacters)
            {
                throw new WordMailMergeSchemaPlanLimitException(
                    $"Mail-merge source column name exceeds {_options.MaxColumnNameCharacters} characters."
                );
            }
            totalCharacters = checked(totalCharacters + column.Name.Length);
            if (totalCharacters > _options.MaxTotalColumnNameCharacters)
            {
                throw new WordMailMergeSchemaPlanLimitException(
                    $"Mail-merge source schema exceeds {_options.MaxTotalColumnNameCharacters} name characters."
                );
            }
            result[ordinal] = column;
        }
        return result;
    }

    private static WordMailMergeSchemaBinding BindField(
        WordMailMergeGraph graph,
        WordMailMergeField field,
        IReadOnlyList<WordMailMergeSourceColumn> columns,
        IReadOnlyDictionary<string, int> exact,
        IReadOnlyDictionary<string, List<int>> insensitive,
        ISet<int> usedColumns,
        ICollection<WordMailMergeSchemaPlanIssue> issues,
        ISet<string> schemaBlocked
    )
    {
        if (field.FieldType is not "MERGEFIELD" and not "MERGEBARCODE")
        {
            schemaBlocked.Add("unsupported_mail_merge_control_fields");
            issues.Add(new WordMailMergeSchemaPlanIssue(
                "MAIL_MERGE_SCHEMA_FIELD_TYPE_UNSUPPORTED",
                WordMailMergeIssueSeverity.Error,
                "The schema planner does not model this mail-merge control field type.",
                field.Id
            ));
            return Binding(
                field,
                null,
                null,
                null,
                null,
                WordMailMergeSchemaBindingStatus.NotApplicable,
                executionBlocking: true
            );
        }
        if (!field.IsComplete)
        {
            schemaBlocked.Add("incomplete_mail_merge_fields");
            issues.Add(new WordMailMergeSchemaPlanIssue(
                "MAIL_MERGE_SCHEMA_FIELD_INCOMPLETE",
                WordMailMergeIssueSeverity.Error,
                "The mail-merge field is incomplete or its instruction could not be parsed.",
                field.Id
            ));
        }
        if (field.IsInDeletedContent)
        {
            schemaBlocked.Add("deleted_mail_merge_fields");
            issues.Add(new WordMailMergeSchemaPlanIssue(
                "MAIL_MERGE_SCHEMA_FIELD_IN_DELETED_CONTENT",
                WordMailMergeIssueSeverity.Error,
                "The mail-merge field is inside deleted revision content.",
                field.Id
            ));
        }
        if (field.BindingStatus == WordMailMergeFieldBindingStatus.Ambiguous)
        {
            schemaBlocked.Add("ambiguous_odso_mapping");
            return Binding(
                field,
                null,
                field.TargetName,
                null,
                null,
                WordMailMergeSchemaBindingStatus.Ambiguous,
                executionBlocking: true
            );
        }

        string? mappingId = null;
        string? requiredName = field.TargetName;
        if (field.MappingIds.Count == 1
            && graph.TryGetMapping(field.MappingIds[0], out var mapping)
            && mapping is not null)
        {
            mappingId = mapping.Id;
            requiredName = mapping.SourceColumnName ?? field.TargetName;
        }
        if (string.IsNullOrWhiteSpace(requiredName))
        {
            schemaBlocked.Add("source_column_name_missing");
            issues.Add(new WordMailMergeSchemaPlanIssue(
                "MAIL_MERGE_SCHEMA_REQUIRED_COLUMN_UNKNOWN",
                WordMailMergeIssueSeverity.Error,
                "The mail-merge field does not identify a required source column.",
                field.Id
            ));
            return Binding(
                field,
                mappingId,
                null,
                null,
                null,
                WordMailMergeSchemaBindingStatus.Missing,
                executionBlocking: true
            );
        }
        if (exact.TryGetValue(requiredName, out var exactOrdinal))
        {
            usedColumns.Add(exactOrdinal);
            return Binding(
                field,
                mappingId,
                requiredName,
                exactOrdinal,
                columns[exactOrdinal],
                WordMailMergeSchemaBindingStatus.ResolvedExact,
                executionBlocking: !field.IsComplete || field.IsInDeletedContent
            );
        }
        if (insensitive.TryGetValue(requiredName, out var ordinals))
        {
            if (ordinals.Count == 1)
            {
                var ordinal = ordinals[0];
                usedColumns.Add(ordinal);
                return Binding(
                    field,
                    mappingId,
                    requiredName,
                    ordinal,
                    columns[ordinal],
                    WordMailMergeSchemaBindingStatus.ResolvedCaseInsensitive,
                    executionBlocking: !field.IsComplete || field.IsInDeletedContent
                );
            }
            schemaBlocked.Add("source_column_ambiguous");
            issues.Add(new WordMailMergeSchemaPlanIssue(
                "MAIL_MERGE_SCHEMA_SOURCE_COLUMN_AMBIGUOUS",
                WordMailMergeIssueSeverity.Error,
                "The required source column matches multiple case-colliding columns.",
                field.Id
            ));
            return Binding(
                field,
                mappingId,
                requiredName,
                null,
                null,
                WordMailMergeSchemaBindingStatus.Ambiguous,
                executionBlocking: true
            );
        }
        schemaBlocked.Add("source_column_missing");
        issues.Add(new WordMailMergeSchemaPlanIssue(
            "MAIL_MERGE_SCHEMA_SOURCE_COLUMN_MISSING",
            WordMailMergeIssueSeverity.Error,
            "The required mail-merge source column is absent from the supplied schema.",
            field.Id
        ));
        return Binding(
            field,
            mappingId,
            requiredName,
            null,
            null,
            WordMailMergeSchemaBindingStatus.Missing,
            executionBlocking: true
        );
    }

    private static WordMailMergeSchemaBinding Binding(
        WordMailMergeField field,
        string? mappingId,
        string? requiredColumnName,
        int? sourceColumnOrdinal,
        WordMailMergeSourceColumn? sourceColumn,
        WordMailMergeSchemaBindingStatus status,
        bool executionBlocking
    ) => new(
        field.Id,
        field.FieldType,
        field.TargetName,
        mappingId,
        requiredColumnName,
        sourceColumnOrdinal,
        sourceColumn?.Name,
        sourceColumn?.DataKind,
        status,
        field.IsComplete,
        field.IsInDeletedContent,
        executionBlocking
    );

    private static string SourceSchemaFingerprint(
        IReadOnlyList<WordMailMergeSourceColumn> columns
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Contract);
        Append(hash, columns.Count.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 0; ordinal < columns.Count; ordinal++)
        {
            Append(hash, ordinal.ToString(CultureInfo.InvariantCulture));
            Append(hash, columns[ordinal].Name);
            Append(hash, columns[ordinal].DataKind.ToString());
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string PlanId(
        string packageFingerprint,
        string schemaFingerprint,
        IReadOnlyList<WordMailMergeSchemaBinding> bindings,
        IReadOnlyList<string> blockers
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Contract);
        Append(hash, packageFingerprint);
        Append(hash, schemaFingerprint);
        foreach (var binding in bindings)
        {
            Append(hash, binding.FieldId);
            Append(hash, binding.MappingId);
            Append(hash, binding.RequiredColumnName);
            Append(hash, binding.SourceColumnOrdinal?.ToString(CultureInfo.InvariantCulture));
            Append(hash, binding.Status.ToString());
            Append(hash, binding.ExecutionBlocking ? "1" : "0");
        }
        foreach (var blocker in blockers)
        {
            Append(hash, blocker);
        }
        return "wmmsp_"
            + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()[..24];
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
