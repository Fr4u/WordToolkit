#!/usr/bin/env python3
from __future__ import annotations

import asyncio
import json
from pathlib import Path

from wordtoolkit.config import Settings
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
    lines = [
        "# MCP tool catalog",
        "",
        "The current remote Python service source of truth is `schemas/mcp-tools.v2.json`; `schemas/mcp-tools.v1.json` remains the immutable historical contract. The native Windows plugin has a separate, deliberately hand-reviewed source in `schemas/mcp-tools-local.v1.json`; `WordToolkit.Native.Tests` validates that catalog and this exporter never overwrites it. Every exported remote tool has an object JSON Schema, MCP side-effect annotations and a stable error envelope.",
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
