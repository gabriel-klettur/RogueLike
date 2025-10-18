import logging
logger = logging.getLogger(__name__)
from typing import Any, TYPE_CHECKING

from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.map.map_tool_bar_panel.map_tool_bar_panel_controller import (
    MapToolBarPanelController,
)
from roguelike_engine.zone.zone_controller import ZonesService


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
        self.zones = ZonesService()
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
        Desplaza la zona en el grid global. Delegado a ZonesService (con guardas de centinela).
        """
        self.zones.move_zone(zone_name, dx, dy)

    def duplicate_zone(self) -> str | None:
        """
        Duplica la zona actualmente seleccionada:
          - Crea una nueva clave con sufijo "_copy"
          - Copia ubicación, habitaciones y datos asociados
        """
        sel = self.state.selected_zone
        if not sel:
            return None
        new_key = self.zones.duplicate_zone(sel)
        if not new_key:
            return None
        # Mantener comportamiento anterior: clonar rooms en memoria
        self.map_manager.zone_rooms[new_key] = list(self.map_manager.zone_rooms.get(sel, []))
        # Mantener selección y logs
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
        new_name = self.zones.add_zone_at_tile(tx, ty)
        self._invalidate_map_cache()
        self.map_manager.reload_map()
        self.state.selected_zone = new_name

        # Recalcular offset alineado al grid para logging
        zone_w, zone_h = global_map_settings.zone_size
        offx = (tx // zone_w) * zone_w
        offy = (ty // zone_h) * zone_h
        logger.debug(f"[MapEditor] Added zone '{new_name}' at offset ({offx}, {offy})")
        # Tutorial pulse
        try:
            setattr(self.state, 'tutorial_zone_added_pulse', True)
        except Exception:
            pass
        return new_name

    def delete_zone(self) -> bool:
        """
        Elimina la zona actualmente seleccionada (excepto 'lobby'):
          1. Retira del JSON de zones y persiste.
          2. Borra archivos de colisiones y overlays asociados.
          3. Recarga offsets y mapa, deselecciona la zona.
        """
        sel = self.state.selected_zone
        if not sel:
            return False
        ok = self.zones.delete_zone(sel)
        if not ok:
            return False
        self._invalidate_map_cache()
        # Recargar mapa y limpiar selección
        self.map_manager.reload_map()
        self.state.selected_zone = None
        logger.debug(f"[MapEditor] Removed zone '{sel}'")

        # Tutorial pulse
        try:
            setattr(self.state, 'tutorial_zone_deleted_pulse', True)
        except Exception:
            pass
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

        # Delegar a servicio (aplica guardas y renombra archivos/JSON)
        success = self.zones.rename_zone(old, new)
        if not success:
            logger.debug("[MapEditor] rename aborted by ZonesService")
            return False

        # Actualizar estructuras del editor para reflejar el nuevo nombre
        rooms = self.map_manager.zone_rooms.pop(old, [])
        self.map_manager.zone_rooms[new] = rooms

        tiles = self.map_manager.tiles_by_zone.pop(old, [])
        for tile in tiles:
            tile.zone = new
        self.map_manager.tiles_by_zone[new] = tiles

        logger.debug(f"[MapEditor] Completed rename from '{old}' to '{new}'")
        # Tutorial pulse
        try:
            setattr(self.state, 'tutorial_zone_renamed_pulse', True)
        except Exception:
            pass
        return True

    def save_zones(self) -> None:
        """
        Persiste el mapping zone_offsets en el JSON correspondiente.
        """
        self.zones.save_zones()
        # Tutorial pulse
        try:
            setattr(self.state, 'tutorial_zones_saved_pulse', True)
        except Exception:
            pass

    def load_zones(self) -> None:
        """
        Carga offsets desde JSON, actualiza additional_zones y limpia caché.
        """
        self.zones.load_zones()

    def _invalidate_map_cache(self) -> None:
        try:
            cache_file = self.map_manager.loader.cache_dir / f"map_{self.map_manager.map_name}.pkl"
            cache_file.unlink(missing_ok=True)
        except Exception:
            pass

    # 3. HELPERS PRIVADOS eliminados por delegación al ZonesService