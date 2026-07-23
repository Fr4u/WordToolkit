from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]


def _tool() -> dict[str, Any]:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text())
    return next(
        tool for tool in catalog["tools"] if tool["name"] == "render_ooxml_semantic_html"
    )


def _data() -> dict[str, Any]:
    return {
        "operation_contract": "wordtoolkit.render_ooxml_semantic_html/1.0",
        "input_file_name": "source.docx",
        "output_file_name": "preview.html",
        "package_fingerprint": "a" * 64,
        "artifact_sha256": "b" * 64,
        "artifact_bytes": 1,
        "backend": "wordtoolkit-semantic-html",
        "backend_version": "1.0",
        "fidelity_class": "semantic_preview_non_paginated",
        "story_scope": "main_document",
        "rendered_story_count": 1,
        "rendered_node_count": 1,
        "paragraph_count": 0,
        "table_count": 0,
        "equation_count": 0,
        "drawing_placeholder_count": 0,
        "unsupported_node_count": 0,
        "warnings": [],
        "output_created": True,
        "source_mutated": False,
        "artifact_contains_document_content": True,
        "external_resources_loaded": False,
        "active_content_executed": False,
        "raw_xml_returned": False,
        "document_text_returned": False,
        "word_opened": False,
    }


def test_target_node_id_requires_expected_package_fingerprint() -> None:
    validator = Draft202012Validator(_tool()["inputSchema"])
    unbound = {
        "local_path": "source.docx",
        "output_path": "preview.html",
        "target_node_id": "wdn_target",
    }
    assert list(validator.iter_errors(unbound))
    bound = {**unbound, "expected_package_fingerprint": "a" * 64}
    assert list(validator.iter_errors(bound)) == []


def test_legacy_output_omits_every_selection_field() -> None:
    validator = Draft202012Validator(_tool()["outputSchema"])
    response = {"ok": True, "data": _data()}
    assert list(validator.iter_errors(response)) == []

    response["data"]["target_node_id"] = "wdn_target"
    assert list(validator.iter_errors(response))


def test_selected_output_requires_the_complete_bounded_metadata_set() -> None:
    validator = Draft202012Validator(_tool()["outputSchema"])
    data = {
        **_data(),
        "selection_applied": True,
        "target_node_id": "wdn_target",
        "target_kind": "table",
        "target_story_kind": "main_document",
        "fragment_wrapper": "table_bodies",
        "target_rendered_node_count": 9,
    }
    assert list(validator.iter_errors({"ok": True, "data": data})) == []

    del data["fragment_wrapper"]
    assert list(validator.iter_errors({"ok": True, "data": data}))
