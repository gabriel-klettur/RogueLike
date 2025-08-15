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

    # Cargar colisiones de buildings
    try:
        with open(BUILDINGS_COLLISIONS_DATA_PATH, 'r', encoding='utf-8') as cf:
            collisions_data = json.load(cf)
    except Exception:
        collisions_data = {}

    with open(BUILDINGS_DATA_PATH, "r", encoding="utf-8") as f:
        try:
            data = json.load(f)
        except json.JSONDecodeError as e:
            logger.error(f"❌ Error al leer JSON: {e}")
            return []

    buildings: List[Building] = []

    for entry in data:
        try:
            #logger.debug(f"📥 Entrada cruda desde JSON: {entry}")

            b = Building(
                rel_x=entry.get("rel_x", 0),
                rel_y=entry.get("rel_y", 0),
                image_path=entry["image_path"],
                solid=entry.get("solid", True),
                scale=tuple(entry["scale"]) if "scale" in entry else None,
                split_ratio=entry.get("split_ratio", 0.5),
                z_bottom=entry.get("z_bottom"),
                z_top=entry.get("z_top"),
            )

            # Inicializar collision_map (deep copy por instancia) asegurando tamaño por CEIL
            coll_entry = collisions_data.get(entry.get("image_path"))
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

            # Alcance de colisión por edificio (CG/CU)
            try:
                b.collider_scope = entry.get("collider_scope", "CG")
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