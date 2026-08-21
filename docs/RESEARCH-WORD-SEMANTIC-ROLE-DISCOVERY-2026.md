# Semantic role discovery for Word paragraphs

Date: 2026-07-26

## Problem

The explicit document-engine goal contains `find every theorem`. WordprocessingML has no
standard theorem, lemma, definition or proof element. A paragraph is a generic block with
properties and inline content. A paragraph style has a stable document-local `styleId`,
but the standard uses that identity for formatting/inheritance, not for a universal
mathematical role. Visible names, aliases, typography and numbering are conventions.

Pretending that a bold paragraph beginning with `Theorem` is a standards-declared theorem
would turn a useful candidate into false certainty. The engine therefore needs a
source-linked evidence graph, not a Boolean regex.

## Standards and Word evidence

- Microsoft describes `w:p` as the basic block-level content unit. Its `w:pPr` carries
  paragraph properties, not a domain-specific semantic type:
  <https://learn.microsoft.com/en-us/office/open-xml/word/working-with-paragraphs>.
- Microsoft documents `w:styleId` as the primary identifier used to reference a style in
  the document. A style controls inherited formatting; an arbitrary style name is not a
  standardized theorem declaration:
  <https://learn.microsoft.com/en-us/office/open-xml/word/how-to-apply-a-style-to-a-paragraph-in-a-word-processing-document>.
- A structured document tag (`w:sdt`) may carry a title/alias and tag and can be bound to
  Custom XML. This is the strongest native place for an author or template to declare a
  semantic role, but the values and schemas are application-defined:
  <https://learn.microsoft.com/en-us/office/dev/add-ins/word/create-better-add-ins-for-word-with-office-open-xml>
  and
  <https://learn.microsoft.com/en-us/visualstudio/vsto/custom-xml-parts-overview>.
- Microsoft states that a content-control data binding maps the control to an XML element
  stored in a Custom XML part. That mapping can preserve data/view separation, but the XML
  vocabulary is not globally standardized as theorem semantics:
  <https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/2805f4e9-9333-4e7a-bb56-f1ce0e9e8e25>.

## Public evidence model

The first public slice recognizes these mathematical roles:

- theorem;
- lemma;
- proposition;
- corollary;
- definition;
- proof;
- example;
- remark;
- axiom;
- assumption.

One source paragraph can receive evidence from four channels, in descending authority:

1. an enclosing content-control tag that uses the closed
   `wordtoolkit:role=<role>` convention;
2. an enclosing content-control alias that uses the same convention;
3. a paragraph style whose exact ID, primary name or alias equals a conservative Polish or
   English role term;
4. a role term at the beginning of the paragraph followed by a strict label boundary.

Custom XML bindings remain inventoried by the existing content-control graph, but target
names and values are not semantic-role evidence in this first slice. A private XML
vocabulary is meaningless until a caller supplies an explicit profile that defines it.
Typography, indentation, numbering alone and fuzzy string similarity are never evidence.

The classification is `declared`, `style_convention`, `lexical_candidate` or
`conflicting`. A unique declared/style/lexical role is useful evidence at decreasing
strength; only a unique declaration is called author-declared. Unresolved source evidence
is reported as an issue rather than fabricated as a candidate. A conflict never chooses a
winner. Revision or unresolved Markup Compatibility content makes the candidate
view-ambiguous and unusable until the caller chooses a view through a future module.

## Privacy and token contract

Default inspection returns package-bound candidate IDs and fingerprints, paragraph IDs,
roles, classifications, story/source order, ambiguity state and evidence counts. It
returns no paragraph text, evidence records, character counts, short text/value hashes,
style names/IDs, content-control identities, Custom XML names or source paths.

Evidence records, style identity, declaration identity, short hashes, source provenance
and sensitive text/count metadata are separately gated. A positive text-preview length
also requires the sensitive-content gate. The declaration convention is recognized
internally, but its raw tag or alias value is never returned. No response returns raw XML
or Custom XML values. Paging after offset zero requires the exact package fingerprint. The
action never opens Word, evaluates a field, follows an external relationship or changes
the package.

## Coverage boundary

`analysis_execution_complete=true` means every eligible projected paragraph was examined
under the selected profile. It does not mean that every human concept was recovered.
`semantic_role_coverage_complete` remains false whenever the document contains ambiguous
revision/MCE content, unmodeled stories, unsupported language/convention evidence or
styles-with-effects that require Word execution. Even a clean result cannot prove that an
author used no unstated convention.

The first slice identifies one role-bearing paragraph, not a multi-paragraph theorem body.
Cross-paragraph block extent, proof-to-theorem linkage, equation/citation dependencies,
template-declared profiles, user-defined role dictionaries, ML classification, mutation
and a qualified multilingual corpus remain separate work.

## Qualified Word acceptance

The gated `RealWordSemanticRoleAcceptanceTests` fixture was opened and saved by Microsoft
Word 16.0 build 16.0.20131 on 2026-07-26, then validated again with the Microsoft Open XML
SDK. The engine recovered one lexical, one explicit-style and one enclosing-SDT theorem
candidate after Word's normalization. A run-level SDT carrying the same declaration
inside an otherwise ordinary paragraph did not become paragraph-role evidence. This
qualifies persistence on that exact Word build; it is not a cross-version, cross-language
or semantic-recall corpus.
