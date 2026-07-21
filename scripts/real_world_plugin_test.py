#!/usr/bin/env python3
"""Exercise the installed WordToolkit plugin through its real MCP STDIO boundary."""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import os
import shutil
import zipfile
from datetime import UTC, datetime
from pathlib import Path
from typing import Any
from urllib.parse import unquote, urlparse

from mcp import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client
from PIL import Image, ImageDraw, ImageFont
from pypdf import PdfReader

ROOT = Path(__file__).resolve().parents[1]
INSERTED_TEXT = "WORDTOOLKIT REAL MCP TEST — bounded round-trip edit 2026-07-19"
INSERTED_EQUATION = r"\sum_{k=1}^{n} k^2 = \frac{n(n+1)(2n+1)}{6}"
PROTECTED_PARTS = (
    "customXml/item1.xml",
    "customXml/itemProps1.xml",
    "customXml/_rels/item1.xml.rels",
    "word/fontTable.xml",
    "word/embeddings/opaque-preservation.bin",
)


def digest_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def digest_file(path: Path) -> str:
    return digest_bytes(path.read_bytes())


def payload(result: Any) -> dict[str, Any]:
    if result.structuredContent:
        return result.structuredContent
    for item in result.content:
        text = getattr(item, "text", None)
        if not text:
            continue
        try:
            value = json.loads(text)
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict):
            return value
    raise RuntimeError("MCP tool returned no structured payload")


