# MCP cancellation and recovery

## Bounded transport

The native server reads line-delimited JSON-RPC with an 8 MiB character limit per
message and at most 64 active request IDs. An oversized line is drained through its line
terminator, receives error `-32600`, and does not merge with the next request.

Clients cancel an active request with either MCP `notifications/cancelled` or the legacy
`$/cancelRequest` notification. `params.requestId` must exactly match the original
string or numeric JSON-RPC ID. The server returns `-32800` for the cancelled request.

## What cancellation can and cannot stop

Saved-package work and queued Word operations observe their request token
cooperatively. A COM call already executing inside Microsoft Word cannot be safely
aborted in general. Killing Word would risk unrelated open documents and is forbidden.

When cancellation reaches an executing COM request, WordToolkit:

1. returns cancellation to the client without waiting for the COM call;
2. rejects new live Word calls with `WORD_HOST_RECOVERY_REQUIRED` while the abandoned
   call is still running;
3. resets its cached COM proxy if the call later returns.

## Supervisor recovery

After a client timeout, send cancellation and allow a short grace period. If the next
live request returns `WORD_HOST_RECOVERY_REQUIRED`, or the MCP process stops answering,
terminate only the `wordtoolkit-native.exe` process and start a fresh plugin runtime.
Never terminate `WINWORD.EXE` as recovery. Reconnect to the intended document and
re-inspect its content/version before any further mutation, because the cancelled COM
call may still have completed inside Word.

The packaged MCP timeout is 180 seconds. That timeout is a supervisor boundary, not a
guarantee that Word itself released the underlying COM call.
