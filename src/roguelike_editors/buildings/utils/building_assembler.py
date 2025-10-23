from __future__ import annotations
import logging
from typing import List

from roguelike_engine.buildings.building import Building
from roguelike_engine.z_layer.persistence import extract_z_from_json
from roguelike_engine.config.config_tiles import TILE_SIZE

from .asset_paths import normalize_asset_path
from .zones import canonicalize_zone
from .collisions_io import load_collisions_sources
from .collisions_apply import apply_collision_for_building
from .split_io import read_templates, read_instances

logger = logging.getLogger(__name__)


def _build_template_map(templates_raw: list[dict]) -> dict[str, dict]:
    tmap: dict[str, dict] = {}
    for t in templates_raw:
        if not isinstance(t, dict):
            continue
        tid = t.get("id")
        if tid is None:
            try:
                idle = (t.get("assets") or {}).get("idle")
                if idle:
                    tid = normalize_asset_path(idle)
            except Exception:
                pass
        if tid is None:
            continue
        tmap[str(tid)] = dict(t)
    return tmap


def _diagnostics_and_dedup(instances_raw: list[dict]) -> list[dict]:
    # Diagnostics
    try:
        total = len(instances_raw)
        key_counts: dict[str, int] = {}
        root_spawn = 0
        tagged_override = 0
        for e in instances_raw:
            try:
                k = f"{str(e.get('zone') or 'lobby')}|{int(e.get('rel_x') or 0)}|{int(e.get('rel_y') or 0)}|{int(e.get('template_id') or -1)}"
            except Exception:
                k = str(id(e))
            key_counts[k] = key_counts.get(k, 0) + 1
            try:
                if e.get("spawn_id") is not None or e.get("spawner_instance_id") is not None:
                    root_spawn += 1
            except Exception:
                pass
            try:
                ov = e.get("overrides") if isinstance(e, dict) else None
                if isinstance(ov, dict) and bool(ov.get("_is_spawner_visual")):
                    tagged_override += 1
            except Exception:
                pass
        dups = sum(1 for c in key_counts.values() if c > 1)
        logger.debug(
            "[Buildings][split] instances file: total=%s, duplicate_pos_tpl_keys=%s, root_spawn_tags=%s, override_spawner_visual_tags=%s",
            total,
            dups,
            root_spawn,
            tagged_override,
        )
    except Exception:
        pass

    # Best-effort dedup on load to avoid duplicates in memory
    try:
        before = len(instances_raw)
        seen: dict[str, dict] = {}

        def _key(e: dict) -> str:
            try:
                return f"{str(e.get('zone') or 'lobby')}|{int(e.get('rel_x') or 0)}|{int(e.get('rel_y') or 0)}|{int(e.get('template_id') or -1)}"
            except Exception:
                return str(id(e))

        def _score(e: dict) -> tuple:
            has_root_sid = 1 if (e.get("spawn_id") is not None or e.get("spawner_instance_id") is not None) else 0
            ov = e.get("overrides") if isinstance(e, dict) else None
            has_tag = 1 if (isinstance(ov, dict) and bool(ov.get("_is_spawner_visual"))) else 0
            try:
                neg_id = -int(e.get("id") or 0)
            except Exception:
                neg_id = 0
            return (has_root_sid, has_tag, neg_id)

        for e in list(instances_raw):
            k = _key(e)
            cur = seen.get(k)
            if cur is None:
                seen[k] = e
            else:
                if _score(e) > _score(cur):
                    seen[k] = e
        instances_dedup = list(seen.values())
        if len(instances_dedup) != before:
            logger.warning(
                "[Buildings][split] Dedup on load by pos/tpl: %s->%s (removed=%s)",
                before,
                len(instances_dedup),
                before - len(instances_dedup),
            )
        return instances_dedup
    except Exception:
        return instances_raw


