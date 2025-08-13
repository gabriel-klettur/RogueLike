import json
import os
import pygame
import logging
logger = logging.getLogger(__name__)

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config import DATA_DIR
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer
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
        self.toolbar = MapToolBarPanelController(self.state)

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
        if zone_name not in offsets:
            return
        x, y = offsets[zone_name]
        offsets[zone_name] = (x + dx, y + dy)

    def duplicate_zone(self) -> None:
        """
        Duplica la zona actualmente seleccionada:
          - Crea una nueva clave con sufijo "_copy"
          - Copia ubicación, habitaciones y datos asociados
        """
        sel = self.state.selected_zone
        if not sel:
            return

        offsets = global_map_settings.zone_offsets
        new_key = self._generate_unique_zone_key(sel, offsets)
        offsets[new_key] = offsets[sel]

        # Clonar lista de habitaciones y matriz (placeholder)
        self.map_manager.zone_rooms[new_key] = list(self.map_manager.zone_rooms.get(sel, []))
        self.map_manager.matrix = self.map_manager.matrix[:]

    # -------------------------------------------------------------
    # 2. OPERACIONES CRUD SOBRE ZONAS
    # -------------------------------------------------------------
    def add_zone(self, tx: int, ty: int) -> None:
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

        json_path = os.path.join(DATA_DIR, "zones", "zones.json")
        offsets = self._load_json_or_empty(json_path)

        new_name = self._ensure_unique_name(base_name, offsets)
        offsets[new_name] = [offx, offy]
        self._save_json(json_path, offsets)

        # Forzar recarga de offsets y mapa
        global_map_settings.__dict__.pop("zone_offsets", None)
        self.map_manager.reload_map()
        self.state.selected_zone = new_name

        logger.debug(f"DEBUG [Controller.add_zone] Added zone {new_name} at offset ({offx}, {offy})")

    def delete_zone(self) -> None:
        """
        Elimina la zona actualmente seleccionada (excepto 'lobby'):
          1. Retira del JSON de zones y persiste.
          2. Borra archivos de colisiones y overlays asociados.
          3. Recarga offsets y mapa, deselecciona la zona.
        """
        sel = self.state.selected_zone
        if not sel or sel == "lobby":
            return

        json_path = os.path.join(DATA_DIR, "zones", "zones.json")
        offsets = self._load_json_or_empty(json_path)
        offsets.pop(sel, None)
        self._save_json(json_path, offsets)

        # Borrar archivo de colisiones de esta zona
        coll_path = os.path.join(DATA_DIR, "collisions", f"{sel}.json")
        self._safe_remove_file(coll_path, "[Controller.delete_zone]")

        # Borrar archivo de overlay de esta zona
        overlay_path = os.path.join(DATA_DIR, "zones", "overlays", f"{sel}.overlay.json")
        self._safe_remove_file(overlay_path, "[Controller.delete_zone]")

        # Recargar offsets y mapa
        global_map_settings.__dict__.pop("zone_offsets", None)
        self.map_manager.reload_map()
        self.state.selected_zone = None

        logger.debug(f"DEBUG [Controller.delete_zone] Removed zone {sel}")

    def rename_zone(self, old_name: str, new_name: str) -> None:
        """
        Renombra una zona (si old_name existe y new_name no existe):
          1. Actualiza JSON de zones y persiste.
          2. Renombra archivos de colisiones y overlays en disco.
          3. Limpia caché y actualiza map_manager (zone_rooms y tiles_by_zone).
        """
        old = old_name.strip()
        new = new_name.strip()
        logger.debug(f"DEBUG [Controller.rename_zone] called with old_name={old!r}, new_name={new!r}")

        if not old or not new or old == new:
            logger.debug("DEBUG [Controller.rename_zone] abort: invalid or same name")
            return

        # Forzar uso de JSON y obtener offsets actuales
        global_map_settings.use_zones_json = True
        offsets = dict(global_map_settings.zone_offsets)

        if old not in offsets or new in offsets:
            logger.debug("DEBUG [Controller.rename_zone] abort: old_name not in offsets or new_name exists")
            return

        # 1. Actualizar JSON de zones
        offsets[new] = offsets.pop(old)
        json_path = os.path.join(DATA_DIR, "zones", "zones.json")
        self._save_json(json_path, offsets)
        logger.debug(f"DEBUG [Controller.rename_zone] saved zones.json at {json_path}")

        # 2. Renombrar archivos de colisiones y overlays
        self._rename_zone_file("collisions", old, new, "[Controller.rename_zone]")
        self._rename_zone_file(os.path.join("zones", "overlays"), old, new, "[Controller.rename_zone]", suffix=".overlay.json")

        # 3. Limpiar caché y actualizar map_manager
        global_map_settings.__dict__.pop("zone_offsets", None)
        rooms = self.map_manager.zone_rooms.pop(old, [])
        self.map_manager.zone_rooms[new] = rooms

        tiles = self.map_manager.tiles_by_zone.pop(old, [])
        for tile in tiles:
            tile.zone = new
        self.map_manager.tiles_by_zone[new] = tiles

        logger.debug(f"DEBUG [Controller.rename_zone] Completed rename from {old} to {new}")

    def save_zones(self) -> None:
        """
        Persiste el mapping zone_offsets en el JSON correspondiente.
        """
        global_map_settings.use_zones_json = True
        json_path = os.path.join(DATA_DIR, "zones", "zones.json")
        self._save_json(json_path, global_map_settings.zone_offsets)

    def load_zones(self) -> None:
        """
        Carga offsets desde JSON, actualiza additional_zones y limpia caché.
        """
        global_map_settings.use_zones_json = True
        json_path = os.path.join(DATA_DIR, "zones", "zones.json")
        try:
            with open(json_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            global_map_settings.additional_zones.clear()
            for k, (x, y) in data.items():
                global_map_settings.additional_zones[k] = (None, None)
            global_map_settings.__dict__.pop("zone_offsets", None)
        except Exception:
            pass

    # -------------------------------------------------------------
    # 3. HELPERS PRIVADOS DE PERSISTENCIA Y ARCHIVOS
    # -------------------------------------------------------------
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


class MapToolbarController:

    """
    Componente de toolbar para el Map Editor:
      - Botón principal para layers view
      - Botones: agregar zona, borrar zona, pintar tiles, vaciar colliders, pintar colliders
      - Manejo de clics para activar/desactivar modos en el estado del editor
    """

    def __init__(self, editor_state):
        self.editor = editor_state

        # Posicionamiento inicial y layout
        self.x, self.y = 10, 10
        self.size = 64
        self.padding = 8

        # Cargar íconos en un diccionario para ToolbarView
        self.icons: dict[str, pygame.Surface] = self._load_icons()
        # Rects de iconos expuestos por ToolbarView (rellenados en render o bajo demanda)
        self.icon_rects: dict[str, pygame.Rect] = {}

        # Dropdown option rects (seguimos usándolo para la vista de capas)
        self.option_rects: dict[Layer | str, pygame.Rect] = {}

        # Vista que envuelve al ToolbarView compartido
        self.view = MapToolbarView(self)

    def handle_click(self, mouse_pos: tuple[int, int]) -> bool:
        """
        Procesa clics usando los rects provistos por ToolbarView (icon_rects).
        Incluye fallback para pre-render: calcula rects con la misma geometría del widget.
        """
        # Asegurar icon_rects aunque no se haya renderizado aún
        if not self.icon_rects:
            widget = getattr(getattr(self, 'view', None), 'widget', None)
            if widget and getattr(widget, 'icon_rects', None):
                self.icon_rects = dict(widget.icon_rects)
            elif widget:
                # Precalcular rects según la geometría del ToolbarView
                edge = getattr(widget, 'edge_padding', 8)
                panel_pos = widget.panel.pos or (widget.x, widget.y)
                size, pad = widget.size, widget.padding
                self.icon_rects = {}
                for idx, tool_name in enumerate(TOOLS):
                    local = pygame.Rect(edge, edge + idx * (size + pad), size, size)
                    self.icon_rects[tool_name] = local.move(panel_pos)

        # Mapa de handlers por tool
        def _toggle_pair(primary: str, disable: list[str]):
            self._toggle_mode(primary, disable=disable)
            logger.debug(f"[DEBUG][Toolbar] {primary} -> {getattr(self.editor, primary)}")

        for tool_name, rect in self.icon_rects.items():
            if rect and rect.collidepoint(mouse_pos):
                if tool_name == "view_layers":
                    self.editor.layers_view_open = not self.editor.layers_view_open
                    logger.debug(f"[DEBUG][Toolbar] layers_view_open -> {self.editor.layers_view_open}")
                    return True
                if tool_name == "add_zone":
                    _toggle_pair("add_zone_mode", ["delete_zone_mode", "paint_tiles_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "delete_zone":
                    _toggle_pair("delete_zone_mode", ["add_zone_mode", "paint_tiles_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "paint_tiles":
                    _toggle_pair("paint_tiles_mode", ["add_zone_mode", "delete_zone_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "clear_colliders":
                    _toggle_pair("clear_colliders_mode", ["add_zone_mode", "delete_zone_mode", "paint_tiles_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "paint_colliders":
                    _toggle_pair("paint_colliders_mode", ["add_zone_mode", "delete_zone_mode", "paint_tiles_mode", "clear_colliders_mode"])
                    return True

        # Dropdown de capas
        if self.editor.layers_view_open:
            for key, rect in self.option_rects.items():
                if rect and rect.collidepoint(mouse_pos):
                    self._handle_dropdown_selection(key)
                    return True

        return False

    def is_active(self, tool: str) -> bool:
        """
        Indica al ToolbarView si un botón debe mostrarse como activo.
        """
        if tool == "view_layers":
            return bool(self.editor.layers_view_open)
        if tool == "add_zone":
            return bool(getattr(self.editor, "add_zone_mode", False))
        if tool == "delete_zone":
            return bool(getattr(self.editor, "delete_zone_mode", False))
        if tool == "paint_tiles":
            return bool(getattr(self.editor, "paint_tiles_mode", False))
        if tool == "clear_colliders":
            return bool(getattr(self.editor, "clear_colliders_mode", False))
        if tool == "paint_colliders":
            return bool(getattr(self.editor, "paint_colliders_mode", False))
        return False

    def _load_icons(self) -> dict[str, pygame.Surface]:
        """Carga y escala los iconos para el toolbar del editor de mapa."""
        return {
            "view_layers": load_image("assets/ui/layers_view_tool.png", (self.size, self.size)),
            "add_zone": load_image("assets/ui/add_zone.png", (self.size, self.size)),
            "delete_zone": load_image("assets/ui/delete_zone.png", (self.size, self.size)),
            "paint_tiles": load_image("assets/ui/pintar_tiles_zone.png", (self.size, self.size)),
            "clear_colliders": load_image("assets/ui/vaciar_colliders_zone.png", (self.size, self.size)),
            "paint_colliders": load_image("assets/ui/pintar_colliders_zone.png", (self.size, self.size)),
        }

    def _toggle_mode(self, mode_attr: str, disable: list[str] = []) -> None:
        """
        Activa/desactiva la modalidad especificada en el editor_state,
        desactivando cualquier otro mode en la lista 'disable'.
        """
        current = getattr(self.editor, mode_attr)
        setattr(self.editor, mode_attr, not current)
        for other in disable:
            setattr(self.editor, other, False)

    def _handle_dropdown_selection(self, key: Layer | str) -> None:
        """
        Procesa la selección en el dropdown de visibilidad:
          - "show_all" / "hide_all": toggle global
          - Layer: toggle visibilidad de esa capa
          - "buildings": toggle show_buildings
          - "colliders": toggle show_colliders
        """
        if key == "show_all":
            for layer in self.editor.visible_layers:
                self.editor.visible_layers[layer] = True
            self.editor.show_buildings = True
            logger.debug("[DEBUG][Layer View] show_all: all layers visible")

        elif key == "hide_all":
            for layer in self.editor.visible_layers:
                self.editor.visible_layers[layer] = False
            self.editor.show_buildings = False
            logger.debug("[DEBUG][Layer View] hide_all: all layers hidden")

        elif isinstance(key, Layer):
            vl = self.editor.visible_layers
            vl[key] = not vl[key]
            logger.debug(f"[DEBUG][Layer View] {key.name}: {'visible' if vl[key] else 'hidden'}")

        elif key == "buildings":
            self.editor.show_buildings = not self.editor.show_buildings
            logger.debug(f"[DEBUG][Layer View] buildings: {'visible' if self.editor.show_buildings else 'hidden'}")

        elif key == "colliders":
            self.editor.show_colliders = not self.editor.show_colliders
            logger.debug(f"[DEBUG][Layer View] colliders: {'visible' if self.editor.show_colliders else 'hidden'}")