# WordToolkit 0.61.4 release qualification

Date: 2026-08-29

Runtime: `0.61.4+codex.20260829104544`

## Confirmed inconsistencies and fixes

- The AI router said `lifecycle="owned"`, but both the native schema and C# runtime accept
  only `persistent` and `scratch`. This was a real pre-COM `INVALID_INPUT` path. The router
  now uses `lifecycle="persistent"` for saved deliverables.
- A new test reads the current `create_live_word_document` enum from
  `mcp-tools-local.v2.json`, extracts every explicit lifecycle value from the skill and its
  references, and rejects undocumented values. It does not duplicate the enum in test
  code.
- The full packaged Word gate now passes `lifecycle="persistent"` explicitly instead of
  receiving it only as a default.
- Guidance semantics now state that `success.required_paths` belongs to the complete
  `response_mode="full"` output contract. Compact omission of telemetry is not failure;
  compact success uses returned predicates and action-specific postconditions.
- Ambiguous guarded mutations previously ranked apply before plan by lexical tie-break.
  `patch rollback`, `merge package`, and `semantic edits` now rank their reviewed plan
  first, while an explicit `apply` token preserves apply-first intent. `patch apply` is
  explicitly apply-first even though `plan_ooxml_patch_apply` is a phrase match; an
  explicit `plan patch apply` remains plan-first. Eight regression cases cover both
  directions, and the router blocks apply without an existing reviewed plan and exact
  bindings.
- Two old chronological checkpoint statements were changed from present tense so their
  15-tool/85-action evidence cannot be mistaken for the current 17-tool/157-action line.

The audit also rejected two false positives. Compact TeX responses do retain
`diagnostics`, `provider`, and the provider-safety fields required by TeX success guidance.
`compile_tex_document` legitimately returns `VERSION_CONFLICT` when its create-new output
already exists or is won by a concurrent publisher, so its new-output recovery is valid.

## Static and automated qualification

- Native action registry: 157 unique actions; 13 unique core actions; no missing core
  action or duplicate.
- Skill action-name scan: 58 referenced actions; zero unknown action-like names.
- Recovery routing: every `next_action` exists.
- Metadata, action-guidance and schema-export generation checks: passed.
- Plugin and skill validators: passed.
- Python suite: 1,584 passed, 17 intentionally skipped, one Starlette deprecation warning.
- Engine tests: 939/939 passed on the pinned .NET SDK 8.0.423.
- Native tests: 944/944 passed.
- LibreOffice tests: 12/12 passed.
- Lifecycle/plugin contract tests: 3/3 passed after the fix.

## Real Microsoft Word qualification

The exact final packaged candidate completed the full-live gate in 163.893 seconds:

- 153 MCP requests;
- 17 exposed tools and 157 available actions;
- 59 positive live actions plus one destructive confirmation guard;
- explicit persistent lifecycle creation;
- 70 paragraphs, one table, 12 native equations, one comment, one footnote, one endnote,
  one inline image and three sections;
- save, close, open and reconnect passed;
- Microsoft Open XML validation passed;
- 193,846-byte Word PDF export passed;
- the test document was not left open and the runtime-owned Word process quit cleanly.

An earlier pre-review candidate gate attempt failed closed at guarded Undo because Word exposed a
non-WordToolkit entry at the top of its external Undo stack. No unsafe Undo was attempted,
cleanup passed, and an identical clean rerun completed the full gate. This transient is
reported rather than counted as a deterministic runtime success.

The three A4 PDF pages from the successful final run were rasterized at 144 DPI and visually inspected. No clipping,
overlap, blank page, unreadable glyph, raw equation control syntax or broken reference
table was observed. This is a functional acceptance fixture, not a polished template.

## Reproducible artifact

Two independent package builds contained 204 files and produced byte-identical ZIPs:

- asset: `WordToolkit-0.61.4+codex.20260829104544-native-win-x64.zip`;
- size: 38,476,379 bytes;
- SHA-256: `8ef03604b581578fc1ba92e460a1d63f6cc3b1c0d221c91b4557d9ffb3c4910e`;
- executable SHA-256:
  `61573766f12c4ddd3869fa862676aae33d18e92fc6420605d9048aa14855ac09`;
- runtime assembly SHA-256:
  `3df863985e36d1953a03e01fa19091343a8ce3f0ffda45dd06ba7b57878ba7fc`.

The archive is a self-contained Windows x64 .NET plugin and contains no Python runtime.
