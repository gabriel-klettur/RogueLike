import time
import pygame
from roguelike_project.engine.game.multiplayer.multiplayer import render_remote_players
from roguelike_project.engine.game.render.minimap import render_minimap
from roguelike_project.utils.mouse import draw_mouse_crosshair
from roguelike_project.config import DEBUG, TILE_SIZE
from roguelike_project.utils.debug_overlay import render_debug_overlay


class Renderer:
    def __init__(self):
        self._dirty_rects = []

    def render_game(self, state, perf_log=None):
        screen = state.screen
        self._dirty_rects = []
        screen.fill((0, 0, 0))

        cam = state.camera

        def benchmark(section, func):
            if perf_log is not None:
                start = time.perf_counter()
                func()
                perf_log[section].append(time.perf_counter() - start)
            else:
                func()

        benchmark("--3.1. tiles", lambda: self._render_tiles(state, cam, screen))
        benchmark("--3.2. obstacles", lambda: self._render_obstacles(state, cam, screen))
        benchmark("--3.3. buildings", lambda: self._render_buildings(state, cam, screen))
        benchmark("--3.4. projectiles", lambda: self._render_projectiles(state, cam, screen))
        benchmark("--3.5. enemies", lambda: self._render_enemies(state, cam, screen))
        benchmark("--3.6. player", lambda: self._render_player(state, cam, screen))
        benchmark("--3.7. hud", lambda: state.player.render_hud(screen, cam))
        benchmark("--3.8. crosshair", lambda: draw_mouse_crosshair(screen, cam))
        benchmark("--3.9. remote_players", lambda: render_remote_players(state))
        benchmark("--3.10. menu", lambda: self._render_menu(state, screen))
        benchmark("--3.11. minimap", lambda: self._render_minimap(state))
        
        if DEBUG and perf_log is not None:
            extra_lines = self._get_custom_debug_lines(state)
            render_debug_overlay(screen, perf_log, extra_lines=extra_lines, position=(8, 8))

        pygame.display.flip()

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

    def _render_obstacles(self, state, cam, screen):
        for obstacle in state.obstacles:
            if getattr(obstacle, 'is_static', True):
                if cam.is_in_view(obstacle.x, obstacle.y, obstacle.size):
                    dirty = obstacle.render(screen, cam)
                    if dirty:
                        self._dirty_rects.append(dirty)

    def _render_buildings(self, state, cam, screen):
        for building in getattr(state, "buildings", []):
            if not getattr(building, 'is_static', False):
                size = (building.image.get_width(), building.image.get_height())
                if cam.is_in_view(building.x, building.y, size):
                    dirty = building.render(screen, cam)
                    if dirty:
                        self._dirty_rects.append(dirty)

    def _render_projectiles(self, state, cam, screen):
        for projectile in state.player.projectiles:
            if cam.is_in_view(projectile.x, projectile.y, projectile.size):
                dirty = projectile.render(screen, cam)
                if dirty:
                    self._dirty_rects.append(dirty)

    def _render_enemies(self, state, cam, screen):
        for enemy in state.enemies:
            if cam.is_in_view(enemy.x, enemy.y, enemy.sprite_size):
                dirty = enemy.render(screen, cam)
                if dirty:
                    self._dirty_rects.append(dirty)

    def _render_player(self, state, cam, screen):
        if cam.is_in_view(state.player.x, state.player.y, state.player.sprite_size):
            dirty = state.player.render(screen, cam)
            if dirty:
                self._dirty_rects.append(dirty)

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
