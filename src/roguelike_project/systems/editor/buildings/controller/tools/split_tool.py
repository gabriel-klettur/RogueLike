
import pygame

from src.roguelike_project.systems.editor.buildings.buildings_editor_config import SPLIT_HANDLE_SIZE

class SplitTool:
    """Barra horizontal que separa bottom / top en un building."""
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

    # ---------- Interacción ----------
    def check_handle_click(self, mouse_pos, building):
        cam = self.state.camera
        bx, by = cam.apply((building.x, building.y))
        _, h_scaled = cam.scale(building.image.get_size())
        y_split = by + int(h_scaled * building.split_ratio)

        handle_rect = pygame.Rect(
            bx + self._handle_offset_x(building),
            y_split - SPLIT_HANDLE_SIZE // 2,
            SPLIT_HANDLE_SIZE, SPLIT_HANDLE_SIZE
        )
        return handle_rect.collidepoint(mouse_pos)

    def start_drag(self, building):
        self.editor.split_dragging = True
        self.editor.selected_building = building

    def update_drag(self, mouse_pos):
        if not self.editor.split_dragging or not self.editor.selected_building:
            return

        b = self.editor.selected_building
        cam = self.state.camera
        mx, my = mouse_pos
        bx, by = cam.apply((b.x, b.y))
        _, h_scaled = cam.scale(b.image.get_size())
        # Ratio en coordenadas de pantalla ⇒ mundo
        rel = (my - by) / max(h_scaled, 1)
        b.split_ratio = max(0.05, min(rel, 0.95))

    def stop_drag(self):
        self.editor.split_dragging = False
    
    # ---------- helpers ----------
    def _handle_offset_x(self, building):
        # coloca el handle en el centro del edificio en pantalla
        return (self.state.camera.scale(building.image.get_size())[0] - SPLIT_HANDLE_SIZE) // 2
