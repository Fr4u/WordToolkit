from __future__ import annotations

import hashlib
import json
import re
from collections import Counter
from dataclasses import dataclass
from typing import Any

from .errors import ErrorCode, WordToolkitError

CAPABILITY_SCHEMA_VERSION = 2
VALID_CAPABILITY_EFFECTS = frozenset(
    {
        "constant",
        "read",
        "format",
        "content",
        "structure",
        "calculation",
        "view",
        "event",
        "external",
        "lifecycle",
        "unknown",
    }
)
VALID_CAPABILITY_EXECUTIONS = frozenset(
    {
        "metadata_only",
        "read_allowed",
        "write_allowed",
        "blocked",
    }
)

_DOCUMENT_TYPES = frozenset({"document", "_document"})
_APPLICATION_TYPES = frozenset(
    {
        "application",
        "_application",
        "_global",
        "global",
        "options",
        "autocorrect",
        "addins",
        "addins2",
        "dictionaries",
        "fileconverters",
        "keybinding",
        "keybindings",
        "languages",
        "recentfiles",
        "system",
        "task",
        "tasks",
        "templates",
    }
)
_EXTERNAL_TYPES = frozenset({"source", "xmlnamespace", "xsltransform"})
_EVENT_TYPE_MARKERS = ("events", "eventsink")
_SENSITIVE_READ_NAMES = frozenset(
    {
        "address",
        "code",
        "connection",
        "fullname",
        "hyperlink",
        "name",
        "path",
        "password",
        "sourcefullname",
        "subaddress",
        "vbproject",
    }
)
_EXTERNAL_NAME_MARKERS = (
    "addins",
    "broadcast",
    "checkin",
    "checkout",
    "dde",
    "download",
    "email",
    "export",
    "fax",
    "fileconverter",
    "filedialog",
    "followhyperlink",
    "import",
    "mail",
    "macro",
    "ole",
    "organizer",
    "print",
    "route",
    "run",
    "send",
    "upload",
    "web",
)
_LIFECYCLE_NAMES = frozenset(
    {
        "addblogdocument",
        "addold",
        "changefileopendirectory",
        "close",
        "newwindow",
        "open",
        "openandrepair",
        "quit",
        "save",
        "saveas",
        "saveas2",
        "savecopyas",
    }
)
_FORMAT_NAME_MARKERS = (
    "align",
    "autofit",
    "bold",
    "border",
    "color",
    "font",
    "format",
    "height",
    "indent",
    "italic",
    "layout",
    "margin",
    "orientation",
    "position",
    "shading",
    "size",
    "spacing",
    "style",
    "underline",
    "width",
)
_CONTENT_NAME_MARKERS = (
    "append",
    "caption",
    "copy",
    "cut",
    "insert",
    "paste",
    "replace",
    "text",
    "type",
)
_STRUCTURE_NAME_MARKERS = (
    "accept",
    "add",
    "apply",
    "bookmark",
    "build",
    "collapse",
    "delete",
    "field",
    "list",
    "merge",
    "move",
    "paragraph",
    "reject",
    "row",
    "section",
    "setrange",
    "sort",
    "split",
    "table",
)
_CALCULATION_NAME_MARKERS = (
    "builddown",
    "buildup",
    "calculate",
    "compute",
    "formula",
    "statistic",
    "update",
)
_VIEW_NAME_MARKERS = (
    "activate",
    "arrange",
    "display",
    "scroll",
    "select",
    "show",
    "view",
    "window",
    "zoom",
)
_READ_METHOD_PREFIXES = (
    "_default",
    "_newenum",
    "can",
    "compare",
    "compute",
    "count",
    "get",
    "has",
    "information",
    "is",
    "item",
)
_REFERENCE_NAME = re.compile(r"[A-Za-z][A-Za-z0-9_]{0,63}")
_INTEGER_TYPES = frozenset({"I1", "I2", "I4", "I8", "INT", "UI1", "UI2", "UI4", "UI8", "UINT"})
_FLOAT_TYPES = frozenset({"CY", "DECIMAL", "R4", "R8"})
_ANY_TYPES = frozenset({"DISPATCH", "EMPTY", "NULL", "UNKNOWN", "VARIANT"})


@dataclass(frozen=True, slots=True)
class PreparedMemberOperation:
    operation_id: str
    capability_id: str
    target_kind: str
    target_result_id: str
    arguments: tuple[Any, ...]
    result_id: str
    profile: dict[str, Any]


def _text(value: Any, limit: int = 256) -> str:
    return str(value or "")[:limit]


