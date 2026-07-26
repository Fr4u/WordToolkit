# Operation-scoped lossless XML parse reuse

Date: 2026-07-26

## Problem

The saved-package engine deliberately has many typed projections. Before this change,
those builders repeatedly parsed the same Word story bytes into separate
`LosslessXmlDocument` objects. Passing one cumulative operation budget through the
pipeline made that duplication visible, but did not remove it. A large analysis paid for
the same immutable source copy, XML audit, `XDocument`, lexical scan and source-span map
many times.

A global document cache would be the wrong cure. It would retain private document
content across requests, complicate invalidation, weaken operation isolation and make
resource ownership dishonest. Reuse must die with the operation that created it.

## Implemented boundary

The cache is keyed by one `WordOperationResourceLease` through a
`ConditionalWeakTable`. There is no process-global content index and no cross-operation
reuse. A successful miss stores one immutable `LosslessXmlDocument`; later callers in
that lease share its exact source/parsed/span storage only after byte-exact identity is
proved. When parser options differ, the caller receives a lightweight view carrying its
own limits over the same immutable parsed core, so later patch validation cannot inherit
the first caller's looser limits.

Two lookup paths are used:

1. An array-backed `ReadOnlyMemory<byte>` first probes backing-array reference, offset
   and count. A full byte comparison is still mandatory because a public caller can wrap
   a mutable array. If the bytes changed, the identity entry is discarded.
2. A different array with the same content uses source length plus SHA-256 to find a
   candidate, followed by a full byte comparison. Hash equality alone never authorizes
   reuse.

The per-lease lock serializes lookup and creation, so concurrent callers cannot produce
duplicate parses for one byte-exact source. The parse object owns its source copy; callers
cannot mutate the retained bytes.

## Limit preservation

Parser options are intentionally not part of the cache identity. Instead the retained
document records source bytes, decoded characters, element count, maximum depth and text
characters. Every hit rechecks those statistics against the current caller's limits.
This allows a stricter-but-satisfied caller to reuse the parse while a stricter failing
caller receives the same `LosslessXmlLimitException` it would receive on a cold parse.
Rejected reuse does not count as a successful cache request or hit.

## Pipeline and accounting

High-level document analysis now shares the cache through package admission, semantic
projection, styles, numbering, references, sections, charts, figures, content controls,
tables, bibliography, active content, settings, properties, diagrams, outline, theme,
font table, markup compatibility, lint, list-sequence execution and dependency analysis
where those builders use lossless XML.

Theme, font, MCE, lint and list-sequence builders now accept the same operation lease.
Their XML parsing and conservative bounded result-collection charges enter distinct
resource stages. Selected transient list-sequence and lint allocations remain outside
the model, so `operation_budget_coverage_complete` remains false.

`operation_budget.xml_parse_cache` reports:

- `model = word_operation_xml_parse_cache_v1`;
- `requests` for successful cache-mediated parses;
- `unique_parses` retained by this operation;
- `cache_hits` returning an already retained parse;
- `avoided_accounted_bytes`, the conservative XML parse charge that was not repeated.

The invariant is `requests = unique_parses + cache_hits`. Avoided bytes are not measured
CLR allocations, working set or released budget. They make the accounting consequence of
reuse explicit without pretending to be a profiler.

## Qualification

Correctness regressions prove byte-exact reuse across separate arrays, same-array fast
reuse, rejection after source-array mutation, stricter parse and patch-limit enforcement,
isolation between leases, deterministic analysis statistics and shared list-sequence
story reuse.

The checked-in benchmark record contains fifteen alternating cold-process runs of the
installed 0.51.0 self-contained Release runtime and the candidate self-contained Release
runtime. On the 5,310-byte equation fixture, median latency moved from 465.041 ms to
451.977 ms (-2.81%) and accounted budget from 2,065,992 to 1,594,336 bytes (-22.83%).
The candidate used 59 cache requests, 11 unique parses and 48 hits.

On the 52,292-byte mixed-domain torture fixture, median latency moved from 928.300 ms to
706.852 ms (-23.86%) and accounted budget from 23,765,680 to 15,397,168 bytes (-35.21%).
The candidate used 312 requests, 40 unique parses and 272 hits. One small-fixture
candidate run was a 1,070.362 ms outlier; the report retains min/max and does not hide it.

These figures establish benefit for two exact fixtures on one Windows host. They do not
prove universal latency improvement, peak-memory reduction or behavior on every package
shape.

## Remaining hard work

This is operation-scoped lossless parse reuse, not a universal immutable document store.
Typed graphs still materialize independent projections. Separate MCP actions still reopen
and reparse the package. Complete operation-wide temporary allocation accounting,
incremental invalidation, content-addressed multi-action storage with explicit privacy and
lifetime policy, and a representative multi-size/multi-domain benchmark corpus remain.
