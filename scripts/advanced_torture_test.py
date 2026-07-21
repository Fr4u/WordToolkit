#!/usr/bin/env python3
"""Build and verify a deliberately difficult WordToolkit round-trip document.

This is an acceptance test, not a showcase generator.  It exercises OPC graph
preservation, direct WordprocessingML edits, native Office Math, revisions,
comments, notes, fields, section geometry, large tables, DrawingML and rendering.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import zipfile
from dataclasses import asdict
from pathlib import Path

from lxml import etree
from PIL import Image, ImageDraw, ImageFont
from pypdf import PdfReader

from docx_mcp.document.base import CT, RELS, W14, W
from wordtoolkit.config import Settings
from wordtoolkit.engine import DocumentRenderer, OoxmlValidator, WordDocumentEngine
from wordtoolkit.math import MathEngine

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "examples" / "advanced"
W_NS = W[1:-1]
R_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
M_NS = "http://schemas.openxmlformats.org/officeDocument/2006/math"
WP_NS = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
DS_NS = "http://schemas.openxmlformats.org/officeDocument/2006/customXml"
TEST_STORE_ID = "{A6C895A1-6B29-470C-84D7-6D14B798EAE7}"

LOREM = (
    "WordToolkit preserves the package graph while applying a narrow, intentional edit. "
    "Every paragraph in this stress document participates in pagination, style inheritance, "
    "field evaluation or round-trip verification. The text is intentionally long enough to "
    "exercise line breaking, widow control and keep-together behavior across renderers."
)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def first_paragraph(engine: WordDocumentEngine) -> etree._Element:
    return next(engine.doc._require("word/document.xml").iter(f"{W}p"))


def paragraph_id(paragraph: etree._Element) -> str:
    value = paragraph.get(f"{W14}paraId")
    if not value:
        raise AssertionError("Paragraph lacks w14:paraId")
    return value


def append_paragraph(
    engine: WordDocumentEngine,
    after: etree._Element,
    text: str,
    style: str = "ToolkitBody",
) -> etree._Element:
    paragraph = etree.Element(f"{W}p")
    paragraph.set(f"{W14}paraId", engine.doc._new_para_id())
    paragraph.set(f"{W14}textId", "77777777")
    properties = etree.SubElement(paragraph, f"{W}pPr")
    etree.SubElement(properties, f"{W}pStyle").set(f"{W}val", style)
    run = etree.SubElement(paragraph, f"{W}r")
    value = etree.SubElement(run, f"{W}t")
    value.text = text
    if text != text.strip():
        value.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
    after.addnext(paragraph)
    engine.doc._mark("word/document.xml")
    return paragraph


def add_heading(
    engine: WordDocumentEngine,
    after: etree._Element,
    text: str,
    level: int = 1,
) -> etree._Element:
    paragraph = append_paragraph(engine, after, text, f"Heading{level}")
    engine.format_paragraph_layout(
        paragraph_id(paragraph),
        keep_with_next=True,
        keep_lines=True,
        widow_control=True,
    )
    return paragraph


def add_page_break(engine: WordDocumentEngine, after: etree._Element) -> etree._Element:
    paragraph = etree.Element(f"{W}p")
    paragraph.set(f"{W14}paraId", engine.doc._new_para_id())
    paragraph.set(f"{W14}textId", "77777777")
    run = etree.SubElement(paragraph, f"{W}r")
    etree.SubElement(run, f"{W}br").set(f"{W}type", "page")
    after.addnext(paragraph)
    engine.doc._mark("word/document.xml")
    return paragraph


def add_numbering(paragraph: etree._Element, num_id: int, level: int) -> None:
    properties = paragraph.find(f"{W}pPr")
    if properties is None:
        properties = etree.Element(f"{W}pPr")
        paragraph.insert(0, properties)
    number = etree.SubElement(properties, f"{W}numPr")
    etree.SubElement(number, f"{W}ilvl").set(f"{W}val", str(level))
    etree.SubElement(number, f"{W}numId").set(f"{W}val", str(num_id))


def style_table(engine: WordDocumentEngine, table: etree._Element) -> None:
    """Apply a compact named style without replacing cell content."""
    for row_index, row in enumerate(table.findall(f"{W}tr")):
        for cell in row.findall(f"{W}tc"):
            for paragraph in cell.findall(f"{W}p"):
                properties = paragraph.find(f"{W}pPr")
                if properties is None:
                    properties = etree.Element(f"{W}pPr")
                    paragraph.insert(0, properties)
                style = properties.find(f"{W}pStyle")
                if style is None:
                    style = etree.Element(f"{W}pStyle")
                    properties.insert(0, style)
                style.set(f"{W}val", "ToolkitTable")
                if row_index == 0:
                    for run in paragraph.iter(f"{W}r"):
                        run_properties = run.find(f"{W}rPr")
                        if run_properties is None:
                            run_properties = etree.Element(f"{W}rPr")
                            run.insert(0, run_properties)
                        if run_properties.find(f"{W}b") is None:
                            etree.SubElement(run_properties, f"{W}b")
    engine.doc._mark("word/document.xml")


def add_section_break(
    engine: WordDocumentEngine,
    paragraph: etree._Element,
    *,
    width: int,
    height: int,
    orientation: str,
    margins: tuple[int, int, int, int],
) -> None:
    pid = paragraph_id(paragraph)
    engine.call("add_section_break", pid, "nextPage")
    engine.call(
        "set_section_properties",
        para_id=pid,
        width=width,
        height=height,
        orientation=orientation,
        margin_top=margins[0],
        margin_right=margins[1],
        margin_bottom=margins[2],
        margin_left=margins[3],
    )


def add_custom_bound_control(engine: WordDocumentEngine, paragraph: etree._Element) -> None:
    engine.call(
        "add_content_control",
        paragraph_id(paragraph),
        "CustomerName",
        "text",
        "Customer name bound to custom XML",
        default="Ada Lovelace",
    )
    document = engine.doc._require("word/document.xml")
    control = next(
        item
        for item in document.iter(f"{W}sdt")
        if item.find(f"{W}sdtPr/{W}tag").get(f"{W}val") == "CustomerName"
    )
    properties = control.find(f"{W}sdtPr")
    binding = etree.SubElement(properties, f"{W}dataBinding")
    binding.set(f"{W}storeItemID", TEST_STORE_ID)
    binding.set(f"{W}xpath", "/wt:profile[1]/wt:name[1]")
    binding.set(f"{W}prefixMappings", "xmlns:wt='urn:wordtoolkit:test'")
    engine.doc._mark("word/document.xml")


def load_test_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    """Load a usable font without assuming a Linux font installation."""
    candidates = (
        Path("C:/Windows/Fonts/arial.ttf"),
        Path("C:/Windows/Fonts/calibri.ttf"),
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
        Path("/usr/share/fonts/dejavu/DejaVuSans.ttf"),
    )
    for candidate in candidates:
        if candidate.is_file():
            return ImageFont.truetype(str(candidate), size)
    try:
        return ImageFont.truetype("DejaVuSans.ttf", size)
    except OSError:
        try:
            return ImageFont.load_default(size=size)
        except TypeError:
            return ImageFont.load_default()


def create_test_images(output: Path) -> tuple[Path, Path]:
    output.mkdir(parents=True, exist_ok=True)
    font = load_test_font(34)
    inline = output / "advanced-inline.png"
    image = Image.new("RGB", (1200, 520), "white")
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((35, 35, 1165, 485), 32, fill="#EAF2F8", outline="#2E74B5", width=8)
    draw.line((170, 360, 430, 170, 690, 310, 965, 105), fill="#E67E22", width=16)
    draw.text((115, 70), "WordToolkit: preserved DrawingML media", font=font, fill="#17365D")
    image.save(inline)

    floating = output / "advanced-floating.png"
    image = Image.new("RGB", (640, 640), "#FDFEFE")
    draw = ImageDraw.Draw(image)
    draw.ellipse((55, 55, 585, 585), fill="#D5F5E3", outline="#1E8449", width=10)
    draw.text((118, 270), "OOXML\nROUND-TRIP", font=font, fill="#145A32", align="center")
    image.save(floating)
    return inline, floating


def inject_preservation_parts(source: Path) -> dict[str, str]:
    """Add linked custom/opaque parts that the editor must preserve byte-for-byte."""
    with zipfile.ZipFile(source) as archive:
        payload = {item.filename: archive.read(item.filename) for item in archive.infolist()}

    content_types = etree.fromstring(payload["[Content_Types].xml"])
    defaults = {item.get("Extension") for item in content_types.findall(f"{CT}Default")}
    if "bin" not in defaults:
        etree.SubElement(
            content_types,
            f"{CT}Default",
            Extension="bin",
            ContentType="application/octet-stream",
        )
    overrides = {item.get("PartName") for item in content_types.findall(f"{CT}Override")}
    for part, content_type in (
        (
            "/customXml/itemProps1.xml",
            "application/vnd.openxmlformats-officedocument.customXmlProperties+xml",
        ),
        (
            "/word/fontTable.xml",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml",
        ),
    ):
        if part not in overrides:
            etree.SubElement(
                content_types, f"{CT}Override", PartName=part, ContentType=content_type
            )
    payload["[Content_Types].xml"] = etree.tostring(
        content_types, xml_declaration=True, encoding="UTF-8", standalone=True
    )

    document_rels = etree.fromstring(payload["word/_rels/document.xml.rels"])
    etree.SubElement(
        document_rels,
        f"{RELS}Relationship",
        Id="rId90",
        Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
        Target="../customXml/item1.xml",
    )
    etree.SubElement(
        document_rels,
        f"{RELS}Relationship",
        Id="rId91",
        Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable",
        Target="fontTable.xml",
    )
    payload["word/_rels/document.xml.rels"] = etree.tostring(
        document_rels, xml_declaration=True, encoding="UTF-8", standalone=True
    )

    package_rels = etree.fromstring(payload["_rels/.rels"])
    etree.SubElement(
        package_rels,
        f"{RELS}Relationship",
        Id="rIdOpaque",
        Type="urn:wordtoolkit:test:opaque-preservation",
        Target="word/embeddings/opaque-preservation.bin",
    )
    payload["_rels/.rels"] = etree.tostring(
        package_rels, xml_declaration=True, encoding="UTF-8", standalone=True
    )

    payload["customXml/item1.xml"] = (
        b'<?xml version="1.0" encoding="UTF-8"?>'
        b'<wt:profile xmlns:wt="urn:wordtoolkit:test"><wt:name>Ada Lovelace</wt:name>'
        b"<wt:classification>round-trip-preserve</wt:classification></wt:profile>"
    )
    payload["customXml/itemProps1.xml"] = (
        b'<?xml version="1.0" encoding="UTF-8" standalone="no"?>'
        b'<ds:datastoreItem ds:itemID="'
        + TEST_STORE_ID.encode()
        + b'" xmlns:ds="'
        + DS_NS.encode()
        + b'"><ds:schemaRefs><ds:schemaRef ds:uri="urn:wordtoolkit:test"/>'
        b"</ds:schemaRefs></ds:datastoreItem>"
    )
    payload["customXml/_rels/item1.xml.rels"] = (
        b'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        b'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        b'<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/'
        b'2006/relationships/customXmlProps" Target="itemProps1.xml"/></Relationships>'
    )
    payload["word/fontTable.xml"] = (
        b'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        b'<w:fonts xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
        b'<w:font w:name="DejaVu Sans"><w:family w:val="swiss"/><w:pitch w:val="variable"/>'
        b"</w:font></w:fonts>"
    )
    payload["word/embeddings/opaque-preservation.bin"] = bytes(range(256)) * 17

    temporary = source.with_suffix(".injecting.docx")
    with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, data in payload.items():
            archive.writestr(name, data)
    temporary.replace(source)
    protected = (
        "customXml/item1.xml",
        "customXml/itemProps1.xml",
        "customXml/_rels/item1.xml.rels",
        "word/fontTable.xml",
        "word/embeddings/opaque-preservation.bin",
    )
    return {name: digest(payload[name]) for name in protected}


def configure_styles(engine: WordDocumentEngine) -> None:
    for name, based_on, next_style in (
        ("Toolkit Body", "Normal", "ToolkitBody"),
        ("Toolkit Subtitle", "Normal", "ToolkitBody"),
        ("Toolkit Callout", "Normal", "ToolkitBody"),
        ("Toolkit Table", "Normal", "ToolkitTable"),
        ("Title", "Normal", "ToolkitBody"),
        ("Caption", "Normal", "ToolkitBody"),
    ):
        engine.call("create_style", name, "paragraph", based_on, next_style)
    engine.call("create_style", "Comment Reference", "character", None, None)
    engine.call("create_style", "Hyperlink", "character", None, None)
    engine.configure_style(
        "Normal", font_name="DejaVu Sans", font_size_pt=11, space_after_pt=6, line_spacing=1.1
    )
    engine.configure_style(
        "ToolkitBody",
        font_name="DejaVu Sans",
        font_size_pt=11,
        font_color="273746",
        space_after_pt=6,
        line_spacing=1.1,
    )
    engine.configure_style(
        "ToolkitSubtitle",
        font_name="DejaVu Sans",
        font_size_pt=13,
        font_color="566573",
        italic=True,
        space_after_pt=10,
    )
    engine.configure_style(
        "ToolkitCallout",
        font_name="DejaVu Sans",
        font_size_pt=10,
        font_color="1F4D78",
        space_before_pt=6,
        space_after_pt=6,
    )
    engine.configure_style(
        "ToolkitTable",
        font_name="DejaVu Sans",
        font_size_pt=9,
        font_color="1B2631",
        space_before_pt=0,
        space_after_pt=0,
        line_spacing=1.0,
    )
    engine.configure_style(
        "Title", font_name="DejaVu Sans", font_size_pt=24, font_color="17365D", bold=True
    )
    for style, size, color, before, after in (
        ("Heading1", 16, "2E74B5", 16, 8),
        ("Heading2", 13, "1F4D78", 12, 6),
        ("Heading3", 12, "34495E", 8, 4),
    ):
        engine.configure_style(
            style,
            font_name="DejaVu Sans",
            font_size_pt=size,
            font_color=color,
            bold=True,
            space_before_pt=before,
            space_after_pt=after,
        )
    engine.call("set_document_language", "pl-PL")


def populate_document(
    engine: WordDocumentEngine, inline_image: Path, floating_image: Path
) -> list[tuple[str | dict, str]]:
    document = engine.doc._require("word/document.xml")
    anchor = first_paragraph(engine)
    engine.call(
        "update_paragraph", paragraph_id(anchor), "WordToolkit — zaawansowany test DOCX", "Title"
    )
    engine.format_paragraph_layout(paragraph_id(anchor), space_after_pt=18, keep_lines=True)
    current = append_paragraph(
        engine,
        anchor,
        "Test produkcyjnego round-trip OPC/WordprocessingML/Office Math",
        "ToolkitSubtitle",
    )
    current = append_paragraph(
        engine,
        current,
        "Wersja testowa • natywne OMML • brak makr • zachowanie nieznanych części",
        "ToolkitCallout",
    )
    current = append_paragraph(engine, current, LOREM)
    current = append_paragraph(engine, current, "Dokument testowy wygenerowany automatycznie. ")
    engine.call("add_field", paragraph_id(current), ' DATE \\@ "yyyy-MM-dd" ', "2026-07-18")
    current = add_page_break(engine, current)

    current = add_heading(engine, current, "1. Nawigacja, pola i współpraca")
    toc = append_paragraph(engine, current, "Spis treści (pole natywne): ")
    engine.call(
        "add_field",
        paragraph_id(toc),
        ' TOC \\o "1-3" \\h \\z \\u ',
        "Spis treści — zaktualizuj pole w programie Word",
    )
    current = toc
    review = add_heading(engine, current, "1.1 Komentarze, przypisy i rewizje", 2)
    current = append_paragraph(
        engine,
        review,
        "To zdanie zawiera komentarz, odpowiedź, przypis dolny oraz przypis końcowy.",
    )
    comment = engine.call(
        "add_comment", paragraph_id(current), "Sprawdź semantykę akapitu.", author="Reviewer A"
    )
    engine.call(
        "reply_to_comment", comment["comment_id"], "Semantyka zweryfikowana.", author="Reviewer B"
    )
    engine.call("add_footnote", paragraph_id(current), "Przypis dolny zachowany w footnotes.xml.")
    engine.call("add_endnote", paragraph_id(current), "Przypis końcowy zachowany w endnotes.xml.")
    current = append_paragraph(
        engine,
        current,
        "Rewizja testowa zmienia frazę wersja robocza na wersja zatwierdzona i usuwa token LEGACY.",
    )
    engine.call("set_track_changes", True, "WordToolkit QA")
    engine.call(
        "replace_text",
        paragraph_id(current),
        find="wersja robocza",
        replace="wersja zatwierdzona",
        author="WordToolkit QA",
        tracked=True,
    )
    engine.call(
        "delete_text", paragraph_id(current), "LEGACY", author="WordToolkit QA", tracked=True
    )
    current = append_paragraph(engine, current, "Dokumentacja źródłowa Microsoft Open XML: ")
    engine.call(
        "add_hyperlink",
        paragraph_id(current),
        "Learn Microsoft Open XML",
        "https://learn.microsoft.com/office/open-xml/",
    )
    engine.call("add_bookmark", paragraph_id(current), "NavigationTarget")
    current = append_paragraph(engine, current, "Odesłanie do zakładki: ")
    engine.call("add_field", paragraph_id(current), " REF NavigationTarget \\h ", "sekcja 1")
    current = append_paragraph(engine, current, "Ada Lovelace", "ToolkitCallout")
    bound_paragraph = current
    current = append_paragraph(engine, current, "Koniec pierwszej sekcji.")
    add_custom_bound_control(engine, bound_paragraph)
    add_section_break(
        engine,
        current,
        width=12240,
        height=15840,
        orientation="portrait",
        margins=(1080, 1080, 1080, 1080),
    )

    current = add_heading(engine, current, "2. Tabela krajobrazowa i geometria")
    current = append_paragraph(
        engine,
        current,
        "Tabela ma stały layout, powtarzalny wiersz nagłówka, jawne szerokości i komórki scalone.",
    )
    table_result = engine.call("add_table", paragraph_id(current), 29, 8, author="WordToolkit QA")
    table_index = table_result["table_index"]
    headers = ("ID", "Sekcja", "Właściciel", "Status", "Ryzyko", "Termin", "Wynik", "Uwagi")
    for column, value in enumerate(headers):
        engine.call("modify_cell", table_index, 0, column, value, tracked=False)
    for row in range(1, 29):
        values = (
            f"WT-{row:03d}",
            f"Pakiet {1 + row % 4}",
            ("Anna", "Bartosz", "Celina")[row % 3],
            ("Gotowe", "W toku", "Kontrola")[row % 3],
            ("Niskie", "Średnie", "Wysokie")[row % 3],
            f"2026-08-{1 + row % 28:02d}",
            f"{91 + row % 9}%",
            "Weryfikacja round-trip i renderu",
        )
        for column, value in enumerate(values):
            engine.call("modify_cell", table_index, row, column, value, tracked=False)
    engine.call("set_header_row", table_index)
    engine.call("set_table_borders", table_index, "single", "B8C2CC", 4)
    engine.call("set_column_widths", table_index, [1.8, 2.7, 2.8, 2.5, 2.3, 2.8, 2.1, 5.0])
    engine.call("merge_cells", table_index, 27, 0, 27, 1)
    table = list(document.iter(f"{W}tbl"))[table_index]
    style_table(engine, table)
    for column in range(8):
        engine.call("set_cell_shading", table_index, 0, column, "D9EAF7")
    current = append_paragraph(engine, table, "Tabela 1: Macierz kontroli pakietu", "Caption")
    current = add_page_break(engine, current)
    current = add_heading(engine, current, "2.1 Kontynuacja tabel i operacje na komórkach", 2)
    current = append_paragraph(engine, current, LOREM)
    small = engine.call("add_table", paragraph_id(current), 5, 4, author="WordToolkit QA")
    small_index = small["table_index"]
    for row in range(5):
        for column in range(4):
            engine.call(
                "modify_cell", small_index, row, column, f"R{row + 1}C{column + 1}", tracked=False
            )
    engine.call("set_header_row", small_index)
    engine.call("set_table_borders", small_index, "single", "B8C2CC", 4)
    engine.call("set_column_widths", small_index, [4.7, 4.7, 4.7, 4.7])
    engine.call("merge_cells", small_index, 2, 1, 2, 2)
    engine.split_cells(small_index, 2, 1)
    small_table = list(document.iter(f"{W}tbl"))[small_index]
    style_table(engine, small_table)
    for column in range(4):
        engine.call("set_cell_shading", small_index, 0, column, "D9EAF7")
    current = append_paragraph(
        engine, small_table, "Scalenie i podział wykonano bez przebudowy dokumentu."
    )
    add_section_break(
        engine,
        current,
        width=15840,
        height=12240,
        orientation="landscape",
        margins=(850, 850, 850, 850),
    )

    current = add_heading(engine, current, "3. A4, kolumny, listy i DrawingML")
    current = append_paragraph(engine, current, LOREM)
    list_definition = engine.call(
        "create_multilevel_list",
        "WordToolkit hierarchy",
        [
            {"num_fmt": "decimal", "lvl_text": "%1.", "indent": 520, "hanging": 260},
            {"num_fmt": "lowerLetter", "lvl_text": "%2)", "indent": 1040, "hanging": 260},
            {"num_fmt": "lowerRoman", "lvl_text": "%3.", "indent": 1560, "hanging": 260},
        ],
    )
    for index in range(12):
        current = append_paragraph(
            engine,
            current,
            f"Poziom {index % 3 + 1}: kontrola numerowania, wcięć i dziedziczenia stylu.",
        )
        add_numbering(current, list_definition["num_id"], index % 3)
        engine.format_paragraph_layout(paragraph_id(current), keep_lines=True, widow_control=True)
    image_heading = add_heading(engine, current, "3.1 Obrazy dostępne i podpisy", 2)
    image_anchor = append_paragraph(
        engine, image_heading, "Obraz osadzony poniżej ma opis alternatywny."
    )
    image_result = engine.call(
        "insert_image",
        paragraph_id(image_anchor),
        str(inline_image),
        width_emu=2_650_000,
        height_emu=1_150_000,
    )
    engine.call(
        "set_image_alt_text",
        image_result["rId"],
        "Wykres liniowy pokazujący zachowanie mediów DrawingML w WordToolkit",
        "Media round-trip",
    )
    image_paragraph = image_anchor.getnext()
    current = append_paragraph(
        engine, image_paragraph, "Rysunek 1: Media osadzone i opisane", "Caption"
    )
    current = add_page_break(engine, current)
    current = add_heading(engine, current, "3.2 Obraz pływający i przepływ tekstu", 2)
    floating_anchor = append_paragraph(engine, current, LOREM)
    floating_result = engine.call(
        "insert_floating_image",
        paragraph_id(floating_anchor),
        str(floating_image),
        3.2,
        3.2,
        11.5,
        8.0,
        "topbottom",
    )
    engine.call(
        "set_image_alt_text",
        floating_result["rId"],
        "Zielony znak jakości OOXML round-trip",
        "OOXML round-trip",
    )
    floating_paragraph = floating_anchor.getnext()
    current = append_paragraph(engine, floating_paragraph, LOREM)
    for _ in range(3):
        current = append_paragraph(engine, current, LOREM)
    add_section_break(
        engine,
        current,
        width=11906,
        height=16838,
        orientation="portrait",
        margins=(1134, 1134, 1134, 1134),
    )

    equation_sources: list[tuple[str | dict, str]] = [
        (r"\frac{x_i^2+\sqrt[3]{y}}{1+\alpha}+\sum_{k=1}^{n}k^2", "latex"),
        (r"\left(\begin{matrix}a&b\\c&d\end{matrix}\right)", "latex"),
        (r"\begin{aligned}x+y&=1\\2x-y&=0\end{aligned}", "latex"),
        (r"\lim_{x\to 0}\frac{\sin x}{x}", "latex"),
        (r"\vec{v}+\hat{x}+\bar{y}+\text{const}", "latex"),
        ("matrix(a&b@c&d)", "unicodemath"),
        ("eqarray(x+y=1@2x-y=0)", "unicodemath"),
        ("√(3&x)+∫_(0)^(1) x^2", "unicodemath"),
        (
            '<math xmlns="http://www.w3.org/1998/Math/MathML"><mrow><munderover><mo>∏</mo><mrow><mi>i</mi><mo>=</mo><mn>1</mn></mrow><mi>n</mi></munderover><msub><mi>x</mi><mi>i</mi></msub></mrow></math>',
            "mathml",
        ),
        (
            '<math xmlns="http://www.w3.org/1998/Math/MathML"><mrow><mo>{</mo><mtable columnalign="left"><mtr><mtd><mrow><mi>x</mi><mo>+</mo><mi>y</mi><mo>=</mo><mn>1</mn></mrow></mtd></mtr><mtr><mtd><mrow><mn>2</mn><mi>x</mi><mo>-</mo><mi>y</mi><mo>=</mo><mn>0</mn></mrow></mtd></mtr></mtable></mrow></math>',
            "mathml",
        ),
        (
            {
                "kind": "fraction",
                "children": [
                    {"kind": "identifier", "value": "a"},
                    {
                        "kind": "radical",
                        "children": [
                            {"kind": "identifier", "value": "b"},
                            {"kind": "number", "value": "5"},
                        ],
                    },
                ],
            },
            "ast",
        ),
        (
            {
                "kind": "function",
                "children": [
                    {"kind": "identifier", "value": "cos"},
                    {"kind": "identifier", "value": "θ"},
                ],
            },
            "ast",
        ),
    ]
    math = MathEngine()
    direct_omml = math.convert(r"\int_{-\infty}^{\infty}e^{-x^2}\,dx=\sqrt{\pi}", "latex", "omml")
    equation_sources.extend(
        [
            (direct_omml, "omml"),
            (r"E=mc^2", "latex"),
            ("α_1+β^2→γ", "unicodemath"),
            (
                '<math xmlns="http://www.w3.org/1998/Math/MathML"><mfrac><mn>1</mn><mrow><mn>1</mn><mo>+</mo><msup><mi>e</mi><mrow><mo>-</mo><mi>x</mi></mrow></msup></mrow></mfrac></math>',
                "mathml",
            ),
        ]
    )

    current = add_heading(engine, current, "4. Galeria natywnych równań Office Math")
    current = append_paragraph(
        engine,
        current,
        "Każdy wzór jest zapisany jako m:oMath lub m:oMathPara. Konwersja nie używa obrazu.",
    )
    for index, (source, source_format) in enumerate(equation_sources):
        if index == 8:
            current = add_heading(engine, current, "4.1 Druga strona galerii równań", 2)
            engine.format_paragraph_layout(paragraph_id(current), page_break_before=True)
        label = add_heading(engine, current, f"Równanie {index + 1} — {source_format}", 3)
        engine.insert_equation(paragraph_id(label), source, source_format, display=True)
        math_para = label.getnext()
        current = append_paragraph(engine, math_para, f"Źródło: {source_format}", "ToolkitCallout")
    inline = append_paragraph(engine, current, "Równanie śródtekstowe zachowuje m:oMath: ")
    engine.insert_equation(
        paragraph_id(inline), r"a^2+b^2=c^2", "latex", display=False, position="append"
    )
    current = inline
    engine.number_equations(start=1)
    current = append_paragraph(engine, current, "Odwołanie do pierwszego numeru równania: ")
    engine.add_equation_reference(paragraph_id(current), "Eq_1")

    # Final section is body-level Letter portrait.
    engine.call(
        "set_section_properties",
        para_id=None,
        width=12240,
        height=15840,
        orientation="portrait",
        margin_top=1080,
        margin_right=1080,
        margin_bottom=1080,
        margin_left=1080,
    )
    engine.call("set_section_columns", 2, 2, True)
    engine.call("set_odd_even_headers", True)
    for section_index in range(4):
        engine.call("set_different_first_page", section_index, True)
        for variant, label in (
            ("default", "WordToolkit • test zaawansowany"),
            ("first", f"WordToolkit • sekcja {section_index + 1}"),
            ("even", "WordToolkit • strona parzysta"),
        ):
            engine.set_header_footer_text("header", variant, label, section_index)
            engine.set_header_footer_text(
                "footer", variant, f"{label} • {{{{PAGE}}}} / {{{{NUMPAGES}}}}", section_index
            )
    engine.call("update_fields")
    engine.doc._mark("word/document.xml")
    return equation_sources


def package_assertions(path: Path, protected_hashes: dict[str, str]) -> dict:
    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        hashes = {name: digest(archive.read(name)) for name in protected_hashes}
        document = etree.fromstring(archive.read("word/document.xml"))
        rels = etree.fromstring(archive.read("_rels/.rels"))
        required = {
            "word/document.xml",
            "word/styles.xml",
            "word/numbering.xml",
            "word/settings.xml",
            "word/fontTable.xml",
            "word/comments.xml",
            "word/commentsExtended.xml",
            "word/footnotes.xml",
            "word/endnotes.xml",
            "customXml/item1.xml",
            "word/embeddings/opaque-preservation.bin",
        }
        missing = sorted(required - names)
        if missing:
            raise AssertionError(f"Missing required package parts: {missing}")
        if hashes != protected_hashes:
            raise AssertionError({"protected_before": protected_hashes, "protected_after": hashes})
        counts = {
            "sections": len(document.xpath("//w:sectPr", namespaces={"w": W_NS})),
            "tables": len(document.xpath("//w:tbl", namespaces={"w": W_NS})),
            "equations": len(document.xpath("//m:oMath", namespaces={"m": M_NS})),
            "math_blocks": len(document.xpath("//m:oMathPara", namespaces={"m": M_NS})),
            "comments": len(document.xpath("//w:commentReference", namespaces={"w": W_NS})),
            "footnotes": len(document.xpath("//w:footnoteReference", namespaces={"w": W_NS})),
            "endnotes": len(document.xpath("//w:endnoteReference", namespaces={"w": W_NS})),
            "insertions": len(document.xpath("//w:ins", namespaces={"w": W_NS})),
            "deletions": len(document.xpath("//w:del", namespaces={"w": W_NS})),
            "fields": len(document.xpath("//w:fldSimple", namespaces={"w": W_NS})),
            "bookmarks": len(document.xpath("//w:bookmarkStart", namespaces={"w": W_NS})),
            "content_controls": len(document.xpath("//w:sdt", namespaces={"w": W_NS})),
            "data_bindings": len(document.xpath("//w:dataBinding", namespaces={"w": W_NS})),
            "drawings": len(document.xpath("//w:drawing", namespaces={"w": W_NS})),
            "image_doc_properties": len(document.xpath("//wp:docPr", namespaces={"wp": WP_NS})),
            "root_relationships": len(rels.findall(f"{RELS}Relationship")),
        }
    minima = {
        "sections": 4,
        "tables": 2,
        "equations": 17,
        "math_blocks": 16,
        "comments": 1,
        "footnotes": 1,
        "endnotes": 1,
        "insertions": 3,
        "deletions": 2,
        "fields": 6,
        "bookmarks": 2,
        "content_controls": 1,
        "data_bindings": 1,
        "drawings": 2,
        "image_doc_properties": 2,
        "root_relationships": 3,
    }
    failures = {
        key: (counts[key], minimum) for key, minimum in minima.items() if counts[key] < minimum
    }
    if failures:
        raise AssertionError({"package_count_failures": failures, "counts": counts})
    return {"counts": counts, "protected_part_hashes": hashes}


def equation_assertions(engine: WordDocumentEngine, sources: list[tuple[str | dict, str]]) -> dict:
    equations = engine.list_equations()
    # One extra inline formula follows the block gallery.
    if len(equations) != len(sources) + 1:
        raise AssertionError({"expected": len(sources) + 1, "actual": len(equations)})
    math = MathEngine()
    exports = {"latex": 0, "unicodemath": 0, "mathml": 0}
    for index, (source, source_format) in enumerate(sources):
        omml = equations[index]["omml"]
        comparison = math.compare(source, source_format, omml, "omml")
        if not comparison.equivalent:
            raise AssertionError({"equation": index + 1, "comparison": asdict(comparison)})
        canonical = math.parse(omml, "omml").to_dict()
        for target in exports:
            exported = math.convert(canonical, "ast", target, display=True)
            try:
                reparsed = math.compare(canonical, "ast", exported, target)
            except Exception as exc:
                raise AssertionError(
                    {
                        "equation": index + 1,
                        "target": target,
                        "exported": exported,
                        "exception": f"{type(exc).__name__}: {exc}",
                    }
                ) from exc
            if not reparsed.equivalent:
                raise AssertionError(
                    {
                        "equation": index + 1,
                        "target": target,
                        "exported": exported,
                        "comparison": asdict(reparsed),
                    }
                )
            exports[target] += 1
    validation = engine.validate_equations()
    if not validation["valid"]:
        raise AssertionError(validation)
    return {
        "native_equations": len(equations),
        "block_source_equations": len(sources),
        "semantic_source_roundtrips": len(sources),
        "semantic_export_reparses": exports,
        "validation": validation,
    }


def rendering_assertions(pdf: Path, pngs: list[Path], visual: dict) -> dict:
    reader = PdfReader(str(pdf))
    if len(reader.pages) < 8:
        raise AssertionError(f"Expected at least 8 rendered pages, got {len(reader.pages)}")
    if len(pngs) != len(reader.pages):
        raise AssertionError("PDF/PNG page count mismatch")
    orientations = []
    blank_pages = []
    replacement_glyph_pages = []
    sparse_pages = []
    extracted_lengths = []
    for index, page in enumerate(reader.pages, start=1):
        width = float(page.mediabox.width)
        height = float(page.mediabox.height)
        orientations.append("landscape" if width > height else "portrait")
        text = (page.extract_text() or "").strip()
        extracted_lengths.append(len(text))
        resources = page.get("/Resources") or {}
        has_images = bool(resources.get("/XObject")) if hasattr(resources, "get") else False
        if not text and not has_images:
            blank_pages.append(index)
        if "�" in text:
            replacement_glyph_pages.append(index)
        if len(text) < 120 and not has_images:
            sparse_pages.append(index)
    if blank_pages:
        raise AssertionError({"blank_pages": blank_pages})
    if replacement_glyph_pages:
        raise AssertionError({"replacement_glyph_pages": replacement_glyph_pages})
    if sparse_pages:
        raise AssertionError({"sparse_pages": sparse_pages, "text_lengths": extracted_lengths})
    if "portrait" not in orientations or "landscape" not in orientations:
        raise AssertionError({"missing_mixed_orientation": orientations})
    if not visual["passed"]:
        raise AssertionError(visual)
    if visual["issues"]:
        raise AssertionError({"visual_warnings": visual["issues"]})

    fonts = {"available": False, "embedded": [], "not_embedded": []}
    pdffonts = shutil.which("pdffonts")
    if pdffonts:
        result = subprocess.run(
            [pdffonts, str(pdf)], capture_output=True, text=True, check=False, timeout=30
        )
        fonts["available"] = result.returncode == 0
        for line in result.stdout.splitlines()[2:]:
            columns = line.split()
            if len(columns) >= 7:
                (
                    fonts["embedded"] if columns[5].lower() == "yes" else fonts["not_embedded"]
                ).append(columns[0])
        if fonts["not_embedded"]:
            raise AssertionError({"fonts_not_embedded": fonts["not_embedded"]})
    return {
        "page_count": len(reader.pages),
        "orientations": orientations,
        "portrait_pages": orientations.count("portrait"),
        "landscape_pages": orientations.count("landscape"),
        "blank_pages": blank_pages,
        "replacement_glyph_pages": replacement_glyph_pages,
        "sparse_pages": sparse_pages,
        "extracted_text_lengths": extracted_lengths,
        "fonts": fonts,
        "visual_audit": visual,
        "pngs": [path.name for path in pngs],
    }


def local_openxml_validator() -> Path | None:
    executable = (
        "wordtoolkit-openxml-validator.exe" if os.name == "nt" else "wordtoolkit-openxml-validator"
    )
    runtime = "win-x64" if os.name == "nt" else "linux-x64"
    candidates = (
        ROOT / "dist" / "wordtoolkit" / "runtime" / "tools" / "openxml-validator" / executable,
        ROOT / "tools" / "OpenXmlValidator" / "bin" / "Release" / "net8.0" / runtime / executable,
    )
    return next((candidate for candidate in candidates if candidate.is_file()), None)


def run() -> dict:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    seed = OUTPUT / "WordToolkit-advanced-seed.docx"
    final = OUTPUT / "WordToolkit-advanced-torture-test.docx"
    pdf = OUTPUT / "WordToolkit-advanced-torture-test.pdf"
    preview = OUTPUT / "preview-pages"
    if preview.exists():
        shutil.rmtree(preview)
    validator = local_openxml_validator()
    settings = Settings(
        storage_root=OUTPUT / ".sessions",
        render_timeout_seconds=180,
        **({"openxml_validator_path": validator} if validator is not None else {}),
    )
    inline_image, floating_image = create_test_images(OUTPUT)

    seed_engine = WordDocumentEngine.create(seed, settings)
    seed_engine.close()
    protected_hashes = inject_preservation_parts(seed)

    engine = WordDocumentEngine(seed, settings)
    open_report = engine.open()
    configure_styles(engine)
    equation_sources = populate_document(engine, inline_image, floating_image)
    save_report = engine.save_version(final)
    engine.close()

    structural = OoxmlValidator(settings).validate(final)
    if not structural["valid"] or structural["errors"] or structural["warnings"]:
        raise AssertionError(structural)
    package = package_assertions(final, protected_hashes)

    reopened = WordDocumentEngine(final, settings)
    reopen_report = reopened.open()
    equations = equation_assertions(reopened, equation_sources)
    accessibility = reopened.call("check_accessibility")
    if accessibility["issue_count"]:
        raise AssertionError(accessibility)
    layout_risks = reopened.layout_risks()
    if layout_risks["count"]:
        raise AssertionError(layout_risks)
    package_audit = reopened.package_audit()
    reopened.close()

    renderer = DocumentRenderer(settings)
    render = renderer.to_pdf(final, pdf)
    pngs = renderer.pages_to_png(pdf, preview, dpi=144)
    visual = renderer.visual_audit(pdf, pngs)
    rendering = rendering_assertions(pdf, pngs, visual)

    report = {
        "status": "passed",
        "document": str(final),
        "pdf": str(pdf),
        "open": open_report,
        "save": save_report,
        "reopen": reopen_report,
        "structural_validation": structural,
        "package_assertions": package,
        "equation_assertions": equations,
        "accessibility": accessibility,
        "layout_risks": layout_risks,
        "package_audit": package_audit,
        "render": render,
        "rendering_assertions": rendering,
        "known_interop_notice": (
            "LibreOffice is the control renderer. Microsoft Word may paginate and shape fonts "
            "differently; Microsoft Word validation remains a separate Windows CI gate."
        ),
    }
    report_path = OUTPUT / "advanced-test-report.json"
    report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    return {
        "status": report["status"],
        "docx": str(final),
        "pdf": str(pdf),
        "report": str(report_path),
        "pages": rendering["page_count"],
        "equations": equations["native_equations"],
        "package_counts": package["counts"],
        "validation": {
            "errors": structural["errors"],
            "warnings": structural["warnings"],
            "accessibility_issues": accessibility["issue_count"],
        },
    }


if __name__ == "__main__":
    print(json.dumps(run(), indent=2, ensure_ascii=False))
