from __future__ import annotations

import asyncio
import json
import threading
import zipfile
from pathlib import Path
from typing import Any

import pytest
from mcp.server.fastmcp import FastMCP
from mcp.server.fastmcp.exceptions import ToolError
from PIL import Image

from wordtoolkit.config import Settings
from wordtoolkit.draft_operations import compact_batch_result
from wordtoolkit.engine import WordDocumentEngine
from wordtoolkit.engine.validator import package_hashes
from wordtoolkit.runtime import ToolRuntime
from wordtoolkit.server import tools as tools_module
from wordtoolkit.server.tools import register_tools


def _payload(result: Any) -> dict[str, Any]:
    structured = getattr(result, "structuredContent", None)
    if isinstance(structured, dict):
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
    server = FastMCP("WordToolkit batch test")
    register_tools(server, runtime)
    return server, runtime


async def _create_document(server: FastMCP) -> dict[str, Any]:
    return _payload(await server.call_tool("create_document", {}))["data"]


def _snapshot_hashes(engine: WordDocumentEngine, path: Path) -> dict[str, str]:
    engine.snapshot(path)
    return package_hashes(path)


def test_compact_batch_result_omits_content_and_unbounded_identifiers() -> None:
    assert compact_batch_result(
        {
            "paragraph_id": "1A2B3C4D",
            "text": "document content",
            "unsafe_id": "x" * 257,
            "changed_count": 3,
            "paragraph_ids": ["A", "B"],
        }
    ) == {
        "paragraph_id": "1A2B3C4D",
        "changed_count": 3,
        "paragraph_ids": ["A", "B"],
    }


@pytest.mark.asyncio
async def test_heterogeneous_batch_uses_one_fork_validation_and_version(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    document_id = created["document_id"]
    anchor_id = created["anchor_paragraph_id"]
    record = runtime.store.documents[document_id]
    original_engine = record.engine
    original_fork = WordDocumentEngine.fork
    validator_type = record.engine.validator.__class__
    original_validate = validator_type.validate
    fork_count = 0
    validation_count = 0

    def counted_fork(self: WordDocumentEngine, checkpoint: Path) -> WordDocumentEngine:
        nonlocal fork_count
        fork_count += 1
        return original_fork(self, checkpoint)

    def counted_validate(self: Any, path: Path) -> dict[str, Any]:
        nonlocal validation_count
        validation_count += 1
        return original_validate(self, path)

    monkeypatch.setattr(WordDocumentEngine, "fork", counted_fork)
    monkeypatch.setattr(validator_type, "validate", counted_validate)
    secret_text = "batch content must not be echoed into the result"

    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": document_id,
                "expected_version": 0,
                "operations": [
                    {
                        "operation": "format_paragraph",
                        "arguments": {"paragraph_id": anchor_id, "alignment": "center"},
                    },
                    {
                        "operation": "insert_paragraph",
                        "arguments": {"after_paragraph_id": anchor_id, "text": secret_text},
                    },
                    {
                        "operation": "enable_track_changes",
                        "arguments": {"enabled": True, "author": "Batch test"},
                    },
                ],
            },
        )
    )

    assert response["ok"] is True
    data = response["data"]
    assert data["draft_version"] == 1
    assert set(data) == {"document_id", "draft_version", "results"}
    assert [item["index"] for item in data["results"]] == [0, 1, 2]
    assert all(set(item) == {"index", "result"} for item in data["results"])
    assert secret_text not in json.dumps(response)
    assert fork_count == 1
    assert validation_count == 1
    assert record.version == 1
    assert record.engine is not original_engine

    snapshot = tmp_path / "batch-success.docx"
    record.engine.snapshot(snapshot)
    with zipfile.ZipFile(snapshot) as archive:
        document_xml = archive.read("word/document.xml").decode("utf-8")
        settings_xml = archive.read("word/settings.xml").decode("utf-8")
    assert secret_text in document_xml
    assert 'w:val="center"' in document_xml
    assert "trackRevisions" in settings_xml


