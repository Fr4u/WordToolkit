from __future__ import annotations

import json
import os
import threading
from contextlib import suppress
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


class EquationLearningStore:
    """Privacy-preserving local outcomes for native Word equation classes.

    The store never receives formula text, document text, paths, owners or live
    document identifiers. It learns only from a bounded structural feature set.
    """

    SCHEMA_VERSION = 1
    MAX_CATEGORIES = 512

    def __init__(self, path: Path):
        self.path = path
        self._lock = threading.RLock()

    @staticmethod
    def category_key(input_format: str, features: tuple[str, ...]) -> str:
        return f"{input_format}|{','.join(sorted(features))}"

    @classmethod
    def _empty(cls) -> dict[str, Any]:
        return {
            "schema_version": cls.SCHEMA_VERSION,
            "privacy": "No formula text, document text, paths or owner identifiers are stored.",
            "observation_count": 0,
            "categories": {},
        }

    def _load(self) -> dict[str, Any]:
        if not self.path.is_file():
            return self._empty()
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError):
            return self._empty()
        if (
            not isinstance(payload, dict)
            or payload.get("schema_version") != self.SCHEMA_VERSION
            or not isinstance(payload.get("categories"), dict)
        ):
            return self._empty()
        return payload

    @staticmethod
    def _nonnegative_int(value: Any) -> int:
        try:
            return max(0, int(value))
        except (TypeError, ValueError, OverflowError):
            return 0

    @staticmethod
    def _nonnegative_float(value: Any) -> float:
        try:
            return max(0.0, float(value))
        except (TypeError, ValueError, OverflowError):
            return 0.0

    def _write(self, payload: dict[str, Any]) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_name(
            f".{self.path.name}.{os.getpid()}.{threading.get_ident()}.tmp"
        )
        try:
            temporary.write_text(
                json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
                encoding="utf-8",
            )
            with suppress(OSError):
                os.chmod(temporary, 0o600)
            temporary.replace(self.path)
        finally:
            with suppress(OSError):
                temporary.unlink()

    def recommendation(
        self,
        input_format: str,
        features: tuple[str, ...],
    ) -> dict[str, Any]:
        with self._lock:
            payload = self._load()
        key = self.category_key(input_format, features)
        category = payload["categories"].get(key, {})
        if not isinstance(category, dict):
            category = {}
        successes = self._nonnegative_int(category.get("successes", 0))
        failures = self._nonnegative_int(category.get("failures", 0))
        observations = successes + failures
        failure_rate = round(failures / observations, 4) if observations else 0.0
        force_readback = bool(
            self._nonnegative_int(category.get("symbol_failures", 0))
            or (failures and (observations < 10 or failure_rate >= 0.1))
        )

        preferred_input_format = input_format
        best_rate = successes / observations if observations else -1.0
        for candidate in payload["categories"].values():
            if not isinstance(candidate, dict):
                continue
            if candidate.get("features") != list(features):
                continue
            candidate_successes = self._nonnegative_int(candidate.get("successes", 0))
            candidate_failures = self._nonnegative_int(candidate.get("failures", 0))
            candidate_observations = candidate_successes + candidate_failures
            if candidate_observations < 3:
                continue
            candidate_rate = candidate_successes / candidate_observations
            if candidate_rate > best_rate:
                best_rate = candidate_rate
                preferred_input_format = str(candidate.get("input_format", input_format))

        confidence = "none"
        if observations >= 10:
            confidence = "high"
        elif observations >= 3:
            confidence = "medium"
        elif observations:
            confidence = "low"
        return {
            "category": key,
            "observations": observations,
            "successes": successes,
            "failures": failures,
            "failure_rate": failure_rate,
            "confidence": confidence,
            "force_live_readback": force_readback,
            "preferred_input_format": preferred_input_format,
        }

    def record_many(self, outcomes: list[dict[str, Any]]) -> None:
        if not outcomes:
            return
        with self._lock:
            payload = self._load()
            categories = payload["categories"]
            now = datetime.now(UTC).isoformat()
            for outcome in outcomes:
                input_format = str(outcome["input_format"])
                features = tuple(sorted(str(item) for item in outcome["features"]))
                key = self.category_key(input_format, features)
                category = categories.setdefault(
                    key,
                    {
                        "input_format": input_format,
                        "features": list(features),
                        "successes": 0,
                        "failures": 0,
                        "readback_successes": 0,
                        "symbol_failures": 0,
                        "total_duration_ms": 0.0,
                        "last_error_code": "",
                        "last_observed_at": "",
                    },
                )
                if not isinstance(category, dict):
                    category = {
                        "input_format": input_format,
                        "features": list(features),
                    }
                    categories[key] = category
                category["successes"] = self._nonnegative_int(category.get("successes", 0))
                category["failures"] = self._nonnegative_int(category.get("failures", 0))
                category["readback_successes"] = self._nonnegative_int(
                    category.get("readback_successes", 0)
                )
                category["symbol_failures"] = self._nonnegative_int(
                    category.get("symbol_failures", 0)
                )
                success = bool(outcome["success"])
                category["successes" if success else "failures"] += 1
                if success and bool(outcome.get("readback_verified", False)):
                    category["readback_successes"] += 1
                error_code = str(outcome.get("error_code", ""))
                if not success:
                    category["last_error_code"] = error_code
                if error_code == "ADVANCED_SYMBOL_DROPPED":
                    category["symbol_failures"] += 1
                category["total_duration_ms"] = round(
                    self._nonnegative_float(category.get("total_duration_ms", 0.0))
                    + self._nonnegative_float(outcome.get("duration_ms", 0.0)),
                    3,
                )
                category["last_observed_at"] = now
                payload["observation_count"] = (
                    self._nonnegative_int(payload.get("observation_count", 0)) + 1
                )

            if len(categories) > self.MAX_CATEGORIES:
                ordered = sorted(
                    categories.items(),
                    key=lambda item: str(item[1].get("last_observed_at", "")),
                )
                for key, _category in ordered[: len(categories) - self.MAX_CATEGORIES]:
                    categories.pop(key, None)
            self._write(payload)

    def inspect(self) -> dict[str, Any]:
        with self._lock:
            payload = self._load()
        categories: list[dict[str, Any]] = []
        for key, category in sorted(payload["categories"].items()):
            if not isinstance(category, dict):
                continue
            successes = self._nonnegative_int(category.get("successes", 0))
            failures = self._nonnegative_int(category.get("failures", 0))
            observations = successes + failures
            categories.append(
                {
                    "category": key,
                    "input_format": category.get("input_format", ""),
                    "features": category.get("features", []),
                    "observations": observations,
                    "successes": successes,
                    "failures": failures,
                    "failure_rate": round(failures / observations, 4) if observations else 0.0,
                    "readback_successes": self._nonnegative_int(
                        category.get("readback_successes", 0)
                    ),
                    "symbol_failures": self._nonnegative_int(category.get("symbol_failures", 0)),
                    "average_duration_ms": round(
                        self._nonnegative_float(category.get("total_duration_ms", 0.0))
                        / observations,
                        3,
                    )
                    if observations
                    else 0.0,
                    "last_error_code": category.get("last_error_code", ""),
                    "last_observed_at": category.get("last_observed_at", ""),
                }
            )
        return {
            "schema_version": self.SCHEMA_VERSION,
            "privacy": payload["privacy"],
            "observation_count": self._nonnegative_int(payload.get("observation_count", 0)),
            "category_count": len(categories),
            "categories": categories,
            "path_exposed": False,
        }


