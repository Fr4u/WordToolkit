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

## Eligibility proof

A direct property element is eligible only when all of these statements are true:

1. It belongs to the exact source-bound `w:pPr` or `w:rPr` for a projected paragraph or
   run.
2. Its property set is completely understood by the existing typed style reader.
3. It is not structural. Paragraph style, numbering, section, revision and run-style
   links are never formatter candidates.
4. It is not a composite superseding group. Shading, run fonts, color and underline are
   excluded until a group-aware proof exists.
5. Every modeled property has at least two resolver contributions.
6. The last contribution is direct formatting from the same semantic node.
7. Its resulting value equals the immediately preceding cascade value.

This is only candidate selection. It is not permission to persist a change.

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

This is a real formatter slice, not a complete formatter. It does not yet simplify
composite properties, conditional table styles, revision views, application defaults,
`stylesWithEffects`, layout or rendered pagination. Those areas remain untouched until
their equivalence can be proved and checked against a representative Word-rendered
corpus.

## Verified release checkpoint

Release `0.39.0+codex.20260724071855` passed 493 Engine tests, 334 Native tests and
1,309 Python/OOXML tests with 16 intentional optional-environment skips; Ruff reported
no errors. Two .NET SDK 8.0.423 package builds produced identical 196-file,
86,619,377-byte trees and identical 36,626,102-byte ZIPs at SHA-256
`9c60c1897c1f8667a77ec107372979bfd96a9f041d0c6aca96dfd46f292d2156`.
The enabled personal source and cache contain the same 196 files with zero differences,
and installed capability discovery reports the exact version and 97 actions.

The installed runtime formatted a deliberately redundant valid package without opening
Word. Planning selected six elements (116 source bytes) in one part. Engine, semantic,
effective-formatting and baseline-aware Open XML checks all passed; the written package
matched its predicted fingerprint and the source SHA-256 remained unchanged. Replanning
the result returned no changes, and no-op apply created no file. The existing Word PID
was unchanged. LibreOffice rendered source and result into equal-size one-page PDFs;
their 144-DPI PNG pages were byte-identical at SHA-256
`2454d70c5b864ae96a11ec8f0d57180007a6b2e508123137853ac195e3e1b441`.
That is a useful independent rendering check, but it does not replace the still-open
multi-version Microsoft Word visual corpus.
