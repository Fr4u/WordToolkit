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

Downstream packages may expose `program/classes/libreoffice.jar` as a symlink to a
distribution-owned archive under `/usr/share/java`. WordToolkit requires the caller to
resolve that link first and binds the resolved regular file by SHA-256; it does not
accept the symlink itself. The runtime render proves that the selected JAR can control
the selected office build, but it is not package-manager or vendor-signature proof that
both files came from the same release.

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

Ubuntu 24.04 qualification passed in hosted CI:
<https://github.com/Fr4u/WordToolkit/actions/runs/30200389656>. The real provider test
completed the full render in four seconds and all 12 adapter tests passed. The qualified
inputs were:

- LibreOffice `24.2.7.2 420(Build:2)`, executable SHA-256
  `eef555c71025262c67274dc6e98d00168c2a2ce0fcd16473c38609ff3ce2ace9`;
- Temurin OpenJDK `17.0.16`, executable SHA-256
  `1c7e3313ab05bef3da61d7659a12cc50622ddc41b6db2b3a88d480445bf9619f`;
- resolved LibreOffice Java archive SHA-256
  `8e8ca596c9bd1333bd35a850ba7991a29107e6cc32ca4e961a597971864d5840`;
- WordToolkit UNO helper JAR SHA-256
  `583ef85be3e0e9282cd1aec06161767606d1c5b9ce91228587fa8f14e57ad462`.

This qualifies the provider on that exact Linux evidence. The reviewed helper JAR is now
embedded in `WordToolkit.LibreOffice.dll`; callers cannot replace it with an arbitrary
classpath entry. CI rebuilds the JAR from the committed Java source with the qualified
JDK 17 toolchain and rejects any byte-level difference from the embedded artifact before
running the real provider test. The provider extracts it only inside the disposable
private workspace, verifies SHA-256
`583ef85be3e0e9282cd1aec06161767606d1c5b9ce91228587fa8f14e57ad462`
before execution, rechecks it afterward and deletes it with the private profile.

This does not qualify the Windows/JDK 21 combination. The higher-level
`wordtoolkit.render_ooxml_libreoffice_artifacts/1.0` action and matching strict CLI now
wrap this provider with OPC/fingerprint validation, independent source-drift checks,
optional Poppler page rasterization, private-staging deletion before publication and a
create-new PDF/PNG/manifest transaction. Fake-backend, rollback and closed-schema tests
pass locally. The public action is not called Linux-qualified until the real hosted lane
runs that exact action rather than only this lower-level provider.
