using System.Runtime.InteropServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class WordComHostTests
{
    private const int RpcEDisconnected = unchecked((int)0x80010108);

    [Fact]
    public async Task InjectedApplicationFactoryNeverClaimsRuntimeOwnership()
    {
        await using var host = new WordComHost(_ => new object());
        _ = await host.InvokeAsync(application => application is not null, launchIfMissing: true);
        Assert.False(host.ApplicationOwnedByRuntime);
    }

    [Fact]
    public async Task MutationIsNeverReplayedAfterComDisconnect()
    {
        await using var host = new WordComHost(_ => new object());
        var attempts = 0;

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                host.InvokeAsync<int>(
                    _ =>
                    {
                        attempts++;
                        throw new COMException("disconnected", RpcEDisconnected);
                    }
                )
        );

        Assert.Equal(1, attempts);
        Assert.Equal("WORD_OPERATION_OUTCOME_UNKNOWN", error.ErrorCode);
        Assert.False(error.Retryable);
        Assert.Contains("disconnected", error.Message, StringComparison.OrdinalIgnoreCase);
        using var details = JsonDocument.Parse(JsonSerializer.Serialize(error.Details));
        Assert.True(details.RootElement.GetProperty("outcome_unknown").GetBoolean());
        Assert.False(details.RootElement.GetProperty("automatic_replay").GetBoolean());
        Assert.Equal(
            "restart_wordtoolkit_runtime_then_reconnect_and_reinspect",
            details.RootElement.GetProperty("recovery").GetString()
        );
    }

    [Fact]
    public async Task ExplicitReplaySafeOperationMayReconnectOnce()
    {
        await using var host = new WordComHost(_ => new object());
        var attempts = 0;

        var result = await host.InvokeAsync(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new COMException("disconnected", RpcEDisconnected);
                }
                return 42;
            },
            WordComReplaySafety.ReplaySafe
        );

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task CancellationAfterMutationStartsReportsUnknownOutcome()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        await using var host = new WordComHost(_ => new object());

        var pending = host.InvokeAsync(
            _ =>
            {
                started.Set();
                release.Wait();
                return true;
            },
            cancellation.Token
        );
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();

        try
        {
            var error = await Assert.ThrowsAsync<NativeToolException>(() => pending);
            Assert.Equal("WORD_OPERATION_OUTCOME_UNKNOWN", error.ErrorCode);
            Assert.False(error.Retryable);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task QueuedCancellationCannotClearRecoveryForAbandonedExecution()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var activeCancellation = new CancellationTokenSource();
        using var queuedCancellation = new CancellationTokenSource();
        await using var host = new WordComHost(_ => new object());

        var active = host.InvokeAsync(
            _ =>
            {
                started.Set();
                release.Wait();
                return true;
            },
            activeCancellation.Token
        );
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        var queued = host.InvokeAsync(_ => true, queuedCancellation.Token);

        try
        {
            activeCancellation.Cancel();
            var activeError = await Assert.ThrowsAsync<NativeToolException>(() => active);
            Assert.Equal("WORD_OPERATION_OUTCOME_UNKNOWN", activeError.ErrorCode);

            queuedCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

            var recoveryError = await Assert.ThrowsAsync<NativeToolException>(
                () => host.InvokeAsync(_ => true)
            );
            Assert.Equal("WORD_HOST_RECOVERY_REQUIRED", recoveryError.ErrorCode);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task QueuedMutationIsRejectedAfterEarlierOutcomeBecomesUnknown()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        await using var host = new WordComHost(_ => new object());
        var queuedAttempts = 0;

        var active = host.InvokeAsync(
            _ =>
            {
                started.Set();
                release.Wait();
                return true;
            },
            cancellation.Token
        );
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        var queued = host.InvokeAsync(
            _ =>
            {
                queuedAttempts++;
                return true;
            }
        );

        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<NativeToolException>(() => active);
            release.Set();

            var recovery = await Assert.ThrowsAsync<NativeToolException>(() => queued);
            Assert.Equal("WORD_HOST_RECOVERY_REQUIRED", recovery.ErrorCode);
            Assert.Equal(0, queuedAttempts);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task CancellationBeforeReplayPreventsSecondReplaySafeAttempt()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        await using var host = new WordComHost(_ => new object());
        var attempts = 0;

        var pending = host.InvokeAsync<int>(
            _ =>
            {
                attempts++;
                started.Set();
                release.Wait();
                throw new COMException("disconnected", RpcEDisconnected);
            },
            WordComReplaySafety.ReplaySafe,
            cancellation.Token
        );
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            cancellation.Cancel();
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(1, attempts);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task CompletedMutationThatLosesCancellationRaceIsStillOutcomeUnknown()
    {
        using var started = new ManualResetEventSlim();
        using var finishOperation = new ManualResetEventSlim();
        using var catchEntered = new ManualResetEventSlim();
        using var allowCancellationObservation = new ManualResetEventSlim();
        using var workerCompleted = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        await using var host = new WordComHost(
            _ => new object(),
            beforeCancellationObservation: () =>
            {
                catchEntered.Set();
                allowCancellationObservation.Wait();
            },
            afterWorkItemCompleted: () => workerCompleted.Set()
        );

        var pending = host.InvokeAsync(
            _ =>
            {
                started.Set();
                finishOperation.Wait();
                return true;
            },
            cancellation.Token
        );
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            var cancelThread = new Thread(cancellation.Cancel) { IsBackground = true };
            cancelThread.Start();
            Assert.True(catchEntered.Wait(TimeSpan.FromSeconds(15)));
            finishOperation.Set();
            Assert.True(workerCompleted.Wait(TimeSpan.FromSeconds(15)));
            allowCancellationObservation.Set();
            Assert.True(cancelThread.Join(TimeSpan.FromSeconds(15)));

            var error = await Assert.ThrowsAsync<NativeToolException>(() => pending);
            Assert.Equal("WORD_OPERATION_OUTCOME_UNKNOWN", error.ErrorCode);
        }
        finally
        {
            finishOperation.Set();
            allowCancellationObservation.Set();
        }
    }

    [Fact]
    public async Task CancellationSignalBlocksQueuedMutationBeforeAwaiterObservesCancellation()
    {
        using var started = new ManualResetEventSlim();
        using var finishOperation = new ManualResetEventSlim();
        using var catchEntered = new ManualResetEventSlim();
        using var allowCancellationObservation = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        await using var host = new WordComHost(
            _ => new object(),
            beforeCancellationObservation: () =>
            {
                catchEntered.Set();
                allowCancellationObservation.Wait();
            }
        );
        var queuedAttempts = 0;

        var active = host.InvokeAsync(
            _ =>
            {
                started.Set();
                finishOperation.Wait();
                return true;
            },
            cancellation.Token
        );
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        var queued = host.InvokeAsync(
            _ =>
            {
                queuedAttempts++;
                return true;
            }
        );

        try
        {
            var cancelThread = new Thread(cancellation.Cancel) { IsBackground = true };
            cancelThread.Start();
            Assert.True(catchEntered.Wait(TimeSpan.FromSeconds(15)));

            finishOperation.Set();
            Assert.False(SpinWait.SpinUntil(() => queuedAttempts != 0, 250));
            Assert.Equal(0, queuedAttempts);

            allowCancellationObservation.Set();
            Assert.True(cancelThread.Join(TimeSpan.FromSeconds(15)));
            var outcome = await Assert.ThrowsAsync<NativeToolException>(() => active);
            Assert.Equal("WORD_OPERATION_OUTCOME_UNKNOWN", outcome.ErrorCode);
            var recovery = await Assert.ThrowsAsync<NativeToolException>(() => queued);
            Assert.Equal("WORD_HOST_RECOVERY_REQUIRED", recovery.ErrorCode);
            Assert.Equal(0, queuedAttempts);
        }
        finally
        {
            finishOperation.Set();
            allowCancellationObservation.Set();
        }
    }

    [Fact]
    public async Task DelayedCancellationObserversCannotLetQueuedMutationStart()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var finishFirst = new ManualResetEventSlim();
        using var blockerEntered = new ManualResetEventSlim();
        using var allowCancellationCallbacks = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        await using var host = new WordComHost(_ => new object());

        var first = host.InvokeAsync(
            _ =>
            {
                firstStarted.Set();
                finishFirst.Wait();
                return true;
            },
            cancellation.Token
        );
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        var second = host.InvokeAsync(
            _ =>
            {
                secondStarted.Set();
                return true;
            }
        );
        using var blockingRegistration = cancellation.Token.Register(() =>
        {
            blockerEntered.Set();
            allowCancellationCallbacks.Wait();
        });

        try
        {
            var cancelThread = new Thread(cancellation.Cancel) { IsBackground = true };
            cancelThread.Start();
            Assert.True(blockerEntered.Wait(TimeSpan.FromSeconds(15)));

            finishFirst.Set();
            var recovery = await Assert.ThrowsAsync<NativeToolException>(() => second);
            Assert.Equal("WORD_HOST_RECOVERY_REQUIRED", recovery.ErrorCode);
            Assert.False(secondStarted.IsSet);

            allowCancellationCallbacks.Set();
            Assert.True(cancelThread.Join(TimeSpan.FromSeconds(15)));

            var outcome = await Assert.ThrowsAsync<NativeToolException>(() => first);
            Assert.Equal("WORD_OPERATION_OUTCOME_UNKNOWN", outcome.ErrorCode);
            Assert.False(secondStarted.IsSet);
        }
        finally
        {
            finishFirst.Set();
            allowCancellationCallbacks.Set();
        }
    }

    [Fact]
    public async Task UnknownMutationOutcomeBlocksFurtherNonReplayableWorkUntilRestart()
    {
        await using var host = new WordComHost(_ => new object());
        var mutationAttempts = 0;

        await Assert.ThrowsAsync<NativeToolException>(
            () =>
                host.InvokeAsync<int>(
                    _ =>
                    {
                        mutationAttempts++;
                        throw new COMException("disconnected", RpcEDisconnected);
                    }
                )
        );

        var recovery = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                host.InvokeAsync(
                    _ =>
                    {
                        mutationAttempts++;
                        return true;
                    }
                )
        );
        Assert.Equal("WORD_HOST_RECOVERY_REQUIRED", recovery.ErrorCode);
        Assert.False(recovery.Retryable);
        Assert.Equal(1, mutationAttempts);

        var inspected = await host.InvokeAsync(
            _ => "readable for diagnosis",
            WordComReplaySafety.ReplaySafe
        );
        Assert.Equal("readable for diagnosis", inspected);
    }

    [Fact]
    public void DocumentCompareIsNeverClassifiedAsReplaySafeRead()
    {
        var member = new WordComMember(
            Name: "Compare",
            Kind: "method",
            MemberId: 1,
            DeclarationIndex: 0,
            FunctionKind: 0,
            InvokeKind: 1,
            CallConvention: 0,
            VtableOffset: 0,
            Parameters: Array.Empty<WordComParameter>(),
            ParameterCount: 0,
            OptionalParameterCount: 0,
            Variadic: false,
            ReturnType: "VOID",
            Flags: 0,
            FlagNames: Array.Empty<string>()
        );
        var effect = WordObjectModelCatalog.ClassifyEffect(
            "Document",
            member.Name,
            member.Kind
        );
        var policy = WordObjectModelCatalog.ClassifyPolicy("Document", member, effect);

        Assert.Equal("blocked", policy.Execution);
        Assert.False(policy.Mutating);
    }

    [Fact]
    public void OleRetryStopsAtItsHardBudget()
    {
        var filter = new OleMessageFilter(
            retryBudgetMilliseconds: 500,
            retryDelayMilliseconds: 100
        );

        Assert.Equal(100, filter.RetryRejectedCall(IntPtr.Zero, 400, 2));
        Assert.Equal(-1, filter.RetryRejectedCall(IntPtr.Zero, 401, 2));
        Assert.Equal(-1, filter.RetryRejectedCall(IntPtr.Zero, 500, 2));
        Assert.Equal(-1, filter.RetryRejectedCall(IntPtr.Zero, 0, 1));
    }
}
