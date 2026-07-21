from __future__ import annotations

import ipaddress
import os
import re
import socket
import stat
import zipfile
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from urllib.parse import urlparse

from lxml import etree

from .config import Settings
from .errors import ErrorCode, WordToolkitError

REL_NS = "{http://schemas.openxmlformats.org/package/2006/relationships}"
MACRO_CONTENT_TYPES = {
    "application/vnd.ms-word.document.macroEnabled.main+xml",
    "application/vnd.ms-word.template.macroEnabledTemplate.main+xml",
    "application/vnd.ms-office.vbaProject",
}
DENIED_RELATIONSHIP_SCHEMES = {"file", "javascript", "data", "vbscript", "ftp"}


@dataclass(slots=True)
class PackageInspection:
    entries: int = 0
    compressed_bytes: int = 0
    uncompressed_bytes: int = 0
    max_ratio_seen: float = 0.0
    external_relationships: list[dict[str, str]] = field(default_factory=list)
    blocked_external_relationships: list[dict[str, str]] = field(default_factory=list)
    opaque_preserved_parts: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "entries": self.entries,
            "compressed_bytes": self.compressed_bytes,
            "uncompressed_bytes": self.uncompressed_bytes,
            "max_ratio_seen": round(self.max_ratio_seen, 2),
            "external_relationships": self.external_relationships,
            "blocked_external_relationships": self.blocked_external_relationships,
            "opaque_preserved_parts": self.opaque_preserved_parts,
            "warnings": self.warnings,
        }


def _safe_member_name(name: str) -> PurePosixPath:
    if "\\" in name or "\x00" in name:
        raise WordToolkitError(ErrorCode.UNSAFE_ARCHIVE, "Unsafe ZIP entry name")
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        raise WordToolkitError(
            ErrorCode.UNSAFE_ARCHIVE, "ZIP entry escapes the package root", {"entry": name}
        )
    if re.match(r"^[A-Za-z]:", name):
        raise WordToolkitError(ErrorCode.UNSAFE_ARCHIVE, "Drive-qualified ZIP entry rejected")
    return path


def secure_xml_parser() -> etree.XMLParser:
    return etree.XMLParser(
        resolve_entities=False,
        no_network=True,
        load_dtd=False,
        dtd_validation=False,
        huge_tree=False,
        remove_blank_text=False,
        recover=False,
    )


def parse_xml_bytes(data: bytes, *, part: str) -> etree._Element:
    head = data[:4096].upper()
    if b"<!DOCTYPE" in head or b"<!ENTITY" in head:
        raise WordToolkitError(
            ErrorCode.UNSAFE_XML, "DTD and entity declarations are forbidden", {"part": part}
        )
    try:
        return etree.fromstring(data, parser=secure_xml_parser())
    except etree.XMLSyntaxError as exc:
        raise WordToolkitError(
            ErrorCode.OOXML_INVALID, "Malformed XML part", {"part": part, "reason": str(exc)}
        ) from exc


def resolve_internal_target(source_part: str, target: str) -> str:
    parsed = urlparse(target)
    if parsed.scheme or parsed.netloc:
        raise WordToolkitError(
            ErrorCode.UNSAFE_RELATIONSHIP,
            "Internal relationship contains an absolute URI",
            {"source": source_part, "target": target},
        )
    base = PurePosixPath(source_part).parent
    combined: list[str] = []
    for part in (base / target).parts:
        if part in {"", "."}:
            continue
        if part == "..":
            if not combined:
                raise WordToolkitError(
                    ErrorCode.UNSAFE_RELATIONSHIP,
                    "Relationship escapes package root",
                    {"source": source_part, "target": target},
                )
            combined.pop()
        else:
            combined.append(part)
    return "/".join(combined)


