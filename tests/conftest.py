"""Compatibility exports for the vendored docx-mcp regression suite."""

from tests.upstream.conftest import *  # noqa: F403
from tests.upstream.conftest import _build_fixture, _build_mike_corpus

__all__ = ["_build_fixture", "_build_mike_corpus"]
