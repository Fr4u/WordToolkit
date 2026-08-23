from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]
NAMES = (
    "inspect_ooxml_equation_paragraph_rewrites",
    "plan_ooxml_equation_paragraph_rewrites",
    "apply_ooxml_equation_paragraph_rewrites",
)


def _tools() -> dict[str, dict[str, Any]]:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v2.json").read_text())
    return {tool["name"]: tool for tool in catalog["tools"] if tool["name"] in NAMES}


def _runtime() -> dict[str, Any]:
    return {
        "runtime": "dotnet-native",
        "python_used": False,
        "performance": {"total_ms": 1.0},
    }


def _unprotected() -> dict[str, Any]:
    return {
        "base_document_protection_enforced": False,
        "result_document_protection_enforced": False,
        "document_protection_metadata_changed": False,
        "unmodeled_document_protection_metadata": False,
        "base_permission_range_count": 0,
        "result_permission_range_count": 0,
        "malformed_permission_range_count": 0,
        "permission_issues_truncated": False,
        "permission_issue_codes": [],
        "authorization_required": False,
    }


def test_equation_paragraph_contracts_are_closed_versioned_and_lazy() -> None:
    tools = _tools()
    assert set(tools) == set(NAMES)
    for _name, tool in tools.items():
        Draft202012Validator.check_schema(tool["inputSchema"])
        Draft202012Validator.check_schema(tool["outputSchema"])
        assert tool["operationVersion"] == "1.0"
        assert tool["inputSchema"]["additionalProperties"] is False
        assert tool["outputSchema"]["additionalProperties"] is False
        assert tool["outputSchema"]["properties"]["data"]["additionalProperties"] is False
        assert tool["permissions"]["network"] == "none"
        assert tool["permissions"]["microsoft_word"] == "none"
        assert tool["annotations"]["openWorldHint"] is False


def test_inspect_requires_one_exact_paragraph_before_returning_text() -> None:
    schema = _tools()[NAMES[0]]["inputSchema"]
    validator = Draft202012Validator(schema)
    base = {"local_path": "input.docx", "expected_package_fingerprint": "a" * 64}
    assert list(validator.iter_errors(base)) == []
    assert list(validator.iter_errors({**base, "include_text": False})) == []
    assert list(validator.iter_errors({**base, "include_text": True}))
    assert (
        list(
            validator.iter_errors({**base, "paragraph_node_id": "wdn_exact", "include_text": True})
        )
        == []
    )


def test_semantic_command_accepts_slots_but_rejects_xml_and_equation_payloads() -> None:
    schema = _tools()[NAMES[1]]["inputSchema"]
    validator = Draft202012Validator(schema)
    request = {
        "local_path": "input.docx",
        "expected_package_fingerprint": "a" * 64,
        "commands": [
            {
                "type": "rewrite_equation_paragraph_text",
                "candidate_id": "wepr_candidate",
                "expected_candidate_fingerprint": "b" * 64,
                "replacement_text_slots": ["before", "after"],
            }
        ],
    }
    assert list(validator.iter_errors(request)) == []
    for forbidden in ("raw_xml", "omml", "latex", "equation_text", "text_node_ids"):
        changed = json.loads(json.dumps(request))
        changed["commands"][0][forbidden] = "must fail"
        assert list(validator.iter_errors(changed))


