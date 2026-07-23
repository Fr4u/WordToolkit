# Tool schema and document migrations

`schemas/mcp-tools.v1.json` is the immutable historical remote contract. `schemas/mcp-tools.v2.json` is current. Additive optional fields may be introduced within one major schema. Renames, removals, type changes, changed side effects, or stricter required inputs require a new major schema and an entry in this directory.

Document migrations are explicit and copy-on-write: a migration reads a source artifact, writes a new DOCX version, lists modified OPC parts, validates the result, and produces a preservation report. WordToolkit never silently rewrites an uploaded original.
