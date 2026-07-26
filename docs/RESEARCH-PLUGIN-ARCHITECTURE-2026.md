# Plugin architecture research and implementation evidence

Date: 2026-07-24

## Verdict

A directory of DLL files is not a plugin architecture. It is an uncontrolled code-loading
surface wearing a cleaner name. `AssemblyLoadContext` can separate dependency resolution,
but it cannot protect the WordToolkit process, documents, credentials, filesystem or live
Word instance from code already executing inside that process. The first implementation
therefore permits only trusted, explicitly registered in-process modules. It does not scan
directories, call `Assembly.LoadFrom`, or advertise dependency isolation as a sandbox.

Version 0.57 adds the first separate-process, closed-IPC and Job Object resource boundary
for the built-in OCR proxy. That boundary contains failure, timeout and memory/process
growth. The missing untrusted-provider boundary is narrower and harder: restricted OS
identity plus brokered filesystem/network access, signed installation and lifecycle. Until
that exists, arbitrary third-party code is still rejected rather than loaded optimistically.

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
   concurrency lease, links cancellation to the declared timeout and rejects oversized
   input or output; trusted in-process capabilities remain cooperative, while the OCR
   process proxy owns hard termination;
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
- The clean hosted-Windows package job for commit `0226954` reproduced the exact local
  36,831,975-byte distributable at SHA-256
  `2028f140497c272032e5fd24084602a8e6716998adf92f4b9049a74aae70084f`.

## First process-boundary implementation — 0.57

The reserved `OutOfProcess` and `ProcessBoundary` values now have one real production
consumer rather than existing as decorative enums. Registration accepts an out-of-process
capability only when the host explicitly supplies an
`IWordToolkitProcessBoundaryProxy`; the same proxy is rejected if it is mislabeled as
trusted in-process code. Policy additionally requires a hard timeout mode and a positive
process-memory ceiling. An ordinary capability object cannot opt itself into this trust
class.

The first proxy is the Tesseract OCR adapter. The parent sends one bounded, closed JSON
request to a fresh copy of `wordtoolkit-native`, including a random request ID, verified
image bytes/hash, explicit provider/model paths and pre-execution hashes of the exact host
executable and assembly. The child validates duplicate/unknown fields, protocol, request
identity and its own binary identity before calling provider code. Its response is a
closed typed OCR object or a bounded error code; implementation types, paths, raw stderr
and exception text never cross the channel. The parent verifies request binding,
exit-code consistency and host hashes again after execution.

On Windows the child is attached before request publication to a Job Object with
`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, a 1 GiB aggregate job-memory limit and an active
process limit of three. The child blocks on stdin before it can launch Tesseract, closing
the ordinary start/assign race for provider execution. Timeout or cancellation terminates
the complete job and also attempts `Process.Kill(entireProcessTree: true)`. Child
processes join the job by default, while breakaway is not enabled. These mechanics follow
Microsoft's documented Job Object, assignment, active-process, memory and kill-on-close
contracts: [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects),
[AssignProcessToJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject),
[basic limits](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information)
and [nested jobs](https://learn.microsoft.com/en-us/windows/win32/procthread/nested-jobs).

This is crash, timeout and resource containment, not a permission sandbox. Microsoft
explicitly separates Job Object process management from per-process security policy. The
child still uses the caller's Windows token; restricted-token/AppContainer policy,
network isolation and filesystem brokering remain absent. The environment is minimized,
but that is not a security boundary.

The seven-sample real-Tesseract benchmark alternated direct and isolated calls over the
same 16,734-byte PNG. All fourteen typed result hashes were identical. Direct median was
248.0910 ms; isolated median was 482.1869 ms, a disclosed +234.0959 ms / +94.36% cost.
Raw evidence is checked in at
`docs/benchmarks/ocr-provider-process-boundary-2026-07-26.json`.

## Honest limits

- A cooperative timeout cancels code that observes the supplied token. It cannot safely
  preempt arbitrary in-process code. WordToolkit detects an overrun after return, but that
  is not a rollback mechanism and must never authorize an untrusted mutator.
- Output size is checked before the result leaves the registry, but after the capability
  returns. Mutating extensions still require the engine's staged candidate, invariant
  proof and atomic publication transaction.
- No third-party assembly discovery, installation, signature verification, dependency
  resolver, unload lifecycle or hot reload exists yet.
- Out-of-process registration currently covers only the built-in OCR proxy. There is no
  signed third-party package discovery, dependency installation, restricted token,
  AppContainer, network isolation, filesystem broker or generic provider lifecycle.
- Job assignment occurs before the parent publishes the request, so provider execution
  cannot begin first. The WordToolkit child itself necessarily starts before assignment;
  pre-assignment memory operations are not retroactively examined by
  `AssignProcessToJobObject`, which is why identity binding and the no-request/no-provider
  startup state are separate invariants.
