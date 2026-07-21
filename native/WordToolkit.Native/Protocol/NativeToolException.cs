namespace WordToolkit.Native.Protocol;

internal sealed class NativeToolException : Exception
{
    public string ErrorCode { get; }
    public object Details { get; }
    public bool Retryable { get; }

    public NativeToolException(
        string errorCode,
        string message,
        object? details = null,
        bool retryable = false
    ) : base(message)
    {
        ErrorCode = errorCode;
        Details = details ?? new { };
        Retryable = retryable;
    }
}