@pytest.mark.asyncio
async def test_middle_operation_failure_rolls_back_entire_batch(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    document_id = created["document_id"]
    record = runtime.store.documents[document_id]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-batch-failure.docx")

    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": document_id,
                "expected_version": 0,
                "operations": [
                    {
                        "operation": "insert_paragraph",
                        "arguments": {
                            "after_paragraph_id": created["anchor_paragraph_id"],
                            "text": "must be discarded",
                        },
                    },
                    {
                        "operation": "replace_paragraph",
                        "arguments": {
                            "paragraph_id": "FFFFFFFF",
                            "text": "cannot resolve",
                        },
                    },
                    {
                        "operation": "enable_track_changes",
                        "arguments": {"enabled": True},
                    },
                ],
            },
        )
    )

    assert response["error"]["details"]["phase"] == "operation"
    assert response["error"]["details"].get("operation_index") == 1, response
    assert response["error"]["details"]["operation"] == "replace_paragraph"
    assert response["error"]["details"]["cause"]["code"] in {
        "DOCUMENT_NOT_FOUND",
        "INVALID_INPUT",
    }
    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-batch-failure.docx") == before


@pytest.mark.asyncio
async def test_nested_failure_does_not_echo_exception_or_document_content(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-redacted-failure.docx")
    original_call = WordDocumentEngine.call
    secret = "private document text from a broken dependency"

    def fail_with_secret(self: WordDocumentEngine, name: str, *args: Any, **kwargs: Any):
        result = original_call(self, name, *args, **kwargs)
        if name == "insert_paragraph":
            raise ValueError(secret)
        return result

    monkeypatch.setattr(WordDocumentEngine, "call", fail_with_secret)
    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "operations": [
                    {
                        "operation": "insert_paragraph",
                        "arguments": {
                            "after_paragraph_id": created["anchor_paragraph_id"],
                            "text": "batch input must also stay out of the error",
                        },
                    }
                ],
            },
        )
    )

    serialized = json.dumps(response)
    assert response["error"]["details"]["operation_index"] == 0
    assert response["error"]["details"]["cause"]["message"] == (
        "The nested document operation failed"
    )
    assert secret not in serialized
    assert "batch input must also stay out of the error" not in serialized
    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-redacted-failure.docx") == before


@pytest.mark.asyncio
async def test_preflight_rejections_never_fork(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_fork = WordDocumentEngine.fork
    fork_count = 0

    def counted_fork(self: WordDocumentEngine, checkpoint: Path) -> WordDocumentEngine:
        nonlocal fork_count
        fork_count += 1
        return original_fork(self, checkpoint)

    monkeypatch.setattr(WordDocumentEngine, "fork", counted_fork)
    base = {
        "document_id": created["document_id"],
        "expected_version": 0,
    }
    cases = [
        [
            {
                "operation": "manage_lists",
                "arguments": {"action": "list"},
            }
        ],
        [
            {
                "operation": "insert_paragraph",
                "arguments": {
                    "after_paragraph_id": created["anchor_paragraph_id"],
                    "text": "x",
                    "document_id": created["document_id"],
                },
            }
        ],
        [
            {
                "operation": "insert_paragraph",
                "arguments": {
                    "after_paragraph_id": created["anchor_paragraph_id"],
                    "text": "x",
                    "unknown": True,
                },
            }
        ],
        [
            {
                "operation": "not_a_tool",
                "arguments": {},
            }
        ],
    ]
    for operations in cases:
        response = _payload(
            await server.call_tool(
                "apply_document_operations",
                {**base, "operations": operations},
            )
        )
        assert response["error"]["code"] == "INVALID_INPUT"

    stale = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                **base,
                "expected_version": 1,
                "operations": [
                    {
                        "operation": "enable_track_changes",
                        "arguments": {"enabled": True},
                    }
                ],
            },
        )
    )
    assert stale["error"]["code"] == "VERSION_CONFLICT"
    for invalid_version in (None, False, 0.0, "0"):
        arguments: dict[str, Any] = {
            "document_id": created["document_id"],
            "operations": [
                {
                    "operation": "enable_track_changes",
                    "arguments": {"enabled": True},
                }
            ],
        }
        if invalid_version is not None:
            arguments["expected_version"] = invalid_version
        invalid = _payload(await server.call_tool("apply_document_operations", arguments))
        assert invalid["error"]["code"] == "INVALID_INPUT"

    seventeen_operations = [
        {
            "operation": "enable_track_changes",
            "arguments": {"enabled": True},
        }
    ] * 17
    for invalid_operations in ([], seventeen_operations):
        with pytest.raises(ToolError, match="operations"):
            await server.call_tool(
                "apply_document_operations",
                {**base, "operations": invalid_operations},
            )
    assert len(seventeen_operations) == 17
    assert fork_count == 0
    assert record.version == 0