def load_from_split(z_state=None) -> List[Building]:
    collisions_global, collisions_instances, collisions_by_id = load_collisions_sources()

    templates_raw = read_templates()
    tmap = _build_template_map(templates_raw)

    instances_raw = read_instances()
    instances = _diagnostics_and_dedup(instances_raw)

    buildings: List[Building] = []
    for inst in instances:
        try:
            if not isinstance(inst, dict):
                continue
            tpl_id = inst.get("template_id")
            if tpl_id is None:
                logger.warning("[Buildings] Instance without template_id: %s", inst)
                continue
            tpl = tmap.get(str(tpl_id))
            if not tpl:
                logger.warning("[Buildings] Missing template id=%s for instance %s", tpl_id, inst)
                continue

            # Merge template with overrides
            entry = dict(tpl)
            overrides = inst.get("overrides")
            if isinstance(overrides, dict):
                try:
                    entry.update(overrides)
                except Exception:
                    pass
            # If this instance is a spawner visual, enforce safe defaults
            is_spawner_visual = False
            try:
                root_flag = bool(inst.get("spawner_visual", False))
            except Exception:
                root_flag = False
            try:
                override_flag = bool((inst.get("overrides") or {}).get("_is_spawner_visual", False))
            except Exception:
                override_flag = False
            is_spawner_visual = bool(root_flag or override_flag)
            if is_spawner_visual:
                # Non-solid, no collisions by default for spawner visuals
                try:
                    entry["solid"] = False
                except Exception:
                    pass
                try:
                    entry["collider_scope"] = "CU"
                except Exception:
                    pass

            # Position/zone from instance
            rel_x = inst.get("rel_x")
            rel_y = inst.get("rel_y")
            if rel_x is None or rel_y is None:
                try:
                    tile = inst.get("tile") or inst.get("local_tile")
                    if tile is not None:
                        tx, ty = int(tile[0]), int(tile[1])
                        rel_x, rel_y = tx * TILE_SIZE, ty * TILE_SIZE
                except Exception:
                    pass
            rel_x = int(rel_x or 0)
            rel_y = int(rel_y or 0)
            entry["rel_x"], entry["rel_y"] = rel_x, rel_y
            if inst.get("zone"):
                entry["zone"] = canonicalize_zone(inst["zone"])  # canonicalize early

            # Bind instance id into merged entry to support per-instance lookups
            try:
                if inst.get("id") is not None:
                    entry["id"] = inst.get("id")
            except Exception:
                pass

            # Ensure assets.idle exists after merge
            assets = entry.get("assets") or {}
            img_idle = normalize_asset_path(assets.get("idle")) if isinstance(assets, dict) else None
            if not img_idle:
                logger.warning("[Buildings] Skipping instance without assets.idle after merge (tpl=%s)", tpl_id)
                continue

            b = Building(
                rel_x=entry.get("rel_x", 0),
                rel_y=entry.get("rel_y", 0),
                image_path=img_idle,
                solid=entry.get("solid", True),
                scale=tuple(entry["scale"]) if "scale" in entry else None,
                split_ratio=entry.get("split_ratio", 0.5),
                z_bottom=entry.get("z_bottom"),
                z_top=entry.get("z_top"),
            )

            # Bind identifiers
            try:
                if inst.get("id") is not None:
                    setattr(b, "id", inst.get("id"))
            except Exception:
                pass
            try:
                sid = inst.get("spawn_id") or inst.get("spawner_instance_id")
                if sid is not None:
                    setattr(b, "spawn_id", str(sid))
                    setattr(b, "spawner_instance_id", str(sid))
            except Exception:
                pass

            # Collision map selection and overrides (skip for spawner visuals)
            if not is_spawner_visual:
                apply_collision_for_building(b, entry, collisions_global, collisions_instances, collisions_by_id)

            # Apply Z-layer
            if z_state:
                extract_z_from_json(entry, z_state, b)

            # Zone assignment
            if entry.get("zone"):
                b.zone = canonicalize_zone(entry["zone"])  # ensure canonical key on object

            # Multi-image visual mapping
            try:
                images_by_state = entry.get("images_by_state")
                if isinstance(images_by_state, dict) and images_by_state:
                    initial_state = entry.get("initial_visual_state")
                    b.model.set_images_by_state(images_by_state, initial_state=initial_state)
                thresholds = entry.get("state_thresholds")
                if thresholds is not None:
                    b.model.set_state_thresholds(thresholds if isinstance(thresholds, list) else None)
            except Exception as _e:
                logger.warning("[Buildings][loader/split] Could not apply images_by_state/state_thresholds: %s", _e, exc_info=False)

            # Collider scope and original scale
            try:
                b.collider_scope = entry.get("collider_scope", "CG")
            except Exception:
                pass
            if entry.get("original_scale"):
                b.original_scale = tuple(entry["original_scale"])  # type: ignore[assignment]

            # Spawner visuals: tag and keep hidden by default until the spawner selects them
            if is_spawner_visual:
                try:
                    setattr(b, "_is_spawner_visual", True)
                    setattr(b, "runtime_hidden", True)
                    # Ensure non-solid at runtime too (in case of late overrides)
                    setattr(b, "solid", False)
                except Exception:
                    pass

            buildings.append(b)
        except Exception as e:
            logger.error("[Buildings][split] Error creating building from instance: %s", e)

    # Final safety: deduplicate by building id in memory
    try:
        seen_ids = set()
        unique = []
        removed = 0
        for b in buildings:
            bid = getattr(b, "id", None)
            if bid is None:
                unique.append(b)
                continue
            if bid in seen_ids:
                removed += 1
                continue
            seen_ids.add(bid)
            unique.append(b)
        if removed:
            logger.warning("[Buildings][split] Removed %s duplicated Building objects by id in memory", removed)
        buildings = unique
    except Exception:
        pass

    logger.info("[Buildings][Cargando Edificios SPLIT] %s edificios (templates+instances)", len(buildings))
    return buildings
