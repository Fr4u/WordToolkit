from __future__ import annotations

import asyncio
import contextlib
import functools
import json
import shutil
from collections.abc import Callable
from pathlib import Path
from typing import Annotated, Any, Literal

from mcp.server.fastmcp import FastMCP
from mcp.types import CallToolResult, TextContent, ToolAnnotations
from pydantic import BaseModel, ConfigDict, Field, WithJsonSchema, model_validator

from docx_mcp.document import DocxDocument
from docx_mcp.document.errors import DocxMcpError, ErrCode
from docx_mcp.markdown import MarkdownConverter

from ..auth import current_subject, require_scope
from ..engine import WordDocumentEngine
from ..errors import ErrorCode, WordToolkitError, ok
from ..ids import opaque_id
from ..runtime import ToolRuntime, clean_filename


class OpenAIFile(BaseModel):
    model_config = ConfigDict(extra="forbid")

    download_url: str = ""
    local_path: str = ""
    file_id: str = ""
    mime_type: str | None = None
    file_name: str | None = None

    @model_validator(mode="after")
    def validate_reference(self) -> OpenAIFile:
        if bool(self.download_url) == bool(self.local_path):
            raise ValueError("Provide exactly one of download_url or local_path")
        return self


class TabStop(BaseModel):
    model_config = ConfigDict(extra="forbid")

    position_mm: float = Field(ge=0, le=500)
    alignment: Literal["left", "center", "right", "decimal", "bar"] = "left"
    leader: Literal["none", "dot", "hyphen", "underscore", "heavy", "middleDot"] = "none"


class ListLevel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    num_fmt: Literal["decimal", "lowerLetter", "upperLetter", "lowerRoman", "upperRoman", "bullet"]
    lvl_text: str = Field(min_length=1, max_length=64)
    indent: int = Field(default=720, ge=0, le=20_000)
    hanging: int = Field(default=360, ge=0, le=10_000)
    style: str | None = Field(default=None, max_length=128)


READ = ToolAnnotations(
    readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False
)
WRITE = ToolAnnotations(
    readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False
)
DELETE = ToolAnnotations(
    readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=False
)
EXPORT = ToolAnnotations(
    readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False
)
FILE_META = {"openai/fileParams": ["file"]}
type DRAFT_VERSION = Annotated[
    Any,
    WithJsonSchema({"type": "integer", "minimum": 0, "title": "Expected Version"}),
]
DRAFT_VERSION_REQUIRED_TOOLS = {
    "insert_paragraph",
    "replace_paragraph",
    "delete_paragraph",
    "move_block",
    "create_style",
    "update_style",
    "apply_style",
    "normalize_formatting",
    "format_paragraph",
    "format_run",
    "manage_lists",
    "insert_caption",
    "insert_table",
    "modify_table",
    "merge_cells",
    "split_cells",
    "set_cell_properties",
    "insert_equation",
    "replace_equation",
    "number_equations",
    "add_equation_reference",
    "manage_headers_footers",
    "manage_footnotes_endnotes",
    "manage_comments",
    "manage_bookmarks",
    "manage_cross_references",
    "manage_fields",
    "insert_image",
    "manage_sections",
    "enable_track_changes",
    "insert_tracked_change",
    "accept_changes",
    "reject_changes",
    "save_document",
    "close_document",
    "repair_document",
    "render_document",
    "render_pages",
    "convert_to_pdf",
    "generate_preview",
}


def _safe(function):
    @functools.wraps(function)
    async def wrapper(*args, **kwargs):
        try:
            return await function(*args, **kwargs)
        except WordToolkitError as exc:
            payload = exc.to_dict()
        except DocxMcpError as exc:
            not_found = {
                ErrCode.STYLE_NOT_FOUND,
                ErrCode.PARA_NOT_FOUND,
                ErrCode.BOOKMARK_NOT_FOUND,
                ErrCode.PART_NOT_FOUND,
                ErrCode.NO_OPEN_DOCUMENT,
            }
            code = (
                ErrorCode.DOCUMENT_NOT_FOUND if exc.code in not_found else ErrorCode.INVALID_INPUT
            )
            payload = WordToolkitError(
                code,
                "The document operation rejected the requested target or structure",
                {"document_error": exc.code.value, "hint": exc.hint},
            ).to_dict()
        except (ValueError, IndexError, KeyError, TypeError) as exc:
            payload = WordToolkitError(
                ErrorCode.INVALID_INPUT, str(exc), {"exception": type(exc).__name__}
            ).to_dict()
        except (
            Exception
        ) as exc:  # Boundary: never leak content, paths, or a traceback to the client.
            payload = WordToolkitError(
                ErrorCode.INTERNAL_ERROR,
                "The operation failed inside WordToolkit",
                {"exception": type(exc).__name__},
                retryable=False,
            ).to_dict()
        return CallToolResult(
            isError=True,
            content=[TextContent(type="text", text=json.dumps(payload, ensure_ascii=False))],
            structuredContent=payload,
        )

    return wrapper


def _public(value: Any) -> Any:
    if isinstance(value, dict):
        return {
            key: _public(item)
            for key, item in value.items()
            if key not in {"path", "pdf", "backup", "pdf_path", "copied_to", "output_path"}
        }
    if isinstance(value, list):
        return [_public(item) for item in value]
    return value


async def _drain_worker(worker: asyncio.Task[Any]) -> Any:
    while True:
        try:
            return await asyncio.shield(worker)
        except asyncio.CancelledError:
            if worker.done():
                return worker.result()


async def _run_locked_worker(function: Callable[..., Any], *args: Any, **kwargs: Any) -> Any:
    """Keep a document lock owned until its background engine call has actually stopped."""
    worker = asyncio.create_task(asyncio.to_thread(function, *args, **kwargs))
    try:
        return await asyncio.shield(worker)
    except asyncio.CancelledError as cancellation:
        with contextlib.suppress(BaseException):
            await _drain_worker(worker)
        raise cancellation


def _prepare_mutation_candidate(
    source: WordDocumentEngine,
    operation: Callable[[Any], Any],
    checkpoint: Path,
    candidate_path: Path,
) -> tuple[WordDocumentEngine, Any]:
    """Build and validate one mutation without exposing partial engine state."""
    clone: WordDocumentEngine | None = None
    try:
        clone = source.fork(checkpoint)
        result = operation(clone)
        clone.snapshot(candidate_path)
        validation = clone.validator.validate(candidate_path)
        if not validation["valid"]:
            issue_codes = sorted(
                {
                    str(issue.get("code", "VALIDATION_ERROR"))
                    for issue in validation.get("issues", [])
                    if isinstance(issue, dict)
                }
            )[:20]
            raise WordToolkitError(
                ErrorCode.OOXML_INVALID,
                "Mutation candidate failed structural validation",
                {
                    "errors": int(validation.get("errors", 0)),
                    "warnings": int(validation.get("warnings", 0)),
                    "issue_codes": issue_codes,
                },
            )
        package = validation.get("package")
        if isinstance(package, dict):
            clone.inspection = package
        return clone, result
    except BaseException:
        if clone is not None:
            with contextlib.suppress(Exception):
                clone.close()
        raise


def _close_engine_safely(engine: WordDocumentEngine) -> None:
    with contextlib.suppress(Exception):
        engine.close()


def _commit_mutation_candidate(
    record: Any, clone: WordDocumentEngine
) -> tuple[int, WordDocumentEngine]:
    previous = record.engine
    clone.path = record.current_path.resolve()
    record.engine = clone
    record.version += 1
    return record.version, previous


async def _mutate(
    runtime: ToolRuntime,
    document_id: str,
    expected_version: int | None,
    operation: Callable[[Any], Any],
) -> dict:
    subject = current_subject()
    require_scope("documents:write")
    async with runtime.store.locked_document(subject, document_id) as record:
        runtime.store.require_version(record, expected_version)
        session = runtime.store.sessions.get(record.session_id)
        if session is None or session.closed:
            raise WordToolkitError(ErrorCode.SESSION_NOT_FOUND, "Session was not found")
        transaction_dir = session.root / ".transactions" / record.document_id / opaque_id("txn")
        cleanup_required = True
        checkpoint = transaction_dir / "before.docx"
        candidate_path = transaction_dir / "candidate.docx"
        worker = asyncio.create_task(
            asyncio.to_thread(
                _prepare_mutation_candidate,
                record.engine,
                operation,
                checkpoint,
                candidate_path,
            )
        )
        cancellation: asyncio.CancelledError | None = None
        clone: WordDocumentEngine | None = None
        try:
            try:
                clone, result = await asyncio.shield(worker)
            except asyncio.CancelledError as exc:
                cancellation = exc
                try:
                    clone, result = await _drain_worker(worker)
                except BaseException:
                    raise cancellation from None

            assert clone is not None
            version, previous = _commit_mutation_candidate(record, clone)
            clone = None
            cleanup_required = False
            try:
                await _run_locked_worker(_close_engine_safely, previous)
            except asyncio.CancelledError as exc:
                cancellation = cancellation or exc
            if cancellation is not None:
                raise cancellation
            return ok(
                {
                    "document_id": document_id,
                    "draft_version": version,
                    "result": _public(result),
                }
            )
        finally:
            if clone is not None:
                with contextlib.suppress(Exception):
                    clone.close()
            if cleanup_required:
                cleanup = asyncio.create_task(
                    asyncio.to_thread(shutil.rmtree, transaction_dir, True)
                )
                with contextlib.suppress(BaseException):
                    await _drain_worker(cleanup)


