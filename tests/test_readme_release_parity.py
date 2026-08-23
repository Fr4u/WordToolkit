import json
import re
from pathlib import Path

ROOT = Path(__file__).parents[1]


def _latest_release_manifest() -> dict:
    manifests = [
        json.loads(path.read_text(encoding="utf-8"))
        for path in (ROOT / "docs" / "releases").glob("v*.json")
    ]
    assert manifests, "At least one published-release manifest is required"
    return max(manifests, key=lambda item: item["published_at"])


def test_readme_latest_release_matches_immutable_release_manifest() -> None:
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    release = _latest_release_manifest()
    asset = release["asset"]

    stated_release = re.search(
        r"Release `(v[^`]+)` contains .*runtime \(`([^`]+)`\)",
        readme,
    )
    assert stated_release, "README must state the latest release and embedded runtime version"
    assert stated_release.groups() == (release["tag"], release["runtime_version"])
    assert (
        f"runtime guidance covering {release['native_action_count']}/"
        f"{release['native_action_count']} actions"
    ) in readme
    assert f"{release['public_mcp_tool_count']} public MCP tools" in readme
    assert f"Asset: `{asset['name']}`" in readme
    assert f"]({asset['url']})" in readme
    assert f"Size: `{asset['size_bytes']:,} bytes`" in readme
    assert f"SHA-256: `{asset['sha256']}`" in readme


def test_release_manifest_is_bounded_and_self_consistent() -> None:
    release = _latest_release_manifest()
    asset = release["asset"]

    assert re.fullmatch(r"v\d+\.\d+\.\d+", release["tag"])
    assert release["runtime_version"].startswith(release["tag"][1:] + "+")
    assert re.fullmatch(r"[0-9a-f]{40}", release["source_commit"])
    assert re.fullmatch(r"[0-9a-f]{64}", asset["sha256"])
    assert 0 < asset["size_bytes"] <= 200 * 1024 * 1024
    assert asset["name"] in asset["url"].replace("%2B", "+")
    assert release["tag"] in release["release_url"]
