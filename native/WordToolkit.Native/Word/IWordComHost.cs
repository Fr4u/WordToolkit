namespace WordToolkit.Native.Word;

internal enum WordComReplaySafety
{
    NonReplayable,
    ReplaySafe,
}

internal interface IWordComHost : IAsyncDisposable
{
    Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    );

    Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        WordComReplaySafety replaySafety,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    ) => InvokeAsync(operation, cancellationToken, launchIfMissing);
}
