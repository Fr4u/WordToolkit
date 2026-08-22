# Third-party notices

## SecurityRonin/docx-mcp

WordToolkit vendors and modifies source and tests from `SecurityRonin/docx-mcp` at commit `b141be8153eff38ffac838b983ccc32f85f71acb`, version 0.7.4.

Copyright © 2026 Security Ronin. Licensed under the MIT License. The unmodified upstream license is retained at `third_party/docx-mcp/LICENSE`; upstream project information is retained beside it.

Material adaptations include session-local extraction, strict DTD/entity rejection, production package limits at the outer boundary, disabled request-time PII model downloads and use behind a remote authenticated service boundary.

## vace/markdown-docx

WordToolkit reviewed `vace/markdown-docx` at commit `0217bc8d4a4b4fad14fcc31285a596d6e566318d`, version 1.7.0. Copyright Vace, MIT License. Its license and a reference-only MathML conversion source are retained under `third_party/markdown-docx`. No file from that project is imported by the WordToolkit runtime.

## Runtime dependencies

Direct Python dependencies and their declared licenses must be verified by the release SBOM job. Principal dependencies are the MCP Python SDK, Starlette, Uvicorn, Pydantic, lxml, latex2mathml, httpx, PyJWT, Pillow, pypdf, Mistune and regex. The container also installs LibreOffice, Poppler and fonts from Debian packages, and builds `DocumentFormat.OpenXml` 3.3.0 from NuGet.

`uv.lock` and the container base image make exact versions auditable. Before redistribution, generate an SBOM and retain all package license files required by their respective licenses.
