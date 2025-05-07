import time
import pygame

from roguelike_game.network.render_multiplayer import render_remote_players
from roguelike_engine.minimap.minimap import render_minimap
from roguelike_engine.utils.mouse import draw_mouse_crosshair
from roguelike_engine.config_tiles import TILE_SIZE
import roguelike_engine.config as config
from roguelike_engine.utils.debug_overlay import render_debug_overlay

# Para dibujar bordes de lobby y dungeon
from roguelike_engine.config_map import (
    LOBBY_WIDTH,
    LOBBY_HEIGHT,
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    GLOBAL_HEIGHT,
    GLOBAL_WIDTH,
    DUNGEON_CONNECT_SIDE
)

from roguelike_engine.map.core.service import _calculate_dungeon_offset

# Sistema de orden Z
from roguelike_game.systems.z_layer.render import render_z_ordered


class Renderer:
    """
    Sistema de renderizado principal del juego.

    Utiliza benchmark opcional por secciones y un sistema de dirty rects.
    Incluye:
      - Renderizado de tiles, entidades, efectos, HUD, crosshair, minimap...
      - Trazado de un marco blanco alrededor del lobby (en modo debug)
      - Trazado de un marco verde alrededor de la dungeon (en modo debug)
      - Debug overlay cuando DEBUG=True
    """

    def __init__(self):
        self._dirty_rects = []

    def render_game(self, state, screen, camera, perf_log=None, menu=None, map=None):                
        self._dirty_rects = []
        screen.fill((0, 0, 0))

        def benchmark(section, func):
            if perf_log is not None:
                start = time.perf_counter()
                func()
                perf_log[section].append(time.perf_counter() - start)
            else:
                func()

        # 1) Tiles
        benchmark("--3.1. tiles", lambda: self._render_tiles(state, camera, screen, map))

        # 2) Entidades orden Z
        benchmark("--3.2. z_entities", lambda: self._render_z_entities(state, camera, screen, map))

        # 3) Efectos
        benchmark("--3.3. effects", lambda: self._render_effects(state, camera, screen))

        # 4) HUD
        benchmark("--3.4. hud", lambda: state.player.render_hud(screen, camera))

        # 4.b) Capa del Tile Editor
        benchmark("--3.4b. tile_editor", lambda: self._render_tile_editor_layer(state, screen, camera, map))

        # 5) Crosshair
        benchmark("--3.5. crosshair", lambda: draw_mouse_crosshair(screen, camera))

        # 6) Jugadores remotos
        benchmark("--3.6. remote_players", lambda: render_remote_players(state))

        # 7) Menú
        benchmark("--3.7. menu", lambda: self._render_menu(state, screen, menu))

        # 8) Minimap
        benchmark("--3.8. minimap", lambda: self._render_minimap(state, screen, map))

        # 9) Otros sistemas
        benchmark("--3.9. systems", lambda: state.systems.render(screen, camera))

        # Debug: overlay y bordes
        if config.DEBUG and perf_log is not None:
            extra_lines = [state] + self._get_custom_debug_lines(state, camera, map)
            benchmark("--99.0. debug overlay", lambda: render_debug_overlay(screen, perf_log, extra_lines=extra_lines, position=(0, 0)))
            benchmark("--99.1. border lobby", lambda: self._render_lobby_border(state, screen, camera))
            benchmark("--99.2. border dungeon", lambda: self._render_dungeon_border(map, screen, camera))
            benchmark("--99.3. border global", lambda: self._render_global_border(state, screen, camera))

        pygame.display.flip()

    def _render_global_border(self, state, screen, camera):
        """
        Dibuja un rectángulo púrpura alrededor de todo el lienzo global.
        """
        w_px = GLOBAL_WIDTH * TILE_SIZE
        h_px = GLOBAL_HEIGHT * TILE_SIZE
        top_left = camera.apply((0, 0))
        size = camera.scale((w_px, h_px))
        rect = pygame.Rect(top_left, size)
        purple = (128, 0, 128)
        pygame.draw.rect(screen, purple, rect, 5)

    def _render_lobby_border(self, state, screen: pygame.Surface, camera):
        """
        Dibuja un rectángulo blanco alrededor del lobby.
        """
        off_x, off_y = getattr(state, "lobby_offset", (0, 0))
        x_px = off_x * TILE_SIZE
        y_px = off_y * TILE_SIZE
        w_px = LOBBY_WIDTH * TILE_SIZE
        h_px = LOBBY_HEIGHT * TILE_SIZE
        top_left = camera.apply((x_px, y_px))
        size = camera.scale((w_px, h_px))
        rect = pygame.Rect(top_left, size)
        pygame.draw.rect(screen, (255, 255, 255), rect, 5)

    def _render_dungeon_border(self, map, screen, camera):
        """
        Dibuja un rectángulo verde alrededor de la dungeon procedural.
        """
        lobby_off_x, lobby_off_y = map.lobby_offset
        dungeon_off_x, dungeon_off_y = _calculate_dungeon_offset((lobby_off_x, lobby_off_y), DUNGEON_CONNECT_SIDE)
        x_px = dungeon_off_x * TILE_SIZE
        y_px = dungeon_off_y * TILE_SIZE
        w_px = DUNGEON_WIDTH * TILE_SIZE
        h_px = DUNGEON_HEIGHT * TILE_SIZE
        top_left = camera.apply((x_px, y_px))
        size = camera.scale((w_px, h_px))
        rect = pygame.Rect(top_left, size)
        pygame.draw.rect(screen, (0, 255, 0), rect, 5)

    def _render_effects(self, state, camera, screen):
        dirty_rects = state.systems.effects.render(screen, camera)
        self._dirty_rects.extend(dirty_rects)

    def _render_tiles(self, state, camera, screen, map):
        for tile in map.tiles_in_region:
            if not camera.is_in_view(tile.x, tile.y, tile.sprite_size):
                continue
            dirty = tile.render(screen, camera)
            if dirty:
                self._dirty_rects.append(dirty)

    def _render_tile_editor_layer(self, state, screen, camera, map):
        if getattr(state, "tile_editor_state", None) and state.tile_editor_state.active:
            state.tile_editor_view.render(screen, camera, map)

    def _render_z_entities(self, state, camera, screen, map):
        all_entities = []
        all_entities.extend([
            e for e in state.obstacles
            if camera.is_in_view(e.x, e.y, getattr(e, "sprite_size", (64, 64)))
        ])
        all_entities.extend([
            e for e in state.enemies
            if camera.is_in_view(e.x, e.y, e.sprite_size)
        ])
        all_entities.extend([
            e for e in state.remote_entities.values()
            if camera.is_in_view(e.x, e.y, e.sprite_size)
        ])
        if camera.is_in_view(state.player.x, state.player.y, state.player.sprite_size):
            all_entities.append(state.player)
        for b in state.buildings:
            if not camera.is_in_view(b.x, b.y, b.image.get_size()):
                continue
            for part in b.get_parts():
                state.z_state.set(part, part.z)
                all_entities.append(part)

        render_z_ordered(all_entities, screen, camera, state.z_state)

        # ——— DEBUG: foot-hitbox de monstruos y colisión con paredes ———
        if config.DEBUG:
            for e in state.enemies:
                sw, sh = e.sprite_size
                foot_h = int(sh * 0.25)
                foot_w = int(sw * 0.5)
                foot_x = e.x + (sw - foot_w) // 2
                foot_y = e.y + sh - foot_h
                foot_box = pygame.Rect(foot_x, foot_y, foot_w, foot_h)

                # dibujar el hitbox de pies (verde)
                tl = camera.apply((foot_box.x, foot_box.y))
                sz = camera.scale((foot_box.width, foot_box.height))
                pygame.draw.rect(screen, (0, 255, 0), pygame.Rect(tl, sz), 1)

                # dibujar colisión con tiles sólidos (magenta)
                for t in map.tiles_in_region:
                    if getattr(t, "solid", False):
                        tile_box = pygame.Rect(t.x, t.y, *t.sprite_size)
                        if foot_box.colliderect(tile_box):
                            tl2 = camera.apply((tile_box.x, tile_box.y))
                            sz2 = camera.scale((tile_box.width, tile_box.height))
                            pygame.draw.rect(screen, (255, 0, 255), pygame.Rect(tl2, sz2), 2)

    def _render_menu(self, state, screen, menu):
        if state.show_menu:
            menu_rect = menu.draw(screen)
            self._dirty_rects.append(menu_rect)

    def _render_minimap(self, state, screen, map):
        minimap_rect = render_minimap(state, screen, map)
        self._dirty_rects.append(minimap_rect)

    def _get_custom_debug_lines(self, state, camera, map):
        lines = [
            f"Modo: {state.mode}",
            f"Pos: ({round(state.player.x)}, {round(state.player.y)})"
        ]
        mx, my = pygame.mouse.get_pos()
        wx = round(mx / camera.zoom + camera.offset_x)
        wy = round(my / camera.zoom + camera.offset_y)
        lines.append(f"Mouse: ({wx}, {wy})")

        tile_col, tile_row = wx // TILE_SIZE, wy // TILE_SIZE
        tile_text = "?"
        for tile in map.tiles_in_region:
            if tile.rect.collidepoint(wx, wy):
                tile_text = tile.tile_type
                break
        lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")
        return lines
