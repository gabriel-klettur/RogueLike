# Path: src/roguelike_game/systems/editor/buildings/controller/picker/picker_controller.py

import os
from dataclasses import dataclass
from typing import List
from roguelike_game.systems.editor.buildings.buildings_editor_config import THUMB_SIZE, THUMB_PADDING
from roguelike_game.systems.editor.buildings.model.building_editor_state import BuildingsEditorState

@dataclass
class DirEntry:
    """Representa un directorio o archivo en el picker."""
    name: str
    path: str
    is_dir: bool

class BuildingPickerController:
    def __init__(self, editor_state: BuildingsEditorState, placer_tool):
        self.editor = editor_state
        self.placer = placer_tool
        # Al iniciar, listamos el contenido de la carpeta base
        self.list_entries()

    def list_entries(self):
        """Lee current_dir y actualiza editor.entries."""
        base = self.editor.current_dir
        entries: List[DirEntry] = []
        try:
            for name in os.listdir(base):
                full = os.path.join(base, name)
                if os.path.isdir(full) or name.lower().endswith(('.png','.jpg','.jpeg','.bmp','.gif')):
                    entries.append(DirEntry(name, full, os.path.isdir(full)))
        except FileNotFoundError:
            pass
        # Directorios primero, luego archivos, ambos alfabeticamente
        entries.sort(key=lambda e: (not e.is_dir, e.name.lower()))
        self.editor.entries = entries

    def change_dir(self, new_dir: str):
        """Navega a una subcarpeta."""
        if os.path.isdir(new_dir):
            self.editor.history.append(self.editor.current_dir)
            self.editor.current_dir = new_dir
            self.list_entries()

    def go_back(self):
        """Vuelve a la carpeta anterior, si existe historial."""
        if self.editor.history:
            self.editor.current_dir = self.editor.history.pop()
            self.list_entries()

    def start_drag(self, entry: DirEntry):
        """Inicia el drag de un archivo (solo imágenes)."""
        if not entry.is_dir:
            self.editor.selected_entry = entry
            self.editor.dragging_building = True

    def stop_drag(self):
        """Cancela cualquier drag en curso."""
        self.editor.selected_entry = None
        self.editor.dragging_building = False

    def close_picker(self):
        """Cerrar solo el panel de selección, sin desactivar el editor."""
        self.editor.picker_active = False
        print("📂 Building Picker CLOSED")

    def place_building(self, mouse_pos, camera, buildings):
        """
        Suelta el building en la posición del ratón,
        usando el placer_tool que venga inyectado.
        """
        if self.editor.dragging_building and self.editor.selected_entry:
            # Calculamos coords mundo
            mx, my = mouse_pos
            wx = mx / camera.zoom + camera.offset_x
            wy = my / camera.zoom + camera.offset_y
            # Usamos el placer_tool, pasándole la ruta seleccionada
            # Se asume que placer_tool tiene un método adaptado para recibir path
            self.placer.place_building_at_path(buildings, wx, wy, self.editor.selected_entry.path)
            self.stop_drag()
