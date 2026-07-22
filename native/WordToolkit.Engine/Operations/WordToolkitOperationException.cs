namespace WordToolkit.Engine.Operations;

public sealed class WordToolkitOperationException : Exception
{
    public WordToolkitOperationException(
        string code,
        string message,
        string? reason = null,
        bool retryable = false,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Reason = reason;
        Retryable = retryable;
    }

    public string Code { get; }

    public string? Reason { get; }

    public bool Retryable { get; }
}
