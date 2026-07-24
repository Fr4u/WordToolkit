# Guarded native authority citations and table of authorities

Date: 2026-07-24

## Scope

This slice adds two narrow live-Word operations:

- `wordtoolkit.mark_live_word_authority_citation/1.0` marks one exact non-empty
  range as a native table-of-authorities entry;
- `wordtoolkit.insert_live_word_table_of_authorities/1.1` creates one editable
  native table of authorities from existing entries.

The operations deliberately do not expose a raw `TA` or `TOA` field-code surface.
Microsoft Word remains responsible for creating, paginating, updating and displaying
the native fields.

## Microsoft object-model basis

The implementation follows the installed Word object model rather than assembling
field instructions as strings:

- [`TablesOfAuthorities.MarkCitation`](https://learn.microsoft.com/en-us/office/vba/api/word.tablesofauthorities.markcitation)
  marks one range and returns a native `Field`.
- [`TablesOfAuthorities.Add`](https://learn.microsoft.com/en-us/office/vba/api/word.tablesofauthorities.add)
  inserts a native table with category, separator and presentation arguments.
- [`TableOfAuthorities.EntrySeparator`](https://learn.microsoft.com/en-us/office/vba/api/word.tableofauthorities.entryseparator)
  documents the entry-to-page-number separator and Word's tab-based default.
- [`TableOfAuthorities.TabLeader`](https://learn.microsoft.com/en-us/office/vba/api/word.tableofauthorities.tableader)
  controls the native leader used by that tab.
- [`WdTabLeader`](https://learn.microsoft.com/en-us/office/vba/api/word.wdtableader)
  defines the supported leader values. WordToolkit maps them to the semantic names
  `spaces`, `dots`, `dashes`, `lines`, `heavy` and `middle_dot`.

`TablesOfAuthorities.Add` has an optional `IncludeSequenceName` argument. Passing an
empty string is not the same thing as omitting that argument. WordToolkit therefore
passes `Type.Missing`, so Word does not construct bogus sequence-based page spans.

## Marking contract

`mark_live_word_authority_citation` requires the current `expected_version` and exactly
one fresh non-empty `selection_token` or `range_token`. Category is bounded to 1..16.
Short and long citation text are each limited to 4,096 single-line characters. When
omitted, they are derived from the exact target range, but the text is never returned.

The action records the field count, calls `TablesOfAuthorities.MarkCitation` in one
custom Undo record and requires exactly one new field of Word type 74. It reacquires the
exact code range and verifies the requested category. Any mismatch requests one Undo;
success advances the live version exactly once and invalidates all range, selection and
Undo grants.

## Insertion contract

`insert_live_word_table_of_authorities` requires at least one matching native entry. A
category from 1..16 selects one category; category 0 means all categories. The target is
the document start, document end, or a fresh collapsed cursor. At most 10,000 existing
tables are admitted.

Separator strings are single-line and at most five characters. The default
entry separator is one tab, the page-range separator is an en dash, and the page-number
separator is comma-space. The default tab leader is dots. The action accepts only the
six semantic leader names above, never a raw COM integer or field switch.

One custom Undo record contains `TablesOfAuthorities.Add`, assignment of the native
`TabLeader`, optional repagination and optional full update. Success requires:

- one-object growth of the native `TablesOfAuthorities` collection;
- one unique reacquired non-empty table range;
- at least one field in that range;
- exact native readback of every requested separator, `Passim`, entry-formatting,
  category-header and tab-leader option.

Any failed condition requests one rollback. The response contains counts, lengths and
the semantic leader name, but never citation text, separator values, generated table
text, field instructions or COM objects.

## Failures found by real Word

The first native proof exposed `0-1`, `0-2` and `0-3` instead of page numbers. The
cause was an empty `IncludeSequenceName` argument. Replacing it with `Type.Missing`
removed the false sequence prefix and restored pages `1`, `2` and `3`.

The second proof had correct numbers but crushed the citation and page number together.
The cause was an empty entry separator. A real tab plus Word's dotted leader fixed the
layout. The contract now reads those options back from the created native object, so a
COM fake or Word build that silently ignores them fails before success is reported.

## Saved-package dependency repair

Word stores each authority mark as a `TA` field and the generated table as a `TOA`
field. The saved-package reference graph now analyses all fields before resolving
cross-field targets. A complete `TA` category resolves to every complete matching
`TOA`; a `TOA` with category 0 accepts every valid authority category. Deleted,
incomplete, malformed and ambiguously categorized fields remain unresolved.

The unified dependency graph maps each resolved `IndexEntry` edge to the actual table
field node instead of emitting a false missing-target warning. This is structural
resolution only: Word, not WordToolkit, remains the authority for pagination and final
display text.

## Verified Word proof

The exact installed `0.39.0+codex.20260724113419` runtime marked three native entries,
inserted one all-category table with default tab and dotted leaders, repaginated,
updated, saved and exported it through Word 16.0. The unlocked package passed Microsoft
Open XML SDK validation with zero errors. Independent inspection found four complete
fields (`TA` x3 and `TOA` x1), three resolved authority dependencies, zero unresolved
dependencies and zero diagnostics. The package graph contains 158 nodes and 239 resolved
edges; all three `field_reference` edges terminate at the same concrete `TOA` field node.
The 14,399-byte DOCX has SHA-256
`28515ac5afbbffd489bae3e6ed62e68b6c7c38d33230b50d52ed28db0a4e3562`.

The three-page A4 PDF displays `Brown v. Board of Education` on page 2 and `Forrester
v. Craddock` on pages 1 and 3 with dotted leaders. Every page was inspected at 144 DPI;
there was no clipping, overlap, raw field syntax, glyph box or false `0-N` prefix. The
44,174-byte PDF has SHA-256
`a824863c425a598dc79be2ec764f34e31a9e122ca1eb24ae2bb7f5b4f6a9d82b`.

## Boundaries still open

- Only installed desktop Word on Windows is qualified for mutation and pagination.
- This slice does not create or rename authority categories, edit an existing mark,
  remove marks, replace an existing table or normalize legal citation style.
- It does not prove equivalent layout across Office versions, languages, printers or
  compatibility modes.
- Dependency resolution is category-based structural evidence, not evaluation of Word's
  field result or legal correctness of a citation.
- A broader locale/version corpus and saved-package mutation planner remain required.
