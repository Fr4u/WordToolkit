# Word numbering sequence execution research — 2026

## Result

WordToolkit now has a bounded, source-linked list-sequence executor instead of only a
`numbering.xml` inventory and single-level resolver. It walks semantic paragraphs in
source order, resolves direct and paragraph-style numbering, maintains independent
counter state per Word story and `numId`, applies restarts, and returns stable paragraph,
item and sequence identities. Counter certainty and rendered-label certainty are separate:
the engine can report an exact integer while refusing to invent a locale-dependent label.

The implementation is exposed as `inspect_ooxml_numbering` with `view=sequences`. It is
read-only, never opens Word, never returns paragraph text, pages at most 100 items per call,
and publishes a closed sequence-item contract. The full executor is capped at 100,000
paragraphs, 100,000 numbered items, 10,000 diagnostics, 64 MiB per parsed XML part and a
512-node ancestry walk.

## Standards and Word behavior used

The base model follows the ISO/Open XML `num`, `abstractNum`, `lvl`, `lvlOverride`,
`startOverride`, `lvlRestart`, `lvlText` and `isLgl` structures. Microsoft documents that
`startOverride` supplies the initial and restarted value and, under the standard, wins over
a conflicting child-level `start` value in the same override. See the
[Open XML SDK `StartOverrideNumberingValue` contract](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.startoverridenumberingvalue?view=openxml-3.0.1).

Word-specific deviations are not guessed:

- Word accepts `lvlRestart` values only from 0 through 7, ignores `lvlRestart` inside a
  replacement `lvl`, and treats a trigger level as that level or any higher level. An
  omitted value means the immediately preceding level or any higher level. See
  [MS-OE376, `lvlRestart`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/dcd6f842-6686-4b2c-8201-9bfbe582af45).
- `w15:restartNumberingAfterBreak` restarts an opted-in abstract definition at a section
  boundary. See
  [MS-DOCX, `restartNumberingAfterBreak`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/cbddeff8-01aa-4486-a48e-6a83dede4f13).
- Word allows at most nine `%1` through `%9` substitutions in `lvlText`, limits the final
  label to 31 characters, and ignores the entire label pattern if it references a level
  deeper than the current level. See
  [MS-OI29500, `lvlText`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/74f54646-dbe0-40ea-90c9-5ec70107e91d).
- Legal numbering converts referenced components to decimal before composing the label;
  the declaration is represented by
  [Open XML SDK `IsLegalNumberingStyle`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.islegalnumberingstyle?view=openxml-3.0.1).
- A missing `numFmt` has the documented decimal default. See
  [Open XML SDK `NumberingFormat`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberingformat?view=openxml-3.0.1).

## A documented rule that real Word contradicted

Microsoft's interoperability note says Word ignores `w:start` when it appears inside the
replacement `w:lvl` under `w:lvlOverride`. See
[MS-OI29500, `start`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/8f145055-5422-4df0-933d-e67a81c633cd).
The standard-facing `startOverride` documentation also says `startOverride` wins when the
two values disagree.

That is not what the qualified desktop build did. A schema-valid fixture contained:

```xml
<w:lvlOverride w:ilvl="1">
  <w:startOverride w:val="3"/>
  <w:lvl w:ilvl="1">
    <w:start w:val="9"/>
    <w:numFmt w:val="lowerLetter"/>
    <w:lvlRestart w:val="0"/>
    <w:lvlText w:val="%1.%2"/>
  </w:lvl>
</w:lvlOverride>
```

Microsoft Word 16.0 build 16.0.20131 returned list values
`1, 9, 10, 2, 9, 1, 9` and labels
`1., 1.i, 1.j, 2., 2.i, 1., 1.i`. This proves three facts for that build:

1. replacement-level `start=9` took precedence over `startOverride=3`;
2. replacement-level `lvlRestart=0` was ignored, because the deeper level restarted at 9
   after the next level-zero item instead of continuing to 11;
3. `restartNumberingAfterBreak=1` restarted the sequence after the section boundary.

The guarded real-Word test validates the fixture first with Microsoft's Open XML SDK,
opens it read-only and hidden, compares `ListFormat.ListValue` and `ListString` with the
engine, closes it without saving, and proves the package SHA-256 is unchanged. The engine
therefore follows the qualified Word result for this conflict and emits
`word_uses_start_inside_level_override` plus
`word_prefers_level_override_start_over_start_override`. The action names the applied rule
`override_level_start_precedes_start_override_on_qualified_word_build`; it does not pretend
that one Office build settles every historical or future version.

## Counter execution

State is isolated by `(story root, numId)`, so a header, footer, note, comment, glossary
entry or text box cannot silently advance the main-document sequence. A level starts from
the qualified replacement-level start, then `startOverride`, then the base abstract-level
start. Unknown or negative starts yield `unresolved_start`; integer overflow yields
`overflow`.

After each exact item, higher-level use resets deeper levels according to Word's restart
cascade. `lvlRestart=0` means never restart. An omitted rule means restart on the previous
level or any higher level. An invalid Word rule does not become a guessed counter: the
affected item reports `unresolved_restart_rule`. A section boundary resets only states
whose effective abstract definition has `restartNumberingAfterBreak=true`.

Paragraph numbering comes from document defaults, a base-first paragraph-style chain and
direct `numPr`, in that order. Direct `numId=0` removes inherited numbering. A direct
`ilvl` wins. When a paragraph-style level is involved, the executor records the Word
compatibility rule rather than silently presenting it as pure ISO behavior.

Paragraphs inside tracked-revision or unresolved Markup Compatibility wrappers are not
assigned an arbitrary view. They are counted as numbered but skipped with an explicit
error, making `counter_coverage_complete=false`.

## Label execution

Exact formatting is intentionally limited to deterministic, locale-independent formats:

- `decimal` and `decimalZero`;
- `upperRoman` and `lowerRoman` for 1 through 3999;
- `upperLetter` and `lowerLetter` for positive values through 1,000,000;
- `none`.

Picture bullets are identified as picture bullets, not faked as Unicode characters.
Custom formats and locale-dependent formats such as Japanese counting keep their exact
counter evidence but return `unsupported_number_format`. Missing, invalid, too-long or
counter-dependent labels likewise have distinct statuses. This is deliberate: a parser
may lack a renderer, but it must not turn uncertainty into plausible-looking fiction.

## Proof

Automated coverage includes nested counters, higher-level restarts, never-restart levels,
legal numbering, conflicting replacement-level start behavior, `startOverride`, section
restarts, style inheritance, direct removal, unsupported formats, invalid `lvlText`,
missing starts, ambiguous revisions and hard paragraph/item limits. The native test also
checks the closed sequence-item schema, operation metadata, absence of paragraph text and
unknown-argument rejection before any package read.

This is still not numbering repair. There is no counter mutation, locale-aware label
renderer, picture-bullet rasterization, revision-view selector, HTML/SVG list integration
or cross-version Office matrix. Those omissions remain explicit.
