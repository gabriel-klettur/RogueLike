# Path: src/roguelike_game/game/render_manager.py
import pygame
from roguelike_engine.utils.mouse import draw_mouse_crosshair
from roguelike_engine.utils.benchmark import benchmark
from roguelike_engine.utils.debug import DebugOverlay, render_debug_overlay
from roguelike_engine.config.config_tiles import TILE_SIZE

# Sistema de orden Z
from roguelike_game.systems.z_layer.render import render_z_ordered

# Importar el decorador centralizado de benchmark
from roguelike_engine.zone.view.zone_view import ZoneView


class RendererManager:
    """
    Sistema de renderizado principal del juego.

    Utiliza benchmark opcional por secciones y un sistema de dirty rects.
    Incluye:
      - Renderizado de tiles, entidades, efectos, HUD, crosshair, minimap...
      - Trazado de un marco blanco alrededor del lobby (en modo debug)
      - Trazado de un marco verde alrededor de la dungeon (en modo debug)
      - Debug overlay cuando DEBUG=True
    """

    def __init__(
        self,
        screen,
        camera,
        map,
        entities,
        buildings_editor,
        tiles_editor,
        perf_log,
        minimap
    ):
        self.screen = screen
        self.camera = camera
        self.map = map
        self.entities = entities
        self.buildings_editor = buildings_editor
        self.tiles_editor = tiles_editor
        self._dirty_rects = []        
        self.debug_overlay = DebugOverlay(perf_log=perf_log)
        self.zone_view = ZoneView()
        self.minimap = minimap
        self._last_state = None  # almacenar último estado para editor
        # Cache last visible layers to minimize cache invalidations
        self._last_visible_layers = None
        # Collision view cache: regenerate surfaces only when zoom changes
        self._collision_last_zoom = None
        self._collision_font = None
        self._collision_surf_solid = None
        self._collision_surf_walkable = None

    def render_game(
        self,
        state,
        screen,
        camera,
        perf_log=None,
        menu=None,
        map=None,
        entities=None,        
        systems=None,        
    ):

        # guardar state para _render_editors
        self._last_state = state

        @benchmark(perf_log, "3.0. init_and_cleaning")
        def _init_and_cleaning():
            screen.fill((0, 0, 0))
            self._dirty_rects = []
        _init_and_cleaning()

        # 1) Map
        @benchmark(perf_log, "3.1. map")
        def _bench_map():
            self._render_map(camera, screen, map)
        _bench_map()

        # 2) Entidades orden Z
        @benchmark(perf_log, "3.2. z_entities")
        def _bench_z_entities():
            # Skip entity rendering in collision-only mode
            if not (self.tiles_editor.editor_state.active and self.tiles_editor.editor_state.show_collisions and not self.tiles_editor.editor_state.show_collisions_overlay):
                self._render_z_entities(state, camera, screen, entities)
        _bench_z_entities()

        # 3) Efectos
        @benchmark(perf_log, "3.3. effects")
        def _bench_effects():
            self._render_effects(camera, screen, systems.effects)
        _bench_effects()

        # 4) HUD
        @benchmark(perf_log, "3.4. hud")
        def _bench_hud():
            self.entities.player.render_hud(screen, camera)
        _bench_hud()

        # 4.b) Capa del Tile Editor
        @benchmark(perf_log, "3.4b. tile_editor")
        def _bench_tile_editor():
            # Skip tile editor UI in collision-only mode
            if not (self.tiles_editor.editor_state.active and self.tiles_editor.editor_state.show_collisions and not self.tiles_editor.editor_state.show_collisions_overlay):
                self._render_tile_editor_layer(state, screen, camera, map)
        _bench_tile_editor()

        # 5) Crosshair
        @benchmark(perf_log, "3.5. crosshair")
        def _bench_crosshair():
            draw_mouse_crosshair(screen, camera)
        _bench_crosshair()

        # 6) Menú
        @benchmark(perf_log, "3.6. menu")
        def _bench_menu():
            self._render_menu(screen, menu)
        _bench_menu()

        # 7) Minimap
        @benchmark(perf_log, "3.7. minimap")
        def _bench_minimap():
            self._render_minimap(screen)
        _bench_minimap()

        # 8) Otros sistemas
        @benchmark(perf_log, "3.8. systems")
        def _bench_systems():
            systems.render(screen, camera)
        _bench_systems()

        # 9) Editores
        @benchmark(perf_log, "3.9. editors")
        def _bench_editors():
            self._render_editors()
        _bench_editors()

        # Debug: overlay y bordes
        render_debug_overlay(self.debug_overlay, screen, state, camera, self.map, entities, show_borders=True)

        @benchmark(perf_log, "3.10. update dirth rects")        
        def _update_dirty_rects():
            # Actualizar solo regiones sucias, o todo si hay demasiadas        
            if len(self._dirty_rects) > 100:
                # demasiados rects, repintamos todo para evitar overhead
                pygame.display.flip()
            else:
                pygame.display.update(self._dirty_rects)

        _update_dirty_rects()

        return self._dirty_rects
        

    def _render_editors(self):
        """
        Renderiza los editores de edificios y tiles si están activos.
        """
        if self.tiles_editor.editor_state.active:
            # Si estamos en modo brush, re-renderizar mapa y entidades para ver el cambio inmediato
            if self.tiles_editor.editor_state.current_tool == "brush":
                self._render_map(self.camera, self.screen, self.map)
                # re-dibujar entidades (edificios, enemigos, jugador)
                self._render_z_entities(
                    self._last_state, self.camera, self.screen, self.entities
                )
            # Render tile editor UI
            self.tiles_editor.view.render(
                self.screen,
                self.camera,
                self.map
            )
            # Mostrar indicador de capa encima del jugador si estamos en modo brush
            if self.tiles_editor.editor_state.current_tool == "brush":
                player = self.entities.player
                # Convertir coords de mundo a pantalla
                sx = (player.x - self.camera.offset_x) * self.camera.zoom
                sy = (player.y - self.camera.offset_y) * self.camera.zoom
                font = pygame.font.SysFont("Arial", 14)
                layer_name = self.tiles_editor.editor_state.current_layer.name
                text_surf = font.render(layer_name, True, (255, 255, 255))
                text_rect = text_surf.get_rect(center=(sx, sy - 20))
                bg_rect = text_rect.inflate(8, 8)
                pygame.draw.rect(self.screen, (0, 0, 0), bg_rect)
                self.screen.blit(text_surf, text_rect)
                self._dirty_rects.extend([bg_rect, text_rect])
        # Render Building Editor UI
        if self.buildings_editor.editor_state.active:
            self.buildings_editor.view.render(
                self.screen,
                self.camera,
                self.entities.buildings
            )

    def _render_effects(self, camera, screen, effects):
        dirty_rects = effects.render(screen, camera)
        self._dirty_rects.extend(dirty_rects)

    def _render_map(self, camera, screen, map):
        # Collision-only mode: render only collision grid
        if self.tiles_editor.editor_state.active and self.tiles_editor.editor_state.show_collisions and not self.tiles_editor.editor_state.show_collisions_overlay:
            dirty = self._render_collisions(screen, camera, map)
            self._dirty_rects.extend(dirty)
            return
        # Layer visibility filter when tile editor is active
        editor_state = getattr(self.tiles_editor, 'editor_state', None)
        if editor_state and editor_state.active and hasattr(editor_state, 'visible_layers'):
            visible = editor_state.visible_layers
            # Only invalidate cache on visibility change
            if visible != self._last_visible_layers:
                self.map.view.invalidate_cache()
                self._last_visible_layers = visible.copy()
            orig = map.tiles_by_layer
            # apply filter without modifying original
            filtered = {layer: orig[layer] for layer in orig if visible.get(layer, True)}
            map.tiles_by_layer = filtered
            dirty_rects = self.map.view.render(screen, camera, map)
            map.tiles_by_layer = orig
        else:
            dirty_rects = self.map.view.render(screen, camera, map)
        self._dirty_rects.extend(dirty_rects)
        # Overlay collision grid in overlay mode
        if self.tiles_editor.editor_state.active and self.tiles_editor.editor_state.show_collisions_overlay:
            dirty2 = self._render_collisions(screen, camera, map)
            self._dirty_rects.extend(dirty2)

    def _render_tile_editor_layer(self, state, screen, camera, map):
        if getattr(state, "tile_editor_state", None) and state.tile_editor_state.active:
            state.tile_editor_view.render(screen, camera, map)

    def _render_z_entities(self, state, camera, screen, entities):
        all_entities = []
        all_entities.extend([
            e for e in entities.obstacles
            if camera.is_in_view(e.x, e.y, getattr(e, "sprite_size", (64, 64)))
        ])
        all_entities.extend([
            e for e in entities.enemies
            if camera.is_in_view(e.x, e.y, e.sprite_size)
        ])
        if camera.is_in_view(entities.player.x, entities.player.y, entities.player.sprite_size):
            all_entities.append(entities.player)
        # Only render buildings if tile editor not active hiding mode
        if not (self.tiles_editor.editor_state.active and not self.tiles_editor.editor_state.show_buildings):
            for b in entities.buildings:
                if not camera.is_in_view(b.x, b.y, b.image.get_size()):
                    continue
                for part in b.get_parts():
                    state.z_state.set(part, part.z)
                    all_entities.append(part)

        render_z_ordered(all_entities, screen, camera, state.z_state)

    def _render_menu(self, screen, menu):
        if menu.show_menu:
            menu_rect = menu.draw(screen)
            self._dirty_rects.append(menu_rect)

    def _render_minimap(self, screen):
            rect = self.minimap.render(screen)
            self._dirty_rects.append(rect)

    def _render_collisions(self, screen, camera, map):
        """Render collision grid (# solid, . walkable) efficiently"""
        dirty = []
        sw, sh = screen.get_size()
        tile_sz = TILE_SIZE
        zoom = camera.zoom
        x_off = camera.offset_x
        y_off = camera.offset_y
        # Determine visible tile range
        col_start = max(0, int(x_off / tile_sz))
        row_start = max(0, int(y_off / tile_sz))
        col_end = min(len(map.tiles[0]), int((x_off + sw / zoom) / tile_sz) + 1)
        row_end = min(len(map.tiles), int((y_off + sh / zoom) / tile_sz) + 1)
        # Regenerate text surfaces only on zoom change
        if zoom != self._collision_last_zoom:
            size = max(1, int(14 * zoom))
            self._collision_font = pygame.font.SysFont("Arial", size)
            self._collision_surf_solid = self._collision_font.render('#', True, (255, 0, 0))
            self._collision_surf_walkable = self._collision_font.render('.', True, (200, 200, 200))
            self._collision_last_zoom = zoom
        # Draw only visible tiles
        for r in range(row_start, row_end):
            for c in range(col_start, col_end):
                tile = map.tiles[r][c]
                surf = self._collision_surf_solid if getattr(tile, 'solid', False) else self._collision_surf_walkable
                sx = int((c * tile_sz - x_off) * zoom)
                sy = int((r * tile_sz - y_off) * zoom)
                # Center collision symbol in tile
                text_rect = surf.get_rect()
                text_rect.center = (sx + tile_sz * zoom / 2, sy + tile_sz * zoom / 2)
                screen.blit(surf, text_rect.topleft)
                dirty.append(text_rect)
        return dirty