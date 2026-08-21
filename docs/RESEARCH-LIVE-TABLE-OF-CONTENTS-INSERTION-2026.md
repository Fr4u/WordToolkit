# Native table-of-contents insertion — 2026-07-24

## Problem

WordToolkit could inspect and refresh an existing contents table but could not create
one through a high-level guarded operation. Falling back to `insert_live_word_fields`
would force the caller or model to compose a raw `TOC` instruction, understand Word's
switch grammar and preserve locale/version behavior. That is the wrong abstraction and
an avoidable source of malformed fields.

## Primary Word evidence

Microsoft documents `TablesOfContents.Add` as the native creation API. Its first
argument is the insertion range; a non-collapsed range is replaced. The remaining
arguments cover heading styles, upper/lower heading levels, TC fields and identifier,
page-number alignment/inclusion, additional styles, hyperlinks, web page-number hiding
and outline levels:

- <https://learn.microsoft.com/en-us/office/vba/api/word.tablesofcontents.add>
- <https://learn.microsoft.com/en-us/office/vba/api/word.tableofcontents>
- <https://learn.microsoft.com/en-us/office/vba/api/word.tableofcontents.update>
- <https://learn.microsoft.com/en-us/office/vba/api/word.document.repaginate>

## Contract decision

`wordtoolkit.insert_live_word_table_of_contents/1.0` is action 103. It requires
`live_document_id` and `expected_version`. The insertion target is `document_start`
(default), `document_end` or a fresh token-verified collapsed `cursor`. Heading levels
are integers from 1 through 9 with lower greater than or equal to upper. At least one of
`use_heading_styles` and `use_outline_levels` must be true.

The first slice deliberately fixes TC-field use, table identifiers and additional-style
strings to disabled/empty values. Those options need their own typed source and style
resolution instead of a string-shaped escape hatch. The public inputs cover page-number
inclusion/alignment, hyperlinks, web page-number hiding, repagination, update and screen
update suppression. Raw field instructions are neither accepted nor returned.

The mutation runs on the persistent Word STA thread in one non-replayable custom Undo
record. It rejects a document that already reaches the 10,000-object bound, calls native
`TablesOfContents.Add`, optionally repaginates and updates, then requires:

1. exactly one additional `TablesOfContents` object;
2. one uniquely reacquired range with the same start and end;
3. a non-empty range; and
4. at least one native field in that range.

Any mismatch requests one Word Undo before the error crosses the tool boundary. Success
advances the live version and invalidates selection, range and Undo grants. The response
contains counts, position, options and verification flags, never result text, field code
or a COM object.

## Verification

The final installed build is `0.39.0+codex.20260724102603`. Capability discovery from
that exact executable reports 103 actions, 15 exposed tools and 16 explicit metadata
contracts. Focused fake-COM tests cover native success/privacy, invalid source settings
before Undo and rollback when Word returns no readable field. The complete gates pass
498 Engine, 355 Native and 1,309 Python tests with 16 intentional skips; Ruff and both
C# format verifiers pass.

The exact installed runtime created a disposable document in Word 16.0, inserted five
Heading 1/2 entries over three pages, created the contents table at position zero,
repaginated, updated, saved and exported through Word. Reconnection validation returned
`valid=true`; Microsoft Open XML SDK was available, valid and reported zero errors.

Because Word kept the source DOCX locked for the independent package reader, an exact
copy was inspected without closing the user's Word session. The 14,992-byte copy at
`artifacts/table-of-contents-insertion-proof/native-toc-insertion-103234-unlocked.docx`
has SHA-256 `718627cd5f91b126aced63f5f1cc3890cc15fefa3fb9cd99567a4c2d63ff0982`.
It contains six complete complex fields: one `TOC` and five nested `PAGEREF`; all five
dependencies resolve, with zero issues, incomplete fields, external fields or
application-invoking fields.

Word exported a three-page A4 PDF at
`artifacts/table-of-contents-insertion-proof/native-toc-insertion-103234.pdf`. It is
40,742 bytes with SHA-256
`bafdd436de71c37e8cc481948b16fed4b839818e9383907d9d10e94af472f221`.
All three pages were rendered at 150 DPI and inspected. The first page shows a populated
two-level contents table with leaders and page numbers 1–3; all headings and body text
remain legible, with no clipping, overlap, black glyph boxes or raw field syntax.

## Remaining boundary

This is not full TOC equivalence. Typed TC-entry creation/table identifiers, additional
style mappings, chapter numbering/separators, page-number-only update, cross-locale and
multi-Office-version corpus coverage, and a saved-package TOC mutation planner remain.
The action therefore closes native heading/outline-based creation, not the whole Word
reference system.
