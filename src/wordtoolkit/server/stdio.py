from __future__ import annotations

import asyncio
import contextlib
import os
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Literal, cast

from mcp.server.fastmcp import FastMCP

from ..config import Settings
from ..runtime import ToolRuntime
from .live_tools import register_live_tools
from .tools import register_tools


def _default_local_storage_root() -> Path:
    configured = os.environ.get("WORDTOOLKIT_STORAGE_ROOT", "").strip()
    if configured:
        return Path(configured).expanduser()
    if os.name == "nt":
        base = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData" / "Local"))
    else:
        base = Path(os.environ.get("XDG_STATE_HOME", Path.home() / ".local" / "state"))
    return base / "WordToolkit" / "sessions"


def _bundled_validator_path() -> Path:
    runtime_root = Path(__file__).resolve().parents[3]
    executable = (
        "wordtoolkit-openxml-validator.exe" if os.name == "nt" else "wordtoolkit-openxml-validator"
    )
    return runtime_root / "tools" / "openxml-validator" / executable


def _local_settings() -> Settings:
    validator = _bundled_validator_path()
    if validator.is_file() and not os.environ.get("WORDTOOLKIT_OPENXML_VALIDATOR_PATH"):
        return Settings(
            auth_mode="local_stdio",
            storage_root=_default_local_storage_root(),
            public_base_url="http://127.0.0.1",
            openxml_validator_path=validator,
        )
    return Settings(
        auth_mode="local_stdio",
        storage_root=_default_local_storage_root(),
        public_base_url="http://127.0.0.1",
    )


def build_stdio_server(settings: Settings | None = None) -> FastMCP:
    settings = settings or _local_settings()
    if not settings.is_local_stdio:
        raise RuntimeError("The STDIO server requires WORDTOOLKIT_AUTH_MODE=local_stdio")
    runtime = ToolRuntime(settings)

    @asynccontextmanager
    async def lifespan(_server: FastMCP):
        stop = asyncio.Event()

        async def cleanup_loop() -> None:
            while not stop.is_set():
                try:
                    await asyncio.wait_for(stop.wait(), timeout=settings.cleanup_interval_seconds)
                except TimeoutError:
                    await runtime.store.cleanup_expired()

        task = asyncio.create_task(cleanup_loop(), name="wordtoolkit-stdio-cleanup")
        try:
            yield {"runtime": runtime}
        finally:
            stop.set()
            task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await task
            for session in list(runtime.store.sessions.values()):
                for document_id in list(session.documents):
                    record = runtime.store.documents.get(document_id)
                    if record:
                        with contextlib.suppress(Exception):
                            record.engine.close()

    log_level = cast(
        Literal["DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"],
        os.environ.get("WORDTOOLKIT_LOG_LEVEL", "WARNING").upper(),
    )
    mcp = FastMCP(
        name="WordToolkit",
        instructions=(
            "Local Microsoft Word and round-trip WordprocessingML editor. For a document "
            "already open in Word, list and connect to the exact live document, inspect it, "
            "use a fresh selection token for cursor edits, validate a SaveCopyAs snapshot, "
            "and explicitly save the same path. For file workflows, open or create an "
            "isolated draft, validate OOXML and equations, render, then export a new file."
        ),
        lifespan=lifespan,
        log_level=log_level,
    )
    register_tools(mcp, runtime)
    register_live_tools(mcp, runtime)
    return mcp


def main() -> None:
    os.environ["WORDTOOLKIT_AUTH_MODE"] = "local_stdio"
    build_stdio_server().run(transport="stdio")


if __name__ == "__main__":
    main()
