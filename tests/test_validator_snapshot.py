from __future__ import annotations

import os
import zipfile
from pathlib import Path

import pytest

from wordtoolkit.config import Settings
from wordtoolkit.engine.validator import OoxmlValidator
from wordtoolkit.errors import ErrorCode, WordToolkitError


def _package(path: Path, extra: str | None = None) -> None:
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(
            "[Content_Types].xml",
            "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'/>",
        )
        archive.writestr(
            "_rels/.rels",
            "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'/>",
        )
        archive.writestr(
            "word/document.xml",
            "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'/>",
        )
        if extra:
            archive.writestr(extra, b"replacement")


def _validator(tmp_path: Path, **kwargs) -> OoxmlValidator:
    return OoxmlValidator(Settings(storage_root=tmp_path, **kwargs))


def test_validate_uses_captured_snapshot_after_source_replacement(tmp_path: Path) -> None:
    source = tmp_path / "source.docx"
    replacement = tmp_path / "replacement.docx"
    _package(source)
    _package(replacement, "word/replacement.bin")
    validator = _validator(tmp_path)
    original_inspect = validator.package_inspector._inspect_snapshot

    def replace_after_capture(path: Path, *, source_suffix: str):
        report = original_inspect(path, source_suffix=source_suffix)
        source.write_bytes(replacement.read_bytes())
        return report

    validator.package_inspector._inspect_snapshot = replace_after_capture
    result = validator.validate(source)
    assert result["package"]["entries"] == 3


def test_capture_rejects_mutation_between_reads(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    source = tmp_path / "source.docx"
    _package(source)
    validator = _validator(tmp_path)
    import wordtoolkit.security as security

    original_open = security._open_shared_source_once
    calls = 0

    def mutating_open(path: Path):
        nonlocal calls
        handle = original_open(path)
        if path == source:
            original_read = handle.read

            def read(*read_args, **read_kwargs):
                nonlocal calls
                calls += 1
                data = original_read(*read_args, **read_kwargs)
                if calls == 2:
                    data = data + b"changed"
                return data

            handle.read = read
        return handle

    monkeypatch.setattr(security, "_open_shared_source_once", mutating_open)
    with pytest.raises(WordToolkitError) as exc:
        validator.validate(source)
    assert exc.value.code == ErrorCode.OOXML_INVALID
    assert exc.value.retryable is True


def test_capture_enforces_compressed_upload_limit(tmp_path: Path) -> None:
    source = tmp_path / "source.docx"
    _package(source)
    with source.open("ab") as handle:
        handle.write(os.urandom(2048))
    validator = _validator(tmp_path, max_upload_bytes=1024)
    with pytest.raises(WordToolkitError) as exc:
        validator.validate(source)
    assert exc.value.code == ErrorCode.LIMIT_EXCEEDED


def test_validate_preserves_macro_enabled_extension_rejection(tmp_path: Path) -> None:
    source = tmp_path / "source.docm"
    _package(source)

    with pytest.raises(WordToolkitError) as exc:
        _validator(tmp_path).validate(source)

    assert exc.value.code == ErrorCode.UNSUPPORTED_FORMAT
