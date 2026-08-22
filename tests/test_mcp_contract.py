from __future__ import annotations

import json
from pathlib import Path

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
    "apply_document_operations",
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
    "apply_document_operations",
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


def test_live_table_formula_items_have_explicit_runtime_contract() -> None:
    catalog = json.loads(
        (Path(__file__).parents[1] / "schemas" / "mcp-tools-local.v1.json").read_text(
            encoding="utf-8"
        )
    )
    tool = next(
        item for item in catalog["tools"] if item["name"] == "insert_live_word_table_formulas"
    )
    item = tool["inputSchema"]["properties"]["formulas"]["items"]
    Draft202012Validator.check_schema(item)
    validator = Draft202012Validator(item)
    valid = {"row": 1, "column": 2, "function": "sum", "directions": ["above"]}
    assert not list(validator.iter_errors(valid))
    assert list(validator.iter_errors({"row": 1, "column": 2, "formula": "=SUM(ABOVE)"}))
    assert list(validator.iter_errors({"row": 1, "column": 2, "function": "sum"}))
    assert list(
        validator.iter_errors(
            {
                "row": 1,
                "column": 2,
                "function": "sum",
                "directions": ["above"],
                "cell_range": {"start": {"row": 1, "column": 1}, "end": {"row": 1, "column": 2}},
            }
        )
    )


def test_live_formatting_contract_is_typed_and_alias_safe() -> None:
    catalog = json.loads(
        (Path(__file__).parents[1] / "schemas" / "mcp-tools-local.v1.json").read_text(
            encoding="utf-8"
        )
    )
    for name in (
        "insert_live_word_text",
        "format_live_word_selection",
        "insert_live_word_list",
        "set_live_word_header_footer",
    ):
        tool = next(item for item in catalog["tools"] if item["name"] == name)
        formatting = tool["inputSchema"]["properties"]["formatting"]
        schema = next(item for item in formatting["anyOf"] if item.get("type") == "object")
        Draft202012Validator.check_schema(schema)
        validator = Draft202012Validator(schema)
        assert not list(
            validator.iter_errors(
                {
                    "font_name": "Aptos",
                    "font_size_pt": 11,
                    "bold": True,
                    "double_strike": True,
                    "paragraph_alignment": "distribute",
                    "highlight_color_index": 6,
                }
            )
        )
        assert list(validator.iter_errors({"font_size_pt": "banana"}))
        assert list(validator.iter_errors({"font_size_pt": 201}))
        assert list(validator.iter_errors({"font_name": "x" * 129}))
        assert list(validator.iter_errors({"font_color_rgb": "#GGGGGG"}))
        assert list(validator.iter_errors({"double_strike": 1}))
        assert not list(validator.iter_errors({"strike": False, "double_strike": True}))
        assert list(validator.iter_errors({"strike": True, "double_strike": True}))
        assert list(validator.iter_errors({"highlight_color_index": 17}))
        assert list(validator.iter_errors({"paragraph_alignment": "thai"}))
        assert list(validator.iter_errors({"font_size": 10, "font_size_pt": 11}))
        assert list(validator.iter_errors({"alignment": "left", "paragraph_alignment": "right"}))
        assert list(validator.iter_errors({"unknown": True}))


def test_live_mixed_formatting_contract_matches_extended_runtime_fields() -> None:
    catalog = json.loads(
        (Path(__file__).parents[1] / "schemas" / "mcp-tools-local.v1.json").read_text(
            encoding="utf-8"
        )
    )
    tool = next(item for item in catalog["tools"] if item["name"] == "apply_live_word_operations")
    defs = tool["inputSchema"]["$defs"]
    run = defs["liveRunFormatting"]
    text = defs["liveTextFormatting"]
    schema = tool["inputSchema"]
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema)
    assert run["properties"]["font_name"]["maxLength"] == 128
    assert {"double_strike", "highlight_color_index"} <= set(run["properties"])
    assert "distribute" in text["properties"]["paragraph_alignment"]["enum"]
    valid = {
        "live_document_id": "live",
        "expected_version": 0,
        "operations": [
            {
                "type": "text",
                "runs": [
                    {
                        "text": "verified",
                        "formatting": {
                            "double_strike": True,
                            "highlight_color_index": 7,
                        },
                    }
                ],
                "formatting": {"paragraph_alignment": "distribute"},
            }
        ],
    }
    assert not list(validator.iter_errors(valid))
    invalid_run = json.loads(json.dumps(valid))
    invalid_run["operations"][0]["runs"][0]["formatting"]["highlight_color_index"] = 17
    assert list(validator.iter_errors(invalid_run))
    invalid_paragraph_field = json.loads(json.dumps(valid))
    invalid_paragraph_field["operations"][0]["runs"][0]["formatting"]["paragraph_alignment"] = (
        "center"
    )
    assert list(validator.iter_errors(invalid_paragraph_field))
    invalid_strikes = json.loads(json.dumps(valid))
    invalid_strikes["operations"][0]["runs"][0]["formatting"].update(
        {"strike": True, "double_strike": True}
    )
    assert list(validator.iter_errors(invalid_strikes))


