# Path: src/roguelike_game/game/render_manager.py

import time
import pygame

from roguelike_game.network.render_multiplayer import render_remote_players
from src.roguelike_engine.minimap.minimap import render_minimap
from src.roguelike_engine.utils.mouse import draw_mouse_crosshair
from src.roguelike_engine.config_tiles import TILE_SIZE
import src.roguelike_engine.config as config
from src.roguelike_engine.utils.debug_overlay import render_debug_overlay

# Para dibujar bordes de lobby y dungeon
from src.roguelike_engine.config_map import (
    LOBBY_WIDTH,
    LOBBY_HEIGHT,
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    DUNGEON_OFFSET_X,
    DUNGEON_OFFSET_Y,
    DUNGEON_CONNECT_SIDE
)

from src.roguelike_engine.map.core.service import _calculate_dungeon_offset

# Sistema de orden Z
from src.roguelike_game.systems.z_layer.render import render_z_ordered


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

    def render_game(self, state, perf_log=None):
        screen = state.screen
        cam = state.camera
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
        benchmark("--3.1. tiles", lambda: self._render_tiles(state, cam, screen))

        # 2) Entidades orden Z
        benchmark("--3.2. z_entities", lambda: self._render_z_entities(state, cam, screen))

        # 3) Efectos
        benchmark("--3.3. effects", lambda: self._render_effects(state, cam, screen))

        # 4) HUD
        benchmark("--3.4. hud", lambda: state.player.render_hud(screen, cam))

        # 4.b) Capa del Tile Editor
        benchmark("--3.4b. tile_editor", lambda: self._render_tile_editor_layer(state, screen))

        # 5) Crosshair
        benchmark("--3.5. crosshair", lambda: draw_mouse_crosshair(screen, cam))

        # 6) Jugadores remotos
        benchmark("--3.6. remote_players", lambda: render_remote_players(state))

        # 7) Menú
        benchmark("--3.7. menu", lambda: self._render_menu(state, screen))

        # 8) Minimap
        benchmark("--3.8. minimap", lambda: self._render_minimap(state))

        # 9) Otros sistemas
        benchmark("--3.9. systems", lambda: state.systems.render(screen, cam))

        # Debug: overlay y bordes
        if config.DEBUG and perf_log is not None:
            extra_lines = [state] + self._get_custom_debug_lines(state)
            benchmark("--99.0. debug overlay", lambda: render_debug_overlay(screen, perf_log, extra_lines=extra_lines, position=(0, 0)))
            benchmark("--99.1. border lobby", lambda: self._render_lobby_border(state, screen, cam))
            benchmark("--99.2. border dungeon", lambda: self._render_dungeon_border(state, screen, cam))

        pygame.display.flip()

    def _render_lobby_border(self, state, screen: pygame.Surface, cam):
        """
        Dibuja un rectángulo blanco alrededor del lobby en la posición
        almacenada en state.lobby_offset.
        """
        off_x, off_y = getattr(state, "lobby_offset", (0, 0))
        x_px = off_x * TILE_SIZE
        y_px = off_y * TILE_SIZE
        w_px = LOBBY_WIDTH * TILE_SIZE
        h_px = LOBBY_HEIGHT * TILE_SIZE

        top_left = cam.apply((x_px, y_px))
        size = cam.scale((w_px, h_px))
        rect = pygame.Rect(top_left, size)
        pygame.draw.rect(screen, (255, 255, 255), rect, 5)

    def _render_dungeon_border(self, state, screen, cam):
        """
        Dibuja un rectángulo verde alrededor de la dungeon procedural,
        calculando dinámicamente su posición según el lobby y el side.
        """
        # 1️⃣ Recuperar offset del lobby (en celdas) que guardamos al inicializar el mapa
        lobby_off_x, lobby_off_y = state.lobby_offset

        # 2️⃣ Calcular offset de la dungeon en celdas, según el side
        dungeon_off_x, dungeon_off_y = _calculate_dungeon_offset(
            (lobby_off_x, lobby_off_y),
            DUNGEON_CONNECT_SIDE
        )

        # 3️⃣ Pasar de celdas a píxeles
        x_px = dungeon_off_x * TILE_SIZE
        y_px = dungeon_off_y * TILE_SIZE
        w_px = DUNGEON_WIDTH * TILE_SIZE
        h_px = DUNGEON_HEIGHT * TILE_SIZE

        # 4️⃣ Dibujar el rectángulo
        top_left = cam.apply((x_px, y_px))
        size = cam.scale((w_px, h_px))
        rect = pygame.Rect(top_left, size)
        pygame.draw.rect(screen, (0, 255, 0), rect, 5)

    def _render_effects(self, state, cam, screen):
        dirty_rects = state.systems.effects.render(screen, cam)
        self._dirty_rects.extend(dirty_rects)

    def _render_tiles(self, state, cam, screen):
        tile_map = state.tile_map

        start_col = int(cam.offset_x // TILE_SIZE)
        end_col = int((cam.offset_x + cam.screen_width / cam.zoom) // TILE_SIZE) + 1
        start_row = int(cam.offset_y // TILE_SIZE)
        end_row = int((cam.offset_y + cam.screen_height / cam.zoom) // TILE_SIZE) + 1

        start_row = max(0, start_row)
        end_row = min(len(tile_map), end_row)
        start_col = max(0, start_col)
        end_col = min(len(tile_map[0]), end_col)

        for row in range(start_row, end_row):
            for col in range(start_col, end_col):
                tile = tile_map[row][col]
                dirty = tile.render(screen, cam)
                if dirty:
                    self._dirty_rects.append(dirty)

    def _render_tile_editor_layer(self, state, screen):
        if getattr(state, "tile_editor_state", None) and state.tile_editor_state.active:
            state.tile_editor_view.render(screen)

    def _render_z_entities(self, state, cam, screen):
        all_entities = []

        all_entities.extend([
            e for e in state.obstacles
            if cam.is_in_view(e.x, e.y, getattr(e, "sprite_size", (64, 64)))
        ])
        all_entities.extend([
            e for e in state.enemies
            if cam.is_in_view(e.x, e.y, e.sprite_size)
        ])
        all_entities.extend([
            e for e in state.remote_entities.values()
            if cam.is_in_view(e.x, e.y, e.sprite_size)
        ])
        if cam.is_in_view(state.player.x, state.player.y, state.player.sprite_size):
            all_entities.append(state.player)

        for b in state.buildings:
            if not cam.is_in_view(b.x, b.y, b.image.get_size()):
                continue
            for part in b.get_parts():
                state.z_state.set(part, part.z)
                all_entities.append(part)

        render_z_ordered(all_entities, screen, cam, state.z_state)

    def _render_menu(self, state, screen):
        if state.show_menu:
            menu_rect = state.menu.draw(screen)
            self._dirty_rects.append(menu_rect)

    def _render_minimap(self, state):
        minimap_rect = render_minimap(state)
        self._dirty_rects.append(minimap_rect)

    def _get_custom_debug_lines(self, state):
        lines = [
            f"Modo: {state.mode}",
            f"Pos: ({round(state.player.x)}, {round(state.player.y)})"
        ]
        mouse_x, mouse_y = pygame.mouse.get_pos()
        world_x = round(mouse_x / state.camera.zoom + state.camera.offset_x)
        world_y = round(mouse_y / state.camera.zoom + state.camera.offset_y)
        lines.append(f"Mouse: ({world_x}, {world_y})")

        tile_col, tile_row = world_x // TILE_SIZE, world_y // TILE_SIZE
        tile_text = "?"
        for tile in state.tiles:
            if tile.rect.collidepoint(world_x, world_y):
                tile_text = tile.tile_type
                break
        lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")
        return lines
