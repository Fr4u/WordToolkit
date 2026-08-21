# OPC digital-signature validation — 2026-07-27

## Decision

WordToolkit now has a bounded, cross-platform integrity verifier for digital signatures
inside DOCX, DOCM, DOTX and DOTM OPC packages. It verifies three distinct layers:

1. the package signature-origin and signature-part relationship topology;
2. the XMLDSIG `SignedInfo` signature value using an embedded certificate only as a
   verification key;
3. every declared package-part digest, including the OPC Relationship Transform and its
   selected relationship subset.

It deliberately does **not** claim certificate-chain trust, signer identity, revocation
status, legal validity or document authorship. Microsoft makes the same separation:
[`VerifySignatures`](https://learn.microsoft.com/en-us/dotnet/api/system.io.packaging.packagedigitalsignaturemanager.verifysignatures)
checks package signatures, while the application still owns certificate policy and
identity trust. The package API overview likewise treats signature validation and trust
as separate consumer responsibilities in the
[`PackageDigitalSignatureManager` contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.packaging.packagedigitalsignaturemanager).

## Standards shape

The verifier follows the OPC signature graph described by Microsoft's
[Open Packaging Conventions overview](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/opc/open-packaging-conventions-overview):

- package relationship -> digital-signature origin;
- origin relationship -> one or more XML signature parts;
- XMLDSIG `SignedInfo` -> one unique internal fragment;
- package object -> manifest references to signed parts;
- optional OPC Relationship Transform -> a declared relationship-ID/type subset followed
  by canonical XML.

The implementation uses the official .NET
[`System.Security.Cryptography.Xml`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.xml)
XMLDSIG primitives, but it does not hand untrusted input to them blindly. Before
`SignedXml.CheckSignature`, it rejects duplicate XML IDs, external or ambiguous
`SignedInfo` references, unsigned manifest objects, unsupported signature/digest/
canonicalization algorithms and unsupported transforms. Only manifests under the unique
object actually referenced by `SignedInfo` enter digest verification; sibling unsigned
objects cannot poison the result. The net8.0 build pins the patched 8.x package line at
[`System.Security.Cryptography.Xml` 8.0.4](https://www.nuget.org/packages/System.Security.Cryptography.Xml/8.0.4).

## Security boundary

`inspect_ooxml_signatures` is read-only and closed-world:

- DTDs, entity resolution and external XML resources are prohibited;
- no certificate store, network, AIA, OCSP or CRL lookup is used;
- external `SignedInfo` references are rejected, never fetched;
- raw XML, document text, certificate bytes, subject, issuer, serial number, email,
  organization and local path have no response field;
- certificate SHA-256/public-key algorithm and OPC part URIs are separate opt-ins;
- SHA-1 is recognized only for legacy verification and is marked `weak_algorithm=true`;
- fixed-time digest comparison is used;
- package/signature/certificate/reference/XML depth and paging limits fail closed;
- origin, signature and certificate parts must carry their exact OPC content types;
- XML encoding is left to the bounded standards-aware reader, including valid UTF-16;
- malformed individual manifest references become typed
  `signature_reference_structure_invalid` evidence instead of escaping the operation.

A `valid` result therefore means that the currently inspected package bytes agree with
the embedded signature under a supported algorithm. It does not mean that the embedded
key belongs to the claimed person or organization. `certificate_chain_trust_verified`
and `revocation_checked` remain literal `false` in every result.

## Independent qualification

A clean tracked DOCX fixture was copied and signed with WindowsBase
`PackageDigitalSignatureManager` using an ephemeral RSA-2048 self-signed certificate,
RSA-SHA256 and canonical XML. The private key was never serialized. WindowsBase returned
`Success`; WordToolkit independently returned:

| Evidence | Result |
|---|---|
| Signature topology | valid |
| XMLDSIG signature value | verified |
| Signed manifest references | 11/11 verified |
| WordToolkit status | `valid` |
| Chain trust | not verified |
| Revocation | not checked |

One byte in signed `word/document.xml` was then changed without touching the signature.
WindowsBase returned `InvalidSignature`; WordToolkit retained a verified `SignedInfo`
value but rejected the package because one manifest digest mismatched. This distinction is
important: the signature object was intact, but the signed document content was not.

The content-free evidence is checked in at
[`docs/benchmarks/opc-signature-qualification-2026-07-27.json`](benchmarks/opc-signature-qualification-2026-07-27.json).
Unit coverage also exercises a real OPC Relationship Transform subset, tampered content,
missing certificates, unsupported transforms, external references, duplicate IDs,
malformed reference structure and byte/count bounds.

## Public contracts

- Engine: `InspectOoxmlSignaturesOperation`
- CLI: `wordtoolkit-native inspect-signatures <path> ...`
- lazy MCP action: `inspect_ooxml_signatures`
- operation contract: `wordtoolkit.inspect_ooxml_signatures/1.0`
- views: `summary`, `signatures`, `references`, `issues`
- stable selectors: `wdsig_*` and `wdsref_*`

The summary is intentionally small. Page only the evidence the next decision consumes;
do not request certificate hashes or source URIs merely because they exist.

## Open work

This tranche does not implement certificate-chain construction, revocation, timestamp
authority validation, external certificate resolution, signing, signature removal,
re-signing, encryption/decryption, permission enforcement or legal-policy evaluation.
Those are separate policy and mutation systems. A future trust evaluator must be explicit
about trust roots, validation time, revocation mode, network behavior and privacy before
it can sit above this integrity layer.
