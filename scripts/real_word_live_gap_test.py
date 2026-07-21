#!/usr/bin/env python3
from __future__ import annotations

import argparse
import asyncio
import json
import os
import time
import zipfile
from contextlib import suppress
from pathlib import Path
from typing import Any

from wordtoolkit import __version__
from wordtoolkit.config import Settings
from wordtoolkit.server.stdio import build_stdio_server

ROOT = Path(__file__).resolve().parents[1]
TEST_FILE_NAME = "WordToolkit-live-competition-test.docx"
MARKER = "WTK_FIND_ALPHA"
REPLACEMENT = "WTK_REPLACED"
COMMENT_TEXT = "WordToolkit live review acceptance comment."


def _payload(result: Any) -> dict[str, Any]:
    structured = getattr(result, "structuredContent", None)
    if structured:
        return dict(structured)
    for item in result:
        text = getattr(item, "text", "")
        if text:
            return dict(json.loads(text))
    raise AssertionError("MCP tool returned no structured payload")


async def _call_ok(server: Any, name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    result = await server.call_tool(name, arguments)
    payload = _payload(result)
    if getattr(result, "isError", False) or payload.get("ok") is not True:
        raise AssertionError(f"{name} failed: {json.dumps(payload, ensure_ascii=False)}")
    return dict(payload["data"])


def _document_by_path(application: Any, path: Path) -> Any:
    wanted = os.path.normcase(str(path.resolve()))
    matches = [
        application.Documents.Item(index)
        for index in range(1, int(application.Documents.Count) + 1)
        if os.path.normcase(str(application.Documents.Item(index).FullName)) == wanted
    ]
    if len(matches) != 1:
        raise AssertionError(f"Expected exactly one open test document, found {len(matches)}")
    return matches[0]


def _wait_for_rot(timeout_seconds: float = 10.0) -> None:
    import pythoncom  # type: ignore[import-untyped]
    import win32com.client  # type: ignore[import-untyped]

    deadline = time.monotonic() + timeout_seconds
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        try:
            active = win32com.client.GetActiveObject("Word.Application")
            if int(active.Documents.Count) >= 1:
                return
        except Exception as exc:
            last_error = exc
        pythoncom.PumpWaitingMessages()
        time.sleep(0.1)
    raise AssertionError(f"Word did not register in the ROT: {type(last_error).__name__}")


async def _exercise_live_tools(server: Any, document_path: Path, document: Any) -> dict[str, Any]:
    connected = await _call_ok(
        server,
        "connect_live_word_document",
        {
            "full_path": str(document_path),
            "use_active": False,
            "activate": True,
        },
    )
    document_id = str(connected["live_document_id"])
    version = int(connected["live_version"])

    found_before = await _call_ok(
        server,
        "find_live_word_text",
        {
            "live_document_id": document_id,
            "search_text": MARKER,
            "match_case": True,
            "whole_word": True,
            "context_chars": 24,
            "max_results": 10,
        },
    )
    if found_before["match_count"] != 2:
        raise AssertionError(f"Native Find returned {found_before['match_count']} matches")

    replaced = await _call_ok(
        server,
        "replace_live_word_text",
        {
            "live_document_id": document_id,
            "search_text": MARKER,
            "replacement_text": REPLACEMENT,
            "match_case": True,
            "whole_word": True,
            "track_changes": "preserve",
            "expected_version": version,
        },
    )
    version = int(replaced["live_version"])
    if replaced["replacements"] != 2 or not replaced["execution"]["rollback_on_error"]:
        raise AssertionError("Transactional live replacement contract was not satisfied")

    found_after = await _call_ok(
        server,
        "find_live_word_text",
        {
            "live_document_id": document_id,
            "search_text": REPLACEMENT,
            "match_case": True,
            "whole_word": True,
            "max_results": 10,
        },
    )
    if found_after["match_count"] != 2:
        raise AssertionError("Replacement postcondition failed")

    layout = await _call_ok(
        server,
        "diagnose_live_word_layout",
        {
            "live_document_id": document_id,
            "max_paragraphs": 100,
            "max_issues": 100,
        },
    )
    if layout["content_returned"]:
        raise AssertionError("Layout diagnosis returned document content")

    first_match = found_after["matches"][0]
    document.Activate()
    document.Range(int(first_match["start"]), int(first_match["end"])).Select()
    selection = await _call_ok(
        server,
        "get_live_word_selection",
        {"live_document_id": document_id},
    )
    added_comment = await _call_ok(
        server,
        "manage_live_word_review",
        {
            "live_document_id": document_id,
            "action": "add_comment",
            "selection_token": selection["selection"]["selection_token"],
            "text": COMMENT_TEXT,
            "expected_version": version,
        },
    )
    version = int(added_comment["live_version"])

    comments = await _call_ok(
        server,
        "inspect_live_word_review",
        {
            "live_document_id": document_id,
            "kind": "comments",
            "limit": 200,
            "include_text": True,
        },
    )
    matching_comments = [
        item for item in comments["items"] if COMMENT_TEXT in item.get("text_preview", "")
    ]
    if len(matching_comments) != 1:
        raise AssertionError("The newly added live comment was not found exactly once")
    comment = matching_comments[0]

    replied = await _call_ok(
        server,
        "manage_live_word_review",
        {
            "live_document_id": document_id,
            "action": "reply_comment",
            "item_index": comment["item_index"],
            "review_token": comment["review_token"],
            "text": "WordToolkit verified reply.",
            "expected_version": version,
        },
    )
    version = int(replied["live_version"])
    if replied["reply_count"] != 1:
        raise AssertionError("Live comment reply was not verified")

    comments_after_reply = await _call_ok(
        server,
        "inspect_live_word_review",
        {
            "live_document_id": document_id,
            "kind": "comments",
            "limit": 200,
            "include_text": True,
        },
    )
    replied_comment = next(
        item
        for item in comments_after_reply["items"]
        if COMMENT_TEXT in item.get("text_preview", "")
    )
    resolved = await _call_ok(
        server,
        "manage_live_word_review",
        {
            "live_document_id": document_id,
            "action": "resolve_comment",
            "item_index": replied_comment["item_index"],
            "review_token": replied_comment["review_token"],
            "resolved": True,
            "expected_version": version,
        },
    )
    version = int(resolved["live_version"])
    if not resolved["resolved"]:
        raise AssertionError("Word did not confirm the resolved comment state")

    tracking_on = await _call_ok(
        server,
        "manage_live_word_review",
        {
            "live_document_id": document_id,
            "action": "set_track_changes",
            "tracking_enabled": True,
            "expected_version": version,
        },
    )
    version = int(tracking_on["live_version"])
    tracked_insert = await _call_ok(
        server,
        "insert_live_word_text",
        {
            "live_document_id": document_id,
            "text": "WordToolkit tracked live insertion",
            "target": "document_end",
            "as_new_paragraph": True,
            "style": "",
            "expected_version": version,
        },
    )
    version = int(tracked_insert["live_version"])
    tracking_off = await _call_ok(
        server,
        "manage_live_word_review",
        {
            "live_document_id": document_id,
            "action": "set_track_changes",
            "tracking_enabled": False,
            "expected_version": version,
        },
    )
    version = int(tracking_off["live_version"])

    revisions = await _call_ok(
        server,
        "inspect_live_word_review",
        {
            "live_document_id": document_id,
            "kind": "revisions",
            "limit": 200,
            "include_text": True,
        },
    )
    if revisions["total_count"] < 1:
        raise AssertionError("Track Changes produced no inspectable revision")
    revision = revisions["items"][0]
    accepted = await _call_ok(
        server,
        "manage_live_word_review",
        {
            "live_document_id": document_id,
            "action": "accept_revision",
            "item_index": revision["item_index"],
            "review_token": revision["review_token"],
            "expected_version": version,
        },
    )
    version = int(accepted["live_version"])

    undo = await _call_ok(
        server,
        "inspect_live_word_undo",
        {"live_document_id": document_id, "max_entries": 20},
    )
    guarded_undo: dict[str, Any]
    if undo["wordtoolkit_undo_eligible"]:
        guarded_undo = await _call_ok(
            server,
            "undo_live_word_operation",
            {
                "live_document_id": document_id,
                "undo_token": undo["undo_token"],
                "expected_version": version,
            },
        )
        version = int(guarded_undo["live_version"])
    else:
        guarded_undo = {
            "undone": False,
            "reason": "Word did not expose a WordToolkit-labeled top Undo entry",
            "history_available": undo["available"],
        }

    saved = await _call_ok(
        server,
        "save_live_word_document",
        {
            "live_document_id": document_id,
            "expected_version": version,
        },
    )
    validation = await _call_ok(
        server,
        "validate_live_word_document",
        {"live_document_id": document_id},
    )
    disconnected = await _call_ok(
        server,
        "disconnect_live_word_document",
        {"live_document_id": document_id},
    )
    if not disconnected["disconnected"]:
        raise AssertionError("Live test handle did not disconnect")

    return {
        "connected_document": connected["document"]["full_name"],
        "find": {
            "before": found_before["match_count"],
            "after": found_after["match_count"],
            "native_find": found_after["performance"]["native_find"],
        },
        "replace": {
            "replacements": replaced["replacements"],
            "single_undo_record": replaced["execution"]["single_undo_record"],
            "rollback_on_error": replaced["execution"]["rollback_on_error"],
            "track_changes_restored": replaced["execution"]["track_changes_restored"],
        },
        "layout": {
            "scanned_paragraphs": layout["scanned_paragraphs"],
            "issue_count": layout["issue_count"],
            "checks": layout["checks"],
            "content_returned": layout["content_returned"],
        },
        "comments": {
            "added_index": added_comment["comment_index"],
            "reply_count": replied["reply_count"],
            "resolved": resolved["resolved"],
            "token_policy": comments["token_policy"],
        },
        "revisions": {
            "inspected": revisions["total_count"],
            "accepted_remaining": accepted["remaining_revisions"],
        },
        "undo": {
            "history_available": undo["available"],
            "eligible": undo["wordtoolkit_undo_eligible"],
            "result": guarded_undo,
            "policy": undo["policy"],
        },
        "saved": saved["saved"],
        "validation": validation["validation"],
        "final_live_version": version,
    }


def _prepare_document(application: Any, path: Path) -> Any:
    document = application.Documents.Add()
    document.Content.Text = (
        f"{MARKER} first occurrence.\r"
        f"{MARKER} second occurrence.\r"
        "Layout probe body paragraph.\r"
        "Another body paragraph.\r"
    )
    document.SaveAs2(str(path), 16)
    document.Activate()
    return document


def main() -> None:
    if os.name != "nt":
        raise SystemExit("The real Word live test requires Windows")
    os.environ["WORDTOOLKIT_AUTH_MODE"] = "local_stdio"
    parser = argparse.ArgumentParser(
        description="Exercise WordToolkit's competitor-gap features in a disposable real Word document"
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=ROOT / "artifacts" / "wordtoolkit-live-competition-test",
    )
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    document_path = output / TEST_FILE_NAME
    report_path = output / "real-word-live-gap-test.json"

    import pythoncom  # type: ignore[import-untyped]
    import win32com.client  # type: ignore[import-untyped]

    pythoncom.CoInitializeEx(pythoncom.COINIT_APARTMENTTHREADED)
    application = None
    document = None
    try:
        application = win32com.client.DispatchEx("Word.Application")
        application.Visible = False
        application.DisplayAlerts = 0
        document = _prepare_document(application, document_path)
        _wait_for_rot()
        validator = (
            ROOT
            / "tools"
            / "OpenXmlValidator"
            / "bin"
            / "Release"
            / "net8.0"
            / "win-x64"
            / "wordtoolkit-openxml-validator.exe"
        )
        if not validator.is_file():
            raise AssertionError(f"Open XML SDK validator was not found: {validator}")
        server = build_stdio_server(
            Settings(
                auth_mode="local_stdio",
                storage_root=output / "storage",
                public_base_url="http://127.0.0.1",
                openxml_validator_path=validator,
            )
        )
        report = asyncio.run(_exercise_live_tools(server, document_path, document))
        report.update(
            {
                "passed": True,
                "wordtoolkit_version": __version__,
                "test_harness_launched_word": True,
                "word_version": str(application.Version),
                "document_path": str(document_path),
                "docx_is_zip": zipfile.is_zipfile(document_path),
            }
        )
        report_path.write_text(
            json.dumps(report, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        print(json.dumps(report, indent=2, ensure_ascii=False))
    finally:
        if document is not None:
            with suppress(Exception):
                document.Close(False)
        if application is not None:
            with suppress(Exception):
                application.Quit(False)
        document = None
        application = None
        pythoncom.CoUninitialize()


if __name__ == "__main__":
    main()
