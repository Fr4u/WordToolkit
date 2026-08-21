"""PDF export mixin: convert the open document to PDF via LibreOffice headless."""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
from pathlib import Path


class PdfExportMixin:
    def convert_to_pdf(self, output_path: str) -> dict:
        """Convert the current document to PDF using LibreOffice headless.

        Saves the document first, then invokes:
          libreoffice --headless --convert-to pdf --outdir <dir> <docx>

        Args:
            output_path: Desired path for the output PDF file.

        Returns:
            {"pdf_path": str}

        Raises:
            RuntimeError: If no document is open, LibreOffice is not found,
                          or the conversion process exits with a non-zero code.
        """
        if self.workdir is None:
            raise RuntimeError("No document is open.")

        lo = shutil.which("libreoffice") or shutil.which("soffice")
        if lo is None:
            raise RuntimeError(
                "LibreOffice not found. Install it and ensure 'libreoffice' or "
                "'soffice' is on PATH."
            )

        out = Path(output_path)
        if out.exists():
            raise FileExistsError(f"PDF output already exists: {out}")
        outdir = out.parent
        outdir.mkdir(parents=True, exist_ok=True)

        # Never let the converter write the caller-visible path.  A private
        # sibling directory keeps conversion and publication on one filesystem.
        staging_dir = Path(tempfile.mkdtemp(prefix=".pdf-export-", dir=str(outdir)))
        generated = staging_dir / (Path(self.source_path).stem + ".pdf")
        try:
            self.save(self.source_path, backup=False)
            result = subprocess.run(
                [
                    lo,
                    "--headless",
                    "--convert-to",
                    "pdf",
                    "--outdir",
                    str(staging_dir),
                    str(self.source_path),
                ],
                capture_output=True,
                text=True,
            )
            if result.returncode != 0:
                raise RuntimeError(
                    f"LibreOffice conversion failed (exit {result.returncode}): {result.stderr.strip()}"
                )
            if not generated.is_file():
                raise RuntimeError("LibreOffice conversion produced no PDF output")

            # link() is create-new on Windows and POSIX.  The final link is the
            # authoritative no-clobber gate if a competitor appears meanwhile.
            os.link(generated, out)
        finally:
            try:
                if generated.exists():
                    generated.unlink()
                staging_dir.rmdir()
            except OSError:
                # Preserve the conversion/publication error; cleanup failures
                # must not leave a partial final output or mask its cause.
                pass

        return {"pdf_path": str(out)}
