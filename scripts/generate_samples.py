#!/usr/bin/env python3
from __future__ import annotations

import json
import os
from pathlib import Path

from lxml import etree
from PIL import Image, ImageDraw

from docx_mcp.document.base import W14, W
from wordtoolkit.config import Settings
from wordtoolkit.engine import DocumentRenderer, WordDocumentEngine
from wordtoolkit.math import MathEngine

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "examples" / "generated"


def first_paragraph(engine: WordDocumentEngine) -> str:
    return next(engine.doc._require("word/document.xml").iter(f"{W}p")).get(f"{W14}paraId")


def append(engine: WordDocumentEngine, anchor: str, text: str, style: str = "Normal") -> str:
    return engine.call("insert_paragraph", anchor, text, style)["para_id"]


def add_spacer_after_last_equation(engine: WordDocumentEngine) -> None:
    equation = engine._equation_element(engine.list_equations()[-1]["equation_id"])
    math_paragraph = next(
        parent
        for parent in equation.iterancestors()
        if etree.QName(parent).localname == "oMathPara"
    )
    outer_paragraph = next(
        parent for parent in math_paragraph.iterancestors() if etree.QName(parent).localname == "p"
    )
    spacer = etree.Element(f"{W}p")
    spacer.set(f"{W14}paraId", engine.doc._new_para_id())
    spacer.set(f"{W14}textId", "77777777")
    outer_paragraph.addnext(spacer)
    engine.doc._mark("word/document.xml")


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


def build_equations(settings: Settings) -> tuple[Path, dict]:
    target = OUTPUT / "WordToolkit-equations.docx"
    engine = WordDocumentEngine.create(target, settings)
    anchor = first_paragraph(engine)
    anchor = append(engine, anchor, "WordToolkit — Native Office Math", "Title")
    anchor = append(
        engine,
        anchor,
        "Every expression below is stored as native OMML, never as an image or plain text.",
    )
    inputs = [
        ("LaTeX", r"\frac{x^2+1}{\sqrt[3]{y}}+\sum_{i=1}^{n}i^2", "latex"),
        ("UnicodeMath", "(x_1^2)/(1+x)+√(3&y)", "unicodemath"),
        (
            "Presentation MathML",
            '<math xmlns="http://www.w3.org/1998/Math/MathML"><mrow><mi>A</mi><mo>=</mo><mfenced><mtable><mtr><mtd><mi>a</mi></mtd><mtd><mi>b</mi></mtd></mtr><mtr><mtd><mi>c</mi></mtd><mtd><mi>d</mi></mtd></mtr></mtable></mfenced></mrow></math>',
            "mathml",
        ),
    ]
    math = MathEngine()
    direct_omml = math.convert(r"\vec{v}=\frac{d\vec{x}}{dt}", "latex", "omml")
    inputs.append(("Direct OMML", direct_omml, "omml"))
    equation_anchors = []
    for label, value, input_format in inputs:
        anchor = append(engine, anchor, label, "Heading1")
        equation_anchors.append((anchor, value, input_format))
    inline_anchor = append(engine, anchor, "Inline equation: ")
    for equation_anchor, value, input_format in equation_anchors:
        engine.insert_equation(equation_anchor, value, input_format, display=True)
        add_spacer_after_last_equation(engine)
    engine.insert_equation(inline_anchor, r"E=mc^2", "latex", display=False, position="append")
    validation = engine.save_version(target)
    equations = engine.validate_equations()
    engine.close()
    return target, {"validation": validation["validation"], "equations": equations}


