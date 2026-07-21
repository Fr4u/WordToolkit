from __future__ import annotations

import importlib
import pkgutil

import docx_mcp
import wordtoolkit

RUNTIME_MODULES = sorted(
    info.name
    for package in (wordtoolkit, docx_mcp)
    for info in pkgutil.walk_packages(package.__path__, package.__name__ + ".")
)


def test_runtime_module_inventory_is_not_suspiciously_small() -> None:
    assert len(RUNTIME_MODULES) >= 75


def test_every_packaged_runtime_module_imports() -> None:
    failures = {}
    for module_name in RUNTIME_MODULES:
        try:
            importlib.import_module(module_name)
        except Exception as exc:  # pragma: no cover - failure details are the assertion payload
            failures[module_name] = f"{type(exc).__name__}: {exc}"
    assert failures == {}
