import logging
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.diagnostics import DiagnosticsOverlay
from roguelike_engine.zone.zone_view import ZoneView

# Delegated renderers
from .map_renderer import render_map as _render_map_impl
from .entities_renderer import render_z_entities as _render_z_entities_impl
from .collisions_overlay import CollisionGridRenderer
from .editors_renderer import render_editors as _render_editors_impl
from .pipeline_runner import run_pipeline


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
        # Debug systems (lazy init)
        self._hitbox_debug_system = None
        self._spell_debug_system = None
        self._patrol_debug_system = None
        self._defend_debug_system = None
        self._npc_attack_debug_system = None
        # Debug logging state caches to avoid spam
        self._last_render_debug_key = None
        self._last_current_layer = None
        self._last_collision_only = None
        # Externalized helpers
        self._collision_renderer = CollisionGridRenderer()

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
        return run_pipeline(self, state, screen, camera, perf_log=perf_log, menu=menu, map=map, entities=entities)

    def _render_editors(self):
        """Delegado a módulo externo para mantener el archivo ligero."""
        _render_editors_impl(self)

    def _render_effects(self, camera, screen, effects):
        dirty_rects = effects.render(screen, camera)
        self._dirty_rects.extend(dirty_rects)

    def _render_map(self, camera, screen, map):
        dirty_rects = _render_map_impl(self, camera, screen, map)
        self._dirty_rects.extend(dirty_rects)

    def _render_tile_editor_layer(self, state, screen, camera, map):
        if getattr(state, "tile_editor_state", None) and state.tile_editor_state.active:
            state.tile_editor_view.render(screen, camera, map)

    def _render_z_entities(self, state, camera, screen, entities):
        _render_z_entities_impl(self, state, camera, screen, entities)

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
        return self._collision_renderer.render(screen, camera, map)

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