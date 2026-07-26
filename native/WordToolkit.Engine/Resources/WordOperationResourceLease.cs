using System.Collections.ObjectModel;

namespace WordToolkit.Engine.Resources;

public enum WordOperationResourceStage
{
    Operation,
    OpcPackage,
    SemanticProjection,
    Styles,
    Numbering,
    References,
    Sections,
    Charts,
    FiguresAndCaptions,
    ContentControls,
    Tables,
    Bibliography,
    ActiveContent,
    Settings,
    DocumentProperties,
    Diagrams,
    Outline,
    Theme,
    FontTable,
    MarkupCompatibility,
    Lint,
    ListSequences,
    DependencyGraph,
}

public sealed record WordOperationResourceStageUsage(
    WordOperationResourceStage Stage,
    long AccountedBytes
);

public sealed record WordOperationResourceUsage(
    string AccountingModel,
    long AccountedBytes,
    long MaximumAccountedBytes,
    IReadOnlyList<WordOperationResourceStageUsage> Stages
);

public sealed record WordOperationXmlParseCacheUsage(
    string Model,
    long Requests,
    long UniqueParses,
    long CacheHits,
    long AvoidedAccountedBytes
);

public sealed class WordOperationResourceLimitException : IOException
{
    internal WordOperationResourceLimitException(
        WordOperationResourceStage stage,
        long accountedBytes,
        long maximumAccountedBytes,
        long attemptedBytes
    )
        : base(
            $"Operation resource budget was exhausted in stage '{stage}' "
                + $"at {accountedBytes} of {maximumAccountedBytes} accounted bytes."
        )
    {
        Stage = stage;
        AccountedBytes = accountedBytes;
        MaximumAccountedBytes = maximumAccountedBytes;
        AttemptedBytes = attemptedBytes;
    }

    public WordOperationResourceStage Stage { get; }

    public long AccountedBytes { get; }

    public long MaximumAccountedBytes { get; }

    public long AttemptedBytes { get; }
}

public sealed class WordOperationResourceLease
{
    public const string AccountingModel = "word_operation_accounted_v1";
    public const long DefaultMaximumAccountedBytes = 640L * 1024 * 1024;

    private const long OperationBaseBytes = 4_096;
    private readonly object _gate = new();
    private readonly long[] _stageBytes =
        new long[Enum.GetValues<WordOperationResourceStage>().Length];
    private long _accountedBytes;
    private long _xmlParseRequests;
    private long _xmlParseCacheHits;
    private long _xmlParseAvoidedAccountedBytes;

    public WordOperationResourceLease(
        long maximumAccountedBytes = DefaultMaximumAccountedBytes
    )
    {
        if (maximumAccountedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAccountedBytes));
        }
        MaximumAccountedBytes = maximumAccountedBytes;
        Charge(WordOperationResourceStage.Operation, OperationBaseBytes);
    }

    public long MaximumAccountedBytes { get; }

    public long AccountedBytes
    {
        get
        {
            lock (_gate)
            {
                return _accountedBytes;
            }
        }
    }

    public void Charge(WordOperationResourceStage stage, long accountedBytes)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        if (accountedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountedBytes));
        }

        lock (_gate)
        {
            if (_accountedBytes > MaximumAccountedBytes - accountedBytes)
            {
                throw new WordOperationResourceLimitException(
                    stage,
                    _accountedBytes,
                    MaximumAccountedBytes,
                    accountedBytes
                );
            }
            _accountedBytes += accountedBytes;
            _stageBytes[(int)stage] += accountedBytes;
        }
    }

    public WordOperationResourceUsage Snapshot()
    {
        lock (_gate)
        {
            var stages = Enum.GetValues<WordOperationResourceStage>()
                .Select(stage => new WordOperationResourceStageUsage(
                    stage,
                    _stageBytes[(int)stage]
                ))
                .Where(usage => usage.AccountedBytes != 0)
                .ToArray();
            return new WordOperationResourceUsage(
                AccountingModel,
                _accountedBytes,
                MaximumAccountedBytes,
                new ReadOnlyCollection<WordOperationResourceStageUsage>(stages)
            );
        }
    }

    public WordOperationXmlParseCacheUsage SnapshotXmlParseCache()
    {
        lock (_gate)
        {
            return new WordOperationXmlParseCacheUsage(
                "word_operation_xml_parse_cache_v1",
                _xmlParseRequests,
                _xmlParseRequests - _xmlParseCacheHits,
                _xmlParseCacheHits,
                _xmlParseAvoidedAccountedBytes
            );
        }
    }

    internal void RecordXmlParseCacheResult(bool cacheHit, int sourceBytes)
    {
        if (sourceBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceBytes));
        }
        lock (_gate)
        {
            _xmlParseRequests++;
            if (!cacheHit)
            {
                return;
            }
            _xmlParseCacheHits++;
            _xmlParseAvoidedAccountedBytes = checked(
                _xmlParseAvoidedAccountedBytes
                    + WordOperationResourceAccounting.AccountedXmlParseBytes(sourceBytes)
            );
        }
    }
}

internal static class WordOperationResourceAccounting
{
    private const long ProjectionBaseBytes = 4_096;
    private const long PackageEntryFixedBytes = 320;
    private const long XmlParseFixedBytes = 8_192;
    private const long XmlSourceExpansionFactor = 12;
    private const long SemanticNodeFixedBytes = 1_024;
    private const long SemanticFingerprintFixedBytes = 192;
    private const long PackageContentTypeFixedBytes = 512;
    private const long PackagePartFixedBytes = 384;
    private const long PackageRelationshipFixedBytes = 768;
    private const long PackageDiagnosticFixedBytes = 384;

