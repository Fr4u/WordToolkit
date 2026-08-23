# Documentation index

This directory documents the current contracts and their limits. The repository is on a development line; documents marked as research, audit or stage evidence are records of bounded work, not a promise that the whole engine is complete.

## Start here

- [Architecture](ARCHITECTURE.md) — components, storage and validation boundaries.
- [Testing](TESTING.md) — local commands and evidence requirements.
- [Security](SECURITY.md) — active content, external links and trust decisions.
- [Known limitations](KNOWN-LIMITATIONS.md) — behavior that is missing or intentionally bounded.
- [AI interoperability](AI-INTEROPERABILITY.md) — model-facing response and safety constraints.
- [Word live integration](WORD-LIVE.md) — connected Word operations and their limits.
- [Tool catalog](TOOL-CATALOG.md) — exposed operation inventory.

## Contracts and feature areas

- [Document engine goal audit](DOCUMENT-ENGINE-GOAL-AUDIT.md)
- [Document engine architecture](DOCUMENT-ENGINE-ARCHITECTURE.md)
- [MCP recovery](MCP-RECOVERY.md)
- [Native migration](NATIVE-MIGRATION.md)
- [Semantic golden corpus](SEMANTIC-GOLDEN-CORPUS.md)
- [Active content graph](ACTIVE-CONTENT-GRAPH.md)
- [OfficeMath repair research](RESEARCH-OFFICEMATH-REPAIR-2026.md)
- [Word render execution research](RESEARCH-WORD-RENDER-EXECUTION-2026.md)
- [Guarded live SmartArt creation research](RESEARCH-SMARTART-CREATION-2026.md)

## How to read validation claims

Package inspection and Open XML validation establish structural evidence only. Engine and Python tests establish behavior covered by those tests. LibreOffice output is a separate backend result. Microsoft Word layout, field refresh, equation normalization, COM behavior and version-specific compatibility require a real Word run on a declared build. If that evidence is absent, the claim is unverified.

The current development line is not a completion certificate for the broader document-engine objective.
# Release qualification

- [WordToolkit 0.60.1 qualification](RELEASE-QUALIFICATION-0.60.1.md)
