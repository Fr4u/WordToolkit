#!/usr/bin/env python3
from __future__ import annotations

import asyncio
import json
from pathlib import Path

from wordtoolkit.config import Settings
from wordtoolkit.draft_operations import (
    DRAFT_BATCH_MAX_ARGUMENT_BYTES,
    DRAFT_BATCH_MAX_FILES,
    DRAFT_BATCH_MAX_OPERATIONS,
    OPERATION_CONTRACT,
    DraftBatchOutcome,
)
from wordtoolkit.errors import ErrorCode
from wordtoolkit.server.app import build_app

ROOT = Path(__file__).resolve().parents[1]
PUBLIC_GATEWAY_NAMES = (
    "get_wordtoolkit_capabilities",
    "search_wordtoolkit_actions",
    "inspect_wordtoolkit_action",
    "execute_wordtoolkit_action",
)


async def main() -> None:
    app = build_app(Settings(storage_root=ROOT / ".schema-storage"))
    tools = await app.state.wordtoolkit_mcp.list_tools()
    payload = {
        "schema_version": "2.0.0",
        "mcp_protocol": "2025-06-18",
        "compatibility_policy": "Additive changes within v2; breaking changes require a new major schema file and migration note.",
        "tools": [tool.model_dump(mode="json", by_alias=True, exclude_none=True) for tool in tools],
    }
    schema_dir = ROOT / "schemas"
    schema_dir.mkdir(exist_ok=True)
    with (schema_dir / "mcp-tools.v2.json").open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(json.dumps(payload, indent=2, ensure_ascii=False) + "\n")
    batch_tool = next(tool for tool in tools if tool.name == "apply_document_operations")
    draft_contract = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "contract": OPERATION_CONTRACT,
        "description": "Provider-neutral ordered mutation contract implemented by the remote Python draft adapter.",
        "input_schema": batch_tool.inputSchema,
        "success_data_schema": DraftBatchOutcome.model_json_schema(mode="serialization"),
        "error_schema": {
            "type": "object",
            "additionalProperties": False,
            "required": ["ok", "error"],
            "properties": {
                "ok": {"const": False},
                "error": {
                    "type": "object",
                    "additionalProperties": False,
                    "required": ["code", "message", "details", "retryable"],
                    "properties": {
                        "code": {"enum": [code.value for code in ErrorCode]},
                        "message": {"type": "string", "maxLength": 500},
                        "details": {"type": "object"},
                        "retryable": {"type": "boolean"},
                    },
                },
            },
        },
        "permissions": ["documents:write"],
        "file_binding": {
            "top_level_field": "files",
            "operation_reference": "insert_image.arguments.file_index",
            "mcp_apps_meta": {"openai/fileParams": ["files"]},
        },
        "limits": {
            "operations_min": 1,
            "operations_max": DRAFT_BATCH_MAX_OPERATIONS,
            "files_max": DRAFT_BATCH_MAX_FILES,
            "aggregate_argument_bytes_max": DRAFT_BATCH_MAX_ARGUMENT_BYTES,
        },
        "side_effects": {
            "document": "All operations commit together on one process-local copy-on-write draft or none commit.",
            "image_staging": "Partial downloads are removed on failure or cancellation; verified complete uploads may remain against session quota if a later operation fails.",
        },
        "examples": {
            "input": {
                "document_id": "doc_example",
                "expected_version": 7,
                "operations": [
                    {
                        "operation": "format_paragraph",
                        "arguments": {"paragraph_id": "1A2B3C4D", "alignment": "center"},
                    },
                    {
                        "operation": "insert_paragraph",
                        "arguments": {
                            "after_paragraph_id": "1A2B3C4D",
                            "text": "A bounded new paragraph.",
                        },
                    },
                ],
            },
            "success_data": {
                "document_id": "doc_example",
                "draft_version": 8,
                "results": [
                    {"index": 0, "result": {}},
                    {
                        "index": 1,
                        "result": {"para_id": "5E6F7A8B"},
                    },
                ],
            },
            "error": {
                "ok": False,
                "error": {
                    "code": "VERSION_CONFLICT",
                    "message": "Draft version changed before the mutation",
                    "details": {"expected": 7, "actual": 8},
                    "retryable": True,
                },
            },
        },
    }
    draft_contract["examples"] = [draft_contract["examples"]]
    with (schema_dir / "draft-operations.v1.json").open(
        "w", encoding="utf-8", newline="\n"
    ) as handle:
        handle.write(json.dumps(draft_contract, indent=2, ensure_ascii=False) + "\n")
    native_schema = json.loads((schema_dir / "mcp-tools-local.v2.json").read_text(encoding="utf-8"))
    native_action_count = len(native_schema["native_runtime"]["actions"])
    native_core_count = len(native_schema["native_runtime"]["core_actions"])
    native_gateway_count = len(PUBLIC_GATEWAY_NAMES)
    public_mcp_tool_count = native_core_count + native_gateway_count
    lines = [
        "# MCP tool catalog",
        "",
        "The current remote Python service source of truth is `schemas/mcp-tools.v2.json`; `schemas/mcp-tools.v1.json` remains the immutable historical contract. The provider-neutral heterogeneous mutation contract, including executable input/success/error examples, is generated as `schemas/draft-operations.v1.json`. The native Windows plugin has a separate, deliberately hand-reviewed source in `schemas/mcp-tools-local.v2.json`; `schemas/mcp-tools-local.v1.json` remains the historical local contract. `WordToolkit.Native.Tests` validates the current catalog and this exporter never overwrites it. Every exported remote tool has an object JSON Schema, MCP side-effect annotations and a stable error envelope.",
        "",
    ]
    lines.extend(
        f"""
The public MCP surface has {public_mcp_tool_count} tools: {native_core_count} core actions and {native_gateway_count} capability gateways.
The capability, search, inspect and execute gateways negotiate the versioned contract
and lazily expose {native_action_count} native actions.

`local_path` and `output_path` are deliberate roles, not aliases: the former names an
existing input package and the latter names a newly written artifact. Likewise, the
gateway dispatcher field is `action`; `action_name` is not a compatibility spelling.

Live Word formatting uses canonical `font_size_pt` and `paragraph_alignment` keys.
Compatibility spellings `font_size` and `alignment` are normalized before COM and
cannot be supplied together with their canonical counterpart. The inspected action
schema enumerates and types every formatting field before execution.

## First-call guidance

For an unknown capability, search first; inspect the exact action; bind every
prerequisite and acquire missing IDs, versions or fingerprints; review the
minimal template example; execute; then verify the documented success paths.
On failure, follow the action's recovery mapping (refresh stale bindings,
re-plan when required, and stop on rollback or quarantine boundaries). This
guidance is generated for all {native_action_count} native actions and checked for
parity by `scripts/generate_action_guidance.py --check`.

Rare saved-package inspectors remain lazy so their schemas do not enter model context until
needed. `inspect_wordtoolkit_extensions` exposes the bounded, content-free registry
catalog, including process-memory limits for hard process-boundary capabilities, without
loading assemblies, reading a document or opening Word.
`inspect_wordtoolkit_observability` exposes only opt-in, content-free runtime health and
bounded audit events; arguments, paths, document content and relationship targets have
no response field, while correlation IDs and record hashes require separate opt-ins.
`inspect_ooxml_encryption` detects bounded Standard, Agile, Extensible or malformed
encrypted OOXML compound envelopes. It accepts no password, decrypts nothing, opens no
Word process and returns no path, stream name or document content.
`inspect_ooxml_signatures` verifies bounded OPC signature topology, supported XMLDSIG
signature values, package-part digests and Relationship Transform subsets. It performs no
network access, chain building or revocation lookup and returns no content, raw XML,
certificate bytes, signer identity or local path. Certificate hashes and OPC source URIs
are independent opt-ins; an integrity-valid result is not a signer-trust decision.
`inspect_ooxml_numbering` now exposes a versioned `view=sequences` in addition to its
definition inventory and single-level resolver. It executes source-ordered paragraph
counters per story and `numId`, separates exact counter and label evidence, and pages
stable paragraph/item/sequence IDs without returning paragraph text. Word restart,
legal-numbering and section-break behavior are explicit; unsupported locale/custom labels,
picture bullets and ambiguous revision/MCE views fail visibly instead of being guessed.
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
`inspect_live_word_version_profile` reads the connected Word version/build, document
compatibility/save format and four property-access probes without content, paths, user or
licence identity. It never infers a product edition from ambiguous Word 16.0 and never
presents member availability as a behavioral guarantee.
`probe_live_word_feature_behaviors` requires explicit confirmation and tests native OMath,
content-control, SmartArt and custom-Undo behavior in four separate invisible unsaved
scratch documents. Success requires close-without-save, original active document/window
restoration and an unchanged open-document count; cleanup uncertainty quarantines the
connected handle.
`prepare_live_word_smartart_text_edits` and `apply_live_word_smartart_text_edits` add a
narrow live mutation path for node text. Tokens bind one node to the complete
Word-executed SmartArt structure and text context; apply performs exact readback in one
Undo record and rolls back if Word changes structure, an untargeted node or the requested
text. Node creation/deletion/reordering and layout/style/color mutation remain unsupported.
`inspect_live_word_smartart_layouts` adds a bounded paged layout catalog with opaque
document-version-bound tokens; `insert_live_word_smartart` consumes one such token and
one range/selection token for guarded inline insertion with count/readback verification,
one Undo record and rollback.
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
        """.strip().splitlines()
    )
    for tool in tools:
        annotations = tool.annotations
        meta = tool.meta or {}
        file_inputs = ", ".join(meta.get("openai/fileParams", [])) or "—"
        lines.append(
            f"| `{tool.name}` | {bool(annotations and annotations.readOnlyHint)} | "
            f"{bool(annotations and annotations.destructiveHint)} | "
            f"{bool(annotations and annotations.idempotentHint)} | {file_inputs} |"
        )
    lines.extend(
        [
            "",
            "## Optimistic concurrency",
            "",
            "Every operation that mutates, saves, repairs, renders or closes an existing draft requires a non-negative `expected_version`. A successful mutation or publication advances `draft_version` exactly once. Missing or stale versions are rejected before document-engine mutation or output/artifact publication. A cancelled background engine call is drained before the document lock is released; successful late completion advances the version. Failed save/render attempts run against an isolated copy-on-write engine and leave the active engine, version, current path and artifact set unchanged.",
            "",
            "`export_document` is conditional: DOCX export requires `expected_version` because it performs the same repair-and-save transaction, while best-effort Markdown export is read-only and may omit it. See `migrations/0014-required-draft-version.md`.",
            "",
            "## Error contract",
            "",
            "Tool failures set MCP `isError: true` and return the same JSON object in text and structured content:",
            "",
            "```json",
            '{"ok":false,"error":{"code":"OOXML_INVALID","message":"...","details":{},"retryable":false}}',
            "```",
            "",
            "Input-validation failures, missing OAuth scopes, version conflicts, unsafe packages, renderer failures and internal boundary errors use distinct stable codes. Internal tracebacks, document text, credentials and server paths are not returned.",
        ]
    )
    docs = ROOT / "docs"
    docs.mkdir(exist_ok=True)
    with (docs / "TOOL-CATALOG.md").open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")
    print(
        f"exported {len(tools)} remote tools; "
        "native local schema is validated by WordToolkit.Native.Tests"
    )


if __name__ == "__main__":
    asyncio.run(main())
