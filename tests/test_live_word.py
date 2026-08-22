from __future__ import annotations

import re
from contextlib import contextmanager
from pathlib import Path
from typing import Any

import pytest

from wordtoolkit.config import Settings
from wordtoolkit.errors import ErrorCode, WordToolkitError
from wordtoolkit.live_member_capabilities import (
    PreparedMemberOperation,
    build_member_capability_registry,
)
from wordtoolkit.live_word import LiveWordBridge, _word_utf16_length
from wordtoolkit.math import MathEngine

_FAKE_MATH = MathEngine()


def _fake_word_open_xml(value: str) -> str:
    omml = _FAKE_MATH.convert(value, "unicodemath", "omml", display=False)
    return (
        '<w:document xmlns:w="http://schemas.openxmlformats.org/'
        'wordprocessingml/2006/main" '
        'xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">'
        f"<w:body><w:p>{omml}</w:p></w:body>"
        "</w:document>"
    )


def _fake_latex_word_open_xml(value: str) -> str:
    omml = _FAKE_MATH.convert(value, "latex", "omml", display=False)
    return (
        '<w:document xmlns:w="http://schemas.openxmlformats.org/'
        'wordprocessingml/2006/main" '
        'xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">'
        f"<w:body><w:p>{omml}</w:p></w:body>"
        "</w:document>"
    )


def _fake_literal_word_open_xml(value: str) -> str:
    return (
        '<w:document xmlns:w="http://schemas.openxmlformats.org/'
        'wordprocessingml/2006/main" '
        'xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">'
        f"<w:body><w:p><m:oMath><m:r><m:t>{value}</m:t></m:r></m:oMath></w:p></w:body>"
        "</w:document>"
    )


class FakeCount:
    def __init__(self, value):
        self.value = value

    @property
    def Count(self) -> int:
        return int(self.value() if callable(self.value) else self.value)


class FakeTypedItem:
    def __init__(self, type_value: int):
        self.Type = type_value


class FakeTypedCollection:
    def __init__(self, type_values: list[int]):
        self.items = [FakeTypedItem(value) for value in type_values]

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeTypedItem:
        return self.items[index - 1]


class FakeStructureItem:
    def __init__(self, **properties):
        for name, value in properties.items():
            setattr(self, name, value)


class FakeStructureCollection:
    def __init__(self, items: list[Any]):
        self.items = items

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int):
        return self.items[index - 1]


class FakeFormattingObject:
    def __init__(self, document, target_range, prefix: str):
        object.__setattr__(self, "document", document)
        object.__setattr__(self, "target_range", target_range)
        object.__setattr__(self, "prefix", prefix)

    def __setattr__(self, name: str, value) -> None:
        if name in {"document", "target_range", "prefix"}:
            object.__setattr__(self, name, value)
            return
        if self.document.fail_format_property == name:
            raise RuntimeError(f"simulated formatting failure for {name}")
        self.document.formatting_log[f"{self.prefix}.{name}"] = value
        self.document.formatting_ranges.append(
            (self.prefix, name, self.target_range.Start, self.target_range.End, value)
        )
        object.__setattr__(self, name, value)


class FakeParagraphFormat(FakeFormattingObject):
    def __init__(self, document, target_range):
        super().__init__(document, target_range, "paragraph")
        object.__setattr__(self, "Alignment", 0)
        object.__setattr__(self, "KeepWithNext", 0)
        object.__setattr__(self, "KeepTogether", 0)
        object.__setattr__(self, "PageBreakBefore", 0)
        object.__setattr__(self, "WidowControl", -1)
        object.__setattr__(self, "OutlineLevel", 10)


class FakeFont(FakeFormattingObject):
    def __init__(self, document, target_range):
        super().__init__(document, target_range, "font")


class FakeFind:
    def __init__(self, target_range):
        self.target_range = target_range

    def ClearFormatting(self) -> None:
        return None

    def Execute(
        self,
        *,
        FindText: str,
        MatchCase: bool,
        MatchWholeWord: bool,
        MatchWildcards: bool,
        Forward: bool,
        Wrap: int,
        Format: bool,
    ) -> bool:
        assert Forward is True
        assert Wrap == 0
        assert Format is False
        needle = FindText
        if MatchWildcards:
            needle = (
                needle.replace("^p", "\r")
                .replace("^t", "\t")
                .replace("^m", "\x0c")
                .replace("^s", "\u00a0")
            )
        haystack = self.target_range.document.text[self.target_range.Start : self.target_range.End]
        if MatchCase:
            match = re.search(
                rf"(?<!\w){re.escape(needle)}(?!\w)" if MatchWholeWord else re.escape(needle),
                haystack,
            )
        else:
            match = re.search(
                rf"(?<!\w){re.escape(needle)}(?!\w)" if MatchWholeWord else re.escape(needle),
                haystack,
                flags=re.IGNORECASE,
            )
        if match is None:
            return False
        base = self.target_range.Start
        self.target_range.Start = base + match.start()
        self.target_range.End = base + match.end()
        return True


class FakeList:
    def __init__(self, list_range, list_type: int):
        self.Range = list_range
        self.Range.ListFormat.ListType = list_type
        stripped = self.Range.Text.rstrip("\r")
        self.item_count = max(1, stripped.count("\r") + 1)


class FakeLists:
    def __init__(self):
        self.items: list[FakeList] = []
        self.fail_apply = False

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeList:
        return self.items[index - 1]

    def Add(self, list_range, list_type: int) -> FakeList:
        if self.fail_apply:
            raise RuntimeError("simulated list-formatting failure")
        item = FakeList(list_range, list_type)
        self.items.append(item)
        return item


class FakeListFormat:
    def __init__(self, document, list_range):
        self.document = document
        self.list_range = list_range
        self.ListType = 0
        self.default_behavior = None

    def ApplyBulletDefault(self, default_behavior: int) -> None:
        self.default_behavior = default_behavior
        self.document.Lists.Add(self.list_range, 2)

    def ApplyNumberDefault(self, default_behavior: int) -> None:
        self.default_behavior = default_behavior
        self.document.Lists.Add(self.list_range, 3)


class FakeRange:
    def __init__(self, document, start: int, end: int):
        self.document = document
        self.Start = start
        self.End = end
        self._style = ""
        self._highlight_color_index = 0
        self.Font = FakeFont(document, self)
        self.ParagraphFormat = FakeParagraphFormat(document, self)
        self.ListFormat = FakeListFormat(document, self)
        self.StoryType = 1
        self.Paragraphs = FakeCount(lambda: self.Text.count("\r"))
        self.Tables = FakeCount(0)
        self.Fields = FakeCount(0)
        self.OMaths = FakeCount(0)
        self.NextStoryRange = None
        self.word_open_xml_override: str | None = None

    @property
    def Style(self) -> str:
        return self._style

    @Style.setter
    def Style(self, value: str) -> None:
        self._style = value
        if value:
            self.document.formatting_log["style"] = value

    @property
    def HighlightColorIndex(self) -> int:
        return self._highlight_color_index

    @HighlightColorIndex.setter
    def HighlightColorIndex(self, value: int) -> None:
        if self.document.fail_format_property == "HighlightColorIndex":
            raise RuntimeError("simulated formatting failure for HighlightColorIndex")
        self._highlight_color_index = value
        self.document.formatting_log["range.HighlightColorIndex"] = value
        self.document.formatting_ranges.append(
            ("range", "HighlightColorIndex", self.Start, self.End, value)
        )

    @property
    def WordOpenXML(self) -> str:
        override = self.word_open_xml_override or self.document.word_open_xml_override
        return override if override is not None else _fake_word_open_xml(self.Text)

    @property
    def Duplicate(self):
        return FakeRange(self.document, self.Start, self.End)

    @property
    def Find(self) -> FakeFind:
        return FakeFind(self)

    @property
    def Text(self) -> str:
        return self.document.text[self.Start : self.End]

    @Text.setter
    def Text(self, value: str) -> None:
        if self.document.fail_text_value is not None and value == self.document.fail_text_value:
            raise RuntimeError("simulated range text failure")
        self.document.text_write_count += 1
        self.document.text = (
            self.document.text[: self.Start] + value + self.document.text[self.End :]
        )
        self.End = self.Start + len(value)
        self.document.Saved = False

    def Collapse(self, direction: int) -> None:
        if direction == 0:
            self.Start = self.End
        else:
            self.End = self.Start

    def SetRange(self, start: int, end: int) -> None:
        self.Start = start
        self.End = end

    def ConvertToTable(self, separator: int, rows: int, columns: int):
        return self.document.Tables.ConvertFromRange(self, separator, rows, columns)


class FakeParagraph:
    def __init__(self, document, index: int, start: int, end: int):
        self.document = document
        self.index = index
        self._range = FakeRange(document, start, end)
        self._range._style = document.paragraph_styles.get(index, "Normal")
        self.Format = FakeParagraphFormat(document, self._range)
        for name, value in document.paragraph_format_values.get(index, {}).items():
            object.__setattr__(self.Format, name, value)
        self._range.ParagraphFormat = self.Format
        self.OutlineLevel = int(
            document.paragraph_format_values.get(index, {}).get("OutlineLevel", 10)
        )

    @property
    def Range(self) -> FakeRange:
        return self._range


class FakeParagraphs:
    def __init__(self, document):
        self.document = document

    def _ranges(self) -> list[tuple[int, int]]:
        ranges: list[tuple[int, int]] = []
        start = 0
        for match in re.finditer(r".*?\r|.+$", self.document.text):
            end = match.end()
            ranges.append((start, end))
            start = end
        return ranges

    @property
    def Count(self) -> int:
        return len(self._ranges())

    def Item(self, index: int) -> FakeParagraph:
        ranges = self._ranges()
        start, end = ranges[index - 1]
        return FakeParagraph(self.document, index, start, end)

    def __call__(self, index: int) -> FakeParagraph:
        return self.Item(index)


class FakeBookmark:
    def __init__(self, name: str, bookmark_range: FakeRange | None = None):
        self.Name = name
        self.Range = bookmark_range


class FakeBookmarks:
    def __init__(self, document, names: set[str]):
        self.document = document
        self.items = {
            name.casefold(): FakeBookmark(name, FakeRange(document, 0, 0)) for name in names
        }
        self.add_calls = 0
        self.fail_add = False

    @property
    def Count(self) -> int:
        return len(self.items)

    def Exists(self, name: str) -> bool:
        return name.casefold() in self.items

    def Add(self, name: str, bookmark_range: FakeRange) -> FakeBookmark:
        self.add_calls += 1
        if self.fail_add:
            raise RuntimeError("simulated bookmark-add failure")
        bookmark = FakeBookmark(name, bookmark_range.Duplicate)
        self.items[name.casefold()] = bookmark
        self.document.Saved = False
        return bookmark

    def Item(self, name: str | int) -> FakeBookmark:
        if isinstance(name, int):
            return list(self.items.values())[name - 1]
        return self.items[name.casefold()]


class FakeField:
    def __init__(
        self,
        collection,
        field_type: int,
        field_text: str,
        result_range: FakeRange,
        preserve_formatting: bool,
    ):
        self.collection = collection
        self.Type = field_type
        self.field_text = field_text
        self.Result = result_range
        self.preserve_formatting = preserve_formatting
        self.update_calls = 0

    def Update(self) -> bool:
        self.update_calls += 1
        return not self.collection.fail_update


class FakeFields:
    def __init__(self, document, type_values: list[int]):
        self.document = document
        self.items = [
            FakeField(self, field_type, "", FakeRange(document, 0, 0), True)
            for field_type in type_values
        ]
        self.fail_add = False
        self.fail_update = False
        self.actual_type_override: int | None = None
        self.add_calls = 0

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeField:
        return self.items[index - 1]

    def Add(
        self,
        field_range: Any,
        field_type: int,
        field_text: str,
        preserve_formatting: bool,
    ) -> FakeField:
        self.add_calls += 1
        if self.fail_add:
            raise RuntimeError("simulated field-add failure")
        display = "4" if field_type == 34 else "1"
        cell = getattr(field_range, "cell", None)
        if cell is None:
            self.document.text = (
                self.document.text[: field_range.Start]
                + display
                + self.document.text[field_range.End :]
            )
        else:
            cell.text = display
        result_range = FakeRange(
            self.document,
            field_range.Start,
            field_range.Start + len(display),
        )
        field = FakeField(
            self,
            self.actual_type_override or field_type,
            field_text,
            result_range,
            preserve_formatting,
        )
        self.items.append(field)
        if cell is not None:
            cell.fields.append(field)
        self.document.Saved = False
        return field


