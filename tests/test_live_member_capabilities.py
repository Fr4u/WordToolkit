from __future__ import annotations

import pytest
from jsonschema import Draft202012Validator

from wordtoolkit.errors import ErrorCode, WordToolkitError
from wordtoolkit.live_member_capabilities import (
    build_member_capability_registry,
    capability_index,
    member_preflight_payload,
    prepare_member_operations,
)


def _catalog() -> dict:
    return {
        "generated_at": "2026-07-20T00:00:00+00:00",
        "library": {
            "guid": "{WORD}",
            "major_version": 8,
            "minor_version": 7,
        },
        "stats": {"member_count": 9},
        "types": [
            {
                "name": "Range",
                "kind": "dispatch",
                "type_index": 1,
                "guid": "{RANGE}",
                "members": [
                    {
                        "name": "Text",
                        "kind": "property_get",
                        "member_id": 1,
                        "declaration_index": 0,
                        "parameters": [],
                        "parameter_count": 0,
                        "return_type": "BSTR",
                    },
                    {
                        "name": "Text",
                        "kind": "property_put",
                        "member_id": 1,
                        "declaration_index": 1,
                        "parameters": [
                            {
                                "name": "value",
                                "type": "BSTR",
                                "flags": 1,
                                "flag_names": ["in"],
                                "optional": False,
                            }
                        ],
                        "parameter_count": 1,
                        "return_type": "VOID",
                    },
                    {
                        "name": "InsertAfter",
                        "kind": "method",
                        "member_id": 2,
                        "declaration_index": 2,
                        "parameters": [],
                        "parameter_count": 0,
                        "return_type": "VOID",
                    },
                    {
                        "name": "StyleBySlot",
                        "kind": "property_put",
                        "member_id": 7,
                        "declaration_index": 3,
                        "invoke_kind": 4,
                        "parameters": [
                            {
                                "name": "slot",
                                "type": "I4",
                                "flags": 17,
                                "flag_names": ["in", "optional"],
                                "optional": True,
                                "default_value": 1,
                            },
                            {
                                "name": "value",
                                "type": "BSTR",
                                "flags": 1,
                                "flag_names": ["in"],
                                "optional": False,
                            },
                        ],
                        "parameter_count": 2,
                        "return_type": "VOID",
                    },
                    {
                        "name": "SetRangeFieldType",
                        "kind": "method",
                        "member_id": 8,
                        "declaration_index": 4,
                        "invoke_kind": 1,
                        "parameters": [
                            {
                                "name": "slot",
                                "type": "I4",
                                "flags": 17,
                                "flag_names": ["in", "optional"],
                                "optional": True,
                                "default_value": 1,
                            },
                            {
                                "name": "field_type",
                                "type": "WdFieldType",
                                "flags": 1,
                                "flag_names": ["in"],
                                "optional": False,
                            },
                        ],
                        "parameter_count": 2,
                        "return_type": "VOID",
                    },
                ],
            },
            {
                "name": "_Application",
                "kind": "dispatch",
                "type_index": 2,
                "guid": "{APP}",
                "members": [
                    {
                        "name": "Quit",
                        "kind": "method",
                        "member_id": 3,
                        "declaration_index": 0,
                        "parameters": [],
                        "parameter_count": 0,
                        "return_type": "VOID",
                    }
                ],
            },
            {
                "name": "DocumentEvents2",
                "kind": "dispatch",
                "type_index": 3,
                "guid": "{EVENT}",
                "members": [
                    {
                        "name": "Close",
                        "kind": "method",
                        "member_id": 4,
                        "declaration_index": 0,
                        "parameters": [],
                        "parameter_count": 0,
                        "return_type": "VOID",
                    }
                ],
            },
            {
                "name": "WdFieldType",
                "kind": "enum",
                "type_index": 4,
                "guid": "{ENUM}",
                "members": [
                    {
                        "name": "wdFieldPage",
                        "kind": "enum_value",
                        "member_id": 5,
                        "declaration_index": 0,
                        "type": "INT",
                        "value": 33,
                    }
                ],
            },
            {
                "name": "_Document",
                "kind": "dispatch",
                "type_index": 5,
                "guid": "{DOC}",
                "members": [
                    {
                        "name": "FullName",
                        "kind": "property_get",
                        "member_id": 6,
                        "declaration_index": 0,
                        "parameters": [],
                        "parameter_count": 0,
                        "return_type": "BSTR",
                    }
                ],
            },
        ],
    }


def test_registry_has_one_stable_profile_per_catalog_member() -> None:
    first = build_member_capability_registry(_catalog())
    second = build_member_capability_registry(_catalog())

    assert first == second
    assert first["stats"]["profile_count"] == 9
    assert first["stats"]["unique_capability_id_count"] == 9
    assert first["stats"]["virtual_tool_count"] == 9
    assert first["stats"]["unique_virtual_tool_name_count"] == 9
    assert first["stats"]["complete"] is True
    assert len(capability_index(first)) == 9

    for profile in first["profiles"]:
        tool = profile["virtual_tool"]
        assert tool["tool_id"] == profile["capability_id"]
        assert tool["name"].startswith("wm_")
        assert isinstance(tool["input_schema"], dict)
        assert isinstance(tool["output_schema"], dict)
        Draft202012Validator.check_schema(tool["input_schema"])
        Draft202012Validator.check_schema(tool["output_schema"])


