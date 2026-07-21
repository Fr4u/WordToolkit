namespace WordToolkit.Native.Word;

internal interface IWordComHost : IAsyncDisposable
{
    Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    );
}
