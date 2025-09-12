import pygame
import logging
from roguelike_engine.utils.mouse import draw_mouse_crosshair
from roguelike_engine.utils.benchmark import benchmark
from roguelike_engine.diagnostics import DiagnosticsOverlay, render_diagnostics_overlay
from roguelike_engine.config.config_tiles import TILE_SIZE
import roguelike_engine.config.config as config
from types import SimpleNamespace
from roguelike_ui.ui_blocker import clear_blockers

# Sistema de orden Z
from roguelike_engine.z_layer.render import render_z_ordered

# Importar el decorador centralizado de benchmark
from roguelike_engine.zone.zone_view import ZoneView


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
        self.diagnostics_overlay = DiagnosticsOverlay(perf_log=perf_log)
        self.zone_view = ZoneView()
        self.minimap = minimap
        self.ecs = ecs

        # FSM editor UI (lazy)
        self._fsm_title_controller = None

        self._last_state = None  # almacenar último estado para editor
        
        self._last_visible_layers = None # Cache last visible layers to minimize cache invalidations
        self._last_map_visible_layers = None # Cache for map editor visible layers
        self._collision_last_zoom = None # Collision view cache: regenerate surfaces only when zoom changes
        self._collision_font = None
        self._collision_surf_solid = None
        self._collision_surf_walkable = None
        # Debug systems (lazy init)
        self._hitbox_debug_system = None
        self._spell_debug_system = None
        self._patrol_debug_system = None
        self._defend_debug_system = None
        # Debug logging state caches to avoid spam
        self._last_render_debug_key = None
        self._last_current_layer = None
        self._last_collision_only = None

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
        # Sync latest references in case the game swapped map/entities (e.g., load/save)
        # This ensures editor redraw paths that use self.map/self.entities point to current objects
        if map is not None:
            self.map = map
        if entities is not None:
            self.entities = entities

        @benchmark(perf_log, "3.0. init_and_cleaning")
        def _init_and_cleaning():
            screen.fill((0, 0, 0))
            self._dirty_rects = []
            clear_blockers()
        _init_and_cleaning()

        # 1) Map
        @benchmark(perf_log, "3.1. map")
        def _bench_map():
            try:
                logger = logging.getLogger(__name__)
                es = getattr(self.tiles_editor, 'editor_state', None)
                tc = es.toolbar_state if es else None
                key = (
                    bool(es and es.active),
                    bool(getattr(tc, 'show_collisions', False)),
                    bool(getattr(tc, 'show_collisions_overlay', False)),
                    getattr(es, 'current_tool', None),
                    round(float(getattr(camera, 'zoom', 1.0)), 2),
                )
                if key != self._last_render_debug_key:
                    if key[0]:
                        logger.debug(
                            "[Render] TileEditor active: collisions=%s overlay=%s tool=%s zoom=%.2f",
                            key[1], key[2], key[3], key[4]
                        )
                    else:
                        logger.debug("[Render] TileEditor inactive; zoom=%.2f", key[4])
                    self._last_render_debug_key = key
            except Exception:
                pass
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

        # Debug overlays for hitboxes, spells, and patrols (F9 toggles config.DEBUG)
        @benchmark(perf_log, "3.55. spell_debug")
        def _bench_spell_debug():
            if getattr(config, "DEBUG", False):
                try:
                    # Lazy import and instantiate debug systems
                    if self._hitbox_debug_system is None:
                        from roguelike_game.ecs.systems.rendering.hitbox_debug_system import HitboxDebugSystem
                        self._hitbox_debug_system = HitboxDebugSystem(perf_log=perf_log)
                    if self._spell_debug_system is None:
                        from roguelike_game.ecs.systems.rendering.spell_collision_debug_system import SpellCollisionDebugSystem
                        self._spell_debug_system = SpellCollisionDebugSystem(perf_log=perf_log)
                    if self._patrol_debug_system is None:
                        from roguelike_game.ecs.systems.rendering.patrol_debug_system import PatrolDebugSystem
                        self._patrol_debug_system = PatrolDebugSystem(perf_log=perf_log)
                    if self._defend_debug_system is None:
                        from roguelike_game.ecs.systems.rendering.defend_area_debug_system import DefendAreaDebugSystem
                        self._defend_debug_system = DefendAreaDebugSystem(perf_log=perf_log)
                    world = self.ecs.ecs_world
                    # Draw hitbox arcs and colliders, then spell-specific collision hints
                    self._hitbox_debug_system.update(world, screen, camera)
                    self._spell_debug_system.update(world, screen, camera)
                    # Draw patrol areas/targets for NPCs with PatrolRoute
                    self._patrol_debug_system.update(world, screen, camera)
                    # Draw defend area circles for NPCs with DefendArea
                    self._defend_debug_system.update(world, screen, camera)
                except Exception:
                    # Never break main render due to optional debug overlays
                    pass
        _bench_spell_debug()

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
                and not self.buildings_editor.editor_state.active
                and not self.map_editor.editor_state.active
                and not (hasattr(state, 'entities_editor_state') and state.entities_editor_state.visible)
                and not (hasattr(state, 'inventory_editor_state') and getattr(state.inventory_editor_state, 'visible', False))
                and not (hasattr(state, 'item_editor_state') and getattr(state.item_editor_state, 'visible', False))
                and not getattr(state, 'spells_editor_visible', False)
                and not getattr(state, 'fsm_editor_visible', False)
                and not getattr(state, 'class_selector_visible', False)
                and not (menu and getattr(menu, 'show_menu', False))
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
        render_diagnostics_overlay(self.diagnostics_overlay, screen, state, camera, self.map, debug_entities, show_borders=True)
        # Resaltar área de expansión de dungeon
        self._render_expand_area(self._last_state)

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

        # Render Building Editor UI (use manager to include toolbar rendering)
        if self.buildings_editor.editor_state.active:
            self.buildings_editor.render(
                self.screen,
                self.camera,
                self.entities.buildings
            )
        # Render Map Editor UI
        if self.map_editor.editor_state.active:
            self.map_editor.render(self.screen, self.camera, self.map)

        # FSM Editor Title: mostrar cuando el FSM Editor está activo (F12)
        try:
            import roguelike_engine.config.config as config
            if getattr(config, "DEBUG_ENTITIES", False):
                self._render_fsm_editor_ui(self.screen)
        except Exception:
            # Evitar romper el render si la UI FSM falla
            pass

        # FSM Editor full UI render (lazy; draws when visible)
        try:
            # Lazy import to avoid circular deps on startup
            from roguelike_editors.fsm.fsm_editor_events import FsmEditorEventHandler
            FsmEditorEventHandler.render(self.screen)
        except Exception:
            # Never break main render if optional UI fails
            pass

    def _render_effects(self, camera, screen, effects):
        dirty_rects = effects.render(screen, camera)
        self._dirty_rects.extend(dirty_rects)

    def _render_map(self, camera, screen, map):
        # Filter tile layers in Map Editor mode using visible_layers state
        if self.map_editor.editor_state.active:
            visible = self.map_editor.editor_state.visible_layers
            # Invalidate cache and log only on visibility change
            if visible != self._last_map_visible_layers:
                map.view.invalidate_cache()
                self._last_map_visible_layers = visible.copy()
                try:
                    logger = logging.getLogger(__name__)
                    vis_names = {getattr(k, 'name', str(k)): v for k, v in visible.items()}
                    logger.debug("[Render][MapEditor] visible_layers=%s", vis_names)
                except Exception:
                    pass
            orig = map.tiles_by_layer
            filtered = {layer: tiles for layer, tiles in orig.items() if visible.get(layer, True)}
            map.tiles_by_layer = filtered
            try:
                dirty_rects = map.view.render(screen, camera, map)
            finally:
                map.tiles_by_layer = orig
            self._dirty_rects.extend(dirty_rects)
            return
        # Collision-only mode: render only collision grid (log only on toggle)
        co_mode = (
            self.tiles_editor.editor_state.active
            and self.tiles_editor.editor_state.toolbar_state.show_collisions
            and not self.tiles_editor.editor_state.toolbar_state.show_collisions_overlay
        )
        last_co = getattr(self, '_last_collision_only', None)
        if co_mode and co_mode != last_co:
            try:
                logging.getLogger(__name__).debug("[Render] Collision-only mode active -> skipping tile layers")
            except Exception:
                pass
        if co_mode:
            dirty = self._render_collisions(screen, camera, map)
            self._dirty_rects.extend(dirty)
            self._last_collision_only = co_mode
            return
        # Layer visibility filter when tile editor is active
        editor_state = getattr(self.tiles_editor, 'editor_state', None)
        if editor_state and editor_state.active:
            visible = editor_state.toolbar_state.visible_layers
            # Only invalidate cache on visibility change
            if visible != self._last_visible_layers:
                map.view.invalidate_cache()
                self._last_visible_layers = visible.copy()
                try:
                    logger = logging.getLogger(__name__)
                    vis_names = {getattr(k, 'name', str(k)): v for k, v in visible.items()}
                    logger.debug("[Render][TilesEditor] visible_layers=%s", vis_names)
                except Exception:
                    pass
            # Log current layer only when it changes
            try:
                current_layer = getattr(editor_state, 'current_layer', None)
                if current_layer != self._last_current_layer:
                    logging.getLogger(__name__).debug("[Render][TilesEditor] current_layer=%s", current_layer)
                    self._last_current_layer = current_layer
            except Exception:
                pass
            # Temporarily filter map layers mapping for rendering
            orig_layers = map.layers
            filtered_layers = {layer: orig_layers[layer] for layer in orig_layers if visible.get(layer, True)}
            map.layers = filtered_layers
            dirty_rects = map.view.render(screen, camera, map)
            map.layers = orig_layers
        else:
            dirty_rects = map.view.render(screen, camera, map)
        self._dirty_rects.extend(dirty_rects)
        # Update collision-only toggle state when not in collision-only mode
        if getattr(self, '_last_collision_only', None) != co_mode:
            self._last_collision_only = co_mode
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
            # Determine if Spawner Editor is active to gate editor_hidden
            spawner_editor_active = False
            try:
                w = self.ecs.ecs_world
                spawner_editor_active = bool(getattr(getattr(w, 'state', None), 'spawner_editor_active', False))
            except Exception:
                spawner_editor_active = False
            for b in entities.buildings:
                # Respect editor/runtime visibility flags and basic visibility toggle
                try:
                    # editor_hidden only applies while spawner editor is active; runtime_hidden always applies
                    if (spawner_editor_active and getattr(b, 'editor_hidden', False)) or getattr(b, 'runtime_hidden', False):
                        continue
                except Exception:
                    pass
                try:
                    if hasattr(b, 'visible') and not getattr(b, 'visible', True):
                        continue
                except Exception:
                    pass
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

    def _render_fsm_editor_ui(self, screen):
        """Renderiza el título del editor FSM usando TitleBar reutilizable."""
        try:
            # Import perezoso para evitar dependencias circulares en importación
            from roguelike_editors.fsm.fsm_title.fsm_title_model import FsmTitleModel
            from roguelike_editors.fsm.fsm_title.fsm_title_controller import FsmTitleController
            if self._fsm_title_controller is None:
                self._fsm_title_controller = FsmTitleController(
                    editor_state=None,
                    model=FsmTitleModel(),
                    font=None,
                )
            # Dibuja y devuelve rect (no usado aquí)
            self._fsm_title_controller.render(screen)
        except Exception:
            # Silencioso: no bloquear el render principal por UI opcional
            pass

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