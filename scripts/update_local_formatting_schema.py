#!/usr/bin/env python3
"""Update the repeated native live-Word formatting contracts deterministically.

The local native catalog intentionally keeps each tool schema self-contained.  This
script replaces only the eight character-formatting schema objects and refuses to
write when their count or shape drifts, so a broad textual replacement cannot leak
font fields into unrelated actions.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = ROOT / "schemas" / "mcp-tools-local.v2.json"
EXPECTED_SCHEMA_COUNTS = {6, 8}
OLD_FORMATTING_DESCRIPTION = "Canonical size/alignment fields are font_size_pt (1..200)"
NEW_FORMATTING_DESCRIPTION = "Canonical size/alignment fields are font_size_pt (1..1638)"
BASE_KEYS = {
    "font_name",
    "font_size_pt",
    "font_color_rgb",
    "bold",
    "italic",
    "underline",
    "strike",
    "double_strike",
    "all_caps",
    "small_caps",
    "hidden",
    "highlight_color_index",
}

UNDERLINE_STYLES = [
    "none",
    "single",
    "words",
    "double",
    "dotted",
    "thick",
    "dash",
    "dot_dash",
    "dot_dot_dash",
    "wavy",
    "dotted_heavy",
    "dash_heavy",
    "dot_dash_heavy",
    "dot_dot_dash_heavy",
    "wavy_heavy",
    "dash_long",
    "wavy_double",
    "dash_long_heavy",
]
LIGATURES = [
    "none",
    "standard",
    "contextual",
    "standard_contextual",
    "historical",
    "standard_historical",
    "contextual_historical",
    "standard_contextual_historical",
    "discretionary",
    "standard_discretionary",
    "contextual_discretionary",
    "standard_contextual_discretionary",
    "historical_discretionary",
    "standard_historical_discretionary",
    "contextual_historical_discretionary",
    "all",
]


def _property_contracts() -> dict[str, Any]:
    name = {"type": "string", "minLength": 1, "maxLength": 128}
    color = {
        "oneOf": [
            {"const": "automatic"},
            {"type": "string", "pattern": "^#[0-9A-Fa-f]{6}$"},
        ]
    }
    return {
        "font_name_ascii": dict(name),
        "font_name_bidi": dict(name),
        "font_name_far_east": dict(name),
        "font_name_other": dict(name),
        "font_size_bidi_pt": {"type": "number", "minimum": 1, "maximum": 1638},
        "font_color_index": {
            "type": "integer",
            "enum": list(range(17)),
            "description": "Microsoft Word WdColorIndex; 0 selects automatic color.",
        },
        "font_color_bidi_index": {
            "type": "integer",
            "enum": list(range(17)),
            "description": "Bidirectional-script WdColorIndex; 0 selects automatic color.",
        },
        "diacritic_color": color,
        "bold_bidi": {"type": "boolean"},
        "italic_bidi": {"type": "boolean"},
        "underline_style": {
            "type": "string",
            "enum": UNDERLINE_STYLES,
            "description": "Canonical Microsoft Word underline style. Do not combine with deprecated underline.",
        },
        "underline_color": color,
        "subscript": {"type": "boolean"},
        "superscript": {"type": "boolean"},
        "shadow": {"type": "boolean"},
        "outline": {"type": "boolean"},
        "emboss": {"type": "boolean"},
        "engrave": {"type": "boolean"},
        "scaling_percent": {"type": "integer", "minimum": 1, "maximum": 600},
        "spacing_pt": {"type": "number", "minimum": -1584, "maximum": 1584},
        "position_pt": {"type": "integer", "minimum": -1584, "maximum": 1584},
        "kerning_pt": {"type": "number", "minimum": 0, "maximum": 1638},
        "disable_character_space_grid": {"type": "boolean"},
        "emphasis_mark": {
            "type": "string",
            "enum": [
                "none",
                "over_solid_circle",
                "over_comma",
                "over_white_circle",
                "under_solid_circle",
            ],
        },
        "ligatures": {"type": "string", "enum": LIGATURES},
        "number_form": {"type": "string", "enum": ["default", "lining", "old_style"]},
        "number_spacing": {
            "type": "string",
            "enum": ["default", "proportional", "tabular"],
        },
        "stylistic_sets": {
            "type": "array",
            "maxItems": 20,
            "uniqueItems": True,
            "items": {"type": "integer", "minimum": 1, "maximum": 20},
        },
        "contextual_alternates": {"type": "boolean"},
        "clear_character_formatting": {
            "type": "boolean",
            "description": "Reset direct character formatting with Word Font.Reset before applying other fields in this object; paragraph formatting is unchanged.",
        },
    }


def _conflicts(include_paragraph: bool) -> list[dict[str, Any]]:
    constraints: list[dict[str, Any]] = [
        {"not": {"required": ["font_size", "font_size_pt"]}},
        {"not": {"required": ["underline", "underline_style"]}},
        {"not": {"required": ["font_color_rgb", "font_color_index"]}},
        {
            "not": {
                "required": ["strike", "double_strike"],
                "properties": {"strike": {"const": True}, "double_strike": {"const": True}},
            }
        },
        {
            "not": {
                "required": ["subscript", "superscript"],
                "properties": {"subscript": {"const": True}, "superscript": {"const": True}},
            }
        },
        {
            "not": {
                "required": ["emboss", "engrave"],
                "properties": {"emboss": {"const": True}, "engrave": {"const": True}},
            }
        },
        {
            "not": {
                "required": ["position_pt", "subscript"],
                "properties": {"subscript": {"const": True}},
            }
        },
        {
            "not": {
                "required": ["position_pt", "superscript"],
                "properties": {"superscript": {"const": True}},
            }
        },
    ]
    if include_paragraph:
        constraints.insert(1, {"not": {"required": ["alignment", "paragraph_alignment"]}})
    return constraints


def _update_schema_object(schema: dict[str, Any]) -> dict[str, Any]:
    properties = schema["properties"]
    include_paragraph = "paragraph_alignment" in properties
    properties["font_size_pt"]["maximum"] = 1638
    if "font_size" in properties:
        properties["font_size"]["maximum"] = 1638
    properties["underline"]["deprecated"] = True
    properties["underline"]["description"] = (
        "Compatibility boolean for single underline. Use underline_style for the complete Word surface."
    )

    additions = _property_contracts()
    rebuilt: dict[str, Any] = {}
    for key, value in properties.items():
        if key in additions:
            continue
        rebuilt[key] = value
        if key == "font_color_rgb":
            rebuilt.update(additions)
    schema["properties"] = rebuilt
    schema["allOf"] = _conflicts(include_paragraph)
    return schema


def _object_spans(text: str) -> list[tuple[int, int]]:
    spans: list[tuple[int, int]] = []
    stack: list[int] = []
    in_string = False
    escaped = False
    for index, character in enumerate(text):
        if in_string:
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == '"':
                in_string = False
            continue
        if character == '"':
            in_string = True
        elif character == "{":
            stack.append(index)
        elif character == "}":
            if not stack:
                raise RuntimeError("Unbalanced closing brace in local schema")
            start = stack.pop()
            spans.append((start, index + 1))
    if stack or in_string:
        raise RuntimeError("Unbalanced JSON in local schema")
    return spans


def _continuation_indent(text: str, start: int) -> str:
    line_start = text.rfind("\n", 0, start) + 1
    line_prefix = text[line_start:start]
    leading_spaces = len(line_prefix) - len(line_prefix.lstrip(" "))
    return " " * (leading_spaces + 2)


def _candidate_spans(text: str) -> list[tuple[int, int, dict[str, Any]]]:
    matches: list[tuple[int, int, dict[str, Any]]] = []
    for start, end in _object_spans(text):
        fragment = text[start:end]
        if '"font_color_rgb"' not in fragment or '"properties"' not in fragment:
            continue
        try:
            value = json.loads(fragment)
        except json.JSONDecodeError:
            continue
        if not isinstance(value, dict) or not isinstance(value.get("properties"), dict):
            continue
        if set(value["properties"]) >= BASE_KEYS:
            matches.append((start, end, value))
    return matches


def _compact_operation_formatting_definitions(text: str) -> str:
    target_names = {"preflight_live_word_operations", "apply_live_word_operations"}
    matches: list[tuple[int, int, dict[str, Any]]] = []
    for start, end in _object_spans(text):
        fragment = text[start:end]
        if '"liveTextFormatting"' not in fragment or '"liveRunFormatting"' not in fragment:
            continue
        try:
            value = json.loads(fragment)
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict) and value.get("name") in target_names:
            matches.append((start, end, value))
    if len(matches) != 2:
        raise RuntimeError(
            f"Expected two operation tools with formatting definitions, found {len(matches)}"
        )

    result = text
    for start, end, tool in sorted(matches, reverse=True):
        definitions = tool["inputSchema"]["$defs"]
        if "liveCharacterFormatting" in definitions:
            continue
        text_formatting = definitions["liveTextFormatting"]
        run_formatting = definitions["liveRunFormatting"]
        character_formatting = dict(run_formatting)
        character_formatting.pop("additionalProperties", None)
        character_formatting.pop("unevaluatedProperties", None)
        paragraph_properties = {
            key: value
            for key, value in text_formatting["properties"].items()
            if key not in run_formatting["properties"]
        }
        compact_text_formatting = {
            "type": "object",
            "allOf": [
                {"$ref": "#/$defs/liveCharacterFormatting"},
                {"not": {"required": ["alignment", "paragraph_alignment"]}},
            ],
            "properties": paragraph_properties,
            "unevaluatedProperties": False,
        }
        compact_run_formatting = {
            "type": "object",
            "allOf": [{"$ref": "#/$defs/liveCharacterFormatting"}],
            "unevaluatedProperties": False,
        }
        rebuilt_definitions: dict[str, Any] = {}
        for key, value in definitions.items():
            if key == "liveTextFormatting":
                rebuilt_definitions["liveCharacterFormatting"] = character_formatting
                rebuilt_definitions["liveTextFormatting"] = compact_text_formatting
            elif key == "liveRunFormatting":
                rebuilt_definitions["liveRunFormatting"] = compact_run_formatting
            else:
                rebuilt_definitions[key] = value
        tool["inputSchema"]["$defs"] = rebuilt_definitions
        indent = _continuation_indent(text, start)
        rendered = json.dumps(tool, ensure_ascii=False, indent=2).replace("\n", "\n" + indent)
        result = result[:start] + rendered + result[end:]
    return result


def render_updated(text: str) -> str:
    description_count = text.count(OLD_FORMATTING_DESCRIPTION)
    if description_count not in {0, 4}:
        raise RuntimeError(
            f"Expected zero or four legacy formatting descriptions, found {description_count}"
        )
    text = text.replace(OLD_FORMATTING_DESCRIPTION, NEW_FORMATTING_DESCRIPTION)
    matches = _candidate_spans(text)
    if len(matches) not in EXPECTED_SCHEMA_COUNTS:
        raise RuntimeError(
            f"Expected one of {sorted(EXPECTED_SCHEMA_COUNTS)} native formatting schema counts, found {len(matches)}"
        )
    result = text
    for start, end, schema in sorted(matches, reverse=True):
        updated = _update_schema_object(schema)
        indent = _continuation_indent(text, start)
        rendered = json.dumps(updated, ensure_ascii=False, indent=2)
        rendered = rendered.replace("\n", "\n" + indent)
        result = result[:start] + rendered + result[end:]
    result = _compact_operation_formatting_definitions(result)
    json.loads(result)
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--check", action="store_true", help="Fail if regeneration would change the file"
    )
    args = parser.parse_args()
    original = SCHEMA_PATH.read_text(encoding="utf-8")
    updated = render_updated(original)
    if args.check:
        if updated != original:
            raise SystemExit(
                "Local formatting schemas are out of date; run this script without --check"
            )
        return 0
    SCHEMA_PATH.write_text(updated, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
