
# Path: src/roguelike_game/systems/editor/buildings/controller/tools/split_tool.py
import pygame

from roguelike_game.systems.editor.buildings.buildings_editor_config import SPLIT_HANDLE_SIZE

class SplitTool:
    """Barra horizontal que separa bottom / top en un building."""
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

    # ---------- Interacción ----------
    def check_handle_click(self, mouse_pos, building, camera):        
        bx, by = camera.apply((building.x, building.y))
        _, h_scaled = camera.scale(building.image.get_size())
        y_split = by + int(h_scaled * building.split_ratio)

        handle_rect = pygame.Rect(
            bx + self._handle_offset_x(building, camera),
            y_split - SPLIT_HANDLE_SIZE // 2,
            SPLIT_HANDLE_SIZE, SPLIT_HANDLE_SIZE
        )
        return handle_rect.collidepoint(mouse_pos)

    def start_drag(self, building):
        self.editor.split_dragging = True
        self.editor.selected_building = building

    def update_drag(self, mouse_pos, camera):
        if not self.editor.split_dragging or not self.editor.selected_building:
            return

        b = self.editor.selected_building        
        mx, my = mouse_pos
        bx, by = camera.apply((b.x, b.y))
        _, h_scaled = camera.scale(b.image.get_size())
        # Ratio en coordenadas de pantalla ⇒ mundo
        rel = (my - by) / max(h_scaled, 1)
        b.split_ratio = max(0.05, min(rel, 0.95))

    def stop_drag(self):
        self.editor.split_dragging = False
    
    # ---------- helpers ----------
    def _handle_offset_x(self, building, camera):
        # coloca el handle en el centro del edificio en pantalla
        return (camera.scale(building.image.get_size())[0] - SPLIT_HANDLE_SIZE) // 2