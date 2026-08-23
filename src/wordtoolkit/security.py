from __future__ import annotations

import hashlib
import hmac
import ipaddress
import os
import re
import shutil
import socket
import stat
import tempfile
import time
import zipfile
from collections.abc import Iterator
from contextlib import contextmanager
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from typing import BinaryIO
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
_SNAPSHOT_CHUNK_BYTES = 1024 * 1024
_ATOMIC_RENAME_RETRY_DELAYS = (0.01, 0.05, 0.2)
_WINDOWS_SHARING_ERRORS = {32, 33}


class _WindowsFileOpenError(OSError):
    winerror: int


def _is_transient_sharing_error(error: OSError) -> bool:
    return getattr(error, "winerror", None) in _WINDOWS_SHARING_ERRORS


def _open_windows_shared_source(source_path: Path) -> BinaryIO:
    import ctypes
    import msvcrt
    from ctypes import wintypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)  # type: ignore[attr-defined]
    create_file = kernel32.CreateFileW
    create_file.argtypes = [
        wintypes.LPCWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.LPVOID,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.HANDLE,
    ]
    create_file.restype = wintypes.HANDLE
    close_handle = kernel32.CloseHandle
    close_handle.argtypes = [wintypes.HANDLE]
    close_handle.restype = wintypes.BOOL
    invalid_handle = wintypes.HANDLE(-1).value
    handle = create_file(
        str(source_path),
        0x80000000,  # GENERIC_READ
        0x00000001 | 0x00000002 | 0x00000004,  # FILE_SHARE_READ/WRITE/DELETE
        None,
        3,  # OPEN_EXISTING
        0x08000000,  # FILE_FLAG_SEQUENTIAL_SCAN
        None,
    )
    if handle == invalid_handle:
        error_code = ctypes.get_last_error()  # type: ignore[attr-defined]
        error = _WindowsFileOpenError(error_code, "CreateFileW failed")
        error.winerror = error_code
        raise error

    try:
        fd = msvcrt.open_osfhandle(  # type: ignore[attr-defined]
            handle, os.O_RDONLY | getattr(os, "O_BINARY", 0)
        )
    except (OSError, ValueError):
        close_handle(handle)
        raise
    try:
        return os.fdopen(fd, "rb", closefd=True)
    except (OSError, ValueError):
        os.close(fd)
        raise


def _open_shared_source_once(source_path: Path) -> BinaryIO:
    if os.name == "nt":
        return _open_windows_shared_source(source_path)
    return source_path.open("rb")


def _open_shared_source(source_path: Path) -> BinaryIO:
    """Open a source with Word-compatible sharing and bounded lock retries."""
    for attempt in range(len(_ATOMIC_RENAME_RETRY_DELAYS) + 1):
        try:
            return _open_shared_source_once(source_path)
        except OSError as exc:
            if not _is_transient_sharing_error(exc) or attempt == len(_ATOMIC_RENAME_RETRY_DELAYS):
                raise
            time.sleep(_ATOMIC_RENAME_RETRY_DELAYS[attempt])
    raise RuntimeError("unreachable")


def _file_identity(value: os.stat_result) -> tuple[int, int, int, int]:
    return (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns)


