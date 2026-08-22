from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image
from pypdf import PdfReader

from ..config import Settings
from ..errors import ErrorCode, WordToolkitError


def _windows_app_path(executable: str) -> str | None:
    if sys.platform != "win32":
        return None
    try:
        import winreg

        for hive, key in (
            (
                winreg.HKEY_LOCAL_MACHINE,
                rf"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executable}",
            ),
            (
                winreg.HKEY_LOCAL_MACHINE,
                rf"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\{executable}",
            ),
            (
                winreg.HKEY_CURRENT_USER,
                rf"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executable}",
            ),
        ):
            try:
                with winreg.OpenKey(hive, key) as handle:
                    value, _kind = winreg.QueryValueEx(handle, "")
                if Path(value).is_file():
                    return value
            except OSError:
                continue
    except ImportError:
        return None
    return None


def _find_executable(*names: str) -> str | None:
    for name in names:
        candidates = (
            (f"{name}.exe", name)
            if os.name == "nt" and not name.lower().endswith(".exe")
            else (name,)
        )
        found = next((path for candidate in candidates if (path := shutil.which(candidate))), None)
        if found:
            return found
        found = _windows_app_path(f"{name}.exe" if not name.endswith(".exe") else name)
        if found:
            return found
    return None


def _renderer_environment(profile: str) -> dict[str, str]:
    allowed = {
        "PATH",
        "SYSTEMROOT",
        "WINDIR",
        "TEMP",
        "TMP",
        "LOCALAPPDATA",
        "APPDATA",
        "USERPROFILE",
        "PROGRAMFILES",
        "PROGRAMFILES(X86)",
        "COMMONPROGRAMFILES",
        "COMMONPROGRAMFILES(X86)",
        "PROGRAMDATA",
        "ALLUSERSPROFILE",
        "PUBLIC",
        "SYSTEMDRIVE",
        "HOMEDRIVE",
        "HOMEPATH",
        "COMSPEC",
        "PATHEXT",
        "OS",
        "PROCESSOR_ARCHITECTURE",
    }
    environment = {key: value for key, value in os.environ.items() if key.upper() in allowed}
    environment.update({"HOME": profile, "LANG": "C.UTF-8"})
    return environment


def _subprocess_flags() -> int:
    if sys.platform != "win32":
        return 0
    return subprocess.CREATE_NO_WINDOW