class FakeOMath:
    def __init__(self, equation_range: FakeRange):
        self.Range = equation_range
        self.built_up = False
        self.Type = 1

    def BuildUp(self) -> None:
        self.built_up = True


class FakeRow:
    def __init__(self):
        self.HeadingFormat = 0


class FakeRows:
    def __init__(self, count: int):
        self.items = [FakeRow() for _index in range(count)]
        self.Alignment = 0

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeRow:
        return self.items[index - 1]


class FakeCellFields:
    def __init__(self, cell):
        self.cell = cell

    @property
    def Count(self) -> int:
        return len(self.cell.fields)

    def Item(self, index: int) -> FakeField:
        return self.cell.fields[index - 1]


class FakeCellRange:
    def __init__(self, cell):
        self.cell = cell
        self.Start = cell.start
        self.End = cell.start + len(cell.text) + 2
        self.StoryType = 1
        self.Fields = FakeCellFields(cell)

    @property
    def Duplicate(self):
        return FakeCellRange(self.cell)

    @property
    def Text(self) -> str:
        return self.cell.text + "\r\x07"

    @Text.setter
    def Text(self, value: str) -> None:
        self.cell.document.text_write_count += 1
        for field in self.cell.fields:
            self.cell.document.Fields.items.remove(field)
        self.cell.fields.clear()
        self.cell.text = value.rstrip("\r\x07")
        self.End = self.Start + len(self.cell.text) + 2
        self.cell.document.Saved = False


class FakeCell:
    def __init__(self, table, row: int, column: int, text: str):
        self.table = table
        self.document = table.Range.document
        self.row = row
        self.column = column
        self.text = text
        self.start = int(table.Range.Start) + ((row - 1) * table.column_count + column)
        self.fields: list[FakeField] = []
        self.formula_calls = 0
        self.formula_expression = ""
        self.numeric_format = ""

    @property
    def Range(self) -> FakeCellRange:
        return FakeCellRange(self)

    def Formula(self, expression: str, numeric_format: str) -> None:
        self.formula_calls += 1
        if self.table.fail_formula:
            raise RuntimeError("simulated cell-formula failure")
        for field in self.fields:
            self.document.Fields.items.remove(field)
        self.fields.clear()
        self.text = ""
        result_range = FakeRange(self.document, self.start, self.start + 1)
        field = FakeField(
            self.document.Fields,
            self.document.Fields.actual_type_override or 34,
            expression,
            result_range,
            True,
        )
        self.fields.append(field)
        self.document.Fields.items.append(field)
        self.document.Fields.add_calls += 1
        self.formula_expression = expression
        self.numeric_format = numeric_format
        self.document.Saved = False


class FakeTableFields:
    def __init__(self, table):
        self.table = table
        self.update_calls = 0

    @property
    def items(self) -> list[FakeField]:
        return [
            field
            for row in range(1, self.table.row_count + 1)
            for column in range(1, self.table.column_count + 1)
            for field in self.table.cells[(row, column)].fields
        ]

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeField:
        return self.items[index - 1]

    def Update(self) -> int:
        self.update_calls += 1
        for index, field in enumerate(self.items, start=1):
            if not field.Update():
                return index
        if self.items:
            self.table.Range.document.Saved = False
        return 0


class FakeTable:
    def __init__(self, table_range: FakeRange, rows: int, columns: int):
        self.Range = table_range
        self.Rows = FakeRows(rows)
        self.Columns = FakeCount(columns)
        self.row_count = rows
        self.column_count = columns
        self.Style = ""
        self.AllowAutoFit = False
        self.autofit_behavior = None
        self.ApplyStyleHeadingRows = False
        self.fail_formula = False
        source_rows = table_range.Text.split("\r")
        self.cells: dict[tuple[int, int], FakeCell] = {}
        for row in range(1, rows + 1):
            values = source_rows[row - 1].split("\t")
            for column in range(1, columns + 1):
                self.cells[(row, column)] = FakeCell(
                    self,
                    row,
                    column,
                    values[column - 1],
                )
        self.Range.Fields = FakeTableFields(self)

    def AutoFitBehavior(self, behavior: int) -> None:
        self.autofit_behavior = behavior

    def Cell(self, row: int, column: int) -> FakeCell:
        return self.cells[(row, column)]


class FakeTables:
    def __init__(self):
        self.items: list[FakeTable] = []
        self.fail_convert = False

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeTable:
        return self.items[index - 1]

    def ConvertFromRange(
        self,
        table_range: FakeRange,
        separator: int,
        rows: int,
        columns: int,
    ) -> FakeTable:
        if self.fail_convert:
            raise RuntimeError("simulated table conversion failure")
        assert separator == 1
        table = FakeTable(table_range, rows, columns)
        self.items.append(table)
        return table


class FakeOMaths:
    def __init__(self):
        self.items: list[FakeOMath] = []

    @property
    def Count(self) -> int:
        return len(self.items)

    def Add(self, equation_range: FakeRange):
        if equation_range.document.word_open_xml_queue:
            equation_range.word_open_xml_override = equation_range.document.word_open_xml_queue.pop(
                0
            )
        equation = FakeOMath(equation_range)
        self.items.append(equation)
        equation_range.OMaths = FakeSingleOMaths(equation)
        return equation_range

    def Item(self, index: int) -> FakeOMath:
        return self.items[index - 1]


class FakeTextRange:
    def __init__(self, text: str, start: int = -1, end: int = -1):
        self.Text = text
        self.Start = start
        self.End = end


class FakeReply:
    def __init__(self, collection, text: str):
        self.collection = collection
        self.Author = "WordToolkit Tester"
        self.Date = "2026-07-20T12:00:00"
        self.Range = FakeTextRange(text)

    @property
    def Index(self) -> int:
        return self.collection.items.index(self) + 1


class FakeReplies:
    def __init__(self):
        self.items: list[FakeReply] = []

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeReply:
        return self.items[index - 1]

    def __call__(self, index: int) -> FakeReply:
        return self.Item(index)

    def Add(self, _scope, text: str) -> FakeReply:
        reply = FakeReply(self, text)
        self.items.append(reply)
        return reply


class FakeComment:
    def __init__(self, collection, scope: FakeRange, text: str):
        self.collection = collection
        self.Scope = scope
        self.Range = FakeTextRange(text)
        self.Author = "WordToolkit Tester"
        self.Initial = "WT"
        self.Date = "2026-07-20T12:00:00"
        self.Done = False
        self.Replies = FakeReplies()

    @property
    def Index(self) -> int:
        return self.collection.items.index(self) + 1

    def Delete(self) -> None:
        self.collection.items.remove(self)


class FakeComments:
    def __init__(self, document):
        self.document = document
        self.items = [
            FakeComment(self, FakeRange(document, 0, 8), "First comment"),
            FakeComment(self, FakeRange(document, 9, 17), "Second comment"),
        ]

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeComment:
        return self.items[index - 1]

    def __call__(self, index: int) -> FakeComment:
        return self.Item(index)

    def Add(self, scope: FakeRange, text: str) -> FakeComment:
        comment = FakeComment(self, scope.Duplicate, text)
        self.items.append(comment)
        self.document.Saved = False
        return comment


class FakeRevision:
    def __init__(self, collection, revision_type: int, start: int, end: int):
        self.collection = collection
        self.Type = revision_type
        self.Author = "WordToolkit Tester"
        self.Date = "2026-07-20T12:00:00"
        self.Range = FakeRange(collection.document, start, end)
        self.decision = ""

    def Accept(self) -> None:
        self.decision = "accept"
        self.collection.items.remove(self)

    def Reject(self) -> None:
        self.decision = "reject"
        self.collection.items.remove(self)


class FakeRevisions:
    def __init__(self, document):
        self.document = document
        self.items = [FakeRevision(self, 1, 0, 8)]

    @property
    def Count(self) -> int:
        return len(self.items)

    def Item(self, index: int) -> FakeRevision:
        return self.items[index - 1]

    def __call__(self, index: int) -> FakeRevision:
        return self.Item(index)


class FakeSingleOMaths:
    def __init__(self, equation: FakeOMath):
        self.equation = equation

    def Item(self, index: int) -> FakeOMath:
        assert index == 1
        return self.equation


class FakeDocuments:
    def __init__(self, documents: list[Any]):
        self.documents = documents

    @property
    def Count(self) -> int:
        return len(self.documents)

    def Item(self, index: int):
        return self.documents[index - 1]


class FakeStoryRanges:
    def __init__(self, document):
        self.document = document

    def Item(self, story_type: int):
        if story_type != 1:
            raise RuntimeError("story is not present")
        return self.document.Content


class FakeSelection:
    def __init__(self, document, start: int, end: int):
        self.document = document
        self.Start = start
        self.End = end
        self.Type = 1

    @property
    def Range(self) -> FakeRange:
        return FakeRange(self.document, self.Start, self.End)

    def SetRange(self, start: int, end: int) -> None:
        self.Start = start
        self.End = end


class FakeWindow:
    Hwnd = 12345

    def ScrollIntoView(self, _target_range, _start: bool) -> None:
        return None


class FakeUndoRecord:
    def __init__(self, application):
        self.application = application
        self.started = 0
        self.ended = 0
        self.current_label = ""
        self.snapshot: tuple[Any, str, bool] | None = None
        self.fail_end_once = False

    def StartCustomRecord(self, label: str) -> None:
        self.started += 1
        self.current_label = label
        document = self.application.ActiveDocument
        self.snapshot = (document, document.text, document.Saved)

    def EndCustomRecord(self) -> None:
        self.ended += 1
        if self.fail_end_once:
            self.fail_end_once = False
            raise RuntimeError("simulated EndCustomRecord failure")
        if self.current_label:
            self.application.undo_entries.insert(0, self.current_label)
            assert self.snapshot is not None
            self.application.undo_snapshots.insert(0, self.snapshot)
            self.current_label = ""
            self.snapshot = None


class FakeUndoControl:
    def __init__(self, application):
        self.application = application

    @property
    def ListCount(self) -> int:
        return len(self.application.undo_entries)

    def List(self, index: int) -> str:
        return self.application.undo_entries[index - 1]


class FakeCommandBars:
    def __init__(self, application):
        self.application = application
        self.available = True

    def FindControl(self, *, Type: int, Id: int):
        assert Type == 6
        assert Id == 128
        return FakeUndoControl(self.application) if self.available else None


class FakeOleDispatch:
    def __init__(self):
        self.invoke_calls: list[tuple[Any, ...]] = []

    def Invoke(self, *arguments):
        self.invoke_calls.append(arguments)
        return None


