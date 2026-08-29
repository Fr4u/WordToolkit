# Live Word authoring

Read this reference for a new Word document, an already-open document, cursor or
selection edits, formatting, tables, lists, fields, comments, SmartArt, captions,
indexes, save, export, or close operations.

## Fast lifecycle

For a new document:

1. Use `create_live_word_document` and retain `live_document_id` plus `live_version`.
2. Build a coherent section in memory and send one `apply_live_word_operations` batch.
3. Save with the current version when persistence was requested.
4. Export through Word when PDF or page images were requested.
5. Disconnect. Close or quit Word only when the user explicitly requested it.

For an existing document:

1. Use `list_live_word_documents`, then `connect_live_word_document`, or use
   `open_live_word_document` for one explicit local path. Never guess the document.
2. Use `inspect_live_word_document` only for state needed by the edit.
3. Acquire a fresh selection/range token immediately before a cursor-bound edit.
4. Mutate with the current `expected_version`; retain the new version.
5. Save/export if requested, then disconnect.

`lifecycle="scratch"` creates an invisible unsaved document, rejects `output_path`,
and closes without saving on disconnect. Persistent documents do not auto-close.

## Authoring efficiently

- Use `apply_live_word_operations` for ordinary text, inline runs, paragraph formatting,
  and interleaved native equations. Do not stream words, sentences, table cells, or
  equations through separate calls.
- A text operation accepts exactly one of non-empty `text` or non-empty `runs`; never pass
  both. Use `runs` when one paragraph needs distinct emphasis. Every run needs non-empty
  text; the runtime concatenates them. Put paragraph formatting on the parent operation.
- Character formatting covers every bounded writable scalar `Word.Font` property,
  including subscript/superscript, all 18 `underline_style` values and underline color,
  script-specific fonts, bidi emphasis, effects, spacing/scaling/position/kerning and
  OpenType typography. Use exact inspected enum names; do not guess raw COM integers.
- Canonical formatting keys include `font_size_pt` (1..1638) and
  `paragraph_alignment`. Compatibility aliases `font_size` and `alignment` are accepted
  only when the canonical key is absent. Deprecated boolean `underline` means single
  underline and cannot be combined with `underline_style`.
- `clear_character_formatting=true` resets direct Font formatting and highlight before
  applying sibling fields; it does not reset paragraph formatting. A successful live
  selection response includes canonical `formatting_readback` and
  `native_formatting_verified=true`.
- Do not preflight ordinary text. Use `preflight_live_word_operations` only for an exact
  risky mixed batch; pass the unchanged operations array to apply after it succeeds.
- For a long apply, set one stable `idempotency_key`. If the caller stops waiting, query
  `get_live_word_operation_status` or replay the exact same request. Do not invent a new
  key while the original receipt is pending.
- Include `_meta.progressToken` on long calls. Progress values are strictly ordered.

Complex equation-heavy work is more reliable in logical groups rather than one enormous
chapter. Prefer roughly 10–25 complex equation operations per batch when Word COM cost is
material; ordinary text can remain in much larger coherent batches within the schema
limit.

## Targeted live actions

Use the exact known action directly. Search once only when the action or schema is unknown.

- Text search or replacement: `find_live_word_text`, `replace_live_word_text`.
- Existing equation inspection/update: `inspect_live_word_equations`, then
  `update_live_word_equation` with the returned index, token, and version.
- Table creation and formulas: `insert_live_word_table`,
  `preflight_live_word_table_formulas`, `insert_live_word_table_formulas`,
  `update_live_word_table_fields`.
- Dropdowns: obtain one fresh non-overlapping `range_token` per target, then
  `insert_live_word_dropdowns`.
- Comments and revisions: `inspect_live_word_review`, then `manage_live_word_review`
  with fresh review tokens. A new comment also needs a fresh non-empty selection.
- Images, notes, headers, and lists: search the exact capability only if its action is not
  already known from the public tool list or this reference.
- SmartArt creation: `inspect_live_word_smartart_layouts`, then
  `insert_live_word_smartart` with one version-bound layout token and one fresh target
  token. For existing node text, inspect live drawing layout, prepare SmartArt edits,
  then apply the returned one-time tokens.
- Captions, contents, figures, authorities, and indexes: use their dedicated actions.
  Never compose raw `SEQ`, `TOC`, `TA`, `XE`, or `INDEX` field instructions.

Table expression formulas are bounded same-table A1 arithmetic. Use parameter cells for
rates and thresholds; WordToolkit does not certify changing tax or payroll law.

## Safety and recovery

- Every mutation needs the current `expected_version`. Any mutation invalidates older
  selection, range, equation, review, SmartArt, and undo tokens.
- `VERSION_CONFLICT` means inspect again and acquire fresh state. Do not retry with an old
  token.
- `STAGING_TARGET_DRIFT` means inspect the connected document before deciding whether to
  retry.
- `WORD_OPERATION_OUTCOME_UNKNOWN` means query the receipt; do not submit a second batch.
- `ROLLBACK_FAILED` is a quarantine boundary. Stop editing, do not reconnect the same
  document, and use `disconnect_live_word_document` only as acknowledgement after human
  inspection.
- Never call raw Undo, raw COM members, macros, DDE, or XML mutation as a shortcut.

## Save, render, and finish

`save_live_word_document` persists the current path and does not advance the content
version. For connected or unsaved content use `export_live_word_artifacts`; do not reopen
the saved package through the fixed renderer. Word PDF is authoritative only for the
installed Word build and current fonts/layout environment.

For a serious document, acceptance is: requested content exists, native object counts are
correct, save/export returned the exact artifact path, the file exists, validation has no
new errors, and rendered pages were inspected. Retain the returned artifact fingerprint or
hash when the action supplies one. Structural validity alone is not visual QA.
