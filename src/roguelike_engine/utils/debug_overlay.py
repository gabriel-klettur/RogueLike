"""
Path: src/roguelike_engine/utils/debug_overlay.py

Clase para renderizar el panel de depuración (métricas y bordes de mapa), con benchmarking integrado.
"""
import pygame
from functools import wraps
from roguelike_engine.config_map import (
    LOBBY_WIDTH, LOBBY_HEIGHT,
    DUNGEON_WIDTH, DUNGEON_HEIGHT,
    GLOBAL_WIDTH, GLOBAL_HEIGHT,
    DUNGEON_CONNECT_SIDE
)
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.map.core.service import _calculate_dungeon_offset
from roguelike_engine.utils.benchmark import benchmark


class DebugOverlay:
    """
    Encapsula el renderizado de:
      - Panel de métricas de performance (benchmark)
      - FPS real y frame time promedio
      - Líneas de estado adicionales
      - Bordes de lobby, dungeon y canvas global

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
        # Registro de métricas
        self.perf_log = perf_log
        
        # Parámetros de texto
        self.font_name = font_name
        self.font_size = font_size
        self.bg_color = bg_color
        self.text_color = text_color
        self.value_color = value_color
        self.padding_x = padding_x
        self.padding_y = padding_y
        self.spacing = spacing

        # Parámetros de bordes
        self.border_width = border_width
        self.border_colors = border_colors or {
            "lobby": (255, 255, 255),
            "dungeon": (0, 255, 0),
            "global": (128, 0, 128),
        }

        # Cache de fuentes
        self._fonts: dict[int, pygame.font.Font] = {}

    def _get_font(self, size: int) -> pygame.font.Font:
        if size not in self._fonts:
            self._fonts[size] = pygame.font.SysFont(self.font_name, size)
        return self._fonts[size]

    @benchmark(lambda self: self.perf_log, "debug_overlay.render")
    def render(
        self,
        screen: pygame.Surface,        
        extra_lines: list | None = None,
        position: tuple[int,int] = (8, 8),
        show_borders: bool = False,
        map_manager = None,
        camera = None,
        entities = None,
    ) -> None:
        """
        Renderiza todo el overlay de debug:
        - Panel de performance
        - Líneas de estado adicionales
        - FPS real y frame time promedio
        - Opcionalmente, bordes del mapa
        """
        font = self._get_font(self.font_size)
        formatted: list[tuple[str,str]] = []
        label_width = 0
        value_width = 0

        # Extraemos métricas del perf_log
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
            label_width = max(label_width, lw)
            value_width = max(value_width, vw)

        # Agregar FPS reales y frame time promedio si se pasa un estado
        lines: list[tuple[str,str]] = []
        if extra_lines and isinstance(extra_lines, list) and hasattr(extra_lines[0], 'clock'):
            state = extra_lines.pop(0)
            fps = state.clock.get_fps()
            lines.append(("____FPS (real):", f"{fps:>6.2f} FPS"))
            ft = (1000/fps) if fps > 0 else 0
            lines.append(("____Frame Time avg", f"{ft:>6.2f} ms"))

        # Añadimos las métricas formateadas
        lines.extend(formatted)
        # Espacio y luego las líneas extra (sin valor)
        if extra_lines:
            lines.append(("", ""))
            for text in extra_lines:
                lines.append((text, ""))

        # Dibujar cada línea con fondo y asegurar strings
        x0, y0 = position
        y = y0
        for left, right in lines:
            text_left = str(left)
            text_right = str(right) if right else None
            left_surf = font.render(text_left, True, self.text_color)
            right_surf = font.render(text_right, True, self.value_color) if text_right else None
            h = left_surf.get_height() if not right_surf else max(left_surf.get_height(), right_surf.get_height())
            w = label_width + value_width + self.padding_x * 2

            # Fondo semi-transparente
            bg = pygame.Surface((w, h + self.padding_y * 2), pygame.SRCALPHA)
            bg.fill(self.bg_color)
            screen.blit(bg, (x0, y))

            # Texto
            screen.blit(left_surf, (x0 + self.padding_x, y + self.padding_y))
            if right_surf:
                screen.blit(right_surf, (x0 + self.padding_x + label_width + 8, y + self.padding_y))

            y += h + self.padding_y + self.spacing

        # --- Bordes del mapa (si aplica) ---
        if show_borders:
            if map_manager is None or camera is None:
                raise ValueError("Para dibujar bordes debe proporcionar map_manager y camera")
            self._draw_lobby_border(screen, camera, map_manager.lobby_offset)
            self._draw_dungeon_border(screen, camera, map_manager.lobby_offset)
            self._draw_global_border(screen, camera)                
        if entities is not None and camera is not None and map_manager is not None:
            self._draw_enemy_hitboxes(screen, camera, entities, map_manager.tiles_in_region)

    def _draw_lobby_border(self, screen: pygame.Surface, camera, lobby_offset: tuple[int,int]) -> None:
        x0, y0 = lobby_offset
        topleft = camera.apply((x0 * TILE_SIZE, y0 * TILE_SIZE))
        size = camera.scale((LOBBY_WIDTH * TILE_SIZE, LOBBY_HEIGHT * TILE_SIZE))
        rect = pygame.Rect(topleft, size)
        pygame.draw.rect(screen, self.border_colors['lobby'], rect, self.border_width)

    def _draw_dungeon_border(self, screen: pygame.Surface, camera, lobby_offset: tuple[int,int]) -> None:
        dx, dy = _calculate_dungeon_offset(lobby_offset, DUNGEON_CONNECT_SIDE)
        topleft = camera.apply((dx * TILE_SIZE, dy * TILE_SIZE))
        size = camera.scale((DUNGEON_WIDTH * TILE_SIZE, DUNGEON_HEIGHT * TILE_SIZE))
        rect = pygame.Rect(topleft, size)
        pygame.draw.rect(screen, self.border_colors['dungeon'], rect, self.border_width)

    def _draw_global_border(self, screen: pygame.Surface, camera) -> None:
        topleft = camera.apply((0, 0))
        size = camera.scale((GLOBAL_WIDTH * TILE_SIZE, GLOBAL_HEIGHT * TILE_SIZE))
        rect = pygame.Rect(topleft, size)
        pygame.draw.rect(screen, self.border_colors['global'], rect, self.border_width)
    

    def _draw_enemy_hitboxes(self, screen, camera, entities, tiles) -> None:
        """Dibuja hitbox de pies y colisiones con tiles sólidos"""
        for e in entities.enemies:
            sw,sh = e.sprite_size
            foot_h = int(sh*0.25)
            foot_w = int(sw*0.5)
            foot_x = e.x + (sw-foot_w)//2
            foot_y = e.y + sh - foot_h
            foot_box = pygame.Rect(foot_x, foot_y, foot_w, foot_h)
            # hitbox en verde
            tl = camera.apply((foot_box.x, foot_box.y))
            sz = camera.scale((foot_box.width, foot_box.height))
            pygame.draw.rect(screen, (0,255,0), pygame.Rect(tl, sz), 1)
            # colisión con tiles sólidos
            for t in tiles:
                if getattr(t, 'solid', False) and foot_box.colliderect(t.rect):
                    tl2 = camera.apply((t.x, t.y))
                    sz2 = camera.scale((t.sprite_size[0], t.sprite_size[1]))
                    pygame.draw.rect(screen, (255,0,255), pygame.Rect(tl2, sz2), 2)

