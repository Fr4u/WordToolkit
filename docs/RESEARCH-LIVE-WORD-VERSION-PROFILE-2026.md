# Connected Word version profile

Date: 2026-07-24
Contract: `wordtoolkit.inspect_live_word_version_profile/1.0`

## Problem

A Word package can declare a compatibility mode, but that declaration does not identify
the Word process that is currently interpreting the document. Conversely, a COM type
library proves that a member exists in an installed library; it does not prove that the
member is available on the current application/document objects or behaves identically in
every build. Treating either signal as a complete feature matrix is rotten architecture:
it converts incomplete evidence into a false guarantee.

The first bounded version-profile slice therefore reports raw environment facts and small
runtime probes without reading document content. It is intentionally not an Office SKU,
update-channel or behavioural-compatibility detector.

## Microsoft object-model evidence

- [`Application.Version`](https://learn.microsoft.com/en-us/office/vba/api/word.application.version)
  is a read-only String containing the Word version number.
- [`Application.Build`](https://learn.microsoft.com/en-us/office/vba/api/word.application.build)
  is a read-only String containing the application version/build number.
- [`Document.CompatibilityMode`](https://learn.microsoft.com/en-us/office/vba/api/word.document.compatibilitymode)
  is a read-only Long. Microsoft documents that older compatibility modes withhold newer
  or enhanced Word features.
- [`WdCompatibilityMode`](https://learn.microsoft.com/en-us/office/vba/api/word.wdcompatibilitymode)
  defines 11, 12, 14, 15 and 65535 for Word 2003, 2007, 2010, 2013 and current mode.
- [`Document.SaveFormat`](https://learn.microsoft.com/en-us/office/vba/api/word.document.saveformat)
  is a read-only Long that can be either a `WdSaveFormat` value or a unique external
  converter number. The profile therefore returns the raw integer and does not force an
  incomplete enum name.
- [`Application.UndoRecord`](https://learn.microsoft.com/en-us/office/vba/api/word.application.undorecord),
  [`Application.OMathAutoCorrect`](https://learn.microsoft.com/en-us/office/vba/api/word.application.omathautocorrect),
  [`Application.SmartArtLayouts`](https://learn.microsoft.com/en-us/office/vba/api/word.application.smartartlayouts)
  and [`Document.ContentControls`](https://learn.microsoft.com/en-us/office/vba/api/word.document.contentcontrols)
  are read-only object/collection properties. Accessing each property is a narrow member
  probe; the operation does not enumerate or mutate the returned object.

## Contract

The only input is one existing `live_document_id`. Unknown fields fail before any COM
call. The action requires an already connected document and never starts Word.

The response contains:

- raw application `version` and `build`, each bounded to 64 characters;
- a parsed non-negative major version and conservative family label;
- raw document compatibility mode and save format;
- a documented compatibility-profile label only for 11, 12, 14, 15 and 65535;
- four independent `available`, `unavailable` or `probe_failed` results;
- fixed issue codes for failed or out-of-contract reads;
- explicit interpretation and security limits.

Major version 16 is labelled `word_16_generation`. The contract sets
`product_edition_inferred=false`; it does not call the installation Microsoft 365,
Word 2019, Word 2021 or Word 2024 based on an ambiguous major number.

## Privacy and failure boundaries

The implementation reads no range, paragraph, equation, shape text, package XML, path,
user identity or licence identity. It returns no COM object and performs no network I/O.
Every COM read is isolated: failure of `Build`, for example, does not erase a successful
`Version` or compatibility-mode result. Exception types and messages are swallowed at the
boundary and replaced with one of eight fixed issue codes.

`available` means only that property access returned a non-null object. It does not prove
that a future mutation will succeed, that all methods on the object are safe, or that
layout and serialization match another Word build. The response states both
`version_identity_is_feature_guarantee=false` and
`runtime_probe_result_is_feature_behavior_guarantee=false`.

## Verification

The native regression suite covers the closed operation metadata, successful Word 16.0
projection, conservative version/compatibility mapping, all three probe states, isolated
property failures, explicit null preservation, fixed issue codes, absence of exception
text, zero sensitive-text reads and rejection of unknown arguments before COM dispatch.

The exact installed `0.39.0+codex.20260724220014` lazy MCP then attached to the current
licensed Word process. It reported application version 16.0, build 16.0.20131, document
compatibility mode 15, save format 12, all four probes available and zero issues. The
operation left `live_version` at zero. Its complete success envelope validated against
the output schema returned by that same installed runtime, and a recursive response-field
scan found no path, full-name, document-text, raw-COM, user-identity or licence-identity
field.

This is only the first honest slice. A complete Word-version subsystem still needs
behavioural probes, policy for update channels and architectures, a qualified matrix of
licensed Word builds, document-mode fixtures, font/printer/layout environment records and
cross-build render/edit/save comparisons.