def test_registry_classifies_execution_boundaries() -> None:
    registry = build_member_capability_registry(_catalog())
    profiles = {
        (item["type"]["name"], item["member"]["name"], item["member"]["kind"]): item
        for item in registry["profiles"]
    }

    assert profiles[("Range", "Text", "property_get")]["policy"]["execution"] == ("read_allowed")
    assert profiles[("Range", "Text", "property_put")]["policy"]["execution"] == ("write_allowed")
    assert profiles[("Range", "InsertAfter", "method")]["policy"]["effect"] == "content"
    assert profiles[("Range", "StyleBySlot", "property_put")]["policy"] == {
        "effect": "format",
        "execution": "blocked",
        "reason": "indexed_property_setter_is_not_verified_undoable",
        "mutating": False,
        "undo_required": False,
    }
    assert profiles[("_Application", "Quit", "method")]["policy"]["effect"] == ("lifecycle")
    assert profiles[("_Application", "Quit", "method")]["policy"]["execution"] == ("blocked")
    assert profiles[("DocumentEvents2", "Close", "method")]["policy"]["effect"] == ("event")
    assert (
        profiles[("WdFieldType", "wdFieldPage", "enum_value")]["policy"]["execution"]
        == "metadata_only"
    )
    assert profiles[("_Document", "FullName", "property_get")]["policy"]["execution"] == "blocked"


def test_preflight_validates_targets_arguments_and_blocked_members() -> None:
    registry = build_member_capability_registry(_catalog())
    profiles = {
        (item["type"]["name"], item["member"]["name"], item["member"]["kind"]): item
        for item in registry["profiles"]
    }
    text_get = profiles[("Range", "Text", "property_get")]["capability_id"]
    text_put = profiles[("Range", "Text", "property_put")]["capability_id"]
    quit_method = profiles[("_Application", "Quit", "method")]["capability_id"]

    prepared = prepare_member_operations(
        registry,
        [
            {
                "operation_id": "read_text",
                "capability_id": text_get,
                "target": {"kind": "document_content"},
                "result_id": "original",
            },
            {
                "operation_id": "write_text",
                "capability_id": text_put,
                "target": {"kind": "document_content"},
                "arguments": ["Changed\r"],
            },
        ],
    )
    payload = member_preflight_payload(registry, prepared)

    assert payload["valid"] is True
    assert payload["operation_count"] == 2
    assert payload["read_count"] == 1
    assert payload["mutating_count"] == 1
    assert payload["single_undo_record_on_execute"] is True

    with pytest.raises(WordToolkitError) as blocked:
        prepare_member_operations(
            registry,
            [
                {
                    "capability_id": quit_method,
                    "target": {"kind": "result", "result_id": "app"},
                }
            ],
        )
    assert blocked.value.code in {ErrorCode.AUTH_FORBIDDEN, ErrorCode.INVALID_INPUT}

    with pytest.raises(WordToolkitError) as wrong_type:
        prepare_member_operations(
            registry,
            [
                {
                    "capability_id": text_put,
                    "target": {"kind": "document_content"},
                    "arguments": [42],
                }
            ],
        )
    assert wrong_type.value.code is ErrorCode.INVALID_INPUT


def test_virtual_tools_support_enum_constants_and_omitted_optional_indexes() -> None:
    registry = build_member_capability_registry(_catalog())
    profiles = {
        (item["type"]["name"], item["member"]["name"], item["member"]["kind"]): item
        for item in registry["profiles"]
    }
    constant = profiles[("WdFieldType", "wdFieldPage", "enum_value")]
    method = profiles[("Range", "SetRangeFieldType", "method")]
    indexed_put = profiles[("Range", "StyleBySlot", "property_put")]

    prepared = prepare_member_operations(
        registry,
        [
            {
                "capability_id": method["capability_id"],
                "target": {"kind": "document_content"},
                "arguments": [
                    {"missing": True},
                    {"constant_id": constant["capability_id"]},
                ],
            },
        ],
    )

    assert prepared[0].arguments == ({"missing": True}, 33)
    assert constant["constant"] == {
        "type": "WdFieldType",
        "storage_type": "INT",
        "value": 33,
    }
    assert constant["virtual_tool"]["kind"] == "constant"
    assert indexed_put["virtual_tool"]["kind"] == "unavailable"
    assert method["virtual_tool"]["input_schema"]["properties"]["arguments"]["minItems"] == 2

    with pytest.raises(WordToolkitError) as omitted_required_position:
        prepare_member_operations(
            registry,
            [
                {
                    "capability_id": method["capability_id"],
                    "target": {"kind": "document_content"},
                    "arguments": [33],
                }
            ],
        )
    assert omitted_required_position.value.code is ErrorCode.INVALID_INPUT


def test_read_virtual_tools_require_result_id_but_writes_may_omit_it() -> None:
    registry = build_member_capability_registry(_catalog())
    profiles = {
        (item["type"]["name"], item["member"]["name"], item["member"]["kind"]): item
        for item in registry["profiles"]
    }
    read_tool = profiles[("Range", "Text", "property_get")]["virtual_tool"]
    write_tool = profiles[("Range", "Text", "property_put")]["virtual_tool"]

    read_operation = {
        "capability_id": read_tool["tool_id"],
        "target": {"kind": "document_content"},
    }
    write_operation = {
        "capability_id": write_tool["tool_id"],
        "target": {"kind": "document_content"},
        "arguments": ["Changed\r"],
    }
    assert not Draft202012Validator(read_tool["input_schema"]).is_valid(read_operation)
    assert Draft202012Validator(write_tool["input_schema"]).is_valid(write_operation)
