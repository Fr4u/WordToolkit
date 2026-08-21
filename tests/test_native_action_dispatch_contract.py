from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ACTION_CASE = re.compile(r'^\s*"(?P<name>[a-z0-9_]+)"\s*=>', re.MULTILINE)
GATEWAY_CONST = re.compile(r'private const string \w+Name = "(?P<name>[a-z0-9_]+)";')


def test_every_native_action_has_a_dispatch_case() -> None:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text(encoding="utf-8"))
    actions = catalog["native_runtime"]["actions"]
    assert len(actions) == 149
    assert len(set(actions)) == 149

    sources = [ROOT / "native" / "WordToolkit.Native" / "Word" / "WordLiveService.cs"]
    dispatch = []
    for source in sources:
        dispatch.extend(ACTION_CASE.findall(source.read_text(encoding="utf-8")))
    dispatch = [name for name in dispatch if name in actions]
    duplicates = sorted(name for name in set(dispatch) if dispatch.count(name) > 1)
    missing = sorted(set(actions) - set(dispatch))
    assert not duplicates, f"duplicate dispatcher cases: {duplicates}"
    assert not missing, f"native actions without dispatcher cases: {missing}"
    assert len(dispatch) == 149, f"expected 149 dispatcher cases, got {len(dispatch)}"

    handler_sources = sorted(
        (ROOT / "native" / "WordToolkit.Native" / "Word").glob("WordLiveService*.cs")
    )
    handler_text = "\n".join(source.read_text(encoding="utf-8") for source in handler_sources)
    assert "NotImplementedException" not in handler_text
    assert "TODO" not in handler_text


def test_public_tool_split_is_11_core_plus_4_gateways() -> None:
    catalog = json.loads((ROOT / "schemas" / "mcp-tools-local.v1.json").read_text(encoding="utf-8"))
    core = set(catalog["native_runtime"]["core_actions"])
    tool_catalog = (
        ROOT / "native" / "WordToolkit.Native" / "Protocol" / "ToolCatalog.cs"
    ).read_text(encoding="utf-8")
    gateways = set(GATEWAY_CONST.findall(tool_catalog))
    assert len(core) == 11
    assert gateways == {
        "get_wordtoolkit_capabilities",
        "search_wordtoolkit_actions",
        "inspect_wordtoolkit_action",
        "execute_wordtoolkit_action",
    }
    assert len(core | gateways) == 15
