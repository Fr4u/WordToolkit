from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GATEWAY_NAMES = {
    "get_wordtoolkit_capabilities",
    "search_wordtoolkit_actions",
    "inspect_wordtoolkit_action",
    "execute_wordtoolkit_action",
}
GATEWAY_EXPOSURE_CALLS = {
    "exposed.Add(CapabilitiesTool());",
    "exposed.Add(SearchActionsTool());",
    "exposed.Add(InspectActionTool());",
    "exposed.Add(ExecuteActionTool());",
}


def _load_public_catalog(source_catalog: dict[str, object]) -> dict[str, object]:
    runtime = source_catalog["native_runtime"]
    assert isinstance(runtime, dict)
    core_names = set(runtime["core_actions"])
    source_tools = source_catalog["tools"]
    assert isinstance(source_tools, list)
    source_tool_names = {tool["name"] for tool in source_tools}
    assert core_names <= source_tool_names

    tool_catalog_source = (
        ROOT / "native" / "WordToolkit.Native" / "Protocol" / "ToolCatalog.cs"
    ).read_text(encoding="utf-8")
    runtime_gateway_names = set(
        re.findall(
            r'private const string \w+Name = "([^"]+)";',
            tool_catalog_source,
        )
    )
    assert runtime_gateway_names == GATEWAY_NAMES
    assert all(call in tool_catalog_source for call in GATEWAY_EXPOSURE_CALLS)

    public_tools = [tool for tool in source_tools if tool["name"] in core_names]
    public_tools.extend({"name": name} for name in sorted(runtime_gateway_names))
    return {"tools": public_tools}


def test_plugin_manifest_matches_native_catalog() -> None:
    manifest = json.loads(
        (ROOT / "plugin" / "wordtoolkit" / ".codex-plugin" / "plugin.json").read_text(
            encoding="utf-8"
        )
    )
    source_catalog = json.loads(
        (ROOT / "schemas" / "mcp-tools-local.v1.json").read_text(encoding="utf-8")
    )
    runtime = source_catalog["native_runtime"]
    action_count = len(runtime["actions"])
    core_names = set(runtime["core_actions"])
    catalog = _load_public_catalog(source_catalog)
    catalog_names = {tool["name"] for tool in catalog["tools"]}
    expected_public_names = core_names | GATEWAY_NAMES

    assert action_count == 151
    assert len(core_names) == 11
    assert len(GATEWAY_NAMES) == 4
    assert len(catalog["tools"]) == len(catalog_names)
    assert catalog_names >= GATEWAY_NAMES
    assert catalog_names >= core_names
    assert catalog_names == expected_public_names
    assert len(catalog_names) == 15

    short_description = manifest["description"]
    long_description = manifest["interface"]["longDescription"]
    public_surface_sentence = (
        "The public MCP surface has 15 tools: 11 core actions and 4 capability gateways."
    )
    assert "15 public MCP tools" in short_description
    assert "11 core actions" in short_description
    assert "4 capability gateways" in short_description
    assert "151 native actions" in short_description
    assert public_surface_sentence in long_description
    assert "lazily expose 151 native actions" in long_description


def test_plugin_mcp_entrypoint_is_local_native_runtime() -> None:
    mcp = json.loads((ROOT / "plugin" / "wordtoolkit" / ".mcp.json").read_text())
    server = mcp["mcpServers"]["wordtoolkit"]
    assert server["command"] == "./runtime/win-x64/wordtoolkit-native.exe"
    assert server["cwd"] == "."
    assert server["args"] == []
