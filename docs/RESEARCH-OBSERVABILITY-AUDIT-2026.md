# Observability and audit research — 2026

## Scope

This note records the primary-source evidence used for the first native WordToolkit
observability and audit slice. The target is not a general-purpose content logger. It is
a privacy-minimizing operational trace that can explain which fixed engine operation ran,
whether it succeeded, was rejected, was cancelled or failed, and whether audit delivery
itself remained healthy.

## Primary sources

### .NET instrumentation boundary

Microsoft documents `System.Diagnostics.ActivitySource`/`Activity` as the native .NET
library API for distributed tracing. Its guidance is to create a uniquely named,
versioned source once and let the application choose a collector. This avoids binding the
Engine assembly to one OpenTelemetry exporter or vendor:

- <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>
- <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/>

Microsoft likewise documents `System.Diagnostics.Metrics.Meter` as the native metric
production API and `MeterListener`/OpenTelemetry as collection choices. Metric tags are
dimensions, so WordToolkit admits only two finite dimensions: registered operation name
and normalized outcome. Paths, document IDs, trace IDs, error messages and input values
are not metric tags:

- <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-collection>
- <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-strongly-typed>

### Correlation and privacy

W3C Trace Context standardizes a 16-byte random trace identifier for correlation and
explicitly says trace fields must not carry personally identifiable or other sensitive
information. WordToolkit uses an `Activity` trace ID when a host collector is active and
otherwise generates a random 16-byte correlation value. It never derives correlation
from a path, document, user, machine or payload:

- <https://www.w3.org/TR/trace-context/>

### Audit content, failure and retention

OWASP separates security/audit trails from ordinary operational telemetry, recommends a
consistent application-wide handler, excludes or specially treats source code, tokens,
PII, connection strings, keys and file paths, requires log-injection defenses, says
logging failure must not stop the application, calls for fault tests and tamper detection,
and requires retention to end when its purpose ends:

- <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>

NIST SP 800-92 frames log management as generation, transmission, storage, access,
analysis and disposal under an explicit organizational policy. WordToolkit therefore
ships bounded technical controls but does not invent a legally correct retention period
for every deployment:

- <https://csrc.nist.gov/pubs/sp/800/92/final>

## Implemented decisions

1. `ActivitySource` and `Meter` are opt-in producers. The Engine has no exporter package
   dependency and performs no network export.
2. Audit recording is independently opt-in through `off`, `memory` or `jsonl` mode.
3. The event schema is closed and contains only a registered operation name/version,
   fixed effect flags, normalized outcome/error code, random correlation, timestamp,
   duration, sequence and chain hashes. Arguments, paths, document IDs, text, XML,
   comments, authors, relationship targets, package fingerprints and binaries do not have
   fields in the contract.
4. Operation/error/sink dimensions are syntax-validated. An unknown or hostile tool name
   becomes the fixed `wordtoolkit_unknown_action` value; it is never echoed into audit.
5. Sink I/O runs behind a bounded non-blocking channel. A slow or throwing sink cannot
   replace or hold the document operation. Queue drops and sink failures are separate
   counters visible through the content-free inspector.
6. Host telemetry listeners are untrusted runtime plumbing. Exceptions raised while an
   activity or metric is started, recorded or stopped are contained and counted; they do
   not replace the operation outcome.
7. The in-memory ring has explicit capacity and time retention. The JSON Lines sink has
   bounded per-file size, daily filenames, retention pruning, UTF-8, one closed event per
   line, write-through flush and no path in its public metadata.
8. Each event hashes the previous record plus a canonical projection of every event field.
   This detects record mutation, deletion or reordering within the observed chain. It is
   deliberately reported as `authenticated=false`: an unkeyed local SHA-256 chain is not
   a signature, trusted timestamp or non-repudiation proof.
9. `inspect_wordtoolkit_observability` is summary-first and pages at most 32 safe events.
   Correlation IDs and per-record hashes require independent opt-ins. It opens no Word
   instance and reads no document or audit-file path.
10. `wordtoolkit-native audit-log verify` checks one bounded JSONL segment with a strict
   parser, duplicate/unknown-field rejection, event-count/byte/line limits, sequence
   continuity and chain recomputation. Its response omits the input path and event bodies.

## Explicit limits

- The JSONL chain detects inconsistency; it does not authenticate the writer or prevent a
  privileged attacker from replacing a complete log. Keyed signing, external anchoring,
  trusted timestamps and read-access auditing remain future security-policy work.
- The first sink is local JSONL only. Remote exporters remain forbidden until explicit
  network, credential, redaction, retry and destination-trust policies exist.
- Audit delivery is best effort. Queue saturation and sink failure are observable, but a
  post-mutation audit failure does not roll back a valid document mutation. A compliance
  mode that requires durable pre/post evidence must integrate with transaction commit and
  recovery rather than pretending an after-the-fact file write is atomic with Word.
- File retention is technical deletion by age. Legal hold, organization-specific policy,
  secure deletion, access-control provisioning and centralized archival are not claimed.
- CLI verification covers one segment. Cross-process/cross-segment anchoring and a signed
  manifest remain open.
