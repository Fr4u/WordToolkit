from __future__ import annotations

import json
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from wordtoolkit.live_learning import EquationLearningStore


def _outcome(
    *,
    input_format: str = "latex",
    features: tuple[str, ...] = ("display", "fraction"),
    success: bool,
    error_code: str = "",
) -> dict:
    return {
        "input_format": input_format,
        "features": features,
        "success": success,
        "readback_verified": success,
        "duration_ms": 12.5,
        "error_code": error_code,
    }


def test_learning_store_adapts_readback_and_preferred_format(tmp_path: Path) -> None:
    store = EquationLearningStore(tmp_path / "learning.json")
    features = ("display", "fraction")

    store.record_many(
        [
            _outcome(success=False, error_code="EQUATION_INVALID"),
            *[_outcome(input_format="unicodemath", success=True) for _index in range(3)],
        ]
    )

    recommendation = store.recommendation("latex", features)
    inspected = store.inspect()

    assert recommendation["force_live_readback"] is True
    assert recommendation["preferred_input_format"] == "unicodemath"
    assert inspected["observation_count"] == 4
    assert inspected["category_count"] == 2
    assert inspected["path_exposed"] is False


def test_learning_store_recovers_from_corrupt_and_malformed_data(tmp_path: Path) -> None:
    path = tmp_path / "learning.json"
    path.write_text("{broken", encoding="utf-8")
    store = EquationLearningStore(path)

    assert store.inspect()["observation_count"] == 0

    path.write_text(
        json.dumps(
            {
                "schema_version": 1,
                "privacy": "malformed test",
                "observation_count": "bad",
                "categories": {
                    "latex|display": {
                        "input_format": "latex",
                        "features": ["display"],
                        "successes": "bad",
                        "failures": None,
                        "total_duration_ms": "not-a-number",
                    },
                    "broken": "not-an-object",
                },
            }
        ),
        encoding="utf-8",
    )

    inspected = store.inspect()
    recommendation = store.recommendation("latex", ("display",))

    assert inspected["observation_count"] == 0
    assert inspected["category_count"] == 1
    assert inspected["categories"][0]["average_duration_ms"] == 0.0
    assert recommendation["observations"] == 0


def test_learning_store_serializes_concurrent_updates(tmp_path: Path) -> None:
    store = EquationLearningStore(tmp_path / "learning.json")

    def record(_index: int) -> None:
        store.record_many([_outcome(success=True)])

    with ThreadPoolExecutor(max_workers=8) as executor:
        list(executor.map(record, range(80)))

    inspected = store.inspect()

    assert inspected["observation_count"] == 80
    assert inspected["categories"][0]["successes"] == 80
    assert not list(tmp_path.glob("*.tmp"))


def test_learning_store_bounds_category_count(tmp_path: Path) -> None:
    store = EquationLearningStore(tmp_path / "learning.json")
    store.record_many(
        [
            _outcome(features=("display", f"feature-{index}"), success=True)
            for index in range(EquationLearningStore.MAX_CATEGORIES + 5)
        ]
    )

    inspected = store.inspect()

    assert inspected["category_count"] == EquationLearningStore.MAX_CATEGORIES
