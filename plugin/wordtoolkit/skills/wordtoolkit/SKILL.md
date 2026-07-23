---
name: wordtoolkit
description: Control real Microsoft Word and inspect, index, query, edit, compare, patch, or three-way merge saved Word OOXML packages through a token-lean native .NET bridge. Use for live documents, package/semantic inspection, fields, bookmarks, reference dependencies, semantic selectors, formatting, equations, comments and review, structures, export, save, close, and validation.
---

# WordToolkit

Use the small core catalog directly. Rare actions are lazy: search by capability
with `search_wordtoolkit_actions`, inspect only the chosen action, then execute
it. If the exact action name is already known, skip search. Keep
`response_mode=compact`; request `full` only when omitted details are required
for the next operation.

When runtime/schema compatibility, supported operation discovery, effect hints or
hard limits matter, call `get_wordtoolkit_capabilities` first. Filter and page its
bounded summaries; do not fetch all action schemas. This call opens no Word instance,
reads no document and returns no document content. Use the reported contract and schema
hashes to detect drift, then inspect only the selected action.
If a client must validate the response shape, request
`get_wordtoolkit_capabilities` with `view=schema`; do not infer a schema from the hash.

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
Use strict structural predicates instead of fetching a tree for the model to walk.
For a paragraph containing an equation, send
`{"kinds":["paragraph"],"descendant":{"kinds":["equation"]}}`. For an
equation anywhere inside a table cell, send
`{"kinds":["equation"],"ancestor":{"kinds":["table_cell"]}}`. A related
predicate may combine `kinds` and `property_equals`; all supplied conditions must
hold on the same related node. `ancestor` and `descendant` never match the result
node itself. They do not mean parent/child, adjacent sibling, or arbitrary graph
reachability. Keep property comparisons exact and never request raw XML merely to
reconstruct ancestry already represented by these predicates. Set `max_results` to
the smallest useful page and use `text_preview_chars=0` when stable IDs and kinds are
enough for the next operation.
Use the lazy `render_ooxml_semantic_html` action when a saved Word package needs a
self-contained structural preview, accessibility inspection artifact, or diff companion
without opening Word. Supply a new `.html` `output_path`; existing files are never
overwritten. Keep `story_scope=main_document` unless headers, footers, notes, comments,
or glossary text are genuinely needed. When only one table, row, cell, paragraph,
equation, drawing, revision or other semantic subtree is needed, first obtain its exact
`node_id` and `package_fingerprint` through semantic query, then pass that ID as
`target_node_id` together with the fingerprint as `expected_package_fingerprint`.
Never guess an ID or reuse it after the package changes. Selected and nested tables,
rows, cells and pure row/cell wrapper chains receive synthetic valid HTML table context;
inspect `fragment_wrapper` and warnings instead of treating wrappers as source content.
Ambiguous mixed chains fail closed. The local artifact contains document text, but the MCP
response returns only hashes, counts, warnings and bounded selection metadata. Treat
`semantic_preview_non_paginated` literally: links are inert, fields expose cached result
content rather than instructions, tracked changes are annotated, equations are linear
text fallbacks, and drawings or unsupported extensions are visible placeholders. Never
use this artifact as evidence of Word pagination, font substitution, line wrapping,
drawing geometry, or print fidelity.
For two or more queries over the same unchanged package, first execute lazy
`manage_ooxml_semantic_index` with `operation=create`. Reuse its
`semantic_index_id` and exact `package_fingerprint` in every
`query_ooxml_semantics` call, then release the handle immediately with
`operation=release`. The index exists only in native process memory, holds at
most 100,000 semantic nodes, expires within 30 minutes, and never survives a
runtime restart. At most four indexes and 250,000 total cached nodes are
allowed. Do not create an index for a single query or assume it tracks file
changes; create returns the same handle only when the path and package
fingerprint are unchanged.
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
Use lazy `inspect_ooxml_references` instead of loading field-code runs or pairing
bookmark markers yourself. Start with `view=summary`, then filter by exact
`field_type`, `bookmark_name`, or `story_id`. Names, instructions, cached results and
dependency keys are redacted by default; request parsed or sensitive detail only when
the next decision consumes it. Treat DDE, LINK, INCLUDE, IMPORT, DATABASE and other
external/automation fields as inert evidence. This inspector never evaluates them,
starts an application or follows a target. Unresolved, duplicated or malformed ranges
are damage signals, not permission to guess what Word would display.
Use lazy `inspect_ooxml_dependencies` when the task asks what depends on a part,
semantic object, style, numbering definition, field target or section story. Start with
`view=summary`; use `view=nodes` only to obtain one stable `wddn_` ID, then request a
bounded `impact` neighborhood or filtered `edges`/`unresolved` page. Keep keys and source
metadata redacted unless the next operation consumes them. The graph joins the
explicitly reported OPC, semantic-containment, style, numbering, reference, section,
classic-chart, content-control, physical/built-in XML-store, binding-target and
repeating-section domains plus nested-table and vertical-merge topology. Its
`explicitly_unmodeled_domains` list is a hard coverage
boundary: absence of an edge for drawings, SmartArt, OLE, bibliography, active content,
signatures, encryption or co-authoring is not proof that the dependency does not exist.
This action never opens Word, executes a field, follows an external target, repairs a
document or authorizes deleting an apparently unused node.
Use lazy `inspect_ooxml_content_controls` instead of reading `w:sdt`, `dataBinding`,
`customXml` or item-properties XML yourself. Start with `view=summary`; page `controls`,
`stores`, `bindings`, `targets`, `repeating_sections` or `issues` only when the next
decision consumes those objects. Filter with one exact `wccc_`, `wccs_` or `wccb_` ID.
Aliases, tags, placeholder names and section titles require `include_names=true`.
Store GUIDs, XPath, prefix mappings, namespace/schema names and target element names
require the separate `include_binding_details=true` opt-in. Source parts, semantic/native
IDs and XML ordinals require `include_source=true`. Custom XML values and raw XML are
never returned. XPath resolution deliberately supports only an absolute child-element
subset with optional positive positions; an unsupported expression is evidence, not
permission to run a general XPath engine or guess the target. The action is read-only,
does not refresh bound display text and never opens Word. Honor nested `*_truncated`
flags instead of assuming one binding response contains every target or item ID.
Use lazy `inspect_ooxml_tables` instead of reading `w:tbl`, `tblGrid`, `trPr` or `tcPr`
yourself. Start with `view=summary`; page `tables`, `rows`, `cells`, `merges` or `issues`
only when the next decision consumes them. Narrow with an exact `wdt_`, `wdtr_` or
`wdtc_` ID. The default response contains topology but no cell text, style ID, caption,
description, layout coordinates or source path. Request names, layout and source through
their three independent opt-ins. Cell text and raw XML are never returned. Treat
grid overflow/underflow, orphan or span-mismatched vertical continuations, noncontiguous
header declarations and out-of-range floating coordinates as damage evidence, not
permission to guess or silently normalize the table. `hMerge` remains a separately
reported legacy state. Declared widths are preferences, not guaranteed rendered widths;
final autofit, page layout, conditional table styles and rendering remain outside this
read graph. Honor nested-ID and grid-width truncation flags.
Use lazy `inspect_ooxml_charts` instead of opening chart XML or embedded workbooks.
Start with `view=summary`; it returns only aggregate counts and plot families. Page
`charts`, `series`, `axes` or `relationships` only when the next decision needs them,
and filter by an exact `chart_id` or `chart_type`. Chart titles and formulas are
redacted by default. Request `include_sensitive=true` only when their bounded text is
necessary, and `include_source=true` only for part or relationship provenance. Cached
point values are never returned, even with sensitive detail enabled. External targets
and embedded packages are never opened. Classic Transitional and Strict DrawingML
charts are modeled; Office 2016 extended charts remain preserved but explicitly
unmodeled. This inspector is read-only evidence, not permission to edit a chart or its
workbook.
Use lazy `inspect_ooxml_markup_compatibility` before interpreting or changing markup
that contains `mc:*` attributes, `mc:AlternateContent`, or unfamiliar extension
namespaces. Start with `view=summary`; page `parts`, `rules`, `alternate_content`,
`affected`, `must_understand`, or `issues` only when the next decision needs them. Pass
`understood_namespaces` only for namespaces the target application is proven to
understand. Pass `application_defined_extension_elements` only from a known markup
configuration; never guess opaque islands from an `ext`-looking name. Namespace URIs
and affected local names are redacted by default, and source paths/hashes/ordinals use
a separate opt-in. The action evaluates and reports the ECMA-376 Part 3 fifth-edition
model without preprocessing or rewriting the package. Legacy `PreserveElements` and
`PreserveAttributes` hints are inventoried, not executed as current-edition rules.
Use lazy `lint_ooxml_document` for a bounded quality or safety audit of a saved package.
Start with `view=summary` and one rule pack when the task is narrow; request paged
`findings` only after the counts show relevant evidence. Keep `include_source=false`
unless an exact part, XML ordinal, byte span or relationship ID will drive the next
decision. The current core, styles, accessibility and security packs have stable
`WTL_` rule IDs and package-bound `wtlint_` finding IDs. Suppress only a reviewed rule
or finding ID. Keep fix metadata off unless a reviewed finding actually needs its
safety/blocking evidence. Keep `analysis_execution_complete` separate from
`document_coverage_complete`: an empty finding list is not a clean bill of health when
coverage omissions or explicitly unmodeled domains remain. Fix metadata with
`implemented=false` is evidence for a future repair, not permission to mutate XML or
claim that the issue was repaired. This action never opens Word, follows an external
target or changes the package.
The only current `implemented=true` lint fix is `set_document_title` for exactly one
existing, empty, lexically safe `dc:title`. Use this strict workflow:

