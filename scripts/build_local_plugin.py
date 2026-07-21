from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PLUGIN_SOURCE = ROOT / "plugin" / "wordtoolkit"
DEFAULT_OUTPUT = ROOT / "dist" / "wordtoolkit"
PLUGIN_VERSION = json.loads((PLUGIN_SOURCE / ".codex-plugin" / "plugin.json").read_text("utf-8"))[
    "version"
]


def _copy_runtime(destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    for name in ("pyproject.toml", "uv.lock", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(ROOT / name, destination / name)
    shutil.copytree(
        ROOT / "src",
        destination / "src",
        ignore=shutil.ignore_patterns("__pycache__", "*.pyc", "*.pyo"),
    )
    runtime_scripts = destination / "scripts"
    runtime_scripts.mkdir(parents=True, exist_ok=True)
    shutil.copy2(
        ROOT / "scripts" / "audit_member_virtual_tools.py",
        runtime_scripts / "audit_member_virtual_tools.py",
    )


def _build_validator(destination: Path) -> None:
    executable = shutil.which("dotnet")
    if executable is None:
        raise RuntimeError("dotnet SDK was not found; omit --build-validator or install .NET 8")
    runtime = "win-x64" if os.name == "nt" else "linux-x64"
    output = destination / "tools" / "openxml-validator"
    output.mkdir(parents=True, exist_ok=True)
    command = [
        executable,
        "publish",
        str(ROOT / "tools" / "OpenXmlValidator" / "OpenXmlValidator.csproj"),
        "-c",
        "Release",
        "-r",
        runtime,
        "--self-contained",
        "false",
        "-p:PublishSingleFile=true",
        "-p:DebugType=None",
        "-o",
        str(output),
    ]
    subprocess.run(command, cwd=ROOT, check=True)


def _write_zip(plugin: Path, archive: Path) -> None:
    archive.parent.mkdir(parents=True, exist_ok=True)
    if archive.exists():
        archive.unlink()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        for file in sorted(plugin.rglob("*")):
            if file.is_file():
                bundle.write(file, Path("wordtoolkit") / file.relative_to(plugin))


def build(output: Path, *, build_validator: bool, archive: Path | None) -> dict[str, object]:
    resolved_output = output.resolve()
    resolved_dist = (ROOT / "dist").resolve()
    if resolved_output != resolved_dist and not resolved_output.is_relative_to(resolved_dist):
        raise RuntimeError("Output must stay inside the repository dist directory")
    if output.exists():
        shutil.rmtree(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(PLUGIN_SOURCE, output)
    runtime = output / "runtime"
    _copy_runtime(runtime)
    if build_validator:
        _build_validator(runtime)
    if archive is not None:
        _write_zip(output, archive)
    manifest = json.loads((output / ".codex-plugin" / "plugin.json").read_text("utf-8"))
    files = [path for path in output.rglob("*") if path.is_file()]
    return {
        "name": manifest["name"],
        "version": manifest["version"],
        "output": str(output.resolve()),
        "archive": str(archive.resolve()) if archive else None,
        "files": len(files),
        "bytes": sum(path.stat().st_size for path in files),
        "validator_bundled": any(
            path.name.startswith("wordtoolkit-openxml-validator")
            for path in (runtime / "tools" / "openxml-validator").glob("*")
        ),
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Build the self-contained local WordToolkit plugin"
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--build-validator", action="store_true")
    parser.add_argument(
        "--archive",
        type=Path,
        default=ROOT / "dist" / f"WordToolkit-{PLUGIN_VERSION}-local.zip",
    )
    args = parser.parse_args()
    print(
        json.dumps(
            build(
                args.output,
                build_validator=args.build_validator,
                archive=args.archive,
            ),
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
