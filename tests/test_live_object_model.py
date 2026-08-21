from __future__ import annotations

import json
from contextlib import contextmanager
from pathlib import Path
from typing import Any

import pytest

from wordtoolkit.config import Settings
from wordtoolkit.errors import ErrorCode, WordToolkitError
from wordtoolkit.live_object_model import WordObjectModelStore, scan_word_object_model
from wordtoolkit.live_word import LiveWordBridge


def _type_attributes(
    *,
    guid: str,
    kind: int,
    functions: int,
    variables: int,
) -> tuple[Any, ...]:
    return (
        guid,
        0,
        -1,
        -1,
        4,
        kind,
        functions,
        variables,
        0,
        0,
        4,
        0,
        8,
        7,
        None,
        (0, 0),
    )


class FakeTypeInfo:
    def __init__(
        self,
        name: str,
        attributes: tuple[Any, ...],
        *,
        functions: list[tuple[Any, ...]] | None = None,
        variables: list[tuple[Any, ...]] | None = None,
        names: dict[int, tuple[str, ...]] | None = None,
    ):
        self.name = name
        self.attributes = attributes
        self.functions = functions or []
        self.variables = variables or []
        self.names = names or {}

    def GetTypeAttr(self):
        return self.attributes

    def GetDocumentation(self, _member_id: int):
        return (
            self.name,
            "secret documentation that must not be stored",
            7,
            r"C:\private\VBAWD10.CHM",
        )

    def GetFuncDesc(self, index: int):
        return self.functions[index]

    def GetVarDesc(self, index: int):
        return self.variables[index]

    def GetNames(self, member_id: int):
        return self.names[member_id]

    def GetRefTypeInfo(self, _reference: Any):
        return self


class FakeTypeLibrary:
    def __init__(self, types: list[FakeTypeInfo]):
        self.types = types

    def GetLibAttr(self):
        return ("{00020905-0000-0000-C000-000000000046}", 0, 1, 8, 7, 8)

    def GetTypeInfoCount(self):
        return len(self.types)

    def GetTypeInfo(self, index: int):
        return self.types[index]


class FakeApplicationTypeInfo:
    def __init__(self, type_library: FakeTypeLibrary):
        self.type_library = type_library

    def GetContainingTypeLib(self):
        return self.type_library, 1


class FakeOleObject:
    def __init__(self, type_library: FakeTypeLibrary):
        self.type_library = type_library

    def GetTypeInfo(self):
        return FakeApplicationTypeInfo(self.type_library)


class FakeApplication:
    def __init__(self, type_library: FakeTypeLibrary):
        self._oleobj_ = FakeOleObject(type_library)


class FakeBackend:
    def __init__(self, application: FakeApplication):
        self.application = application
        self.attach_calls = 0

    @contextmanager
    def attach(self):
        self.attach_calls += 1
        yield self.application


class FakeValidator:
    pass


def _fake_application() -> FakeApplication:
    query_interface = (
        1,
        (),
        (),
        4,
        1,
        4,
        0,
        0,
        (24, 0, None),
        0,
    )
    text_get = (
        2,
        (),
        (),
        4,
        2,
        4,
        0,
        0,
        (8, 0, None),
        0,
    )
    text_put = (
        2,
        (),
        ((8, 1, None),),
        4,
        4,
        4,
        0,
        0,
        (24, 0, None),
        0,
    )
    insert_after = (
        3,
        (),
        ((8, 1, None),),
        4,
        1,
        4,
        0,
        0,
        (24, 0, None),
        0,
    )
    field_type = FakeTypeInfo(
        "WdFieldType",
        _type_attributes(
            guid="{FIELD-GUID}",
            kind=0,
            functions=0,
            variables=2,
        ),
        variables=[
            (10, 34, (22, 0, None), 0, 2),
            (11, 33, (22, 0, None), 0, 2),
        ],
        names={10: ("wdFieldFormula",), 11: ("wdFieldPage",)},
    )
    range_type = FakeTypeInfo(
        "Range",
        _type_attributes(
            guid="{RANGE-GUID}",
            kind=4,
            functions=4,
            variables=0,
        ),
        functions=[query_interface, text_get, text_put, insert_after],
        names={
            1: ("QueryInterface",),
            2: ("Text", "value"),
            3: ("InsertAfter", "Text"),
        },
    )
    return FakeApplication(FakeTypeLibrary([field_type, range_type]))


