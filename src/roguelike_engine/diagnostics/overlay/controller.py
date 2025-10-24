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
        # Effective minimized state: if animating, treat as expanded to render content for animation
        effective_minimized = self.model.is_minimized and not self.model.animating

        if effective_minimized:
            base_pos = self.model.panel_pos if self.model.panel_pos is not None else position
            self.view.rebuild_minimized(self.model, base_pos)
        else:
            rebuild = (now - self.model.last_update_time) >= self.model.update_interval or self.model.panel_surf is None
            if rebuild:
                lines, label_w, value_w, line_levels, value_colors = lines_builder.build_lines(
                    self.model, self.view, state, camera, map_manager, entities, extra_lines
                )

                if getattr(self.model, 'paging_enabled', False):
                    line_h = self.view.line_height(self.model)
                    screen_surf = pygame.display.get_surface()
                    if screen_surf is not None:
                        screen_h = screen_surf.get_height()
                        visible_h = max(line_h, min(getattr(self.model, 'max_surface_height', 8000), screen_h - position[1] + 200))
                    else:
                        visible_h = getattr(self.model, 'max_surface_height', 8000)
                    lines_per_page = max(1, int(visible_h // line_h))
                    total_lines = len(lines)
                    total_pages = max(1, (total_lines + lines_per_page - 1) // lines_per_page)
                    pi = max(0, min(self.model.page_index, total_pages - 1))
                    i0 = pi * lines_per_page
                    i1 = min(total_lines, i0 + lines_per_page)
                    page_lines = lines[i0:i1]
                    page_levels = line_levels[i0:i1]
                    page_colors = value_colors[i0:i1]
                    self.model.page_index = pi
                    self.model.total_lines = total_lines
                    self.model.lines_per_page = lines_per_page
                    self.model.total_pages = total_pages
                    base_pos = self.model.panel_pos if self.model.panel_pos is not None else position
                    self.view.rebuild_panel(self.model, base_pos, page_lines, label_w, value_w)
                    self.model.line_levels = page_levels
                    self.model.value_colors = page_colors
                else:
                    base_pos = self.model.panel_pos if self.model.panel_pos is not None else position
                    self.view.rebuild_panel(self.model, base_pos, lines, label_w, value_w)
                    self.model.line_levels = line_levels
                    self.model.value_colors = value_colors
                self.model.last_update_time = now

        # After (re)build, if anchored to top-right and not dragging and no manual pos, snap rect to top-right
        if self.model.panel_rect is not None and not getattr(self.model, 'dragging', False):
            if getattr(self.model, 'anchor_top_right', False) and self.model.panel_pos is None:
                try:
                    screen_surf = pygame.display.get_surface()
                    if screen_surf is not None:
                        sw, _ = screen_surf.get_size()
                        margin = int(getattr(self.model, 'anchor_margin', 8) or 8)
                        new_left = max(0, sw - self.model.panel_rect.width - margin)
                        new_top = margin
                        self.model.panel_rect.topleft = (new_left, new_top)
                except Exception:
                    pass
        # If minimized, ensure button rects reflect the final anchored position
        if self.model.panel_rect is not None and (self.model.is_minimized and not self.model.animating):
            try:
                self.view.rebuild_minimized(self.model, self.model.panel_rect.topleft)
            except Exception:
                pass

        # Draw with optional vertical clip animation
        if self.model.panel_surf and self.model.panel_rect:
            clip = screen.get_clip()
            rect = self.model.panel_rect
            h = rect.height
            target_h = h
            do_reset_after_draw = False
            if self.model.animating:
                dt = max(0.0, now - self.model.anim_start_time)
                t = 1.0 if self.model.anim_duration <= 0 else min(1.0, dt / self.model.anim_duration)
                min_h = max(1, int(self.model.minimized_height or self.view.line_height(self.model)))
                if self.model.anim_mode == "minimize":
                    target_h = int(h - (h - min_h) * t)
                elif self.model.anim_mode == "restore":
                    target_h = int(min_h + (h - min_h) * t)
                # Complete animation -> defer reset until after blit
                if t >= 1.0:
                    self.model.animating = False
                    if self.model.anim_mode == "minimize":
                        self.model.is_minimized = True
                    elif self.model.anim_mode == "restore":
                        self.model.is_minimized = False
                    self.model.anim_mode = ""
                    do_reset_after_draw = True
            # Apply clip
            if target_h < h:
                clip_rect = pygame.Rect(rect.left, rect.top, rect.width, max(1, target_h))
                screen.set_clip(clip_rect)
            else:
                screen.set_clip(rect)
            screen.blit(self.model.panel_surf, (rect.left, rect.top - self.model.scroll_offset))
            screen.set_clip(clip)
            # Recompute effective minimized after any state changes above
            effective_minimized_now = self.model.is_minimized and not self.model.animating
            # Draw minimize button on expanded panel
            if not effective_minimized_now and not self.model.animating and rect is not None:
                btn_size = max(16, min(22, self.view.line_height(self.model) - 6))
                bx = rect.right - 6 - btn_size
                by = rect.top + 3
                self.model.btn_min_rect = pygame.Rect(bx, by, btn_size, btn_size)
                pygame.draw.rect(screen, (220, 220, 220), self.model.btn_min_rect, border_radius=4)
                # draw minus symbol
                mx1 = bx + 4
                mx2 = bx + btn_size - 4
                my = by + btn_size // 2
                pygame.draw.line(screen, (30, 30, 30), (mx1, my), (mx2, my), 2)

            if do_reset_after_draw:
                try:
                    self.model.reset_panel()
                    self.model.save_persisted_state()
                except Exception:
                    pass

            # Hover highlight for owning group when expanded
            if not effective_minimized_now:
                mx, my = pygame.mouse.get_pos()
                if rect.collidepoint((mx, my)):
                    line_h = self.view.line_height(self.model)
                    local_y = my - rect.top + self.model.scroll_offset
                    index = local_y // line_h
                    keys = self.model.line_keys
                    levels = getattr(self.model, 'line_levels', [])
                    if 0 <= index < len(keys) and 0 <= index < len(levels):
                        cur_level = levels[index]
                        if cur_level is not None:
                            h_idx = index
                            while h_idx >= 0:
                                if keys[h_idx].endswith(':') and levels[h_idx] is not None and levels[h_idx] <= cur_level:
                                    break
                                h_idx -= 1
                            if h_idx >= 0 and keys[h_idx].endswith(':'):
                                header_level = levels[h_idx] or 0
                                j = h_idx + 1
                                while j < len(keys):
                                    lv = levels[j] if j < len(levels) else None
                                    if lv is None or lv <= header_level:
                                        break
                                    j += 1
                                start_idx = h_idx
                                end_idx = j - 1
                                if end_idx >= start_idx:
                                    rect_x = rect.left
                                    rect_y = rect.top - self.model.scroll_offset + start_idx * line_h
                                    rect_w = rect.width
                                    rect_h = (end_idx - start_idx + 1) * line_h
                                    pygame.draw.rect(screen, (255, 255, 0), pygame.Rect(rect_x, rect_y, rect_w, rect_h), 2)

        if show_borders:
            if not (map_manager and camera):
                raise ValueError("Para dibujar bordes debe proporcionar map_manager y camera")
            self.draw_borders(screen, camera, map_manager)
