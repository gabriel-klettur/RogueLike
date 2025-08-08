import pygame
from roguelike_engine.utils.mouse import draw_mouse_crosshair
from roguelike_engine.utils.benchmark import benchmark
from roguelike_engine.debuger.debug import DebugOverlay, render_debug_overlay
from roguelike_engine.config.config_tiles import TILE_SIZE
import roguelike_engine.config.config as config
from types import SimpleNamespace
from roguelike_ui.ui_blocker import clear_blockers

# Sistema de orden Z
from roguelike_engine.z_layer.render import render_z_ordered

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
        map_editor,
        perf_log,
        minimap,
        ecs
    ):
        self.screen = screen
        self.camera = camera
        self.map = map
        self.entities = entities
        self.buildings_editor = buildings_editor
        self.tiles_editor = tiles_editor
        self.map_editor = map_editor
        self._dirty_rects = []        
        self.debug_overlay = DebugOverlay(perf_log=perf_log)
        self.zone_view = ZoneView()
        self.minimap = minimap
        self.ecs = ecs

        self._last_state = None  # almacenar último estado para editor
        
        self._last_visible_layers = None # Cache last visible layers to minimize cache invalidations
        self._last_map_visible_layers = None # Cache for map editor visible layers
        self._collision_last_zoom = None # Collision view cache: regenerate surfaces only when zoom changes
        self._collision_font = None
        self._collision_surf_solid = None
        self._collision_surf_walkable = None
        # Cache para help overlay: (mode_key, screen_size) -> (surface, rect)
        self._help_overlay_key = None
        self._help_overlay_surf = None

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
            clear_blockers()
        _init_and_cleaning()

        # 1) Map
        @benchmark(perf_log, "3.1. map")
        def _bench_map():
            self._render_map(camera, screen, map)
        _bench_map()

        # 5) ECS trail snapshots
        @benchmark(perf_log, "3.5. ecs_trail")
        def _bench_ecs_trail():
            for eid, trail in self.ecs.ecs_world.components.get('TrailComponent', {}).items():
                for snap in trail.snapshots:
                    orig = snap.image
                    zoom = camera.zoom
                    if zoom != 1.0:
                        w, h = orig.get_size()
                        image_scaled = pygame.transform.scale(orig, (int(w * zoom), int(h * zoom)))
                    else:
                        image_scaled = orig
                    screen.blit(image_scaled, camera.apply(snap.pos))
        _bench_ecs_trail()

        # 2) Entidades orden Z
        @benchmark(perf_log, "3.2. z_entities")
        def _bench_z_entities():
            # Skip entity rendering in collision-only mode
            if not (self.tiles_editor.editor_state.active and self.tiles_editor.editor_state.toolbar_state.show_collisions and not self.tiles_editor.editor_state.toolbar_state.show_collisions_overlay):
                self._render_z_entities(state, camera, screen, entities)
        _bench_z_entities()

        # 4) Capa del Tile Editor
        @benchmark(perf_log, "3.4. tile_editor")
        def _bench_tile_editor():
            # Skip tile editor UI in collision-only mode
            if not (self.tiles_editor.editor_state.active and self.tiles_editor.editor_state.toolbar_state.show_collisions and not self.tiles_editor.editor_state.toolbar_state.show_collisions_overlay):
                self._render_tile_editor_layer(state, screen, camera, map)
        _bench_tile_editor()

        # 6) Crosshair
        @benchmark(perf_log, "3.6. crosshair")
        def _bench_crosshair():
            draw_mouse_crosshair(screen, camera)
        _bench_crosshair()

        # 7) Menú
        @benchmark(perf_log, "3.7. menu")
        def _bench_menu():
            self._render_menu(screen, menu)
        _bench_menu()

        # 8) Minimap
        @benchmark(perf_log, "3.8. minimap")
        def _bench_minimap():
            if (
                not self.tiles_editor.editor_state.active
                and not (hasattr(state, 'entities_editor_state') and state.entities_editor_state.visible)
                and not (hasattr(state, 'inventory_editor_state') and getattr(state.inventory_editor_state, 'visible', False))
            ):
                self._render_minimap(screen)
        _bench_minimap()

        # 11) Editores
        @benchmark(perf_log, "3.11. editors")
        def _bench_editors():
            self._render_editors()
        _bench_editors()


        # Debug: overlay y bordes
        debug_entities = SimpleNamespace(player=self.ecs.ecs_world.player_position)
        render_debug_overlay(self.debug_overlay, screen, state, camera, self.map, debug_entities, show_borders=True)
        # Resaltar área de expansión de dungeon
        self._render_expand_area(self._last_state)
        # Mostrar ayuda de controles según el modo
        self._render_help_overlay(state)

        # Reemplazar dirty rects por flip completo para rendimiento constante
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

        # Render Building Editor UI
        if self.buildings_editor.editor_state.active:
            self.buildings_editor.view.render(
                self.screen,
                self.camera,
                self.entities.buildings
            )
        # Render Map Editor UI
        if self.map_editor.editor_state.active:
            self.map_editor.render(self.screen, self.camera, self.map)

    def _render_effects(self, camera, screen, effects):
        dirty_rects = effects.render(screen, camera)
        self._dirty_rects.extend(dirty_rects)

    def _render_map(self, camera, screen, map):
        # Filter tile layers in Map Editor mode using visible_layers state
        if self.map_editor.editor_state.active:
            # Invalidate cache on layer visibility change
            visible = self.map_editor.editor_state.visible_layers
            if visible != self._last_map_visible_layers:
                self.map.view.invalidate_cache()
                self._last_map_visible_layers = visible.copy()
            orig = map.tiles_by_layer
            filtered = {layer: tiles for layer, tiles in orig.items() if visible.get(layer, True)}
            map.tiles_by_layer = filtered
            try:
                dirty_rects = self.map.view.render(screen, camera, map)
            finally:
                map.tiles_by_layer = orig
            self._dirty_rects.extend(dirty_rects)
            return
        # Collision-only mode: render only collision grid
        if self.tiles_editor.editor_state.active and self.tiles_editor.editor_state.toolbar_state.show_collisions and not self.tiles_editor.editor_state.toolbar_state.show_collisions_overlay:
            dirty = self._render_collisions(screen, camera, map)
            self._dirty_rects.extend(dirty)
            return
        # Layer visibility filter when tile editor is active
        editor_state = getattr(self.tiles_editor, 'editor_state', None)
        if editor_state and editor_state.active:
            visible = editor_state.toolbar_state.visible_layers
            # Only invalidate cache on visibility change
            if visible != self._last_visible_layers:
                self.map.view.invalidate_cache()
                self._last_visible_layers = visible.copy()
            # Temporarily filter map layers mapping for rendering
            orig_layers = map.layers
            filtered_layers = {layer: orig_layers[layer] for layer in orig_layers if visible.get(layer, True)}
            map.layers = filtered_layers
            dirty_rects = self.map.view.render(screen, camera, map)
            map.layers = orig_layers
        else:
            dirty_rects = self.map.view.render(screen, camera, map)
        self._dirty_rects.extend(dirty_rects)
        # Overlay collision grid in overlay mode
        if self.tiles_editor.editor_state.active and self.tiles_editor.editor_state.toolbar_state.show_collisions_overlay:
            dirty2 = self._render_collisions(screen, camera, map)
            self._dirty_rects.extend(dirty2)

    def _render_tile_editor_layer(self, state, screen, camera, map):
        if getattr(state, "tile_editor_state", None) and state.tile_editor_state.active:
            state.tile_editor_view.render(screen, camera, map)

    def _render_z_entities(self, state, camera, screen, entities):
        # Hide buildings and NPCs in Map Editor mode
        if self.map_editor.editor_state.active:
            # Draw buildings if enabled in Map Editor
            if self.map_editor.editor_state.show_buildings:
                parts = []
                for b in entities.buildings:
                    if not camera.is_in_view(b.x, b.y, b.image.get_size()):
                        continue
                    for part in b.get_parts():
                        state.z_state.set(part, part.z)
                        parts.append(part)
                render_z_ordered(parts, screen, camera, state.z_state)
            return
        all_entities = []
        # Only render buildings if not hidden by editor or collision-only mode (NPC rendering removed; gestionado por ECS)
        editor_state = self.tiles_editor.editor_state
        if not ((editor_state.active and not editor_state.toolbar_state.show_buildings)
                or (editor_state.active and editor_state.toolbar_state.show_collisions and not editor_state.toolbar_state.show_collisions_overlay)):
            for b in entities.buildings:
                if not camera.is_in_view(b.x, b.y, b.image.get_size()):
                    continue
                for part in b.get_parts():
                    state.z_state.set(part, part.z)
                    all_entities.append(part)
        # 4) NPCs ECS: envolver cada entidad y asignar capa Z
        for eid in self.ecs.ecs_world.get_entities_with('Position', 'Sprite', 'ZLayer'):
            layer = self.ecs.ecs_world.components['ZLayer'][eid].layer
            # wrapper ligero para que tenga x,y,render
            npc = _NPCWrapper(self.ecs.ecs_world, eid)
            state.z_state.set(npc, layer)
            all_entities.append(npc)

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

    def _render_help_overlay(self, state):
        # Cachea el overlay de ayuda para evitar renderizado de texto cada frame
        screen = self.screen
        size = screen.get_size()
        # Ocultar la leyenda de comandos cuando un editor de superposición está visible
        # (Entities Editor, Inventory Editor, etc.)
        if hasattr(state, 'entities_editor_state') and getattr(state.entities_editor_state, 'visible', False):
            return
        if hasattr(state, 'inventory_editor_state') and getattr(state.inventory_editor_state, 'visible', False):
            return
        if self.map_editor.editor_state.active:
            mode = 'map'
        elif self.buildings_editor.editor_state.active:
            mode = 'buildings'
        elif self.tiles_editor.editor_state.active:
            mode = 'tiles'
        elif config.DEBUG:
            mode = 'debug'
        else:
            mode = 'normal'
        key = (mode, size)
        if key != self._help_overlay_key:
            # Reconstruir overlay            
            screen_w, screen_h = size
            if mode == 'map':
                lines = ["Modo Edición Mapas:", "F11: modo", "ESC: salir",
                         "N: duplicar zona", "L: cargar zonas", "Ctrl+S: guardar zonas",
                         "D: borrar zona", "H: ocultar zona", "Click Izq: toolbar",
                         "Click Medio: arrastrar", "Rueda: zoom"]
            elif mode == 'buildings':
                lines = [
                    "Modo Edición Edificios:", "F10: modo", "P: selector edificio",
                    "ESC: salir", "D: reset", "R: redimensionar",
                    "Ctrl+S: guardar", "Ctrl+Z: deshacer", "N: aleatorio",
                    "Supr: borrar"
                ]
            elif mode == 'tiles':
                lines = ["Modo Edición Tiles:", "F8: editor tiles", "ESC: salir",
                         "B: alternar edificios", "Click Izq: pintar", "Rueda: capa",
                         "Click Der: arrastrar"]
            elif mode == 'debug':
                lines = [
                    "Debug Mode:",
                    "F9: Toggle Debug Overlay",
                    "F12: Toggle Hitbox Debug",
                    "Mouse Wheel: Scroll Overlay"
                ]
            else:
                lines = ["Normal Mode:", "ESC: Menu", "[IN DUNGEON] Red area expand dungeon", "F8:Tiles Editor", "F9: Debug Mode","F10: Buildings Editor",
                         "F11: Map Editor", "F5: Entities Editor",                         
                         "E: Slash","X: Healing", "Mouse left: Fire Ball", "Mouse right: Slash",
                         "Mouse middle: Laser Beam"
                         ]
            font = pygame.font.SysFont("Arial", 14)
            pad = 5
            texts = [font.render(l, True, (255,255,255)) for l in lines]
            lh = texts[0].get_height() if texts else 0
            bw = max((t.get_width() for t in texts), default=0) + pad*2
            bh = len(texts)*lh + pad*2
            overlay = pygame.Surface((bw, bh), flags=pygame.SRCALPHA)
            overlay.fill((0,0,0,128))
            for i, t in enumerate(texts):
                overlay.blit(t, (pad, pad + i*lh))
            rect = overlay.get_rect()
            rect.bottomright = (screen_w - pad, screen_h - pad)
            self._help_overlay_surf = (overlay, rect)
            self._help_overlay_key = key
        # Blitear overlay cacheado
        surf, rect = self._help_overlay_surf
        screen.blit(surf, rect)

    def _render_expand_area(self, state):
        """Dibuja overlay semitransparente en los 9 tiles del trigger de expansión."""
        if not hasattr(state, 'expand_area_coords'):
            return
        for tx, ty in state.expand_area_coords:
            zoom = self.camera.zoom
            x = int((tx * TILE_SIZE - self.camera.offset_x) * zoom)
            y = int((ty * TILE_SIZE - self.camera.offset_y) * zoom)
            size = int(TILE_SIZE * zoom)
            rect = pygame.Rect(x, y, size, size)
            # Red semi-transparent fill and border for visibility
            surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
            surf.fill((255, 0, 0, 150))
            self.screen.blit(surf, rect.topleft)
            pygame.draw.rect(self.screen, (255, 0, 0), rect, width=2)
            self._dirty_rects.append(rect)

