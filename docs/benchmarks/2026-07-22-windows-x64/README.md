# Document-engine scale baseline — Windows x64 — 2026-07-22

This is a measured baseline, not a throughput promise. Results came from one process per
point on Windows 10.0.19045, .NET 8.0.26 workstation GC, an Intel Core i5-10400F (6
cores/12 logical processors) and 64 GiB physical RAM. The exact JSON reports in this
directory contain timings, retained managed memory, total allocations and process peak
working set.

## Dependency graph

The synthetic DOCX has one main story containing plain one-run paragraphs. The measured
time is package read + semantic projection + dependency-graph construction. It does not
represent complex style, field, relationship or review density.

| Actual dependency nodes | Edges | Measured time | Peak working set | Managed allocations |
|---:|---:|---:|---:|---:|
| 9,997 | 9,996 | 0.60 s | 79.0 MiB | 124.0 MiB |
| 99,997 | 99,996 | 4.91 s | 449.9 MiB | 1,229.1 MiB |
| 499,999 | 499,998 | 19.21 s | 2,119.2 MiB | 6,086.3 MiB |
| 998,998 | 998,997 | 38.56 s | 4,173.1 MiB | 12,177.6 MiB |

The million-node ceiling is reachable on this 64 GiB machine, but the cost is severe.
It must not be advertised as an ordinary safe workload. The next engineering target is
lower-allocation node/source storage and adjacency construction, followed by the same
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

## Reproduction

```powershell
dotnet build native/WordToolkit.Engine.Benchmarks/WordToolkit.Engine.Benchmarks.csproj -c Release
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- graph --target-nodes 100000
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- patch --payload-mib 64 --parts 64
```

The manual `document-engine-benchmarks` workflow runs all recorded scale points with the
`scale` profile. Its artifacts allow comparison across hosts without overwriting this
baseline.