class SafePackageInspector:
    def __init__(self, settings: Settings):
        self.settings = settings

    def inspect(self, path: Path) -> PackageInspection:
        if path.suffix.lower() in {".docm", ".dotm"}:
            raise WordToolkitError(
                ErrorCode.UNSUPPORTED_FORMAT, "Macro-enabled Word files are rejected"
            )
        if path.stat().st_size > self.settings.max_upload_bytes:
            raise WordToolkitError(ErrorCode.LIMIT_EXCEEDED, "Compressed file exceeds upload limit")
        report = PackageInspection()
        try:
            archive = zipfile.ZipFile(path)
        except zipfile.BadZipFile as exc:
            raise WordToolkitError(
                ErrorCode.OOXML_INVALID, "File is not a valid OPC ZIP package"
            ) from exc
        with archive:
            infos = archive.infolist()
            report.entries = len(infos)
            if len(infos) > self.settings.max_zip_entries:
                raise WordToolkitError(ErrorCode.UNSAFE_ARCHIVE, "ZIP entry count limit exceeded")
            seen: set[str] = set()
            for info in infos:
                name = str(_safe_member_name(info.filename))
                folded = name.casefold()
                if folded in seen:
                    raise WordToolkitError(
                        ErrorCode.UNSAFE_ARCHIVE,
                        "Duplicate or case-colliding ZIP entry",
                        {"entry": name},
                    )
                seen.add(folded)
                mode = info.external_attr >> 16
                if stat.S_ISLNK(mode):
                    raise WordToolkitError(ErrorCode.UNSAFE_ARCHIVE, "Symlink ZIP entry rejected")
                report.compressed_bytes += info.compress_size
                report.uncompressed_bytes += info.file_size
                ratio = info.file_size / max(info.compress_size, 1)
                report.max_ratio_seen = max(report.max_ratio_seen, ratio)
                if ratio > self.settings.max_compression_ratio and info.file_size > 1024 * 1024:
                    raise WordToolkitError(
                        ErrorCode.UNSAFE_ARCHIVE,
                        "Suspicious ZIP compression ratio",
                        {"entry": name, "ratio": round(ratio, 2)},
                    )
            if report.uncompressed_bytes > self.settings.max_uncompressed_bytes:
                raise WordToolkitError(
                    ErrorCode.UNSAFE_ARCHIVE, "Uncompressed package limit exceeded"
                )
            required = {"[content_types].xml", "_rels/.rels", "word/document.xml"}
            if not required.issubset(seen):
                raise WordToolkitError(
                    ErrorCode.OOXML_INVALID,
                    "Required OPC/WordprocessingML parts are missing",
                    {"missing": sorted(required - seen)},
                )
            for info in infos:
                name = info.filename
                if PurePosixPath(name).suffix.lower() != ".xml" and not name.endswith(".rels"):
                    continue
                data = archive.read(info)
                root = parse_xml_bytes(data, part=name)
                if name == "[Content_Types].xml":
                    values = {value for value in root.xpath("//@ContentType")}
                    forbidden = sorted(values & MACRO_CONTENT_TYPES)
                    if forbidden:
                        raise WordToolkitError(
                            ErrorCode.UNSUPPORTED_FORMAT,
                            "Macro-enabled content types are rejected",
                            {"content_types": forbidden},
                        )
                if name.endswith(".rels"):
                    self._inspect_relationships(name, root, report, seen)
            parsed_parts = {
                "[Content_Types].xml",
                "_rels/.rels",
                "docProps/core.xml",
                "word/document.xml",
                "word/footnotes.xml",
                "word/endnotes.xml",
                "word/comments.xml",
                "word/commentsExtended.xml",
                "word/styles.xml",
                "word/numbering.xml",
                "word/settings.xml",
                "word/fontTable.xml",
            }
            report.opaque_preserved_parts = sorted(
                info.filename
                for info in infos
                if not info.is_dir()
                and info.filename not in parsed_parts
                and not info.filename.endswith(".rels")
                and not re.fullmatch(r"word/(?:header|footer)\d+\.xml", info.filename)
            )
            if report.blocked_external_relationships:
                raise WordToolkitError(
                    ErrorCode.UNSAFE_RELATIONSHIP,
                    "Package contains a forbidden external relationship",
                    {"relationships": report.blocked_external_relationships[:20]},
                )
        return report

    @staticmethod
    def _inspect_relationships(
        rels_name: str,
        root: etree._Element,
        report: PackageInspection,
        package_entries: set[str],
    ) -> None:
        if rels_name == "_rels/.rels":
            source_part = ""
        else:
            rel_path = PurePosixPath(rels_name)
            source_part = str(rel_path.parent.parent / rel_path.name.removesuffix(".rels"))
        for rel in root.findall(f"{REL_NS}Relationship"):
            target = rel.get("Target", "")
            mode = rel.get("TargetMode", "Internal")
            item = {"part": rels_name, "id": rel.get("Id", ""), "target": target}
            if mode == "External":
                report.external_relationships.append(item)
                scheme = urlparse(target).scheme.lower()
                if scheme in DENIED_RELATIONSHIP_SCHEMES or scheme not in {
                    "http",
                    "https",
                    "mailto",
                }:
                    report.blocked_external_relationships.append(item)
                continue
            resolved = resolve_internal_target(source_part, target)
            if resolved.casefold() not in package_entries:
                report.warnings.append(f"Missing relationship target: {rels_name} -> {target}")

    def extract(self, package: Path, destination: Path) -> PackageInspection:
        report = self.inspect(package)
        destination.mkdir(parents=True, exist_ok=False)
        root = destination.resolve()
        with zipfile.ZipFile(package) as archive:
            for info in archive.infolist():
                rel = _safe_member_name(info.filename)
                target = (destination / Path(*rel.parts)).resolve()
                if not target.is_relative_to(root):
                    raise WordToolkitError(ErrorCode.UNSAFE_ARCHIVE, "ZIP extraction escaped root")
                if info.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                    continue
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(info) as source, target.open("wb") as output:
                    remaining = self.settings.max_uncompressed_bytes
                    while chunk := source.read(min(1024 * 1024, remaining + 1)):
                        remaining -= len(chunk)
                        if remaining < 0:
                            raise WordToolkitError(
                                ErrorCode.UNSAFE_ARCHIVE, "Extraction limit exceeded"
                            )
                        output.write(chunk)
        return report