1. Retain the exact package fingerprint and `wtlint_` finding ID from
   `lint_ooxml_document` with `include_fix=true`.
2. Call `plan_ooxml_lint_repair` with that fingerprint and finding, explicit
   `repair_kind=set_document_title`, the reviewed title, and a new same-extension output
   path. Keep `include_details=false` unless a validation block must be diagnosed.
3. Review `lint_repair_apply_plan_id`, `apply_blocked`, block codes, target-finding
   resolution and baseline-versus-candidate Open XML validation. The title is hashed,
   not echoed, in the response.
4. Call `apply_ooxml_lint_repair` with the identical source, output, fingerprint,
   finding, repair kind and title plus the exact returned apply-plan ID.

Never use that path for a missing title element, duplicate titles, mixed-markup title,
signed package or another lint rule. The actions fail closed, never open Word and never
overwrite the source or an existing output.
Use lazy `inspect_ooxml_equations` for equations already stored in a saved Word
package. Start with `view=summary`; it returns structural counts and statuses without
formula text or raw OMML. Use `view=equations` to obtain an exact equation ID, then
`view=nodes` with that ID and an optional `node_kind` to page the canonical OfficeMath
graph. Request `detail=properties` or `include_source=true` only when the next decision
uses them. A positive `text_preview_chars` requires `include_sensitive=true`; otherwise
text remains absent and only a short fingerprint is exposed. The action is parse-only:
it does not open Word, convert notation, fetch external content, repair malformed math,
or prove that two notations are mathematically equivalent.
Use lazy `inspect_ooxml_review` for comments and tracked changes already stored in a
saved Word package. Start with `view=summary`, then page only the required comments,
threads, revisions, moves, permissions, people, settings or issues. Filter by exact
comment/revision ID, story, revision kind or author fingerprint before requesting
detail. Text and personal values are fingerprinted/redacted by default; a bounded text
preview requires both `include_sensitive=true` and positive `text_preview_chars`.
Source metadata is separately opt-in and raw XML is never returned. The inspector is
parse-only: it does not open Word, accept/reject changes, resolve comments, merge review
state or mutate the package.
Use lazy `compare_ooxml_semantics` for two saved DOCX/DOCM/DOTX/DOTM snapshots. Start
with `view=summary`; do not load all differences just to learn whether anything changed.
Retain both returned package fingerprints and keep `package_equivalent`,
`semantically_equivalent` and `matching_complete` separate. If semantic detail is needed,
request `view=changes` with narrow node/change/story filters and page with `next_offset`.
Request `view=entries` only to explain package or opaque-part drift, and
`view=diagnostics` when matching is incomplete or projected changes remain unclassified.
Text and property values require explicit sensitive opt-in; hashes and source locations
are separate opt-ins. Never call two documents identical when package evidence differs,
matching is incomplete, or `unclassified_projected_entry_count` is nonzero. This action
does not open Word, mutate either file, return raw XML, create a patch/merge, or produce a
tracked-change comparison document.
For a portable saved-package patch, use this strict lazy workflow:

