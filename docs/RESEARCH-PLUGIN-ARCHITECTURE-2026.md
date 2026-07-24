# Plugin architecture research and implementation evidence

Date: 2026-07-24

## Verdict

A directory of DLL files is not a plugin architecture. It is an uncontrolled code-loading
surface wearing a cleaner name. `AssemblyLoadContext` can separate dependency resolution,
but it cannot protect the WordToolkit process, documents, credentials, filesystem or live
Word instance from code already executing inside that process. The first implementation
therefore permits only trusted, explicitly registered in-process modules. It does not scan
directories, call `Assembly.LoadFrom`, or advertise dependency isolation as a sandbox.

The missing untrusted-provider boundary remains a separate process with a closed IPC
contract and operating-system enforcement. Until that exists, an untrusted extension is
rejected, not loaded optimistically.

## Primary-source findings

- Microsoft's .NET plugin tutorial uses `AssemblyDependencyResolver` and a custom
  `AssemblyLoadContext` to isolate dependency resolution, but explicitly warns that
  untrusted code cannot be loaded safely into a trusted .NET process and recommends an OS
  or virtualization boundary for security or reliability:
  <https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support>.
- The `AssemblyLoadContext` API documentation is blunter: it provides no security
  features and all loaded code has the process's full permissions. It recommends process
  boundaries and IPC for actual isolation:
  <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext>.
- Microsoft's conceptual loading documentation says contexts provide type and dependency
  scopes, not binary isolation; shared contract types must be deliberately loaded from a
  common context:
  <https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext>.
- Microsoft's library versioning guidance distinguishes package, assembly, file and
  informational versions and recommends semantic versioning for the public package
  contract. WordToolkit therefore keeps extension release SemVer separate from the
  engine and capability interface `major.minor` contracts:
  <https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning>.
- Windows Job Objects can group processes, enforce memory/CPU/time limits, account for
  resources and terminate a process tree. They are a necessary building block for the
  future Windows out-of-process host, but the same documentation notes that security
  restrictions still belong to individual process security policy:
  <https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects>.
- Microsoft's Open XML documentation demonstrates schema validation through
  `OpenXmlValidator.Validate`; the official SDK repository states that the SDK is a
  low-level OPC/Open XML framework rather than a high-level document engine:
  <https://learn.microsoft.com/en-us/office/open-xml/word/how-to-validate-a-word-processing-document>
  and <https://github.com/dotnet/Open-XML-SDK>.

## Implemented boundary

`WordToolkit.Engine.Extensions` now owns a versioned, fail-closed registry:

1. a host must explicitly allow each stable extension ID;
2. trust level and isolation mode must be accepted by policy;
3. every capability declares one of fourteen kinds, a stable interface contract and a
   compatible `major.minor` version;
4. permissions are flags for package/content access, mutation, sensitive metadata,
   filesystem, network, process, live Word and credentials;
5. per-capability limits cover input bytes, output bytes, concurrent invocations and a
   cooperative timeout;
6. duplicate IDs, conflicting metadata, unknown interface kinds/versions, undefined
   permission bits and policy escalation fail before the registry is published;
7. `Build()` freezes registration; the public catalog is read-only, source-order
   independent and bound by a deterministic SHA-256;
8. invocation checks the requested CLR interface exactly, takes a non-blocking
   concurrency lease, links cancellation to the cooperative timeout and rejects oversized
   input or output;
9. implementation types, assembly names/paths and exception details are absent from the
   catalog and public execution errors.

The first real registered capability is
`wordtoolkit.validator.openxml.microsoft365`. The production CLI and saved-package/live
Word transaction paths for semantic styles, comment bodies, review decisions, package
patches and patch rollback now obtain `IWordPackageCandidateValidator` through the same
registry-backed adapter. Tests may still instantiate the SDK validator directly as an
independent oracle.

`InspectExtensionCatalogOperation`, native `extensions` CLI and lazy
`inspect_wordtoolkit_extensions` MCP expose one typed, bounded and content-free result.
The operation reads no document, opens no Word instance, performs no assembly discovery,
uses no network and returns no implementation type or path. The normal capability
manifest now contains 112 actions, 15 exposed MCP tools and 25 actions with complete
operation version, permission, reversibility and output-schema metadata.

## Evidence

- Engine tests prove registration-order-independent hashing; closed allowlists; engine
  and interface version compatibility; permission and resource denial; duplicate and
  post-freeze rejection; input/output/concurrency/timeout limits; catalog paging,
  filtering and redaction; and validator routing through the registry.
- Native tests prove direct Engine/CLI/handler/lazy-MCP catalog parity, closed schemas,
  lazy exposure, strict input rejection and zero Word COM invocations.
- Existing style, comment, review, patch and rollback suites exercise the registry-backed
  Open XML SDK validator through the production transaction paths.
- Two SDK 8.0.423 builds are byte-identical. The enabled personal plugin
  `0.39.0+codex.20260724201229`, its marketplace source and the build contain the same 196
  files and 87,396,612 bytes with zero path/length/hash differences. The installed EXE
  reports 112 actions and 25 complete metadata contracts; its real lazy-MCP call returns
  catalog SHA-256
  `dfb26f3c1da808d94ebfac6782fff391f9e174e7c606235a9e05de6dc2b234bd`
  with `loads_assemblies=false` and `opens_word=false`.

## Honest limits

- A cooperative timeout cancels code that observes the supplied token. It cannot safely
  preempt arbitrary in-process code. WordToolkit detects an overrun after return, but that
  is not a rollback mechanism and must never authorize an untrusted mutator.
- Output size is checked before the result leaves the registry, but after the capability
  returns. Mutating extensions still require the engine's staged candidate, invariant
  proof and atomic publication transaction.
- No third-party assembly discovery, installation, signature verification, dependency
  resolver, unload lifecycle or hot reload exists yet.
- `OutOfProcess` and `ProcessBoundary` are reserved contract values and are rejected by
  the current builder. The future host needs framed IPC, process identity, restricted
  tokens/AppContainer policy where feasible, Job Object limits, network/filesystem
  brokering, crash recovery and compatibility corpus tests before those values can be
  claimed as implemented.
