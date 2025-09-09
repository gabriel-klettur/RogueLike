import os
import json
import logging
from typing import Optional, Dict, List, Tuple

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config import DATA_DIR
from roguelike_engine.map.model.layer import Layer
import roguelike_engine.map.model.overlay.overlay_manager as overlay_manager
from .zone_model import Zone

logger = logging.getLogger(__name__)


class ZonesService:
    """
    Servicio de orquestación para Zonas del mundo.

    Responsabilidades profesionales:
    - Fábrica y utilidades de consulta (registro) basadas en global_map_settings.
    - CRUD con políticas (evitar operar sobre el centinela 'no zone'/'no-zone').
    - Persistencia de offsets (zones.json) y de overlays multi-capa por zona.
    - Helpers de coordinación (detección de zona por coordenadas y conversión local/global).

    Nota: Este servicio no realiza render ni I/O de mapa fuera de su ámbito
    (no llama a map_manager). Consumidores son responsables de refrescar vistas.
    """

    # ------------------------------
    # Construcción y consulta
    # ------------------------------
    def create_zone(self, name: str, offset: Tuple[int, int]) -> Zone:
        """
        Crea un modelo de zona con tamaño inyectado desde configuración.
        No persiste nada por sí mismo.
        """
        w, h = global_map_settings.zone_size
        return Zone(name=name, offset=offset, width=w, height=h)

    def list_zone_names(self, include_sentinel: bool = False) -> List[str]:
        names = list(global_map_settings.zone_offsets.keys())
        if include_sentinel:
            return names
        return [n for n in names if not self._is_sentinel(n)]

    def zone_at_tile(self, tx: int, ty: int) -> Optional[str]:
        """Devuelve el nombre de la zona que contiene (tx, ty) en coordenadas de tile."""
        w, h = global_map_settings.zone_size
        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if ox <= tx < ox + w and oy <= ty < oy + h:
                return zn
        return None

    def global_to_local(self, tx: int, ty: int, zone_name: str) -> Optional[Tuple[int, int]]:
        """Convierte (tx, ty) globales a locales para 'zone_name' si caen dentro; si no, None."""
        offsets = global_map_settings.zone_offsets
        if zone_name not in offsets:
            return None
        ox, oy = offsets[zone_name]
        w, h = global_map_settings.zone_size
        lx, ly = tx - ox, ty - oy
        if 0 <= lx < w and 0 <= ly < h:
            return lx, ly
        return None

    def move_zone(self, zone_name: str, dx: int, dy: int) -> None:
        """
        Desplaza la zona en el grid global de zonas según (dx, dy).
        Actualiza únicamente el mapping en global_map_settings.zone_offsets.
        Ignora el centinela.
        """
        if self._is_sentinel(zone_name):
            return
        offsets = global_map_settings.zone_offsets
        if zone_name not in offsets:
            return
        x, y = offsets[zone_name]
        offsets[zone_name] = (x + dx, y + dy)

    # ------------------------------
    # CRUD de zonas (con políticas)
    # ------------------------------
    def add_zone_at_tile(self, tx: int, ty: int) -> str:
        """
        Agrega una nueva zona alineada al grid de zonas y la persiste en zones.json.
        Retorna el nombre creado.
        """
        zone_w, zone_h = global_map_settings.zone_size
        offx = (tx // zone_w) * zone_w
        offy = (ty // zone_h) * zone_h
        base_name = f"zone_{offx}_{offy}"

        json_path = self._zones_json_path()
        offsets = self._load_json_or_empty(json_path)

        new_name = self._generate_unique_zone_key(base_name, offsets)
        offsets[new_name] = [offx, offy]
        self._save_json(json_path, offsets)

        # Forzar recálculo de offsets en settings
        global_map_settings.use_zones_json = True
        global_map_settings.__dict__.pop("zone_offsets", None)
        logger.debug(f"[ZonesService] Added zone '{new_name}' at offset ({offx}, {offy})")
        return new_name

    def duplicate_zone(self, name: str) -> Optional[str]:
        """
        Duplica una zona existente, generando un nombre único y persistiendo en zones.json.
        No opera sobre el centinela ni si la zona no existe.
        """
        if not name or self._is_sentinel(name):
            return None
        offsets_cur = dict(global_map_settings.zone_offsets)
        if name not in offsets_cur:
            return None

        json_path = self._zones_json_path()
        offsets = self._load_json_or_empty(json_path) or {
            k: list(v) for k, v in offsets_cur.items() if not self._is_sentinel(k)
        }
        new_name = self._generate_unique_zone_key(name, offsets)
        offsets[new_name] = list(offsets[name]) if name in offsets else list(offsets_cur[name])
        self._save_json(json_path, offsets)
        global_map_settings.__dict__.pop("zone_offsets", None)
        logger.debug(f"[ZonesService] Duplicated zone '{name}' -> '{new_name}'")
        return new_name

    def delete_zone(self, name: str) -> bool:
        """
        Elimina una zona del JSON y borra archivos asociados. No opera sobre 'lobby' ni centinela.
        """
        if not name or name == "lobby" or self._is_sentinel(name):
            return False

        json_path = self._zones_json_path()
        offsets = self._load_json_or_empty(json_path)
        if name not in offsets:
            # Si no está en JSON, intentar reconstruir desde settings actuales
            offsets = {k: list(v) for k, v in global_map_settings.zone_offsets.items() if not self._is_sentinel(k)}
            if name not in offsets:
                return False
        offsets.pop(name, None)
        self._save_json(json_path, offsets)

        # Borrar archivos asociados
        coll_path = os.path.join(DATA_DIR, "map", "collisions", f"{name}.json")
        self._safe_remove_file(coll_path, "[ZonesService.delete_zone]")
        overlay_path = os.path.join(DATA_DIR, "map", "zones", "overlays", f"{name}.overlay.json")
        self._safe_remove_file(overlay_path, "[ZonesService.delete_zone]")

        global_map_settings.__dict__.pop("zone_offsets", None)
        logger.debug(f"[ZonesService] Removed zone '{name}'")
        return True

    def rename_zone(self, old: str, new: str) -> bool:
        """
        Renombra una zona en zones.json y renombra archivos de colisiones/overlays.
        No opera hacia/desde el centinela.
        """
        old = (old or "").strip()
        new = (new or "").strip()
        if not old or not new or old == new:
            return False
        if self._is_sentinel(old) or self._is_sentinel(new):
            return False

        offsets_cur = dict(global_map_settings.zone_offsets)
        if old not in offsets_cur:
            return False
        if new in offsets_cur:
            return False

        json_path = self._zones_json_path()
        offsets = self._load_json_or_empty(json_path) or {
            k: list(v) for k, v in offsets_cur.items() if not self._is_sentinel(k)
        }
        if old not in offsets and old in offsets_cur:
            offsets[old] = list(offsets_cur[old])
        if new in offsets:
            return False

        offsets[new] = offsets.pop(old)
        self._save_json(json_path, offsets)

        # Renombrar archivos asociados
        self._rename_zone_file(os.path.join("map", "collisions"), old, new, suffix=".json", debug_tag="[ZonesService.rename_zone]")
        self._rename_zone_file(os.path.join("map", "zones", "overlays"), old, new, suffix=".overlay.json", debug_tag="[ZonesService.rename_zone]")

        global_map_settings.__dict__.pop("zone_offsets", None)
        logger.debug(f"[ZonesService] Renamed zone '{old}' -> '{new}'")
        return True

    # ------------------------------
    # Overlays multi-capa por zona
    # ------------------------------
    def load_layers(self, zone_name: str) -> Dict[Layer, List[List[str]]]:
        if self._is_sentinel(zone_name):
            return {}
        return overlay_manager.load_layers(zone_name)

    def save_layers(self, zone_name: str, layers: Dict[Layer, List[List[str]]]) -> None:
        if self._is_sentinel(zone_name):
            return
        overlay_manager.save_layers(zone_name, layers)

    def save_zones(self) -> None:
        """Persiste zone_offsets filtrando el centinela en zones.json."""
        json_path = self._zones_json_path()
        filtered = {k: v for k, v in global_map_settings.zone_offsets.items() if not self._is_sentinel(k)}
        self._save_json(json_path, filtered)

    def load_zones(self) -> None:
        """
        Carga offsets desde JSON, actualiza additional_zones para discovery y limpia caché.
        """
        global_map_settings.use_zones_json = True
        json_path = self._zones_json_path()
        try:
            with open(json_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            global_map_settings.additional_zones.clear()
            for k, (x, y) in data.items():
                global_map_settings.additional_zones[k] = (None, None)
            global_map_settings.__dict__.pop("zone_offsets", None)
        except Exception as e:
            logger.debug(f"[ZonesService] load_zones failed to read {json_path}: {e}")

    # ------------------------------
    # Helpers privados
    # ------------------------------
    def _zones_json_path(self) -> str:
        return os.path.join(DATA_DIR, "map", "zones", "zones.json")

    def _is_sentinel(self, name: str) -> bool:
        return name in ("no zone", "no-zone")

    def _load_json_or_empty(self, path: str) -> Dict[str, List[int]]:
        try:
            with open(path, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            return {}

    def _save_json(self, path: str, data: Dict[str, List[int]]) -> None:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)

    def _generate_unique_zone_key(self, base: str, existing: Dict[str, List[int]]) -> str:
        new_key = base
        idx = 1
        while new_key in existing or self._is_sentinel(new_key):
            new_key = f"{base}_{idx}"
            idx += 1
        return new_key

    def _safe_remove_file(self, file_path: str, debug_tag: str = "") -> None:
        if os.path.isfile(file_path):
            try:
                os.remove(file_path)
                logger.debug(f"DEBUG {debug_tag} Removed file {file_path}")
            except Exception as e:
                logger.debug(f"DEBUG {debug_tag} failed to remove file {file_path}: {e}")

    def _rename_zone_file(self, subdir: str, old: str, new: str, suffix: str = ".json", debug_tag: str = "") -> None:
        old_file = os.path.join(DATA_DIR, subdir, f"{old}{suffix}")
        new_file = os.path.join(DATA_DIR, subdir, f"{new}{suffix}")
        if os.path.exists(old_file):
            try:
                os.makedirs(os.path.dirname(new_file), exist_ok=True)
                os.rename(old_file, new_file)
                logger.debug(f"DEBUG {debug_tag} Renamed file {old_file} -> {new_file}")
            except Exception as e:
                logger.debug(f"DEBUG {debug_tag} Failed to rename file {old_file}: {e}")