1. Call `plan_ooxml_patch` with the original and target files. Keep the compact summary
   unless a bounded `operations` or `risks` page is needed. Retain both fingerprints and
   `patch_id`.
2. Review semantic counts, matcher completeness, risk counts, default block codes and
   the independent required authorization names. Do not confuse semantic equivalence
   with package equivalence.
3. Call `create_ooxml_patch` with both exact fingerprints, the reviewed patch ID and a
   new `.wtpatch` path. Existing artifacts are never overwritten.
4. Before mutation, call `plan_ooxml_patch_apply` with the destination fingerprint,
   artifact and patch ID. Review `apply_plan_id`, risk evidence, baseline/candidate Open
   XML validation, hard blocks and the exact authorization flags required. The apply-plan
   ID is bound to this destination path, and the result package type must match its file
   extension.
5. Call `apply_ooxml_patch` only with that exact base fingerprint, patch ID and apply-plan
   ID. Set only the individual risk authorizations the user accepted and keep the
   recovery backup by default.

Use `inspect_ooxml_patch` when only artifact integrity or a bounded operation page is
needed. It never returns payload bytes or raw XML. A `.wtpatch` is exact for OPC entry
names and uncompressed before/after payloads, not for ZIP compression metadata or record
layout. Signature invalidation, macros/OLE/ActiveX, external relationships, opaque binary
payloads and new structural errors are separate gates; never collapse them into a broad
force flag. Validation truncation or inability to open the candidate is non-overridable.
These actions do not open Word.
For a three-way merge of saved Word packages, use this strict lazy workflow:

