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
for compatibility. This initial operation does not yet have an explicit catalogue output
schema; the manifest reports that gap rather than inferring one from its .NET type.

The second executable contract is `wordtoolkit.transform_ooxml_package/1.0`. Its typed
request chooses `replace_first_text_occurrence`, `accept_all_tracked_changes` or
`reject_all_tracked_changes`, always writes to a distinct new path and returns the exact
result fingerprint. SDK, CLI and MCP share the same core and canonical JSON. Untouched
entries keep their source bytes; signed packages, existing outputs, ambiguous MCE or
revision text and unsafe review shapes fail closed. The replace operation can span
ordinary WordprocessingML runs but excludes OfficeMath. Review-all uses the existing
source-preserving review planner, requires zero remaining revisions/move ranges in the
candidate and retains exact inverse proof internally.

The same operation also sits behind `wordtoolkit-native docx-platform-adapter`, a direct
implementation of `docx-platform-tests` protocol v1. That process receives only the
neutral operation JSON and input package, never the scenario assertions or expected
output. Codes 0/1/2/3 mean success/error/unsupported/protocol mismatch. This adapter is
a test interoperability seam, not a third mutation implementation.

The third shared executable contract is
`wordtoolkit.query_ooxml_semantics/1.0`. `QueryWordPackageOperation` performs bounded OPC
reading, Word semantic projection and selection for SDK, `query-package` CLI and direct
saved-package MCP requests. Its projected-document overload also backs process-memory
semantic-index queries, so the adapter no longer owns a second result mapper. The
canonical result keeps existing `node_id`, `kind`, paging and candidate-plan fields and
adds object category, story, child count, identity semantics, projection facts and an
explicit disclosure record. An optional local package fingerprint is an optimistic-read
precondition. It opens no Word process, follows no external relationship and returns no
raw XML. Engine and CLI execution construct no COM host. The MCP process initializes its
shared COM host for the wider live-Word surface, but this operation never dispatches work
to that host or starts Word.

Properties remain opt-in. `anchor`, `author`, `date`, `guid`, `initials`, `instruction`
and `name` values are redacted by default even when properties are requested; returning
them requires both `include_properties=true` and
`include_sensitive_properties=true`. Property filters may still select by an exact value
without echoing it. Every result marks document content as untrusted data.
Values longer than the public property budget are shortened and named in
`truncated_property_names`; source locators are never silently shortened. Complex-field
instruction text is suppressed from node and ancestor/subtree previews unless the same
second opt-in is present. `disclosure` reports sensitive properties and sensitive text
previews separately.

This operation was the first source-catalogue entry with `operationVersion`, a closed
MCP successful `structuredContent` output schema, normalized filesystem/network/Word
permissions and a reversibility record. Tool errors keep `isError=true`; current MCP SDKs
skip output-schema validation for that error path. The paged capability manifest returns
its existing closed v1 summaries plus honest metadata-coverage counts; the operation
version, permissions, reversibility record and full output schema stay behind
`inspect_wordtoolkit_action`. This avoids silently widening a closed v1 operation-summary
object and breaking clients that validate against the original schema.
This follows the pinned MCP 2025-06-18
[`Tool.outputSchema`](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/schema/2025-06-18/schema.ts)
contract and the official TypeScript SDK rule that successful structured content is
validated while `isError=true` results skip output-schema validation.

The saved-package dependency inspector applies the same content-minimization rule to
resource safety. Engine callers receive the full deterministic graph accounting record,
including exact compact-adjacency index bytes. The default AI response receives only
`byte_budget: {model, used, maximum}`. It does not receive GC snapshots, raw allocation
breakdowns or another diagnostic array on every call. Dependency diagnostic items require
`include_issues=true` or `view=issues`; the schema is regression-checked against every
Engine dependency node and edge kind so typed clients cannot lose new filters. Crossing
the budget is a
`PACKAGE_LIMIT`, not a prompt to retry with more disclosure. This byte boundary is local
to the dependency graph and never claims that upstream semantic and typed projections
share one process-memory quota.

