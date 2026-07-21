from __future__ import annotations

import json
import os
import threading
import time
from contextlib import suppress
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

TYPE_KINDS = {
    0: "enum",
    1: "record",
    2: "module",
    3: "interface",
    4: "dispatch",
    5: "coclass",
    6: "alias",
    7: "union",
}
MEMBER_KINDS = {
    1: "method",
    2: "property_get",
    4: "property_put",
    8: "property_put_ref",
}
VARTYPES = {
    0: "EMPTY",
    1: "NULL",
    2: "I2",
    3: "I4",
    4: "R4",
    5: "R8",
    6: "CY",
    7: "DATE",
    8: "BSTR",
    9: "DISPATCH",
    10: "ERROR",
    11: "BOOL",
    12: "VARIANT",
    13: "UNKNOWN",
    14: "DECIMAL",
    16: "I1",
    17: "UI1",
    18: "UI2",
    19: "UI4",
    20: "I8",
    21: "UI8",
    22: "INT",
    23: "UINT",
    24: "VOID",
    25: "HRESULT",
    26: "PTR",
    27: "SAFEARRAY",
    28: "CARRAY",
    29: "USERDEFINED",
    30: "LPSTR",
    31: "LPWSTR",
    36: "RECORD",
}
PARAM_FLAGS = {
    1: "in",
    2: "out",
    4: "lcid",
    8: "retval",
    16: "optional",
    32: "has_default",
    64: "has_custom_data",
}
FUNCTION_FLAGS = {
    1: "restricted",
    2: "source",
    4: "bindable",
    8: "request_edit",
    16: "display_bind",
    32: "default_bind",
    64: "hidden",
    128: "uses_get_last_error",
    256: "default_collection_element",
    512: "ui_default",
    1024: "non_browsable",
    2048: "replaceable",
    4096: "immediate_bind",
}
IMPLEMENTED_TYPE_FLAGS = {
    1: "default",
    2: "source",
    4: "restricted",
    8: "default_vtable",
}
BASE_DISPATCH_MEMBERS = {
    "QueryInterface",
    "AddRef",
    "Release",
    "GetTypeInfoCount",
    "GetTypeInfo",
    "GetIDsOfNames",
    "Invoke",
}
VALID_TYPE_KINDS = frozenset(TYPE_KINDS.values())
VALID_MEMBER_KINDS = frozenset(
    {*MEMBER_KINDS.values(), "enum_value", "variable"}
)


def _bounded_name(value: Any, fallback: str) -> str:
    text = str(value or "")
    text = "".join(" " if ord(character) < 32 else character for character in text)
    text = " ".join(text.split())
    return text[:256] or fallback


def _integer(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError, OverflowError):
        return default


def _type_name(type_info: Any, reference: Any) -> str:
    try:
        referenced = type_info.GetRefTypeInfo(reference)
        return _bounded_name(
            referenced.GetDocumentation(-1)[0],
            f"USERDEFINED({reference})",
        )
    except Exception:
        return f"USERDEFINED({reference})"


def _typedesc_name(type_info: Any, descriptor: Any, *, depth: int = 0) -> str:
    if depth >= 8:
        return "UNKNOWN"
    if isinstance(descriptor, int):
        raw_type = descriptor
        detail = None
    elif isinstance(descriptor, (tuple, list)) and descriptor:
        raw_type = _integer(descriptor[0], -1)
        detail = descriptor[1] if len(descriptor) > 1 else None
    else:
        return "UNKNOWN"

    by_reference = bool(raw_type & 0x4000)
    is_array = bool(raw_type & 0x2000)
    base_type = raw_type & 0x0FFF
    if base_type == 29 and detail is not None:
        label = _type_name(type_info, detail)
    elif base_type in {26, 27, 28} and detail is not None:
        nested = _typedesc_name(type_info, detail, depth=depth + 1)
        if base_type == 26:
            label = f"{nested}*"
        elif base_type == 27:
            label = f"SAFEARRAY({nested})"
        else:
            label = f"CARRAY({nested})"
    else:
        label = VARTYPES.get(base_type, f"VT_{base_type}")
    if is_array and not label.startswith(("SAFEARRAY(", "CARRAY(")):
        label = f"SAFEARRAY({label})"
    if by_reference:
        label = f"{label}&"
    return label[:256]


