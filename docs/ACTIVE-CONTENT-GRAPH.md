# Active-content metadata graph

WordToolkit projects a bounded, read-only graph of active-content declarations,
relationships and package parts. The graph is evidence for inspection and policy. It is
not an execution, extraction, decryption or trust engine.

## Scope

The graph recognizes exact standardized or Microsoft relationship types rather than
matching URI suffixes. It covers Transitional and Strict office relationship namespaces
where both forms exist.

| Layer | Typed evidence |
|---|---|
| Word declarations | legacy `o:OLEObject`, `w:objectEmbed`, `w:objectLink`, `w:control` |
| OLE/package topology | internal or external OLE targets and embedded-package parts |
| ActiveX | XML definition part, class/persistence metadata, property count, license presence/length and its binary-persistence relationship |
| VBA/support | VBA project, Word VBA data, attached toolbars and VBA project-signature parts |
| Office customization | Ribbon custom UI, Quick Access Toolbar user customization and key-map customization |
| Package signatures | digital-signature origin and signature parts plus their relationship topology |

Payload records retain only bounded metadata: stable ID, kind, part URI, content type,
uncompressed length, package reachability, incoming relationship count, XML flag,
potential-execution flag, container family and the SHA-256 already computed by OPC
admission. The active-content graph does not load payload bytes again.

Declaration records retain the declared object/control metadata needed to diagnose
binding and target-mode contradictions. Field-code text is discarded; only presence and
exact character count remain. ActiveX property names and values are not retained. A
license is reduced to presence and character count.

## Stable identities and topology

- `wdad_…` identifies a Word object/control declaration.
- `wdax_…` identifies an ActiveX definition.
- `wdap_…` identifies a typed payload part.
- `wdar_…` identifies one typed relationship occurrence.

IDs are deterministic for one package fingerprint. They are selectors, not secrecy
boundaries. Duplicate relationship IDs in one source relationship set remain separate
occurrences and produce errors rather than making the builder select one arbitrarily.
Missing declarations, unresolved internal targets, forbidden external targets, ActiveX
binary ambiguity, macro-container contradictions and signature-root/source contradictions
remain typed diagnostics.

External OLE/package links are represented but never resolved by fetching their target.
For that reason their relationship state remains external and unresolved even when the
declaration itself is successfully bound to the relationship declaration.

## Hard safety boundary

All active-content projection and inspection paths enforce these facts:

- Word is not opened;
- macros and controls are not executed;
- binary payloads are not decoded;
- embedded packages are not opened or recursively inspected;
- external targets are not followed;
- XML parsing forbids DTD/entity expansion and network resolution;
- raw XML, field-code text, binary values, ActiveX license strings and ActiveX property
  values have no MCP response field;
- signature presence and topology are not cryptographic signature validation;
- no package mutation is performed.

The current model therefore cannot prove whether VBA is malicious, whether an OLE
compound file is internally valid, whether an embedded workbook is safe, whether an
ActiveX binary matches its declared class, or whether a digital signature is authentic,
trusted, current or revocation-clean.

## Token-lean inspection

`inspect_ooxml_active_content` defaults to `view=summary`. Its six paged views are:

- `summary`: payload-kind and declaration-kind counts;
- `declarations`: object/control declarations and binding state;
- `controls`: ActiveX definition and binary-binding topology;
- `payloads`: bounded payload metadata without bytes;
- `relationships`: typed target topology;
- `issues`: bounded diagnostics.

Use exact `declaration_id`, `control_id` or `payload_id` selectors or exact kind/role
filters before increasing `max_items`. Four disclosures are independent:

- `include_names` reveals bounded ProgID, object/control, class and persistence metadata;
- `include_targets` reveals bounded internal/external declared targets;
- `include_hashes` reveals payload SHA-256 values;
- `include_source` reveals bounded part URIs, content/relationship types, relationship
  IDs and XML ordinals.

The default direct response is regression-capped below 5,000 serialized characters and
the full mirrored JSON-RPC gateway envelope below 8,000 characters. One
`word_operation_accounted_v1` lease (`wop1`) spans OPC admission/metadata and the graph.
An exhausted lease fails with `PACKAGE_LIMIT`; clients cannot raise the server ceiling.

## Unified dependency graph

The dependency graph adds three node kinds:

- `active_content_payload`;
- `active_content_declaration`;
- `active_x_control`.

It adds explicit define, relationship-to-payload, declaration-to-payload and
ActiveX-to-binary-payload edges. External and unresolved evidence remains visible. The
coverage flag `active_content=true` means this metadata topology is included; it does
not mean binary internals, execution behavior, signature cryptography or encryption are
modeled.

## Remaining work

- compound OLE and embedded-package internal parsers behind a separate extraction policy;
- safe VBA metadata/static-analysis adapter without execution;
- ActiveX binary-format validation;
- signature-chain, trust, timestamp and revocation verification plus explicit removal or
  re-sign workflows;
- encrypted-package detection/decrypt/re-encrypt adapter with caller-owned keys;
- typed, authorization-bound active-content mutation and cross-version Word round trips;
- hostile and real-producer corpus expansion plus calibrated large-package benchmarks.

## Primary references

- [Open XML SDK: `ObjectLink`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.objectlink?view=openxml-3.0.1)
- [Open XML SDK: `DigitalSignatureOriginPart`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.wordprocessingdocument.digitalsignatureoriginpart?view=openxml-3.0.1)
- [Microsoft: convert DOCM to DOCX and remove `VbaProjectPart`](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-convert-a-word-processing-document-from-the-docm-to-the-docx-file-format)
- [Open XML SDK getting started](https://learn.microsoft.com/en-us/office/open-xml/getting-started)