def _integer(value: Any) -> int:
    try:
        return int(value)
    except (TypeError, ValueError, OverflowError):
        return 0


def _stable_id(prefix: str, parts: list[Any]) -> str:
    serialized = json.dumps(parts, ensure_ascii=True, separators=(",", ":"))
    digest = hashlib.sha256(serialized.encode("utf-8")).hexdigest()[:32]
    return f"{prefix}_{digest}"


def _event_type(type_name: str) -> bool:
    normalized = type_name.casefold()
    return any(marker in normalized for marker in _EVENT_TYPE_MARKERS)


def _effect(type_name: str, name: str, member_kind: str) -> str:
    lowered_name = name.casefold()
    if member_kind == "enum_value":
        return "constant"
    if _event_type(type_name):
        return "event"
    if lowered_name in _LIFECYCLE_NAMES:
        return "lifecycle"
    if any(marker in lowered_name for marker in _EXTERNAL_NAME_MARKERS):
        return "external"
    if any(marker in lowered_name for marker in _FORMAT_NAME_MARKERS):
        return "format"
    if any(marker in lowered_name for marker in _CALCULATION_NAME_MARKERS):
        return "calculation"
    if any(marker in lowered_name for marker in _CONTENT_NAME_MARKERS):
        return "content"
    if any(marker in lowered_name for marker in _STRUCTURE_NAME_MARKERS):
        return "structure"
    if any(marker in lowered_name for marker in _VIEW_NAME_MARKERS):
        return "view"
    if member_kind in {"property_get", "variable"}:
        return "read"
    return "unknown"


def _target_roots(type_name: str) -> list[str]:
    normalized = type_name.casefold()
    if normalized in _DOCUMENT_TYPES:
        return ["document", "result"]
    if normalized == "selection":
        return ["selection", "result"]
    if normalized == "range":
        return ["document_content", "selection_range", "result"]
    return ["result"]


def _execution_policy(
    *,
    type_name: str,
    member: dict[str, Any],
    effect: str,
) -> tuple[str, str, bool]:
    member_kind = _text(member.get("kind"), 64)
    name = _text(member.get("name")).casefold()
    flags = {_text(item, 32) for item in member.get("flag_names", [])}
    type_normalized = type_name.casefold()

    if member_kind == "enum_value":
        return "metadata_only", "enum_constant_has_no_runtime_target", False
    if member_kind == "variable":
        return "metadata_only", "type_library_variable_is_metadata_only", False
    if effect == "event" or "source" in flags:
        return "blocked", "event_callback_is_not_an_invocable_edit", False
    if "restricted" in flags:
        return "blocked", "type_library_marks_member_restricted", False
    if type_normalized in _EXTERNAL_TYPES:
        return "blocked", "external_type_mutation_is_out_of_scope", False
    if effect == "lifecycle":
        return "blocked", "document_or_application_lifecycle_is_out_of_scope", False
    if effect == "external":
        return "blocked", "external_side_effect_is_out_of_scope", False

    if member_kind == "property_get":
        if name in _SENSITIVE_READ_NAMES:
            return "blocked", "sensitive_or_external_metadata_is_not_returned", False
        return "read_allowed", "bounded_property_read", False

    if member_kind in {"property_put", "property_put_ref"}:
        if type_normalized in _APPLICATION_TYPES:
            return "blocked", "application_global_mutation_is_out_of_scope", False
        if name in _SENSITIVE_READ_NAMES:
            return "blocked", "sensitive_or_external_setting_is_out_of_scope", False
        if _integer(member.get("parameter_count")) != 1:
            return (
                "blocked",
                "indexed_property_setter_is_not_verified_undoable",
                False,
            )
        return "write_allowed", "document_scoped_property_write", True

    if member_kind == "method":
        if type_normalized in _APPLICATION_TYPES:
            return "blocked", "application_global_mutation_is_out_of_scope", False
        if name.startswith(_READ_METHOD_PREFIXES):
            return "read_allowed", "bounded_method_read", False
        if effect == "view":
            return "blocked", "view_state_action_requires_a_dedicated_tool", False
        if effect == "unknown":
            return "blocked", "method_effect_has_not_been_proven_document_scoped", False
        return "write_allowed", "document_scoped_method_call", True

    return "blocked", "unsupported_invocation_kind", False


