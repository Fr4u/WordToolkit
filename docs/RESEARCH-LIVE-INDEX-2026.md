# Guarded native Word index entries and indexes — 2026-07-24

## Result

WordToolkit now exposes `wordtoolkit.mark_live_word_index_entry/1.0` and
`wordtoolkit.insert_live_word_index/1.0`. The first asks Word to create one real hidden
`XE` field at an exact token-bound location. The second asks Word to create one editable
`INDEX` field from existing entries. Neither action asks an AI agent to assemble field
instructions, and neither returns private entry/bookmark/cross-reference text or the
generated index body.

The shared `wordtoolkit.update_live_word_reference_tables/1.1` action now includes the
native `Indexes` collection. Saved-package reference and dependency graphs also resolve
complete non-deleted `XE` sources to concrete complete `INDEX` field nodes instead of
leaving every index entry as a false unresolved target.

## Primary Word object-model evidence

- [`Indexes.MarkEntry`](https://learn.microsoft.com/en-us/office/vba/api/word.indexes.markentry)
  inserts an `XE` field after a range and returns the created `Field`. Its semantic inputs
  include entry hierarchy, cross-reference, bookmark page range and bold/italic page
  numbers.
- [`Indexes.Add`](https://learn.microsoft.com/en-us/office/vba/api/word.indexes.add)
  returns an `Index` and accepts heading separator, page-number alignment, indented or
  run-in layout, column count, accented-letter grouping, sort and language options.
- The official [`Index` object](https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.interop.word.index?view=word-pia)
  exposes the range and readable `AccentedLetters`, `HeadingSeparator`, `NumberOfColumns`,
  `RightAlignPageNumbers`, `SortBy`, `TabLeader` and `Type` properties plus `Update`.
- [`WdHeadingSeparator`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.interop.word.wdheadingseparator?view=word-pia),
  [`WdIndexType`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.interop.word.wdindextype?view=word-pia)
  and [`WdTabLeader`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.interop.word.wdtableader?view=word-pia)
  provide the exact enum values used by the semantic mappings.
- Microsoft Support's
  [index workflow](https://support.microsoft.com/en-us/word/create-and-update-an-index)
  confirms that Word collects marked entries, sorts them, removes duplicate same-page
  references and supports subentries, cross-references and page-number emphasis.

## Safety contract

`mark_live_word_index_entry` requires `expected_version` and exactly one fresh selection
or range token. Empty `main_entry` derives from the target and therefore requires
non-empty target text; explicit text also permits a collapsed insertion point. Each
hierarchy component is single-line and colon-free, because hierarchy is represented by
the `subentries` array rather than hidden field grammar. Total composed entry and optional
cross-reference text are capped at 4,096 characters. Bookmark names are capped at Word's
40-character boundary and must resolve through the connected document before mutation.
A cross-reference cannot be combined with bookmark-based pagination or page-number
formatting because those are different index-entry result modes.

After native `Indexes.MarkEntry`, WordToolkit requires exactly one field-count increment,
type `4`, one unique exact code range and parsed readback of entry hierarchy,
cross-reference/bookmark switches and bold/italic flags. Any mismatch requests one Undo.

`insert_live_word_index` requires at least one complete parseable type-4 `XE` field and
accepts only bounded semantic settings. It calls `Indexes.Add`, applies a semantic tab
leader, optionally repaginates and updates, then requires exactly one index-collection
increment, one unique non-empty type-8 field range and exact readback of every exposed
native option. Sort method and index language are deliberately left to Word in contract
1.0; exposing locale-dependent values without a qualified language/runtime matrix would
be a false promise.

## Explicit remaining boundary

This slice does not yet expose Mark All, concordance-file AutoMark, AutoText entry or
cross-reference sources, phonetic reading, explicit East Asian sort/language/filter,
built-in index formats, editing/removing existing marks, or durable saved-package index
mutation. Word remains the layout, sorting, pagination and rendering authority. Those
limits stay visible in the goal audit rather than being buried behind a generic field
escape hatch.

## Verification evidence

Unit, full-suite, installed-runtime, Microsoft SDK, saved-package graph and Word-rendered
PDF evidence will be appended after the exact release candidate passes every gate. Until
then this section is intentionally not a claim of real-Word completion.
