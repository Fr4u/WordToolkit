from __future__ import annotations

import hashlib
import hmac
import mimetypes
import os
import re
import shutil
import time
from pathlib import Path
from urllib.parse import unquote, urljoin, urlparse

import httpx
from mcp.types import CallToolResult, ContentBlock, ResourceLink, TextContent
from pydantic import AnyUrl

from .config import Settings
from .engine import DocumentRenderer, OoxmlValidator
from .errors import ErrorCode, WordToolkitError
from .ids import opaque_id
from .live_word import LiveWordBridge
from .security import atomic_permissions, validate_remote_url
from .sessions import ArtifactRecord, SessionRecord, SessionStore


def clean_filename(value: str, default: str = "upload.bin") -> str:
    name = Path(value or default).name
    name = re.sub(r"[^A-Za-z0-9._ -]", "_", name).strip(" .")
    return name[:160] or default


class ToolRuntime:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.store = SessionStore(settings)
        self.renderer = DocumentRenderer(settings)
        self.validator = OoxmlValidator(settings)
        self.live_word = LiveWordBridge(settings, self.validator)

    async def session(self, subject: str, session_id: str = "") -> SessionRecord:
        return (
            await self.store.get_session(subject, session_id)
            if session_id
            else await self.store.create_session(subject)
        )

    async def download_file(self, file, session: SessionRecord, *, extensions: set[str]) -> Path:
        if self.settings.is_local_stdio:
            explicit_local_path = getattr(file, "local_path", "")
            local_source = (
                Path(explicit_local_path)
                if explicit_local_path
                else self._local_file_path(file.download_url)
            )
            if local_source is not None:
                return self._copy_local_file(local_source, session, extensions=extensions)

        validate_remote_url(file.download_url, self.settings.upload_host_suffixes)
        filename = clean_filename(file.file_name or f"{file.file_id}.bin")
        extension = Path(filename).suffix.lower()
        if extension not in extensions:
            raise WordToolkitError(
                ErrorCode.UNSUPPORTED_FORMAT,
                "File extension is not allowed for this operation",
                {"extension": extension, "allowed": sorted(extensions)},
            )
        target_dir = session.root / "uploads"
        target_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
        stored_bytes = sum(
            item.stat().st_size for item in session.root.rglob("*") if item.is_file()
        )
        remaining_session = self.settings.max_session_bytes - stored_bytes
        if remaining_session <= 0:
            raise WordToolkitError(ErrorCode.LIMIT_EXCEEDED, "Session storage quota exceeded")
        target = target_dir / f"{opaque_id('upl')}-{filename}"
        url = file.download_url
        timeout = httpx.Timeout(self.settings.http_download_timeout_seconds)
        async with httpx.AsyncClient(timeout=timeout, follow_redirects=False) as client:
            for _ in range(4):
                validate_remote_url(url, self.settings.upload_host_suffixes)
                async with client.stream(
                    "GET", url, headers={"Accept": "application/octet-stream"}
                ) as response:
                    if response.status_code in {301, 302, 303, 307, 308}:
                        location = response.headers.get("location")
                        if not location:
                            raise WordToolkitError(
                                ErrorCode.INVALID_INPUT, "File redirect has no location"
                            )
                        url = urljoin(url, location)
                        continue
                    if response.status_code != 200:
                        raise WordToolkitError(
                            ErrorCode.INVALID_INPUT,
                            "File download failed",
                            {"status": response.status_code},
                            retryable=response.status_code >= 500,
                        )
                    declared = response.headers.get("content-length")
                    per_file_limit = min(self.settings.max_upload_bytes, remaining_session)
                    if declared and int(declared) > per_file_limit:
                        raise WordToolkitError(
                            ErrorCode.LIMIT_EXCEEDED, "Remote file exceeds upload limit"
                        )
                    size = 0
                    with target.open("wb") as output:
                        async for chunk in response.aiter_bytes(1024 * 1024):
                            size += len(chunk)
                            if size > per_file_limit:
                                output.close()
                                target.unlink(missing_ok=True)
                                raise WordToolkitError(
                                    ErrorCode.LIMIT_EXCEEDED, "Downloaded file exceeds upload limit"
                                )
                            output.write(chunk)
                    atomic_permissions(target)
                    return target
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "Too many file download redirects")

    @staticmethod
    def _local_file_path(reference: str) -> Path | None:
        value = reference.strip()
        if not value:
            return None
        parsed = urlparse(value)
        if parsed.scheme == "file":
            if parsed.netloc not in {"", "localhost"}:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "Network file URLs are not allowed")
            raw_path = unquote(parsed.path)
            if os.name == "nt" and re.match(r"^/[A-Za-z]:/", raw_path):
                raw_path = raw_path[1:]
            return Path(raw_path)
        if not parsed.scheme:
            candidate = Path(value)
            if candidate.is_absolute():
                return candidate
        return None

    def _copy_local_file(
        self,
        source: Path,
        session: SessionRecord,
        *,
        extensions: set[str],
    ) -> Path:
        try:
            resolved = source.expanduser().resolve(strict=True)
        except (FileNotFoundError, OSError) as exc:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT, "Local input file was not found"
            ) from exc
        if not resolved.is_file():
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "Local input is not a file")
        extension = resolved.suffix.lower()
        if extension not in extensions:
            raise WordToolkitError(
                ErrorCode.UNSUPPORTED_FORMAT,
                "File extension is not allowed for this operation",
                {"extension": extension, "allowed": sorted(extensions)},
            )
        stored_bytes = sum(
            item.stat().st_size for item in session.root.rglob("*") if item.is_file()
        )
        per_file_limit = min(
            self.settings.max_upload_bytes,
            self.settings.max_session_bytes - stored_bytes,
        )
        if per_file_limit <= 0 or resolved.stat().st_size > per_file_limit:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED, "Local input exceeds the session limit"
            )
        target_dir = session.root / "uploads"
        target_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
        target = target_dir / f"{opaque_id('upl')}-{clean_filename(resolved.name)}"
        shutil.copy2(resolved, target)
        atomic_permissions(target)
        return target

    def sign_artifact(self, artifact: ArtifactRecord) -> str:
        expires = int(artifact.expires_at)
        payload = f"{artifact.artifact_id}.{artifact.owner}.{expires}".encode()
        signature = hmac.new(
            self.settings.signing_secret.encode(), payload, hashlib.sha256
        ).hexdigest()
        return f"{self.settings.public_base_url.rstrip('/')}/v1/artifacts/{artifact.artifact_id}/download?owner={artifact.owner}&expires={expires}&sig={signature}"

    def artifact_uri(self, artifact: ArtifactRecord) -> str:
        if self.settings.is_local_stdio:
            return artifact.path.resolve().as_uri()
        return self.sign_artifact(artifact)

    def verify_artifact_signature(
        self, artifact_id: str, owner: str, expires: int, signature: str
    ) -> bool:
        if expires < time.time():
            return False
        payload = f"{artifact_id}.{owner}.{expires}".encode()
        expected = hmac.new(
            self.settings.signing_secret.encode(), payload, hashlib.sha256
        ).hexdigest()
        return hmac.compare_digest(signature, expected)

    async def artifact_result(
        self,
        subject: str,
        path: Path,
        *,
        mime_type: str | None = None,
        filename: str | None = None,
        data: dict | None = None,
        label: str = "File ready",
    ) -> CallToolResult:
        artifact = await self.store.register_artifact(
            subject,
            path,
            mime_type or mimetypes.guess_type(path.name)[0] or "application/octet-stream",
            filename or path.name,
        )
        try:
            url = self.artifact_uri(artifact)
            structured = {
                "ok": True,
                "data": {
                    **(data or {}),
                    "artifact": {
                        "artifact_id": artifact.artifact_id,
                        "file_name": artifact.filename,
                        "mime_type": artifact.mime_type,
                        "size_bytes": artifact.path.stat().st_size,
                        "expires_at": int(artifact.expires_at),
                        "download_url": url,
                    },
                },
                "warnings": [],
            }
            return CallToolResult(
                content=[
                    TextContent(type="text", text=label),
                    ResourceLink(
                        type="resource_link",
                        name=artifact.filename,
                        title=artifact.filename,
                        uri=AnyUrl(url),
                        mimeType=artifact.mime_type,
                        size=artifact.path.stat().st_size,
                    ),
                ],
                structuredContent=structured,
            )
        except Exception:
            await self.store.discard_artifacts([artifact])
            raise

    async def multi_artifact_result(
        self,
        subject: str,
        files: list[tuple[Path, str, str]],
        *,
        data: dict | None = None,
        label: str = "Files ready",
    ) -> CallToolResult:
        artifacts = await self.store.register_artifacts(subject, files)
        try:
            records = []
            content: list[ContentBlock] = [TextContent(type="text", text=label)]
            for artifact, (path, mime_type, filename) in zip(artifacts, files, strict=True):
                url = self.artifact_uri(artifact)
                records.append(
                    {
                        "artifact_id": artifact.artifact_id,
                        "file_name": filename,
                        "mime_type": mime_type,
                        "size_bytes": path.stat().st_size,
                        "expires_at": int(artifact.expires_at),
                        "download_url": url,
                    }
                )
                content.append(
                    ResourceLink(
                        type="resource_link",
                        name=filename,
                        title=filename,
                        uri=AnyUrl(url),
                        mimeType=mime_type,
                        size=path.stat().st_size,
                    )
                )
            return CallToolResult(
                content=content,
                structuredContent={
                    "ok": True,
                    "data": {**(data or {}), "artifacts": records},
                    "warnings": [],
                },
            )
        except Exception:
            await self.store.discard_artifacts(artifacts)
            raise
