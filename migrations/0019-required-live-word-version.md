# Local MCP v1 to v2: required Word Live version

## Why this is breaking

The local v1 schema exposed `expected_version` as optional on several Word Live
writes. The Python and native bridges therefore treated an omitted value as a
disabled optimistic-concurrency check for text, formatting, tables, lists,
fields, equations, images, comments, notes, headers, footers and persistence.
That fail-open behavior allowed a caller to write against state it had never
observed.

The native plugin now publishes `schemas/mcp-tools-local.v2.json` and keeps
`schemas/mcp-tools-local.v1.json` unchanged for historical comparison. There is
no safe compatibility shim for an omitted precondition.

## Client migration

1. Read `live_version` when opening, creating, connecting to or inspecting a
   live document.
2. Send that integer as `expected_version` on every unconditional Word Live
   write and on `save_live_word_document`.
3. Replace the stored version only after a successful content mutation.
   Persistence alone does not increment `live_version`.
4. On `VERSION_CONFLICT`, inspect the document again and rebuild the operation;
   never retry the stale payload blindly.
5. A read-only `execute_live_word_member_operations` batch may omit the field.
   If any selected capability mutates Word, the field is mandatory.

Missing versions now return `INVALID_INPUT` before any COM attachment or write.
Supplied stale versions return `VERSION_CONFLICT`. The runtime repeats the
version check inside the COM callback so a change between preflight and
execution is also rejected.
