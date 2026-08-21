# Migration 0017 — guarded live SmartArt text

- Native action count: 99.
- Explicit version/permission/reversibility/output-schema coverage: 12 actions.
- Added `prepare_live_word_smartart_text_edits` and
  `apply_live_word_smartart_text_edits` as lazy actions.
- Callers must obtain the exact live drawing locator, prepare one bounded root and use
  one-time node tokens with the returned live version.
- Apply supports single-line text replacement only. It does not accept raw node indexes,
  COM paths, XML, node creation/deletion/reordering or layout/style/color edits.
- Existing saved-package `inspect_ooxml_diagrams` remains read-only. Do not replace the
  new live workflow with a direct `data*.xml` rewrite; a persisted Diagram Drawing may
  need synchronization by Microsoft Word.
