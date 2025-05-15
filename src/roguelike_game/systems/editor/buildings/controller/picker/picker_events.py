# src/roguelike_game/systems/editor/buildings/controller/picker/picker_events.py

# Path: src/roguelike_game/systems/editor/buildings/controller/picker/picker_events.py
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_game.systems.editor.buildings.buildings_editor_config import (
    ICON_BACK, THUMB_SIZE, THUMB_PADDING, NAV_HEIGHT
)

class PickerEventHandler:
    def __init__(self, editor_state, controller, buildings):
        self.editor = editor_state
        self.ctrl = controller
        self.buildings = buildings
        self.back_icon = load_image(ICON_BACK, (NAV_HEIGHT, NAV_HEIGHT))

    def handle(self, ev, camera):
        # 1) ESC: si estoy arrastrando, cancelo drag; si no, cierro picker
        if ev.type == pygame.KEYDOWN and ev.key == pygame.K_ESCAPE:
            if self.editor.dragging_building:
                self.ctrl.stop_drag()
            else:
                self.ctrl.close_picker()  # implementa este método en tu picker controller
            return

        # 2) Clic con botón izquierdo
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            mx, my = ev.pos

            # 2.1) Si estoy arrastrando, suelto sobre el mapa
            if self.editor.dragging_building:
                self.ctrl.place_building((mx, my), camera, self.buildings)
                return

            # 2.2) Flecha “atrás”
            if mx < NAV_HEIGHT and my < NAV_HEIGHT:
                self.ctrl.go_back()
                return

            # 2.3) Thumbnails (zona exacta)
            screen = pygame.display.get_surface()
            sw, sh = screen.get_size()
            cols = max(1, sw // (THUMB_SIZE + THUMB_PADDING))
            rows = (len(self.editor.entries) + cols - 1) // cols
            thumbs_bottom = NAV_HEIGHT + rows * (THUMB_SIZE + THUMB_PADDING)

            if NAV_HEIGHT <= my < thumbs_bottom:
                col = mx // (THUMB_SIZE + THUMB_PADDING)
                row = (my - NAV_HEIGHT) // (THUMB_SIZE + THUMB_PADDING)
                idx = row * cols + col
                if 0 <= idx < len(self.editor.entries):
                    entry = self.editor.entries[idx]
                    if entry.is_dir:
                        self.ctrl.change_dir(entry.path)
                    else:
                        self.ctrl.start_drag(entry)
                return