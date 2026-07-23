from __future__ import annotations

import math
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

OPERATION_CONTRACT: Literal["wordtoolkit.apply_document_operations/1.0"] = (
    "wordtoolkit.apply_document_operations/1.0"
)

ORDINARY_DRAFT_MUTATION_TOOLS = (
    "insert_paragraph",
    "replace_paragraph",
    "delete_paragraph",
    "move_block",
    "create_style",
    "update_style",
    "apply_style",
    "normalize_formatting",
    "format_paragraph",
    "format_run",
    "manage_lists",
    "insert_caption",
    "insert_table",
    "modify_table",
    "merge_cells",
    "split_cells",
    "set_cell_properties",
    "insert_equation",
    "replace_equation",
    "number_equations",
    "add_equation_reference",
    "manage_headers_footers",
    "manage_footnotes_endnotes",
    "manage_comments",
    "manage_bookmarks",
    "manage_cross_references",
    "manage_fields",
    "insert_image",
    "manage_sections",
    "enable_track_changes",
    "insert_tracked_change",
    "accept_changes",
    "reject_changes",
)

DRAFT_BATCH_ACTIONS: dict[str, frozenset[str]] = {
    "manage_lists": frozenset(
        {"apply", "create_multilevel", "restart", "promote", "demote", "suppress"}
    ),
    "manage_headers_footers": frozenset({"set_text", "replace_text", "delete"}),
    "manage_footnotes_endnotes": frozenset({"add", "update", "delete"}),
    "manage_comments": frozenset({"add", "reply", "update", "resolve", "delete"}),
    "manage_bookmarks": frozenset({"add", "remove"}),
    "manage_cross_references": frozenset({"add"}),
    "manage_fields": frozenset(
        {"add", "delete", "update_on_open", "generate_toc", "generate_figures", "generate_tables"}
    ),
    "manage_sections": frozenset(
        {
            "add_break",
            "delete_break",
            "set_page",
            "set_columns",
            "different_first_page",
            "odd_even_headers",
            "page_break",
        }
    ),
}

DRAFT_BATCH_MAX_OPERATIONS = 16
DRAFT_BATCH_MAX_FILES = DRAFT_BATCH_MAX_OPERATIONS
DRAFT_BATCH_MAX_ARGUMENT_BYTES = 1_048_576


class DraftBatchOperation(BaseModel):
    """Provider-neutral operation envelope; adapters validate nested arguments."""

    model_config = ConfigDict(extra="forbid")

    operation: str = Field(min_length=1, max_length=64)
    arguments: dict[str, Any]


class DraftBatchFile(BaseModel):
    """Top-level file reference compatible with MCP Apps file parameters."""

    model_config = ConfigDict(extra="forbid")

    download_url: str = Field(min_length=1, max_length=4096)
    file_id: str = Field(min_length=1, max_length=512)
    mime_type: str = Field(default="", max_length=255)
    file_name: str = Field(default="", max_length=255)


class DraftBatchStepResult(BaseModel):
    model_config = ConfigDict(extra="forbid")

    index: int = Field(ge=0, lt=DRAFT_BATCH_MAX_OPERATIONS)
    result: dict[str, Any]


class DraftBatchOutcome(BaseModel):
    """Stable compact result shared by current and future protocol adapters."""

    model_config = ConfigDict(extra="forbid")

    document_id: str
    draft_version: int = Field(ge=0)
    results: list[DraftBatchStepResult] = Field(min_length=1, max_length=DRAFT_BATCH_MAX_OPERATIONS)

    @model_validator(mode="after")
    def result_order_matches(self) -> DraftBatchOutcome:
        if [item.index for item in self.results] != list(range(len(self.results))):
            raise ValueError("batch result indexes must be contiguous and ordered")
        return self


def compact_batch_result(value: Any) -> dict[str, Any]:
    """Project mutation results without echoing document content into AI context."""
    if not isinstance(value, dict):
        return {}
    allowed_suffixes = ("_id", "_ids", "_index", "_count")
    allowed_names = {
        "action",
        "changed",
        "count",
        "created",
        "deleted",
        "enabled",
        "inserted",
        "moved",
        "replaced",
        "status",
        "updated",
    }
    compact: dict[str, Any] = {}
    for key, item in value.items():
        if key not in allowed_names and not key.endswith(allowed_suffixes):
            continue
        scalar_is_safe = (
            (isinstance(item, str) and len(item) <= 256)
            or isinstance(item, (int, bool))
            or item is None
            or (isinstance(item, float) and math.isfinite(item))
        )
        if scalar_is_safe:
            compact[key] = item
        elif isinstance(item, list) and len(item) <= 32:
            safe_items = all(
                (not isinstance(entry, str) or len(entry) <= 256)
                and (not isinstance(entry, float) or math.isfinite(entry))
                and (isinstance(entry, (str, int, float, bool)) or entry is None)
                for entry in item
            )
            if safe_items:
                compact[key] = item
    return compact


__all__ = [
    "DRAFT_BATCH_ACTIONS",
    "DRAFT_BATCH_MAX_ARGUMENT_BYTES",
    "DRAFT_BATCH_MAX_FILES",
    "DRAFT_BATCH_MAX_OPERATIONS",
    "DraftBatchFile",
    "DraftBatchOperation",
    "DraftBatchOutcome",
    "DraftBatchStepResult",
    "OPERATION_CONTRACT",
    "ORDINARY_DRAFT_MUTATION_TOOLS",
    "compact_batch_result",
]