@contextmanager
def stable_path_snapshot(source_path: Path, *, max_bytes: int) -> Iterator[Path]:
    """Yield one bounded private copy after two matching reads of one source handle."""
    suffix = source_path.suffix.lower() or ".bin"
    with tempfile.TemporaryDirectory(prefix="wordtoolkit_snapshot_") as work:
        snapshot_path = Path(work) / f"package{suffix}"
        try:
            with _open_shared_source(source_path) as source, snapshot_path.open("xb") as snapshot:
                os.chmod(snapshot_path, 0o600)
                before = os.fstat(source.fileno())
                first_digest = hashlib.sha256()
                first_size = 0
                while chunk := source.read(min(_SNAPSHOT_CHUNK_BYTES, max_bytes - first_size + 1)):
                    first_size += len(chunk)
                    if first_size > max_bytes:
                        raise WordToolkitError(
                            ErrorCode.LIMIT_EXCEEDED,
                            "Compressed file exceeds upload limit",
                        )
                    first_digest.update(chunk)
                    snapshot.write(chunk)
                between = os.fstat(source.fileno())

                source.seek(0)
                second_digest = hashlib.sha256()
                second_size = 0
                while chunk := source.read(min(_SNAPSHOT_CHUNK_BYTES, max_bytes - second_size + 1)):
                    second_size += len(chunk)
                    if second_size > max_bytes:
                        raise WordToolkitError(
                            ErrorCode.LIMIT_EXCEEDED,
                            "Compressed file exceeds upload limit",
                        )
                    second_digest.update(chunk)
                after = os.fstat(source.fileno())
                path_state = source_path.stat()
        except WordToolkitError:
            raise
        except OSError as exc:
            if _is_transient_sharing_error(exc):
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED,
                    "Package is temporarily locked by another process",
                    {
                        "reason": "sharing_violation",
                        "attempts": len(_ATOMIC_RENAME_RETRY_DELAYS) + 1,
                    },
                    retryable=True,
                ) from exc
            raise WordToolkitError(
                ErrorCode.DOCUMENT_NOT_FOUND,
                "Package could not be read",
            ) from exc

        stable = (
            _file_identity(before)
            == _file_identity(between)
            == _file_identity(after)
            == _file_identity(path_state)
            and first_size == second_size
            and hmac.compare_digest(first_digest.digest(), second_digest.digest())
        )
        if not stable:
            raise WordToolkitError(
                ErrorCode.OOXML_INVALID,
                "Package changed while a stable snapshot was being captured",
                retryable=True,
            )
        yield snapshot_path


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


