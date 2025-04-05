import pygame
from roguelike_project.core.game.multiplayer.multiplayer import render_remote_players
from roguelike_project.core.game.render.minimap import render_minimap
from roguelike_project.utils.mouse import draw_mouse_crosshair
from roguelike_project.config import DEBUG, TILE_SIZE

class Renderer:
    def __init__(self):
        self._dirty_rects = []
        self._debug_surfaces = {}

    def render_game(self, state):
        screen = state.screen
        self._dirty_rects = []

        # Limpiar pantalla
        screen.fill((0, 0, 0))

        # Dibujar tiles
        for tile in state.tiles:
            dirty = tile.render(screen, state.camera)
            if dirty:
                self._dirty_rects.append(dirty)

        # Dibujar obstáculos estáticos
        for obstacle in state.obstacles:
            if getattr(obstacle, 'is_static', True):
                dirty = obstacle.render(screen, state.camera)
                if dirty:
                    self._dirty_rects.append(dirty)

        # Buildings no estáticos
        for building in getattr(state, "buildings", []):
            if not getattr(building, 'is_static', False):
                dirty = building.render(screen, state.camera)
                if dirty:
                    self._dirty_rects.append(dirty)

        # Proyectiles del jugador
        for projectile in state.player.projectiles:
            dirty = projectile.render(screen, state.camera)
            if dirty:
                self._dirty_rects.append(dirty)

        # Enemigos
        for enemy in state.enemies:
            dirty = enemy.render(screen, state.camera)
            if dirty:
                self._dirty_rects.append(dirty)

        # Jugador
        dirty = state.player.render(screen, state.camera)
        if dirty:
            self._dirty_rects.append(dirty)

        # HUD
        state.player.render_hud(screen, state.camera)
        draw_mouse_crosshair(screen, state.camera)

        # Jugadores remotos
        render_remote_players(state)

        # Menú
        if state.show_menu:
            menu_rect = state.menu.draw(screen)
            self._dirty_rects.append(menu_rect)

        # Minimap
        minimap_rect = render_minimap(state)
        self._dirty_rects.append(minimap_rect)

        # Debug
        if DEBUG:
            self._render_debug_info(state)

        pygame.display.flip()

    def _render_debug_info(self, state):
        screen = state.screen
        debug_lines = []

        fps = state.clock.get_fps()
        debug_lines.append(f"FPS: {int(fps)}")
        debug_lines.append(f"Modo: {state.mode}")

        px, py = round(state.player.x), round(state.player.y)
        debug_lines.append(f"Pos: ({px}, {py})")

        mouse_x, mouse_y = pygame.mouse.get_pos()
        world_x = round(mouse_x / state.camera.zoom + state.camera.offset_x)
        world_y = round(mouse_y / state.camera.zoom + state.camera.offset_y)
        debug_lines.append(f"Mouse: ({world_x}, {world_y})")

        tile_col, tile_row = int(world_x // TILE_SIZE), int(world_y // TILE_SIZE)
        tile_text = "?"
        for tile in state.tiles:
            if tile.rect.collidepoint(world_x, world_y):
                tile_text = tile.tile_type
                break
        debug_lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")

        y_offset = 8
        for i, text in enumerate(debug_lines):
            if i not in self._debug_surfaces or self._debug_surfaces[i][0] != text:
                text_surface = state.font.render(text, True, (255, 255, 255))
                bg_rect = pygame.Rect(8, y_offset, text_surface.get_width() + 4, text_surface.get_height() + 4)
                bg_surface = pygame.Surface((bg_rect.width, bg_rect.height))
                bg_surface.fill((0, 0, 0))
                self._debug_surfaces[i] = (text, bg_surface, text_surface, bg_rect)

            _, bg_surface, text_surface, bg_rect = self._debug_surfaces[i]
            screen.blit(bg_surface, bg_rect)
            screen.blit(text_surface, (bg_rect.x + 2, bg_rect.y + 2))
            self._dirty_rects.append(bg_rect)
            y_offset += 22
