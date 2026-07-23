# Operation-wide dependency inspection resource lease — Windows x64 — 2026-07-23

Each `dependency-100k-0.39.0-sample-*.json` file is the unedited output of one
cold Release process. The command was repeated five times:

```text
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release --no-build -- graph --target-nodes 100000 --output <sample.json>
```

The synthetic package produced 99,993 semantic nodes and a dependency graph with
99,997 nodes and 99,996 edges in every sample. The operation-wide
`word_operation_accounted_v1` model deterministically charged 539,282,576 of the
671,088,640-byte default (80.4%); the nested dependency-graph model charged
130,132,744 of 134,217,728 bytes. Stage charges are embedded in every raw sample.

The five-sample medians were:

- dependency build: 2,956.082 ms;
- measured package-read + semantic-projection + dependency-build total: 5,491.3294 ms;
- retained managed-memory delta: 175,334,544 bytes;
- managed allocation delta: 1,610,477,144 bytes;
- process peak working set: 615,305,216 bytes.

Against the checked-in 0.38.0 five-process series on the same generator, host and
runtime, dependency-build time changed by +0.07%, measured total by +1.08%, retained
managed delta by -10.26%, allocations by +0.11%, and peak working set by +6.75%.
Those values are observations, not guarantees. In particular, the accounted-byte
model is a conservative deterministic admission budget, not a heap or peak-working-set
measurement. The series does not support a peak-memory improvement claim.

Environment recorded by each sample: WordToolkit Engine 0.39.0, .NET 8.0.29,
Microsoft Windows 10.0.19045 x64, 12 logical processors, workstation GC.