def _element_type(type_info: Any, element_description: Any) -> str:
    if isinstance(element_description, (tuple, list)) and element_description:
        return _typedesc_name(type_info, element_description[0])
    return _typedesc_name(type_info, element_description)


def _flag_names(value: int, mapping: dict[int, str]) -> list[str]:
    return [name for bit, name in mapping.items() if value & bit]


def _safe_default(value: Any) -> bool | int | float | str | None:
    if value is None or isinstance(value, (bool, int, float)):
        return value
    if isinstance(value, str):
        return _bounded_name(value, "")
    return None


def _function_member(type_info: Any, descriptor: Any, index: int) -> dict[str, Any] | None:
    member_id = _integer(descriptor[0])
    raw_parameters = descriptor[2] if len(descriptor) > 2 else ()
    if not isinstance(raw_parameters, (tuple, list)):
        raw_parameters = ()
    parameter_count = len(raw_parameters)
    raw_optional_count = _integer(descriptor[6])
    variadic = raw_optional_count == -1
    optional_count = (
        -1
        if variadic
        else max(0, min(parameter_count, raw_optional_count))
    )
    try:
        names = tuple(type_info.GetNames(member_id))
    except Exception:
        names = ()
    name = _bounded_name(names[0] if names else "", f"member_{member_id}")
    if name in BASE_DISPATCH_MEMBERS:
        return None

    invoke_kind = _integer(descriptor[4], 1)
    member_kind = MEMBER_KINDS.get(invoke_kind, f"invoke_{invoke_kind}")
    parameters: list[dict[str, Any]] = []
    for parameter_index in range(parameter_count):
        element = raw_parameters[parameter_index]
        flags = (
            _integer(element[1])
            if isinstance(element, (tuple, list)) and len(element) > 1
            else 0
        )
        optional_by_position = (
            optional_count > 0
            and parameter_index >= parameter_count - optional_count
        )
        parameter = {
            "name": _bounded_name(
                names[parameter_index + 1]
                if parameter_index + 1 < len(names)
                else "",
                f"arg{parameter_index + 1}",
            ),
            "type": _element_type(type_info, element),
            "flags": flags,
            "flag_names": _flag_names(flags, PARAM_FLAGS),
            "optional": bool(flags & 16 or optional_by_position),
        }
        if flags & 32:
            raw_default = (
                element[2]
                if isinstance(element, (tuple, list)) and len(element) > 2
                else None
            )
            parameter["default_value"] = _safe_default(raw_default)
        parameters.append(parameter)
    function_flags = _integer(descriptor[9])
    return {
        "name": name,
        "kind": member_kind,
        "member_id": member_id,
        "declaration_index": index,
        "function_kind": _integer(descriptor[3]),
        "invoke_kind": invoke_kind,
        "call_convention": _integer(descriptor[5]),
        "vtable_offset": _integer(descriptor[7]),
        "parameters": parameters,
        "parameter_count": parameter_count,
        "optional_parameter_count": optional_count,
        "variadic": variadic,
        "return_type": _element_type(type_info, descriptor[8]),
        "flags": function_flags,
        "flag_names": _flag_names(function_flags, FUNCTION_FLAGS),
    }


def _variable_member(
    type_info: Any,
    descriptor: Any,
    index: int,
    *,
    enum_type: bool,
) -> dict[str, Any]:
    member_id = _integer(descriptor[0])
    try:
        names = tuple(type_info.GetNames(member_id))
    except Exception:
        names = ()
    member: dict[str, Any] = {
        "name": _bounded_name(names[0] if names else "", f"member_{member_id}"),
        "kind": "enum_value" if enum_type else "variable",
        "member_id": member_id,
        "declaration_index": index,
        "type": _element_type(type_info, descriptor[2]),
        "flags": _integer(descriptor[3]),
    }
    if enum_type:
        value = descriptor[1]
        member["value"] = (
            value if isinstance(value, (bool, int, float, str)) else None
        )
    return member


