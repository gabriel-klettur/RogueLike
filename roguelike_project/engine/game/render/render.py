# roguelike_project/engine/game/render/render.py

import time
import pygame

from roguelike_project.engine.game.systems.multiplayer.multiplayer import render_remote_players
from roguelike_project.engine.game.render.minimap.minimap import render_minimap
from roguelike_project.utils.mouse import draw_mouse_crosshair
from roguelike_project.config import TILE_SIZE
import roguelike_project.config as config
from roguelike_project.utils.debug_overlay import render_debug_overlay

# 🆕 Sistema Z
from roguelike_project.systems.z_layer.render import render_z_ordered
from roguelike_project.systems.z_layer.visual_effects import apply_z_visual_effect


class Renderer:
    """
    Sistema de renderizado principal del juego.

    Utiliza benchmark opcional por secciones y un sistema de dirty rects.
    Ahora incluye renderizado ordenado por capa Z.
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

        benchmark("--3.1. tiles", lambda: self._render_tiles(state, cam, screen))
        benchmark("--3.2. z_entities", lambda: self._render_z_entities(state, cam, screen))
        benchmark("--3.3. effects", lambda: self._render_effects(state, cam, screen))
        benchmark("--3.4. hud", lambda: state.player.render_hud(screen, cam))
        benchmark("--3.4b tile_editor",     lambda: self._render_tile_editor_layer(state, screen))
        benchmark("--3.5. crosshair", lambda: draw_mouse_crosshair(screen, cam))
        benchmark("--3.6. remote_players", lambda: render_remote_players(state))
        benchmark("--3.7. menu", lambda: self._render_menu(state, screen))
        benchmark("--3.8. minimap", lambda: self._render_minimap(state))
        benchmark("--3.9. systems", lambda: state.systems.render(screen, cam))

        if config.DEBUG and perf_log is not None:
            extra_lines = [state] + self._get_custom_debug_lines(state)
            render_debug_overlay(screen, perf_log, extra_lines=extra_lines, position=(8, 8))

        pygame.display.flip()

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
        """
        Pintamos un borde sobre el tile seleccionado y
        delegamos al TileEditor para picker y UI.
        """
        if hasattr(state, "tile_editor") and state.tile_editor:
            state.tile_editor.render_selection_outline(screen)
            state.tile_editor.render_picker(screen)

    def _render_z_entities(self, state, cam, screen):
        """
        Renderiza entidades ordenadas por Z y Y.
        Los edificios se dividen en dos mitades.
        """
        all_entities = []

        # Obstáculos, enemigos, etc.
        all_entities.extend([
            e for e in state.obstacles if cam.is_in_view(e.x, e.y, getattr(e, "sprite_size", (64, 64)))
        ])
        all_entities.extend([
            e for e in state.enemies if cam.is_in_view(e.x, e.y, e.sprite_size)
        ])
        all_entities.extend([
            e for e in state.remote_entities.values() if cam.is_in_view(e.x, e.y, e.sprite_size)
        ])
        if cam.is_in_view(state.player.x, state.player.y, state.player.sprite_size):
            all_entities.append(state.player)

        # --- Buildings bipartitos ------------------------------------
        for b in state.buildings:
            if not cam.is_in_view(b.x, b.y, b.image.get_size()):
                continue

            for part in b.get_parts():
                # Registramos la Z de cada wrapper individual
                state.z_state.set(part, part.z)
                all_entities.append(part)

        # Orden y render
        render_z_ordered(all_entities, screen, cam, state.z_state)

        # Efectos de depuración
        for entity in all_entities:
            apply_z_visual_effect(entity, state.player, screen, cam, state.z_state)

    def _render_menu(self, state, screen):
        if state.show_menu:
            menu_rect = state.menu.draw(screen)
            self._dirty_rects.append(menu_rect)

    def _render_minimap(self, state):
        minimap_rect = render_minimap(state)
        self._dirty_rects.append(minimap_rect)

    def _get_custom_debug_lines(self, state):
        lines = []
        lines.append(f"Modo: {state.mode}")
        px, py = round(state.player.x), round(state.player.y)
        lines.append(f"Pos: ({px}, {py})")

        mouse_x, mouse_y = pygame.mouse.get_pos()
        world_x = round(mouse_x / state.camera.zoom + state.camera.offset_x)
        world_y = round(mouse_y / state.camera.zoom + state.camera.offset_y)
        lines.append(f"Mouse: ({world_x}, {world_y})")

        tile_col, tile_row = int(world_x // TILE_SIZE), int(world_y // TILE_SIZE)
        tile_text = "?"
        for tile in state.tiles:
            if tile.rect.collidepoint(world_x, world_y):
                tile_text = tile.tile_type
                break
        lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")
        return lines