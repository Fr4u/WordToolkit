# WordToolkit 0.62.0 release qualification

Date: 2026-08-29

Runtime: `0.62.0+codex.20260829173741`

## Scope and boundaries

- Live text, inline runs and exact-selection formatting now expose every bounded writable
  scalar `Word.Font` property used by ordinary Word ranges: script-specific names and
  bidirectional size; RGB/indexed/diacritic/underline colors; Latin and bidi emphasis;
  all 18 underline styles; strike, scripts, caps, hidden text and four scalar effects;
  scaling, spacing, position and kerning; East Asian emphasis/grid controls; OpenType
  ligatures, number forms/spacing, stylistic sets and contextual alternates.
- `clear_character_formatting` calls `Font.Reset`, clears highlight explicitly and then
  applies sibling overrides in one rollback-aware transaction.
- Each requested scalar is immediately read back from COM. An ignored property or mixed
  `wdUndefined=9999999` result fails as `FORMATTING_INVALID` with the exact field.
- The native and Python surfaces return canonical enum names, booleans and `#RRGGBB`
  colors rather than raw COM integers.
- Relative `Grow`/`Shrink`, content-mutating `Range.Case`, deprecated animation and nested
  OfficeArt fill/glow/reflection/3-D objects are deliberately excluded from the scalar
  ordinary-body-text contract. The documentation states those boundaries instead of
  pretending they are safe formatting flags.

## Static and automated qualification

- Deterministic native formatting schema generation is idempotent and updates six direct
  schema objects. The two operation tools reuse one `liveCharacterFormatting` definition
  rather than duplicating 43 fields for parent and run formatting.
- The 17-tool public catalog remains below its 25,000-character gate after the surface
  expansion.
- Python: 1,610 passed, 17 intentionally skipped, one existing Starlette deprecation
  warning.
- Engine: 939/939 passed on pinned .NET SDK 8.0.423.
- Native: 979/979 passed in Release.
- LibreOffice: 12/12 passed in Release.
- Formatting unit/contract slice: 50/50 passed, including every underline enum and
  `wdUndefined` failure behavior.
- Plugin validator: passed.

## Real Microsoft Word qualification

- A dedicated hidden Word instance applied and read back every new scalar on exact run
  ranges, proved heterogeneous ranges return `wdUndefined`, saved DOCX, exported PDF,
  passed WordToolkit package inspection and Microsoft Open XML SDK validation, and
  preserved literal OOXML text under `AllCaps`.
- The final PDF page was rasterized at 240 DPI and visually inspected. Subscript,
  superscript, colored double/wavy underlines, OpenType text, East Asian emphasis dots,
  emboss/outline, raised text and clear-formatting output were visible without clipping,
  overlap, black boxes or unreadable glyphs.
- The exact packaged executable passed a safe scratch-document MCP smoke:
  17 public tools, two formatting operations, live version 2, native formatting verified,
  scratch closed without save. It did not target the active user document.
- The general live acceptance script now defaults to a scratch document. Active-document
  testing requires an explicit `-UseActiveDocument` switch for disposable fixtures.

## Reproducible artifact and installation

Two independent package builds contained 204 files and produced byte-identical ZIPs:

- asset: `WordToolkit-0.62.0+codex.20260829173741-native-win-x64.zip`;
- size: 38,494,696 bytes;
- SHA-256: `62edd0ee8a2b6e987b1200b29dd70696fe41faa2b3f16d34878ddc3b4a4fbb9f`;
- executable SHA-256:
  `6294d215209635102f9073d12e163c6368189a284599e4d4372f87caf22e4ef0`;
- runtime assembly SHA-256:
  `1453db47fb379fb93de72ac7cb97da055c45ff78984c202ea177c91b471c21c1`.

The local `wordtoolkit@personal` installation is enabled at
`0.62.0+codex.20260829173741`. Source and installed cache contain 204/204 matching files
with zero missing, extra or hash-mismatched entries. The previous marketplace source is
preserved at `C:\Users\Admin\plugins\wordtoolkit.backup-0.61.4-20260829164917`; the
superseded pre-review 0.62.0 candidate is preserved separately at
`C:\Users\Admin\plugins\wordtoolkit.backup-0.62.0-20260829164917`.
