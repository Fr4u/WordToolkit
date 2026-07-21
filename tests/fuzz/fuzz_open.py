"""Re-export the vendored byte-level fuzz target."""

from tests.upstream.fuzz.fuzz_open import TestOneInput

__all__ = ["TestOneInput"]
