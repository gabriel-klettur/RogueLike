import pygame
from roguelike_project.core.game.multiplayer.multiplayer import render_remote_players
from roguelike_project.core.game.render.minimap import render_minimap
from roguelike_project.utils.mouse import draw_mouse_crosshair
from roguelike_project.config import DEBUG, TILE_SIZE

class Renderer:
    def __init__(self):
        self._static_surface = None
        self._dirty_rects = []
        self._debug_surfaces = {}
        self._last_camera_pos = (0, 0)
        self._camera_moved = True

    def _create_static_layer(self, state):
        """Pre-render all static tiles to a single surface"""
        # Calculate maximum needed surface size
        max_x = max(tile.x for tile in state.tiles) + TILE_SIZE
        max_y = max(tile.y for tile in state.tiles) + TILE_SIZE
        self._static_surface = pygame.Surface((max_x, max_y))
        
        # Create a dummy camera with scale=1 for static rendering
        class DummyCamera:
            def __init__(self):
                # Default scale factor of 1 (no scaling)
                self.scale_factor = 1
            
            def scale(self, size):
                """Return size unchanged for static rendering"""
                return size
            
            def apply(self, position):
                """Return position unchanged"""
                return position
            
            def apply_rect(self, rect):
                """Return rect unchanged"""
                return rect
        
        dummy_camera = DummyCamera()
        
        for tile in state.tiles:
            tile.render(self._static_surface, dummy_camera)  # Use dummy camera
        
        for obstacle in state.obstacles:
            if getattr(obstacle, 'is_static', True):  # Default to True if attribute doesn't exist
                obstacle.render(self._static_surface, dummy_camera)

    def render_game(self, state):
        screen = state.screen
        self._dirty_rects = []
        
        # Initialize static layer if needed
        if self._static_surface is None:
            self._create_static_layer(state)

        # Clear screen
        screen.fill((0, 0, 0))
        
        # Draw static layer
        static_rect = screen.blit(
            self._static_surface,
            (-state.camera.offset_x, -state.camera.offset_y)
        )
        self._dirty_rects.append(static_rect)

        # Dynamic objects
        for building in getattr(state, "buildings", []):
            if not getattr(building, 'is_static', False):
                dirty_rect = building.render(screen, state.camera)
                if dirty_rect:
                    self._dirty_rects.append(dirty_rect)

        for projectile in state.player.projectiles:
            dirty_rect = projectile.render(screen, state.camera)
            if dirty_rect:
                self._dirty_rects.append(dirty_rect)

        for enemy in state.enemies:
            dirty_rect = enemy.render(screen, state.camera)
            if dirty_rect:
                self._dirty_rects.append(dirty_rect)

        # Player and HUD
        dirty_rect = state.player.render(screen, state.camera)
        if dirty_rect:
            self._dirty_rects.append(dirty_rect)
        
        state.player.render_hud(screen, state.camera)
        draw_mouse_crosshair(screen, state.camera)
        render_remote_players(state)

        if state.show_menu:
            menu_rect = state.menu.draw(screen)
            self._dirty_rects.append(menu_rect)

        minimap_rect = render_minimap(state)
        self._dirty_rects.append(minimap_rect)

        if DEBUG:
            self._render_debug_info(state)

        pygame.display.flip()

    def _render_debug_info(self, state):
        """Optimized debug rendering with cached surfaces"""
        screen = state.screen
        debug_lines = []
        
        # FPS
        fps = state.clock.get_fps()
        debug_lines.append(f"FPS: {int(fps)}")
        
        # Mode
        debug_lines.append(f"Modo: {state.mode}")
        
        # Player position
        player_x, player_y = round(state.player.x), round(state.player.y)
        debug_lines.append(f"Pos: ({player_x}, {player_y})")
        
        # Mouse position
        mouse_x, mouse_y = pygame.mouse.get_pos()
        world_x = round(mouse_x / state.camera.zoom + state.camera.offset_x)
        world_y = round(mouse_y / state.camera.zoom + state.camera.offset_y)
        debug_lines.append(f"Mouse: ({world_x}, {world_y})")
        
        # Tile info
        tile_col, tile_row = int(world_x // TILE_SIZE), int(world_y // TILE_SIZE)
        tile_text = "?"
        for tile in state.tiles:
            if tile.rect.collidepoint(world_x, world_y):
                tile_text = tile.tile_type
                break
        debug_lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")

        # Render all debug info
        y_offset = 8
        for i, text in enumerate(debug_lines):
            if i not in self._debug_surfaces or self._debug_surfaces[i][0] != text:
                # Only re-render if text changed
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