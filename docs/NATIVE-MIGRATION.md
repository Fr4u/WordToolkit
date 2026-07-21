# Native runtime migration

## Outcome

WordToolkit 0.18 uses a self-contained .NET 8 Windows x64 MCP server.

The installed MCP process:

- launches `wordtoolkit-native.exe` directly;
- does not invoke `python`, `pythonw`, `uv`, PowerShell or a shell helper;
- contains no Python source, bytecode, lockfile, project file or virtual environment;
- exposes 48 native tools;
- starts or attaches to Word and controls it through a persistent COM STA thread.

The old Python source remains outside the packaged plugin as historical and remote-service reference code.

## Architecture

```text
Codex model
    |
    | line-delimited MCP JSON-RPC over STDIO
    v
wordtoolkit-native.exe
    |
    | bounded queue
    v
persistent background STA thread
    |
    | Running Object Table attachment or explicit COM activation
    v
real Microsoft Word
    |
    +-- open/create/save/close/quit lifecycle
    +-- one bulk Range.Text assignment
    +-- bounded native Find / reverse replacement
    +-- one ConvertToTable call
    +-- typed table formulas, bookmarks and allowlisted fields
    +-- one ListFormat call
    +-- images, comments, notes, headers and footers
    +-- tokenized review, structure maps and layout diagnosis
    +-- installed Word type-library catalog and guarded capability execution
    +-- native OMath Add + BuildUp
    +-- native ExportAsFixedFormat PDF
    +-- one custom Undo record per mutation batch
```

`IOleMessageFilter` handles Word busy/rejected-call retries on the owning STA thread. No COM proxy crosses threads.

## Version and target identity

Each connection returns an opaque `live_document_id` plus a monotonic `live_version`. Writes may require an exact `expected_version`.

Selection tokens bind:

- live document ID;
- live version;
- Word window handle;
- story type;
- start and end range;
- SHA-256 of bounded nearby context.

Undo tokens bind:

- live document ID;
- live version;
- exact top Undo entry.

The runtime refuses Undo unless the current top entry still matches and begins with `WordToolkit:`.

## Transaction strategy

Large generated output is not sent as simulated keystrokes. `apply_live_word_operations`:

1. validates and converts the complete operation array;
2. builds one text payload;
3. records exact relative ranges;
4. suspends screen updates;
5. starts one custom Word Undo record;
6. assigns the payload once;
7. applies formatting to tracked text ranges;
8. creates equations in reverse range order so later range changes cannot invalidate earlier offsets;
9. verifies the resulting native equation count;
10. closes the Undo record or rolls back the whole transaction.

This is the lowest-latency safe approximation of “model generation directly into Word”. Token-by-token COM writes would be visibly slower, produce a rotten Undo history and expose partially generated structure.

## Real measurements

Environment:

- Windows;
- Microsoft Word 16.0;
- 2026-07-20;
- active real Word documents, not mocks.

### Old Python baseline

100 text operations, 48,800 characters:

- initial Word connection: 79.665 ms;
- batch wall time: 751.658 ms;
- reported COM transaction: 717.924 ms;
- first `uv run` additionally created `.venv`, installed 41 packages in about 690 ms, and took about 4.4 s end to end.

### Native development runtime

100 text operations, 48,800 characters:

- cold connection: 356.036–377.688 ms;
- batch wall time: 259.455–268.126 ms;
- reported native transaction: 243.594–250.860 ms;
- Undo: 31–56 ms.

### Packaged self-contained runtime

- process start through MCP `initialize`: 106.767 ms;
- tool count: 48;
- Python/uv children: 0;
- observed child processes: `conhost.exe` only.

The 48,800-character mutation path improved by roughly 2.9×.

### Installed-cache acceptance

The executable from:

```text
C:\Users\Admin\.codex\plugins\cache\personal\wordtoolkit\0.18.0+codex.20260720163946
```

passed:

- insert;
- native Find;
- transactional replacement;
- guarded Undo;
- native table;
- native numbered list;
- LaTeX-to-native-OMath equation;
- native start/open/close lifecycle;
- content-bound Find token and native comment;
- native footnote, inline image, header and footer;
- secure MathML/OMML-to-native-OMath equations;
- native PDF export;
- saved-DOCX Open XML SDK validation;
- cleanup back to the original document state.

No Python or `uv` child process appeared.

## Package evidence

Built package:

```text
dist/WordToolkit-0.18.0+codex.20260720163946-native-win-x64.zip
```

Properties:

- 194 files;
- 81,882,172 uncompressed bytes;
- 36,372,648 ZIP bytes;
- executable SHA-256:
  `bb3f3eae500aac1988d04b35c6284dd4468dfd38f9def94f2d60c1f9a17a8ed2`.
- runtime assembly SHA-256:
  `b5cc960c972be84e03e2c429b36c0def261e0d3c836c72134b9bae9dd66499a9`.

The installed-cache executable and runtime assembly had identical package hashes.

Forbidden packaged runtime files found: 0.

## NativeAOT decision

A NativeAOT experiment produced a roughly 25 MB executable but failed real COM startup:

```text
System.NotSupportedException:
COM Interop requires ComWrapper instance registered for marshalling
```

The experiment was rejected and removed. Shipping a smaller binary that cannot attach to Word would be cargo-cult optimization. The release uses self-contained multi-file JIT .NET, which passed the real COM tests.

True NativeAOT would require a source-generated or handwritten `IDispatch`/`ComWrappers` layer rather than dynamic COM. That is a separate migration, not a publish flag.

## Bounded breadth

Version 0.18 exposes 48 implemented and real-Word-tested native tools. It
restores review management, table formulas and recalculation, bookmarks,
allowlisted fields, broad structure maps, bounded structure inspection, layout
diagnosis and the installed Word object-model catalog.

The catalog found 767 types and 12,167 members on the release machine. Every
member receives a stable metadata profile, but only policy-approved capability
IDs execute. Raw member names and dotted COM paths are never accepted.
Lifecycle, macro, DDE, print/mail/web, password/path, event, restricted,
application-global, unknown mutation and unverified setter effects remain
blocked. This is broad discovery with a narrow execution throat, not a false
claim that every ribbon command is safe automation.

## Known limits

- Windows x64 only.
- Word must be installed. The runtime can start it through COM when explicitly requested.
- Existing-file opening is limited to explicit absolute Word-readable DOC, DOCX, DOCM, DOT, DOTX, DOTM, ODT, RTF, TXT, PDF, HTML/MHTML and XML paths. Macro execution is force-disabled and external links are not updated during open.
- Images are embedded as inline shapes; floating layout and text wrapping are not yet exposed.
- Comment creation, reply, resolve and delete plus token-verified revision decisions and Track Changes state are exposed. Threaded replies or comment resolution may still be unavailable in older Word COM models and fail closed.
- Headers and footers currently accept text plus bounded style/formatting, not arbitrary fields, drawings or linked content.
- Closing and quitting require explicit save/discard policies. Untitled dirty documents fail closed under `save_all`.
- PDF export uses Word's native renderer. It is not an Undoable document mutation, and replacing a PDF requires `overwrite=true`.
- LaTeX coverage is intentionally bounded; unsupported commands fail before mutation.
- MathML and OMML are securely parsed and converted to Word linear math; source markup is not inserted verbatim or preserved byte-for-byte.
- Equation verification currently confirms native OMath creation and count. Full semantic OMML AST readback is not claimed.
- The local source tree still contains the previous Python implementation, but the installed plugin runtime does not.
