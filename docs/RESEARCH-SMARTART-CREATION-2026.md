# Guarded live SmartArt creation research, 2026-08-23

## Result

WordToolkit can now inspect the layout catalog exposed by a connected Microsoft Word
process and create one inline native SmartArt object from a reviewed, opaque layout
token. This is a narrow creation path, not a claim of complete SmartArt editing parity.

Microsoft documents `Application.SmartArtLayouts` as the installed layout collection
and exposes scalar layout members such as `Id`, `Name`, `Category`, and `Description`:

- <https://learn.microsoft.com/en-us/office/vba/api/word.application.smartartlayouts>
- <https://learn.microsoft.com/en-us/office/vba/api/overview/library-reference/smartartlayout-members-office>

Microsoft documents `InlineShapes.AddSmartArt(Layout, Range)` as the inline insertion
entry point. WordToolkit uses that exact two-argument call; it does not route a raw
`Application` object through the generic member executor:

- <https://learn.microsoft.com/en-us/office/vba/api/word.inlineshapes.addsmartart>

## Contract

`inspect_live_word_smartart_layouts`:

- scans at most 2,048 layouts and returns at most 100 per page;
- returns scalar metadata only and records unavailable metadata explicitly;
- issues a random 256-bit lowercase hexadecimal token bound to the connected document,
  `live_version`, layout index, and exact layout ID;
- returns no raw COM object, XML, path, document text, user identity, or licence data.

`insert_live_word_smartart`:

- requires the current `expected_version`, one fresh layout token, and exactly one fresh
  `range_token` or `selection_token`;
- reacquires the layout and verifies the exact layout ID before the first write;
- collapses a duplicated target range to its start, so selected source text is not
  silently deleted;
- consumes the layout token before mutation;
- inserts exactly one inline shape in one custom Word Undo record;
- verifies the inline-shape count delta, non-empty inserted range, native SmartArt
  identity, and exact layout readback;
- increments `live_version` only after all checks pass;
- uses the existing verified rollback and quarantine path on any mismatch.

The 2,048-layout scan ceiling is terminal. When Word reports more entries,
`catalog_truncated=true`, `truncated=true`, and `next_offset=null` at the ceiling mean
that the policy cap was reached, not that an unbounded continuation is available.

## Evidence

The gated real-Word acceptance test ran on 2026-08-23 against Microsoft Word
`Version=16.0`, `Build=16.0.20326`. It created a hidden unsaved scratch document, read
the first installed layout token, found a native range token, inserted one SmartArt
object, and independently verified through Word COM that:

- `InlineShapes.Count` changed from zero to one;
- the returned object reported native SmartArt;
- `SmartArt.Layout.Id` exactly matched the reviewed catalog layout ID;
- the scratch document closed without saving.

Command:

```powershell
$env:WORDTOOLKIT_REAL_WORD_SMARTART_CREATION_TEST='1'
C:\Users\Admin\.dotnet8\dotnet.exe test native\WordToolkit.Native.Tests\WordToolkit.Native.Tests.csproj -c Release --filter FullyQualifiedName~RealWordSmartArtCreationAcceptanceTests --no-restore
```

Fake-host regressions separately cover schema conformance, scalar catalog projection,
successful insertion, one-time token reuse rejection, unknown token rejection, readback
mismatch rollback, unchanged version after rollback, Undo balance, and restoration of
screen updating.

## Deliberate limits

The action creates inline SmartArt only. It does not create floating shapes, add or
delete nodes, reorder hierarchy, modify layout/style/color after insertion, return
SmartArt text, or guarantee pixel layout. Visual acceptance still requires native Word
PDF or page-image rendering and human or image-based inspection.
