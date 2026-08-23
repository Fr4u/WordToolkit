from __future__ import annotations

import os
import socket
import stat
import subprocess
import zipfile
from pathlib import Path

import pytest

from docx_mcp.document import DocxDocument
from wordtoolkit.config import Settings
from wordtoolkit.engine import OoxmlValidator, WordDocumentEngine
from wordtoolkit.errors import ErrorCode, WordToolkitError
from wordtoolkit.security import (
    PackageInspection,
    SafePackageInspector,
    _safe_member_name,
    atomic_permissions,
    parse_xml_bytes,
    resolve_internal_target,
    safe_join,
    validate_remote_url,
)
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


def minimal_package(path: Path, *extra_entries: tuple[str | zipfile.ZipInfo, bytes]) -> None:
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(
            "[Content_Types].xml",
            b'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types" />',
        )
        archive.writestr(
            "_rels/.rels",
            b'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships" />',
        )
        archive.writestr(
            "word/document.xml",
            b'<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body /></w:document>',
        )
        for name, content in extra_entries:
            archive.writestr(name, content)


@pytest.mark.parametrize(
    "name",
    [
        "",
        ".",
        "./word/document.xml",
        "word/./document.xml",
        "word//document.xml",
        "../escape",
        "word/../../escape",
        "/absolute",
        "C:/drive-qualified",
        "word\\document.xml",
        "word/evil\x00.xml",
    ],
)
def test_rejects_noncanonical_or_unsafe_zip_member_names(name: str) -> None:
    with pytest.raises(WordToolkitError) as error:
        _safe_member_name(name)
    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE


def test_accepts_canonical_file_and_directory_zip_member_names() -> None:
    assert str(_safe_member_name("word/document.xml")) == "word/document.xml"
    assert str(_safe_member_name("word/")) == "word"


@pytest.mark.parametrize(
    "payload",
    [
        (
            '<?xml version="1.0" encoding="utf-16"?>'
            '<!DOCTYPE root [<!ENTITY x "blocked">]><root>&x;</root>'
        ).encode("utf-16"),
        b" " * 5000 + b'<!DOCTYPE root [<!ENTITY x "blocked">]><root>&x;</root>',
    ],
)
def test_rejects_dtd_outside_the_fast_ascii_prefix(payload: bytes) -> None:
    with pytest.raises(WordToolkitError) as error:
        parse_xml_bytes(payload, part="word/document.xml")
    assert error.value.code == ErrorCode.UNSAFE_XML


def test_doctype_text_inside_xml_comment_is_not_treated_as_markup() -> None:
    root = parse_xml_bytes(
        b"<root><!-- literal <!DOCTYPE marker, not markup> --><child /></root>",
        part="word/document.xml",
    )
    assert root.tag == "root"


def test_malformed_xml_has_controlled_error_contract() -> None:
    with pytest.raises(WordToolkitError) as error:
        parse_xml_bytes(b"<root>", part="word/document.xml")
    assert error.value.code == ErrorCode.OOXML_INVALID


def test_internal_relationship_target_is_normalized_against_source_part() -> None:
    assert (
        resolve_internal_target("word/document.xml", "media/image1.png") == "word/media/image1.png"
    )
    assert (
        resolve_internal_target("word/document.xml", "../docProps/core.xml") == "docProps/core.xml"
    )


@pytest.mark.parametrize(
    "target",
    [
        "../../../escape.xml",
        "/word/document.xml",
        "\\word\\document.xml",
        "https://example.com/document.xml",
        "//example.com/document.xml",
    ],
)
def test_rejects_relationship_targets_outside_package_namespace(target: str) -> None:
    with pytest.raises(WordToolkitError) as error:
        resolve_internal_target("word/document.xml", target)
    assert error.value.code == ErrorCode.UNSAFE_RELATIONSHIP


def _dns_result(address: str, port: int = 443) -> list[tuple]:
    family = socket.AF_INET6 if ":" in address else socket.AF_INET
    return [(family, socket.SOCK_STREAM, socket.IPPROTO_TCP, "", (address, port))]