class DocumentRenderer:
    def __init__(self, settings: Settings):
        self.settings = settings

    def to_pdf(self, docx: Path, output: Path) -> dict:
        executable = _find_executable("libreoffice", "soffice")
        if executable is None:
            raise WordToolkitError(
                ErrorCode.RENDERER_UNAVAILABLE, "LibreOffice headless was not found"
            )
        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="wordtoolkit_lo_") as work:
            workdir = Path(work)
            profile = workdir / "profile"
            render_dir = workdir / "output"
            profile.mkdir()
            render_dir.mkdir()
            staged_docx = workdir / "document.docx"
            shutil.copy2(docx, staged_docx)
            command = [
                executable,
                f"-env:UserInstallation={profile.as_uri()}",
                "--headless",
                "--nologo",
                "--nodefault",
                "--nolockcheck",
                "--convert-to",
                "pdf:writer_pdf_Export",
                "--outdir",
                str(render_dir),
                str(staged_docx),
            ]
            try:
                result = subprocess.run(
                    command,
                    stdin=subprocess.DEVNULL,
                    capture_output=True,
                    text=True,
                    timeout=self.settings.render_timeout_seconds,
                    check=False,
                    env=_renderer_environment(str(profile)),
                    creationflags=_subprocess_flags(),
                )
            except subprocess.TimeoutExpired as exc:
                raise WordToolkitError(
                    ErrorCode.RENDER_TIMEOUT, "LibreOffice conversion timed out", retryable=True
                ) from exc
            if result.returncode != 0:
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED,
                    "LibreOffice failed to render the document",
                    {"returncode": result.returncode},
                )
            generated = render_dir / "document.pdf"
            if not generated.exists():
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED, "LibreOffice did not produce a PDF"
                )
            shutil.copy2(generated, output)
        return {
            "pdf": str(output),
            "bytes": output.stat().st_size,
            "renderer": "LibreOffice headless",
            "compatibility_notice": "Microsoft Word and LibreOffice can paginate and shape fonts differently; this is not pixel-identical Word rendering.",
        }

    def pages_to_png(self, pdf: Path, output_dir: Path, *, dpi: int = 144) -> list[Path]:
        executable = _find_executable("pdftoppm")
        if executable is None:
            raise WordToolkitError(ErrorCode.RENDERER_UNAVAILABLE, "Poppler pdftoppm was not found")
        output_dir.mkdir(parents=True, exist_ok=True)
        prefix = output_dir / "page"
        # pdftoppm changes zero-padding with the page count.  Reusing a preview
        # directory could otherwise mix stale page-01.png files with fresh
        # page-1.png files and make the audit inspect the wrong document.
        for stale in output_dir.glob(f"{prefix.name}-*.png"):
            stale.unlink()
        # Render pages one at a time.  Affected Poppler development builds can
        # otherwise leave worker processes flushing one page after the parent
        # command has exited, producing intermittently truncated PNGs.
        pages = []
        for page_number in range(1, len(PdfReader(str(pdf)).pages) + 1):
            page = output_dir / f"page-{page_number}.png"
            result = subprocess.run(
                [
                    executable,
                    "-png",
                    "-r",
                    str(dpi),
                    "-f",
                    str(page_number),
                    "-l",
                    str(page_number),
                    "-singlefile",
                    str(pdf),
                    str(page.with_suffix("")),
                ],
                stdin=subprocess.DEVNULL,
                capture_output=True,
                text=True,
                timeout=self.settings.render_timeout_seconds,
                check=False,
                creationflags=_subprocess_flags(),
            )
            if result.returncode != 0:
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED,
                    "PDF page rendering failed",
                    {"page": page_number},
                )
            try:
                with Image.open(page) as image:
                    image.load()
            except OSError as exc:
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED,
                    "Rendered PNG is truncated",
                    {"page": page_number},
                ) from exc
            pages.append(page)
        return pages

    def visual_audit(self, pdf: Path, pages: list[Path]) -> dict:
        reader = PdfReader(str(pdf))
        issues = []
        for index, page in enumerate(reader.pages):
            text = (page.extract_text() or "").strip()
            resources = page.get("/Resources") or {}
            has_images = bool(resources.get("/XObject")) if hasattr(resources, "get") else False
            if not text and not has_images:
                issues.append({"page": index + 1, "type": "blank_page", "severity": "warning"})
            elif len(text) < 120 and not has_images:
                issues.append(
                    {
                        "page": index + 1,
                        "type": "sparse_page",
                        "text_characters": len(text),
                        "severity": "warning",
                    }
                )
        for index, page_path in enumerate(pages):
            with Image.open(page_path) as image:
                gray = image.convert("L")
                width, height = gray.size
                edge = max(2, min(width, height) // 250)
                regions = {
                    "left": (0, 0, edge, height),
                    "right": (width - edge, 0, width, height),
                    "top": (0, 0, width, edge),
                    "bottom": (0, height - edge, width, height),
                }
                for name, box in regions.items():
                    values = [
                        int(value[0] if isinstance(value, tuple) else value)
                        for value in gray.crop(box).get_flattened_data()
                    ]
                    ink = sum(value < 225 for value in values) / max(len(values), 1)
                    if ink > 0.02:
                        issues.append(
                            {
                                "page": index + 1,
                                "type": "content_touches_page_edge",
                                "edge": name,
                                "ink_ratio": round(ink, 4),
                                "severity": "warning",
                            }
                        )
        return {
            "passed": not any(item["severity"] == "error" for item in issues),
            "page_count": len(reader.pages),
            "issues": issues,
            "limitations": [
                "Heuristics detect blank/sparse pages and edge clipping but cannot prove typographic correctness.",
                "A Windows CI runner must perform final Microsoft Word interoperability checks.",
            ],
        }