@pytest.mark.asyncio
async def test_aggregate_argument_limit_is_enforced_before_fork(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_fork = WordDocumentEngine.fork
    fork_count = 0

    def counted_fork(self: WordDocumentEngine, checkpoint: Path) -> WordDocumentEngine:
        nonlocal fork_count
        fork_count += 1
        return original_fork(self, checkpoint)

    monkeypatch.setattr(WordDocumentEngine, "fork", counted_fork)
    operations = [
        {
            "operation": "insert_paragraph",
            "arguments": {
                "after_paragraph_id": created["anchor_paragraph_id"],
                "text": str(index) + ("x" * 179_999),
            },
        }
        for index in range(6)
    ]
    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "operations": operations,
            },
        )
    )

    assert response["error"]["code"] == "LIMIT_EXCEEDED"
    assert response["error"]["details"]["actual_bytes"] > 1_048_576
    assert fork_count == 0
    assert record.version == 0


@pytest.mark.asyncio
async def test_image_stage_is_removed_and_document_remains_atomic_on_later_failure(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    document_id = created["document_id"]
    record = runtime.store.documents[document_id]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-image-batch.docx")
    image_path = tmp_path / "pixel.png"
    Image.new("RGB", (2, 2), (20, 40, 60)).save(image_path)

    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": document_id,
                "expected_version": 0,
                "files": [
                    {
                        "download_url": image_path.resolve().as_uri(),
                        "file_id": "file_test_pixel",
                        "mime_type": "image/png",
                        "file_name": "pixel.png",
                    }
                ],
                "operations": [
                    {
                        "operation": "insert_image",
                        "arguments": {
                            "paragraph_id": created["anchor_paragraph_id"],
                            "file_index": 0,
                            "width_mm": 10,
                            "height_mm": 10,
                        },
                    },
                    {
                        "operation": "delete_paragraph",
                        "arguments": {"paragraph_id": "FFFFFFFF"},
                    },
                ],
            },
        )
    )

    assert response["error"]["details"].get("operation_index") == 1, response
    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-image-batch.docx") == before
    uploads = list((runtime.store.sessions[record.session_id].root / "uploads").glob("*.png"))
    assert uploads == []


@pytest.mark.asyncio
async def test_image_stage_is_removed_after_successful_batch(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    image_path = tmp_path / "success.png"
    Image.new("RGB", (2, 2), (20, 40, 60)).save(image_path)

    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "files": [
                    {
                        "download_url": image_path.resolve().as_uri(),
                        "file_id": "file_success_pixel",
                        "mime_type": "image/png",
                        "file_name": "success.png",
                    }
                ],
                "operations": [
                    {
                        "operation": "insert_image",
                        "arguments": {
                            "paragraph_id": created["anchor_paragraph_id"],
                            "file_index": 0,
                            "width_mm": 10,
                            "height_mm": 10,
                        },
                    }
                ],
            },
        )
    )

    assert response["ok"] is True
    assert record.version == 1
    uploads = list((runtime.store.sessions[record.session_id].root / "uploads").glob("*.png"))
    assert uploads == []


