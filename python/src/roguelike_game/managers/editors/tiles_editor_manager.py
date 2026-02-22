from roguelike_editors.tiles.tile_editor_state import TileEditorState
from roguelike_editors.tiles.tile_editor_controller import TileEditorController
from roguelike_editors.tiles.tile_editor_events import TileEditorEventHandler
from roguelike_editors.tiles.tile_editor_view import TileEditorView
from roguelike_engine.config.map_config import global_map_settings

import logging
logger = logging.getLogger(__name__)

class TilesEditorManager:
    def __init__(self, game):
        
        # Inicialización del editor de tiles
        self.editor_state = TileEditorState()
        self.controller   = TileEditorController(self.editor_state, self.editor_state.picker_state)
        self.view         = TileEditorView(self.controller, self.editor_state)
        self.handler      = TileEditorEventHandler(game.state, self.editor_state, self.controller)
        self.controller.ecs_world = game.ecs.ecs_world
        self.game = game  # Guardar referencia para forzar recarga de mapa

    def toggle(self):
        """Activa/desactiva el editor de tiles y limpia sub-estado al cerrarlo."""
        active = not self.editor_state.active
        self.editor_state.active = active

        # Al abrir el Tile Editor, mostrar panel tamaño y panel vista
        if active:
            # PRINT DIRECTO - DEBE APARECER EN TERMINAL
            print("\n" + "="*70)
            print("[TilesEditor] ========== OPENING TILES EDITOR ==========")
            print("="*70)
            logger.info(f"[TilesEditor] ========== OPENING TILES EDITOR ==========")
            self.editor_state.size_panel_state.visible = True
            self.editor_state.toolbar_state.view_active = True
            
            # Limpiar caches del controlador para asegurar datos frescos del mundo actual
            try:
                cache_before = len(self.controller.brush_cache)
                code_before = len(self.controller._code_cache)
                self.controller.brush_cache.clear()
                self.controller._code_cache.clear()
                logger.info(f"[TilesEditor] Cleared caches: brush={cache_before}→0, code={code_before}→0")
            except Exception as e:
                logger.error(f"[TilesEditor] Failed to clear caches: {e}")
            
            # CRÍTICO: Forzar recarga completa del mapa para asegurar tiles del mundo actual
            try:
                current_world = getattr(global_map_settings, 'current_world', '?')
                overlays_dir = getattr(global_map_settings, 'overlays_dir', '?')
                # PRINT DIRECTO
                print(f"[TilesEditor] BEFORE reload: current_world={current_world}")
                print(f"[TilesEditor] BEFORE reload: overlays_dir={overlays_dir}")
                print(f"[TilesEditor] BEFORE reload: map.tiles={len(self.game.map.tiles)}x{len(self.game.map.tiles[0]) if self.game.map.tiles else 0}")
                
                logger.info(f"[TilesEditor] BEFORE reload: current_world={current_world}")
                logger.info(f"[TilesEditor] BEFORE reload: overlays_dir={overlays_dir}")
                logger.info(f"[TilesEditor] BEFORE reload: map.tiles={len(self.game.map.tiles)}x{len(self.game.map.tiles[0]) if self.game.map.tiles else 0}")
                
                # Forzar recarga del mapa
                self.game.map.reload_map()
                
                # Invalidar vista del mapa para forzar re-renderizado
                try:
                    self.game.map.view.invalidate_cache()
                    logger.info(f"[TilesEditor] Invalidated map view cache")
                except Exception as e2:
                    logger.warning(f"[TilesEditor] Could not invalidate view cache: {e2}")
                
                # PRINT DIRECTO
                print(f"[TilesEditor] AFTER reload: map.tiles={len(self.game.map.tiles)}x{len(self.game.map.tiles[0]) if self.game.map.tiles else 0}")
                print(f"[TilesEditor] AFTER reload: map.name={self.game.map.name}")
                print("[TilesEditor] ========== TILES EDITOR READY ===========")
                print("="*70 + "\n")
                
                logger.info(f"[TilesEditor] AFTER reload: map.tiles={len(self.game.map.tiles)}x{len(self.game.map.tiles[0]) if self.game.map.tiles else 0}")
                logger.info(f"[TilesEditor] AFTER reload: map.name={self.game.map.name}")
                logger.info(f"[TilesEditor] ========== TILES EDITOR READY ==========")
            except Exception as e:
                # PRINT DIRECTO DEL ERROR
                print(f"\n❌ [TilesEditor] CRITICAL ERROR: {e}")
                import traceback
                traceback.print_exc()
                print("\n")
                logger.error(f"[TilesEditor] CRITICAL: Failed to reload map on open: {e}", exc_info=True)

        if not active:
            print("\n[TilesEditor] ========== CLOSING TILES EDITOR ==========\n")
            logger.info(f"[TilesEditor] ========== CLOSING TILES EDITOR ==========")
            # reset de selección
            self.editor_state.picker_open       = False
            self.editor_state.selected_tile     = None
            self.editor_state.current_choice    = None

        logger.info("🟩 [TilesEditor] Tile-Editor ON REAL!" if active else "🟥 [TilesEditor] Tile-Editor OFF")

    def handle(self, camera, game_map, events):
        """
        Enruta eventos al manejador del editor de tiles.
        """
        if self.editor_state.active:
            self.handler.handle(events, camera, game_map)

    def update(self, camera, game_map):
        """
        Actualiza el controlador si está activo.
        """
        if self.editor_state.active:
            self.controller.update(camera, game_map)

    def render(self, screen, camera, game_map):
        """
        Renderiza la vista del editor de tiles si está activo.
        """
        if self.editor_state.active:
            # Diagnóstico: Verificar de qué mundo es el mapa que recibimos
            try:
                if not hasattr(self, '_last_render_log_time'):
                    self._last_render_log_time = 0
                import time
                now = time.time()
                # Log cada 5 segundos para no saturar
                if now - self._last_render_log_time > 5.0:
                    current_world = getattr(global_map_settings, 'current_world', '?')
                    map_name = getattr(game_map, 'name', '?')
                    tiles_count = f"{len(game_map.tiles)}x{len(game_map.tiles[0]) if game_map.tiles else 0}"
                    logger.info(f"[TilesEditor][Render] current_world={current_world} game_map.name={map_name} tiles={tiles_count}")
                    self._last_render_log_time = now
            except Exception:
                pass
            self.view.render(screen, camera, game_map)