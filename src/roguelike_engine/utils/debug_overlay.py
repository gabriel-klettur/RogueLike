import time
import pygame
from roguelike_engine.config.map_config import ZONE_WIDTH, ZONE_HEIGHT, GLOBAL_WIDTH, GLOBAL_HEIGHT
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import calculate_dungeon_offset
from roguelike_engine.utils.benchmark import benchmark


class DebugOverlay:
    """
    Encapsula el renderizado de:
      - Panel de métricas de performance (benchmark)
      - FPS real y frame time promedio
      - Líneas de estado personalizadas
      - Bordes de lobby, dungeon y canvas global
      - Hitboxes de pies de enemigos (modo DEBUG)

    Se optimiza mediante throttling y caching de superficies de texto.
    """
    def __init__(
        self,
        perf_log: dict[str, list[float]],
        font_name: str = "Consolas",
        font_size: int = 18,
        bg_color: tuple[int, int, int, int] = (0, 0, 0, 180),
        text_color: tuple[int, int, int] = (255, 255, 255),
        value_color: tuple[int, int, int] = (200, 255, 200),
        padding_x: int = 10,
        padding_y: int = 4,
        spacing: int = 4,
        border_colors: dict[str, tuple[int, int, int]] | None = None,
        border_width: int = 5,
        update_interval: float = 0.2,
    ):
        # Configuración visual
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
        # Caching interno
        self._fonts: dict[int, pygame.font.Font] = {}
        self._text_cache: dict[str, pygame.Surface] = {}
        self._panel_surf: pygame.Surface | None = None
        self._panel_rect: pygame.Rect | None = None
        self._update_interval = update_interval
        self._last_update_time = 0.0

    def _get_font(self, size: int) -> pygame.font.Font:
        if size not in self._fonts:
            self._fonts[size] = pygame.font.SysFont(self.font_name, size)
        return self._fonts[size]

    def _rebuild_panel(
        self,
        position: tuple[int, int],
        lines: list[tuple[str, str]],
        label_w: int,
        value_w: int,
    ):
        # Calcula dimensiones del panel
        font = self._get_font(self.font_size)
        line_h = font.get_height() + self.padding_y * 2 + self.spacing
        total_h = line_h * len(lines)
        total_w = label_w + value_w + self.padding_x * 2 + 8
        # Superficie de fondo única
        surf = pygame.Surface((total_w, total_h), pygame.SRCALPHA)
        surf.fill(self.bg_color)
        # Pintar cada línea (cacheando textos)
        y = 0
        for left, right in lines:
            key_l = f"L:{left}"
            if key_l not in self._text_cache:
                self._text_cache[key_l] = font.render(left, True, self.text_color)
            surf_l = self._text_cache[key_l]
            surf.blit(surf_l, (self.padding_x, y + self.padding_y))
            if right:
                key_r = f"R:{right}"
                if key_r not in self._text_cache:
                    self._text_cache[key_r] = font.render(right, True, self.value_color)
                surf_r = self._text_cache[key_r]
                surf.blit(surf_r, (self.padding_x + label_w + 8, y + self.padding_y))
            y += line_h
        # Cache
        self._panel_surf = surf
        self._panel_rect = surf.get_rect(topleft=position)

    @benchmark(lambda self: self.perf_log, "debug_overlay.render")
    def render(
        self,
        screen: pygame.Surface,
        state=None,
        camera=None,
        map_manager=None,
        entities=None,
        extra_lines: list[str] | None = None,
        position: tuple[int, int] = (8, 8),
        show_borders: bool = False,
    ) -> None:
        now = time.perf_counter()
        rebuild = (now - self._last_update_time) >= self._update_interval
        if rebuild or self._panel_surf is None:
            # 1) Recolectar datos de perf
            font = self._get_font(self.font_size)
            formatted = []
            label_w = value_w = 0
            for key, samples in self.perf_log.items():
                recent = samples[-60:]
                if not recent:
                    continue
                avg_ms = sum(recent) / len(recent) * 1000
                lbl = f"{key:<18}"
                val = f"{avg_ms:>6.2f} ms"
                formatted.append((lbl, val))
                lw, _ = font.size(lbl)
                vw, _ = font.size(val)
                label_w = max(label_w, lw)
                value_w = max(value_w, vw)
            # FPS + frame time
            lines: list[tuple[str, str]] = []
            if state and hasattr(state, 'clock'):
                fps = state.clock.get_fps()
                ft  = (1000 / fps) if fps > 0 else 0
                lines.append(("FPS:", f"{fps:0.1f}"))
                lines.append(("FrameTime:", f"{ft:0.1f} ms"))
            lines.extend(formatted)
            # Líneas extra
            if extra_lines is None and state and camera and map_manager and entities:
                extra_lines = self._get_custom_debug_lines(state, camera, map_manager, entities)
            if extra_lines:
                lines.append(("", ""))
                lines.extend((text, "") for text in extra_lines)
            # Reconstruir superficie
            self._rebuild_panel(position, lines, label_w, value_w)
            self._last_update_time = now

        # Pintar el panel cacheado
        if self._panel_surf and self._panel_rect:
            screen.blit(self._panel_surf, self._panel_rect)

        # Dibujar bordes si pide
        if show_borders:
            if not (map_manager and camera):
                raise ValueError("Para dibujar bordes debe proporcionar map_manager y camera")
            self._draw_lobby_border(screen, camera, map_manager.lobby_offset)
            self._draw_dungeon_border(screen, camera, map_manager.lobby_offset)
            self._draw_global_border(screen, camera)

        # Dibujar hitboxes simplificadas sin recorrer todos los tiles
        if entities and camera:
            for e in entities.enemies:
                hit = getattr(e.movement, 'hitbox', None)
                if not hit:
                    continue
                foot = hit()
                if not camera.is_in_view(foot.x, foot.y, (foot.width, foot.height)):
                    continue
                tl = camera.apply((foot.x, foot.y))
                sz = camera.scale((foot.width, foot.height))
                pygame.draw.rect(screen, (0, 255, 0), pygame.Rect(tl, sz), 1)

    def _get_custom_debug_lines(
        self,
        state,
        camera,
        map_manager,
        entities
    ) -> list[str]:
        lines: list[str] = []
        lines.append(f"Modo: {state.mode}")
        px, py = round(entities.player.x), round(entities.player.y)
        lines.append(f"Pos: ({px}, {py})")
        mx, my = pygame.mouse.get_pos()
        wx = round(mx / camera.zoom + camera.offset_x)
        wy = round(my / camera.zoom + camera.offset_y)
        lines.append(f"Mouse: ({wx}, {wy})")
        tile_col, tile_row = wx // TILE_SIZE, wy // TILE_SIZE
        tile_text = next(
            (t.tile_type for t in map_manager.tiles_in_region if t.rect.collidepoint(wx, wy)),
            "?"
        )
        lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")
        return lines

    def _draw_lobby_border(self, screen, camera, lobby_offset: tuple[int, int]) -> None:
        x0, y0 = lobby_offset
        tl = camera.apply((x0 * TILE_SIZE, y0 * TILE_SIZE))
        sz = camera.scale((ZONE_WIDTH * TILE_SIZE, ZONE_HEIGHT * TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['lobby'], pygame.Rect(tl, sz), self.border_width)

    def _draw_dungeon_border(self, screen, camera, lobby_offset: tuple[int, int]) -> None:
        dx, dy = calculate_dungeon_offset(lobby_offset)
        tl = camera.apply((dx * TILE_SIZE, dy * TILE_SIZE))
        sz = camera.scale((ZONE_WIDTH * TILE_SIZE, ZONE_HEIGHT * TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['dungeon'], pygame.Rect(tl, sz), self.border_width)

    def _draw_global_border(self, screen, camera) -> None:
        tl = camera.apply((0, 0))
        sz = camera.scale((GLOBAL_WIDTH * TILE_SIZE, GLOBAL_HEIGHT * TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['global'], pygame.Rect(tl, sz), self.border_width)
