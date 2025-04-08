import pygame
from roguelike_project.engine.game.multiplayer.multiplayer import render_remote_players
from roguelike_project.engine.game.render.minimap import render_minimap
from roguelike_project.utils.mouse import draw_mouse_crosshair
from roguelike_project.config import DEBUG, TILE_SIZE
from roguelike_project.utils.debug_overlay import render_debug_overlay  # ✅

class Renderer:
    def __init__(self):
        self._dirty_rects = []

    def render_game(self, state, perf_log=None):
        screen = state.screen
        self._dirty_rects = []
        screen.fill((0, 0, 0))

        cam = state.camera

        for tile in state.tiles:
            if cam.is_in_view(tile.x, tile.y, (TILE_SIZE, TILE_SIZE)):
                dirty = tile.render(screen, cam)
                if dirty:
                    self._dirty_rects.append(dirty)

        for obstacle in state.obstacles:
            if getattr(obstacle, 'is_static', True):
                if cam.is_in_view(obstacle.x, obstacle.y, obstacle.size):
                    dirty = obstacle.render(screen, cam)
                    if dirty:
                        self._dirty_rects.append(dirty)

        for building in getattr(state, "buildings", []):
            if not getattr(building, 'is_static', False):
                size = (building.image.get_width(), building.image.get_height())
                if cam.is_in_view(building.x, building.y, size):
                    dirty = building.render(screen, cam)
                    if dirty:
                        self._dirty_rects.append(dirty)

        for projectile in state.player.projectiles:
            if cam.is_in_view(projectile.x, projectile.y, projectile.size):
                dirty = projectile.render(screen, cam)
                if dirty:
                    self._dirty_rects.append(dirty)

        for enemy in state.enemies:
            if cam.is_in_view(enemy.x, enemy.y, enemy.sprite_size):
                dirty = enemy.render(screen, cam)
                if dirty:
                    self._dirty_rects.append(dirty)

        if cam.is_in_view(state.player.x, state.player.y, state.player.sprite_size):
            dirty = state.player.render(screen, cam)
            if dirty:
                self._dirty_rects.append(dirty)

        state.player.render_hud(screen, cam)
        draw_mouse_crosshair(screen, cam)

        render_remote_players(state)

        if state.show_menu:
            menu_rect = state.menu.draw(screen)
            self._dirty_rects.append(menu_rect)

        minimap_rect = render_minimap(state)
        self._dirty_rects.append(minimap_rect)

        if DEBUG and perf_log is not None:
            extra_lines = self._get_custom_debug_lines(state)
            render_debug_overlay(screen, perf_log, extra_lines=extra_lines, position=(8, 340))

        pygame.display.flip()

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