async def _read(runtime: ToolRuntime, document_id: str, operation: Callable[[Any], Any]) -> dict:
    subject = current_subject()
    require_scope("documents:read")
    async with runtime.store.locked_document(subject, document_id) as record:
        result = await _run_locked_worker(operation, record.engine)
        return ok(
            {
                "document_id": document_id,
                "draft_version": record.version,
                "result": _public(result),
            }
        )


async def _read_at_version(
    runtime: ToolRuntime,
    document_id: str,
    expected_version: int | None,
    operation: Callable[[Any], Any],
) -> dict:
    subject = current_subject()
    require_scope("documents:read")
    async with runtime.store.locked_document(subject, document_id) as record:
        runtime.store.require_version(record, expected_version)
        result = await _run_locked_worker(operation, record.engine)
        return ok(
            {
                "document_id": document_id,
                "draft_version": record.version,
                "result": _public(result),
            }
        )


def _first_paragraph_id(engine: WordDocumentEngine) -> str:
    from docx_mcp.document.base import W14, W

    paragraph = next(engine.doc._require("word/document.xml").iter(f"{W}p"), None)
    if paragraph is None:
        raise WordToolkitError(ErrorCode.OOXML_INVALID, "Document has no paragraph anchor")
    return paragraph.get(f"{W14}paraId", "")


def _register_document(
    runtime: ToolRuntime,
    subject: str,
    session,
    engine: WordDocumentEngine,
    source_name: str,
):
    return runtime.store.add_document(subject, session.session_id, engine.path, engine, source_name)


