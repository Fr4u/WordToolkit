from __future__ import annotations

import json
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

from wordtoolkit.live_learning import StructureLearningStore


def _observations(*, clean: bool = True) -> dict[str, dict]:
    return {
        "equation_types": {
            "present": True,
            "scanned": True,
            "types": [0, 1],
            "read_errors": 0 if clean else 1,
            "truncated": False,
        },
        "field_types": {
            "present": False,
            "scanned": False,
            "types": [],
            "read_errors": 0,
            "truncated": False,
        },
    }


def test_structure_learning_uses_exponential_rescan_schedule(tmp_path: Path) -> None:
    store = StructureLearningStore(tmp_path / "structures.json")

    first = store.recommendation(("equation_types", "field_types"))
    assert first["collections"]["equation_types"]["scan_due_on_next_presence"] is True

    store.record_map(_observations(), duration_ms=10)
    second = store.recommendation(("equation_types",))
    assert second["collections"]["equation_types"]["scan_due_on_next_presence"] is True

    store.record_map(_observations(), duration_ms=12)
    third = store.recommendation(("equation_types",))
    assert third["collections"]["equation_types"]["scan_due_on_next_presence"] is False

    store.record_map(
        {
            "equation_types": {
                "present": True,
                "scanned": False,
                "types": [],
                "read_errors": 0,
                "truncated": False,
            }
        },
        duration_ms=3,
    )
    fourth = store.recommendation(("equation_types",))
    assert fourth["collections"]["equation_types"]["scan_due_on_next_presence"] is True


def test_structure_learning_retries_after_dirty_scan_without_document_data(
    tmp_path: Path,
) -> None:
    path = tmp_path / "structures.json"
    store = StructureLearningStore(path)
    store.record_map(_observations(clean=False), duration_ms=9.5)

    recommendation = store.recommendation(("equation_types",))
    inspected = store.inspect()
    raw = path.read_text(encoding="utf-8")

    assert recommendation["collections"]["equation_types"]["scan_due_on_next_presence"] is True
    assert inspected["collections"][0]["scan_failures"] == 1
    assert inspected["collections"][0]["known_types"] == [0, 1]
    assert inspected["document_counts_stored"] is False
    assert inspected["content_stored"] is False
    assert inspected["path_exposed"] is False
    assert "document.docx" not in raw
    assert "paragraph text" not in raw


def test_structure_learning_recovers_from_oversized_or_malformed_store(
    tmp_path: Path,
) -> None:
    path = tmp_path / "structures.json"
    path.write_text("{broken", encoding="utf-8")
    store = StructureLearningStore(path)
    assert store.inspect()["observation_count"] == 0

    path.write_text(
        json.dumps(
            {
                "schema_version": 1,
                "privacy": "test",
                "observation_count": "bad",
                "total_duration_ms": "bad",
                "collections": {
                    "equation_types": {
                        "observations": "bad",
                        "known_types": ["0", "bad", 1],
                    },
                    "../../bad": {},
                },
            }
        ),
        encoding="utf-8",
    )
    inspected = store.inspect()
    assert inspected["observation_count"] == 0
    assert inspected["collection_count"] == 1
    assert inspected["collections"][0]["known_types"] == [0, 1]

    path.write_bytes(b"x" * (StructureLearningStore.MAX_FILE_BYTES + 1))
    assert store.inspect()["observation_count"] == 0


def test_structure_learning_serializes_concurrent_maps(tmp_path: Path) -> None:
    store = StructureLearningStore(tmp_path / "structures.json")

    def record(_index: int) -> None:
        store.record_map(_observations(), duration_ms=1)

    with ThreadPoolExecutor(max_workers=8) as executor:
        list(executor.map(record, range(40)))

    inspected = store.inspect()
    assert inspected["observation_count"] == 40
    assert inspected["average_duration_ms"] == 1.0
    assert not list(tmp_path.glob("*.tmp"))


def test_structure_learning_adapts_property_probes_without_values(
    tmp_path: Path,
) -> None:
    path = tmp_path / "structures.json"
    store = StructureLearningStore(path)

    first = store.property_recommendation(
        "content_controls",
        ("range", "title", "tag"),
    )
    assert all(item["probe_due_on_next_inspection"] for item in first["properties"].values())

    store.record_inspection(
        "content_controls",
        {
            "range": {
                "attempted": True,
                "successful_reads": 2,
                "failed_reads": 0,
            },
            "title": {
                "attempted": True,
                "successful_reads": 2,
                "failed_reads": 0,
            },
            "tag": {
                "attempted": True,
                "successful_reads": 0,
                "failed_reads": 2,
            },
        },
        duration_ms=4.5,
    )
    second = store.property_recommendation(
        "content_controls",
        ("range", "title", "tag"),
    )
    assert second["properties"]["range"]["status"] == "supported"
    assert second["properties"]["range"]["probe_due_on_next_inspection"] is True
    assert second["properties"]["tag"]["status"] == "unavailable"
    assert second["properties"]["tag"]["probe_due_on_next_inspection"] is True

    store.record_inspection(
        "content_controls",
        {
            "range": {
                "attempted": True,
                "successful_reads": 1,
                "failed_reads": 0,
            },
            "title": {
                "attempted": True,
                "successful_reads": 1,
                "failed_reads": 0,
            },
            "tag": {
                "attempted": True,
                "successful_reads": 0,
                "failed_reads": 1,
            },
        },
        duration_ms=3.5,
    )
    third = store.property_recommendation(
        "content_controls",
        ("range", "title", "tag"),
    )
    inspected = store.inspect()
    raw = path.read_text(encoding="utf-8")

    assert third["properties"]["tag"]["probe_due_on_next_inspection"] is False
    assert inspected["inspection_observation_count"] == 2
    assert inspected["average_inspection_duration_ms"] == 4.0
    assert inspected["property_values_stored"] is False
    assert inspected["content_stored"] is False
    assert inspected["document_counts_stored"] is False
    assert "Confidential title" not in raw
    assert "CustomerTag" not in raw


def test_structure_learning_rejects_unbounded_property_names(tmp_path: Path) -> None:
    store = StructureLearningStore(tmp_path / "structures.json")

    with pytest.raises(ValueError):
        store.property_recommendation("content_controls", ("../../text",))
