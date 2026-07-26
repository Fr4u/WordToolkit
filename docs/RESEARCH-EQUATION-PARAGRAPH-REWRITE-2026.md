# Equation-paragraph rewrite research and design — 2026

## Decision

The semantic request "rewrite only the paragraph containing an equation" is not a
generic paragraph replacement. A Word paragraph can contain paragraph properties,
ordinary runs, fields, hyperlinks, revisions, range markers, content controls,
drawings and OfficeMath. Flattening that content to text would silently destroy object
identity and behavior.

WordToolkit therefore models only a closed, provable subset:

```text
paragraph := optional w:pPr + (ordinary text run | direct OfficeMath anchor)+
ordinary text run := optional w:rPr + one or more w:t
text slots := maximal ordered ordinary-run groups before, between and after anchors
anchor := direct m:oMath or m:oMathPara, preserved byte-for-byte
```

Every other direct or run-level structure blocks the candidate. This is a semantic
operation for AI agents, not permission to edit XML.

## Primary evidence

- ECMA-376 defines the OOXML vocabularies and document representation. The current
  publication page identifies Part 1 as the markup-language reference and Part 2 as
  OPC: <https://ecma-international.org/publications-and-standards/standards/ecma-376/>.
- Microsoft's ISO/IEC 29500 paragraph guidance describes `w:p` as the block-level
  paragraph with optional paragraph properties, inline content and revision IDs. It
  also identifies `w:r` as a formatting run and `w:t` as text:
  <https://learn.microsoft.com/en-us/office/open-xml/word/working-with-paragraphs>.
- The Open XML SDK paragraph/run example confirms that a paragraph normally contains
  runs and each run contains text, while the run type can also appear under fields and
  other rich containers. That is why descendants cannot be flattened indiscriminately:
  <https://learn.microsoft.com/en-us/office/open-xml/word/how-to-apply-a-style-to-a-paragraph-in-a-word-processing-document>.
- Microsoft's Word implementation notes state that Word will not open `m:oMath`
  outside a `w:p`, may merge adjacent `m:oMath` elements without a separating break,
  and rejects nested `m:oMath` shapes that the base grammar might otherwise admit:
  <https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/ab7a0345-712e-4eef-9bcc-80c37e68d9bb>.
- The Open XML SDK `m:oMathPara` reference defines a display-math zone containing one
  or more `m:oMath` elements. The outer display zone must therefore be one immutable
  anchor even when it contains several equations:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.math.paragraph?view=openxml-3.0.1>.
- Microsoft's implementation notes explicitly report unpredictable Word behavior for
  comments, ruby and pictures inside math. This is evidence for a fail-closed boundary,
  not a reason to copy or normalize those constructs:
  <https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/9c37e55b-cb80-4086-b754-a2822a74fe98>.

## Object and identity model

`WordEquationParagraphRewriteCatalogBuilder` projects each semantic `Paragraph` with
an equation descendant back to its exact lossless XML source element. It reports:

- stable semantic paragraph identity and story kind;
- one package-bound candidate ID plus a complete candidate fingerprint;
- ordered text slots with character counts, SHA-256 and existing text-leaf identities;
- direct inline/display OfficeMath anchors with exact source-byte SHA-256;
- fixed block codes rather than raw element names or XML.

The candidate fingerprint binds paragraph source identity, a rewrite-specific
structure fingerprint, ordered text slots, exact OfficeMath anchors and every block
reason. The structure fingerprint ignores only Word `w:rsid*` session attributes and
`xml:space` on `w:t`; the latter may legitimately change when a new slot begins or ends
with whitespace. It does not ignore run properties, element order, fields, wrappers,
extensions or any OfficeMath byte.

## Mutation contract

The public workflow has three lazy actions:

1. `inspect_ooxml_equation_paragraph_rewrites` pages compact candidates. Text is absent
   by default. Complete slot text requires one exact paragraph ID and is capped at
   65,536 returned characters.
2. `plan_ooxml_equation_paragraph_rewrites` accepts up to 64 exact candidates. Each
   command supplies one string per ordered slot. A slot without an existing `w:t`
   cannot receive new text because doing so would require an unreviewed structural
   insertion.
3. `apply_ooxml_equation_paragraph_rewrites` rebuilds the same plan from the current
   package, requires the original package/candidate/plan fingerprints, blocks signed
   packages and publishes atomically with a sibling backup by default.

The transaction keeps the existing run elements. The first text leaf of a slot receives
the replacement and later leaves become empty. This preserves every run and run-property
object without asking the agent to micromanage run IDs. It is deterministic, but it does
not claim that stylistic emphasis inside old prose has been semantically reassigned to
corresponding words in new prose.

## Proof obligations

Planning constructs the exact candidate package and requires all of the following:

- the predicted package fingerprint matches the materialized candidate;
- the equation-containing paragraph set does not change;
- every selected paragraph retains its rewrite-specific structure fingerprint;
- every OfficeMath anchor retains the same kind, source ordinal, contained equation
  count and exact XML SHA-256;
- every selected slot reads back as the requested text and retains its exact text-node
  ordinals;
- every unselected equation paragraph retains its complete candidate fingerprint;
- every unrelated OPC entry remains byte-identical through the underlying lossless
  transaction;
- applying the exact inverse reconstructs every original uncompressed OPC entry byte;
- Microsoft Open XML SDK validation introduces no new error.

Missing schema validation, signatures, stale evidence, malformed OPC, rich inline
content, missing text leaves, excess resource use, candidate drift, equation drift,
structure drift or inverse failure all block publication.

## Deliberate limits

The first supported slice does not:

- rewrite the OfficeMath expression;
- expose OMML, LaTeX or equation text to the model;
- insert a new run into an empty gap;
- cross a field, hyperlink, bookmark, revision, content control, drawing, tab, break or
  other rich boundary;
- choose a paragraph linguistically;
- map emphasis from the old prose onto different words in the replacement;
- claim byte-identical ZIP container metadata or visual equivalence before Word render.

These are not hidden omissions. They are explicit borders around the state the engine
can currently prove.
