# Operation-wide dependency-inspection resource lease — 2026-07-23

This note records the design and evidence for `word_operation_accounted_v1`. It closes
the admitted gap where `inspect_ooxml_dependencies` built an OPC snapshot, semantic
document and eight typed source graphs before the graph-local 128 MiB budget existed.
It does not claim an exact process-memory ceiling.

## Boundary

The Native adapter creates one `WordOperationResourceLease` before `OpcPackageReader`
and passes the same instance through:

1. ZIP central-directory preflight, retained OPC entries and derived OPC metadata;
2. semantic projection;
3. style projection;
4. numbering projection;
5. reference projection;
6. section and settings projection;
7. chart projection;
8. figure and caption projection;
9. content-control and Custom XML projection;
10. table projection;
11. final dependency-graph construction.

The production ceiling is 640 MiB (671,088,640 accounted bytes). Callers can lower an
Engine lease for stricter environments, but the MCP input contract cannot raise the
server ceiling. The existing 128 MiB `dependency_graph_accounted_v1` budget remains an
independent nested defense.

## Accounting model

`word_operation_accounted_v1` is deterministic and cumulative:

- 4,096 bytes for the operation;
- 4,096 bytes for each entered projection stage;
- before materializing `ZipArchive.Entries`: the bounded tail preflight buffer, central
  directory bytes and 256 fixed bytes per declared entry;
- before retaining an OPC entry: 320 fixed bytes, aligned byte-array storage and the
  entry-name string;
- before retaining OPC content types, parts, relationships and diagnostics: bounded
  fixed record/index allowances plus their strings;
- before a guarded lossless XML parse: 8,192 fixed bytes plus twelve times the source
  byte count, aligned to eight bytes;
- before retaining each semantic node: 1,024 fixed bytes plus its retained strings and
  property key/value strings;
- before retaining each semantic fingerprint-cache value: 192 fixed bytes plus the
  aligned fingerprint string;
- bounded fixed item charges for typed selector/index state whose inputs already exist
  in the package or semantic graph;
- every graph-local dependency charge forwarded once into the shared lease.

The XML factor covers the source copy, decoded UTF-16 text, `XDocument`, lexical model,
element arrays/maps and derived typed projection allowance. It is a stable conservative
engineering constant, not a CLR object-layout formula. Repeated parsing consumes the
lease repeatedly because the allocations repeat. Earlier charges are not released.
That makes the contract resistant to cumulative projection work but more conservative
than a simultaneous-live-set model.

Every rejected charge reports only its stable stage, current usage, maximum and attempted
bytes. It never includes paths, XML, document text or identifiers. The MCP alias is
`wop1` and the success response adds only:

```json
"operation_budget": {"model":"wop1","used":539282576,"maximum":671088640}
```

The existing graph tuple remains:

```json
"byte_budget": {"model":"wdg1","used":130132744,"maximum":134217728}
```

After 0.39 is published, changing these accounting rules requires a new Engine model and
MCP alias; published numbers cannot be silently reinterpreted. The unreleased v1 model
was recalibrated after red-team review added the missing OPC preflight/metadata charges.

## Pre-allocation controls

- ZIP entry-count and central-directory-byte limits are read directly from bounded EOCD/
  ZIP64 metadata before `ZipArchive.Entries` is exposed. Malformed bounds fail as invalid
  data, including arithmetic-overflow attempts;
- OPC entry accounting occurs before `GC.AllocateUninitializedArray<byte>`;
- content-type declarations, relationships and diagnostics have explicit count ceilings;
  non-seekable streams use a 576 MiB capped, delete-on-close disk spool before the same
  central-directory preflight instead of relying on implicit unbounded ZIP buffering;
- Lossless XML accounting occurs before `ReadOnlyMemory<byte>.ToArray()` and therefore
  before decoding, DOM construction and lexical scanning.
- semantic node and fingerprint accounting occurs before insertion into retained trees
  and caches;
- semantic fingerprint recursion now observes the request cancellation token;
- table, figure and content-control aggregate byte checks reject the next part before
  its XML parse rather than after it;
- dependency node, edge and issue charges still occur before retained graph insertion.

Local XML element-count ceilings that require a completed parse remain separate. Count
selector arrays in several typed graphs are bounded by their existing local ceilings but
are not individually exact heap-accounted; their stage parser/input allowances cover
them conservatively.

## Calibration

The historical 0.38.0 99,997-node point accounted 130,132,744 graph-local bytes and
peaked near 576 MiB working set across its checked-in five-run series. With the shared
lease enabled, the same generated package accounts 539,282,576 operation bytes:

| Stage | Accounted bytes |
|---|---:|
| OPC package | 2,258,848 |
| semantic projection | 281,231,576 |
| styles | 4,096 |
| numbering | 4,096 |
| references | 38,678,416 |
| sections/settings | 9,603,424 |
| charts | 4,352 |
| figures/captions | 38,678,416 |
| content controls | 4,096 |
| tables | 38,678,416 |
| dependency graph | 130,132,744 |
| operation base | 4,096 |

A 512 MiB candidate failed deterministically during final graph construction at
536,870,624 accounted bytes. Keeping it would have broken a previously admitted
production boundary. The 640 MiB default admits the same point at 80.4% usage and leaves
131,806,064 accounted bytes of margin. Five cold Release processes measured 2,956.082 ms
median dependency build, 5,491.3294 ms median package/semantic/graph total and 615,305,216
bytes median peak working set. This is calibration for one repetitive corpus, not proof
for dense charts, Custom XML, figures, fields or mixed hostile documents.

## Primary runtime evidence

- `GC.GetTotalMemory` is a managed-heap measurement excluding fragmentation, not a
  deterministic per-operation quota:
  <https://learn.microsoft.com/dotnet/api/system.gc.gettotalmemory>.
- `GC.GetGCMemoryInfo` reports global collector and machine/container pressure, not a
  stable document-specific limit:
  <https://learn.microsoft.com/dotnet/api/system.gc.getgcmemoryinfo>.

## Remaining fracture

- XML stories are still reparsed by multiple typed graphs. A shared immutable parsed
  representation is the next structural memory fix.
- Cumulative accounting intentionally does not release prior stage charges and therefore
  is not a peak-live-memory estimator.
- The twelve-times XML factor and fixed record constants need mixed-domain hostile-corpus
  calibration on Linux and Windows server GC as well as workstation GC.
- The lease protects `inspect_ooxml_dependencies`; other saved-package inspections still
  use their existing independent limits until explicitly migrated.
