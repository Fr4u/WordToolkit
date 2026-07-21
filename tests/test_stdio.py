from __future__ import annotations

import json
from pathlib import Path
from urllib.parse import unquote, urlparse

import pytest

from wordtoolkit.config import Settings
from wordtoolkit.runtime import ToolRuntime
from wordtoolkit.server.stdio import build_stdio_server
from wordtoolkit.server.tools import OpenAIFile

LIVE_TOOLS = {
    "list_live_word_documents",
    "connect_live_word_document",
    "inspect_live_word_document",
    "map_live_word_structures",
    "inspect_live_word_structure_items",
    "inspect_live_word_equation_learning",
    "inspect_live_word_structure_learning",
    "inspect_live_word_object_model_types",
    "inspect_live_word_object_model_members",
    "inspect_live_word_member_capabilities",
    "preflight_live_word_member_operations",
    "execute_live_word_member_operations",
    "get_live_word_selection",
    "find_live_word_text",
    "replace_live_word_text",
    "inspect_live_word_review",
    "manage_live_word_review",
    "diagnose_live_word_layout",
    "inspect_live_word_undo",
    "undo_live_word_operation",
    "insert_live_word_text",
    "format_live_word_selection",
    "insert_live_word_table",
    "preflight_live_word_table_formulas",
    "insert_live_word_table_formulas",
    "update_live_word_table_fields",
    "insert_live_word_list",
    "preflight_live_word_bookmarks",
    "insert_live_word_bookmarks",
    "preflight_live_word_fields",
    "insert_live_word_fields",
    "insert_live_word_equation",
    "insert_live_word_equations_batch",
    "preflight_live_word_equations",
    "apply_live_word_operations",
    "validate_live_word_document",
    "save_live_word_document",
    "disconnect_live_word_document",
}


@pytest.mark.asyncio
async def test_live_word_tools_exist_only_on_local_stdio(tmp_path: Path) -> None:
    server = build_stdio_server(
        Settings(auth_mode="local_stdio", storage_root=tmp_path / "storage")
    )
    mapping = {tool.name: tool for tool in await server.list_tools()}

    assert mapping.keys() >= LIVE_TOOLS
    for name in LIVE_TOOLS:
        assert mapping[name].annotations.openWorldHint is True
    assert mapping["insert_live_word_text"].annotations.destructiveHint is True
    assert mapping["find_live_word_text"].annotations.readOnlyHint is True
    assert mapping["replace_live_word_text"].annotations.destructiveHint is True
    assert mapping["inspect_live_word_review"].annotations.readOnlyHint is True
    assert mapping["manage_live_word_review"].annotations.destructiveHint is True
    assert mapping["diagnose_live_word_layout"].annotations.readOnlyHint is True
    assert mapping["inspect_live_word_undo"].annotations.readOnlyHint is True
    assert mapping["undo_live_word_operation"].annotations.destructiveHint is True
    assert mapping["format_live_word_selection"].annotations.destructiveHint is True
    assert mapping["insert_live_word_table"].annotations.destructiveHint is True
    assert mapping["preflight_live_word_table_formulas"].annotations.readOnlyHint is True
    assert mapping["insert_live_word_table_formulas"].annotations.destructiveHint is True
    assert mapping["update_live_word_table_fields"].annotations.destructiveHint is True
    assert mapping["insert_live_word_list"].annotations.destructiveHint is True
    assert mapping["inspect_live_word_structure_items"].annotations.readOnlyHint is True
    assert mapping["inspect_live_word_object_model_types"].annotations.readOnlyHint is True
    assert mapping["inspect_live_word_object_model_members"].annotations.readOnlyHint is True
    assert mapping["inspect_live_word_member_capabilities"].annotations.readOnlyHint is True
    assert mapping["preflight_live_word_member_operations"].annotations.readOnlyHint is True
    assert (
        mapping["execute_live_word_member_operations"].annotations.destructiveHint
        is True
    )
    assert mapping["preflight_live_word_bookmarks"].annotations.readOnlyHint is True
    assert mapping["insert_live_word_bookmarks"].annotations.destructiveHint is True
    assert mapping["preflight_live_word_fields"].annotations.readOnlyHint is True
    assert mapping["insert_live_word_fields"].annotations.destructiveHint is True
    assert mapping["insert_live_word_equation"].annotations.destructiveHint is True
    assert mapping["preflight_live_word_equations"].annotations.readOnlyHint is True
    assert mapping["apply_live_word_operations"].annotations.destructiveHint is True


@pytest.mark.asyncio
async def test_stdio_server_can_create_and_export_local_document(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("WORDTOOLKIT_AUTH_MODE", "local_stdio")
    server = build_stdio_server(
        Settings(
            auth_mode="local_stdio",
            storage_root=tmp_path / "storage",
            public_base_url="http://127.0.0.1",
        )
    )

    created = await server.call_tool("create_document", {})
    payload = json.loads(created[0].text)
    document_id = payload["data"]["document_id"]
    exported = await server.call_tool(
        "save_document", {"document_id": document_id, "file_name": "local.docx"}
    )
    result = exported.structuredContent
    uri = result["data"]["artifact"]["download_url"]
    parsed_path = unquote(urlparse(uri).path)
    if parsed_path.startswith("/") and parsed_path[2:3] == ":":
        parsed_path = parsed_path[1:]

    assert uri.startswith("file:///")
    assert Path(parsed_path).is_file()


@pytest.mark.asyncio
async def test_local_stdio_accepts_explicit_local_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("WORDTOOLKIT_AUTH_MODE", "local_stdio")
    runtime = ToolRuntime(Settings(auth_mode="local_stdio", storage_root=tmp_path / "storage"))
    session = await runtime.session("local-test")
    source = tmp_path / "input.docx"
    source.write_bytes(b"not-a-real-docx")
    reference = OpenAIFile(local_path=str(source), file_name=source.name)

    copied = await runtime.download_file(reference, session, extensions={".docx"})

    assert copied.read_bytes() == source.read_bytes()
    assert copied.resolve().is_relative_to(session.root.resolve())
