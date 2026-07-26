# Isolated LibreOffice backend for WordToolkit

Date: 2026-07-26

## Problem

The document-engine goal requires a LibreOffice backend, version probe and shared visual
corpus. The repository already contains a historical Python helper that finds
`libreoffice` or `soffice` on `PATH`, creates a temporary user profile and calls
`--convert-to pdf:writer_pdf_Export`. That helper is useful compatibility plumbing, but it
is not yet a qualified engine backend.

The old path does not bind execution to an explicit executable hash/version, does not
prove the selected process is LibreOffice before opening a document, assumes the generated
filename, does not bind page output to the source package fingerprint, and does not publish
PDF/PNG/provenance as one verified transaction. Most importantly, command-line conversion
does not expose the document-load `MacroExecutionMode` and `UpdateDocMode` controls that
the UNO media descriptor provides. A fresh profile is isolation evidence; it is not proof
that active content and external updates cannot run.

## Official contract evidence

- LibreOffice documents `--version`, `--headless`, `--nologo`, `--nodefault`,
  `--nolockcheck`, `--norestore`, `--accept` and conversion parameters. It also states
  that LibreOffice requires write access to its user profile directory:
  <https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html>.
- The official conversion-filter table names Writer PDF export
  `writer_pdf_Export`:
  <https://help.libreoffice.org/latest/en-US/text/shared/guide/convertfilters.html>.
- The official PDF command-line contract accepts a typed JSON filter-property string and
  documents `PageRange`:
  <https://help.libreoffice.org/latest/en-GB/text/shared/guide/pdf_params.html>.
- Published UNO `XComponentLoader.loadComponentFromURL` accepts a media descriptor and
  returns a loaded component; the caller must close/dispose it according to the supported
  interfaces:
  <https://api.libreoffice.org/docs/idl/ref/interfacecom_1_1sun_1_1star_1_1frame_1_1XComponentLoader.html>.
- The published UNO `MediaDescriptor` includes `MacroExecutionMode`, `UpdateDocMode`,
  `ReadOnly`, `Hidden`, `PickListEntry` and `RepairPackage`. `ReadOnly` is explicitly a UI
  restriction, not an API immutability guarantee:
  <https://api.libreoffice.org/docs/idl/ref/servicecom_1_1sun_1_1star_1_1document_1_1MediaDescriptor.html>.
- LibreOffice's own security documentation says the very-high macro level disables every
  macro outside trusted file locations. Trusted locations therefore must remain empty in
  an isolated profile:
  <https://help.libreoffice.org/latest/en-US/text/shared/optionen/macrosecurity_sl.html>
  and
  <https://help.libreoffice.org/latest/en-US/text/shared/optionen/macrosecurity_ts.html>.
- Published UNO `XRenderable` exposes renderer count and renderer descriptors, but this
  is a rendering API, not a promise of Word-equivalent pagination or pixels:
  <https://api.libreoffice.org/docs/idl/ref/interfacecom_1_1sun_1_1star_1_1view_1_1XRenderable.html>.

## Target adapter boundary

LibreOffice remains an out-of-process compatibility backend. It never becomes the source
of truth for OOXML declarations, semantic identity or Word fidelity.

### 1. Capability and version probe

The first public operation must:

- require one explicit absolute local executable path; never search `PATH`;
- reject UNC, device-namespace, mapped-network and reparse-point paths;
- hash the exact executable before process start and optionally require an expected hash;
- execute only the fixed `--version` argument with closed stdin, bounded stdout/stderr,
  bounded timeout and process-tree termination;
- require a recognizable LibreOffice product/version result instead of accepting any
  executable that exits zero;
- return product version, executable hash/size, host OS/architecture and the exact
  limitations of the probe, but never the executable path or environment values;
- open no document, create no profile and claim no render capability from version output
  alone.

### 2. One-shot isolated render

The first render operation must use a new private workspace and profile per request. It
must copy the source package into that workspace, retain and recheck the exact package
fingerprint, set a sanitized environment, disable quickstart/recovery/UI and close stdin.
The preferred implementation is an isolated UNO child adapter that loads with explicit
`MacroExecutionMode=NEVER_EXECUTE`, `UpdateDocMode=NO_UPDATE`, `Hidden=true`,
`ReadOnly=true`, `PickListEntry=false` and `RepairPackage=false`, exports through the
typed Writer PDF filter and explicitly closes the document and office process.

Command-line `--convert-to` may remain a lower-assurance fallback only if the response
labels that weaker active-content proof. It must never be silently substituted for the
UNO lane.

### 3. Verified artifact publication

The adapter must stage PDF, optional Poppler-derived PNG pages and one provenance manifest
under no-clobber names, validate every artifact, recheck the source hash after process
exit and publish the whole set transactionally. Provenance must include:

- LibreOffice product version and executable SHA-256;
- integration lane (`uno` or explicitly weaker `command_line`);
- source package fingerprint;
- filter name and closed filter options;
- host OS/architecture and bounded locale/font inventory;
- PDF/page hashes, geometry and selected range;
- macro/link/update policy and whether each was actually enforced or merely assumed;
- process exit, timeout/termination and cleanup proof.

No response calls the result Word-identical. A timeout, profile cleanup failure, source
drift, ambiguous output, unexpected sibling artifact or unverifiable active-content policy
must fail closed and publish nothing.

### 4. Shared visual corpus

The same immutable DOCX fixtures must run through Microsoft Word and LibreOffice under
recorded versions, fonts, locale and page geometry. Measurements must separate:

- package/source preservation;
- page count and MediaBox geometry;
- text extraction and missing-glyph evidence;
- per-page raster dimensions and bounded pixel-difference metrics;
- headings/lists/fields/OMML/tables/floating objects/charts/SmartArt/content controls;
- known unsupported or differently interpreted features.

Word remains the authoritative Windows fixed-layout backend. LibreOffice is a named,
versioned compatibility result. Agreement on one corpus is not general equivalence.

## Implementation order

1. public explicit-path version probe with deterministic tests;
2. isolated one-shot UNO load/export adapter and cleanup proof;
3. transactional PDF/PNG/manifest publication using the existing fixed-artifact spine;
4. Linux CI qualification against the installed LibreOffice package;
5. shared Word-versus-LibreOffice corpus and report;
6. only then evaluate a supervised persistent UNO worker for throughput.

A persistent `--accept` listener is not the first step. It creates a long-lived unauthenticated
control surface and multiplies stale-profile, crash-recovery and cross-request state risks.
Correct isolation and evidence come before throughput.
