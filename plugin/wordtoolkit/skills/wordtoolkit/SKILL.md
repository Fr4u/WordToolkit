---
name: wordtoolkit
description: Control real Microsoft Word and inspect or query saved Word OOXML packages through a token-lean native .NET bridge. Use for live documents, package/semantic inspection, semantic selectors, formatting, equations, review, structures, export, save, close, and validation.
---

# WordToolkit

Use the small core catalog directly. Rare actions are lazy: search by capability
with `search_wordtoolkit_actions`, inspect only the chosen action, then execute
it. If the exact action name is already known, skip search. Keep
`response_mode=compact`; request `full` only when omitted details are required
for the next operation.

## Token discipline

- Generate a coherent document section in the model, then send one
  `apply_live_word_operations` batch. Never stream tokens, sentences, table
  cells, list items, or equations through many calls.
- Do not preflight ordinary text. Preflight equations or typed Word objects
  only when syntax is unfamiliar or the batch is risky.
- Do not inspect an advanced action already inspected in the current turn.
- Do not request full responses for confirmation; compact mutation responses
  already include version, counts, native verification, and document state.
- Read bounded ranges or structure pages. Never request document content that
  is not needed for the task.

## Core lifecycle

For a saved DOCX/DOCM/DOTX/DOTM that only needs structural inspection, use
`inspect_ooxml_package` directly. It does not open or start Word, never fetches
external relationships, returns a compact summary by default, and exposes
bounded part metadata only with `include_details=true`.
Use the lazy `inspect_ooxml_semantics` action when meaning is needed without
opening Word. Keep previews and node counts bounded; request source XML paths
only for a precise diagnostic or planned edit.
Use the lazy `query_ooxml_semantics` action when the task needs exact node IDs,
text nodes hidden beneath an outline item, a style/property selector, or a
phrase spanning several runs. Filter narrowly, page with `next_offset`, keep
previews short, and request properties or source provenance only when the next
operation consumes them. The query covers the main body and related header,
footer, footnote, endnote, comment and glossary stories; use `source_part_uri`
when the edit must stay inside one story.
Use lazy `inspect_ooxml_sections` instead of inferring section ownership from
part filenames. Its effective mode resolves default, first-page and even-page
header/footer display targets; request full bindings only when relationship
provenance or inheritance must drive the next operation.
Use lazy `inspect_ooxml_styles` instead of loading `styles.xml` or guessing from
visible names. Filter by style type and keep `detail=metadata` for discovery;
request declared properties, document defaults, latent exceptions, or
base-first inheritance only when the next decision consumes them. Treat an
unresolvable `basedOn` chain as evidence of document damage, not as permission
to invent effective formatting. This action is read-only and reports declarations;
use the dedicated resolvers for numbering, themes, and effective node formatting.
Use lazy `inspect_ooxml_numbering` instead of reading `numbering.xml`. Keep the
default `view=instances` and `detail=metadata` for discovery. Filter by
`number_id` or `abstract_number_id`; request `view=resolved_level` with one
`number_id` and `level_index` when the next decision needs the effective level.
Use `detail=declared` and `include_source=true` only for property or corruption
diagnosis. Treat missing targets, circular style links, mismatched overrides and
out-of-range levels as damage; never invent a list definition.
Use lazy `inspect_ooxml_theme` instead of loading `theme1.xml`. Keep
`view=colors` or `view=fonts` and `detail=metadata` for ordinary decisions;
request declarations, transforms, unknown markup, or source ordinals only when
the next step consumes that evidence. An empty East Asian or complex-script
primary typeface is language-dependent, not permission to substitute a Latin
font. Environmental system colors and DrawingML transform chains that cannot be
resolved deterministically remain explicit diagnostics.
Use lazy `inspect_ooxml_settings` instead of loading `settings.xml`. Start with
`view=summary` or `view=compatibility`; request document variables or mail-merge
metadata only when the task needs them. Values, queries, connection strings and
targets are redacted by default. Protection hashes and salts are never returned,
even with sensitive metadata enabled, because they do not help an ordinary edit.
Treat document protection as an editing restriction, not encryption.
Use lazy `inspect_ooxml_fonts` instead of reading `fontTable.xml` or embedded font
parts. Keep `view=fonts` and metadata detail for discovery; filter by exact font
name when possible. Request embedded-face metadata only when diagnosing portability.
Never ask for font bytes. Hashes and source ordinals are opt-in, and unsupported or
malformed embedded-face relationships remain explicit diagnostics.
After obtaining a paragraph or run ID, use lazy `resolve_ooxml_formatting` only
when a formatting decision needs more than the declared style. Filter
`property_names` aggressively and leave provenance/source disabled unless the
next decision must explain an override. Treat `coverage_omissions` and
`compatibility_warnings` as hard limits: the action resolves modeled defaults,
paragraph styles, an effective numbering level, character styles, direct
properties, settings-driven concrete theme fonts, declared/embedded font-table
metadata, and theme RGB colors. It does not pretend that conditional table styles,
an unmappable locale, Office's private HSL quantization, revision views,
application defaults, or documented Word compatibility edges are final rendering.
Its compact `numbering`, `theme`, `settings` and `font_table` blocks are enough for
most decisions; call the dedicated graph inspectors only when their declarations
or diagnostics are actually needed.
For a saved-package text edit, use this strict lazy workflow:

1. Query the narrowest possible `text` nodes and retain the package fingerprint.
2. Call `plan_ooxml_text_edits` with node IDs, replacements, and exact
   `expected_text` whenever it is known.
3. Review `plan_id`, counts, byte delta, and any `apply_blocked` reason.
4. Only after approval, call `apply_ooxml_text_edits` with the identical commands,
   original fingerprint, and returned plan ID. Keep the recovery backup unless the
   user explicitly accepts its removal.

Never bypass the plan with raw XML. Signed packages are intentionally blocked; do
not attempt to strip or invalidate a signature through another action.

1. `list_live_word_documents`.
2. Use `start_word_application` only when Word is unavailable.
3. Use `create_live_word_document`, `open_live_word_document`, or
   `connect_live_word_document`; never guess a document name or path.
4. Retain `live_document_id` and `live_version`. Pass `expected_version` on
   every mutation.
5. Use `get_live_word_selection` immediately before a cursor/selection edit.
6. Save or export only when requested.
7. Finish with `disconnect_live_word_document`. Close or quit only when the
   user explicitly asks; those actions require their guarded policies.

`apply_live_word_operations` is the default authoring tool. It creates native
text and editable OMath in one Word Undo transaction and rolls back on failure.

## Lazy actions

Search with two or three capability words such as `image`, `find replace`,
`table formula`, `review comment`, `header footer`, `equation preflight`,
`PDF export`, `validate DOCX`, or `close document`. Search returns at most a
small bounded list. Inspect exactly one schema and execute it; never guess its
arguments.

## Equations and safety

Equation inputs may be LaTeX, UnicodeMath, Presentation MathML, or OMML.
Prefer LaTeX for model output. Equations must remain native editable OMath;
never replace them with screenshots or plain-text approximations.

Use fresh selection, range, review, and undo tokens exactly where the inspected
schema requires them. Never invent IDs, versions, tokens, paths, styles, or
capability IDs. Never bypass optimistic version checks. Never invoke raw
macros, DDE, arbitrary COM member names, or raw Undo. Never overwrite DOCX;
overwrite PDF only when explicitly requested with `overwrite=true`.
