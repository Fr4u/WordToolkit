# AI interoperability contract

## Scope

WordToolkit exposes one vendor-neutral capability payload through two adapters:

- MCP tool: `get_wordtoolkit_capabilities`;
- native CLI: `wordtoolkit-native capabilities --format json`.

Both adapters call the same in-process generator. Native action and core exposure sets
come from `native_runtime` in the embedded local schema; the runtime no longer carries
a second hard-coded catalogue. The contract is discovery-only. It
does not open Microsoft Word, inspect a file, return document content, invoke an action
handler or contact a network service.

The first executable contract shared by SDK, CLI and MCP is
`wordtoolkit.inspect_ooxml_package/1.0`. The public .NET operation lives in
`WordToolkit.Engine.Operations`; `wordtoolkit-native inspect-package` and MCP call the
same code. `WordToolkitOperationJson` defines the canonical `snake_case` representation
and omission of null values, so transport adapters do not invent their own success
shape. Stable operation errors expose `code`, `message`, optional `reason` and
`retryable`; CLI adds a process exit class and MCP retains its standard tool envelope.

The operation reads only `.docx`, `.docm`, `.dotx` and `.dotm`, enforces bounded OPC/XML
parsing, restores caller stream position, does not mutate a file, does not fetch external
relationships and returns neither the local path nor external targets. `valid_word_package`
requires exact Transitional/Strict relationship and document-root semantics, exactly
one direct `w:body`, and extension/content-type agreement; a suffix
look-alike or empty document root is rejected. Stream labels are portable leaf names,
reject volume/path syntax and are capped at 512 characters, while MCP rejects fields
outside the closed input schema. Default
diagnostic items expose stable codes and severities but
redact package part names and relationship IDs; bounded locations require the explicit
`include_details` opt-in. Full MCP mode keeps legacy runtime timing outside canonical data
for compatibility. An explicit JSON output schema is not yet present in the source
catalogue; the typed .NET result and codec do not erase that remaining manifest gap.

## Version and compatibility

The response identifies:

- `contract_schema`: stable family and major version;
- `contract_schema_version`: the semantic version retained from the embedded local tool
  schema. Fetch its exact bytes with `view=schema` or CLI `--schema`;
- `toolkit_version`: native assembly version;
- `protocols.mcp`: MCP protocol version retained from that same schema;
- `compatibility_policy`: the source schema's declared compatibility rule;
- `source.schema_sha256`: exact embedded local schema bytes;
- `source.native_action_contract_sha256`: canonical native 85-action subset, core
  exposure registry and header;
- `source.capability_schema_sha256`: normative capability JSON Schema bytes.

Clients may cache a page only while the three hashes and toolkit version remain
unchanged. A changed source hash can be whitespace-only; a changed native-action hash
means the filtered executable contract changed. Within contract major version 1,
additive fields and operations are allowed. Removal, type tightening or changed meaning
requires a new major schema plus a migration note.

## Request

```json
{
  "query": "patch",
  "offset": 0,
  "limit": 8
}
```

All fields are optional. `query` is case-insensitive and bounded to 128 characters.
`offset` must be non-negative. `limit` defaults to 12 and cannot exceed 32. Unknown
fields and incorrect JSON types fail with `INVALID_INPUT`; the CLI returns exit code 64.
Set `view` to `schema` without query/paging fields to receive the exact embedded Draft
2020-12 JSON Schema text, its media type and SHA-256. The CLI equivalent is
`capabilities --schema --format json`. The hash is over UTF-8 bytes of `schema_json`, so
an installed client can verify it without repository or filesystem access.

## Response

The response includes global versions, hashes, counts, metadata coverage, limits,
operation-specific format policy, discovery-call security properties, paging and sorted
operation summaries. Each summary contains:

- exact operation name;
- `core` or `lazy` exposure;
- one-sentence description;
- SHA-256 of the exact compact input schema;
- the four canonical MCP effect hints: read-only, destructive, idempotent and
  open-world.

The manifest does not return full input schemas. After selecting one operation, call
`inspect_wordtoolkit_action`; execute only after validating its schema and effect hints.
This keeps discovery bounded instead of paying for all 85 schemas.

## Metadata coverage is evidence, not decoration

`metadata_coverage` counts canonical fields actually present in the embedded source.
The first contract reports full input-schema and MCP-effect coverage, but zero explicit
output-schema, permission, reversibility and per-operation-version coverage. Zero is not
permission to infer those properties from action names. An AI planner must inspect the
chosen operation and obtain explicit user approval for risky mutations until normalized
metadata is added to the source contract.

## Format and backend qualification

Saved Open XML package inspection recognizes `.docx`, `.docm`, `.dotx` and `.dotm`, but
format support remains operation-specific. Live formats are delegated to the installed
Microsoft Word build. The manifest does not claim that every operation supports every
listed extension, that LibreOffice is equivalent to Word, or that a runtime probe has
passed. A future compatible extension will add per-operation and backend availability
records rather than replacing these cautions with inference.

## Examples

MCP:

```json
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_wordtoolkit_capabilities","arguments":{"query":"review","limit":4}}}
```

CLI:

```powershell
wordtoolkit-native capabilities --query review --limit 4 --format json
wordtoolkit-native capabilities --schema --format json
wordtoolkit-native inspect-package .\input.docx --include-details --format json
```

The CLI prints the canonical manifest data to standard output. Usage and validation
errors go to standard error. It never falls through to the MCP loop or creates a Word
COM host.

## Normative files and tests

- `schemas/wordtoolkit-capabilities.v1.schema.json` defines the closed top-level and
  operation-summary shape.
- `native/WordToolkit.Native/Protocol/CapabilityManifest.cs` generates the data.
- `native/WordToolkit.Native/Protocol/CapabilityCli.cs` is the CLI adapter.
- `native/WordToolkit.Native.Tests/CapabilityManifestTests.cs` proves deterministic
  hashes, paging, schema coverage, JSON round-trip, CLI parity, input bounds and the
  no-document-handler security boundary.
- `native/WordToolkit.Engine.Tests/InspectWordPackageOperationTests.cs` proves typed
  path/stream parity, canonical JSON round-trip, read-only behavior, identity checks,
  bounded-package failures, hostile names, extension/content-type checks,
  default diagnostic redaction and stream/cancellation semantics.
- `native/WordToolkit.Native.Tests/InspectPackageCliTests.cs` proves byte-normalized
  SDK/CLI/MCP success parity, closed MCP arguments, stable error codes and the
  no-Word-invocation boundary.
