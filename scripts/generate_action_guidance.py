"""Generate deterministic first-call guidance for every native action."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = ROOT / "schemas/mcp-tools-local.v1.json"
OUT = ROOT / "schemas/action-guidance.v1.json"
OVERRIDES = ROOT / "schemas/action-guidance-overrides.v1.json"

OPAQUE = ("id", "token", "fingerprint", "hash", "sha256")


def placeholder(name: str) -> object:
    n = name.lower()
    if "version" in n or n.endswith("count") or n in {"offset", "limit", "index"}:
        return {"type": "integer", "source": "binding", "minimum": 0}
    if n in {
        "commands",
        "operations",
        "equations",
        "items",
        "candidates",
        "edits",
        "rows",
        "fields",
        "formulas",
        "bookmarks",
        "policies",
    }:
        return []
    if any(x in n for x in OPAQUE) or n.endswith("path") or n.endswith("_id"):
        return f"<bind:{name}>"
    return f"<provide:{name}>"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--output", type=Path, default=OUT)
    a = ap.parse_args()
    root = json.loads(SCHEMA.read_text(encoding="utf-8"))
    actions = root["native_runtime"]["actions"]
    tools = {x["name"]: x for x in root["tools"]}
    overrides = json.loads(OVERRIDES.read_text(encoding="utf-8")) if OVERRIDES.exists() else {}
    entries = []
    for name in actions:
        tool = tools[name]
        inp = tool.get("inputSchema", {})
        required = inp.get("required", [])
        all_props = inp.get("properties", {})
        ex = {k: placeholder(k) for k in required}
        prereq = []
        acquire = []
        bindings = {k: {"source": "external_input"} for k in required}
        if name in {
            "apply_live_word_operations",
            "save_live_word_document",
            "disconnect_live_word_document",
        }:
            prereq.append("live_document_id acquired from a lifecycle action")
            bindings["live_document_id"] = {"source": "lifecycle_action"}
        if "expected_version" in all_props:
            prereq.append("fresh expected_version from live response")
            bindings["expected_version"] = {"source": "live_response.live_version"}
            ex["expected_version"] = {
                "type": "integer",
                "source": "live_response.live_version",
            }
        if "local_path" in required:
            prereq.append("explicit existing local_path")
        if "expected_package_fingerprint" in all_props:
            prereq.append("exact package fingerprint from inspect action")
            bindings["expected_package_fingerprint"] = {
                "source": "inspect_response.package_fingerprint"
            }
            ex.setdefault(
                "expected_package_fingerprint",
                {"type": "string", "source": "inspect_response.package_fingerprint"},
            )
        if "expected_plan_id" in required or "expected_apply_plan_id" in required:
            prereq.append("exact plan ID and identical commands from preceding plan")
        if any(x in required for x in ("output_path", "output_directory", "artifact_stem")):
            prereq.append("explicit new output path; never overwrite")
        if name.startswith("run_ooxml_ocr"):
            prereq.append("inspect OCR candidates first; local_only privacy")
        if any("sha256" in x for x in required):
            prereq.append("explicit executable/JAR paths and expected SHA-256")
        if name.startswith(("plan_", "apply_")):
            acquire.append("review plan and block reasons before apply")
        ov = overrides.get(name, {})
        prereq.extend(ov.get("prerequisites", []))
        acquire.extend(ov.get("acquisition_steps", []))
        bindings.update(ov.get("bindings", {}))
        out = tool.get("outputSchema", {})
        required_out = list(out.get("required", []))
        predicates = [
            f"{p} is present"
            for p in required_out
            if p
            in {
                "ok",
                "valid",
                "can_apply",
                "version",
                "package_fingerprint",
                "output_path",
                "output_paths",
                "artifact_hashes",
            }
        ]
        if name.startswith(("plan_ooxml_", "apply_ooxml_")) and "package" in name:
            prereq = ["exact package_fingerprint from the matching inspect/query action"]
        # Recovery guidance is additive: generic safety paths must remain present
        # even when an action supplies custom recovery overrides. Custom entries
        # take precedence only for the same key.
        recovery = {}
        if "expected_version" in all_props or name.startswith("apply_live_"):
            recovery["VERSION_CONFLICT"] = {
                "next_action": "inspect_live_word_document",
                "bindings": {"expected_version": "live_response.live_version"},
            }
        if "expected_package_fingerprint" in all_props:
            recovery["VERSION_CONFLICT"] = {
                "next_action": "inspect_ooxml_package",
                "bindings": {"expected_package_fingerprint": "package_fingerprint"},
            }
        has_plan_field = any(
            field in all_props
            for field in ("expected_plan_id", "expected_apply_plan_id", "plan_id")
        )
        if (has_plan_field or "PLAN_MISMATCH" in ov.get("recovery", {})) and not name.startswith(
            "apply_live_"
        ):
            recovery.setdefault(
                "PLAN_MISMATCH",
                {
                    "next_action": name.replace("apply_", "plan_", 1),
                    "bindings": {"expected_plan_id": "plan_id"},
                },
            )
        if name.startswith("apply_live_"):
            recovery.setdefault(
                "STAGING_TARGET_DRIFT",
                {
                    "next_action": "inspect_live_word_document",
                    "bindings": {"live_document_id": "live_document_id"},
                },
            )
            recovery.setdefault(
                "ROLLBACK_FAILED",
                {
                    "next_action": "disconnect_live_word_document",
                    "bindings": {"live_document_id": "live_document_id"},
                },
            )
        # Explicit overrides win while retaining all generic defaults.
        for key, value in ov.get("recovery", {}).items():
            recovery[key] = value
        entries.append(
            {
                "name": name,
                "prerequisites": prereq,
                "acquisition_steps": acquire,
                "bindings": bindings,
                "example": {
                    "gateway": "execute_wordtoolkit_action",
                    "action": name,
                    "template_only": True,
                    "arguments": ex,
                },
                "success": {"required_paths": required_out, "predicates": predicates},
                "recovery": recovery,
                "recipe_ids": ov.get("recipe_ids", ["native.default"]),
            }
        )
    doc = {"schema_version": "1.0.0", "actions": entries}
    text = json.dumps(doc, ensure_ascii=False, indent=2) + "\n"
    if a.check:
        if json.loads(a.output.read_text(encoding="utf-8")) != doc:
            raise SystemExit("guidance drift")
    else:
        a.output.write_text(text, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
