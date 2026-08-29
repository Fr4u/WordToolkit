---
name: wordtoolkit
description: Control real Microsoft Word and inspect, edit, compare, validate, render, patch, or merge saved Word OOXML packages through the native .NET bridge. Use for live Word documents, DOCX packages, native equations, formatting, fields, comments, review, structures, export, and guarded changes.
---

# WordToolkit

Use WordToolkit as a narrow execution engine, not as a catalog to read up front. Pick one route, load only its reference, and perform the smallest verified workflow that completes the request.

## Fast route

1. Classify the request before calling tools:
   - Live Word creation or editing: read [references/live-word.md](references/live-word.md).
   - Native equations, LaTeX conversion, direct OMML, or complete TeX documents: also read [references/equations-and-tex.md](references/equations-and-tex.md).
   - Saved DOCX inspection, querying, comparison, validation, or rendering: read [references/saved-packages.md](references/saved-packages.md).
   - Plan/apply edits, repair, patching, merge, signatures, OCR, or external providers: read [references/guarded-edits-and-providers.md](references/guarded-edits-and-providers.md).
2. Never read all references by default. Load only the route needed for the current request.
3. If an exact public action is known, call it directly. Do not search for it.
4. If an action is unknown, call `search_wordtoolkit_actions` once with a capability phrase. The top match includes the executable input contract and first-call guidance, but deliberately omits `outputSchema`. Inspect only when you need a non-top candidate or must reason about exact output fields, effects, or the complete contract.
5. Keep `response_mode="compact"`. Request `full` only when omitted details are necessary for the immediately following decision.

## Common recipes

### Create a Word document

`create_live_word_document` -> one coherent `apply_live_word_operations` batch -> `save_live_word_document` -> render or export when appearance matters. Inspect only when the apply response does not prove the postcondition needed for the next step.

Use `lifecycle="scratch"` for temporary work and omit `output_path`. Use `lifecycle="persistent"` with an explicit output path for a deliverable. Do not create a scratch document as a substitute for opening a user file.

### Edit an existing Word document

`open_live_word_document` -> inspect current state/version -> acquire a fresh selector/range token when the action requires it -> apply one bounded mutation -> verify state/content -> save.

### Inspect a saved DOCX without Word

Call `inspect_ooxml_package` directly only for structural package facts. For semantics, security, fields, relationships, validation, or rendering, use the dedicated route in [references/saved-packages.md](references/saved-packages.md); use `analyze_ooxml_document` when a broad audit must choose the next narrow action.

### Discover a rare capability

Call `search_wordtoolkit_actions` with one precise phrase such as `insert dropdown`, `compare documents`, or `repair numbering`. Execute the returned top contract directly. Call `inspect_wordtoolkit_action` only for a lower-ranked result or when the full contract is required.

Never execute a returned `apply_ooxml_*` action unless the exact reviewed plan and every required fingerprint/token are already in context. Without that evidence, select the matching plan workflow first even if an apply action appears in search results.

## Non-negotiable safety rules

- Never guess argument names, action names, document IDs, versions, fingerprints, range tokens, plan IDs, patch IDs, or review tokens. Obtain them from the current runtime response.
- Bind every live mutation to the latest `expected_version`. After a successful mutation, use the returned version for the next one.
- Treat version-bound and range-bound tokens as single-use unless the action explicitly says otherwise. Any document mutation makes earlier tokens suspect.
- Bind saved-package apply calls to the current file fingerprint and the exact reviewed plan. Re-inspect after any file change.
- Never bypass guarded actions with raw COM, raw XML, macros, or a broad script when WordToolkit has a typed action.
- Use no-clobber output paths by default for saved-package transforms and exports. `save_live_word_document` may persist the already-connected document at its current path; do not redirect or overwrite any other source unless the user requested it and the typed action supports it safely.
- Closing a document, quitting Word, overwriting a file, replacing active content, accepting/rejecting review items, and removing signatures are explicit side effects. Do not infer permission from a general editing request.
- If an action returns `ROLLBACK_FAILED`, `ROLLBACK_SNAPSHOT_UNSTABLE`, quarantine, or an uncertain commit state, stop writing. Inspect the current document/package before retrying.
- A timeout is not proof of failure. Query operation status or inspect document version/content before retrying; blind retry can duplicate content.

