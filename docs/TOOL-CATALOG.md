# MCP tool catalog

The current remote Python service source of truth is `schemas/mcp-tools.v2.json`; `schemas/mcp-tools.v1.json` remains the immutable historical contract. The provider-neutral heterogeneous mutation contract, including executable input/success/error examples, is generated as `schemas/draft-operations.v1.json`. The native Windows plugin has a separate, deliberately hand-reviewed source in `schemas/mcp-tools-local.v1.json`; `WordToolkit.Native.Tests` validates that catalog and this exporter never overwrites it. Every exported remote tool has an object JSON Schema, MCP side-effect annotations and a stable error envelope.

The native catalog currently contains 112 actions behind 15 core/gateway tools. Rare
saved-package inspectors remain lazy so their schemas do not enter model context until
needed. `inspect_wordtoolkit_extensions` exposes the bounded, content-free registry
catalog without loading assemblies, reading a document or opening Word.
`inspect_wordtoolkit_observability` exposes only opt-in, content-free runtime health and
bounded audit events; arguments, paths, document content and relationship targets have
no response field, while correlation IDs and record hashes require separate opt-ins.
`convert_ooxml_flat_opc` is a lazy, create-new transport operation shared by
Engine, CLI and MCP. It converts Word OPC packages to or from bounded Flat OPC XML,
blocks signatures, never opens Word, verifies semantic/relationship parity before
publication and returns only hashes, counts and filenames.
`inspect_ooxml_active_content` is read-only and closed-world: it inventories
typed OLE/ActiveX/VBA/embedded-package/customization/signature metadata without opening
Word, payloads or external targets. Its names, relationship targets, hashes and source
locations require independent opt-ins; raw XML and active-content values are unavailable.
`inspect_ooxml_properties` separately models core, extended and custom properties. It
validates package reachability, declared scalar types and custom identity, redacts custom
names and every value by default, and never evaluates a field or decodes a complex value.
`inspect_ooxml_diagrams` models native SmartArt data, points, connections and related
layout/style/color/persisted-drawing parts. It never executes layout or returns point
text or raw XML; model keys, keyed fingerprints and source provenance are separate
opt-ins. `inspect_live_word_drawing_layout` is the complementary connected-Word action.
It asks Word to repaginate and returns bounded reference-aware
shape/inline/group/SmartArt object layout without COM or XML. Text is not read without
opt-in; screen pixels are capped, viewport-dependent and never called page geometry.
`prepare_live_word_smartart_text_edits` and `apply_live_word_smartart_text_edits` add a
narrow live mutation path for node text. Tokens bind one node to the complete
Word-executed SmartArt structure and text context; apply performs exact readback in one
Undo record and rolls back if Word changes structure, an untargeted node or the requested
text. Node creation/deletion/reordering and layout/style/color mutation remain unsupported.
`insert_live_word_caption`, `insert_live_word_table_of_figures` and
`insert_live_word_table_of_contents` are guarded live Word mutations. The first two
resolve localized built-in or exact existing custom caption labels. The contents action
accepts only semantic heading levels, source flags and presentation options. They create
native Word fields, verify collection counts and exact field ranges and roll back on
mismatch. None accepts raw field instructions or returns generated table/caption text.
`mark_live_word_authority_citation` binds one fresh non-empty selection or range to one
native `TA` entry in category 1..16. `insert_live_word_table_of_authorities` creates one
native `TOA` for an exact category or all categories. It defaults to a real tab with
dotted leaders, verifies every separator/display option by native readback and rolls the
single custom Undo record back on mismatch. Neither action returns citation text,
separator values, generated table text, field instructions or COM objects.
`mark_live_word_index_entry` binds one fresh selection or range to a native `XE` field,
supports a bounded hierarchy, cross-reference, existing bookmark page range and bold or
italic page numbers, and verifies the exact generated field. `insert_live_word_index`
creates one native default-type `INDEX` from existing complete entries and verifies
heading separation, layout, columns, accented-letter handling and tab leader by native
readback. Neither action accepts raw field code or returns entry/index text.
`update_live_word_reference_tables` refreshes existing native tables of contents,
figures, authorities and indexes through Word. It selects all supported objects or one
exact kind and one-based index, caps a transaction at 128 objects, optionally repaginates,
verifies stable collection counts and readable field ranges, and rolls the custom Undo
record back on any mismatch. It never accepts or returns field instructions or generated
table text.
`plan_ooxml_format` and `apply_ooxml_format` are also lazy. They expose one explicit,
bounded policy that removes scalar direct formatting proven redundant against the
resolved cascade. Font, color, underline and shading elements additionally require a
bounded candidate-by-candidate package reparse and group-equivalence proof; unresolved
table/revision/unmodeled cascade layers are skipped. The actions validate the cumulative
isolated candidate and create only a new output. They do not open Word, return document
content, overwrite a file or turn XML pretty-printing into a false claim of document
formatting.

