import time
import pygame
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import calculate_dungeon_offset
from roguelike_engine.utils.benchmark import benchmark
import roguelike_engine.config.config as config


class DebugOverlay:
    def __init__(
        self,
        perf_log: dict[str, list[float]],
        font_name: str = "Consolas",
        font_size: int = 12,
        bg_color: tuple[int, int, int, int] = (0, 0, 0, 180),
        text_color: tuple[int, int, int] = (255, 255, 255),
        value_color: tuple[int, int, int] = (200, 255, 200),
        padding_x: int = 10,
        padding_y: int = 4,
        spacing: int = 4,
        border_colors: dict[str, tuple[int, int, int]] | None = None,
        border_width: int = 5,
        update_interval: float = 0.2,
        scroll_speed: int = 20
    ):
        self.perf_log = perf_log
        self.font_name = font_name
        self.font_size = font_size
        self.bg_color = bg_color
        self.text_color = text_color
        self.value_color = value_color
        self.padding_x = padding_x
        self.padding_y = padding_y
        self.spacing = spacing
        self.border_width = border_width
        self.border_colors = border_colors or {
            "lobby":    (255, 255, 255),
            "dungeon":  (0, 255,   0),
            "global":   (128,   0, 128),
        }
        self._fonts: dict[int, pygame.font.Font] = {}
        self._text_cache: dict[str, pygame.Surface] = {}
        self._panel_surf: pygame.Surface | None = None
        self._panel_rect: pygame.Rect | None = None
        self._update_interval = update_interval
        self._last_update_time = 0.0
        self._scroll_offset = 0
        self._scroll_speed = scroll_speed
        self._collapsed_groups: set[str] = set()
        # Flag para colapsar todos los grupos en la primera renderización
        self._initially_collapsed: bool = True

    def _get_font(self, size: int) -> pygame.font.Font:
        if size not in self._fonts:
            self._fonts[size] = pygame.font.SysFont(self.font_name, size)
        return self._fonts[size]

    def handle_event(self, event):
        if event.type == pygame.MOUSEWHEEL:
            if event.y > 0:  # Scroll up
                self._scroll_offset = max(0, self._scroll_offset - self._scroll_speed)
            elif event.y < 0:  # Scroll down
                self._scroll_offset += self._scroll_speed
            return
        if event.type == pygame.MOUSEBUTTONDOWN:
            if event.button == 4:  
                self._scroll_offset = max(0, self._scroll_offset - self._scroll_speed)
            elif event.button == 5:  
                self._scroll_offset += self._scroll_speed
            elif event.button == 1:
                if self._panel_rect and self._panel_rect.collidepoint(event.pos):
                    local_y = event.pos[1] - self._panel_rect.top + self._scroll_offset
                    line_h = self._get_font(self.font_size).get_height() + self.padding_y * 2 + self.spacing
                    index = local_y // line_h
                    if index < len(self._line_keys):
                        key = self._line_keys[index]
                        if key:
                            if key.endswith(':'):
                                root = key[:-1].strip()
                                group = root.split('.')[0]
                            else:
                                group = key.split('.')[0]
                            if group in self._collapsed_groups:
                                self._collapsed_groups.remove(group)
                            else:
                                self._collapsed_groups.add(group)
                            # Force panel rebuild on next render
                            self._panel_surf = None

    def _rebuild_panel(self, position, lines, label_w, value_w):
        font = self._get_font(self.font_size)
        line_h = font.get_height() + self.padding_y * 2 + self.spacing
        total_h = line_h * len(lines)
        total_w = label_w + value_w + self.padding_x * 2 + 8
        surf = pygame.Surface((total_w, total_h), pygame.SRCALPHA)
        surf.fill(self.bg_color)

        self._line_keys = []
        y = 0
        for left, right in lines:
            is_header = left.strip().endswith(':')
            # Render label
            cache_label = f"{('HL' if is_header else 'L')}:{left}"
            if cache_label not in self._text_cache:
                if is_header:
                    bold_font = pygame.font.SysFont(self.font_name, self.font_size, bold=True)
                    self._text_cache[cache_label] = bold_font.render(left, True, (255, 255, 0))
                else:
                    self._text_cache[cache_label] = font.render(left, True, self.text_color)
            surf_l = self._text_cache[cache_label]
            surf.blit(surf_l, (self.padding_x, y + self.padding_y))
            self._line_keys.append(left.strip())
            # Render value
            if right:
                cache_val = f"{('HV' if is_header else 'R')}:{right}"
                if cache_val not in self._text_cache:
                    if is_header:
                        bold_font = pygame.font.SysFont(self.font_name, self.font_size, bold=True)
                        self._text_cache[cache_val] = bold_font.render(right, True, (255, 255, 0))
                    else:
                        self._text_cache[cache_val] = font.render(right, True, self.value_color)
                surf_r = self._text_cache[cache_val]
                surf.blit(surf_r, (self.padding_x + label_w + 8, y + self.padding_y))
            y += line_h

        self._panel_surf = surf
        self._panel_rect = surf.get_rect(topleft=position)

    @benchmark(lambda self: self.perf_log, "3.12. debug.render")
    def render(self, screen, state=None, camera=None, map_manager=None, entities=None, extra_lines=None, position=(8, 8), show_borders=False):
        now = time.perf_counter()
        rebuild = (now - self._last_update_time) >= self._update_interval
        if rebuild or self._panel_surf is None:
            font = self._get_font(self.font_size)
            lines = []
            label_w = value_w = 0
            groups = {}
            for key, samples in self.perf_log.items():
                recent = samples[-60:]
                if not recent:
                    continue
                avg_ms = sum(recent) / len(recent) * 1000
                group = key.split(".")[0]
                groups.setdefault(group, []).append((key, avg_ms))
            # Colapsar todos los grupos en la primera generación del panel
            if self._initially_collapsed:
                self._collapsed_groups = set(groups.keys())
                self._initially_collapsed = False

            for group, entries in sorted(groups.items()):
                # Mostrar número total de elementos y usar TOTAL en el encabezado
                count = len(entries)
                total_item = next(((full_key, avg) for full_key, avg in entries if 'TOTAL' in full_key.upper()), None)
                if total_item:
                    total_key, total_val = total_item
                    header_lbl = f'{total_key} ({count}):'
                    header_val = f'{total_val:>6.2f} ms'
                else:
                    header_lbl = f'{group} ({count}):'
                    header_val = ''
                lines.append((header_lbl, header_val))
                if group not in self._collapsed_groups:
                    for full_key, avg_ms in sorted(entries):
                        # Omit total entry from detail list (already shown in header)
                        if total_item and full_key == total_key:
                            continue
                        lbl = f"  {full_key:<20}"
                        val = f"{avg_ms:>6.2f} ms"
                        lines.append((lbl, val))
                        lw, _ = font.size(lbl)
                        vw, _ = font.size(val)
                        label_w = max(label_w, lw)
                        value_w = max(value_w, vw)

            if state and hasattr(state, 'clock'):
                fps = state.clock.get_fps()
                ft  = (1000 / fps) if fps > 0 else 0
                lines.insert(0, ("FrameTime:", f"{ft:0.1f} ms"))
                lines.insert(0, ("FPS:", f"{fps:0.1f}"))

            if extra_lines is None and state and camera and map_manager and entities:
                extra_lines = self._get_custom_debug_lines(state, camera, map_manager, entities)
            if extra_lines:
                lines.append(("", ""))
                lines.extend((text, "") for text in extra_lines)

            # Ajustar ancho al contenido completo
            for left, right in lines:
                lw, _ = font.size(left)
                vw, _ = font.size(right)
                label_w = max(label_w, lw)
                value_w = max(value_w, vw)

            self._rebuild_panel(position, lines, label_w, value_w)
            self._last_update_time = now

        if self._panel_surf and self._panel_rect:
            clip = screen.get_clip()
            screen.set_clip(self._panel_rect)
            screen.blit(self._panel_surf, (self._panel_rect.left, self._panel_rect.top - self._scroll_offset))
            screen.set_clip(clip)
            mx, my = pygame.mouse.get_pos()
            if self._panel_rect.collidepoint((mx, my)):
                font = self._get_font(self.font_size)
                line_h = font.get_height() + self.padding_y * 2 + self.spacing
                local_y = my - self._panel_rect.top + self._scroll_offset
                index = local_y // line_h
                if 0 <= index < len(self._line_keys):
                    start_idx = index
                    while start_idx > 0 and not self._line_keys[start_idx].endswith(':'):
                        start_idx -= 1
                    end_idx = start_idx + 1
                    while end_idx < len(self._line_keys) and not self._line_keys[end_idx].endswith(':'):
                        end_idx += 1
                    end_idx -= 1
                    rect_x = self._panel_rect.left
                    rect_y = self._panel_rect.top - self._scroll_offset + start_idx * line_h
                    rect_w = self._panel_rect.width
                    rect_h = (end_idx - start_idx + 1) * line_h
                    pygame.draw.rect(screen, (255, 255, 0), pygame.Rect(rect_x, rect_y, rect_w, rect_h), 2)

        if show_borders:
            if not (map_manager and camera):
                raise ValueError("Para dibujar bordes debe proporcionar map_manager y camera")
            self._draw_lobby_border(screen, camera, map_manager.lobby_offset)
            self._draw_dungeon_border(screen, camera, map_manager.lobby_offset)
            self._draw_global_border(screen, camera)


    def _get_custom_debug_lines(self, state, camera, map_manager, entities):
        lines = [
            f"Modo: {state.mode}",
            f"Pos: ({round(entities.player.x)}, {round(entities.player.y)})"
        ]
        mx, my = pygame.mouse.get_pos()
        wx = round(mx / camera.zoom + camera.offset_x)
        wy = round(my / camera.zoom + camera.offset_y)
        lines.append(f"Mouse: ({wx}, {wy})")
        tile_col, tile_row = wx // TILE_SIZE, wy // TILE_SIZE
        tile_text = next((t.tile_type for t in map_manager.tiles_in_region if t.rect.collidepoint(wx, wy)), "?")
        lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")
        return lines

    def _draw_lobby_border(self, screen, camera, lobby_offset):
        x0, y0 = lobby_offset
        tl = camera.apply((x0 * TILE_SIZE, y0 * TILE_SIZE))
        sz = camera.scale((global_map_settings.zone_width * TILE_SIZE, global_map_settings.zone_height * TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['lobby'], pygame.Rect(tl, sz), self.border_width)

    def _draw_dungeon_border(self, screen, camera, lobby_offset):
        dx, dy = calculate_dungeon_offset(lobby_offset)
        tl = camera.apply((dx * TILE_SIZE, dy * TILE_SIZE))
        sz = camera.scale((global_map_settings.zone_width * TILE_SIZE, global_map_settings.zone_height * TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['dungeon'], pygame.Rect(tl, sz), self.border_width)

    def _draw_global_border(self, screen, camera):
        tl = camera.apply((0, 0))
        sz = camera.scale((global_map_settings.global_width * TILE_SIZE, global_map_settings.global_height * TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['global'], pygame.Rect(tl, sz), self.border_width)


# Debug helpers
def draw_debug_rect(screen, camera, rect, color=(255,255,255), width=1):
    if not config.DEBUG:
        return
    scaled_rect = pygame.Rect(camera.apply(rect.topleft), camera.scale(rect.size))
    pygame.draw.rect(screen, color, scaled_rect, width)

def draw_debug_mask_outline(screen, camera, surface, origin, color=(255,0,0), width=1):
    if not config.DEBUG:
        return
    mask = pygame.mask.from_surface(surface)
    outline = mask.outline()
    pts = [camera.apply((origin[0]+x, origin[1]+y)) for x,y in outline]
    if len(pts) >= 3:
        pygame.draw.polygon(screen, color, pts, width)

def draw_zone_border(screen, camera, tiles, zone_name, colors, border_width):
    if not config.DEBUG or not tiles:
        return
    xs = [t.x for t in tiles]
    ys = [t.y for t in tiles]
    min_x, max_x = min(xs), max(xs) + TILE_SIZE
    min_y, max_y = min(ys), max(ys) + TILE_SIZE
    top_left = camera.apply((min_x, min_y))
    bottom_right = camera.apply((max_x, max_y))
    w = bottom_right[0] - top_left[0]
    h = bottom_right[1] - top_left[1]
    rect = pygame.Rect(top_left, (w, h))
    color = colors.get(zone_name, (200,200,200))
    pygame.draw.rect(screen, color, rect, border_width)

def render_debug_overlay(debug_overlay, screen, state, camera, map_manager, entities, show_borders=False):
    if not config.DEBUG or debug_overlay.perf_log is None:
        return
    debug_overlay.render(
        screen,
        state=state,
        camera=camera,
        map_manager=map_manager,
        entities=entities,
        show_borders=show_borders
    )
