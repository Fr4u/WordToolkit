from __future__ import annotations

from pathlib import Path

from scripts import clean_workspace


def _write_project(root: Path, version: str = "9.8.7") -> None:
    (root / "pyproject.toml").write_text(
        f'[project]\nname = "wordtoolkit-test"\nversion = "{version}"\n',
        encoding="utf-8",
    )
    manifest = root / "plugin" / "wordtoolkit" / ".codex-plugin" / "plugin.json"
    manifest.parent.mkdir(parents=True)
    manifest.write_text(f'{{"version":"{version}"}}\n', encoding="utf-8")


def test_clean_workspace_keeps_current_release_and_venv_by_default(
    tmp_path: Path, monkeypatch
) -> None:
    _write_project(tmp_path)
    current_release = tmp_path / "dist" / "wordtoolkit"
    current_archive = tmp_path / "dist" / "WordToolkit-9.8.7-native-win-x64.zip"
    old_release = tmp_path / "dist" / "wordtoolkit-1.0.0-release"
    cache = tmp_path / "src" / "wordtoolkit" / "__pycache__"
    nested_cache = tmp_path / "src" / "docx_mcp" / "skill" / "__pycache__"
    venv = tmp_path / ".venv"
    for directory in (current_release, old_release, cache, nested_cache, venv):
        directory.mkdir(parents=True, exist_ok=True)
        (directory / "payload.bin").write_bytes(b"x")
    current_archive.write_bytes(b"zip")
    monkeypatch.setattr(clean_workspace, "ROOT", tmp_path)

    dry_run = clean_workspace.clean(apply=False, include_venv=False)
    assert dry_run["applied"] is False
    assert old_release.exists()
    assert cache.exists()

    result = clean_workspace.clean(apply=True, include_venv=False)

    assert result["applied"] is True
    assert not old_release.exists()
    assert not cache.exists()
    assert not (tmp_path / "src" / "docx_mcp" / "skill").exists()
    assert current_release.exists()
    assert current_archive.exists()
    assert venv.exists()


def test_clean_workspace_removes_venv_only_when_requested(tmp_path: Path, monkeypatch) -> None:
    _write_project(tmp_path)
    venv = tmp_path / ".venv"
    venv.mkdir()
    (venv / "pyvenv.cfg").write_text("home = test", encoding="utf-8")
    monkeypatch.setattr(clean_workspace, "ROOT", tmp_path)

    result = clean_workspace.clean(apply=True, include_venv=True)

    assert result["targets"] == 1
    assert not venv.exists()


def test_clean_workspace_repeatable_keep_preserves_files_and_directories(
    tmp_path: Path, monkeypatch
) -> None:
    _write_project(tmp_path)
    keep_dir = tmp_path / "dist" / "acceptance"
    keep_file = tmp_path / "artifacts" / "evidence.json"
    disposable = tmp_path / "dist" / "old"
    keep_dir.mkdir(parents=True)
    keep_file.parent.mkdir(parents=True)
    keep_file.write_text("evidence", encoding="utf-8")
    disposable.mkdir(parents=True)
    monkeypatch.setattr(clean_workspace, "ROOT", tmp_path)

    dry_run = clean_workspace.clean(
        apply=False, include_venv=False, keeps=("dist/acceptance", "artifacts/evidence.json")
    )
    assert all(
        Path(candidate["path"]) not in {keep_dir, keep_file} for candidate in dry_run["candidates"]
    )
    assert disposable.exists()
    clean_workspace.clean(
        apply=True, include_venv=False, keeps=("dist/acceptance", "artifacts/evidence.json")
    )
    assert keep_dir.exists()
    assert keep_file.exists()
    assert not disposable.exists()


def test_clean_workspace_rejects_root_and_outside_keep(tmp_path: Path, monkeypatch) -> None:
    _write_project(tmp_path)
    monkeypatch.setattr(clean_workspace, "ROOT", tmp_path)
    for keep in (".", "..", str(tmp_path.parent / "outside")):
        try:
            clean_workspace.clean(apply=False, include_venv=False, keeps=(keep,))
        except RuntimeError:
            pass
        else:
            raise AssertionError(f"keep path was accepted: {keep}")


def test_clean_workspace_rejects_symlink_keep_escaping_root(tmp_path: Path, monkeypatch) -> None:
    _write_project(tmp_path)
    outside = tmp_path.parent / "outside-target"
    outside.mkdir()
    link = tmp_path / "dist" / "escape"
    link.parent.mkdir()
    try:
        link.symlink_to(outside, target_is_directory=True)
    except (OSError, NotImplementedError):
        return
    monkeypatch.setattr(clean_workspace, "ROOT", tmp_path)
    try:
        clean_workspace.clean(apply=False, include_venv=False, keeps=("dist/escape",))
    except RuntimeError:
        pass
    else:
        raise AssertionError("escaping symlink keep path was accepted")