@pytest.mark.asyncio
async def test_image_stage_is_removed_after_successful_standalone_insert(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    image_path = tmp_path / "standalone.png"
    Image.new("RGB", (2, 2), (60, 40, 20)).save(image_path)

    response = _payload(
        await server.call_tool(
            "insert_image",
            {
                "document_id": created["document_id"],
                "paragraph_id": created["anchor_paragraph_id"],
                "file": {
                    "download_url": image_path.resolve().as_uri(),
                    "file_id": "file_standalone_pixel",
                    "mime_type": "image/png",
                    "file_name": "standalone.png",
                },
                "expected_version": 0,
            },
        )
    )

    assert response["ok"] is True
    assert response["warnings"] == []
    assert record.version == 1
    uploads = list((runtime.store.sessions[record.session_id].root / "uploads").glob("*.png"))
    assert uploads == []


@pytest.mark.asyncio
async def test_image_cleanup_failure_warns_without_masking_successful_commit(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    image_path = tmp_path / "cleanup-warning.png"
    Image.new("RGB", (2, 2), (20, 60, 40)).save(image_path)

    def fail_cleanup(_session: Any, _path: Path) -> bool:
        raise OSError("simulated cleanup failure")

    monkeypatch.setattr(tools_module, "_discard_session_upload", fail_cleanup)
    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "files": [
                    {
                        "download_url": image_path.resolve().as_uri(),
                        "file_id": "file_cleanup_warning",
                        "mime_type": "image/png",
                        "file_name": "cleanup-warning.png",
                    }
                ],
                "operations": [
                    {
                        "operation": "insert_image",
                        "arguments": {
                            "paragraph_id": created["anchor_paragraph_id"],
                            "file_index": 0,
                        },
                    }
                ],
            },
        )
    )

    assert response["ok"] is True
    assert response["warnings"] == [tools_module.UPLOAD_CLEANUP_WARNING]
    assert record.version == 1


@pytest.mark.asyncio
async def test_image_cleanup_failure_never_masks_the_original_batch_error(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    image_path = tmp_path / "cleanup-error.png"
    Image.new("RGB", (2, 2), (40, 20, 60)).save(image_path)

    def fail_cleanup(_session: Any, _path: Path) -> bool:
        raise OSError("simulated cleanup failure")

    monkeypatch.setattr(tools_module, "_discard_session_upload", fail_cleanup)
    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "files": [
                    {
                        "download_url": image_path.resolve().as_uri(),
                        "file_id": "file_cleanup_error",
                        "mime_type": "image/png",
                        "file_name": "cleanup-error.png",
                    }
                ],
                "operations": [
                    {
                        "operation": "insert_image",
                        "arguments": {
                            "paragraph_id": created["anchor_paragraph_id"],
                            "file_index": 0,
                        },
                    },
                    {
                        "operation": "delete_paragraph",
                        "arguments": {"paragraph_id": "FFFFFFFF"},
                    },
                ],
            },
        )
    )

    assert response["error"]["details"]["operation_index"] == 1
    assert record.version == 0
    assert record.engine is original_engine


@pytest.mark.asyncio
async def test_invalid_image_stage_is_removed_before_batch_fork(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    invalid_path = tmp_path / "invalid.png"
    invalid_path.write_bytes(b"not-an-image")

    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "files": [
                    {
                        "download_url": invalid_path.resolve().as_uri(),
                        "file_id": "file_invalid_pixel",
                        "mime_type": "image/png",
                        "file_name": "invalid.png",
                    }
                ],
                "operations": [
                    {
                        "operation": "insert_image",
                        "arguments": {
                            "paragraph_id": created["anchor_paragraph_id"],
                            "file_index": 0,
                        },
                    }
                ],
            },
        )
    )

    assert response["error"]["code"] == "INVALID_INPUT"
    assert record.version == 0
    uploads = list((runtime.store.sessions[record.session_id].root / "uploads").glob("*.png"))
    assert uploads == []


