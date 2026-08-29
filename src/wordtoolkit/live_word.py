from __future__ import annotations

import gc
import hashlib
import hmac
import os
import re
import shutil
import tempfile
import threading
import time
from collections.abc import Callable, Iterator
from contextlib import contextmanager, suppress
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Literal, Protocol, cast

from lxml import etree

from .config import Settings
from .engine import OoxmlValidator
from .errors import ErrorCode, WordToolkitError
from .ids import opaque_id
from .live_learning import EquationLearningStore, StructureLearningStore
from .live_member_capabilities import (
    VALID_CAPABILITY_EFFECTS,
    VALID_CAPABILITY_EXECUTIONS,
    PreparedMemberOperation,
    build_member_capability_registry,
    member_preflight_payload,
    prepare_member_operations,
)
from .live_object_model import (
    VALID_MEMBER_KINDS,
    VALID_TYPE_KINDS,
    WordObjectModelStore,
    scan_word_object_model,
)
from .math import MathEngine
from .math.omml import M

LiveTarget = Literal["selection", "cursor", "document_end"]
EquationInputFormat = Literal["latex", "unicodemath", "mathml", "omml", "ast"]
TableAutoFit = Literal["fixed", "content", "window"]
TableFormulaFunction = Literal["sum", "average", "count", "max", "min", "product"]
TableFormulaDirection = Literal["above", "below", "left", "right"]
LiveListKind = Literal["bullet", "numbered"]
LiveReviewKind = Literal["comments", "revisions"]
LiveReviewAction = Literal[
    "add_comment",
    "reply_comment",
    "resolve_comment",
    "delete_comment",
    "accept_revision",
    "reject_revision",
    "set_track_changes",
]
LiveTrackChangesMode = Literal["preserve", "enable", "disable"]
LiveFieldKind = Literal[
    "page",
    "num_pages",
    "section",
    "section_pages",
    "date",
    "time",
    "create_date",
    "save_date",
    "print_date",
    "file_name",
    "author",
    "title",
    "subject",
    "word_count",
    "character_count",
    "sequence",
    "reference",
    "formula",
]
AMBIGUOUS_FRACTION_COEFFICIENT = re.compile(r"(?<![\w])\d+\s*/\s*\d+\s+(?=[A-Za-z(\u222b\u221a])")
WORD_STORY_TYPES = {
    1: "main_text",
    2: "footnotes",
    3: "endnotes",
    4: "comments",
    5: "text_frames",
    6: "even_page_headers",
    7: "primary_headers",
    8: "even_page_footers",
    9: "primary_footers",
    10: "first_page_headers",
    11: "first_page_footers",
    12: "footnote_separator",
    13: "footnote_continuation_separator",
    14: "footnote_continuation_notice",
    15: "endnote_separator",
    16: "endnote_continuation_separator",
    17: "endnote_continuation_notice",
}
WORD_STRUCTURE_COLLECTIONS = {
    "paragraphs": "Paragraphs",
    "sections": "Sections",
    "styles": "Styles",
    "tables": "Tables",
    "equations": "OMaths",
    "fields": "Fields",
    "form_fields": "FormFields",
    "bookmarks": "Bookmarks",
    "hyperlinks": "Hyperlinks",
    "comments": "Comments",
    "revisions": "Revisions",
    "content_controls": "ContentControls",
    "inline_shapes": "InlineShapes",
    "floating_shapes": "Shapes",
    "footnotes": "Footnotes",
    "endnotes": "Endnotes",
    "lists": "Lists",
    "list_paragraphs": "ListParagraphs",
    "subdocuments": "Subdocuments",
    "variables": "Variables",
    "tables_of_contents": "TablesOfContents",
    "tables_of_figures": "TablesOfFigures",
    "tables_of_authorities": "TablesOfAuthorities",
}
WORD_STRUCTURE_ITEM_SPECS: dict[str, dict[str, Any]] = {
    "paragraphs": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("style", "Style", "string"),
            ("outline_level", "OutlineLevel", "int"),
        ),
    },
    "sections": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("index", "Index", "int"),
            ("start_type", "Start", "int"),
        ),
    },
    "styles": {
        "properties": (
            ("name_local", "NameLocal", "string"),
            ("type", "Type", "int"),
            ("built_in", "BuiltIn", "bool"),
            ("in_use", "InUse", "bool"),
            ("automatically_update", "AutomaticallyUpdate", "bool"),
        ),
    },
    "tables": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("row_count", "Rows.Count", "int"),
            ("column_count", "Columns.Count", "int"),
            ("nesting_level", "NestingLevel", "int"),
            ("style", "Style", "string"),
            ("allow_autofit", "AllowAutoFit", "bool"),
        ),
    },
    "equations": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("type", "Type", "int"),
        ),
    },
    "fields": {
        "text_path": "Result",
        "properties": (
            ("result_range", "Result", "range"),
            ("type", "Type", "int"),
            ("locked", "Locked", "bool"),
        ),
    },
    "form_fields": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("type", "Type", "int"),
            ("name", "Name", "string"),
            ("enabled", "Enabled", "bool"),
        ),
    },
    "bookmarks": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("name", "Name", "string"),
        ),
    },
    "hyperlinks": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("type", "Type", "int"),
            ("name", "Name", "string"),
            ("has_external_address", "Address", "presence"),
            ("has_internal_target", "SubAddress", "presence"),
        ),
    },
    "comments": {
        "text_path": "Range",
        "properties": (
            ("scope_range", "Scope", "range"),
            ("comment_range", "Range", "range"),
            ("author", "Author", "string"),
            ("initials", "Initial", "string"),
            ("date", "Date", "string"),
        ),
    },
    "revisions": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("type", "Type", "int"),
            ("author", "Author", "string"),
            ("date", "Date", "string"),
        ),
    },
    "content_controls": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("id", "ID", "int"),
            ("type", "Type", "int"),
            ("title", "Title", "string"),
            ("tag", "Tag", "string"),
            ("lock_contents", "LockContents", "bool"),
            ("lock_control", "LockContentControl", "bool"),
            ("showing_placeholder_text", "ShowingPlaceholderText", "bool"),
        ),
    },
    "inline_shapes": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("type", "Type", "int"),
            ("width_points", "Width", "float"),
            ("height_points", "Height", "float"),
            ("alternative_text", "AlternativeText", "string"),
            ("title", "Title", "string"),
            ("has_chart", "HasChart", "bool"),
            ("has_smart_art", "HasSmartArt", "bool"),
        ),
    },
    "floating_shapes": {
        "text_path": "Anchor",
        "properties": (
            ("anchor_range", "Anchor", "range"),
            ("type", "Type", "int"),
            ("name", "Name", "string"),
            ("width_points", "Width", "float"),
            ("height_points", "Height", "float"),
            ("alternative_text", "AlternativeText", "string"),
            ("title", "Title", "string"),
            ("has_chart", "HasChart", "bool"),
            ("has_smart_art", "HasSmartArt", "bool"),
            ("lock_anchor", "LockAnchor", "bool"),
            ("visible", "Visible", "bool"),
        ),
    },
    "footnotes": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("reference_range", "Reference", "range"),
            ("index", "Index", "int"),
        ),
    },
    "endnotes": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("reference_range", "Reference", "range"),
            ("index", "Index", "int"),
        ),
    },
    "lists": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("list_type", "Range.ListFormat.ListType", "int"),
        ),
    },
    "list_paragraphs": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("list_type", "Range.ListFormat.ListType", "int"),
            ("style", "Style", "string"),
        ),
    },
    "subdocuments": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("locked", "Locked", "bool"),
        ),
    },
    "variables": {
        "properties": (
            ("name", "Name", "string"),
            ("value", "Value", "string"),
        ),
    },
    "tables_of_contents": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("use_heading_styles", "UseHeadingStyles", "bool"),
            ("upper_heading_level", "UpperHeadingLevel", "int"),
            ("lower_heading_level", "LowerHeadingLevel", "int"),
        ),
    },
    "tables_of_figures": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("caption_label", "Caption", "string"),
        ),
    },
    "tables_of_authorities": {
        "text_path": "Range",
        "properties": (
            ("range", "Range", "range"),
            ("category", "Category", "int"),
        ),
    },
}
WORD_TYPED_COLLECTIONS = {
    "equation_types": ("OMaths", "Type"),
    "field_types": ("Fields", "Type"),
    "form_field_types": ("FormFields", "Type"),
    "content_control_types": ("ContentControls", "Type"),
    "revision_types": ("Revisions", "Type"),
    "inline_shape_types": ("InlineShapes", "Type"),
    "floating_shape_types": ("Shapes", "Type"),
    "style_types": ("Styles", "Type"),
    "list_types": ("Lists", "Range.ListFormat.ListType"),
}
WORD_TYPED_STRUCTURE_NAMES = {
    "equation_types": "equations",
    "field_types": "fields",
    "form_field_types": "form_fields",
    "content_control_types": "content_controls",
    "revision_types": "revisions",
    "inline_shape_types": "inline_shapes",
    "floating_shape_types": "floating_shapes",
    "style_types": "styles",
    "list_types": "lists",
}
LIVE_EDITABLE_STRUCTURE_NAMES = {
    "paragraphs",
    "styles",
    "tables",
    "equations",
    "fields",
    "bookmarks",
    "comments",
    "revisions",
    "lists",
    "list_paragraphs",
}
LIVE_INSPECTABLE_STRUCTURE_NAMES = set(WORD_STRUCTURE_ITEM_SPECS)
WORD_REVISION_TYPES = {
    1: "insert",
    2: "delete",
    3: "property",
    4: "paragraph_number",
    5: "display_field",
    6: "reconcile",
    7: "conflict",
    8: "style",
    9: "replace",
    10: "section_property",
    11: "table_property",
    12: "cell_insert",
    13: "cell_delete",
    14: "cell_merge",
}
WORD_LIST_TYPES = {
    "bullet": 2,
    "numbered": 3,
}
WORD_SAFE_FIELD_TYPES = {
    "page": 33,
    "num_pages": 26,
    "section": 65,
    "section_pages": 66,
    "date": 31,
    "time": 32,
    "create_date": 21,
    "save_date": 22,
    "print_date": 23,
    "file_name": 29,
    "author": 17,
    "title": 15,
    "subject": 16,
    "word_count": 27,
    "character_count": 28,
    "sequence": 12,
    "reference": 3,
    "formula": 34,
}
WORD_DATE_FIELD_KINDS = {
    "date",
    "time",
    "create_date",
    "save_date",
    "print_date",
}
WORD_FORMULA_WORDS = {
    "ABS",
    "AND",
    "AVERAGE",
    "COUNT",
    "FALSE",
    "IF",
    "INT",
    "MAX",
    "MIN",
    "MOD",
    "NOT",
    "OR",
    "PRODUCT",
    "ROUND",
    "SIGN",
    "SUM",
    "TRUE",
}
WORD_INTERNATIONAL_LIST_SEPARATOR = 17
WORD_INTERNATIONAL_DECIMAL_SEPARATOR = 18
WORD_INTERNATIONAL_THOUSANDS_SEPARATOR = 19
WORD_FIELD_MARKER = "\ue000"


def _word_utf16_length(value: str) -> int:
    """Return the number of UTF-16 code units used by Word Range offsets."""

    return len(value.encode("utf-16-le", errors="surrogatepass")) // 2


