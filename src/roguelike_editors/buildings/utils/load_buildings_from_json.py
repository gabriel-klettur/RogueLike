import os
import json
from typing import List
from roguelike_engine.config.config import BUILDINGS_DATA_PATH, BUILDINGS_COLLISIONS_DATA_PATH
from roguelike_engine.z_layer.persistence import extract_z_from_json
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings

from roguelike_engine.buildings.building import Building

import logging
logger = logging.getLogger(__name__)

def _normalize_asset_path(p):
    try:
        if not p or not isinstance(p, str):
            return p
        q = p.replace("\\", "/")
        while '//' in q:
            q = q.replace('//', '/')
        base, ext = os.path.splitext(q)
        if ext:
            q = f"{base}{ext.lower()}"
        return q
    except Exception:
        return p

def _canonicalize_zone(zone: str) -> str:
    """
    Map arbitrary zone label from JSON to the canonical key used in
    global_map_settings.zone_offsets. Performs case-insensitive match and
    normalizes base zones ('lobby', 'dungeon') to lowercase.
    """
    try:
        if not zone or not isinstance(zone, str):
            return zone
        # Respect sentinel value used when an entity is intentionally outside any zone
        if zone.lower() == "no zone":
            return "no zone"
        offsets = getattr(global_map_settings, 'zone_offsets', {}) or {}
        # Exact match first
        if zone in offsets:
            return zone
        low = zone.lower()
        # Normalize known base zones
        if low in ("lobby", "dungeon") and low in offsets:
            return low
        # Case-insensitive lookup among existing keys
        for k in offsets.keys():
            if k.lower() == low:
                return k
        # Fallback: return original and warn
        logger.warning(f"[Buildings] Zone '{zone}' not found in offsets (keys={list(offsets.keys())}). Using as-is; building may be misaligned.")
        return zone
    except Exception:
        return zone

