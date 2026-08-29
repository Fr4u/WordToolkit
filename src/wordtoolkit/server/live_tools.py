from __future__ import annotations

import asyncio
from typing import Annotated, Any, Literal

from mcp.server.fastmcp import FastMCP
from mcp.types import ToolAnnotations
from pydantic import BaseModel, ConfigDict, Field, model_validator

from ..auth import current_subject, require_scope
from ..errors import ok
from ..runtime import ToolRuntime
from .tools import _safe

LIVE_READ = ToolAnnotations(
    readOnlyHint=True,
    destructiveHint=False,
    idempotentHint=True,
    openWorldHint=True,
)
LIVE_WRITE = ToolAnnotations(
    readOnlyHint=False,
    destructiveHint=True,
    idempotentHint=False,
    openWorldHint=True,
)
LIVE_HANDLE = ToolAnnotations(
    readOnlyHint=False,
    destructiveHint=False,
    idempotentHint=True,
    openWorldHint=True,
)
LIVE_VERSION = Annotated[int, Field(strict=True, ge=0)]
WordColorIndex = Literal[-1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]
WordDirectColor = (
    Literal["automatic"]
    | Annotated[
        str,
        Field(pattern=r"^#[0-9A-Fa-f]{6}$"),
    ]
)
WordUnderlineStyle = Literal[
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
WordEmphasisMark = Literal[
    "none",
    "over_solid_circle",
    "over_comma",
    "over_white_circle",
    "under_solid_circle",
]
WordLigatures = Literal[
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


def _formatting_schema_conflicts(*, include_paragraph: bool) -> list[dict[str, Any]]:
    conflicts: list[dict[str, Any]] = [
        {"not": {"required": ["font_size", "font_size_pt"]}},
        {"not": {"required": ["underline", "underline_style"]}},
        {"not": {"required": ["font_color_rgb", "font_color_index"]}},
        {
            "not": {
                "required": ["strike", "double_strike"],
                "properties": {
                    "strike": {"const": True},
                    "double_strike": {"const": True},
                },
            }
        },
        {
            "not": {
                "required": ["subscript", "superscript"],
                "properties": {
                    "subscript": {"const": True},
                    "superscript": {"const": True},
                },
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
        conflicts.insert(1, {"not": {"required": ["alignment", "paragraph_alignment"]}})
    return conflicts


class LiveRunFormatting(BaseModel):
    model_config = ConfigDict(
        extra="forbid",
        json_schema_extra={"allOf": _formatting_schema_conflicts(include_paragraph=False)},
    )

    font_name: str | None = Field(default=None, min_length=1, max_length=128)
    font_name_ascii: str | None = Field(default=None, min_length=1, max_length=128)
    font_name_bidi: str | None = Field(default=None, min_length=1, max_length=128)
    font_name_far_east: str | None = Field(default=None, min_length=1, max_length=128)
    font_name_other: str | None = Field(default=None, min_length=1, max_length=128)
    font_size_pt: float | None = Field(default=None, ge=1, le=1638)
    font_size_bidi_pt: float | None = Field(default=None, ge=1, le=1638)
    font_size: float | None = Field(
        default=None,
        ge=1,
        le=1638,
        json_schema_extra={"deprecated": True},
    )
    font_color_rgb: str | None = Field(default=None, pattern=r"^#[0-9A-Fa-f]{6}$")
    font_color_index: WordColorIndex | None = None
    font_color_bidi_index: WordColorIndex | None = None
    diacritic_color: WordDirectColor | None = None
    bold: bool | None = None
    italic: bool | None = None
    bold_bidi: bool | None = None
    italic_bidi: bool | None = None
    underline: bool | None = Field(
        default=None,
        json_schema_extra={"deprecated": True},
        description="Compatibility boolean for single underline; use underline_style for every Word style.",
    )
    underline_style: WordUnderlineStyle | None = None
    underline_color: WordDirectColor | None = None
    strike: bool | None = None
    double_strike: bool | None = None
    subscript: bool | None = None
    superscript: bool | None = None
    all_caps: bool | None = None
    small_caps: bool | None = None
    hidden: bool | None = None
    shadow: bool | None = None
    outline: bool | None = None
    emboss: bool | None = None
    engrave: bool | None = None
    scaling_percent: int | None = Field(default=None, ge=1, le=600)
    spacing_pt: float | None = Field(default=None, ge=-1584, le=1584)
    position_pt: int | None = Field(default=None, ge=-1584, le=1584)
    kerning_pt: float | None = Field(default=None, ge=0, le=1638)
    disable_character_space_grid: bool | None = None
    emphasis_mark: WordEmphasisMark | None = None
    ligatures: WordLigatures | None = None
    number_form: Literal["default", "lining", "old_style"] | None = None
    number_spacing: Literal["default", "proportional", "tabular"] | None = None
    stylistic_sets: list[Annotated[int, Field(ge=1, le=20)]] | None = Field(
        default=None,
        max_length=20,
    )
    contextual_alternates: bool | None = None
    clear_character_formatting: bool | None = Field(
        default=None,
        description="Reset direct Font formatting before applying the remaining fields; paragraph formatting is unchanged.",
    )
    highlight_color_index: int | None = Field(default=None, ge=0, le=16)

    @model_validator(mode="after")
    def validate_run_formatting_contract(self) -> LiveRunFormatting:
        if self.font_size is not None and self.font_size_pt is not None:
            raise ValueError("Use either font_size or font_size_pt, not both")
        if self.strike is True and self.double_strike is True:
            raise ValueError("strike and double_strike cannot both be true")
        if self.underline is not None and self.underline_style is not None:
            raise ValueError("Use either deprecated underline or canonical underline_style")
        if self.font_color_rgb is not None and self.font_color_index is not None:
            raise ValueError("Use either font_color_rgb or font_color_index")
        if self.subscript is True and self.superscript is True:
            raise ValueError("subscript and superscript cannot both be true")
        if self.emboss is True and self.engrave is True:
            raise ValueError("emboss and engrave cannot both be true")
        if self.position_pt is not None and (self.subscript is True or self.superscript is True):
            raise ValueError("position_pt cannot combine with subscript/superscript")
        if self.stylistic_sets is not None and len(set(self.stylistic_sets)) != len(
            self.stylistic_sets
        ):
            raise ValueError("stylistic_sets values must be unique")
        return self


class LiveTextFormatting(LiveRunFormatting):
    model_config = ConfigDict(
        extra="forbid",
        json_schema_extra={"allOf": _formatting_schema_conflicts(include_paragraph=True)},
    )

    paragraph_alignment: Literal["left", "center", "right", "justify", "distribute"] | None = None
    alignment: Literal["left", "center", "right", "justify", "distribute"] | None = Field(
        default=None,
        json_schema_extra={"deprecated": True},
    )
    space_before_pt: float | None = Field(default=None, ge=0, le=1584)
    space_after_pt: float | None = Field(default=None, ge=0, le=1584)
    left_indent_pt: float | None = Field(default=None, ge=-1584, le=1584)
    right_indent_pt: float | None = Field(default=None, ge=-1584, le=1584)
    first_line_indent_pt: float | None = Field(default=None, ge=-1584, le=1584)
    keep_with_next: bool | None = None
    keep_together: bool | None = None
    page_break_before: bool | None = None
    widow_control: bool | None = None

    @model_validator(mode="after")
    def reject_duplicate_alignment_alias(self) -> LiveTextFormatting:
        if self.alignment is not None and self.paragraph_alignment is not None:
            raise ValueError("Use either alignment or paragraph_alignment, not both")
        return self


class LiveTextRun(BaseModel):
    model_config = ConfigDict(extra="forbid")

    text: str = Field(min_length=1, max_length=200_000)
    formatting: LiveRunFormatting = Field(default_factory=LiveRunFormatting)


class LiveTextOperation(BaseModel):
    model_config = ConfigDict(
        extra="forbid",
        json_schema_extra={
            "oneOf": [
                {"required": ["text"], "not": {"required": ["runs"]}},
                {"required": ["runs"], "not": {"required": ["text"]}},
            ]
        },
    )

    type: Literal["text"]
    text: str | None = Field(default=None, min_length=1, max_length=200_000)
    runs: list[LiveTextRun] | None = Field(default=None, min_length=1, max_length=1_000)
    as_new_paragraph: bool = False
    style: str = Field(default="", max_length=128)
    formatting: LiveTextFormatting | None = None

    @model_validator(mode="after")
    def require_exactly_one_text_source(self) -> LiveTextOperation:
        if (self.text is None) == (self.runs is None):
            raise ValueError("Provide exactly one of text or runs")
        return self


class LiveEquationOperation(BaseModel):
    model_config = ConfigDict(extra="forbid")

    type: Literal["equation"]
    value: str | dict[str, Any]
    input_format: Literal["latex", "unicodemath", "mathml", "omml", "ast"] = "latex"
    display: bool = True
    verify_readback: bool | None = None


LiveWordOperation = Annotated[
    LiveTextOperation | LiveEquationOperation,
    Field(discriminator="type"),
]


def register_live_tools(mcp: FastMCP, runtime: ToolRuntime) -> None:
    """Register Windows desktop tools only on the local STDIO server."""

    @mcp.tool(
        title="List open Microsoft Word documents",
        description="List documents in the already-running Microsoft Word application. This local Windows-only tool never launches Word and never opens a file copy.",
        annotations=LIVE_READ,
    )
    @_safe
    async def list_live_word_documents() -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(runtime.live_word.list_documents)
        return ok(result)

    @mcp.tool(
        title="Connect to an open Microsoft Word document",
        description="Connect WordToolkit to exactly one document that is already open in Microsoft Word. The returned live_document_id targets the real Word document rather than an isolated DOCX draft.",
        annotations=LIVE_HANDLE,
    )
    @_safe
    async def connect_live_word_document(
        document_name: str = Field(default="", max_length=260),
        full_path: str = Field(default="", max_length=32_767),
        use_active: bool = True,
        activate: bool = True,
    ) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.connect,
            subject,
            document_name=document_name,
            full_path=full_path,
            use_active=use_active,
            activate=activate,
        )
        return ok(result)

    @mcp.tool(
        title="Inspect connected Word Live document",
        description="Read live Microsoft Word metadata and counts for a connected document without exporting or rebuilding it.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_document(live_document_id: str) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(runtime.live_word.inspect, subject, live_document_id)
        return ok(result)

    @mcp.tool(
        title="Map structures in connected Microsoft Word document",
        description="Inventory Word stories and bounded collection counts for sections, styles, tables, equations, fields, bookmarks, links, comments, revisions, content controls, shapes, notes, lists and generated tables without returning document content.",
        annotations=LIVE_READ,
    )
    @_safe
    async def map_live_word_structures(
        live_document_id: str,
        include_type_histograms: bool = False,
        adaptive_type_histograms: bool = True,
        max_type_items: int = Field(default=2_000, ge=1, le=10_000),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.structure_map,
            subject,
            live_document_id,
            include_type_histograms=include_type_histograms,
            adaptive_type_histograms=adaptive_type_histograms,
            max_type_items=max_type_items,
        )
        return ok(result)

    @mcp.tool(
        title="Inspect native items in one Word structure collection",
        description="Read a bounded page of semantic metadata from one native Word collection through one COM attachment. Supports paragraphs, sections, styles, tables, equations, fields, forms, bookmarks, links, comments, revisions, content controls, shapes, notes, lists, subdocuments, variables and generated tables. External addresses and field codes are never returned; optional text previews are bounded and never stored by adaptive learning.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_structure_items(
        live_document_id: str,
        structure: Literal[
            "paragraphs",
            "sections",
            "styles",
            "tables",
            "equations",
            "fields",
            "form_fields",
            "bookmarks",
            "hyperlinks",
            "comments",
            "revisions",
            "content_controls",
            "inline_shapes",
            "floating_shapes",
            "footnotes",
            "endnotes",
            "lists",
            "list_paragraphs",
            "subdocuments",
            "variables",
            "tables_of_contents",
            "tables_of_figures",
            "tables_of_authorities",
        ],
        offset: int = Field(default=0, ge=0, le=1_000_000),
        limit: int = Field(default=50, ge=1, le=200),
        include_text: bool = False,
        max_text_chars: int = Field(default=500, ge=1, le=2_000),
        adaptive_property_probing: bool = True,
    ) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.inspect_structure_items,
            subject,
            live_document_id,
            structure=structure,
            offset=offset,
            limit=limit,
            include_text=include_text,
            max_text_chars=max_text_chars,
            adaptive_property_probing=adaptive_property_probing,
        )
        return ok(result)

    @mcp.tool(
        title="Inspect local Word equation learning",
        description="Read privacy-preserving aggregate outcomes learned from native Word equation classes. No formula text, document text, paths or owner identifiers are stored or returned.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_equation_learning() -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(runtime.live_word.inspect_learning)
        return ok(result)

    @mcp.tool(
        title="Inspect local Word structure learning",
        description="Read privacy-preserving aggregate evidence used to schedule adaptive native type scans. It stores only fixed collection names, native enum values, scan outcomes and timing—never document content, counts, paths, handles or identifiers.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_structure_learning() -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(runtime.live_word.inspect_structure_learning)
        return ok(result)

    @mcp.tool(
        title="Browse installed Microsoft Word object-model types",
        description="Build or query a bounded local catalog of the actual Word COM type library installed on this PC. Returns paged API type metadata only—never document content, document counts, paths, handles, owner identifiers, help text or help-file paths. The first scan attaches read-only to already-running Word; cached queries do not attach again. Set refresh=true after an Office update.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_object_model_types(
        query: str = Field(default="", max_length=128),
        kind: Literal[
            "",
            "enum",
            "record",
            "module",
            "interface",
            "dispatch",
            "coclass",
            "alias",
            "union",
        ] = "",
        offset: int = Field(default=0, ge=0, le=1_000_000),
        limit: int = Field(default=100, ge=1, le=200),
        refresh: bool = False,
    ) -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.inspect_object_model_types,
            query=query,
            kind=kind,
            offset=offset,
            limit=limit,
            refresh=refresh,
        )
        return ok(result)

    @mcp.tool(
        title="Browse members of one installed Microsoft Word object-model type",
        description="Return a bounded page of methods, properties, parameters, variables or enum values for one exact type from the cached installed Word COM catalog. No Word document content or local help paths are read, stored or returned.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_object_model_members(
        type_name: str = Field(max_length=256),
        query: str = Field(default="", max_length=128),
        kind: Literal[
            "",
            "method",
            "property_get",
            "property_put",
            "property_put_ref",
            "enum_value",
            "variable",
        ] = "",
        offset: int = Field(default=0, ge=0, le=1_000_000),
        limit: int = Field(default=100, ge=1, le=200),
        refresh: bool = False,
    ) -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.inspect_object_model_members,
            type_name,
            query=query,
            kind=kind,
            offset=offset,
            limit=limit,
            refresh=refresh,
        )
        return ok(result)

    @mcp.tool(
        title="Browse individual virtual tools for installed Microsoft Word members",
        description="Search the derived registry without dumping thousands of repeated schemas. The default summary detail returns stable capability IDs, signature counts, target roots and safety policy; after choosing one capability, request detail='full' with its capability ID as query and limit=1 for exact JSON input/output schemas. Query by type, member name, capability ID, or virtual-tool name. Constants, reads, document-scoped edits, events, lifecycle actions and blocked external effects remain distinct, and arbitrary COM paths are never exposed.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_member_capabilities(
        query: str = Field(default="", max_length=128),
        type_name: str = Field(default="", max_length=256),
        member_kind: Literal[
            "",
            "method",
            "property_get",
            "property_put",
            "property_put_ref",
            "enum_value",
            "variable",
        ] = "",
        effect: Literal[
            "",
            "constant",
            "read",
            "format",
            "content",
            "structure",
            "calculation",
            "view",
            "event",
            "external",
            "lifecycle",
            "unknown",
        ] = "",
        execution: Literal[
            "",
            "metadata_only",
            "read_allowed",
            "write_allowed",
            "blocked",
        ] = "",
        detail: Literal["summary", "full"] = "summary",
        offset: int = Field(default=0, ge=0, le=1_000_000),
        limit: int = Field(default=100, ge=1, le=200),
        refresh: bool = False,
    ) -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.inspect_member_capabilities,
            query=query,
            type_name=type_name,
            member_kind=member_kind,
            effect=effect,
            execution=execution,
            detail=detail,
            offset=offset,
            limit=limit,
            refresh=refresh,
        )
        return ok(result)

    @mcp.tool(
        title="Preflight catalog-backed Microsoft Word member operations",
        description="Validate up to 50 operations against their per-member virtual-tool contracts: stable capability IDs, typed targets, result and enum-constant references, optional omissions, argument positions, COM parameter types and safety policy before any live Word mutation.",
        annotations=LIVE_READ,
    )
    @_safe
    async def preflight_live_word_member_operations(
        operations: list[dict],
    ) -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.preflight_member_operations,
            operations,
        )
        return ok(result)

    @mcp.tool(
        title="Execute catalog-backed operations in an open Microsoft Word document",
        description="Execute up to 50 preflighted per-member virtual tools by stable capability ID. Targets are restricted to the connected document, its current selection or content, and typed results of earlier operations. Only operations that include a result_id publish entries in the results array; executed_count counts all executed operations. A mutating batch requires expected_version and runs in one Word Undo record with rollback on failure. Indexed setters remain non-executable until their exact Word Undo behavior is proven.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def execute_live_word_member_operations(
        live_document_id: str,
        operations: list[dict],
        activate: bool = True,
        expected_version: int | None = None,
        optimize_screen_updates: bool = True,
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.execute_member_operations,
            subject,
            live_document_id,
            operations=operations,
            activate=activate,
            expected_version=expected_version,
            optimize_screen_updates=optimize_screen_updates,
        )
        return ok(result)

    @mcp.tool(
        title="Read Word Live selection",
        description="Read the current cursor or selection range and a selection_token for a subsequent cursor or selection mutation. Selected text is bounded to 10,000 characters.",
        annotations=LIVE_READ,
    )
    @_safe
    async def get_live_word_selection(live_document_id: str) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(runtime.live_word.selection, subject, live_document_id)
        return ok(result)

    @mcp.tool(
        title="Find text in the connected Microsoft Word document",
        description="Run bounded native Word Find against the connected document and return exact ranges plus short context. Supports Word wildcards without returning or rebuilding the full document.",
        annotations=LIVE_READ,
    )
    @_safe
    async def find_live_word_text(
        live_document_id: str,
        search_text: str = Field(max_length=255),
        match_case: bool = False,
        whole_word: bool = False,
        use_wildcards: bool = False,
        context_chars: int = Field(default=80, ge=0, le=2_000),
        max_results: int = Field(default=100, ge=1, le=5_000),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.find_text,
            subject,
            live_document_id,
            search_text=search_text,
            match_case=match_case,
            whole_word=whole_word,
            use_wildcards=use_wildcards,
            context_chars=context_chars,
            max_results=max_results,
        )
        return ok(result)

    @mcp.tool(
        title="Replace text transactionally in the connected Microsoft Word document",
        description="Find and replace up to 5,000 native Word matches through one COM attachment and one custom Undo record. The complete match set is bounded before mutation, optimistic live_version is required, Track Changes state is restored, and partial failure rolls back.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def replace_live_word_text(
        live_document_id: str,
        search_text: str = Field(max_length=255),
        replacement_text: str = Field(default="", max_length=200_000),
        match_case: bool = False,
        whole_word: bool = False,
        use_wildcards: bool = False,
        replace_all: bool = True,
        track_changes: Literal["preserve", "enable", "disable"] = "preserve",
        max_replacements: int = Field(default=1_000, ge=1, le=5_000),
        optimize_screen_updates: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.replace_text,
            subject,
            live_document_id,
            search_text=search_text,
            replacement_text=replacement_text,
            match_case=match_case,
            whole_word=whole_word,
            use_wildcards=use_wildcards,
            replace_all=replace_all,
            track_changes=track_changes,
            max_replacements=max_replacements,
            optimize_screen_updates=optimize_screen_updates,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Inspect comments or tracked revisions in Microsoft Word",
        description="Read one bounded page of live comments or revisions and issue a content-bound review_token for every item. Later mutations require the matching token and reject stale raw indexes.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_review(
        live_document_id: str,
        kind: Literal["comments", "revisions"],
        offset: int = Field(default=0, ge=0, le=1_000_000),
        limit: int = Field(default=50, ge=1, le=200),
        include_text: bool = True,
        max_text_chars: int = Field(default=500, ge=1, le=2_000),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.inspect_review,
            subject,
            live_document_id,
            kind=kind,
            offset=offset,
            limit=limit,
            include_text=include_text,
            max_text_chars=max_text_chars,
        )
        return ok(result)

    @mcp.tool(
        title="Manage comments and tracked revisions in Microsoft Word",
        description="Add a comment to a fresh selection, reply to, resolve, or delete a token-verified comment, accept or reject one token-verified revision, or set Track Changes. Undoable actions use one custom record with rollback; non-undoable Word properties use verified manual rollback.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def manage_live_word_review(
        live_document_id: str,
        action: Literal[
            "add_comment",
            "reply_comment",
            "resolve_comment",
            "delete_comment",
            "accept_revision",
            "reject_revision",
            "set_track_changes",
        ],
        item_index: int = Field(default=0, ge=0, le=1_000_000),
        review_token: str = Field(default="", max_length=128),
        selection_token: str = Field(default="", max_length=128),
        text: str = Field(default="", max_length=20_000),
        resolved: bool = True,
        tracking_enabled: bool | None = None,
        optimize_screen_updates: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.manage_review,
            subject,
            live_document_id,
            action=action,
            item_index=item_index,
            review_token=review_token,
            selection_token=selection_token,
            text=text,
            resolved=resolved,
            tracking_enabled=tracking_enabled,
            optimize_screen_updates=optimize_screen_updates,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Diagnose live Microsoft Word layout",
        description="Run a bounded single-pass scan for keep-with-next chains, long headings, body page breaks, oversized keep-together paragraphs, disabled widow control, empty-paragraph runs, manual page breaks and heading-style overuse. Returns ranges and metadata, never document text.",
        annotations=LIVE_READ,
    )
    @_safe
    async def diagnose_live_word_layout(
        live_document_id: str,
        max_paragraphs: int = Field(default=10_000, ge=1, le=25_000),
        max_issues: int = Field(default=500, ge=1, le=2_000),
        keep_with_next_threshold: int = Field(default=5, ge=2, le=100),
        long_heading_chars: int = Field(default=100, ge=20, le=2_000),
        long_keep_together_chars: int = Field(default=1_200, ge=100, le=20_000),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.diagnose_layout,
            subject,
            live_document_id,
            max_paragraphs=max_paragraphs,
            max_issues=max_issues,
            keep_with_next_threshold=keep_with_next_threshold,
            long_heading_chars=long_heading_chars,
            long_keep_together_chars=long_keep_together_chars,
        )
        return ok(result)

    @mcp.tool(
        title="Inspect guarded Microsoft Word Undo",
        description="Read a bounded Word Undo list and issue an undo_token only when the current top entry begins with WordToolkit:. Undocumented Word history access is treated as optional and failure closes the gate.",
        annotations=LIVE_READ,
    )
    @_safe
    async def inspect_live_word_undo(
        live_document_id: str,
        max_entries: int = Field(default=20, ge=1, le=50),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.inspect_undo,
            subject,
            live_document_id,
            max_entries=max_entries,
        )
        return ok(result)

    @mcp.tool(
        title="Undo exactly one verified WordToolkit operation",
        description="Undo only the current top Word entry, only when it is labeled WordToolkit: and still matches a fresh undo_token plus expected live_version. It never accepts a raw count and never crosses a manual user edit.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def undo_live_word_operation(
        live_document_id: str,
        undo_token: str = Field(max_length=128),
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.undo_operation,
            subject,
            live_document_id,
            undo_token=undo_token,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Insert text directly in open Microsoft Word",
        description="Insert text into the real open Word document at the verified selection, cursor, or document end. It can create a styled paragraph and the change appears immediately in Word.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def insert_live_word_text(
        live_document_id: str,
        text: str = Field(max_length=200_000),
        target: Literal["selection", "cursor", "document_end"] = "cursor",
        as_new_paragraph: bool = False,
        style: str = Field(default="", max_length=128),
        formatting: LiveTextFormatting | None = None,
        selection_token: str = Field(default="", max_length=128),
        replace_selection: bool = False,
        activate: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.insert_text,
            subject,
            live_document_id,
            text=text,
            target=target,
            as_new_paragraph=as_new_paragraph,
            style=style,
            formatting=formatting.model_dump(exclude_none=True) if formatting else None,
            selection_token=selection_token,
            replace_selection=replace_selection,
            activate=activate,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Format the current selection directly in Microsoft Word",
        description="Apply a Word style plus the complete bounded scalar Word.Font and paragraph formatting surface to the exact non-empty live selection verified by a fresh selection token. Every requested field is read back from COM; mixed or ignored values fail inside one rollback-aware Undo record.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def format_live_word_selection(
        live_document_id: str,
        selection_token: str = Field(max_length=128),
        style: str = Field(default="", max_length=128),
        formatting: LiveTextFormatting | None = None,
        optimize_screen_updates: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.format_selection,
            subject,
            live_document_id,
            selection_token=selection_token,
            style=style,
            formatting=formatting.model_dump(exclude_none=True) if formatting else None,
            optimize_screen_updates=optimize_screen_updates,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Insert a native table directly in Microsoft Word",
        description="Create a rectangular native Word table from up to 5,000 cells in one COM attachment. Text is inserted once as a bounded tab/paragraph payload and converted with Range.ConvertToTable, avoiding per-cell COM writes.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def insert_live_word_table(
        live_document_id: str,
        rows: list[list[str]],
        target: Literal["selection", "cursor", "document_end"] = "document_end",
        selection_token: str = Field(default="", max_length=128),
        replace_selection: bool = False,
        style: str = Field(default="", max_length=128),
        header_row: bool = True,
        autofit: Literal["fixed", "content", "window"] = "window",
        alignment: Literal["left", "center", "right"] = "left",
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.insert_table,
            subject,
            live_document_id,
            rows=rows,
            target=target,
            selection_token=selection_token,
            replace_selection=replace_selection,
            style=style,
            header_row=header_row,
            autofit=autofit,
            alignment=alignment,
            activate=activate,
            optimize_screen_updates=optimize_screen_updates,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Preflight native Word table formulas",
        description="Validate up to 200 typed table-cell calculations without attaching to Word. Supports SUM, AVERAGE, COUNT, MAX, MIN and PRODUCT over positional directions or bounded A1-style ranges generated from row and column coordinates; raw formulas and field codes are never accepted.",
        annotations=LIVE_READ,
    )
    @_safe
    async def preflight_live_word_table_formulas(formulas: list[dict]) -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.preflight_table_formulas,
            formulas,
        )
        return ok(result)

    @mcp.tool(
        title="Insert native formulas into an existing Word table",
        description="Insert and calculate up to 200 typed native formula fields in cells of one existing rectangular Word table through one COM attachment and one Undo transaction. Destination cells must be empty unless replacement is explicitly enabled; native field types, result ranges and final counts are verified. Formula fields calculate on insertion; force_update requests an additional explicit recalculation.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def insert_live_word_table_formulas(
        live_document_id: str,
        table_index: int = Field(ge=1, le=10_000),
        formulas: list[dict] = Field(min_length=1, max_length=200),
        activate: bool = True,
        optimize_screen_updates: bool = True,
        force_update: bool = False,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.insert_table_formulas,
            subject,
            live_document_id,
            table_index=table_index,
            formulas=formulas,
            activate=activate,
            optimize_screen_updates=optimize_screen_updates,
            force_update=force_update,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Update all native fields in one existing Word table",
        description="Recalculate up to 5,000 existing native fields—including table formulas—in one Word Fields.Update call, one COM attachment and one Undo transaction. Field types and counts are verified before and after; field codes, results and document content are never returned.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def update_live_word_table_fields(
        live_document_id: str,
        table_index: int = Field(ge=1, le=10_000),
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.update_table_fields,
            subject,
            live_document_id,
            table_index=table_index,
            activate=activate,
            optimize_screen_updates=optimize_screen_updates,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Insert a native list directly in Microsoft Word",
        description="Create up to 1,000 native bulleted or numbered Word paragraphs in one COM attachment. The complete text is inserted once, then one bounded Range.ListFormat operation applies native numbering without per-item COM writes.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def insert_live_word_list(
        live_document_id: str,
        items: list[str],
        list_kind: Literal["bullet", "numbered"] = "bullet",
        target: Literal["selection", "cursor", "document_end"] = "document_end",
        selection_token: str = Field(default="", max_length=128),
        replace_selection: bool = False,
        style: str = Field(default="", max_length=128),
        formatting: LiveTextFormatting | None = None,
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.insert_list,
            subject,
            live_document_id,
            items=items,
            list_kind=list_kind,
            target=target,
            selection_token=selection_token,
            replace_selection=replace_selection,
            style=style,
            formatting=formatting.model_dump(exclude_none=True) if formatting else None,
            activate=activate,
            optimize_screen_updates=optimize_screen_updates,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Preflight native Word bookmarks",
        description="Validate up to 200 named Word bookmark ranges without attaching to Word. Names are bounded and case-insensitively unique; document text is not returned.",
        annotations=LIVE_READ,
    )
    @_safe
    async def preflight_live_word_bookmarks(bookmarks: list[dict]) -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(
            runtime.live_word.preflight_bookmarks,
            bookmarks,
        )
        return ok(result)

    @mcp.tool(
        title="Insert native bookmarks directly in Microsoft Word",
        description="Insert up to 200 named native Word bookmarks through one COM attachment, one text payload and one Undo transaction. Existing-name collisions and any native range mismatch fail before the live version advances.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def insert_live_word_bookmarks(
        live_document_id: str,
        bookmarks: list[dict],
        target: Literal["selection", "cursor", "document_end"] = "document_end",
        selection_token: str = Field(default="", max_length=128),
        replace_selection: bool = False,
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.insert_bookmarks,
            subject,
            live_document_id,
            bookmarks=bookmarks,
            target=target,
            selection_token=selection_token,
            replace_selection=replace_selection,
            activate=activate,
            optimize_screen_updates=optimize_screen_updates,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Preflight safe native Word fields",
        description="Validate up to 200 typed Word fields without attaching to Word. Raw field codes are never accepted; formula fields use a restricted arithmetic grammar and external-data field classes are impossible to request.",
        annotations=LIVE_READ,
    )
    @_safe
    async def preflight_live_word_fields(fields: list[dict]) -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(runtime.live_word.preflight_fields, fields)
        return ok(result)

    @mcp.tool(
        title="Insert safe native fields directly in Microsoft Word",
        description="Insert up to 200 typed native Word fields through one COM attachment, one surrounding-text payload and one Undo transaction. Supports page/document metadata, date/time, sequence, internal reference and restricted formula fields; raw or external-data field codes are rejected.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def insert_live_word_fields(
        live_document_id: str,
        fields: list[dict],
        target: Literal["selection", "cursor", "document_end"] = "document_end",
        selection_token: str = Field(default="", max_length=128),
        replace_selection: bool = False,
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.insert_fields,
            subject,
            live_document_id,
            fields=fields,
            target=target,
            selection_token=selection_token,
            replace_selection=replace_selection,
            activate=activate,
            optimize_screen_updates=optimize_screen_updates,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Insert native equation directly in open Microsoft Word",
        description="Convert LaTeX, UnicodeMath, MathML, OMML or AST input to Word linear math, create and verify one native OMath in the real open document, build it up, and show the change immediately.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def insert_live_word_equation(
        live_document_id: str,
        value: str | dict,
        input_format: Literal["latex", "unicodemath", "mathml", "omml", "ast"] = "latex",
        display: bool = True,
        target: Literal["selection", "cursor", "document_end"] = "cursor",
        selection_token: str = Field(default="", max_length=128),
        replace_selection: bool = False,
        activate: bool = True,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.insert_equation,
            subject,
            live_document_id,
            value=value,
            input_format=input_format,
            display=display,
            target=target,
            selection_token=selection_token,
            replace_selection=replace_selection,
            activate=activate,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Insert a batch of native equations directly in open Microsoft Word",
        description="Insert up to 100 native Word equations in one COM attachment. Use this for fast worksheets or step-by-step integral solutions. The batch verifies native OMath counts by default; set verify_readback=true when per-equation OMML AST readback is needed.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def insert_live_word_equations_batch(
        live_document_id: str,
        equations: list[dict],
        activate: bool = True,
        expected_version: LIVE_VERSION = Field(),
        verify_readback: bool = False,
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.insert_equations_batch,
            subject,
            live_document_id,
            equations=equations,
            activate=activate,
            expected_version=expected_version,
            verify_readback=verify_readback,
        )
        return ok(result)

    @mcp.tool(
        title="Preflight equations for Microsoft Word",
        description="Check up to 200 equation inputs before any Word mutation. Returns canonical AST, Word linear math, syntax-rule hits, advanced-symbol risks and whether native live readback is required.",
        annotations=LIVE_READ,
    )
    @_safe
    async def preflight_live_word_equations(
        equations: list[dict],
    ) -> dict:
        require_scope("documents:read")
        result = await asyncio.to_thread(runtime.live_word.preflight_equations, equations)
        return ok(result)

    @mcp.tool(
        title="Apply a fast mixed batch directly in open Microsoft Word",
        description="Append up to 200 interleaved text and native equation operations in one COM attachment and one Word Undo transaction. Text accepts either one text value or 1-1000 inline runs with schema-enumerated character formatting; paragraph formatting belongs only on the enclosing text operation. The complete batch is preflighted before mutation, inserted as one payload, and rolled back if any style, OMath build-up, count or symbol-preservation check fails.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def apply_live_word_operations(
        live_document_id: str,
        operations: list[LiveWordOperation] = Field(min_length=1, max_length=200),
        activate: bool = True,
        expected_version: LIVE_VERSION = Field(),
        verify_readback: bool = False,
        optimize_screen_updates: bool = True,
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.apply_operations,
            subject,
            live_document_id,
            operations=[operation.model_dump(exclude_none=True) for operation in operations],
            activate=activate,
            expected_version=expected_version,
            verify_readback=verify_readback,
            optimize_screen_updates=optimize_screen_updates,
        )
        return ok(result)

    @mcp.tool(
        title="Validate open Word document snapshot",
        description="Copy the already-saved live DOCX to an internal temporary snapshot, validate its OOXML and native equations, then discard the snapshot. Unsaved live changes are rejected so validation never saves implicitly.",
        annotations=LIVE_READ,
    )
    @_safe
    async def validate_live_word_document(live_document_id: str) -> dict:
        subject = current_subject()
        require_scope("documents:read")
        result = await asyncio.to_thread(runtime.live_word.validate, subject, live_document_id)
        return ok(result)

    @mcp.tool(
        title="Save the same open Microsoft Word document",
        description="Call Document.Save on the connected Word Live document, writing changes to its existing path without exporting a new file. Unsaved, protected, or read-only documents are rejected.",
        annotations=LIVE_WRITE,
    )
    @_safe
    async def save_live_word_document(
        live_document_id: str,
        expected_version: LIVE_VERSION = Field(),
    ) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(
            runtime.live_word.save,
            subject,
            live_document_id,
            expected_version=expected_version,
        )
        return ok(result)

    @mcp.tool(
        title="Disconnect Word Live document",
        description="Release the WordToolkit live handle without closing Microsoft Word or the document.",
        annotations=LIVE_HANDLE,
    )
    @_safe
    async def disconnect_live_word_document(live_document_id: str) -> dict:
        subject = current_subject()
        require_scope("documents:write")
        result = await asyncio.to_thread(runtime.live_word.disconnect, subject, live_document_id)
        return ok(result)
