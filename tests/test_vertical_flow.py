from __future__ import annotations

import zipfile

import pytest
from lxml import etree

from docx_mcp.document.base import W14, W
from wordtoolkit.config import Settings
from wordtoolkit.engine import DocumentRenderer, OoxmlValidator, WordDocumentEngine
from wordtoolkit.engine.renderer import _find_executable


def settings(tmp_path):
    return Settings(storage_root=tmp_path / "storage", render_timeout_seconds=60)


def test_create_edit_equation_validate_reopen(tmp_path) -> None:
    config = settings(tmp_path)
    source = tmp_path / "source.docx"
    output = tmp_path / "output.docx"
    engine = WordDocumentEngine.create(source, config)
    anchor = next(engine.doc._require("word/document.xml").iter(f"{W}p")).get(f"{W14}paraId")
    paragraph = engine.call("insert_paragraph", anchor, "Native Office Math follows:", "Normal")
    inserted = engine.insert_equation(
        paragraph["para_id"],
        r"\int_0^1 \frac{x^2}{1+x}\,dx",
        "latex",
        display=True,
    )
    assert inserted["display"] is True

    result = engine.save_version(output)
    assert result["validation"]["valid"]
    assert result["round_trip_preservation"]["preserved"]
    engine.close()

    reopened = WordDocumentEngine(output, config)
    reopened.open()
    equations = reopened.list_equations()
    assert len(equations) == 1
    assert reopened.validate_equations()["valid"]
    with zipfile.ZipFile(output) as archive:
        xml = etree.fromstring(archive.read("word/document.xml"))
        ns = {"m": "http://schemas.openxmlformats.org/officeDocument/2006/math"}
        assert len(xml.xpath("//m:oMathPara/m:oMath", namespaces=ns)) == 1
    reopened.close()


def test_insert_equation_returns_the_inserted_equation_when_anchored_in_the_middle(
    tmp_path,
) -> None:
    config = settings(tmp_path)
    source = tmp_path / "middle-insertion.docx"
    engine = WordDocumentEngine.create(source, config)
    anchor = next(engine.doc._require("word/document.xml").iter(f"{W}p")).get(f"{W14}paraId")

    engine.insert_equation(anchor, r"x=1", "latex", display=True)
    inserted = engine.insert_equation(anchor, r"y=2", "latex", display=True)
    equations = engine.list_equations()

    assert len(equations) == 2
    assert inserted["equation_id"] == equations[0]["equation_id"]
    assert inserted["ast"] == equations[0]["ast"]
    assert inserted["ast"] != equations[1]["ast"]
    engine.close()


def test_round_trip_preservation_remembers_changes_across_multiple_saves(tmp_path) -> None:
    config = settings(tmp_path)
    source = tmp_path / "multi-save-source.docx"
    preview = tmp_path / "multi-save-preview.docx"
    exported = tmp_path / "multi-save-export.docx"
    engine = WordDocumentEngine.create(source, config)
    anchor = next(engine.doc._require("word/document.xml").iter(f"{W}p")).get(f"{W14}paraId")
    engine.call("insert_paragraph", anchor, "Change before preview", "Normal")

    first = engine.save_version(preview)
    second = engine.save_version(exported)

    assert first["round_trip_preservation"]["preserved"]
    assert second["round_trip_preservation"]["preserved"]
    assert second["round_trip_preservation"]["unexpectedly_changed_parts"] == []
    engine.close()


def test_dotx_template_becomes_docx_main_content_type(tmp_path) -> None:
    config = settings(tmp_path)
    source = tmp_path / "source.docx"
    template = tmp_path / "template.dotx"
    output = tmp_path / "from-template.docx"
    WordDocumentEngine.create(source, config).close()
    with (
        zipfile.ZipFile(source) as original,
        zipfile.ZipFile(template, "w", zipfile.ZIP_DEFLATED) as destination,
    ):
        for item in original.infolist():
            data = original.read(item.filename)
            if item.filename == "[Content_Types].xml":
                data = data.replace(
                    b"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                    b"application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml",
                )
            destination.writestr(item, data)
    engine = WordDocumentEngine.create(output, config, template)
    engine.save_version(output)
    engine.close()
    with zipfile.ZipFile(output) as archive:
        content_types = archive.read("[Content_Types].xml")
    assert b"wordprocessingml.document.main+xml" in content_types
    assert b"wordprocessingml.template.main+xml" not in content_types


