from __future__ import annotations

import asyncio
import contextlib
import time
from contextlib import asynccontextmanager
from urllib.parse import urlparse

import uvicorn
from mcp.server.auth.settings import AuthSettings
from mcp.server.fastmcp import FastMCP
from mcp.server.transport_security import TransportSecuritySettings
from pydantic import AnyHttpUrl
from starlette.applications import Starlette
from starlette.middleware import Middleware
from starlette.requests import Request
from starlette.responses import FileResponse, JSONResponse, Response
from starlette.routing import Mount, Route

from .. import __version__
from ..auth import WordToolkitTokenVerifier
from ..config import Settings
from ..runtime import ToolRuntime
from .tools import register_tools


class RequestTooLarge(Exception):
    pass


class RequestSizeLimitMiddleware:
    def __init__(self, app, max_bytes: int):
        self.app = app
        self.max_bytes = max_bytes

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        headers = {key.lower(): value for key, value in scope.get("headers", [])}
        declared = headers.get(b"content-length")
        if declared:
            try:
                if int(declared) > self.max_bytes:
                    await JSONResponse({"error": "request_too_large"}, status_code=413)(
                        scope, receive, send
                    )
                    return
            except ValueError:
                await JSONResponse({"error": "invalid_content_length"}, status_code=400)(
                    scope, receive, send
                )
                return
        consumed = 0
        response_started = False

        async def limited_receive():
            nonlocal consumed
            message = await receive()
            if message["type"] == "http.request":
                consumed += len(message.get("body", b""))
                if consumed > self.max_bytes:
                    raise RequestTooLarge
            return message

        async def tracked_send(message):
            nonlocal response_started
            if message["type"] == "http.response.start":
                response_started = True
            await send(message)

        try:
            await self.app(scope, limited_receive, tracked_send)
        except RequestTooLarge:
            if response_started:
                raise
            await JSONResponse({"error": "request_too_large"}, status_code=413)(
                scope, receive, send
            )


def build_app(settings: Settings | None = None) -> Starlette:
    settings = settings or Settings()
    settings.assert_production_safe()
    if settings.is_local_stdio:
        raise RuntimeError("local_stdio authentication is only valid for the STDIO server")
    runtime = ToolRuntime(settings)
    public = settings.public_base_url.rstrip("/")
    public_host = urlparse(public).netloc

    auth = AuthSettings(
        issuer_url=AnyHttpUrl(settings.oauth_issuer or public),
        resource_server_url=AnyHttpUrl(f"{public}/mcp"),
        service_documentation_url=AnyHttpUrl(f"{public}/docs"),
        required_scopes=["documents:read"],
    )
    transport_security = TransportSecuritySettings(
        enable_dns_rebinding_protection=True,
        allowed_hosts=sorted({public_host, "127.0.0.1:8787", "localhost:8787"}),
        allowed_origins=list(settings.cors_origins),
    )
    mcp = FastMCP(
        name="WordToolkit",
        instructions=(
            "Round-trip WordprocessingML editor. Open or create a document, use small "
            "document tools, validate before export, and render for visual QA. Equations "
            "must remain native OMML unless the caller explicitly chooses an external fallback."
        ),
        website_url=f"{public}/docs",
        host=settings.bind_host,
        port=settings.port,
        streamable_http_path="/mcp",
        json_response=True,
        stateless_http=True,
        auth=auth,
        token_verifier=WordToolkitTokenVerifier(settings),
        transport_security=transport_security,
    )
    mcp._mcp_server.version = __version__
    register_tools(mcp, runtime)
    mcp_app = mcp.streamable_http_app()

    async def health(_request: Request) -> JSONResponse:
        return JSONResponse(
            {
                "status": "ok",
                "service": "WordToolkit",
                "transport": "MCP Streamable HTTP",
                "authentication": settings.auth_mode,
                "storage": "ephemeral isolated sessions",
            },
            headers={"Cache-Control": "no-store"},
        )

    async def docs(_request: Request) -> JSONResponse:
        return JSONResponse(
            {
                "name": "WordToolkit",
                "mcp_endpoint": f"{public}/mcp",
                "authorization": "OAuth 2.1 bearer token; development_token is local-only",
                "scopes": list(settings.scopes),
                "notice": "LibreOffice rendering is a compatibility check, not pixel-identical Microsoft Word rendering.",
            },
            headers={"Cache-Control": "no-store"},
        )

    async def protected_resource_metadata(_request: Request) -> JSONResponse:
        return JSONResponse(
            {
                "resource": f"{public}/mcp",
                "authorization_servers": [settings.oauth_issuer or public],
                "scopes_supported": list(settings.scopes),
                "bearer_methods_supported": ["header"],
                "resource_documentation": f"{public}/docs",
            },
            headers={"Cache-Control": "public, max-age=300"},
        )

    async def download_artifact(request: Request) -> Response:
        artifact_id = request.path_params["artifact_id"]
        owner = request.query_params.get("owner", "")
        signature = request.query_params.get("sig", "")
        try:
            expires = int(request.query_params.get("expires", "0"))
        except ValueError:
            expires = 0
        if not runtime.verify_artifact_signature(artifact_id, owner, expires, signature):
            return JSONResponse({"error": "invalid_or_expired_download"}, status_code=403)
        artifact = runtime.store.artifacts.get(artifact_id)
        if (
            artifact is None
            or artifact.owner != owner
            or artifact.expires_at < time.time()
            or not artifact.path.exists()
            or not artifact.path.resolve().is_relative_to(runtime.store.root)
        ):
            return JSONResponse({"error": "artifact_not_found"}, status_code=404)
        return FileResponse(
            path=artifact.path,
            media_type=artifact.mime_type,
            filename=artifact.filename,
            headers={
                "Cache-Control": "private, no-store, max-age=0",
                "X-Content-Type-Options": "nosniff",
                "Content-Security-Policy": "default-src 'none'; sandbox",
            },
        )

    @asynccontextmanager
    async def lifespan(app: Starlette):
        del app
        stop = asyncio.Event()

        async def cleanup_loop() -> None:
            while not stop.is_set():
                try:
                    await asyncio.wait_for(stop.wait(), timeout=settings.cleanup_interval_seconds)
                except TimeoutError:
                    await runtime.store.cleanup_expired()

        task = asyncio.create_task(cleanup_loop(), name="wordtoolkit-cleanup")
        async with mcp_app.router.lifespan_context(mcp_app):
            try:
                yield {"runtime": runtime, "mcp": mcp}
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

    application = Starlette(
        debug=settings.environment == "development",
        routes=[
            Route("/health", health, methods=["GET"]),
            Route("/docs", docs, methods=["GET"]),
            Route(
                "/.well-known/oauth-protected-resource",
                protected_resource_metadata,
                methods=["GET"],
            ),
            Route("/v1/artifacts/{artifact_id:str}/download", download_artifact, methods=["GET"]),
            Mount("/", app=mcp_app),
        ],
        middleware=[Middleware(RequestSizeLimitMiddleware, max_bytes=settings.max_request_bytes)],
        lifespan=lifespan,
    )
    application.state.wordtoolkit_runtime = runtime
    application.state.wordtoolkit_mcp = mcp
    return application


app = build_app()


def main() -> None:
    settings = Settings()
    uvicorn.run(
        "wordtoolkit.server.app:app",
        host=settings.bind_host,
        port=settings.port,
        proxy_headers=True,
        forwarded_allow_ips="127.0.0.1",
        log_level="info",
    )


if __name__ == "__main__":
    main()
