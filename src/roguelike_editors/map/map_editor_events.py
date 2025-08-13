import pygame
import os
import json
import logging
logger = logging.getLogger(__name__)
from pygame.locals import *
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config import DATA_DIR, ASSETS_DIR
from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_to_json
from roguelike_engine.map.model.overlay.overlay_manager import load_layers, save_layers
from roguelike_game.ecs.core.spatial_index import SpatialIndex
from roguelike_ui.ui_blocker import is_blocked


class MapEditorEventHandler:
    """
    Maneja eventos para el Map Editor, organizado en:
      1. Ciclo de ejecución asíncrona de herramientas
      2. Captura de eventos de Pygame: zoom, panning, teclado y ratón
      3. Handlers privados para cada herramienta asíncrona
      4. Handlers privados para cada modo de clic y confirmación
      5. Helpers generales para coordenadas y persistencia
    """

    def __init__(self, manager, state, controller, map_manager):
        self.manager = manager
        self.state = state
        self.controller = controller
        self.map_manager = map_manager

    def handle(self, camera, map_manager, events=None):
        # 1. Ciclo de ejecución asíncrona
        if self.state.executing_tool:
            self._process_async_tool(camera)
            return

        # 2. Procesar eventos de Pygame
        # Pan continuo con flechas del teclado (independiente de eventos discretos)
        self._handle_keyboard_pan(camera)
        ev_iter = events if events is not None else pygame.event.get()
        for ev in ev_iter:
            # Delegar eventos al widget del toolbar (arrastre con botón derecho, etc.)
            try:
                self.controller.toolbar.view.handle_event(ev)
            except Exception:
                pass
            if ev.type == pygame.QUIT:
                self.manager.game.state.running = False
                continue

            if ev.type == pygame.MOUSEWHEEL:
                self._handle_zoom(ev, camera)
                continue

            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button in (2, 3):
                # Evitar conflicto con UI (toolbar, diálogos, etc.)
                mx, my = ev.pos
                if not is_blocked(mx, my):
                    self._start_panning(ev, camera)
                    continue

            if ev.type == pygame.MOUSEBUTTONUP and ev.button in (2, 3):
                self.state.panning = False
                continue

            if ev.type == pygame.MOUSEMOTION:
                # Actualizar panning activo
                if self.state.panning:
                    self._update_panning(ev, camera)
                    continue
                # Fallback: iniciar panning si se detecta botón medio o derecho sostenido durante el movimiento
                buttons = getattr(ev, "buttons", None)
                if buttons and len(buttons) >= 3 and (buttons[1] or buttons[2]):
                    mx, my = ev.pos
                    if not is_blocked(mx, my):
                        self.state.panning = True
                        self.state.pan_start_mouse = ev.pos
                        self.state.pan_start_offset = (camera.offset_x, camera.offset_y)
                        self._update_panning(ev, camera)
                        continue

            if ev.type == pygame.KEYDOWN:
                # Modo renombrar
                if self.state.renaming_zone:
                    if self._handle_renaming_keys(ev):
                        continue

                # Teclas de atajo globales
                if ev.key == pygame.K_F11:
                    self.manager.toggle()
                    continue
                if ev.key == pygame.K_ESCAPE:
                    self.manager.game.state.running = False
                    continue
                if ev.key == pygame.K_n:
                    self.controller.duplicate_zone()
                    continue
                if ev.key == pygame.K_l:
                    self.controller.load_zones()
                    continue
                if ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
                    self.controller.save_zones()
                    continue
                if ev.key == pygame.K_d:
                    self.controller.delete_zone()
                    continue
                if ev.key == pygame.K_h and self.state.selected_zone:
                    self.controller.toggle_hide_zone(self.state.selected_zone)
                    continue

            # Modo renombrar con clic
            if self.state.renaming_zone and ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                if self._handle_renaming_click(ev):
                    continue

            # Clic izquierdo para interacciones generales
            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                # Toolbar
                if self.controller.toolbar.handle_click(ev.pos):
                    continue

                # Si el clic cae dentro de un panel bloqueante (fondo del toolbar), consumirlo
                mx, my = ev.pos
                if is_blocked(mx, my):
                    continue

                # Confirmaciones de diálogos
                if self._handle_confirmation_dialogs(ev):
                    continue

                # Modos de clic según state (añadir, borrar, pintar)
                if self._handle_mode_clicks(ev, camera):
                    continue

                # Selección y detección de doble-clic en zona
                if self._handle_zone_selection(ev, camera):
                    continue

    # -------------------------------------------------------------
    # 1. EJECUCIÓN ASÍNCRONA DE HERRAMIENTAS
    # -------------------------------------------------------------
    def _process_async_tool(self, camera):
        tool = self.state.executing_tool
        if tool == "paint_tiles":
            self._handle_paint_tiles_execution(camera)
        elif tool == "clear_colliders":
            self._handle_clear_colliders_execution()
        elif tool == "paint_colliders":
            self._handle_paint_colliders_execution()

    def _handle_paint_tiles_execution(self, camera):
        idx = self.state.execution_index
        zone = self.state.executing_zone
        if idx < self.state.execution_total:
            tile = self.state.execution_list[idx]
            self._apply_tile_overlay(tile)
            self._apply_ground_overlay(tile)
            self.state.execution_index += 1
        else:
            self._finalize_paint_tiles(zone)
            self._clear_async_state()

    def _apply_tile_overlay(self, tile):
        orig = tile.tile_type
        tile.overlay_code = self.state.tile_code
        tile.sprite = get_sprite_for_tile(orig, tile.overlay_code)
        tile.scaled_cache.clear()

    def _apply_ground_overlay(self, tile):
        tx = tile.x // TILE_SIZE
        ty = tile.y // TILE_SIZE
        ground_layer = self.map_manager.tiles_by_layer.get(Layer.Ground)
        if ground_layer and 0 <= ty < len(ground_layer) and 0 <= tx < len(ground_layer[0]):
            gt = ground_layer[ty][tx]
            orig2 = gt.tile_type
            gt.overlay_code = tile.overlay_code
            gt.sprite = get_sprite_for_tile(orig2, gt.overlay_code)
            gt.scaled_cache.clear()

    def _finalize_paint_tiles(self, zone):
        layers = load_layers(zone)
        off_x, off_y = global_map_settings.zone_offsets.get(zone)
        wz, hz = global_map_settings.zone_size
        grid = [["" for _ in range(wz)] for _ in range(hz)]
        for t in self.map_manager.tiles_by_zone.get(zone, []):
            lx = t.x // TILE_SIZE - off_x
            ly = t.y // TILE_SIZE - off_y
            if 0 <= lx < wz and 0 <= ly < hz:
                grid[ly][lx] = t.overlay_code
        layers[Layer.Ground] = grid
        save_layers(zone, layers)
        logger.debug(f"DEBUG: persisted overlay for zone {zone}")
        self.map_manager.view.invalidate_cache()

    def _handle_clear_colliders_execution(self):
        idx = self.state.execution_index
        zone = self.state.executing_zone
        if idx < self.state.execution_total:
            self.state.execution_index += 1
        else:
            self._finalize_clear_colliders(zone)
            self._clear_async_state()

    def _finalize_clear_colliders(self, zone):
        w, h = global_map_settings.zone_size
        grid = [["#" for _ in range(w)] for _ in range(h)]
        path = os.path.join(DATA_DIR, "collisions", f"{zone}.json")
        try:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(grid, f, indent=2)
            logger.debug(f"DEBUG [MapEditorEventHandler] cleared colliders for zone {zone}")
        except Exception as e:
            logger.debug(f"DEBUG [MapEditorEventHandler] failed to clear colliders for zone {zone}: {e}")
        self.map_manager.reload_map()
        self.manager.game.ecs.ecs_world.spatial_index = SpatialIndex(
            self.map_manager, self.manager.game.buildings.buildings
        )

    def _handle_paint_colliders_execution(self):
        idx = self.state.execution_index
        zone = self.state.executing_zone
        if idx < self.state.execution_total:
            self.state.execution_index += 1
        else:
            self._finalize_paint_colliders(zone)
            self._clear_async_state()

    def _finalize_paint_colliders(self, zone):
        w, h = global_map_settings.zone_size
        grid = [["." for _ in range(w)] for _ in range(h)]
        path = os.path.join(DATA_DIR, "collisions", f"{zone}.json")
        try:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(grid, f, indent=2)
            logger.debug(f"DEBUG [MapEditorEventHandler] painted colliders for zone {zone}")
        except Exception as e:
            logger.debug(f"DEBUG [MapEditorEventHandler] failed to paint colliders for zone {zone}: {e}")
        self.map_manager.reload_map()
        self.manager.game.ecs.ecs_world.spatial_index = SpatialIndex(
            self.map_manager, self.manager.game.buildings.buildings
        )

    def _clear_async_state(self):
        self.state.executing_tool = None
        self.state.executing_zone = None
        self.state.execution_list.clear()
        self.state.execution_index = 0
        self.state.execution_total = 0
        # Limpiar tile_code si existe
        if hasattr(self.state, "tile_code"):
            self.state.tile_code = None

    # -------------------------------------------------------------
    # 2. HANDLERS DE EVENTOS DE PYGAME
    # -------------------------------------------------------------
    def _handle_zoom(self, ev, camera):
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        zoom_step = 0.1
        new_zoom = camera.zoom + zoom_step if ev.y > 0 else camera.zoom - zoom_step
        camera.zoom = max(new_zoom, 0.01)
        camera.offset_x = wx - mx / camera.zoom
        camera.offset_y = wy - my / camera.zoom

    def _start_panning(self, ev, camera):
        self.state.panning = True
        self.state.pan_start_mouse = ev.pos
        self.state.pan_start_offset = (camera.offset_x, camera.offset_y)

    def _update_panning(self, ev, camera):
        mx, my = ev.pos
        dx = (mx - self.state.pan_start_mouse[0]) / camera.zoom
        dy = (my - self.state.pan_start_mouse[1]) / camera.zoom
        # Mover la cámara en la misma dirección que las flechas del teclado
        # Flecha derecha incrementa offset_x; arrastrar a la derecha también debe incrementarlo
        camera.offset_x = self.state.pan_start_offset[0] + dx
        camera.offset_y = self.state.pan_start_offset[1] + dy

    def _handle_keyboard_pan(self, camera):
        """
        Permite mover la cámara con flechas del teclado de forma continua.
        La velocidad en pantalla se mantiene consistente mediante 1/zoom.
        """
        keys = pygame.key.get_pressed()
        dx = (1 if keys[pygame.K_RIGHT] else 0) - (1 if keys[pygame.K_LEFT] else 0)
        dy = (1 if keys[pygame.K_DOWN] else 0) - (1 if keys[pygame.K_UP] else 0)
        if dx or dy:
            step = 20 / max(camera.zoom, 0.01)  # 20 px en pantalla por frame aprox.
            camera.offset_x += dx * step
            camera.offset_y += dy * step

    def _handle_renaming_keys(self, ev) -> bool:
        if ev.key == pygame.K_RETURN:
            old_zone = self.state.renaming_zone
            new_name = self.state.rename_input.strip()
            logger.debug(f"DEBUG: renaming {old_zone} -> {new_name}")
            self.controller.rename_zone(old_zone, new_name)
            for b in self.manager.game.buildings.buildings:
                if getattr(b, "zone", None) == old_zone:
                    b.zone = new_name
                    logger.debug(f"DEBUG: building {b} zone updated from {old_zone} to {new_name}")
            save_buildings_to_json(
                self.manager.game.buildings.buildings,
                z_state=self.manager.game.z_state,
                zone_offsets=global_map_settings.zone_offsets,
            )
            logger.debug("DEBUG: persisted buildings_data.json")
            self.state.selected_zone = new_name
            self.state.renaming_zone = None
            self.state.rename_input = ""
            pygame.key.set_repeat()
            return True

        if ev.key == pygame.K_BACKSPACE:
            self.state.rename_input = self.state.rename_input[:-1]
            return True

        if ev.unicode and ev.unicode.isprintable():
            self.state.rename_input += ev.unicode
            return True

        return False

    def _handle_renaming_click(self, ev) -> bool:
        if self.state.rename_accept_rect and self.state.rename_accept_rect.collidepoint(ev.pos):
            old_zone = self.state.renaming_zone
            new_name = self.state.rename_input.strip()
            logger.debug(f"DEBUG: accept rename {old_zone} -> {new_name}")
            self.controller.rename_zone(old_zone, new_name)
            for b in self.manager.game.buildings.buildings:
                if getattr(b, "zone", None) == old_zone:
                    b.zone = new_name
                    logger.debug(f"DEBUG: building {b} zone updated from {old_zone} to {new_name}")
            save_buildings_to_json(
                self.manager.game.buildings.buildings,
                z_state=self.manager.game.z_state,
                zone_offsets=global_map_settings.zone_offsets,
            )
            logger.debug("DEBUG: persisted buildings_data.json")
            self.state.selected_zone = new_name
        self.state.renaming_zone = None
        self.state.rename_input = ""
        self.state.rename_input_rect = None
        self.state.rename_accept_rect = None
        pygame.key.set_repeat()
        return True

    

    # -------------------------------------------------------------
    # 3. HANDLERS DE DIÁLOGOS DE CONFIRMACIÓN
    # -------------------------------------------------------------
    def _handle_confirmation_dialogs(self, ev) -> bool:
        # Borrar zona
        if self.state.confirm_delete_zone:
            if self.state.confirm_yes_rect and self.state.confirm_yes_rect.collidepoint(ev.pos):
                zone = self.state.pending_delete_zone
                self.state.selected_zone = zone
                self.controller.delete_zone()
                self.state.reset_delete_dialog()
                return True
            if self.state.confirm_no_rect and self.state.confirm_no_rect.collidepoint(ev.pos):
                self.state.reset_delete_dialog()
                return True

        # Pintar tiles
        if self.state.confirm_paint_tiles:
            zone = self.state.pending_paint_tiles_zone
            if self.state.confirm_paint_yes_rect and self.state.confirm_paint_yes_rect.collidepoint(ev.pos):
                logger.debug(f"DEBUG: scheduling paint tiles for zone {zone}")
                self.state.begin_async_tool("paint_tiles", zone, self.map_manager.tiles_by_zone.get(zone, []))
                self.state.reset_paint_tiles_dialog()
                self.state.tile_code = "floor"
                return True
            if self.state.confirm_paint_no_rect and self.state.confirm_paint_no_rect.collidepoint(ev.pos):
                logger.debug("DEBUG: canceled paint tiles")
                self.state.reset_paint_tiles_dialog()
                return True

        # Vaciar colliders
        if self.state.confirm_clear_colliders:
            zone = self.state.pending_clear_colliders_zone
            if self.state.confirm_clear_colliders_yes_rect and self.state.confirm_clear_colliders_yes_rect.collidepoint(ev.pos):
                self.state.begin_async_tool("clear_colliders", zone, self.map_manager.tiles_by_zone.get(zone, []))
                self.state.reset_clear_colliders_dialog()
                return True
            if self.state.confirm_clear_colliders_no_rect and self.state.confirm_clear_colliders_no_rect.collidepoint(ev.pos):
                self.state.reset_clear_colliders_dialog()
                return True

        # Pintar colliders
        if self.state.confirm_paint_colliders:
            zone = self.state.pending_paint_colliders_zone
            if self.state.confirm_paint_colliders_yes_rect and self.state.confirm_paint_colliders_yes_rect.collidepoint(ev.pos):
                self.state.begin_async_tool("paint_colliders", zone, self.map_manager.tiles_by_zone.get(zone, []))
                self.state.reset_paint_colliders_dialog()
                return True
            if self.state.confirm_paint_colliders_no_rect and self.state.confirm_paint_colliders_no_rect.collidepoint(ev.pos):
                self.state.reset_paint_colliders_dialog()
                return True

        # Añadir zona
        if self.state.confirm_add_zone:
            tx, ty = self.state.pending_add_zone_coords
            if self.state.confirm_add_yes_rect and self.state.confirm_add_yes_rect.collidepoint(ev.pos):
                self.controller.add_zone(tx, ty)
                self.state.reset_add_zone_dialog()
                return True
            if self.state.confirm_add_no_rect and self.state.confirm_add_no_rect.collidepoint(ev.pos):
                self.state.reset_add_zone_dialog()
                return True

        return False

    # -------------------------------------------------------------
    # 4. HANDLERS DE CLIC SEGÚN MODO
    # -------------------------------------------------------------
    def _handle_mode_clicks(self, ev, camera) -> bool:
        # Obtener coordenadas de clic en world y grid
        world_x, world_y = self._screen_to_world(ev.pos, camera)
        tx = int(world_x) // TILE_SIZE
        ty = int(world_y) // TILE_SIZE

        # Modo: Añadir zona
        if self.state.add_zone_mode:
            self.state.pending_add_zone_coords = (tx, ty)
            self.state.confirm_add_zone = True
            self.state.add_zone_mode = False
            return True

        # Modo: Borrar zona
        if self.state.delete_zone_mode:
            for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                w, h = global_map_settings.zone_size
                if ox <= tx < ox + w and oy <= ty < oy + h:
                    self.state.pending_delete_zone = zn
                    self.state.confirm_delete_zone = True
                    self.state.delete_zone_mode = False
                    return True

        # Modo: Pintar tiles
        if self.state.paint_tiles_mode:
            for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                w, h = global_map_settings.zone_size
                if ox <= tx < ox + w and oy <= ty < oy + h:
                    self.state.pending_paint_tiles_zone = zn
                    self.state.confirm_paint_tiles = True
                    self.state.paint_tiles_mode = False
                    logger.debug(f"DEBUG: paint_tiles_mode: pending zone {zn}, asking confirmation")
                    return True

        # Modo: Vaciar colliders
        if self.state.clear_colliders_mode:
            for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                w, h = global_map_settings.zone_size
                if ox <= tx < ox + w and oy <= ty < oy + h:
                    self.state.pending_clear_colliders_zone = zn
                    self.state.confirm_clear_colliders = True
                    self.state.clear_colliders_mode = False
                    return True

        # Modo: Pintar colliders
        if self.state.paint_colliders_mode:
            for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                w, h = global_map_settings.zone_size
                if ox <= tx < ox + w and oy <= ty < oy + h:
                    self.state.pending_paint_colliders_zone = zn
                    self.state.confirm_paint_colliders = True
                    self.state.paint_colliders_mode = False
                    return True

        return False

    # -------------------------------------------------------------
    # 5. SELECCIÓN Y DOBLE-CLIC EN ZONA
    # -------------------------------------------------------------
    def _handle_zone_selection(self, ev, camera) -> bool:
        world_x, world_y = self._screen_to_world(ev.pos, camera)
        tx = int(world_x) // TILE_SIZE
        ty = int(world_y) // TILE_SIZE

        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            w, h = global_map_settings.zone_size
            if ox <= tx < ox + w and oy <= ty < oy + h:
                now = pygame.time.get_ticks()
                if self.state.last_click_zone == zn and now - self.state.last_click_time <= 400:
                    # Detección de doble-clic → iniciar renombrado
                    self.state.renaming_zone = zn
                    self.state.rename_input = zn
                    pygame.key.set_repeat(400, 50)
                    # Centrar cámara en la zona
                    self._center_camera_on_zone(camera, zn)
                    self.state.last_click_zone = None
                    self.state.last_click_time = 0
                    return True

                # Clic simple: seleccionar zona y preparar doble-clic
                self.state.selected_zone = zn
                self.state.last_click_zone = zn
                self.state.last_click_time = now
                return True

        return False

    def _center_camera_on_zone(self, camera, zone):
        ox, oy = global_map_settings.zone_offsets[zone]
        zw, zh = global_map_settings.zone_size
        cx = (ox * TILE_SIZE) + (zw * TILE_SIZE) / 2
        cy = (oy * TILE_SIZE) + (zh * TILE_SIZE) / 2
        camera.offset_x = cx - camera.screen_width / (2 * camera.zoom)
        camera.offset_y = cy - camera.screen_height / (2 * camera.zoom)

    # -------------------------------------------------------------
    # 6. HELPERS GENERALES
    # -------------------------------------------------------------
    def _screen_to_world(self, pos, camera) -> tuple[float, float]:
        """
        Convierte coordenadas de pantalla (x, y) a coordenadas de mundo,
        aplicando zoom y offset de cámara.
        """
        mx, my = pos
        return mx / camera.zoom + camera.offset_x, my / camera.zoom + camera.offset_y