class _NPCWrapper:
    """Envoltorio optimizado para renderizar NPCs dentro de render_z_ordered."""
    __slots__ = ('eid', 'pos_map', 'sprite_map', 'scale_map')
    # Cache de superficies escaladas: {(eid, scale): Surface}
    _scale_cache = {}

    def __init__(self, world, eid):
        comps = world.components
        self.eid = eid
        self.pos_map = comps['Position']
        self.sprite_map = comps['Sprite']
        self.scale_map = comps.get('Scale', {})

    @property
    def x(self):
        return self.pos_map[self.eid].x

    @property
    def y(self):
        return self.pos_map[self.eid].y

    def render(self, screen, camera):
        # Hot-path: referencias locales
        blit = screen.blit
        apply = camera.apply
        eid = self.eid
        sprite = self.sprite_map[eid]
        orig = sprite.image
        scale_comp = self.scale_map.get(eid)
        entity_scale = scale_comp.scale if scale_comp else 1.0
        scale_factor = entity_scale * camera.zoom
        if scale_factor != 1.0:
            # Quantize factor for cache key stability
            key = (eid, round(scale_factor, 2), id(orig))
            image = _NPCWrapper._scale_cache.get(key)
            if image is None:
                w, h = orig.get_size()
                image = pygame.transform.scale(orig, (int(w * scale_factor), int(h * scale_factor)))
                _NPCWrapper._scale_cache[key] = image
        else:
            image = orig
        blit(image, apply((self.x, self.y)))