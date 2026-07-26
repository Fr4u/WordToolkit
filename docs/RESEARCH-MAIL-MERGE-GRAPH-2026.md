# Native Word mail-merge graph — research and implementation boundary (2026)

## Result

WordToolkit now models saved Word mail merge as a typed graph instead of treating
`w:mailMerge` as a few settings strings. The graph joins:

- the settings-level configuration;
- top-level and ODSO data-source relationships;
- ODSO field mappings, including Word's positional predefined-address semantics;
- the optional recipient-data part and every saved include/exclude record;
- `MERGEFIELD` and `MERGEBARCODE` field objects from every projected story;
- binding decisions from field target to zero, one, or several mappings;
- source-linked diagnostics and the shared cross-domain dependency graph.

The implementation is read-only. It parses the exact saved OPC package. It does not
open Word, Excel, Access, ODBC, OLE DB, a data source, a query, a recipient table or an
external relationship. It does not execute or preview a merge.

## Normative and application evidence

The implementation was checked against primary Microsoft material:

- [Word VBA `MailMerge` object](https://learn.microsoft.com/en-us/office/vba/api/word.mailmerge)
  defines the application object boundary and exposes destination, data source and
  execution state. WordToolkit deliberately models saved evidence, not this live
  execution surface.
- [Word VBA `MailMerge.OpenDataSource`](https://learn.microsoft.com/en-us/office/vba/api/word.mailmerge.opendatasource)
  shows that opening a source is an effectful application operation. The package graph
  therefore never treats a saved connection string or relationship as permission to
  open it.
- [MS-OI29500 `fieldMapData`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/df0bbd4b-c5e4-4836-a65c-f956185a0f8d)
  records a Word-specific deviation: for the 30 predefined address fields, Word uses
  the `fieldMapData` position and ignores a conflicting `mappedName`. The graph retains
  both the declared value and the Word-effective positional name and reports a
  disagreement.
- [MS-OI29500 recipient-data relationship](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/af3dc913-8c08-4843-ab40-495a92170b96)
  requires the recipient-data relationship role. The graph validates Transitional and
  Strict relationship types and fails closed on missing, ambiguous, mistyped or missing
  internal targets.
- [MS-OI29500 recipient-data part](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/b05d3137-d2c6-4c6e-9afa-eb92d7ccf317)
  constrains the part's cardinality and outgoing relationships. WordToolkit reports a
  recipient part with other incoming cardinality or any owned relationship as an
  error.

The parser accepts Transitional and Strict WordprocessingML and relationship
namespaces and both standardized and legacy Microsoft recipient-data content/element
namespaces.

## Semantic decisions

### Configuration and sources

`WordMailMergeConfiguration` retains non-content execution metadata and bounded saved
query/connection values. `WordMailMergeRelationship` separates four roles:

1. top-level data source;
2. header source;
3. ODSO source;
4. recipient data.

Existence, relationship-type validity, target existence, externality and structural
resolution are independent facts. An external URI can be structurally resolved without
being opened or followed.

### Mappings

Each `fieldMapData` receives a stable package-bound ID, position, source column,
declared mapped name, Word-effective predefined name, column index, language ID and
dynamic-address flag. Duplicate source names remain explicit. A conflict between
`mappedName` and Word's positional rule is information, not silent normalization.

### Recipients

Each saved recipient record exposes inclusion state, column, identity kind and stable
ID. Missing and ambiguous identities are diagnostics. Identity values are never shown
by the default MCP response; a process-keyed HMAC fingerprint supports equality checks
without making short or predictable identities dictionary-attackable.

### Fields and binding

Mail-merge fields reuse the lossless reference graph. `MERGEFIELD` and
`MERGEBARCODE` targets bind case-insensitively to the source-column name first, then to
the Word-effective predefined name. Results are explicit:

- `resolved_by_source_column_name`;
- `resolved_by_word_predefined_name`;
- `ambiguous`;
- `missing`;
- `not_applicable` for other mail-merge control fields.

No field is evaluated. A field result already stored in the document is not presented
as current source truth.

## Public inspection contract

`inspect_ooxml_mail_merge` provides paged `summary`, `configuration`,
`relationships`, `mappings`, `recipients`, `fields` and `issues` views.

Default output is token-lean and redacts:

- query, connection and UDL strings;
- table and source-column names;
- declared mapping and field-target names;
- mail subject and address-field metadata;
- recipient identities;
- relationship targets and source locations.

Sensitive values, relationship targets and source provenance require three independent
opt-ins. The response has a 65,536-character projected-item budget and the whole
operation uses the shared 640 MiB accounted resource lease. The action has no network
or Microsoft Word permission.

`analyze_ooxml_document/1.1` adds content-free mail-merge counts and the
`MAIL_MERGE_EVIDENCE` routing signal. It returns no query, source name, target, identity
or document text.

## Scale evidence

The checked-in synthetic benchmarks use 30 mapped fields and unique saved recipient
identities, repeat graph construction seven times, and exclude package/semantic/
settings/reference setup from the graph timing:

| Recipients | Median | p95 | Accounted graph bytes | Median allocated bytes | Peak working set |
|---:|---:|---:|---:|---:|---:|
| 10,000 | 314.36 ms | 367.06 ms | 24,942,744 | 105,107,424 | 175,599,616 |
| 100,000 | 2,151.73 ms | 2,770.77 ms | 248,862,744 | 1,030,632,576 | 1,166,508,032 |

Evidence:

- `docs/benchmarks/mail-merge-10k-2026-07-26.json`
- `docs/benchmarks/mail-merge-100k-2026-07-26.json`

These are one Windows 10 x64 workstation and .NET 8.0.29, not universal latency
claims. The 100,000-recipient allocation and peak working set are heavy. They are
reported instead of hidden; a future streaming recipient-data projection is still an
optimization target.

## Tests

The regression corpus covers:

- complete Transitional and Strict packages;
- Word's 30-position mapping rule;
- resolved source-column bindings;
- included/excluded and uniqueTag/hash recipients;
- wrong relationship types, missing internal targets, ambiguous recipient identities
  and forbidden recipient-part relationships;
- mapping, recipient, field, metadata and operation-budget limits;
- shared dependency nodes and edges;
- default MCP redaction, independent disclosure flags and no-COM execution;
- content-free high-level analysis and closed output-schema conformance.

## Remaining boundary

This tranche does not implement data-source drivers, schema discovery, record value
materialization, conditional merge regions, template slot constraints, deterministic
merge execution, live Word `MailMerge` control, generated-document comparison or a
mail-merge editor. It also does not prove that a remote database, workbook, query or
credential is valid. Those are separate, effectful capabilities and must never be
smuggled into a saved-package inspector.
