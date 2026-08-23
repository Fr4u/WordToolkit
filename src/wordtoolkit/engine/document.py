from __future__ import annotations

import contextlib
import copy
import hashlib
import os
import re
import shutil
import tempfile
import zipfile
from collections.abc import Iterator
from pathlib import Path
from typing import Any, Literal, cast

from lxml import etree

from docx_mcp.document import DocxDocument
from docx_mcp.document.base import CT, NSMAP, RELS, W14, W

from ..config import Settings
from ..errors import ErrorCode, WordToolkitError
from ..math import MathEngine
from ..math.omml import M
from ..security import SafePackageInspector, reject_reparse_ancestors
from .validator import OoxmlValidator, package_hashes, preservation_report

R = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"
RPR_ORDER = (
    "rStyle",
    "rFonts",
    "b",
    "bCs",
    "i",
    "iCs",
    "caps",
    "smallCaps",
    "strike",
    "dstrike",
    "outline",
    "shadow",
    "emboss",
    "imprint",
    "noProof",
    "snapToGrid",
    "vanish",
    "webHidden",
    "color",
    "spacing",
    "w",
    "kern",
    "position",
    "sz",
    "szCs",
    "highlight",
    "u",
    "effect",
    "bdr",
    "shd",
    "fitText",
    "vertAlign",
    "rtl",
    "cs",
    "em",
    "lang",
    "eastAsianLayout",
    "specVanish",
    "oMath",
    "rPrChange",
)


@contextlib.contextmanager
def _atomic_package_target(output: Path) -> Iterator[Path]:
    """Yield a same-directory DOCX staging path and atomically publish it on success."""
    output = Path(os.path.abspath(output))
    reject_reparse_ancestors(output, label="Package output path")
    output.parent.mkdir(parents=True, exist_ok=True)
    reject_reparse_ancestors(output, label="Package output path")
    descriptor, raw_staging = tempfile.mkstemp(
        prefix=f".{output.stem}.", suffix=".docx", dir=output.parent
    )
    os.close(descriptor)
    staging = Path(raw_staging)
    try:
        yield staging
        os.replace(staging, output)
    except BaseException:
        with contextlib.suppress(OSError):
            staging.unlink(missing_ok=True)
        raise


PPR_ORDER = (
    "pStyle",
    "keepNext",
    "keepLines",
    "pageBreakBefore",
    "framePr",
    "widowControl",
    "numPr",
    "suppressLineNumbers",
    "pBdr",
    "shd",
    "tabs",
    "suppressAutoHyphens",
    "kinsoku",
    "wordWrap",
    "overflowPunct",
    "topLinePunct",
    "autoSpaceDE",
    "autoSpaceDN",
    "bidi",
    "adjustRightInd",
    "snapToGrid",
    "spacing",
    "ind",
    "contextualSpacing",
    "mirrorIndents",
    "suppressOverlap",
    "jc",
    "textDirection",
    "textAlignment",
    "textboxTightWrap",
    "outlineLvl",
    "divId",
    "cnfStyle",
    "rPr",
    "sectPr",
    "pPrChange",
)


def _ordered_child(parent: etree._Element, tag: str, order: tuple[str, ...]) -> etree._Element:
    existing = parent.find(f"{W}{tag}")
    if existing is not None:
        return existing
    element = etree.Element(f"{W}{tag}")
    rank = {name: index for index, name in enumerate(order)}
    wanted = rank.get(tag, len(order))
    for index, child in enumerate(parent):
        local = etree.QName(child).localname if isinstance(child.tag, str) else ""
        if rank.get(local, len(order)) > wanted:
            parent.insert(index, element)
            return element
    parent.append(element)
    return element


