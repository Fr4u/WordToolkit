# Authenticated `.wtpatch` envelopes

Raw `.wtpatch` artifacts are deterministic reversible package patches. They are not
encrypted, and their content hashes do not establish who created them. The optional
`OpcPackagePatchEnvelopeCodec` adds transport/storage protection without changing the
inner patch format.

## Protection modes

- AES-256-GCM: confidentiality and authenticated metadata/payload with a caller-owned
  32-byte key, fresh 96-bit nonce and 128-bit tag.
- ECDSA-SHA256: origin authentication over canonical metadata, tag and payload. A
  restricted signer key ID is part of the signed metadata.
- Both: signature verification occurs before decryption, then GCM authentication and
  inner `.wtpatch` validation run before a patch is returned.
- Neither: a valid envelope container around a raw patch; this adds no trust boundary.

An expected signer key ID is only a selector. Trust comes from independently provisioning
the matching ECDSA public key. Never accept a public key embedded beside an untrusted
patch as proof of authorship.

## Engine API

```csharp
var codec = new OpcPackagePatchEnvelopeCodec();
var encryptionKey = LoadThirtyTwoBytesFromASecretStore();
using var signer = LoadEcdsaPrivateKeyFromASecretStore();

codec.Write(
    destination,
    patch,
    encryptionKey,
    signer,
    signerKeyId: "release-key-2026"
);
```

Reading requires the decryption key, a trusted verifier and—when policy demands
pinning—the exact key ID:

```csharp
using var verifier = LoadTrustedEcdsaPublicKey();
var result = codec.Read(
    source,
    encryptionKey,
    verifier,
    expectedSignerKeyId: "release-key-2026"
);
var patch = result.Patch;
```

The codec never serializes keys. Callers own key rotation, secure storage, revocation and
identity binding. Raw key bytes must not be placed in prompts, command history, source
control or document metadata. The current MCP contract intentionally has no key argument;
shipping one without a real secret-store policy would move the leak instead of fixing it.

## Remaining limits

The envelope materializes the serialized patch and AES-GCM payload in memory. It obeys a
separate 140 MiB serialized-patch ceiling by default, on top of the inner patch limits.
Encryption and signatures solve confidentiality/authenticity, not the version-1 patch
format's memory cost.
