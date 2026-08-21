# Document-engine scale baseline — Windows x64 — 2026-07-22

This is a measured baseline, not a throughput promise. Results came from one process per
point on Windows 10.0.19045, .NET 8 workstation GC, an Intel Core i5-10400F (6
cores/12 logical processors) and 64 GiB physical RAM. The dependency/patch points used
.NET 8.0.26; the later MCE, content-control binding and table points used .NET 8.0.29. The exact JSON reports in this
directory contain timings, retained managed memory, total allocations and process peak
working set.

## Content-control and Custom XML binding graph

The synthetic DOCX contains one physical Custom XML store, one distinct positional
XPath per plain-text content control and one resolved element per binding. The measured
time is package read + semantic projection + binding-graph construction. It excludes
Word and dependency-graph materialization.

| Controls/bindings/targets | Measured time | Binding build | Retained managed delta | Peak working set | Managed allocations |
|---:|---:|---:|---:|---:|---:|
| 10,000 | 2.30 s | 0.79 s | 123.1 MiB | 201.9 MiB | 567.4 MiB |
| 100,000 | 15.40 s | 5.23 s | 1,274.4 MiB | 1,761.8 MiB | 5,960.7 MiB |

All bindings resolved with zero diagnostics. The 100,000 point reached the exact
control and binding ceilings, but required a benchmark-only metadata-character budget
of 64 MiB; production remains at 16 MiB and rejects this deliberately metadata-dense
fixture. The first implementation also exposed quadratic positional XPath traversal.
The measured version indexes each parent's children by QName once and selects `[n]` in
constant time. Even after that correction, allocation remains severe and requires
compact source/identity storage before this path can be called light.

## Table graph

The synthetic DOCX contains one fixed-layout 20-column table. The first column forms
five-row vertical merge chains, the first row is a repeating header, and no cell text is
needed. The measured time is package read + semantic projection + table-graph
construction. It excludes dependency materialization, Word and rendering.

| Physical cells | Rows | Vertical merges | Measured time | Table build | Retained managed delta | Peak working set | Managed allocations |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 10,000 | 500 | 100 | 0.89 s | 0.25 s | 37.8 MiB | 110.1 MiB | 190.6 MiB |
| 100,000 | 5,000 | 1,000 | 5.23 s | 1.30 s | 440.2 MiB | 579.3 MiB | 1,885.1 MiB |

Both points produced the exact requested cell count and zero diagnostics. The larger
point remains well below the five-million-cell rejection ceiling, yet already allocates
roughly 1.84 GiB. The ceiling is therefore a fail-closed bound, not a claim of cheap
operation. Sharing a parsed story representation between semantic projection and typed
table construction is the next clear allocation target.

## Dependency graph

The synthetic DOCX has one main story containing plain one-run paragraphs. The measured
time is package read + semantic projection + dependency-graph construction. It does not
represent complex style, field, relationship or review density.

| Actual dependency nodes | Edges | Measured time | Peak working set | Managed allocations |
|---:|---:|---:|---:|---:|
| 9,997 | 9,996 | 0.66 s | 81.5 MiB | 141.6 MiB |
| 99,997 | 99,996 | 4.92 s | 571.9 MiB | 1,403.0 MiB |
| 499,999 | 499,998 | 20.64 s | 2,149.9 MiB | 6,936.2 MiB |
| 998,998 | 998,997 | 40.08 s | 5,209.2 MiB | 13,891.5 MiB |

The million-node ceiling is reachable on this 64 GiB machine, but the cost is severe.
The current dependency build also constructs the typed table graph; even a table-free
fixture therefore pays for a second safe parse of each projected story. The largest
point now peaks near 5.09 GiB and allocates about 13.57 GiB. It must not be advertised as
an ordinary safe workload. The next engineering target is a shared immutable parsed
story plus lower-allocation node/source and adjacency storage, followed by the same
fixtures with dense cross-domain edges.

## `.wtpatch` materialization

Each input package has the stated unique changed payload size. Because version 1 stores
both before and after bytes, the patch payload is twice that input size. Measurements
retain both package snapshots, the created patch, the ZIP artifact and the decoded patch
to expose the current worst in-process materialization path.

| Changed input per package | Patch payload | Measured time | Peak working set | Managed allocations |
|---:|---:|---:|---:|---:|
| 16 MiB | 32 MiB | 1.34 s | 198.0 MiB | 179.1 MiB |
| 64 MiB | 128 MiB | 5.24 s | 786.5 MiB | 879.1 MiB |
| 128 MiB | 256 MiB | 10.58 s | 1,326.2 MiB | 1,285.9 MiB |
| 200 MiB | 400 MiB | 15.61 s | 2,158.1 MiB | 2,234.4 MiB |

These results caused the aggregate default to be reduced from 512 MiB to 128 MiB (with
64 MiB per blob and a 100:1 ratio). The 256/400 MiB points deliberately record the old
explicit high-limit path. Even the reduced ceiling is a rejection bound, not a promise
of cheap processing, until streaming payload storage replaces the byte-array model.

## Markup Compatibility graph

The synthetic DOCX uses one main XML story with inherited `mc:Ignorable`,
`mc:ProcessContent` and `mc:MustUnderstand` rules, periodic ignored and unwrapped
elements, and one `mc:AlternateContent` block. The requested value is a hard XML-element
ceiling; the generator chooses the largest paragraph count that remains below it. The
measured time is package read plus source-preserving MCE graph construction. It excludes
Word, rendering and any compatibility transform.

| Requested ceiling | Actual XML elements | MCE-affected elements | Measured time | Retained managed delta | Peak working set | Managed allocations |
|---:|---:|---:|---:|---:|---:|---:|
| 100,000 | 99,999 | 1,955 | 0.65 s | 112.4 MiB | 152.3 MiB | 181.0 MiB |
| 500,000 | 499,999 | 9,773 | 2.75 s | 493.2 MiB | 552.7 MiB | 882.8 MiB |
| 999,000 | 998,998 | 19,526 | 4.78 s | 981.6 MiB | 1,108.0 MiB | 1,763.1 MiB |

The graph reaches the configured million-element boundary on this machine, but it is
not cheap: the largest point retains roughly 1.03 GB of managed memory. This is a
resource-bound proof, not a default throughput promise. Compact source references and
affected-element storage remain necessary before calling this path light.

## Reproduction

```powershell
dotnet build native/WordToolkit.Engine.Benchmarks/WordToolkit.Engine.Benchmarks.csproj -c Release
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- graph --target-nodes 100000
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- bindings --target-nodes 100000
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- tables --target-nodes 100000
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- patch --payload-mib 64 --parts 64
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- mce --target-nodes 100000
```

The manual `document-engine-benchmarks` workflow runs all recorded scale points with the
`scale` profile. Its artifacts allow comparison across hosts without overwriting this
baseline.
