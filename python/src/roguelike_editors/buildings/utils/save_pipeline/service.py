"""Saving pipeline utilities for buildings split persistence."""
from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Iterable, Optional

from roguelike_engine.config.config import (
    BUILDINGS_INSTANCES_PATH,
    BUILDINGS_TEMPLATES_PATH,
)
from roguelike_engine.z_layer.persistence import inject_z_into_json

from .allocation import InstanceAllocator
from .audit import audit_changes
from .builders import build_instance_overrides
from .deduplication import deduplicate
from .filters import collect_persistable, get_spawn_id
from .io import ensure_directories, load_existing, write_instances, write_templates
from .models import AllocationStats, ExistingData, PipelineContext, SavePaths, SaveResult
from .placement import extract_position
from .result_logger import log_summary
from .templates import TemplateCatalog
from .world_state import is_blank_world

logger = logging.getLogger(__name__)

__all__ = ["SavePipeline", "run_save_pipeline"]


@dataclass(slots=True)
class PipelineConfig:
    """Configuration options for the buildings save pipeline."""

    z_state: Optional[object] = None
    zone_offsets: Optional[object] = None  # Reserved for future use / compatibility


class SavePipeline:
    """High-level orchestrator for split JSON persistence."""

    def __init__(
        self,
        buildings: Iterable[object],
        templates_path: Optional[str] = None,
        instances_path: Optional[str] = None,
        *,
        config: Optional[PipelineConfig] = None,
    ) -> None:
        self._config = config or PipelineConfig()
        paths = SavePaths(
            templates_path=templates_path or BUILDINGS_TEMPLATES_PATH,
            instances_path=instances_path or BUILDINGS_INSTANCES_PATH,
        )
        self._context = PipelineContext(
            buildings=buildings,
            paths=paths,
            existing=ExistingData(templates=[], instances=[]),
        )

    def run(self) -> SaveResult:
        ensure_directories(self._context.paths)
        self._context.existing = load_existing(self._context.paths)

        catalog = TemplateCatalog(self._context.existing.templates)
        allocator = InstanceAllocator(self._context.existing.instances)

        persistable, skipped_visuals = collect_persistable(self._context.buildings)
        stats = AllocationStats()
        instances: list[dict] = []

        for building in persistable:
            template_id, _ = catalog.get_or_create(building)
            zone, rel_x, rel_y = extract_position(building)
            spawn_id = get_spawn_id(building)
            overrides = build_instance_overrides(building)
            overrides = self._apply_z_state(building, overrides)

            instance_id = allocator.allocate(
                building,
                template_id,
                zone,
                rel_x,
                rel_y,
                spawn_id,
                stats,
            )

            instance_payload = self._build_instance_payload(
                instance_id,
                template_id,
                zone,
                rel_x,
                rel_y,
                spawn_id,
                overrides,
            )
            instances.append(instance_payload)

        templates_out = catalog.as_sorted_list()
        instances_out, removed = deduplicate(instances)
        if removed:
            logger.debug(
                "[Buildings][SaveSplit] Dedup instances by pos/tpl: %s->%s (removed=%s)",
                len(instances),
                len(instances_out),
                removed,
            )

        if is_blank_world():
            logger.info("[Buildings][SaveSplit] Blank world detected; forcing empty instances save.")
            instances_out = []

        write_templates(self._context.paths.templates_path, templates_out)
        write_instances(self._context.paths.instances_path, sorted(instances_out, key=_instance_sort_key))

        audit_changes(self._context.existing.instances, instances_out)

        result = SaveResult(
            templates_saved=len(templates_out),
            instances_saved=len(instances_out),
            skipped_spawner_visuals=skipped_visuals,
            allocation=stats,
        )
        log_summary(result)
        return result

    def _apply_z_state(self, building: object, overrides: Optional[dict]) -> Optional[dict]:
        if self._config.z_state is None:
            return overrides
        try:
            z_payload = inject_z_into_json(building, self._config.z_state)
        except Exception:
            return overrides
        if z_payload is None:
            return overrides
        overrides = overrides or {}
        overrides["z"] = z_payload
        return overrides

    @staticmethod
    def _build_instance_payload(
        instance_id: int,
        template_id: int,
        zone: Optional[str],
        rel_x: int,
        rel_y: int,
        spawn_id: Optional[str],
        overrides: Optional[dict],
    ) -> dict:
        payload = {
            "id": int(instance_id),
            "template_id": int(template_id),
            "zone": zone,
            "rel_x": rel_x,
            "rel_y": rel_y,
        }
        if spawn_id is not None:
            payload["spawn_id"] = spawn_id
        if overrides:
            payload["overrides"] = overrides
        return payload


def run_save_pipeline(
    buildings: Iterable[object],
    *,
    z_state: Optional[object] = None,
    zone_offsets: Optional[object] = None,
    templates_path: Optional[str] = None,
    instances_path: Optional[str] = None,
) -> SaveResult:
    """Execute the split save pipeline and return a structured summary."""

    pipeline = SavePipeline(
        buildings,
        templates_path=templates_path,
        instances_path=instances_path,
        config=PipelineConfig(z_state=z_state, zone_offsets=zone_offsets),
    )
    return pipeline.run()


def _instance_sort_key(entry: dict) -> int:
    try:
        return int(entry.get("id") or 0)
    except Exception:
        return 0
