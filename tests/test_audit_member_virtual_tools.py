from __future__ import annotations

from scripts.audit_member_virtual_tools import _schema_validation_key, audit


def test_schema_validation_key_ignores_const_payload_only() -> None:
    first = {"type": "object", "properties": {"id": {"const": "a"}}}
    second = {"type": "object", "properties": {"id": {"const": "b"}}}
    assert _schema_validation_key(first) == _schema_validation_key(second)


def test_schema_validation_key_keeps_structural_differences() -> None:
    base = {"type": "object", "required": ["id"], "properties": {"id": {"type": "string"}}}
    changed = {"type": "object", "required": [], "properties": {"id": {"type": "string"}}}
    assert _schema_validation_key(base) != _schema_validation_key(changed)


def test_audit_preserves_invalid_schema_failure(monkeypatch, tmp_path) -> None:
    import scripts.audit_member_virtual_tools as module

    profile = {
        "capability_id": "cap",
        "policy": {"execution": "available"},
        "virtual_tool": {
            "tool_id": "cap",
            "name": "tool",
            "kind": "method",
            "input_schema": {"type": 17},
            "output_schema": {"type": "object"},
        },
    }
    monkeypatch.setattr(
        module,
        "build_member_capability_registry",
        lambda _: {
            "profiles": [profile],
            "catalog_generated_at": "",
            "schema_version": "v1",
            "stats": {"complete": True, "catalog_member_count": 1},
        },
    )
    source = tmp_path / "catalog.json"
    source.write_text("{}", encoding="utf-8")
    result = audit(source)
    assert result["passed"] is False
    assert result["failures"][0]["failure"] == "invalid_input_schema"