def test_styles_paragraph_runs_lists_and_caption_save_as_ooxml(tmp_path) -> None:
    config = settings(tmp_path)
    path = tmp_path / "formatting.docx"
    engine = WordDocumentEngine.create(path, config)
    anchor = next(engine.doc._require("word/document.xml").iter(f"{W}p")).get(f"{W14}paraId")
    paragraph = engine.call("insert_paragraph", anchor, "Formatted list item", "Normal")
    paragraph_id = paragraph["para_id"]
    engine.call("create_style", "Toolkit Body", "paragraph", "Normal", "Toolkit Body")
    engine.configure_style(
        "Toolkit Body",
        font_name="Liberation Serif",
        font_size_pt=11,
        font_color="223344",
        space_after_pt=6,
        line_spacing=1.15,
    )
    engine.call("apply_style_to_range", [paragraph_id], "Toolkit Body")
    engine.format_paragraph_layout(
        paragraph_id,
        alignment="both",
        keep_with_next=True,
        keep_lines=True,
        widow_control=True,
        tab_stops=[{"position_mm": 25, "alignment": "left", "leader": "dot"}],
    )
    engine.format_run(paragraph_id, 0, bold=True, color="3355AA", underline="single")
    engine.call("add_list", [paragraph_id], style="numbered")
    caption = engine.call("insert_caption", paragraph_id, "Formatting example", "Table")
    assert caption["label"] == "Table"
    footer = engine.set_header_footer_text("footer", "even", "Page {{PAGE}} of {{NUMPAGES}}", 0)
    header = engine.set_header_footer_text("header", "first", "First page", 0)
    assert footer["part"].startswith("word/footer")
    assert header["part"].startswith("word/header")
    engine.call("set_different_first_page", 0, True)
    engine.call("set_odd_even_headers", True)
    result = engine.save_version(path)
    assert result["validation"]["valid"]
    engine.close()


def test_table_widths_are_fixed_and_merged_cells_sum_grid_columns(tmp_path) -> None:
    config = settings(tmp_path)
    path = tmp_path / "table-geometry.docx"
    engine = WordDocumentEngine.create(path, config)
    anchor = next(engine.doc._require("word/document.xml").iter(f"{W}p")).get(f"{W14}paraId")
    table = engine.call("add_table", anchor, 3, 4, author="WordToolkit")
    engine.call("merge_cells", table["table_index"], 1, 1, 1, 2)
    engine.call("set_column_widths", table["table_index"], [2.0, 3.0, 4.0, 5.0])

    root = engine.doc._require("word/document.xml")
    table_xml = next(root.iter(f"{W}tbl"))
    properties = table_xml.find(f"{W}tblPr")
    expected = [int(value * 567) for value in (2.0, 3.0, 4.0, 5.0)]
    assert properties.find(f"{W}tblW").get(f"{W}w") == str(sum(expected))
    assert properties.find(f"{W}tblW").get(f"{W}type") == "dxa"
    assert properties.find(f"{W}tblInd").get(f"{W}w") == "120"
    assert properties.find(f"{W}tblLayout").get(f"{W}type") == "fixed"
    merged = table_xml.findall(f"{W}tr")[1].findall(f"{W}tc")[1]
    assert merged.find(f"{W}tcPr/{W}tcW").get(f"{W}w") == str(expected[1] + expected[2])

    result = engine.save_version(path)
    assert result["validation"]["valid"]
    engine.close()


def test_layout_risk_uses_the_effective_section_width(tmp_path) -> None:
    config = settings(tmp_path)
    path = tmp_path / "section-aware-table-risk.docx"
    engine = WordDocumentEngine.create(path, config)
    anchor = next(engine.doc._require("word/document.xml").iter(f"{W}p")).get(f"{W14}paraId")
    table = engine.call("add_table", anchor, 2, 2, author="WordToolkit")
    engine.call(
        "set_section_properties",
        para_id=None,
        width=15840,
        height=12240,
        orientation="landscape",
        margin_left=850,
        margin_right=850,
    )
    engine.call("set_column_widths", table["table_index"], [10.0, 10.0])
    assert engine.layout_risks()["count"] == 0
    engine.call("set_column_widths", table["table_index"], [13.0, 13.0])
    risks = engine.layout_risks()
    assert risks["count"] == 1
    assert risks["risks"][0]["type"] == "table_overflow"
    engine.close()


@pytest.mark.render
def test_render_pdf_png_and_visual_audit(tmp_path) -> None:
    if not _find_executable("soffice", "libreoffice"):
        pytest.skip("LibreOffice not installed")
    if not _find_executable("pdftoppm"):
        pytest.skip("Poppler not installed")
    config = settings(tmp_path)
    docx = tmp_path / "render.docx"
    engine = WordDocumentEngine.create(docx, config)
    anchor = next(engine.doc._require("word/document.xml").iter(f"{W}p")).get(f"{W14}paraId")
    paragraph = engine.call("insert_paragraph", anchor, "Rendered equation", "Heading1")
    engine.insert_equation(paragraph["para_id"], r"\sum_{i=1}^{n} i^2", "latex", display=True)
    engine.save_version(docx)
    renderer = DocumentRenderer(config)
    pdf = tmp_path / "render.pdf"
    render = renderer.to_pdf(docx, pdf)
    page_dir = tmp_path / "pages"
    page_dir.mkdir()
    (page_dir / "page-01.png").write_bytes(b"stale")
    pages = renderer.pages_to_png(pdf, page_dir, dpi=96)
    assert not (page_dir / "page-01.png").exists()
    audit = renderer.visual_audit(pdf, pages)
    assert render["renderer"] == "LibreOffice headless"
    assert pdf.stat().st_size > 1000
    assert pages and all(page.stat().st_size > 1000 for page in pages)
    assert audit["page_count"] >= 1
    assert audit["passed"]
    assert OoxmlValidator(config).validate(docx)["valid"]
