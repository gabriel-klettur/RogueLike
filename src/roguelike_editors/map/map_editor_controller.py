import json
import os
import logging
logger = logging.getLogger(__name__)
from typing import Any, TYPE_CHECKING

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config import DATA_DIR
from roguelike_editors.map.map_tool_bar_panel.map_tool_bar_panel_controller import (
    MapToolBarPanelController,
)


class MapEditorController:
    """
    Lógica de negocio para el Map Editor, organizada en responsabilidades:
      1. Selección y visibilidad de zonas
      2. Operaciones CRUD sobre zonas (añadir, duplicar, mover, borrar, renombrar, cargar/guardar)
      3. Helpers privados para persistencia y archivos en disco
      4. Inicialización de la toolbar
    """

    def __init__(self, state, map_manager):
        self.state = state
        self.map_manager = map_manager
        # Delegate toolbar responsibilities to map_tool_bar_panel package
        # Provide back-reference so tool controllers can invoke map CRUD
        self.toolbar = MapToolBarPanelController(self.state, map_controller=self)

    # -------------------------------------------------------------
    # 1. SELECCIÓN Y VISIBILIDAD DE ZONAS
    # -------------------------------------------------------------
    def select_zone(self, zone_name: str) -> None:
        """Selecciona la zona si existe en el map_manager."""
        if zone_name in self.map_manager.tiles_by_zone:
            self.state.selected_zone = zone_name

    def toggle_hide_zone(self, zone_name: str) -> None:
        """
        Alterna el estado de oculto/visible para la zona indicada,
        esto solo afecta la capa de renderizado, no elimina datos.
        """
        hidden = self.state.hidden_zones
        if zone_name in hidden:
            hidden.remove(zone_name)
        else:
            hidden.add(zone_name)

    def move_zone(self, zone_name: str, dx: int, dy: int) -> None:
        """
        Desplaza la zona en el grid global de zonas según (dx, dy).
        Actualiza únicamente el mapping en global_map_settings.zone_offsets.
        """
        offsets = global_map_settings.zone_offsets
        # Evitar mover el centinela 'no zone'
        if self._is_sentinel_zone(zone_name):
            return
        if zone_name not in offsets:
            return
        x, y = offsets[zone_name]
        offsets[zone_name] = (x + dx, y + dy)

    def duplicate_zone(self) -> str | None:
        """
        Duplica la zona actualmente seleccionada:
          - Crea una nueva clave con sufijo "_copy"
          - Copia ubicación, habitaciones y datos asociados
        """
        sel = self.state.selected_zone
        if not sel:
            return None
        # Evitar duplicar el centinela 'no zone'
        if self._is_sentinel_zone(sel):
            return None

        offsets = global_map_settings.zone_offsets
        new_key = self._generate_unique_zone_key(sel, offsets)
        offsets[new_key] = offsets[sel]

        # Clonar lista de habitaciones y matriz (placeholder)
        self.map_manager.zone_rooms[new_key] = list(self.map_manager.zone_rooms.get(sel, []))
        self.map_manager.matrix = self.map_manager.matrix[:]
        logger.debug(f"[MapEditor] Duplicated zone '{sel}' -> '{new_key}'")
        return new_key

    # -------------------------------------------------------------
    # 2. OPERACIONES CRUD SOBRE ZONAS
    # -------------------------------------------------------------
    def add_zone(self, tx: int, ty: int) -> str:
        """
        Agrega una nueva zona de tamaño zone_size alineada al grid de zonas.
        1. Calcula offset en tiles basado en (tx, ty).
        2. Lee/actualiza JSON de zonas en disco.
        3. Recarga settings y mapa, selecciona la nueva zona.
        """
        zone_w, zone_h = global_map_settings.zone_size
        offx = (tx // zone_w) * zone_w
        offy = (ty // zone_h) * zone_h
        base_name = f"zone_{offx}_{offy}"

        json_path = self._zones_json_path()
        offsets = self._load_json_or_empty(json_path)

        new_name = self._ensure_unique_name(base_name, offsets)
        offsets[new_name] = [offx, offy]
        self._save_json(json_path, offsets)

        # Forzar recarga de offsets y mapa
        global_map_settings.use_zones_json = True
        global_map_settings.__dict__.pop("zone_offsets", None)
        self.map_manager.reload_map()
        self.state.selected_zone = new_name

        logger.debug(f"[MapEditor] Added zone '{new_name}' at offset ({offx}, {offy})")
        return new_name

    def delete_zone(self) -> bool:
        """
        Elimina la zona actualmente seleccionada (excepto 'lobby'):
          1. Retira del JSON de zones y persiste.
          2. Borra archivos de colisiones y overlays asociados.
          3. Recarga offsets y mapa, deselecciona la zona.
        """
        sel = self.state.selected_zone
        if not sel or sel in ("lobby",) or self._is_sentinel_zone(sel):
            return False

        json_path = self._zones_json_path()
        offsets = self._load_json_or_empty(json_path)
        offsets.pop(sel, None)
        self._save_json(json_path, offsets)

        # Borrar archivo de colisiones de esta zona
        coll_path = os.path.join(DATA_DIR, "map", "collisions", f"{sel}.json")
        self._safe_remove_file(coll_path, "[Controller.delete_zone]")

        # Borrar archivo de overlay de esta zona
        overlay_path = os.path.join(DATA_DIR, "map", "zones", "overlays", f"{sel}.overlay.json")
        self._safe_remove_file(overlay_path, "[Controller.delete_zone]")

        # Recargar offsets y mapa
        global_map_settings.__dict__.pop("zone_offsets", None)
        self.map_manager.reload_map()
        self.state.selected_zone = None

        logger.debug(f"[MapEditor] Removed zone '{sel}'")
        return True

    def rename_zone(self, old_name: str, new_name: str) -> bool:
        """
        Renombra una zona (si old_name existe y new_name no existe):
          1. Actualiza JSON de zones y persiste.
          2. Renombra archivos de colisiones y overlays en disco.
          3. Limpia caché y actualiza map_manager (zone_rooms y tiles_by_zone).
        """
        old = old_name.strip()
        new = new_name.strip()
        logger.debug(f"[MapEditor] rename_zone old={old!r} new={new!r}")

        if not old or not new or old == new:
            logger.debug("[MapEditor] rename aborted: invalid or same name")
            return False

        # No permitir renombrar hacia/desde el centinela 'no zone'
        if self._is_sentinel_zone(old) or self._is_sentinel_zone(new):
            logger.debug("[MapEditor] rename aborted: sentinel 'no zone' involved")
            return False

        # Forzar uso de JSON y obtener offsets actuales
        global_map_settings.use_zones_json = True
        offsets = dict(global_map_settings.zone_offsets)

        if old not in offsets or new in offsets:
            logger.debug("[MapEditor] rename aborted: old not found or new already exists")
            return False

        # 1. Actualizar JSON de zones
        offsets[new] = offsets.pop(old)
        json_path = self._zones_json_path()
        self._save_json(json_path, offsets)
        logger.debug(f"[MapEditor] zones.json saved at {json_path}")

        # 2. Renombrar archivos de colisiones y overlays
        self._rename_zone_file(os.path.join("map", "collisions"), old, new, "[Controller.rename_zone]")
        self._rename_zone_file(os.path.join("map", "zones", "overlays"), old, new, "[Controller.rename_zone]", suffix=".overlay.json")

        # 3. Limpiar caché y actualizar map_manager
        global_map_settings.__dict__.pop("zone_offsets", None)
        rooms = self.map_manager.zone_rooms.pop(old, [])
        self.map_manager.zone_rooms[new] = rooms

        tiles = self.map_manager.tiles_by_zone.pop(old, [])
        for tile in tiles:
            tile.zone = new
        self.map_manager.tiles_by_zone[new] = tiles

        logger.debug(f"[MapEditor] Completed rename from '{old}' to '{new}'")
        return True

    def save_zones(self) -> None:
        """
        Persiste el mapping zone_offsets en el JSON correspondiente.
        """
        global_map_settings.use_zones_json = True
        json_path = self._zones_json_path()
        # Filtrar el centinela 'no zone'/'no-zone' para no persistirlo como zona real
        filtered = {k: v for k, v in global_map_settings.zone_offsets.items() if k not in ("no zone", "no-zone")}
        self._save_json(json_path, filtered)

    def load_zones(self) -> None:
        """
        Carga offsets desde JSON, actualiza additional_zones y limpia caché.
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
            # No interrumpir flujo por errores de lectura; registrar a nivel debug
            logger.debug(f"[MapEditor] load_zones failed to read {json_path}: {e}")

    # -------------------------------------------------------------
    # 3. HELPERS PRIVADOS DE PERSISTENCIA Y ARCHIVOS
    # -------------------------------------------------------------
    def _zones_json_path(self) -> str:
        """Devuelve la ruta al archivo principal de zonas (zones.json)."""
        return os.path.join(DATA_DIR, "map", "zones", "zones.json")

    def _is_sentinel_zone(self, name: str) -> bool:
        """True si 'name' corresponde al centinela especial de 'no zone'."""
        return name in ("no zone", "no-zone")

    def _load_json_or_empty(self, path: str) -> dict:
        """
        Abre y parsea JSON en 'path'; si falla, retorna {}.
        """
        try:
            with open(path, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            return {}

    def _save_json(self, path: str, data: dict) -> None:
        """
        Persiste 'data' en formato JSON legible con indentación.
        """
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)

    def _safe_remove_file(self, file_path: str, debug_tag: str = "") -> None:
        """
        Elimina el archivo si existe, imprimiendo debug en caso de éxito o fallo.
        """
        if os.path.isfile(file_path):
            try:
                os.remove(file_path)
                logger.debug(f"DEBUG {debug_tag} Removed file {file_path}")
            except Exception as e:
                logger.debug(f"DEBUG {debug_tag} failed to remove file {file_path}: {e}")

    def _rename_zone_file(self, subdir: str, old: str, new: str, debug_tag: str = "", suffix: str = ".json") -> None:
        """
        Renombra archivo de zona en un subdirectorio específico:
          - subdir: ruta relativa dentro de DATA_DIR
          - old, new: nombres de zona
          - suffix: extensión del archivo (default ".json", usar ".overlay.json" para overlays)
        """
        old_file = os.path.join(DATA_DIR, subdir, f"{old}{suffix}")
        new_file = os.path.join(DATA_DIR, subdir, f"{new}{suffix}")
        if os.path.exists(old_file):
            try:
                os.makedirs(os.path.dirname(new_file), exist_ok=True)
                os.rename(old_file, new_file)
                logger.debug(f"DEBUG {debug_tag} Renamed file {old_file} -> {new_file}")
            except Exception as e:
                logger.debug(f"DEBUG {debug_tag} Failed to rename file {old_file}: {e}")

    def _generate_unique_zone_key(self, base: str, offsets: dict) -> str:
        """
        Genera una clave única a partir de 'base', agregando sufijo _1, _2, ... si existe.
        """
        new_key = base
        idx = 1
        while new_key in offsets:
            new_key = f"{base}_{idx}"
            idx += 1
        return new_key

    def _ensure_unique_name(self, base: str, existing: dict) -> str:
        """
        Versión pública de _generate_unique_zone_key, solo cambia nombre sin afectar offsets.
        """
        return self._generate_unique_zone_key(base, existing)