@pytest.mark.asyncio
async def test_batch_rejects_missing_and_unused_top_level_files_before_fork(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_fork = WordDocumentEngine.fork
    fork_count = 0

    def counted_fork(self: WordDocumentEngine, checkpoint: Path) -> WordDocumentEngine:
        nonlocal fork_count
        fork_count += 1
        return original_fork(self, checkpoint)

    monkeypatch.setattr(WordDocumentEngine, "fork", counted_fork)
    file_reference = {
        "download_url": "https://files.example.test/pixel.png",
        "file_id": "file_pixel",
        "mime_type": "image/png",
        "file_name": "pixel.png",
    }
    missing = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "operations": [
                    {
                        "operation": "insert_image",
                        "arguments": {
                            "paragraph_id": created["anchor_paragraph_id"],
                            "file_index": 0,
                        },
                    }
                ],
            },
        )
    )
    unused = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "files": [file_reference],
                "operations": [
                    {
                        "operation": "enable_track_changes",
                        "arguments": {"enabled": True},
                    }
                ],
            },
        )
    )

    assert missing["error"]["code"] == "INVALID_INPUT"
    assert missing["error"]["details"]["file_count"] == 0
    assert unused["error"]["code"] == "INVALID_INPUT"
    assert unused["error"]["details"]["unused_file_count"] == 1
    assert fork_count == 0
    assert record.version == 0


@pytest.mark.asyncio
async def test_cancel_during_image_staging_is_drained_before_commit(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    image_path = tmp_path / "staged.png"
    Image.new("RGB", (2, 2), (1, 2, 3)).save(image_path)
    started = asyncio.Event()
    release = asyncio.Event()

    async def blocked_stage(_runtime: Any, _session: Any, _file: Any) -> Path:
        started.set()
        await release.wait()
        return image_path

    monkeypatch.setattr(tools_module, "_download_verified_image", blocked_stage)
    mutation = asyncio.create_task(
        server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "files": [
                    {
                        "download_url": "https://files.example.test/staged.png",
                        "file_id": "file_staged",
                        "mime_type": "image/png",
                        "file_name": "staged.png",
                    }
                ],
                "operations": [
                    {
                        "operation": "insert_image",
                        "arguments": {
                            "paragraph_id": created["anchor_paragraph_id"],
                            "file_index": 0,
                            "width_mm": 10,
                            "height_mm": 10,
                        },
                    }
                ],
            },
        )
    )
    await asyncio.wait_for(started.wait(), timeout=2)
    mutation.cancel()
    await asyncio.sleep(0.05)
    assert not mutation.done()
    release.set()
    with pytest.raises(asyncio.CancelledError):
        await mutation

    assert record.version == 1
    snapshot = tmp_path / "cancelled-staging.docx"
    record.engine.snapshot(snapshot)
    with zipfile.ZipFile(snapshot) as archive:
        assert any(name.startswith("word/media/") for name in archive.namelist())


@pytest.mark.asyncio
async def test_cancelled_batch_drains_success_and_commits_once(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_call = WordDocumentEngine.call
    started = threading.Event()
    release = threading.Event()

    def blocked_call(self: WordDocumentEngine, name: str, *args: Any, **kwargs: Any):
        if name == "set_track_changes":
            started.set()
            if not release.wait(timeout=5):
                raise TimeoutError("test did not release the batch worker")
        return original_call(self, name, *args, **kwargs)

    monkeypatch.setattr(WordDocumentEngine, "call", blocked_call)
    mutation = asyncio.create_task(
        server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "operations": [
                    {
                        "operation": "insert_paragraph",
                        "arguments": {
                            "after_paragraph_id": created["anchor_paragraph_id"],
                            "text": "committed batch after cancellation",
                        },
                    },
                    {
                        "operation": "enable_track_changes",
                        "arguments": {"enabled": True},
                    },
                ],
            },
        )
    )
    assert await asyncio.to_thread(started.wait, 2)
    mutation.cancel()
    await asyncio.sleep(0.05)
    assert record.lock.locked()
    release.set()
    with pytest.raises(asyncio.CancelledError):
        await mutation

    assert record.version == 1
    snapshot = tmp_path / "cancelled-batch.docx"
    record.engine.snapshot(snapshot)
    with zipfile.ZipFile(snapshot) as archive:
        document_xml = archive.read("word/document.xml").decode("utf-8")
    assert "committed batch after cancellation" in document_xml


