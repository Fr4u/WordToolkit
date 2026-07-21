namespace WordToolkit.Engine.Xml;

public sealed record LosslessXmlOptions
{
    public static LosslessXmlOptions Default { get; } = new();

    public int MaxSourceBytes { get; init; } = 128 * 1024 * 1024;

    public long MaxXmlCharacters { get; init; } = 128L * 1024 * 1024;

    public int MaxXmlElements { get; init; } = 1_000_000;

    public int MaxXmlDepth { get; init; } = 256;

    public long MaxTextCharacters { get; init; } = 32L * 1024 * 1024;

    internal void Validate()
    {
        if (MaxSourceBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSourceBytes));
        }

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
    }
}

public class LosslessXmlException : IOException
{
    public LosslessXmlException(string message)
        : base(message)
    {
    }

    public LosslessXmlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LosslessXmlParseException : LosslessXmlException
{
    public LosslessXmlParseException(string message)
        : base(message)
    {
    }

    public LosslessXmlParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LosslessXmlLimitException : LosslessXmlException
{
    public LosslessXmlLimitException(string message)
        : base(message)
    {
    }
}

public sealed class LosslessXmlEncodingException : LosslessXmlException
{
    public LosslessXmlEncodingException(string message)
        : base(message)
    {
    }

    public LosslessXmlEncodingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LosslessXmlEditException : LosslessXmlException
{
    public LosslessXmlEditException(string message)
        : base(message)
    {
    }

    public LosslessXmlEditException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LosslessXmlPreconditionException : InvalidOperationException
{
    public LosslessXmlPreconditionException(string message)
        : base(message)
    {
    }
}