def test_live_table_formula_batch_bounds_are_published() -> None:
    catalog = json.loads(
        (Path(__file__).parents[1] / "schemas" / "mcp-tools-local.v1.json").read_text(
            encoding="utf-8"
        )
    )
    tool = next(
        item for item in catalog["tools"] if item["name"] == "preflight_live_word_table_formulas"
    )
    formulas = tool["inputSchema"]["properties"]["formulas"]
    Draft202012Validator.check_schema(tool["inputSchema"])
    validator = Draft202012Validator(tool["inputSchema"])
    assert formulas["minItems"] == 1
    assert formulas["maxItems"] == 200
    valid = {"row": 1, "column": 1, "function": "sum", "directions": ["above"]}
    assert not list(validator.iter_errors({"formulas": [valid]}))
    assert list(validator.iter_errors({"formulas": []}))
    assert list(validator.iter_errors({"formulas": [valid] * 201}))


def test_live_caption_schema_accepts_exactly_one_target_token() -> None:
    catalog = json.loads(
        (Path(__file__).parents[1] / "schemas" / "mcp-tools-local.v1.json").read_text(
            encoding="utf-8"
        )
    )
    tool = next(item for item in catalog["tools"] if item["name"] == "insert_live_word_caption")
    schema = tool["inputSchema"]
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema)
    base = {"live_document_id": "live", "expected_version": 0}

    assert not list(validator.iter_errors({**base, "selection_token": "selection"}))
    assert not list(validator.iter_errors({**base, "range_token": "range"}))
    assert list(validator.iter_errors(base))
    assert list(
        validator.iter_errors({**base, "selection_token": "selection", "range_token": "range"})
    )


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
    assert mapping["apply_document_operations"].meta["openai/fileParams"] == ["files"]
    for name in DRAFT_MUTATION_TOOLS:
        schema = mapping[name].inputSchema
        assert "expected_version" in schema["required"], name
        assert schema["properties"]["expected_version"]["minimum"] == 0, name
        assert "DRAFT_VERSION" not in schema.get("$defs", {}), name
    batch_tool = mapping["apply_document_operations"]
    assert batch_tool.annotations.destructiveHint is True
    batch_schema = batch_tool.inputSchema
    Draft202012Validator.check_schema(batch_schema)
    assert len(json.dumps(batch_schema, separators=(",", ":"))) < 20_000
    operation_items = batch_schema["properties"]["operations"]
    assert operation_items["minItems"] == 1
    assert operation_items["maxItems"] == 16
    files_schema = batch_schema["properties"]["files"]
    assert files_schema["maxItems"] == 16
    file_schema = files_schema["items"]
    assert file_schema["additionalProperties"] is False
    assert set(file_schema["properties"]) == {
        "download_url",
        "file_id",
        "mime_type",
        "file_name",
    }
    assert file_schema["required"] == ["download_url", "file_id"]
    item_schema = operation_items["items"]
    assert item_schema["additionalProperties"] is False
    assert item_schema["required"] == ["operation", "arguments"]
    variants = item_schema["oneOf"]
    assert len(variants) == 33
    by_operation = {variant["properties"]["operation"]["const"]: variant for variant in variants}
    assert len(by_operation) == len(variants)
    assert set(by_operation) == DRAFT_MUTATION_TOOLS - {
        "apply_document_operations",
        "save_document",
        "close_document",
        "repair_document",
        "render_document",
        "render_pages",
        "convert_to_pdf",
        "generate_preview",
    }
    for operation, variant in by_operation.items():
        arguments = variant["properties"]["arguments"]
        assert arguments["additionalProperties"] is False, operation
        assert "document_id" not in arguments["properties"], operation
        assert "expected_version" not in arguments["properties"], operation
        assert "title" not in json.dumps(variant), operation
    assert by_operation["manage_lists"]["properties"]["arguments"]["properties"]["action"][
        "enum"
    ] == ["apply", "create_multilevel", "demote", "promote", "restart", "suppress"]
    assert "action" in by_operation["manage_lists"]["properties"]["arguments"]["required"]
    image_arguments = by_operation["insert_image"]["properties"]["arguments"]
    assert "file" not in image_arguments["properties"]
    assert image_arguments["properties"]["file_index"] == {
        "type": "integer",
        "minimum": 0,
        "maximum": 15,
    }
    assert "file_index" in image_arguments["required"]
    assert "$defs" not in batch_schema
    batch_validator = Draft202012Validator(batch_schema)
    valid_batch = {
        "document_id": "doc",
        "expected_version": 0,
        "operations": [
            {
                "operation": "enable_track_changes",
                "arguments": {"enabled": True},
            }
        ],
    }
    assert not list(batch_validator.iter_errors(valid_batch))
    valid_image_batch = {
        "document_id": "doc",
        "expected_version": 0,
        "files": [
            {
                "download_url": "https://files.example.test/pixel.png",
                "file_id": "file_pixel",
                "mime_type": "image/png",
                "file_name": "pixel.png",
            }
        ],
        "operations": [
            {
                "operation": "insert_image",
                "arguments": {"paragraph_id": "ABCD1234", "file_index": 0},
            }
        ],
    }
    assert not list(batch_validator.iter_errors(valid_image_batch))
    invalid_batches = [
        {**valid_batch, "operations": []},
        {**valid_batch, "operations": valid_batch["operations"] * 17},
        {
            **valid_batch,
            "operations": [
                {
                    "operation": "enable_track_changes",
                    "arguments": {"enabled": True, "document_id": "nested"},
                }
            ],
        },
        {
            **valid_batch,
            "operations": [
                {
                    "operation": "manage_lists",
                    "arguments": {"action": "list"},
                }
            ],
        },
        {
            **valid_batch,
            "operations": [
                {
                    "operation": "enable_track_changes",
                    "arguments": {"enabled": True, "unknown": 1},
                }
            ],
        },
        {
            **valid_batch,
            "operations": [{"operation": "unknown", "arguments": {}}],
        },
        {
            **valid_batch,
            "operations": [
                {
                    "operation": "insert_image",
                    "arguments": {
                        "paragraph_id": "ABCD1234",
                        "file": {
                            "download_url": "https://files.example.test/pixel.png",
                            "file_id": "file_pixel",
                        },
                    },
                }
            ],
        },
        {**valid_image_batch, "files": [{"download_url": "https://files.example.test/x"}]},
        {**valid_batch, "expected_version": False},
    ]
    for invalid_batch in invalid_batches:
        assert list(batch_validator.iter_errors(invalid_batch)), invalid_batch
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


