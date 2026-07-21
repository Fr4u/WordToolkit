from __future__ import annotations

import argparse
import asyncio
import json
import os
from pathlib import Path

from mcp import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client

ROOT = Path(__file__).resolve().parents[1]


def _payload(result) -> dict:
    if result.structuredContent:
        return result.structuredContent
    for item in result.content:
        text = getattr(item, "text", None)
        if not text:
            continue
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            continue
    raise RuntimeError("MCP tool returned no structured payload")


async def smoke_test(plugin: Path) -> dict[str, object]:
    parameters = StdioServerParameters(
        command="uv",
        args=[
            "run",
            "--isolated",
            "--project",
            "./runtime",
            "--frozen",
            "wordtoolkit-stdio",
        ],
        cwd=plugin,
        env={
            **os.environ,
            "PYTHONDONTWRITEBYTECODE": "1",
            "WORDTOOLKIT_AUTH_MODE": "local_stdio",
            "PYTHONUTF8": "1",
            "VIRTUAL_ENV": "",
        },
    )
    async with stdio_client(parameters) as (read, write), ClientSession(read, write) as session:
        initialized = await session.initialize()
        tools = await session.list_tools()
        created = _payload(await session.call_tool("create_document", {}))
        document_id = created["data"]["document_id"]
        validated = _payload(
            await session.call_tool("validate_ooxml", {"document_id": document_id})
        )
        exported = _payload(
            await session.call_tool(
                "export_document",
                {
                    "document_id": document_id,
                    "file_name": "WordToolkit-local-smoke.docx",
                },
            )
        )
        preview = _payload(
            await session.call_tool(
                "generate_preview",
                {"document_id": document_id, "max_pages": 2, "dpi": 96},
            )
        )

    validation = validated["data"]["validation"]
    official = validation["validators"]["microsoft_openxml_sdk"]
    result = {
        "server": initialized.serverInfo.name,
        "tools": len(tools.tools),
        "validation_valid": validation["valid"],
        "validator_available": official["available"],
        "validator_valid": official["valid"],
        "docx_uri": exported["data"]["artifact"]["download_url"],
        "preview_uris": [artifact["download_url"] for artifact in preview["data"]["artifacts"]],
        "preview_pages": preview["data"]["page_count"],
        "visual_audit": preview["data"]["visual_audit"]["passed"],
    }
    if result["tools"] != 103:
        raise RuntimeError(f"Expected 103 tools, received {result['tools']}")
    if not result["validation_valid"] or not result["validator_valid"]:
        raise RuntimeError("Generated DOCX failed structural or Open XML SDK validation")
    if not result["preview_pages"] or not result["visual_audit"]:
        raise RuntimeError("Preview rendering or visual audit failed")
    uris = [result["docx_uri"], *result["preview_uris"]]
    if not all(str(uri).startswith("file:///") for uri in uris):
        raise RuntimeError("Local plugin returned a non-local artifact URI")
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description="Exercise the built local plugin over MCP STDIO")
    parser.add_argument("--plugin", type=Path, default=ROOT / "dist" / "wordtoolkit")
    args = parser.parse_args()
    print(json.dumps(asyncio.run(smoke_test(args.plugin.resolve())), indent=2))


if __name__ == "__main__":
    main()
