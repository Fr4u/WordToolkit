from __future__ import annotations

import zipfile
from pathlib import Path

import pytest

from docx_mcp.document import DocxDocument
from wordtoolkit.config import Settings
from wordtoolkit.engine import OoxmlValidator, WordDocumentEngine
from wordtoolkit.errors import ErrorCode, WordToolkitError
from wordtoolkit.security import SafePackageInspector
from wordtoolkit.sessions import SessionStore


def config(tmp_path: Path, **values) -> Settings:
    return Settings(storage_root=tmp_path / "storage", **values)


def rewrite_part(source: Path, target: Path, name: str, value: bytes) -> None:
    with (
        zipfile.ZipFile(source) as original,
        zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as output,
    ):
        for item in original.infolist():
            output.writestr(item, value if item.filename == name else original.read(item.filename))


def test_rejects_path_traversal(tmp_path) -> None:
    path = tmp_path / "bad.docx"
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("../escape", b"x")
    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).inspect(path)
    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE


def test_rejects_xxe(tmp_path) -> None:
    source = tmp_path / "source.docx"
    bad = tmp_path / "xxe.docx"
    DocxDocument.create(str(source)).close()
    payload = b'<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>&e;</w:t></w:r></w:p></w:body></w:document>'
    rewrite_part(source, bad, "word/document.xml", payload)
    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).inspect(bad)
    assert error.value.code == ErrorCode.UNSAFE_XML


def test_rejects_macro_content_type(tmp_path) -> None:
    source = tmp_path / "source.docx"
    bad = tmp_path / "macro.docx"
    DocxDocument.create(str(source)).close()
    with zipfile.ZipFile(source) as archive:
        content_types = archive.read("[Content_Types].xml").replace(
            b"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            b"application/vnd.ms-word.document.macroEnabled.main+xml",
        )
    rewrite_part(source, bad, "[Content_Types].xml", content_types)
    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).inspect(bad)
    assert error.value.code == ErrorCode.UNSUPPORTED_FORMAT


def test_rejects_forbidden_external_relationship(tmp_path) -> None:
    source = tmp_path / "source.docx"
    bad = tmp_path / "external.docx"
    DocxDocument.create(str(source)).close()
    with zipfile.ZipFile(source) as archive:
        rels = archive.read("word/_rels/document.xml.rels").replace(
            b"</Relationships>",
            b'<Relationship Id="rEvil" Type="http://example.invalid/evil" Target="file:///etc/passwd" TargetMode="External"/></Relationships>',
        )
    rewrite_part(source, bad, "word/_rels/document.xml.rels", rels)
    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).inspect(bad)
    assert error.value.code == ErrorCode.UNSAFE_RELATIONSHIP


def test_rejects_forbidden_package_root_relationship(tmp_path) -> None:
    source = tmp_path / "source.docx"
    bad = tmp_path / "root-external.docx"
    DocxDocument.create(str(source)).close()
    with zipfile.ZipFile(source) as archive:
        rels = archive.read("_rels/.rels").replace(
            b"</Relationships>",
            b'<Relationship Id="rRootEvil" Type="http://example.invalid/evil" Target="file:///etc/passwd" TargetMode="External"/></Relationships>',
        )
    rewrite_part(source, bad, "_rels/.rels", rels)
    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).inspect(bad)
    assert error.value.code == ErrorCode.UNSAFE_RELATIONSHIP


def test_rejects_zip_bomb_ratio(tmp_path) -> None:
    source = tmp_path / "source.docx"
    bomb = tmp_path / "bomb.docx"
    DocxDocument.create(str(source)).close()
    with (
        zipfile.ZipFile(source) as original,
        zipfile.ZipFile(bomb, "w", zipfile.ZIP_DEFLATED) as output,
    ):
        for item in original.infolist():
            output.writestr(item, original.read(item.filename))
        output.writestr("word/media/bomb.bin", b"0" * (2 * 1024 * 1024))
    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path, max_compression_ratio=10)).inspect(bomb)
    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE


def test_unknown_part_round_trip_preserved(tmp_path) -> None:
    initial = tmp_path / "initial.docx"
    with_unknown = tmp_path / "unknown.docx"
    output = tmp_path / "output.docx"
    DocxDocument.create(str(initial)).close()
    marker = b"opaque-vendor-extension-contents"
    with (
        zipfile.ZipFile(initial) as source,
        zipfile.ZipFile(with_unknown, "w", zipfile.ZIP_DEFLATED) as target,
    ):
        for item in source.infolist():
            target.writestr(item, source.read(item.filename))
        target.writestr("customXml/vendor-extension.bin", marker)
    engine = WordDocumentEngine(with_unknown, config(tmp_path))
    engine.open()
    anchor = engine.call("get_document_outline")
    del anchor
    result = engine.save_version(output)
    engine.close()
    with zipfile.ZipFile(output) as archive:
        assert archive.read("customXml/vendor-extension.bin") == marker
    assert result["round_trip_preservation"]["preserved"]


def test_validator_reports_unreachable_opc_part(tmp_path) -> None:
    initial = tmp_path / "initial.docx"
    orphaned = tmp_path / "orphaned.docx"
    DocxDocument.create(str(initial)).close()
    with (
        zipfile.ZipFile(initial) as source,
        zipfile.ZipFile(orphaned, "w", zipfile.ZIP_DEFLATED) as target,
    ):
        for item in source.infolist():
            target.writestr(item, source.read(item.filename))
        target.writestr("customXml/unreachable.bin", b"opaque")
    result = OoxmlValidator(config(tmp_path)).validate(orphaned)
    assert result["valid"]
    assert any(
        issue["code"] == "ORPHANED_PART" and issue["part"] == "customXml/unreachable.bin"
        for issue in result["issues"]
    )


@pytest.mark.asyncio
async def test_exported_artifact_survives_session_close_until_artifact_expiry(tmp_path) -> None:
    settings = config(tmp_path)
    store = SessionStore(settings)
    session = await store.create_session("owner")
    source = session.root / "versions" / "result.docx"
    source.parent.mkdir(parents=True)
    source.write_bytes(b"validated-result")
    artifact = await store.register_artifact(
        "owner",
        source,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "result.docx",
    )
    await store.close_session("owner", session.session_id)
    assert artifact.path.exists()
    assert artifact.path.read_bytes() == b"validated-result"
    artifact.expires_at = 0
    cleanup = await store.cleanup_expired()
    assert cleanup["artifacts"] == 1
    assert not artifact.path.exists()
