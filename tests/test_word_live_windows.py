from __future__ import annotations

import asyncio
import json
import os
from pathlib import Path

import pytest

from wordtoolkit.config import Settings
from wordtoolkit.server.stdio import build_stdio_server


def _payload(result) -> dict:
    structured = getattr(result, "structuredContent", None)
    if structured:
        return structured
    for item in result:
        text = getattr(item, "text", "")
        if text:
            return json.loads(text)
    raise AssertionError("Tool returned no structured payload")


async def _call_ok(server, name: str, arguments: dict) -> dict:
    result = await server.call_tool(name, arguments)
    payload = _payload(result)
    assert not getattr(result, "isError", False), payload
    assert payload["ok"] is True, payload
    return payload["data"]


def _select_range_in_running_word(path: Path, start: int, end: int) -> None:
    import pythoncom  # type: ignore[import-untyped]
    import win32com.client  # type: ignore[import-untyped]

    pythoncom.CoInitializeEx(pythoncom.COINIT_APARTMENTTHREADED)
    application = None
    try:
        application = win32com.client.GetActiveObject("Word.Application")
        wanted = os.path.normcase(str(path))
        matches = [
            application.Documents.Item(index)
            for index in range(1, int(application.Documents.Count) + 1)
            if os.path.normcase(str(application.Documents.Item(index).FullName)) == wanted
        ]
        if len(matches) != 1:
            raise AssertionError("Live test document could not be selected exactly")
        matches[0].Range(start, end).Select()
    finally:
        application = None
        pythoncom.CoUninitialize()


def _read_range_formatting_in_running_word(path: Path, start: int, end: int) -> dict[str, int]:
    import pythoncom  # type: ignore[import-untyped]
    import win32com.client  # type: ignore[import-untyped]

    pythoncom.CoInitializeEx(pythoncom.COINIT_APARTMENTTHREADED)
    application = None
    try:
        application = win32com.client.GetActiveObject("Word.Application")
        wanted = os.path.normcase(str(path))
        matches = [
            application.Documents.Item(index)
            for index in range(1, int(application.Documents.Count) + 1)
            if os.path.normcase(str(application.Documents.Item(index).FullName)) == wanted
        ]
        if len(matches) != 1:
            raise AssertionError("Live test document could not be inspected exactly")
        target = matches[0].Range(start, end)
        return {
            "double_strike": int(target.Font.DoubleStrikeThrough),
            "highlight_color_index": int(target.HighlightColorIndex),
            "paragraph_alignment": int(target.ParagraphFormat.Alignment),
        }
    finally:
        application = None
        pythoncom.CoUninitialize()