def _parameter_payload(parameter: Any) -> dict[str, Any]:
    if not isinstance(parameter, dict):
        parameter = {}
    payload: dict[str, Any] = {
        "name": _text(parameter.get("name")),
        "type": _text(parameter.get("type"), 256) or "UNKNOWN",
        "flags": _integer(parameter.get("flags")),
        "flag_names": [_text(item, 32) for item in parameter.get("flag_names", [])[:16]],
        "optional": bool(parameter.get("optional", False)),
    }
    if "default_value" in parameter:
        value = parameter.get("default_value")
        payload["default_value"] = (
            value if value is None or isinstance(value, (bool, int, float, str)) else None
        )
    return payload


def _normalized_input_parameters(
    member_kind: str,
    parameters: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    normalized = [dict(parameter) for parameter in parameters]
    if member_kind in {"property_put", "property_put_ref"} and normalized:
        for parameter in normalized[:-1]:
            parameter["role"] = "index"
        normalized[-1]["optional"] = False
        normalized[-1]["role"] = "value"
    return normalized


def _safe_tool_segment(value: str, limit: int) -> str:
    segment = re.sub(r"[^a-z0-9]+", "_", value.casefold()).strip("_")
    return (segment or "member")[:limit].rstrip("_")


def _reference_schema(reference_name: str) -> dict[str, Any]:
    return {
        "type": "object",
        "additionalProperties": False,
        "required": [reference_name],
        "properties": {
            reference_name: {
                "type": "string",
                "pattern": _REFERENCE_NAME.pattern,
                "maxLength": 64,
            }
        },
    }


def _primitive_json_schema(
    expected_type: str,
    enum_types: frozenset[str],
) -> dict[str, Any]:
    base_type = expected_type.upper().rstrip("&*")
    normalized = _normalized_com_type(expected_type)
    if normalized in enum_types:
        return {"type": "integer"}
    if base_type == "BSTR":
        return {"type": "string", "maxLength": 100_000}
    if base_type == "BOOL":
        return {"type": "boolean"}
    if base_type in _INTEGER_TYPES:
        return {"type": "integer"}
    if base_type in _FLOAT_TYPES:
        return {"type": "number"}
    if base_type in {"DATE", "FILETIME"}:
        return {"type": ["string", "number"]}
    if base_type in _ANY_TYPES:
        return {"type": ["null", "boolean", "integer", "number", "string"]}
    return {"type": "null"}


def _parameter_json_schema(
    parameter: dict[str, Any],
    enum_types: frozenset[str],
) -> dict[str, Any]:
    expected_type = str(parameter["type"])
    variants = [
        _primitive_json_schema(expected_type, enum_types),
        _reference_schema("result_id"),
    ]
    if _normalized_com_type(expected_type) in enum_types or (
        expected_type.upper().rstrip("&*") in _INTEGER_TYPES | _ANY_TYPES
    ):
        variants.append(_reference_schema("constant_id"))
    if bool(parameter["optional"]):
        variants.append(
            {
                "type": "object",
                "additionalProperties": False,
                "required": ["missing"],
                "properties": {"missing": {"const": True}},
            }
        )
    schema: dict[str, Any] = {
        "title": str(parameter["name"])[:128],
        "description": (f"Word COM parameter {parameter['name']} ({expected_type})"),
        "oneOf": variants,
    }
    if "default_value" in parameter:
        schema["default"] = parameter["default_value"]
    return schema


def _target_json_schema(allowed_roots: list[str]) -> dict[str, Any]:
    variants: list[dict[str, Any]] = []
    for root in allowed_roots:
        properties: dict[str, Any] = {"kind": {"const": root}}
        required = ["kind"]
        if root == "result":
            properties["result_id"] = {
                "type": "string",
                "pattern": _REFERENCE_NAME.pattern,
                "maxLength": 64,
            }
            required.append("result_id")
        variants.append(
            {
                "type": "object",
                "additionalProperties": False,
                "required": required,
                "properties": properties,
            }
        )
    return {"oneOf": variants}


def _void_com_type(value: str) -> bool:
    return _normalized_com_type(value) in {"void", "empty", "null"}


def _input_parameters(profile: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        parameter
        for parameter in profile["signature"]["parameters"]
        if "out" not in parameter["flag_names"] or "in" in parameter["flag_names"]
    ]


def _virtual_tool_definition(
    profile: dict[str, Any],
    enum_types: frozenset[str],
) -> dict[str, Any]:
    capability_id = str(profile["capability_id"])
    type_name = str(profile["type"]["name"])
    member_name = str(profile["member"]["name"])
    member_kind = str(profile["member"]["kind"])
    execution = str(profile["policy"]["execution"])
    tool_kind = {
        "enum_value": "constant",
        "property_get": "read",
        "property_put": "edit",
        "property_put_ref": "edit",
        "method": "call",
        "variable": "metadata",
    }.get(member_kind, "unavailable")
    if execution == "blocked":
        tool_kind = "unavailable"
    tool_name = "_".join(
        (
            "wm",
            _safe_tool_segment(type_name, 24),
            _safe_tool_segment(member_name, 32),
            _safe_tool_segment(member_kind, 16),
            capability_id.rsplit("_", 1)[-1][:12],
        )
    )
    return_type = str(profile["signature"]["return_type"])

    if member_kind == "enum_value":
        constant = profile["constant"]
        input_schema: dict[str, Any] = {
            "type": "object",
            "additionalProperties": False,
            "maxProperties": 0,
        }
        output_schema: dict[str, Any] = {
            "type": "object",
            "additionalProperties": False,
            "required": ["constant_id", "type", "value"],
            "properties": {
                "constant_id": {"const": capability_id},
                "type": {"const": type_name},
                "value": {"const": constant["value"]},
            },
        }
        endpoint = "inspect_live_word_member_capabilities"
    elif execution not in {"read_allowed", "write_allowed"}:
        input_schema = {"type": "object", "not": {}}
        output_schema = {
            "type": "object",
            "additionalProperties": False,
            "required": ["capability_id", "execution", "reason"],
            "properties": {
                "capability_id": {"const": capability_id},
                "execution": {"const": execution},
                "reason": {"const": profile["policy"]["reason"]},
            },
        }
        endpoint = "inspect_live_word_member_capabilities"
    else:
        parameters = _input_parameters(profile)
        required_positions = [
            index for index, parameter in enumerate(parameters) if not bool(parameter["optional"])
        ]
        minimum_arguments = max(required_positions, default=-1) + 1
        arguments_schema: dict[str, Any] = {
            "type": "array",
            "minItems": minimum_arguments,
        }
        if parameters:
            arguments_schema["prefixItems"] = [
                _parameter_json_schema(parameter, enum_types) for parameter in parameters
            ]
        if bool(profile["signature"]["variadic"]):
            arguments_schema["items"] = {
                "oneOf": [
                    _primitive_json_schema("VARIANT", enum_types),
                    _reference_schema("result_id"),
                    _reference_schema("constant_id"),
                ]
            }
        else:
            arguments_schema["maxItems"] = len(parameters)
        properties = {
            "operation_id": {
                "type": "string",
                "pattern": _REFERENCE_NAME.pattern,
                "maxLength": 64,
            },
            "capability_id": {"const": capability_id},
            "target": _target_json_schema(profile["target"]["allowed_roots"]),
            "arguments": arguments_schema,
        }
        if not _void_com_type(return_type):
            properties["result_id"] = {
                "type": "string",
                "pattern": _REFERENCE_NAME.pattern,
                "maxLength": 64,
            }
        required = ["capability_id", "target"]
        if minimum_arguments:
            required.append("arguments")
        input_schema = {
            "type": "object",
            "additionalProperties": False,
            "required": required,
            "properties": properties,
        }
        output_schema = {
            "type": "object",
            "additionalProperties": False,
            "required": ["operation_id", "capability_id", "executed"],
            "properties": {
                "operation_id": {"type": "string"},
                "capability_id": {"const": capability_id},
                "executed": {"const": True},
                "result_id": {"type": "string"},
                "declared_type": {"const": return_type},
                "value": {},
            },
        }
        endpoint = "execute_live_word_member_operations"

    return {
        "tool_id": capability_id,
        "name": tool_name,
        "title": f"{type_name}.{member_name} [{member_kind}]",
        "kind": tool_kind,
        "availability": execution,
        "endpoint": endpoint,
        "input_schema": input_schema,
        "output_schema": output_schema,
    }


def build_member_capability_registry(catalog: dict[str, Any]) -> dict[str, Any]:
    """Derive one deterministic virtual tool definition for every catalog member."""

    library = catalog.get("library", {})
    library_identity = [
        _text(library.get("guid"), 64),
        _integer(library.get("major_version")),
        _integer(library.get("minor_version")),
    ]
    profiles: list[dict[str, Any]] = []
    enum_types = frozenset(
        _normalized_com_type(_text(item.get("name")))
        for item in catalog.get("types", [])
        if isinstance(item, dict) and _text(item.get("kind"), 64) == "enum"
    )

    for type_item in catalog.get("types", []):
        if not isinstance(type_item, dict):
            continue
        type_name = _text(type_item.get("name"))
        type_identity = [
            *library_identity,
            _text(type_item.get("guid"), 64),
            _integer(type_item.get("type_index")),
            type_name,
        ]
        for member in type_item.get("members", []):
            if not isinstance(member, dict):
                continue
            member_name = _text(member.get("name"))
            member_kind = _text(member.get("kind"), 64)
            identity = [
                *type_identity,
                _integer(member.get("member_id")),
                _integer(member.get("declaration_index")),
                member_kind,
                member_name,
            ]
            effect = _effect(type_name, member_name, member_kind)
            execution, reason, mutating = _execution_policy(
                type_name=type_name,
                member=member,
                effect=effect,
            )
            parameters = _normalized_input_parameters(
                member_kind,
                [_parameter_payload(parameter) for parameter in member.get("parameters", [])[:255]],
            )
            profile: dict[str, Any] = {
                "capability_id": _stable_id("wmc1", identity),
                "accessor_group_id": _stable_id(
                    "wma1",
                    [
                        *type_identity,
                        _integer(member.get("member_id")),
                        member_name,
                    ],
                ),
                "type": {
                    "name": type_name,
                    "kind": _text(type_item.get("kind"), 64),
                    "type_index": _integer(type_item.get("type_index")),
                    "guid": _text(type_item.get("guid"), 64),
                },
                "member": {
                    "name": member_name,
                    "kind": member_kind,
                    "member_id": _integer(member.get("member_id")),
                    "declaration_index": _integer(member.get("declaration_index")),
                    "flags": _integer(member.get("flags")),
                    "invoke_kind": _integer(member.get("invoke_kind")),
                    "function_kind": _integer(member.get("function_kind")),
                    "call_convention": _integer(member.get("call_convention")),
                    "flag_names": [_text(item, 32) for item in member.get("flag_names", [])[:16]],
                },
                "signature": {
                    "parameters": parameters,
                    "parameter_count": _integer(member.get("parameter_count")),
                    "optional_parameter_count": _integer(member.get("optional_parameter_count")),
                    "variadic": bool(member.get("variadic", False)),
                    "return_type": _text(
                        member.get("return_type", member.get("type", "UNKNOWN")),
                        256,
                    )
                    or "UNKNOWN",
                },
                "target": {
                    "required_type": type_name,
                    "allowed_roots": _target_roots(type_name),
                    "result_chaining_allowed": True,
                },
                "policy": {
                    "effect": effect,
                    "execution": execution,
                    "reason": reason,
                    "mutating": mutating,
                    "undo_required": mutating,
                },
            }
            if member_kind == "enum_value":
                constant_value = member.get("value")
                profile["constant"] = {
                    "type": type_name,
                    "storage_type": _text(member.get("type"), 256) or "UNKNOWN",
                    "value": (
                        constant_value
                        if constant_value is None
                        or isinstance(constant_value, (bool, int, float, str))
                        else None
                    ),
                }
            profiles.append(profile)

    profiles.sort(
        key=lambda item: (
            item["type"]["name"].casefold(),
            item["member"]["name"].casefold(),
            item["member"]["kind"],
            item["member"]["declaration_index"],
        )
    )
    for profile in profiles:
        profile["virtual_tool"] = _virtual_tool_definition(profile, enum_types)
    ids = [item["capability_id"] for item in profiles]
    tool_names = [item["virtual_tool"]["name"] for item in profiles]
    execution_counts = Counter(item["policy"]["execution"] for item in profiles)
    effect_counts = Counter(item["policy"]["effect"] for item in profiles)
    kind_counts = Counter(item["member"]["kind"] for item in profiles)
    declared_members = _integer(catalog.get("stats", {}).get("member_count"))
    complete = (
        len(profiles) == declared_members
        and len(ids) == len(set(ids))
        and len(tool_names) == len(set(tool_names))
        and all(
            isinstance(item["virtual_tool"].get("input_schema"), dict)
            and isinstance(item["virtual_tool"].get("output_schema"), dict)
            for item in profiles
        )
    )
    return {
        "schema_version": CAPABILITY_SCHEMA_VERSION,
        "catalog_generated_at": _text(catalog.get("generated_at"), 64),
        "library": {
            "guid": library_identity[0],
            "major_version": library_identity[1],
            "minor_version": library_identity[2],
        },
        "stats": {
            "catalog_member_count": declared_members,
            "profile_count": len(profiles),
            "unique_capability_id_count": len(set(ids)),
            "virtual_tool_count": len(tool_names),
            "unique_virtual_tool_name_count": len(set(tool_names)),
            "complete": complete,
            "execution_counts": dict(sorted(execution_counts.items())),
            "effect_counts": dict(sorted(effect_counts.items())),
            "member_kind_counts": dict(sorted(kind_counts.items())),
        },
        "profiles": profiles,
    }


def capability_index(registry: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {
        str(profile.get("capability_id", "")): profile
        for profile in registry.get("profiles", [])
        if isinstance(profile, dict) and profile.get("capability_id")
    }


def _normalized_com_type(value: str) -> str:
    normalized = value.strip().casefold().replace(" ", "")
    while normalized.endswith(("*", "&")):
        normalized = normalized[:-1]
    return normalized.removeprefix("_")


def _compatible_com_type(actual: str, expected: str) -> bool:
    actual_normalized = _normalized_com_type(actual)
    expected_normalized = _normalized_com_type(expected)
    if actual_normalized in {"dispatch", "unknown", "variant"}:
        return False
    if expected_normalized in {"dispatch", "unknown", "variant"}:
        return True
    return actual_normalized == expected_normalized


def _argument_reference(value: Any) -> tuple[str, str]:
    if not isinstance(value, dict):
        return "", ""
    keys = set(value)
    if keys == {"missing"}:
        if value.get("missing") is not True:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "The missing argument marker must be exactly {'missing': true}",
            )
        return "missing", ""
    if keys not in ({"result_id"}, {"constant_id"}):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "Argument objects may contain one result_id, constant_id, or missing marker",
        )
    reference_kind = "result" if "result_id" in value else "constant"
    field_name = f"{reference_kind}_id"
    reference_id = value.get(field_name)
    if not isinstance(reference_id, str) or not _REFERENCE_NAME.fullmatch(reference_id):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            f"Argument {field_name} is invalid",
        )
    return reference_kind, reference_id


