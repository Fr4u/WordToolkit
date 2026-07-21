# Stage 1 — source, license and standards audit

## Reviewed foundations

### SecurityRonin/docx-mcp

- Repository: <https://github.com/SecurityRonin/docx-mcp>
- Reviewed commit: `b141be8153eff38ffac838b983ccc32f85f71acb`
- Reviewed version: 0.7.4
- License: MIT, copyright 2026 Security Ronin
- Reuse: the OOXML document mixins and their regression suite are vendored and adapted under `src/docx_mcp` and `tests/upstream`. The upstream license and README are retained in `third_party/docx-mcp`.

Strengths: direct ZIP/OOXML editing, paragraph IDs, native Word comments, notes, revisions, fields, styles, tables, relationships, raw-part preservation and a broad regression corpus. The extracted package remains the source of truth; a small edit does not flatten the document through Markdown.

Production gaps found: STDIO-only startup, process-global document state, local-path inputs, no authentication, no multi-tenant isolation, no Streamable HTTP, permissive/request-time dependency downloads, large ZIP limits, no signed artifact delivery, and a monolithic tool server. Its equation converter handled only a small MathML subset and flattened important structures; it was not suitable for the stated Office Math contract.

WordToolkit therefore reuses the mature document mixins, but puts them behind a separate session engine, bounded package inspector, canonical equation model, versioned exports and a remote MCP boundary. Security-relevant patches are maintained in this repository, not hidden in deployment glue.

### vace/markdown-docx

- Repository: <https://github.com/vace/markdown-docx>
- Reviewed commit: `0217bc8d4a4b4fad14fcc31285a596d6e566318d`
- Reviewed version: 1.7.0
- License: MIT, author Vace
- Reuse: no runtime code is copied. A reference copy of its MathML-to-docx approach is retained under `third_party/markdown-docx` with the upstream license.

Strengths: a practical TypeScript generation pipeline using KaTeX/MathML and mappings for fractions, scripts, radicals, n-ary operators and matrices. It demonstrates why MathML is a useful interchange stage.

Gap for this product: it is a generator-first pipeline, not a round-trip editor for arbitrary existing packages. Rebuilding an uploaded DOCX from Markdown would lose unknown parts, revisions, relationships, style semantics and vendor extensions. Its own examples also identify complex-math limitations. WordToolkit uses a canonical semantic AST and native OMML writer/parser instead.

## Language and library decision

| Criterion | Python 3.12 | TypeScript/Node |
|---|---|---|
| Existing direct-OOXML foundation | Strong: `docx-mcp`, `lxml` | Would require a rewrite or generator libraries |
| Safe XML parsing | `lxml` with DTD/entity/network disabled | Good libraries exist, but less aligned with the selected foundation |
| MCP Streamable HTTP | Official Python MCP SDK | Official TypeScript MCP SDK |
| PDF/page QA | Mature subprocess/Pillow/PyPDF integration | Possible, no material advantage |
| Existing DOCX round-trip | Stronger | Most popular packages are generation-oriented |
| Office Math generation | Custom semantic layer needed in either language | KaTeX helps parse LaTeX but does not solve DOCX round-trip |

Python 3.12 was selected for the service and document engine. `python-docx` is not used as the editing core and is not represented as full Word support. A small .NET 8 utility uses Microsoft's Open XML SDK as an additional validator in the production image.

## Standards sources used for format decisions

- Microsoft, Open XML package/SDK overview: <https://learn.microsoft.com/en-us/office/open-xml/about-the-open-xml-sdk>
- Microsoft, `OfficeMath` (`m:oMath`) class and parent semantics: <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.math.officemath?view=openxml-3.0.1>
- Microsoft Office Math DrawingML extension specification: <https://learn.microsoft.com/en-us/openspecs/office_standards/ms-odrawxml/853b19c7-68a9-4f9a-a2ae-5e6cb0d02e62>
- MCP Streamable HTTP transport: <https://modelcontextprotocol.io/specification/2025-11-25/basic/transports>
- MCP authorization: <https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization>
- OpenAI Apps SDK MCP server guide: <https://developers.openai.com/apps-sdk/build/mcp-server>
- OpenAI authentication guide: <https://developers.openai.com/apps-sdk/build/auth>
- OpenAI deployment guide: <https://developers.openai.com/apps-sdk/deploy>

The Microsoft Open XML SDK validator is authoritative for schema-level checks it implements. WordToolkit also performs security and package-integrity checks that a schema validator is not designed to provide.

