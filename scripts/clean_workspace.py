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


def _keep_paths(keeps: tuple[str, ...]) -> tuple[Path, ...]:
    normalized: list[Path] = []
    for value in keeps:
        target = _assert_inside(ROOT, ROOT / value)
        if target.is_symlink() and not target.resolve(strict=False).is_relative_to(ROOT.resolve()):
            raise RuntimeError(f"Cleanup keep path escapes repository root: {value}")
        normalized.append(target)
    return tuple(normalized)


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


def _candidates(*, include_venv: bool, keeps: tuple[str, ...] = ()) -> list[Path]:
    keep_paths = _keep_paths(keeps)
    version = _project_version()
    keep_dist = {
        ".gitignore",
        "wordtoolkit",
        f"WordToolkit-{version}-native-win-x64.zip",
    }
    candidates: list[Path] = []
    dist = ROOT / "dist"
    if dist.is_dir():
        candidates.extend(child for child in dist.iterdir() if child.name not in keep_dist)
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
            ".tmp-introspect",
            ".tmp-schema-inspect",
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
        # Never turn a reparse point into its target and delete the target.
        if candidate.is_symlink():
            continue
        safe = _assert_inside(ROOT, candidate)
        if safe.exists() or safe.is_symlink():
            unique[str(safe).casefold()] = safe
    selected: list[Path] = []
    ordered = sorted(
        unique.values(),
        key=lambda item: (len(item.parts), str(item).casefold()),
    )

    def expand(candidate: Path) -> list[Path]:
        """Descend through a generated directory when only part is kept."""
        if any(candidate == keep or candidate.is_relative_to(keep) for keep in keep_paths):
            return []
        descendants = [keep for keep in keep_paths if keep.is_relative_to(candidate)]
        if not descendants or not candidate.is_dir() or candidate.is_symlink():
            return [candidate]
        result: list[Path] = []
        for child in candidate.iterdir():
            safe_child = _assert_inside(ROOT, child)
            result.extend(expand(safe_child))
        return result

    for candidate in ordered:
        expanded = expand(candidate)
        if not expanded:
            continue
        for item in expanded:
            if any(item.is_relative_to(parent) for parent in selected):
                continue
            selected.append(item)
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


def clean(*, apply: bool, include_venv: bool, keeps: tuple[str, ...] = ()) -> dict[str, object]:
    candidates = [_measure(path) for path in _candidates(include_venv=include_venv, keeps=keeps)]
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
    parser.add_argument(
        "--keep",
        action="append",
        default=[],
        metavar="REPO_RELATIVE_PATH",
        help="Preserve a repository-relative file or directory; repeatable.",
    )
    args = parser.parse_args()
    print(
        json.dumps(
            clean(
                apply=args.apply,
                include_venv=args.include_venv,
                keeps=tuple(args.keep),
            ),
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
