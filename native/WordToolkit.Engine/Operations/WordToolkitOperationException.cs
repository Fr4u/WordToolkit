namespace WordToolkit.Engine.Operations;

public sealed class WordToolkitOperationException : Exception
{
    public WordToolkitOperationException(
        string code,
        string message,
        string? reason = null,
        bool retryable = false,
        Exception? innerException = null,
        object? details = null
    )
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Reason = reason;
        Retryable = retryable;
        Details = details;
    }

    public string Code { get; }

    public string? Reason { get; }

    public bool Retryable { get; }

    public object? Details { get; }
}

public sealed record WordToolkitRecoveryDetails(
    IReadOnlyList<string> RecoveryArtifactNames
);
