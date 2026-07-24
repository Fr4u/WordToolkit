# Safe Word formatter research and contract

Last updated: 2026-07-24.

## Decision

WordToolkit does not implement a generic OOXML pretty-printer. That idea is rotten at
the root: whitespace, element order, compatibility islands and application-private
markup can carry meaning or trigger Word normalization. Rewriting every part merely to
make XML look neat gives no user-visible value and creates a large corruption surface.

The first formatter policy is deliberately narrower:
`remove_redundant_direct_formatting`. It removes an existing paragraph/run formatting
element only after proving that the complete modeled cascade produces the same result
without it. The formatter changes no text, style definition, style assignment,
numbering, section boundary, revision property container or relationship.

## Source model

WordprocessingML text is organized through paragraphs and runs; run properties carry
run-level formatting, while paragraph properties carry paragraph-level formatting.
Microsoft's Open XML guidance describes the run and paragraph structures directly:

- [Working with runs](https://learn.microsoft.com/en-us/office/open-xml/word/working-with-runs)
- [Working with paragraphs](https://learn.microsoft.com/en-us/office/open-xml/word/working-with-paragraphs)

Styles live in the styles part and are applied through style references rather than by
copying every property into every paragraph. Microsoft documents both applying an
existing paragraph style and adding a style definition:

- [Apply a style to a paragraph](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-apply-a-style-to-a-paragraph-in-a-word-processing-document)
- [Create and add a paragraph style](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-create-and-add-a-paragraph-style-to-a-word-processing-document)

The SDK exposes the styles collection and document defaults as separate typed elements:

- [Styles class](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.styles?view=openxml-3.0.1)
- [DocDefaults class](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.docdefaults?view=openxml-3.0.1)

Those sources support the structural model. The exact redundancy algorithm and its
safety bounds are WordToolkit's implementation decision, not a claim copied from
Microsoft documentation.

Composite properties cannot be treated as one scalar. The SDK surface exposes separate
theme and fallback attributes for fonts, color, underline and shading, while Microsoft's
Word-specific notes document cases where the theme member changes how the fallback is
used and where an omitted table-cell shading element falls back to the table style:

- [RunFonts.AsciiTheme](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.runfonts.asciitheme?view=openxml-3.0.1)
- [Color.ThemeColor](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.color.themecolor?view=openxml-3.0.1)
- [Underline class](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.underline?view=openxml-3.0.1)
- [Shading class](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.shading?view=openxml-3.0.1)
- [MS-OE376: table-cell shading](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/c7a6a5fd-538c-4a77-8cbb-0f447298dace)

The last source is also the reason a formatter must not remove formatting inside a table
while conditional table-style resolution is absent. Equal visible fallback attributes do
not prove equal group behavior.

## Eligibility proof

A direct property element is eligible only when all of these statements are true:

1. It belongs to the exact source-bound `w:pPr` or `w:rPr` for a projected paragraph or
   run.
2. Its property set is completely understood by the existing typed style reader.
3. It is not structural. Paragraph style, numbering, section, revision and run-style
   links are never formatter candidates.
4. The node does not depend on an unresolved conditional-table, numbering,
   revision-view, unmodeled-property or Word numbering-compatibility cascade layer.
5. For a scalar element, every modeled property has at least two resolver contributions.
6. For a scalar element, the last contribution is direct formatting from the same
   semantic node and its resulting value equals the immediately preceding cascade value.
7. For `rFonts`, `color`, `u` or paragraph/run `shd`, the element enters the separate
   composite proof below. No per-attribute shortcut is accepted.

This is only candidate selection. It is not permission to persist a change.

## Composite group proof

Composite candidates are evaluated in deterministic source order against the cumulative
candidate built so far. For each element, the planner temporarily adds its exact
source-span removal, serializes the candidate in memory, reparses the OPC package,
reprojects semantic content, rebuilds style/numbering/theme/settings/font graphs and
compares complete effective formatting for every affected node. Paragraph candidates
also cover descendant runs. Only a passing trial remains in the cumulative patch set.

This catches the failure a scalar comparison misses: a direct `w:rFonts w:ascii="Aptos"`
can suppress an inherited `w:asciiTheme="minorHAnsi"`; the cached ASCII name may look
equal while deleting the direct element restores a theme member and changes the group.
Equivalent full font/color/underline/shading groups are removed; partial or theme-drifting
groups remain byte-for-byte untouched.

The planner attempts at most 64 composite proofs. Crossing that ceiling aborts the whole
plan. It never silently stops after formatting only the beginning of a document. The
compact response exposes the attempted count as
`scan.composite_candidate_proofs`. Candidate-by-candidate full-package projection is
deliberately correctness-first; incremental invalidation remains future work.

## Candidate proof

The planner removes only exact source spans in an isolated package candidate and then:

1. predicts the complete package fingerprint;
2. serializes and reparses the candidate through the bounded OPC engine;
3. verifies that only planned parts changed;
4. reprojects every semantic story and compares a content-only semantic fingerprint;
5. rebuilds style, numbering, theme, settings and font-table graphs;
6. resolves formatting again for every affected node; paragraph changes also require
   proof for descendant runs;
7. compares effective paragraph/run property maps, toggle semantics, style IDs,
   omissions, warnings and unmodeled-element inventories;
8. compares Microsoft Open XML SDK errors with the source baseline.

Any mismatch rejects the entire plan. There is no partial success.

## Mutation and privacy contract

Planning is read-only. Apply rebuilds the plan from the current source and requires an
apply-plan ID bound to the candidate validation, signature state and reviewed absolute
output path. The destination must not exist, must preserve the source extension and is
created atomically. Source overwrite is impossible through this action. A no-op writes
nothing. Signed packages, unavailable validation, new validation errors or truncated
validation block apply.

Responses contain only counts, fingerprints, policy names, bounded property element
names and optional source part/ordinal metadata. They never contain document text, raw
XML or COM objects, and the formatter never opens Microsoft Word.

## Current boundary

This is a real formatter slice, not a complete formatter. It now simplifies four
composite element families only through the bounded package-level proof above. It still
does not resolve conditional table styles, revision views, application defaults,
`stylesWithEffects`, layout or rendered pagination. Those unresolved layers remain
untouched until their equivalence can be proved and checked against a representative
Word-rendered corpus.

## Verified release checkpoint

Release `0.39.0+codex.20260724080018` passed 497 Engine tests, 334 Native tests and
1,309 Python/OOXML tests with 16 intentional optional-environment skips; Ruff reported
no errors. Two .NET SDK 8.0.423 package builds produced identical 196-file,
86,623,437-byte trees and identical 36,627,205-byte ZIPs at SHA-256
`6bb2fce0a85bf61f03aeab320c68af985061bbcbff02e09b55299872f759a66f`.
The enabled personal source and cache contain the same 196 files with zero differences,
and installed capability discovery reports the exact version and 97 actions.

The installed runtime formatted a deliberately redundant valid package without opening
Word. Planning scanned 12 candidates, executed five composite proofs and selected 11
elements (330 source bytes) in one part. Engine, semantic, effective-formatting and
baseline-aware Open XML checks all passed; the written package matched its predicted
fingerprint `ce2bb1fa46ff438053b9ff4e7c0b498198c9130783e56431dbd57817cfe8e8dc`.
Replanning the result returned no changes, and no-op apply created no file.

The same installed runtime then connected the source and opened the result read-only in
Microsoft Word. Both saved snapshots were valid with zero Microsoft Open XML SDK errors
and exported as one-page 23,821-byte PDFs. Poppler rasterization at 144 DPI produced
byte-identical PNG pages at SHA-256
`2a882af2560fb684e55664c647e964ae3eebd98403292eaf07def3463895c966`.
The rendered page was visually inspected and retained its font, color, underline and
shading without clipping or overlap. The existing Word PID 14820 was unchanged. This is
a real licensed Word equality point, but it still does not replace a representative,
versioned Microsoft Word visual corpus.