1. Supply a real common ancestor, left branch, right branch and a new output path to
   `plan_ooxml_merge`. Keep `view=summary` first. Retain all three fingerprints,
   `merge_id` and `merge_apply_plan_id`.
2. If conflicts exist, request bounded `view=conflicts` pages. Leave text previews and
   hashes off unless the next decision needs them. A one-sided or identical entry change
   is automatic; disjoint text changes in one XML part are automatic only when each
   branch reconstructs byte-exactly from the ancestor. Everything else remains a
   conflict.
3. Resubmit explicit resolutions by stable `conflict_id`, choosing only
   `use_ancestor`, `use_left` or `use_right`. Review the new merge/apply-plan IDs,
   remaining conflict count, resulting patch/risk evidence, Open XML validation, hard
   blocks and exact authorization names. Never infer that an unresolved conflict was
   accepted.
4. Call `apply_ooxml_merge` with the same paths and resolutions, all three exact
   fingerprints, and the destination-bound `expected_merge_apply_plan_id`. Set only
   authorizations the user accepted. The output must not exist; merge never overwrites
   an input or destination.

The merge path does not open Word, expose payloads/raw XML or retain document content in
a server cache. Output-type mismatch, unresolved conflicts, validation truncation and
failure to open the candidate are non-overridable. Arbitrary structural OOXML and
revision-aware merge are not implemented; they must stay as conflicts rather than be
flattened into a plausible-looking but damaged document.
For a saved-package tracked-revision decision, use this strict lazy workflow:

1. Inspect only the required revisions and retain the exact package fingerprint,
   stable revision IDs or redacted author fingerprints.
2. Call `plan_ooxml_review_decisions` with an explicit `accept` or `reject` decision.
   Select by IDs/fingerprints, or deliberately set `select_all=true`; an empty implicit
   all-selection is forbidden. Use `allow_cascade=true` only after reviewing nested or
   paired-move dependencies.
3. Review `plan_id`, counts, byte delta, `can_apply`, block codes and the baseline-versus-
   candidate Microsoft Open XML validation result. Request details only if a block must
   be diagnosed.
4. Call `apply_ooxml_review_decisions` with selectors that reproduce the same resolved
   revision decisions, the original package fingerprint and exact returned plan ID. Keep
   the recovery backup by default.