def test_success_envelopes_expose_proof_without_echoing_text_or_xml() -> None:
    tools = _tools()
    inspect = {
        "ok": True,
        "data": {
            "operation_contract": "wordtoolkit.inspect_ooxml_equation_paragraph_rewrites/1.0",
            "file_name": "input.docx",
            "package_fingerprint": "a" * 64,
            "total_candidate_count": 1,
            "rewritable_candidate_count": 1,
            "offset": 0,
            "returned_count": 1,
            "next_offset": None,
            "candidates": [
                {
                    "candidate_id": "wepr_candidate",
                    "candidate_fingerprint": "b" * 64,
                    "paragraph_node_id": "wdn_paragraph",
                    "story_kind": "main",
                    "equation_anchor_count": 1,
                    "inline_equation_anchor_count": 1,
                    "display_equation_anchor_count": 0,
                    "text_slot_count": 2,
                    "editable_text_slot_count": 2,
                    "text_node_count": 2,
                    "text_character_count": 11,
                    "can_rewrite": True,
                    "blocked_reasons": [],
                }
            ],
            "text_included": False,
            "returned_text_characters": 0,
            "raw_xml_returned": False,
            "mutation_performed": False,
            "word_opened": False,
            **_runtime(),
        },
    }
    plan = {
        "ok": True,
        "data": {
            "operation_contract": "wordtoolkit.plan_ooxml_equation_paragraph_rewrites/1.0",
            "file_name": "input.docx",
            "plan_id": "weprplan_reviewed",
            "base_package_fingerprint": "a" * 64,
            "result_package_fingerprint": "c" * 64,
            "submitted_command_count": 1,
            "paragraph_count": 1,
            "equation_anchor_count": 1,
            "text_slot_count": 2,
            "changed_text_slot_count": 2,
            "text_node_operation_count": 2,
            "changed_text_node_operation_count": 2,
            "changed_part_count": 1,
            "total_xml_byte_delta": 4,
            "has_changes": True,
            "exact_equation_bytes_preserved": True,
            "paragraph_structure_preserved": True,
            "exact_inverse_verified": True,
            "can_apply": True,
            "apply_blocked": False,
            "apply_blocked_reasons": [],
            "candidate_validation": {
                "performed": True,
                "valid": True,
                "no_new_errors": True,
                "error_count": 0,
                "baseline_error_count": 0,
                "candidate_error_count": 0,
                "errors_truncated": False,
                "issues": [],
            },
            "raw_text_returned": False,
            "raw_xml_returned": False,
            "mutation_performed": False,
            "word_opened": False,
            "protection": _unprotected(),
            "required_authorizations": [],
            **_runtime(),
        },
    }
    apply = {
        "ok": True,
        "data": {
            "operation_contract": "wordtoolkit.apply_ooxml_equation_paragraph_rewrites/1.0",
            "file_name": "input.docx",
            "plan_id": "weprplan_reviewed",
            "applied": True,
            "no_op": False,
            "paragraph_count": 1,
            "equation_anchor_count": 1,
            "text_node_operation_count": 2,
            "previous_package_fingerprint": "a" * 64,
            "package_fingerprint": "c" * 64,
            "predicted_package_fingerprint": "c" * 64,
            "backup_path": "input.docx.wordtoolkit-backup",
            "changed_entry_names": ["word/document.xml"],
            "diagnostic_count": 0,
            "microsoft_schema_valid": True,
            "microsoft_schema_no_new_errors": True,
            "exact_equation_bytes_preserved": True,
            "paragraph_structure_preserved": True,
            "exact_inverse_verified": True,
            "raw_text_returned": False,
            "raw_xml_returned": False,
            "mutation_performed": True,
            "word_opened": False,
            "explicit_authorizations": [],
            **_runtime(),
        },
    }
    for name, response in zip(NAMES, (inspect, plan, apply), strict=True):
        validator = Draft202012Validator(tools[name]["outputSchema"])
        assert list(validator.iter_errors(response)) == []
        response["data"]["raw_xml"] = "forbidden"
        assert list(validator.iter_errors(response))
        del response["data"]["raw_xml"]
        response["data"]["paragraph_text"] = "forbidden"
        assert list(validator.iter_errors(response))


def test_plan_schema_stays_bounded_for_lazy_discovery() -> None:
    compact = json.dumps(_tools()[NAMES[1]], ensure_ascii=False, separators=(",", ":"))
    assert len(compact) < 12_000
