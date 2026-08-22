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
    if not name or "\\" in name or "\x00" in name:
        raise WordToolkitError(ErrorCode.UNSAFE_ARCHIVE, "Unsafe ZIP entry name")
    raw_parts = name[:-1].split("/") if name.endswith("/") else name.split("/")
    if not raw_parts or any(part in {"", ".", ".."} for part in raw_parts):
        raise WordToolkitError(
            ErrorCode.UNSAFE_ARCHIVE, "ZIP entry escapes the package root", {"entry": name}
        )
    path = PurePosixPath(name)
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
    try:
        root = etree.fromstring(data, parser=secure_xml_parser())
    except etree.XMLSyntaxError as exc:
        raise WordToolkitError(
            ErrorCode.OOXML_INVALID, "Malformed XML part", {"part": part, "reason": str(exc)}
        ) from exc
    if root.getroottree().docinfo.doctype or any(
        isinstance(node, etree._Entity) for node in root.iter()
    ):
        raise WordToolkitError(
            ErrorCode.UNSAFE_XML, "DTD and entity declarations are forbidden", {"part": part}
        )
    return root


def resolve_internal_target(source_part: str, target: str) -> str:
    parsed = urlparse(target)
    if parsed.scheme or parsed.netloc or target.startswith("/") or "\\" in target:
        raise WordToolkitError(
            ErrorCode.UNSAFE_RELATIONSHIP,
            "Internal relationship contains an absolute URI",
            {"source": source_part, "target": target},
        )
    base = PurePosixPath(source_part).parent
    combined: list[str] = []
    for part in (base / target).parts:
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
            remaining = self.settings.max_uncompressed_bytes
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
                    while chunk := source.read(min(1024 * 1024, remaining + 1)):
                        remaining -= len(chunk)
                        if remaining < 0:
                            raise WordToolkitError(
                                ErrorCode.UNSAFE_ARCHIVE, "Extraction limit exceeded"
                            )
                        output.write(chunk)
        return report


def validate_remote_url(
    url: str,
    allowed_suffixes: tuple[str, ...],
    *,
    allow_localhost: bool = False,
) -> tuple[str, ...]:
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower().rstrip(".")
    is_local_test_host = host in {"127.0.0.1", "::1", "localhost"}
    if parsed.scheme not in {"http", "https"}:
        raise WordToolkitError(ErrorCode.INVALID_INPUT, "Only HTTPS file URLs are accepted")
    if not host or parsed.username is not None or parsed.password is not None:
        raise WordToolkitError(ErrorCode.INVALID_INPUT, "File URL authority is invalid")
    local_access_allowed = allow_localhost and is_local_test_host
    if is_local_test_host and not local_access_allowed:
        raise WordToolkitError(ErrorCode.AUTH_FORBIDDEN, "Localhost file URL rejected")
    if parsed.scheme != "https" and not local_access_allowed:
        raise WordToolkitError(ErrorCode.INVALID_INPUT, "Only HTTPS file URLs are accepted")
    normalized_suffixes = {
        suffix.strip().lower().strip(".") for suffix in allowed_suffixes if suffix.strip(".")
    }
    if not any(host == suffix or host.endswith(f".{suffix}") for suffix in normalized_suffixes):
        raise WordToolkitError(
            ErrorCode.AUTH_FORBIDDEN, "File URL host is not allowlisted", {"host": host}
        )
    try:
        port = parsed.port or (80 if parsed.scheme == "http" else 443)
    except ValueError as exc:
        raise WordToolkitError(ErrorCode.INVALID_INPUT, "File URL port is invalid") from exc
    try:
        addresses = {item[4][0] for item in socket.getaddrinfo(host, port)}
    except socket.gaierror as exc:
        raise WordToolkitError(ErrorCode.INVALID_INPUT, "File URL host cannot be resolved") from exc
    if not addresses:
        raise WordToolkitError(ErrorCode.INVALID_INPUT, "File URL host cannot be resolved")
    validated_addresses: list[ipaddress.IPv4Address | ipaddress.IPv6Address] = []
    for address in addresses:
        try:
            ip = ipaddress.ip_address(address)
        except ValueError as exc:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT, "File URL host resolved incorrectly"
            ) from exc
        if not local_access_allowed and (not ip.is_global or ip.is_multicast):
            raise WordToolkitError(ErrorCode.AUTH_FORBIDDEN, "Private network file URL rejected")
        validated_addresses.append(ip)
    validated_addresses.sort(key=lambda ip: (ip.version, str(ip)))
    return tuple(str(ip) for ip in validated_addresses)


def safe_join(root: Path, *segments: str) -> Path:
    target = root.joinpath(*segments).resolve()
    if not target.is_relative_to(root.resolve()):
        raise WordToolkitError(ErrorCode.UNSAFE_PATH, "Resolved path escaped storage root")
    return target


def atomic_permissions(path: Path) -> None:
    os.chmod(path, 0o600)
