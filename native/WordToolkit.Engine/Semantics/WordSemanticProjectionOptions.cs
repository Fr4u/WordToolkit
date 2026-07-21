namespace WordToolkit.Engine.Semantics;

public sealed record WordSemanticProjectionOptions
{
    public static WordSemanticProjectionOptions Default { get; } = new();

    public long MaxXmlCharacters { get; init; } = 128L * 1024 * 1024;

    public int MaxXmlElements { get; init; } = 1_000_000;

    public int MaxXmlDepth { get; init; } = 256;

    public long MaxTextCharacters { get; init; } = 32L * 1024 * 1024;

    public int MaxStoryParts { get; init; } = 512;

    public int MaxStoryRelationships { get; init; } = 4_096;

    internal void Validate()
    {
        if (MaxXmlCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlCharacters));
        }

        if (MaxXmlElements <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlElements));
        }

        if (MaxXmlDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlDepth));
        }

        if (MaxTextCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTextCharacters));
        }

        if (MaxStoryParts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStoryParts));
        }

        if (MaxStoryRelationships <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStoryRelationships));
        }
    }
}

public sealed class WordSemanticProjectionException : IOException
{
    public WordSemanticProjectionException(string message)
        : base(message)
    {
    }

    public WordSemanticProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordSemanticLimitException : IOException
{
    public WordSemanticLimitException(string message)
        : base(message)
    {
    }
}
