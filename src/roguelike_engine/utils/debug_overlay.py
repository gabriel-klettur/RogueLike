"""
Path: src/roguelike_engine/utils/debug_overlay.py

Clase para renderizar el panel de depuración (métricas, bordes, hitboxes y líneas de estado), con benchmarking integrado.
"""
# Path: src/roguelike_engine/utils/debug_overlay.py
import pygame
from roguelike_engine.config_map import (
    ZONE_WIDTH, ZONE_HEIGHT,
    ZONE_WIDTH, ZONE_HEIGHT,
    GLOBAL_WIDTH, GLOBAL_HEIGHT,
    DUNGEON_CONNECT_SIDE
)
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import calculate_dungeon_offset
from roguelike_engine.utils.benchmark import benchmark


class DebugOverlay:
    """
    Encapsula el renderizado de:
      - Panel de métricas de performance (benchmark)
      - FPS real y frame time promedio
      - Líneas de estado personalizadas
      - Bordes de lobby, dungeon y canvas global
      - Hitboxes de pies de enemigos y colisión con paredes (modo DEBUG)

    La función `render` está decorada para registrar automáticamente
    su tiempo de ejecución bajo la clave 'debug_overlay.render'.
    """
    def __init__(
        self,
        perf_log: dict[str, list[float]],
        font_name: str = "Consolas",
        font_size: int = 18,
        bg_color: tuple[int,int,int,int] = (0, 0, 0, 180),
        text_color: tuple[int,int,int] = (255, 255, 255),
        value_color: tuple[int,int,int] = (200, 255, 200),
        padding_x: int = 10,
        padding_y: int = 4,
        spacing: int = 4,
        border_colors: dict[str, tuple[int,int,int]] | None = None,
        border_width: int = 5,
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
            "lobby": (255, 255, 255),
            "dungeon": (0, 255, 0),
            "global": (128, 0, 128),
        }
        self._fonts: dict[int, pygame.font.Font] = {}

    def _get_font(self, size: int) -> pygame.font.Font:
        if size not in self._fonts:
            self._fonts[size] = pygame.font.SysFont(self.font_name, size)
        return self._fonts[size]

    @benchmark(lambda self: self.perf_log, "debug_overlay.render")
    def render(
        self,
        screen: pygame.Surface,
        state=None,
        camera=None,
        map_manager=None,
        entities=None,
        extra_lines: list[str] | None = None,
        position: tuple[int,int] = (8, 8),
        show_borders: bool = False,
    ) -> None:
        """
        Renderiza todo el overlay de debug:
        - Panel de performance
        - FPS real y frame time promedio
        - Líneas de estado (generadas internamente o recibidas)
        - Bordes del mapa si show_borders=True
        - Hitboxes y colisiones si entities está presente
        """
        font = self._get_font(self.font_size)
        # 1) Panel de performance
        formatted: list[tuple[str,str]] = []
        label_w = value_w = 0
        for key, samples in self.perf_log.items():
            recent = samples[-60:]
            if not recent:
                continue
            avg_ms = sum(recent) / len(recent) * 1000
            label = f"{key:<18}"
            value = f"{avg_ms:>6.2f} ms"
            formatted.append((label, value))
            lw, _ = font.size(label)
            vw, _ = font.size(value)
            label_w = max(label_w, lw)
            value_w = max(value_w, vw)

        lines: list[tuple[str,str]] = []
        # FPS reales + frame time avg
        if state and hasattr(state, 'clock'):
            fps = state.clock.get_fps()
            ft = (1000/fps) if fps > 0 else 0
            lines.append(("____FPS (real):", f"{fps:>6.2f} FPS"))
            lines.append(("____Frame Time avg", f"{ft:>6.2f} ms"))
        lines.extend(formatted)

        # Generar líneas adicionales si no se pasaron
        if extra_lines is None and state and camera and map_manager and entities:
            extra_lines = self._get_custom_debug_lines(state, camera, map_manager, entities)

        if extra_lines:
            lines.append(("", ""))
            for text in extra_lines:
                lines.append((text, ""))

        # Dibujar panel
        x0, y0 = position
        y = y0
        for left, right in lines:
            surf_l = font.render(str(left), True, self.text_color)
            surf_r = font.render(str(right), True, self.value_color) if right else None
            h = surf_l.get_height() if not surf_r else max(surf_l.get_height(), surf_r.get_height())
            w = label_w + value_w + self.padding_x*2
            bg = pygame.Surface((w, h + self.padding_y*2), pygame.SRCALPHA)
            bg.fill(self.bg_color)
            screen.blit(bg, (x0, y))
            screen.blit(surf_l, (x0 + self.padding_x, y + self.padding_y))
            if surf_r:
                screen.blit(surf_r, (x0 + self.padding_x + label_w + 8, y + self.padding_y))
            y += h + self.padding_y + self.spacing

        # 2) Bordes de mapa
        if show_borders:
            if not (map_manager and camera):
                raise ValueError("Para dibujar bordes debe proporcionar map_manager y camera")
            self._draw_lobby_border(screen, camera, map_manager.lobby_offset)
            self._draw_dungeon_border(screen, camera, map_manager.lobby_offset)
            self._draw_global_border(screen, camera)

        # 3) Hitboxes de pies y colisiones
        if entities and camera and map_manager:
            self._draw_enemy_hitboxes(screen, camera, entities, map_manager.tiles_in_region)

    def _get_custom_debug_lines(
        self,
        state,
        camera,
        map_manager,
        entities
    ) -> list[str]:
        """
        Genera líneas de estado:
          - Modo de juego
          - Posición del jugador
          - Posición del mouse en mundo
          - Tipo de tile bajo el cursor
        """
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

    def _draw_lobby_border(self, screen, camera, lobby_offset: tuple[int,int]) -> None:
        x0,y0 = lobby_offset
        tl = camera.apply((x0*TILE_SIZE, y0*TILE_SIZE))
        sz = camera.scale((ZONE_WIDTH*TILE_SIZE, ZONE_HEIGHT*TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['lobby'], pygame.Rect(tl, sz), self.border_width)

    def _draw_dungeon_border(self, screen, camera, lobby_offset: tuple[int,int]) -> None:
        dx,dy = calculate_dungeon_offset(lobby_offset)
        tl = camera.apply((dx*TILE_SIZE, dy*TILE_SIZE))
        sz = camera.scale((ZONE_WIDTH*TILE_SIZE, ZONE_HEIGHT*TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['dungeon'], pygame.Rect(tl, sz), self.border_width)

    def _draw_global_border(self, screen, camera) -> None:
        tl = camera.apply((0,0))
        sz = camera.scale((GLOBAL_WIDTH*TILE_SIZE, GLOBAL_HEIGHT*TILE_SIZE))
        pygame.draw.rect(screen, self.border_colors['global'], pygame.Rect(tl, sz), self.border_width)

    def _draw_enemy_hitboxes(
        self,
        screen,
        camera,
        entities,
        tiles: list
    ) -> None:
        for e in entities.enemies:
            sw,sh = e.sprite_size
            foot_h = int(sh*0.25)
            foot_w = int(sw*0.5)
            foot_x = e.x + (sw-foot_w)//2
            foot_y = e.y + sh - foot_h
            foot_box = pygame.Rect(foot_x, foot_y, foot_w, foot_h)
            tl = camera.apply((foot_box.x, foot_box.y))
            sz = camera.scale((foot_box.width, foot_box.height))
            pygame.draw.rect(screen, (0,255,0), pygame.Rect(tl, sz), 1)
            for t in tiles:
                if getattr(t, 'solid', False) and foot_box.colliderect(t.rect):
                    tl2 = camera.apply((t.x, t.y))
                    sz2 = camera.scale((t.sprite_size[0], t.sprite_size[1]))
                    pygame.draw.rect(screen, (255,0,255), pygame.Rect(tl2, sz2), 2)