from __future__ import annotations

import json

import pytest
from starlette.testclient import TestClient

from wordtoolkit.config import Settings
from wordtoolkit.server.app import build_app

REQUIRED_TOOLS = {
    "create_document",
    "create_from_template",
    "open_document",
    "inspect_document",
    "save_document",
    "close_document",
    "get_outline",
    "get_sections",
    "get_paragraph",
    "insert_paragraph",
    "replace_paragraph",
    "delete_paragraph",
    "move_block",
    "list_styles",
    "create_style",
    "update_style",
    "apply_style",
    "inspect_direct_formatting",
    "normalize_formatting",
    "list_tables",
    "get_table",
    "insert_table",
    "modify_table",
    "merge_cells",
    "split_cells",
    "set_cell_properties",
    "insert_equation",
    "replace_equation",
    "list_equations",
    "get_equation",
    "convert_equation",
    "validate_equations",
    "number_equations",
    "add_equation_reference",
    "manage_headers_footers",
    "manage_footnotes_endnotes",
    "manage_comments",
    "manage_bookmarks",
    "manage_cross_references",
    "manage_fields",
    "insert_image",
    "manage_sections",
    "enable_track_changes",
    "list_tracked_changes",
    "insert_tracked_change",
    "accept_changes",
    "reject_changes",
    "compare_documents",
    "validate_ooxml",
    "audit_document",
    "detect_corruption",
    "repair_document",
    "check_accessibility",
    "check_layout_risks",
    "detect_orphaned_relationships",
    "render_document",
    "render_pages",
    "convert_to_pdf",
    "export_document",
    "generate_preview",
}


@pytest.mark.asyncio
async def test_all_required_tools_have_object_schemas_and_annotations(tmp_path) -> None:
    app = build_app(
        Settings(public_base_url="http://testserver", storage_root=tmp_path / "storage")
    )
    tools = await app.state.wordtoolkit_mcp.list_tools()
    mapping = {tool.name: tool for tool in tools}
    assert mapping.keys() >= REQUIRED_TOOLS
    assert "connect_live_word_document" not in mapping
    assert len(mapping) == len(tools)
    for name, tool in mapping.items():
        assert tool.description, name
        assert tool.inputSchema.get("type") == "object", name
        assert tool.annotations is not None, name
    assert mapping["open_document"].meta["openai/fileParams"] == ["file"]
    assert mapping["compare_documents"].meta["openai/fileParams"] == ["base_file", "revised_file"]


def test_streamable_http_requires_bearer_auth(tmp_path) -> None:
    settings = Settings(public_base_url="http://testserver", storage_root=tmp_path / "storage")
    app = build_app(settings)
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": {
            "protocolVersion": "2025-06-18",
            "capabilities": {},
            "clientInfo": {"name": "contract-test", "version": "1"},
        },
    }
    with TestClient(app) as client:
        denied = client.post("/mcp", json=request, headers={"Origin": "https://chatgpt.com"})
        assert denied.status_code == 401
        headers = {
            "Authorization": f"Bearer {settings.development_bearer_token}",
            "Origin": "https://chatgpt.com",
            "Accept": "application/json, text/event-stream",
            "Content-Type": "application/json",
        }
        allowed = client.post("/mcp", content=json.dumps(request), headers=headers)
        assert allowed.status_code == 200, allowed.text
        assert allowed.json()["result"]["serverInfo"]["name"] == "WordToolkit"

        tool_request = {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "tools/call",
            "params": {
                "name": "convert_equation",
                "arguments": {
                    "value": r"\frac{x}{y}",
                    "input_format": "latex",
                    "output_format": "omml",
                    "display": True,
                },
            },
        }
        tool_response = client.post("/mcp", json=tool_request, headers=headers)
        assert tool_response.status_code == 200
        assert not tool_response.json()["result"].get("isError", False)
        assert "oMathPara" in str(tool_response.json()["result"])


def test_http_request_body_limit_is_enforced_before_mcp_parsing(tmp_path) -> None:
    settings = Settings(
        public_base_url="http://testserver",
        storage_root=tmp_path / "storage",
        max_request_bytes=1024,
    )
    with TestClient(build_app(settings)) as client:
        response = client.post(
            "/mcp",
            content=b"x" * 1025,
            headers={"Origin": "https://chatgpt.com"},
        )
    assert response.status_code == 413
    assert response.json()["error"] == "request_too_large"


def test_oauth_protected_resource_metadata_advertises_both_tool_scopes(tmp_path) -> None:
    settings = Settings(public_base_url="http://testserver", storage_root=tmp_path / "storage")
    with TestClient(build_app(settings)) as client:
        response = client.get("/.well-known/oauth-protected-resource")
    assert response.status_code == 200
    assert response.json()["resource"] == "http://testserver/mcp"
    assert set(response.json()["scopes_supported"]) == {"documents:read", "documents:write"}