class FakeDocument:
    def __init__(self, name: str, path: Path):
        self.Name = name
        self.FullName = str(path)
        self.Path = str(path.parent)
        self.Saved = True
        self.ReadOnly = False
        self.ProtectionType = -1
        self.Final = False
        self.CompatibilityMode = 15
        self.text = "Existing paragraph\r"
        self.text_write_count = 0
        self.fail_text_value: str | None = None
        self.formatting_log: dict[str, Any] = {}
        self.formatting_ranges: list[tuple[str, str, int, int, Any]] = []
        self.fail_format_property: str | None = None
        self.word_open_xml_override: str | None = None
        self.word_open_xml_queue: list[str] = []
        self.paragraph_styles: dict[int, str] = {}
        self.paragraph_format_values: dict[int, dict[str, Any]] = {}
        self.Paragraphs = FakeParagraphs(self)
        self.Sections = FakeCount(2)
        self.Styles = FakeTypedCollection([1, 1, 2, 3])
        self.Tables = FakeTables()
        self.OMaths = FakeOMaths()
        self.Fields = FakeFields(self, [3, 3, 88])
        self.FormFields = FakeTypedCollection([])
        self.Bookmarks = FakeBookmarks(self, {"KnownBookmark", "Eq_1"})
        self.Hyperlinks = FakeCount(1)
        self.Comments = FakeComments(self)
        self.Revisions = FakeRevisions(self)
        self._track_revisions = False
        self.fail_track_value: bool | None = None
        self.ContentControls = FakeTypedCollection([8])
        self.InlineShapes = FakeTypedCollection([3])
        self.Shapes = FakeTypedCollection([1, 17])
        self.Footnotes = FakeCount(0)
        self.Endnotes = FakeCount(0)
        self.Lists = FakeLists()
        existing_list_range = FakeRange(self, 0, len("Existing paragraph"))
        self.Lists.Add(existing_list_range, 3)
        self.ListParagraphs = FakeCount(lambda: sum(item.item_count for item in self.Lists.items))
        self.Subdocuments = FakeCount(0)
        self.Variables = FakeCount(1)
        self.TablesOfContents = FakeCount(1)
        self.TablesOfFigures = FakeCount(0)
        self.TablesOfAuthorities = FakeCount(0)
        self.StoryRanges = FakeStoryRanges(self)
        self._oleobj_ = FakeOleDispatch()
        self._application = None
        self.save_calls = 0
        self.undo_calls = 0

    @property
    def Content(self) -> FakeRange:
        return FakeRange(self, 0, len(self.text))

    @property
    def TrackRevisions(self) -> bool:
        return self._track_revisions

    @TrackRevisions.setter
    def TrackRevisions(self, value: bool) -> None:
        normalized = bool(value)
        if self.fail_track_value is not None and normalized == self.fail_track_value:
            self.fail_track_value = None
            raise RuntimeError("simulated TrackRevisions assignment failure")
        self._track_revisions = normalized

    def Range(self, start: int, end: int) -> FakeRange:
        return FakeRange(self, start, end)

    def Activate(self) -> None:
        self._application.ActiveDocument = self
        self._application.Selection = FakeSelection(self, len(self.text) - 1, len(self.text) - 1)

    def Save(self) -> None:
        self.Saved = True
        self.save_calls += 1

    def SaveCopyAs(self, output: str) -> None:
        Path(output).write_bytes(b"fake-live-snapshot")

    def Undo(self, _times: int) -> bool:
        self.undo_calls += 1
        if self._application.undo_entries:
            self._application.undo_entries.pop(0)
            if self._application.undo_snapshots:
                document, text, saved = self._application.undo_snapshots.pop(0)
                document.text = text
                document.Saved = saved
            return True
        return False


class FakeApplication:
    def __init__(self, documents: list[FakeDocument]):
        self.Documents = FakeDocuments(documents)
        self.ActiveDocument = documents[0]
        self.Selection = FakeSelection(documents[0], 0, 0)
        self.ActiveWindow = FakeWindow()
        self.undo_entries: list[str] = []
        self.undo_snapshots: list[tuple[Any, str, bool]] = []
        self.CommandBars = FakeCommandBars(self)
        self.UndoRecord = FakeUndoRecord(self)
        self.UserName = "WordToolkit Tester"
        self.Visible = True
        self.ScreenUpdating = True
        self.international = {
            17: ",",
            18: ".",
            19: ",",
        }
        for document in documents:
            document._application = self

    def International(self, index: int) -> str:
        return self.international[index]


class FakeBackend:
    def __init__(self, application: FakeApplication):
        self.application = application
        self.attach_calls = 0

    @contextmanager
    def attach(self):
        self.attach_calls += 1
        yield self.application


class FakeValidator:
    def validate(self, path: Path) -> dict:
        assert path.read_bytes() == b"fake-live-snapshot"
        return {"valid": True, "errors": 0, "issues": []}


@pytest.fixture
def live_bridge(tmp_path: Path):
    source = tmp_path / "Live.docx"
    source.write_bytes(b"fake-live-snapshot")
    document = FakeDocument("Live.docx", source)
    application = FakeApplication([document])
    bridge = LiveWordBridge(
        Settings(auth_mode="local_stdio", storage_root=tmp_path / "storage"),
        FakeValidator(),  # type: ignore[arg-type]
        backend=FakeBackend(application),
    )
    return bridge, application, document


def test_live_word_list_connect_inspect_and_selection(live_bridge) -> None:
    bridge, _application, document = live_bridge

    listing = bridge.list_documents()
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]
    inspected = bridge.inspect("owner", document_id)
    selection = bridge.selection("owner", document_id)

    assert listing["word_running"] is True
    assert listing["documents"][0]["name"] == "Live.docx"
    assert inspected["document"]["paragraph_count"] == 1
    assert selection["selection"]["collapsed"] is True
    assert document.Name == connected["document"]["name"]


def test_live_word_maps_stories_and_structures_without_returning_content(
    live_bridge,
) -> None:
    bridge, _application, _document = live_bridge
    connected = bridge.connect("owner", use_active=True)

    result = bridge.structure_map(
        "owner",
        connected["live_document_id"],
        include_type_histograms=True,
    )

    assert result["structures"]["sections"] == 2
    assert result["structures"]["fields"] == 3
    assert result["structures"]["comments"] == 2
    assert result["structures"]["content_controls"] == 1
    assert result["structures"]["lists"] == 1
    assert result["structures"]["list_paragraphs"] == 1
    assert result["type_histograms"]["field_types"]["types"] == {"3": 2, "88": 1}
    assert result["type_histograms"]["content_control_types"]["types"] == {"8": 1}
    assert result["type_histograms"]["floating_shape_types"]["types"] == {
        "1": 1,
        "17": 1,
    }
    assert result["type_histograms"]["list_types"]["types"] == {"3": 1}
    assert result["stories"] == [
        {
            "story_type": 1,
            "name": "main_text",
            "instances": 1,
            "character_count": len("Existing paragraph\r"),
            "paragraph_count": 1,
            "table_count": 0,
            "field_count": 0,
            "equation_count": 0,
            "truncated": False,
        }
    ]
    assert "comments" not in result["live_edit_support"]["present_but_not_live_editable"]
    assert "revisions" not in result["live_edit_support"]["present_but_not_live_editable"]
    assert "lists" not in result["live_edit_support"]["present_but_not_live_editable"]
    assert result["content_returned"] is False
    assert "Existing paragraph" not in str(result)


def test_live_word_adaptively_rescans_structure_types(live_bridge) -> None:
    bridge, _application, _document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]

    first = bridge.structure_map("owner", document_id)
    second = bridge.structure_map("owner", document_id)
    third = bridge.structure_map("owner", document_id)
    learned = bridge.inspect_structure_learning()

    expected_scans = {
        "field_types",
        "content_control_types",
        "revision_types",
        "inline_shape_types",
        "floating_shape_types",
        "style_types",
        "list_types",
    }
    assert set(first["type_histograms"]) == expected_scans
    assert set(second["type_histograms"]) == expected_scans
    assert third["type_histograms"] == {}
    assert first["type_histogram_scan_reasons"]["field_types"] == "adaptive_due"
    assert third["structure_learning"]["observation_recorded"] is True
    assert third["structure_learning"]["document_content_stored"] is False
    assert third["structure_learning"]["document_counts_stored"] is False
    assert learned["observation_count"] == 3
    assert learned["content_stored"] is False
    assert learned["document_counts_stored"] is False
    assert learned["path_exposed"] is False
    assert "Existing paragraph" not in str(learned)
    assert str(document_id) not in str(learned)


def test_live_word_inspects_bounded_structure_items_without_storing_values(
    live_bridge,
) -> None:
    bridge, _application, document = live_bridge
    document.ContentControls = FakeStructureCollection(
        [
            FakeStructureItem(
                ID=41,
                Type=8,
                Title="Customer name",
                Tag="CustomerTag",
                Range=FakeRange(document, 0, len("Existing")),
                LockContents=False,
                LockContentControl=True,
                ShowingPlaceholderText=False,
            ),
            FakeStructureItem(
                ID=42,
                Type=6,
                Title="Approval date",
                Tag="ApprovalTag",
                Range=FakeRange(document, 9, len("Existing paragraph")),
                LockContents=True,
                LockContentControl=False,
                ShowingPlaceholderText=True,
            ),
        ]
    )
    connected = bridge.connect("owner", use_active=True)

    result = bridge.inspect_structure_items(
        "owner",
        connected["live_document_id"],
        structure="content_controls",
        offset=0,
        limit=1,
        include_text=True,
        max_text_chars=5,
    )
    learned = bridge.inspect_structure_learning()
    raw = bridge.structure_learning.path.read_text(encoding="utf-8")

    assert result["available"] is True
    assert result["total_count"] == 2
    assert result["returned_count"] == 1
    assert result["truncated"] is True
    assert result["items"][0]["properties"]["id"] == 41
    assert result["items"][0]["properties"]["type"] == 8
    assert result["items"][0]["properties"]["title"] == "Customer name"
    assert result["items"][0]["properties"]["tag"] == "CustomerTag"
    assert result["items"][0]["properties"]["range"] == {
        "start": 0,
        "end": len("Existing"),
        "character_count": len("Existing"),
        "story_type": 1,
    }
    assert result["items"][0]["text_preview"] == "Exist"
    assert result["items"][0]["text_truncated"] is True
    assert result["text_content_returned"] is True
    assert result["external_addresses_returned"] is False
    assert result["field_codes_returned"] is False
    assert result["property_learning"]["observation_recorded"] is True
    assert result["property_learning"]["property_values_stored"] is False
    assert result["performance"]["com_attachments"] == 1
    assert result["performance"]["collection_item_reads"] == 1
    assert result["performance"]["property_read_attempts"] == 8
    assert result["performance"]["text_read_attempts"] == 1
    assert learned["inspection_observation_count"] == 1
    assert learned["property_values_stored"] is False
    assert "Customer name" not in raw
    assert "CustomerTag" not in raw
    assert "Existing" not in raw


def test_live_word_adaptively_skips_repeatedly_unavailable_properties(
    live_bridge,
) -> None:
    bridge, _application, document = live_bridge
    document.ContentControls = FakeStructureCollection(
        [
            FakeStructureItem(
                ID=1,
                Type=8,
                Title="Known",
                Range=FakeRange(document, 0, 3),
                LockContents=False,
                LockContentControl=False,
                ShowingPlaceholderText=False,
            )
        ]
    )
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]

    first = bridge.inspect_structure_items(
        "owner",
        document_id,
        structure="content_controls",
    )
    second = bridge.inspect_structure_items(
        "owner",
        document_id,
        structure="content_controls",
    )
    third = bridge.inspect_structure_items(
        "owner",
        document_id,
        structure="content_controls",
    )

    assert "tag" in first["items"][0]["unavailable_properties"]
    assert "tag" in second["items"][0]["unavailable_properties"]
    assert "tag" in third["properties_skipped"]
    assert "tag" not in third["properties_probed"]
    assert "tag" not in third["items"][0]["unavailable_properties"]


def test_live_word_structure_item_inspection_rejects_unbounded_requests(
    live_bridge,
) -> None:
    bridge, _application, _document = live_bridge
    connected = bridge.connect("owner", use_active=True)

    with pytest.raises(WordToolkitError) as structure_error:
        bridge.inspect_structure_items(
            "owner",
            connected["live_document_id"],
            structure="not_a_word_collection",
        )
    assert structure_error.value.code is ErrorCode.INVALID_INPUT

    with pytest.raises(WordToolkitError) as limit_error:
        bridge.inspect_structure_items(
            "owner",
            connected["live_document_id"],
            structure="bookmarks",
            limit=201,
        )
    assert limit_error.value.code is ErrorCode.INVALID_INPUT


