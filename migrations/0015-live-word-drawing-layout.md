# Live Word drawing layout

## Added

- Lazy action: `inspect_live_word_drawing_layout`.
- Operation contract: `wordtoolkit.inspect_live_word_drawing_layout/1.0`.
- Native action count: 95.
- Tested live-action count: 49.

## Client behavior

Use the action only with a current `live_document_id`. Start with a small page and no
text. Enable group members, SmartArt nodes, sensitive text or viewport pixels only when
the next decision consumes them.

Do not persist `wdlo_` IDs. They are traversal-scoped runtime locators, not package IDs.
Do not interpret alignment constants as point offsets, group-local coordinates as page
coordinates, or `Window.GetPoint` pixels as document geometry. Compare against
`inspect_ooxml_figures`/`inspect_ooxml_diagrams` when package provenance matters; Word
may normalize declared groups and diagram shapes into different runtime object kinds.

No existing action was removed or tightened. The addition is compatible within the
local v1 schema policy.