The fourth shared executable boundary consists of
`wordtoolkit.plan_ooxml_semantic_edits/1.0` and
`wordtoolkit.apply_ooxml_semantic_edits/1.0`. `StyleWordPackageOperation` owns all seven
typed style commands, strict command/selector validation, selector expansion, deterministic
intent hashing, exact-candidate projection, signature checks and atomic application.
`StyleEditOperationJson` is the single closed JSON parser used by CLI and MCP; a field
valid for one command variant is not silently accepted on another. `set_style_where`
requires an explicit `max_matches`, rejects empty selection and cannot expand the whole
transaction beyond 200 resolved operations. Request JSON is capped at 256 Ki characters,
at most 200 package parts may change, and validation issue locations require the existing
`include_details` opt-in.

The fifth shared boundary is
`wordtoolkit.plan_ooxml_comment_body_edits/1.0` plus
`wordtoolkit.apply_ooxml_comment_body_edits/1.0`. `CommentBodyWordPackageOperation`
selects comment definitions by stable semantic `comment_id`, matches exact bounded text
across adjacent runs in one ordinary direct comment paragraph, binds the requested match
count and optional exact source-body hash into the reviewed plan, and lowers the result to
the shared lossless text transaction core. Paragraphs, table cells, tabs, breaks, fields,
content controls and rich structures are explicit segment boundaries rather than
characters silently discarded by a flattened search. Its
strict JSON codec is shared by the `comment-body-package` CLI and lazy MCP actions.
Responses expose counts, hashes and changed-part evidence, never comment text or raw XML.
Exact-candidate validation reprojects both the semantic document and review graph and
proves that anchors, authors, reply topology, durable IDs, reactions, revisions,
permissions and every unselected comment are invariant before the Microsoft schema
adapter is consulted. Apply then rebuilds the plan, blocks signatures and writes
atomically with a recovery backup by default.

Schema validation crosses a deliberate dependency boundary. The plain `net8.0`
`WordToolkit.Engine` project declares `IWordPackageCandidateValidator` but does not depend
on Microsoft or any AI provider. `WordToolkit.OpenXmlSdk` implements the interface with
Microsoft Open XML SDK 3.3.0 using bounded baseline-versus-candidate error comparison.
Plan reports a missing validator; apply fails closed with `VALIDATOR_REQUIRED`. The
native CLI and MCP inject the standard adapter, reject new schema errors, rebuild the
reviewed plan against the current fingerprint and use the atomic writer with a retained
backup by default. The backup is recovery evidence. Public rollback is a separate
`plan_ooxml_patch_rollback` -> `apply_ooxml_patch_rollback` transaction that derives the
reverse patch from the original artifact, binds its own plan ID to the exact destination
and re-runs the same validation and independent risk gates.

The atomic writer also closes the last non-cooperative race it can observe without an OS
transaction: after `File.Replace`, the displaced backup must still match the reviewed
fingerprint. If it does not, the exact displaced bytes are restored and the operation
returns `VERSION_CONFLICT`. If a second writer changes the destination during that
compensation, the newer bytes are retained in an opaque sibling `.conflict` artifact and
the operation returns `RECOVERY_REQUIRED` instead of deleting them. Failed compensation
exposes at most two still-existing artifact names; it exposes neither absolute paths nor
document content and omits details when no artifact exists. Exceptions thrown by an injected validator are untrusted
and collapse to fixed `VALIDATION_FAILED` text. Bounded numeric/structural diagnostics are
still preserved for an ordinary `OOXML_SCHEMA_INVALID` result.

## Version and compatibility

The response identifies:

- `contract_schema`: stable family and major version;
- `contract_schema_version`: the semantic version retained from the embedded local tool
  schema. Fetch its exact bytes with `view=schema` or CLI `--schema`;
- `toolkit_version`: native assembly version;
- `protocols.mcp`: MCP protocol version retained from that same schema;
- `compatibility_policy`: the source schema's declared compatibility rule;
- `source.schema_sha256`: exact embedded local schema bytes;
- `source.native_action_contract_sha256`: canonical native 115-action subset, core
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

