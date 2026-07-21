# 0002 — round-trip and visual hardening

Implementation release: 0.1.1. Tool schema: still v1; no MCP tool was removed,
renamed or given a new required input.

Document-engine changes are copy-on-write compatible:

- package-root `_rels/.rels` is now inspected and validated like every other
  relationship part;
- unreachable OPC parts are reported as `ORPHANED_PART` warnings;
- inline DrawingML receives unique, schema-ordered `wp:docPr` metadata;
- table widths and merged-cell widths are deterministic, while layout risk is
  evaluated against the effective section and column width;
- LaTeX/UnicodeMath conversion preserves cases, equation arrays and adjacent
  identifier boundaries;
- duplicate body-level section properties are consolidated without dropping
  non-conflicting header/footer or page settings;
- PDF page previews are cleaned, rendered sequentially and decoded before use.

No stored document is migrated in place. Reopening and exporting an older DOCX
creates a new validated version under the normal preservation contract.