class WordDocumentEngine:
    """Round-trip editor that keeps the upstream OOXML engine behind a stable API."""

    def __init__(self, path: Path, settings: Settings):
        self.path = path.resolve()
        self.settings = settings
        self.package_inspector = SafePackageInspector(settings)
        self.validator = OoxmlValidator(settings)
        self.math = MathEngine()
        self.document: DocxDocument | None = None
        self.initial_hashes: dict[str, str] = {}
        self.cumulative_modified_parts: set[str] = set()
        self.inspection: dict[str, Any] = {}
        self._owned_workspace_root: Path | None = None

    @classmethod
    def create(
        cls, path: Path, settings: Settings, template: Path | None = None
    ) -> WordDocumentEngine:
        engine = cls(path, settings)
        document = cast(
            DocxDocument,
            DocxDocument.create(str(path), template_path=str(template) if template else None),
        )
        engine.document = document
        # DOTX is accepted as input but the product is deliberately a DOCX.  Merely
        # renaming the ZIP leaves Word's main-part content type as a template.
        if template and template.suffix.lower() == ".dotx":
            content_types = document._tree("[Content_Types].xml")
            if content_types is not None:
                template_type = "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml"
                document_type = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"
                for override in content_types:
                    if (
                        override.get("PartName") == "/word/document.xml"
                        and override.get("ContentType") == template_type
                    ):
                        override.set("ContentType", document_type)
                        document._mark("[Content_Types].xml")
                document.save(str(path), backup=False)
        engine._ensure_paragraph_ids()
        engine.inspection = engine.package_inspector.inspect(path).to_dict()
        engine.initial_hashes = package_hashes(path)
        engine.cumulative_modified_parts = set(engine.doc._modified)
        return engine

    def open(self) -> dict:
        try:
            with self.package_inspector.inspect_stable(self.path) as (snapshot, inspection):
                self.inspection = inspection.to_dict()
                self.initial_hashes = package_hashes(snapshot)
                self.document = DocxDocument(str(self.path))
                info = self.document.open(package_path=snapshot)
        except BaseException:
            with contextlib.suppress(Exception):
                self.close()
            raise
        normalizations = self._ensure_paragraph_ids()
        self.cumulative_modified_parts = set(self.doc._modified)
        return {
            "document": info,
            "package": self.inspection,
            "normalizations": normalizations,
        }

    def _ensure_paragraph_ids(self) -> dict:
        seen: set[str] = set()
        assigned = 0
        replaced = 0
        for part, tree in self.doc._trees.items():
            if not part.endswith(".xml"):
                continue
            changed = False
            for paragraph in tree.iter(f"{W}p"):
                value = paragraph.get(f"{W14}paraId", "")
                valid = False
                if value and value not in seen:
                    try:
                        valid = 0 <= int(value, 16) < 0x80000000
                    except ValueError:
                        valid = False
                if not valid:
                    if value:
                        replaced += 1
                    else:
                        assigned += 1
                    value = self.doc._new_para_id()
                    paragraph.set(f"{W14}paraId", value)
                    if not paragraph.get(f"{W14}textId"):
                        paragraph.set(f"{W14}textId", "77777777")
                    changed = True
                seen.add(value)
            if changed:
                self.doc._mark(part)
        return {"assigned_missing": assigned, "replaced_invalid_or_duplicate": replaced}

    def close(self) -> None:
        owned_workspace_root = self._owned_workspace_root
        try:
            if self.document is not None:
                self.document.close()
        finally:
            self.document = None
            self._owned_workspace_root = None
            if owned_workspace_root is not None:
                shutil.rmtree(owned_workspace_root, ignore_errors=True)

    @property
    def doc(self) -> DocxDocument:
        if self.document is None:
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Document is closed")
        return self.document

    def call(self, method: str, *args, **kwargs):
        target = getattr(self.doc, method, None)
        if target is None or method.startswith("_"):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT, "Unsupported document operation", {"method": method}
            )
        return target(*args, **kwargs)

    def inspect(self) -> dict:
        info = self.doc.get_info()
        info.pop("path", None)
        return {
            "info": info,
            "outline": self.doc.get_document_outline(),
            "sections": self.doc.get_sections(),
            "styles": len(self.doc.get_styles()),
            "tables": len(self.doc.get_tables()),
            "equations": len(self.list_equations()),
            "package": self.inspection,
        }

    def snapshot(self, output: Path) -> dict:
        """Serialize the current in-memory state without clearing dirty flags or repairing it."""
        if self.doc.workdir is None:
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Document is closed")
        with (
            _atomic_package_target(output) as staging,
            zipfile.ZipFile(staging, "w", zipfile.ZIP_DEFLATED) as archive,
        ):
            for root, _dirs, files in os.walk(self.doc.workdir):
                for filename in files:
                    source = Path(root) / filename
                    part = source.relative_to(self.doc.workdir).as_posix()
                    if part in self.doc._modified and part in self.doc._trees:
                        data = etree.tostring(
                            self.doc._trees[part],
                            xml_declaration=True,
                            encoding="UTF-8",
                            standalone=True,
                        )
                        archive.writestr(part, data)
                    elif part in self.doc._binaries:
                        archive.writestr(part, self.doc._binaries[part])
                    else:
                        archive.write(source, part)
            for part, data in self.doc._binaries.items():
                if not (self.doc.workdir / part).exists():
                    archive.writestr(part, data)
        return {"path": str(output), "size_bytes": output.stat().st_size}

    def fork(self, snapshot_path: Path) -> WordDocumentEngine:
        """Create an isolated copy-on-write engine for an atomic publish attempt."""
        self.snapshot(snapshot_path)
        clone = WordDocumentEngine(snapshot_path, self.settings)
        clone._owned_workspace_root = snapshot_path.parent.resolve()
        try:
            clone.open()
        except Exception:
            clone.close()
            raise
        clone.initial_hashes = dict(self.initial_hashes)
        clone.cumulative_modified_parts = (
            set(self.cumulative_modified_parts) | set(self.doc._modified) | set(clone.doc._modified)
        )
        return clone

    def save_version(self, output: Path) -> dict:
        pending_modified = set(self.doc._modified)
        with _atomic_package_target(output) as staging:
            result = self.doc.save(str(staging), backup=False)
            self.cumulative_modified_parts.update(pending_modified)
            self.cumulative_modified_parts.update(result.get("modified_parts", []))
            validation = self.validator.validate(staging)
            if not validation["valid"]:
                raise WordToolkitError(
                    ErrorCode.OOXML_INVALID,
                    "Saved package failed structural validation",
                    {"issues": validation["issues"][:50]},
                )
            after = package_hashes(staging)
            preservation = preservation_report(
                self.initial_hashes, after, sorted(self.cumulative_modified_parts)
            )
        return {
            **result,
            "path": str(output),
            "validation": validation,
            "round_trip_preservation": preservation,
        }

    def list_equations(self) -> list[dict]:
        document = self.doc._require("word/document.xml")
        results = []
        for index, omath in enumerate(document.iter(f"{M}oMath")):
            xml = etree.tostring(omath, encoding="unicode")
            canonical = self.math.parse(xml, "omml")
            digest = hashlib.sha256(repr(canonical.to_dict()).encode()).hexdigest()[:12]
            block = any(parent.tag == f"{M}oMathPara" for parent in omath.iterancestors())
            paragraph = next(
                (parent for parent in omath.iterancestors() if parent.tag == f"{W}p"), None
            )
            results.append(
                {
                    "equation_id": f"eq_{index}_{digest}",
                    "index": index,
                    "display": block,
                    "paragraph_id": paragraph.get(f"{W14}paraId")
                    if paragraph is not None
                    else None,
                    "omml": xml,
                    "ast": canonical.to_dict(),
                }
            )
        return results

    def _equation_element(self, equation_id: str) -> etree._Element:
        try:
            index = int(equation_id.split("_", 2)[1])
        except (IndexError, ValueError) as exc:
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "Malformed equation_id") from exc
        equations = list(self.doc._require("word/document.xml").iter(f"{M}oMath"))
        if index >= len(equations):
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Equation was not found")
        return equations[index]

    def insert_equation(
        self,
        anchor_para_id: str,
        value: str | dict,
        input_format: Literal["latex", "unicodemath", "mathml", "omml", "ast"],
        *,
        display: bool,
        position: Literal["after", "before", "append"] = "after",
    ) -> dict:
        document = self.doc._require("word/document.xml")
        paragraph = self.doc._find_para(document, anchor_para_id)
        if paragraph is None:
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Anchor paragraph was not found")
        element = self.math.omml_element(value, input_format, display=display)
        if not display:
            omath = element if element.tag == f"{M}oMath" else element.find(f"{M}oMath")
            if omath is None:
                raise WordToolkitError(
                    ErrorCode.OOXML_INVALID, "Generated equation contains no m:oMath element"
                )
            paragraph.append(omath)
        elif position == "append":
            paragraph.append(element)
            omath = element.find(f"{M}oMath")
        else:
            omath = element.find(f"{M}oMath")
            math_paragraph = etree.Element(f"{W}p")
            math_paragraph.set(f"{W14}paraId", self.doc._new_para_id())
            math_paragraph.set(f"{W14}textId", "77777777")
            math_paragraph.append(element)
            if position == "before":
                paragraph.addprevious(math_paragraph)
            else:
                paragraph.addnext(math_paragraph)
        if omath is None:
            raise WordToolkitError(
                ErrorCode.OOXML_INVALID, "Generated equation contains no m:oMath element"
            )
        self.doc._mark("word/document.xml")
        equations = list(document.iter(f"{M}oMath"))
        inserted_index = equations.index(omath)
        return self.list_equations()[inserted_index]

    def replace_equation(
        self,
        equation_id: str,
        value: str | dict,
        input_format: Literal["latex", "unicodemath", "mathml", "omml", "ast"],
    ) -> dict:
        old = self._equation_element(equation_id)
        new = self.math.omml_element(value, input_format, display=False)
        old.getparent().replace(old, new)
        self.doc._mark("word/document.xml")
        index = int(equation_id.split("_", 2)[1])
        return self.list_equations()[index]

    def get_equation(self, equation_id: str) -> dict:
        index = int(equation_id.split("_", 2)[1])
        equation = self.list_equations()[index]
        node = self.math.parse(equation["omml"], "omml")
        equation["latex"] = self.math.convert(node.to_dict(), "ast", "latex")
        equation["unicodemath"] = self.math.convert(node.to_dict(), "ast", "unicodemath")
        equation["mathml"] = self.math.convert(
            node.to_dict(), "ast", "mathml", display=equation["display"]
        )
        return equation

    def validate_equations(self) -> dict:
        valid, issues = 0, []
        for equation in self.list_equations():
            try:
                self.math.parse(equation["omml"], "omml")
                valid += 1
            except Exception as exc:
                issues.append({"equation_id": equation["equation_id"], "error": str(exc)})
        return {"valid": not issues, "valid_count": valid, "issues": issues}

    def number_equations(self, *, start: int = 1, prefix: str = "Eq_") -> dict:
        document = self.doc._require("word/document.xml")
        existing = {
            item.get(f"{W}name", "")
            for item in document.iter(f"{W}bookmarkStart")
            if item.get(f"{W}name", "").startswith(prefix)
        }
        numbered = []
        sequence = start
        bookmark_id = self.doc._next_markup_id(document)
        for equation in list(document.iter(f"{M}oMath")):
            math_para = next(
                (x for x in equation.iterancestors() if x.tag == f"{M}oMathPara"), None
            )
            if math_para is None or math_para.getparent() is None:
                continue
            bookmark = f"{prefix}{sequence}"
            if bookmark in existing:
                sequence += 1
                continue
            number_para = etree.Element(f"{W}p")
            ppr = etree.SubElement(number_para, f"{W}pPr")
            etree.SubElement(ppr, f"{W}jc").set(f"{W}val", "right")
            run1 = etree.SubElement(number_para, f"{W}r")
            etree.SubElement(run1, f"{W}t").text = "("
            etree.SubElement(
                number_para, f"{W}bookmarkStart", {f"{W}id": str(bookmark_id), f"{W}name": bookmark}
            )
            field = etree.SubElement(
                number_para, f"{W}fldSimple", {f"{W}instr": " SEQ Equation \\* ARABIC "}
            )
            field_run = etree.SubElement(field, f"{W}r")
            etree.SubElement(field_run, f"{W}t").text = str(sequence)
            etree.SubElement(number_para, f"{W}bookmarkEnd", {f"{W}id": str(bookmark_id)})
            run2 = etree.SubElement(number_para, f"{W}r")
            etree.SubElement(run2, f"{W}t").text = ")"
            outer_paragraph = next((x for x in math_para.iterancestors() if x.tag == f"{W}p"), None)
            anchor = outer_paragraph if outer_paragraph is not None else math_para
            anchor.addnext(number_para)
            numbered.append({"sequence": sequence, "bookmark": bookmark})
            sequence += 1
            bookmark_id += 1
        self.doc._mark("word/document.xml")
        return {"numbered": numbered}

    def add_equation_reference(self, para_id: str, bookmark: str, text: str = "") -> dict:
        document = self.doc._require("word/document.xml")
        paragraph = self.doc._find_para(document, para_id)
        if paragraph is None:
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Paragraph was not found")
        if text:
            run = etree.SubElement(paragraph, f"{W}r")
            etree.SubElement(run, f"{W}t").text = text
        field = etree.SubElement(paragraph, f"{W}fldSimple", {f"{W}instr": f" REF {bookmark} \\h "})
        run = etree.SubElement(field, f"{W}r")
        etree.SubElement(run, f"{W}t").text = "0"
        self.doc._mark("word/document.xml")
        return {"paragraph_id": para_id, "bookmark": bookmark}

    def set_header_footer_text(
        self,
        story_kind: Literal["header", "footer"],
        variant: Literal["default", "first", "even"],
        text: str,
        section_index: int = 0,
    ) -> dict:
        document = self.doc._require("word/document.xml")
        sections = list(document.iter(f"{W}sectPr"))
        if not sections:
            body = document.find(f"{W}body")
            if body is None:
                raise WordToolkitError(ErrorCode.OOXML_INVALID, "Document body is missing")
            sections = [etree.SubElement(body, f"{W}sectPr")]
            self.doc._mark("word/document.xml")
        if section_index < 0 or section_index >= len(sections):
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "Section index is out of range")
        section = sections[section_index]
        rels = self.doc._require("word/_rels/document.xml.rels")
        reference_tag = f"{W}{story_kind}Reference"
        reference = next(
            (item for item in section.findall(reference_tag) if item.get(f"{W}type") == variant),
            None,
        )
        part = ""
        if reference is not None:
            reference_id = reference.get(f"{R}id", "")
            relationship = rels.find(f'{RELS}Relationship[@Id="{reference_id}"]')
            if relationship is not None:
                part = f"word/{relationship.get('Target', '')}"
        if not part or self.doc._tree(part) is None:
            existing = [
                name
                for name in self.doc._trees
                if name.startswith(f"word/{story_kind}") and name.endswith(".xml")
            ]
            part = f"word/{story_kind}{len(existing) + 1}.xml"
            root = etree.Element(
                f"{W}{'hdr' if story_kind == 'header' else 'ftr'}",
                nsmap={"w": NSMAP["w"]},
            )
            self.doc._trees[part] = root
            workdir = self.doc.workdir
            if workdir is None:
                raise WordToolkitError(
                    ErrorCode.INTERNAL_ERROR, "Document working directory is unavailable"
                )
            physical = workdir / part
            physical.parent.mkdir(parents=True, exist_ok=True)
            physical.write_bytes(etree.tostring(root, xml_declaration=True, encoding="UTF-8"))

            ids = []
            for item in rels.findall(f"{RELS}Relationship"):
                value = item.get("Id", "")
                if value.startswith("rId") and value[3:].isdigit():
                    ids.append(int(value[3:]))
            relationship_id = f"rId{max(ids, default=0) + 1}"
            relationship = etree.SubElement(rels, f"{RELS}Relationship")
            relationship.set("Id", relationship_id)
            relationship.set(
                "Type",
                f"http://schemas.openxmlformats.org/officeDocument/2006/relationships/{story_kind}",
            )
            relationship.set("Target", Path(part).name)
            self.doc._mark("word/_rels/document.xml.rels")

            content_types = self.doc._require("[Content_Types].xml")
            override = etree.SubElement(content_types, f"{CT}Override")
            override.set("PartName", f"/{part}")
            override.set(
                "ContentType",
                f"application/vnd.openxmlformats-officedocument.wordprocessingml.{story_kind}+xml",
            )
            self.doc._mark("[Content_Types].xml")

            reference = etree.Element(reference_tag)
            reference.set(f"{R}id", relationship_id)
            reference.set(f"{W}type", variant)
            insert_at = 0 if story_kind == "header" else len(section.findall(f"{W}headerReference"))
            section.insert(insert_at, reference)
            self.doc._mark("word/document.xml")

        root = self.doc._require(part)
        for child in list(root):
            root.remove(child)
        paragraph = etree.SubElement(root, f"{W}p")
        paragraph.set(f"{W14}paraId", self.doc._new_para_id())
        paragraph.set(f"{W14}textId", "77777777")
        for token in re.split(r"(\{\{(?:PAGE|NUMPAGES)\}\})", text):
            if not token:
                continue
            field_match = re.fullmatch(r"\{\{(PAGE|NUMPAGES)\}\}", token)
            if field_match:
                field = etree.SubElement(
                    paragraph,
                    f"{W}fldSimple",
                    {f"{W}instr": f" {field_match.group(1)} "},
                )
                run = etree.SubElement(field, f"{W}r")
                etree.SubElement(run, f"{W}t").text = "1"
            else:
                run = etree.SubElement(paragraph, f"{W}r")
                value = etree.SubElement(run, f"{W}t")
                value.text = token
                if token != token.strip():
                    value.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
        self.doc._mark(part)
        return {
            "story": story_kind,
            "variant": variant,
            "section_index": section_index,
            "part": part,
        }

    def inspect_direct_formatting(self, para_id: str | None = None) -> dict:
        document = self.doc._require("word/document.xml")
        paragraphs = (
            [self.doc._find_para(document, para_id)] if para_id else list(document.iter(f"{W}p"))
        )
        details = []
        for paragraph in filter(None, paragraphs):
            runs = []
            for index, run in enumerate(paragraph.findall(f".//{W}r")):
                props = run.find(f"{W}rPr")
                if props is not None and len(props):
                    runs.append(
                        {
                            "run_index": index,
                            "properties": [etree.QName(x).localname for x in props],
                        }
                    )
            if runs:
                details.append({"para_id": paragraph.get(f"{W14}paraId"), "runs": runs})
        return {"paragraphs_with_direct_formatting": len(details), "details": details}

    def normalize_formatting(self, para_ids: list[str] | None = None) -> dict:
        document = self.doc._require("word/document.xml")
        paragraphs = list(document.iter(f"{W}p"))
        if para_ids:
            wanted = set(para_ids)
            paragraphs = [x for x in paragraphs if x.get(f"{W14}paraId") in wanted]
        removable = {
            "b",
            "bCs",
            "i",
            "iCs",
            "color",
            "highlight",
            "rFonts",
            "sz",
            "szCs",
            "u",
            "strike",
            "dstrike",
            "vertAlign",
            "shd",
        }
        removed = 0
        for paragraph in paragraphs:
            for run in paragraph.findall(f".//{W}r"):
                props = run.find(f"{W}rPr")
                if props is None:
                    continue
                for child in list(props):
                    if etree.QName(child).localname in removable:
                        props.remove(child)
                        removed += 1
                if len(props) == 0:
                    run.remove(props)
        if removed:
            self.doc._mark("word/document.xml")
        return {"removed_direct_properties": removed, "paragraphs_examined": len(paragraphs)}

    def configure_style(
        self,
        name: str,
        *,
        font_name: str | None = None,
        font_size_pt: float | None = None,
        font_color: str | None = None,
        bold: bool | None = None,
        italic: bool | None = None,
        space_before_pt: float | None = None,
        space_after_pt: float | None = None,
        line_spacing: float | None = None,
    ) -> dict:
        styles = self.doc._require("word/styles.xml")
        style = self.doc._find_style(styles, name)
        if style is None:
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Style was not found")

        rpr = style.find(f"{W}rPr")
        if any(value is not None for value in (font_name, font_size_pt, font_color, bold, italic)):
            rpr = rpr if rpr is not None else etree.SubElement(style, f"{W}rPr")
        if font_name is not None:
            fonts = _ordered_child(rpr, "rFonts", RPR_ORDER)
            for attribute in ("ascii", "hAnsi", "cs"):
                fonts.set(f"{W}{attribute}", font_name)
        if font_size_pt is not None:
            for tag in ("sz", "szCs"):
                _ordered_child(rpr, tag, RPR_ORDER).set(f"{W}val", str(round(font_size_pt * 2)))
        if font_color is not None:
            _ordered_child(rpr, "color", RPR_ORDER).set(f"{W}val", font_color.upper())
        for tag, enabled in (("b", bold), ("i", italic)):
            if enabled is None:
                continue
            existing = rpr.find(f"{W}{tag}")
            if enabled and existing is None:
                _ordered_child(rpr, tag, RPR_ORDER)
            elif not enabled and existing is not None:
                rpr.remove(existing)

        if any(value is not None for value in (space_before_pt, space_after_pt, line_spacing)):
            ppr = style.find(f"{W}pPr")
            if ppr is None:
                ppr = etree.Element(f"{W}pPr")
                style_rpr = style.find(f"{W}rPr")
                if style_rpr is not None:
                    style_rpr.addprevious(ppr)
                else:
                    style.append(ppr)
            spacing = _ordered_child(ppr, "spacing", PPR_ORDER)
            if space_before_pt is not None:
                spacing.set(f"{W}before", str(round(space_before_pt * 20)))
            if space_after_pt is not None:
                spacing.set(f"{W}after", str(round(space_after_pt * 20)))
            if line_spacing is not None:
                spacing.set(f"{W}line", str(round(line_spacing * 240)))
                spacing.set(f"{W}lineRule", "auto")
        self.doc._mark("word/styles.xml")
        return {"style": name, "configured": True}

    def format_run(
        self,
        para_id: str,
        run_index: int,
        *,
        font_name: str | None = None,
        font_size_pt: float | None = None,
        color: str | None = None,
        highlight: str | None = None,
        bold: bool | None = None,
        italic: bool | None = None,
        underline: str | None = None,
        strike: bool | None = None,
        vertical: str | None = None,
    ) -> dict:
        document = self.doc._require("word/document.xml")
        paragraph = self.doc._find_para(document, para_id)
        if paragraph is None:
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Paragraph was not found")
        runs = list(paragraph.iter(f"{W}r"))
        if run_index < 0 or run_index >= len(runs):
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "Run index is out of range")
        run = runs[run_index]
        rpr = run.find(f"{W}rPr")
        if rpr is None:
            rpr = etree.Element(f"{W}rPr")
            run.insert(0, rpr)

        def upsert(tag: str, value: str | None = None) -> etree._Element:
            element = _ordered_child(rpr, tag, RPR_ORDER)
            if value is not None:
                element.set(f"{W}val", value)
            return element

        if font_name is not None:
            fonts = upsert("rFonts")
            for attribute in ("ascii", "hAnsi", "cs"):
                fonts.set(f"{W}{attribute}", font_name)
        if font_size_pt is not None:
            upsert("sz", str(round(font_size_pt * 2)))
            upsert("szCs", str(round(font_size_pt * 2)))
        if color is not None:
            upsert("color", color.upper())
        if highlight is not None:
            upsert("highlight", highlight)
        if underline is not None:
            upsert("u", underline)
        if vertical is not None:
            upsert("vertAlign", vertical)
        for tag, enabled in (("b", bold), ("i", italic), ("strike", strike)):
            if enabled is None:
                continue
            element = rpr.find(f"{W}{tag}")
            if enabled and element is None:
                _ordered_child(rpr, tag, RPR_ORDER)
            elif not enabled and element is not None:
                rpr.remove(element)
        if len(rpr) == 0:
            run.remove(rpr)
        self.doc._mark("word/document.xml")
        return {"paragraph_id": para_id, "run_index": run_index, "formatted": True}

    def format_paragraph_layout(
        self,
        para_id: str,
        *,
        alignment: str | None = None,
        space_before_pt: float | None = None,
        space_after_pt: float | None = None,
        line_spacing: float | None = None,
        left_indent_mm: float | None = None,
        right_indent_mm: float | None = None,
        first_line_mm: float | None = None,
        hanging_mm: float | None = None,
        keep_with_next: bool | None = None,
        keep_lines: bool | None = None,
        widow_control: bool | None = None,
        page_break_before: bool | None = None,
        tab_stops: list[dict] | None = None,
    ) -> dict:
        document = self.doc._require("word/document.xml")
        paragraph = self.doc._find_para(document, para_id)
        if paragraph is None:
            raise WordToolkitError(ErrorCode.DOCUMENT_NOT_FOUND, "Paragraph was not found")
        if first_line_mm is not None and hanging_mm is not None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT, "first_line_mm and hanging_mm are mutually exclusive"
            )
        ppr = paragraph.find(f"{W}pPr")
        if ppr is None:
            ppr = etree.Element(f"{W}pPr")
            paragraph.insert(0, ppr)

        def upsert(tag: str) -> etree._Element:
            return _ordered_child(ppr, tag, PPR_ORDER)

        if alignment is not None:
            upsert("jc").set(f"{W}val", alignment)
        if any(x is not None for x in (space_before_pt, space_after_pt, line_spacing)):
            spacing = upsert("spacing")
            if space_before_pt is not None:
                spacing.set(f"{W}before", str(round(space_before_pt * 20)))
            if space_after_pt is not None:
                spacing.set(f"{W}after", str(round(space_after_pt * 20)))
            if line_spacing is not None:
                spacing.set(f"{W}line", str(round(line_spacing * 240)))
                spacing.set(f"{W}lineRule", "auto")
        if any(x is not None for x in (left_indent_mm, right_indent_mm, first_line_mm, hanging_mm)):
            indent = upsert("ind")
            values = {
                "left": left_indent_mm,
                "right": right_indent_mm,
                "firstLine": first_line_mm,
                "hanging": hanging_mm,
            }
            for attribute, value in values.items():
                if value is not None:
                    indent.set(f"{W}{attribute}", str(round(value * 56.6929)))
            if first_line_mm is not None:
                indent.attrib.pop(f"{W}hanging", None)
            if hanging_mm is not None:
                indent.attrib.pop(f"{W}firstLine", None)
        for tag, enabled in (
            ("keepNext", keep_with_next),
            ("keepLines", keep_lines),
            ("widowControl", widow_control),
            ("pageBreakBefore", page_break_before),
        ):
            if enabled is None:
                continue
            element = ppr.find(f"{W}{tag}")
            if enabled and element is None:
                _ordered_child(ppr, tag, PPR_ORDER)
            elif not enabled and element is not None:
                ppr.remove(element)
        if tab_stops is not None:
            existing = ppr.find(f"{W}tabs")
            if existing is not None:
                ppr.remove(existing)
            if tab_stops:
                tabs = _ordered_child(ppr, "tabs", PPR_ORDER)
                for item in tab_stops:
                    tab = etree.SubElement(tabs, f"{W}tab")
                    tab.set(f"{W}val", item["alignment"])
                    tab.set(f"{W}pos", str(round(float(item["position_mm"]) * 56.6929)))
                    if item.get("leader") and item["leader"] != "none":
                        tab.set(f"{W}leader", item["leader"])
        self.doc._mark("word/document.xml")
        return {"paragraph_id": para_id, "formatted": True}

    def move_block(self, block_ref: str, target_ref: str, position: str) -> dict:
        body = self.doc._require("word/document.xml").find(f"{W}body")
        if body is None:
            raise WordToolkitError(ErrorCode.OOXML_INVALID, "Document body is missing")
        source = self._resolve_block(body, block_ref)
        target = self._resolve_block(body, target_ref)
        if source is target:
            return {"moved": False, "reason": "same block"}
        parent = source.getparent()
        parent.remove(source)
        if position == "before":
            target.addprevious(source)
        else:
            target.addnext(source)
        self.doc._mark("word/document.xml")
        return {
            "moved": True,
            "block_ref": block_ref,
            "target_ref": target_ref,
            "position": position,
        }

    @staticmethod
    def _resolve_block(body: etree._Element, block_ref: str) -> etree._Element:
        kind, _, value = block_ref.partition(":")
        if kind == "p":
            element = next((x for x in body.iter(f"{W}p") if x.get(f"{W14}paraId") == value), None)
        elif kind == "table":
            tables = list(body.findall(f"{W}tbl"))
            element = tables[int(value)] if value.isdigit() and int(value) < len(tables) else None
        else:
            element = None
        if element is None:
            raise WordToolkitError(
                ErrorCode.DOCUMENT_NOT_FOUND,
                "Block reference was not found",
                {"block_ref": block_ref},
            )
        return element

    def split_cells(self, table_index: int, row: int, col: int) -> dict:
        table = list(self.doc._require("word/document.xml").iter(f"{W}tbl"))[table_index]
        target_row = table.findall(f"{W}tr")[row]
        cell = target_row.findall(f"{W}tc")[col]
        tcpr = cell.find(f"{W}tcPr")
        if tcpr is None:
            return {"split": False, "reason": "cell is not merged"}
        grid_span = tcpr.find(f"{W}gridSpan")
        span = int(grid_span.get(f"{W}val", "1")) if grid_span is not None else 1
        if grid_span is not None:
            tcpr.remove(grid_span)
        vmerge = tcpr.find(f"{W}vMerge")
        if vmerge is not None:
            tcpr.remove(vmerge)
        inserted = 0
        for _ in range(max(0, span - 1)):
            clone = copy.deepcopy(cell)
            for paragraph in clone.findall(f"{W}p"):
                for child in list(paragraph):
                    if child.tag != f"{W}pPr":
                        paragraph.remove(child)
            cell.addnext(clone)
            inserted += 1
        self.doc._mark("word/document.xml")
        return {"split": bool(inserted or vmerge is not None), "new_cells": inserted}

    def package_audit(self) -> dict:
        base = self.doc.audit()
        external = self.inspection.get("external_relationships", [])
        return {
            **base,
            "external_relationships": external,
            "unknown_parts_preserved_by_default": True,
        }

    def layout_risks(self) -> dict:
        doc = self.doc._require("word/document.xml")
        body = doc.find(f"{W}body")
        risks = []

        def section_after(block: etree._Element) -> etree._Element | None:
            if body is None:
                return None
            outer = block
            while outer.getparent() is not None and outer.getparent() is not body:
                outer = outer.getparent()
            children = list(body)
            try:
                start = children.index(outer)
            except ValueError:
                return body.find(f"{W}sectPr")
            for child in children[start:]:
                if child.tag == f"{W}sectPr":
                    return child
                if child.tag == f"{W}p":
                    section = child.find(f"{W}pPr/{W}sectPr")
                    if section is not None:
                        return section
            return body.find(f"{W}sectPr")

        def available_width(section: etree._Element | None) -> int:
            page_width = 12240
            left = right = 1440
            columns = 1
            spacing = 720
            if section is not None:
                page = section.find(f"{W}pgSz")
                margins = section.find(f"{W}pgMar")
                cols = section.find(f"{W}cols")
                if page is not None:
                    page_width = int(page.get(f"{W}w", page_width))
                if margins is not None:
                    left = int(margins.get(f"{W}left", left))
                    right = int(margins.get(f"{W}right", right))
                if cols is not None:
                    columns = max(1, int(cols.get(f"{W}num", "1")))
                    spacing = int(cols.get(f"{W}space", spacing))
            printable = max(0, page_width - left - right)
            return max(0, (printable - spacing * (columns - 1)) // columns)

        for index, table in enumerate(doc.iter(f"{W}tbl")):
            grid = table.find(f"{W}tblGrid")
            width = (
                sum(int(x.get(f"{W}w", "0")) for x in grid.findall(f"{W}gridCol"))
                if grid is not None
                else 0
            )
            available = available_width(section_after(table))
            if width and available and width > available:
                risks.append(
                    {
                        "type": "table_overflow",
                        "table_index": index,
                        "width_twips": width,
                        "available_width_twips": available,
                    }
                )
        for paragraph in doc.iter(f"{W}p"):
            ppr = paragraph.find(f"{W}pPr")
            style = (
                ppr.find(f"{W}pStyle").get(f"{W}val", "")
                if ppr is not None and ppr.find(f"{W}pStyle") is not None
                else ""
            )
            if style.startswith("Heading") and (ppr is None or ppr.find(f"{W}keepNext") is None):
                risks.append(
                    {
                        "type": "orphan_heading",
                        "para_id": paragraph.get(f"{W14}paraId"),
                        "style": style,
                    }
                )
        return {"risks": risks, "count": len(risks)}
