from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]


def _tool() -> dict[str, Any]:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text())
    return next(
        tool for tool in catalog["tools"] if tool["name"] == "inspect_ooxml_signatures"
    )


def _response() -> dict[str, Any]:
    return {
        "ok": True,
        "data": {
            "operation_contract": "wordtoolkit.inspect_ooxml_signatures/1.0",
            "file_name": "signed.docx",
            "package_fingerprint": "a" * 64,
            "view": "signatures",
            "signature_origin_declared": True,
            "signature_origin_count": 1,
            "signature_count": 1,
            "valid_signature_count": 1,
            "invalid_signature_count": 0,
            "unsupported_signature_count": 0,
            "indeterminate_signature_count": 0,
            "all_discovered_signatures_valid": True,
            "cryptographic_integrity_validation_performed": True,
            "certificate_chain_trust_verified": False,
            "revocation_checked": False,
            "signatures": [
                {
                    "signature_id": "wdsig_" + "b" * 24,
                    "status": "valid",
                    "topology_valid": True,
                    "signature_value_verified": True,
                    "manifest_references_verified": True,
                    "manifest_reference_count": 1,
                    "signed_part_count": 1,
                    "signed_relationship_part_count": 0,
                    "selected_relationship_count": 0,
                    "signature_algorithm": (
                        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256"
                    ),
                    "canonicalization_algorithm": (
                        "http://www.w3.org/TR/2001/REC-xml-c14n-20010315"
                    ),
                    "weak_algorithm": False,
                    "certificate_present": True,
                    "certificate_sha256": None,
                    "public_key_algorithm": None,
                    "certificate_time_valid_at_inspection": True,
                    "signature_part_uri": None,
                    "issue_codes": [],
                }
            ],
            "references": [],
            "issues": [],
            "paging": {
                "offset": 0,
                "limit": 20,
                "returned": 1,
                "total": 1,
                "next_offset": None,
            },
            "security": {
                "returns_document_content": False,
                "returns_raw_xml": False,
                "returns_certificate_bytes": False,
                "returns_certificate_identity": False,
                "returns_paths": False,
                "opens_word": False,
                "uses_network": False,
                "certificate_chain_trust_verified": False,
                "revocation_checked": False,
                "external_references_resolved": False,
            },
            "runtime": "dotnet-native",
            "python_used": False,
            "performance": {"total_ms": 1.0},
        },
    }


def test_signature_contract_is_valid_closed_bounded_and_read_only() -> None:
    tool = _tool()
    Draft202012Validator.check_schema(tool["inputSchema"])
    Draft202012Validator.check_schema(tool["outputSchema"])
    assert tool["inputSchema"]["additionalProperties"] is False
    assert tool["outputSchema"]["additionalProperties"] is False
    assert tool["outputSchema"]["properties"]["data"]["additionalProperties"] is False
    assert tool["inputSchema"]["properties"]["limit"]["maximum"] == 100
    assert tool["annotations"] == {
        "readOnlyHint": True,
        "destructiveHint": False,
        "idempotentHint": True,
        "openWorldHint": False,
    }


def test_signature_success_contract_separates_integrity_from_signer_trust() -> None:
    validator = Draft202012Validator(_tool()["outputSchema"])
    response = _response()
    assert list(validator.iter_errors(response)) == []
    data = response["data"]
    assert data["cryptographic_integrity_validation_performed"] is True
    assert data["certificate_chain_trust_verified"] is False
    assert data["revocation_checked"] is False
    assert data["security"]["uses_network"] is False

    del data["paging"]["next_offset"]
    assert list(validator.iter_errors(response)) == []


def test_signature_success_contract_rejects_identity_bytes_content_and_paths() -> None:
    validator = Draft202012Validator(_tool()["outputSchema"])
    for field, value in (
        ("signer_identity", "CN=private"),
        ("certificate_bytes", "AA=="),
        ("raw_xml", "<Signature/>"),
        ("local_path", "C:/private/signed.docx"),
    ):
        response = _response()
        response["data"][field] = value
        assert list(validator.iter_errors(response)), field


def test_signature_request_rejects_trust_network_and_secret_inputs() -> None:
    validator = Draft202012Validator(_tool()["inputSchema"])
    valid = {
        "local_path": "signed.docx",
        "view": "references",
        "offset": 0,
        "limit": 20,
        "include_source": True,
        "include_certificate_hash": True,
    }
    assert list(validator.iter_errors(valid)) == []
    for forbidden in ("trust_signer", "check_revocation", "password", "network"):
        invalid = dict(valid)
        invalid[forbidden] = True
        assert list(validator.iter_errors(invalid)), forbidden
