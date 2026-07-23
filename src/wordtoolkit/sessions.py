from __future__ import annotations

import asyncio
import contextlib
import shutil
import time
from collections.abc import AsyncIterator
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .config import Settings
from .errors import ErrorCode, WordToolkitError
from .ids import opaque_id, owner_key
from .security import safe_join


@dataclass(slots=True)
class DocumentRecord:
    document_id: str
    owner: str
    session_id: str
    original_path: Path
    current_path: Path
    engine: Any
    created_at: float
    touched_at: float
    version: int = 0
    source_name: str = "document.docx"
    closed: bool = False
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)


@dataclass(slots=True)
class SessionRecord:
    session_id: str
    owner: str
    root: Path
    created_at: float
    touched_at: float
    documents: set[str] = field(default_factory=set)
    closed: bool = False


@dataclass(slots=True)
class ArtifactRecord:
    artifact_id: str
    owner: str
    path: Path
    mime_type: str
    filename: str
    created_at: float
    expires_at: float


class SessionStore:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.root = settings.storage_root.resolve()
        self.root.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.sessions: dict[str, SessionRecord] = {}
        self.documents: dict[str, DocumentRecord] = {}
        self.artifacts: dict[str, ArtifactRecord] = {}
        self._map_lock = asyncio.Lock()

    async def create_session(self, subject: str) -> SessionRecord:
        owner = owner_key(subject)
        session_id = opaque_id("ses")
        root = safe_join(self.root, owner, session_id)
        now = time.time()
        record = SessionRecord(session_id, owner, root, now, now)
        async with self._map_lock:
            active = sum(item.owner == owner for item in self.sessions.values())
            if active >= self.settings.max_sessions_per_owner:
                raise WordToolkitError(ErrorCode.LIMIT_EXCEEDED, "Active session limit exceeded")
            root.mkdir(parents=True, mode=0o700)
            self.sessions[session_id] = record
        return record

    async def get_session(self, subject: str, session_id: str) -> SessionRecord:
        owner = owner_key(subject)
        record = self.sessions.get(session_id)
        if record is None or record.owner != owner or record.closed:
            raise WordToolkitError(ErrorCode.SESSION_NOT_FOUND, "Session was not found")
        if time.time() - record.touched_at > self.settings.session_ttl_seconds:
            await self.close_session(subject, session_id)
            raise WordToolkitError(ErrorCode.SESSION_NOT_FOUND, "Session has expired")
        record.touched_at = time.time()
        return record

    async def add_document(
        self,
        subject: str,
        session_id: str,
        path: Path,
        engine: Any,
        source_name: str,
    ) -> DocumentRecord:
        session = await self.get_session(subject, session_id)
        document_id = opaque_id("doc")
        now = time.time()
        record = DocumentRecord(
            document_id=document_id,
            owner=session.owner,
            session_id=session_id,
            original_path=path,
            current_path=path,
            engine=engine,
            created_at=now,
            touched_at=now,
            source_name=source_name,
        )
        async with self._map_lock:
            if session.closed or self.sessions.get(session_id) is not session:
                with contextlib.suppress(Exception):
                    engine.close()
                raise WordToolkitError(ErrorCode.SESSION_NOT_FOUND, "Session was not found")
            if len(session.documents) >= self.settings.max_documents_per_session:
                with contextlib.suppress(Exception):
                    engine.close()
                raise WordToolkitError(
                    ErrorCode.LIMIT_EXCEEDED, "Document limit for this session was exceeded"
                )
            self.documents[document_id] = record
            session.documents.add(document_id)
        return record

    async def get_document(self, subject: str, document_id: str) -> DocumentRecord:
        owner = owner_key(subject)
        record = self.documents.get(document_id)
        if record is None or record.owner != owner or record.closed:
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Document was not found")
        await self.get_session(subject, record.session_id)
        record.touched_at = time.time()
        return record

    @contextlib.asynccontextmanager
    async def locked_document(
        self, subject: str, document_id: str
    ) -> AsyncIterator[DocumentRecord]:
        record = await self.get_document(subject, document_id)
        async with record.lock:
            if record.closed or self.documents.get(document_id) is not record:
                raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Document was not found")
            session = self.sessions.get(record.session_id)
            if session is None or session.closed:
                raise WordToolkitError(ErrorCode.SESSION_NOT_FOUND, "Session was not found")
            yield record

    @staticmethod
    def require_version(record: DocumentRecord, expected_version: int | None) -> None:
        if (
            expected_version is None
            or isinstance(expected_version, bool)
            or not isinstance(expected_version, int)
            or expected_version < 0
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "expected_version is required for every draft mutation",
                {"field": "expected_version"},
            )
        if expected_version != record.version:
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "Draft version changed before the mutation",
                {"expected": expected_version, "actual": record.version},
                retryable=True,
            )

    async def register_artifact(
        self,
        subject: str,
        path: Path,
        mime_type: str,
        filename: str,
        ttl: int | None = None,
    ) -> ArtifactRecord:
        return (
            await self.register_artifacts(
                subject,
                [(path, mime_type, filename)],
                ttl=ttl,
            )
        )[0]

    async def register_artifacts(
        self,
        subject: str,
        files: list[tuple[Path, str, str]],
        ttl: int | None = None,
    ) -> list[ArtifactRecord]:
        if not files:
            return []
        owner = owner_key(subject)
        resolved_files: list[tuple[Path, str, str]] = []
        sessions: dict[str, SessionRecord] = {}
        for path, mime_type, filename in files:
            resolved = path.resolve()
            session = next(
                (
                    item
                    for item in self.sessions.values()
                    if not item.closed
                    and item.owner == owner
                    and resolved.is_relative_to(item.root.resolve())
                ),
                None,
            )
            if session is None:
                raise WordToolkitError(
                    ErrorCode.UNSAFE_PATH, "Artifact is outside the caller's session"
                )
            if not resolved.is_file():
                raise WordToolkitError(
                    ErrorCode.DOCUMENT_NOT_FOUND, "Artifact source was not found"
                )
            resolved_files.append((resolved, mime_type, filename))
            sessions[session.session_id] = session
        for session in sessions.values():
            stored_bytes = sum(
                item.stat().st_size for item in session.root.rglob("*") if item.is_file()
            )
            if stored_bytes > self.settings.max_session_bytes:
                raise WordToolkitError(ErrorCode.LIMIT_EXCEEDED, "Session storage quota exceeded")
        now = time.time()
        artifact_dir = safe_join(self.root, owner, "artifacts")
        artifact_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
        async with self._map_lock:
            if any(
                session.closed or self.sessions.get(session.session_id) is not session
                for session in sessions.values()
            ):
                raise WordToolkitError(ErrorCode.SESSION_NOT_FOUND, "Session was not found")
            active = sum(
                item.owner == owner and item.expires_at >= now for item in self.artifacts.values()
            )
            if active + len(resolved_files) > self.settings.max_artifacts_per_owner:
                raise WordToolkitError(ErrorCode.LIMIT_EXCEEDED, "Active artifact limit exceeded")
            artifact_bytes = sum(
                item.path.stat().st_size
                for item in self.artifacts.values()
                if item.owner == owner and item.expires_at >= now and item.path.exists()
            )
            requested_bytes = sum(path.stat().st_size for path, _mime, _name in resolved_files)
            if artifact_bytes + requested_bytes > self.settings.max_artifact_bytes_per_owner:
                raise WordToolkitError(ErrorCode.LIMIT_EXCEEDED, "Artifact storage quota exceeded")
            records: list[ArtifactRecord] = []
            copied: list[Path] = []
            try:
                for resolved, mime_type, filename in resolved_files:
                    artifact_id = opaque_id("art")
                    safe_filename = Path(filename).name.replace("\r", "_").replace("\n", "_")[:160]
                    artifact_path = (
                        artifact_dir / f"{artifact_id}-{safe_filename or 'artifact.bin'}"
                    )
                    copied.append(artifact_path)
                    shutil.copy2(resolved, artifact_path)
                    records.append(
                        ArtifactRecord(
                            artifact_id=artifact_id,
                            owner=owner,
                            path=artifact_path,
                            mime_type=mime_type,
                            filename=safe_filename or "artifact.bin",
                            created_at=now,
                            expires_at=now + (ttl or self.settings.artifact_ttl_seconds),
                        )
                    )
            except Exception:
                for copied_path in copied:
                    with contextlib.suppress(OSError):
                        copied_path.unlink()
                raise
            self.artifacts.update({record.artifact_id: record for record in records})
        return records

    async def discard_artifacts(self, records: list[ArtifactRecord]) -> None:
        async with self._map_lock:
            for record in records:
                if self.artifacts.get(record.artifact_id) is record:
                    self.artifacts.pop(record.artifact_id, None)
                with contextlib.suppress(OSError):
                    record.path.unlink()

    def get_artifact(self, subject: str, artifact_id: str) -> ArtifactRecord:
        record = self.artifacts.get(artifact_id)
        if record is None or record.owner != owner_key(subject) or record.expires_at < time.time():
            raise WordToolkitError(
                ErrorCode.DOCUMENT_NOT_FOUND, "Artifact was not found or expired"
            )
        return record

    async def close_document(
        self, subject: str, document_id: str, expected_version: int | None
    ) -> None:
        record = await self.get_document(subject, document_id)
        async with record.lock:
            if record.closed or self.documents.get(document_id) is not record:
                raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Document was not found")
            self.require_version(record, expected_version)
            record.engine.close()
            record.closed = True
            async with self._map_lock:
                self.documents.pop(document_id, None)
                session = self.sessions.get(record.session_id)
                if session:
                    session.documents.discard(document_id)

    async def close_session(self, subject: str, session_id: str) -> None:
        owner = owner_key(subject)
        record = self.sessions.get(session_id)
        if record is None or record.owner != owner:
            return
        await self._close_session_record(record)

    async def _close_session_record(self, record: SessionRecord) -> None:
        async with self._map_lock:
            if self.sessions.get(record.session_id) is not record:
                return
            record.closed = True
            document_ids = list(record.documents)
        for document_id in document_ids:
            doc = self.documents.get(document_id)
            if doc is None:
                continue
            async with doc.lock:
                if self.documents.get(document_id) is not doc:
                    continue
                doc.closed = True
                with contextlib.suppress(Exception):
                    doc.engine.close()
                async with self._map_lock:
                    self.documents.pop(document_id, None)
                    record.documents.discard(document_id)
        async with self._map_lock:
            self.sessions.pop(record.session_id, None)
        shutil.rmtree(record.root, ignore_errors=True)

    async def cleanup_expired(self) -> dict[str, int]:
        now = time.time()
        removed_sessions = 0
        removed_artifacts = 0
        for session in list(self.sessions.values()):
            if now - session.touched_at > self.settings.session_ttl_seconds:
                await self._close_session_record(session)
                removed_sessions += 1
        for artifact in list(self.artifacts.values()):
            if artifact.expires_at < now:
                self.artifacts.pop(artifact.artifact_id, None)
                with contextlib.suppress(FileNotFoundError):
                    artifact.path.unlink()
                removed_artifacts += 1
        return {"sessions": removed_sessions, "artifacts": removed_artifacts}