def scan_word_object_model(
    application: Any,
    *,
    max_types: int = 2_000,
    max_members_per_type: int = 2_000,
    max_total_members: int = 50_000,
) -> dict[str, Any]:
    """Read the installed Word type library without touching document content."""

    started = time.perf_counter()
    application_type_info = application._oleobj_.GetTypeInfo()
    type_library, application_type_index = application_type_info.GetContainingTypeLib()
    library_attributes = tuple(type_library.GetLibAttr())
    declared_type_count = max(0, _integer(type_library.GetTypeInfoCount()))
    scanned_type_count = min(declared_type_count, max_types)
    types: list[dict[str, Any]] = []
    total_members = 0
    scan_errors = 0
    truncated = declared_type_count > scanned_type_count

    for type_index in range(scanned_type_count):
        try:
            type_info = type_library.GetTypeInfo(type_index)
            attributes = tuple(type_info.GetTypeAttr())
            try:
                documentation = type_info.GetDocumentation(-1)
                name = _bounded_name(documentation[0], f"type_{type_index}")
            except Exception:
                name = f"type_{type_index}"
            type_kind_value = _integer(attributes[5], -1)
            function_count = max(0, _integer(attributes[6]))
            variable_count = max(0, _integer(attributes[7]))
            implemented_type_count = max(0, _integer(attributes[8]))
            implemented_types: list[dict[str, Any]] = []
            for implemented_index in range(implemented_type_count):
                try:
                    reference = type_info.GetRefTypeOfImplType(implemented_index)
                    implemented_info = type_info.GetRefTypeInfo(reference)
                    implemented_attributes = tuple(implemented_info.GetTypeAttr())
                    implemented_flags = _integer(
                        type_info.GetImplTypeFlags(implemented_index)
                    )
                    implemented_types.append(
                        {
                            "name": _bounded_name(
                                implemented_info.GetDocumentation(-1)[0],
                                f"implemented_type_{implemented_index}",
                            ),
                            "kind": TYPE_KINDS.get(
                                _integer(implemented_attributes[5], -1),
                                f"typekind_{_integer(implemented_attributes[5], -1)}",
                            ),
                            "guid": str(implemented_attributes[0])[:64],
                            "flags": implemented_flags,
                            "flag_names": _flag_names(
                                implemented_flags,
                                IMPLEMENTED_TYPE_FLAGS,
                            ),
                        }
                    )
                except Exception:
                    scan_errors += 1
            member_limit = min(
                max_members_per_type,
                max(0, max_total_members - total_members),
            )
            members: list[dict[str, Any]] = []
            for function_index in range(min(function_count, member_limit)):
                try:
                    member = _function_member(
                        type_info,
                        type_info.GetFuncDesc(function_index),
                        function_index,
                    )
                    if member is not None:
                        members.append(member)
                except Exception:
                    scan_errors += 1
            remaining = max(0, member_limit - len(members))
            for variable_index in range(min(variable_count, remaining)):
                try:
                    members.append(
                        _variable_member(
                            type_info,
                            type_info.GetVarDesc(variable_index),
                            variable_index,
                            enum_type=type_kind_value == 0,
                        )
                    )
                except Exception:
                    scan_errors += 1
            if function_count + variable_count > member_limit:
                truncated = True
            total_members += len(members)
            types.append(
                {
                    "name": name,
                    "kind": TYPE_KINDS.get(type_kind_value, f"typekind_{type_kind_value}"),
                    "type_index": type_index,
                    "guid": str(attributes[0])[:64],
                    "flags": _integer(attributes[11]),
                    "declared_function_count": function_count,
                    "declared_variable_count": variable_count,
                    "implemented_type_count": implemented_type_count,
                    "implemented_types": implemented_types,
                    "member_count": len(members),
                    "members": members,
                }
            )
        except Exception:
            scan_errors += 1

    types.sort(key=lambda item: (str(item["name"]).casefold(), int(item["type_index"])))
    return {
        "schema_version": 2,
        "generated_at": datetime.now(UTC).isoformat(),
        "source": "installed_microsoft_word_com_type_library",
        "privacy": (
            "Only installed Word API metadata is stored. No document content, "
            "document counts, paths, handles, owners, help text or help-file paths are stored."
        ),
        "library": {
            "guid": str(library_attributes[0])[:64],
            "lcid": _integer(library_attributes[1]),
            "syskind": _integer(library_attributes[2]),
            "major_version": _integer(library_attributes[3]),
            "minor_version": _integer(library_attributes[4]),
            "flags": _integer(library_attributes[5]),
            "declared_type_count": declared_type_count,
            "application_type_index": _integer(application_type_index),
        },
        "stats": {
            "type_count": len(types),
            "member_count": total_members,
            "scan_errors": scan_errors,
            "truncated": truncated,
            "scan_duration_ms": round((time.perf_counter() - started) * 1_000, 3),
        },
        "types": types,
    }