@pytest.mark.word
@pytest.mark.asyncio
async def test_word_live_edits_and_saves_the_same_open_document(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    if os.name != "nt":
        pytest.skip("Word Live requires Windows")
    configured = os.environ.get("WORDTOOLKIT_WORD_LIVE_TEST_DOCUMENT", "").strip()
    if not configured:
        pytest.skip("Set WORDTOOLKIT_WORD_LIVE_TEST_DOCUMENT to an open disposable DOCX")
    monkeypatch.setenv("WORDTOOLKIT_AUTH_MODE", "local_stdio")
    document_path = Path(configured).resolve(strict=True)
    server = build_stdio_server(
        Settings(
            auth_mode="local_stdio",
            storage_root=tmp_path / "storage",
            public_base_url="http://127.0.0.1",
        )
    )

    connected = await _call_ok(
        server,
        "connect_live_word_document",
        {
            "full_path": str(document_path),
            "use_active": False,
            "activate": True,
        },
    )
    document_id = connected["live_document_id"]
    object_model_types = await _call_ok(
        server,
        "inspect_live_word_object_model_types",
        {
            "query": "WdFieldType",
            "kind": "enum",
            "limit": 10,
            "refresh": True,
        },
    )
    object_model_members = await _call_ok(
        server,
        "inspect_live_word_object_model_members",
        {
            "type_name": "WdFieldType",
            "query": "wdFieldExpression",
            "kind": "enum_value",
            "limit": 10,
        },
    )
    member_capabilities = await _call_ok(
        server,
        "inspect_live_word_member_capabilities",
        {
            "type_name": "Range",
            "query": "Text",
            "member_kind": "property_get",
            "execution": "read_allowed",
            "limit": 10,
        },
    )
    text_capability = next(
        item for item in member_capabilities["capabilities"] if item["member"]["name"] == "Text"
    )
    member_operations = [
        {
            "operation_id": "read_document_content",
            "capability_id": text_capability["capability_id"],
            "target": {"kind": "document_content"},
            "result_id": "document_text",
        }
    ]
    member_preflight = await _call_ok(
        server,
        "preflight_live_word_member_operations",
        {"operations": member_operations},
    )
    member_read = await _call_ok(
        server,
        "execute_live_word_member_operations",
        {
            "live_document_id": document_id,
            "operations": member_operations,
            "activate": True,
        },
    )
    before = await _call_ok(server, "inspect_live_word_document", {"live_document_id": document_id})
    inserted_text = await _call_ok(
        server,
        "insert_live_word_text",
        {
            "live_document_id": document_id,
            "text": "WordToolkit Windows live integration test",
            "target": "document_end",
            "as_new_paragraph": True,
            "style": "Heading 1",
            "formatting": {
                "font_name": "Aptos",
                "font_size_pt": 14,
                "font_color_rgb": "#204060",
                "space_after_pt": 6,
            },
            "activate": True,
            "expected_version": 0,
        },
    )
    await asyncio.to_thread(
        _select_range_in_running_word,
        document_path,
        inserted_text["inserted_range"]["start"],
        inserted_text["inserted_range"]["end"],
    )
    live_selection = await _call_ok(
        server,
        "get_live_word_selection",
        {"live_document_id": document_id},
    )
    formatted = await _call_ok(
        server,
        "format_live_word_selection",
        {
            "live_document_id": document_id,
            "selection_token": live_selection["selection"]["selection_token"],
            "style": "Heading 2",
            "formatting": {
                "bold": True,
                "double_strike": True,
                "highlight_color_index": 7,
                "paragraph_alignment": "distribute",
                "keep_with_next": True,
            },
            "expected_version": 1,
        },
    )
    formatted_readback = await asyncio.to_thread(
        _read_range_formatting_in_running_word,
        document_path,
        inserted_text["inserted_range"]["start"],
        inserted_text["inserted_range"]["end"],
    )
    assert formatted_readback == {
        "double_strike": -1,
        "highlight_color_index": 7,
        "paragraph_alignment": 4,
    }
    inserted_equation = await _call_ok(
        server,
        "insert_live_word_equation",
        {
            "live_document_id": document_id,
            "value": r"\frac{x+1}{2}=3",
            "input_format": "latex",
            "display": True,
            "target": "document_end",
            "activate": True,
            "expected_version": 2,
        },
    )
    preflight = await _call_ok(
        server,
        "preflight_live_word_equations",
        {
            "equations": [
                {
                    "value": r"\Delta=b^2-4ac",
                    "input_format": "latex",
                },
                {
                    "value": r"x=\frac{-b+\sqrt{\Delta}}{2a}",
                    "input_format": "latex",
                },
            ]
        },
    )
    mixed = await _call_ok(
        server,
        "apply_live_word_operations",
        {
            "live_document_id": document_id,
            "operations": [
                {
                    "type": "text",
                    "text": "WordToolkit fast mixed live integration test",
                    "as_new_paragraph": True,
                    "style": "Heading 2",
                    "formatting": {
                        "italic": True,
                        "space_after_pt": 4,
                    },
                },
                {
                    "type": "equation",
                    "value": r"\Delta=b^2-4ac",
                    "input_format": "latex",
                },
                {
                    "type": "equation",
                    "value": r"x=\frac{-b+\sqrt{\Delta}}{2a}",
                    "input_format": "latex",
                    "verify_readback": True,
                },
            ],
            "activate": True,
            "expected_version": 3,
            "optimize_screen_updates": True,
        },
    )
    inserted_table = await _call_ok(
        server,
        "insert_live_word_table",
        {
            "live_document_id": document_id,
            "rows": [
                ["Metric", "A", "B"],
                ["First sample", "10", "20"],
                ["Second sample", "30", "40"],
                ["Summary", "", ""],
            ],
            "target": "document_end",
            "header_row": True,
            "autofit": "window",
            "alignment": "center",
            "expected_version": 6,
        },
    )
    table_formula_preflight = await _call_ok(
        server,
        "preflight_live_word_table_formulas",
        {
            "formulas": [
                {
                    "row": 4,
                    "column": 2,
                    "function": "sum",
                    "directions": ["above"],
                    "numeric_format": "0.00",
                },
                {
                    "row": 4,
                    "column": 3,
                    "function": "average",
                    "cell_range": {
                        "start": {"row": 2, "column": 3},
                        "end": {"row": 3, "column": 3},
                    },
                    "numeric_format": "0.00",
                },
            ]
        },
    )
    inserted_table_formulas = await _call_ok(
        server,
        "insert_live_word_table_formulas",
        {
            "live_document_id": document_id,
            "table_index": before["document"]["table_count"] + 1,
            "formulas": [
                {
                    "row": 4,
                    "column": 2,
                    "function": "sum",
                    "directions": ["above"],
                    "numeric_format": "0.00",
                },
                {
                    "row": 4,
                    "column": 3,
                    "function": "average",
                    "cell_range": {
                        "start": {"row": 2, "column": 3},
                        "end": {"row": 3, "column": 3},
                    },
                    "numeric_format": "0.00",
                },
            ],
            "expected_version": 7,
        },
    )
    updated_table_fields = await _call_ok(
        server,
        "update_live_word_table_fields",
        {
            "live_document_id": document_id,
            "table_index": before["document"]["table_count"] + 1,
            "expected_version": 9,
        },
    )
    inserted_list = await _call_ok(
        server,
        "insert_live_word_list",
        {
            "live_document_id": document_id,
            "items": [
                "Native Word bullet created from one text payload",
                "No per-item COM writes",
                "Verified through ListFormat.ListType",
            ],
            "list_kind": "bullet",
            "target": "document_end",
            "formatting": {
                "font_name": "Aptos",
                "space_after_pt": 3,
            },
            "expected_version": 10,
        },
    )
    bookmark_number = before["document"]["bookmark_count"] + 1
    bookmark_name = f"LiveDefinition_{bookmark_number}"
    result_bookmark_name = f"LiveResult_{bookmark_number + 1}"
    bookmark_preflight = await _call_ok(
        server,
        "preflight_live_word_bookmarks",
        {
            "bookmarks": [
                {
                    "name": bookmark_name,
                    "text": "Native bookmark definition",
                    "as_new_paragraph": True,
                },
                {
                    "name": result_bookmark_name,
                    "text": "Native bookmark result",
                    "prefix_text": " | ",
                },
            ]
        },
    )
    inserted_bookmarks = await _call_ok(
        server,
        "insert_live_word_bookmarks",
        {
            "live_document_id": document_id,
            "bookmarks": [
                {
                    "name": bookmark_name,
                    "text": "Native bookmark definition",
                    "as_new_paragraph": True,
                    "style": "Heading 2",
                    "formatting": {"bold": True},
                },
                {
                    "name": result_bookmark_name,
                    "text": "Native bookmark result",
                    "prefix_text": " | ",
                    "formatting": {"italic": True},
                },
            ],
            "target": "document_end",
            "expected_version": 11,
        },
    )
    inspected_bookmarks = await _call_ok(
        server,
        "inspect_live_word_structure_items",
        {
            "live_document_id": document_id,
            "structure": "bookmarks",
            "offset": 0,
            "limit": 200,
            "include_text": True,
            "max_text_chars": 100,
        },
    )
    field_preflight = await _call_ok(
        server,
        "preflight_live_word_fields",
        {
            "fields": [
                {"kind": "page"},
                {"kind": "num_pages"},
                {
                    "kind": "formula",
                    "expression": "ROUND((10+2)/3,2)",
                    "numeric_format": "0.00",
                },
                {"kind": "reference", "bookmark": bookmark_name},
            ]
        },
    )
    inserted_fields = await _call_ok(
        server,
        "insert_live_word_fields",
        {
            "live_document_id": document_id,
            "fields": [
                {
                    "kind": "page",
                    "prefix_text": "Page ",
                    "suffix_text": " of ",
                    "as_new_paragraph": True,
                },
                {"kind": "num_pages"},
                {
                    "kind": "formula",
                    "expression": "ROUND((10+2)/3,2)",
                    "numeric_format": "0.00",
                    "prefix_text": " | Safe field calculation: ",
                },
                {
                    "kind": "reference",
                    "bookmark": bookmark_name,
                    "prefix_text": " | Native bookmark reference: ",
                },
            ],
            "target": "document_end",
            "expected_version": 13,
        },
    )
    structure_map = await _call_ok(
        server,
        "map_live_word_structures",
        {
            "live_document_id": document_id,
            "include_type_histograms": True,
            "max_type_items": 2_000,
        },
    )
    learning = await _call_ok(
        server,
        "inspect_live_word_equation_learning",
        {},
    )
    structure_learning = await _call_ok(
        server,
        "inspect_live_word_structure_learning",
        {},
    )
    saved = await _call_ok(
        server,
        "save_live_word_document",
        {"live_document_id": document_id, "expected_version": 17},
    )
    validation = await _call_ok(
        server,
        "validate_live_word_document",
        {"live_document_id": document_id},
    )
    disconnected = await _call_ok(
        server,
        "disconnect_live_word_document",
        {"live_document_id": document_id},
    )

    assert before["document"]["full_name"] == str(document_path)
    assert object_model_types["matched_count"] == 1
    assert object_model_types["source_access"]["catalog_generated"] is True
    assert object_model_types["privacy"]["document_content_stored"] is False
    assert object_model_members["source_access"]["cache_hit"] is True
    assert object_model_members["source_access"]["word_attached"] is False
    assert object_model_members["members"][0]["name"] == "wdFieldExpression"
    assert object_model_members["members"][0]["value"] == 34
    assert member_capabilities["registry"]["stats"]["complete"] is True
    assert member_capabilities["registry"]["stats"]["profile_count"] == 12_167
    assert member_preflight["valid"] is True
    assert member_preflight["mutating_count"] == 0
    assert member_read["live_version"] == 0
    assert member_read["results"][0]["kind"] == "text"
    assert inserted_text["document"]["full_name"] == str(document_path)
    assert inserted_text["formatting"]["font_color_rgb"] == "#204060"
    assert formatted["live_version"] == 2
    assert formatted["formatting"]["paragraph_alignment"] == "distribute"
    assert inserted_equation["equation"]["native_verified"] is True
    assert preflight["valid"] is True
    assert preflight["mutated_word"] is False
    assert mixed["operation_count"] == 3
    assert mixed["performance"]["com_attachments"] == 1
    assert mixed["operations"][2]["equation"]["readback_verified"] is True
    assert mixed["document"]["equation_count"] == before["document"]["equation_count"] + 3
    assert inserted_table["live_version"] == 7
    assert inserted_table["table"]["native_verified"] is True
    assert inserted_table["table"]["rows"] == 4
    assert inserted_table["document"]["table_count"] == before["document"]["table_count"] + 1
    assert table_formula_preflight["valid"] is True
    assert table_formula_preflight["mutated_word"] is False
    assert table_formula_preflight["raw_field_codes_accepted"] is False
    assert inserted_table_formulas["live_version"] == 9
    assert inserted_table_formulas["formula_count"] == 2
    assert inserted_table_formulas["performance"]["com_attachments"] == 1
    assert inserted_table_formulas["performance"]["field_add_calls"] == 2
    assert inserted_table_formulas["performance"]["field_update_calls"] == 0
    assert all(formula["calculated_on_insert"] for formula in inserted_table_formulas["formulas"])
    assert all(formula["native_verified"] for formula in inserted_table_formulas["formulas"])
    assert updated_table_fields["live_version"] == 10
    assert updated_table_fields["updated"] is True
    assert updated_table_fields["table"]["field_count"] == 2
    assert updated_table_fields["performance"]["field_update_calls"] == 1
    assert updated_table_fields["field_codes_returned"] is False
    assert updated_table_fields["field_results_returned"] is False
    assert inserted_list["live_version"] == 11
    assert inserted_list["list"]["native_verified"] is True
    assert inserted_list["list"]["list_type"] == 2
    assert inserted_list["list"]["item_count"] == 3
    assert bookmark_preflight["valid"] is True
    assert bookmark_preflight["word_attached"] is False
    assert bookmark_preflight["mutated_word"] is False
    assert inserted_bookmarks["live_version"] == 13
    assert inserted_bookmarks["performance"]["com_attachments"] == 1
    assert inserted_bookmarks["performance"]["text_assignments"] == 1
    assert inserted_bookmarks["performance"]["bookmark_add_calls"] == 2
    assert all(bookmark["native_verified"] for bookmark in inserted_bookmarks["bookmarks"])
    assert (
        inserted_bookmarks["document"]["bookmark_count"] == before["document"]["bookmark_count"] + 2
    )
    assert inspected_bookmarks["available"] is True
    bookmark_items = {
        item["properties"]["name"]: item
        for item in inspected_bookmarks["items"]
        if "name" in item["properties"]
    }
    assert bookmark_name in bookmark_items
    assert result_bookmark_name in bookmark_items
    assert bookmark_items[bookmark_name]["properties"]["range"]
    assert bookmark_items[result_bookmark_name]["properties"]["range"]
    assert inspected_bookmarks["text_content_returned"] is True
    assert inspected_bookmarks["external_addresses_returned"] is False
    assert inspected_bookmarks["field_codes_returned"] is False
    assert inspected_bookmarks["property_learning"]["observation_recorded"] is True
    assert inspected_bookmarks["property_learning"]["property_values_stored"] is False
    assert inspected_bookmarks["performance"]["com_attachments"] == 1
    assert field_preflight["valid"] is True
    assert field_preflight["mutated_word"] is False
    assert field_preflight["raw_field_codes_accepted"] is False
    assert inserted_fields["live_version"] == 17
    assert inserted_fields["performance"]["com_attachments"] == 1
    assert inserted_fields["performance"]["text_assignments"] == 1
    assert inserted_fields["performance"]["field_add_calls"] == 4
    assert all(field["native_verified"] for field in inserted_fields["fields"])
    assert inserted_fields["document"]["field_count"] == before["document"]["field_count"] + 6
    assert structure_map["content_returned"] is False
    assert structure_map["structures"]["equations"] == before["document"]["equation_count"] + 3
    assert structure_map["structures"]["tables"] == before["document"]["table_count"] + 1
    assert structure_map["structures"]["lists"] >= 1
    assert structure_map["structures"]["list_paragraphs"] >= 3
    assert structure_map["type_histograms"]["list_types"]["types"].get("2", 0) >= 1
    assert structure_map["structures"]["bookmarks"] == before["document"]["bookmark_count"] + 2
    assert structure_map["structures"]["fields"] == before["document"]["field_count"] + 6
    assert structure_map["type_histograms"]["field_types"]["types"].get("26", 0) >= 1
    assert structure_map["type_histograms"]["field_types"]["types"].get("33", 0) >= 1
    assert structure_map["type_histograms"]["field_types"]["types"].get("34", 0) >= 1
    assert structure_map["type_histograms"]["field_types"]["types"].get("3", 0) >= 1
    assert structure_map["structure_learning"]["observation_recorded"] is True
    assert structure_map["structure_learning"]["document_content_stored"] is False
    assert structure_map["structure_learning"]["document_counts_stored"] is False
    assert any(story["name"] == "main_text" for story in structure_map["stories"])
    assert learning["observation_count"] >= 3
    assert learning["path_exposed"] is False
    assert structure_learning["observation_count"] >= 1
    assert structure_learning["inspection_observation_count"] >= 1
    assert structure_learning["content_stored"] is False
    assert structure_learning["document_counts_stored"] is False
    assert structure_learning["property_values_stored"] is False
    assert structure_learning["path_exposed"] is False
    assert saved["document"]["full_name"] == str(document_path)
    assert saved["saved"] is True
    assert validation["validation"]["valid"] is True
    assert disconnected["disconnected"] is True
