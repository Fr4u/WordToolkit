# Deployment and client installation

## Local Codex plugin

The local plugin is the default path for one machine. It uses MCP STDIO and
does not expose a listening port.

1. Install `uv`.
2. Run `uv run python scripts/build_local_plugin.py --build-validator`.
3. Install `dist/wordtoolkit` through a local Codex marketplace.
4. Start a new Codex task so the new skill and MCP server are discovered.

On first launch, `uv` creates the pinned runtime environment under the plugin's
`runtime/` directory. Document sessions and exported artifacts live beneath
the user's local application-data directory. Local input accepts an absolute
path or `file://` URI. Local output is returned as an MCP resource link.

`local_stdio` is deliberately rejected by the HTTP application. It is a
single-user trust boundary for the local Codex host, not an authentication
mode for a network service.

## Local integration test

1. Copy `.env.example` to `.env` and replace both placeholder secrets.
2. Run `docker compose up --build`.
3. Verify `GET http://127.0.0.1:8787/health`.
4. Send MCP requests to `http://127.0.0.1:8787/mcp` with `Authorization: Bearer <development token>`.

The local token mode is intentionally rejected when `WORDTOOLKIT_ENVIRONMENT=production`.

## Production prerequisites

- public HTTPS hostname under your control;
- OAuth 2.1/OIDC provider capable of RS256/ES256/EdDSA access tokens;
- an API/audience representing the exact MCP resource;
- scopes `documents:read` and `documents:write`;
- secret manager values for signing secret and IdP configuration;
- one container instance with at least 2 CPU, 2 GiB RAM and bounded ephemeral storage;
- egress access to the IdP JWKS and authorized OpenAI file delivery hosts.

Set:

```dotenv
WORDTOOLKIT_ENVIRONMENT=production
WORDTOOLKIT_PUBLIC_BASE_URL=https://wordtoolkit.example.com
WORDTOOLKIT_BIND_HOST=0.0.0.0
WORDTOOLKIT_AUTH_MODE=oauth_jwt
WORDTOOLKIT_OAUTH_ISSUER=https://id.example.com/
WORDTOOLKIT_OAUTH_AUDIENCE=https://wordtoolkit.example.com/mcp
WORDTOOLKIT_OAUTH_JWKS_URL=https://id.example.com/.well-known/jwks.json
WORDTOOLKIT_OAUTH_SCOPES=documents:read documents:write
WORDTOOLKIT_SIGNING_SECRET=<at-least-32-random-characters>
WORDTOOLKIT_STORAGE_ROOT=/data/sessions
WORDTOOLKIT_ALLOWED_UPLOAD_HOST_SUFFIXES=.oaiusercontent.com,.blob.core.windows.net,.amazonaws.com
```

The OAuth provider must allow ChatGPT's callback URL shown during connector setup. Do not guess this URL: copy the current value from the ChatGPT connector UI. Configure exact redirect URIs, PKCE and short-lived access tokens. The MCP service is a resource server; it does not store passwords or issue login sessions.

## Render deployment

`render.yaml` defines a Docker web service, health check and bounded persistent disk. Create the service from the blueprint, supply all `sync: false` secrets, replace the hostname, and keep `numInstances: 1`. Render terminates TLS; the application validates forwarded host/origin and still requires OAuth.

## Google Cloud Run

Build and push the image, create the secrets named by `deploy/cloudrun-service.yaml`, then deploy that service description. Cloud Run ingress must allow public HTTPS transport because MCP performs end-user bearer authorization. Keep maximum instances at one for this release. Use a dedicated VPC/egress policy if document confidentiality requires it.

## ChatGPT installation and phone use

After the service is live:

1. In ChatGPT settings, open Connectors/Apps and add a custom remote MCP server.
2. Enter `https://<your-host>/mcp`.
3. Complete OAuth login and grant only the requested document scopes.
4. Start a Work Mode conversation and attach a DOCX from the phone.
5. Ask WordToolkit to open the attachment, inspect it, make a bounded edit, run `validate_ooxml`, then `generate_preview`.
6. Review the returned PNG/PDF on the phone. Request `export_document` only after the preview is acceptable.
7. Download the signed DOCX/PDF before its expiry.

No mobile step depends on `C:\...`, a local STDIO server or server filesystem paths. ChatGPT file parameters supply authorized download references; output arrives as signed HTTPS resource links.

## Codex plugin installation

The checked-in `plugin/wordtoolkit/.mcp.json` is the local STDIO configuration.
Build the self-contained plugin directory with `scripts/build_local_plugin.py`
before installing it. The included skill instructs Codex to use
open/inspect/small-edit/validate/render/export sequencing.

For a remote deployment, configure the deployed HTTPS MCP endpoint separately
in Codex or publish a remote plugin variant. Never put OAuth secrets in the
plugin manifest. Authentication occurs between the client, identity provider
and remote MCP endpoint.

## Upgrade and rollback

Pin an image digest in production. Before deployment, compare `schemas/mcp-tools.v1.json` and review `migrations/`. Roll forward only when the tool schema change is additive or a matching major migration exists. Rollback changes the container image; exported DOCX artifacts are self-contained and do not depend on a server database.
