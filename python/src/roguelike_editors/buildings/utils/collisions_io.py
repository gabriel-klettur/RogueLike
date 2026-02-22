import json
import os
from roguelike_engine.config.config import (
    BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
    BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
    BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
)


def load_collisions_sources() -> tuple[dict, dict, dict]:
    def _read_dict(path: str) -> dict:
        try:
            if os.path.exists(path):
                with open(path, "r", encoding="utf-8-sig") as f:
                    d = json.load(f) or {}
                    return d if isinstance(d, dict) else {}
        except Exception:
            return {}
        return {}

    exists_any = any(
        os.path.exists(p)
        for p in (
            BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
            BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
            BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
        )
    )
    if not exists_any:
        return {}, {}, {}

    collisions_global = _read_dict(BUILDINGS_COLLISIONS_BY_IMAGE_PATH)
    collisions_instances = _read_dict(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH)
    collisions_by_id = _read_dict(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH)
    return collisions_global, collisions_instances, collisions_by_id
