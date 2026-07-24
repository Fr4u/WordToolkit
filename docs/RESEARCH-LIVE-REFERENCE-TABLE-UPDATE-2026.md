# Guarded live reference-table update — 2026-07-24

## Question

WordToolkit could create a native table of figures, but it had no high-level action for
refreshing reference tables that already existed in a connected document. An AI agent
could fall back to raw field instructions or a broad document-wide field update. Both
paths are rotten boundaries: they expose implementation syntax, touch more document
state than requested and cannot prove which native objects survived the operation.

## Authoritative Word behavior

- [`TableOfContents.Update`](https://learn.microsoft.com/en-us/office/vba/api/word.tableofcontents.update)
  refreshes the entries shown in one native table of contents. Microsoft documents a
  separate [`UpdatePageNumbers`](https://learn.microsoft.com/en-us/office/vba/api/word.tableofcontents.updatepagenumbers)
  method for the narrower page-number operation.
- [`TableOfFigures.Update`](https://learn.microsoft.com/en-us/office/vba/api/word.tableoffigures.update)
  refreshes the entries shown in one native table of figures. It also has a separate
  [`UpdatePageNumbers`](https://learn.microsoft.com/en-us/office/vba/api/word.tableoffigures.updatepagenumbers)
  method.
- [`TableOfAuthorities.Update`](https://learn.microsoft.com/en-us/office/vba/api/word.tableofauthorities.update)
  refreshes entries in one native table of authorities. The documented
  [`TableOfAuthorities` object](https://learn.microsoft.com/en-us/office/vba/api/word.tableofauthorities)
  exposes `Update`, but not the same cross-family page-number-only contract. WordToolkit
  therefore must not pretend that one uniform `page_numbers_only` option exists.
- [`Document.Repaginate`](https://learn.microsoft.com/en-us/office/vba/api/word.document.repaginate)
  asks the installed Word build to repaginate the document. This is meaningful before a
  reference-table update because page numbers are layout results, not package-only facts.
- [`Fields.Update`](https://learn.microsoft.com/en-us/office/vba/api/word.fields.update)
  can refresh a broad field collection, but Microsoft warns that its reported failing
  field index may be incorrect in Word 2016 and possibly other versions. A guarded
  object-specific action should not treat that return value as proof of success.

## Resulting contract

`wordtoolkit.update_live_word_reference_tables/1.0` accepts a connected document ID,
required optimistic version, `kind=all|table_of_contents|table_of_figures|table_of_authorities`,
an optional one-based index for one exact kind, and two execution flags. The operation:

1. rejects a stale version, a missing target, an invalid kind/index combination and any
   request that would touch more than 128 native objects before opening an Undo record;
2. captures all three collection counts and validates every selected object's duplicate
   range plus a non-empty field collection;
3. disables screen updating only for the bounded call, starts one custom Word Undo
   record and repaginates by default;
4. calls the native full `Update` method on each selected Word object;
5. requires all three collection counts to remain stable, reacquires every target by
   kind/index and validates its range and fields again;
6. advances the live version and invalidates stale selection/range/Undo grants only
   after every check succeeds;
7. requests one bounded Undo and returns an error if any call or readback fails.

The compact success response contains only object counts, the requested selector,
repagination status, verification flags, document metadata and timing. It never returns
generated table text, field instructions or raw COM objects.

## Deliberate limits

- This is a refresh operation, not reference repair. It does not rewrite malformed field
  instructions, rebuild bookmarks, infer missing citations or create absent tables.
- Validation proves native object/range/field survival and stable collection cardinality;
  it does not prove the linguistic correctness of entries or cross-version pagination.
- The operation covers the three dedicated Word object collections only. Indexes,
  bibliographies and arbitrary fields require separate typed contracts.
- Microsoft Word remains the layout authority. A saved DOCX validation and rendered PDF
  are still required before a visually important document is called complete.

## Verification gates

The unit harness covers all-kind update, one exact indexed target, disabled
repagination, zero targets, the 128-object ceiling, response privacy and automatic Undo
after invalid post-update range readback. Release evidence must additionally use the
installed packaged runtime against real Microsoft Word, save the document, validate its
OOXML with the Microsoft Open XML SDK and inspect the Word-rendered PDF.

The final installed `0.39.0+codex.20260724100603` proof satisfies those gates in Word
16.0. One action updated one object from each of the three collections while preserving
1/1/1 counts. Microsoft Open XML SDK validation returned zero errors. The saved package
contains 15 complete fields (`TOC` 2, `PAGEREF` 9, `SEQ` 2, `TA` 1, `TOA` 1), with zero
reference issues and no external or application-invoking fields. A false warning found
during this proof was fixed at its source: `TA` now reads the Word-defined long citation
from the `\l` switch rather than demanding a nonexistent positional target. The
four-page Word PDF was rendered at 150 DPI and every page was inspected without finding
clipping, overlap or broken glyphs. This remains one installed Word build and one
document, not cross-version or corpus-wide proof.