def register_tools(mcp: FastMCP, runtime: ToolRuntime) -> None:
    async def _run_publish_worker(
        function: Callable[..., Any],
        *args: Any,
        cancel_result_cleanup: Callable[[Any], Any] | None = None,
        **kwargs: Any,
    ) -> Any:
        worker = asyncio.create_task(asyncio.to_thread(function, *args, **kwargs))
        try:
            return await asyncio.shield(worker)
        except asyncio.CancelledError as cancellation:
            result: Any = None
            completed = False
            try:
                result = await _drain_worker(worker)
                completed = True
            except BaseException:
                completed = False
            if completed and cancel_result_cleanup is not None:
                cleanup = asyncio.create_task(asyncio.to_thread(cancel_result_cleanup, result))
                with contextlib.suppress(BaseException):
                    await _drain_worker(cleanup)
            raise cancellation

    async def _fork_draft(record) -> tuple[Path, WordDocumentEngine]:
        session = runtime.store.sessions.get(record.session_id)
        if session is None or session.closed:
            raise WordToolkitError(ErrorCode.SESSION_NOT_FOUND, "Session was not found")
        transaction_dir = session.root / ".transactions" / record.document_id / opaque_id("txn")
        checkpoint = transaction_dir / "draft.docx"
        try:
            clone = await _run_publish_worker(
                record.engine.fork,
                checkpoint,
                cancel_result_cleanup=lambda abandoned: abandoned.close(),
            )
        except BaseException:
            await _run_publish_worker(shutil.rmtree, transaction_dir, True)
            raise
        return transaction_dir, clone

    async def _cleanup_publish_attempt(
        transaction_dir: Path | None,
        clone: WordDocumentEngine | None,
        outputs: list[Path],
        *,
        committed: bool,
    ) -> None:
        cancellation: asyncio.CancelledError | None = None

        async def cleanup_worker(function: Callable[..., Any], *args: Any) -> None:
            nonlocal cancellation
            try:
                await _run_publish_worker(function, *args)
            except asyncio.CancelledError as exc:
                cancellation = cancellation or exc
            except Exception:
                return

        if not committed:
            if clone is not None:
                await cleanup_worker(clone.close)
            for output in outputs:
                with contextlib.suppress(OSError):
                    if output.is_dir():
                        await cleanup_worker(shutil.rmtree, output, True)
                    else:
                        output.unlink()
        if transaction_dir is not None and not committed:
            await cleanup_worker(shutil.rmtree, transaction_dir, True)
        if cancellation is not None:
            raise cancellation

    def _commit_published_engine(
        record,
        clone: WordDocumentEngine,
        version: int,
        current_path: Path,
    ) -> WordDocumentEngine:
        previous = record.engine
        clone.path = current_path.resolve()
        record.engine = clone
        record.current_path = current_path
        record.version = version
        return previous

    async def _publish_docx(
        document_id: str,
        expected_version: int | None,
        file_name: str,
        label: str,
    ) -> CallToolResult:
        subject = current_subject()
        require_scope("documents:write")
        async with runtime.store.locked_document(subject, document_id) as record:
            runtime.store.require_version(record, expected_version)
            version = record.version + 1
            root = runtime.store.sessions[record.session_id].root
            output = (
                root
                / "versions"
                / record.document_id
                / f"v{version}-{clean_filename(file_name, 'document.docx')}"
            )
            if output.suffix.lower() != ".docx":
                output = output.with_suffix(".docx")
            transaction_dir: Path | None = None
            clone: WordDocumentEngine | None = None
            committed = False
            try:
                transaction_dir, clone = await _fork_draft(record)
                result = await _run_publish_worker(clone.save_version, output)
                inspection = await _run_publish_worker(clone.package_inspector.inspect, output)
                clone.inspection = inspection.to_dict()
                response = await runtime.artifact_result(
                    subject,
                    output,
                    mime_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    filename=output.name,
                    data={
                        "document_id": document_id,
                        "draft_version": version,
                        "save": _public(result),
                    },
                    label=label,
                )
                previous = _commit_published_engine(record, clone, version, output)
                committed = True
                await _run_publish_worker(_close_engine_safely, previous)
                return response
            finally:
                await _cleanup_publish_attempt(
                    transaction_dir, clone, [output], committed=committed
                )

    # ── Lifecycle ──────────────────────────────────────────────────────────

    @mcp.tool(
        title="Create Word document",
        description="Use this when a new DOCX is needed. Creates an isolated draft; it does not overwrite any user file. Page settings are bounded and the result must be saved/exported explicitly.",
        annotations=WRITE,
    )
    @_safe
    async def create_document(
        session_id: str = "",
        page_size: Literal["A4", "Letter"] = "A4",
        orientation: Literal["portrait", "landscape"] = "portrait",
        margin_mm: float = Field(default=25.4, ge=5, le=80),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        session = await runtime.session(subject, session_id)
        path = session.root / "documents" / f"{opaque_id('src')}-created.docx"
        path.parent.mkdir(parents=True, exist_ok=True)
        engine = await asyncio.to_thread(WordDocumentEngine.create, path, runtime.settings)
        width, height = (210.0, 297.0) if page_size == "A4" else (215.9, 279.4)
        await asyncio.to_thread(engine.call, "set_page_size", width, height)
        await asyncio.to_thread(engine.call, "set_page_orientation", orientation)
        await asyncio.to_thread(
            engine.call,
            "set_page_margins",
            top_mm=margin_mm,
            bottom_mm=margin_mm,
            left_mm=margin_mm,
            right_mm=margin_mm,
        )
        record = await _register_document(runtime, subject, session, engine, "created.docx")
        return ok(
            {
                "session_id": session.session_id,
                "document_id": record.document_id,
                "draft_version": 0,
                "anchor_paragraph_id": _first_paragraph_id(engine),
                "page_size": page_size,
                "orientation": orientation,
            }
        )

    @mcp.tool(
        title="Create from Word template",
        description="Use this when a DOCX or DOTX template must be preserved. Downloads only the authorized file reference, rejects macros and unsafe packages, copies it into an isolated session, and creates a new DOCX draft.",
        annotations=WRITE,
        meta=FILE_META,
    )
    @_safe
    async def create_from_template(file: OpenAIFile, session_id: str = "") -> dict:
        subject = current_subject()
        require_scope("documents:write")
        session = await runtime.session(subject, session_id)
        source = await runtime.download_file(file, session, extensions={".docx", ".dotx"})
        path = session.root / "documents" / f"{opaque_id('src')}-from-template.docx"
        path.parent.mkdir(parents=True, exist_ok=True)
        engine = await asyncio.to_thread(WordDocumentEngine.create, path, runtime.settings, source)
        record = await _register_document(
            runtime, subject, session, engine, clean_filename(file.file_name or "template.dotx")
        )
        return ok(
            {
                "session_id": session.session_id,
                "document_id": record.document_id,
                "draft_version": 0,
                "anchor_paragraph_id": _first_paragraph_id(engine),
                "package": engine.inspection,
            }
        )

    @mcp.tool(
        title="Create from Markdown",
        description="Use this when Markdown must become a new DOCX. Accepts raw Markdown or an authorized .md file, preserves a supplied template only when separately requested, and never converts an existing DOCX through Markdown.",
        annotations=WRITE,
        meta={"openai/fileParams": ["file"]},
    )
    @_safe
    async def create_from_markdown(
        markdown: str = "", file: OpenAIFile | None = None, session_id: str = ""
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        if bool(markdown) == bool(file):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT, "Provide exactly one of markdown or file"
            )
        session = await runtime.session(subject, session_id)
        if file:
            md_path = await runtime.download_file(file, session, extensions={".md", ".markdown"})
            markdown = md_path.read_text(encoding="utf-8")
        if len(markdown.encode("utf-8")) > runtime.settings.max_upload_bytes:
            raise WordToolkitError(ErrorCode.LIMIT_EXCEEDED, "Markdown exceeds the input limit")
        path = session.root / "documents" / f"{opaque_id('src')}-from-markdown.docx"
        path.parent.mkdir(parents=True, exist_ok=True)
        engine = await asyncio.to_thread(WordDocumentEngine.create, path, runtime.settings)
        await asyncio.to_thread(
            MarkdownConverter.convert, engine.doc, markdown, base_dir=session.root
        )
        record = await _register_document(runtime, subject, session, engine, "from-markdown.docx")
        return ok(
            {
                "session_id": session.session_id,
                "document_id": record.document_id,
                "draft_version": 0,
                "limitations": [
                    "Markdown math delimiters are not interpreted; use insert_equation for native OMML."
                ],
            }
        )

    @mcp.tool(
        title="Open Word document",
        description="Use this when an uploaded DOCX must be inspected or edited. Reads an authorized ChatGPT file reference, rejects DOCM/DOTM, ZIP bombs, XXE and unsafe relationships, then opens an isolated round-trip draft.",
        annotations=WRITE,
        meta=FILE_META,
    )
    @_safe
    async def open_document(file: OpenAIFile, session_id: str = "") -> dict:
        subject = current_subject()
        require_scope("documents:write")
        session = await runtime.session(subject, session_id)
        source = await runtime.download_file(file, session, extensions={".docx"})
        path = session.root / "documents" / f"{opaque_id('src')}-opened.docx"
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, path)
        engine = WordDocumentEngine(path, runtime.settings)
        info = await asyncio.to_thread(engine.open)
        record = await _register_document(
            runtime, subject, session, engine, clean_filename(file.file_name or "document.docx")
        )
        return ok(
            {
                "session_id": session.session_id,
                "document_id": record.document_id,
                "draft_version": 0,
                "inspection": _public(info),
            }
        )

    @mcp.tool(
        title="Inspect Word document",
        description="Use this for a read-only overview of package parts, structure, styles, sections, tables and native equations. It does not save or mutate the draft.",
        annotations=READ,
    )
    @_safe
    async def inspect_document(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.inspect())

    @mcp.tool(
        title="Save Word document",
        description="Use this to create a new validated DOCX version. It never overwrites the uploaded original. Saving may apply documented structural repairs and returns a temporary download link.",
        annotations=EXPORT,
    )
    @_safe
    async def save_document(
        document_id: str,
        file_name: str = "document.docx",
        expected_version: DRAFT_VERSION = None,
    ) -> CallToolResult:
        return await _publish_docx(
            document_id,
            expected_version,
            file_name,
            "Validated DOCX version ready",
        )

    @mcp.tool(
        title="Close Word document",
        description="Use this to discard the in-memory draft and delete its extracted temporary files. Previously exported versions remain available until their artifact expiry.",
        annotations=DELETE,
    )
    @_safe
    async def close_document(document_id: str, expected_version: DRAFT_VERSION = None) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        await runtime.store.close_document(subject, document_id, expected_version)
        return ok({"document_id": document_id, "closed": True})

    # ── Structure ──────────────────────────────────────────────────────────

    @mcp.tool(
        title="Get document outline",
        description="Use this to read heading levels, text and stable paragraph IDs without changing the draft.",
        annotations=READ,
    )
    @_safe
    async def get_outline(document_id: str, max_level: int = Field(default=6, ge=1, le=9)) -> dict:
        return await _read(
            runtime, document_id, lambda engine: engine.call("get_document_outline", max_level)
        )

    @mcp.tool(
        title="Get document sections",
        description="Use this to inspect page sizes, margins, orientation, columns and section breaks without changing the draft.",
        annotations=READ,
    )
    @_safe
    async def get_sections(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.call("get_sections"))

    @mcp.tool(
        title="Get paragraph",
        description="Use this to read one paragraph by its w14:paraId. It returns text and style but does not flatten the whole document.",
        annotations=READ,
    )
    @_safe
    async def get_paragraph(document_id: str, paragraph_id: str) -> dict:
        return await _read(
            runtime, document_id, lambda engine: engine.call("get_paragraph", paragraph_id)
        )

    @mcp.tool(
        title="Insert paragraph",
        description="Use this to insert one styled paragraph after a known paragraph ID. It mutates only document.xml and preserves unrelated parts.",
        annotations=WRITE,
    )
    @_safe
    async def insert_paragraph(
        document_id: str,
        after_paragraph_id: str,
        text: str = Field(max_length=200_000),
        style: str | None = None,
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call("insert_paragraph", after_paragraph_id, text, style),
        )

    @mcp.tool(
        title="Replace paragraph",
        description="Use this to replace the text and/or style of one paragraph. This is a direct edit, not a document rebuild; use insert_tracked_change when review markup is required.",
        annotations=WRITE,
    )
    @_safe
    async def replace_paragraph(
        document_id: str,
        paragraph_id: str,
        text: str | None = Field(default=None, max_length=200_000),
        style: str | None = None,
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call("update_paragraph", paragraph_id, text, style),
        )

    @mcp.tool(
        title="Delete paragraph",
        description="Use this to remove exactly one paragraph from the current draft. The uploaded original is untouched, but the draft mutation is destructive until rejected by reopening or restoring a prior export.",
        annotations=DELETE,
    )
    @_safe
    async def delete_paragraph(
        document_id: str, paragraph_id: str, expected_version: DRAFT_VERSION = None
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call("delete_paragraph", paragraph_id),
        )

    @mcp.tool(
        title="Move document block",
        description="Use this to move one paragraph or top-level table before or after another block. References use p:<paraId> or table:<zero-based-index>.",
        annotations=WRITE,
    )
    @_safe
    async def move_block(
        document_id: str,
        block_ref: str,
        target_ref: str,
        position: Literal["before", "after"] = "after",
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.move_block(block_ref, target_ref, position),
        )

    # ── Styles ─────────────────────────────────────────────────────────────

    @mcp.tool(
        title="List Word styles",
        description="Use this to inspect paragraph and character styles before applying formatting. It is read-only.",
        annotations=READ,
    )
    @_safe
    async def list_styles(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.call("get_styles"))

    @mcp.tool(
        title="Create Word style",
        description="Use this to add one named paragraph or character style to styles.xml. Prefer this over direct formatting for reusable formatting.",
        annotations=WRITE,
    )
    @_safe
    async def create_style(
        document_id: str,
        name: str,
        style_type: Literal["paragraph", "character"] = "paragraph",
        based_on: str | None = None,
        next_style: str | None = None,
        font_name: str | None = Field(default=None, max_length=128),
        font_size_pt: float | None = Field(default=None, ge=1, le=200),
        font_color: str | None = Field(default=None, pattern=r"^[0-9A-Fa-f]{6}$"),
        bold: bool | None = None,
        italic: bool | None = None,
        space_before_pt: float | None = Field(default=None, ge=0, le=1000),
        space_after_pt: float | None = Field(default=None, ge=0, le=1000),
        line_spacing: float | None = Field(default=None, ge=0.5, le=10),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        def operation(engine):
            created = engine.call("create_style", name, style_type, based_on, next_style)
            configured = engine.configure_style(
                name,
                font_name=font_name,
                font_size_pt=font_size_pt,
                font_color=font_color,
                bold=bold,
                italic=italic,
                space_before_pt=space_before_pt,
                space_after_pt=space_after_pt,
                line_spacing=line_spacing,
            )
            return {"created": created, "formatting": configured}

        return await _mutate(
            runtime,
            document_id,
            expected_version,
            operation,
        )

    @mcp.tool(
        title="Update Word style",
        description="Use this to change inheritance or next-style metadata of one existing named style. It does not replace unrelated style definitions.",
        annotations=WRITE,
    )
    @_safe
    async def update_style(
        document_id: str,
        name: str,
        based_on: str | None = None,
        next_style: str | None = None,
        font_name: str | None = Field(default=None, max_length=128),
        font_size_pt: float | None = Field(default=None, ge=1, le=200),
        font_color: str | None = Field(default=None, pattern=r"^[0-9A-Fa-f]{6}$"),
        bold: bool | None = None,
        italic: bool | None = None,
        space_before_pt: float | None = Field(default=None, ge=0, le=1000),
        space_after_pt: float | None = Field(default=None, ge=0, le=1000),
        line_spacing: float | None = Field(default=None, ge=0.5, le=10),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        def operation(engine):
            metadata = engine.call("update_style", name, based_on, next_style)
            formatting = engine.configure_style(
                name,
                font_name=font_name,
                font_size_pt=font_size_pt,
                font_color=font_color,
                bold=bold,
                italic=italic,
                space_before_pt=space_before_pt,
                space_after_pt=space_after_pt,
                line_spacing=line_spacing,
            )
            return {"metadata": metadata, "formatting": formatting}

        return await _mutate(
            runtime,
            document_id,
            expected_version,
            operation,
        )

    @mcp.tool(
        title="Apply Word style",
        description="Use this to apply an existing style to explicit paragraph IDs. It avoids replacing template styles with direct formatting.",
        annotations=WRITE,
    )
    @_safe
    async def apply_style(
        document_id: str,
        paragraph_ids: list[str] = Field(min_length=1, max_length=500),
        style: str = Field(min_length=1, max_length=128),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call("apply_style_to_range", paragraph_ids, style),
        )

    @mcp.tool(
        title="Inspect direct formatting",
        description="Use this to find run-level direct formatting that may override styles. It does not alter the document.",
        annotations=READ,
    )
    @_safe
    async def inspect_direct_formatting(document_id: str, paragraph_id: str | None = None) -> dict:
        return await _read(
            runtime, document_id, lambda engine: engine.inspect_direct_formatting(paragraph_id)
        )

    @mcp.tool(
        title="Normalize direct formatting",
        description="Use this to remove supported direct run properties from selected paragraphs so style inheritance can take effect. It preserves fields, text and paragraph structure.",
        annotations=WRITE,
    )
    @_safe
    async def normalize_formatting(
        document_id: str,
        paragraph_ids: list[str] | None = Field(default=None, max_length=500),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.normalize_formatting(paragraph_ids),
        )

    @mcp.tool(
        title="Format one Word paragraph",
        description="Set paragraph alignment, spacing, indentation, tab stops and pagination flags without changing its text or style assignment.",
        annotations=WRITE,
    )
    @_safe
    async def format_paragraph(
        document_id: str,
        paragraph_id: str,
        alignment: Literal["left", "center", "right", "both", "distribute"] | None = None,
        space_before_pt: float | None = Field(default=None, ge=0, le=1000),
        space_after_pt: float | None = Field(default=None, ge=0, le=1000),
        line_spacing: float | None = Field(default=None, ge=0.5, le=10),
        left_indent_mm: float | None = Field(default=None, ge=-500, le=500),
        right_indent_mm: float | None = Field(default=None, ge=-500, le=500),
        first_line_mm: float | None = Field(default=None, ge=0, le=500),
        hanging_mm: float | None = Field(default=None, ge=0, le=500),
        keep_with_next: bool | None = None,
        keep_lines_together: bool | None = None,
        widow_control: bool | None = None,
        page_break_before: bool | None = None,
        tab_stops: list[TabStop] | None = Field(default=None, max_length=32),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        tabs = [item.model_dump() for item in tab_stops] if tab_stops is not None else None
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.format_paragraph_layout(
                paragraph_id,
                alignment=alignment,
                space_before_pt=space_before_pt,
                space_after_pt=space_after_pt,
                line_spacing=line_spacing,
                left_indent_mm=left_indent_mm,
                right_indent_mm=right_indent_mm,
                first_line_mm=first_line_mm,
                hanging_mm=hanging_mm,
                keep_with_next=keep_with_next,
                keep_lines=keep_lines_together,
                widow_control=widow_control,
                page_break_before=page_break_before,
                tab_stops=tabs,
            ),
        )

    @mcp.tool(
        title="Format one Word run",
        description="Set direct font, size, color, highlight, emphasis, underline, strike or sub/superscript on one run identified by paragraph and run index.",
        annotations=WRITE,
    )
    @_safe
    async def format_run(
        document_id: str,
        paragraph_id: str,
        run_index: int = Field(ge=0),
        font_name: str | None = Field(default=None, max_length=128),
        font_size_pt: float | None = Field(default=None, ge=1, le=200),
        color: str | None = Field(default=None, pattern=r"^[0-9A-Fa-f]{6}$"),
        highlight: Literal[
            "yellow",
            "green",
            "cyan",
            "magenta",
            "blue",
            "red",
            "darkBlue",
            "darkCyan",
            "darkGreen",
            "darkMagenta",
            "darkRed",
            "darkYellow",
            "darkGray",
            "lightGray",
            "black",
            "white",
        ]
        | None = None,
        bold: bool | None = None,
        italic: bool | None = None,
        underline: Literal["none", "single", "double", "dotted", "dash", "wave"] | None = None,
        strike: bool | None = None,
        vertical: Literal["baseline", "superscript", "subscript"] | None = None,
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.format_run(
                paragraph_id,
                run_index,
                font_name=font_name,
                font_size_pt=font_size_pt,
                color=color,
                highlight=highlight,
                bold=bold,
                italic=italic,
                underline=underline,
                strike=strike,
                vertical=vertical,
            ),
        )

    @mcp.tool(
        title="Manage Word lists",
        description="Inspect, apply, create, restart, promote, demote or suppress native single- and multilevel Word numbering definitions.",
        annotations=WRITE,
    )
    @_safe
    async def manage_lists(
        document_id: str,
        action: Literal[
            "list", "apply", "create_multilevel", "restart", "promote", "demote", "suppress"
        ] = "list",
        paragraph_ids: list[str] | None = Field(default=None, max_length=500),
        paragraph_id: str = "",
        list_style: Literal["bullet", "numbered"] = "bullet",
        name: str = Field(default="WordToolkitList", max_length=128),
        levels: list[ListLevel] | None = Field(default=None, max_length=9),
        level: int = Field(default=0, ge=0, le=8),
        start: int = Field(default=1, ge=0, le=1_000_000),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        if action == "list":
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call("get_lists"),
            )
        if action == "apply":
            if not paragraph_ids:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "apply requires paragraph_ids")

            def operation(engine):
                return engine.call("add_list", paragraph_ids, style=list_style, start=start)
        elif action == "create_multilevel":
            if not levels:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "create_multilevel requires levels")
            definitions = [item.model_dump(exclude_none=True) for item in levels]

            def operation(engine):
                return engine.call("create_multilevel_list", name, definitions)
        elif action == "restart":

            def operation(engine):
                return engine.call("restart_numbering", paragraph_id, level, start)
        elif action == "promote":

            def operation(engine):
                return engine.call("promote_list_item", paragraph_id)
        elif action == "demote":

            def operation(engine):
                return engine.call("demote_list_item", paragraph_id)
        else:

            def operation(engine):
                return engine.call("suppress_numbering", paragraph_id)

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Insert Word caption",
        description="Insert a native Caption-styled paragraph with sequence text after a figure or table anchor.",
        annotations=WRITE,
    )
    @_safe
    async def insert_caption(
        document_id: str,
        after_paragraph_id: str,
        text: str = Field(max_length=20_000),
        label: Literal["Figure", "Table", "Equation"] = "Figure",
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call("insert_caption", after_paragraph_id, text, label),
        )

    # ── Tables ─────────────────────────────────────────────────────────────

    @mcp.tool(
        title="List Word tables",
        description="Use this for a compact read-only inventory of tables and cell content.",
        annotations=READ,
    )
    @_safe
    async def list_tables(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.call("get_tables"))

    @mcp.tool(
        title="Get Word table",
        description="Use this to read one table by zero-based index, including its cells and properties.",
        annotations=READ,
    )
    @_safe
    async def get_table(document_id: str, table_index: int = Field(ge=0)) -> dict:
        return await _read(
            runtime, document_id, lambda engine: engine.call("get_table", table_index)
        )

    @mcp.tool(
        title="Insert Word table",
        description="Use this to insert one native Word table after a paragraph. Row and column counts are bounded to prevent pathological documents.",
        annotations=WRITE,
    )
    @_safe
    async def insert_table(
        document_id: str,
        after_paragraph_id: str,
        rows: int = Field(ge=1, le=200),
        columns: int = Field(ge=1, le=50),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call("add_table", after_paragraph_id, rows, columns),
        )

    @mcp.tool(
        title="Modify Word table",
        description="Use this for one bounded table operation: add_row, delete_row, delete_table, sort, apply_style or duplicate_row. It does not rebuild the document.",
        annotations=WRITE,
    )
    @_safe
    async def modify_table(
        document_id: str,
        table_index: int = Field(ge=0),
        action: Literal[
            "add_row", "delete_row", "delete_table", "sort", "apply_style", "duplicate_row"
        ] = "add_row",
        row_index: int = -1,
        cells: list[str] | None = Field(default=None, max_length=50),
        column_index: int = Field(default=0, ge=0),
        ascending: bool = True,
        style: str = "",
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        def operation(engine):
            if action == "add_row":
                return engine.call("add_table_row", table_index, row_index, cells)
            if action == "delete_row":
                return engine.call("delete_table_row", table_index, row_index)
            if action == "delete_table":
                return engine.call("delete_table", table_index)
            if action == "sort":
                return engine.call("sort_table", table_index, column_index, ascending)
            if action == "apply_style":
                return engine.call("set_table_style", table_index, style)
            return engine.call("duplicate_table_row", table_index, row_index)

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Merge table cells",
        description="Use this to merge a rectangular range of cells in one table. Coordinates are zero-based and the operation changes table grid semantics.",
        annotations=WRITE,
    )
    @_safe
    async def merge_cells(
        document_id: str,
        table_index: int = Field(ge=0),
        start_row: int = Field(ge=0),
        start_column: int = Field(ge=0),
        end_row: int = Field(ge=0),
        end_column: int = Field(ge=0),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call(
                "merge_cells", table_index, start_row, start_column, end_row, end_column
            ),
        )

    @mcp.tool(
        title="Split merged table cell",
        description="Use this to remove supported horizontal/vertical merge properties from one cell. Complex irregular merges are reported rather than silently rebuilt.",
        annotations=WRITE,
    )
    @_safe
    async def split_cells(
        document_id: str,
        table_index: int = Field(ge=0),
        row: int = Field(ge=0),
        column: int = Field(ge=0),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.split_cells(table_index, row, column),
        )

    @mcp.tool(
        title="Set table cell properties",
        description="Use this to change one cell's width, vertical alignment or shading, optionally its row height. Only provided properties are touched.",
        annotations=WRITE,
    )
    @_safe
    async def set_cell_properties(
        document_id: str,
        table_index: int = Field(ge=0),
        row: int = Field(ge=0),
        column: int = Field(ge=0),
        width_mm: float | None = Field(default=None, ge=1, le=500),
        vertical_alignment: Literal["top", "center", "bottom"] | None = None,
        fill_color: str | None = Field(default=None, pattern=r"^[0-9A-Fa-f]{6}$"),
        row_height_mm: float | None = Field(default=None, ge=1, le=500),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        def operation(engine):
            results = []
            if width_mm is not None:
                results.append(engine.call("set_cell_width", table_index, row, column, width_mm))
            if vertical_alignment is not None:
                results.append(
                    engine.call(
                        "set_cell_vertical_alignment", table_index, row, column, vertical_alignment
                    )
                )
            if fill_color is not None:
                results.append(
                    engine.call("set_cell_shading", table_index, row, column, fill_color)
                )
            if row_height_mm is not None:
                results.append(
                    engine.call("set_row_height", table_index, row, row_height_mm, "atLeast")
                )
            return results

        return await _mutate(runtime, document_id, expected_version, operation)

    # ── Native Office Math ─────────────────────────────────────────────────

    @mcp.tool(
        title="Insert native Word equation",
        description="Use this to insert LaTeX, UnicodeMath, Presentation MathML, OMML or structured AST as native Office Math. Inline output is m:oMath; display output is m:oMathPara. No rasterization or text fallback occurs.",
        annotations=WRITE,
    )
    @_safe
    async def insert_equation(
        document_id: str,
        anchor_paragraph_id: str,
        value: str | dict,
        input_format: Literal["latex", "unicodemath", "mathml", "omml", "ast"] = "latex",
        display: bool = True,
        position: Literal["after", "before", "append"] = "after",
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.insert_equation(
                anchor_paragraph_id, value, input_format, display=display, position=position
            ),
        )

    @mcp.tool(
        title="Replace native Word equation",
        description="Use this to replace exactly one equation by equation_id while retaining native OMML and the surrounding document structure.",
        annotations=WRITE,
    )
    @_safe
    async def replace_equation(
        document_id: str,
        equation_id: str,
        value: str | dict,
        input_format: Literal["latex", "unicodemath", "mathml", "omml", "ast"] = "latex",
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.replace_equation(equation_id, value, input_format),
        )

    @mcp.tool(
        title="List native Word equations",
        description="Use this for a read-only inventory of m:oMath elements with stable per-draft IDs, display/inline status and canonical AST.",
        annotations=READ,
    )
    @_safe
    async def list_equations(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.list_equations())

    @mcp.tool(
        title="Get native Word equation",
        description="Use this to read one equation as OMML plus best-effort LaTeX, UnicodeMath, MathML and canonical AST.",
        annotations=READ,
    )
    @_safe
    async def get_equation(document_id: str, equation_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.get_equation(equation_id))

    @mcp.tool(
        title="Convert equation format",
        description="Use this for a stateless semantic conversion among LaTeX, UnicodeMath, Presentation MathML, OMML and the WordToolkit AST. Unsupported syntax fails explicitly; no image or plain-text fallback is emitted.",
        annotations=READ,
    )
    @_safe
    async def convert_equation(
        value: str | dict,
        input_format: Literal["latex", "unicodemath", "mathml", "omml", "ast"],
        output_format: Literal["latex", "unicodemath", "mathml", "omml", "ast"],
        display: bool = False,
    ) -> dict:
        require_scope("documents:read")
        from ..math import MathEngine

        result = MathEngine().convert(value, input_format, output_format, display=display)
        return ok({"output_format": output_format, "value": result})

    @mcp.tool(
        title="Validate native Word equations",
        description="Use this to parse every m:oMath in the current draft and report structural failures without modifying the document.",
        annotations=READ,
    )
    @_safe
    async def validate_equations(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.validate_equations())

    @mcp.tool(
        title="Number display equations",
        description="Use this to add automatic SEQ Equation fields and bookmarks after display equations. Existing source equations remain native OMML; fields are update-on-open in Word.",
        annotations=WRITE,
    )
    @_safe
    async def number_equations(
        document_id: str,
        start: int = Field(default=1, ge=1, le=1_000_000),
        bookmark_prefix: str = Field(default="Eq_", pattern=r"^[A-Za-z][A-Za-z0-9_]{0,30}$"),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.number_equations(start=start, prefix=bookmark_prefix),
        )

    @mcp.tool(
        title="Add equation reference",
        description="Use this to append a native REF field pointing to a numbered equation bookmark. Word updates the cached field value when fields are refreshed.",
        annotations=WRITE,
    )
    @_safe
    async def add_equation_reference(
        document_id: str,
        paragraph_id: str,
        bookmark: str,
        prefix_text: str = "",
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.add_equation_reference(paragraph_id, bookmark, prefix_text),
        )

    # ── Document elements ─────────────────────────────────────────────────

    @mcp.tool(
        title="Manage headers and footers",
        description="Read, create, replace or remove a default/first/even header or footer story. PAGE and NUMPAGES placeholders use {{PAGE}} and {{NUMPAGES}} native fields.",
        annotations=WRITE,
    )
    @_safe
    async def manage_headers_footers(
        document_id: str,
        action: Literal["list", "set_text", "replace_text", "delete"] = "list",
        story_kind: Literal["header", "footer"] = "header",
        variant: Literal["default", "first", "even"] = "default",
        section_index: int = Field(default=0, ge=0, le=1000),
        text: str = Field(default="", max_length=200_000),
        old_text: str = "",
        new_text: str = "",
        author: str = "WordToolkit",
        tracked: bool = True,
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        if action == "list":
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call("get_headers_footers"),
            )
        if action == "set_text":

            def operation(engine):
                return engine.set_header_footer_text(story_kind, variant, text, section_index)
        elif action == "replace_text":
            if not old_text:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "replace_text requires old_text")

            def operation(engine):
                return engine.call(
                    "edit_header_footer",
                    story_kind,
                    old_text,
                    new_text,
                    author=author,
                    tracked=tracked,
                )
        else:

            def operation(engine):
                return engine.call(
                    "delete_header" if story_kind == "header" else "delete_footer",
                    variant,
                )

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Manage footnotes and endnotes",
        description="List, add, update, delete or structurally validate native Word footnotes/endnotes. Note references and definition parts are kept linked.",
        annotations=WRITE,
    )
    @_safe
    async def manage_footnotes_endnotes(
        document_id: str,
        note_kind: Literal["footnote", "endnote"] = "footnote",
        action: Literal["list", "add", "update", "delete", "validate"] = "list",
        paragraph_id: str = "",
        note_id: int | None = Field(default=None, ge=1),
        text: str = Field(default="", max_length=200_000),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        plural = f"{note_kind}s"
        if action in {"list", "validate"}:
            method = f"get_{plural}" if action == "list" else f"validate_{plural}"
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call(method),
            )
        if action == "add":
            if not paragraph_id or not text:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT, "add requires paragraph_id and text"
                )

            def operation(engine):
                return engine.call(f"add_{note_kind}", paragraph_id, text)
        elif action == "update":
            if note_id is None:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "update requires note_id")

            def operation(engine):
                return engine.call(f"update_{note_kind}", note_id, text)
        else:
            if note_id is None:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "delete requires note_id")

            def operation(engine):
                return engine.call(f"delete_{note_kind}", note_id)

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Manage Word comments",
        description="List comment threads or add, reply, update, resolve and delete native Word comments and comment-extension metadata.",
        annotations=WRITE,
    )
    @_safe
    async def manage_comments(
        document_id: str,
        action: Literal["list", "threads", "add", "reply", "update", "resolve", "delete"] = "list",
        paragraph_id: str = "",
        comment_id: int | None = Field(default=None, ge=0),
        text: str = Field(default="", max_length=200_000),
        author: str = Field(default="WordToolkit", max_length=128),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        if action in {"list", "threads"}:
            method = "get_comments" if action == "list" else "list_comment_threads"
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call(method),
            )
        if action == "add":
            if not paragraph_id or not text:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT, "add requires paragraph_id and text"
                )

            def operation(engine):
                return engine.call("add_comment", paragraph_id, text, author=author)
        elif action == "reply":
            if comment_id is None or not text:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT, "reply requires comment_id and text"
                )

            def operation(engine):
                return engine.call("reply_to_comment", comment_id, text, author=author)
        elif action == "update":
            if comment_id is None:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "update requires comment_id")

            def operation(engine):
                return engine.call("update_comment", comment_id, text)
        elif action == "resolve":
            if comment_id is None:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "resolve requires comment_id")

            def operation(engine):
                return engine.call("resolve_comment", comment_id)
        else:
            if comment_id is None:
                raise WordToolkitError(ErrorCode.INVALID_INPUT, "delete requires comment_id")

            def operation(engine):
                return engine.call("delete_comment", comment_id)

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Manage bookmarks",
        description="List, add, read or remove Word bookmarks. Bookmark names are constrained to Word-compatible identifiers.",
        annotations=WRITE,
    )
    @_safe
    async def manage_bookmarks(
        document_id: str,
        action: Literal["list", "add", "get_text", "remove"] = "list",
        paragraph_id: str = "",
        name: str = Field(default="", pattern=r"^[A-Za-z_][A-Za-z0-9_]{0,39}$"),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        if action == "list":
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call("list_bookmarks"),
            )
        if action == "get_text":
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call("get_bookmarked_text", name),
            )
        if action == "add":
            if not paragraph_id or not name:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT, "add requires paragraph_id and name"
                )

            def operation(engine):
                return engine.call("add_bookmark", paragraph_id, name)
        else:

            def operation(engine):
                return engine.call("remove_bookmark", name)

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Manage cross references",
        description="List hyperlinks or add an internal cross-reference between two paragraph IDs using a bookmark-backed Word hyperlink.",
        annotations=WRITE,
    )
    @_safe
    async def manage_cross_references(
        document_id: str,
        action: Literal["list", "add"] = "list",
        source_paragraph_id: str = "",
        target_paragraph_id: str = "",
        text: str = Field(default="", max_length=10_000),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        if action == "list":
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call("list_hyperlinks"),
            )
        if not source_paragraph_id or not target_paragraph_id or not text:
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "add requires source, target and text")
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call(
                "add_cross_reference", source_paragraph_id, target_paragraph_id, text
            ),
        )

    @mcp.tool(
        title="Manage Word fields",
        description="List, add, delete or mark fields for update. Field codes are bounded; common values include TOC, PAGE, NUMPAGES and REF bookmark.",
        annotations=WRITE,
    )
    @_safe
    async def manage_fields(
        document_id: str,
        action: Literal[
            "list",
            "add",
            "delete",
            "update_on_open",
            "generate_toc",
            "generate_figures",
            "generate_tables",
        ] = "list",
        paragraph_id: str = "",
        field_id: str = "",
        field_code: str = Field(default="", max_length=4096),
        cached_value: str = Field(default="", max_length=10_000),
        max_heading_level: int = Field(default=3, ge=1, le=9),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        if action == "list":
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call("list_fields"),
            )
        if action == "add":
            if not paragraph_id or not field_code:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT, "add requires paragraph_id and field_code"
                )

            def operation(engine):
                return engine.call("add_field", paragraph_id, field_code, cached_value)
        elif action == "delete":

            def operation(engine):
                return engine.call("delete_field", field_id)
        elif action == "update_on_open":

            def operation(engine):
                return engine.call("update_fields")
        elif action == "generate_toc":

            def operation(engine):
                return engine.call("generate_toc", max_heading_level)
        elif action == "generate_figures":

            def operation(engine):
                return engine.call("generate_tof", paragraph_id)
        else:

            def operation(engine):
                return engine.call("generate_tot", paragraph_id)

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Insert embedded image",
        description="Download an authorized PNG/JPEG/GIF/TIFF image, verify its image type, then embed it through DrawingML. No external image relationship is created.",
        annotations=WRITE,
        meta=FILE_META,
    )
    @_safe
    async def insert_image(
        document_id: str,
        paragraph_id: str,
        file: OpenAIFile,
        width_mm: float = Field(default=60, ge=1, le=500),
        height_mm: float = Field(default=40, ge=1, le=500),
        alt_text: str = Field(default="", max_length=1000),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        record = await runtime.store.get_document(subject, document_id)
        session = await runtime.store.get_session(subject, record.session_id)
        image_path = await runtime.download_file(
            file, session, extensions={".png", ".jpg", ".jpeg", ".gif", ".tif", ".tiff"}
        )
        from PIL import Image

        try:
            with Image.open(image_path) as image:
                image.verify()
        except Exception as exc:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT, "Uploaded file is not a valid supported image"
            ) from exc
        width_emu, height_emu = int(width_mm * 36000), int(height_mm * 36000)

        def operation(engine):
            result = engine.call(
                "insert_image",
                paragraph_id,
                str(image_path),
                width_emu=width_emu,
                height_emu=height_emu,
            )
            if alt_text:
                engine.call("set_image_alt_text", result["rId"], alt_text)
            return result

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Manage Word sections",
        description="Inspect or change page/section properties including breaks, size, margins, orientation, columns and first/odd-even headers.",
        annotations=WRITE,
    )
    @_safe
    async def manage_sections(
        document_id: str,
        action: Literal[
            "list",
            "add_break",
            "delete_break",
            "set_page",
            "set_columns",
            "different_first_page",
            "odd_even_headers",
            "page_break",
        ] = "list",
        paragraph_id: str = "",
        section_index: int = Field(default=0, ge=0, le=1000),
        break_type: Literal["nextPage", "continuous", "evenPage", "oddPage"] = "nextPage",
        width_mm: float | None = Field(default=None, ge=50, le=1000),
        height_mm: float | None = Field(default=None, ge=50, le=1000),
        orientation: Literal["portrait", "landscape"] | None = None,
        margin_mm: float | None = Field(default=None, ge=0, le=200),
        columns: int = Field(default=1, ge=1, le=12),
        enabled: bool = True,
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        if action == "list":
            return await _read_at_version(
                runtime,
                document_id,
                expected_version,
                lambda engine: engine.call("get_sections"),
            )
        if action == "add_break":

            def operation(engine):
                return engine.call("add_section_break", paragraph_id, break_type)
        elif action == "delete_break":

            def operation(engine):
                return engine.call("delete_section_break", paragraph_id)
        elif action == "page_break":

            def operation(engine):
                return engine.call("add_page_break", paragraph_id)
        elif action == "set_page":
            kwargs: dict[str, Any] = {}
            if width_mm is not None and height_mm is not None:
                kwargs.update(width=int(width_mm * 56.6929), height=int(height_mm * 56.6929))
            if orientation is not None:
                kwargs["orientation"] = orientation
            if margin_mm is not None:
                twips = int(margin_mm * 56.6929)
                kwargs.update(
                    margin_top=twips, margin_bottom=twips, margin_left=twips, margin_right=twips
                )

            def operation(engine):
                return engine.call("set_section_properties", **kwargs)
        elif action == "set_columns":

            def operation(engine):
                return engine.call("set_section_columns", section_index, columns)
        elif action == "different_first_page":

            def operation(engine):
                return engine.call("set_different_first_page", section_index, enabled)
        else:

            def operation(engine):
                return engine.call("set_odd_even_headers", enabled)

        return await _mutate(runtime, document_id, expected_version, operation)

    # ── Revisions ─────────────────────────────────────────────────────────

    @mcp.tool(
        title="Enable tracked changes",
        description="Enable or disable Word's w:trackRevisions setting. This does not retroactively wrap existing content.",
        annotations=WRITE,
    )
    @_safe
    async def enable_track_changes(
        document_id: str,
        enabled: bool = True,
        author: str = "WordToolkit",
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call("set_track_changes", enabled, author),
        )

    @mcp.tool(
        title="List tracked changes",
        description="Read pending w:ins and w:del revisions with author, date, paragraph and text; no mutation occurs.",
        annotations=READ,
    )
    @_safe
    async def list_tracked_changes(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.call("get_tracked_changes"))

    @mcp.tool(
        title="Insert tracked change",
        description="Insert, delete or replace text using native Word revision elements. Context may be supplied to disambiguate a match.",
        annotations=WRITE,
    )
    @_safe
    async def insert_tracked_change(
        document_id: str,
        paragraph_id: str,
        change_type: Literal["insert", "delete", "replace"],
        text: str = Field(max_length=200_000),
        replacement: str = Field(default="", max_length=200_000),
        author: str = Field(default="WordToolkit", max_length=128),
        position: str = "end",
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        if change_type == "insert":

            def operation(engine):
                return engine.call(
                    "insert_text",
                    paragraph_id,
                    text,
                    position=position,
                    author=author,
                    tracked=True,
                )
        elif change_type == "delete":

            def operation(engine):
                return engine.call("delete_text", paragraph_id, text, author=author, tracked=True)
        else:

            def operation(engine):
                return engine.call(
                    "replace_text",
                    paragraph_id,
                    find=text,
                    replace=replacement,
                    author=author,
                    tracked=True,
                )

        return await _mutate(runtime, document_id, expected_version, operation)

    @mcp.tool(
        title="Accept tracked changes",
        description="Accept one change by ID or all pending revisions. This is destructive within the draft; the uploaded original remains untouched.",
        annotations=DELETE,
    )
    @_safe
    async def accept_changes(
        document_id: str,
        change_id: int | None = Field(default=None, ge=0),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        method = "accept_change" if change_id is not None else "accept_all_changes"
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call(method, *([change_id] if change_id is not None else [])),
        )

    @mcp.tool(
        title="Reject tracked changes",
        description="Reject one change by ID or all pending revisions. This is destructive within the draft; the uploaded original remains untouched.",
        annotations=DELETE,
    )
    @_safe
    async def reject_changes(
        document_id: str,
        change_id: int | None = Field(default=None, ge=0),
        expected_version: DRAFT_VERSION = None,
    ) -> dict:
        method = "reject_change" if change_id is not None else "reject_all_changes"
        return await _mutate(
            runtime,
            document_id,
            expected_version,
            lambda engine: engine.call(method, *([change_id] if change_id is not None else [])),
        )

    @mcp.tool(
        title="Compare two Word documents",
        description="Compare two authorized DOCX files and return a third DOCX containing native insertion/deletion revision markup. The inputs are never overwritten.",
        annotations=EXPORT,
        meta={"openai/fileParams": ["base_file", "revised_file"]},
    )
    @_safe
    async def compare_documents(
        base_file: OpenAIFile,
        revised_file: OpenAIFile,
        session_id: str = "",
        file_name: str = "comparison.docx",
    ) -> CallToolResult:
        subject = current_subject()
        require_scope("documents:write")
        session = await runtime.session(subject, session_id)
        base = await runtime.download_file(base_file, session, extensions={".docx"})
        revised = await runtime.download_file(revised_file, session, extensions={".docx"})
        runtime.validator.package_inspector.inspect(base)
        runtime.validator.package_inspector.inspect(revised)
        output = (
            session.root
            / "artifacts"
            / f"{opaque_id('cmp')}-{clean_filename(file_name, 'comparison.docx')}"
        )
        output.parent.mkdir(parents=True, exist_ok=True)
        await asyncio.to_thread(
            DocxDocument.compare_documents, str(base), str(revised), str(output)
        )
        validation = await asyncio.to_thread(runtime.validator.validate, output)
        if not validation["valid"]:
            raise WordToolkitError(
                ErrorCode.OOXML_INVALID,
                "Comparison result failed validation",
                {"issues": validation["issues"][:50]},
            )
        return await runtime.artifact_result(
            subject,
            output,
            mime_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            filename=clean_filename(file_name, "comparison.docx"),
            data={"validation": validation},
        )

    # ── Quality control ───────────────────────────────────────────────────

    async def _snapshot(document_id: str, label: str) -> tuple[str, int, Path]:
        subject = current_subject()
        async with runtime.store.locked_document(subject, document_id) as record:
            root = runtime.store.sessions[record.session_id].root
            version = record.version
            output = (
                root
                / "quality"
                / record.document_id
                / f"{label}-v{version}-{opaque_id('snap')}.docx"
            )
            await _run_locked_worker(record.engine.snapshot, output)
            return subject, version, output

    @mcp.tool(
        title="Validate OOXML package",
        description="Run bounded ZIP/OPC, XML, relationship, note, paraId and native OMML validation on a snapshot without committing a version.",
        annotations=READ,
    )
    @_safe
    async def validate_ooxml(document_id: str) -> dict:
        require_scope("documents:read")
        _subject, version, path = await _snapshot(document_id, "validate")
        result = await asyncio.to_thread(runtime.validator.validate, path)
        return ok({"document_id": document_id, "draft_version": version, "validation": result})

    @mcp.tool(
        title="Audit Word document",
        description="Audit document structure and external relationship inventory. No external target is fetched and no content is logged.",
        annotations=READ,
    )
    @_safe
    async def audit_document(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.package_audit())

    @mcp.tool(
        title="Detect document corruption",
        description="Run integrity validation and return a corruption verdict with explicit issue codes; it does not modify the draft.",
        annotations=READ,
    )
    @_safe
    async def detect_corruption(document_id: str) -> dict:
        require_scope("documents:read")
        _subject, version, path = await _snapshot(document_id, "corruption")
        validation = await asyncio.to_thread(runtime.validator.validate, path)
        return ok(
            {
                "document_id": document_id,
                "draft_version": version,
                "corrupt": not validation["valid"],
                "issues": validation["issues"],
            }
        )

    @mcp.tool(
        title="Repair Word document",
        description="Apply the engine's conservative pre-save repairs, validate the package, and return a new DOCX artifact. Unknown untouched parts remain round-trip protected.",
        annotations=EXPORT,
    )
    @_safe
    async def repair_document(
        document_id: str,
        file_name: str = "repaired.docx",
        expected_version: DRAFT_VERSION = None,
    ) -> CallToolResult:
        return await _export_docx(
            document_id,
            expected_version,
            file_name,
            "Repaired and validated DOCX ready",
        )

    @mcp.tool(
        title="Check document accessibility",
        description="Check image alternative text, heading order, table headers and language metadata using OOXML heuristics.",
        annotations=READ,
    )
    @_safe
    async def check_accessibility(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.call("check_accessibility"))

    @mcp.tool(
        title="Check layout risks",
        description="Check static OOXML layout risks such as over-wide tables and headings lacking keep-with-next. Rendering checks are separate.",
        annotations=READ,
    )
    @_safe
    async def check_layout_risks(document_id: str) -> dict:
        return await _read(runtime, document_id, lambda engine: engine.layout_risks())

    @mcp.tool(
        title="Detect orphaned relationships",
        description="Report missing internal relationship targets and unreferenced OPC parts without deleting them.",
        annotations=READ,
    )
    @_safe
    async def detect_orphaned_relationships(document_id: str) -> dict:
        require_scope("documents:read")
        _subject, version, path = await _snapshot(document_id, "relationships")
        validation = await asyncio.to_thread(runtime.validator.validate, path)
        issues = [
            item
            for item in validation["issues"]
            if item["code"].startswith("REL_") or "ORPHANED" in item["code"]
        ]
        return ok({"document_id": document_id, "draft_version": version, "issues": issues})

    # ── Export and visual verification ────────────────────────────────────

    async def _export_docx(
        document_id: str,
        expected_version: int | None,
        file_name: str,
        label: str = "Validated DOCX ready",
    ) -> CallToolResult:
        return await _publish_docx(document_id, expected_version, file_name, label)

    async def _render(
        document_id: str,
        file_name: str,
        include_pages: bool,
        dpi: int,
        expected_version: int | None,
        max_pages: int | None = None,
    ) -> CallToolResult:
        subject = current_subject()
        require_scope("documents:write")
        async with runtime.store.locked_document(subject, document_id) as record:
            runtime.store.require_version(record, expected_version)
            version = record.version + 1
            root = runtime.store.sessions[record.session_id].root
            stem = Path(clean_filename(file_name, "document.pdf")).stem
            directory = root / "renders" / record.document_id / f"v{version}"
            docx = directory / f"{stem}.docx"
            pdf = directory / f"{stem}.pdf"
            transaction_dir: Path | None = None
            clone: WordDocumentEngine | None = None
            committed = False
            try:
                transaction_dir, clone = await _fork_draft(record)
                save = await _run_publish_worker(clone.save_version, docx)
                rendering = await _run_publish_worker(runtime.renderer.to_pdf, docx, pdf)
                pages = await _run_publish_worker(
                    runtime.renderer.pages_to_png, pdf, directory / "pages", dpi=dpi
                )
                visual = await _run_publish_worker(runtime.renderer.visual_audit, pdf, pages)
                inspection = await _run_publish_worker(clone.package_inspector.inspect, docx)
                clone.inspection = inspection.to_dict()
                files = [(pdf, "application/pdf", pdf.name)]
                if include_pages:
                    selected = pages[:max_pages] if max_pages else pages
                    files.extend((page, "image/png", f"{stem}-{page.name}") for page in selected)
                response = await runtime.multi_artifact_result(
                    subject,
                    files,
                    data={
                        "document_id": document_id,
                        "draft_version": version,
                        "save": _public(save),
                        "rendering": _public(rendering),
                        "visual_audit": visual,
                        "page_count": len(pages),
                    },
                    label="Rendered files and visual QA ready",
                )
                previous = _commit_published_engine(record, clone, version, docx)
                committed = True
                await _run_publish_worker(_close_engine_safely, previous)
                return response
            finally:
                await _cleanup_publish_attempt(
                    transaction_dir, clone, [directory], committed=committed
                )

    @mcp.tool(
        title="Render Word document",
        description="Save, validate and render the current draft to PDF with isolated LibreOffice headless, then run page-level visual heuristics. Word and LibreOffice are not pixel-identical.",
        annotations=EXPORT,
    )
    @_safe
    async def render_document(
        document_id: str,
        file_name: str = "document.pdf",
        expected_version: DRAFT_VERSION = None,
    ) -> CallToolResult:
        return await _render(document_id, file_name, False, 144, expected_version)

    @mcp.tool(
        title="Render Word pages",
        description="Save and render the draft to PDF and bounded PNG page previews using LibreOffice and Poppler. Returns MCP resource links instead of internal working paths.",
        annotations=EXPORT,
    )
    @_safe
    async def render_pages(
        document_id: str,
        file_name: str = "document.pdf",
        dpi: int = Field(default=144, ge=72, le=300),
        max_pages: int = Field(default=20, ge=1, le=100),
        expected_version: DRAFT_VERSION = None,
    ) -> CallToolResult:
        return await _render(document_id, file_name, True, dpi, expected_version, max_pages)

    @mcp.tool(
        title="Convert Word document to PDF",
        description="Validate the draft, convert it with LibreOffice headless, render pages for visual QA, and return the PDF. Pagination may differ from Microsoft Word.",
        annotations=EXPORT,
    )
    @_safe
    async def convert_to_pdf(
        document_id: str,
        file_name: str = "document.pdf",
        expected_version: DRAFT_VERSION = None,
    ) -> CallToolResult:
        return await _render(document_id, file_name, False, 144, expected_version)

    @mcp.tool(
        title="Export Word document",
        description="Export the current draft as a new validated DOCX or as best-effort Markdown. DOCX export preserves untouched OPC parts; Markdown is intentionally lossy.",
        annotations=EXPORT,
    )
    @_safe
    async def export_document(
        document_id: str,
        output_format: Literal["docx", "markdown"] = "docx",
        file_name: str = "document.docx",
        expected_version: DRAFT_VERSION = None,
    ) -> CallToolResult:
        if output_format == "docx":
            return await _export_docx(document_id, expected_version, file_name)
        subject = current_subject()
        require_scope("documents:read")
        async with runtime.store.locked_document(subject, document_id) as record:
            root = runtime.store.sessions[record.session_id].root
            version = record.version
            markdown_name = clean_filename(Path(file_name).with_suffix(".md").name, "document.md")
            output = (
                root
                / "exports"
                / record.document_id
                / f"v{version}-{opaque_id('exp')}-{markdown_name}"
            )
            output.parent.mkdir(parents=True, exist_ok=True)
            try:
                result = await _run_publish_worker(
                    record.engine.call, "export_markdown", str(output)
                )
                return await runtime.artifact_result(
                    subject,
                    output,
                    mime_type="text/markdown",
                    filename=markdown_name,
                    data={
                        "document_id": document_id,
                        "draft_version": version,
                        "export": _public(result),
                    },
                    label="Best-effort Markdown export ready",
                )
            except BaseException:
                with contextlib.suppress(OSError):
                    output.unlink()
                raise

    @mcp.tool(
        title="Generate document preview",
        description="Generate a PDF plus the first preview pages and visual-audit findings for mobile review. The preview is LibreOffice-based, not a pixel guarantee for Word.",
        annotations=EXPORT,
    )
    @_safe
    async def generate_preview(
        document_id: str,
        max_pages: int = Field(default=6, ge=1, le=20),
        dpi: int = Field(default=120, ge=72, le=200),
        expected_version: DRAFT_VERSION = None,
    ) -> CallToolResult:
        return await _render(document_id, "preview.pdf", True, dpi, expected_version, max_pages)

    version_schema = {"type": "integer", "minimum": 0, "title": "Expected Version"}

    def inline_version_schema(parameters: dict[str, Any]) -> None:
        parameters["properties"]["expected_version"] = dict(version_schema)
        definitions = parameters.get("$defs")
        if isinstance(definitions, dict):
            definitions.pop("DRAFT_VERSION", None)
            if not definitions:
                parameters.pop("$defs")

    for tool_name in DRAFT_VERSION_REQUIRED_TOOLS:
        registered = mcp._tool_manager.get_tool(tool_name)
        if registered is None:
            raise RuntimeError(f"Draft-version contract references an unknown tool: {tool_name}")
        inline_version_schema(registered.parameters)
        required = registered.parameters.setdefault("required", [])
        if "expected_version" not in required:
            required.append("expected_version")

    export_tool = mcp._tool_manager.get_tool("export_document")
    if export_tool is None:
        raise RuntimeError("Draft-version contract references an unknown export tool")
    inline_version_schema(export_tool.parameters)
    export_tool.parameters["allOf"] = [
        {
            "if": {"properties": {"output_format": {"const": "docx"}}},
            "then": {"required": ["expected_version"]},
        }
    ]
