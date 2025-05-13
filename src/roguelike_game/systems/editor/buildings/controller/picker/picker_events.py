# Path: src/roguelike_game/systems/editor/buildings/controller/picker/picker_events.py

import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_game.systems.editor.buildings.buildings_editor_config import (
    ICON_BACK, THUMB_SIZE, THUMB_PADDING, NAV_HEIGHT
)

class PickerEventHandler:
    def __init__(self, editor_state, controller, buildings):
        self.editor = editor_state
        self.ctrl   = controller
        self.buildings = buildings
        # Cargamos el icono de “atrás”
        self.back_icon = load_image(ICON_BACK, (NAV_HEIGHT, NAV_HEIGHT))

    def handle(self, ev, camera):
        """Procesa eventos cuando picker_active=True."""
        # 1) Tecla Esc: cancelar drag, pero no cerrar el picker
        if ev.type == pygame.KEYDOWN and ev.key == pygame.K_ESCAPE:
            self.ctrl.stop_drag()

        # 2) Clic con botón izquierdo
        elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            mx, my = ev.pos

            # 2.1) Flecha de “atrás” (zona NAV_HEIGHT×NAV_HEIGHT en la esquina)
            if mx < NAV_HEIGHT and my < NAV_HEIGHT:
                self.ctrl.go_back()
                return

            # 2.2) Clic sobre miniaturas: si está en la zona de thumbnails
            if my >= NAV_HEIGHT:
                screen = pygame.display.get_surface()
                sw, sh = screen.get_size()
                cols = max(1, sw // (THUMB_SIZE + THUMB_PADDING))
                row = (my - NAV_HEIGHT) // (THUMB_SIZE + THUMB_PADDING)
                col = mx // (THUMB_SIZE + THUMB_PADDING)
                idx = row * cols + col
                entries = self.editor.entries
                if 0 <= idx < len(entries):
                    entry = entries[idx]
                    if entry.is_dir:
                        self.ctrl.change_dir(entry.path)
                    else:
                        self.ctrl.start_drag(entry)
                return

            # 2.3) Si estamos arrastrando un building, el siguiente clic en cualquier parte lo coloca
            if self.editor.dragging_building:
                self.ctrl.place_building((mx, my), camera, self.buildings)
