namespace WordToolkit.Engine.Packaging;

public sealed record OpcPackageLimits
{
    public static OpcPackageLimits Default { get; } = new();

    public int MaxEntries { get; init; } = 20_000;

    public long MaxArchiveBytes { get; init; } = 576L * 1024 * 1024;

    public long MaxCentralDirectoryBytes { get; init; } = 64L * 1024 * 1024;

    public long MaxEntryUncompressedBytes { get; init; } = 128L * 1024 * 1024;

    public long MaxTotalUncompressedBytes { get; init; } = 512L * 1024 * 1024;

    public double MaxCompressionRatio { get; init; } = 1_000;

    public long MaxMetadataXmlCharacters { get; init; } = 64L * 1024 * 1024;

    public int MaxMetadataXmlElements { get; init; } = 1_000_000;

    public int MaxContentTypeDeclarations { get; init; } = 50_000;

    public int MaxRelationships { get; init; } = 500_000;

    public int MaxDiagnostics { get; init; } = 100_000;

    internal void Validate()
    {
        if (MaxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEntries));
        }

        if (MaxArchiveBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxArchiveBytes));
        }

        if (MaxCentralDirectoryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCentralDirectoryBytes));
        }

        if (MaxEntryUncompressedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEntryUncompressedBytes));
        }

        if (MaxTotalUncompressedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalUncompressedBytes));
        }

        if (MaxCompressionRatio <= 0 || double.IsNaN(MaxCompressionRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCompressionRatio));
        }

        if (MaxMetadataXmlCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMetadataXmlCharacters));
        }

        if (MaxMetadataXmlElements <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMetadataXmlElements));
        }

        if (MaxContentTypeDeclarations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxContentTypeDeclarations));
        }

        if (MaxRelationships <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRelationships));
        }

        if (MaxDiagnostics <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDiagnostics));
        }
    }
}

public sealed class OpcPackageLimitException : IOException
{
    public OpcPackageLimitException(string message)
        : base(message)
    {
    }
}