class WordObjectModelStore:
    SCHEMA_VERSION = 2
    MAX_FILE_BYTES = 12 * 1024 * 1024
    MAX_TYPES = 2_000
    MAX_TOTAL_MEMBERS = 50_000

    def __init__(self, path: Path):
        self.path = path
        self._lock = threading.RLock()
        self._cached_payload: dict[str, Any] | None = None
        self._cached_signature: tuple[int, int] | None = None

    @classmethod
    def _valid(cls, payload: Any) -> bool:
        if (
            not isinstance(payload, dict)
            or payload.get("schema_version") != cls.SCHEMA_VERSION
            or not isinstance(payload.get("library"), dict)
            or not isinstance(payload.get("stats"), dict)
            or not isinstance(payload.get("types"), list)
            or len(payload["types"]) > cls.MAX_TYPES
        ):
            return False
        member_count = 0
        for item in payload["types"]:
            if not isinstance(item, dict) or not isinstance(item.get("members"), list):
                return False
            member_count += len(item["members"])
            if member_count > cls.MAX_TOTAL_MEMBERS:
                return False
        return True

    def load(self) -> dict[str, Any] | None:
        with self._lock:
            try:
                stat = self.path.stat()
            except OSError:
                self._cached_payload = None
                self._cached_signature = None
                return None
            if stat.st_size > self.MAX_FILE_BYTES:
                return None
            signature = (stat.st_mtime_ns, stat.st_size)
            if self._cached_payload is not None and signature == self._cached_signature:
                return self._cached_payload
            try:
                payload = json.loads(self.path.read_text(encoding="utf-8"))
            except (OSError, UnicodeError, json.JSONDecodeError):
                return None
            if not self._valid(payload):
                return None
            self._cached_payload = payload
            self._cached_signature = signature
            return payload

    def write(self, payload: dict[str, Any]) -> None:
        if not self._valid(payload):
            raise ValueError("Word object-model catalog is invalid or exceeds its limits")
        serialized = (
            json.dumps(payload, ensure_ascii=False, separators=(",", ":"), sort_keys=True)
            + "\n"
        ).encode("utf-8")
        if len(serialized) > self.MAX_FILE_BYTES:
            raise ValueError("Word object-model catalog exceeds its storage limit")
        with self._lock:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            temporary = self.path.with_name(
                f".{self.path.name}.{os.getpid()}.{threading.get_ident()}.tmp"
            )
            try:
                temporary.write_bytes(serialized)
                with suppress(OSError):
                    os.chmod(temporary, 0o600)
                temporary.replace(self.path)
                stat = self.path.stat()
                self._cached_payload = payload
                self._cached_signature = (stat.st_mtime_ns, stat.st_size)
            finally:
                with suppress(OSError):
                    temporary.unlink()