def _validate_primitive_argument(
    value: Any,
    expected_type: str,
    enum_types: frozenset[str],
) -> None:
    base_type = expected_type.upper().rstrip("&*")
    normalized = _normalized_com_type(expected_type)
    if value is None:
        if base_type in _ANY_TYPES:
            return
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "null is valid only for a VARIANT or dispatch-compatible parameter",
            {"expected_type": expected_type},
        )
    if base_type == "BSTR" and not isinstance(value, str):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "A Word BSTR parameter requires a string",
            {"expected_type": expected_type},
        )
    if base_type == "BOOL" and not isinstance(value, bool):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "A Word BOOL parameter requires true or false",
            {"expected_type": expected_type},
        )
    if base_type in _INTEGER_TYPES and (isinstance(value, bool) or not isinstance(value, int)):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "An integer Word parameter requires an integer",
            {"expected_type": expected_type},
        )
    if base_type in _FLOAT_TYPES and (
        isinstance(value, bool) or not isinstance(value, (int, float))
    ):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "A numeric Word parameter requires a number",
            {"expected_type": expected_type},
        )
    if normalized in enum_types and (isinstance(value, bool) or not isinstance(value, int)):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "A Word enum parameter requires an integer or a typed constant_id",
            {"expected_type": expected_type},
        )
    if base_type in {"DATE", "FILETIME"} and (
        isinstance(value, bool) or not isinstance(value, (int, float, str))
    ):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "A Word date parameter requires a number or string",
            {"expected_type": expected_type},
        )
    if isinstance(value, str) and len(value) > 100_000:
        raise WordToolkitError(
            ErrorCode.LIMIT_EXCEEDED,
            "A member-operation string argument exceeds 100,000 characters",
        )
    if isinstance(value, (list, dict)):
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "Nested member-operation arguments are not supported",
        )
    known_scalar = (
        base_type in _ANY_TYPES
        or base_type in _INTEGER_TYPES
        or base_type in _FLOAT_TYPES
        or base_type in {"BOOL", "BSTR", "DATE", "FILETIME"}
        or normalized in enum_types
    )
    if not known_scalar and value is not None:
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "A Word object parameter requires a typed earlier result_id",
            {"expected_type": expected_type},
        )


