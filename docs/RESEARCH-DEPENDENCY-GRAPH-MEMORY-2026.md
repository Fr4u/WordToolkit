# Dependency-graph memory boundary research — 2026-07-23

This note records the evidence and design choice behind the first byte budget for the
unified Word dependency graph. It is not a claim that the whole document-engine process
now has a hard resident-memory ceiling.

## Primary evidence

- Microsoft documents `GC.GetTotalMemory` as the managed heap size excluding
  fragmentation. It is useful for measurement, but it cannot prove the maximum live
  process footprint or provide a deterministic per-operation safety contract:
  <https://learn.microsoft.com/dotnet/api/system.gc.gettotalmemory>.
- `GC.GetGCMemoryInfo` reports collector and machine/container memory information. Its
  high-memory threshold is a GC heuristic based on global physical-memory pressure, not
  a stable package-specific quota:
  <https://learn.microsoft.com/dotnet/api/system.gc.getgcmemoryinfo> and
  <https://learn.microsoft.com/dotnet/api/system.gcmemoryinfo.highmemoryloadthresholdbytes>.
- Microsoft describes `FrozenDictionary` as a read-only lookup structure with a
  relatively high construction cost and explicitly says it should be initialized only
  with trusted keys because keys affect construction time. A hostile, build-once DOCX
  graph is therefore not an automatic fit:
  <https://learn.microsoft.com/dotnet/api/system.collections.frozen.frozendictionary-2>.
- `ArrayPool<T>` loans mutable buffers and may retain returned arrays. Microsoft warns
  that ownership after `Return` is relinquished and that incorrect reuse is a
  high-severity security error. The final immutable graph cannot safely retain rented
  arrays; pooling remains a possible future optimization only for short-lived scratch
  buffers with strict clearing and ownership:
  <https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1.return>.

## Measured fracture

Five cold-process pre-change 0.37.0 runs on the checked-in 100,000-node synthetic fixture
each produced 99,997 dependency nodes and 99,996 edges. The medians were 234,933,120
retained managed bytes, 1,678,719,936 allocated managed bytes, 3,244.6 ms dependency
build time and 575,979,520 bytes peak working set. The graph constructor duplicated both
edge lists into two `GroupBy`/dictionary adjacency maps, with one ordered edge array per
populated node.

The historical 998,998-node point peaked above 5.2 GiB and allocated about 13.9 GiB.
The numeric one-million-node ceiling was therefore a rejection count, not a meaningful
memory boundary.

## Implemented design

`WordDependencyGraph` now stores adjacency as two compact compressed-row indexes:

- one offset array and one edge-index array for incoming edges;
- one offset array and one edge-index array for outgoing edges;
- one node-ID-to-index map;
- zero per-node adjacency dictionaries or edge arrays;
- allocation-free typed adjacency views for the common direct `foreach` path.

The exact retained adjacency-index payload is:

```text
2 * (node_count + 1) * sizeof(int) + 2 * edge_count * sizeof(int)
```

Construction checks cancellation while counting, prefixing, filling and sorting those
indexes.

## Deterministic byte accounting

The `dependency_graph_accounted_v1` model charges before retaining each new graph item:

- 4,096 base bytes;
- 320 fixed bytes per unique node;
- 352 fixed bytes per unique edge;
- 192 fixed bytes per issue;
- every retained string field as eight-byte-aligned `24 + 2 * character_count` bytes.

The fixed charges deliberately include a conservative allowance for records, arrays,
hash entries, references and construction drafts. Shared strings may be charged more
than once. This makes the value deterministic and conservative as an operation-local
allocation proxy, but it is not an exact CLR object-layout calculation.

The production default is 128 MiB. Node, edge, issue and 65,536-character per-key and
per-metadata limits remain independent. The builder rejects before retaining the item
that would cross the byte budget. The Engine exposes the full usage record; the MCP
summary exposes only `{model, used, maximum}` as `byte_budget` so that the safety proof
does not destroy the low-token contract.

## After-change measurement

On the same host and fixture, five final 0.38.0 cold-process runs each accounted
130,132,744 bytes and used 1,599,952 bytes for both compact adjacency indexes. Median
retained managed memory was 195,381,520 bytes and median managed allocation was
1,608,727,168 bytes. Against the five-run 0.37.0 series, median retained managed memory
fell by 39,551,600 bytes (16.8%), median managed allocations by 69,992,768 bytes (4.2%),
median dependency construction from 3,244.6 ms to 2,954.1 ms (9.0%) and median total
measured time by 5.4%. Median peak working set moved from 575,979,520 to 576,376,832
bytes (+0.1%), so the data does not support a peak-working-set improvement claim.
Semantic projection and the other typed source graphs remain the dominant unsolved
process-level cost.

## What remains broken

- The saved-package dependency pipeline now uses one operation-wide lease from ZIP
  admission through every migrated graph and final dependency construction, plus
  operation-scoped byte-exact lossless XML reuse. Selected temporary allocations and
  typed projection duplication remain outside complete accounting.
- The accounting constants are deliberately stable, not runtime-specific heap truth.
  Benchmark measurements must remain beside them.
- Long relationship metadata is already present in the bounded OPC snapshot before the
  dependency layer sees it. The new 65,536-character graph metadata ceiling prevents a
  second huge stable-ID/materialization allocation but does not replace a tighter OPC
  metadata policy.
- The node-ID map remains a hash dictionary. The keys are engine-generated fixed-length
  hashes, but a future compact intern table could reduce it further.

The next memory step is explicit accounting for the remaining temporary allocations,
then a bounded multi-action immutable store whose privacy, invalidation and lifetime
rules are part of the public contract. The current lease and per-operation cache close
the repeated lossless-parse path; they do not make every typed projection cheap.
