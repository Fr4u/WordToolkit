from __future__ import annotations

import asyncio
import hmac
import os
import time

import jwt
from mcp.server.auth.middleware.auth_context import get_access_token
from mcp.server.auth.provider import AccessToken

from .config import Settings
from .errors import ErrorCode, WordToolkitError


class WordToolkitTokenVerifier:
    def __init__(self, settings: Settings):
        self.settings = settings
        self._jwk_client = (
            jwt.PyJWKClient(
                settings.oauth_jwks_url
                or settings.oauth_issuer.rstrip("/") + "/.well-known/jwks.json"
            )
            if settings.auth_mode == "oauth_jwt"
            else None
        )

    async def verify_token(self, token: str) -> AccessToken | None:
        if self.settings.auth_mode == "development_token":
            if not hmac.compare_digest(token, self.settings.development_bearer_token):
                return None
            return AccessToken(
                token="redacted",
                client_id="wordtoolkit-development",
                scopes=list(self.settings.scopes),
                expires_at=int(time.time()) + 3600,
                resource=self.settings.public_base_url,
                subject="development-user",
                claims={"sub": "development-user"},
            )
        if self.settings.auth_mode == "local_stdio":
            return None
        try:
            if self._jwk_client is None:
                return None
            signing_key = await asyncio.to_thread(self._jwk_client.get_signing_key_from_jwt, token)
            claims = jwt.decode(
                token,
                signing_key.key,
                algorithms=["RS256", "ES256", "EdDSA"],
                audience=self.settings.oauth_audience,
                issuer=self.settings.oauth_issuer,
                options={"require": ["exp", "iat", "sub"]},
            )
        except Exception:
            return None
        raw_scopes = claims.get("scope", claims.get("scp", []))
        scopes = raw_scopes.split() if isinstance(raw_scopes, str) else list(raw_scopes)
        return AccessToken(
            token="redacted",
            client_id=str(claims.get("azp", claims.get("client_id", "chatgpt"))),
            scopes=scopes,
            expires_at=int(claims["exp"]),
            resource=self.settings.public_base_url,
            subject=str(claims["sub"]),
            claims=claims,
        )


def _local_stdio_subject() -> str | None:
    if os.environ.get("WORDTOOLKIT_AUTH_MODE", "").strip().lower() != "local_stdio":
        return None
    return os.environ.get("WORDTOOLKIT_LOCAL_SUBJECT", "local-codex-user").strip() or None


def current_subject() -> str:
    token = get_access_token()
    if token is not None and token.subject:
        return token.subject
    local_subject = _local_stdio_subject()
    if local_subject:
        return local_subject
    if token is None or not token.subject:
        raise WordToolkitError(ErrorCode.AUTH_REQUIRED, "Authentication is required")
    return token.subject


def require_scope(scope: str) -> None:
    token = get_access_token()
    if token is None and _local_stdio_subject():
        return
    if token is None:
        raise WordToolkitError(ErrorCode.AUTH_REQUIRED, "Authentication is required")
    if scope not in token.scopes:
        raise WordToolkitError(
            ErrorCode.AUTH_FORBIDDEN, "Required OAuth scope is missing", {"scope": scope}
        )
