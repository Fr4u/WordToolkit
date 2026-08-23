from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def test_all_native_actions_have_metadata_without_catalog_drift() -> None:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text())
    actions = catalog["native_runtime"]["actions"]
    tools = {tool["name"]: tool for tool in catalog["tools"]}
    assert len(actions) == 149
    assert len(tools) == 214
    for action in actions:
        name = action if isinstance(action, str) else action["name"]
        tool = tools[name]
        assert isinstance(tool["operationVersion"], str) and tool["operationVersion"]
        assert tool["permissions"] is not None
        assert tool["reversibility"] is not None
        assert isinstance(tool["outputSchema"], dict)
        if isinstance(action, dict):
            assert tool["inputSchema"] == action["inputSchema"]
            assert tool["annotations"] == action["annotations"]


def test_nullable_backup_paths_are_optional_on_the_wire() -> None:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text())
    for tool in catalog["tools"]:
        pending = [tool.get("outputSchema", {})]
        while pending:
            schema = pending.pop()
            if not isinstance(schema, dict):
                continue
            properties = schema.get("properties", {})
            backup = properties.get("backup_path")
            if isinstance(backup, dict) and "null" in backup.get("type", []):
                assert "backup_path" not in schema.get("required", []), tool["name"]
            pending.extend(value for value in properties.values() if isinstance(value, dict))
            pending.extend(
                value for value in schema.get("$defs", {}).values() if isinstance(value, dict)
            )


def test_metadata_proposals_cover_exact_uncovered_set() -> None:
    uncovered = json.loads((ROOT / "schemas" / "native-action-metadata.v1.json").read_text())
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text())
    names = {tool["name"] for tool in catalog["tools"]}
    assert len(uncovered["actions"]) == 89
    assert len({item["name"] for item in uncovered["actions"]}) == 89
    assert all(item["name"] in names for item in uncovered["actions"])


def test_known_encoding_repairs_are_explicit_and_no_replacement_chars_remain() -> None:
    text = (ROOT / "schemas" / "mcp-tools-local.v1.json").read_text(encoding="utf-8")
    assert "�" not in text
    catalog = json.loads(text)
    tools = {tool["name"]: tool for tool in catalog["tools"]}
    assert chr(0x2014) + "never" in tools["inspect_live_word_structure_learning"]["description"]
    assert chr(0x2014) + "never" in tools["inspect_live_word_object_model_types"]["description"]
    assert chr(0x2014) + "including" in tools["update_live_word_table_fields"]["description"]
    assert tools["insert_live_word_table_of_authorities"]["inputSchema"]["properties"][
        "page_range_separator"
    ]["default"] == chr(0x2013)


def test_delta_is_self_contained_without_git_or_dist_and_serialization_is_idempotent() -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        shutil.copy2(ROOT / "schemas" / "native-action-metadata.v1.json", root / "delta.json")
        payload = json.loads((root / "delta.json").read_text(encoding="utf-8"))
        assert len(payload["actions"]) == 89
        (root / "delta.json").write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
        first = (root / "delta.json").read_bytes()
        payload = json.loads((root / "delta.json").read_text(encoding="utf-8"))
        (root / "delta.json").write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
        second = (root / "delta.json").read_bytes()
        assert hashlib.sha256(first).digest() == hashlib.sha256(second).digest()
        assert not (root / ".git").exists()
        assert not (root / "dist").exists()


def test_apply_repairs_missing_and_changed_fields_without_catalog_reserialization() -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "scripts").mkdir()
        (root / "schemas").mkdir()
        shutil.copy2(
            ROOT / "scripts" / "apply_metadata_contracts.py",
            root / "scripts" / "apply_metadata_contracts.py",
        )
        shutil.copy2(
            ROOT / "schemas" / "native-action-metadata.v1.json",
            root / "schemas" / "native-action-metadata.v1.json",
        )
        catalog_path = root / "schemas" / "mcp-tools-local.v1.json"
        catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text())
        delta = json.loads((ROOT / "schemas" / "native-action-metadata.v1.json").read_text())
        names = {item["name"] for item in delta["actions"]}
        targets = [tool for tool in catalog["tools"] if tool["name"] in names]
        targets[0].pop("permissions")
        targets[1]["reversibility"] = {"changed": True}
        catalog_path.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n")
        script = root / "scripts" / "apply_metadata_contracts.py"
        subprocess.run([sys.executable, str(script), "--apply"], check=True)
        first = catalog_path.read_bytes()
        subprocess.run([sys.executable, str(script), "--apply"], check=True)
        assert first == catalog_path.read_bytes()
