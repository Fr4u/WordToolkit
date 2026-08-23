from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]


def _tool() -> dict[str, Any]:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v2.json").read_text())
    return next(tool for tool in catalog["tools"] if tool["name"] == "inspect_ooxml_encryption")


def _response() -> dict[str, Any]:
    return {
        "ok": True,
        "data": {
            "operation_contract": "wordtoolkit.inspect_ooxml_encryption/1.0",
            "file_name": "protected.docx",
            "bytes": 6656,
            "container_kind": "encrypted_ooxml_compound_file",
            "encryption_state": "encrypted",
            "is_encrypted_ooxml": True,
            "complete_encryption_container": True,
            "has_encryption_info_stream": True,
            "has_encrypted_package_stream": True,
            "has_data_spaces_storage": True,
            "encryption_info_variant": "agile",
            "encryption_info_major": 4,
            "encryption_info_minor": 4,
            "compound_file_major_version": 3,
            "sector_size": 512,
            "directory_entry_count": 4,
            "root_child_count": 3,
            "issue_codes": [],
            "security": {
                "accepts_password": False,
                "decrypts_content": False,
                "returns_document_content": False,
                "returns_stream_names": False,
                "returns_paths": False,
                "opens_word": False,
                "uses_network": False,
                "encryption_info_bytes_read_maximum": 8,
            },
            "runtime": "dotnet-native",
            "python_used": False,
            "performance": {"total_ms": 1.0},
        },
    }


def test_encryption_contract_is_valid_closed_and_read_only() -> None:
    tool = _tool()
    Draft202012Validator.check_schema(tool["inputSchema"])
    Draft202012Validator.check_schema(tool["outputSchema"])
    assert tool["inputSchema"]["additionalProperties"] is False
    assert tool["outputSchema"]["additionalProperties"] is False
    assert tool["outputSchema"]["properties"]["data"]["additionalProperties"] is False
    assert tool["annotations"] == {
        "readOnlyHint": True,
        "destructiveHint": False,
        "idempotentHint": True,
        "openWorldHint": False,
    }


def test_encryption_success_contract_rejects_secret_or_content_fields() -> None:
    validator = Draft202012Validator(_tool()["outputSchema"])
    response = _response()
    assert list(validator.iter_errors(response)) == []

    response["data"]["password"] = "secret"
    assert list(validator.iter_errors(response))
    del response["data"]["password"]
    response["data"]["encrypted_package_bytes"] = "AA=="
    assert list(validator.iter_errors(response))


def test_plain_zip_may_omit_not_applicable_nullable_cfb_fields() -> None:
    validator = Draft202012Validator(_tool()["outputSchema"])
    response = _response()
    response["data"].update(
        {
            "container_kind": "opc_zip_candidate",
            "encryption_state": "not_encrypted",
            "is_encrypted_ooxml": False,
            "complete_encryption_container": False,
            "has_encryption_info_stream": False,
            "has_encrypted_package_stream": False,
            "has_data_spaces_storage": False,
            "encryption_info_variant": "not_applicable",
            "directory_entry_count": 0,
            "root_child_count": 0,
        }
    )
    for field in (
        "encryption_info_major",
        "encryption_info_minor",
        "compound_file_major_version",
        "sector_size",
    ):
        del response["data"][field]
    assert list(validator.iter_errors(response)) == []


def test_encryption_request_rejects_password_fields() -> None:
    validator = Draft202012Validator(_tool()["inputSchema"])
    assert list(validator.iter_errors({"local_path": "protected.docx"})) == []
    assert list(
        validator.iter_errors({"local_path": "protected.docx", "password": "must-not-be-accepted"})
    )
