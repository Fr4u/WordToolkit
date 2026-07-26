# LibreOffice UNO render backend research — 2026-07-26

## Decision

The portable control plane will use the official Java UNO binding in a disposable
child process. It will not use `soffice --convert-to` as a hidden substitute for UNO,
and it will not call the LibreOffice CLI/.NET binding a cross-platform API.

LibreOffice's SDK installation guide describes Java and C++ support on Unix-like
systems, but marks the CLI compiler configuration as Windows-only. That makes the
CLI binding unsuitable as the one portable bridge. The official Java runtime exposes
`Bootstrap`, `XUnoUrlResolver` and the generated UNO interfaces on every supported
LibreOffice installation. A Java worker can therefore connect to one explicitly
started office over a local named pipe while the .NET parent retains process, timeout,
hash and cleanup control.

Primary sources:

- LibreOffice SDK installation and binding requirements:
  <https://api.libreoffice.org/docs/install.html>
- Java UNO `Bootstrap`:
  <https://api.libreoffice.org/docs/java/ref/com/sun/star/comp/helper/Bootstrap.html>
- `XComponentLoader.loadComponentFromURL`:
  <https://api.libreoffice.org/docs/idl/ref/interfacecom_1_1sun_1_1star_1_1frame_1_1XComponentLoader.html>
- `MediaDescriptor` load properties:
  <https://api.libreoffice.org/docs/idl/ref/servicecom_1_1sun_1_1star_1_1document_1_1MediaDescriptor.html>
- `MacroExecMode.NEVER_EXECUTE`:
  <https://api.libreoffice.org/docs/idl/ref/namespacecom_1_1sun_1_1star_1_1document_1_1MacroExecMode.html>
- `UpdateDocMode.NO_UPDATE`:
  <https://api.libreoffice.org/docs/idl/ref/namespacecom_1_1sun_1_1star_1_1document_1_1UpdateDocMode.html>
- `XStorable.storeToURL` export semantics:
  <https://api.libreoffice.org/docs/idl/ref/interfacecom_1_1sun_1_1star_1_1frame_1_1XStorable.html>
- `XCloseable.close` ownership and veto semantics:
  <https://api.libreoffice.org/docs/idl/ref/interfacecom_1_1sun_1_1star_1_1util_1_1XCloseable.html>
- Writer input and PDF export filter names:
  <https://help.libreoffice.org/latest/en-US/text/shared/guide/convertfilters.html>
- typed PDF filter data including `PageRange` and PDF/A selection:
  <https://help.libreoffice.org/latest/en-GB/text/shared/guide/pdf_params.html>

## One-shot execution design

The parent starts one exact, SHA-256-bound LibreOffice entry point with:

- a fresh private `UserInstallation` directory;
- `--headless`, `--invisible`, `--nologo`, `--nodefault`, `--nolockcheck`,
  `--norestore` and `--nofirststartwizard`;
- one random local UNO pipe;
- no TCP listener and no inherited document path in process arguments.

It then starts one exact, SHA-256-bound Java executable. The Java classpath contains
only the reviewed WordToolkit helper JAR and the exact LibreOffice installation's
`libreoffice.jar`. Document and output URLs cross a versioned bounded binary protocol
on standard input, not the process command line. Standard output is a fixed binary
result record; both stderr streams are drained but never returned raw.

The worker supplies these explicit load properties:

- `Hidden=true`;
- `ReadOnly=true`;
- `PickListEntry=false`;
- `RepairPackage=false`;
- `MacroExecutionMode=NEVER_EXECUTE`;
- `UpdateDocMode=NO_UPDATE`;
- an extension-matched Writer input filter.

After load it requires a Writer `TextDocument`, verifies `XStorable.isReadonly()`,
records the original source location, exports through
`XStorable.storeToURL(..., FilterName=writer_pdf_Export, ...)`, and proves that the
source location did not change. It closes the document with
`XCloseable.close(false)` and requires `XDesktop2.terminate()` to succeed. The parent
requires a zero helper exit, a zero LibreOffice exit, a valid bounded PDF, stable
pre/post hashes, deletion of the private profile and deletion of the complete private
workspace before it accepts the observation.

## Security and evidence boundaries

This is process isolation, not a sandbox. The current design does not provide OS-level
network isolation, syscall confinement, a complete hash closure over every dynamically
loaded LibreOffice module, vendor-signature proof or an atomic binding between the
hashed path and the bytes mapped by the operating system.

`MacroExecutionMode=NEVER_EXECUTE` and `UpdateDocMode=NO_UPDATE` are explicit official
load requests. Until adversarial fixtures prove that a macro and an external update do
not produce observable effects, the public evidence must say “requested”, not
“behaviorally verified”. `ReadOnly=true` is additionally read back through
`XStorable.isReadonly()`, but LibreOffice itself documents that this is a logical/UI
read-only state and does not prevent later API mutation.

LibreOffice pagination is LibreOffice pagination. No successful export is evidence of
pixel equivalence, pagination equivalence or object-model equivalence with Microsoft
Word. Microsoft Word remains the authoritative fixed-layout backend on qualified
Windows hosts.

## Current qualification state

The Java helper compiles locally against LibreOffice 26.2.4.2 and creates a valid PDF
through the complete UNO load/export/close/terminate path. On the tested Windows host,
Oracle JDK 21.0.6 then terminates with native access violation `0xC0000005` in
`msvcp140.dll`, even after the helper emitted a complete success record and the private
LibreOffice process exited cleanly. This is a backend failure, not a successful render.
The provider requires a zero helper exit and therefore rejects it.

Ubuntu 24.04 qualification with exact LibreOffice, Temurin JDK 17, `libreoffice.jar`
and helper hashes is wired into hosted CI. This document must be updated with the actual
run URL, versions and hashes only after that lane passes. No public MCP render action is
permitted before the provider is qualified and the higher-level PDF/PNG/manifest
publication transaction is complete.
