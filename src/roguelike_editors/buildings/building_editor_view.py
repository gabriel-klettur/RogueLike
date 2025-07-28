import pygame
from roguelike_editors.buildings.tools.default_tool_view import DefaultToolView

from roguelike_editors.buildings.tools.split_tool_view   import SplitToolView
from roguelike_editors.buildings.tools.z_tool_view       import ZToolView

from roguelike_editors.buildings.picker.picker_view      import PickerView
from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, CLR_HOVER, CLR_SELECTION

class BuildingEditorView:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state
        self.default_view  = DefaultToolView(state, editor_state)

        self.split_view    = SplitToolView(state, editor_state)
        self.z_bottom_view = ZToolView(state, editor_state, target="bottom")
        self.z_top_view    = ZToolView(state, editor_state, target="top")
                
        self.picker_view = PickerView(editor_state)

    def _render_building_collision_overlay(self, screen, camera, building):
        from roguelike_engine.config.config_tiles import TILE_SIZE
        cell_w, cell_h = camera.scale((TILE_SIZE, TILE_SIZE))
        for ry, row in enumerate(building.collision_map):
            for cx, val in enumerate(row):
                if val == "#":
                    wx = building.x + cx * TILE_SIZE
                    wy = building.y + ry * TILE_SIZE
                    sx, sy = camera.apply((wx, wy))
                    overlay = pygame.Surface((cell_w, cell_h), pygame.SRCALPHA)
                    overlay.fill((255, 0, 0, 100))
                    screen.blit(overlay, (sx, sy))

    def _render_collision_picker(self, screen):
        """Render collision brush picker (# solid, . walk)"""
        options = [("#", "Solid"), (".", "Walk")]
        w = len(options) * (THUMB + PAD) + PAD
        label_font = pygame.font.SysFont("Arial", 14)
        char_font = pygame.font.SysFont("Arial", THUMB)
        h = THUMB + PAD + label_font.get_height() + PAD
        mouse_pos = pygame.mouse.get_pos()
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        surf.fill((20, 20, 20, 235))
        sw, sh = screen.get_size()
        if self.editor.collision_picker_pos is None:
            px = 0
            py = 0
            self.editor.collision_picker_pos = (px, py)
        else:
            px, py = self.editor.collision_picker_pos
        self.editor.collision_picker_panel_size = (w, h)
        self.editor.collision_picker_rects.clear()
        for i, (ch, label) in enumerate(options):
            x = PAD + i * (THUMB + PAD)
            y = PAD
            color = (255, 0, 0) if ch == "#" else (200, 200, 200)
            text_surf = char_font.render(ch, True, color)
            surf.blit(text_surf, (x + (THUMB - text_surf.get_width()) // 2,
                                  y + (THUMB - text_surf.get_height()) // 2))
            abs_rect = pygame.Rect(px + x, py + y, THUMB, THUMB)
            self.editor.collision_picker_rects[ch] = abs_rect
            # Mostrar selección
            if abs_rect.collidepoint(mouse_pos):
                pygame.draw.rect(surf, CLR_HOVER, (x, y, THUMB, THUMB), 3)
            elif self.editor.collision_choice == ch:
                pygame.draw.rect(surf, CLR_SELECTION, (x, y, THUMB, THUMB), 3)
            lbl_surf = label_font.render(label, True, (255, 255, 255))
            surf.blit(lbl_surf, (x + (THUMB - lbl_surf.get_width()) // 2,
                                 y + THUMB + PAD))
        screen.blit(surf, (px, py))

    def render(self, screen, camera, buildings):
        if not self.editor.active:
            return

        # Collision brush mode: persist active collision building until exit or scroll
        if self.editor.current_tool == 'collision_brush':
            target = getattr(self.editor, 'collision_active_building', None)
            if target and getattr(target, 'collision_map', None):
                self._render_building_collision_overlay(screen, camera, target)
                x, y = camera.apply((target.x, target.y))
                w, h = camera.scale(target.image.get_size())
                pygame.draw.rect(screen, (0, 255, 255), (x, y, w, h), 4)
            if self.editor.collision_picker_open:
                self._render_collision_picker(screen)
            return

        # (Modo normal: renderizado completo con bordes y z-layer)
        if self.editor.picker_active:
            self.picker_view.render(screen, camera)
        for b in buildings:
            # Solo mostrar opciones en el edificio activo (persistente)
            if b != getattr(self.editor, 'active_building', None):
                continue
            x, y = camera.apply((b.x, b.y))
            w, h = camera.scale(b.image.get_size())
            rect = pygame.Rect(x, y, w, h)
            pygame.draw.rect(screen, (0, 255, 255), rect, 4)
            pygame.draw.rect(screen, (255, 255, 255), rect, 1)
            if self.editor.collision_picker_open and getattr(b, 'collision_map', None):
                self._render_building_collision_overlay(screen, camera, b)
            self.default_view.render_reset_handle(screen, b, camera)
            self.split_view.render(screen, b, camera)
            self.z_bottom_view.render(screen, b, camera)
            self.z_top_view.render(screen, b, camera)
        if self.editor.collision_picker_open:
            self._render_collision_picker(screen)