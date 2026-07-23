from __future__ import annotations

import asyncio
import json
import threading
import zipfile
from pathlib import Path
from typing import Any

import pytest
from mcp.server.fastmcp import FastMCP

from docx_mcp.document.base import W14, W
from wordtoolkit.config import Settings
from wordtoolkit.engine import WordDocumentEngine
from wordtoolkit.engine.validator import package_hashes
from wordtoolkit.errors import ErrorCode, WordToolkitError
from wordtoolkit.runtime import ToolRuntime
from wordtoolkit.server.tools import register_tools
from wordtoolkit.sessions import SessionStore


def _tool_payload(result) -> dict:
    structured = getattr(result, "structuredContent", None)
    if structured is not None:
        return structured
    return json.loads(result[0].text)


def _build_server(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> tuple[FastMCP, ToolRuntime]:
    monkeypatch.setenv("WORDTOOLKIT_AUTH_MODE", "local_stdio")
    settings = Settings(
        auth_mode="local_stdio",
        storage_root=tmp_path / "storage",
        public_base_url="http://127.0.0.1",
    )
    runtime = ToolRuntime(settings)
    server = FastMCP("WordToolkit concurrency test")
    register_tools(server, runtime)
    return server, runtime


async def _create_document(server: FastMCP) -> dict:
    return _tool_payload(await server.call_tool("create_document", {}))["data"]


def _snapshot_hashes(engine: WordDocumentEngine, path: Path) -> dict[str, str]:
    engine.snapshot(path)
    return package_hashes(path)


@pytest.mark.asyncio
async def test_missing_and_stale_versions_never_mutate_or_publish(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    document_id = created["document_id"]
    record = runtime.store.documents[document_id]
    original_engine = record.engine

    for invalid_version in (None, False, 0.0, "0"):
        arguments: dict[str, Any] = {
            "document_id": document_id,
            "after_paragraph_id": created["anchor_paragraph_id"],
            "text": "invalid version",
        }
        if invalid_version is not None:
            arguments["expected_version"] = invalid_version
        invalid = _tool_payload(
            await server.call_tool(
                "insert_paragraph",
                arguments,
            )
        )
        assert invalid["error"] == {
            "code": "INVALID_INPUT",
            "message": "expected_version is required for every draft mutation",
            "details": {"field": "expected_version"},
            "retryable": False,
        }
        assert record.version == 0

    mixed_read = _tool_payload(
        await server.call_tool(
            "manage_lists",
            {"document_id": document_id, "action": "list"},
        )
    )
    assert mixed_read["error"]["code"] == "INVALID_INPUT"
    assert record.version == 0

    inserted = _tool_payload(
        await server.call_tool(
            "insert_paragraph",
            {
                "document_id": document_id,
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "first writer",
                "expected_version": 0,
            },
        )
    )
    assert inserted["data"]["draft_version"] == 1
    assert record.engine is not original_engine
    assert original_engine.document is None
    assert record.engine.doc.workdir is not None
    assert record.engine.doc.workdir.exists()
    before = _snapshot_hashes(record.engine, tmp_path / "before-stale.docx")
    assert {"[Content_Types].xml", "_rels/.rels", "word/document.xml"} <= before.keys()
    engine_before = record.engine

    stale = _tool_payload(
        await server.call_tool(
            "insert_paragraph",
            {
                "document_id": document_id,
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "second stale writer",
                "expected_version": 0,
            },
        )
    )
    assert stale["error"] == {
        "code": "VERSION_CONFLICT",
        "message": "Draft version changed before the mutation",
        "details": {"expected": 0, "actual": 1},
        "retryable": True,
    }

    stale_save = _tool_payload(
        await server.call_tool(
            "save_document",
            {
                "document_id": document_id,
                "file_name": "stale.docx",
                "expected_version": 0,
            },
        )
    )
    assert stale_save["error"]["code"] == "VERSION_CONFLICT"
    assert record.version == 1
    assert record.engine is engine_before
    assert _snapshot_hashes(record.engine, tmp_path / "after-stale.docx") == before
    assert not runtime.store.artifacts
    assert not list(runtime.store.sessions[record.session_id].root.rglob("v2-*.docx"))


@pytest.mark.asyncio
async def test_markdown_export_is_a_version_stable_locked_snapshot(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    document_id = created["document_id"]
    record = runtime.store.documents[document_id]
    artifact_started = asyncio.Event()
    release_artifact = asyncio.Event()
    original_artifact_result = runtime.artifact_result
    calls = 0

    async def blocked_artifact_result(*args: Any, **kwargs: Any):
        nonlocal calls
        calls += 1
        if calls == 1:
            artifact_started.set()
            await release_artifact.wait()
        return await original_artifact_result(*args, **kwargs)

    monkeypatch.setattr(runtime, "artifact_result", blocked_artifact_result)
    first_export = asyncio.create_task(
        server.call_tool(
            "export_document",
            {
                "document_id": document_id,
                "output_format": "markdown",
                "file_name": "snapshot.md",
            },
        )
    )
    await asyncio.wait_for(artifact_started.wait(), timeout=2)

    mutation = asyncio.create_task(
        server.call_tool(
            "insert_paragraph",
            {
                "document_id": document_id,
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "content from version one",
                "expected_version": 0,
            },
        )
    )
    await asyncio.sleep(0.05)
    assert not mutation.done(), "mutation escaped while Markdown artifact publication held the lock"
    release_artifact.set()

    first = _tool_payload(await first_export)
    mutated = _tool_payload(await mutation)
    assert first["data"]["draft_version"] == 0
    assert mutated["data"]["draft_version"] == 1

    second = _tool_payload(
        await server.call_tool(
            "export_document",
            {
                "document_id": document_id,
                "output_format": "markdown",
                "file_name": "snapshot.md",
            },
        )
    )
    assert second["data"]["draft_version"] == 1
    first_path = runtime.store.artifacts[first["data"]["artifact"]["artifact_id"]].path
    second_path = runtime.store.artifacts[second["data"]["artifact"]["artifact_id"]].path
    assert first_path != second_path
    assert "content from version one" not in first_path.read_text(encoding="utf-8")
    assert "content from version one" in second_path.read_text(encoding="utf-8")
    assert record.version == 1


@pytest.mark.asyncio
async def test_quality_snapshot_reports_captured_version_during_later_mutation(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_validate = runtime.validator.validate
    validation_started = threading.Event()
    release_validation = threading.Event()

    def blocked_validate(path: Path):
        validation_started.set()
        if not release_validation.wait(timeout=5):
            raise TimeoutError("test did not release snapshot validation")
        return original_validate(path)

    monkeypatch.setattr(runtime.validator, "validate", blocked_validate)
    validation = asyncio.create_task(
        server.call_tool("validate_ooxml", {"document_id": created["document_id"]})
    )
    assert await asyncio.to_thread(validation_started.wait, 2)

    mutation = _tool_payload(
        await server.call_tool(
            "insert_paragraph",
            {
                "document_id": created["document_id"],
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "mutation after immutable quality snapshot",
                "expected_version": 0,
            },
        )
    )
    assert mutation["data"]["draft_version"] == 1
    release_validation.set()
    validation_result = _tool_payload(await validation)

    assert validation_result["data"]["draft_version"] == 0
    snapshots = list(
        (runtime.store.sessions[record.session_id].root / "quality" / record.document_id).glob(
            "validate-v0-snap_*.docx"
        )
    )
    assert len(snapshots) == 1
    assert record.version == 1


@pytest.mark.asyncio
async def test_sequential_mutations_and_publish_keep_one_live_workspace(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]

    first = _tool_payload(
        await server.call_tool(
            "insert_paragraph",
            {
                "document_id": created["document_id"],
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "first transactional edit",
                "expected_version": 0,
            },
        )
    )
    assert first["data"]["draft_version"] == 1
    first_workspace = record.engine._owned_workspace_root
    assert first_workspace is not None and first_workspace.is_dir()
    assert record.engine.doc.workdir is not None and record.engine.doc.workdir.is_dir()

    second = _tool_payload(
        await server.call_tool(
            "insert_paragraph",
            {
                "document_id": created["document_id"],
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "second transactional edit",
                "expected_version": 1,
            },
        )
    )
    assert second["data"]["draft_version"] == 2
    second_workspace = record.engine._owned_workspace_root
    assert second_workspace is not None and second_workspace.is_dir()
    assert second_workspace != first_workspace
    assert not first_workspace.exists()

    saved = _tool_payload(
        await server.call_tool(
            "save_document",
            {
                "document_id": created["document_id"],
                "file_name": "sequential.docx",
                "expected_version": 2,
            },
        )
    )
    assert saved["data"]["draft_version"] == 3
    published_workspace = record.engine._owned_workspace_root
    assert published_workspace is not None and published_workspace.is_dir()
    assert not second_workspace.exists()
    assert record.engine.doc.workdir is not None and record.engine.doc.workdir.is_dir()

    artifact = runtime.store.artifacts[saved["data"]["artifact"]["artifact_id"]]
    with zipfile.ZipFile(artifact.path) as archive:
        names = set(archive.namelist())
        assert {"[Content_Types].xml", "_rels/.rels", "word/document.xml"} <= names
        document_xml = archive.read("word/document.xml").decode("utf-8")
    assert "first transactional edit" in document_xml
    assert "second transactional edit" in document_xml

    transaction_root = (
        runtime.store.sessions[record.session_id].root / ".transactions" / record.document_id
    )
    assert [path for path in transaction_root.iterdir() if path.is_dir()] == [published_workspace]

    closed = _tool_payload(
        await server.call_tool(
            "close_document",
            {"document_id": created["document_id"], "expected_version": 3},
        )
    )
    assert closed["data"]["closed"] is True
    assert not published_workspace.exists()


@pytest.mark.asyncio
async def test_committed_replacement_cleanup_is_off_event_loop_and_drained_on_cancel(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]

    first = _tool_payload(
        await server.call_tool(
            "insert_paragraph",
            {
                "document_id": created["document_id"],
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "first committed edit",
                "expected_version": 0,
            },
        )
    )
    assert first["data"]["draft_version"] == 1
    replaced_engine = record.engine
    replaced_workspace = replaced_engine._owned_workspace_root
    assert replaced_workspace is not None and replaced_workspace.is_dir()

    close_started = threading.Event()
    release_close = threading.Event()
    original_close = replaced_engine.close

    def slow_close() -> None:
        close_started.set()
        release_close.wait(2)
        original_close()

    monkeypatch.setattr(replaced_engine, "close", slow_close)
    mutation = asyncio.create_task(
        server.call_tool(
            "insert_paragraph",
            {
                "document_id": created["document_id"],
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "second committed edit",
                "expected_version": 1,
            },
        )
    )

    try:
        assert await asyncio.to_thread(close_started.wait, 2)
        await asyncio.wait_for(asyncio.sleep(0), timeout=0.1)
        assert not mutation.done()
        mutation.cancel()
    finally:
        release_close.set()

    with pytest.raises(asyncio.CancelledError):
        await mutation
    assert record.version == 2
    assert record.engine is not replaced_engine
    assert record.engine._owned_workspace_root is not None
    assert record.engine._owned_workspace_root.is_dir()
    assert not replaced_workspace.exists()


@pytest.mark.asyncio
async def test_failed_mutation_discards_partially_changed_clone(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-failed-mutation.docx")
    original_call = WordDocumentEngine.call

    def fail_after_mutation(self: WordDocumentEngine, name: str, *args: Any, **kwargs: Any):
        result = original_call(self, name, *args, **kwargs)
        if name == "insert_paragraph":
            raise WordToolkitError(ErrorCode.OOXML_INVALID, "injected mutation failure")
        return result

    monkeypatch.setattr(WordDocumentEngine, "call", fail_after_mutation)
    failed = _tool_payload(
        await server.call_tool(
            "insert_paragraph",
            {
                "document_id": created["document_id"],
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "must remain isolated",
                "expected_version": 0,
            },
        )
    )

    assert failed["error"]["code"] == "OOXML_INVALID"
    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-failed-mutation.docx") == before
    transaction_root = (
        runtime.store.sessions[record.session_id].root / ".transactions" / record.document_id
    )
    assert not list(transaction_root.rglob("*.docx"))


@pytest.mark.asyncio
async def test_invalid_mutation_candidate_is_rejected_before_engine_swap(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-invalid-candidate.docx")

    def reject_candidate(_path: Path) -> dict:
        return {
            "valid": False,
            "errors": 2,
            "warnings": 1,
            "issues": [
                {"code": "XML_INVALID", "message": "sensitive candidate detail"},
                {"code": "REL_TARGET_MISSING", "message": "sensitive relationship"},
            ],
        }

    monkeypatch.setattr(record.engine.validator.__class__, "validate", lambda self, path: reject_candidate(path))
    failed = _tool_payload(
        await server.call_tool(
            "insert_paragraph",
            {
                "document_id": created["document_id"],
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "candidate rejected by validation",
                "expected_version": 0,
            },
        )
    )

    assert failed["error"] == {
        "code": "OOXML_INVALID",
        "message": "Mutation candidate failed structural validation",
        "details": {
            "errors": 2,
            "warnings": 1,
            "issue_codes": ["REL_TARGET_MISSING", "XML_INVALID"],
        },
        "retryable": False,
    }
    assert "sensitive" not in json.dumps(failed)
    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-invalid-candidate.docx") == before


@pytest.mark.asyncio
async def test_failed_save_discards_mutated_clone_and_keeps_original_draft(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-failed-save.docx")

    def fail_after_clone_mutation(self: WordDocumentEngine, _output: Path) -> dict:
        paragraph = next(self.doc._require("word/document.xml").iter(f"{W}p"))
        self.call(
            "insert_paragraph",
            paragraph.get(f"{W14}paraId", ""),
            "must never reach the active draft",
        )
        raise WordToolkitError(ErrorCode.OOXML_INVALID, "injected save failure")

    monkeypatch.setattr(WordDocumentEngine, "save_version", fail_after_clone_mutation)
    failed = _tool_payload(
        await server.call_tool(
            "save_document",
            {
                "document_id": created["document_id"],
                "file_name": "failed.docx",
                "expected_version": 0,
            },
        )
    )

    assert failed["error"]["code"] == "OOXML_INVALID"
    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-failed-save.docx") == before
    assert not runtime.store.artifacts
    session_root = runtime.store.sessions[record.session_id].root
    assert not list(session_root.rglob("v1-*.docx"))
    assert not list((session_root / ".transactions").rglob("*.docx"))


@pytest.mark.asyncio
async def test_failed_render_rolls_back_save_repairs_version_and_outputs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-failed-render.docx")

    def fail_render(_docx: Path, _pdf: Path) -> dict:
        raise WordToolkitError(ErrorCode.RENDERER_UNAVAILABLE, "injected renderer failure")

    monkeypatch.setattr(runtime.renderer, "to_pdf", fail_render)
    failed = _tool_payload(
        await server.call_tool(
            "render_document",
            {
                "document_id": created["document_id"],
                "file_name": "failed.pdf",
                "expected_version": 0,
            },
        )
    )

    assert failed["error"]["code"] == "RENDERER_UNAVAILABLE"
    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-failed-render.docx") == before
    assert not runtime.store.artifacts
    session_root = runtime.store.sessions[record.session_id].root
    assert not (session_root / "renders" / record.document_id / "v1").exists()
    assert not list((session_root / ".transactions").rglob("*.docx"))


@pytest.mark.asyncio
async def test_cancelled_fork_is_drained_and_removed_before_document_unlock(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    original_fork = WordDocumentEngine.fork
    started = threading.Event()
    release = threading.Event()
    transaction_dirs: list[Path] = []

    def blocked_fork(self: WordDocumentEngine, checkpoint: Path) -> WordDocumentEngine:
        transaction_dirs.append(checkpoint.parent)
        started.set()
        if not release.wait(timeout=5):
            raise TimeoutError("test did not release the fork worker")
        return original_fork(self, checkpoint)

    monkeypatch.setattr(WordDocumentEngine, "fork", blocked_fork)
    publish = asyncio.create_task(
        server.call_tool(
            "save_document",
            {
                "document_id": created["document_id"],
                "file_name": "cancelled.docx",
                "expected_version": 0,
            },
        )
    )
    assert await asyncio.to_thread(started.wait, 2)
    publish.cancel()
    await asyncio.sleep(0.05)
    assert not publish.done(), "document lock escaped while the fork worker was still active"
    release.set()
    with pytest.raises(asyncio.CancelledError):
        await publish

    assert transaction_dirs
    assert not transaction_dirs[0].exists()
    assert record.engine is original_engine
    assert record.version == 0
    assert not runtime.store.artifacts


@pytest.mark.asyncio
async def test_repeated_cancellation_cannot_interrupt_publish_cleanup(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    original_save = WordDocumentEngine.save_version
    original_close = WordDocumentEngine.close
    save_started = threading.Event()
    release_save = threading.Event()
    close_started = threading.Event()
    release_close = threading.Event()

    def blocked_save(self: WordDocumentEngine, output: Path) -> dict:
        save_started.set()
        if not release_save.wait(timeout=5):
            raise TimeoutError("test did not release save_version")
        return original_save(self, output)

    def blocked_clone_close(self: WordDocumentEngine) -> None:
        if self is not original_engine:
            close_started.set()
            if not release_close.wait(timeout=5):
                raise TimeoutError("test did not release clone.close")
        original_close(self)

    monkeypatch.setattr(WordDocumentEngine, "save_version", blocked_save)
    monkeypatch.setattr(WordDocumentEngine, "close", blocked_clone_close)
    publish = asyncio.create_task(
        server.call_tool(
            "save_document",
            {
                "document_id": created["document_id"],
                "file_name": "double-cancel.docx",
                "expected_version": 0,
            },
        )
    )
    assert await asyncio.to_thread(save_started.wait, 2)
    publish.cancel()
    release_save.set()
    assert await asyncio.to_thread(close_started.wait, 2)
    publish.cancel()
    await asyncio.sleep(0.05)
    assert not publish.done(), "second cancellation escaped before clone.close completed"
    release_close.set()
    with pytest.raises(asyncio.CancelledError):
        await publish

    session_root = runtime.store.sessions[record.session_id].root
    assert not list(session_root.rglob("v1-double-cancel.docx"))
    assert not list((session_root / ".transactions").rglob("*.docx"))
    assert record.engine is original_engine
    assert record.version == 0
    assert not runtime.store.artifacts


@pytest.mark.asyncio
async def test_cancelled_mutation_keeps_lock_and_advances_version_after_worker_success(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_call = WordDocumentEngine.call
    started = threading.Event()
    release = threading.Event()

    def blocked_call(self: WordDocumentEngine, name: str, *args: Any, **kwargs: Any):
        if name == "insert_paragraph":
            started.set()
            if not release.wait(timeout=5):
                raise TimeoutError("test did not release the mutation worker")
        return original_call(self, name, *args, **kwargs)

    monkeypatch.setattr(WordDocumentEngine, "call", blocked_call)
    mutation = asyncio.create_task(
        server.call_tool(
            "insert_paragraph",
            {
                "document_id": created["document_id"],
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": "completed after caller cancellation",
                "expected_version": 0,
            },
        )
    )
    assert await asyncio.to_thread(started.wait, 2)
    mutation.cancel()
    await asyncio.sleep(0.05)
    assert record.lock.locked(), "cancelled mutation released the document lock early"
    mutation.cancel()
    await asyncio.sleep(0.05)
    assert record.lock.locked(), "second cancellation escaped before the worker completed"
    release.set()
    with pytest.raises(asyncio.CancelledError):
        await mutation

    assert record.version == 1
    markdown = _tool_payload(
        await server.call_tool(
            "export_document",
            {
                "document_id": created["document_id"],
                "output_format": "markdown",
                "file_name": "cancelled-mutation.md",
            },
        )
    )
    artifact = runtime.store.artifacts[markdown["data"]["artifact"]["artifact_id"]]
    assert "completed after caller cancellation" in artifact.path.read_text(encoding="utf-8")
    stale = _tool_payload(
        await server.call_tool(
            "save_document",
            {
                "document_id": created["document_id"],
                "file_name": "stale-after-cancel.docx",
                "expected_version": 0,
            },
        )
    )
    assert stale["error"]["code"] == "VERSION_CONFLICT"


class _FakeEngine:
    def __init__(self) -> None:
        self.closed = False

    def close(self) -> None:
        self.closed = True


@pytest.mark.asyncio
async def test_close_rejects_stale_version_and_invalidates_pre_resolved_waiter(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    settings = Settings(auth_mode="local_stdio", storage_root=tmp_path / "storage")
    store = SessionStore(settings)
    subject = "close-race"
    session = await store.create_session(subject)
    source = session.root / "document.docx"
    source.write_bytes(b"draft")
    engine = _FakeEngine()
    record = await store.add_document(subject, session.session_id, source, engine, source.name)
    record.version = 1

    with pytest.raises(WordToolkitError) as stale:
        await store.close_document(subject, record.document_id, 0)
    assert stale.value.code == ErrorCode.VERSION_CONFLICT
    assert store.documents[record.document_id] is record
    assert not record.closed
    assert not engine.closed

    resolved = asyncio.Event()
    release = asyncio.Event()
    original_get_document = store.get_document

    async def gated_get_document(request_subject: str, document_id: str):
        result = await original_get_document(request_subject, document_id)
        if asyncio.current_task() and asyncio.current_task().get_name() == "pre-resolved-waiter":
            resolved.set()
            await release.wait()
        return result

    monkeypatch.setattr(store, "get_document", gated_get_document)

    async def wait_for_lock() -> WordToolkitError | None:
        try:
            async with store.locked_document(subject, record.document_id):
                return None
        except WordToolkitError as exc:
            return exc

    waiter = asyncio.create_task(wait_for_lock(), name="pre-resolved-waiter")
    await resolved.wait()
    await store.close_document(subject, record.document_id, 1)
    release.set()
    waiter_error = await waiter

    assert waiter_error is not None
    assert waiter_error.code == ErrorCode.DOCUMENT_NOT_FOUND
    assert record.closed
    assert engine.closed
    assert record.document_id not in store.documents


@pytest.mark.asyncio
async def test_multi_artifact_registration_is_all_or_nothing(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    settings = Settings(auth_mode="local_stdio", storage_root=tmp_path / "storage")
    store = SessionStore(settings)
    subject = "artifact-transaction"
    session = await store.create_session(subject)
    first = session.root / "first.bin"
    second = session.root / "second.bin"
    first.write_bytes(b"first")
    second.write_bytes(b"second")

    from wordtoolkit import sessions as sessions_module

    original_copy = sessions_module.shutil.copy2
    calls = 0

    def fail_second_copy(source: Path, target: Path):
        nonlocal calls
        calls += 1
        if calls == 2:
            Path(target).write_bytes(b"partial")
            raise OSError("injected copy failure")
        return original_copy(source, target)

    monkeypatch.setattr(sessions_module.shutil, "copy2", fail_second_copy)
    with pytest.raises(OSError, match="injected copy failure"):
        await store.register_artifacts(
            subject,
            [
                (first, "application/octet-stream", "first.bin"),
                (second, "application/octet-stream", "second.bin"),
            ],
        )

    assert not store.artifacts
    assert not list(store.root.rglob("art_*-*.bin"))