The closed v1 summary deliberately does not grow per-operation version, output-schema,
permission or reversibility fields. Their presence in the source catalogue is counted by
`metadata_coverage`; retrieve their exact values for the selected action through
`inspect_wordtoolkit_action`. This preserves the published v1 summary shape while making
coverage gaps visible.

The manifest does not return full input schemas. After selecting one operation, call
`inspect_wordtoolkit_action`; execute only after validating its schema and effect hints.
This keeps discovery bounded instead of paying for all 115 schemas.

## Metadata coverage is evidence, not decoration

`metadata_coverage` counts canonical fields actually present in the embedded source.
All 115 operations have input schemas and MCP effect annotations. Extension catalog,
observability and OOXML-encryption/numbering inspection, semantic query,
semantic HTML/SVG rendering, semantic-style plan/apply, comment-body plan/apply and the
safe formatter plan/apply plus the Word-executed drawing-layout and version-profile inspectors,
isolated feature-behavior probes and guarded
SmartArt text prepare/apply pair, native caption/table-of-figures/table-of-contents
insertion, native authority-citation marking/table-of-authorities insertion, native
index-entry/index insertion, guarded reference-table update and the saved-package
patch rollback plan/apply pair and Flat OPC conversion have explicit
output-schema, permission, reversibility and
per-operation-version metadata;
the remaining 86 are still uncovered. Missing metadata
is not permission to infer behavior from action names. An AI planner must inspect the
chosen operation and obtain explicit user approval for risky mutations until normalized
metadata is added to each source contract.

`inspect_wordtoolkit_observability` is a runtime-health action, not a document query.
Its summary-first result is bounded and excludes arguments, document content, package
XML, paths and relationship targets. Event correlation IDs and record hashes are
independent opt-ins. Audit remains off unless the host explicitly chooses bounded memory
or local JSONL mode; no remote exporter exists. The append chain is explicitly
unauthenticated, so an AI must not present it as signed compliance evidence.

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
wordtoolkit-native query-package --request .\query.json --format json
wordtoolkit-native style-package --mode plan --request .\style-plan.json --format json
wordtoolkit-native transform-package .\input.docx .\output.docx --operation accept_all_tracked_changes --format json
wordtoolkit-native flat-opc-package .\input.docx .\transport.xml --direction to_flat_opc --format json
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
- `native/WordToolkit.Engine.Tests/TransformWordPackageOperationTests.cs` proves
  cross-run first-only replacement, opaque-entry preservation, OfficeMath exclusion,
  MCE/revision ambiguity rejection, accept/reject-all, signature/output collision gates
  and clean-package cloning.
- `native/WordToolkit.Native.Tests/TransformPackageCliTests.cs` proves canonical
  Engine/CLI/MCP parity, closed arguments, protocol-v1 exit semantics, honest unsafe-input
  decline and the no-Word-invocation boundary.
- `native/WordToolkit.Engine.Tests/QueryWordPackageOperationTests.cs` proves read-only
  package hashing, path/stream parity and restoration, semantic object projection,
  stale-read rejection, indexed/linear parity, extension/content-type enforcement,
  snake-case enum/additive-result JSON behavior, explicit truncation and two-stage
  sensitive-property disclosure.
- `native/WordToolkit.Native.Tests/QueryPackageCliTests.cs` proves byte-normalized
  Engine/CLI/MCP result parity, local fingerprint error parity, closed request JSON,
  actual-result/output-schema conformance, output/permission discovery and zero Word-host
  invocation.
- `native/WordToolkit.Engine.Tests/StyleWordPackageOperationTests.cs` proves public
  plan/apply, strict variant JSON, selector bounds, validator fail-closed behavior,
  baseline-aware Microsoft schema validation, opaque-part preservation and backup
  restoration.
- `native/WordToolkit.Native.Tests/StylePackageCliTests.cs` proves Engine/CLI/MCP plan
  parity, JSON CLI apply, stable conflict exits and zero Word-host invocation.
