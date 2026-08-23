from __future__ import annotations

import hashlib
import json
import subprocess
import zipfile
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

from lxml import etree

from ..config import Settings
from ..math.omml import M, parse_omml
from ..security import (
    REL_NS,
    SafePackageInspector,
    parse_xml_bytes,
    resolve_internal_target,
)

W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"
W14 = "{http://schemas.microsoft.com/office/word/2010/wordml}"
CT = "{http://schemas.openxmlformats.org/package/2006/content-types}"


@dataclass(slots=True)
class ValidationIssue:
    severity: str
    code: str
    message: str
    part: str = ""
    detail: dict = field(default_factory=dict)

    def to_dict(self) -> dict:
        return {
            "severity": self.severity,
            "code": self.code,
            "message": self.message,
            "part": self.part,
            "detail": self.detail,
        }


class OoxmlValidator:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.package_inspector = SafePackageInspector(settings)

    def validate(self, path: Path) -> dict:
        issues: list[ValidationIssue] = []
        # Every pass, including the optional external validator, consumes one
        # bounded private snapshot. Reading ``path`` again would permit a
        # concurrent replacement to mix two package versions.
        with self.package_inspector.inspect_stable(path) as (snapshot, package_report):
            with zipfile.ZipFile(snapshot) as archive:
                bad_crc = archive.testzip()
                if bad_crc:
                    issues.append(ValidationIssue("error", "ZIP_CRC", "CRC failure", bad_crc))
                names = {name.casefold(): name for name in archive.namelist()}
                roots: dict[str, etree._Element] = {}
                for name in archive.namelist():
                    if PurePosixPath(name).suffix.lower() != ".xml" and not name.endswith(".rels"):
                        continue
                    try:
                        roots[name] = parse_xml_bytes(archive.read(name), part=name)
                    except Exception as exc:
                        issues.append(ValidationIssue("error", "XML_INVALID", str(exc), name))
                self._validate_content_types(roots, names, issues)
                self._validate_relationships(roots, names, issues)
                self._validate_para_ids(roots, issues)
                self._validate_notes(roots, "footnote", issues)
                self._validate_notes(roots, "endnote", issues)
                self._validate_math(roots, issues)
                self._validate_orphaned_parts(roots, set(archive.namelist()), issues)
            official = self._official_openxml_validation(snapshot)
        structural_valid = not any(issue.severity == "error" for issue in issues)
        structural_errors = sum(issue.severity == "error" for issue in issues)
        official_errors = int(
            official.get("errors") or (1 if official.get("valid") is False else 0)
        )
        return {
            "valid": structural_valid and official.get("valid") is not False,
            "errors": structural_errors + official_errors,
            "warnings": sum(issue.severity == "warning" for issue in issues),
            "issues": [issue.to_dict() for issue in issues],
            "package": package_report.to_dict(),
            "validators": {
                "wordtoolkit_structural": {"valid": structural_valid},
                "microsoft_openxml_sdk": official,
            },
        }

    def _official_openxml_validation(self, path: Path) -> dict:
        executable = self.settings.openxml_validator_path
        if not executable.is_file():
            return {
                "available": False,
                "valid": None,
                "notice": "Microsoft Open XML SDK validator binary is not installed in this runtime",
            }
        try:
            result = subprocess.run(  # noqa: S603
                [str(executable), str(path)],
                capture_output=True,
                text=True,
                timeout=self.settings.openxml_validator_timeout_seconds,
                check=False,
            )
        except subprocess.TimeoutExpired:
            return {"available": True, "valid": False, "error": "validator_timeout"}
        try:
            payload = json.loads(result.stdout)
        except json.JSONDecodeError:
            return {
                "available": True,
                "valid": False,
                "error": "validator_output_invalid",
                "exit_code": result.returncode,
            }
        payload["available"] = True
        payload["exit_code"] = result.returncode
        return payload

    @staticmethod
    def _validate_content_types(
        roots: dict[str, etree._Element], names: dict[str, str], issues: list[ValidationIssue]
    ) -> None:
        root = roots.get("[Content_Types].xml")
        if root is None:
            return
        overrides = [x.get("PartName", "").lstrip("/") for x in root.findall(f"{CT}Override")]
        for part, count in Counter(x.casefold() for x in overrides).items():
            if count > 1:
                issues.append(
                    ValidationIssue(
                        "error",
                        "DUPLICATE_CONTENT_TYPE",
                        "Duplicate content type override",
                        "[Content_Types].xml",
                        {"part": part},
                    )
                )
        for part in overrides:
            if part.casefold() not in names:
                issues.append(
                    ValidationIssue(
                        "error",
                        "CONTENT_TYPE_TARGET_MISSING",
                        "Content type points to a missing part",
                        "[Content_Types].xml",
                        {"part": part},
                    )
                )

    @staticmethod
    def _source_for_rels(name: str) -> str:
        if name == "_rels/.rels":
            return ""
        path = PurePosixPath(name)
        return str(path.parent.parent / path.name.removesuffix(".rels"))

    def _validate_relationships(
        self,
        roots: dict[str, etree._Element],
        names: dict[str, str],
        issues: list[ValidationIssue],
    ) -> None:
        for part, root in roots.items():
            if not part.endswith(".rels"):
                continue
            ids = [x.get("Id", "") for x in root.findall(f"{REL_NS}Relationship")]
            duplicates = [value for value, count in Counter(ids).items() if count > 1]
            if duplicates:
                issues.append(
                    ValidationIssue(
                        "error",
                        "DUPLICATE_REL_ID",
                        "Relationship IDs are not unique",
                        part,
                        {"ids": duplicates},
                    )
                )
            source = self._source_for_rels(part)
            for rel in root.findall(f"{REL_NS}Relationship"):
                target = rel.get("Target", "")
                if rel.get("TargetMode") == "External":
                    continue
                try:
                    resolved = resolve_internal_target(source, target)
                except Exception as exc:
                    issues.append(
                        ValidationIssue(
                            "error", "REL_TARGET_UNSAFE", str(exc), part, {"target": target}
                        )
                    )
                    continue
                if resolved.casefold() not in names:
                    issues.append(
                        ValidationIssue(
                            "error",
                            "REL_TARGET_MISSING",
                            "Internal relationship target is missing",
                            part,
                            {"id": rel.get("Id"), "target": target, "resolved": resolved},
                        )
                    )

    def _validate_orphaned_parts(
        self,
        roots: dict[str, etree._Element],
        package_names: set[str],
        issues: list[ValidationIssue],
    ) -> None:
        """Report OPC parts unreachable from package-level relationships."""
        graph: dict[str, set[str]] = {}
        for rels_part, root in roots.items():
            if not rels_part.endswith(".rels"):
                continue
            source = self._source_for_rels(rels_part)
            for relationship in root.findall(f"{REL_NS}Relationship"):
                if relationship.get("TargetMode") == "External":
                    continue
                try:
                    target = resolve_internal_target(source, relationship.get("Target", ""))
                except Exception:  # noqa: S112 - unsafe targets are reported in relationship pass
                    continue
                graph.setdefault(source, set()).add(target)

        reachable: set[str] = set()
        pending = list(graph.get("", set()))
        while pending:
            part = pending.pop()
            if part in reachable:
                continue
            reachable.add(part)
            pending.extend(graph.get(part, set()) - reachable)

        candidates = {
            name
            for name in package_names
            if name
            and not name.endswith("/")
            and name != "[Content_Types].xml"
            and not name.endswith(".rels")
        }
        for part in sorted(candidates - reachable):
            issues.append(
                ValidationIssue(
                    "warning",
                    "ORPHANED_PART",
                    "OPC part is not reachable from package relationships",
                    part,
                )
            )

    @staticmethod
    def _validate_para_ids(roots: dict[str, etree._Element], issues: list[ValidationIssue]) -> None:
        locations: dict[str, list[str]] = {}
        for part, root in roots.items():
            for paragraph in root.iter(f"{W}p"):
                para_id = paragraph.get(f"{W14}paraId")
                if para_id:
                    locations.setdefault(para_id, []).append(part)
        duplicates = {key: parts for key, parts in locations.items() if len(parts) > 1}
        if duplicates:
            issues.append(
                ValidationIssue(
                    "error",
                    "DUPLICATE_PARA_ID",
                    "w14:paraId must be unique across stories",
                    detail={"duplicates": duplicates},
                )
            )

    @staticmethod
    def _validate_notes(
        roots: dict[str, etree._Element], kind: str, issues: list[ValidationIssue]
    ) -> None:
        document = roots.get("word/document.xml")
        definitions = roots.get(f"word/{kind}s.xml")
        if document is None:
            return
        ref_ids = {x.get(f"{W}id") for x in document.iter(f"{W}{kind}Reference") if x.get(f"{W}id")}
        def_ids = set()
        if definitions is not None:
            def_ids = {
                x.get(f"{W}id")
                for x in definitions.findall(f"{W}{kind}")
                if x.get(f"{W}id") not in {"-1", "0", None}
            }
        missing = sorted(ref_ids - def_ids)
        orphaned = sorted(def_ids - ref_ids)
        if missing:
            issues.append(
                ValidationIssue(
                    "error",
                    f"{kind.upper()}_MISSING",
                    f"{kind} references have no definitions",
                    detail={"ids": missing},
                )
            )
        if orphaned:
            issues.append(
                ValidationIssue(
                    "warning",
                    f"{kind.upper()}_ORPHANED",
                    f"{kind} definitions are unreferenced",
                    detail={"ids": orphaned},
                )
            )

    @staticmethod
    def _validate_math(roots: dict[str, etree._Element], issues: list[ValidationIssue]) -> None:
        count = 0
        for part, root in roots.items():
            for equation in root.iter(f"{M}oMath"):
                count += 1
                try:
                    parse_omml(etree.tostring(equation, encoding="unicode"))
                except Exception as exc:
                    issues.append(
                        ValidationIssue(
                            "error", "OMML_INVALID", str(exc), part, {"equation_index": count - 1}
                        )
                    )


def package_hashes(path: Path) -> dict[str, str]:
    with zipfile.ZipFile(path) as archive:
        return {
            name: hashlib.sha256(archive.read(name)).hexdigest()
            for name in archive.namelist()
            if not name.endswith("/")
        }


def preservation_report(
    before: dict[str, str], after: dict[str, str], modified_parts: list[str]
) -> dict:
    modified = {part.casefold() for part in modified_parts}
    missing = [part for part in before if part not in after]
    changed_unexpectedly = [
        part
        for part, digest in before.items()
        if part in after and after[part] != digest and part.casefold() not in modified
    ]
    new_parts = [part for part in after if part not in before]
    return {
        "preserved": not missing and not changed_unexpectedly,
        "missing_parts": missing,
        "unexpectedly_changed_parts": changed_unexpectedly,
        "new_parts": new_parts,
    }