def test_remote_url_accepts_exact_or_subdomain_allowlist_boundary(monkeypatch) -> None:
    monkeypatch.setattr(
        socket, "getaddrinfo", lambda _host, port: _dns_result("93.184.216.34", port)
    )
    validate_remote_url("https://example.com/file.docx", ("example.com",))
    validate_remote_url("https://files.example.com/file.docx", ("example.com",))


def test_remote_url_rejects_suffix_without_dns_label_boundary(monkeypatch) -> None:
    monkeypatch.setattr(
        socket, "getaddrinfo", lambda _host, port: _dns_result("93.184.216.34", port)
    )
    with pytest.raises(WordToolkitError) as error:
        validate_remote_url("https://badexample.com/file.docx", ("example.com",))
    assert error.value.code == ErrorCode.AUTH_FORBIDDEN


@pytest.mark.parametrize(
    "url",
    [
        "file://localhost/tmp/file.docx",
        "ftp://localhost/file.docx",
        "http://files.example.com/file.docx",
        "javascript://localhost/file.docx",
    ],
)
def test_remote_url_rejects_non_https_schemes(url: str) -> None:
    with pytest.raises(WordToolkitError) as error:
        validate_remote_url(url, ("localhost", "example.com"))
    assert error.value.code == ErrorCode.INVALID_INPUT


def test_remote_url_allows_local_http_only_when_explicitly_allowlisted(monkeypatch) -> None:
    monkeypatch.setattr(socket, "getaddrinfo", lambda _host, port: _dns_result("127.0.0.1", port))
    assert validate_remote_url(
        "http://localhost:8787/file.docx", ("localhost",), allow_localhost=True
    ) == ("127.0.0.1",)


@pytest.mark.parametrize("url", ["http://localhost/file.docx", "https://localhost/file.docx"])
def test_remote_url_rejects_localhost_without_explicit_runtime_opt_in(url: str) -> None:
    with pytest.raises(WordToolkitError) as error:
        validate_remote_url(url, ("localhost",))
    assert error.value.code == ErrorCode.AUTH_FORBIDDEN


@pytest.mark.parametrize("address", ["10.0.0.8", "100.64.0.8", "224.0.0.8"])
def test_remote_url_rejects_non_global_dns_answers(monkeypatch, address: str) -> None:
    monkeypatch.setattr(socket, "getaddrinfo", lambda _host, port: _dns_result(address, port))
    with pytest.raises(WordToolkitError) as error:
        validate_remote_url("https://files.example.com/file.docx", ("example.com",))
    assert error.value.code == ErrorCode.AUTH_FORBIDDEN


def test_remote_url_rejects_mixed_public_and_private_dns_answers(monkeypatch) -> None:
    monkeypatch.setattr(
        socket,
        "getaddrinfo",
        lambda _host, port: _dns_result("93.184.216.34", port) + _dns_result("127.0.0.1", port),
    )
    with pytest.raises(WordToolkitError) as error:
        validate_remote_url("https://files.example.com/file.docx", ("example.com",))
    assert error.value.code == ErrorCode.AUTH_FORBIDDEN


def test_remote_url_prefers_validated_ipv4_before_ipv6_for_first_hop(monkeypatch) -> None:
    monkeypatch.setattr(
        socket,
        "getaddrinfo",
        lambda _host, port: (
            _dns_result("2606:2800:220:1:248:1893:25c8:1946", port)
            + _dns_result("93.184.216.34", port)
        ),
    )

    assert validate_remote_url("https://files.example.com/file.docx", ("example.com",)) == (
        "93.184.216.34",
        "2606:2800:220:1:248:1893:25c8:1946",
    )


def test_remote_url_rejects_credentials_invalid_port_and_dns_failure(monkeypatch) -> None:
    with pytest.raises(WordToolkitError) as credentials:
        validate_remote_url("https://user:secret@example.com/file.docx", ("example.com",))
    assert credentials.value.code == ErrorCode.INVALID_INPUT

    with pytest.raises(WordToolkitError) as port:
        validate_remote_url("https://example.com:not-a-port/file.docx", ("example.com",))
    assert port.value.code == ErrorCode.INVALID_INPUT

    def fail_dns(_host, _port):
        raise socket.gaierror("not found")

    monkeypatch.setattr(socket, "getaddrinfo", fail_dns)
    with pytest.raises(WordToolkitError) as dns:
        validate_remote_url("https://example.com/file.docx", ("example.com",))
    assert dns.value.code == ErrorCode.INVALID_INPUT


