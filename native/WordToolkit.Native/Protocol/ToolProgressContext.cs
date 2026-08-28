namespace WordToolkit.Native.Protocol;

internal static class ToolProgressContext
{
    private static readonly AsyncLocal<ProgressSink?> Current = new();

    internal static IDisposable Push(Func<string, Task> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var previous = Current.Value;
        var sink = new ProgressSink(report);
        Current.Value = sink;
        return new Scope(previous, sink);
    }

    internal static async ValueTask ReportAsync(string message)
    {
        var sink = Current.Value;
        if (sink is null || !sink.Active || string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        await sink.Report(message[..Math.Min(message.Length, 256)]);
    }

    private sealed class ProgressSink(Func<string, Task> report)
    {
        public Func<string, Task> Report { get; } = report;
        public volatile bool Active = true;
    }

    private sealed class Scope(ProgressSink? previous, ProgressSink current) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            current.Active = false;
            if (ReferenceEquals(Current.Value, current))
            {
                Current.Value = previous;
            }
        }
    }
}