async def call_ok(session: ClientSession, name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    result = await session.call_tool(name, arguments)
    value = payload(result)
    if result.isError or not value.get("ok"):
        raise AssertionError(f"{name} failed: {json.dumps(value, ensure_ascii=False)}")
    return value


async def call_error(
    session: ClientSession,
    name: str,
    arguments: dict[str, Any],
    expected_code: str,
) -> dict[str, Any]:
    result = await session.call_tool(name, arguments)
    value = payload(result)
    if not result.isError or value.get("ok") is not False:
        raise AssertionError(f"{name} unexpectedly succeeded: {value}")
    actual = value.get("error", {}).get("code")
    if actual != expected_code:
        raise AssertionError(f"{name}: expected {expected_code}, received {actual}: {value}")
    serialized = json.dumps(value, ensure_ascii=False)
    if "Traceback" in serialized or str(ROOT.resolve()) in serialized:
        raise AssertionError(f"{name} leaked an internal traceback or source path")
    return value


def file_uri_to_path(uri: str) -> Path:
    parsed = urlparse(uri)
    if parsed.scheme != "file":
        raise AssertionError(f"Expected a local file URI, received {uri!r}")
    raw = unquote(parsed.path)
    if os.name == "nt" and len(raw) >= 3 and raw[0] == "/" and raw[2] == ":":
        raw = raw[1:]
    return Path(raw)


def copy_artifact(uri: str, destination: Path) -> Path:
    source = file_uri_to_path(uri)
    if not source.is_file():
        raise AssertionError(f"MCP artifact does not exist: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    return destination


def artifact_filename(artifact: dict[str, Any], fallback: str) -> str:
    for key in ("file_name", "filename", "name"):
        value = artifact.get(key)
        if isinstance(value, str) and value:
            return Path(value).name
    return fallback


def rewrite_part(source: Path, target: Path, part: str, replacement: bytes) -> None:
    with (
        zipfile.ZipFile(source) as original,
        zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as output,
    ):
        names = set(original.namelist())
        if part not in names:
            raise AssertionError(f"Fixture source lacks {part}")
        for item in original.infolist():
            output.writestr(
                item,
                replacement if item.filename == part else original.read(item.filename),
            )


def make_security_fixtures(source: Path, output: Path) -> dict[str, tuple[Path, str]]:
    output.mkdir(parents=True, exist_ok=True)

    invalid = output / "invalid-zip.docx"
    invalid.write_bytes(b"this is not an OPC package")

    traversal = output / "zip-traversal.docx"
    with zipfile.ZipFile(traversal, "w") as archive:
        archive.writestr("../escape.txt", b"must never escape")

    xxe = output / "xxe.docx"
    xxe_payload = (
        b'<?xml version="1.0"?>'
        b'<!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]>'
        b'<w:document xmlns:w="http://schemas.openxmlformats.org/'
        b'wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>'
        b"&e;</w:t></w:r></w:p></w:body></w:document>"
    )
    rewrite_part(source, xxe, "word/document.xml", xxe_payload)

    with zipfile.ZipFile(source) as archive:
        content_types = archive.read("[Content_Types].xml")
        macro_types = content_types.replace(
            b"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            b"application/vnd.ms-word.document.macroEnabled.main+xml",
        )
        if macro_types == content_types:
            raise AssertionError("Could not build the macro content-type fixture")
        relationships = archive.read("word/_rels/document.xml.rels")
        external_relationships = relationships.replace(
            b"</Relationships>",
            b'<Relationship Id="rEvil" Type="urn:wordtoolkit:test:evil" '
            b'Target="file:///etc/passwd" TargetMode="External"/>'
            b"</Relationships>",
        )
        if external_relationships == relationships:
            raise AssertionError("Could not build the external-relationship fixture")

    macro = output / "macro-content-type.docx"
    rewrite_part(source, macro, "[Content_Types].xml", macro_types)

    external = output / "external-file-relationship.docx"
    rewrite_part(
        source,
        external,
        "word/_rels/document.xml.rels",
        external_relationships,
    )

    bomb = output / "compression-bomb.docx"
    with (
        zipfile.ZipFile(source) as original,
        zipfile.ZipFile(bomb, "w", zipfile.ZIP_DEFLATED) as archive,
    ):
        for item in original.infolist():
            archive.writestr(item, original.read(item.filename))
        archive.writestr("word/media/compression-bomb.bin", b"0" * (2 * 1024 * 1024))

    return {
        "invalid_zip": (invalid, "OOXML_INVALID"),
        "zip_traversal": (traversal, "UNSAFE_ARCHIVE"),
        "xxe": (xxe, "UNSAFE_XML"),
        "macro_content_type": (macro, "UNSUPPORTED_FORMAT"),
        "external_file_relationship": (external, "UNSAFE_RELATIONSHIP"),
        "compression_bomb": (bomb, "UNSAFE_ARCHIVE"),
    }


def protected_hashes(path: Path) -> dict[str, str]:
    with zipfile.ZipFile(path) as archive:
        return {part: digest_bytes(archive.read(part)) for part in PROTECTED_PARTS}


def make_contact_sheet(pages: list[Path], output: Path) -> Path:
    if not pages:
        raise AssertionError("No PNG pages were returned for visual review")
    cell_width, cell_height = 360, 520
    columns = 3
    rows = (len(pages) + columns - 1) // columns
    sheet = Image.new("RGB", (cell_width * columns, cell_height * rows), "#202020")
    font = ImageFont.load_default()
    for index, page in enumerate(pages):
        with Image.open(page) as opened:
            image = opened.convert("RGB")
            image.thumbnail((cell_width - 20, cell_height - 40))
        x = (index % columns) * cell_width + (cell_width - image.width) // 2
        y = (index // columns) * cell_height + 25
        sheet.paste(image, (x, y))
        ImageDraw.Draw(sheet).text(
            ((index % columns) * cell_width + 8, (index // columns) * cell_height + 6),
            f"Page {index + 1}",
            fill="white",
            font=font,
        )
    sheet.save(output)
    return output


async def run(plugin: Path, source: Path, output: Path) -> dict[str, Any]:
    output.mkdir(parents=True, exist_ok=False)
    source_hash_before = digest_file(source)
    protected_before = protected_hashes(source)
    fixtures = make_security_fixtures(source, output / "security-fixtures")

    parameters = StdioServerParameters(
        command="uv",
        args=[
            "run",
            "--isolated",
            "--project",
            "./runtime",
            "--frozen",
            "wordtoolkit-stdio",
        ],
        cwd=plugin,
        env={
            **os.environ,
            "PYTHONDONTWRITEBYTECODE": "1",
            "WORDTOOLKIT_AUTH_MODE": "local_stdio",
            "PYTHONUTF8": "1",
            "VIRTUAL_ENV": "",
        },
    )

    async with stdio_client(parameters) as (read, write), ClientSession(read, write) as session:
        initialized = await session.initialize()
        tools = await session.list_tools()
        if len(tools.tools) != 103:
            raise AssertionError(f"Expected 103 MCP tools, received {len(tools.tools)}")

        opened = await call_ok(
            session,
            "open_document",
            {"file": {"local_path": str(source), "file_name": source.name}},
        )
        document_id = opened["data"]["document_id"]
        session_id = opened["data"]["session_id"]
        if opened["data"]["draft_version"] != 0:
            raise AssertionError("Freshly opened draft did not start at version 0")

        inspected = await call_ok(session, "inspect_document", {"document_id": document_id})
        inspection = inspected["data"]["result"]
        if inspection["equations"] != 17 or inspection["tables"] != 2:
            raise AssertionError(f"Unexpected source structure: {inspection}")

        outlined = await call_ok(session, "get_outline", {"document_id": document_id})
        outline = outlined["data"]["result"]
        if not outline:
            raise AssertionError("The torture document returned no heading anchors")
        anchor = outline[0]["para_id"]

        inserted = await call_ok(
            session,
            "insert_paragraph",
            {
                "document_id": document_id,
                "after_paragraph_id": anchor,
                "text": INSERTED_TEXT,
                "style": "ToolkitBody",
                "expected_version": 0,
            },
        )
        if inserted["data"]["draft_version"] != 1:
            raise AssertionError("Paragraph insertion did not advance the draft to version 1")
        inserted_id = inserted["data"]["result"]["para_id"]

        conflict = await call_error(
            session,
            "insert_equation",
            {
                "document_id": document_id,
                "anchor_paragraph_id": inserted_id,
                "value": INSERTED_EQUATION,
                "input_format": "latex",
                "display": True,
                "position": "after",
                "expected_version": 0,
            },
            "VERSION_CONFLICT",
        )
        if conflict["error"]["details"] != {"expected": 0, "actual": 1}:
            raise AssertionError(f"Version conflict returned bad details: {conflict}")

        equation = await call_ok(
            session,
            "insert_equation",
            {
                "document_id": document_id,
                "anchor_paragraph_id": inserted_id,
                "value": INSERTED_EQUATION,
                "input_format": "latex",
                "display": True,
                "position": "after",
                "expected_version": 1,
            },
        )
        if equation["data"]["draft_version"] != 2:
            raise AssertionError("Equation insertion did not advance the draft to version 2")

        equations = await call_ok(session, "list_equations", {"document_id": document_id})
        equation_list = equations["data"]["result"]
        inserted_equation = equation["data"]["result"]
        equation_ids = {item["equation_id"] for item in equation_list}
        if (
            len(equation_list) != 18
            or not inserted_equation["display"]
            or inserted_equation["equation_id"] not in equation_ids
        ):
            raise AssertionError("Native equation inventory did not contain the new display OMML")

        equation_validation = await call_ok(
            session, "validate_equations", {"document_id": document_id}
        )
        equation_result = equation_validation["data"]["result"]
        if not equation_result["valid"] or equation_result["valid_count"] != 18:
            raise AssertionError(f"Equation validation failed: {equation_result}")

        validation = await call_ok(session, "validate_ooxml", {"document_id": document_id})
        validation_result = validation["data"]["validation"]
        official = validation_result["validators"]["microsoft_openxml_sdk"]
        if not validation_result["valid"] or not official["available"] or not official["valid"]:
            raise AssertionError(f"OOXML validation failed: {validation_result}")

        preview = await call_ok(
            session,
            "generate_preview",
            {"document_id": document_id, "max_pages": 20, "dpi": 120},
        )
        preview_data = preview["data"]
        if not preview_data["visual_audit"]["passed"]:
            raise AssertionError(f"Visual audit failed: {preview_data['visual_audit']}")
        preview_files: list[Path] = []
        for index, artifact in enumerate(preview_data["artifacts"]):
            name = artifact_filename(artifact, f"preview-{index}")
            preview_files.append(
                copy_artifact(
                    artifact["download_url"],
                    output / "preview" / name,
                )
            )
        preview_pdf = next(path for path in preview_files if path.suffix.lower() == ".pdf")
        preview_pages = sorted(
            (path for path in preview_files if path.suffix.lower() == ".png"),
            key=lambda path: int(path.stem.rsplit("-", 1)[-1]),
        )
        pdf_pages = len(PdfReader(str(preview_pdf)).pages)
        if pdf_pages != preview_data["page_count"] or len(preview_pages) != pdf_pages:
            raise AssertionError("Preview PDF, PNG artifacts and MCP page count do not agree")
        contact_sheet = make_contact_sheet(preview_pages, output / "preview-contact-sheet.png")

        exported = await call_ok(
            session,
            "export_document",
            {
                "document_id": document_id,
                "output_format": "docx",
                "file_name": "WordToolkit-real-roundtrip.docx",
            },
        )
        export_data = exported["data"]
        exported_path = copy_artifact(
            export_data["artifact"]["download_url"],
            output / "WordToolkit-real-roundtrip.docx",
        )
        preservation = export_data["save"]["round_trip_preservation"]
        if not preservation["preserved"]:
            raise AssertionError(f"Round-trip preservation failed: {preservation}")
        if protected_hashes(exported_path) != protected_before:
            raise AssertionError("Protected opaque/custom parts changed byte-for-byte")

        reopened = await call_ok(
            session,
            "open_document",
            {
                "file": {
                    "local_path": str(exported_path),
                    "file_name": exported_path.name,
                },
                "session_id": session_id,
            },
        )
        reopened_id = reopened["data"]["document_id"]
        paragraph = await call_ok(
            session,
            "get_paragraph",
            {"document_id": reopened_id, "paragraph_id": inserted_id},
        )
        if paragraph["data"]["result"]["text"] != INSERTED_TEXT:
            raise AssertionError("Inserted paragraph did not survive export and reopen")
        reopened_equations = await call_ok(session, "list_equations", {"document_id": reopened_id})
        if len(reopened_equations["data"]["result"]) != 18:
            raise AssertionError("Inserted native equation did not survive export and reopen")
        reopened_validation = await call_ok(session, "validate_ooxml", {"document_id": reopened_id})
        if not reopened_validation["data"]["validation"]["valid"]:
            raise AssertionError("Reopened exported DOCX failed validation")

        compared = await call_ok(
            session,
            "compare_documents",
            {
                "base_file": {"local_path": str(source), "file_name": source.name},
                "revised_file": {
                    "local_path": str(exported_path),
                    "file_name": exported_path.name,
                },
                "session_id": session_id,
                "file_name": "WordToolkit-real-comparison.docx",
            },
        )
        compared_path = copy_artifact(
            compared["data"]["artifact"]["download_url"],
            output / "WordToolkit-real-comparison.docx",
        )
        if not compared["data"]["validation"]["valid"]:
            raise AssertionError("Tracked-change comparison failed validation")
        comparison_opened = await call_ok(
            session,
            "open_document",
            {
                "file": {
                    "local_path": str(compared_path),
                    "file_name": compared_path.name,
                },
                "session_id": session_id,
            },
        )
        comparison_id = comparison_opened["data"]["document_id"]
        changes = await call_ok(session, "list_tracked_changes", {"document_id": comparison_id})
        if not changes["data"]["result"]:
            raise AssertionError("Comparison document contained no native tracked changes")

        security_results: dict[str, str] = {}
        for label, (fixture, expected_code) in fixtures.items():
            error = await call_error(
                session,
                "open_document",
                {
                    "file": {
                        "local_path": str(fixture),
                        "file_name": fixture.name,
                    },
                    "session_id": session_id,
                },
                expected_code,
            )
            security_results[label] = error["error"]["code"]

        missing = await call_error(
            session,
            "inspect_document",
            {"document_id": "doc_missing_real_test"},
            "DOCUMENT_NOT_FOUND",
        )

        for active_id in (comparison_id, reopened_id, document_id):
            await call_ok(session, "close_document", {"document_id": active_id})

    source_hash_after = digest_file(source)
    if source_hash_after != source_hash_before:
        raise AssertionError("The immutable source DOCX was modified")

    report: dict[str, Any] = {
        "status": "passed",
        "timestamp_utc": datetime.now(UTC).isoformat(),
        "server": {
            "name": initialized.serverInfo.name,
            "version": initialized.serverInfo.version,
            "plugin_path": str(plugin),
            "tool_count": len(tools.tools),
        },
        "source": {
            "path": str(source),
            "sha256_before": source_hash_before,
            "sha256_after": source_hash_after,
            "immutable": source_hash_before == source_hash_after,
            "tables": inspection["tables"],
            "equations_before": inspection["equations"],
        },
        "edit": {
            "paragraph_id": inserted_id,
            "text": INSERTED_TEXT,
            "equation_id": equation["data"]["result"]["equation_id"],
            "equations_after": len(equation_list),
            "version_conflict": conflict["error"],
        },
        "validation": {
            "valid": validation_result["valid"],
            "issues": validation_result["issues"],
            "microsoft_openxml_sdk": official,
            "equations": equation_result,
            "reopened_valid": reopened_validation["data"]["validation"]["valid"],
        },
        "preview": {
            "pdf": str(preview_pdf),
            "pages": pdf_pages,
            "pngs": [str(path) for path in preview_pages],
            "contact_sheet": str(contact_sheet),
            "visual_audit": preview_data["visual_audit"],
        },
        "round_trip": {
            "exported_docx": str(exported_path),
            "opaque_parts_preserved": protected_hashes(exported_path) == protected_before,
            "preservation_report": preservation,
            "paragraph_survived_reopen": True,
            "equation_survived_reopen": True,
        },
        "comparison": {
            "docx": str(compared_path),
            "tracked_changes": len(changes["data"]["result"]),
            "valid": compared["data"]["validation"]["valid"],
        },
        "security": {
            "cases": security_results,
            "missing_document": missing["error"]["code"],
            "traceback_or_source_path_leaked": False,
        },
    }
    report_path = output / "real-world-test-report.json"
    report_path.write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    report["report"] = str(report_path)
    return report


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Run a real installed-plugin WordToolkit acceptance test"
    )
    parser.add_argument("--plugin", type=Path, required=True)
    parser.add_argument(
        "--source",
        type=Path,
        default=ROOT / "examples" / "advanced" / "WordToolkit-advanced-torture-test.docx",
    )
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    output = args.output or (
        ROOT / "artifacts" / "real-tests" / datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
    )
    print(
        json.dumps(
            asyncio.run(run(args.plugin.resolve(), args.source.resolve(), output.resolve())),
            indent=2,
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
