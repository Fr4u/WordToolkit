import hashlib
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).parents[1]


def load(p):
    return json.loads(p.read_text(encoding="utf-8"))


def test_guidance_contract_and_determinism(tmp_path):
    schema = load(ROOT / "schemas/mcp-tools-local.v2.json")
    guide = load(ROOT / "schemas/action-guidance.v1.json")
    tools = {x["name"]: x for x in schema["tools"]}
    assert len(guide["actions"]) == 157 and {x["name"] for x in guide["actions"]} == set(
        schema["native_runtime"]["actions"]
    )
    for g in guide["actions"]:
        t = tools[g["name"]]
        req = t.get("inputSchema", {}).get("required", [])
        args = g["example"]["arguments"]
        assert g["example"]["template_only"] is True
        assert all(x in args and x in g["bindings"] for x in req)
        assert len(g["success"]["required_paths"]) == len(
            t.get("outputSchema", {}).get("required", [])
        )
        assert not any(v == 0 for v in args.values())
        if "expected_version" in t.get("inputSchema", {}).get("properties", {}):
            assert g["bindings"]["expected_version"] == {"source": "live_response.live_version"}
            assert g["example"]["arguments"]["expected_version"] == {
                "type": "integer",
                "source": "live_response.live_version",
            }
    out = tmp_path / "a.json"
    subprocess.check_call(
        [sys.executable, str(ROOT / "scripts/generate_action_guidance.py"), "--output", str(out)]
    )
    assert (
        hashlib.sha256(out.read_bytes()).hexdigest()
        == hashlib.sha256((ROOT / "schemas/action-guidance.v1.json").read_bytes()).hexdigest()
    )


def test_live_apply_guidance_does_not_claim_plan_recovery():
    guide = load(ROOT / "schemas/action-guidance.v1.json")
    action = next(x for x in guide["actions"] if x["name"] == "apply_live_word_operations")
    assert "PLAN_MISMATCH" not in action["recovery"]


def test_live_apply_guidance_has_terminal_recovery_paths():
    guide = load(ROOT / "schemas/action-guidance.v1.json")
    action = next(x for x in guide["actions"] if x["name"] == "apply_live_word_operations")
    assert action["recovery"] == {
        "VERSION_CONFLICT": {
            "next_action": "inspect_live_word_document",
            "bindings": {"expected_version": "live_response.live_version"},
        },
        "STAGING_TARGET_DRIFT": {
            "next_action": "inspect_live_word_document",
            "bindings": {"live_document_id": "live_document_id"},
        },
        "ROLLBACK_FAILED": {
            "next_action": "disconnect_live_word_document",
            "bindings": {"live_document_id": "live_document_id"},
        },
        "WORD_OPERATION_OUTCOME_UNKNOWN": {
            "next_action": "get_live_word_operation_status",
            "bindings": {"operation_id": "operation_id"},
        },
    }
    assert len(action["recovery"]) == 4
    assert action["recovery"]["ROLLBACK_FAILED"]["next_action"] != "apply_live_word_operations"


def test_plan_apply_guidance_requires_plan_recovery():
    guide = load(ROOT / "schemas/action-guidance.v1.json")
    action = next(x for x in guide["actions"] if x["name"] == "apply_ooxml_text_edits")
    assert action["recovery"]["PLAN_MISMATCH"] == {
        "next_action": "plan_ooxml_text_edits",
        "bindings": {"expected_plan_id": "plan_id"},
    }


def test_protected_typed_mutator_guidance_binds_optional_plan_token():
    guide = load(ROOT / "schemas/action-guidance.v1.json")
    actions = {item["name"]: item for item in guide["actions"]}
    names = {
        "apply_ooxml_numbering_repair",
        "apply_ooxml_numbering_rebuild",
        "apply_ooxml_semantic_edits",
        "apply_ooxml_template_style_alignment",
        "apply_ooxml_equation_paragraph_rewrites",
        "apply_ooxml_text_edits",
        "apply_ooxml_review_decisions",
    }
    for name in names:
        action = actions[name]
        assert action["bindings"]["protected_edit_authorization"] == {
            "source": "plan_response.protection_authorization_id"
        }
        assert action["example"]["arguments"]["protected_edit_authorization"] == (
            "<bind:protected_edit_authorization>"
        )
        assert action["recovery"]["EDIT_POLICY_BLOCKED"]["next_action"].startswith("plan_ooxml_")
