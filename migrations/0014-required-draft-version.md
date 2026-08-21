# Remote MCP v1 to v2: required draft version

## Why this is breaking

Remote schema v1 declared `expected_version` optional on existing-draft mutations and omitted it from save, repair, render, preview and close. Omitting the value bypassed optimistic concurrency, so two clients could both write from version 0 and the later call silently operated on the first client's result. Making the precondition optional would preserve the defect; there is no safe compatibility shim.

Remote package 0.40.0 therefore publishes `schemas/mcp-tools.v2.json`. The v1 file remains immutable for historical comparison. The unified version avoids reusing the already released 0.17.0 identifier from the repository changelog.

## Client migration

1. Store `draft_version` returned by create/open and every successful mutating or publishing call.
2. Send that value as `expected_version` on every edit, save, repair, render, preview and close of the existing draft.
3. Replace the stored value only after a successful response. A successful edit/save/render advances it by exactly one.
4. On `VERSION_CONFLICT`, discard the planned write, re-read the required document state and construct a new operation against the returned `actual` version. Do not blindly retry the stale payload.
5. `export_document` with `output_format="docx"` also requires `expected_version` and advances the draft because pre-save repairs may be committed. `output_format="markdown"` is read-only, does not advance the version and may omit the field.
6. Send the current version to `close_document`; close itself does not increment it.
7. After transport cancellation or timeout, re-read the draft before retrying. WordToolkit drains an already-started engine call under the document lock; if that mutation completed, the server advances the version even though the caller did not receive its response.

The machine schema marks the field as required. Missing, boolean, fractional or string values return WordToolkit's bounded `INVALID_INPUT` envelope instead of leaking framework validation text. A supplied stale integer returns the stable `VERSION_CONFLICT` envelope with `expected`, `actual` and `retryable=true`.

## Publication semantics

Save, repair, DOCX export, render, page render, PDF conversion and preview now operate on an isolated copy-on-write engine. Validation, renderer work and all artifact copies must succeed before the active engine is replaced and `draft_version` advances. A failed attempt leaves the active engine, version, current path and artifact inventory unchanged and removes its attempt outputs. Background calls never outlive the document lock, including repeated cancellation.

No stored DOCX migration is required. The change affects only the remote operation contract and in-memory draft session protocol. The native Windows plugin keeps its independently versioned local schema.
