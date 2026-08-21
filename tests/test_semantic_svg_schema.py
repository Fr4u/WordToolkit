from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]


def _tool() -> dict[str, Any]:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text())
    return next(tool for tool in catalog["tools"] if tool["name"] == "render_ooxml_semantic_svg")


def _response() -> dict[str, Any]:
    return {
        "ok": True,
        "data": {
            "operation_contract": "wordtoolkit.render_ooxml_semantic_svg/1.0",
            "input_file_name": "source.docx",
            "output_file_name": "target.svg",
            "package_fingerprint": "a" * 64,
            "artifact_sha256": "b" * 64,
            "artifact_bytes": 100,
            "artifact_media_type": "image/svg+xml",
            "output_format": "svg",
            "backend": "wordtoolkit-semantic-svg",
            "backend_version": "1.0",
            "fidelity_class": "semantic_vector_preview_non_paginated",
            "layout_basis": "semantic_flow_estimated",
            "text_output_mode": "text",
            "paginated": False,
            "exact_text_metrics": False,
            "pixel_equivalence_claimed": False,
            "story_scope": "main_document",
            "selection_applied": True,
            "target_node_id": "wdn_target",
            "target_kind": "equation",
            "target_story_kind": "main_document",
            "target_subtree_fingerprint": "c" * 64,
            "viewport_width_px": 1024,
            "viewport_height_px": 120,
            "rendered_story_count": 1,
            "rendered_node_count": 3,
            "paragraph_count": 1,
            "table_count": 0,
            "equation_count": 1,
            "drawing_placeholder_count": 0,
            "unsupported_node_count": 0,
            "warnings": ["TEXT_METRICS_ESTIMATED"],
            "output_created": True,
            "source_mutated": False,
            "artifact_contains_document_content": True,
            "external_resources_loaded": False,
            "active_content_executed": False,
            "raw_xml_returned": False,
            "document_text_returned": False,
            "word_opened": False,
        },
    }


def test_svg_contract_schemas_are_valid_and_closed() -> None:
    tool = _tool()
    Draft202012Validator.check_schema(tool["inputSchema"])
    Draft202012Validator.check_schema(tool["outputSchema"])
    assert tool["inputSchema"]["additionalProperties"] is False
    assert tool["outputSchema"]["additionalProperties"] is False
    assert tool["outputSchema"]["properties"]["data"]["additionalProperties"] is False


def test_svg_requires_an_exact_fingerprint_bound_target() -> None:
    validator = Draft202012Validator(_tool()["inputSchema"])
    request = {
        "local_path": "source.docx",
        "output_path": "target.svg",
        "expected_package_fingerprint": "a" * 64,
        "target_node_id": "wdn_target",
    }
    assert list(validator.iter_errors(request)) == []
    for field in ("expected_package_fingerprint", "target_node_id"):
        invalid = dict(request)
        del invalid[field]
        assert list(validator.iter_errors(invalid))


def test_svg_response_rejects_fidelity_lies_and_unpublished_fields() -> None:
    validator = Draft202012Validator(_tool()["outputSchema"])
    response = _response()
    assert list(validator.iter_errors(response)) == []

    response["data"]["pixel_equivalence_claimed"] = True
    assert list(validator.iter_errors(response))
    response["data"]["pixel_equivalence_claimed"] = False
    response["data"]["document_text"] = "must not be returned"
    assert list(validator.iter_errors(response))