def test_safe_join_and_atomic_permissions_keep_files_beneath_root(tmp_path, monkeypatch) -> None:
    root = tmp_path / "root"
    root.mkdir()
    assert safe_join(root, "nested", "file.docx") == root / "nested" / "file.docx"
    with pytest.raises(WordToolkitError) as error:
        safe_join(root, "..", "escape.docx")
    assert error.value.code == ErrorCode.UNSAFE_PATH

    calls: list[tuple[Path, int]] = []
    monkeypatch.setattr(
        "wordtoolkit.security.os.chmod", lambda path, mode: calls.append((path, mode))
    )
    target = root / "file.docx"
    atomic_permissions(target)
    assert calls == [(target, 0o600)]


def test_package_inspection_dictionary_is_bounded_and_stable() -> None:
    report = PackageInspection(
        entries=3,
        compressed_bytes=100,
        uncompressed_bytes=250,
        max_ratio_seen=2.345,
        external_relationships=[{"target": "https://example.com"}],
        blocked_external_relationships=[],
        opaque_preserved_parts=["custom/vendor.bin"],
        warnings=["warning"],
    )
    assert report.to_dict() == {
        "entries": 3,
        "compressed_bytes": 100,
        "uncompressed_bytes": 250,
        "max_ratio_seen": 2.35,
        "external_relationships": [{"target": "https://example.com"}],
        "blocked_external_relationships": [],
        "opaque_preserved_parts": ["custom/vendor.bin"],
        "warnings": ["warning"],
    }


def test_inspector_rejects_macro_extension_oversize_and_invalid_zip(tmp_path) -> None:
    with pytest.raises(WordToolkitError) as macro:
        SafePackageInspector(config(tmp_path)).inspect(tmp_path / "macro.docm")
    assert macro.value.code == ErrorCode.UNSUPPORTED_FORMAT

    oversized = tmp_path / "oversized.docx"
    oversized.write_bytes(b"x" * 1025)
    with pytest.raises(WordToolkitError) as size:
        SafePackageInspector(config(tmp_path, max_upload_bytes=1024)).inspect(oversized)
    assert size.value.code == ErrorCode.LIMIT_EXCEEDED

    invalid = tmp_path / "invalid.docx"
    invalid.write_bytes(b"not-a-zip")
    with pytest.raises(WordToolkitError) as package:
        SafePackageInspector(config(tmp_path)).inspect(invalid)
    assert package.value.code == ErrorCode.OOXML_INVALID


def test_inspector_rejects_entry_count_case_collision_symlink_and_missing_parts(tmp_path) -> None:
    too_many = tmp_path / "too-many.docx"
    minimal_package(
        too_many,
        *((f"custom/item-{index}.bin", b"x") for index in range(8)),
    )
    with pytest.raises(WordToolkitError) as count:
        SafePackageInspector(config(tmp_path, max_zip_entries=10)).inspect(too_many)
    assert count.value.code == ErrorCode.UNSAFE_ARCHIVE

    collision = tmp_path / "collision.docx"
    minimal_package(collision, ("word/Document.xml", b"duplicate"))
    with pytest.raises(WordToolkitError) as duplicate:
        SafePackageInspector(config(tmp_path)).inspect(collision)
    assert duplicate.value.code == ErrorCode.UNSAFE_ARCHIVE

    symlink = zipfile.ZipInfo("word/media/link.bin")
    symlink.create_system = 3
    symlink.external_attr = (stat.S_IFLNK | 0o777) << 16
    linked = tmp_path / "symlink.docx"
    minimal_package(linked, (symlink, b"target"))
    with pytest.raises(WordToolkitError) as link:
        SafePackageInspector(config(tmp_path)).inspect(linked)
    assert link.value.code == ErrorCode.UNSAFE_ARCHIVE

    missing = tmp_path / "missing.docx"
    with zipfile.ZipFile(missing, "w") as archive:
        archive.writestr("word/document.xml", b"<document />")
    with pytest.raises(WordToolkitError) as required:
        SafePackageInspector(config(tmp_path)).inspect(missing)
    assert required.value.code == ErrorCode.OOXML_INVALID