def validate_remote_url(url: str, allowed_suffixes: tuple[str, ...]) -> None:
    parsed = urlparse(url)
    if parsed.scheme != "https" and parsed.hostname not in {"127.0.0.1", "localhost"}:
        raise WordToolkitError(ErrorCode.INVALID_INPUT, "Only HTTPS file URLs are accepted")
    host = (parsed.hostname or "").lower()
    if not any(host == suffix.lstrip(".") or host.endswith(suffix) for suffix in allowed_suffixes):
        raise WordToolkitError(
            ErrorCode.AUTH_FORBIDDEN, "File URL host is not allowlisted", {"host": host}
        )
    try:
        addresses = {item[4][0] for item in socket.getaddrinfo(host, parsed.port or 443)}
    except socket.gaierror as exc:
        raise WordToolkitError(ErrorCode.INVALID_INPUT, "File URL host cannot be resolved") from exc
    for address in addresses:
        ip = ipaddress.ip_address(address)
        if host not in {"127.0.0.1", "localhost"} and (
            ip.is_private or ip.is_loopback or ip.is_link_local or ip.is_reserved or ip.is_multicast
        ):
            raise WordToolkitError(ErrorCode.AUTH_FORBIDDEN, "Private network file URL rejected")


def safe_join(root: Path, *segments: str) -> Path:
    target = root.joinpath(*segments).resolve()
    if not target.is_relative_to(root.resolve()):
        raise WordToolkitError(ErrorCode.UNSAFE_PATH, "Resolved path escaped storage root")
    return target


def atomic_permissions(path: Path) -> None:
    os.chmod(path, 0o600)
