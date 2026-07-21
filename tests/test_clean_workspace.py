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


def test_clean_workspace_removes_venv_only_when_requested(
    tmp_path: Path, monkeypatch
) -> None:
    _write_project(tmp_path)
    venv = tmp_path / ".venv"
    venv.mkdir()
    (venv / "pyvenv.cfg").write_text("home = test", encoding="utf-8")
    monkeypatch.setattr(clean_workspace, "ROOT", tmp_path)

    result = clean_workspace.clean(apply=True, include_venv=True)

    assert result["targets"] == 1
    assert not venv.exists()
