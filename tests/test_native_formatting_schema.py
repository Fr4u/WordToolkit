from __future__ import annotations

import importlib.util
import json
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

ROOT = Path(__file__).parents[1]
CATALOG_PATH = ROOT / "schemas" / "mcp-tools-local.v2.json"
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
CHARACTER_KEYS = BASE_KEYS | {
    "font_name_ascii",
    "font_name_bidi",
    "font_name_far_east",
    "font_name_other",
    "font_size_bidi_pt",
    "font_color_index",
    "font_color_bidi_index",
    "diacritic_color",
    "bold_bidi",
    "italic_bidi",
    "underline_style",
    "underline_color",
    "subscript",
    "superscript",
    "shadow",
    "outline",
    "emboss",
    "engrave",
    "scaling_percent",
    "spacing_pt",
    "position_pt",
    "kerning_pt",
    "disable_character_space_grid",
    "emphasis_mark",
    "ligatures",
    "number_form",
    "number_spacing",
    "stylistic_sets",
    "contextual_alternates",
    "clear_character_formatting",
}


def _formatting_schemas(value: Any) -> list[dict[str, Any]]:
    matches: list[dict[str, Any]] = []
    if isinstance(value, dict):
        properties = value.get("properties")
        if isinstance(properties, dict) and set(properties) >= BASE_KEYS:
            matches.append(value)
        for child in value.values():
            matches.extend(_formatting_schemas(child))
    elif isinstance(value, list):
        for child in value:
            matches.extend(_formatting_schemas(child))
    return matches


def test_all_native_formatting_copies_publish_the_complete_character_contract() -> None:
    catalog = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))
    schemas = _formatting_schemas(catalog)

    assert len(schemas) == 6
    for schema in schemas:
        Draft202012Validator.check_schema(schema)
        properties = schema["properties"]
        assert set(properties) >= CHARACTER_KEYS
        assert properties["font_size_pt"]["maximum"] == 1638
        assert properties["underline"]["deprecated"] is True
        assert len(properties["underline_style"]["enum"]) == 18
        assert properties["font_color_index"]["enum"] == [-1, *range(1, 17)]


def test_native_formatting_schema_accepts_full_surface_and_rejects_conflicts() -> None:
    catalog = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))
    schemas = _formatting_schemas(catalog)
    full = {
        "font_name": "Aptos",
        "font_name_ascii": "Arial",
        "font_name_bidi": "Arial",
        "font_name_far_east": "Yu Gothic",
        "font_name_other": "Courier New",
        "font_size_pt": 14,
        "font_size_bidi_pt": 12,
        "font_color_rgb": "#123456",
        "font_color_bidi_index": 6,
        "diacritic_color": "automatic",
        "bold": True,
        "italic": True,
        "bold_bidi": True,
        "italic_bidi": False,
        "underline_style": "wavy_double",
        "underline_color": "#654321",
        "strike": True,
        "double_strike": False,
        "subscript": False,
        "superscript": False,
        "shadow": True,
        "outline": True,
        "emboss": True,
        "engrave": False,
        "scaling_percent": 110,
        "spacing_pt": 0.5,
        "position_pt": 2,
        "kerning_pt": 8,
        "disable_character_space_grid": True,
        "emphasis_mark": "over_solid_circle",
        "ligatures": "standard_contextual",
        "number_form": "lining",
        "number_spacing": "tabular",
        "stylistic_sets": [1, 3],
        "contextual_alternates": True,
        "clear_character_formatting": True,
        "highlight_color_index": 7,
    }
    conflict_examples = [
        {"underline": True, "underline_style": "single"},
        {"font_color_rgb": "#000000", "font_color_index": 1},
        {"strike": True, "double_strike": True},
        {"subscript": True, "superscript": True},
        {"emboss": True, "engrave": True},
        {"position_pt": 1, "subscript": True},
        {"position_pt": 1, "superscript": True},
    ]

    for schema in schemas:
        validator = Draft202012Validator(schema)
        assert not list(validator.iter_errors(full))
        for conflict in conflict_examples:
            assert list(validator.iter_errors(conflict)), conflict


def test_native_formatting_schema_generator_is_idempotent() -> None:
    script_path = ROOT / "scripts" / "update_local_formatting_schema.py"
    spec = importlib.util.spec_from_file_location("update_local_formatting_schema", script_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    original = CATALOG_PATH.read_text(encoding="utf-8")

    assert module.render_updated(original) == original
