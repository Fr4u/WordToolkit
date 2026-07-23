# MCP cancellation and recovery

## Bounded transport

The native server reads line-delimited JSON-RPC with an 8 MiB character limit per
message and at most 64 active request IDs. An oversized line is drained through its line
terminator, receives error `-32600`, and does not merge with the next request.

Clients cancel an active request with either MCP `notifications/cancelled` or the legacy
`$/cancelRequest` notification. `params.requestId` must exactly match the original
string or numeric JSON-RPC ID. A request cancelled before live COM work begins returns
JSON-RPC `-32800`.

## Disconnect and replay contract

Every COM dispatch carries replay safety before it enters the STA queue. The conservative
default is `NonReplayable`. A non-replayable delegate is never automatically executed
again after a COM disconnect because Word may have committed all or part of its effect
before the proxy failed. The call returns the non-retryable tool error
`WORD_OPERATION_OUTCOME_UNKNOWN`, with `outcome_unknown=true` and
`automatic_replay=false`. The unknown-outcome gate remains sticky: diagnostic
replay-safe reads may run after the abandoned call returns, but every non-replayable
operation is rejected until the caller restarts only the WordToolkit runtime, reconnects
and inspects the intended document. The Word process is never terminated as recovery.

Only a delegate explicitly classified as `ReplaySafe` may reconnect and execute once
more. That classification is reserved for proven read-only or idempotent behavior; it is
not inferred from a broad method-name prefix. Generic object-model member batches remain
non-replayable even when their current policy contains only reads. Before the second
attempt, the host checks that the request is still awaited and not cancelled. If that
attempt fails, the normal mapped COM error is returned; there is no unbounded delegate
replay.

Busy-call retry inside COM is separately bounded. `IOleMessageFilter` waits 100 ms after
`SERVERCALL_RETRYLATER` and cancels the call after 30 seconds of elapsed retry time. This
uses the `dwTickCount` contract documented by
[Microsoft](https://learn.microsoft.com/windows/win32/api/objidl/nf-objidl-imessagefilter-retryrejectedcall).

## What cancellation can and cannot stop

Saved-package work and queued Word operations observe their request token
cooperatively. A COM call already executing inside Microsoft Word cannot be safely
aborted in general. Killing Word would risk unrelated open documents and is forbidden.

When cancellation reaches an executing replay-safe COM request, WordToolkit:

1. returns cancellation to the client without waiting for the COM call;
2. rejects new live Word calls with `WORD_HOST_RECOVERY_REQUIRED` while the abandoned
   call is still running;
3. resets its cached COM proxy if the call later returns.

Recovery is reference-counted per abandoned execution. Cancelling another request that
is still queued cannot clear the recovery state owned by a call still executing in Word.
The cancellation signal is registered when work is submitted, rather than being inferred
later from the waiting client's continuation. Once a non-replayable operation has started,
that signal makes the unknown-outcome gate visible before the STA worker can start the next
queued non-replayable operation. As a second barrier, the STA queue does not advance until
the corresponding `InvokeAsync` path has published whether its client observed a result,
an error or cancellation. Safety therefore does not depend on cancellation callback order
or thread scheduling.

When cancellation reaches an executing non-replayable COM request, the first two
recovery steps are identical, but the response is the structured, non-retryable
`WORD_OPERATION_OUTCOME_UNKNOWN` tool error rather than a claim that the operation was
cancelled. This remains true when completion races the cancellation continuation:
starting the operation is retained as state even after the worker reaches its terminal
state. Cancellation stopped the client's wait, not Word's synchronous call.

## Supervisor recovery

After a client timeout, send cancellation and allow a short grace period. If the next
live request returns `WORD_HOST_RECOVERY_REQUIRED`, or the MCP process stops answering,
terminate only the `wordtoolkit-native.exe` process and start a fresh plugin runtime.
Never terminate `WINWORD.EXE` as recovery. Reconnect to the intended document and
re-inspect its content/version before any further mutation, because the cancelled COM
call may still have completed inside Word.

The packaged MCP timeout is 180 seconds. That timeout is a supervisor boundary, not a
guarantee that Word itself released the underlying COM call.