@pytest.mark.asyncio
async def test_cancelled_batch_failure_is_drained_and_discarded(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-cancelled-failure.docx")
    original_call = WordDocumentEngine.call
    started = threading.Event()
    release = threading.Event()

    def blocked_failure(self: WordDocumentEngine, name: str, *args: Any, **kwargs: Any):
        result = original_call(self, name, *args, **kwargs)
        if name == "insert_paragraph":
            started.set()
            if not release.wait(timeout=5):
                raise TimeoutError("test did not release the failing batch worker")
            raise ValueError("injected failure after clone mutation")
        return result

    monkeypatch.setattr(WordDocumentEngine, "call", blocked_failure)
    mutation = asyncio.create_task(
        server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "operations": [
                    {
                        "operation": "insert_paragraph",
                        "arguments": {
                            "after_paragraph_id": created["anchor_paragraph_id"],
                            "text": "must be discarded after cancellation",
                        },
                    }
                ],
            },
        )
    )
    assert await asyncio.to_thread(started.wait, 2)
    mutation.cancel()
    release.set()
    with pytest.raises(asyncio.CancelledError):
        await mutation

    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-cancelled-failure.docx") == before


@pytest.mark.asyncio
async def test_close_waits_for_batch_and_rechecks_committed_version(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_call = WordDocumentEngine.call
    started = threading.Event()
    release = threading.Event()

    def blocked_call(self: WordDocumentEngine, name: str, *args: Any, **kwargs: Any):
        if name == "set_track_changes":
            started.set()
            if not release.wait(timeout=5):
                raise TimeoutError("test did not release close-race batch")
        return original_call(self, name, *args, **kwargs)

    monkeypatch.setattr(WordDocumentEngine, "call", blocked_call)
    batch = asyncio.create_task(
        server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "operations": [
                    {
                        "operation": "enable_track_changes",
                        "arguments": {"enabled": True},
                    }
                ],
            },
        )
    )
    assert await asyncio.to_thread(started.wait, 2)
    close = asyncio.create_task(
        server.call_tool(
            "close_document",
            {"document_id": created["document_id"], "expected_version": 0},
        )
    )
    await asyncio.sleep(0.05)
    assert not close.done()
    release.set()

    batch_result = _payload(await batch)
    close_result = _payload(await close)
    assert batch_result["data"]["draft_version"] == 1
    assert close_result["error"]["code"] == "VERSION_CONFLICT"
    assert record.version == 1
    assert record.closed is False


@pytest.mark.asyncio
async def test_final_candidate_rejection_has_phase_and_rolls_back(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    server, runtime = _build_server(tmp_path, monkeypatch)
    created = await _create_document(server)
    record = runtime.store.documents[created["document_id"]]
    original_engine = record.engine
    before = _snapshot_hashes(original_engine, tmp_path / "before-rejected-batch.docx")

    def reject_candidate(_self: Any, _path: Path) -> dict[str, Any]:
        return {
            "valid": False,
            "errors": 1,
            "warnings": 0,
            "issues": [{"code": "XML_INVALID", "message": "do not leak this"}],
        }

    monkeypatch.setattr(record.engine.validator.__class__, "validate", reject_candidate)
    response = _payload(
        await server.call_tool(
            "apply_document_operations",
            {
                "document_id": created["document_id"],
                "expected_version": 0,
                "operations": [
                    {
                        "operation": "enable_track_changes",
                        "arguments": {"enabled": True},
                    }
                ],
            },
        )
    )

    assert response["error"]["code"] == "OOXML_INVALID"
    assert response["error"]["details"]["phase"] == "candidate_validation"
    assert "operation_index" not in response["error"]["details"]
    assert "do not leak" not in json.dumps(response)
    assert record.version == 0
    assert record.engine is original_engine
    assert _snapshot_hashes(record.engine, tmp_path / "after-rejected-batch.docx") == before