def _validate_argument(
    value: Any,
    expected_type: str,
    available_results: dict[str, str],
    constants: dict[str, dict[str, Any]],
    enum_types: frozenset[str],
    *,
    optional: bool,
) -> Any:
    reference_kind, reference_id = _argument_reference(value)
    if reference_kind == "missing":
        if not optional:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Only an optional Word parameter may use {'missing': true}",
                {"expected_type": expected_type},
            )
        return value
    if reference_kind == "result":
        actual_type = available_results.get(reference_id)
        if actual_type is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Argument result_id must refer to an earlier operation",
                {"result_id": reference_id},
            )
        if not _compatible_com_type(actual_type, expected_type):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "An argument result type does not match the Word parameter type",
                {
                    "result_id": reference_id,
                    "actual_type": actual_type,
                    "expected_type": expected_type,
                },
            )
        return value
    if reference_kind == "constant":
        constant = constants.get(reference_id)
        if constant is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Argument constant_id must refer to an enum virtual tool",
                {"constant_id": reference_id},
            )
        expected_normalized = _normalized_com_type(expected_type)
        constant_type = _normalized_com_type(str(constant["type"]["name"]))
        expected_base = expected_type.upper().rstrip("&*")
        if (
            expected_normalized != constant_type
            and expected_base not in _INTEGER_TYPES | _ANY_TYPES
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "The enum constant type does not match the Word parameter type",
                {
                    "constant_id": reference_id,
                    "constant_type": constant["type"]["name"],
                    "expected_type": expected_type,
                },
            )
        return constant["constant"]["value"]
    _validate_primitive_argument(value, expected_type, enum_types)
    return value


