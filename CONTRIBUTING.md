# Contributing

WordToolkit changes must be narrow, reproducible and honest about what was verified. Do not turn a package-level result into a claim about Microsoft Word behavior.

## Before opening a change

1. Read [the documentation index](docs/README.md), [testing guide](docs/TESTING.md), [security policy](docs/SECURITY.md) and [known limitations](docs/KNOWN-LIMITATIONS.md).
2. State the exact scope, affected boundary and files changed.
3. Keep generated artifacts, local outputs, credentials and unrelated worktree changes out of the change.

## Required pull-request evidence

Every pull request must include:

- a short problem statement and the smallest proposed change;
- tests or a precise reason a test cannot be added;
- exact commands run and their results;
- whether validation covered saved-package structure, the native engine, MCP contracts, LibreOffice, or a real Microsoft Word build;
- for live Word claims: Word version/build, document format, operation, read-back or rendered evidence, and cleanup result;
- known gaps, skipped checks and anything that remains unverified.

Do not claim Word compatibility, visual equivalence, rollback success, signature validity or release readiness without evidence for that claim. A CI pass is not a substitute for a licensed Word acceptance run.

## Scope and safety

Changes must preserve fingerprint/precondition checks, bounded responses, failure-closed behavior and the existing plan/apply or transaction boundary. Do not add raw XML or binary payloads to model-facing responses, execute macros or external fields, follow external targets, or bypass validation to make a test green.

Before handing off, run `git diff --check`, inspect the final diff, and confirm that only intended files changed. Do not commit, stage or push from a review workspace unless explicitly asked.
