# Figure/caption benchmark — Windows x64 — 2026-07-23

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