| Tool | Read only | Destructive | Idempotent | File inputs |
|---|---:|---:|---:|---|
| `create_document` | False | False | False | — |
| `create_from_template` | False | False | False | file |
| `create_from_markdown` | False | False | False | file |
| `open_document` | False | False | False | file |
| `inspect_document` | True | False | True | — |
| `save_document` | False | False | False | — |
| `close_document` | False | True | False | — |
| `get_outline` | True | False | True | — |
| `get_sections` | True | False | True | — |
| `get_paragraph` | True | False | True | — |
| `insert_paragraph` | False | False | False | — |
| `replace_paragraph` | False | False | False | — |
| `delete_paragraph` | False | True | False | — |
| `move_block` | False | False | False | — |
| `list_styles` | True | False | True | — |
| `create_style` | False | False | False | — |
| `update_style` | False | False | False | — |
| `apply_style` | False | False | False | — |
| `inspect_direct_formatting` | True | False | True | — |
| `normalize_formatting` | False | False | False | — |
| `format_paragraph` | False | False | False | — |
| `format_run` | False | False | False | — |
| `manage_lists` | False | False | False | — |
| `insert_caption` | False | False | False | — |
| `list_tables` | True | False | True | — |
| `get_table` | True | False | True | — |
| `insert_table` | False | False | False | — |
| `modify_table` | False | False | False | — |
| `merge_cells` | False | False | False | — |
| `split_cells` | False | False | False | — |
| `set_cell_properties` | False | False | False | — |
| `insert_equation` | False | False | False | — |
| `replace_equation` | False | False | False | — |
| `list_equations` | True | False | True | — |
| `get_equation` | True | False | True | — |
| `convert_equation` | True | False | True | — |
| `validate_equations` | True | False | True | — |
| `number_equations` | False | False | False | — |
| `add_equation_reference` | False | False | False | — |
| `manage_headers_footers` | False | False | False | — |
| `manage_footnotes_endnotes` | False | False | False | — |
| `manage_comments` | False | False | False | — |
| `manage_bookmarks` | False | False | False | — |
| `manage_cross_references` | False | False | False | — |
| `manage_fields` | False | False | False | — |
| `insert_image` | False | False | False | file |
| `manage_sections` | False | False | False | — |
| `enable_track_changes` | False | False | False | — |
| `list_tracked_changes` | True | False | True | — |
| `insert_tracked_change` | False | False | False | — |
| `accept_changes` | False | True | False | — |
| `reject_changes` | False | True | False | — |
| `apply_document_operations` | False | True | False | files |
| `compare_documents` | False | False | False | base_file, revised_file |
| `validate_ooxml` | True | False | True | — |
| `audit_document` | True | False | True | — |
| `detect_corruption` | True | False | True | — |
| `repair_document` | False | False | False | — |
| `check_accessibility` | True | False | True | — |
| `check_layout_risks` | True | False | True | — |
| `detect_orphaned_relationships` | True | False | True | — |
| `render_document` | False | False | False | — |
| `render_pages` | False | False | False | — |
| `convert_to_pdf` | False | False | False | — |
| `export_document` | False | False | False | — |
| `generate_preview` | False | False | False | — |

## Optimistic concurrency

Every operation that mutates, saves, repairs, renders or closes an existing draft requires a non-negative `expected_version`. A successful mutation or publication advances `draft_version` exactly once. Missing or stale versions are rejected before document-engine mutation or output/artifact publication. A cancelled background engine call is drained before the document lock is released; successful late completion advances the version. Failed save/render attempts run against an isolated copy-on-write engine and leave the active engine, version, current path and artifact set unchanged.

`export_document` is conditional: DOCX export requires `expected_version` because it performs the same repair-and-save transaction, while best-effort Markdown export is read-only and may omit it. See `migrations/0014-required-draft-version.md`.

## Error contract

Tool failures set MCP `isError: true` and return the same JSON object in text and structured content:

```json
{"ok":false,"error":{"code":"OOXML_INVALID","message":"...","details":{},"retryable":false}}
```

Input-validation failures, missing OAuth scopes, version conflicts, unsafe packages, renderer failures and internal boundary errors use distinct stable codes. Internal tracebacks, document text, credentials and server paths are not returned.
