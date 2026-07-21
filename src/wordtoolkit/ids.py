from __future__ import annotations

import base64
import hashlib
import secrets


def opaque_id(prefix: str, nbytes: int = 16) -> str:
    token = base64.b32encode(secrets.token_bytes(nbytes)).decode("ascii").rstrip("=").lower()
    return f"{prefix}_{token}"


def owner_key(subject: str) -> str:
    return hashlib.sha256(subject.encode("utf-8")).hexdigest()[:32]
