# OOXML encryption detection research — 2026-07-24

## Verdict

An encrypted OOXML file is not an OPC ZIP package with a password flag. Microsoft stores
the encrypted source package as streams inside an OLE Compound File Binary container.
Treating that file as an ordinary broken ZIP destroys the diagnostic boundary: corruption,
legacy compound documents and intentional encryption collapse into the same lie.

This tranche adds detection only. It does not accept passwords, derive keys, authenticate,
decrypt, encrypt, repair or open Microsoft Word. That missing secret-handling and publication
work remains explicit rather than being hidden behind a Boolean called `encrypted`.

## Primary Microsoft specifications

- [MS-OFFCRYPTO encrypted ECMA-376 document structure](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-offcrypto/2995dc93-0564-468c-891a-d950464479fb)
  defines the DataSpaces-based encrypted package inside an OLE compound file.
- [EncryptedPackage stream](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-offcrypto/b60c8b35-2db2-4409-8710-59d88a793f83)
  contains the encrypted ECMA-376 source package and begins with its eight-byte plaintext
  size.
- [EncryptionInfo stream](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-offcrypto/2895eba1-acb1-4624-9bde-2cdad3fea015)
  carries versioned encryption parameters. The four-byte version prefix distinguishes
  Standard (`2.2`, `3.2` or `4.2`), Extensible (`3.3` or `4.3`) and Agile (`4.4`) forms.
- [Agile encryption segments](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-offcrypto/9e61da63-8ddb-4c0a-b25d-f85d990f44c8)
  describes the segmented encrypted payload. Detection does not interpret or expose it.
- [MS-CFB header](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/05060311-bfce-4b12-874d-71fd4ce63aea)
  and [directory entries](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/a94d7445-c4be-49cd-b6b9-2f4abc663817)
  define the compound-file signature, sector geometry, FAT/DIFAT chains and directory tree.

## Implemented boundary

`InspectOoxmlEncryptionOperation` owns one provider-neutral contract:
`wordtoolkit.inspect_ooxml_encryption/1.0`. Direct Engine, strict
`inspect-encryption` CLI and lazy `inspect_ooxml_encryption` MCP call the same operation.
The general `inspect_ooxml_package` boundary now returns `DOCUMENT_ENCRYPTED` for a
complete envelope and `ENCRYPTION_CONTAINER_INVALID` for partial root markers instead of
collapsing either case into generic ZIP corruption.

The CFB probe:

- accepts Word, Excel, PowerPoint and Office-theme OOXML extensions;
- distinguishes OPC ZIP candidates, other compound files, complete encrypted OOXML,
  partial encryption markers, malformed CFB and unknown containers;
- validates header geometry, file/sector bounds, DIFAT and FAT identity, directory chains,
  the root sibling tree, regular-stream chains, MiniFAT chains and mini-stream bounds;
- requires exactly one root `EncryptionInfo` stream, one root `EncryptedPackage` stream
  and one root DataSpaces storage before reporting `is_encrypted_ooxml=true`;
- reads at most eight bytes from `EncryptionInfo` and never reads the encrypted payload;
- verifies the Standard AES/CryptoAPI flag pair, the Extensible external-provider flag or
  the Agile reserved `0x40` word before calling a recognized version structurally complete;
- reports only fixed classifications, counts and issue codes. It returns the leaf filename,
  but never the local path, stream names, raw bytes or document content.

Default limits are 576 MiB per file, 65,536 directory slots and 1,200,000 sectors in any
bounded parse. Cycle detection and exact expected-chain lengths stop hostile allocation
tables from becoming unbounded work.

## Proven cases

The deterministic in-memory corpus covers all six recognized version pairs, an unknown
future version, ordinary ZIP,
missing root markers, malformed FAT identity, path redaction, stream-position restoration,
unsupported extensions and file-byte ceilings. Native tests separately prove strict CLI,
closed lazy metadata, direct MCP dispatch, password-field rejection and zero Word COM calls.

A separate licensed Word 16.0 probe saved a new 19,456-byte password-protected DOCX. The
Release CLI classified it as CFB major 3 with 512-byte sectors, all three required root
markers, Agile `4.4`, 12 directory slots, three root children and zero issue codes. The
temporary document and its test-only password were deleted immediately after inspection;
the detector itself never received that password and did not launch Word. The probe also
exposed legitimate surplus FAT-sector preallocation by Word. The first exact-minimum check
was wrong; the corrected parser accepts a conservative local surplus ceiling of 109 sectors
while still enforcing the calculated minimum, physical sector count and global cap. A
deterministic regression constructs and accepts an independently valid surplus-FAT CFB.

The packaged Engine DLL and enabled-cache DLL both have SHA-256
`d4e341d892cdaac0b4ba1fed1582bffba522ce2691845d52a7908d17ccaf7776`;
the same source revision passed the licensed Word probe above.
The installed `0.39.0+codex.20260724210114` runtime also executed the lazy action on a valid
OPC package; that response validated against the output schema returned by the installed
action inspector and disclosed no path. Exact build-tree, personal-source and cache
identity closes local package-versus-installed drift, but not hosted or multi-Office-version
proof.

## Deliberately still missing

The current `complete_encryption_container` means that the required root markers and their
stream chains are structurally complete. It does not yet prove the full nested DataSpaces
transform graph or cryptographic correctness. Saved-package operations other than the
general inspector do not yet translate every encrypted input into one universal
`DOCUMENT_ENCRYPTED` error.

The next security slice must define an explicit authorization and secret-provider contract,
non-string secret transport, memory zeroization, allowed algorithm/KDF policy, verifier-first
decryption, authenticated create-new publication, wrong-password behavior, metadata leakage
rules and real Office interoperability. Until then, decryption and encryption remain absent.