def prepare_member_operations(
    registry: dict[str, Any],
    operations: list[dict[str, Any]],
) -> list[PreparedMemberOperation]:
    if not isinstance(operations, list) or not 1 <= len(operations) <= 50:
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "operations must contain from 1 to 50 member operations",
        )
    try:
        encoded_size = len(
            json.dumps(operations, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        )
    except (TypeError, ValueError, OverflowError) as exc:
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "operations must contain JSON-compatible values",
        ) from exc
    if encoded_size > 512_000:
        raise WordToolkitError(
            ErrorCode.LIMIT_EXCEEDED,
            "member operations exceed the 512,000-byte preflight limit",
        )

    profiles = capability_index(registry)
    constants = {
        capability_id: profile
        for capability_id, profile in profiles.items()
        if profile.get("member", {}).get("kind") == "enum_value"
    }
    enum_types = frozenset(
        _normalized_com_type(str(profile["type"]["name"])) for profile in constants.values()
    )
    prepared: list[PreparedMemberOperation] = []
    operation_ids: set[str] = set()
    available_results: dict[str, str] = {}
    allowed_operation_keys = {
        "operation_id",
        "capability_id",
        "target",
        "arguments",
        "result_id",
    }

    for position, operation in enumerate(operations, start=1):
        if not isinstance(operation, dict):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Each member operation must be an object",
                {"position": position},
            )
        unknown_keys = sorted(set(operation) - allowed_operation_keys)
        if unknown_keys:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "A member operation contains unsupported fields",
                {"position": position, "fields": unknown_keys},
            )
        operation_id = operation.get("operation_id", f"op_{position}")
        if not isinstance(operation_id, str) or not _REFERENCE_NAME.fullmatch(operation_id):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "operation_id is invalid",
                {"position": position},
            )
        if operation_id in operation_ids:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "operation_id values must be unique",
                {"operation_id": operation_id},
            )
        operation_ids.add(operation_id)

        capability_id = operation.get("capability_id")
        if not isinstance(capability_id, str) or len(capability_id) > 64:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "capability_id must be a string of at most 64 characters",
                {"position": position},
            )
        profile = profiles.get(capability_id)
        if profile is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "The Word member capability_id was not found in this catalog",
                {"position": position, "capability_id": capability_id},
            )
        execution = str(profile["policy"]["execution"])
        if execution not in {"read_allowed", "write_allowed"}:
            raise WordToolkitError(
                ErrorCode.AUTH_FORBIDDEN,
                "The Word member capability is not executable",
                {
                    "position": position,
                    "capability_id": capability_id,
                    "execution": execution,
                    "reason": profile["policy"]["reason"],
                },
            )

        target = operation.get("target")
        if not isinstance(target, dict) or set(target) - {"kind", "result_id"}:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "target must contain a supported kind and optional result_id",
                {"position": position},
            )
        target_kind = target.get("kind")
        if not isinstance(target_kind, str) or target_kind not in {
            "document",
            "selection",
            "selection_range",
            "document_content",
            "result",
        }:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "target.kind is not supported",
                {"position": position},
            )
        allowed_roots = profile["target"]["allowed_roots"]
        if target_kind not in allowed_roots:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "The target root does not match the capability type",
                {
                    "position": position,
                    "target_kind": target_kind,
                    "required_type": profile["target"]["required_type"],
                    "allowed_roots": allowed_roots,
                },
            )
        target_result_id = ""
        if target_kind == "result":
            raw_target_result_id = target.get("result_id")
            if (
                not isinstance(raw_target_result_id, str)
                or not _REFERENCE_NAME.fullmatch(raw_target_result_id)
                or raw_target_result_id not in available_results
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "A result target must refer to an earlier operation",
                    {"position": position, "result_id": raw_target_result_id},
                )
            target_result_id = raw_target_result_id
            actual_type = available_results[target_result_id]
            required_type = str(profile["target"]["required_type"])
            if not _compatible_com_type(actual_type, required_type):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "The target result type does not match the capability type",
                    {
                        "position": position,
                        "actual_type": actual_type,
                        "required_type": required_type,
                    },
                )
        elif "result_id" in target:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "target.result_id is valid only when target.kind is result",
                {"position": position},
            )

        arguments = operation.get("arguments", [])
        if not isinstance(arguments, list) or len(arguments) > 64:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "arguments must be an array of at most 64 values",
                {"position": position},
            )
        input_parameters = _input_parameters(profile)
        required_positions = [
            index for index, parameter in enumerate(input_parameters) if not parameter["optional"]
        ]
        required_count = max(required_positions, default=-1) + 1
        variadic = bool(profile["signature"]["variadic"])
        if len(arguments) < required_count or (
            not variadic and len(arguments) > len(input_parameters)
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "The argument count does not match the Word member signature",
                {
                    "position": position,
                    "provided": len(arguments),
                    "required": required_count,
                    "maximum": None if variadic else len(input_parameters),
                },
            )
        normalized_arguments = []
        for argument_index, value in enumerate(arguments):
            expected_type = (
                str(input_parameters[argument_index]["type"])
                if argument_index < len(input_parameters)
                else "VARIANT"
            )
            normalized_arguments.append(
                _validate_argument(
                    value,
                    expected_type,
                    available_results,
                    constants,
                    enum_types,
                    optional=(
                        bool(input_parameters[argument_index]["optional"])
                        if argument_index < len(input_parameters)
                        else False
                    ),
                )
            )

        result_id = operation.get("result_id", "")
        if result_id:
            if not isinstance(result_id, str) or not _REFERENCE_NAME.fullmatch(result_id):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "result_id is invalid",
                    {"position": position},
                )
            if result_id in available_results:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "result_id values must be unique",
                    {"position": position, "result_id": result_id},
                )
            return_type = str(profile["signature"]["return_type"])
            if _normalized_com_type(return_type) in {"void", "empty", "null"}:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "A void Word member cannot publish a result_id",
                    {"position": position},
                )
            available_results[result_id] = return_type
        elif result_id is not None and not isinstance(result_id, str):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "result_id must be a string",
                {"position": position},
            )

        prepared.append(
            PreparedMemberOperation(
                operation_id=operation_id,
                capability_id=capability_id,
                target_kind=target_kind,
                target_result_id=target_result_id,
                arguments=tuple(normalized_arguments),
                result_id=result_id,
                profile=profile,
            )
        )
    return prepared