WORD_BOOKMARK_NAME = re.compile(r"[A-Za-z][A-Za-z0-9_]{0,39}")
LIVE_EQUATION_STRUCTURE_KINDS = frozenset(
    {
        "fraction",
        "superscript",
        "subscript",
        "sub_sup",
        "radical",
        "nary",
        "delimiter",
        "matrix",
        "matrix_row",
        "cell",
        "equations",
        "accent",
        "limit_lower",
        "limit_upper",
        "function",
        "enclosure",
    }
)
LIVE_EQUATION_FIDELITY_READBACK_FEATURES = frozenset(
    {
        "fraction",
        "power",
        "subscript",
        "subscript_and_power",
        "radical",
        "nary_operator",
        "integral",
        "matrix",
        "accent",
        "limit",
        "function",
        "structured_delimiter",
        "commutator_or_brackets",
        "hbar",
        "dagger",
        "long_expression",
    }
)
LIVE_EQUATION_IGNORABLE_CHARACTERS = frozenset(
    {
        "\u200b",  # zero-width space
        "\u2061",  # function application
        "\u2062",  # invisible times
        "\u2063",  # invisible separator
        "\u2064",  # invisible plus
        "\ufeff",  # zero-width no-break space
    }
)
PARAGRAPH_ALIGNMENT = {
    "left": 0,
    "center": 1,
    "right": 2,
    "justify": 3,
    "distribute": 4,
}
TABLE_AUTOFIT = {
    "fixed": 0,
    "content": 1,
    "window": 2,
}
TABLE_ALIGNMENT = {
    "left": 0,
    "center": 1,
    "right": 2,
}
WORD_TABLE_FORMULA_FUNCTIONS = {
    "sum": "SUM",
    "average": "AVERAGE",
    "count": "COUNT",
    "max": "MAX",
    "min": "MIN",
    "product": "PRODUCT",
}
WORD_TABLE_FORMULA_DIRECTIONS = {
    "above": "ABOVE",
    "below": "BELOW",
    "left": "LEFT",
    "right": "RIGHT",
}
WORD_AUTOMATIC_COLOR = -16_777_216
WORD_UNDERLINE_STYLES = {
    "none": 0,
    "single": 1,
    "words": 2,
    "double": 3,
    "dotted": 4,
    "thick": 6,
    "dash": 7,
    "dot_dash": 9,
    "dot_dot_dash": 10,
    "wavy": 11,
    "dotted_heavy": 20,
    "dash_heavy": 23,
    "dot_dash_heavy": 25,
    "dot_dot_dash_heavy": 26,
    "wavy_heavy": 27,
    "dash_long": 39,
    "wavy_double": 43,
    "dash_long_heavy": 55,
}
WORD_EMPHASIS_MARKS = {
    "none": 0,
    "over_solid_circle": 1,
    "over_comma": 2,
    "over_white_circle": 3,
    "under_solid_circle": 4,
}
WORD_LIGATURES = {
    "none": 0,
    "standard": 1,
    "contextual": 2,
    "standard_contextual": 3,
    "historical": 4,
    "standard_historical": 5,
    "contextual_historical": 6,
    "standard_contextual_historical": 7,
    "discretionary": 8,
    "standard_discretionary": 9,
    "contextual_discretionary": 10,
    "standard_contextual_discretionary": 11,
    "historical_discretionary": 12,
    "standard_historical_discretionary": 13,
    "contextual_historical_discretionary": 14,
    "all": 15,
}
WORD_NUMBER_FORMS = {"default": 0, "lining": 1, "old_style": 2}
WORD_NUMBER_SPACING = {"default": 0, "proportional": 1, "tabular": 2}
TEXT_FORMATTING_KEYS = {
    "font_name",
    "font_size_pt",
    "font_color_rgb",
    "bold",
    "italic",
    "underline",
    "all_caps",
    "small_caps",
    "strike",
    "double_strike",
    "hidden",
    "highlight_color_index",
    "paragraph_alignment",
    "space_before_pt",
    "space_after_pt",
    "left_indent_pt",
    "right_indent_pt",
    "first_line_indent_pt",
    "keep_with_next",
    "keep_together",
    "page_break_before",
    "widow_control",
}
INLINE_RUN_FORMATTING_KEYS = {
    "font_name",
    "font_size_pt",
    "font_color_rgb",
    "bold",
    "italic",
    "underline",
    "all_caps",
    "small_caps",
    "strike",
    "double_strike",
    "hidden",
    "highlight_color_index",
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
TEXT_FORMATTING_KEYS |= INLINE_RUN_FORMATTING_KEYS
TEXT_FORMATTING_ALIASES = {
    "font_size": "font_size_pt",
    "alignment": "paragraph_alignment",
}


class WordBackend(Protocol):
    @contextmanager
    def attach(self) -> Iterator[Any]: ...


class PyWin32WordBackend:
    """Attach to an existing Word.Application without launching Word."""

    @contextmanager
    def attach(self) -> Iterator[Any]:
        if os.name != "nt":
            raise WordToolkitError(
                ErrorCode.LIVE_WORD_UNAVAILABLE,
                "Word Live is available only on Windows",
            )
        try:
            import pythoncom  # type: ignore[import-untyped]
            import win32com.client  # type: ignore[import-untyped]
        except ImportError as exc:
            raise WordToolkitError(
                ErrorCode.LIVE_WORD_UNAVAILABLE,
                "The Windows COM runtime required by Word Live is unavailable",
            ) from exc

        pythoncom.CoInitializeEx(pythoncom.COINIT_APARTMENTTHREADED)
        application = None
        try:
            try:
                application = win32com.client.GetActiveObject("Word.Application")
            except Exception as exc:
                raise WordToolkitError(
                    ErrorCode.LIVE_WORD_UNAVAILABLE,
                    "Microsoft Word is not running or has no automation-visible instance",
                    {"exception": type(exc).__name__},
                    retryable=True,
                ) from exc
            yield application
        finally:
            application = None
            gc.collect()
            pythoncom.CoUninitialize()


@dataclass(slots=True)
class LiveWordRecord:
    owner: str
    document_id: str
    name: str
    full_name: str
    window_hwnd: int
    version: int = 0
    undo_barrier_version: int = -1


@dataclass(frozen=True, slots=True)
class PreparedLiveEquation:
    linear: str
    display: bool
    input_format: EquationInputFormat
    verify_readback: bool
    required_symbol_groups: tuple[tuple[str, ...], ...]
    rules: tuple[str, ...]
    warnings: tuple[str, ...]
    ast: dict[str, Any]
    features: tuple[str, ...]
    learning: dict[str, Any]


@dataclass(frozen=True, slots=True)
class PreparedLiveField:
    kind: LiveFieldKind
    field_type: int
    field_text: str
    preserve_formatting: bool
    update: bool
    prefix_text: str
    suffix_text: str
    as_new_paragraph: bool
    bookmark: str
    rules: tuple[str, ...]
    formula_expression: str = ""
    numeric_format: str = ""


@dataclass(frozen=True, slots=True)
class PreparedLiveTableFormula:
    row: int
    column: int
    function: TableFormulaFunction
    directions: tuple[TableFormulaDirection, ...]
    range_start: tuple[int, int] | None
    range_end: tuple[int, int] | None
    numeric_format: str
    replace_existing: bool
    expression: str
    rules: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class PreparedLiveBookmark:
    name: str
    text: str
    prefix_text: str
    suffix_text: str
    as_new_paragraph: bool
    style: str
    formatting: dict[str, Any]
    rules: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class PreparedLiveTextOperation:
    text: str
    as_new_paragraph: bool
    style: str
    formatting: dict[str, Any]
    runs: tuple[tuple[str, dict[str, Any]], ...] = ()


@dataclass(frozen=True, slots=True)
class PreparedLiveEquationOperation:
    equation: PreparedLiveEquation


PreparedLiveOperation = PreparedLiveTextOperation | PreparedLiveEquationOperation


class LiveWordBridge:
    """Bounded live-document operations backed by the Word COM object model.

    COM proxies never leave the thread in which they were acquired. A record keeps
    only a stable document identity; each operation attaches to the running Word
    instance again and resolves the document in that apartment.
    """

    def __init__(
        self,
        settings: Settings,
        validator: OoxmlValidator,
        *,
        backend: WordBackend | None = None,
    ):
        self.settings = settings
        self.validator = validator
        self.math = MathEngine()
        self.learning = EquationLearningStore(
            settings.storage_root / "word-live-equation-learning.json"
        )
        self.structure_learning = StructureLearningStore(
            settings.storage_root / "word-live-structure-learning.json"
        )
        self.object_model = WordObjectModelStore(
            settings.storage_root / "word-live-object-model.json"
        )
        self.backend = backend or PyWin32WordBackend()
        self._backend_injected = backend is not None
        self._records: dict[str, LiveWordRecord] = {}
        self._member_registry_key = ""
        self._member_registry: dict[str, Any] | None = None
        self._token_secret = os.urandom(32)
        self._lock = threading.RLock()

    def _require_available(self) -> None:
        if not self.settings.is_local_stdio:
            raise WordToolkitError(
                ErrorCode.AUTH_FORBIDDEN,
                "Word Live is restricted to the local STDIO plugin",
            )
        if os.name != "nt" and not self._backend_injected:
            raise WordToolkitError(
                ErrorCode.LIVE_WORD_UNAVAILABLE,
                "Word Live is available only on Windows",
            )

    def _execute(self, operation: Callable[[Any], Any]) -> Any:
        self._require_available()
        with self._lock:
            try:
                with self.backend.attach() as application:
                    return operation(application)
            except WordToolkitError:
                raise
            except Exception as exc:
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED,
                    "Microsoft Word rejected the live document operation",
                    {"exception": type(exc).__name__},
                    retryable=True,
                ) from exc

    @staticmethod
    def _collection_items(collection: Any) -> list[Any]:
        return [collection.Item(index) for index in range(1, int(collection.Count) + 1)]

    @staticmethod
    def _string_property(value: Any, name: str) -> str:
        try:
            return str(getattr(value, name) or "")
        except Exception:
            return ""

    @classmethod
    def _document_identity(cls, document: Any) -> tuple[str, str]:
        name = cls._string_property(document, "Name")
        full_name = cls._string_property(document, "FullName")
        return name, full_name

    @classmethod
    def _document_info(cls, application: Any, document: Any) -> dict[str, Any]:
        name, full_name = cls._document_identity(document)
        path = cls._string_property(document, "Path")
        active = False
        with suppress(Exception):
            active = document is application.ActiveDocument or (
                cls._string_property(application.ActiveDocument, "FullName").casefold()
                == full_name.casefold()
                and bool(full_name)
            )
        window_hwnd = 0
        with suppress(Exception):
            window_hwnd = int(application.ActiveWindow.Hwnd) if active else 0
        return {
            "name": name,
            "full_name": full_name,
            "path": path,
            "saved_to_disk": bool(path),
            "active": active,
            "window_hwnd": window_hwnd,
            "saved": bool(document.Saved),
            "read_only": bool(document.ReadOnly),
            "compatibility_mode": int(document.CompatibilityMode),
            "paragraph_count": int(document.Paragraphs.Count),
            "equation_count": int(document.OMaths.Count),
            "table_count": int(document.Tables.Count),
            "field_count": int(document.Fields.Count),
            "bookmark_count": int(document.Bookmarks.Count),
        }

    @staticmethod
    def _normalize_path(value: str) -> str:
        if not value:
            return ""
        try:
            return os.path.normcase(os.path.abspath(value))
        except OSError:
            return os.path.normcase(value)

    def list_documents(self) -> dict[str, Any]:
        def operation(application: Any) -> dict[str, Any]:
            documents = [
                self._document_info(application, document)
                for document in self._collection_items(application.Documents)
            ]
            return {
                "word_running": True,
                "visible": bool(application.Visible),
                "document_count": len(documents),
                "documents": documents,
            }

        try:
            return self._execute(operation)
        except WordToolkitError as exc:
            if exc.code is ErrorCode.LIVE_WORD_UNAVAILABLE:
                return {
                    "word_running": False,
                    "visible": False,
                    "document_count": 0,
                    "documents": [],
                    "notice": exc.message,
                }
            raise

    def connect(
        self,
        owner: str,
        *,
        document_name: str = "",
        full_path: str = "",
        use_active: bool = True,
        activate: bool = True,
    ) -> dict[str, Any]:
        if document_name and full_path:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Provide document_name or full_path, not both",
            )
        if (document_name or full_path) and use_active:
            use_active = False

        def operation(application: Any) -> dict[str, Any]:
            documents = self._collection_items(application.Documents)
            if not documents:
                raise WordToolkitError(
                    ErrorCode.DOCUMENT_NOT_FOUND,
                    "Microsoft Word has no open documents",
                )
            if full_path:
                wanted = self._normalize_path(full_path)
                matches = [
                    document
                    for document in documents
                    if self._normalize_path(self._document_identity(document)[1]) == wanted
                ]
            elif document_name:
                matches = [
                    document
                    for document in documents
                    if self._document_identity(document)[0].casefold() == document_name.casefold()
                ]
            elif use_active:
                matches = [application.ActiveDocument]
            else:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Choose the active document or provide an exact document selector",
                )
            if len(matches) != 1:
                raise WordToolkitError(
                    ErrorCode.DOCUMENT_NOT_FOUND,
                    "The live document selector did not resolve to exactly one document",
                    {"matches": len(matches)},
                )
            document = matches[0]
            if activate:
                document.Activate()
            name, resolved_full_name = self._document_identity(document)
            identity = self._normalize_path(resolved_full_name) or name.casefold()
            record = next(
                (
                    item
                    for item in self._records.values()
                    if item.owner == owner
                    and (self._normalize_path(item.full_name) or item.name.casefold()) == identity
                ),
                None,
            )
            if record is None:
                window_hwnd = 0
                with suppress(Exception):
                    window_hwnd = int(application.ActiveWindow.Hwnd)
                record = LiveWordRecord(
                    owner=owner,
                    document_id=opaque_id("live"),
                    name=name,
                    full_name=resolved_full_name,
                    window_hwnd=window_hwnd,
                )
                self._records[record.document_id] = record
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    def _record(self, owner: str, document_id: str) -> LiveWordRecord:
        record = self._records.get(document_id)
        if record is None or record.owner != owner:
            raise WordToolkitError(
                ErrorCode.DOCUMENT_NOT_FOUND,
                "The Word Live document handle was not found",
            )
        return record

    def _resolve_document(self, application: Any, record: LiveWordRecord) -> Any:
        documents = self._collection_items(application.Documents)
        wanted_path = self._normalize_path(record.full_name)
        if wanted_path:
            path_matches = [
                document
                for document in documents
                if self._normalize_path(self._document_identity(document)[1]) == wanted_path
            ]
            if len(path_matches) == 1:
                return path_matches[0]
        name_matches = [
            document
            for document in documents
            if self._document_identity(document)[0].casefold() == record.name.casefold()
        ]
        if len(name_matches) == 1:
            document = name_matches[0]
            record.name, record.full_name = self._document_identity(document)
            return document
        raise WordToolkitError(
            ErrorCode.DOCUMENT_NOT_FOUND,
            "The connected Word document is no longer open",
        )

    @staticmethod
    def _check_version(record: LiveWordRecord, expected_version: int | None) -> None:
        if expected_version is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "expected_version is required for every Word Live write",
                {"field": "expected_version"},
            )
        if (
            isinstance(expected_version, bool)
            or not isinstance(expected_version, int)
            or expected_version < 0
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "expected_version must be a non-negative integer",
                {"field": "expected_version"},
            )
        if record.version != expected_version:
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "The Word Live handle changed before the mutation",
                {"expected": expected_version, "actual": record.version},
                retryable=True,
            )

    def inspect(self, owner: str, document_id: str) -> dict[str, Any]:
        record = self._record(owner, document_id)

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    @staticmethod
    def _safe_collection_count(value: Any, property_name: str) -> int | None:
        try:
            return int(getattr(value, property_name).Count)
        except Exception:
            return None

    @staticmethod
    def _collection_type_histogram(
        value: Any,
        collection_name: str,
        type_property: str,
        *,
        max_items: int = 10_000,
    ) -> dict[str, Any]:
        try:
            collection = getattr(value, collection_name)
            total = max(0, int(collection.Count))
        except Exception:
            return {
                "available": False,
                "total": None,
                "scanned": 0,
                "truncated": False,
                "read_errors": 0,
                "types": {},
            }
        scanned = min(total, max_items)
        histogram: dict[str, int] = {}
        read_errors = 0
        for index in range(1, scanned + 1):
            try:
                item = collection.Item(index)
                type_value = item
                for attribute in type_property.split("."):
                    type_value = getattr(type_value, attribute)
                key = str(int(type_value))
                histogram[key] = histogram.get(key, 0) + 1
            except Exception:
                read_errors += 1
        return {
            "available": True,
            "total": total,
            "scanned": scanned,
            "truncated": total > scanned,
            "read_errors": read_errors,
            "types": dict(
                sorted(
                    histogram.items(),
                    key=lambda item: int(item[0]),
                )
            ),
        }

    @classmethod
    def _story_inventory(cls, document: Any) -> list[dict[str, Any]]:
        stories: list[dict[str, Any]] = []
        for story_type, name in WORD_STORY_TYPES.items():
            current = None
            with suppress(Exception):
                current = document.StoryRanges.Item(story_type)
            if current is None:
                continue
            instances = 0
            characters = 0
            paragraphs = 0
            tables = 0
            fields = 0
            equations = 0
            while current is not None and instances < 256:
                instances += 1
                with suppress(Exception):
                    characters += max(0, int(current.End) - int(current.Start))
                paragraphs += cls._safe_collection_count(current, "Paragraphs") or 0
                tables += cls._safe_collection_count(current, "Tables") or 0
                fields += cls._safe_collection_count(current, "Fields") or 0
                equations += cls._safe_collection_count(current, "OMaths") or 0
                try:
                    current = current.NextStoryRange
                except Exception:
                    current = None
            stories.append(
                {
                    "story_type": story_type,
                    "name": name,
                    "instances": instances,
                    "character_count": characters,
                    "paragraph_count": paragraphs,
                    "table_count": tables,
                    "field_count": fields,
                    "equation_count": equations,
                    "truncated": instances == 256 and current is not None,
                }
            )
        return stories

    def structure_map(
        self,
        owner: str,
        document_id: str,
        *,
        include_type_histograms: bool = False,
        adaptive_type_histograms: bool = True,
        max_type_items: int = 2_000,
    ) -> dict[str, Any]:
        if not isinstance(include_type_histograms, bool) or not isinstance(
            adaptive_type_histograms, bool
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "type histogram flags must be true or false",
            )
        if not 1 <= max_type_items <= 10_000:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "max_type_items must be between 1 and 10,000",
            )
        record = self._record(owner, document_id)
        recommendation = self.structure_learning.recommendation(tuple(WORD_TYPED_COLLECTIONS))
        started = time.perf_counter()

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            structures = {
                name: self._safe_collection_count(document, property_name)
                for name, property_name in WORD_STRUCTURE_COLLECTIONS.items()
            }
            unsupported_present = [
                name
                for name, count in structures.items()
                if count and name not in LIVE_EDITABLE_STRUCTURE_NAMES
            ]
            stories = self._story_inventory(document)
            collections_to_scan: set[str] = set()
            scan_reasons: dict[str, str] = {}
            if include_type_histograms:
                collections_to_scan.update(WORD_TYPED_COLLECTIONS)
                scan_reasons.update({name: "explicit" for name in WORD_TYPED_COLLECTIONS})
            elif adaptive_type_histograms:
                learned = recommendation["collections"]
                for name in WORD_TYPED_COLLECTIONS:
                    structure_name = WORD_TYPED_STRUCTURE_NAMES[name]
                    if not structures.get(structure_name):
                        continue
                    if bool(learned[name]["scan_due_on_next_presence"]):
                        collections_to_scan.add(name)
                        scan_reasons[name] = "adaptive_due"
            type_histograms = {
                name: self._collection_type_histogram(
                    document,
                    WORD_TYPED_COLLECTIONS[name][0],
                    WORD_TYPED_COLLECTIONS[name][1],
                    max_items=max_type_items,
                )
                for name in WORD_TYPED_COLLECTIONS
                if name in collections_to_scan
            }
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "document": self._document_info(application, document),
                "structures": structures,
                "type_histograms": type_histograms,
                "type_histograms_included": bool(type_histograms),
                "type_histograms_requested": include_type_histograms,
                "adaptive_type_histograms": adaptive_type_histograms,
                "type_histogram_scan_reasons": scan_reasons,
                "max_type_items": max_type_items,
                "stories": stories,
                "live_edit_support": {
                    "supported": [
                        "main-story text insertion",
                        "bounded native find and transactional replace",
                        "paragraph style assignment on inserted text",
                        "token-safe font and paragraph formatting",
                        "native rectangular tables",
                        "native bulleted and numbered lists",
                        "safe native fields and formula fields",
                        "native named bookmarks",
                        "native inline and display OMath",
                        "token-safe comments and tracked-change review",
                        "bounded live layout diagnostics",
                        "WordToolkit-only guarded Undo",
                    ],
                    "present_but_not_live_editable": unsupported_present,
                    "unknown_count_properties": [
                        name for name, count in structures.items() if count is None
                    ],
                },
                "live_item_inspection": {
                    "supported_structures": sorted(LIVE_INSPECTABLE_STRUCTURE_NAMES),
                    "bounded": True,
                    "external_addresses_returned": False,
                    "field_codes_returned": False,
                },
                "content_returned": False,
            }

        result = cast(dict[str, Any], self._execute(operation))
        duration_ms = (time.perf_counter() - started) * 1000
        observations: dict[str, dict[str, Any]] = {}
        for name in WORD_TYPED_COLLECTIONS:
            histogram = result["type_histograms"].get(name)
            observations[name] = {
                "present": bool(result["structures"].get(WORD_TYPED_STRUCTURE_NAMES[name])),
                "scanned": histogram is not None,
                "types": list(histogram["types"]) if histogram is not None else [],
                "read_errors": histogram["read_errors"] if histogram is not None else 0,
                "truncated": histogram["truncated"] if histogram is not None else False,
            }
        observation_recorded = False
        try:
            self.structure_learning.record_map(
                observations,
                duration_ms=duration_ms,
            )
            observation_recorded = True
        except (OSError, TypeError, ValueError):
            observation_recorded = False
        result["structure_learning"] = {
            "observation_recorded": observation_recorded,
            "duration_ms": round(duration_ms, 3),
            "collections_scanned": sorted(result["type_histograms"]),
            "document_content_stored": False,
            "document_counts_stored": False,
            "path_exposed": False,
        }
        return result

    @staticmethod
    def _resolve_property_path(value: Any, path: str) -> Any:
        resolved = value
        for attribute in path.split("."):
            resolved = getattr(resolved, attribute)
        return resolved

    @staticmethod
    def _range_summary(value: Any) -> dict[str, Any]:
        start = int(value.Start)
        end = int(value.End)
        result: dict[str, Any] = {
            "start": start,
            "end": end,
            "character_count": max(0, end - start),
        }
        with suppress(Exception):
            result["story_type"] = int(value.StoryType)
        return result

    @classmethod
    def _structure_property_value(
        cls,
        item: Any,
        path: str,
        value_kind: str,
    ) -> tuple[Any, bool]:
        value = cls._resolve_property_path(item, path)
        if value_kind == "range":
            return cls._range_summary(value), False
        if value_kind == "int":
            return int(value), False
        if value_kind == "float":
            return round(float(value), 3), False
        if value_kind == "bool":
            return bool(value), False
        if value_kind == "presence":
            return bool(str(value or "")), False
        if value_kind == "string":
            text = str(value or "").replace("\x00", "")
            return text[:512], len(text) > 512
        raise ValueError("Unsupported live structure property kind")

    @staticmethod
    def _word_text_preview(value: Any, max_chars: int) -> tuple[str, bool]:
        text = str(value.Text or "")
        text = text.replace("\r", "\n").replace("\x07", "").replace("\x00", "")
        return text[:max_chars], len(text) > max_chars

    def inspect_structure_items(
        self,
        owner: str,
        document_id: str,
        *,
        structure: str,
        offset: int = 0,
        limit: int = 50,
        include_text: bool = False,
        max_text_chars: int = 500,
        adaptive_property_probing: bool = True,
    ) -> dict[str, Any]:
        """Read bounded semantic metadata from one native Word collection."""

        if structure not in WORD_STRUCTURE_ITEM_SPECS:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Unsupported Word Live structure collection",
                {"supported": sorted(WORD_STRUCTURE_ITEM_SPECS)},
            )
        if isinstance(offset, bool) or not isinstance(offset, int) or not 0 <= offset <= 1_000_000:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "offset must be an integer between 0 and 1,000,000",
            )
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 200:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "limit must be an integer between 1 and 200",
            )
        if (
            isinstance(max_text_chars, bool)
            or not isinstance(max_text_chars, int)
            or not 1 <= max_text_chars <= 2_000
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "max_text_chars must be an integer between 1 and 2,000",
            )
        if not isinstance(include_text, bool) or not isinstance(adaptive_property_probing, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "inspection flags must be true or false",
            )

        record = self._record(owner, document_id)
        spec = WORD_STRUCTURE_ITEM_SPECS[structure]
        property_specs = tuple(spec["properties"])
        property_names = tuple(item[0] for item in property_specs)
        recommendation = self.structure_learning.property_recommendation(
            structure,
            property_names,
        )
        if adaptive_property_probing:
            properties_to_probe = {
                name
                for name, evidence in recommendation["properties"].items()
                if bool(evidence["probe_due_on_next_inspection"])
            }
        else:
            properties_to_probe = set(property_names)
        started = time.perf_counter()

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            collection_name = WORD_STRUCTURE_COLLECTIONS[structure]
            try:
                collection = getattr(document, collection_name)
                total = max(0, int(collection.Count))
            except Exception:
                return {
                    "live_document_id": record.document_id,
                    "live_version": record.version,
                    "document": self._document_info(application, document),
                    "structure": structure,
                    "collection_property": collection_name,
                    "available": False,
                    "total_count": None,
                    "offset": offset,
                    "limit": limit,
                    "returned_count": 0,
                    "truncated": False,
                    "items": [],
                    "item_read_errors": 0,
                    "properties_probed": [],
                    "properties_skipped": sorted(property_names),
                    "property_outcomes": {},
                    "text_content_returned": False,
                    "external_addresses_returned": False,
                    "field_codes_returned": False,
                }

            first_index = min(total, offset) + 1
            last_index = min(total, offset + limit)
            items: list[dict[str, Any]] = []
            item_read_errors = 0
            text_returned = False
            outcomes: dict[str, dict[str, Any]] = {
                name: {
                    "attempted": name in properties_to_probe,
                    "successful_reads": 0,
                    "failed_reads": 0,
                }
                for name in property_names
            }
            for index in range(first_index, last_index + 1):
                try:
                    item = collection.Item(index)
                except Exception:
                    item_read_errors += 1
                    items.append(
                        {
                            "index": index,
                            "properties": {},
                            "unavailable_properties": ["item"],
                        }
                    )
                    continue
                values: dict[str, Any] = {}
                unavailable: list[str] = []
                truncated_properties: list[str] = []
                for name, path, value_kind in property_specs:
                    if name not in properties_to_probe:
                        continue
                    try:
                        value, truncated = self._structure_property_value(
                            item,
                            path,
                            value_kind,
                        )
                        values[name] = value
                        outcomes[name]["successful_reads"] += 1
                        if truncated:
                            truncated_properties.append(name)
                    except Exception:
                        outcomes[name]["failed_reads"] += 1
                        unavailable.append(name)
                result_item: dict[str, Any] = {
                    "index": index,
                    "properties": values,
                    "unavailable_properties": sorted(unavailable),
                }
                if truncated_properties:
                    result_item["truncated_properties"] = sorted(truncated_properties)
                text_path = spec.get("text_path")
                if include_text and text_path:
                    try:
                        text_range = self._resolve_property_path(item, text_path)
                        preview, truncated = self._word_text_preview(
                            text_range,
                            max_text_chars,
                        )
                        result_item["text_preview"] = preview
                        result_item["text_truncated"] = truncated
                        text_returned = True
                    except Exception:
                        result_item["text_unavailable"] = True
                items.append(result_item)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "document": self._document_info(application, document),
                "structure": structure,
                "collection_property": collection_name,
                "available": True,
                "total_count": total,
                "offset": offset,
                "limit": limit,
                "returned_count": len(items),
                "truncated": offset + len(items) < total,
                "items": items,
                "item_read_errors": item_read_errors,
                "properties_probed": sorted(properties_to_probe),
                "properties_skipped": sorted(set(property_names) - properties_to_probe),
                "property_outcomes": outcomes,
                "text_content_returned": text_returned,
                "external_addresses_returned": False,
                "field_codes_returned": False,
            }

        result = cast(dict[str, Any], self._execute(operation))
        duration_ms = (time.perf_counter() - started) * 1000
        outcomes = cast(dict[str, dict[str, Any]], result.pop("property_outcomes", {}))
        property_read_attempts = sum(
            int(outcome.get("successful_reads", 0)) + int(outcome.get("failed_reads", 0))
            for outcome in outcomes.values()
        )
        observation_recorded = False
        if result["available"] and result["returned_count"] and outcomes:
            try:
                self.structure_learning.record_inspection(
                    structure,
                    outcomes,
                    duration_ms=duration_ms,
                )
                observation_recorded = True
            except (OSError, TypeError, ValueError):
                observation_recorded = False
        result["property_learning"] = {
            "adaptive_property_probing": adaptive_property_probing,
            "observation_recorded": observation_recorded,
            "duration_ms": round(duration_ms, 3),
            "property_values_stored": False,
            "document_content_stored": False,
            "document_counts_stored": False,
            "path_exposed": False,
        }
        result["performance"] = {
            "com_attachments": 1,
            "collection_item_reads": result["returned_count"],
            "property_read_attempts": property_read_attempts,
            "text_read_attempts": sum(
                1
                for item in result["items"]
                if "text_preview" in item or item.get("text_unavailable")
            ),
        }
        return result

    def inspect_learning(self) -> dict[str, Any]:
        self._require_available()
        return self.learning.inspect()

    def inspect_structure_learning(self) -> dict[str, Any]:
        self._require_available()
        return self.structure_learning.inspect()

    @staticmethod
    def _validate_object_model_page(
        *,
        query: str,
        kind: str,
        offset: int,
        limit: int,
        valid_kinds: frozenset[str],
        refresh: bool,
    ) -> tuple[str, str]:
        if not isinstance(query, str) or len(query) > 128:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "query must be a string of at most 128 characters",
            )
        if not isinstance(kind, str) or len(kind) > 64:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "kind must be a string of at most 64 characters",
            )
        normalized_kind = kind.strip().casefold()
        if normalized_kind and normalized_kind not in valid_kinds:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "kind is not supported by the Word object-model catalog",
                {"kind": kind, "allowed": sorted(valid_kinds)},
            )
        if isinstance(offset, bool) or not isinstance(offset, int) or not 0 <= offset <= 1_000_000:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "offset must be an integer from 0 to 1,000,000",
            )
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 200:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "limit must be an integer from 1 to 200",
            )
        if not isinstance(refresh, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "refresh must be true or false",
            )
        return query.strip().casefold(), normalized_kind

    def _word_object_model_catalog(
        self,
        *,
        refresh: bool,
    ) -> tuple[dict[str, Any], dict[str, Any]]:
        self._require_available()
        if not refresh:
            cached = self.object_model.load()
            if cached is not None:
                return cached, {
                    "cache_hit": True,
                    "catalog_generated": False,
                    "word_attached": False,
                    "cache_persisted": True,
                    "com_attachments": 0,
                }

        catalog = cast(
            dict[str, Any],
            self._execute(lambda application: scan_word_object_model(application)),
        )
        cache_persisted = False
        try:
            self.object_model.write(catalog)
            cache_persisted = True
        except (OSError, TypeError, ValueError):
            cache_persisted = False
        return catalog, {
            "cache_hit": False,
            "catalog_generated": True,
            "word_attached": True,
            "cache_persisted": cache_persisted,
            "com_attachments": 1,
        }

    def _word_member_registry(self, catalog: dict[str, Any]) -> dict[str, Any]:
        library = cast(dict[str, Any], catalog.get("library", {}))
        stats = cast(dict[str, Any], catalog.get("stats", {}))
        key = "\0".join(
            (
                str(catalog.get("generated_at", "")),
                str(library.get("guid", "")),
                str(library.get("major_version", "")),
                str(library.get("minor_version", "")),
                str(stats.get("member_count", "")),
            )
        )
        if self._member_registry is None or key != self._member_registry_key:
            registry = build_member_capability_registry(catalog)
            if not bool(registry.get("stats", {}).get("complete", False)):
                raise WordToolkitError(
                    ErrorCode.INTERNAL_ERROR,
                    "The Word member-capability registry is incomplete",
                    registry.get("stats", {}),
                )
            self._member_registry = registry
            self._member_registry_key = key
        return self._member_registry

    @staticmethod
    def _object_model_common(
        catalog: dict[str, Any],
        source: dict[str, Any],
    ) -> dict[str, Any]:
        library = cast(dict[str, Any], catalog.get("library", {}))
        stats = cast(dict[str, Any], catalog.get("stats", {}))
        return {
            "catalog": {
                "generated_at": str(catalog.get("generated_at", ""))[:64],
                "source": "installed_microsoft_word_com_type_library",
                "library": {
                    "guid": str(library.get("guid", ""))[:64],
                    "lcid": int(library.get("lcid", 0)),
                    "syskind": int(library.get("syskind", 0)),
                    "major_version": int(library.get("major_version", 0)),
                    "minor_version": int(library.get("minor_version", 0)),
                    "flags": int(library.get("flags", 0)),
                    "declared_type_count": int(library.get("declared_type_count", 0)),
                    "application_type_index": int(library.get("application_type_index", 0)),
                },
                "stats": {
                    "type_count": int(stats.get("type_count", 0)),
                    "member_count": int(stats.get("member_count", 0)),
                    "scan_errors": int(stats.get("scan_errors", 0)),
                    "truncated": bool(stats.get("truncated", False)),
                    "scan_duration_ms": float(stats.get("scan_duration_ms", 0.0)),
                },
            },
            "source_access": source,
            "privacy": {
                "api_metadata_stored": True,
                "document_content_stored": False,
                "document_counts_stored": False,
                "paths_stored_or_returned": False,
                "handles_or_owner_ids_stored": False,
                "help_text_or_help_paths_stored": False,
            },
        }

    def inspect_object_model_types(
        self,
        *,
        query: str = "",
        kind: str = "",
        offset: int = 0,
        limit: int = 100,
        refresh: bool = False,
    ) -> dict[str, Any]:
        normalized_query, normalized_kind = self._validate_object_model_page(
            query=query,
            kind=kind,
            offset=offset,
            limit=limit,
            valid_kinds=VALID_TYPE_KINDS,
            refresh=refresh,
        )
        catalog, source = self._word_object_model_catalog(refresh=refresh)
        matches: list[dict[str, Any]] = []
        for item in catalog.get("types", []):
            if not isinstance(item, dict):
                continue
            name = str(item.get("name", ""))[:256]
            item_kind = str(item.get("kind", ""))[:64]
            if normalized_query and normalized_query not in name.casefold():
                continue
            if normalized_kind and item_kind.casefold() != normalized_kind:
                continue
            matches.append(
                {
                    "name": name,
                    "kind": item_kind,
                    "type_index": int(item.get("type_index", 0)),
                    "guid": str(item.get("guid", ""))[:64],
                    "flags": int(item.get("flags", 0)),
                    "declared_function_count": int(item.get("declared_function_count", 0)),
                    "declared_variable_count": int(item.get("declared_variable_count", 0)),
                    "implemented_type_count": int(item.get("implemented_type_count", 0)),
                    "member_count": int(item.get("member_count", 0)),
                }
            )
        page = matches[offset : offset + limit]
        result = self._object_model_common(catalog, source)
        result.update(
            {
                "query": query.strip(),
                "kind": normalized_kind,
                "offset": offset,
                "limit": limit,
                "matched_count": len(matches),
                "returned_count": len(page),
                "has_more": offset + len(page) < len(matches),
                "types": page,
                "document_content_returned": False,
            }
        )
        return result

    def inspect_object_model_members(
        self,
        type_name: str,
        *,
        query: str = "",
        kind: str = "",
        offset: int = 0,
        limit: int = 100,
        refresh: bool = False,
    ) -> dict[str, Any]:
        if not isinstance(type_name, str) or not type_name.strip() or len(type_name) > 256:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "type_name must be a non-empty string of at most 256 characters",
            )
        normalized_query, normalized_kind = self._validate_object_model_page(
            query=query,
            kind=kind,
            offset=offset,
            limit=limit,
            valid_kinds=VALID_MEMBER_KINDS,
            refresh=refresh,
        )
        catalog, source = self._word_object_model_catalog(refresh=refresh)
        wanted = type_name.strip().casefold()
        selected = next(
            (
                item
                for item in catalog.get("types", [])
                if isinstance(item, dict) and str(item.get("name", "")).casefold() == wanted
            ),
            None,
        )
        if selected is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "The requested Word object-model type was not found",
                {"type_name": type_name.strip()},
            )

        matches: list[dict[str, Any]] = []
        for member in selected.get("members", []):
            if not isinstance(member, dict):
                continue
            name = str(member.get("name", ""))[:256]
            member_kind = str(member.get("kind", ""))[:64]
            if normalized_query and normalized_query not in name.casefold():
                continue
            if normalized_kind and member_kind.casefold() != normalized_kind:
                continue
            payload: dict[str, Any] = {
                "name": name,
                "kind": member_kind,
                "member_id": int(member.get("member_id", 0)),
                "declaration_index": int(member.get("declaration_index", 0)),
                "flags": int(member.get("flags", 0)),
                "flag_names": [str(value)[:32] for value in member.get("flag_names", [])[:16]],
            }
            if member_kind in {"method", "property_get", "property_put", "property_put_ref"}:
                parameters = []
                for parameter in member.get("parameters", [])[:255]:
                    if not isinstance(parameter, dict):
                        continue
                    parameters.append(
                        {
                            "name": str(parameter.get("name", ""))[:256],
                            "type": str(parameter.get("type", "UNKNOWN"))[:256],
                            "flags": int(parameter.get("flags", 0)),
                            "flag_names": [
                                str(value)[:32] for value in parameter.get("flag_names", [])[:16]
                            ],
                            "optional": bool(parameter.get("optional", False)),
                            **(
                                {"default_value": parameter.get("default_value")}
                                if "default_value" in parameter
                                else {}
                            ),
                        }
                    )
                payload.update(
                    {
                        "parameters": parameters,
                        "parameter_count": int(member.get("parameter_count", 0)),
                        "optional_parameter_count": int(member.get("optional_parameter_count", 0)),
                        "function_kind": int(member.get("function_kind", 0)),
                        "invoke_kind": int(member.get("invoke_kind", 0)),
                        "call_convention": int(member.get("call_convention", 0)),
                        "vtable_offset": int(member.get("vtable_offset", 0)),
                        "variadic": bool(member.get("variadic", False)),
                        "return_type": str(member.get("return_type", "UNKNOWN"))[:256],
                    }
                )
            else:
                payload["type"] = str(member.get("type", "UNKNOWN"))[:256]
                if member_kind == "enum_value":
                    value = member.get("value")
                    payload["value"] = value if isinstance(value, (bool, int, float, str)) else None
            matches.append(payload)

        page = matches[offset : offset + limit]
        result = self._object_model_common(catalog, source)
        result.update(
            {
                "type": {
                    "name": str(selected.get("name", ""))[:256],
                    "kind": str(selected.get("kind", ""))[:64],
                    "type_index": int(selected.get("type_index", 0)),
                    "guid": str(selected.get("guid", ""))[:64],
                    "member_count": int(selected.get("member_count", 0)),
                    "implemented_types": [
                        {
                            "name": str(item.get("name", ""))[:256],
                            "kind": str(item.get("kind", ""))[:64],
                            "guid": str(item.get("guid", ""))[:64],
                            "flags": int(item.get("flags", 0)),
                            "flag_names": [
                                str(value)[:32] for value in item.get("flag_names", [])[:16]
                            ],
                        }
                        for item in selected.get("implemented_types", [])[:32]
                        if isinstance(item, dict)
                    ],
                },
                "query": query.strip(),
                "kind": normalized_kind,
                "offset": offset,
                "limit": limit,
                "matched_count": len(matches),
                "returned_count": len(page),
                "has_more": offset + len(page) < len(matches),
                "members": page,
                "document_content_returned": False,
            }
        )
        return result

    def inspect_member_capabilities(
        self,
        *,
        query: str = "",
        type_name: str = "",
        member_kind: str = "",
        effect: str = "",
        execution: str = "",
        detail: str = "summary",
        offset: int = 0,
        limit: int = 100,
        refresh: bool = False,
    ) -> dict[str, Any]:
        normalized_query, normalized_kind = self._validate_object_model_page(
            query=query,
            kind=member_kind,
            offset=offset,
            limit=limit,
            valid_kinds=VALID_MEMBER_KINDS,
            refresh=refresh,
        )
        if not isinstance(type_name, str) or len(type_name) > 256:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "type_name must be a string of at most 256 characters",
            )
        normalized_type = type_name.strip().casefold()
        normalized_effect = effect.strip().casefold()
        normalized_execution = execution.strip().casefold()
        if not isinstance(detail, str) or detail not in {"summary", "full"}:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "detail must be summary or full",
                {"detail": detail, "allowed": ["summary", "full"]},
            )
        if normalized_effect and normalized_effect not in VALID_CAPABILITY_EFFECTS:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "effect is not supported by the Word capability registry",
                {"effect": effect, "allowed": sorted(VALID_CAPABILITY_EFFECTS)},
            )
        if normalized_execution and normalized_execution not in VALID_CAPABILITY_EXECUTIONS:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "execution is not supported by the Word capability registry",
                {
                    "execution": execution,
                    "allowed": sorted(VALID_CAPABILITY_EXECUTIONS),
                },
            )
        catalog, source = self._word_object_model_catalog(refresh=refresh)
        registry = self._word_member_registry(catalog)
        matches = []
        for profile in registry["profiles"]:
            profile_type = str(profile["type"]["name"])
            member = cast(dict[str, Any], profile["member"])
            policy = cast(dict[str, Any], profile["policy"])
            virtual_tool = cast(dict[str, Any], profile["virtual_tool"])
            searchable = (
                f"{profile_type} {member['name']} {profile['capability_id']} {virtual_tool['name']}"
            ).casefold()
            if normalized_query and normalized_query not in searchable:
                continue
            if normalized_type and profile_type.casefold() != normalized_type:
                continue
            if normalized_kind and str(member["kind"]).casefold() != normalized_kind:
                continue
            if normalized_effect and str(policy["effect"]) != normalized_effect:
                continue
            if normalized_execution and str(policy["execution"]) != normalized_execution:
                continue
            matches.append(profile)
        matched_page = matches[offset : offset + limit]
        if detail == "full":
            page = matched_page
        else:
            page = [self._member_capability_summary(profile) for profile in matched_page]
        result = self._object_model_common(catalog, source)
        result.update(
            {
                "registry": {
                    "schema_version": int(registry["schema_version"]),
                    "stats": registry["stats"],
                },
                "query": query.strip(),
                "type_name": type_name.strip(),
                "member_kind": normalized_kind,
                "effect": normalized_effect,
                "execution": normalized_execution,
                "detail": detail,
                "offset": offset,
                "limit": limit,
                "matched_count": len(matches),
                "returned_count": len(page),
                "has_more": offset + len(page) < len(matches),
                "capabilities": page,
                "full_profile_query": (
                    "Call this tool with detail='full', query=<capability_id> and limit=1 "
                    "to retrieve the exact input/output schemas for one selected capability."
                ),
                "document_content_returned": False,
            }
        )
        return result

    @staticmethod
    def _member_capability_summary(profile: dict[str, Any]) -> dict[str, Any]:
        profile_type = cast(dict[str, Any], profile["type"])
        member = cast(dict[str, Any], profile["member"])
        signature = cast(dict[str, Any], profile["signature"])
        target = cast(dict[str, Any], profile["target"])
        policy = cast(dict[str, Any], profile["policy"])
        summary: dict[str, Any] = {
            "capability_id": profile["capability_id"],
            "type_name": profile_type["name"],
            "member_name": member["name"],
            "member_kind": member["kind"],
            "parameter_count": signature["parameter_count"],
            "optional_parameter_count": signature["optional_parameter_count"],
            "variadic": signature["variadic"],
            "return_type": signature["return_type"],
            "allowed_roots": target["allowed_roots"],
            "effect": policy["effect"],
            "execution": policy["execution"],
            "reason": policy["reason"],
            "mutating": policy["mutating"],
        }
        summary["constant"] = profile.get("constant")
        return summary

    def _prepare_member_operations(
        self,
        operations: list[dict[str, Any]],
    ) -> tuple[
        dict[str, Any],
        dict[str, Any],
        list[PreparedMemberOperation],
    ]:
        catalog, source = self._word_object_model_catalog(refresh=False)
        registry = self._word_member_registry(catalog)
        prepared = prepare_member_operations(registry, operations)
        return registry, source, prepared

    def preflight_member_operations(
        self,
        operations: list[dict[str, Any]],
    ) -> dict[str, Any]:
        registry, source, prepared = self._prepare_member_operations(operations)
        payload = member_preflight_payload(registry, prepared)
        payload["source_access"] = source
        payload["document_content_returned"] = False
        return payload

    @staticmethod
    def _resolve_member_argument(
        value: Any,
        results: dict[str, Any],
    ) -> Any:
        if isinstance(value, dict):
            if "result_id" in value:
                return results[str(value["result_id"])]
            if value == {"missing": True}:
                try:
                    import pythoncom
                except ImportError as exc:
                    raise WordToolkitError(
                        ErrorCode.LIVE_WORD_UNAVAILABLE,
                        "Optional COM argument omission requires PyWin32",
                    ) from exc
                return pythoncom.Missing
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "An unresolved member argument reference reached execution",
            )
        return value

    @staticmethod
    def _member_result_payload(value: Any, declared_type: str) -> dict[str, Any]:
        if value is None or isinstance(value, (bool, int, float)):
            return {
                "kind": "scalar",
                "declared_type": declared_type,
                "value": value,
                "truncated": False,
            }
        if isinstance(value, str):
            return {
                "kind": "text",
                "declared_type": declared_type,
                "value": value[:10_000],
                "truncated": len(value) > 10_000,
            }
        if isinstance(value, (tuple, list)):
            safe_values = [
                item
                for item in value[:100]
                if item is None or isinstance(item, (bool, int, float, str))
            ]
            return {
                "kind": "array",
                "declared_type": declared_type,
                "value": [item[:1_000] if isinstance(item, str) else item for item in safe_values],
                "truncated": len(value) > 100 or len(safe_values) != len(value[:100]),
            }
        return {
            "kind": "com_object",
            "declared_type": declared_type,
            "runtime_python_type": type(value).__name__[:128],
            "value_returned": False,
            "usable_by_result_id": True,
        }

    @classmethod
    def _invoke_member_operation(
        cls,
        prepared: PreparedMemberOperation,
        target: Any,
        results: dict[str, Any],
    ) -> Any:
        member = cast(dict[str, Any], prepared.profile["member"])
        member_name = str(member["name"])
        member_kind = str(member["kind"])
        arguments = [cls._resolve_member_argument(value, results) for value in prepared.arguments]
        try:
            if member_kind == "property_get":
                value = getattr(target, member_name)
                if arguments:
                    if not callable(value):
                        raise TypeError("indexed property is not callable")
                    return value(*arguments)
                return value
            if member_kind in {"property_put", "property_put_ref"}:
                if len(arguments) == 1:
                    setattr(target, member_name, arguments[0])
                    return None
                dispatch = getattr(target, "_oleobj_", None)
                if dispatch is None or not callable(getattr(dispatch, "Invoke", None)):
                    raise TypeError("indexed property assignment requires an IDispatch target")
                invoke_kind = int(member.get("invoke_kind", 0))
                if invoke_kind not in {4, 8}:
                    invoke_kind = 8 if member_kind == "property_put_ref" else 4
                dispatch.Invoke(
                    int(member["member_id"]),
                    0,
                    invoke_kind,
                    0,
                    *arguments,
                )
                return None
            if member_kind == "method":
                method = getattr(target, member_name)
                if not callable(method):
                    raise TypeError("Word member is not callable")
                return method(*arguments)
        except Exception as exc:
            raise WordToolkitError(
                ErrorCode.EXTERNAL_TOOL_FAILED,
                "Microsoft Word rejected a catalog-backed member operation",
                {
                    "operation_id": prepared.operation_id,
                    "capability_id": prepared.capability_id,
                    "member": (
                        f"{prepared.profile['type']['name']}.{prepared.profile['member']['name']}"
                    ),
                    "exception": type(exc).__name__,
                },
                retryable=True,
            ) from exc
        raise WordToolkitError(
            ErrorCode.INVALID_INPUT,
            "The catalog-backed member kind is not executable",
            {
                "operation_id": prepared.operation_id,
                "member_kind": member_kind,
            },
        )

    def execute_member_operations(
        self,
        owner: str,
        document_id: str,
        *,
        operations: list[dict[str, Any]],
        activate: bool = True,
        expected_version: int | None = None,
        optimize_screen_updates: bool = True,
    ) -> dict[str, Any]:
        if not isinstance(activate, bool) or not isinstance(optimize_screen_updates, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "member-operation execution flags must be true or false",
            )
        record = self._record(owner, document_id)
        registry, source, prepared = self._prepare_member_operations(operations)
        preflight = member_preflight_payload(registry, prepared)
        mutating = bool(preflight["mutating_count"])
        if mutating and expected_version is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "expected_version is required for mutating Word member operations",
            )
        if expected_version is not None:
            self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            if expected_version is not None:
                self._check_version(record, expected_version)
            if activate:
                document.Activate()
            raw_results: dict[str, Any] = {}
            returned_results: list[dict[str, Any]] = []

            def apply_all() -> None:
                for index, item in enumerate(prepared):
                    try:
                        if item.target_kind == "document":
                            target = document
                        elif item.target_kind == "document_content":
                            target = document.Content
                        elif item.target_kind in {"selection", "selection_range"}:
                            self._require_active(application, document)
                            target = (
                                application.Selection
                                if item.target_kind == "selection"
                                else application.Selection.Range
                            )
                        else:
                            target = raw_results[item.target_result_id]
                        value = self._invoke_member_operation(
                            item,
                            target,
                            raw_results,
                        )
                        if item.result_id:
                            raw_results[item.result_id] = value
                            returned_results.append(
                                {
                                    "operation_id": item.operation_id,
                                    "result_id": item.result_id,
                                    **self._member_result_payload(
                                        value,
                                        str(item.profile["signature"]["return_type"]),
                                    ),
                                }
                            )
                    except WordToolkitError as exc:
                        details = dict(exc.details or {})
                        details.pop("failed_operation_index_available", None)
                        details.pop("failure_scope", None)
                        details["failed_operation_index"] = index
                        raise WordToolkitError(
                            exc.code,
                            exc.message,
                            details,
                            exc.retryable,
                        ) from exc
                    except Exception as exc:
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "A catalog-backed member operation failed",
                            {
                                "operation_id": item.operation_id,
                                "capability_id": item.capability_id,
                                "failed_operation_index": index,
                                "exception": type(exc).__name__,
                            },
                            retryable=True,
                        ) from exc

            if mutating:
                with (
                    self._screen_updates_suspended(
                        application,
                        enabled=optimize_screen_updates,
                    ),
                    self._undoable(
                        application,
                        document,
                        "WordToolkit: catalog member operations",
                    ),
                ):
                    apply_all()
                record.version += 1
            else:
                apply_all()
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "preflight": preflight,
                "executed_count": len(prepared),
                "mutating": mutating,
                "results": returned_results,
                "document": self._document_info(application, document),
                "source_access": source,
                "execution": {
                    "catalog_member_names_only": True,
                    "arbitrary_com_paths_allowed": False,
                    "single_com_attachment": True,
                    "single_undo_record": mutating,
                    "rollback_on_error": mutating,
                    "screen_updates_suspended": (mutating and optimize_screen_updates),
                },
            }

        try:
            return cast(dict[str, Any], self._execute(operation))
        except WordToolkitError as exc:
            details = dict(exc.details or {})
            if "failed_operation_index" in details:
                raise
            details.update(
                {
                    "failed_operation_index_available": False,
                    "failure_scope": "batch",
                }
            )
            raise WordToolkitError(
                exc.code,
                exc.message,
                details,
                exc.retryable,
            ) from exc

    def selection(self, owner: str, document_id: str, *, max_chars: int = 10_000) -> dict[str, Any]:
        record = self._record(owner, document_id)

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            self._require_active(application, document)
            selection = application.Selection
            text = str(selection.Range.Text or "")
            truncated = len(text) > max_chars
            token = self._selection_token(application, document, record)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "selection": {
                    "start": int(selection.Start),
                    "end": int(selection.End),
                    "collapsed": int(selection.Start) == int(selection.End),
                    "text": text[:max_chars],
                    "truncated": truncated,
                    "selection_type": int(selection.Type),
                    "story_type": int(selection.Range.StoryType),
                    "selection_token": token,
                },
            }

        return self._execute(operation)

    @staticmethod
    def _validate_live_find_request(
        search_text: str,
        *,
        match_case: bool,
        whole_word: bool,
        use_wildcards: bool,
        context_chars: int,
        max_results: int,
    ) -> None:
        if not isinstance(search_text, str) or not search_text:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "search_text must be a non-empty string",
            )
        if len(search_text) > 255:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "search_text exceeds Word's 255-character Find limit",
                {"length": len(search_text), "limit": 255},
            )
        unsafe = [
            f"U+{ord(character):04X}"
            for character in search_text
            if (ord(character) < 32 and character not in {"\t", "\n", "\r", "\x0c"})
            or ord(character) == 127
        ]
        if unsafe:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "search_text contains control characters that are unsafe for Word Find",
                {"characters": sorted(set(unsafe))},
            )
        if not all(isinstance(value, bool) for value in (match_case, whole_word, use_wildcards)):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Word Find flags must be true or false",
            )
        if isinstance(context_chars, bool) or not isinstance(context_chars, int):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "context_chars must be an integer",
            )
        if not 0 <= context_chars <= 2_000:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "context_chars must be between 0 and 2,000",
            )
        if isinstance(max_results, bool) or not isinstance(max_results, int):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "max_results must be an integer",
            )
        if not 1 <= max_results <= 5_001:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "max_results must be between 1 and 5,001",
            )

    @staticmethod
    def _clean_word_preview(value: str, max_chars: int) -> tuple[str, bool]:
        cleaned = value.replace("\r", "\n").replace("\x07", "")
        return cleaned[:max_chars], len(cleaned) > max_chars

    @classmethod
    def _find_live_ranges(
        cls,
        document: Any,
        *,
        search_text: str,
        match_case: bool,
        whole_word: bool,
        use_wildcards: bool,
        context_chars: int,
        max_results: int,
    ) -> list[dict[str, Any]]:
        content_end = max(0, int(document.Content.End))
        cursor = 0
        matches: list[dict[str, Any]] = []
        while cursor <= content_end and len(matches) < max_results:
            search_range = document.Range(cursor, content_end)
            find = search_range.Find
            find.ClearFormatting()
            found = bool(
                find.Execute(
                    FindText=search_text,
                    MatchCase=match_case,
                    MatchWholeWord=whole_word if not use_wildcards else False,
                    MatchWildcards=use_wildcards,
                    Forward=True,
                    Wrap=0,
                    Format=False,
                )
            )
            if not found:
                break
            start = int(search_range.Start)
            end = int(search_range.End)
            if end < start:
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED,
                    "Word Find returned an invalid or backward range",
                    {"cursor": cursor, "start": start, "end": end},
                    retryable=True,
                )
            # Word may expand a Find range back to the OMath run containing the
            # cursor and return the match that just ended there. Skip that
            # duplicate while advancing through the equation instead of
            # aborting a valid find/replace operation.
            if start < cursor:
                cursor = min(content_end + 1, cursor + 1)
                continue
            if start == end:
                cursor = min(content_end + 1, max(cursor + 1, end + 1))
                continue
            context_start = max(0, start - context_chars)
            context_end = min(content_end, end + context_chars)
            raw_context = str(document.Range(context_start, context_end).Text or "")
            context, context_truncated = cls._clean_word_preview(
                raw_context,
                max(1, (context_chars * 2) + 255),
            )
            raw_match = str(search_range.Text or "")
            match_text, match_truncated = cls._clean_word_preview(raw_match, 255)
            matches.append(
                {
                    "start": start,
                    "end": end,
                    "text": match_text,
                    "text_truncated": match_truncated,
                    "context": context,
                    "context_truncated": context_truncated,
                }
            )
            cursor = end if end > cursor else cursor + 1
        return matches

    def find_text(
        self,
        owner: str,
        document_id: str,
        *,
        search_text: str,
        match_case: bool = False,
        whole_word: bool = False,
        use_wildcards: bool = False,
        context_chars: int = 80,
        max_results: int = 100,
    ) -> dict[str, Any]:
        self._validate_live_find_request(
            search_text,
            match_case=match_case,
            whole_word=whole_word,
            use_wildcards=use_wildcards,
            context_chars=context_chars,
            max_results=max_results,
        )
        record = self._record(owner, document_id)

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            matches = self._find_live_ranges(
                document,
                search_text=search_text,
                match_case=match_case,
                whole_word=whole_word,
                use_wildcards=use_wildcards,
                context_chars=context_chars,
                max_results=max_results + 1,
            )
            truncated = len(matches) > max_results
            returned = matches[:max_results]
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "query": {
                    "search_text": search_text,
                    "match_case": match_case,
                    "whole_word": whole_word,
                    "whole_word_effective": whole_word and not use_wildcards,
                    "use_wildcards": use_wildcards,
                },
                "match_count": len(returned),
                "truncated": truncated,
                "matches": returned,
                "document": self._document_info(application, document),
                "performance": {
                    "com_attachments": 1,
                    "native_find": True,
                    "content_round_trip": False,
                },
            }

        return cast(dict[str, Any], self._execute(operation))

    @staticmethod
    def _replacement_payload(replacement_text: str, *, use_wildcards: bool) -> str:
        if not use_wildcards:
            return replacement_text
        return (
            replacement_text.replace("^p", "\r")
            .replace("^t", "\t")
            .replace("^m", "\x0c")
            .replace("^s", "\u00a0")
        )

    def replace_text(
        self,
        owner: str,
        document_id: str,
        *,
        search_text: str,
        replacement_text: str,
        match_case: bool = False,
        whole_word: bool = False,
        use_wildcards: bool = False,
        replace_all: bool = True,
        track_changes: LiveTrackChangesMode = "preserve",
        max_replacements: int = 1_000,
        optimize_screen_updates: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        if expected_version is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "expected_version is required for live replacement",
            )
        if not isinstance(replacement_text, str) or len(replacement_text) > 200_000:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "replacement_text must be a string of at most 200,000 characters",
            )
        unsafe = [
            f"U+{ord(character):04X}"
            for character in replacement_text
            if (ord(character) < 32 and character not in {"\t", "\n", "\r", "\x0c"})
            or ord(character) == 127
        ]
        if unsafe:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "replacement_text contains unsafe control characters",
                {"characters": sorted(set(unsafe))},
            )
        if not isinstance(replace_all, bool) or not isinstance(optimize_screen_updates, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "replacement flags must be true or false",
            )
        if track_changes not in {"preserve", "enable", "disable"}:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "track_changes must be preserve, enable, or disable",
            )
        if (
            isinstance(max_replacements, bool)
            or not isinstance(max_replacements, int)
            or not 1 <= max_replacements <= 5_000
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "max_replacements must be between 1 and 5,000",
            )
        search_limit = max_replacements if replace_all else 1
        self._validate_live_find_request(
            search_text,
            match_case=match_case,
            whole_word=whole_word,
            use_wildcards=use_wildcards,
            context_chars=0,
            max_results=search_limit + 1,
        )
        payload = self._replacement_payload(replacement_text, use_wildcards=use_wildcards)
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            matches = self._find_live_ranges(
                document,
                search_text=search_text,
                match_case=match_case,
                whole_word=whole_word,
                use_wildcards=use_wildcards,
                context_chars=0,
                max_results=search_limit + 1,
            )
            if replace_all and len(matches) > max_replacements:
                raise WordToolkitError(
                    ErrorCode.LIMIT_EXCEEDED,
                    "Replacement refused before mutation because the match count exceeds the limit",
                    {
                        "limit": max_replacements,
                        "at_least_matches": len(matches),
                    },
                )
            selected = matches[:search_limit]
            if not selected:
                return {
                    "live_document_id": record.document_id,
                    "live_version": record.version,
                    "mutated": False,
                    "replacements": 0,
                    "document": self._document_info(application, document),
                    "execution": {
                        "com_attachments": 1,
                        "single_undo_record": False,
                        "rollback_on_error": False,
                        "track_changes_restored": True,
                    },
                }
            original_tracking = bool(getattr(document, "TrackRevisions", False))
            desired_tracking = (
                original_tracking if track_changes == "preserve" else track_changes == "enable"
            )
            if desired_tracking != original_tracking:
                document.TrackRevisions = desired_tracking
            transaction_succeeded = False
            try:
                with (
                    self._screen_updates_suspended(
                        application,
                        enabled=optimize_screen_updates,
                    ),
                    self._undoable(
                        application,
                        document,
                        "WordToolkit: replace live text",
                    ),
                ):
                    for match in reversed(selected):
                        target_range = document.Range(int(match["start"]), int(match["end"]))
                        target_range.Text = payload
                transaction_succeeded = True
            finally:
                if bool(getattr(document, "TrackRevisions", False)) != original_tracking:
                    try:
                        document.TrackRevisions = original_tracking
                        if bool(document.TrackRevisions) != original_tracking:
                            raise RuntimeError("Word did not restore Track Changes")
                    except Exception as exc:
                        if transaction_succeeded:
                            with suppress(Exception):
                                document.Undo(1)
                        with suppress(Exception):
                            document.TrackRevisions = original_tracking
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Word did not restore the prior Track Changes state",
                            {"exception": type(exc).__name__},
                            retryable=True,
                        ) from exc
            record.version += 1
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "mutated": True,
                "replacements": len(selected),
                "replace_all": replace_all,
                "tracked": desired_tracking,
                "track_changes_mode": track_changes,
                "document": self._document_info(application, document),
                "execution": {
                    "com_attachments": 1,
                    "native_find": True,
                    "content_round_trip": False,
                    "single_undo_record": True,
                    "rollback_on_error": True,
                    "track_changes_restored": True,
                    "screen_updates_suspended": optimize_screen_updates,
                },
            }

        return cast(dict[str, Any], self._execute(operation))

    @staticmethod
    def _review_range(item: Any, kind: LiveReviewKind) -> Any:
        return item.Scope if kind == "comments" else item.Range

    @staticmethod
    def _comment_reply_count(item: Any) -> tuple[int, bool]:
        try:
            return max(0, int(item.Replies.Count)), True
        except Exception:
            return 0, False

    @staticmethod
    def _comment_resolved_state(item: Any) -> tuple[bool, bool]:
        try:
            return bool(item.Done), True
        except Exception:
            return False, False

    @classmethod
    def _review_signature(cls, item: Any, kind: LiveReviewKind) -> str:
        target_range = cls._review_range(item, kind)
        text_range = item.Range
        reply_count, replies_supported = cls._comment_reply_count(item)
        resolved, resolve_supported = cls._comment_resolved_state(item)
        fields = [
            kind,
            str(int(getattr(item, "Type", 0))) if kind == "revisions" else "",
            cls._string_property(item, "Author"),
            cls._string_property(item, "Date"),
            str(int(getattr(target_range, "Start", -1))),
            str(int(getattr(target_range, "End", -1))),
            str(reply_count) if kind == "comments" else "",
            str(replies_supported) if kind == "comments" else "",
            str(resolved) if kind == "comments" else "",
            str(resolve_supported) if kind == "comments" else "",
            hashlib.sha256(str(getattr(text_range, "Text", "") or "").encode("utf-8")).hexdigest(),
        ]
        return "\0".join(fields)

    def _review_token(
        self,
        record: LiveWordRecord,
        kind: LiveReviewKind,
        index: int,
        item: Any,
    ) -> str:
        payload = "\0".join(
            (
                "review",
                record.document_id,
                str(record.version),
                kind,
                str(index),
                self._review_signature(item, kind),
            )
        )
        return hmac.new(self._token_secret, payload.encode("utf-8"), hashlib.sha256).hexdigest()

    def _require_review_item(
        self,
        document: Any,
        record: LiveWordRecord,
        *,
        kind: LiveReviewKind,
        item_index: int,
        review_token: str,
    ) -> Any:
        if not review_token:
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "A fresh review_token is required for this review mutation",
                retryable=True,
            )
        collection = document.Comments if kind == "comments" else document.Revisions
        total = int(collection.Count)
        if not 1 <= item_index <= total:
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "The reviewed Word item no longer exists at that position",
                {"item_index": item_index, "total_count": total},
                retryable=True,
            )
        item = collection.Item(item_index)
        actual = self._review_token(record, kind, item_index, item)
        if not hmac.compare_digest(actual, review_token):
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "The Word comment or revision changed after inspection",
                {"kind": kind, "item_index": item_index},
                retryable=True,
            )
        return item

    def inspect_review(
        self,
        owner: str,
        document_id: str,
        *,
        kind: LiveReviewKind,
        offset: int = 0,
        limit: int = 50,
        include_text: bool = True,
        max_text_chars: int = 500,
    ) -> dict[str, Any]:
        if kind not in {"comments", "revisions"}:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "kind must be comments or revisions",
            )
        if isinstance(offset, bool) or not isinstance(offset, int) or not 0 <= offset <= 1_000_000:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "offset must be between 0 and 1,000,000",
            )
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 200:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "limit must be between 1 and 200",
            )
        if (
            isinstance(max_text_chars, bool)
            or not isinstance(max_text_chars, int)
            or not 1 <= max_text_chars <= 2_000
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "max_text_chars must be between 1 and 2,000",
            )
        if not isinstance(include_text, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "include_text must be true or false",
            )
        record = self._record(owner, document_id)

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            collection = document.Comments if kind == "comments" else document.Revisions
            total = max(0, int(collection.Count))
            first = min(total, offset) + 1
            last = min(total, offset + limit)
            items: list[dict[str, Any]] = []
            for index in range(first, last + 1):
                item = collection.Item(index)
                target_range = self._review_range(item, kind)
                payload: dict[str, Any] = {
                    "item_index": index,
                    "review_token": self._review_token(record, kind, index, item),
                    "range": {
                        "start": int(getattr(target_range, "Start", -1)),
                        "end": int(getattr(target_range, "End", -1)),
                    },
                    "author": self._string_property(item, "Author"),
                    "date": self._string_property(item, "Date"),
                }
                if kind == "comments":
                    reply_count, replies_supported = self._comment_reply_count(item)
                    resolved, resolve_supported = self._comment_resolved_state(item)
                    payload["resolved"] = resolved
                    payload["resolve_supported"] = resolve_supported
                    payload["reply_count"] = reply_count
                    payload["replies_supported"] = replies_supported
                else:
                    revision_type = int(getattr(item, "Type", 0))
                    payload["type_id"] = revision_type
                    payload["type"] = WORD_REVISION_TYPES.get(
                        revision_type,
                        f"unknown_{revision_type}",
                    )
                if include_text:
                    raw_text = str(getattr(item.Range, "Text", "") or "")
                    preview, truncated = self._clean_word_preview(raw_text, max_text_chars)
                    payload["text_preview"] = preview
                    payload["text_truncated"] = truncated
                items.append(payload)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "kind": kind,
                "track_changes": bool(getattr(document, "TrackRevisions", False)),
                "total_count": total,
                "offset": offset,
                "limit": limit,
                "returned_count": len(items),
                "truncated": offset + len(items) < total,
                "items": items,
                "token_policy": {
                    "fresh_token_required_for_mutation": True,
                    "raw_index_without_token_allowed": False,
                    "invalidated_by_live_version_change": True,
                    "current_item_fingerprint_verified": True,
                },
                "document": self._document_info(application, document),
            }

        return cast(dict[str, Any], self._execute(operation))

    def manage_review(
        self,
        owner: str,
        document_id: str,
        *,
        action: LiveReviewAction,
        item_index: int = 0,
        review_token: str = "",
        selection_token: str = "",
        text: str = "",
        resolved: bool = True,
        tracking_enabled: bool | None = None,
        optimize_screen_updates: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        supported = {
            "add_comment",
            "reply_comment",
            "resolve_comment",
            "delete_comment",
            "accept_revision",
            "reject_revision",
            "set_track_changes",
        }
        if action not in supported:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Unsupported Word review action",
                {"supported": sorted(supported)},
            )
        if expected_version is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "expected_version is required for review mutations",
            )
        if not isinstance(optimize_screen_updates, bool) or not isinstance(resolved, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "review flags must be true or false",
            )
        text_actions = {"add_comment", "reply_comment"}
        if action in text_actions and (not isinstance(text, str) or not text or len(text) > 20_000):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Comment text must contain between 1 and 20,000 characters",
            )
        if action == "set_track_changes" and not isinstance(tracking_enabled, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "tracking_enabled must be true or false for set_track_changes",
            )
        if action not in {"add_comment", "set_track_changes"} and (
            isinstance(item_index, bool) or not isinstance(item_index, int) or item_index < 1
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "item_index must be a positive integer",
            )
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            result: dict[str, Any]
            undoable = False
            if action == "set_track_changes":
                previous = bool(getattr(document, "TrackRevisions", False))
                assert tracking_enabled is not None
                if previous == tracking_enabled:
                    return {
                        "live_document_id": record.document_id,
                        "live_version": record.version,
                        "action": action,
                        "mutated": False,
                        "previous_state": previous,
                        "track_changes": previous,
                        "document": self._document_info(application, document),
                        "execution": {
                            "single_undo_record": False,
                            "manual_rollback_on_error": True,
                        },
                    }
                try:
                    document.TrackRevisions = tracking_enabled
                    if bool(document.TrackRevisions) != tracking_enabled:
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Word did not apply the requested Track Changes state",
                            retryable=True,
                        )
                except Exception:
                    with suppress(Exception):
                        document.TrackRevisions = previous
                    raise
                result = {
                    "previous_state": previous,
                    "track_changes": tracking_enabled,
                }
            elif action == "add_comment":
                self._require_active(application, document)
                self._verify_selection(
                    application,
                    document,
                    record,
                    selection_token,
                    replace_selection=True,
                    target="selection",
                )
                target_range = application.Selection.Range.Duplicate
                if int(target_range.Start) == int(target_range.End):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "Adding a live comment requires a non-empty Word selection",
                    )
                before_count = int(document.Comments.Count)
                with self._undoable(
                    application,
                    document,
                    "WordToolkit: add live comment",
                ):
                    comment = document.Comments.Add(target_range, text)
                    if int(document.Comments.Count) != before_count + 1:
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Word did not create exactly one comment",
                            retryable=True,
                        )
                undoable = True
                result = {
                    "comment_index": int(getattr(comment, "Index", before_count + 1)),
                    "scope": {
                        "start": int(target_range.Start),
                        "end": int(target_range.End),
                    },
                }
            elif action in {
                "reply_comment",
                "resolve_comment",
                "delete_comment",
            }:
                comment = self._require_review_item(
                    document,
                    record,
                    kind="comments",
                    item_index=item_index,
                    review_token=review_token,
                )
                if action == "reply_comment":
                    before_count, replies_supported = self._comment_reply_count(comment)
                    if not replies_supported:
                        raise WordToolkitError(
                            ErrorCode.LIVE_WORD_UNAVAILABLE,
                            "This Word version does not expose threaded comment replies through COM",
                        )
                    with self._undoable(
                        application,
                        document,
                        "WordToolkit: reply to live comment",
                    ):
                        reply = comment.Replies.Add(comment.Scope, text)
                        if int(comment.Replies.Count) != before_count + 1:
                            raise WordToolkitError(
                                ErrorCode.EXTERNAL_TOOL_FAILED,
                                "Word did not create exactly one comment reply",
                                retryable=True,
                            )
                    undoable = True
                    result = {
                        "comment_index": item_index,
                        "reply_index": int(getattr(reply, "Index", before_count + 1)),
                        "reply_count": int(comment.Replies.Count),
                    }
                elif action == "resolve_comment":
                    previous, resolve_supported = self._comment_resolved_state(comment)
                    if not resolve_supported:
                        raise WordToolkitError(
                            ErrorCode.LIVE_WORD_UNAVAILABLE,
                            "This Word comment model does not expose resolution state through COM",
                        )
                    if previous == resolved:
                        return {
                            "live_document_id": record.document_id,
                            "live_version": record.version,
                            "action": action,
                            "mutated": False,
                            "comment_index": item_index,
                            "resolved": previous,
                            "document": self._document_info(application, document),
                            "execution": {
                                "single_undo_record": False,
                                "manual_rollback_on_error": True,
                            },
                        }
                    try:
                        comment.Done = resolved
                        if bool(comment.Done) != resolved:
                            raise WordToolkitError(
                                ErrorCode.EXTERNAL_TOOL_FAILED,
                                "Word did not apply the requested comment state",
                                retryable=True,
                            )
                    except Exception:
                        with suppress(Exception):
                            comment.Done = previous
                        raise
                    result = {
                        "comment_index": item_index,
                        "previous_state": previous,
                        "resolved": resolved,
                    }
                else:
                    before_count = int(document.Comments.Count)
                    with self._undoable(
                        application,
                        document,
                        "WordToolkit: delete live comment",
                    ):
                        comment.Delete()
                        if int(document.Comments.Count) != before_count - 1:
                            raise WordToolkitError(
                                ErrorCode.EXTERNAL_TOOL_FAILED,
                                "Word did not delete exactly one comment",
                                retryable=True,
                            )
                    undoable = True
                    result = {
                        "deleted_comment_index": item_index,
                        "remaining_comments": int(document.Comments.Count),
                    }
            else:
                revision = self._require_review_item(
                    document,
                    record,
                    kind="revisions",
                    item_index=item_index,
                    review_token=review_token,
                )
                before_count = int(document.Revisions.Count)
                verb = "accept" if action == "accept_revision" else "reject"
                with self._undoable(
                    application,
                    document,
                    f"WordToolkit: {verb} live revision",
                ):
                    if action == "accept_revision":
                        revision.Accept()
                    else:
                        revision.Reject()
                    if int(document.Revisions.Count) >= before_count:
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            f"Word did not {verb} the selected revision",
                            retryable=True,
                        )
                undoable = True
                result = {
                    "reviewed_revision_index": item_index,
                    "decision": verb,
                    "remaining_revisions": int(document.Revisions.Count),
                }
            record.version += 1
            if not undoable:
                record.undo_barrier_version = record.version
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "action": action,
                "mutated": True,
                **result,
                "document": self._document_info(application, document),
                "execution": {
                    "com_attachments": 1,
                    "single_undo_record": undoable,
                    "rollback_on_error": undoable,
                    "manual_rollback_on_error": not undoable,
                    "raw_index_without_token_allowed": action
                    in {"add_comment", "set_track_changes"},
                    "screen_updates_suspended": optimize_screen_updates and undoable,
                },
            }

        return cast(
            dict[str, Any],
            self._execute(
                lambda application: self._manage_review_with_screen_updates(
                    application,
                    operation,
                    enabled=optimize_screen_updates,
                )
            ),
        )

    def _manage_review_with_screen_updates(
        self,
        application: Any,
        operation: Callable[[Any], dict[str, Any]],
        *,
        enabled: bool,
    ) -> dict[str, Any]:
        with self._screen_updates_suspended(application, enabled=enabled):
            return operation(application)

    @staticmethod
    def _word_property_true(value: Any) -> bool:
        if isinstance(value, bool):
            return value
        try:
            numeric = int(value)
        except (TypeError, ValueError):
            return bool(value)
        return numeric not in {0, 9_999_999}

    def diagnose_layout(
        self,
        owner: str,
        document_id: str,
        *,
        max_paragraphs: int = 10_000,
        max_issues: int = 500,
        keep_with_next_threshold: int = 5,
        long_heading_chars: int = 100,
        long_keep_together_chars: int = 1_200,
    ) -> dict[str, Any]:
        limits = {
            "max_paragraphs": (max_paragraphs, 1, 25_000),
            "max_issues": (max_issues, 1, 2_000),
            "keep_with_next_threshold": (keep_with_next_threshold, 2, 100),
            "long_heading_chars": (long_heading_chars, 20, 2_000),
            "long_keep_together_chars": (long_keep_together_chars, 100, 20_000),
        }
        for name, (value, minimum, maximum) in limits.items():
            if (
                isinstance(value, bool)
                or not isinstance(value, int)
                or not minimum <= value <= maximum
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} must be between {minimum:,} and {maximum:,}",
                )
        record = self._record(owner, document_id)

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            paragraphs = document.Paragraphs
            total = max(0, int(paragraphs.Count))
            scan_count = min(total, max_paragraphs)
            issues: list[dict[str, Any]] = []
            dropped_issues = 0
            read_errors = 0
            style_counts: dict[str, int] = {}
            heading_count = 0
            manual_page_breaks = 0
            keep_chain_start = 0
            keep_chain_length = 0
            empty_chain_start = 0
            empty_chain_length = 0

            def add_issue(issue: dict[str, Any]) -> None:
                nonlocal dropped_issues
                if len(issues) < max_issues:
                    issues.append(issue)
                else:
                    dropped_issues += 1

            def flush_keep_chain() -> None:
                nonlocal keep_chain_start, keep_chain_length
                if keep_chain_length >= keep_with_next_threshold:
                    add_issue(
                        {
                            "type": "keep_with_next_chain",
                            "severity": "high",
                            "start_paragraph": keep_chain_start,
                            "end_paragraph": keep_chain_start + keep_chain_length - 1,
                            "length": keep_chain_length,
                        }
                    )
                keep_chain_start = 0
                keep_chain_length = 0

            def flush_empty_chain() -> None:
                nonlocal empty_chain_start, empty_chain_length
                if empty_chain_length >= 3:
                    add_issue(
                        {
                            "type": "consecutive_empty_paragraphs",
                            "severity": "medium",
                            "start_paragraph": empty_chain_start,
                            "end_paragraph": empty_chain_start + empty_chain_length - 1,
                            "length": empty_chain_length,
                        }
                    )
                empty_chain_start = 0
                empty_chain_length = 0

            for index in range(1, scan_count + 1):
                try:
                    paragraph = paragraphs.Item(index)
                    paragraph_range = paragraph.Range
                    paragraph_format = getattr(paragraph, "Format", paragraph_range.ParagraphFormat)
                    raw_text = str(paragraph_range.Text or "")
                    visible_text = raw_text.rstrip("\r\x07")
                    manual_page_breaks += raw_text.count("\x0c")
                    raw_style = getattr(paragraph_range, "Style", "")
                    style_name = self._string_property(raw_style, "NameLocal") or str(
                        raw_style or "Unknown"
                    )
                    style_counts[style_name] = style_counts.get(style_name, 0) + 1
                    try:
                        outline_level = int(paragraph.OutlineLevel)
                    except Exception:
                        outline_level = int(getattr(paragraph_format, "OutlineLevel", 10))
                    style_key = style_name.casefold()
                    is_heading = 1 <= outline_level <= 9 or style_key.startswith(
                        ("heading", "nagłówek", "başlık")
                    )
                    if is_heading:
                        heading_count += 1
                    keep_with_next = self._word_property_true(
                        getattr(paragraph_format, "KeepWithNext", False)
                    )
                    keep_together = self._word_property_true(
                        getattr(paragraph_format, "KeepTogether", False)
                    )
                    page_break_before = self._word_property_true(
                        getattr(paragraph_format, "PageBreakBefore", False)
                    )
                    widow_control = self._word_property_true(
                        getattr(paragraph_format, "WidowControl", True)
                    )
                    if keep_with_next:
                        if keep_chain_length == 0:
                            keep_chain_start = index
                        keep_chain_length += 1
                    else:
                        flush_keep_chain()
                    if not visible_text.strip():
                        if empty_chain_length == 0:
                            empty_chain_start = index
                        empty_chain_length += 1
                    else:
                        flush_empty_chain()
                    if is_heading and len(visible_text) > long_heading_chars:
                        add_issue(
                            {
                                "type": "long_heading",
                                "severity": "medium",
                                "paragraph": index,
                                "style": style_name,
                                "outline_level": outline_level,
                                "text_length": len(visible_text),
                            }
                        )
                    if page_break_before and not is_heading and index > 1:
                        add_issue(
                            {
                                "type": "page_break_before_body",
                                "severity": "medium",
                                "paragraph": index,
                                "style": style_name,
                            }
                        )
                    if keep_together and len(visible_text) > long_keep_together_chars:
                        add_issue(
                            {
                                "type": "long_keep_together_paragraph",
                                "severity": "high",
                                "paragraph": index,
                                "style": style_name,
                                "text_length": len(visible_text),
                            }
                        )
                    if not widow_control and len(visible_text) >= 200 and not is_heading:
                        add_issue(
                            {
                                "type": "widow_control_disabled",
                                "severity": "low",
                                "paragraph": index,
                                "style": style_name,
                                "text_length": len(visible_text),
                            }
                        )
                except Exception:
                    read_errors += 1
            flush_keep_chain()
            flush_empty_chain()
            if manual_page_breaks:
                add_issue(
                    {
                        "type": "manual_page_breaks",
                        "severity": "info",
                        "count": manual_page_breaks,
                    }
                )
            if scan_count >= 10 and heading_count / scan_count > 0.5:
                add_issue(
                    {
                        "type": "heading_style_overuse",
                        "severity": "medium",
                        "heading_paragraphs": heading_count,
                        "scanned_paragraphs": scan_count,
                        "ratio": round(heading_count / scan_count, 4),
                    }
                )
            sorted_styles = sorted(
                style_counts.items(),
                key=lambda item: (-item[1], item[0].casefold()),
            )
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "total_paragraphs": total,
                "scanned_paragraphs": scan_count,
                "scan_truncated": scan_count < total,
                "issue_count": len(issues),
                "issues_truncated": dropped_issues > 0,
                "dropped_issue_count": dropped_issues,
                "issues": issues,
                "style_summary": dict(sorted_styles[:100]),
                "style_summary_truncated": len(sorted_styles) > 100,
                "heading_paragraphs": heading_count,
                "manual_page_breaks": manual_page_breaks,
                "paragraph_read_errors": read_errors,
                "content_returned": False,
                "document": self._document_info(application, document),
                "checks": [
                    "keep_with_next_chain",
                    "long_heading",
                    "page_break_before_body",
                    "long_keep_together_paragraph",
                    "widow_control_disabled",
                    "consecutive_empty_paragraphs",
                    "manual_page_breaks",
                    "heading_style_overuse",
                ],
                "performance": {
                    "com_attachments": 1,
                    "paragraph_reads": scan_count,
                    "bounded": True,
                },
            }

        return cast(dict[str, Any], self._execute(operation))

    @staticmethod
    def _undo_entries(application: Any, *, max_entries: int) -> tuple[list[str], bool]:
        try:
            control = application.CommandBars.FindControl(Type=6, Id=128)
            if control is None:
                return [], False
            total = max(0, int(control.ListCount))
            entries = [
                str(control.List(index) or "")[:512]
                for index in range(1, min(total, max_entries) + 1)
            ]
            return entries, True
        except Exception:
            return [], False

    def _undo_token(self, record: LiveWordRecord, top_entry: str) -> str:
        payload = "\0".join(
            (
                "undo",
                record.document_id,
                str(record.version),
                top_entry,
            )
        )
        return hmac.new(self._token_secret, payload.encode("utf-8"), hashlib.sha256).hexdigest()

    def inspect_undo(
        self,
        owner: str,
        document_id: str,
        *,
        max_entries: int = 20,
    ) -> dict[str, Any]:
        if (
            isinstance(max_entries, bool)
            or not isinstance(max_entries, int)
            or not 1 <= max_entries <= 50
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "max_entries must be between 1 and 50",
            )
        record = self._record(owner, document_id)

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            self._require_active(application, document)
            entries, available = self._undo_entries(application, max_entries=max_entries)
            top_entry = entries[0] if entries else ""
            barrier_active = record.undo_barrier_version == record.version
            eligible = available and not barrier_active and top_entry.startswith("WordToolkit:")
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "available": available,
                "entries": entries,
                "returned_count": len(entries),
                "top_entry": top_entry,
                "wordtoolkit_undo_eligible": eligible,
                "undo_token": self._undo_token(record, top_entry) if eligible else "",
                "undo_barrier_active": barrier_active,
                "policy": {
                    "only_top_entry": True,
                    "wordtoolkit_prefix_required": True,
                    "raw_times_allowed": False,
                    "fresh_token_required": True,
                    "fails_closed_when_history_unavailable": True,
                },
                "document": self._document_info(application, document),
            }

        return cast(dict[str, Any], self._execute(operation))

    def undo_operation(
        self,
        owner: str,
        document_id: str,
        *,
        undo_token: str,
        expected_version: int | None,
    ) -> dict[str, Any]:
        if expected_version is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "expected_version is required for guarded WordToolkit Undo",
            )
        if not isinstance(undo_token, str) or not undo_token:
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "A fresh undo_token is required",
                retryable=True,
            )
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._require_active(application, document)
            if record.undo_barrier_version == record.version:
                raise WordToolkitError(
                    ErrorCode.AUTH_FORBIDDEN,
                    "The latest WordToolkit mutation is a verified property change without a Word Undo entry",
                    {"live_version": record.version},
                )
            entries, available = self._undo_entries(application, max_entries=1)
            if not available:
                raise WordToolkitError(
                    ErrorCode.LIVE_WORD_UNAVAILABLE,
                    "Word's Undo history is not accessible; guarded Undo fails closed",
                    retryable=True,
                )
            top_entry = entries[0] if entries else ""
            if not top_entry.startswith("WordToolkit:"):
                raise WordToolkitError(
                    ErrorCode.AUTH_FORBIDDEN,
                    "The latest Word action was not created by WordToolkit",
                    {"top_entry": top_entry},
                )
            actual = self._undo_token(record, top_entry)
            if not hmac.compare_digest(actual, undo_token):
                raise WordToolkitError(
                    ErrorCode.VERSION_CONFLICT,
                    "The Word Undo stack changed after inspection",
                    retryable=True,
                )
            undone = bool(document.Undo(1))
            if not undone:
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED,
                    "Word refused to undo the latest WordToolkit operation",
                    {"top_entry": top_entry},
                    retryable=True,
                )
            record.version += 1
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "undone": True,
                "undone_entry": top_entry,
                "document": self._document_info(application, document),
                "policy": {
                    "only_top_entry": True,
                    "wordtoolkit_prefix_verified": True,
                    "token_verified": True,
                    "manual_user_edits_crossed": False,
                },
            }

        return cast(dict[str, Any], self._execute(operation))

    @classmethod
    def _is_active(cls, application: Any, document: Any) -> bool:
        try:
            active_name, active_full_name = cls._document_identity(application.ActiveDocument)
            name, full_name = cls._document_identity(document)
            if full_name and active_full_name:
                return cls._normalize_path(full_name) == cls._normalize_path(active_full_name)
            return bool(name) and name.casefold() == active_name.casefold()
        except Exception:
            return False

    @classmethod
    def _require_active(cls, application: Any, document: Any) -> None:
        if not cls._is_active(application, document):
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "The connected document is not the active Word document",
                retryable=True,
            )

    @classmethod
    def _selection_token(
        cls,
        application: Any,
        document: Any,
        record: LiveWordRecord,
    ) -> str:
        cls._require_active(application, document)
        selection = application.Selection
        start = int(selection.Start)
        end = int(selection.End)
        content_end = max(0, int(document.Content.End) - 1)
        context_start = max(0, start - 64)
        context_end = min(content_end, end + 64)
        context = str(document.Range(context_start, context_end).Text or "")
        window_hwnd = int(application.ActiveWindow.Hwnd)
        payload = "\0".join(
            (
                record.document_id,
                str(record.version),
                str(window_hwnd),
                str(int(selection.Type)),
                str(int(selection.Range.StoryType)),
                str(start),
                str(end),
                context,
            )
        )
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()

    @classmethod
    def _verify_selection(
        cls,
        application: Any,
        document: Any,
        record: LiveWordRecord,
        selection_token: str,
        *,
        replace_selection: bool,
        target: LiveTarget,
    ) -> None:
        if not selection_token:
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "A fresh selection_token is required for cursor or selection edits",
                retryable=True,
            )
        actual = cls._selection_token(application, document, record)
        if not hmac.compare_digest(actual, selection_token):
            raise WordToolkitError(
                ErrorCode.VERSION_CONFLICT,
                "The Word cursor or selection changed before the edit",
                retryable=True,
            )
        selection = application.Selection
        if int(selection.Range.StoryType) != 1:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Word Live editing currently supports only the main document story",
                {"story_type": int(selection.Range.StoryType)},
            )
        if int(selection.Type) not in {1, 2}:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "The current Word selection type is not supported for a live edit",
                {"selection_type": int(selection.Type)},
            )
        if (
            target == "selection"
            and int(selection.Start) != int(selection.End)
            and not replace_selection
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Replacing selected content requires replace_selection=true",
            )
        if "\x07" in str(selection.Range.Text or ""):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "The selection contains a table cell marker and cannot be replaced safely",
            )

    @classmethod
    def _target_range(
        cls,
        application: Any,
        document: Any,
        record: LiveWordRecord,
        target: LiveTarget,
        *,
        selection_token: str,
        replace_selection: bool,
        activate: bool,
    ) -> Any:
        if target in {"selection", "cursor"}:
            cls._verify_selection(
                application,
                document,
                record,
                selection_token,
                replace_selection=replace_selection,
                target=target,
            )
            target_range = application.Selection.Range.Duplicate
            if target == "cursor":
                target_range.Collapse(0)
            return target_range
        if activate:
            document.Activate()
        end = max(0, int(document.Content.End) - 1)
        return document.Range(end, end)

    @staticmethod
    def _ensure_editable(document: Any) -> None:
        if bool(document.ReadOnly):
            raise WordToolkitError(
                ErrorCode.AUTH_FORBIDDEN,
                "The connected Word document is read-only",
            )
        protection_type = int(getattr(document, "ProtectionType", -1))
        if protection_type != -1:
            raise WordToolkitError(
                ErrorCode.AUTH_FORBIDDEN,
                "The connected Word document is protected against editing",
                {"protection_type": protection_type},
            )
        if bool(getattr(document, "Final", False)):
            raise WordToolkitError(
                ErrorCode.AUTH_FORBIDDEN,
                "The connected Word document is marked as final",
            )

    @staticmethod
    def _reject_ambiguous_fraction_coefficient(linear: str) -> None:
        if AMBIGUOUS_FRACTION_COEFFICIENT.search(linear):
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "Ambiguous UnicodeMath fraction coefficient; add an explicit multiplication sign",
                {"example": "Write 1/3·(x^2+1)^(3/2), not 1/3 (x^2+1)^(3/2)"},
            )

    @staticmethod
    def _required_equation_symbols(
        value: str | dict[str, Any],
        linear: str,
    ) -> tuple[tuple[str, ...], ...]:
        source = value if isinstance(value, str) else ""
        groups: list[tuple[str, ...]] = []
        if "\\hbar" in source or "ℏ" in source or "ħ" in source or "ℏ" in linear:
            groups.append(("ℏ", "ħ"))
        if "\\dagger" in source or "†" in source or "†" in linear:
            groups.append(("†",))
        return tuple(groups)

    @classmethod
    def _equation_features(
        cls,
        ast: dict[str, Any],
        linear: str,
        *,
        display: bool,
    ) -> tuple[str, ...]:
        kinds: set[str] = set()

        def visit(node: Any) -> None:
            if not isinstance(node, dict):
                return
            kind = node.get("kind")
            if isinstance(kind, str):
                kinds.add(kind)
            for child in node.get("children", []):
                visit(child)

        visit(ast)
        features: set[str] = {"display" if display else "inline"}
        mappings = {
            "fraction": "fraction",
            "superscript": "power",
            "subscript": "subscript",
            "sub_sup": "subscript_and_power",
            "radical": "radical",
            "nary": "nary_operator",
            "matrix": "matrix",
            "accent": "accent",
            "limit_lower": "limit",
            "limit_upper": "limit",
            "function": "function",
            "delimiter": "structured_delimiter",
        }
        features.update(mapped for kind, mapped in mappings.items() if kind in kinds)
        if "∫" in linear:
            features.add("integral")
        if "ℏ" in linear or "ħ" in linear:
            features.add("hbar")
        if "†" in linear:
            features.add("dagger")
        if "[" in linear and "]" in linear:
            features.add("commutator_or_brackets")
        if any("\u0370" <= character <= "\u03ff" for character in linear):
            features.add("greek")
        if "·" in linear:
            features.add("explicit_multiplication")
        if len(linear) > 200:
            features.add("long_expression")
        return tuple(sorted(features))

    @staticmethod
    def _equation_ast_text(node: Any) -> str:
        if not isinstance(node, dict):
            return ""
        kind = node.get("kind")
        children = node.get("children", [])
        if not isinstance(children, list):
            children = []
        if kind == "delimiter":
            attrs = node.get("attrs", {})
            attrs = attrs if isinstance(attrs, dict) else {}
            begin = str(attrs.get("begin", "("))
            end = str(attrs.get("end", ")"))
            return begin + "".join(LiveWordBridge._equation_ast_text(x) for x in children) + end
        prefix = ""
        if kind == "radical":
            prefix = "√"
        elif kind == "nary":
            prefix = str(node.get("value", "∑") or "∑")
        value = node.get("value", "")
        leaf = str(value) if not children and isinstance(value, str) else ""
        return (
            prefix + leaf + "".join(LiveWordBridge._equation_ast_text(child) for child in children)
        )

    @staticmethod
    def _normalize_equation_contract_text(value: str) -> str:
        translations = {
            "−": "-",
            "‐": "-",
            "‑": "-",
            "ϕ": "φ",
            "ħ": "ℏ",
        }
        normalized: list[str] = []
        for character in value:
            if character.isspace() or character in LIVE_EQUATION_IGNORABLE_CHARACTERS:
                continue
            normalized.append(translations.get(character, character))
        return "".join(normalized)

    @classmethod
    def _equation_structure_contract(cls, node: Any) -> tuple[Any, ...]:
        if not isinstance(node, dict):
            return ()
        kind = node.get("kind")
        children = node.get("children", [])
        if not isinstance(children, list):
            children = []
        if (
            kind == "nary"
            and len(children) == 3
            and not cls._normalize_equation_contract_text(cls._equation_ast_text(children[1]))
            and not cls._normalize_equation_contract_text(cls._equation_ast_text(children[2]))
        ):
            return cls._equation_structure_contract(children[0])
        if kind in LIVE_EQUATION_STRUCTURE_KINDS:
            slots = tuple(
                (
                    cls._normalize_equation_contract_text(cls._equation_ast_text(child)),
                    cls._equation_structure_contract(child),
                )
                for child in children
            )
            return ((str(kind), slots),)
        nested: list[Any] = []
        for child in children:
            nested.extend(cls._equation_structure_contract(child))
        return tuple(nested)

    @staticmethod
    def _equation_structure_counts(contract: tuple[Any, ...]) -> dict[str, int]:
        counts: dict[str, int] = {}

        def visit(nodes: tuple[Any, ...]) -> None:
            for node in nodes:
                if not isinstance(node, tuple) or len(node) != 2:
                    continue
                kind, slots = node
                counts[str(kind)] = counts.get(str(kind), 0) + 1
                if not isinstance(slots, tuple):
                    continue
                for slot in slots:
                    if isinstance(slot, tuple) and len(slot) == 2 and isinstance(slot[1], tuple):
                        visit(slot[1])

        visit(contract)
        return dict(sorted(counts.items()))

    @classmethod
    def _equation_fidelity_contract(cls, ast: dict[str, Any]) -> tuple[str, tuple[Any, ...]]:
        return (
            cls._normalize_equation_contract_text(cls._equation_ast_text(ast)),
            cls._equation_structure_contract(ast),
        )

    def _prepare_equation(
        self,
        value: str | dict[str, Any],
        input_format: EquationInputFormat,
        *,
        display: bool,
        verify_readback: bool,
    ) -> PreparedLiveEquation:
        if input_format == "unicodemath" and isinstance(value, str):
            self._reject_ambiguous_fraction_coefficient(value)
        try:
            linear = str(self.math.convert(value, input_format, "unicodemath", display=display))
            ast = cast(
                dict[str, Any],
                self.math.convert(value, input_format, "ast", display=display),
            )
        except WordToolkitError:
            raise
        except Exception as exc:
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "Equation preflight conversion failed",
                {
                    "input_format": input_format,
                    "exception": type(exc).__name__,
                },
            ) from exc
        if not linear:
            raise WordToolkitError(ErrorCode.EQUATION_INVALID, "Equation input is empty")
        self._reject_ambiguous_fraction_coefficient(linear)

        required_symbols = self._required_equation_symbols(value, linear)
        features = self._equation_features(ast, linear, display=display)
        structural_readback = bool(LIVE_EQUATION_FIDELITY_READBACK_FEATURES.intersection(features))
        learning = self.learning.recommendation(input_format, features)
        rules: list[str] = ["native_omath"]
        warnings: list[str] = []
        if "/" in linear:
            rules.append("fraction_scope")
        if "^" in linear:
            rules.append("power_scope")
        if "√" in linear:
            rules.append("radical_scope")
        if "∫" in linear:
            rules.append("integral_scope")
        if "·" in linear:
            rules.append("explicit_multiplication")
        if required_symbols:
            rules.append("advanced_symbol_preservation")
            warnings.append(
                "Advanced symbols require native Word readback and symbol-preservation checks."
            )
        if structural_readback:
            rules.append("structural_fidelity_readback")
            warnings.append(
                "Structured notation requires native OMML readback and fidelity comparison."
            )
        if input_format == "unicodemath" and required_symbols:
            warnings.append(
                "For hbar or dagger notation, LaTeX input is safer than direct UnicodeMath."
            )
        if learning["force_live_readback"]:
            rules.append("learned_live_readback")
            warnings.append(
                "Past local outcomes for this structural equation class force native readback."
            )
        if learning["preferred_input_format"] != input_format:
            warnings.append(
                "Local outcomes favor "
                f"{learning['preferred_input_format']} for this structural equation class."
            )

        return PreparedLiveEquation(
            linear=linear,
            display=display,
            input_format=input_format,
            verify_readback=verify_readback
            or bool(required_symbols)
            or structural_readback
            or bool(learning["force_live_readback"]),
            required_symbol_groups=required_symbols,
            rules=tuple(rules),
            warnings=tuple(warnings),
            ast=ast,
            features=features,
            learning=learning,
        )

    def _prepare_equation_item(
        self,
        item: dict[str, Any],
        *,
        verify_readback_default: bool,
    ) -> PreparedLiveEquation:
        value = item.get("value")
        if not isinstance(value, (str, dict)):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Each equation requires a string or object value",
            )
        input_format = item.get("input_format", "latex")
        if input_format not in {"latex", "unicodemath", "mathml", "omml", "ast"}:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Unsupported equation input format",
                {"input_format": input_format},
            )
        return self._prepare_equation(
            value,
            cast(EquationInputFormat, input_format),
            display=bool(item.get("display", True)),
            verify_readback=bool(item.get("verify_readback", verify_readback_default)),
        )

    def preflight_equations(self, equations: list[dict[str, Any]]) -> dict[str, Any]:
        """Check a live-equation batch without touching Microsoft Word."""
        self._require_available()
        if not equations:
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "equations must not be empty")
        if len(equations) > 200:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "At most 200 equations may be checked in one preflight",
            )

        results: list[dict[str, Any]] = []
        for index, item in enumerate(equations):
            if not isinstance(item, dict):
                results.append(
                    {
                        "index": index,
                        "valid": False,
                        "error": {
                            "code": ErrorCode.INVALID_INPUT.value,
                            "message": "Each equation must be an object",
                        },
                    }
                )
                continue
            try:
                prepared = self._prepare_equation_item(
                    item,
                    verify_readback_default=False,
                )
                results.append(
                    {
                        "index": index,
                        "valid": True,
                        "input_format": prepared.input_format,
                        "display": prepared.display,
                        "linear_input": prepared.linear,
                        "ast": prepared.ast,
                        "rules": list(prepared.rules),
                        "warnings": list(prepared.warnings),
                        "features": list(prepared.features),
                        "learning": prepared.learning,
                        "requires_live_readback": prepared.verify_readback,
                    }
                )
            except WordToolkitError as exc:
                results.append(
                    {
                        "index": index,
                        "valid": False,
                        "error": {
                            "code": exc.code.value,
                            "message": exc.message,
                            "details": exc.details or {},
                        },
                    }
                )

        invalid = sum(not item["valid"] for item in results)
        return {
            "valid": invalid == 0,
            "equation_count": len(results),
            "valid_count": len(results) - invalid,
            "invalid_count": invalid,
            "equations": results,
            "mutated_word": False,
        }

    @staticmethod
    def _normalize_text_formatting(
        value: Any,
        *,
        allow_paragraph_formatting: bool = True,
    ) -> dict[str, Any]:
        if value is None:
            value = {}
        if not isinstance(value, dict):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "formatting must be an object",
            )
        value = dict(value)
        for alias, canonical in TEXT_FORMATTING_ALIASES.items():
            if alias not in value:
                continue
            if canonical in value:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"Use either {alias} or {canonical}, not both",
                )
            value[canonical] = value.pop(alias)
        allowed_fields = (
            TEXT_FORMATTING_KEYS if allow_paragraph_formatting else INLINE_RUN_FORMATTING_KEYS
        )
        unknown = sorted(set(value) - allowed_fields)
        if unknown:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                (
                    "Unsupported live text formatting fields"
                    if allow_paragraph_formatting
                    else "Unsupported inline run formatting fields"
                ),
                {"fields": unknown},
            )
        normalized: dict[str, Any] = {}
        for name in (
            "font_name",
            "font_name_ascii",
            "font_name_bidi",
            "font_name_far_east",
            "font_name_other",
        ):
            if name not in value:
                continue
            font_name = value[name]
            if not isinstance(font_name, str) or not font_name.strip() or len(font_name) > 128:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} must be a non-empty string of at most 128 characters",
                )
            normalized[name] = font_name.strip()
        if "font_color_rgb" in value:
            color = value["font_color_rgb"]
            if not isinstance(color, str) or not re.fullmatch(r"#[0-9A-Fa-f]{6}", color):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "font_color_rgb must use #RRGGBB",
                )
            normalized["font_color_rgb"] = color.upper()
        for name in {
            "bold",
            "italic",
            "bold_bidi",
            "italic_bidi",
            "underline",
            "all_caps",
            "small_caps",
            "strike",
            "double_strike",
            "subscript",
            "superscript",
            "hidden",
            "shadow",
            "outline",
            "emboss",
            "engrave",
            "disable_character_space_grid",
            "contextual_alternates",
            "clear_character_formatting",
            "keep_with_next",
            "keep_together",
            "page_break_before",
            "widow_control",
        }:
            if name in value:
                if not isinstance(value[name], bool):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        f"{name} must be true or false",
                    )
                normalized[name] = value[name]
        if normalized.get("strike") is True and normalized.get("double_strike") is True:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "strike and double_strike cannot both be true because Microsoft Word preserves only one strike mode",
            )
        enum_fields = {
            "underline_style": WORD_UNDERLINE_STYLES,
            "emphasis_mark": WORD_EMPHASIS_MARKS,
            "ligatures": WORD_LIGATURES,
            "number_form": WORD_NUMBER_FORMS,
            "number_spacing": WORD_NUMBER_SPACING,
        }
        for name, allowed in enum_fields.items():
            if name not in value:
                continue
            selected = value[name]
            if not isinstance(selected, str) or selected not in allowed:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} is not a supported Microsoft Word value",
                    {"allowed": list(allowed)},
                )
            normalized[name] = selected
        if "underline_style" in value and "underline" in value:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Use either deprecated underline or canonical underline_style, not both",
            )
        if value.get("emboss") is True and value.get("engrave") is True:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "emboss and engrave cannot both be true",
            )
        if value.get("subscript") is True and value.get("superscript") is True:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "subscript and superscript cannot both be true",
            )
        if "stylistic_sets" in value:
            stylistic_sets = value["stylistic_sets"]
            if (
                not isinstance(stylistic_sets, list)
                or len(stylistic_sets) > 20
                or any(
                    isinstance(item, bool) or not isinstance(item, int) for item in stylistic_sets
                )
                or any(not 1 <= item <= 20 for item in stylistic_sets)
                or len(set(stylistic_sets)) != len(stylistic_sets)
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "stylistic_sets must contain unique integers from 1 to 20",
                )
            normalized["stylistic_sets"] = list(stylistic_sets)
        for name in ("font_color_index", "font_color_bidi_index"):
            if name not in value:
                continue
            color_index = value[name]
            if (
                isinstance(color_index, bool)
                or not isinstance(color_index, int)
                or not 0 <= color_index <= 16
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} must be 0 (automatic) or an integer from 1 to 16",
                )
            normalized[name] = color_index
        for name in ("diacritic_color", "underline_color"):
            if name not in value:
                continue
            color = value[name]
            if not isinstance(color, str) or (
                color != "automatic" and not re.fullmatch(r"#[0-9A-Fa-f]{6}", color)
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} must be automatic or use #RRGGBB",
                )
            normalized[name] = color if color == "automatic" else color.upper()
        if "position_pt" in value and (
            value.get("subscript") is True or value.get("superscript") is True
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "position_pt cannot be combined with an enabled subscript or superscript",
            )
        if "font_color_rgb" in value and "font_color_index" in value:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Use either font_color_rgb or font_color_index, not both",
            )
        numeric_ranges = {
            "font_size_pt": (1.0, 1638.0),
            "font_size_bidi_pt": (1.0, 1638.0),
            "scaling_percent": (1.0, 600.0),
            "spacing_pt": (-1584.0, 1584.0),
            "position_pt": (-1584.0, 1584.0),
            "kerning_pt": (0.0, 1638.0),
            "space_before_pt": (0.0, 1584.0),
            "space_after_pt": (0.0, 1584.0),
            "left_indent_pt": (-1584.0, 1584.0),
            "right_indent_pt": (-1584.0, 1584.0),
            "first_line_indent_pt": (-1584.0, 1584.0),
        }
        for name, (minimum, maximum) in numeric_ranges.items():
            if name not in value:
                continue
            number = value[name]
            if isinstance(number, bool) or not isinstance(number, (int, float)):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} must be a number",
                )
            number = float(number)
            if name in {"scaling_percent", "position_pt"} and not number.is_integer():
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} must be an integer",
                )
            if not minimum <= number <= maximum:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} is outside the supported Word point range",
                    {"minimum": minimum, "maximum": maximum},
                )
            normalized[name] = int(number) if name in {"scaling_percent", "position_pt"} else number
        if "highlight_color_index" in value:
            highlight = value["highlight_color_index"]
            if (
                isinstance(highlight, bool)
                or not isinstance(highlight, int)
                or not 0 <= highlight <= 16
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "highlight_color_index must be an integer from 0 to 16",
                )
            normalized["highlight_color_index"] = highlight
        if "paragraph_alignment" in value:
            alignment = value["paragraph_alignment"]
            if not isinstance(alignment, str) or alignment not in PARAGRAPH_ALIGNMENT:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "paragraph_alignment must be left, center, right, justify or distribute",
                )
            normalized["paragraph_alignment"] = alignment
        return normalized

    @staticmethod
    def _word_rgb(value: str) -> int:
        red = int(value[1:3], 16)
        green = int(value[3:5], 16)
        blue = int(value[5:7], 16)
        return red | (green << 8) | (blue << 16)

    @classmethod
    def _word_color(cls, value: str) -> int:
        return WORD_AUTOMATIC_COLOR if value == "automatic" else cls._word_rgb(value)

    @staticmethod
    def _word_rgb_text(value: int) -> str:
        red = value & 0xFF
        green = (value >> 8) & 0xFF
        blue = (value >> 16) & 0xFF
        return f"#{red:02X}{green:02X}{blue:02X}"

    @classmethod
    def _apply_text_formatting(
        cls,
        target_range: Any,
        formatting: dict[str, Any],
    ) -> dict[str, Any]:
        font = target_range.Font
        if formatting.get("clear_character_formatting"):
            font.Reset()
            target_range.HighlightColorIndex = 0
        paragraph = target_range.ParagraphFormat
        direct_font = {
            "font_name": "Name",
            "font_name_ascii": "NameAscii",
            "font_name_bidi": "NameBi",
            "font_name_far_east": "NameFarEast",
            "font_name_other": "NameOther",
            "font_size_pt": "Size",
            "font_size_bidi_pt": "SizeBi",
            "font_color_index": "ColorIndex",
            "font_color_bidi_index": "ColorIndexBi",
            "scaling_percent": "Scaling",
            "spacing_pt": "Spacing",
            "position_pt": "Position",
            "kerning_pt": "Kerning",
        }
        for source, target in direct_font.items():
            if source in formatting:
                setattr(font, target, formatting[source])
        if "font_color_rgb" in formatting:
            font.Color = cls._word_rgb(formatting["font_color_rgb"])
        if "diacritic_color" in formatting:
            font.DiacriticColor = cls._word_color(formatting["diacritic_color"])
        if "underline_color" in formatting:
            font.UnderlineColor = cls._word_color(formatting["underline_color"])
        if "highlight_color_index" in formatting:
            target_range.HighlightColorIndex = formatting["highlight_color_index"]
        boolean_font = {
            "bold": "Bold",
            "italic": "Italic",
            "bold_bidi": "BoldBi",
            "italic_bidi": "ItalicBi",
            "all_caps": "AllCaps",
            "small_caps": "SmallCaps",
            "strike": "StrikeThrough",
            "double_strike": "DoubleStrikeThrough",
            "subscript": "Subscript",
            "superscript": "Superscript",
            "hidden": "Hidden",
            "shadow": "Shadow",
            "outline": "Outline",
            "emboss": "Emboss",
            "engrave": "Engrave",
            "disable_character_space_grid": "DisableCharacterSpaceGrid",
            "contextual_alternates": "ContextualAlternates",
        }
        for source, target in boolean_font.items():
            if source in formatting:
                setattr(font, target, -1 if formatting[source] else 0)
        if "underline" in formatting:
            font.Underline = 1 if formatting["underline"] else 0
        enum_font = {
            "underline_style": ("Underline", WORD_UNDERLINE_STYLES),
            "emphasis_mark": ("EmphasisMark", WORD_EMPHASIS_MARKS),
            "ligatures": ("Ligatures", WORD_LIGATURES),
            "number_form": ("NumberForm", WORD_NUMBER_FORMS),
            "number_spacing": ("NumberSpacing", WORD_NUMBER_SPACING),
        }
        for source, (target, values) in enum_font.items():
            if source in formatting:
                setattr(font, target, values[formatting[source]])
        if "stylistic_sets" in formatting:
            font.StylisticSet = sum(1 << (item - 1) for item in formatting["stylistic_sets"])

        direct_paragraph = {
            "space_before_pt": "SpaceBefore",
            "space_after_pt": "SpaceAfter",
            "left_indent_pt": "LeftIndent",
            "right_indent_pt": "RightIndent",
            "first_line_indent_pt": "FirstLineIndent",
        }
        for source, target in direct_paragraph.items():
            if source in formatting:
                setattr(paragraph, target, formatting[source])
        if "paragraph_alignment" in formatting:
            paragraph.Alignment = PARAGRAPH_ALIGNMENT[formatting["paragraph_alignment"]]
        boolean_paragraph = {
            "keep_with_next": "KeepWithNext",
            "keep_together": "KeepTogether",
            "page_break_before": "PageBreakBefore",
            "widow_control": "WidowControl",
        }
        for source, target in boolean_paragraph.items():
            if source in formatting:
                setattr(paragraph, target, -1 if formatting[source] else 0)
        readback = cls._capture_text_formatting(target_range, formatting)
        for field, expected in formatting.items():
            if field == "clear_character_formatting":
                continue
            actual = readback.get(field)
            if not cls._formatting_values_equal(expected, actual):
                raise WordToolkitError(
                    ErrorCode.FORMATTING_INVALID,
                    "Microsoft Word did not retain the requested formatting",
                    {"field": field, "expected": expected, "actual": actual},
                )
        return readback

    @classmethod
    def _capture_text_formatting(
        cls,
        target_range: Any,
        formatting: dict[str, Any],
    ) -> dict[str, Any]:
        font = target_range.Font
        paragraph = target_range.ParagraphFormat
        direct_font = {
            "font_name": "Name",
            "font_name_ascii": "NameAscii",
            "font_name_bidi": "NameBi",
            "font_name_far_east": "NameFarEast",
            "font_name_other": "NameOther",
            "font_size_pt": "Size",
            "font_size_bidi_pt": "SizeBi",
            "font_color_index": "ColorIndex",
            "font_color_bidi_index": "ColorIndexBi",
            "scaling_percent": "Scaling",
            "spacing_pt": "Spacing",
            "position_pt": "Position",
            "kerning_pt": "Kerning",
        }
        boolean_font = {
            "bold": "Bold",
            "italic": "Italic",
            "bold_bidi": "BoldBi",
            "italic_bidi": "ItalicBi",
            "all_caps": "AllCaps",
            "small_caps": "SmallCaps",
            "strike": "StrikeThrough",
            "double_strike": "DoubleStrikeThrough",
            "subscript": "Subscript",
            "superscript": "Superscript",
            "hidden": "Hidden",
            "shadow": "Shadow",
            "outline": "Outline",
            "emboss": "Emboss",
            "engrave": "Engrave",
            "disable_character_space_grid": "DisableCharacterSpaceGrid",
            "contextual_alternates": "ContextualAlternates",
        }
        enum_font = {
            "underline_style": ("Underline", WORD_UNDERLINE_STYLES),
            "emphasis_mark": ("EmphasisMark", WORD_EMPHASIS_MARKS),
            "ligatures": ("Ligatures", WORD_LIGATURES),
            "number_form": ("NumberForm", WORD_NUMBER_FORMS),
            "number_spacing": ("NumberSpacing", WORD_NUMBER_SPACING),
        }
        direct_paragraph = {
            "space_before_pt": "SpaceBefore",
            "space_after_pt": "SpaceAfter",
            "left_indent_pt": "LeftIndent",
            "right_indent_pt": "RightIndent",
            "first_line_indent_pt": "FirstLineIndent",
        }
        boolean_paragraph = {
            "keep_with_next": "KeepWithNext",
            "keep_together": "KeepTogether",
            "page_break_before": "PageBreakBefore",
            "widow_control": "WidowControl",
        }
        readback: dict[str, Any] = {}
        for field in formatting:
            if field in direct_font:
                readback[field] = getattr(font, direct_font[field])
            elif field == "font_color_rgb":
                readback[field] = cls._word_rgb_text(int(font.Color))
            elif field in {"diacritic_color", "underline_color"}:
                word_color = int(
                    getattr(
                        font, "DiacriticColor" if field == "diacritic_color" else "UnderlineColor"
                    )
                )
                readback[field] = (
                    "automatic"
                    if word_color == WORD_AUTOMATIC_COLOR
                    else cls._word_rgb_text(word_color)
                )
            elif field in boolean_font:
                readback[field] = cls._word_boolean(getattr(font, boolean_font[field]))
            elif field == "underline":
                underline = int(font.Underline)
                readback[field] = True if underline == 1 else False if underline == 0 else underline
            elif field in enum_font:
                property_name, values = enum_font[field]
                actual = int(getattr(font, property_name))
                readback[field] = next(
                    (name for name, word_value in values.items() if word_value == actual),
                    actual,
                )
            elif field == "stylistic_sets":
                mask = int(font.StylisticSet)
                readback[field] = [index for index in range(1, 21) if mask & (1 << (index - 1))]
            elif field == "highlight_color_index":
                readback[field] = int(target_range.HighlightColorIndex)
            elif field in direct_paragraph:
                readback[field] = getattr(paragraph, direct_paragraph[field])
            elif field == "paragraph_alignment":
                alignment = int(paragraph.Alignment)
                readback[field] = next(
                    (
                        name
                        for name, word_value in PARAGRAPH_ALIGNMENT.items()
                        if word_value == alignment
                    ),
                    alignment,
                )
            elif field in boolean_paragraph:
                readback[field] = cls._word_boolean(getattr(paragraph, boolean_paragraph[field]))
            elif field == "clear_character_formatting":
                readback[field] = bool(formatting[field])
        return readback

    @staticmethod
    def _word_boolean(value: Any) -> bool | int:
        integer = int(value)
        return True if integer == -1 else False if integer == 0 else integer

    @staticmethod
    def _formatting_values_equal(expected: Any, actual: Any) -> bool:
        if isinstance(expected, bool) or isinstance(actual, bool):
            return type(expected) is type(actual) and expected == actual
        if isinstance(expected, (int, float)) and isinstance(actual, (int, float)):
            return abs(float(expected) - float(actual)) <= 0.001
        return expected == actual

    @staticmethod
    def _paragraph_payload(
        document: Any,
        start: int,
        text: str,
        *,
        as_new_paragraph: bool,
    ) -> tuple[str, int, int]:
        normalized = text.replace("\r\n", "\n").replace("\r", "\n").replace("\n", "\r")
        prefix = ""
        suffix = ""
        if as_new_paragraph:
            previous = str(document.Range(start - 1, start).Text or "") if start > 0 else ""
            prefix = "" if start == 0 or previous == "\r" else "\r"
            suffix = "\r"
        return prefix + normalized + suffix, len(prefix), len(suffix)

    @staticmethod
    def _show_range(application: Any, target_range: Any, caret: int) -> None:
        with suppress(Exception):
            application.Selection.SetRange(caret, caret)
            application.ActiveWindow.ScrollIntoView(target_range, True)

    @contextmanager
    def _undoable(self, application: Any, document: Any, label: str) -> Iterator[None]:
        undo_record = application.UndoRecord
        started = False
        try:
            undo_record.StartCustomRecord(label)
            started = True
            yield
        except Exception:
            if started:
                with suppress(Exception):
                    undo_record.EndCustomRecord()
                started = False
                with suppress(Exception):
                    document.Undo(1)
            raise
        else:
            if started:
                try:
                    undo_record.EndCustomRecord()
                    started = False
                except Exception:
                    with suppress(Exception):
                        undo_record.EndCustomRecord()
                    started = False
                    with suppress(Exception):
                        document.Undo(1)
                    raise
        finally:
            if started:
                with suppress(Exception):
                    undo_record.EndCustomRecord()

    @staticmethod
    @contextmanager
    def _screen_updates_suspended(application: Any, *, enabled: bool) -> Iterator[None]:
        if not enabled:
            yield
            return
        original: bool | None = None
        try:
            original = bool(application.ScreenUpdating)
            application.ScreenUpdating = False
        except Exception:
            original = None
        try:
            yield
        finally:
            if original is not None:
                with suppress(Exception):
                    application.ScreenUpdating = original

    @staticmethod
    def _learning_error_code(error: WordToolkitError) -> str:
        if "dropped a required advanced equation symbol" in error.message:
            return "ADVANCED_SYMBOL_DROPPED"
        if "changed equation text or structure" in error.message:
            return "NATIVE_FIDELITY_MISMATCH"
        return error.code.value

    def _record_equation_learning(
        self,
        equations: list[PreparedLiveEquation],
        *,
        success: bool,
        duration_ms: float,
        error: WordToolkitError | None = None,
    ) -> bool:
        if not equations:
            return False
        if error is not None and error.code not in {
            ErrorCode.EQUATION_INVALID,
            ErrorCode.EXTERNAL_TOOL_FAILED,
        }:
            return False
        per_equation_duration = duration_ms / len(equations)
        outcomes = [
            {
                "input_format": equation.input_format,
                "features": equation.features,
                "success": success,
                "readback_verified": success and equation.verify_readback,
                "duration_ms": per_equation_duration,
                "error_code": self._learning_error_code(error) if error else "",
            }
            for equation in equations
        ]
        try:
            self.learning.record_many(outcomes)
            return True
        except (OSError, TypeError, ValueError):
            return False

    def insert_text(
        self,
        owner: str,
        document_id: str,
        *,
        text: str,
        target: LiveTarget,
        as_new_paragraph: bool,
        style: str,
        formatting: dict[str, Any] | None = None,
        selection_token: str = "",
        replace_selection: bool = False,
        activate: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        if not text:
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "Text must not be empty")
        normalized_formatting = self._normalize_text_formatting(formatting)
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            target_range = self._target_range(
                application,
                document,
                record,
                target,
                selection_token=selection_token,
                replace_selection=replace_selection,
                activate=activate,
            )
            start = int(target_range.Start)
            payload, prefix_length, suffix_length = self._paragraph_payload(
                document,
                start,
                text,
                as_new_paragraph=as_new_paragraph,
            )
            formatting_readback: dict[str, Any] = {}
            with self._undoable(application, document, "WordToolkit: insert text"):
                target_range.Text = payload
                inserted_start = start + prefix_length
                inserted_end = int(target_range.End) - suffix_length
                inserted = document.Range(inserted_start, inserted_end)
                if style:
                    inserted.Style = style
                if normalized_formatting:
                    formatting_readback = self._apply_text_formatting(
                        inserted,
                        normalized_formatting,
                    )
            record.version += 1
            caret = int(target_range.End)
            self._show_range(application, inserted, caret)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "inserted_range": {"start": inserted_start, "end": inserted_end},
                "formatting": normalized_formatting,
                "native_formatting_verified": bool(normalized_formatting),
                "formatting_readback": formatting_readback,
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    def format_selection(
        self,
        owner: str,
        document_id: str,
        *,
        selection_token: str,
        style: str = "",
        formatting: dict[str, Any] | None = None,
        optimize_screen_updates: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        if not isinstance(style, str) or len(style) > 128:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "style must be a string of at most 128 characters",
            )
        normalized = self._normalize_text_formatting(formatting)
        if not style and not normalized:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Provide a style or at least one formatting field",
            )
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            self._require_active(application, document)
            if not selection_token:
                raise WordToolkitError(
                    ErrorCode.VERSION_CONFLICT,
                    "A fresh selection_token is required for live formatting",
                    retryable=True,
                )
            actual = self._selection_token(application, document, record)
            if not hmac.compare_digest(actual, selection_token):
                raise WordToolkitError(
                    ErrorCode.VERSION_CONFLICT,
                    "The Word selection changed before formatting",
                    retryable=True,
                )
            selection = application.Selection
            target_range = selection.Range.Duplicate
            start = int(target_range.Start)
            end = int(target_range.End)
            if start == end:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Live formatting requires a non-empty Word selection",
                )
            if int(target_range.StoryType) != 1:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Live formatting currently supports only the main document story",
                    {"story_type": int(target_range.StoryType)},
                )
            formatting_readback: dict[str, Any] = {}
            with (
                self._screen_updates_suspended(
                    application,
                    enabled=optimize_screen_updates,
                ),
                self._undoable(
                    application,
                    document,
                    "WordToolkit: format live selection",
                ),
            ):
                if style:
                    target_range.Style = style
                if normalized:
                    formatting_readback = self._apply_text_formatting(
                        target_range,
                        normalized,
                    )
            record.version += 1
            with suppress(Exception):
                application.ActiveWindow.ScrollIntoView(target_range, True)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "formatted_range": {"start": start, "end": end},
                "selection_type": int(selection.Type),
                "story_type": int(target_range.StoryType),
                "style": style,
                "formatting": normalized,
                "native_formatting_verified": bool(normalized),
                "formatting_readback": formatting_readback,
                "screen_updates_suspended": optimize_screen_updates,
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    @staticmethod
    def _prepare_table_rows(rows: list[list[str]]) -> tuple[list[list[str]], int]:
        if not isinstance(rows, list) or not rows:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "rows must be a non-empty array",
            )
        if len(rows) > 200:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "A live table may contain at most 200 rows",
            )
        column_count: int | None = None
        prepared: list[list[str]] = []
        total_characters = 0
        for row_index, row in enumerate(rows):
            if not isinstance(row, list) or not row:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Each live table row must be a non-empty array",
                    {"row": row_index},
                )
            if column_count is None:
                column_count = len(row)
                if column_count > 50:
                    raise WordToolkitError(
                        ErrorCode.LIMIT_EXCEEDED,
                        "A live table may contain at most 50 columns",
                    )
            elif len(row) != column_count:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "All live table rows must have the same number of columns",
                    {
                        "row": row_index,
                        "expected": column_count,
                        "actual": len(row),
                    },
                )
            prepared_row: list[str] = []
            for column_index, cell in enumerate(row):
                if not isinstance(cell, str):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "Every live table cell must be a string",
                        {"row": row_index, "column": column_index},
                    )
                if "\t" in cell or "\x07" in cell:
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "Live table cells cannot contain tab or Word cell-marker characters",
                        {"row": row_index, "column": column_index},
                    )
                normalized = cell.replace("\r\n", "\n").replace("\r", "\n").replace("\n", "\v")
                total_characters += len(normalized)
                if len(normalized) > 50_000:
                    raise WordToolkitError(
                        ErrorCode.LIMIT_EXCEEDED,
                        "One live table cell exceeds 50,000 characters",
                        {"row": row_index, "column": column_index},
                    )
                prepared_row.append(normalized)
            prepared.append(prepared_row)
        if len(prepared) * int(column_count or 0) > 5_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "A live table may contain at most 5,000 cells",
            )
        if total_characters > 500_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "Live table text exceeds 500,000 characters",
            )
        return prepared, int(column_count or 0)

    @staticmethod
    def _word_table_column_name(column: int) -> str:
        name = ""
        current = column
        while current:
            current, remainder = divmod(current - 1, 26)
            name = chr(65 + remainder) + name
        return name

    @classmethod
    def _prepare_table_formula_item(
        cls,
        item: dict[str, Any],
        index: int,
    ) -> PreparedLiveTableFormula:
        if not isinstance(item, dict):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Each live table formula must be an object",
                {"formula": index},
            )
        allowed = {
            "row",
            "column",
            "function",
            "directions",
            "cell_range",
            "numeric_format",
            "replace_existing",
        }
        unknown = sorted(set(item) - allowed)
        if unknown:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Unsupported live table formula arguments",
                {"formula": index, "arguments": unknown},
            )

        def coordinate(name: str, maximum: int, source: dict[str, Any] = item) -> int:
            value = source.get(name)
            if isinstance(value, bool) or not isinstance(value, int) or not 1 <= value <= maximum:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} must be an integer from 1 to {maximum}",
                    {"formula": index},
                )
            return value

        row = coordinate("row", 200)
        column = coordinate("column", 50)
        function_value = item.get("function")
        if (
            not isinstance(function_value, str)
            or function_value not in WORD_TABLE_FORMULA_FUNCTIONS
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Unsupported live table formula function",
                {
                    "formula": index,
                    "allowed": sorted(WORD_TABLE_FORMULA_FUNCTIONS),
                },
            )
        function = cast(TableFormulaFunction, function_value)

        directions_value = item.get("directions")
        cell_range_value = item.get("cell_range")
        if (directions_value is None) == (cell_range_value is None):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Provide exactly one of directions or cell_range",
                {"formula": index},
            )

        directions: tuple[TableFormulaDirection, ...] = ()
        range_start: tuple[int, int] | None = None
        range_end: tuple[int, int] | None = None
        if directions_value is not None:
            if not isinstance(directions_value, list) or not 1 <= len(directions_value) <= 2:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "directions must contain one or two positional directions",
                    {"formula": index},
                )
            if any(
                not isinstance(value, str) or value not in WORD_TABLE_FORMULA_DIRECTIONS
                for value in directions_value
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Unsupported live table formula direction",
                    {
                        "formula": index,
                        "allowed": sorted(WORD_TABLE_FORMULA_DIRECTIONS),
                    },
                )
            if len(set(directions_value)) != len(directions_value):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "directions must be unique",
                    {"formula": index},
                )
            directions = tuple(cast(list[TableFormulaDirection], directions_value))
            operands = ",".join(
                WORD_TABLE_FORMULA_DIRECTIONS[direction] for direction in directions
            )
        else:
            if not isinstance(cell_range_value, dict) or set(cell_range_value) != {
                "start",
                "end",
            }:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "cell_range must contain only start and end coordinates",
                    {"formula": index},
                )
            start_value = cell_range_value["start"]
            end_value = cell_range_value["end"]
            if (
                not isinstance(start_value, dict)
                or set(start_value) != {"row", "column"}
                or not isinstance(end_value, dict)
                or set(end_value) != {"row", "column"}
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "cell_range start and end must contain row and column",
                    {"formula": index},
                )
            range_start = (
                coordinate("row", 200, start_value),
                coordinate("column", 50, start_value),
            )
            range_end = (
                coordinate("row", 200, end_value),
                coordinate("column", 50, end_value),
            )
            if range_start[0] > range_end[0] or range_start[1] > range_end[1]:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "cell_range start must not follow its end",
                    {"formula": index},
                )
            if range_start[0] <= row <= range_end[0] and range_start[1] <= column <= range_end[1]:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "cell_range cannot contain the destination formula cell",
                    {"formula": index},
                )
            start_name = f"{cls._word_table_column_name(range_start[1])}{range_start[0]}"
            end_name = f"{cls._word_table_column_name(range_end[1])}{range_end[0]}"
            operands = start_name if start_name == end_name else f"{start_name}:{end_name}"

        numeric_format = item.get("numeric_format", "")
        if not isinstance(numeric_format, str) or len(numeric_format) > 64:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "numeric_format must be a string of at most 64 characters",
                {"formula": index},
            )
        if numeric_format and not re.fullmatch(r"[0#.,%$€£¥()\-\+ ]+", numeric_format):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "numeric_format contains unsupported characters",
                {"formula": index},
            )
        replace_existing = item.get("replace_existing", False)
        if not isinstance(replace_existing, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "replace_existing must be true or false",
                {"formula": index},
            )
        expression = f"={WORD_TABLE_FORMULA_FUNCTIONS[function]}({operands})"
        return PreparedLiveTableFormula(
            row=row,
            column=column,
            function=function,
            directions=directions,
            range_start=range_start,
            range_end=range_end,
            numeric_format=numeric_format,
            replace_existing=replace_existing,
            expression=expression,
            rules=(
                "typed_table_formula",
                "no_raw_field_code",
                "bounded_table_coordinates",
                "native_formula_field_verification",
                "locale_aware_formula_separators",
            ),
        )

    @classmethod
    def _prepare_table_formulas(
        cls,
        formulas: list[dict[str, Any]],
    ) -> list[PreparedLiveTableFormula]:
        if not isinstance(formulas, list) or not formulas:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "formulas must be a non-empty array",
            )
        if len(formulas) > 200:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "At most 200 live table formulas may be processed in one batch",
            )
        prepared = [
            cls._prepare_table_formula_item(item, index) for index, item in enumerate(formulas)
        ]
        targets = [(item.row, item.column) for item in prepared]
        if len(set(targets)) != len(targets):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "A live table formula batch cannot target the same cell twice",
            )
        return prepared

    def preflight_table_formulas(self, formulas: list[dict[str, Any]]) -> dict[str, Any]:
        self._require_available()
        if not isinstance(formulas, list) or not formulas:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "formulas must be a non-empty array",
            )
        if len(formulas) > 200:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "At most 200 live table formulas may be checked in one preflight",
            )
        results: list[dict[str, Any]] = []
        valid_targets: list[tuple[int, int]] = []
        for index, item in enumerate(formulas):
            try:
                prepared = self._prepare_table_formula_item(item, index)
                target = (prepared.row, prepared.column)
                duplicate = target in valid_targets
                if duplicate:
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "A live table formula batch cannot target the same cell twice",
                        {"formula": index, "row": prepared.row, "column": prepared.column},
                    )
                valid_targets.append(target)
                results.append(
                    {
                        "index": index,
                        "valid": True,
                        "row": prepared.row,
                        "column": prepared.column,
                        "function": prepared.function,
                        "source": "directions" if prepared.directions else "cell_range",
                        "directions": list(prepared.directions),
                        "has_numeric_format": bool(prepared.numeric_format),
                        "replace_existing": prepared.replace_existing,
                        "field_type": WORD_SAFE_FIELD_TYPES["formula"],
                        "rules": list(prepared.rules),
                    }
                )
            except WordToolkitError as exc:
                results.append(
                    {
                        "index": index,
                        "valid": False,
                        "error": {
                            "code": exc.code.value,
                            "message": exc.message,
                            "details": exc.details or {},
                        },
                    }
                )
        invalid = sum(not item["valid"] for item in results)
        return {
            "valid": invalid == 0,
            "formula_count": len(results),
            "valid_count": len(results) - invalid,
            "invalid_count": invalid,
            "formulas": results,
            "raw_field_codes_accepted": False,
            "mutated_word": False,
            "content_returned": False,
        }

    def insert_table(
        self,
        owner: str,
        document_id: str,
        *,
        rows: list[list[str]],
        target: LiveTarget = "document_end",
        selection_token: str = "",
        replace_selection: bool = False,
        style: str = "",
        header_row: bool = True,
        autofit: TableAutoFit = "window",
        alignment: Literal["left", "center", "right"] = "left",
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        prepared_rows, column_count = self._prepare_table_rows(rows)
        if not isinstance(style, str) or len(style) > 128:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "style must be a string of at most 128 characters",
            )
        if autofit not in TABLE_AUTOFIT:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "autofit must be fixed, content or window",
            )
        if alignment not in TABLE_ALIGNMENT:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "alignment must be left, center or right",
            )
        row_count = len(prepared_rows)
        tsv = "\r".join("\t".join(row) for row in prepared_rows)
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            target_range = self._target_range(
                application,
                document,
                record,
                target,
                selection_token=selection_token,
                replace_selection=replace_selection,
                activate=activate,
            )
            start = int(target_range.Start)
            payload, prefix_length, suffix_length = self._paragraph_payload(
                document,
                start,
                tsv,
                as_new_paragraph=True,
            )
            before = int(document.Tables.Count)
            with (
                self._screen_updates_suspended(
                    application,
                    enabled=optimize_screen_updates,
                ),
                self._undoable(
                    application,
                    document,
                    "WordToolkit: insert native table",
                ),
            ):
                target_range.Text = payload
                table_start = start + prefix_length
                table_end = int(target_range.End) - suffix_length
                conversion_range = document.Range(table_start, table_end)
                table = conversion_range.ConvertToTable(1, row_count, column_count)
                after = int(document.Tables.Count)
                if after != before + 1:
                    raise WordToolkitError(
                        ErrorCode.EXTERNAL_TOOL_FAILED,
                        "Microsoft Word did not create exactly one native table",
                        {"before": before, "after": after},
                    )
                if style:
                    table.Style = style
                table.AllowAutoFit = autofit != "fixed"
                table.AutoFitBehavior(TABLE_AUTOFIT[autofit])
                table.Rows.Alignment = TABLE_ALIGNMENT[alignment]
                if header_row:
                    table.Rows.Item(1).HeadingFormat = -1
                    with suppress(Exception):
                        table.ApplyStyleHeadingRows = True
                table_range = table.Range.Duplicate
            record.version += 1
            self._show_range(application, table_range, int(table_range.End))
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "table": {
                    "index": after,
                    "rows": row_count,
                    "columns": column_count,
                    "range": {
                        "start": int(table_range.Start),
                        "end": int(table_range.End),
                    },
                    "style": style,
                    "header_row": header_row,
                    "autofit": autofit,
                    "alignment": alignment,
                    "native_verified": True,
                },
                "content_returned": False,
                "screen_updates_suspended": optimize_screen_updates,
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    @staticmethod
    def _cell_visible_text(cell_range: Any) -> str:
        text = str(cell_range.Text or "")
        if text.endswith("\r\x07"):
            return text[:-2]
        return text.rstrip("\r\x07")

    def insert_table_formulas(
        self,
        owner: str,
        document_id: str,
        *,
        table_index: int,
        formulas: list[dict[str, Any]],
        activate: bool = True,
        optimize_screen_updates: bool = True,
        force_update: bool = False,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        if (
            isinstance(table_index, bool)
            or not isinstance(table_index, int)
            or not 1 <= table_index <= 10_000
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "table_index must be an integer from 1 to 10,000",
            )
        if not isinstance(force_update, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "force_update must be true or false",
            )
        started_at = time.perf_counter()
        prepared = self._prepare_table_formulas(formulas)
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            table_count = int(document.Tables.Count)
            if table_index > table_count:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "The requested table does not exist in the live document",
                    {"table_index": table_index, "table_count": table_count},
                )
            table = document.Tables.Item(table_index)
            try:
                row_count = int(table.Rows.Count)
                column_count = int(table.Columns.Count)
            except Exception as exc:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "The requested table is not a uniform rectangular table",
                    {"table_index": table_index},
                ) from exc

            cells: list[tuple[Any, int, bool]] = []
            removed_fields = 0
            for index, item in enumerate(prepared):
                if item.row > row_count or item.column > column_count:
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "A formula destination is outside the live table",
                        {
                            "formula": index,
                            "row": item.row,
                            "column": item.column,
                            "table_rows": row_count,
                            "table_columns": column_count,
                        },
                    )
                if (
                    item.range_start is not None
                    and item.range_end is not None
                    and (item.range_end[0] > row_count or item.range_end[1] > column_count)
                ):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "A formula source range is outside the live table",
                        {
                            "formula": index,
                            "table_rows": row_count,
                            "table_columns": column_count,
                        },
                    )
                try:
                    cell = table.Cell(item.row, item.column)
                    cell_range = cell.Range.Duplicate
                    existing_fields = int(cell_range.Fields.Count)
                    has_content = bool(self._cell_visible_text(cell_range))
                except Exception as exc:
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "A formula cell cannot be addressed in the live table",
                        {"formula": index, "row": item.row, "column": item.column},
                    ) from exc
                if (has_content or existing_fields) and not item.replace_existing:
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "A formula destination is not empty; set replace_existing=true explicitly",
                        {"formula": index, "row": item.row, "column": item.column},
                    )
                needs_clear = has_content or existing_fields > 0
                cells.append((cell, existing_fields, needs_clear))
                if needs_clear:
                    removed_fields += existing_fields

            list_separator, decimal_separator, thousands_separator = self._word_formula_locale(
                application
            )
            before = int(document.Fields.Count)
            created: list[tuple[Any, Any, bool]] = []
            clear_assignments = 0
            with (
                self._screen_updates_suspended(
                    application,
                    enabled=optimize_screen_updates,
                ),
                self._undoable(
                    application,
                    document,
                    "WordToolkit: insert native table formulas",
                ),
            ):
                for index, item in enumerate(prepared):
                    cell, _existing_fields, needs_clear = cells[index]
                    if needs_clear:
                        content_range = cell.Range.Duplicate
                        content_range.End = max(
                            int(content_range.Start), int(content_range.End) - 1
                        )
                        content_range.Text = ""
                        clear_assignments += 1
                    expression = self._localize_formula_expression(
                        item.expression.removeprefix("="),
                        list_separator=list_separator,
                        decimal_separator=decimal_separator,
                    )
                    numeric_format = self._localize_numeric_format(
                        item.numeric_format,
                        decimal_separator=decimal_separator,
                        thousands_separator=thousands_separator,
                    )
                    field_text = expression
                    if numeric_format:
                        field_text += f' \\# "{numeric_format}"'
                    insertion_range = cell.Range.Duplicate
                    insertion_range.End = insertion_range.Start
                    field = document.Fields.Add(
                        insertion_range,
                        WORD_SAFE_FIELD_TYPES["formula"],
                        field_text,
                        True,
                    )
                    cell_fields = cell.Range.Fields
                    if int(cell_fields.Count) != 1:
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Microsoft Word did not create exactly one field in the formula cell",
                            {"formula": index, "field_count": int(cell_fields.Count)},
                        )
                    actual_type = int(field.Type)
                    if actual_type != WORD_SAFE_FIELD_TYPES["formula"]:
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Microsoft Word created an unexpected table formula field type",
                            {
                                "formula": index,
                                "expected": WORD_SAFE_FIELD_TYPES["formula"],
                                "actual": actual_type,
                            },
                        )
                    result_range = field.Result.Duplicate
                    if int(result_range.End) <= int(result_range.Start):
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Microsoft Word did not calculate a native table formula on insertion",
                            {"formula": index, "row": item.row, "column": item.column},
                        )
                    updated = False
                    if force_update:
                        updated = bool(field.Update())
                        if not updated:
                            raise WordToolkitError(
                                ErrorCode.EXTERNAL_TOOL_FAILED,
                                "Microsoft Word could not update a native table formula",
                                {"formula": index, "row": item.row, "column": item.column},
                            )
                    created.append((field, result_range, updated))
                after = int(document.Fields.Count)
                expected_after = before - removed_fields + len(prepared)
                if after != expected_after:
                    raise WordToolkitError(
                        ErrorCode.EXTERNAL_TOOL_FAILED,
                        "Microsoft Word did not create the expected number of table formula fields",
                        {
                            "before": before,
                            "after": after,
                            "expected": expected_after,
                        },
                    )
                result_formulas = [
                    {
                        "index": index,
                        "row": item.row,
                        "column": item.column,
                        "function": item.function,
                        "source": "directions" if item.directions else "cell_range",
                        "directions": list(item.directions),
                        "field_type": WORD_SAFE_FIELD_TYPES["formula"],
                        "calculated_on_insert": True,
                        "updated": created[index][2],
                        "replaced_existing": cells[index][2],
                        "range": {
                            "start": int(created[index][1].Start),
                            "end": int(created[index][1].End),
                        },
                        "native_verified": True,
                    }
                    for index, item in enumerate(prepared)
                ]
                last_range = created[-1][1]
            record.version += len(prepared)
            if activate:
                with suppress(Exception):
                    document.Activate()
            self._show_range(application, last_range, int(last_range.End))
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "table": {
                    "index": table_index,
                    "rows": row_count,
                    "columns": column_count,
                },
                "formulas": result_formulas,
                "formula_count": len(prepared),
                "field_count_before": before,
                "field_count_after": after,
                "performance": {
                    "com_attachments": 1,
                    "table_lookups": 1,
                    "field_add_calls": len(prepared),
                    "field_update_calls": len(prepared) if force_update else 0,
                    "cell_clear_assignments": clear_assignments,
                    "undo_transactions": 1,
                    "screen_updates_suspended": optimize_screen_updates,
                    "calculation_mode": (
                        "on_insert_and_explicit_update" if force_update else "on_insert"
                    ),
                },
                "raw_field_codes_accepted": False,
                "content_returned": False,
                "document": self._document_info(application, document),
            }

        result = self._execute(operation)
        result["performance"]["duration_ms"] = round(
            (time.perf_counter() - started_at) * 1_000,
            3,
        )
        return result

    @staticmethod
    def _live_field_type_histogram(fields: Any, field_count: int) -> dict[str, int]:
        histogram: dict[str, int] = {}
        for index in range(1, field_count + 1):
            try:
                field_type = str(int(fields.Item(index).Type))
            except Exception as exc:
                raise WordToolkitError(
                    ErrorCode.EXTERNAL_TOOL_FAILED,
                    "Microsoft Word did not expose a stable field type before recalculation",
                    {"field_index": index},
                ) from exc
            histogram[field_type] = histogram.get(field_type, 0) + 1
        return dict(sorted(histogram.items(), key=lambda item: int(item[0])))

    def update_table_fields(
        self,
        owner: str,
        document_id: str,
        *,
        table_index: int,
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        if (
            isinstance(table_index, bool)
            or not isinstance(table_index, int)
            or not 1 <= table_index <= 10_000
        ):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "table_index must be an integer from 1 to 10,000",
            )
        if not isinstance(activate, bool) or not isinstance(optimize_screen_updates, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "activate and optimize_screen_updates must be true or false",
            )
        started_at = time.perf_counter()
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            table_count = int(document.Tables.Count)
            if table_index > table_count:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "The requested table does not exist in the live document",
                    {"table_index": table_index, "table_count": table_count},
                )
            table = document.Tables.Item(table_index)
            fields = table.Range.Fields
            before_count = max(0, int(fields.Count))
            if before_count > 5_000:
                raise WordToolkitError(
                    ErrorCode.LIMIT_EXCEEDED,
                    "A live table field refresh is limited to 5,000 fields",
                    {"field_count": before_count},
                )
            before_histogram = self._live_field_type_histogram(fields, before_count)
            if before_count == 0:
                return {
                    "live_document_id": record.document_id,
                    "live_version": record.version,
                    "table": {
                        "index": table_index,
                        "field_count": 0,
                        "field_type_histogram": {},
                    },
                    "updated": False,
                    "no_op": True,
                    "performance": {
                        "com_attachments": 1,
                        "table_lookups": 1,
                        "field_type_reads": 0,
                        "field_update_calls": 0,
                        "undo_transactions": 0,
                        "screen_updates_suspended": False,
                    },
                    "field_codes_returned": False,
                    "field_results_returned": False,
                    "content_returned": False,
                    "document": self._document_info(application, document),
                }

            with (
                self._screen_updates_suspended(
                    application,
                    enabled=optimize_screen_updates,
                ),
                self._undoable(
                    application,
                    document,
                    "WordToolkit: update native table fields",
                ),
            ):
                update_result = int(fields.Update())
                if update_result != 0:
                    raise WordToolkitError(
                        ErrorCode.EXTERNAL_TOOL_FAILED,
                        "Microsoft Word reported an error while updating table fields",
                        {
                            "reported_first_error_index": update_result,
                            "reported_index_may_be_inaccurate": True,
                        },
                    )
                after_count = max(0, int(fields.Count))
                after_histogram = self._live_field_type_histogram(fields, after_count)
                if after_count != before_count or after_histogram != before_histogram:
                    raise WordToolkitError(
                        ErrorCode.EXTERNAL_TOOL_FAILED,
                        "Microsoft Word changed the native field structure during recalculation",
                        {
                            "field_count_before": before_count,
                            "field_count_after": after_count,
                        },
                    )

            record.version += 1
            if activate:
                with suppress(Exception):
                    document.Activate()
                    application.ActiveWindow.ScrollIntoView(table.Range, True)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "table": {
                    "index": table_index,
                    "field_count": after_count,
                    "field_type_histogram": after_histogram,
                },
                "updated": True,
                "no_op": False,
                "native_verified": True,
                "word_update_result": update_result,
                "performance": {
                    "com_attachments": 1,
                    "table_lookups": 1,
                    "field_type_reads": before_count + after_count,
                    "field_update_calls": 1,
                    "undo_transactions": 1,
                    "screen_updates_suspended": optimize_screen_updates,
                },
                "field_codes_returned": False,
                "field_results_returned": False,
                "content_returned": False,
                "document": self._document_info(application, document),
            }

        result = self._execute(operation)
        result["performance"]["duration_ms"] = round(
            (time.perf_counter() - started_at) * 1_000,
            3,
        )
        return result

    @staticmethod
    def _prepare_list_items(items: list[str]) -> list[str]:
        if not isinstance(items, list) or not items:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "items must be a non-empty array",
            )
        if len(items) > 1_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "A live list may contain at most 1,000 items",
            )
        prepared: list[str] = []
        total_characters = 0
        for index, item in enumerate(items):
            if not isinstance(item, str) or not item:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Every live list item must be a non-empty string",
                    {"item": index},
                )
            if "\x07" in item:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Live list items cannot contain Word cell-marker characters",
                    {"item": index},
                )
            normalized = item.replace("\r\n", "\n").replace("\r", "\n").replace("\n", "\v")
            if len(normalized) > 50_000:
                raise WordToolkitError(
                    ErrorCode.LIMIT_EXCEEDED,
                    "One live list item exceeds 50,000 characters",
                    {"item": index},
                )
            total_characters += len(normalized)
            prepared.append(normalized)
        if total_characters > 500_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "Live list text exceeds 500,000 characters",
            )
        return prepared

    def insert_list(
        self,
        owner: str,
        document_id: str,
        *,
        items: list[str],
        list_kind: LiveListKind = "bullet",
        target: LiveTarget = "document_end",
        selection_token: str = "",
        replace_selection: bool = False,
        style: str = "",
        formatting: dict[str, Any] | None = None,
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        prepared_items = self._prepare_list_items(items)
        if list_kind not in WORD_LIST_TYPES:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "list_kind must be bullet or numbered",
            )
        if not isinstance(style, str) or len(style) > 128:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "style must be a string of at most 128 characters",
            )
        normalized_formatting = self._normalize_text_formatting(formatting)
        payload_text = "\r".join(prepared_items)
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            target_range = self._target_range(
                application,
                document,
                record,
                target,
                selection_token=selection_token,
                replace_selection=replace_selection,
                activate=activate,
            )
            start = int(target_range.Start)
            payload, prefix_length, suffix_length = self._paragraph_payload(
                document,
                start,
                payload_text,
                as_new_paragraph=True,
            )
            before = self._safe_collection_count(document, "Lists")
            with (
                self._screen_updates_suspended(
                    application,
                    enabled=optimize_screen_updates,
                ),
                self._undoable(
                    application,
                    document,
                    "WordToolkit: insert native list",
                ),
            ):
                target_range.Text = payload
                list_start = start + prefix_length
                list_end = int(target_range.End) - suffix_length
                list_range = document.Range(list_start, list_end)
                if style:
                    list_range.Style = style
                if normalized_formatting:
                    self._apply_text_formatting(list_range, normalized_formatting)
                list_format = list_range.ListFormat
                if list_kind == "bullet":
                    list_format.ApplyBulletDefault(1)
                else:
                    list_format.ApplyNumberDefault(1)
                actual_type = int(list_format.ListType)
                if actual_type != WORD_LIST_TYPES[list_kind]:
                    raise WordToolkitError(
                        ErrorCode.EXTERNAL_TOOL_FAILED,
                        "Microsoft Word did not create the requested native list type",
                        {
                            "expected": WORD_LIST_TYPES[list_kind],
                            "actual": actual_type,
                        },
                    )
                after = self._safe_collection_count(document, "Lists")
                list_range_result = list_range.Duplicate
            record.version += 1
            self._show_range(
                application,
                list_range_result,
                int(list_range_result.End),
            )
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "list": {
                    "kind": list_kind,
                    "item_count": len(prepared_items),
                    "list_type": actual_type,
                    "range": {
                        "start": int(list_range_result.Start),
                        "end": int(list_range_result.End),
                    },
                    "style": style,
                    "formatting": normalized_formatting,
                    "list_count_before": before,
                    "list_count_after": after,
                    "native_verified": True,
                },
                "content_returned": False,
                "screen_updates_suspended": optimize_screen_updates,
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    @staticmethod
    def _normalize_bookmark_text(value: Any, name: str, index: int) -> str:
        if not isinstance(value, str):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                f"{name} must be a string",
                {"bookmark": index},
            )
        if len(value) > 100_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                f"{name} exceeds 100,000 characters",
                {"bookmark": index},
            )
        if any(character in value for character in ("\x00", "\x07", "\x13", "\x14", "\x15")):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                f"{name} contains a reserved Word control character",
                {"bookmark": index},
            )
        return value.replace("\r\n", "\n").replace("\r", "\n").replace("\n", "\r")

    @classmethod
    def _prepare_bookmark_item(
        cls,
        item: dict[str, Any],
        index: int,
    ) -> PreparedLiveBookmark:
        if not isinstance(item, dict):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Each live bookmark must be an object",
                {"bookmark": index},
            )
        allowed = {
            "name",
            "text",
            "prefix_text",
            "suffix_text",
            "as_new_paragraph",
            "style",
            "formatting",
        }
        unknown = sorted(set(item) - allowed)
        if unknown:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Unsupported live bookmark arguments",
                {"bookmark": index, "arguments": unknown},
            )
        name = item.get("name")
        if not isinstance(name, str) or WORD_BOOKMARK_NAME.fullmatch(name) is None:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Bookmark name must start with an ASCII letter and contain at most "
                "40 ASCII letters, digits or underscores",
                {"bookmark": index},
            )
        text = cls._normalize_bookmark_text(item.get("text"), "text", index)
        if not text:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Bookmark text must not be empty",
                {"bookmark": index},
            )
        prefix_text = cls._normalize_bookmark_text(
            item.get("prefix_text", ""),
            "prefix_text",
            index,
        )
        suffix_text = cls._normalize_bookmark_text(
            item.get("suffix_text", ""),
            "suffix_text",
            index,
        )
        as_new_paragraph = item.get("as_new_paragraph", False)
        if not isinstance(as_new_paragraph, bool):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "as_new_paragraph must be true or false",
                {"bookmark": index},
            )
        style = item.get("style", "")
        if not isinstance(style, str) or len(style) > 128:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "style must be a string of at most 128 characters",
                {"bookmark": index},
            )
        formatting = cls._normalize_text_formatting(item.get("formatting"))
        return PreparedLiveBookmark(
            name=name,
            text=text,
            prefix_text=prefix_text,
            suffix_text=suffix_text,
            as_new_paragraph=as_new_paragraph,
            style=style,
            formatting=formatting,
            rules=(
                "native_bookmark",
                "case_insensitive_unique_name",
                "bounded_bookmark_range",
                "native_range_verification",
            ),
        )

    @classmethod
    def _prepare_bookmarks(
        cls,
        bookmarks: list[dict[str, Any]],
    ) -> list[PreparedLiveBookmark]:
        if not isinstance(bookmarks, list) or not bookmarks:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "bookmarks must be a non-empty array",
            )
        if len(bookmarks) > 200:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "At most 200 live bookmarks may be processed in one batch",
            )
        prepared = [cls._prepare_bookmark_item(item, index) for index, item in enumerate(bookmarks)]
        folded_names = [item.name.casefold() for item in prepared]
        if len(folded_names) != len(set(folded_names)):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Live bookmark names must be unique without regard to capitalization",
            )
        total_characters = sum(
            len(item.prefix_text) + len(item.text) + len(item.suffix_text) for item in prepared
        )
        if total_characters > 500_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "Live bookmark payload exceeds 500,000 characters",
            )
        return prepared

    def preflight_bookmarks(self, bookmarks: list[dict[str, Any]]) -> dict[str, Any]:
        self._require_available()
        if not isinstance(bookmarks, list) or not bookmarks:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "bookmarks must be a non-empty array",
            )
        if len(bookmarks) > 200:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "At most 200 live bookmarks may be preflighted in one batch",
            )
        results: list[dict[str, Any]] = []
        folded_names: set[str] = set()
        for index, item in enumerate(bookmarks):
            try:
                prepared = self._prepare_bookmark_item(item, index)
                folded = prepared.name.casefold()
                if folded in folded_names:
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "Live bookmark names must be unique without regard to capitalization",
                        {"bookmark": index},
                    )
                folded_names.add(folded)
                results.append(
                    {
                        "index": index,
                        "valid": True,
                        "name": prepared.name,
                        "rules": list(prepared.rules),
                    }
                )
            except WordToolkitError as exc:
                results.append(
                    {
                        "index": index,
                        "valid": False,
                        "error": {
                            "code": exc.code.value,
                            "message": exc.message,
                            "details": exc.details or {},
                        },
                    }
                )
        invalid = sum(not item["valid"] for item in results)
        return {
            "valid": invalid == 0,
            "bookmark_count": len(results),
            "valid_count": len(results) - invalid,
            "invalid_count": invalid,
            "bookmarks": results,
            "word_attached": False,
            "mutated_word": False,
            "content_returned": False,
        }

    @staticmethod
    def _bookmark_batch_payload(
        document: Any,
        start: int,
        bookmarks: list[PreparedLiveBookmark],
    ) -> tuple[str, list[tuple[int, int]]]:
        payload = ""
        ranges: list[tuple[int, int]] = []
        previous = str(document.Range(start - 1, start).Text or "") if start > 0 else ""
        for item in bookmarks:
            if item.as_new_paragraph and not (
                (not payload and (start == 0 or previous == "\r")) or payload.endswith("\r")
            ):
                payload += "\r"
            payload += item.prefix_text
            bookmark_start = _word_utf16_length(payload)
            payload += item.text
            ranges.append((bookmark_start, _word_utf16_length(payload)))
            payload += item.suffix_text
            if item.as_new_paragraph and not payload.endswith("\r"):
                payload += "\r"
        return payload, ranges

    def insert_bookmarks(
        self,
        owner: str,
        document_id: str,
        *,
        bookmarks: list[dict[str, Any]],
        target: LiveTarget = "document_end",
        selection_token: str = "",
        replace_selection: bool = False,
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        prepared = self._prepare_bookmarks(bookmarks)
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            existing = [
                item.name for item in prepared if bool(document.Bookmarks.Exists(item.name))
            ]
            if existing:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "A requested bookmark already exists in the live document",
                    {"names": existing},
                )
            target_range = self._target_range(
                application,
                document,
                record,
                target,
                selection_token=selection_token,
                replace_selection=replace_selection,
                activate=activate,
            )
            start = int(target_range.Start)
            payload, relative_ranges = self._bookmark_batch_payload(
                document,
                start,
                prepared,
            )
            before = int(document.Bookmarks.Count)
            created: dict[int, Any] = {}
            with (
                self._screen_updates_suspended(
                    application,
                    enabled=optimize_screen_updates,
                ),
                self._undoable(
                    application,
                    document,
                    "WordToolkit: insert native bookmarks",
                ),
            ):
                target_range.Text = payload
                for index, item in enumerate(prepared):
                    relative_start, relative_end = relative_ranges[index]
                    bookmark_range = document.Range(
                        start + relative_start,
                        start + relative_end,
                    )
                    if item.style:
                        bookmark_range.Style = item.style
                    if item.formatting:
                        self._apply_text_formatting(bookmark_range, item.formatting)
                    document.Bookmarks.Add(item.name, bookmark_range)
                    if not bool(document.Bookmarks.Exists(item.name)):
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Microsoft Word did not create a requested native bookmark",
                            {"bookmark": index},
                        )
                    bookmark = document.Bookmarks.Item(item.name)
                    actual_range = bookmark.Range.Duplicate
                    if (
                        str(bookmark.Name).casefold() != item.name.casefold()
                        or int(actual_range.Start) != int(bookmark_range.Start)
                        or int(actual_range.End) != int(bookmark_range.End)
                    ):
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Microsoft Word changed a requested native bookmark range",
                            {"bookmark": index},
                        )
                    created[index] = bookmark
                after = int(document.Bookmarks.Count)
                if after != before + len(prepared):
                    raise WordToolkitError(
                        ErrorCode.EXTERNAL_TOOL_FAILED,
                        "Microsoft Word did not create the expected number of bookmarks",
                        {
                            "before": before,
                            "after": after,
                            "expected": len(prepared),
                        },
                    )
                result_bookmarks = []
                for index, item in enumerate(prepared):
                    result_range = created[index].Range.Duplicate
                    result_bookmarks.append(
                        {
                            "index": index,
                            "name": item.name,
                            "range": {
                                "start": int(result_range.Start),
                                "end": int(result_range.End),
                            },
                            "native_verified": True,
                        }
                    )
                last_range = created[len(prepared) - 1].Range.Duplicate
            record.version += len(prepared)
            self._show_range(application, last_range, int(last_range.End))
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "bookmarks": result_bookmarks,
                "bookmark_count_before": before,
                "bookmark_count_after": after,
                "performance": {
                    "com_attachments": 1,
                    "text_assignments": 1,
                    "bookmark_add_calls": len(prepared),
                    "undo_transactions": 1,
                    "screen_updates_suspended": optimize_screen_updates,
                },
                "content_returned": False,
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    @staticmethod
    def _normalize_field_affix(value: Any, name: str, index: int) -> str:
        if not isinstance(value, str):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                f"{name} must be a string",
                {"field": index},
            )
        if len(value) > 50_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                f"{name} exceeds 50,000 characters",
                {"field": index},
            )
        if any(character in value for character in ("\x00", "\x07", "\x13", "\x14", "\x15")):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                f"{name} contains a reserved Word control character",
                {"field": index},
            )
        if WORD_FIELD_MARKER in value:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                f"{name} contains the reserved WordToolkit field marker",
                {"field": index},
            )
        return value.replace("\r\n", "\n").replace("\r", "\n").replace("\n", "\r")

    @classmethod
    def _prepare_field_item(cls, item: dict[str, Any], index: int) -> PreparedLiveField:
        if not isinstance(item, dict):
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Each live field must be an object",
                {"field": index},
            )
        kind_value = item.get("kind")
        if not isinstance(kind_value, str) or kind_value not in WORD_SAFE_FIELD_TYPES:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Unsupported live field kind",
                {
                    "field": index,
                    "allowed": sorted(WORD_SAFE_FIELD_TYPES),
                },
            )
        kind = cast(LiveFieldKind, kind_value)
        common_keys = {
            "kind",
            "preserve_formatting",
            "update",
            "prefix_text",
            "suffix_text",
            "as_new_paragraph",
        }
        specific_keys: set[str] = set()
        if kind == "formula":
            specific_keys = {"expression", "numeric_format"}
        elif kind in WORD_DATE_FIELD_KINDS:
            specific_keys = {"date_format"}
        elif kind == "sequence":
            specific_keys = {"identifier", "restart_at"}
        elif kind == "reference":
            specific_keys = {"bookmark", "hyperlink"}
        elif kind == "file_name":
            specific_keys = {"include_path"}
        unknown = sorted(set(item) - common_keys - specific_keys)
        if unknown:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "Unsupported arguments for live field kind",
                {"field": index, "kind": kind, "arguments": unknown},
            )

        def boolean(name: str, default: bool) -> bool:
            value = item.get(name, default)
            if not isinstance(value, bool):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    f"{name} must be true or false",
                    {"field": index},
                )
            return value

        preserve_formatting = boolean("preserve_formatting", True)
        update = boolean("update", True)
        as_new_paragraph = boolean("as_new_paragraph", False)
        prefix_text = cls._normalize_field_affix(item.get("prefix_text", ""), "prefix_text", index)
        suffix_text = cls._normalize_field_affix(item.get("suffix_text", ""), "suffix_text", index)
        field_text = ""
        bookmark = ""
        formula_expression = ""
        numeric_format = ""
        rules = ["safe_field_kind", "no_raw_field_code", "native_field_type_verification"]

        if kind == "formula":
            expression = item.get("expression")
            if not isinstance(expression, str):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "formula requires a string expression",
                    {"field": index},
                )
            expression = expression.strip()
            if expression.startswith("="):
                expression = expression[1:].strip()
            if not expression or len(expression) > 1_000:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "formula expression must contain 1 to 1,000 characters",
                    {"field": index},
                )
            if not re.fullmatch(r"[0-9A-Za-z_+\-*/^%(),.<>=\s]+", expression):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "formula contains unsupported characters",
                    {"field": index},
                )
            words = {word.upper() for word in re.findall(r"[A-Za-z_]+", expression)}
            unsupported_words = sorted(words - WORD_FORMULA_WORDS)
            if unsupported_words:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "formula contains unsupported names",
                    {"field": index, "names": unsupported_words},
                )
            depth = 0
            maximum_depth = 0
            for character in expression:
                if character == "(":
                    depth += 1
                    maximum_depth = max(maximum_depth, depth)
                elif character == ")":
                    depth -= 1
                    if depth < 0:
                        break
            if depth != 0 or maximum_depth > 32:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "formula parentheses are unbalanced or nested too deeply",
                    {"field": index, "maximum_depth": maximum_depth},
                )
            field_text = expression
            formula_expression = expression
            numeric_format = item.get("numeric_format", "")
            if not isinstance(numeric_format, str) or len(numeric_format) > 64:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "numeric_format must be a string of at most 64 characters",
                    {"field": index},
                )
            if numeric_format:
                if not re.fullmatch(r"[0#.,%$€£¥()\-\+ ]+", numeric_format):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "numeric_format contains unsupported characters",
                        {"field": index},
                    )
                field_text += f' \\# "{numeric_format}"'
            rules.extend(("restricted_formula_grammar", "locale_aware_formula_separators"))
        elif kind in WORD_DATE_FIELD_KINDS:
            date_format = item.get("date_format", "")
            if not isinstance(date_format, str) or len(date_format) > 64:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "date_format must be a string of at most 64 characters",
                    {"field": index},
                )
            if date_format:
                if not re.fullmatch(r"[A-Za-z0-9\s.,:/\-]+", date_format):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "date_format contains unsupported characters",
                        {"field": index},
                    )
                field_text = f'\\@ "{date_format}"'
        elif kind == "sequence":
            identifier = item.get("identifier")
            if not isinstance(identifier, str) or not re.fullmatch(
                r"[A-Za-z][A-Za-z0-9_]{0,30}",
                identifier,
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "sequence identifier must match [A-Za-z][A-Za-z0-9_]{0,30}",
                    {"field": index},
                )
            field_text = f"{identifier} \\* ARABIC"
            restart_at = item.get("restart_at")
            if restart_at is not None:
                if (
                    isinstance(restart_at, bool)
                    or not isinstance(restart_at, int)
                    or not 1 <= restart_at <= 1_000_000
                ):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "restart_at must be an integer from 1 to 1,000,000",
                        {"field": index},
                    )
                field_text += f" \\r {restart_at}"
        elif kind == "reference":
            bookmark_value = item.get("bookmark")
            if not isinstance(bookmark_value, str) or not re.fullmatch(
                r"[A-Za-z][A-Za-z0-9_]{0,39}",
                bookmark_value,
            ):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "reference bookmark must match [A-Za-z][A-Za-z0-9_]{0,39}",
                    {"field": index},
                )
            bookmark = bookmark_value
            field_text = bookmark
            if boolean("hyperlink", True):
                field_text += " \\h"
            rules.append("bookmark_must_exist_live")
        elif kind == "file_name" and boolean("include_path", False):
            field_text = "\\p"

        return PreparedLiveField(
            kind=kind,
            field_type=WORD_SAFE_FIELD_TYPES[kind],
            field_text=field_text,
            preserve_formatting=preserve_formatting,
            update=update,
            prefix_text=prefix_text,
            suffix_text=suffix_text,
            as_new_paragraph=as_new_paragraph,
            bookmark=bookmark,
            rules=tuple(rules),
            formula_expression=formula_expression,
            numeric_format=numeric_format,
        )

    @classmethod
    def _prepare_fields(cls, fields: list[dict[str, Any]]) -> list[PreparedLiveField]:
        if not isinstance(fields, list) or not fields:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "fields must be a non-empty array",
            )
        if len(fields) > 200:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "At most 200 live fields may be processed in one batch",
            )
        prepared = [cls._prepare_field_item(item, index) for index, item in enumerate(fields)]
        surrounding_characters = sum(
            len(item.prefix_text) + len(item.suffix_text) for item in prepared
        )
        if surrounding_characters > 500_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "Live field surrounding text exceeds 500,000 characters",
            )
        return prepared

    def preflight_fields(self, fields: list[dict[str, Any]]) -> dict[str, Any]:
        self._require_available()
        if not isinstance(fields, list) or not fields:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "fields must be a non-empty array",
            )
        if len(fields) > 200:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "At most 200 live fields may be checked in one preflight",
            )
        results: list[dict[str, Any]] = []
        for index, item in enumerate(fields):
            try:
                prepared = self._prepare_field_item(item, index)
                results.append(
                    {
                        "index": index,
                        "valid": True,
                        "kind": prepared.kind,
                        "field_type": prepared.field_type,
                        "preserve_formatting": prepared.preserve_formatting,
                        "update": prepared.update,
                        "as_new_paragraph": prepared.as_new_paragraph,
                        "rules": list(prepared.rules),
                    }
                )
            except WordToolkitError as exc:
                results.append(
                    {
                        "index": index,
                        "valid": False,
                        "error": {
                            "code": exc.code.value,
                            "message": exc.message,
                            "details": exc.details or {},
                        },
                    }
                )
        invalid = sum(not item["valid"] for item in results)
        return {
            "valid": invalid == 0,
            "field_count": len(results),
            "valid_count": len(results) - invalid,
            "invalid_count": invalid,
            "fields": results,
            "raw_field_codes_accepted": False,
            "mutated_word": False,
            "content_returned": False,
        }

    @staticmethod
    def _field_batch_payload(
        document: Any,
        start: int,
        fields: list[PreparedLiveField],
    ) -> tuple[str, list[tuple[int, int]]]:
        payload = ""
        markers: list[tuple[int, int]] = []
        previous = str(document.Range(start - 1, start).Text or "") if start > 0 else ""
        for item in fields:
            if item.as_new_paragraph and not (
                (not payload and (start == 0 or previous == "\r")) or payload.endswith("\r")
            ):
                payload += "\r"
            payload += item.prefix_text
            marker_start = _word_utf16_length(payload)
            payload += WORD_FIELD_MARKER
            markers.append((marker_start, marker_start + 1))
            payload += item.suffix_text
            if item.as_new_paragraph and not payload.endswith("\r"):
                payload += "\r"
        return payload, markers

    @staticmethod
    def _word_international_character(application: Any, index: int, name: str) -> str:
        try:
            value = str(application.International(index))
        except Exception as exc:
            raise WordToolkitError(
                ErrorCode.EXTERNAL_TOOL_FAILED,
                "Microsoft Word did not expose a required locale separator",
                {"separator": name},
            ) from exc
        if len(value) != 1 or value in {'"', "\\", "\r", "\n"}:
            raise WordToolkitError(
                ErrorCode.EXTERNAL_TOOL_FAILED,
                "Microsoft Word returned an invalid locale separator",
                {"separator": name},
            )
        return value

    @classmethod
    def _word_formula_locale(cls, application: Any) -> tuple[str, str, str]:
        return (
            cls._word_international_character(
                application,
                WORD_INTERNATIONAL_LIST_SEPARATOR,
                "list",
            ),
            cls._word_international_character(
                application,
                WORD_INTERNATIONAL_DECIMAL_SEPARATOR,
                "decimal",
            ),
            cls._word_international_character(
                application,
                WORD_INTERNATIONAL_THOUSANDS_SEPARATOR,
                "thousands",
            ),
        )

    @staticmethod
    def _localize_formula_expression(
        expression: str,
        *,
        list_separator: str,
        decimal_separator: str,
    ) -> str:
        localized = expression.replace(",", list_separator)
        if decimal_separator != ".":
            localized = re.sub(r"(?<=\d)\.(?=\d)", decimal_separator, localized)
        return localized

    @staticmethod
    def _localize_numeric_format(
        numeric_format: str,
        *,
        decimal_separator: str,
        thousands_separator: str,
    ) -> str:
        if not numeric_format:
            return ""
        placeholder = "\ue001"
        localized = numeric_format.replace(",", placeholder)
        localized = localized.replace(".", decimal_separator)
        return localized.replace(placeholder, thousands_separator)

    @classmethod
    def _localized_field_text(
        cls,
        application: Any,
        item: PreparedLiveField,
    ) -> str:
        if item.kind != "formula":
            return item.field_text
        list_separator, decimal_separator, thousands_separator = cls._word_formula_locale(
            application
        )
        expression = cls._localize_formula_expression(
            item.formula_expression,
            list_separator=list_separator,
            decimal_separator=decimal_separator,
        )
        field_text = expression
        if item.numeric_format:
            numeric_format = cls._localize_numeric_format(
                item.numeric_format,
                decimal_separator=decimal_separator,
                thousands_separator=thousands_separator,
            )
            field_text += f' \\# "{numeric_format}"'
        return field_text

    def insert_fields(
        self,
        owner: str,
        document_id: str,
        *,
        fields: list[dict[str, Any]],
        target: LiveTarget = "document_end",
        selection_token: str = "",
        replace_selection: bool = False,
        activate: bool = True,
        optimize_screen_updates: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        prepared = self._prepare_fields(fields)
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            for index, item in enumerate(prepared):
                if item.bookmark and not bool(document.Bookmarks.Exists(item.bookmark)):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "Reference bookmark does not exist in the live document",
                        {"field": index, "bookmark": item.bookmark},
                    )
            target_range = self._target_range(
                application,
                document,
                record,
                target,
                selection_token=selection_token,
                replace_selection=replace_selection,
                activate=activate,
            )
            start = int(target_range.Start)
            payload, marker_ranges = self._field_batch_payload(document, start, prepared)
            before = int(document.Fields.Count)
            created: dict[int, tuple[Any, bool]] = {}
            with (
                self._screen_updates_suspended(
                    application,
                    enabled=optimize_screen_updates,
                ),
                self._undoable(
                    application,
                    document,
                    "WordToolkit: insert safe native fields",
                ),
            ):
                target_range.Text = payload
                for index in range(len(prepared) - 1, -1, -1):
                    item = prepared[index]
                    relative_start, relative_end = marker_ranges[index]
                    field_range = document.Range(
                        start + relative_start,
                        start + relative_end,
                    )
                    field = document.Fields.Add(
                        field_range,
                        item.field_type,
                        self._localized_field_text(application, item),
                        item.preserve_formatting,
                    )
                    actual_type = int(field.Type)
                    if actual_type != item.field_type:
                        raise WordToolkitError(
                            ErrorCode.EXTERNAL_TOOL_FAILED,
                            "Microsoft Word created an unexpected native field type",
                            {
                                "field": index,
                                "expected": item.field_type,
                                "actual": actual_type,
                            },
                        )
                    updated = False
                    if item.update:
                        updated = bool(field.Update())
                        if not updated:
                            raise WordToolkitError(
                                ErrorCode.EXTERNAL_TOOL_FAILED,
                                "Microsoft Word could not update the native field result",
                                {"field": index, "kind": item.kind},
                            )
                    created[index] = (field, updated)
                after = int(document.Fields.Count)
                if after != before + len(prepared):
                    raise WordToolkitError(
                        ErrorCode.EXTERNAL_TOOL_FAILED,
                        "Microsoft Word did not create the expected number of native fields",
                        {
                            "before": before,
                            "after": after,
                            "expected": len(prepared),
                        },
                    )
                result_fields: list[dict[str, Any]] = []
                for index, item in enumerate(prepared):
                    field, updated = created[index]
                    result_range = field.Result.Duplicate
                    result_fields.append(
                        {
                            "index": index,
                            "kind": item.kind,
                            "field_type": item.field_type,
                            "updated": updated,
                            "preserve_formatting": item.preserve_formatting,
                            "range": {
                                "start": int(result_range.Start),
                                "end": int(result_range.End),
                            },
                            "native_verified": True,
                        }
                    )
                last_range = created[len(prepared) - 1][0].Result.Duplicate
            record.version += len(prepared)
            self._show_range(application, last_range, int(last_range.End))
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "fields": result_fields,
                "field_count_before": before,
                "field_count_after": after,
                "performance": {
                    "com_attachments": 1,
                    "text_assignments": 1,
                    "field_add_calls": len(prepared),
                    "undo_transactions": 1,
                    "screen_updates_suspended": optimize_screen_updates,
                },
                "raw_field_codes_accepted": False,
                "content_returned": False,
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    def insert_equation(
        self,
        owner: str,
        document_id: str,
        *,
        value: str | dict[str, Any],
        input_format: EquationInputFormat,
        display: bool,
        target: LiveTarget,
        selection_token: str = "",
        replace_selection: bool = False,
        activate: bool = True,
        expected_version: int | None = None,
    ) -> dict[str, Any]:
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)
        prepared = self._prepare_equation(
            value,
            input_format,
            display=display,
            verify_readback=True,
        )
        linear = prepared.linear

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            target_range = self._target_range(
                application,
                document,
                record,
                target,
                selection_token=selection_token,
                replace_selection=replace_selection,
                activate=activate,
            )
            start = int(target_range.Start)
            payload, prefix_length, suffix_length = self._paragraph_payload(
                document,
                start,
                linear,
                as_new_paragraph=display,
            )
            before = int(document.OMaths.Count)
            with self._undoable(application, document, "WordToolkit: insert equation"):
                target_range.Text = payload
                equation_start = start + prefix_length
                equation_end = int(target_range.End) - suffix_length
                equation_range = document.Range(equation_start, equation_end)
                added_range = document.OMaths.Add(equation_range)
                after = int(document.OMaths.Count)
                if after != before + 1:
                    raise WordToolkitError(
                        ErrorCode.EQUATION_INVALID,
                        "Microsoft Word did not create exactly one native equation",
                        {"before": before, "after": after},
                    )
                equation = added_range.OMaths.Item(1)
                equation.BuildUp()
                equation.Type = 0 if display else 1
                verified_ast = self._verify_live_equation(
                    equation,
                    expected_ast=prepared.ast,
                    required_symbol_groups=prepared.required_symbol_groups,
                )
            record.version += 1
            caret = int(target_range.End)
            self._show_range(application, equation.Range, caret)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "equation": {
                    "index": after,
                    "display": display,
                    "linear_input": linear,
                    "range": {"start": equation_start, "end": equation_end},
                    "native_verified": True,
                    "ast": verified_ast,
                },
                "document": self._document_info(application, document),
            }

        started = time.perf_counter()
        try:
            result = self._execute(operation)
        except WordToolkitError as exc:
            self._record_equation_learning(
                [prepared],
                success=False,
                duration_ms=(time.perf_counter() - started) * 1000,
                error=exc,
            )
            raise
        result["equation"]["learning_observation_recorded"] = self._record_equation_learning(
            [prepared],
            success=True,
            duration_ms=(time.perf_counter() - started) * 1000,
        )
        result["equation"]["features"] = list(prepared.features)
        result["equation"]["learning"] = prepared.learning
        return result

    def insert_equations_batch(
        self,
        owner: str,
        document_id: str,
        *,
        equations: list[dict[str, Any]],
        activate: bool = True,
        expected_version: int | None = None,
        verify_readback: bool = False,
    ) -> dict[str, Any]:
        """Insert several native display equations in one COM attachment.

        The regular equation tool is intentionally bounded to one mutation so a
        caller can interleave text, selection edits, and equations. Long math
        worksheets need a faster path: keeping the Word COM apartment attached
        while the equations are added removes one expensive attach/detach cycle
        per equation.
        """
        if not equations:
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "equations must not be empty")
        if len(equations) > 100:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "At most 100 equations may be inserted in one batch",
            )
        prepared: list[PreparedLiveEquation] = []
        for item in equations:
            if not isinstance(item, dict):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Each batch equation must be an object",
                )
            prepared.append(
                self._prepare_equation_item(
                    item,
                    verify_readback_default=verify_readback,
                )
            )

        record = self._record(owner, document_id)
        self._check_version(record, expected_version)
        failed_equation: PreparedLiveEquation | None = None

        def operation(application: Any) -> dict[str, Any]:
            nonlocal failed_equation
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            before = int(document.OMaths.Count)
            results: list[dict[str, Any]] = []
            last_equation = None
            with self._undoable(application, document, "WordToolkit: insert equation batch"):
                for prepared_equation in prepared:
                    failed_equation = prepared_equation
                    linear = prepared_equation.linear
                    display = prepared_equation.display
                    end = max(0, int(document.Content.End) - 1)
                    target_range = document.Range(end, end)
                    payload, prefix_length, suffix_length = self._paragraph_payload(
                        document,
                        end,
                        linear,
                        as_new_paragraph=display,
                    )
                    target_range.Text = payload
                    equation_start = end + prefix_length
                    equation_end = int(target_range.End) - suffix_length
                    equation_range = document.Range(equation_start, equation_end)
                    added_range = document.OMaths.Add(equation_range)
                    equation = added_range.OMaths.Item(1)
                    equation.BuildUp()
                    equation.Type = 0 if display else 1
                    verified_ast = (
                        self._verify_live_equation(
                            equation,
                            expected_ast=prepared_equation.ast,
                            required_symbol_groups=prepared_equation.required_symbol_groups,
                        )
                        if prepared_equation.verify_readback
                        else None
                    )
                    last_equation = equation
                    results.append(
                        {
                            "index": int(document.OMaths.Count),
                            "display": display,
                            "linear_input": linear,
                            "range": {"start": equation_start, "end": equation_end},
                            "native_verified": True,
                            "ast": verified_ast,
                        }
                    )
                    failed_equation = None
                after = int(document.OMaths.Count)
                if after != before + len(prepared):
                    failed_equation = prepared[-1]
                    raise WordToolkitError(
                        ErrorCode.EQUATION_INVALID,
                        "Microsoft Word did not create the expected number of native equations",
                        {"before": before, "after": after, "expected": len(prepared)},
                    )
            record.version += len(prepared)
            if last_equation is not None:
                self._show_range(application, last_equation.Range, int(last_equation.Range.End))
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "equations": results,
                "document": self._document_info(application, document),
            }

        started = time.perf_counter()
        try:
            result = self._execute(operation)
        except WordToolkitError as exc:
            self._record_equation_learning(
                [failed_equation] if failed_equation is not None else [],
                success=False,
                duration_ms=(time.perf_counter() - started) * 1000,
                error=exc,
            )
            raise
        recorded = self._record_equation_learning(
            prepared,
            success=True,
            duration_ms=(time.perf_counter() - started) * 1000,
        )
        result["learning_observations_recorded"] = len(prepared) if recorded else 0
        for item, prepared_equation in zip(result["equations"], prepared, strict=True):
            item["features"] = list(prepared_equation.features)
            item["learning"] = prepared_equation.learning
        return result

    def apply_operations(
        self,
        owner: str,
        document_id: str,
        *,
        operations: list[dict[str, Any]],
        activate: bool = True,
        expected_version: int | None = None,
        verify_readback: bool = False,
        optimize_screen_updates: bool = True,
    ) -> dict[str, Any]:
        """Append mixed text and native equations in one Word COM transaction."""
        started_total = time.perf_counter()
        if not operations:
            raise WordToolkitError(ErrorCode.INVALID_INPUT, "operations must not be empty")
        if len(operations) > 200:
            raise WordToolkitError(
                ErrorCode.INVALID_INPUT,
                "At most 200 operations may be applied in one batch",
            )

        prepared: list[PreparedLiveOperation] = []
        equation_count = 0
        total_text = 0
        for index, item in enumerate(operations):
            if not isinstance(item, dict):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Each operation must be an object",
                    {"index": index},
                )
            operation_type = item.get("type")
            if operation_type == "text":
                text = item.get("text")
                runs_value = item.get("runs")
                if (text is None) == (runs_value is None):
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "Each text operation requires exactly one of text or runs",
                        {"index": index},
                    )
                runs: tuple[tuple[str, dict[str, Any]], ...] = ()
                if runs_value is not None:
                    if not isinstance(runs_value, list) or not runs_value or len(runs_value) > 1000:
                        raise WordToolkitError(
                            ErrorCode.INVALID_INPUT,
                            "runs must contain 1 to 1,000 items",
                            {"index": index},
                        )
                    parsed_runs: list[tuple[str, dict[str, Any]]] = []
                    for run_index, run in enumerate(runs_value):
                        if not isinstance(run, dict) or set(run) - {"text", "formatting"}:
                            raise WordToolkitError(
                                ErrorCode.INVALID_INPUT,
                                "Each inline run requires text and optional formatting",
                                {"index": index, "run": run_index},
                            )
                        run_text = run.get("text")
                        if not isinstance(run_text, str) or not run_text:
                            raise WordToolkitError(
                                ErrorCode.INVALID_INPUT,
                                "Each inline run requires non-empty text",
                                {"index": index, "run": run_index},
                            )
                        parsed_runs.append(
                            (
                                run_text,
                                self._normalize_text_formatting(
                                    run.get("formatting", {}),
                                    allow_paragraph_formatting=False,
                                ),
                            )
                        )
                    runs = tuple(parsed_runs)
                    text = "".join(run_text for run_text, _ in runs)
                style = item.get("style", "")
                if not isinstance(text, str) or not text:
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "Each text operation requires non-empty text",
                        {"index": index},
                    )
                if not isinstance(style, str) or len(style) > 128:
                    raise WordToolkitError(
                        ErrorCode.INVALID_INPUT,
                        "Text operation style must be a string of at most 128 characters",
                        {"index": index},
                    )
                if len(text) > 200_000:
                    raise WordToolkitError(
                        ErrorCode.LIMIT_EXCEEDED,
                        "One text operation exceeds 200,000 characters",
                        {"index": index},
                    )
                total_text += len(text)
                text_formatting = self._normalize_text_formatting(item.get("formatting", {}))
                prepared.append(
                    PreparedLiveTextOperation(
                        text=text,
                        runs=runs,
                        as_new_paragraph=bool(item.get("as_new_paragraph", False)),
                        style=style,
                        formatting=text_formatting,
                    )
                )
            elif operation_type == "equation":
                equation_count += 1
                try:
                    prepared_equation = self._prepare_equation_item(
                        item,
                        verify_readback_default=verify_readback,
                    )
                except WordToolkitError as exc:
                    details = dict(exc.details or {})
                    details["failed_operation_index"] = index
                    raise WordToolkitError(
                        exc.code,
                        exc.message,
                        details,
                        exc.retryable,
                    ) from exc
                prepared.append(PreparedLiveEquationOperation(prepared_equation))
            else:
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "Operation type must be 'text' or 'equation'",
                    {"index": index, "type": operation_type},
                )
        if total_text > 500_000:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "Combined text exceeds 500,000 characters",
            )
        if equation_count > 100:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "At most 100 equations may be applied in one batch",
            )

        record = self._record(owner, document_id)
        self._check_version(record, expected_version)
        prepared_equations = [
            item.equation for item in prepared if isinstance(item, PreparedLiveEquationOperation)
        ]
        failed_equation: PreparedLiveEquation | None = None
        failed_operation_index: int | None = None
        aggregate_batch_failure = False

        def operation(application: Any) -> dict[str, Any]:
            nonlocal failed_equation, failed_operation_index, aggregate_batch_failure
            started_com = time.perf_counter()
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            if activate:
                document.Activate()
            insertion_start = max(0, int(document.Content.End) - 1)
            previous = (
                str(document.Range(insertion_start - 1, insertion_start).Text or "")
                if insertion_start > 0
                else ""
            )
            chunks: list[str] = []
            segments: list[tuple[int, int]] = []
            offset = 0
            for prepared_operation in prepared:
                if isinstance(prepared_operation, PreparedLiveTextOperation):
                    raw = prepared_operation.text
                    as_new_paragraph = prepared_operation.as_new_paragraph
                else:
                    raw = prepared_operation.equation.linear
                    as_new_paragraph = prepared_operation.equation.display
                normalized = raw.replace("\r\n", "\n").replace("\r", "\n").replace("\n", "\r")
                prefix = ""
                suffix = ""
                if as_new_paragraph:
                    prefix = "" if insertion_start + offset == 0 or previous == "\r" else "\r"
                    suffix = "\r"
                piece = prefix + normalized + suffix
                segment_start = offset + _word_utf16_length(prefix)
                segment_end = segment_start + _word_utf16_length(normalized)
                chunks.append(piece)
                segments.append((segment_start, segment_end))
                offset += _word_utf16_length(piece)
                if piece:
                    previous = piece[-1]

            payload = "".join(chunks)
            target_range = document.Range(insertion_start, insertion_start)
            before_equations = int(document.OMaths.Count)
            operation_results: list[dict[str, Any] | None] = [None] * len(prepared)
            tracked_ranges: list[Any | None] = [None] * len(prepared)
            last_range = None

            with (
                self._screen_updates_suspended(
                    application,
                    enabled=optimize_screen_updates,
                ),
                self._undoable(
                    application,
                    document,
                    "WordToolkit: apply mixed live batch",
                ),
            ):
                target_range.Text = payload

                for index, prepared_operation in enumerate(prepared):
                    if not isinstance(prepared_operation, PreparedLiveTextOperation):
                        continue
                    failed_operation_index = index
                    relative_start, relative_end = segments[index]
                    inserted = document.Range(
                        insertion_start + relative_start,
                        insertion_start + relative_end,
                    )
                    if prepared_operation.style:
                        inserted.Style = prepared_operation.style
                    if prepared_operation.formatting:
                        self._apply_text_formatting(inserted, prepared_operation.formatting)
                    if prepared_operation.runs:
                        run_offset = relative_start
                        for run_text, run_formatting in prepared_operation.runs:
                            normalized_run_text = (
                                run_text.replace("\r\n", "\n")
                                .replace("\r", "\n")
                                .replace("\n", "\r")
                            )
                            run_end = run_offset + _word_utf16_length(normalized_run_text)
                            if run_formatting:
                                self._apply_text_formatting(
                                    document.Range(
                                        insertion_start + run_offset,
                                        insertion_start + run_end,
                                    ),
                                    run_formatting,
                                )
                            run_offset = run_end
                    tracked_ranges[index] = inserted.Duplicate
                    operation_results[index] = {
                        "type": "text",
                        "range": {},
                        "style": prepared_operation.style,
                        "formatting": prepared_operation.formatting,
                        "run_count": (
                            len(prepared_operation.runs) if prepared_operation.runs else 1
                        ),
                    }
                    failed_operation_index = None

                equation_positions = [
                    index
                    for index, item in enumerate(prepared)
                    if isinstance(item, PreparedLiveEquationOperation)
                ]
                for ordinal, index in reversed(list(enumerate(equation_positions, start=1))):
                    failed_operation_index = index
                    prepared_equation = cast(
                        PreparedLiveEquationOperation,
                        prepared[index],
                    ).equation
                    failed_equation = prepared_equation
                    relative_start, relative_end = segments[index]
                    equation_range = document.Range(
                        insertion_start + relative_start,
                        insertion_start + relative_end,
                    )
                    added_range = document.OMaths.Add(equation_range)
                    equation = added_range.OMaths.Item(1)
                    equation.BuildUp()
                    equation.Type = 0 if prepared_equation.display else 1
                    tracked_ranges[index] = equation.Range.Duplicate
                    verified_ast = (
                        self._verify_live_equation(
                            equation,
                            expected_ast=prepared_equation.ast,
                            required_symbol_groups=prepared_equation.required_symbol_groups,
                        )
                        if prepared_equation.verify_readback
                        else None
                    )
                    operation_results[index] = {
                        "type": "equation",
                        "equation": {
                            "index": before_equations + ordinal,
                            "display": prepared_equation.display,
                            "linear_input": prepared_equation.linear,
                            "range": {},
                            "native_verified": True,
                            "readback_verified": verified_ast is not None,
                            "ast": verified_ast,
                            "rules": list(prepared_equation.rules),
                            "features": list(prepared_equation.features),
                            "learning": prepared_equation.learning,
                        },
                    }
                    failed_equation = None
                    failed_operation_index = None

                after_equations = int(document.OMaths.Count)
                if after_equations != before_equations + equation_count:
                    aggregate_batch_failure = True
                    failed_operation_index = None
                    failed_equation = prepared_equations[-1] if prepared_equations else None
                    raise WordToolkitError(
                        ErrorCode.EQUATION_INVALID,
                        "Microsoft Word did not create the expected number of native equations",
                        {
                            "before": before_equations,
                            "after": after_equations,
                            "expected": equation_count,
                        },
                    )
                for index, result_item in enumerate(operation_results):
                    failed_operation_index = index
                    tracked = tracked_ranges[index]
                    if result_item is None or tracked is None:
                        raise WordToolkitError(
                            ErrorCode.INTERNAL_ERROR,
                            "A mixed Word operation produced no tracked range",
                            {"index": index},
                        )
                    range_data = {
                        "start": int(tracked.Start),
                        "end": int(tracked.End),
                    }
                    if result_item["type"] == "equation":
                        result_item["equation"]["range"] = range_data
                    else:
                        result_item["range"] = range_data
                    failed_operation_index = None
                last_range = tracked_ranges[-1]

            record.version += len(prepared)
            if last_range is not None:
                self._show_range(application, last_range, int(target_range.End))
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "operation_count": len(prepared),
                "text_operation_count": len(prepared) - equation_count,
                "equation_operation_count": equation_count,
                "operations": cast(list[dict[str, Any]], operation_results),
                "document": self._document_info(application, document),
                "performance": {
                    "com_attachments": 1,
                    "screen_updates_suspended": optimize_screen_updates,
                    "com_transaction_ms": round(
                        (time.perf_counter() - started_com) * 1000,
                        3,
                    ),
                },
            }

        try:
            result = self._execute(operation)
        except WordToolkitError as exc:
            details = dict(exc.details or {})
            if aggregate_batch_failure:
                details.update(
                    {
                        "failed_operation_index_available": False,
                        "failure_scope": "batch",
                    }
                )
                exc = WordToolkitError(exc.code, exc.message, details, exc.retryable)
            elif failed_operation_index is not None:
                details["failed_operation_index"] = failed_operation_index
                exc = WordToolkitError(exc.code, exc.message, details, exc.retryable)
            self._record_equation_learning(
                [failed_equation] if failed_equation is not None else [],
                success=False,
                duration_ms=(time.perf_counter() - started_total) * 1000,
                error=exc,
            )
            raise exc
        result["performance"]["total_ms"] = round(
            (time.perf_counter() - started_total) * 1000,
            3,
        )
        recorded = self._record_equation_learning(
            prepared_equations,
            success=True,
            duration_ms=result["performance"]["total_ms"],
        )
        result["learning_observations_recorded"] = len(prepared_equations) if recorded else 0
        return result

    def _verify_live_equation(
        self,
        equation: Any,
        *,
        expected_ast: dict[str, Any],
        required_symbol_groups: tuple[tuple[str, ...], ...] = (),
    ) -> dict[str, Any]:
        word_open_xml = str(equation.Range.WordOpenXML or "")
        if not word_open_xml:
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "Microsoft Word returned no OOXML for the inserted equation",
            )
        try:
            root = etree.fromstring(word_open_xml.encode("utf-8"))
            omath = next(root.iter(f"{M}oMath"), None)
            if omath is None:
                raise ValueError("m:oMath was not found")
            equation_text = "".join(omath.itertext())
            missing = [
                list(group)
                for group in required_symbol_groups
                if not any(symbol in equation_text for symbol in group)
            ]
            if missing:
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID,
                    "Microsoft Word dropped a required advanced equation symbol",
                    {"missing_symbol_groups": missing},
                )
            xml = etree.tostring(omath, encoding="unicode")
            actual_ast = self.math.parse(xml, "omml").to_dict()
            expected_text, expected_structure = self._equation_fidelity_contract(expected_ast)
            actual_text, actual_structure = self._equation_fidelity_contract(actual_ast)
            text_preserved = actual_text == expected_text
            structure_preserved = actual_structure == expected_structure
            if not text_preserved or not structure_preserved:
                expected_digest = hashlib.sha256(
                    repr((expected_text, expected_structure)).encode("utf-8")
                ).hexdigest()
                actual_digest = hashlib.sha256(
                    repr((actual_text, actual_structure)).encode("utf-8")
                ).hexdigest()
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID,
                    "Microsoft Word changed equation text or structure during native build-up",
                    {
                        "text_preserved": text_preserved,
                        "structure_preserved": structure_preserved,
                        "expected_structure_counts": self._equation_structure_counts(
                            expected_structure
                        ),
                        "actual_structure_counts": self._equation_structure_counts(
                            actual_structure
                        ),
                        "expected_contract_sha256": expected_digest,
                        "actual_contract_sha256": actual_digest,
                    },
                )
            return actual_ast
        except WordToolkitError:
            raise
        except Exception as exc:
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "The inserted Word equation could not be read back as native OMML",
                {"exception": type(exc).__name__},
            ) from exc

    def validate(self, owner: str, document_id: str) -> dict[str, Any]:
        record = self._record(owner, document_id)

        def operation(application: Any) -> dict[str, Any]:
            document = self._resolve_document(application, record)
            full_name = self._string_property(document, "FullName")
            suffix = Path(full_name).suffix.lower()
            if suffix != ".docx":
                raise WordToolkitError(
                    ErrorCode.UNSUPPORTED_FORMAT,
                    "Live OOXML validation requires an open DOCX document",
                    {"extension": suffix},
                )
            if not bool(document.Saved):
                raise WordToolkitError(
                    ErrorCode.VERSION_CONFLICT,
                    "The live document has unsaved changes; save the same document before OOXML validation",
                    retryable=True,
                )
            source = Path(full_name)
            if not source.is_file():
                raise WordToolkitError(
                    ErrorCode.DOCUMENT_NOT_FOUND,
                    "The saved live document path was not found",
                )
            with tempfile.TemporaryDirectory(prefix="wordtoolkit_live_") as work:
                snapshot = Path(work) / "live-snapshot.docx"
                shutil.copy2(source, snapshot)
                validation = self.validator.validate(snapshot)
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "native_equation_count": int(document.OMaths.Count),
                "validation": validation,
                "snapshot_only": True,
                "original_saved": bool(document.Saved),
            }

        return self._execute(operation)

    def save(
        self,
        owner: str,
        document_id: str,
        *,
        expected_version: int | None,
    ) -> dict[str, Any]:
        record = self._record(owner, document_id)
        self._check_version(record, expected_version)

        def operation(application: Any) -> dict[str, Any]:
            self._check_version(record, expected_version)
            document = self._resolve_document(application, record)
            self._ensure_editable(document)
            if not self._string_property(document, "Path"):
                raise WordToolkitError(
                    ErrorCode.INVALID_INPUT,
                    "The connected document has no file path; save it in Word before using live save",
                )
            document.Save()
            return {
                "live_document_id": record.document_id,
                "live_version": record.version,
                "saved": bool(document.Saved),
                "document": self._document_info(application, document),
            }

        return self._execute(operation)

    def disconnect(self, owner: str, document_id: str) -> dict[str, Any]:
        self._require_available()
        with self._lock:
            record = self._record(owner, document_id)
            del self._records[document_id]
            return {
                "live_document_id": record.document_id,
                "disconnected": True,
            }
