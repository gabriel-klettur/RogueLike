from __future__ import annotations

from typing import Optional, Tuple
from .visuals_geometry import calc_centered_rel as _calc_centered_rel
from .visuals_building import append_building_object_in_world as _append_building_object_in_world
from .spawner_visuals_persistence import persist_spawner_instance_visuals as _persist_spawner_instance_visuals
from .visuals_auto_repair import auto_repair_state_visuals as _auto_repair_state_visuals
from .visuals_preflight import preflight_validate_spawner_visuals as _preflight_validate_spawner_visuals


def calc_centered_rel(local_tile: Tuple[int, int], tpl_entry: Optional[dict], img_path: Optional[str]) -> Tuple[int, int, Optional[Tuple[int, int]]]:
    return _calc_centered_rel(local_tile, tpl_entry, img_path)


def append_building_object_in_world(world, inst_entry: dict, tpl_entry: Optional[dict], img_path: Optional[str]) -> None:
    return _append_building_object_in_world(world, inst_entry, tpl_entry, img_path)


def persist_spawner_instance_visuals(inst_id: Optional[str], visuals: dict, ensure_visible_in_game: bool = True) -> None:
    return _persist_spawner_instance_visuals(inst_id, visuals, ensure_visible_in_game)


def auto_repair_state_visuals(world, eid: int, cfg, inst: dict) -> None:
    """Ensure visuals mapping produces valid Building instances in disk and memory.

    - Creates missing building instances from template_id.
    - Updates cfg.state_visuals and instance visuals mapping.
    - Persists to spawners_instances.json when needed.
    """
    return _auto_repair_state_visuals(world, eid, cfg, inst)


def preflight_validate_spawner_visuals() -> int:
    """Batch-validate and repair spawner visuals across all instances on disk.

    - Ensures that every visuals[*] mapping points to an existing building instance id.
    - Creates missing building instances from template_id with `_is_spawner_visual: true`.
    - Persists updated visuals maps back to spawners_instances.json.

    Returns the number of spawner instances updated.
    """
    return _preflight_validate_spawner_visuals()