def test_live_word_inserts_text_in_same_document_and_checks_version(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]

    inserted = bridge.insert_text(
        "owner",
        document_id,
        text="Live paragraph",
        target="document_end",
        as_new_paragraph=True,
        style="Heading 1",
        formatting={
            "font_name": "Aptos",
            "font_size_pt": 14,
            "bold": True,
            "font_color_rgb": "#123456",
            "paragraph_alignment": "center",
            "space_after_pt": 6,
        },
        expected_version=0,
    )

    assert "Live paragraph" in document.text
    assert inserted["live_version"] == 1
    assert inserted["document"]["saved"] is False
    assert inserted["formatting"]["font_color_rgb"] == "#123456"
    assert document.formatting_log["style"] == "Heading 1"
    assert document.formatting_log["font.Name"] == "Aptos"
    assert document.formatting_log["font.Size"] == 14.0
    assert document.formatting_log["font.Bold"] == -1
    assert document.formatting_log["font.Color"] == 0x563412
    assert document.formatting_log["paragraph.Alignment"] == 1
    assert document.formatting_log["paragraph.SpaceAfter"] == 6.0
    with pytest.raises(WordToolkitError) as error:
        bridge.insert_text(
            "owner",
            document_id,
            text="stale edit",
            target="document_end",
            as_new_paragraph=True,
            style="",
            expected_version=0,
        )
    assert error.value.code is ErrorCode.VERSION_CONFLICT


def test_live_word_formats_exact_selection_in_one_undo_record(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]
    application.Selection.SetRange(0, 8)
    selection = bridge.selection("owner", document_id)["selection"]
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_text_writes = document.text_write_count

    result = bridge.format_selection(
        "owner",
        document_id,
        selection_token=selection["selection_token"],
        style="Emphasis",
        formatting={
            "italic": True,
            "underline": True,
            "highlight_color_index": 7,
            "keep_with_next": True,
            "first_line_indent_pt": 18,
        },
        expected_version=0,
    )

    assert backend.attach_calls - before_attach == 1
    assert document.text_write_count - before_text_writes == 0
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.Selection.Start == 0
    assert application.Selection.End == 8
    assert application.ScreenUpdating is True
    assert result["live_version"] == 1
    assert result["formatted_range"] == {"start": 0, "end": 8}
    assert document.formatting_log["style"] == "Emphasis"
    assert document.formatting_log["font.Italic"] == -1
    assert document.formatting_log["font.Underline"] == 1
    assert document.formatting_log["range.HighlightColorIndex"] == 7
    assert document.formatting_log["paragraph.KeepWithNext"] == -1
    assert document.formatting_log["paragraph.FirstLineIndent"] == 18.0


