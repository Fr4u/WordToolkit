"""Apply the checked-in 89-action metadata delta."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "schemas/mcp-tools-local.v2.json"
DELTA = ROOT / "schemas/native-action-metadata.v1.json"
FIELDS = ("operationVersion", "permissions", "reversibility", "outputSchema")


def _property_segments(inner: str):
    """Yield top-level object property segments without decoding/reformatting."""
    out, start, depth, quoted, escaped = [], 0, 0, False, False
    for i, ch in enumerate(inner):
        if quoted:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                quoted = False
            continue
        if ch == '"':
            quoted = True
        elif ch in "[{":
            depth += 1
        elif ch in "]}":
            depth -= 1
        elif ch == "," and depth == 0:
            out.append(inner[start:i])
            start = i + 1
    out.append(inner[start:])
    return out


def _segment_name(segment: str):
    import re

    match = re.match(r"\s*\"([^\"]+)\"\s*:", segment)
    return match.group(1) if match else None


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apply", action="store_true")
    p.add_argument("--check", action="store_true")
    a = p.parse_args()
    if a.apply == a.check:
        raise SystemExit("choose exactly one of --check or --apply")
    c = json.loads(CATALOG.read_text(encoding="utf8"))
    d = json.loads(DELTA.read_text(encoding="utf8"))
    items = d["actions"]
    if (
        len(items) != 89
        or len({x["name"] for x in items}) != 89
        or len(c["native_runtime"]["actions"]) != 157
    ):
        raise SystemExit("metadata set/count drift")
    by = {x["name"]: x for x in items}
    tools = {x["name"]: x for x in c["tools"]}
    if any(x["name"] not in tools or any(f not in x for f in FIELDS) for x in items):
        raise SystemExit("invalid metadata delta")
    for n, x in by.items():
        for f in FIELDS:
            tools[n][f] = x[f]
    if a.check:
        cur = json.loads(CATALOG.read_text(encoding="utf8"))
        ct = {x["name"]: x for x in cur["tools"]}
        if sum(all(f in x for f in FIELDS) for x in cur["tools"]) != 157:
            raise SystemExit("metadata coverage is not 157/157")
        if any(ct[n].get(f) != x[f] for n, x in by.items() for f in FIELDS):
            raise SystemExit("metadata drift")
    else:
        text = CATALOG.read_text(encoding="utf8")
        start = text.index('"tools": [')
        body = text[start:]
        spans = []
        depth = 0
        begin = None
        quoted = False
        escaped = False
        for i, ch in enumerate(body):
            if quoted:
                if escaped:
                    escaped = False
                elif ch == "\\":
                    escaped = True
                elif ch == '"':
                    quoted = False
                continue
            if ch == '"':
                quoted = True
            elif ch == "{":
                if depth == 0:
                    begin = i
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    spans.append((begin, i + 1))
        replacements = []
        for left, right in spans:
            obj = json.loads(body[left:right])
            name = obj.get("name")
            if name not in by:
                continue
            if all(obj.get(field) == by[name][field] for field in FIELDS):
                continue
            # Remove only the four metadata property segments, retaining every
            # nonmetadata byte. This also repairs altered metadata in fixtures.
            original = body[left:right]
            kept = [s for s in _property_segments(original[1:-1]) if _segment_name(s) not in FIELDS]
            additions = []
            for field in FIELDS:
                value = json.dumps(by[name][field], ensure_ascii=False, indent=4).replace(
                    "\n", "\n    "
                )
                additions.append(f"    {json.dumps(field)}: {value}")
            prefix = "{" + ",".join(kept)
            if kept:
                replacement = prefix + ",\n" + ",\n".join(additions) + "\n  }"
            else:
                replacement = "{" + ",\n".join(additions) + "\n  }"
            replacements.append((left, right, replacement))
        for left, right, replacement in reversed(replacements):
            body = body[:left] + replacement + body[right:]
        CATALOG.write_text(text[:start] + body, encoding="utf8")


if __name__ == "__main__":
    main()
