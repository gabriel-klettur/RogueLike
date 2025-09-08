import time
import pygame
from typing import List, Optional

from .model import DiagnosticsOverlayModel
from .view import DiagnosticsOverlayView
from .services import lines_builder
from .services import renderers
from .types import CameraLike, StateLike, MapManagerLike, EntitiesLike


class DiagnosticsOverlayController:
    def __init__(self, model: DiagnosticsOverlayModel, view: DiagnosticsOverlayView):
        self.model = model
        self.view = view

    def get_custom_debug_lines(
        self,
        state: StateLike,
        camera: CameraLike,
        map_manager: MapManagerLike,
        entities: EntitiesLike,
    ) -> List[str]:
        # Delegated to services.probes for better modularity
        from .services import probes
        return probes.get_custom_debug_lines(state, camera, map_manager, entities)

    def draw_borders(self, screen, camera, map_manager):
        # Delegate to renderer service
        renderers.draw_borders(screen, camera, map_manager, self.model)

    def render(
        self,
        screen,
        state: Optional[StateLike] = None,
        camera: Optional[CameraLike] = None,
        map_manager: Optional[MapManagerLike] = None,
        entities: Optional[EntitiesLike] = None,
        extra_lines: Optional[List[str]] = None,
        position=(8, 8),
        show_borders=False,
    ):
        now = time.perf_counter()
        rebuild = (now - self.model.last_update_time) >= self.model.update_interval
        if rebuild or self.model.panel_surf is None:
            lines, label_w, value_w, line_levels = lines_builder.build_lines(
                self.model, self.view, state, camera, map_manager, entities, extra_lines
            )

            # Paging: slice lines according to current page and available height
            if getattr(self.model, 'paging_enabled', False):
                line_h = self.view.line_height(self.model)
                screen_surf = pygame.display.get_surface()
                if screen_surf is not None:
                    screen_h = screen_surf.get_height()
                    # Estimar alto visible coherente con view (usa +200 margen)
                    visible_h = max(line_h, min(getattr(self.model, 'max_surface_height', 8000), screen_h - position[1] + 200))
                else:
                    visible_h = getattr(self.model, 'max_surface_height', 8000)
                lines_per_page = max(1, int(visible_h // line_h))
                total_lines = len(lines)
                total_pages = max(1, (total_lines + lines_per_page - 1) // lines_per_page)
                # Clamp y slice
                pi = max(0, min(self.model.page_index, total_pages - 1))
                i0 = pi * lines_per_page
                i1 = min(total_lines, i0 + lines_per_page)
                page_lines = lines[i0:i1]
                page_levels = line_levels[i0:i1]
                # Persist runtime paging metadata
                self.model.page_index = pi
                self.model.total_lines = total_lines
                self.model.lines_per_page = lines_per_page
                self.model.total_pages = total_pages
                # Rebuild with just the page
                self.view.rebuild_panel(self.model, position, page_lines, label_w, value_w)
                self.model.line_levels = page_levels
            else:
                # No paging: render all (ya limitado por max_lines si aplica)
                self.view.rebuild_panel(self.model, position, lines, label_w, value_w)
                self.model.line_levels = line_levels
            self.model.last_update_time = now

        if self.model.panel_surf and self.model.panel_rect:
            clip = screen.get_clip()
            screen.set_clip(self.model.panel_rect)
            screen.blit(self.model.panel_surf, (self.model.panel_rect.left, self.model.panel_rect.top - self.model.scroll_offset))
            screen.set_clip(clip)
            # Hover highlight group rectangle
            mx, my = pygame.mouse.get_pos()
            if self.model.panel_rect.collidepoint((mx, my)):
                line_h = self.view.line_height(self.model)
                local_y = my - self.model.panel_rect.top + self.model.scroll_offset
                index = local_y // line_h
                keys = self.model.line_keys
                levels = getattr(self.model, 'line_levels', [])
                if 0 <= index < len(keys) and 0 <= index < len(levels):
                    cur_level = levels[index]
                    if cur_level is not None:
                        # Find owning header at level <= current line's level
                        h = index
                        while h >= 0:
                            if keys[h].endswith(':') and levels[h] is not None and levels[h] <= cur_level:
                                break
                            h -= 1
                        if h >= 0 and keys[h].endswith(':'):
                            header_level = levels[h] or 0
                            j = h + 1
                            while j < len(keys):
                                lv = levels[j] if j < len(levels) else None
                                # Stop at separators/others (None) or any line at same or shallower level
                                if lv is None or lv <= header_level:
                                    break
                                j += 1
                            start_idx = h
                            end_idx = j - 1
                            if end_idx >= start_idx:
                                rect_x = self.model.panel_rect.left
                                rect_y = self.model.panel_rect.top - self.model.scroll_offset + start_idx * line_h
                                rect_w = self.model.panel_rect.width
                                rect_h = (end_idx - start_idx + 1) * line_h
                                pygame.draw.rect(screen, (255, 255, 0), pygame.Rect(rect_x, rect_y, rect_w, rect_h), 2)

        if show_borders:
            if not (map_manager and camera):
                raise ValueError("Para dibujar bordes debe proporcionar map_manager y camera")
            self.draw_borders(screen, camera, map_manager)
