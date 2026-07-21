from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from typing import Any


class ErrorCode(StrEnum):
    AUTH_REQUIRED = "AUTH_REQUIRED"
    AUTH_FORBIDDEN = "AUTH_FORBIDDEN"
    INVALID_INPUT = "INVALID_INPUT"
    UNSAFE_PATH = "UNSAFE_PATH"
    UNSAFE_ARCHIVE = "UNSAFE_ARCHIVE"
    UNSAFE_XML = "UNSAFE_XML"
    UNSAFE_RELATIONSHIP = "UNSAFE_RELATIONSHIP"
    UNSUPPORTED_FORMAT = "UNSUPPORTED_FORMAT"
    DOCUMENT_NOT_FOUND = "DOCUMENT_NOT_FOUND"
    SESSION_NOT_FOUND = "SESSION_NOT_FOUND"
    VERSION_CONFLICT = "VERSION_CONFLICT"
    EQUATION_INVALID = "EQUATION_INVALID"
    OOXML_INVALID = "OOXML_INVALID"
    RENDERER_UNAVAILABLE = "RENDERER_UNAVAILABLE"
    RENDER_TIMEOUT = "RENDER_TIMEOUT"
    EXTERNAL_TOOL_FAILED = "EXTERNAL_TOOL_FAILED"
    LIVE_WORD_UNAVAILABLE = "LIVE_WORD_UNAVAILABLE"
    LIMIT_EXCEEDED = "LIMIT_EXCEEDED"
    INTERNAL_ERROR = "INTERNAL_ERROR"


@dataclass(slots=True)
class WordToolkitError(Exception):
    code: ErrorCode
    message: str
    details: dict[str, Any] | None = None
    retryable: bool = False

    def __str__(self) -> str:
        return f"{self.code}: {self.message}"

    def to_dict(self) -> dict[str, Any]:
        return {
            "ok": False,
            "error": {
                "code": self.code.value,
                "message": self.message,
                "details": self.details or {},
                "retryable": self.retryable,
            },
        }


def ok(data: Any, *, warnings: list[str] | None = None) -> dict[str, Any]:
    return {"ok": True, "data": data, "warnings": warnings or []}
