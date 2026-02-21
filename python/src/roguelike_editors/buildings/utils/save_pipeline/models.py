"""Data models for the buildings split save pipeline."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Iterable, List, Tuple


@dataclass(frozen=True)
class SavePaths:
    """File targets used by the split save pipeline."""

    templates_path: str
    instances_path: str


@dataclass(slots=True)
class ExistingData:
    """Snapshot of the current JSON files before saving."""

    templates: List[dict]
    instances: List[dict]


@dataclass(slots=True)
class PipelineContext:
    """Aggregated data shared across pipeline steps."""

    buildings: Iterable[object]
    paths: SavePaths
    existing: ExistingData


@dataclass(slots=True)
class AllocationStats:
    """ID allocation metrics collected during the save pass."""

    preserved_count: int = 0
    reused_spawn_count: int = 0
    reused_position_count: int = 0
    new_assigned_count: int = 0
    preserved_samples: List[int] = field(default_factory=list)
    reused_spawn_samples: List[Tuple[str, int]] = field(default_factory=list)
    reused_position_samples: List[Tuple[str, int]] = field(default_factory=list)
    new_assigned_samples: List[int] = field(default_factory=list)


@dataclass(slots=True)
class SaveResult:
    """Result summary for the split save pipeline."""

    templates_saved: int
    instances_saved: int
    skipped_spawner_visuals: int
    allocation: AllocationStats