def test_inspector_rejects_total_uncompressed_limit(tmp_path) -> None:
    package = tmp_path / "expanded.docx"
    minimal_package(package, ("custom/opaque.bin", b"x" * 2048))
    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path, max_uncompressed_bytes=1024)).inspect(package)
    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE


def test_inspector_reports_allowed_external_missing_internal_and_opaque_parts(tmp_path) -> None:
    source = tmp_path / "source.docx"
    package = tmp_path / "relationships.docx"
    DocxDocument.create(str(source)).close()
    with (
        zipfile.ZipFile(source) as original,
        zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as output,
    ):
        for item in original.infolist():
            content = original.read(item.filename)
            if item.filename == "word/_rels/document.xml.rels":
                content = content.replace(
                    b"</Relationships>",
                    b'<Relationship Id="rHttp" Type="https://example.com/type" Target="https://example.com/resource" TargetMode="External"/>'
                    b'<Relationship Id="rMissing" Type="https://example.com/type" Target="missing.bin"/>'
                    b"</Relationships>",
                )
            output.writestr(item, content)
        output.writestr("custom/vendor.bin", b"opaque")

    report = SafePackageInspector(config(tmp_path)).inspect(package)
    assert report.external_relationships == [
        {
            "part": "word/_rels/document.xml.rels",
            "id": "rHttp",
            "target": "https://example.com/resource",
        }
    ]
    assert report.blocked_external_relationships == []
    assert report.warnings == [
        "Missing relationship target: word/_rels/document.xml.rels -> missing.bin"
    ]
    assert "custom/vendor.bin" in report.opaque_preserved_parts


def test_inspector_extracts_canonical_directory_entries(tmp_path) -> None:
    package = tmp_path / "extract.docx"
    directory = zipfile.ZipInfo("custom/")
    directory.external_attr = (stat.S_IFDIR | 0o755) << 16
    minimal_package(package, (directory, b""), ("custom/blob.bin", b"opaque"))
    destination = tmp_path / "extracted"
    report = SafePackageInspector(config(tmp_path)).extract(package, destination)
    assert report.entries == 5
    assert (destination / "word" / "document.xml").is_file()
    assert (destination / "custom").is_dir()
    assert (destination / "custom" / "blob.bin").read_bytes() == b"opaque"


def test_extraction_enforces_cumulative_limit_after_inspection(tmp_path, monkeypatch) -> None:
    package = tmp_path / "changed-after-inspection.docx"
    minimal_package(package, ("custom/a.bin", b"a" * 600), ("custom/b.bin", b"b" * 600))
    inspector = SafePackageInspector(config(tmp_path, max_uncompressed_bytes=1024))
    monkeypatch.setattr(
        inspector,
        "_inspect_snapshot",
        lambda _package, *, source_suffix: PackageInspection(entries=5),
    )

    destination = tmp_path / "extracted"
    with pytest.raises(WordToolkitError) as error:
        inspector.extract(package, destination)

    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE
    assert error.value.message == "Extraction limit exceeded"
    assert not destination.exists()
    assert not list(tmp_path.glob(".extracted.wordtoolkit-*"))


def test_extraction_uses_one_snapshot_after_source_replacement(tmp_path, monkeypatch) -> None:
    package = tmp_path / "source.docx"
    replacement = tmp_path / "replacement.docx"
    minimal_package(package, ("custom/original.bin", b"original"))
    minimal_package(replacement, ("custom/replacement.bin", b"replacement"))
    inspector = SafePackageInspector(config(tmp_path))
    original_inspect = inspector._inspect_snapshot

    def replace_after_inspection(snapshot, *, source_suffix):
        report = original_inspect(snapshot, source_suffix=source_suffix)
        package.write_bytes(replacement.read_bytes())
        return report

    monkeypatch.setattr(inspector, "_inspect_snapshot", replace_after_inspection)
    destination = tmp_path / "extracted"
    inspector.extract(package, destination)

    assert (destination / "custom" / "original.bin").read_bytes() == b"original"
    assert not (destination / "custom" / "replacement.bin").exists()


