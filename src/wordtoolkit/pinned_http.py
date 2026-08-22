"""Small httpx transport that pins DNS resolution for one HTTP hop.

The request origin remains the original hostname, so httpcore still performs
normal certificate validation and sends the correct TLS SNI. Only the TCP
connect destination is replaced with the caller-approved address.
"""

from __future__ import annotations

import ipaddress
import typing

import httpcore
import httpx
from httpcore._backends.auto import AutoBackend
from httpx._config import DEFAULT_LIMITS, create_ssl_context
from httpx._transports.default import map_httpcore_exceptions


class PinnedAsyncNetworkBackend(httpcore.AsyncNetworkBackend):
    """Delegate all networking except TCP hostname resolution to AutoBackend."""

    def __init__(self, hostname: str, pinned_address: str):
        self.hostname = hostname
        self.pinned_address = str(ipaddress.ip_address(pinned_address))
        self._delegate = AutoBackend()

    async def connect_tcp(
        self,
        host: str,
        port: int,
        timeout: float | None = None,
        local_address: str | None = None,
        socket_options: typing.Iterable[httpcore.SOCKET_OPTION] | None = None,
    ) -> httpcore.AsyncNetworkStream:
        if host != self.hostname:
            raise RuntimeError("Pinned transport received an unexpected origin host")
        return await self._delegate.connect_tcp(
            self.pinned_address,
            port,
            timeout=timeout,
            local_address=local_address,
            socket_options=socket_options,
        )

    async def connect_unix_socket(self, *args, **kwargs):
        raise RuntimeError("Pinned transport does not support Unix sockets")

    async def sleep(self, seconds: float) -> None:
        await self._delegate.sleep(seconds)


class _AsyncResponseBody(typing.Protocol):
    def __aiter__(self) -> typing.AsyncIterator[bytes]: ...

    async def aclose(self) -> None: ...


class _AsyncResponseStream(httpx.AsyncByteStream):
    def __init__(self, stream: _AsyncResponseBody):
        self._stream = stream

    async def __aiter__(self):
        async for chunk in self._stream:
            yield chunk

    async def aclose(self):
        await self._stream.aclose()


class PinnedAsyncTransport(httpx.AsyncBaseTransport):
    """An httpx transport for exactly one already-validated origin and address.

    This adapter intentionally follows the small request/response bridge in
    httpx 0.28.1 and httpcore 1.0.9, which are locked by ``uv.lock``. Tests must
    fail on dependency upgrades that change those private adapter contracts.
    """

    def __init__(self, hostname: str, pinned_address: str, *, verify: bool = True):
        self.hostname = hostname
        self.backend = PinnedAsyncNetworkBackend(hostname, pinned_address)
        ssl_context = create_ssl_context(verify=verify, trust_env=False)
        self._pool = httpcore.AsyncConnectionPool(
            ssl_context=ssl_context,
            max_connections=DEFAULT_LIMITS.max_connections,
            max_keepalive_connections=DEFAULT_LIMITS.max_keepalive_connections,
            keepalive_expiry=DEFAULT_LIMITS.keepalive_expiry,
            network_backend=self.backend,
        )

    async def __aenter__(self):
        await self._pool.__aenter__()
        return self

    async def __aexit__(self, exc_type=None, exc_value=None, traceback=None):
        with map_httpcore_exceptions():
            await self._pool.__aexit__(exc_type, exc_value, traceback)

    async def handle_async_request(self, request: httpx.Request) -> httpx.Response:
        if request.url.host != self.hostname:
            raise RuntimeError("Pinned transport received an unexpected request host")
        req = httpcore.Request(
            method=request.method,
            url=httpcore.URL(
                scheme=request.url.raw_scheme,
                host=request.url.raw_host,
                port=request.url.port,
                target=request.url.raw_path,
            ),
            headers=request.headers.raw,
            content=request.stream,
            extensions=request.extensions,
        )
        with map_httpcore_exceptions():
            response = await self._pool.handle_async_request(req)
        return httpx.Response(
            status_code=response.status,
            headers=response.headers,
            stream=_AsyncResponseStream(typing.cast(_AsyncResponseBody, response.stream)),
            extensions=response.extensions,
            request=request,
        )
