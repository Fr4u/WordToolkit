# Flat OPC adapter research and implementation evidence

Date: 2026-07-24

## Verdict

Flat OPC is not a prettier DOCX. It is a transport projection of an OPC package into
one XML document. Treating it as ordinary XML text is a rotten boundary: binary parts
become Base64, every part carries its own content type, relationship parts remain real
parts, and `[Content_Types].xml` disappears from the transport and must be rebuilt.
Byte identity of XML serialization is not promised. Any implementation that ignores
those facts can produce a ZIP that validates structurally while changing the document
or destroying a package signature.

WordToolkit now owns this boundary in the neutral Engine. Open XML SDK is an independent
test oracle, not the implementation underneath the codec.

## Primary-source findings

- ECMA-376 Part 2 defines the Open Packaging Conventions package model. The current
  ECMA page identifies Part 2 as the independently updated OPC component of the fifth
  edition: <https://ecma-international.org/publications-and-standards/standards/ecma-376/>.
- Microsoft documents the Word/Office.js OOXML payload as an OPC package flattened into
  one XML document rooted at `pkg:package`, with `pkg:part`, `pkg:xmlData` and binary
  data for related binary parts: <https://learn.microsoft.com/en-us/office/dev/add-ins/word/create-better-add-ins-for-word-with-office-open-xml>.
- The official Open XML SDK exposes `ToFlatOpcDocument` and `FromFlatOpcDocument` for
  `WordprocessingDocument`: <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.wordprocessingdocument.toflatopcdocument>.
- The SDK source shows the exact Microsoft namespace, treats content types ending in
  `xml` as XML except AltChunk targets, emits binary payloads with `pkg:compression="store"`,
  and creates package parts from the `pkg:name` and `pkg:contentType` attributes:
  <https://github.com/dotnet/Open-XML-SDK/blob/main/src/DocumentFormat.OpenXml.Framework/Packaging/FlatOpcExtensions.cs>.

The SDK implementation is useful compatibility evidence, but it is not a sufficient
hostile-input parser for WordToolkit. Its public convenience path materializes an
`XDocument`, accepts the package element tree directly and does not own WordToolkit's
aggregate package budgets, create-new filesystem transaction or semantic verification.

## Implemented architecture

`FlatOpcPackageCodec` performs the format conversion inside `WordToolkit.Engine`:

1. parse the outer XML incrementally with DTD prohibited and no resolver;
2. enforce outer character/depth, part count, attribute, per-part decoded byte and
   aggregate decoded byte limits;
3. require the Microsoft `xmlPackage` namespace and a closed `package -> part ->
   xmlData|binaryData` shape;
4. reject duplicate, normalized-duplicate and case-colliding part URIs, traversal,
   embedded `[Content_Types].xml`, invalid compression values and nested binary XML;
5. decode Base64 in chunks into a bounded stream;
6. validate each XML payload with the lossless XML parser's DTD, depth, element, text
   and encoding limits;
7. reconstruct `[Content_Types].xml` with one exact override per transported part;
8. create a deterministic OPC ZIP and immediately re-read it through
   `OpcPackageReader`;
9. on export, preserve non-XML and malformed-XML payloads as binary and force XML-typed
   AltChunk targets to remain binary;
10. write deterministic, compact Flat OPC without returning any document XML to the AI.

`FlatOpcWordPackageOperation` adds the Word and transaction boundary. It accepts only
create-new paths, writes to an isolated sibling, blocks signed packages, verifies the
result as a Word semantic document, binds the output extension to the main-part content
type, compares the exact part-name/content-type/relationship sets, compares binary
payload bytes and XML trees, and only then moves the artifact into place. The same typed
request/result/error contract is used by direct .NET, `flat-opc-package` CLI and lazy
`convert_ooxml_flat_opc` MCP.

## Evidence

- 20 Engine tests cover Microsoft SDK interoperability, own-codec round trips, binary
  parts, relationships, AltChunk handling, deterministic ZIP output, manifest
  reconstruction, declaration-normalizing round trips of a bundled real Word document,
  destination non-mutation and bounded hostile inputs.
- Four Native tests prove direct Engine/CLI/MCP canonical result parity, byte-identical
  deterministic Flat OPC output, no Word invocation, strict schema rejection and
  machine-readable CLI failures.
- `native/WordToolkit.Engine.Tests/Corpus/flat-opc-corruption-v1.json` publishes 13
  named hostile cases covering DTD/entity input, root/schema drift, missing/multiple
  payloads, multiple XML roots, invalid Base64, duplicate/case-colliding/traversal URIs,
  embedded content types, unknown compression and nested binary XML.
- The full checkpoint passes 531 Engine and 399 Native tests.

## Honest limits

- XML part bytes may be reserialized. Tree semantics are proved; lexical byte identity
  is not. Signed packages are therefore blocked instead of silently invalidated.
- The outer XML is streamed, but decoded part payloads are retained until the candidate
  package can be published. The aggregate budget is 512 MiB and needs measured memory
  and latency evidence on large real Word exports.
- `pkg:padding` is validated but not reproduced as ZIP padding. Compression intent is
  accepted from the documented values; `store` maps to no compression and other values
  map to the deterministic engine compression policy.
- Full OPC URI conformance, encrypted packages, cross-version Word-produced Flat OPC
  fixtures and cloud ETag/version adapters remain open. The audit stays `Partial` where
  those wider claims are required.