def test_extraction_rejects_preexisting_symlink_parent(tmp_path) -> None:
    package = tmp_path / "package.docx"
    minimal_package(package, ("custom/payload.bin", b"secret"))
    outside = tmp_path / "outside"
    outside.mkdir()
    parent = tmp_path / "staging"
    try:
        parent.symlink_to(outside, target_is_directory=True)
    except OSError as exc:
        if os.name != "nt" or getattr(exc, "winerror", None) != 1314:
            raise
        command = os.environ.get("COMSPEC", r"C:\Windows\System32\cmd.exe")
        created = subprocess.run(
            [command, "/d", "/c", "mklink", "/J", str(parent), str(outside)],
            capture_output=True,
            text=True,
            check=False,
        )
        if created.returncode != 0:
            pytest.skip("Windows runtime cannot create a symlink or junction for this test")

    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).extract(package, parent / "output")

    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE
    assert not (outside / "output" / "custom" / "payload.bin").exists()


def test_extraction_never_overwrites_existing_destination(tmp_path) -> None:
    package = tmp_path / "package.docx"
    minimal_package(package, ("custom/payload.bin", b"secret"))
    destination = tmp_path / "existing"
    destination.mkdir()
    sentinel = destination / "keep.bin"
    sentinel.write_bytes(b"keep")

    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).extract(package, destination)

    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE
    assert sentinel.read_bytes() == b"keep"
    assert not (destination / "custom" / "payload.bin").exists()


def test_extraction_fails_closed_on_file_directory_collision(tmp_path) -> None:
    package = tmp_path / "collision.docx"
    minimal_package(
        package,
        ("custom", b"file"),
        ("custom/payload.bin", b"secret"),
    )
    destination = tmp_path / "extracted"

    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).extract(package, destination)

    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE
    assert not destination.exists()
    assert not list(tmp_path.glob(".extracted.wordtoolkit-*"))


def test_extraction_normalizes_invalid_destination_parent(tmp_path) -> None:
    package = tmp_path / "package.docx"
    minimal_package(package, ("custom/payload.bin", b"secret"))
    parent = tmp_path / "parent"
    parent.write_bytes(b"not a directory")

    with pytest.raises(WordToolkitError) as error:
        SafePackageInspector(config(tmp_path)).extract(package, parent / "output")

    assert error.value.code == ErrorCode.UNSAFE_ARCHIVE
    assert parent.read_bytes() == b"not a directory"


@pytest.mark.parametrize("host", ["localhost", "127.0.0.1", "::1"])
def test_production_configuration_rejects_local_upload_hosts(host: str) -> None:
    settings = Settings(
        environment="production",
        public_base_url="https://docs.example",
        auth_mode="oauth_jwt",
        oauth_issuer="https://issuer.example",
        oauth_audience="wordtoolkit",
        oauth_jwks_url="https://issuer.example/jwks",
        signing_secret="x" * 32,
        allowed_upload_host_suffixes=host,
    )

    with pytest.raises(RuntimeError, match="production upload allowlist"):
        settings.assert_production_safe()


def test_development_configuration_may_explicitly_allow_local_upload_host() -> None:
    Settings(
        environment="development", allowed_upload_host_suffixes="localhost"
    ).assert_production_safe()


def test_remote_url_rejects_empty_or_malformed_dns_answers(monkeypatch) -> None:
    monkeypatch.setattr(socket, "getaddrinfo", lambda _host, _port: [])
    with pytest.raises(WordToolkitError) as empty:
        validate_remote_url("https://example.com/file.docx", ("example.com",))
    assert empty.value.code == ErrorCode.INVALID_INPUT

    monkeypatch.setattr(
        socket,
        "getaddrinfo",
        lambda _host, port: [(socket.AF_INET, socket.SOCK_STREAM, 0, "", ("not-an-ip", port))],
    )
    with pytest.raises(WordToolkitError) as malformed:
        validate_remote_url("https://example.com/file.docx", ("example.com",))
    assert malformed.value.code == ErrorCode.INVALID_INPUT


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