    public static void ChargeProjectionBase(
        WordOperationResourceLease? lease,
        WordOperationResourceStage stage
    ) => lease?.Charge(stage, ProjectionBaseBytes);

    public static void ChargePackageEntry(
        WordOperationResourceLease? lease,
        string entryName,
        long uncompressedBytes
    )
    {
        if (lease is null)
        {
            return;
        }
        var bytes = checked(
            PackageEntryFixedBytes
                + AlignedByteArrayBytes(uncompressedBytes)
                + AccountedStringBytes(entryName)
        );
        lease.Charge(WordOperationResourceStage.OpcPackage, bytes);
    }

    public static void ChargeZipPreflightBuffer(
        WordOperationResourceLease? lease,
        int bytes
    )
    {
        if (lease is null)
        {
            return;
        }
        lease.Charge(
            WordOperationResourceStage.OpcPackage,
            AlignedByteArrayBytes(bytes)
        );
    }

    public static void ChargeZipCentralDirectory(
        WordOperationResourceLease? lease,
        long bytes,
        long entries
    )
    {
        if (lease is null)
        {
            return;
        }
        var accountedBytes = checked(
            Align(bytes) + checked(entries * 256L)
        );
        lease.Charge(WordOperationResourceStage.OpcPackage, accountedBytes);
    }

    public static void ChargePackageContentType(
        WordOperationResourceLease? lease,
        string key,
        string value
    ) => ChargePackageMetadata(
        lease,
        PackageContentTypeFixedBytes,
        key,
        value
    );

    public static void ChargePackagePart(
        WordOperationResourceLease? lease,
        string uri,
        string? contentType
    ) => ChargePackageMetadata(
        lease,
        PackagePartFixedBytes,
        uri,
        contentType
    );

    public static void ChargePackageRelationship(
        WordOperationResourceLease? lease,
        string sourcePartUri,
        string relationshipPartUri,
        string id,
        string type,
        string target,
        string? resolvedTargetPartUri,
        string? targetFragment
    ) => ChargePackageMetadata(
        lease,
        PackageRelationshipFixedBytes,
        sourcePartUri,
        relationshipPartUri,
        id,
        type,
        target,
        resolvedTargetPartUri,
        targetFragment
    );

    public static void ChargePackageDiagnostic(
        WordOperationResourceLease? lease,
        string code,
        string message,
        string? partUri,
        string? relationshipId
    ) => ChargePackageMetadata(
        lease,
        PackageDiagnosticFixedBytes,
        code,
        message,
        partUri,
        relationshipId
    );

    public static void ChargeXmlParse(
        WordOperationResourceLease? lease,
        WordOperationResourceStage stage,
        int sourceBytes
    )
    {
        if (lease is null)
        {
            return;
        }
        lease.Charge(stage, AccountedXmlParseBytes(sourceBytes));
    }

    internal static long AccountedXmlParseBytes(int sourceBytes)
    {
        if (sourceBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceBytes));
        }
        var bytes = checked(
            XmlParseFixedBytes + checked((long)sourceBytes * XmlSourceExpansionFactor)
        );
        return Align(bytes);
    }

    public static void ChargeSemanticNode(
        WordOperationResourceLease? lease,
        string id,
        string? parentId,
        string sourcePartUri,
        string sourcePath,
        string? text,
        IReadOnlyDictionary<string, string> properties,
        string identityFingerprint,
        string subtreeFingerprint,
        string structuralFingerprint
    )
    {
        if (lease is null)
        {
            return;
        }
        long bytes = SemanticNodeFixedBytes;
        bytes = checked(bytes + AccountedStringBytes(id));
        bytes = checked(bytes + AccountedStringBytes(parentId));
        bytes = checked(bytes + AccountedStringBytes(sourcePartUri));
        bytes = checked(bytes + AccountedStringBytes(sourcePath));
        bytes = checked(bytes + AccountedStringBytes(text));
        bytes = checked(bytes + AccountedStringBytes(identityFingerprint));
        bytes = checked(bytes + AccountedStringBytes(subtreeFingerprint));
        bytes = checked(bytes + AccountedStringBytes(structuralFingerprint));
        foreach (var pair in properties)
        {
            bytes = checked(bytes + 64 + AccountedStringBytes(pair.Key));
            bytes = checked(bytes + AccountedStringBytes(pair.Value));
        }
        lease.Charge(WordOperationResourceStage.SemanticProjection, bytes);
    }

    public static void ChargeSemanticFingerprint(
        WordOperationResourceLease? lease,
        string fingerprint
    ) => lease?.Charge(
        WordOperationResourceStage.SemanticProjection,
        checked(SemanticFingerprintFixedBytes + AccountedStringBytes(fingerprint))
    );

    public static void ChargeItems(
        WordOperationResourceLease? lease,
        WordOperationResourceStage stage,
        long count,
        long fixedBytesPerItem
    )
    {
        if (lease is null || count == 0)
        {
            return;
        }
        if (count < 0 || fixedBytesPerItem <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        lease.Charge(stage, checked(count * fixedBytesPerItem));
    }

    public static long AccountedStringBytes(string? value)
    {
        if (value is null)
        {
            return 0;
        }
        return Align(checked(24L + checked((long)value.Length * sizeof(char))));
    }

    private static void ChargePackageMetadata(
        WordOperationResourceLease? lease,
        long fixedBytes,
        params string?[] values
    )
    {
        if (lease is null)
        {
            return;
        }
        var bytes = fixedBytes;
        foreach (var value in values)
        {
            bytes = checked(bytes + AccountedStringBytes(value));
        }
        lease.Charge(WordOperationResourceStage.OpcPackage, bytes);
    }

    private static long AlignedByteArrayBytes(long length) =>
        Align(checked(24L + length));

    private static long Align(long value) => checked((value + 7L) & ~7L);
}