def test_generated_draft_operation_contract_examples_are_valid() -> None:
    contract_path = Path(__file__).resolve().parents[1] / "schemas" / "draft-operations.v1.json"
    contract = json.loads(contract_path.read_text(encoding="utf-8"))
    assert contract["contract"] == "wordtoolkit.apply_document_operations/1.0"
    assert contract["permissions"] == ["documents:write"]
    assert contract["file_binding"] == {
        "top_level_field": "files",
        "operation_reference": "insert_image.arguments.file_index",
        "mcp_apps_meta": {"openai/fileParams": ["files"]},
    }
    assert contract["limits"] == {
        "operations_min": 1,
        "operations_max": 16,
        "files_max": 16,
        "aggregate_argument_bytes_max": 1_048_576,
    }
    for schema_name in ("input_schema", "success_data_schema", "error_schema"):
        Draft202012Validator.check_schema(contract[schema_name])
    examples = contract["examples"]
    assert isinstance(examples, list)
    assert examples
    for example in examples:
        assert not list(
            Draft202012Validator(contract["input_schema"]).iter_errors(example["input"])
        )
        assert not list(
            Draft202012Validator(contract["success_data_schema"]).iter_errors(
                example["success_data"]
            )
        )
        assert not list(
            Draft202012Validator(contract["error_schema"]).iter_errors(example["error"])
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
