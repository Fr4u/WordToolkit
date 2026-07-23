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
    (schema_dir / "mcp-tools.v2.json").write_text(
        json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
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
    (schema_dir / "draft-operations.v1.json").write_text(
        json.dumps(draft_contract, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    lines = [
        "# MCP tool catalog",
        "",
        "The current remote Python service source of truth is `schemas/mcp-tools.v2.json`; `schemas/mcp-tools.v1.json` remains the immutable historical contract. The provider-neutral heterogeneous mutation contract, including executable input/success/error examples, is generated as `schemas/draft-operations.v1.json`. The native Windows plugin has a separate, deliberately hand-reviewed source in `schemas/mcp-tools-local.v1.json`; `WordToolkit.Native.Tests` validates that catalog and this exporter never overwrites it. Every exported remote tool has an object JSON Schema, MCP side-effect annotations and a stable error envelope.",
        "",
        "| Tool | Read only | Destructive | Idempotent | File inputs |",
        "|---|---:|---:|---:|---|",
    ]
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
    (docs / "TOOL-CATALOG.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(
        f"exported {len(tools)} remote tools; "
        "native local schema is validated by WordToolkit.Native.Tests"
    )


if __name__ == "__main__":
    asyncio.run(main())