## Efficiency rules

- Batch adjacent deterministic text and formatting operations. One coherent batch is faster and easier to roll back than dozens of tiny calls.
- Do not preflight plain text-only batches. Preflight equation-heavy or risky mixed batches when failure would be expensive.
- Prefer 10-25 operations for equation-heavy live batches. Split very large COM workloads at logical boundaries.
- Reuse stable document facts, but never reuse stale version/fingerprint/token bindings.
- For long operations, pass an idempotency key when supported and poll `get_live_word_operation_status` instead of resubmitting.
- Use bounded page/range queries. Do not request the entire semantic tree when one section, table, bookmark, or page answers the question.
- Return only facts needed by the user: artifact path, mutation result, version, validation result, and unresolved limitations. Do not dump schemas or internal hashes unless they explain a failure.
- In action guidance, `success.required_paths` describes the complete `response_mode="full"` output contract. A compact response may intentionally omit telemetry such as `runtime`, `python_used`, or `performance`; that omission is not failure. For compact responses, verify the returned success predicates and the action-specific postcondition. Request `full` only when an omitted path is needed for the next decision.

## Live mutation contract

Before mutation, know the target `live_document_id` and current version. For selector-based actions, acquire the exact fresh range/selection token demanded by the schema. Apply one logical change, then verify the action-specific postcondition. Examples:

- Text insertion: expected text/runs exist at the intended location and paragraph counts are plausible.
- Formatting: read back the targeted formatting, not only the operation status.
- Equation insertion: equation count increases and native verification succeeds.
- Tables, dropdowns, SmartArt, comments, and review actions: verify native object type/count plus title, tag, or identity where applicable.
- Save/export: returned path exists and the saved package or rendered output passes the requested validation.

A text operation uses exactly one of non-empty `text` or non-empty `runs`; do not provide both. Every run needs non-empty text, and the runtime concatenates the runs into the paragraph text.

## Equations and visual quality

Use native OfficeMath when the user needs editable Word equations. A success flag alone is insufficient: verify native equation objects and render the final document or PDF when layout matters. Word-compatible LaTeX is a subset of LaTeX; use equation preflight for risky syntax and report rejected source with the failed operation index.

Use `input_format="latex"` for an isolated editable equation. Use `compile_tex_document` only for a complete TeX document with class/package context; it returns PDF, not OfficeMath. For exact Word structures unsupported by the converter, use reviewed direct OMML through the typed WordToolkit path described in the equation reference.

## Saved-package guarded changes

Inspection is read-only. Mutation starts with a reviewed plan bound to the current source fingerprint and destination. Apply exactly that plan, verify the output package, and use a new destination for retries. Typed repair or review actions may impose stronger tokens; follow the returned guidance exactly.

Use independent validation when the deliverable is important:

- OOXML/package checks establish structural validity.
- Microsoft Open XML SDK validation establishes schema validity.
- Word open/save/export establishes native compatibility.
- Rendered page inspection establishes layout quality.

None of these alone proves all the others.

## Success and recovery

Before claiming success, require all applicable predicates:

- the action reports success;
- the expected version or package fingerprint changed exactly once;
- the intended content/object exists;
- native or schema validation passes when requested;
- the saved artifact exists at the exact path;
- rendered output was inspected when appearance is part of acceptance.

On failure, preserve the first concrete cause, `failed_operation_index`, Word/SDK message, and rollback state. Do not hide a simple formula or schema error behind a generic recovery error. If state is uncertain, inspect before any retry.
