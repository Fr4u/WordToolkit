# Document-engine benchmarks — Windows x64 — 2026-07-23

## Figure/caption graph

Environment and exact samples are embedded in `figures-10k.json`. The command was:

```text
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- figures --target-nodes 10000 --output docs/benchmarks/2026-07-23-windows-x64/figures-10k.json
```

The synthetic DOCX contains 10,000 inline picture declarations, 10,000 distinct
relationship IDs targeting one inert internal resource, and 10,000 neighboring `SEQ
Figure` fields. This forces relationship lookup scale instead of hiding it behind one
shared ID. The benchmark performs
package read, semantic projection, reference graph construction and style graph
construction once, then builds the figure/caption graph seven times over the immutable
inputs. It reports the median and p95 of those seven graph builds.

Before indexing, the 10,000-object run did not complete inside the bounded interactive
measurement window and emitted no result artifact. Inspection found three quadratic
full scans: captions per figure, associations per ambiguity diagnostic and fields per
paragraph. Because the baseline run was deliberately terminated, there is no honest
baseline median to quote.

After indexing, the current 0.37.0 command completed in 23.3 seconds wall time. The
figure graph median was 1,897.6 ms and p95 2,084.6 ms. It produced 10,000 logical figures, 10,000 captions,
10,000 resources and 19,999 candidates, with 10,000 mutually unique selected
associations and no diagnostic. Setup took 7,370.7 ms. Retained managed memory from the
pre-read baseline through the final graph was 317,194,760 bytes; peak process working
set was 1,251,549,184 bytes. The benchmark uses workstation GC on a 12-logical-processor
Windows 10.0.19045 host running .NET 8.0.29.

The package is highly compressible and structurally repetitive. This is a deterministic
scale boundary, not a throughput or representative-memory promise for arbitrary DOCX
files.

## Dependency-graph byte budget and compact adjacency

The `dependency-100k-0.37.0-before.json` and
`dependency-100k-0.38.0-after.json` series each contain five cold-process samples using
the same generator, host, .NET 8.0.29 runtime and 99,997-node/99,996-edge result. The
command below was repeated five times per version:

```text
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- graph --target-nodes 100000
```

The 0.38.0 graph accounts 130,132,744 of its default 134,217,728-byte budget; its two
compressed-row adjacency indexes occupy exactly 1,599,952 bytes. Relative to 0.37.0,
median retained managed memory fell from 234,933,120 to 195,381,520 bytes (16.8%),
median managed allocations from 1,678,719,936 to 1,608,727,168 bytes (4.2%), median
dependency build time from 3,244.6 to 2,954.1 ms (9.0%) and median total measured time
from 5,742.6 to 5,432.6 ms (5.4%). Median peak working set moved from 575,979,520 to
576,376,832 bytes (+0.1%), so the series does not support a peak-memory improvement
claim.

This controlled series measures the implementation change, not an absolute heap guarantee.
The byte model covers dependency-graph construction and retention; upstream semantic
and typed graph projections retain their independent limits and costs.