def build_showcase(settings: Settings) -> tuple[Path, dict]:
    target = OUTPUT / "WordToolkit-showcase.docx"
    image_path = OUTPUT / "sample-figure.png"
    image = Image.new("RGB", (640, 280), "white")
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(
        (40, 40, 600, 240), radius=25, fill="#E8F0FE", outline="#315EFB", width=8
    )
    draw.text((105, 120), "WordToolkit round-trip media", fill="#111827")
    image.save(image_path)

    engine = WordDocumentEngine.create(target, settings)
    anchor = first_paragraph(engine)
    title = append(engine, anchor, "WordToolkit OOXML showcase", "Title")
    intro = append(
        engine,
        title,
        "Styles, tables, sections, comments, notes, revisions, fields, media and OMML coexist in one package.",
    )
    heading = append(engine, intro, "Structured content", "Heading1")
    body = append(engine, heading, "This sentence has a native comment and a footnote.")
    engine.call("add_comment", body, "Native comment stored in comments.xml", author="WordToolkit")
    engine.call("add_footnote", body, "Native footnote definition with a linked reference.")
    table_anchor = append(engine, body, "Table with template style", "Heading2")
    image_anchor = append(engine, table_anchor, "Embedded DrawingML image", "Heading2")
    revision_anchor = append(engine, image_anchor, "Tracked revision sample: original phrase.")
    math_anchor = append(engine, revision_anchor, "Native equation", "Heading1")
    reference = append(engine, math_anchor, "See equation ")
    page_paragraph = append(engine, reference, "Current page: ")

    engine.call("set_track_changes", True, "WordToolkit")
    engine.call(
        "replace_text",
        revision_anchor,
        find="original phrase",
        replace="reviewed phrase",
        author="WordToolkit",
        tracked=True,
    )
    engine.call("add_table", table_anchor, 3, 3, author="WordToolkit")
    rows = (
        ("Format", "Part", "Preserved"),
        ("DOCX", "document.xml", "yes"),
        ("Math", "OMML", "native"),
    )
    for row, values in enumerate(rows):
        for column, value in enumerate(values):
            engine.call("modify_cell", 0, row, column, value)
    image_result = engine.call(
        "insert_image",
        image_anchor,
        str(image_path),
        width_emu=3_600_000,
        height_emu=1_575_000,
    )
    engine.call(
        "set_image_alt_text",
        image_result["rId"],
        "Blue WordToolkit round-trip media diagram",
    )
    engine.insert_equation(math_anchor, r"\frac{x^2+1}{x+1}=x", "latex", display=True)
    engine.number_equations(start=1)
    engine.add_equation_reference(reference, "Eq_1")
    engine.call("add_bookmark", reference, "ReferenceParagraph")
    engine.call("add_field", page_paragraph, " PAGE ", "1")
    engine.call("set_different_first_page", 0, True)
    engine.call("set_odd_even_headers", True)
    engine.set_header_footer_text(
        "header", "first", "WordToolkit — first-page header", section_index=0
    )
    engine.set_header_footer_text(
        "footer", "default", "Page {{PAGE}} of {{NUMPAGES}}", section_index=0
    )
    engine.set_header_footer_text(
        "footer", "even", "Even page {{PAGE}} of {{NUMPAGES}}", section_index=0
    )
    engine.call("generate_toc", 3)
    engine.call("update_fields")
    result = engine.save_version(target)
    audit = engine.package_audit()
    engine.close()
    return target, {
        "validation": result["validation"],
        "preservation": result["round_trip_preservation"],
        "audit": audit,
    }


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    validator = local_openxml_validator()
    settings = Settings(
        storage_root=OUTPUT / ".sessions",
        render_timeout_seconds=90,
        **({"openxml_validator_path": validator} if validator is not None else {}),
    )
    equation_doc, equation_report = build_equations(settings)
    showcase_doc, showcase_report = build_showcase(settings)
    renderer = DocumentRenderer(settings)
    pdf = OUTPUT / "WordToolkit-showcase.pdf"
    render = renderer.to_pdf(showcase_doc, pdf)
    pages = renderer.pages_to_png(pdf, OUTPUT / "preview-pages", dpi=120)
    visual = renderer.visual_audit(pdf, pages)
    equation_pdf = OUTPUT / "WordToolkit-equations.pdf"
    equation_render = renderer.to_pdf(equation_doc, equation_pdf)
    equation_pages = renderer.pages_to_png(equation_pdf, OUTPUT / "equation-preview-pages", dpi=120)
    equation_visual = renderer.visual_audit(equation_pdf, equation_pages)
    report = {
        "equation_document": equation_doc.name,
        "equation_report": equation_report,
        "showcase_document": showcase_doc.name,
        "showcase_report": showcase_report,
        "render": render,
        "visual_audit": visual,
        "preview_pages": [path.name for path in pages],
        "equation_render": equation_render,
        "equation_visual_audit": equation_visual,
        "equation_preview_pages": [path.name for path in equation_pages],
    }
    (OUTPUT / "generation-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(
        json.dumps(
            {
                "documents": [equation_doc.name, showcase_doc.name],
                "pdf": pdf.name,
                "validation": {
                    "equations": equation_report["validation"]["valid"],
                    "showcase": showcase_report["validation"]["valid"],
                },
                "visual": visual,
                "equation_visual": equation_visual,
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
