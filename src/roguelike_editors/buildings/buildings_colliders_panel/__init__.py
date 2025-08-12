import os
import json
from dataclasses import dataclass, field
import pygame

try:
    from roguelike_engine.config.config_tiles import TILE_SIZE
except Exception:
    TILE_SIZE = 32

try:
    from roguelike_engine.config.config import BUILDINGS_COLLISIONS_DATA_PATH
except Exception:
    BUILDINGS_COLLISIONS_DATA_PATH = "data/buildings/collisions.json"

from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, CLR_HOVER, CLR_SELECTION


@dataclass
class BuildingCollidersPanelModel:
    active: bool = False
    picker_open: bool = False
    picker_pos: tuple[int, int] | None = None
    picker_dragging: bool = False
    picker_drag_offset: tuple[int, int] = (0, 0)
    picker_panel_size: tuple[int, int] = (0, 0)
    picker_rects: dict[str, pygame.Rect] = field(default_factory=dict)

    choice: str | None = None  # '#' sólido, '.' caminable
    brush_dragging: bool = False
    active_building = None

    def reset_runtime(self):
        self.picker_dragging = False
        self.brush_dragging = False
        self.picker_rects.clear()


class BuildingCollidersPanelView:
    def __init__(self, state, editor_state, model):
        self.state = state
        self.editor_state = editor_state
        self.model = model

    def _render_building_collision_overlay(self, screen, camera, building):
        if not getattr(building, 'collision_map', None):
            return
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

    def _render_picker(self, screen, editor_view=None):
        options = [("#", "Solid"), (".", "Walk")]
        w = len(options) * (THUMB + PAD) + PAD
        label_font = pygame.font.SysFont("Arial", 14)
        char_font = pygame.font.SysFont("Arial", THUMB)
        h = THUMB + PAD + label_font.get_height() + PAD
        mouse_pos = pygame.mouse.get_pos()
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        surf.fill((20, 20, 20, 235))

        # anchor under title if not manually positioned
        if self.model.picker_pos is None and editor_view is not None:
            try:
                title_rect = getattr(editor_view, '_last_title_rect', None)
                if title_rect is not None:
                    px = int(title_rect.left)
                    py = int(title_rect.bottom + 8)
                else:
                    px, py = 0, 0
            except Exception:
                px, py = 0, 0
            self.model.picker_pos = (px, py)
        else:
            px, py = self.model.picker_pos or (0, 0)

        self.model.picker_panel_size = (w, h)
        self.model.picker_rects.clear()
        for i, (ch, label) in enumerate(options):
            x = PAD + i * (THUMB + PAD)
            y = PAD
            color = (255, 0, 0) if ch == "#" else (200, 200, 200)
            text_surf = char_font.render(ch, True, color)
            surf.blit(text_surf, (x + (THUMB - text_surf.get_width()) // 2,
                                  y + (THUMB - text_surf.get_height()) // 2))
            abs_rect = pygame.Rect(px + x, py + y, THUMB, THUMB)
            self.model.picker_rects[ch] = abs_rect
            # hover/selection
            if abs_rect.collidepoint(mouse_pos):
                pygame.draw.rect(surf, CLR_HOVER, (x, y, THUMB, THUMB), 3)
            elif self.model.choice == ch:
                pygame.draw.rect(surf, CLR_SELECTION, (x, y, THUMB, THUMB), 3)
            lbl_surf = label_font.render(label, True, (255, 255, 255))
            surf.blit(lbl_surf, (x + (THUMB - lbl_surf.get_width()) // 2,
                                 y + THUMB + PAD))
        screen.blit(surf, (px, py))

    def render(self, screen, camera, buildings, editor_view=None):
        if not self.model.active:
            return
        # highlight and overlay for active building
        b = self.model.active_building
        if b and getattr(b, 'collision_map', None):
            self._render_building_collision_overlay(screen, camera, b)
            x, y = camera.apply((b.x, b.y))
            w, h = camera.scale(b.image.get_size())
            pygame.draw.rect(screen, (0, 255, 255), (x, y, w, h), 4)
        if self.model.picker_open:
            self._render_picker(screen, editor_view)


class BuildingCollidersPanelEventHandler:
    def __init__(self, state, editor_state, model):
        self.state = state
        self.editor_state = editor_state
        self.model = model

    def _paint_at_mouse(self, camera, buildings):
        if not self.model.choice:
            return True
        mx, my = pygame.mouse.get_pos()
        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y
        for b in reversed(buildings):
            x_b, y_b = b.x, b.y
            w_img, h_img = b.image.get_size()
            rect = pygame.Rect(x_b, y_b, w_img, h_img)
            if rect.collidepoint(world_x, world_y):
                self.model.active_building = b
                col = int((world_x - x_b) // TILE_SIZE)
                row = int((world_y - y_b) // TILE_SIZE)
                if 0 <= row < len(b.collision_map) and 0 <= col < len(b.collision_map[0]):
                    b.collision_map[row][col] = self.model.choice
                    # invalidate collision tiles cache in model if present
                    try:
                        b.model._collision_tiles_cache = None
                        b.model._collision_tile_objs = None
                    except Exception:
                        pass
                return True
        return False

    def _save_collisions(self, buildings):
        data = {}
        for b in buildings:
            if getattr(b, 'collision_map', None) is None:
                continue
            data[getattr(b, 'image_path', '')] = {
                'width': len(b.collision_map[0]) if b.collision_map else 0,
                'height': len(b.collision_map),
                'collision': b.collision_map,
            }
        os.makedirs(os.path.dirname(BUILDINGS_COLLISIONS_DATA_PATH), exist_ok=True)
        with open(BUILDINGS_COLLISIONS_DATA_PATH, 'w', encoding='utf-8') as cf:
            json.dump(data, cf, indent=4)

    def handle(self, event, camera, buildings) -> bool:
        if not self.model.active:
            return False
        if event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = event.pos
            # Picker interactions
            if self.model.picker_open:
                x0, y0 = self.model.picker_pos or (0, 0)
                w, h = self.model.picker_panel_size
                if x0 <= mx <= x0 + w and y0 <= my <= y0 + h:
                    if event.button == 1:
                        for ch, rect in self.model.picker_rects.items():
                            if rect.collidepoint((mx, my)):
                                self.model.choice = ch
                                return True
                    elif event.button == 3:
                        self.model.picker_dragging = True
                        dx = mx - x0; dy = my - y0
                        self.model.picker_drag_offset = (dx, dy)
                        return True
            # Brush start
            if event.button == 1 and self.model.choice:
                self.model.brush_dragging = True
                self._paint_at_mouse(camera, buildings)
                return True
        elif event.type == pygame.MOUSEBUTTONUP:
            if event.button == 3 and self.model.picker_dragging:
                self.model.picker_dragging = False
                return True
            if event.button == 1 and self.model.brush_dragging:
                self.model.brush_dragging = False
                # persist
                self._save_collisions(buildings)
                return True
        elif event.type == pygame.MOUSEMOTION:
            mx, my = event.pos
            if self.model.picker_dragging:
                dx, dy = self.model.picker_drag_offset
                self.model.picker_pos = (mx - dx, my - dy)
                return True
            if self.model.brush_dragging and self.model.choice:
                self._paint_at_mouse(camera, buildings)
                return True
        return False


class BuildingCollidersPanelController:
    def __init__(self, state, editor_state, editor_view):
        self.state = state
        self.editor_state = editor_state
        self.editor_view = editor_view
        self.model = BuildingCollidersPanelModel()
        self.view = BuildingCollidersPanelView(state, editor_state, self.model)
        self.events = BuildingCollidersPanelEventHandler(state, editor_state, self.model)

    def is_active(self) -> bool:
        return self.model.active

    def activate(self):
        self.model.active = True
        self.model.picker_open = True
        self.model.brush_dragging = False

    def deactivate(self):
        self.model.active = False
        self.model.picker_open = False
        self.model.reset_runtime()

    def toggle(self):
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    def handle_event(self, event, camera, buildings) -> bool:
        return self.events.handle(event, camera, buildings)

    def render(self, screen, camera, buildings):
        self.view.render(screen, camera, buildings, editor_view=self.editor_view)
