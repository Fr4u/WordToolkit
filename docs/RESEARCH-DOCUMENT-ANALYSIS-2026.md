# High-level Word document analysis for AI planning

Date: 2026-07-26

## Problem

WordToolkit already had narrow saved-package inspectors for OPC, semantics, styles,
numbering, references, tables, charts, figures, content controls, active content,
dependencies, markup compatibility and lint. That surface was powerful but expensive for
an AI client to navigate blindly. A broad request such as "analyse this document" could
cause a wasteful fan-out, duplicate package parsing and a large collection of detailed
responses before the client knew which domain mattered.

The opposite shortcut—returning a confident one-line health score—would be worse. Word
documents contain opaque extension markup, active-content binaries, application-specific
layout behavior and domains that the current engine does not model. A single score would
erase those boundaries and turn absence of evidence into a false clean bill of health.

## Implemented contract

`wordtoolkit.analyze_ooxml_document/1.1` is one read-only Engine operation exposed by the
strict `analyze-package` JSON CLI and lazy `analyze_ooxml_document` MCP action. Its request
contains only:

- one local DOCX, DOCM, DOTX or DOTM path;
- an optional exact package fingerprint for stale-input rejection;
- `max_signals`, bounded from 1 to 32 and defaulting to 12.

The result joins high-level evidence from one package pass:

- OPC entry, part, relationship, reachability, diagnostic and structural-validity counts;
- source-linked semantic object counts without text;
- dependency node, edge, unresolved, external and diagnostic-domain counts;
- lint severity/category counts and grouped repair opportunities;
- typed active-content and external-relationship presence;
- content-free mail-merge configuration, mapping, recipient, field, issue and external/
  sensitive-source counts without query, connection, table, column, identity or target
  values;
- markup-compatibility counts under an explicitly empty application capability profile;
- deterministic prioritized signals naming the exact narrow action to call next;
- separate execution, document-coverage, semantic-completeness and resource-accounting
  boundaries.

The operation never opens Word, starts a renderer, follows an external relationship,
opens a mail-merge data source, runs a saved query or merge, decodes a binary payload,
opens an embedded package, executes active content or mutates the source.

## Pipeline and identity

The implementation opens the package read-only, constructs the bounded OPC snapshot and
checks the optional fingerprint before projection. It then builds the semantic, style,
numbering, reference, section, theme, settings, font, chart, content-control, table and
dependency models consumed by the native linter and markup-compatibility evaluator. The
result retains the package fingerprint so a later narrow inspect or plan can reject drift.

The operation deliberately reuses production builders rather than inventing a second
"fast" parser. This keeps broad analysis aligned with the evidence that narrow actions
will later inspect. Those builders now share byte-exact immutable lossless XML documents
inside this one operation lease. The cache is weak-keyed by the lease, rechecks every
caller's XML limits and exposes compact request/hit/avoided-charge statistics. It is not
a multi-action cache; a later inspector still reopens and reparses the package.

## Signal semantics

Signals are evidence routes, not repair commands. They are ordered by severity and stable
code. Each carries an evidence count, domain, exact `next_action` and an explicit flag
stating whether automatic mutation is blocked.

Critical structural evidence is emitted only when the OPC package is structurally invalid
or lint produced a fatal finding. Ordinary lint errors use the separate
`LINT_ERROR_FINDINGS` signal. This distinction matters: calling every lint error package
corruption would poison planning and hide the difference between a valid package with bad
content structure and a broken package container.

Active content, unresolved dependencies, external relationships, sensitive/external
mail-merge source evidence and unsupported markup can block automatic mutation even when
the package is parseable. `MAIL_MERGE_EVIDENCE` routes to the narrow privacy-redacted
`inspect_ooxml_mail_merge` action. An implemented repair
candidate is only a route to a reviewed plan action; it is never permission to edit.

## Privacy and token discipline

The public result has no field for document text, raw XML, XML byte spans, source paths,
relationship targets, mail-merge queries/connections/source names/recipient identities,
binary payloads or active-content code. The absolute input path is
not echoed; only the leaf file name is returned. Disclosure booleans make those absences
machine-checkable.

The default response is summary-first and bounded to twelve signals. A closed-schema MCP
test requires the compact response for a representative document to remain below 7,500
characters and proves that a secret body string never appears. The purpose is not to
compress every narrow result into one huge result. It is to pay once for a small routing
decision, then open only the domain needed for the next decision.

## Honest coverage boundary

`analysis_execution_complete` can be true while `document_coverage_complete`,
`semantic_completeness_claimed` and `operation_budget_coverage_complete` remain false.
The first release explicitly lists these unmodeled areas:

- active-content binary internals and execution;
- cryptographic signature validation and resigning;
- encrypted-package adaptation;
- coauthoring sessions;
- rendered DrawingML/VML geometry and font metrics;
- the full target-application capability profile for markup compatibility.

Theme, font, markup-compatibility, lint and list-sequence XML parsing plus bounded result
collections now charge the shared operation lease. Selected transient list-sequence and
lint allocations are not fully modeled, so the operation reports incomplete budget
coverage instead of a false resource guarantee.

## Verification

The regression suite proves deterministic repeated analysis, stream-position restoration,
stale-fingerprint rejection, unknown-field rejection, active-content classification,
content non-disclosure, bounded signal paging, strict CLI behavior, closed MCP output
schema conformance, absence of COM invocation and the compact-response ceiling. A real
equation-heavy DOCX with four lint errors remains structurally valid and now emits
`LINT_ERROR_FINDINGS`, not the false critical structural signal.

Cache regressions additionally prove separate-array byte-exact reuse, same-array fast
reuse, rejection after source mutation, stricter-limit enforcement, lease isolation,
deterministic statistics and shared list-sequence parsing. Fifteen alternating
self-contained Release runs are recorded in
`benchmarks/2026-07-26-operation-scoped-xml-cache.json`: the small fixture improved 2.81%
in median latency and 22.83% in accounted budget; the mixed-domain fixture improved
23.86% and 35.21%. These two fixtures are qualification evidence, not a universal
performance claim.

## Remaining work

This slice does not finish "document analysis" in the absolute sense. The next hard work
is complete operation-wide temporary allocation accounting, an explicitly bounded and
privacy-governed multi-action immutable store, a representative multi-size/multi-domain
token/latency corpus, a qualified Word layout evidence join,
signature/encryption/coauthoring adapters and a policy-aware planner that can turn
reviewed signals into a dependency-ordered transaction without inventing evidence.