def load_buildings_from_json(
    z_state=None
) -> List:
    """
    Carga edificios desde JSON usando coordenadas relativas.
    - Si `z_state` se proporciona, inyecta la capa Z.
    """
    if not os.path.exists(BUILDINGS_DATA_PATH):
        logger.warning(f"⚠️ Archivo no encontrado: {BUILDINGS_DATA_PATH}")
        return []

    # Cargar colisiones de buildings (soporta esquema legacy y nuevo con secciones 'global', 'instances' y 'by_building_id')
    try:
        with open(BUILDINGS_COLLISIONS_DATA_PATH, 'r', encoding='utf-8-sig') as cf:
            raw_cd = json.load(cf) or {}
    except Exception:
        raw_cd = {}
    try:
        if isinstance(raw_cd, dict) and ("global" in raw_cd or "instances" in raw_cd or "by_building_id" in raw_cd):
            collisions_global = raw_cd.get("global", {}) or {}
            collisions_instances = raw_cd.get("instances", {}) or {}
            collisions_by_id = raw_cd.get("by_building_id", {}) or {}
        else:
            # Legacy plano: todo es global por image_path
            collisions_global = raw_cd if isinstance(raw_cd, dict) else {}
            collisions_instances = {}
            collisions_by_id = {}
    except Exception:
        collisions_global = {}
        collisions_instances = {}
        collisions_by_id = {}

    with open(BUILDINGS_DATA_PATH, "r", encoding="utf-8-sig") as f:
        try:
            data = json.load(f)
        except json.JSONDecodeError as e:
            logger.error(f"❌ Error al leer JSON: {e}")
            return []

    # Auto-asignación de IDs faltantes (persistente y backward-compatible)
    changed_ids = False
    try:
        existing_ids = [int(e.get("id")) for e in data if isinstance(e, dict) and str(e.get("id")).isdigit()]
        next_id = (max(existing_ids) + 1) if existing_ids else 1
        for e in data:
            if "id" not in e or e.get("id") is None or (isinstance(e.get("id"), str) and not str(e.get("id")).isdigit()):
                e["id"] = next_id
                next_id += 1
                changed_ids = True
    except Exception:
        # Si algo falla, no impedimos la carga; simplemente no persistimos
        changed_ids = False

    if changed_ids:
        try:
            with open(BUILDINGS_DATA_PATH, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=4)
            logger.info("[Buildings] IDs auto-asignados y persistidos en buildings_data.json")
        except Exception as _e:
            logger.warning(f"[Buildings] No se pudo persistir IDs auto-asignados: {_e}")

    buildings: List[Building] = []

    for entry in data:
        try:
            #logger.debug(f"📥 Entrada cruda desde JSON: {entry}")

            # Require new JSON structure: assets.idle (no legacy fallbacks)
            _img_path = None
            try:
                assets = entry.get("assets") or {}
                if isinstance(assets, dict):
                    _img_path = _normalize_asset_path(assets.get("idle"))
            except Exception:
                _img_path = None
            if not _img_path:
                raise ValueError(f"[Buildings][loader] Missing required assets.idle (id={entry.get('id')}, zone={entry.get('zone')}, rel=({entry.get('rel_x')},{entry.get('rel_y')}))")

            b = Building(
                rel_x=entry.get("rel_x", 0),
                rel_y=entry.get("rel_y", 0),
                image_path=_img_path,
                solid=entry.get("solid", True),
                scale=tuple(entry["scale"]) if "scale" in entry else None,
                split_ratio=entry.get("split_ratio", 0.5),
                z_bottom=entry.get("z_bottom"),
                z_top=entry.get("z_top"),
            )
            # spawn_id (enlaza con spawner instance)
            try:
                sid = entry.get("spawn_id")
                if sid is not None:
                    setattr(b, "spawn_id", str(sid))
                    setattr(b, "spawner_instance_id", str(sid))
            except Exception:
                pass

            # Inicializar collision_map (deep copy por instancia) asegurando tamaño por CEIL
            # Selección de colisión: preferir instancia por spawn_id; luego por building_id; si no, usar global por idle/image_path
            coll_entry = None
            try:
                sid = getattr(b, "spawn_id", None)
                if sid and sid in collisions_instances:
                    coll_entry = collisions_instances.get(sid)
                if not coll_entry:
                    # Por building_id si existe en datos de colisiones
                    bid = entry.get("id")
                    if bid is not None:
                        bid_str = str(bid)
                        if bid_str in collisions_by_id:
                            coll_entry = collisions_by_id.get(bid_str)
                if not coll_entry:
                    coll_entry = collisions_global.get(_img_path) or collisions_global.get(_img_path.replace('/', '\\'))
            except Exception:
                coll_entry = collisions_global.get(_img_path) or collisions_global.get(_img_path.replace('/', '\\'))

            desired_cols = max(1, (b.image.get_width() + TILE_SIZE - 1) // TILE_SIZE)
            desired_rows = max(1, (b.image.get_height() + TILE_SIZE - 1) // TILE_SIZE)
            if coll_entry and "collision" in coll_entry:
                src = [row[:] for row in coll_entry["collision"]]
                # Ajustar a (desired_rows, desired_cols): pad con '.' o truncar si sobra
                cur_rows = len(src)
                cur_cols = len(src[0]) if cur_rows > 0 else 0
                # Normalizar filas a desired_rows
                if cur_rows < desired_rows:
                    # Añadir filas vacías al final
                    for _ in range(desired_rows - cur_rows):
                        src.append(["." for _ in range(cur_cols or desired_cols)])
                    cur_rows = desired_rows
                elif cur_rows > desired_rows:
                    src = src[:desired_rows]
                    cur_rows = desired_rows
                # Normalizar columnas a desired_cols
                if cur_cols < desired_cols:
                    for r in range(cur_rows):
                        # Si no hay columnas, crear la fila desde cero
                        if cur_cols == 0:
                            src[r] = ["." for _ in range(desired_cols)]
                        else:
                            src[r].extend(["."] * (desired_cols - cur_cols))
                elif cur_cols > desired_cols:
                    for r in range(cur_rows):
                        src[r] = src[r][:desired_cols]
                b.collision_map = src
            else:
                # Crear mapa por defecto usando CEIL para cubrir todo el asset
                w = desired_cols
                h = desired_rows
                b.collision_map = [["." for _ in range(w)] for _ in range(h)]

            # Si el edificio es CU y tiene override por instancia, aplicarlo encima del global.
            try:
                if entry.get("collider_scope", "CG") == "CU":
                    ov = entry.get("collision_override")
                    if ov and "collision" in ov:
                        src = [row[:] for row in ov["collision"]]
                        cur_rows = len(src)
                        cur_cols = len(src[0]) if cur_rows > 0 else 0
                        # Normalizar filas a desired_rows
                        if cur_rows < desired_rows:
                            for _ in range(desired_rows - cur_rows):
                                src.append(["." for _ in range(cur_cols or desired_cols)])
                            cur_rows = desired_rows
                        elif cur_rows > desired_rows:
                            src = src[:desired_rows]
                            cur_rows = desired_rows
                        # Normalizar columnas a desired_cols
                        if cur_cols < desired_cols:
                            for r in range(cur_rows):
                                if cur_cols == 0:
                                    src[r] = ["." for _ in range(desired_cols)]
                                else:
                                    src[r].extend(["."] * (desired_cols - cur_cols))
                        elif cur_cols > desired_cols:
                            for r in range(cur_rows):
                                src[r] = src[r][:desired_cols]
                        b.collision_map = src
            except Exception:
                # No impedir carga si algo falla en override
                pass

            # Aplicar capa Z
            if z_state:
                extract_z_from_json(entry, z_state, b)

            # Asignar zona si viene en JSON
            if entry.get("zone"):
                b.zone = _canonicalize_zone(entry["zone"]) 

            # ────────────────────────────────────────────────────────────────
            # Multi-image visual support (backward compatible):
            # If 'images_by_state' is present, configure the model mapping and optional initial state.
            # If 'state_thresholds' present, store them for runtime mapping from damage ratio.
            try:
                images_by_state = entry.get("images_by_state")
                if isinstance(images_by_state, dict) and images_by_state:
                    initial_state = entry.get("initial_visual_state")
                    # Apply mapping first; this will keep current displayed scale
                    b.model.set_images_by_state(images_by_state, initial_state=initial_state)
                thresholds = entry.get("state_thresholds")
                if thresholds is not None:
                    b.model.set_state_thresholds(thresholds if isinstance(thresholds, list) else None)
            except Exception as _e:
                logger.warning(f"[Buildings][loader] Could not apply images_by_state/state_thresholds: {_e}", exc_info=False)

            # Alcance de colisión por edificio (CG/CU)
            try:
                b.collider_scope = entry.get("collider_scope", "CG")
            except Exception:
                pass

            # Asignar ID del edificio al objeto cargado
            try:
                setattr(b, "id", entry.get("id"))
            except Exception:
                pass

            # Restaurar escala original si estaba en JSON
            if entry.get("original_scale"):
                b.original_scale = tuple(entry["original_scale"])

            buildings.append(b)

        except Exception as e:
            logger.error(f"⚠️ Error al crear edificio desde entrada JSON: {e}")

    logger.info(f"[Buildings][Cargando Edificios] {len(buildings)} edificios cargados desde: [{BUILDINGS_DATA_PATH}]")
    return buildings