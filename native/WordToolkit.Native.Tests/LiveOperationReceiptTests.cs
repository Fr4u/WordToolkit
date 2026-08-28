using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LiveOperationReceiptTests
{
    [Fact]
    public async Task ConcurrentDuplicatesRunOneExecutionAndReplayTheSameResult()
    {
        var store = new LiveOperationReceiptStore();
        var release = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var executions = 0;
        Task<object> Factory()
        {
            Interlocked.Increment(ref executions);
            return release.Task;
        }

        var first = store.GetOrCreate("wlop_same", "abc", Factory);
        var duplicate = store.GetOrCreate("wlop_same", "abc", Factory);
        Assert.True(first.WasCreatedForCaller);
        Assert.False(duplicate.WasCreatedForCaller);
        Assert.Same(first.Execution.Value, duplicate.Execution.Value);
        Assert.Equal(1, Volatile.Read(ref executions));

        release.SetResult(new { live_document_id = "live_1", live_version = 7 });
        await first.Execution.Value;
        await duplicate.Execution.Value;
        using var status = JsonDocument.Parse(
            JsonSerializer.Serialize(store.Status("wlop_same"), JsonDefaults.Compact)
        );
        Assert.Equal(
            "succeeded",
            status.RootElement.GetProperty("operation_status").GetString()
        );
        Assert.True(status.RootElement.GetProperty("outcome_known").GetBoolean());
        Assert.DoesNotContain("document text", status.RootElement.GetRawText());
    }

    [Fact]
    public void ReusingOneIdForDifferentIntentFailsClosed()
    {
        var store = new LiveOperationReceiptStore();
        store.GetOrCreate(
            "wlop_conflict",
            "first",
            () => Task.FromResult<object>(new { ok = true })
        );

        var error = Assert.Throws<NativeToolException>(() =>
            store.GetOrCreate(
                "wlop_conflict",
                "second",
                () => Task.FromResult<object>(new { ok = true })
            )
        );
        Assert.Equal("IDEMPOTENCY_CONFLICT", error.ErrorCode);
    }

    [Fact]
    public async Task CallerCancellationDoesNotCancelOwnedExecutionOrLoseOutcome()
    {
        var store = new LiveOperationReceiptStore();
        var release = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var receipt = store.GetOrCreate(
            "wlop_cancel",
            "intent",
            () => release.Task
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await receipt.Execution.Value.WaitAsync(cancellation.Token)
        );
        release.SetResult(
            new
            {
                live_document_id = "live_1",
                live_version = 8,
                operation_count = 4,
            }
        );
        await receipt.Execution.Value;
        using var status = JsonDocument.Parse(
            JsonSerializer.Serialize(store.Status("wlop_cancel"), JsonDefaults.Compact)
        );
        Assert.Equal(
            "succeeded",
            status.RootElement.GetProperty("operation_status").GetString()
        );
        Assert.Equal(
            8,
            status.RootElement.GetProperty("result").GetProperty("live_version").GetInt32()
        );
    }

    [Fact]
    public async Task RegistryIsBoundedAndTerminalReceiptsExpire()
    {
        var now = DateTimeOffset.Parse("2026-08-28T10:00:00Z");
        var store = new LiveOperationReceiptStore(() => now);
        for (var index = 0; index < LiveOperationReceiptStore.MaximumEntries; index++)
        {
            var receipt = store.GetOrCreate(
                $"wlop_{index}",
                index.ToString(),
                () => Task.FromResult<object>(new { live_version = index })
            );
            await receipt.Execution.Value;
        }
        Assert.Equal(LiveOperationReceiptStore.MaximumEntries, store.Count);

        var extra = store.GetOrCreate(
            "wlop_extra",
            "extra",
            () => Task.FromResult<object>(new { live_version = 999 })
        );
        await extra.Execution.Value;
        Assert.Equal(LiveOperationReceiptStore.MaximumEntries, store.Count);

        now += LiveOperationReceiptStore.EntryTimeToLive + TimeSpan.FromSeconds(1);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void RegistryNeverEvictsPendingWorkToMakeRoom()
    {
        var store = new LiveOperationReceiptStore();
        var never = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        for (var index = 0; index < LiveOperationReceiptStore.MaximumEntries; index++)
        {
            store.GetOrCreate(
                $"wlop_pending_{index}",
                index.ToString(),
                () => never.Task
            );
        }

        var error = Assert.Throws<NativeToolException>(() =>
            store.GetOrCreate(
                "wlop_overflow",
                "overflow",
                () => never.Task
            )
        );
        Assert.Equal("LIVE_OPERATION_RECEIPT_LIMIT", error.ErrorCode);
    }
}