def reject_reparse_ancestors(
    path: Path,
    *,
    error_code: ErrorCode = ErrorCode.UNSAFE_PATH,
    label: str = "Path",
) -> None:
    """Fail closed if any existing path component is a symlink or Windows reparse point."""
    current = Path(path.anchor) if path.is_absolute() else Path.cwd()
    for part in path.parts[1:] if path.is_absolute() else path.parts:
        current /= part
        try:
            metadata = current.lstat()
        except FileNotFoundError:
            continue
        except OSError as exc:
            raise WordToolkitError(
                error_code,
                f"{label} cannot be safely inspected",
                {"component": current.name},
            ) from exc
        reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
        file_attributes = getattr(metadata, "st_file_attributes", 0)
        if stat.S_ISLNK(metadata.st_mode) or file_attributes & reparse_flag:
            raise WordToolkitError(
                error_code,
                f"{label} contains a symbolic or reparse link",
                {"component": current.name},
            )


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

    @contextmanager
    def inspect_stable(self, path: Path) -> Iterator[tuple[Path, PackageInspection]]:
        """Yield one inspected snapshot for additional reads in the same operation."""
        if path.suffix.lower() in {".docm", ".dotm"}:
            raise WordToolkitError(
                ErrorCode.UNSUPPORTED_FORMAT, "Macro-enabled Word files are rejected"
            )
        with stable_path_snapshot(path, max_bytes=self.settings.max_upload_bytes) as snapshot:
            yield snapshot, self._inspect_snapshot(snapshot, source_suffix=path.suffix.lower())

    def inspect(self, path: Path) -> PackageInspection:
        with self.inspect_stable(path) as (_, report):
            return report

    def _inspect_snapshot(self, path: Path, *, source_suffix: str) -> PackageInspection:
        if source_suffix in {".docm", ".dotm"}:
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
        self._reject_reparse_ancestors(destination)
        if destination.exists() or destination.is_symlink():
            raise WordToolkitError(
                ErrorCode.UNSAFE_ARCHIVE,
                "ZIP extraction destination must not exist",
            )
        with self.inspect_stable(package) as (snapshot, report):
            staging = self._create_private_staging(destination)
            try:
                try:
                    with zipfile.ZipFile(snapshot) as archive:
                        remaining = self.settings.max_uncompressed_bytes
                        for info in archive.infolist():
                            rel = _safe_member_name(info.filename)
                            target = staging.joinpath(*rel.parts)
                            if info.is_dir():
                                self._reject_reparse_ancestors(target)
                                target.mkdir(parents=True, exist_ok=True)
                                self._reject_reparse_ancestors(target)
                                continue
                            self._reject_reparse_ancestors(target.parent)
                            target.parent.mkdir(parents=True, exist_ok=True)
                            self._reject_reparse_ancestors(target.parent)
                            # O_EXCL prevents following an attacker-created replacement
                            # file. O_NOFOLLOW is available on POSIX; component checks
                            # above provide the Windows fallback for reparse paths.
                            flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
                            flags |= getattr(os, "O_NOFOLLOW", 0)
                            try:
                                output_fd = os.open(target, flags, 0o600)
                            except OSError as exc:
                                raise WordToolkitError(
                                    ErrorCode.UNSAFE_ARCHIVE,
                                    "ZIP extraction target is not a new regular path",
                                    {"entry": rel.as_posix()},
                                ) from exc
                            with (
                                archive.open(info) as source,
                                os.fdopen(output_fd, "wb") as output,
                            ):
                                while chunk := source.read(
                                    min(_SNAPSHOT_CHUNK_BYTES, remaining + 1)
                                ):
                                    remaining -= len(chunk)
                                    if remaining < 0:
                                        raise WordToolkitError(
                                            ErrorCode.UNSAFE_ARCHIVE,
                                            "Extraction limit exceeded",
                                        )
                                    output.write(chunk)
                except WordToolkitError:
                    raise
                except (OSError, RuntimeError, zipfile.BadZipFile) as exc:
                    raise WordToolkitError(
                        ErrorCode.UNSAFE_ARCHIVE,
                        "ZIP extraction failed before publication",
                    ) from exc
                self._publish_staging_directory(staging, destination)
            finally:
                if staging.exists():
                    shutil.rmtree(staging, ignore_errors=True)
            return report

    @classmethod
    def _publish_staging_directory(cls, staging: Path, destination: Path) -> None:
        """Publish a new directory, tolerating bounded transient scanner locks."""
        attempts = len(_ATOMIC_RENAME_RETRY_DELAYS) + 1
        for attempt in range(attempts):
            cls._reject_reparse_ancestors(destination)
            if destination.exists() or destination.is_symlink():
                raise WordToolkitError(
                    ErrorCode.UNSAFE_ARCHIVE,
                    "ZIP extraction destination appeared before publication",
                )
            try:
                staging.rename(destination)
                return
            except PermissionError as exc:
                if attempt == attempts - 1:
                    raise WordToolkitError(
                        ErrorCode.UNSAFE_ARCHIVE,
                        "ZIP extraction result could not be published atomically",
                        retryable=True,
                    ) from exc
                time.sleep(_ATOMIC_RENAME_RETRY_DELAYS[attempt])
            except OSError as exc:
                raise WordToolkitError(
                    ErrorCode.UNSAFE_ARCHIVE,
                    "ZIP extraction result could not be published atomically",
                ) from exc

    @classmethod
    def _create_private_staging(cls, destination: Path) -> Path:
        staging: Path | None = None
        try:
            destination.parent.mkdir(parents=True, exist_ok=True)
            cls._reject_reparse_ancestors(destination.parent)
            staging = Path(
                tempfile.mkdtemp(
                    prefix=f".{destination.name}.wordtoolkit-",
                    dir=destination.parent,
                )
            )
            os.chmod(staging, 0o700)
            return staging
        except WordToolkitError:
            raise
        except OSError as exc:
            if staging is not None:
                shutil.rmtree(staging, ignore_errors=True)
            raise WordToolkitError(
                ErrorCode.UNSAFE_ARCHIVE,
                "Private extraction staging could not be created",
            ) from exc

    @staticmethod
    def _reject_reparse_ancestors(path: Path) -> None:
        """Fail closed if any existing component is a symlink/reparse path."""
        reject_reparse_ancestors(
            path,
            error_code=ErrorCode.UNSAFE_ARCHIVE,
            label="Extraction path",
        )


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
