from __future__ import annotations

import argparse
import json
import os
import shutil
import stat
from dataclasses import asdict, dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class Candidate:
    path: str
    files: int
    bytes: int
    kind: str


def _project_version() -> str:
    manifest = json.loads(
        (ROOT / "plugin" / "wordtoolkit" / ".codex-plugin" / "plugin.json").read_text(
            encoding="utf-8"
        )
    )
    return str(manifest["version"])


def _assert_inside(root: Path, target: Path) -> Path:
    resolved_root = root.resolve()
    resolved_target = target.resolve(strict=False)
    if resolved_target == resolved_root or not resolved_target.is_relative_to(resolved_root):
        raise RuntimeError(f"Cleanup target escapes or equals repository root: {resolved_target}")
    return resolved_target


def _measure(path: Path) -> Candidate:
    if path.is_symlink():
        return Candidate(str(path), 1, path.lstat().st_size, "symlink")
    if path.is_file():
        return Candidate(str(path), 1, path.stat().st_size, "file")
    files = 0
    size = 0
    for directory, _subdirectories, names in os.walk(path, followlinks=False):
        base = Path(directory)
        for name in names:
            item = base / name
            try:
                size += item.lstat().st_size
                files += 1
            except FileNotFoundError:
                continue
    return Candidate(str(path), files, size, "directory")


def _candidates(*, include_venv: bool) -> list[Path]:
    version = _project_version()
    keep_dist = {
        ".gitignore",
        "wordtoolkit",
        f"WordToolkit-{version}-native-win-x64.zip",
    }
    candidates: list[Path] = []
    dist = ROOT / "dist"
    if dist.is_dir():
        candidates.extend(
            child for child in dist.iterdir() if child.name not in keep_dist
        )
    candidates.extend(
        ROOT / relative
        for relative in (
            "artifacts",
            ".coverage",
            "htmlcov",
            ".hypothesis",
            ".mypy_cache",
            ".pytest_cache",
            ".ruff_cache",
            ".schema-storage",
            ".schema-storage-local",
            ".tmp-field-count",
            ".tmp-live-012",
            ".tmp-live-012-fast",
            ".tmp-live-012-fast2",
            ".tmp-live-012-replace",
            ".tmp-live-fidelity",
            ".tmp-tool-count",
            "examples/generated/.work",
            "src/docx_mcp/skill",
            "tools/OpenXmlValidator/bin",
            "tools/OpenXmlValidator/obj",
            "native/WordToolkit.Native/bin",
            "native/WordToolkit.Native/obj",
            "native/WordToolkit.Native.Tests/bin",
            "native/WordToolkit.Native.Tests/obj",
        )
    )
    for cache in ROOT.rglob("__pycache__"):
        relative = cache.relative_to(ROOT)
        if ".venv" not in relative.parts:
            candidates.append(cache)
    if include_venv:
        candidates.append(ROOT / ".venv")
    unique: dict[str, Path] = {}
    for candidate in candidates:
        safe = _assert_inside(ROOT, candidate)
        if safe.exists() or safe.is_symlink():
            unique[str(safe).casefold()] = safe
    selected: list[Path] = []
    ordered = sorted(
        unique.values(),
        key=lambda item: (len(item.parts), str(item).casefold()),
    )
    for candidate in ordered:
        if any(candidate.is_relative_to(parent) for parent in selected):
            continue
        selected.append(candidate)
    return selected


def _remove(path: Path) -> None:
    if path.is_symlink() or path.is_file():
        path.chmod(stat.S_IWRITE)
        path.unlink()
        return

    def remove_readonly(function, blocked_path, _exception) -> None:
        os.chmod(blocked_path, stat.S_IWRITE)
        function(blocked_path)

    shutil.rmtree(path, onexc=remove_readonly)


def clean(*, apply: bool, include_venv: bool) -> dict[str, object]:
    candidates = [_measure(path) for path in _candidates(include_venv=include_venv)]
    if apply:
        for candidate in candidates:
            path = _assert_inside(ROOT, Path(candidate.path))
            _remove(path)
    return {
        "applied": apply,
        "repository": str(ROOT),
        "project_version": _project_version(),
        "targets": len(candidates),
        "files": sum(candidate.files for candidate in candidates),
        "bytes": sum(candidate.bytes for candidate in candidates),
        "candidates": [asdict(candidate) for candidate in candidates],
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Remove generated WordToolkit build history and local caches safely."
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Perform deletion. Without this flag the command is a dry run.",
    )
    parser.add_argument(
        "--include-venv",
        action="store_true",
        help="Also remove the repository-local virtual environment.",
    )
    args = parser.parse_args()
    print(
        json.dumps(
            clean(apply=args.apply, include_venv=args.include_venv),
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