def member_preflight_payload(
    registry: dict[str, Any],
    prepared: list[PreparedMemberOperation],
) -> dict[str, Any]:
    operations = []
    for item in prepared:
        operations.append(
            {
                "operation_id": item.operation_id,
                "capability_id": item.capability_id,
                "member": (f"{item.profile['type']['name']}.{item.profile['member']['name']}"),
                "member_kind": item.profile["member"]["kind"],
                "target_kind": item.target_kind,
                "target_result_id": item.target_result_id,
                "argument_count": len(item.arguments),
                "result_id": item.result_id,
                "return_type": item.profile["signature"]["return_type"],
                "effect": item.profile["policy"]["effect"],
                "execution": item.profile["policy"]["execution"],
                "mutating": item.profile["policy"]["mutating"],
            }
        )
    mutating_count = sum(1 for item in prepared if bool(item.profile["policy"]["mutating"]))
    return {
        "valid": True,
        "registry_complete": bool(registry["stats"]["complete"]),
        "registry_profile_count": int(registry["stats"]["profile_count"]),
        "operation_count": len(prepared),
        "mutating_count": mutating_count,
        "read_count": len(prepared) - mutating_count,
        "requires_expected_version": mutating_count > 0,
        "single_com_attachment_on_execute": True,
        "single_undo_record_on_execute": mutating_count > 0,
        "operations": operations,
    }
