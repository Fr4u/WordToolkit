from __future__ import annotations

import argparse
import hashlib
import json
import os
from collections import Counter
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

from wordtoolkit.live_member_capabilities import build_member_capability_registry


def _schema_validation_key(schema: Any) -> str:
    """Canonicalize instance-dependent const values before schema validation.

    ``check_schema`` validates the schema grammar, not whether a const happens to
    equal a particular capability ID.  Replacing const payloads preserves every
    structural validation check and lets identical generated templates share one
    validation pass.
    """

    def normalize(value: Any) -> Any:
        if isinstance(value, dict):
            return {
                key: ("__const__" if key == "const" else normalize(item))
                for key, item in value.items()
            }
        if isinstance(value, list):
            return [normalize(item) for item in value]
        return value

    return json.dumps(normalize(schema), ensure_ascii=True, sort_keys=True, separators=(",", ":"))


def _default_catalog() -> Path:
    configured = os.environ.get("WORDTOOLKIT_STORAGE_ROOT", "").strip()
    if configured:
        root = Path(configured).expanduser()
    elif os.name == "nt":
        root = Path(os.environ["LOCALAPPDATA"]) / "WordToolkit" / "sessions"
    else:
        root = (
            Path(os.environ.get("XDG_STATE_HOME", Path.home() / ".local" / "state"))
            / "WordToolkit"
            / "sessions"
        )
    return root / "word-live-object-model.json"


def audit(catalog_path: Path) -> dict[str, Any]:
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    registry = build_member_capability_registry(catalog)
    profiles = registry["profiles"]
    failures: list[dict[str, Any]] = []
    tool_names: set[str] = set()
    coverage_rows = []
    # The registry intentionally emits many structurally identical schemas.  Schema
    # checking is expensive (jsonschema walks every nested keyword), so validate each
    # canonical schema only once while preserving the exact failure classification.
    schema_check_cache: dict[str, str | None] = {}

    for position, profile in enumerate(profiles):
        capability_id = str(profile["capability_id"])
        tool = profile.get("virtual_tool", {})
        if tool.get("tool_id") != capability_id:
            failures.append(
                {
                    "position": position,
                    "capability_id": capability_id,
                    "failure": "virtual_tool_id_mismatch",
                }
            )
        tool_name = str(tool.get("name", ""))
        if not tool_name or tool_name in tool_names:
            failures.append(
                {
                    "position": position,
                    "capability_id": capability_id,
                    "failure": "missing_or_duplicate_virtual_tool_name",
                }
            )
        tool_names.add(tool_name)
        for schema_name in ("input_schema", "output_schema"):
            schema = tool.get(schema_name)
            cache_key = _schema_validation_key(schema)
            cached_failure = schema_check_cache.get(cache_key, "__missing__")
            if cached_failure != "__missing__":
                if cached_failure is not None:
                    failures.append(
                        {
                            "position": position,
                            "capability_id": capability_id,
                            "failure": f"invalid_{schema_name}",
                            "exception": cached_failure,
                        }
                    )
                continue
            try:
                Draft202012Validator.check_schema(schema)
            except Exception as exc:
                schema_check_cache[cache_key] = type(exc).__name__
                failures.append(
                    {
                        "position": position,
                        "capability_id": capability_id,
                        "failure": f"invalid_{schema_name}",
                        "exception": type(exc).__name__,
                    }
                )
            else:
                schema_check_cache[cache_key] = None
        coverage_rows.append(
            [
                capability_id,
                tool_name,
                tool.get("availability"),
                tool.get("endpoint"),
                tool.get("input_schema"),
                tool.get("output_schema"),
            ]
        )

    digest = hashlib.sha256(
        json.dumps(
            coverage_rows,
            ensure_ascii=True,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
    ).hexdigest()
    stats = registry["stats"]
    passed = (
        not failures
        and bool(stats["complete"])
        and int(stats["catalog_member_count"]) == len(profiles)
        and len(tool_names) == len(profiles)
    )
    return {
        "passed": passed,
        "catalog_path": str(catalog_path.resolve()),
        "catalog_generated_at": registry["catalog_generated_at"],
        "schema_version": registry["schema_version"],
        "catalog_member_count": stats["catalog_member_count"],
        "profile_count": len(profiles),
        "virtual_tool_count": len(tool_names),
        "schemas_checked": len(profiles) * 2,
        "execution_counts": dict(
            sorted(Counter(item["policy"]["execution"] for item in profiles).items())
        ),
        "virtual_tool_kind_counts": dict(
            sorted(Counter(item["virtual_tool"]["kind"] for item in profiles).items())
        ),
        "coverage_sha256": digest,
        "failure_count": len(failures),
        "failures": failures[:100],
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description=(
            "Validate the individual virtual-tool definition generated for every "
            "installed Microsoft Word COM member."
        )
    )
    parser.add_argument("--catalog", type=Path, default=_default_catalog())
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    result = audit(args.catalog)
    serialized = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.output.with_suffix(args.output.suffix + ".tmp")
        temporary.write_text(serialized, encoding="utf-8")
        temporary.replace(args.output)
    print(serialized, end="")
    if not result["passed"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