The saved-package path never opens Word or returns author names/text. It fails closed for
unsupported paragraph merges, table-grid or vertical-merge reconstruction, rejected
legacy numbering changes, custom XML and conflicting nested decisions. When Word itself
must resolve one of those structures, connect to the live document and use the guarded
`manage_live_word_review` workflow instead.
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
Prefer an object-specific saved-package edit whenever one exists.

When the requested text is inside a comment, do not query technical `w:t` node IDs and
do not use the generic text-edit action. Inspect only the required comment to retain its
stable `comment_id`, then use this body-only workflow:

1. Call `plan_ooxml_comment_body_edits` with `replace_comment_body_text`, the exact
   `comment_id`, bounded `find_text`, replacement, and exact `expected_match_count`.
   Add `expected_body_sha256` when it was returned by an earlier reviewed plan.
2. Review the deterministic plan ID, comment/match/text-node counts, candidate validation
   and block reasons. Leave details off unless hashes or changed-part evidence are needed.
3. Call `apply_ooxml_comment_body_edits` with identical commands, the original package
   fingerprint and exact plan ID. Keep the recovery backup by default.

This operation can match across adjacent Word runs in the same ordinary direct comment
paragraph, but never across paragraph/table-cell, tab, break, field, content-control or
other rich structural boundaries. It changes only editable text leaves inside the selected
comment definitions. It returns no comment text or raw XML and verifies that
anchors, authors, threads, durable IDs, reactions, permissions, revisions, unselected
comments and every unrelated part remain unchanged. Duplicate comment targets, unexpected
match counts, signatures, plan drift and new schema errors fail closed.

For other saved-package text edits, use this strict lazy workflow:

1. Query the narrowest possible `text` nodes and retain the package fingerprint.
2. Call `plan_ooxml_text_edits` with node IDs, replacements, and exact
   `expected_text` whenever it is known.
3. Review `plan_id`, counts, byte delta, and any `apply_blocked` reason.
4. Only after approval, call `apply_ooxml_text_edits` with the identical commands,
   original fingerprint, and returned plan ID. Keep the recovery backup unless the
   user explicitly accepts its removal.

Never bypass the plan with raw XML. Signed packages are intentionally blocked; do
not attempt to strip or invalidate a signature through another action.

For a saved-package style definition or assignment, use this strict lazy workflow:

1. Retain the exact package fingerprint. For a surgical edit, query only the target
   `paragraph`, `run`, or `table` nodes and keep their explicit current `style_id`. For
   a bulk edit already expressible as strict kind/text/property/ancestor/descendant/
   subtree/story predicates, do not download node IDs: use one server-side selector.
2. Inspect the narrow style type with `inspect_ooxml_styles`. To reuse a style, use its
   exact `style_id`, not its visible name. To add a definition, choose one new ID and
   either use `create_style` with a type plus optional `based_on_style_id`, `next_style_id`,
   quick-format flag and UI priority, or `clone_style` with one exact existing source ID.
   A clone preserves the source definition's modeled and opaque formatting but becomes
   custom, non-default and unlinked. To remove a proven exact duplicate, use
   `consolidate_style` with explicit `source_style_id` and `target_style_id`; never infer
   either ID from visible names and never use consolidation as fuzzy style matching. To
   remove a proven-unused custom, non-default definition, use `delete_unused_style` with
   its exact `style_id` only after narrow style/dependency inspection. A linter finding is
   evidence, not authorization: the semantic plan must independently prove that every
   modeled and unmodeled consumer is absent. To change only the visible primary name of a
   custom, non-default definition, use `rename_style` with its exact stable `style_id` and
   new `name`. Never invent a replacement ID and never use the visible name as the ID.
   Do not send XML or formatting-property
   fragments.
3. Put definition commands and assignments in the same batch when the new style must be
   used immediately. Send exact nodes as `set_style`, or use `set_style_where` with a single
   `paragraph`/`run`/`table` kind, strict optional predicates, and an explicit
   `max_matches` ceiling. Add `expected_style_id` when every selected node must retain
   one current style, or `require_no_explicit_style=true` when every match must still
   have none. One batch resolves at most 200 operations and contains at most 16 selector
   commands.
