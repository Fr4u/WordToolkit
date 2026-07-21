# Tool schema and document migrations

`schemas/mcp-tools.v1.json` is immutable after a release. Additive optional fields may be introduced in a minor release. Renames, removals, type changes, changed side effects, or stricter required inputs require a new major schema and an entry in this directory.

Document migrations are explicit and copy-on-write: a migration reads a source artifact, writes a new DOCX version, lists modified OPC parts, validates the result, and produces a preservation report. WordToolkit never silently rewrites an uploaded original.

