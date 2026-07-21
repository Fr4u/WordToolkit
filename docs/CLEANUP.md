# Workspace cleanup

WordToolkit keeps source, tests, one current release and reproducible golden
documents. Historical releases, local environments, caches and transient test
artifacts are generated data. They are not product modules.

## What remains

- `src/wordtoolkit`: authentication, session isolation, Word Live, math,
  validation, rendering and MCP surfaces.
- `src/docx_mcp`: the compatibility OOXML engine used by
  `WordDocumentEngine`. Its old package name does not make it dead code.
- `plugin/wordtoolkit`: the single authoritative Codex plugin and skill.
- `tests`: unit, contract, security, round-trip and real-application harnesses.
- `examples/generated`: bounded golden DOCX/PDF/PNG evidence. Its `.work`
  scratch directory is disposable.
- `dist/wordtoolkit-<current>-release` and its matching ZIP only.

The obsolete `src/docx_mcp/skill` copy and `docx_mcp.cli` launcher are
deliberately absent. They described a different server lifecycle, attempted
to install instructions into `~/.claude/skills`, and were not declared
WordToolkit entry points.

## Safe cleaner

Preview the exact cleanup set:

```powershell
python scripts/clean_workspace.py
```

Apply it:

```powershell
python scripts/clean_workspace.py --apply
```

Remove the repository-local virtual environment only after final testing:

```powershell
python scripts/clean_workspace.py --apply --include-venv
```

The cleaner refuses the repository root and any path outside it. It keeps the
release matching `project.version`, collapses nested deletion targets and does
not follow directory symlinks.

## Proof that remaining modules work

`tests/test_runtime_modules.py` imports every packaged module under
`wordtoolkit` and `docx_mcp`. The full test suite then exercises the runtime
behavior. Release acceptance still requires:

1. full pytest and Ruff;
2. mypy on the maintained `src/wordtoolkit` layer;
3. plugin and skill validation;
4. a clean local-plugin build;
5. an MCP STDIO smoke test against that built directory;
6. exact file/hash comparison after marketplace installation.

Passing imports alone is not enough, but a module that cannot import is never
allowed into a release.
