from __future__ import annotations

from pathlib import Path

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="WORDTOOLKIT_", env_file=".env", extra="ignore")

    environment: str = "development"
    public_base_url: str = "http://127.0.0.1:8787"
    bind_host: str = "127.0.0.1"
    port: int = 8787
    storage_root: Path = Path("/tmp/wordtoolkit")
    session_ttl_seconds: int = Field(default=3600, ge=300, le=86_400)
    artifact_ttl_seconds: int = Field(default=3600, ge=300, le=604_800)
    cleanup_interval_seconds: int = Field(default=300, ge=30, le=3600)
    max_sessions_per_owner: int = Field(default=5, ge=1, le=100)
    max_documents_per_session: int = Field(default=20, ge=1, le=500)
    max_artifacts_per_owner: int = Field(default=100, ge=1, le=5000)
    max_artifact_bytes_per_owner: int = Field(
        default=1024 * 1024 * 1024, ge=1024 * 1024, le=50 * 1024 * 1024 * 1024
    )
    max_session_bytes: int = Field(
        default=512 * 1024 * 1024, ge=1024 * 1024, le=10 * 1024 * 1024 * 1024
    )
    max_upload_bytes: int = Field(default=50 * 1024 * 1024, ge=1024, le=200 * 1024 * 1024)
    max_request_bytes: int = Field(default=2 * 1024 * 1024, ge=1024, le=20 * 1024 * 1024)
    max_zip_entries: int = Field(default=5000, ge=10, le=20_000)
    max_uncompressed_bytes: int = Field(default=250 * 1024 * 1024, ge=1024, le=1024 * 1024 * 1024)
    max_compression_ratio: float = Field(default=100.0, ge=2, le=1000)
    render_timeout_seconds: int = Field(default=90, ge=5, le=600)
    openxml_validator_path: Path = Path("/usr/local/bin/wordtoolkit-openxml-validator")
    openxml_validator_timeout_seconds: int = Field(default=45, ge=5, le=300)
    http_download_timeout_seconds: int = Field(default=30, ge=5, le=120)
    allowed_upload_host_suffixes: str = (
        ".oaiusercontent.com,.blob.core.windows.net,.amazonaws.com,localhost,127.0.0.1"
    )
    auth_mode: str = "development_token"
    development_bearer_token: str = "change-me-before-deployment"
    oauth_issuer: str = ""
    oauth_audience: str = ""
    oauth_jwks_url: str = ""
    oauth_scopes: str = "documents:read documents:write"
    signing_secret: str = "change-me-to-a-long-random-secret"
    cors_allowed_origins: str = "https://chatgpt.com"

    @field_validator("auth_mode")
    @classmethod
    def validate_auth_mode(cls, value: str) -> str:
        if value not in {"development_token", "oauth_jwt", "local_stdio"}:
            raise ValueError("auth_mode must be development_token, oauth_jwt or local_stdio")
        return value

    @property
    def is_local_stdio(self) -> bool:
        return self.auth_mode == "local_stdio"

    @property
    def upload_host_suffixes(self) -> tuple[str, ...]:
        return tuple(x.strip().lower() for x in self.allowed_upload_host_suffixes.split(",") if x)

    @property
    def cors_origins(self) -> tuple[str, ...]:
        return tuple(x.strip() for x in self.cors_allowed_origins.split(",") if x)

    @property
    def scopes(self) -> tuple[str, ...]:
        return tuple(self.oauth_scopes.split())

    def assert_production_safe(self) -> None:
        if self.environment != "production":
            return
        problems: list[str] = []
        if not self.public_base_url.startswith("https://"):
            problems.append("public_base_url must use HTTPS")
        if self.auth_mode != "oauth_jwt":
            problems.append("production requires oauth_jwt")
        if not self.oauth_issuer or not self.oauth_audience or not self.oauth_jwks_url:
            problems.append("oauth_issuer, oauth_audience and oauth_jwks_url are required")
        if self.signing_secret.startswith("change-me") or len(self.signing_secret) < 32:
            problems.append("signing_secret must be at least 32 random characters")
        local_upload_hosts = {"localhost", "127.0.0.1", "::1"}
        configured_upload_hosts = {value.lstrip(".") for value in self.upload_host_suffixes}
        if configured_upload_hosts & local_upload_hosts:
            problems.append(
                "production upload allowlist must not include localhost, 127.0.0.1 or ::1"
            )
        if problems:
            raise RuntimeError("Unsafe production configuration: " + "; ".join(problems))