def test_live_word_rejects_invalid_formatting_before_word_mutation(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    application.Selection.SetRange(0, 8)
    token = bridge.selection("owner", connected["live_document_id"])["selection"]["selection_token"]
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.format_selection(
            "owner",
            connected["live_document_id"],
            selection_token=token,
            formatting={"font_color_rgb": "red", "unknown": True},
            expected_version=0,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert application.UndoRecord.started == before_undo
    assert document.formatting_log == {}


def test_live_word_normalizes_compatibility_formatting_aliases_before_com(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    application.Selection.SetRange(0, 8)
    token = bridge.selection("owner", connected["live_document_id"])["selection"]["selection_token"]

    bridge.format_selection(
        "owner",
        connected["live_document_id"],
        selection_token=token,
        formatting={"font_size": 12, "alignment": "center"},
        expected_version=0,
    )

    assert document.formatting_log["font.Size"] == 12.0
    assert document.formatting_log["paragraph.Alignment"] == 1


def test_live_word_rejects_alias_and_canonical_formatting_conflict_before_com(
    live_bridge,
) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    application.Selection.SetRange(0, 8)
    token = bridge.selection("owner", connected["live_document_id"])["selection"]["selection_token"]
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.format_selection(
            "owner",
            connected["live_document_id"],
            selection_token=token,
            formatting={"font_size": 12, "font_size_pt": 14},
            expected_version=0,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert application.UndoRecord.started == before_undo
    assert document.formatting_log == {}


def test_live_word_rolls_back_live_formatting_failure(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    application.Selection.SetRange(0, 8)
    token = bridge.selection("owner", connected["live_document_id"])["selection"]["selection_token"]
    document.fail_format_property = "Bold"

    with pytest.raises(WordToolkitError):
        bridge.format_selection(
            "owner",
            connected["live_document_id"],
            selection_token=token,
            formatting={"bold": True},
            expected_version=0,
        )

    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_inserts_native_table_from_one_text_payload(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_text_writes = document.text_write_count

    result = bridge.insert_table(
        "owner",
        connected["live_document_id"],
        rows=[
            ["Symbol", "Value", "Unit"],
            ["ℏ", "1.054571817×10^-34", "J·s"],
            ["c", "299792458", "m/s"],
        ],
        style="Grid Table 4 - Accent 1",
        header_row=True,
        autofit="window",
        alignment="center",
        expected_version=0,
    )

    table = document.Tables.Item(1)
    assert backend.attach_calls - before_attach == 1
    assert document.text_write_count - before_text_writes == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert document.Tables.Count == 1
    assert table.row_count == 3
    assert table.column_count == 3
    assert table.Style == "Grid Table 4 - Accent 1"
    assert table.AllowAutoFit is True
    assert table.autofit_behavior == 2
    assert table.Rows.Alignment == 1
    assert table.Rows.Item(1).HeadingFormat == -1
    assert table.ApplyStyleHeadingRows is True
    assert result["live_version"] == 1
    assert result["table"]["native_verified"] is True
    assert result["table"]["rows"] == 3
    assert result["table"]["columns"] == 3
    assert result["content_returned"] is False
    assert "Symbol\tValue\tUnit" in document.text


def test_live_word_rejects_invalid_table_before_mutation(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_text = document.text
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_table(
            "owner",
            connected["live_document_id"],
            rows=[["A", "B"], ["ragged"]],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert document.text == before_text
    assert document.Tables.Count == 0
    assert application.UndoRecord.started == before_undo


def test_live_word_rolls_back_table_conversion_failure(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document.Tables.fail_convert = True

    with pytest.raises(WordToolkitError):
        bridge.insert_table(
            "owner",
            connected["live_document_id"],
            rows=[["A", "B"], ["1", "2"]],
            expected_version=0,
        )

    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_preflights_typed_table_formulas_without_attaching(live_bridge) -> None:
    bridge, _application, document = live_bridge
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_text = document.text

    result = bridge.preflight_table_formulas(
        [
            {
                "row": 4,
                "column": 2,
                "function": "sum",
                "directions": ["above"],
                "numeric_format": "0.00",
            },
            {
                "row": 4,
                "column": 3,
                "function": "average",
                "cell_range": {
                    "start": {"row": 2, "column": 3},
                    "end": {"row": 3, "column": 3},
                },
            },
            {
                "row": 4,
                "column": 2,
                "function": "max",
                "directions": ["above"],
            },
            {
                "row": 2,
                "column": 2,
                "function": "sum",
                "directions": ["left"],
                "formula": "=DDEAUTO()",
            },
        ]
    )

    assert backend.attach_calls == before_attach
    assert document.text == before_text
    assert result["valid"] is False
    assert result["valid_count"] == 2
    assert result["invalid_count"] == 2
    assert result["formulas"][0]["field_type"] == 34
    assert result["formulas"][0]["source"] == "directions"
    assert result["formulas"][1]["source"] == "cell_range"
    assert result["formulas"][2]["error"]["code"] == ErrorCode.INVALID_INPUT.value
    assert result["formulas"][3]["error"]["code"] == ErrorCode.INVALID_INPUT.value
    assert result["raw_field_codes_accepted"] is False
    assert result["mutated_word"] is False
    assert result["content_returned"] is False


def test_live_word_inserts_native_table_formula_batch_in_one_attachment(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    bridge.insert_table(
        "owner",
        connected["live_document_id"],
        rows=[
            ["Item", "Q1", "Q2", "Total"],
            ["A", "10", "20", ""],
            ["B", "30", "40", ""],
            ["Summary", "", "", ""],
        ],
        expected_version=0,
    )
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_fields = document.Fields.Count
    before_undo = application.UndoRecord.started

    result = bridge.insert_table_formulas(
        "owner",
        connected["live_document_id"],
        table_index=1,
        formulas=[
            {
                "row": 2,
                "column": 4,
                "function": "sum",
                "directions": ["left"],
                "numeric_format": "0.00",
            },
            {
                "row": 3,
                "column": 4,
                "function": "sum",
                "directions": ["left"],
            },
            {
                "row": 4,
                "column": 2,
                "function": "sum",
                "directions": ["above"],
            },
            {
                "row": 4,
                "column": 3,
                "function": "average",
                "directions": ["above"],
            },
            {
                "row": 4,
                "column": 4,
                "function": "sum",
                "cell_range": {
                    "start": {"row": 2, "column": 4},
                    "end": {"row": 3, "column": 4},
                },
            },
        ],
        expected_version=1,
    )

    table = document.Tables.Item(1)
    assert backend.attach_calls - before_attach == 1
    assert application.UndoRecord.started - before_undo == 1
    assert application.UndoRecord.started == application.UndoRecord.ended
    assert application.ScreenUpdating is True
    assert document.Fields.Count == before_fields + 5
    assert table.Cell(2, 4).fields[0].field_text == 'SUM(LEFT) \\# "0.00"'
    assert table.Cell(4, 3).fields[0].field_text == "AVERAGE(ABOVE)"
    assert table.Cell(4, 4).fields[0].field_text == "SUM(D2:D3)"
    assert all(field.update_calls == 0 for field in document.Fields.items[before_fields:])
    assert result["live_version"] == 6
    assert result["formula_count"] == 5
    assert all(item["native_verified"] for item in result["formulas"])
    assert all(item["calculated_on_insert"] for item in result["formulas"])
    assert not any(item["updated"] for item in result["formulas"])
    assert result["performance"] | {"duration_ms": 0} == {
        "com_attachments": 1,
        "table_lookups": 1,
        "field_add_calls": 5,
        "field_update_calls": 0,
        "cell_clear_assignments": 0,
        "undo_transactions": 1,
        "screen_updates_suspended": True,
        "calculation_mode": "on_insert",
        "duration_ms": 0,
    }
    assert result["performance"]["duration_ms"] >= 0
    assert result["raw_field_codes_accepted"] is False
    assert result["content_returned"] is False
    assert "=SUM" not in str(result)


def test_live_word_table_formula_requires_explicit_replacement(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    bridge.insert_table(
        "owner",
        connected["live_document_id"],
        rows=[["Value", "Result"], ["10", "old result"]],
        expected_version=0,
    )
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_table_formulas(
            "owner",
            connected["live_document_id"],
            table_index=1,
            formulas=[
                {
                    "row": 2,
                    "column": 2,
                    "function": "sum",
                    "directions": ["left"],
                }
            ],
            expected_version=1,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert application.UndoRecord.started == before_undo
    result = bridge.insert_table_formulas(
        "owner",
        connected["live_document_id"],
        table_index=1,
        formulas=[
            {
                "row": 2,
                "column": 2,
                "function": "sum",
                "directions": ["left"],
                "replace_existing": True,
            }
        ],
        expected_version=1,
    )
    assert result["formulas"][0]["replaced_existing"] is True
    assert result["performance"]["cell_clear_assignments"] == 1
    assert result["live_version"] == 2


def test_live_word_table_formula_can_force_explicit_update(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    bridge.insert_table(
        "owner",
        connected["live_document_id"],
        rows=[["Value", "Result"], ["10", ""]],
        expected_version=0,
    )

    result = bridge.insert_table_formulas(
        "owner",
        connected["live_document_id"],
        table_index=1,
        formulas=[
            {
                "row": 2,
                "column": 2,
                "function": "sum",
                "directions": ["left"],
            }
        ],
        force_update=True,
        expected_version=1,
    )

    field = document.Tables.Item(1).Cell(2, 2).fields[0]
    assert field.update_calls == 1
    assert result["formulas"][0]["calculated_on_insert"] is True
    assert result["formulas"][0]["updated"] is True
    assert result["performance"]["field_update_calls"] == 1
    assert result["performance"]["calculation_mode"] == "on_insert_and_explicit_update"


def test_live_word_rolls_back_native_table_formula_failure(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    bridge.insert_table(
        "owner",
        connected["live_document_id"],
        rows=[["Value", "Result"], ["10", ""]],
        expected_version=0,
    )
    document.Fields.fail_add = True

    with pytest.raises(WordToolkitError):
        bridge.insert_table_formulas(
            "owner",
            connected["live_document_id"],
            table_index=1,
            formulas=[
                {
                    "row": 2,
                    "column": 2,
                    "function": "sum",
                    "directions": ["left"],
                }
            ],
            expected_version=1,
        )

    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 2
    assert application.ScreenUpdating is True
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 1


def test_live_word_updates_existing_table_fields_in_one_bulk_call(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document.text = "1\t2\r3\t4"
    table = document.Tables.ConvertFromRange(document.Content, 1, 2, 2)
    table.Cell(2, 2).Formula("=SUM(ABOVE)", "")
    field = table.Cell(2, 2).fields[0]
    before_undo = application.UndoRecord.started
    before_attachments = bridge.backend.attach_calls

    result = bridge.update_table_fields(
        "owner",
        connected["live_document_id"],
        table_index=1,
        expected_version=0,
    )

    assert result["live_version"] == 1
    assert result["updated"] is True
    assert result["no_op"] is False
    assert result["word_update_result"] == 0
    assert result["table"]["field_count"] == 1
    assert result["table"]["field_type_histogram"] == {"34": 1}
    assert result["performance"]["field_update_calls"] == 1
    assert result["performance"]["field_type_reads"] == 2
    assert result["field_codes_returned"] is False
    assert result["field_results_returned"] is False
    assert table.Range.Fields.update_calls == 1
    assert field.update_calls == 1
    assert bridge.backend.attach_calls - before_attachments == 1
    assert application.UndoRecord.started - before_undo == 1
    assert application.UndoRecord.started == application.UndoRecord.ended
    assert application.ScreenUpdating is True


def test_live_word_table_field_update_is_a_version_stable_no_op_when_empty(
    live_bridge,
) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document.text = "1\t2\r3\t4"
    document.Tables.ConvertFromRange(document.Content, 1, 2, 2)
    before_undo = application.UndoRecord.started

    result = bridge.update_table_fields(
        "owner",
        connected["live_document_id"],
        table_index=1,
        expected_version=0,
    )

    assert result["live_version"] == 0
    assert result["updated"] is False
    assert result["no_op"] is True
    assert result["performance"]["field_update_calls"] == 0
    assert result["performance"]["undo_transactions"] == 0
    assert application.UndoRecord.started == before_undo


def test_live_word_rolls_back_failed_table_field_update(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document.text = "1\t2\r3\t4"
    table = document.Tables.ConvertFromRange(document.Content, 1, 2, 2)
    table.Cell(2, 2).Formula("=SUM(ABOVE)", "")
    document.Fields.fail_update = True

    with pytest.raises(WordToolkitError) as error:
        bridge.update_table_fields(
            "owner",
            connected["live_document_id"],
            table_index=1,
            expected_version=0,
        )

    assert error.value.code is ErrorCode.EXTERNAL_TOOL_FAILED
    assert error.value.details["reported_first_error_index"] == 1
    assert bridge._records[connected["live_document_id"]].version == 0
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True


@pytest.mark.parametrize(
    ("list_kind", "expected_type"),
    [("bullet", 2), ("numbered", 3)],
)
def test_live_word_inserts_native_list_from_one_text_payload(
    live_bridge,
    list_kind: str,
    expected_type: int,
) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_lists = document.Lists.Count
    before_text_writes = document.text_write_count

    result = bridge.insert_list(
        "owner",
        connected["live_document_id"],
        items=["First item", "Second item", "Third\nline"],
        list_kind=list_kind,
        style="List Paragraph",
        formatting={"font_name": "Aptos", "space_after_pt": 3},
        expected_version=0,
    )

    created = document.Lists.Item(before_lists + 1)
    assert backend.attach_calls - before_attach == 1
    assert document.text_write_count - before_text_writes == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert document.Lists.Count == before_lists + 1
    assert created.Range.ListFormat.ListType == expected_type
    assert created.Range.ListFormat.default_behavior == 1
    assert created.item_count == 3
    assert document.formatting_log["style"] == "List Paragraph"
    assert document.formatting_log["font.Name"] == "Aptos"
    assert document.formatting_log["paragraph.SpaceAfter"] == 3.0
    assert result["live_version"] == 1
    assert result["list"]["kind"] == list_kind
    assert result["list"]["item_count"] == 3
    assert result["list"]["list_type"] == expected_type
    assert result["list"]["native_verified"] is True
    assert result["content_returned"] is False
    assert "First item\rSecond item\rThird\vline" in document.text


def test_live_word_rejects_invalid_list_before_mutation(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_text = document.text
    before_lists = document.Lists.Count
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_list(
            "owner",
            connected["live_document_id"],
            items=["valid", ""],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert document.text == before_text
    assert document.Lists.Count == before_lists
    assert application.UndoRecord.started == before_undo


def test_live_word_rolls_back_list_formatting_failure(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_lists = document.Lists.Count
    document.Lists.fail_apply = True

    with pytest.raises(WordToolkitError):
        bridge.insert_list(
            "owner",
            connected["live_document_id"],
            items=["one", "two"],
            expected_version=0,
        )

    assert document.Lists.Count == before_lists
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_preflights_native_bookmarks_without_attaching(live_bridge) -> None:
    bridge, _application, document = live_bridge
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_text = document.text

    result = bridge.preflight_bookmarks(
        [
            {
                "name": "Definition_1",
                "text": "Bounded native bookmark",
                "as_new_paragraph": True,
            },
            {"name": "definition_1", "text": "Duplicate by case"},
            {"name": "bad bookmark", "text": "Invalid name"},
        ]
    )

    assert backend.attach_calls == before_attach
    assert document.text == before_text
    assert result["valid"] is False
    assert result["valid_count"] == 1
    assert result["invalid_count"] == 2
    assert result["bookmarks"][0]["rules"] == [
        "native_bookmark",
        "case_insensitive_unique_name",
        "bounded_bookmark_range",
        "native_range_verification",
    ]
    assert result["word_attached"] is False
    assert result["mutated_word"] is False
    assert result["content_returned"] is False


def test_live_word_inserts_native_bookmarks_in_one_attachment(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_writes = document.text_write_count
    before_bookmarks = document.Bookmarks.Count

    result = bridge.insert_bookmarks(
        "owner",
        connected["live_document_id"],
        bookmarks=[
            {
                "name": "Definition_1",
                "text": "First definition",
                "prefix_text": "Label: ",
                "as_new_paragraph": True,
                "style": "Heading 2",
                "formatting": {"bold": True},
            },
            {
                "name": "Result_1",
                "text": "Second result",
                "prefix_text": " | ",
                "formatting": {"italic": True},
            },
        ],
        expected_version=0,
    )

    first = document.Bookmarks.Item("definition_1")
    second = document.Bookmarks.Item("RESULT_1")
    assert backend.attach_calls - before_attach == 1
    assert document.text_write_count - before_writes == 1
    assert document.Bookmarks.add_calls == 2
    assert document.Bookmarks.Count == before_bookmarks + 2
    assert first.Range.Text == "First definition"
    assert second.Range.Text == "Second result"
    assert result["live_version"] == 2
    assert result["bookmark_count_before"] == before_bookmarks
    assert result["bookmark_count_after"] == before_bookmarks + 2
    assert result["document"]["bookmark_count"] == before_bookmarks + 2
    assert all(item["native_verified"] for item in result["bookmarks"])
    assert result["performance"] == {
        "com_attachments": 1,
        "text_assignments": 1,
        "bookmark_add_calls": 2,
        "undo_transactions": 1,
        "screen_updates_suspended": True,
    }
    assert result["content_returned"] is False
    assert "First definition" not in str(result)
    assert application.UndoRecord.started == application.UndoRecord.ended == 1


def test_live_word_bookmark_payload_ranges_use_word_utf16_offsets(live_bridge) -> None:
    bridge, _application, document = live_bridge
    prepared = bridge._prepare_bookmarks(
        [
            {"name": "Emoji_1", "prefix_text": "\U0001f600 ", "text": "alpha"},
            {"name": "Emoji_2", "prefix_text": "\U0001f680 ", "text": "beta"},
        ]
    )

    payload, ranges = bridge._bookmark_batch_payload(document, 0, prepared)

    assert payload == "\U0001f600 alpha\U0001f680 beta"
    assert ranges == [(3, 8), (11, 15)]


def test_live_word_rejects_existing_bookmark_before_mutation(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_text = document.text
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_bookmarks(
            "owner",
            connected["live_document_id"],
            bookmarks=[{"name": "knownbookmark", "text": "Collision"}],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert document.text == before_text
    assert document.Bookmarks.add_calls == 0
    assert application.UndoRecord.started == before_undo
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_rolls_back_native_bookmark_add_failure(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_bookmarks = document.Bookmarks.Count
    document.Bookmarks.fail_add = True

    with pytest.raises(WordToolkitError):
        bridge.insert_bookmarks(
            "owner",
            connected["live_document_id"],
            bookmarks=[{"name": "Failure_1", "text": "Rollback"}],
            expected_version=0,
        )

    assert document.Bookmarks.Count == before_bookmarks
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_preflights_safe_fields_without_attaching(live_bridge) -> None:
    bridge, _application, document = live_bridge
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_text = document.text

    result = bridge.preflight_fields(
        [
            {"kind": "page", "prefix_text": "Page "},
            {
                "kind": "formula",
                "expression": "=ROUND((10+2)/3, 2)",
                "numeric_format": "0.00",
            },
            {"kind": "reference", "bookmark": "KnownBookmark"},
            {"kind": "formula", "expression": 'DDE("cmd")'},
            {"kind": "page", "field_code": "DDEAUTO c:\\evil"},
        ]
    )

    assert backend.attach_calls == before_attach
    assert document.text == before_text
    assert result["valid"] is False
    assert result["valid_count"] == 3
    assert result["invalid_count"] == 2
    assert result["fields"][0]["field_type"] == 33
    assert result["fields"][1]["field_type"] == 34
    assert "restricted_formula_grammar" in result["fields"][1]["rules"]
    assert result["fields"][3]["error"]["code"] == ErrorCode.INVALID_INPUT.value
    assert result["raw_field_codes_accepted"] is False
    assert result["mutated_word"] is False
    assert result["content_returned"] is False


def test_live_word_inserts_safe_native_field_batch_in_one_attachment(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_writes = document.text_write_count
    before_fields = document.Fields.Count

    result = bridge.insert_fields(
        "owner",
        connected["live_document_id"],
        fields=[
            {
                "kind": "page",
                "prefix_text": "Page ",
                "suffix_text": " of ",
                "as_new_paragraph": True,
            },
            {"kind": "num_pages"},
            {
                "kind": "formula",
                "expression": "ROUND((10+2)/3,2)",
                "numeric_format": "0.00",
                "prefix_text": " Result: ",
            },
            {
                "kind": "reference",
                "bookmark": "KnownBookmark",
                "prefix_text": " Reference: ",
            },
        ],
        expected_version=0,
    )

    created = document.Fields.items[before_fields:]
    assert backend.attach_calls - before_attach == 1
    assert document.text_write_count - before_writes == 1
    assert document.Fields.add_calls == 4
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert document.Fields.Count == before_fields + 4
    assert {field.Type for field in created} == {3, 26, 33, 34}
    assert all(field.update_calls == 1 for field in created)
    assert result["live_version"] == 4
    assert [field["kind"] for field in result["fields"]] == [
        "page",
        "num_pages",
        "formula",
        "reference",
    ]
    assert all(field["native_verified"] for field in result["fields"])
    assert result["field_count_before"] == before_fields
    assert result["field_count_after"] == before_fields + 4
    assert result["performance"] == {
        "com_attachments": 1,
        "text_assignments": 1,
        "field_add_calls": 4,
        "undo_transactions": 1,
        "screen_updates_suspended": True,
    }
    assert result["raw_field_codes_accepted"] is False
    assert result["content_returned"] is False
    assert result["document"]["field_count"] == before_fields + 4
    assert "ROUND" not in str(result)


def test_live_word_field_markers_use_word_utf16_offsets(live_bridge) -> None:
    bridge, _application, document = live_bridge
    prepared = bridge._prepare_fields(
        [
            {"kind": "page", "prefix_text": "\U0001f600 "},
            {"kind": "num_pages", "prefix_text": "\U0001f680 "},
        ]
    )

    payload, markers = bridge._field_batch_payload(document, 0, prepared)

    assert payload == "\U0001f600 \ue000\U0001f680 \ue000"
    assert markers == [(3, 4), (7, 8)]


def test_live_word_localizes_formula_field_separators(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    application.international = {
        17: ";",
        18: ",",
        19: "\u00a0",
    }

    bridge.insert_fields(
        "owner",
        connected["live_document_id"],
        fields=[
            {
                "kind": "formula",
                "expression": "ROUND(1234.5/3,2)",
                "numeric_format": "#,##0.00",
            }
        ],
        expected_version=0,
    )

    field = document.Fields.items[-1]
    assert field.field_text == 'ROUND(1234,5/3;2) \\# "#\u00a0##0,00"'


def test_live_word_rejects_unsafe_field_before_attachment(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    backend = bridge.backend
    before_attach = backend.attach_calls
    before_text = document.text

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_fields(
            "owner",
            connected["live_document_id"],
            fields=[
                {
                    "kind": "formula",
                    "expression": 'INCLUDETEXT("https://example.com")',
                }
            ],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert backend.attach_calls == before_attach
    assert document.text == before_text
    assert document.Fields.add_calls == 0


def test_live_word_rejects_missing_reference_before_mutation(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_text = document.text
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_fields(
            "owner",
            connected["live_document_id"],
            fields=[{"kind": "reference", "bookmark": "MissingBookmark"}],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert document.text == before_text
    assert document.Fields.add_calls == 0
    assert application.UndoRecord.started == before_undo


def test_live_word_rolls_back_native_field_add_failure(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_fields = document.Fields.Count
    document.Fields.fail_add = True

    with pytest.raises(WordToolkitError):
        bridge.insert_fields(
            "owner",
            connected["live_document_id"],
            fields=[{"kind": "page", "prefix_text": "Page "}],
            expected_version=0,
        )

    assert document.Fields.Count == before_fields
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_inserts_native_built_up_equation(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)

    result = bridge.insert_equation(
        "owner",
        connected["live_document_id"],
        value=r"\frac{x+1}{2}=3",
        input_format="latex",
        display=True,
        target="document_end",
        expected_version=0,
    )

    assert document.OMaths.Count == 1
    assert document.OMaths.Item(1).built_up is True
    assert document.OMaths.Item(1).Type == 0
    assert result["equation"]["linear_input"] == "(x+1)/(2)=3"
    assert result["equation"]["native_verified"] is True
    assert result["document"]["equation_count"] == 1


def test_live_word_rejects_ambiguous_fraction_coefficient(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_equation(
            "owner",
            connected["live_document_id"],
            value="I=1/3 (x^2+1)^(3/2)+C",
            input_format="unicodemath",
            display=True,
            target="document_end",
            expected_version=0,
        )

    assert error.value.code is ErrorCode.EQUATION_INVALID
    assert "explicit multiplication" in error.value.message
    assert document.OMaths.Count == 0


def test_live_word_accepts_explicit_fraction_multiplication(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)

    result = bridge.insert_equation(
        "owner",
        connected["live_document_id"],
        value="I=1/3·(x^2+1)^(3/2)+C",
        input_format="unicodemath",
        display=True,
        target="document_end",
        expected_version=0,
    )

    assert result["equation"]["native_verified"] is True
    assert document.OMaths.Count == 1


def test_live_word_inserts_equation_batch_in_one_logical_mutation(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    formulas = [
        r"\int x\,dx=\frac{x^2}{2}+C",
        r"\int 2x\cos(x^2)\,dx=\sin(x^2)+C",
    ]
    document.word_open_xml_queue = [_fake_latex_word_open_xml(formula) for formula in formulas]

    result = bridge.insert_equations_batch(
        "owner",
        connected["live_document_id"],
        equations=[{"value": formula, "input_format": "latex"} for formula in formulas],
        expected_version=0,
    )

    assert document.OMaths.Count == 2
    assert result["live_version"] == 2
    assert len(result["equations"]) == 2
    assert all(item["native_verified"] for item in result["equations"])


def test_live_word_preflights_equations_without_attaching_or_mutating(live_bridge) -> None:
    bridge, _application, document = live_bridge
    backend = bridge.backend
    before_calls = backend.attach_calls

    result = bridge.preflight_equations(
        [
            {
                "value": r"\frac{1}{3}\cdot(x^2+1)^{3/2}",
                "input_format": "latex",
            },
            {
                "value": "I=1/3 (x^2+1)^(3/2)+C",
                "input_format": "unicodemath",
            },
        ]
    )

    assert result["valid"] is False
    assert result["valid_count"] == 1
    assert result["invalid_count"] == 1
    assert "fraction_scope" in result["equations"][0]["rules"]
    assert result["equations"][1]["error"]["code"] == ErrorCode.EQUATION_INVALID
    assert result["mutated_word"] is False
    assert backend.attach_calls == before_calls
    assert document.OMaths.Count == 0


def test_live_word_applies_mixed_batch_with_one_attachment_and_one_undo_record(
    live_bridge,
) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    backend = bridge.backend
    before_calls = backend.attach_calls
    before_undo = application.UndoRecord.started

    result = bridge.apply_operations(
        "owner",
        connected["live_document_id"],
        operations=[
            {
                "type": "text",
                "text": "Równanie kwadratowe",
                "as_new_paragraph": True,
                "style": "Heading 1",
                "formatting": {
                    "font_color_rgb": "#204060",
                    "keep_with_next": True,
                },
            },
            {
                "type": "equation",
                "value": r"\Delta=b^2-4ac",
                "input_format": "latex",
            },
            {
                "type": "text",
                "text": "Pierwiastki:",
                "as_new_paragraph": True,
            },
            {
                "type": "equation",
                "value": r"x=\frac{-b+\sqrt{\Delta}}{2a}",
                "input_format": "latex",
                "verify_readback": True,
            },
        ],
        expected_version=0,
    )

    assert backend.attach_calls - before_calls == 1
    assert application.UndoRecord.started - before_undo == 1
    assert application.UndoRecord.started == application.UndoRecord.ended
    assert application.ScreenUpdating is True
    assert result["live_version"] == 4
    assert result["operation_count"] == 4
    assert result["text_operation_count"] == 2
    assert result["equation_operation_count"] == 2
    assert result["performance"]["com_attachments"] == 1
    assert document.OMaths.Count == 2
    assert "Równanie kwadratowe" in document.text
    assert "Pierwiastki:" in document.text
    assert result["operations"][0]["formatting"]["font_color_rgb"] == "#204060"
    assert document.formatting_log["font.Color"] == 0x604020
    assert document.formatting_log["paragraph.KeepWithNext"] == -1
    assert result["operations"][3]["equation"]["readback_verified"] is True


def test_live_word_mixed_batch_accepts_inline_runs(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)

    result = bridge.apply_operations(
        "owner",
        connected["live_document_id"],
        operations=[
            {
                "type": "text",
                "runs": [
                    {"text": "plain ", "formatting": {}},
                    {"text": "bold", "formatting": {"bold": True}},
                    {"text": " and large", "formatting": {"font_size_pt": 18}},
                ],
                "as_new_paragraph": True,
            }
        ],
        expected_version=0,
    )

    assert "plain bold and large" in document.text
    assert result["operation_count"] == 1
    assert result["operations"][0]["type"] == "text"
    assert result["operations"][0]["run_count"] == 3
    bold_range = next(item for item in document.formatting_ranges if item[:2] == ("font", "Bold"))
    size_range = next(item for item in document.formatting_ranges if item[:2] == ("font", "Size"))
    assert document.text[bold_range[2] : bold_range[3]] == "bold"
    assert bold_range[4] == -1
    assert document.text[size_range[2] : size_range[3]] == " and large"
    assert size_range[4] == 18.0


def test_live_word_inline_runs_reject_paragraph_formatting_before_mutation(
    live_bridge,
) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_text = document.text
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.apply_operations(
            "owner",
            connected["live_document_id"],
            operations=[
                {
                    "type": "text",
                    "runs": [
                        {
                            "text": "must not mutate",
                            "formatting": {"paragraph_alignment": "center"},
                        }
                    ],
                }
            ],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.INVALID_INPUT
    assert error.value.details == {"fields": ["paragraph_alignment"]}
    assert document.text == before_text
    assert application.UndoRecord.started == before_undo


def test_live_word_inline_run_ranges_follow_mixed_newline_normalization(
    live_bridge,
) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)

    result = bridge.apply_operations(
        "owner",
        connected["live_document_id"],
        operations=[
            {
                "type": "text",
                "runs": [
                    {"text": "A\r\n", "formatting": {"bold": True}},
                    {"text": "B\n", "formatting": {"italic": True}},
                    {"text": "C\rD", "formatting": {"underline": True}},
                ],
                "as_new_paragraph": True,
            }
        ],
        expected_version=0,
    )

    formatted_text = {
        name: document.text[start:end]
        for prefix, name, start, end, _value in document.formatting_ranges
        if prefix == "font" and name in {"Bold", "Italic", "Underline"}
    }
    assert formatted_text == {
        "Bold": "A\r",
        "Italic": "B\r",
        "Underline": "C\rD",
    }
    assert result["operations"][0]["run_count"] == 3


def test_live_word_inline_run_ranges_use_word_utf16_offsets(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    insertion_start = len(document.text) - 1

    bridge.apply_operations(
        "owner",
        connected["live_document_id"],
        operations=[
            {
                "type": "text",
                "runs": [
                    {"text": "A\U0001f600\r\n", "formatting": {"bold": True}},
                    {"text": "B", "formatting": {"italic": True}},
                ],
            }
        ],
        expected_version=0,
    )

    formatted_ranges = {
        name: (start, end)
        for prefix, name, start, end, _value in document.formatting_ranges
        if prefix == "font" and name in {"Bold", "Italic"}
    }
    assert _word_utf16_length("A\U0001f600\r") == 4
    assert formatted_ranges == {
        "Bold": (insertion_start, insertion_start + 4),
        "Italic": (insertion_start + 4, insertion_start + 5),
    }


def test_live_word_mixed_batch_is_preflighted_before_mutation(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    before_text = document.text
    before_undo = application.UndoRecord.started

    with pytest.raises(WordToolkitError) as error:
        bridge.apply_operations(
            "owner",
            connected["live_document_id"],
            operations=[
                {
                    "type": "text",
                    "text": "This must never be inserted",
                    "as_new_paragraph": True,
                },
                {
                    "type": "equation",
                    "value": "I=1/3 (x^2+1)^(3/2)+C",
                    "input_format": "unicodemath",
                },
            ],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.EQUATION_INVALID
    assert document.text == before_text
    assert application.UndoRecord.started == before_undo


def test_live_word_mixed_batch_rolls_back_after_partial_word_failure(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    original_add = document.OMaths.Add
    add_calls = 0

    def fail_second_add(equation_range):
        nonlocal add_calls
        add_calls += 1
        if add_calls == 2:
            raise RuntimeError("simulated second OMath failure")
        return original_add(equation_range)

    document.OMaths.Add = fail_second_add
    with pytest.raises(WordToolkitError):
        bridge.apply_operations(
            "owner",
            connected["live_document_id"],
            operations=[
                {
                    "type": "text",
                    "text": "Transactional batch",
                    "as_new_paragraph": True,
                },
                {"type": "equation", "value": "x=1", "input_format": "latex"},
                {"type": "equation", "value": "y=2", "input_format": "latex"},
            ],
            expected_version=0,
        )

    assert add_calls == 2
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_rolls_back_when_word_drops_hbar(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document.word_open_xml_override = _fake_word_open_xml("E=ω")

    with pytest.raises(WordToolkitError) as error:
        bridge.apply_operations(
            "owner",
            connected["live_document_id"],
            operations=[
                {
                    "type": "equation",
                    "value": r"E=\hbar\omega",
                    "input_format": "latex",
                }
            ],
            expected_version=0,
            verify_readback=False,
        )

    assert error.value.code is ErrorCode.EQUATION_INVALID
    assert "dropped" in error.value.message
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert application.ScreenUpdating is True
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_rolls_back_when_native_build_changes_structure(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document.word_open_xml_override = _fake_literal_word_open_xml("hat(H)ψ=Eψ")

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_equation(
            "owner",
            connected["live_document_id"],
            value=r"\hat{H}\psi=E\psi",
            input_format="latex",
            display=True,
            target="document_end",
            expected_version=0,
        )

    assert error.value.code is ErrorCode.EQUATION_INVALID
    assert "changed equation text or structure" in error.value.message
    assert error.value.details["structure_preserved"] is False
    assert "accent" in error.value.details["expected_structure_counts"]
    assert "accent" not in error.value.details["actual_structure_counts"]
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0
    learning = bridge.inspect_learning()
    stored = (bridge.settings.storage_root / "word-live-equation-learning.json").read_text(
        encoding="utf-8"
    )
    assert learning["observation_count"] == 1
    assert learning["categories"][0]["last_error_code"] == "NATIVE_FIDELITY_MISMATCH"
    assert r"\hat{H}\psi=E\psi" not in stored
    assert "Existing paragraph" not in stored


def test_live_word_structured_equations_force_fidelity_readback(live_bridge) -> None:
    bridge, _application, document = live_bridge
    backend = bridge.backend
    before_calls = backend.attach_calls

    result = bridge.preflight_equations(
        [
            {
                "value": r"\frac{x+1}{2}=3",
                "input_format": "latex",
            }
        ]
    )

    equation = result["equations"][0]
    assert equation["valid"] is True
    assert equation["requires_live_readback"] is True
    assert "structural_fidelity_readback" in equation["rules"]
    assert any("fidelity" in warning for warning in equation["warnings"])
    assert backend.attach_calls == before_calls
    assert document.OMaths.Count == 0


def test_live_word_learns_failed_equation_class_without_storing_formula_text(
    live_bridge,
) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    original_add = document.OMaths.Add

    def reject_add(_range):
        raise RuntimeError("simulated native parser failure")

    document.OMaths.Add = reject_add
    with pytest.raises(WordToolkitError):
        bridge.insert_equation(
            "owner",
            connected["live_document_id"],
            value="x=1",
            input_format="latex",
            display=True,
            target="document_end",
            expected_version=0,
        )
    document.OMaths.Add = original_add

    learning = bridge.inspect_learning()
    preflight = bridge.preflight_equations(
        [{"value": "y=2", "input_format": "latex", "display": True}]
    )
    stored = (bridge.settings.storage_root / "word-live-equation-learning.json").read_text(
        encoding="utf-8"
    )

    assert learning["observation_count"] == 1
    assert learning["category_count"] == 1
    assert learning["categories"][0]["failures"] == 1
    assert learning["categories"][0]["last_error_code"] == ErrorCode.EXTERNAL_TOOL_FAILED
    assert preflight["equations"][0]["learning"]["force_live_readback"] is True
    assert "learned_live_readback" in preflight["equations"][0]["rules"]
    assert "x=1" not in stored
    assert "y=2" not in stored
    assert "Existing paragraph" not in stored


def test_live_word_cursor_edit_requires_fresh_selection_token(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]
    selection = bridge.selection("owner", document_id)["selection"]

    inserted = bridge.insert_text(
        "owner",
        document_id,
        text=" at cursor",
        target="cursor",
        as_new_paragraph=False,
        style="",
        selection_token=selection["selection_token"],
        expected_version=0,
    )

    assert inserted["live_version"] == 1
    fresh = bridge.selection("owner", document_id)["selection"]["selection_token"]
    application.Selection.SetRange(0, 0)
    with pytest.raises(WordToolkitError) as error:
        bridge.insert_text(
            "owner",
            document_id,
            text="stale cursor",
            target="cursor",
            as_new_paragraph=False,
            style="",
            selection_token=fresh,
            expected_version=1,
        )
    assert error.value.code is ErrorCode.VERSION_CONFLICT
    assert "stale cursor" not in document.text


def test_live_word_refuses_to_replace_selection_without_explicit_flag(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]
    application.Selection.SetRange(0, 8)
    token = bridge.selection("owner", document_id)["selection"]["selection_token"]

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_text(
            "owner",
            document_id,
            text="Replacement",
            target="selection",
            as_new_paragraph=False,
            style="",
            selection_token=token,
            replace_selection=False,
            expected_version=0,
        )
    assert error.value.code is ErrorCode.INVALID_INPUT
    assert document.text.startswith("Existing")


def test_live_word_rolls_back_undo_record_when_equation_build_fails(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]

    def reject_add(_range):
        raise RuntimeError("simulated Word failure")

    document.OMaths.Add = reject_add
    with pytest.raises(WordToolkitError):
        bridge.insert_equation(
            "owner",
            document_id,
            value="x=1",
            input_format="latex",
            display=True,
            target="document_end",
            expected_version=0,
        )

    assert document.undo_calls == 1
    assert bridge.inspect("owner", document_id)["live_version"] == 0


def test_live_word_validates_snapshot_saves_same_path_and_disconnects(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]

    validation = bridge.validate("owner", document_id)
    saved = bridge.save("owner", document_id, expected_version=0)
    inspected_after_save = bridge.inspect("owner", document_id)
    disconnected = bridge.disconnect("owner", document_id)

    assert validation["validation"]["valid"] is True
    assert validation["snapshot_only"] is True
    assert saved["saved"] is True
    assert saved["live_version"] == 0
    assert inspected_after_save["live_version"] == 0
    assert document.save_calls == 1
    assert disconnected["disconnected"] is True
    with pytest.raises(WordToolkitError):
        bridge.inspect("owner", document_id)


def test_live_word_is_not_available_over_remote_transport(tmp_path: Path, live_bridge) -> None:
    _bridge, application, _document = live_bridge
    remote = LiveWordBridge(
        Settings(auth_mode="development_token", storage_root=tmp_path / "remote"),
        FakeValidator(),  # type: ignore[arg-type]
        backend=FakeBackend(application),
    )

    with pytest.raises(WordToolkitError) as error:
        remote.connect("owner", use_active=True)

    assert error.value.code is ErrorCode.AUTH_FORBIDDEN


def _catalog_for_member_execution() -> dict[str, Any]:
    return {
        "schema_version": 2,
        "generated_at": "2026-07-20T00:00:00+00:00",
        "source": "installed_microsoft_word_com_type_library",
        "privacy": "metadata only",
        "library": {
            "guid": "{WORD}",
            "lcid": 0,
            "syskind": 1,
            "major_version": 8,
            "minor_version": 7,
            "flags": 0,
            "declared_type_count": 2,
            "application_type_index": 0,
        },
        "stats": {
            "type_count": 2,
            "member_count": 4,
            "scan_errors": 0,
            "truncated": False,
            "scan_duration_ms": 1.0,
        },
        "types": [
            {
                "name": "Range",
                "kind": "dispatch",
                "type_index": 0,
                "guid": "{RANGE}",
                "flags": 0,
                "declared_function_count": 3,
                "declared_variable_count": 0,
                "implemented_type_count": 0,
                "implemented_types": [],
                "member_count": 3,
                "members": [
                    {
                        "name": "Text",
                        "kind": "property_get",
                        "member_id": 1,
                        "declaration_index": 0,
                        "parameters": [],
                        "parameter_count": 0,
                        "optional_parameter_count": 0,
                        "variadic": False,
                        "return_type": "BSTR",
                        "flags": 0,
                        "flag_names": [],
                    },
                    {
                        "name": "Text",
                        "kind": "property_put",
                        "member_id": 1,
                        "declaration_index": 1,
                        "parameters": [
                            {
                                "name": "value",
                                "type": "BSTR",
                                "flags": 1,
                                "flag_names": ["in"],
                                "optional": False,
                            }
                        ],
                        "parameter_count": 1,
                        "optional_parameter_count": 0,
                        "variadic": False,
                        "return_type": "VOID",
                        "flags": 0,
                        "flag_names": [],
                    },
                    {
                        "name": "InsertAfter",
                        "kind": "method",
                        "member_id": 2,
                        "declaration_index": 2,
                        "parameters": [
                            {
                                "name": "Text",
                                "type": "BSTR",
                                "flags": 1,
                                "flag_names": ["in"],
                                "optional": False,
                            }
                        ],
                        "parameter_count": 1,
                        "optional_parameter_count": 0,
                        "variadic": False,
                        "return_type": "VOID",
                        "flags": 0,
                        "flag_names": [],
                    },
                ],
            },
            {
                "name": "_Document",
                "kind": "dispatch",
                "type_index": 1,
                "guid": "{DOCUMENT}",
                "flags": 0,
                "declared_function_count": 1,
                "declared_variable_count": 0,
                "implemented_type_count": 0,
                "implemented_types": [],
                "member_count": 1,
                "members": [
                    {
                        "name": "Compatibility",
                        "kind": "property_put",
                        "member_id": 55,
                        "declaration_index": 0,
                        "invoke_kind": 4,
                        "parameters": [
                            {
                                "name": "Type",
                                "type": "I4",
                                "flags": 1,
                                "flag_names": ["in"],
                                "optional": False,
                            },
                            {
                                "name": "value",
                                "type": "BOOL",
                                "flags": 1,
                                "flag_names": ["in"],
                                "optional": False,
                            },
                        ],
                        "parameter_count": 2,
                        "optional_parameter_count": 0,
                        "variadic": False,
                        "return_type": "VOID",
                        "flags": 0,
                        "flag_names": [],
                    }
                ],
            },
        ],
    }


def test_catalog_member_operations_execute_in_one_undo_record(live_bridge) -> None:
    bridge, application, document = live_bridge
    catalog = _catalog_for_member_execution()
    bridge.object_model.write(catalog)
    registry = build_member_capability_registry(catalog)
    profiles = {
        (item["member"]["name"], item["member"]["kind"]): item for item in registry["profiles"]
    }
    connected = bridge.connect("owner", use_active=True)
    operations = [
        {
            "operation_id": "read_original",
            "capability_id": profiles[("Text", "property_get")]["capability_id"],
            "target": {"kind": "document_content"},
            "result_id": "original",
        },
        {
            "operation_id": "replace_content",
            "capability_id": profiles[("Text", "property_put")]["capability_id"],
            "target": {"kind": "document_content"},
            "arguments": ["Changed\r"],
        },
    ]

    preflight = bridge.preflight_member_operations(operations)
    result = bridge.execute_member_operations(
        "owner",
        connected["live_document_id"],
        operations=operations,
        expected_version=0,
    )

    assert preflight["valid"] is True
    assert preflight["mutating_count"] == 1
    assert result["executed_count"] == 2
    assert result["live_version"] == 1
    assert result["results"][0]["value"] == "Existing paragraph\r"
    assert result["execution"]["single_undo_record"] is True
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert document.text == "Changed\r"


def test_indexed_property_adapter_uses_catalog_dispid(live_bridge) -> None:
    bridge, _application, document = live_bridge
    catalog = _catalog_for_member_execution()
    registry = build_member_capability_registry(catalog)
    compatibility = next(
        item
        for item in registry["profiles"]
        if item["type"]["name"] == "_Document" and item["member"]["name"] == "Compatibility"
    )
    prepared = PreparedMemberOperation(
        operation_id="indexed_put",
        capability_id=compatibility["capability_id"],
        target_kind="document",
        target_result_id="",
        arguments=(33, True),
        result_id="",
        profile=compatibility,
    )

    assert compatibility["policy"]["execution"] == "blocked"
    assert bridge._invoke_member_operation(prepared, document, {}) is None
    assert document._oleobj_.invoke_calls == [(55, 0, 4, 0, 33, True)]


def test_catalog_member_operation_rolls_back_and_preserves_version(live_bridge) -> None:
    bridge, application, document = live_bridge
    catalog = _catalog_for_member_execution()
    bridge.object_model.write(catalog)
    registry = build_member_capability_registry(catalog)
    insert_after = next(
        item for item in registry["profiles"] if item["member"]["name"] == "InsertAfter"
    )
    connected = bridge.connect("owner", use_active=True)

    with pytest.raises(WordToolkitError) as error:
        bridge.execute_member_operations(
            "owner",
            connected["live_document_id"],
            operations=[
                {
                    "capability_id": insert_after["capability_id"],
                    "target": {"kind": "document_content"},
                    "arguments": ["Never inserted"],
                }
            ],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.EXTERNAL_TOOL_FAILED
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_native_find_is_bounded_and_returns_context(live_bridge) -> None:
    bridge, _application, document = live_bridge
    document.text = "Alpha beta alpha.\r"
    connected = bridge.connect("owner", use_active=True)

    result = bridge.find_text(
        "owner",
        connected["live_document_id"],
        search_text="alpha",
        match_case=False,
        whole_word=True,
        context_chars=5,
        max_results=10,
    )

    assert result["match_count"] == 2
    assert result["truncated"] is False
    assert [item["start"] for item in result["matches"]] == [0, 11]
    assert result["performance"] == {
        "com_attachments": 1,
        "native_find": True,
        "content_round_trip": False,
    }
    assert all(len(item["context"]) <= 265 for item in result["matches"])


def test_live_word_replace_preflights_and_mutates_in_one_undo_record(live_bridge) -> None:
    bridge, application, document = live_bridge
    document.text = "Alpha beta alpha.\r"
    document.TrackRevisions = True
    connected = bridge.connect("owner", use_active=True)
    before_attach = bridge.backend.attach_calls

    result = bridge.replace_text(
        "owner",
        connected["live_document_id"],
        search_text="alpha",
        replacement_text="X",
        match_case=False,
        whole_word=True,
        track_changes="disable",
        expected_version=0,
    )

    assert bridge.backend.attach_calls - before_attach == 1
    assert document.text == "X beta X.\r"
    assert document.TrackRevisions is True
    assert result["replacements"] == 2
    assert result["live_version"] == 1
    assert result["execution"]["single_undo_record"] is True
    assert result["execution"]["rollback_on_error"] is True
    assert application.UndoRecord.started == application.UndoRecord.ended == 1


def test_live_word_replace_refuses_limit_before_mutation(live_bridge) -> None:
    bridge, application, document = live_bridge
    document.text = "x x x\r"
    connected = bridge.connect("owner", use_active=True)
    before_writes = document.text_write_count

    with pytest.raises(WordToolkitError) as error:
        bridge.replace_text(
            "owner",
            connected["live_document_id"],
            search_text="x",
            replacement_text="y",
            max_replacements=2,
            expected_version=0,
        )

    assert error.value.code is ErrorCode.LIMIT_EXCEEDED
    assert document.text == "x x x\r"
    assert document.text_write_count == before_writes
    assert application.UndoRecord.started == 0
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_replace_rolls_back_failure_and_preserves_version(live_bridge) -> None:
    bridge, application, document = live_bridge
    document.text = "alpha alpha\r"
    document.fail_text_value = "failure"
    connected = bridge.connect("owner", use_active=True)

    with pytest.raises(WordToolkitError) as error:
        bridge.replace_text(
            "owner",
            connected["live_document_id"],
            search_text="alpha",
            replacement_text="failure",
            expected_version=0,
        )

    assert error.value.code is ErrorCode.EXTERNAL_TOOL_FAILED
    assert document.text == "alpha alpha\r"
    assert document.undo_calls == 1
    assert application.UndoRecord.started == application.UndoRecord.ended == 1
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_replace_rolls_back_if_track_changes_cannot_be_restored(
    live_bridge,
) -> None:
    bridge, _application, document = live_bridge
    document.text = "alpha alpha\r"
    document.TrackRevisions = True
    document.fail_track_value = True
    connected = bridge.connect("owner", use_active=True)

    with pytest.raises(WordToolkitError) as error:
        bridge.replace_text(
            "owner",
            connected["live_document_id"],
            search_text="alpha",
            replacement_text="beta",
            track_changes="disable",
            expected_version=0,
        )

    assert error.value.code is ErrorCode.EXTERNAL_TOOL_FAILED
    assert document.text == "alpha alpha\r"
    assert document.TrackRevisions is True
    assert document.undo_calls == 1
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0


def test_live_word_review_tokens_guard_comments_and_revisions(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]

    comments = bridge.inspect_review("owner", document_id, kind="comments")
    first_comment = comments["items"][0]
    resolved = bridge.manage_review(
        "owner",
        document_id,
        action="resolve_comment",
        item_index=first_comment["item_index"],
        review_token=first_comment["review_token"],
        resolved=True,
        expected_version=0,
    )

    assert resolved["resolved"] is True
    assert resolved["execution"]["single_undo_record"] is False
    assert document.Comments.Item(1).Done is True
    with pytest.raises(WordToolkitError) as stale:
        bridge.manage_review(
            "owner",
            document_id,
            action="delete_comment",
            item_index=first_comment["item_index"],
            review_token=first_comment["review_token"],
            expected_version=1,
        )
    assert stale.value.code is ErrorCode.VERSION_CONFLICT

    revisions = bridge.inspect_review("owner", document_id, kind="revisions")
    revision = revisions["items"][0]
    accepted = bridge.manage_review(
        "owner",
        document_id,
        action="accept_revision",
        item_index=revision["item_index"],
        review_token=revision["review_token"],
        expected_version=1,
    )

    assert accepted["decision"] == "accept"
    assert accepted["remaining_revisions"] == 0
    assert accepted["live_version"] == 2
    assert application.UndoRecord.started == application.UndoRecord.ended == 1


def test_live_word_review_token_detects_external_item_change(live_bridge) -> None:
    bridge, _application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]
    inspected = bridge.inspect_review("owner", document_id, kind="comments")
    comment = inspected["items"][0]
    document.Comments.Item(1).Range.Text = "Externally changed comment"

    with pytest.raises(WordToolkitError) as error:
        bridge.manage_review(
            "owner",
            document_id,
            action="delete_comment",
            item_index=comment["item_index"],
            review_token=comment["review_token"],
            expected_version=0,
        )

    assert error.value.code is ErrorCode.VERSION_CONFLICT
    assert document.Comments.Count == 2


def test_live_word_review_adds_and_replies_with_verified_ranges(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]
    application.Selection.SetRange(0, 8)
    selection = bridge.selection("owner", document_id)["selection"]

    added = bridge.manage_review(
        "owner",
        document_id,
        action="add_comment",
        selection_token=selection["selection_token"],
        text="Check this sentence.",
        expected_version=0,
    )

    assert added["comment_index"] == 3
    assert document.Comments.Count == 3
    assert added["live_version"] == 1
    inspected = bridge.inspect_review("owner", document_id, kind="comments", offset=2)
    comment = inspected["items"][0]
    replied = bridge.manage_review(
        "owner",
        document_id,
        action="reply_comment",
        item_index=comment["item_index"],
        review_token=comment["review_token"],
        text="Fixed.",
        expected_version=1,
    )

    assert replied["reply_count"] == 1
    assert replied["live_version"] == 2
    assert document.Comments.Item(3).Replies.Item(1).Range.Text == "Fixed."
    assert application.UndoRecord.started == application.UndoRecord.ended == 2


def test_live_word_track_changes_uses_verified_manual_rollback_policy(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)

    result = bridge.manage_review(
        "owner",
        connected["live_document_id"],
        action="set_track_changes",
        tracking_enabled=True,
        expected_version=0,
    )

    assert document.TrackRevisions is True
    assert result["track_changes"] is True
    assert result["live_version"] == 1
    assert result["execution"]["single_undo_record"] is False
    assert result["execution"]["manual_rollback_on_error"] is True
    assert application.UndoRecord.started == 0
    undo = bridge.inspect_undo("owner", connected["live_document_id"])
    assert undo["undo_barrier_active"] is True
    assert undo["wordtoolkit_undo_eligible"] is False


def test_live_word_layout_diagnosis_is_bounded_and_returns_no_text(live_bridge) -> None:
    bridge, _application, document = live_bridge
    paragraphs = [
        "H" * 120,
        "Body with manual break\x0c",
        "Body three",
        "Body four",
        "Body five",
        "L" * 1_300,
    ]
    document.text = "\r".join(paragraphs) + "\r"
    document.paragraph_styles[1] = "Heading 1"
    document.paragraph_format_values[1] = {"OutlineLevel": 1, "KeepWithNext": -1}
    for index in range(2, 6):
        document.paragraph_format_values[index] = {"KeepWithNext": -1}
    document.paragraph_format_values[6] = {
        "PageBreakBefore": -1,
        "KeepTogether": -1,
        "WidowControl": 0,
    }
    connected = bridge.connect("owner", use_active=True)

    result = bridge.diagnose_layout("owner", connected["live_document_id"])
    issue_types = {issue["type"] for issue in result["issues"]}

    assert result["scanned_paragraphs"] == 6
    assert result["content_returned"] is False
    assert "keep_with_next_chain" in issue_types
    assert "long_heading" in issue_types
    assert "page_break_before_body" in issue_types
    assert "long_keep_together_paragraph" in issue_types
    assert "widow_control_disabled" in issue_types
    assert "manual_page_breaks" in issue_types
    assert not any("text" in issue for issue in result["issues"])


def test_live_word_guarded_undo_never_crosses_manual_user_action(live_bridge) -> None:
    bridge, application, _document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    document_id = connected["live_document_id"]
    bridge.insert_text(
        "owner",
        document_id,
        text="Undo me",
        target="document_end",
        as_new_paragraph=True,
        style="",
        expected_version=0,
    )

    inspected = bridge.inspect_undo("owner", document_id)
    assert inspected["wordtoolkit_undo_eligible"] is True
    undone = bridge.undo_operation(
        "owner",
        document_id,
        undo_token=inspected["undo_token"],
        expected_version=1,
    )

    assert undone["undone"] is True
    assert undone["live_version"] == 2
    assert undone["policy"]["manual_user_edits_crossed"] is False
    application.undo_entries.insert(0, "Typing")
    guarded = bridge.inspect_undo("owner", document_id)
    assert guarded["wordtoolkit_undo_eligible"] is False
    assert guarded["undo_token"] == ""
    with pytest.raises(WordToolkitError) as error:
        bridge.undo_operation(
            "owner",
            document_id,
            undo_token=inspected["undo_token"],
            expected_version=2,
        )
    assert error.value.code is ErrorCode.AUTH_FORBIDDEN


def test_live_word_rolls_back_when_undo_record_cannot_close(live_bridge) -> None:
    bridge, application, document = live_bridge
    connected = bridge.connect("owner", use_active=True)
    application.UndoRecord.fail_end_once = True

    with pytest.raises(WordToolkitError) as error:
        bridge.insert_text(
            "owner",
            connected["live_document_id"],
            text="Must be rolled back",
            target="document_end",
            as_new_paragraph=True,
            style="",
            expected_version=0,
        )

    assert error.value.code is ErrorCode.EXTERNAL_TOOL_FAILED
    assert document.text == "Existing paragraph\r"
    assert document.undo_calls == 1
    assert bridge.inspect("owner", connected["live_document_id"])["live_version"] == 0
