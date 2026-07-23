from __future__ import annotations

import json

import pytest
from jsonschema import Draft202012Validator
from starlette.testclient import TestClient

from wordtoolkit import __version__
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

DRAFT_MUTATION_TOOLS = {
    "insert_paragraph",
    "replace_paragraph",
    "delete_paragraph",
    "move_block",
    "create_style",
    "update_style",
    "apply_style",
    "normalize_formatting",
    "format_paragraph",
    "format_run",
    "manage_lists",
    "insert_caption",
    "insert_table",
    "modify_table",
    "merge_cells",
    "split_cells",
    "set_cell_properties",
    "insert_equation",
    "replace_equation",
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
    "insert_tracked_change",
    "accept_changes",
    "reject_changes",
    "save_document",
    "close_document",
    "repair_document",
    "render_document",
    "render_pages",
    "convert_to_pdf",
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
    for name in DRAFT_MUTATION_TOOLS:
        schema = mapping[name].inputSchema
        assert "expected_version" in schema["required"], name
        assert schema["properties"]["expected_version"]["minimum"] == 0, name
        assert "DRAFT_VERSION" not in schema.get("$defs", {}), name
    export_version = mapping["export_document"].inputSchema["properties"]["expected_version"]
    export_schema = mapping["export_document"].inputSchema
    assert "DRAFT_VERSION" not in export_schema.get("$defs", {})
    assert "expected_version" not in export_schema.get("required", [])
    assert export_version == {
        "type": "integer",
        "minimum": 0,
        "title": "Expected Version",
    }
    Draft202012Validator.check_schema(export_schema)
    validator = Draft202012Validator(export_schema)
    assert list(validator.iter_errors({"document_id": "doc"}))
    assert list(validator.iter_errors({"document_id": "doc", "output_format": "docx"}))
    assert not list(
        validator.iter_errors(
            {"document_id": "doc", "output_format": "docx", "expected_version": 0}
        )
    )
    assert not list(validator.iter_errors({"document_id": "doc", "output_format": "markdown"}))
    assert list(
        validator.iter_errors(
            {
                "document_id": "doc",
                "output_format": "markdown",
                "expected_version": False,
            }
        )
    )


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
        assert allowed.json()["result"]["serverInfo"]["version"] == __version__

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
