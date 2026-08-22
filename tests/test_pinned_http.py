import httpcore
import httpx
import pytest

from wordtoolkit.pinned_http import PinnedAsyncNetworkBackend, PinnedAsyncTransport


def test_private_transport_adapter_versions_are_deliberately_locked():
    assert httpx.__version__ == "0.28.1"
    assert httpcore.__version__ == "1.0.9"


@pytest.mark.asyncio
async def test_backend_replaces_only_tcp_host(monkeypatch):
    calls = []

    class Delegate:
        async def connect_tcp(self, host, port, **kwargs):
            calls.append((host, port, kwargs))
            return object()

    backend = PinnedAsyncNetworkBackend("files.example", "93.184.216.34")
    backend._delegate = Delegate()
    await backend.connect_tcp("files.example", 443, timeout=2)
    assert calls[0][0] == "93.184.216.34"
    with pytest.raises(RuntimeError):
        await backend.connect_tcp("other.example", 443)


@pytest.mark.asyncio
async def test_transport_pins_tcp_but_preserves_original_tls_hostname():
    calls = []

    class Stream(httpcore.AsyncNetworkStream):
        def __init__(self):
            self.response = b"HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"

        async def read(self, max_bytes, timeout=None):
            chunk, self.response = self.response[:max_bytes], self.response[max_bytes:]
            return chunk

        async def write(self, buffer, timeout=None):
            return None

        async def aclose(self):
            return None

        async def start_tls(self, ssl_context, server_hostname=None, timeout=None):
            calls.append(("tls", server_hostname))
            return self

    class Delegate:
        async def connect_tcp(self, host, port, **kwargs):
            calls.append(("tcp", host, port))
            return Stream()

    transport = PinnedAsyncTransport("files.example", "93.184.216.34")
    transport.backend._delegate = Delegate()
    async with httpx.AsyncClient(transport=transport, trust_env=False) as client:
        response = await client.get("https://files.example/file.docx")

    assert response.status_code == 200
    assert ("tcp", "93.184.216.34", 443) in calls
    assert ("tls", "files.example") in calls


@pytest.mark.asyncio
async def test_transport_rejects_a_different_origin():
    transport = PinnedAsyncTransport("files.example", "93.184.216.34")
    with pytest.raises(RuntimeError):
        await transport.handle_async_request(httpx.Request("GET", "https://other.example/x"))
    await transport.__aexit__(None, None, None)
