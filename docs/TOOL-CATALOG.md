# MCP tool catalog

The current remote Python service source of truth is `schemas/mcp-tools.v2.json`; `schemas/mcp-tools.v1.json` remains the immutable historical contract. The provider-neutral heterogeneous mutation contract, including executable input/success/error examples, is generated as `schemas/draft-operations.v1.json`. The native Windows plugin has a separate, deliberately hand-reviewed source in `schemas/mcp-tools-local.v1.json`; `WordToolkit.Native.Tests` validates that catalog and this exporter never overwrites it. Every exported remote tool has an object JSON Schema, MCP side-effect annotations and a stable error envelope.

The native catalog currently contains 93 actions behind 15 core/gateway tools. Rare
saved-package inspectors remain lazy so their schemas do not enter model context until
needed. `inspect_ooxml_active_content` is read-only and closed-world: it inventories
typed OLE/ActiveX/VBA/embedded-package/customization/signature metadata without opening
Word, payloads or external targets. Its names, relationship targets, hashes and source
locations require independent opt-ins; raw XML and active-content values are unavailable.
`inspect_ooxml_properties` separately models core, extended and custom properties. It
validates package reachability, declared scalar types and custom identity, redacts custom
names and every value by default, and never evaluates a field or decodes a complex value.

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