def test_scan_word_object_model_excludes_help_paths_and_base_dispatch_members() -> None:
    catalog = scan_word_object_model(_fake_application())
    serialized = json.dumps(catalog)

    assert catalog["library"]["major_version"] == 8
    assert catalog["stats"]["type_count"] == 2
    assert catalog["stats"]["member_count"] == 5
    assert catalog["stats"]["scan_errors"] == 0
    assert "QueryInterface" not in serialized
    assert "VBAWD10.CHM" not in serialized
    assert "secret documentation" not in serialized
    assert "document text" not in serialized

    range_type = next(item for item in catalog["types"] if item["name"] == "Range")
    insert_after = next(item for item in range_type["members"] if item["name"] == "InsertAfter")
    assert insert_after["kind"] == "method"
    assert insert_after["parameters"] == [
        {
            "name": "Text",
            "type": "BSTR",
            "flags": 1,
            "flag_names": ["in"],
            "optional": False,
        }
    ]
    text_put_member = next(
        item
        for item in range_type["members"]
        if item["name"] == "Text" and item["kind"] == "property_put"
    )
    assert text_put_member["parameter_count"] == 1
    assert text_put_member["optional_parameter_count"] == 0
    assert text_put_member["invoke_kind"] == 4
    assert text_put_member["variadic"] is False


def test_word_object_model_store_is_atomic_bounded_and_fail_safe(tmp_path: Path) -> None:
    path = tmp_path / "catalog.json"
    store = WordObjectModelStore(path)
    catalog = scan_word_object_model(_fake_application())

    assert store.load() is None
    store.write(catalog)
    assert store.load() == catalog
    assert not list(tmp_path.glob("*.tmp"))
    assert path.stat().st_size < WordObjectModelStore.MAX_FILE_BYTES

    path.write_text("{broken", encoding="utf-8")
    assert store.load() is None
    path.write_bytes(b"x" * (WordObjectModelStore.MAX_FILE_BYTES + 1))
    assert store.load() is None


def test_live_bridge_pages_and_reuses_installed_word_object_model_cache(
    tmp_path: Path,
) -> None:
    backend = FakeBackend(_fake_application())
    bridge = LiveWordBridge(
        Settings(auth_mode="local_stdio", storage_root=tmp_path / "storage"),
        FakeValidator(),  # type: ignore[arg-type]
        backend=backend,
    )

    first = bridge.inspect_object_model_types(
        query="field",
        kind="enum",
        limit=1,
    )
    second = bridge.inspect_object_model_members(
        "WdFieldType",
        kind="enum_value",
        limit=1,
    )

    assert first["matched_count"] == 1
    assert first["types"][0]["name"] == "WdFieldType"
    assert first["source_access"]["catalog_generated"] is True
    assert first["source_access"]["word_attached"] is True
    assert second["returned_count"] == 1
    assert second["has_more"] is True
    assert second["members"][0]["name"] == "wdFieldFormula"
    assert second["members"][0]["value"] == 34
    assert second["source_access"]["cache_hit"] is True
    assert second["source_access"]["word_attached"] is False
    assert second["privacy"]["document_content_stored"] is False
    assert second["privacy"]["paths_stored_or_returned"] is False
    assert backend.attach_calls == 1

    refreshed = bridge.inspect_object_model_types(refresh=True, limit=1)
    assert refreshed["source_access"]["catalog_generated"] is True
    assert backend.attach_calls == 2


def test_live_bridge_rejects_unknown_object_model_filters(tmp_path: Path) -> None:
    bridge = LiveWordBridge(
        Settings(auth_mode="local_stdio", storage_root=tmp_path / "storage"),
        FakeValidator(),  # type: ignore[arg-type]
        backend=FakeBackend(_fake_application()),
    )

    with pytest.raises(WordToolkitError) as error:
        bridge.inspect_object_model_types(kind="document_text")

    assert error.value.code is ErrorCode.INVALID_INPUT