class StructureLearningStore:
    """Bounded local evidence for adaptive Word structure and property scans.

    The store receives only fixed collection/property names, native enum
    values, probe outcomes and timing. It never receives property values,
    document counts, content, paths, owners, handles or document-derived
    identifiers.
    """

    SCHEMA_VERSION = 1
    MAX_COLLECTIONS = 64
    MAX_KNOWN_TYPES = 256
    MAX_PROPERTIES = 512
    MAX_FILE_BYTES = 1_000_000

    def __init__(self, path: Path):
        self.path = path
        self._lock = threading.RLock()

    @classmethod
    def _empty(cls) -> dict[str, Any]:
        return {
            "schema_version": cls.SCHEMA_VERSION,
            "privacy": (
                "No document content, counts, paths, owners, handles or "
                "document-derived identifiers are stored."
            ),
            "observation_count": 0,
            "total_duration_ms": 0.0,
            "inspection_observation_count": 0,
            "total_inspection_duration_ms": 0.0,
            "collections": {},
        }

    def _load(self) -> dict[str, Any]:
        if not self.path.is_file():
            return self._empty()
        try:
            if self.path.stat().st_size > self.MAX_FILE_BYTES:
                return self._empty()
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError):
            return self._empty()
        if (
            not isinstance(payload, dict)
            or payload.get("schema_version") != self.SCHEMA_VERSION
            or not isinstance(payload.get("collections"), dict)
        ):
            return self._empty()
        return payload

    def _write(self, payload: dict[str, Any]) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_name(
            f".{self.path.name}.{os.getpid()}.{threading.get_ident()}.tmp"
        )
        try:
            temporary.write_text(
                json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
                encoding="utf-8",
            )
            with suppress(OSError):
                os.chmod(temporary, 0o600)
            temporary.replace(self.path)
        finally:
            with suppress(OSError):
                temporary.unlink()

    @staticmethod
    def _collection_name(value: Any) -> str:
        name = str(value)
        if (
            not name
            or len(name) > 64
            or any(character not in "abcdefghijklmnopqrstuvwxyz0123456789_" for character in name)
        ):
            raise ValueError("Invalid structure-learning collection name")
        return name

    @staticmethod
    def _property_name(value: Any) -> str:
        name = str(value)
        if (
            not name
            or len(name) > 96
            or any(character not in "abcdefghijklmnopqrstuvwxyz0123456789_." for character in name)
        ):
            raise ValueError("Invalid structure-learning property name")
        return name

    @staticmethod
    def _native_types(values: Any) -> list[int]:
        if not isinstance(values, (list, tuple, set)):
            return []
        native_types: set[int] = set()
        for value in values:
            try:
                parsed = int(value)
            except (TypeError, ValueError, OverflowError):
                continue
            if -32_768 <= parsed <= 32_767:
                native_types.add(parsed)
        return sorted(native_types)

    def recommendation(self, collection_names: tuple[str, ...]) -> dict[str, Any]:
        normalized = tuple(sorted({self._collection_name(name) for name in collection_names}))[
            : self.MAX_COLLECTIONS
        ]
        with self._lock:
            payload = self._load()
        recommendations: dict[str, dict[str, Any]] = {}
        collections = payload["collections"]
        for name in normalized:
            category = collections.get(name, {})
            if not isinstance(category, dict):
                category = {}
            present_observations = EquationLearningStore._nonnegative_int(
                category.get("present_observations", 0)
            )
            scan_observations = EquationLearningStore._nonnegative_int(
                category.get("scan_observations", 0)
            )
            next_scan_presence = max(
                1,
                EquationLearningStore._nonnegative_int(category.get("next_scan_presence", 1)),
            )
            last_scan_clean = bool(category.get("last_scan_clean", True))
            scan_due = present_observations + 1 >= next_scan_presence or not last_scan_clean
            confidence = "none"
            if scan_observations >= 8:
                confidence = "high"
            elif scan_observations >= 3:
                confidence = "medium"
            elif scan_observations:
                confidence = "low"
            recommendations[name] = {
                "scan_due_on_next_presence": scan_due,
                "present_observations": present_observations,
                "scan_observations": scan_observations,
                "next_scan_presence": next_scan_presence,
                "last_scan_clean": last_scan_clean,
                "known_types": self._native_types(category.get("known_types", [])),
                "confidence": confidence,
            }
        return {
            "adaptive": True,
            "collections": recommendations,
            "content_used": False,
            "path_exposed": False,
        }

    def property_recommendation(
        self,
        collection_name: str,
        property_names: tuple[str, ...],
    ) -> dict[str, Any]:
        """Recommend which fixed COM properties should be probed next.

        A property that has ever succeeded remains enabled because callers need
        its current value. A property that repeatedly fails is retried at
        inspection observations 1, 2, 4, 8 and so on.
        """

        collection = self._collection_name(collection_name)
        normalized = tuple(sorted({self._property_name(name) for name in property_names}))[
            : self.MAX_PROPERTIES
        ]
        with self._lock:
            payload = self._load()
        category = payload["collections"].get(collection, {})
        if not isinstance(category, dict):
            category = {}
        inspection_observations = EquationLearningStore._nonnegative_int(
            category.get("inspection_observations", 0)
        )
        learned_properties = category.get("properties", {})
        if not isinstance(learned_properties, dict):
            learned_properties = {}
        recommendations: dict[str, dict[str, Any]] = {}
        for name in normalized:
            learned = learned_properties.get(name, {})
            if not isinstance(learned, dict):
                learned = {}
            probes = EquationLearningStore._nonnegative_int(learned.get("probe_observations", 0))
            successes = EquationLearningStore._nonnegative_int(learned.get("successful_reads", 0))
            failures = EquationLearningStore._nonnegative_int(learned.get("failed_reads", 0))
            next_probe = max(
                1,
                EquationLearningStore._nonnegative_int(learned.get("next_probe_inspection", 1)),
            )
            supported = successes > 0
            due = supported or inspection_observations + 1 >= next_probe
            status = "supported" if supported else ("unavailable" if probes else "unknown")
            recommendations[name] = {
                "probe_due_on_next_inspection": due,
                "status": status,
                "probe_observations": probes,
                "successful_reads": successes,
                "failed_reads": failures,
                "next_probe_inspection": next_probe,
            }
        return {
            "adaptive": True,
            "collection": collection,
            "inspection_observations": inspection_observations,
            "properties": recommendations,
            "property_values_used": False,
            "content_used": False,
            "path_exposed": False,
        }

    def record_map(
        self,
        observations: dict[str, dict[str, Any]],
        *,
        duration_ms: float,
    ) -> None:
        if not observations:
            return
        normalized: dict[str, dict[str, Any]] = {}
        for raw_name, observation in list(observations.items())[: self.MAX_COLLECTIONS]:
            name = self._collection_name(raw_name)
            if not isinstance(observation, dict):
                raise ValueError("Invalid structure-learning observation")
            normalized[name] = observation
        with self._lock:
            payload = self._load()
            collections = payload["collections"]
            now = datetime.now(UTC).isoformat()
            for name, observation in normalized.items():
                category = collections.setdefault(
                    name,
                    {
                        "observations": 0,
                        "present_observations": 0,
                        "scan_observations": 0,
                        "scan_failures": 0,
                        "known_types": [],
                        "next_scan_presence": 1,
                        "last_scan_clean": True,
                        "last_observed_at": "",
                    },
                )
                if not isinstance(category, dict):
                    category = {}
                    collections[name] = category
                category["observations"] = (
                    EquationLearningStore._nonnegative_int(category.get("observations", 0)) + 1
                )
                present = bool(observation.get("present", False))
                present_observations = EquationLearningStore._nonnegative_int(
                    category.get("present_observations", 0)
                )
                if present:
                    present_observations += 1
                category["present_observations"] = present_observations
                scanned = bool(observation.get("scanned", False))
                if scanned:
                    category["scan_observations"] = (
                        EquationLearningStore._nonnegative_int(category.get("scan_observations", 0))
                        + 1
                    )
                    read_errors = EquationLearningStore._nonnegative_int(
                        observation.get("read_errors", 0)
                    )
                    clean = read_errors == 0 and not bool(observation.get("truncated", False))
                    category["last_scan_clean"] = clean
                    if not clean:
                        category["scan_failures"] = (
                            EquationLearningStore._nonnegative_int(category.get("scan_failures", 0))
                            + 1
                        )
                    known_types = set(self._native_types(category.get("known_types", [])))
                    known_types.update(self._native_types(observation.get("types", [])))
                    category["known_types"] = sorted(known_types)[: self.MAX_KNOWN_TYPES]
                    category["next_scan_presence"] = (
                        max(present_observations + 1, present_observations * 2)
                        if clean
                        else present_observations + 1
                    )
                category["last_observed_at"] = now

            if len(collections) > self.MAX_COLLECTIONS:
                ordered = sorted(
                    collections.items(),
                    key=lambda item: str(item[1].get("last_observed_at", "")),
                )
                for key, _category in ordered[: len(collections) - self.MAX_COLLECTIONS]:
                    collections.pop(key, None)
            payload["observation_count"] = (
                EquationLearningStore._nonnegative_int(payload.get("observation_count", 0)) + 1
            )
            payload["total_duration_ms"] = round(
                EquationLearningStore._nonnegative_float(payload.get("total_duration_ms", 0.0))
                + EquationLearningStore._nonnegative_float(duration_ms),
                3,
            )
            self._write(payload)

    def record_inspection(
        self,
        collection_name: str,
        property_outcomes: dict[str, dict[str, Any]],
        *,
        duration_ms: float,
    ) -> None:
        """Record aggregate property-read outcomes without receiving values."""

        if not property_outcomes:
            return
        collection = self._collection_name(collection_name)
        normalized: dict[str, dict[str, Any]] = {}
        for raw_name, outcome in list(property_outcomes.items())[: self.MAX_PROPERTIES]:
            name = self._property_name(raw_name)
            if not isinstance(outcome, dict):
                raise ValueError("Invalid structure property observation")
            normalized[name] = {
                "attempted": bool(outcome.get("attempted", False)),
                "successful_reads": EquationLearningStore._nonnegative_int(
                    outcome.get("successful_reads", 0)
                ),
                "failed_reads": EquationLearningStore._nonnegative_int(
                    outcome.get("failed_reads", 0)
                ),
            }
        with self._lock:
            payload = self._load()
            collections = payload["collections"]
            now = datetime.now(UTC).isoformat()
            category = collections.setdefault(collection, {})
            if not isinstance(category, dict):
                category = {}
                collections[collection] = category
            inspection_observations = (
                EquationLearningStore._nonnegative_int(category.get("inspection_observations", 0))
                + 1
            )
            category["inspection_observations"] = inspection_observations
            properties = category.setdefault("properties", {})
            if not isinstance(properties, dict):
                properties = {}
                category["properties"] = properties
            for name, outcome in normalized.items():
                learned = properties.setdefault(
                    name,
                    {
                        "probe_observations": 0,
                        "successful_reads": 0,
                        "failed_reads": 0,
                        "next_probe_inspection": 1,
                        "last_probe_clean": True,
                        "last_observed_at": "",
                    },
                )
                if not isinstance(learned, dict):
                    learned = {}
                    properties[name] = learned
                if outcome["attempted"]:
                    learned["probe_observations"] = (
                        EquationLearningStore._nonnegative_int(learned.get("probe_observations", 0))
                        + 1
                    )
                    successful_reads = outcome["successful_reads"]
                    failed_reads = outcome["failed_reads"]
                    learned["successful_reads"] = (
                        EquationLearningStore._nonnegative_int(learned.get("successful_reads", 0))
                        + successful_reads
                    )
                    learned["failed_reads"] = (
                        EquationLearningStore._nonnegative_int(learned.get("failed_reads", 0))
                        + failed_reads
                    )
                    clean = successful_reads > 0 and failed_reads == 0
                    learned["last_probe_clean"] = clean
                    learned["next_probe_inspection"] = (
                        inspection_observations + 1
                        if successful_reads > 0
                        else max(
                            inspection_observations + 1,
                            inspection_observations * 2,
                        )
                    )
                    learned["last_observed_at"] = now
            if len(properties) > self.MAX_PROPERTIES:
                ordered = sorted(
                    properties.items(),
                    key=lambda item: str(item[1].get("last_observed_at", "")),
                )
                for key, _property in ordered[: len(properties) - self.MAX_PROPERTIES]:
                    properties.pop(key, None)
            category["last_observed_at"] = now
            payload["inspection_observation_count"] = (
                EquationLearningStore._nonnegative_int(
                    payload.get("inspection_observation_count", 0)
                )
                + 1
            )
            payload["total_inspection_duration_ms"] = round(
                EquationLearningStore._nonnegative_float(
                    payload.get("total_inspection_duration_ms", 0.0)
                )
                + EquationLearningStore._nonnegative_float(duration_ms),
                3,
            )
            self._write(payload)

    def inspect(self) -> dict[str, Any]:
        with self._lock:
            payload = self._load()
        categories: list[dict[str, Any]] = []
        for raw_name, category in sorted(payload["collections"].items()):
            try:
                name = self._collection_name(raw_name)
            except ValueError:
                continue
            if not isinstance(category, dict):
                continue
            observations = EquationLearningStore._nonnegative_int(category.get("observations", 0))
            learned_properties = category.get("properties", {})
            if not isinstance(learned_properties, dict):
                learned_properties = {}
            properties: list[dict[str, Any]] = []
            for raw_property, learned in sorted(learned_properties.items()):
                try:
                    property_name = self._property_name(raw_property)
                except ValueError:
                    continue
                if not isinstance(learned, dict):
                    continue
                successful_reads = EquationLearningStore._nonnegative_int(
                    learned.get("successful_reads", 0)
                )
                properties.append(
                    {
                        "property": property_name,
                        "status": "supported" if successful_reads else "unavailable",
                        "probe_observations": EquationLearningStore._nonnegative_int(
                            learned.get("probe_observations", 0)
                        ),
                        "successful_reads": successful_reads,
                        "failed_reads": EquationLearningStore._nonnegative_int(
                            learned.get("failed_reads", 0)
                        ),
                        "next_probe_inspection": max(
                            1,
                            EquationLearningStore._nonnegative_int(
                                learned.get("next_probe_inspection", 1)
                            ),
                        ),
                        "last_probe_clean": bool(learned.get("last_probe_clean", True)),
                        "last_observed_at": learned.get("last_observed_at", ""),
                    }
                )
            categories.append(
                {
                    "collection": name,
                    "observations": observations,
                    "inspection_observations": EquationLearningStore._nonnegative_int(
                        category.get("inspection_observations", 0)
                    ),
                    "present_observations": EquationLearningStore._nonnegative_int(
                        category.get("present_observations", 0)
                    ),
                    "scan_observations": EquationLearningStore._nonnegative_int(
                        category.get("scan_observations", 0)
                    ),
                    "scan_failures": EquationLearningStore._nonnegative_int(
                        category.get("scan_failures", 0)
                    ),
                    "known_types": self._native_types(category.get("known_types", [])),
                    "next_scan_presence": max(
                        1,
                        EquationLearningStore._nonnegative_int(
                            category.get("next_scan_presence", 1)
                        ),
                    ),
                    "last_scan_clean": bool(category.get("last_scan_clean", True)),
                    "last_observed_at": category.get("last_observed_at", ""),
                    "properties": properties,
                }
            )
        observation_count = EquationLearningStore._nonnegative_int(
            payload.get("observation_count", 0)
        )
        inspection_observation_count = EquationLearningStore._nonnegative_int(
            payload.get("inspection_observation_count", 0)
        )
        return {
            "schema_version": self.SCHEMA_VERSION,
            "privacy": payload.get("privacy", self._empty()["privacy"]),
            "observation_count": observation_count,
            "collection_count": len(categories),
            "average_duration_ms": round(
                EquationLearningStore._nonnegative_float(payload.get("total_duration_ms", 0.0))
                / observation_count,
                3,
            )
            if observation_count
            else 0.0,
            "inspection_observation_count": inspection_observation_count,
            "average_inspection_duration_ms": round(
                EquationLearningStore._nonnegative_float(
                    payload.get("total_inspection_duration_ms", 0.0)
                )
                / inspection_observation_count,
                3,
            )
            if inspection_observation_count
            else 0.0,
            "collections": categories,
            "content_stored": False,
            "document_counts_stored": False,
            "property_values_stored": False,
            "path_exposed": False,
        }
