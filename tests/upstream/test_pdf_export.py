"""Tests for P9.5: PDF export — convert_to_pdf."""

from __future__ import annotations

from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

from docx_mcp.document import DocxDocument


def _make_doc(tmp_path: Path) -> DocxDocument:
    out = str(tmp_path / "test.docx")
    return DocxDocument.create(out)


class TestConvertToPdf:
    def test_rejects_existing_output_without_invoking_libreoffice(self, tmp_path: Path):
        doc = _make_doc(tmp_path)
        pdf_out = tmp_path / "out.pdf"
        pdf_out.write_bytes(b"existing")
        with patch("shutil.which", return_value="/usr/bin/libreoffice"), pytest.raises(
            FileExistsError
        ):
            doc.convert_to_pdf(str(pdf_out))

    def test_competing_output_is_not_clobbered(self, tmp_path: Path):
        doc = _make_doc(tmp_path)
        pdf_out = tmp_path / "out.pdf"
        generated = tmp_path / "test.pdf"

        def convert(_args, **_kwargs):
            generated_path = Path(_args[_args.index("--outdir") + 1]) / "test.pdf"
            generated_path.write_bytes(b"generated")
            pdf_out.write_bytes(b"competitor")
            return MagicMock(returncode=0)

        with patch("shutil.which", return_value="/usr/bin/libreoffice"), patch(
            "subprocess.run", side_effect=convert
        ), pytest.raises(FileExistsError):
            doc.convert_to_pdf(str(pdf_out))
        assert pdf_out.read_bytes() == b"competitor"
        assert not generated.exists()

    def test_returns_pdf_path_key(self, tmp_path: Path):
        """convert_to_pdf returns dict with 'pdf_path' key."""
        doc = _make_doc(tmp_path)
        pdf_out = str(tmp_path / "out.pdf")

        with (
            patch("shutil.which", return_value="/usr/bin/libreoffice"),
            patch("subprocess.run") as mock_run,
        ):
            mock_run.return_value = MagicMock(returncode=0)
            mock_run.side_effect = lambda args, **_: (
                (Path(args[args.index("--outdir") + 1]) / "test.pdf").write_bytes(b"%PDF-test"),
                MagicMock(returncode=0),
            )[1]
            result = doc.convert_to_pdf(pdf_out)

        assert "pdf_path" in result

    def test_raises_if_libreoffice_not_found(self, tmp_path: Path):
        """convert_to_pdf raises RuntimeError when LibreOffice is not installed."""
        doc = _make_doc(tmp_path)
        with (
            patch("shutil.which", return_value=None),
            pytest.raises(RuntimeError, match="LibreOffice"),
        ):  # noqa: E501
            doc.convert_to_pdf(str(tmp_path / "out.pdf"))

    def test_calls_libreoffice_subprocess(self, tmp_path: Path):
        """convert_to_pdf calls libreoffice --headless --convert-to pdf."""
        doc = _make_doc(tmp_path)
        pdf_out = str(tmp_path / "out.pdf")

        with (
            patch("shutil.which", return_value="/usr/bin/libreoffice"),
            patch("subprocess.run") as mock_run,
        ):
            mock_run.return_value = MagicMock(returncode=0)
            mock_run.side_effect = lambda args, **_: (
                (Path(args[args.index("--outdir") + 1]) / "test.pdf").write_bytes(b"%PDF-test"),
                MagicMock(returncode=0),
            )[1]
            doc.convert_to_pdf(pdf_out)

        call_args = mock_run.call_args
        cmd = call_args[0][0]
        assert "--headless" in cmd
        assert "--convert-to" in cmd
        assert "pdf" in cmd

    def test_raises_if_subprocess_fails(self, tmp_path: Path):
        """convert_to_pdf raises RuntimeError when libreoffice exits non-zero."""
        doc = _make_doc(tmp_path)
        pdf_out = str(tmp_path / "out.pdf")

        with (
            patch("shutil.which", return_value="/usr/bin/libreoffice"),
            patch("subprocess.run") as mock_run,
        ):
            mock_run.return_value = MagicMock(returncode=1, stderr="error msg")
            with pytest.raises(RuntimeError, match="conversion failed"):
                doc.convert_to_pdf(pdf_out)

    def test_raises_if_no_document_open(self, tmp_path: Path):
        """convert_to_pdf raises RuntimeError when no document is open."""
        doc = DocxDocument.__new__(DocxDocument)
        doc.workdir = None
        with pytest.raises(RuntimeError, match="No document"):
            doc.convert_to_pdf(str(tmp_path / "out.pdf"))