4. Review the deterministic `plan_id`, changed counts, `style_consolidation_count`,
   `style_deletion_count`, `style_rename_count`, `style_reference_update_count`, candidate Open XML validation,
   `can_apply`, and block reasons. Request details only to diagnose a block or audit one
   definition operation.
5. Call `apply_ooxml_semantic_edits` with the identical commands, original fingerprint,
   and exact plan ID. Keep the recovery backup by default.

`set_style_where` rejects zero matches and any result above `max_matches`; its exact
selector intent, not JSON property order, is bound into the reviewed plan ID. It cannot
select “missing property” directly: `require_no_explicit_style` is a precondition on all
nodes selected by the other predicates, not a hidden absence filter.

The current command family can create a minimal inherited paragraph, character, table,
or numbering style, clone one existing definition, safely consolidate an explicit exact
custom duplicate, delete an explicitly selected proven-unused custom non-default style,
rename only the primary visible name of an explicitly selected custom non-default style,
and assign compatible paragraph, character, or table styles. Creation
does not accept arbitrary formatting properties; clone is the lossless path for retaining
an existing definition's formatting. Consolidation requires a custom non-default source,
an existing same-type target and full canonical equivalence after identity normalization.
It updates recognized references across projected stories, revisions, glossary metadata,
styles and numbering, then removes the source with an exact inverse. Deletion permits a
closed batch of mutually dependent unused definitions but blocks every surviving semantic,
style, numbering, glossary, latent-style, `STYLEREF` or unmodeled XML consumer. Linked
paragraph/character pairs require one explicit equivalent batch. Non-equivalence, chains,
existing style/numbering graph damage, unmodeled XML consumers, matching latent-style
exceptions, unsafe `STYLEREF`, macros, `altChunk`, automatic linked-template updates and
`stylesWithEffects` all fail closed. Rename preserves the stable `styleId`, aliases,
formatting and ID-based references; collisions and name-addressed fields block rather than
being rewritten. The family still does not change style IDs, delete referenced or built-in
definitions, or perform fuzzy repair, infer document roles, align to a template, or implement
conditional table-style semantics. Planning writes nothing; apply blocks signatures, stale
sources, changed intent, plan drift and new Microsoft Open XML validation errors. Neither
action opens Word or returns XML/document text.

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
For an integral, write an explicit differential such as
`\int f(x)\,\mathrm{d}x`; `\,d x`, `\operatorname{d}x`, and `\dd x` are also
accepted. Use the exact field name `input_format`, never `source_format`.
WordToolkit canonicalizes the differential and groups the complete n-ary operand.
Use `\left\|u\right\|` for a norm. `\mathcal`, `\mathfrak`, `\mathbb`,
`\mathsf`, `\mathtt`, and simple alphanumeric `\mathrm` preserve their native Word
math alphabet. `\mathbf{...}` and `\boldsymbol{...}` preserve their native bold or
bold-italic weight across nested fractions, radicals, scripts, delimiters and n-ary
objects. Presentation MathML preserves inherited or token-level `mathvariant` and OMML
preserves all four `m:sty` values (`p`, `b`, `i`, `bi`) plus structural-control bold and
italic properties. Contextual Arabic `initial`, `tailed`, `looped` and `stretched`
MathML variants fail closed because this linear Word path cannot represent them
losslessly.
Sensitive equations force bounded OMML readback and rollback on structural drift;
only differentials belonging to integral operands are required to remain under the
matching n-ary body, so ordinary derivative notation remains valid. Raw OMML is never
returned. Keep the default compact response. Request
`response_mode="full"` only to diagnose the exact converted Word linear form.
Do not confuse live equation insertion with saved-package equation inspection. The
former asks Word to create professional OMath; the latter reads existing OMML into a
bounded semantic graph and deliberately performs no conversion or mutation.

Use fresh selection, range, review, and undo tokens exactly where the inspected
schema requires them. Never invent IDs, versions, tokens, paths, styles, or
capability IDs. Never bypass optimistic version checks. Never invoke raw
macros, DDE, arbitrary COM member names, or raw Undo. Never overwrite DOCX;
overwrite PDF only when explicitly requested with `overwrite=true`.
