namespace WordToolkit.Engine.Packaging;

public sealed record OpcPackageLimits
{
    public static OpcPackageLimits Default { get; } = new();

    public int MaxEntries { get; init; } = 20_000;

    public long MaxEntryUncompressedBytes { get; init; } = 128L * 1024 * 1024;

    public long MaxTotalUncompressedBytes { get; init; } = 512L * 1024 * 1024;

    public double MaxCompressionRatio { get; init; } = 1_000;

    public long MaxMetadataXmlCharacters { get; init; } = 64L * 1024 * 1024;

    internal void Validate()
    {
        if (MaxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEntries));
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
    }
}

public sealed class OpcPackageLimitException : IOException
{
    public OpcPackageLimitException(string message)
        : base(message)
    {
    }
}
