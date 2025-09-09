import pygame
import logging
logger = logging.getLogger(__name__)
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config_editor import TILE_PAINT_BATCH, TILE_PAINT_TICK
from roguelike_engine.config.config_camera import ALLOWED_ZOOMS, next_allowed_zoom
from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_split

from roguelike_engine.map.model.overlay.overlay_manager import load_layers, save_layers
from roguelike_game.ecs.core.spatial_index import SpatialIndex
from roguelike_ui.ui_blocker import is_blocked
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.tile.utils.loader import get_sprite_for_tile
from roguelike_editors.map.services.overlay_service import set_overlay_cell, merge_zone_to_world
from roguelike_editors.map.commands.paint_tiles_command import PaintTilesCommand


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
            # Delegar eventos al panel de Tutorial si está activo, para bloquear clicks/teclas (ESC) sobre el panel
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL, pygame.KEYDOWN):
                try:
                    tutorial = getattr(self, 'tutorial', None)
                    if tutorial and tutorial.is_active() and tutorial.handle_event(ev):
                        continue
                except Exception:
                    pass
            if ev.type == pygame.QUIT:
                # Persist camera if the editor is active and app is quitting
                try:
                    if self.state.active and camera is not None:
                        self.manager._save_persisted_camera(camera.offset_x, camera.offset_y, camera.zoom)
                except Exception:
                    pass
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
                # Undo / Redo
                if (ev.key == pygame.K_z) and (ev.mod & pygame.KMOD_CTRL):
                    self._perform_undo(camera)
                    continue
                if (ev.key == pygame.K_y) and (ev.mod & pygame.KMOD_CTRL):
                    self._perform_redo(camera)
                    continue
                if ev.key == pygame.K_ESCAPE:
                    self.manager.game.state.running = False
                    continue
                if ev.key == pygame.K_n:
                    new_zone = self.controller.duplicate_zone()
                    if new_zone:
                        self.state.selected_zone = new_zone
                        logger.info(f"[MapEditor] Duplicated zone selected: {new_zone}")
                    continue
                if ev.key == pygame.K_l:
                    self.controller.load_zones()
                    continue
                if ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
                    self.controller.save_zones()
                    continue
                if ev.key == pygame.K_d:
                    # Open delete confirmation for the currently selected zone via tool
                    self.controller.toolbar.delete_zone.request_delete_selected()
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
            # Incremental view update: coalesce dirty cells and refresh chunks in batches
            try:
                if len(self.state.dirty_cells) >= TILE_PAINT_BATCH or (
                    self.state.execution_index % TILE_PAINT_TICK == 0 and self.state.dirty_cells
                ):
                    cells = list(self.state.dirty_cells)
                    self.map_manager.view.update_chunks(self.map_manager, camera, cells)
                    self.state.dirty_cells.clear()
            except Exception:
                # Never break the async loop on visual update errors
                pass
            # Progreso (con throttling cada 10%)
            total = max(self.state.execution_total, 1)
            percent = int((self.state.execution_index / total) * 100)
            if percent >= (self.state.last_progress_report + 10):
                elapsed = pygame.time.get_ticks() - self.state.execution_start_time
                logger.debug(
                    f"[MapEditor] Painting zone={zone} progress={percent}% "
                    f"({self.state.execution_index}/{total}) elapsed={elapsed}ms"
                )
                self.state.last_progress_report = percent
        else:
            # Flush any remaining dirty cells before finalizing
            try:
                if self.state.dirty_cells:
                    cells = list(self.state.dirty_cells)
                    self.map_manager.view.update_chunks(self.map_manager, camera, cells)
                    self.state.dirty_cells.clear()
            except Exception:
                pass
            try:
                self._finalize_paint_tiles(zone)
            except Exception as e:
                logger.exception(f"[MapEditor] Error finalizing paint tiles for zone={zone}: {e}")
            finally:
                self._clear_async_state()
                # Pulso para el tutorial (finalización de pintado)
                try:
                    setattr(self.state, 'tutorial_paint_tiles_finalized_pulse', True)
                except Exception:
                    pass

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
        # Update the in-memory world-sized Ground layer grid used by the renderer
        # and record undo/redo edit
        world = self.map_manager.layers.get(Layer.Ground)
        before = None
        if world and 0 <= ty < len(world) and 0 <= tx < len(world[0]):
            before = world[ty][tx]
        set_overlay_cell(self.map_manager, tx, ty, tile.overlay_code)
        try:
            if before != tile.overlay_code and self.state.current_command is not None:
                self.state.current_command.add_edit(ty, tx, before, tile.overlay_code)
        except Exception:
            pass
        # Track dirty cell for coalesced chunk refresh
        self.state.dirty_cells.add((ty, tx))

    def _finalize_paint_tiles(self, zone):
        start = pygame.time.get_ticks()
        layers = self.controller.zones.load_layers(zone)
        off_x, off_y = global_map_settings.zone_offsets.get(zone)
        wz, hz = global_map_settings.zone_size
        grid = [["" for _ in range(wz)] for _ in range(hz)]
        for t in self.map_manager.tiles_by_zone.get(zone, []):
            lx = t.x // TILE_SIZE - off_x
            ly = t.y // TILE_SIZE - off_y
            if 0 <= lx < wz and 0 <= ly < hz:
                grid[ly][lx] = t.overlay_code
        painted = sum(1 for row in grid for code in row if code)
        layers[Layer.Ground] = grid
        self.controller.zones.save_layers(zone, layers)
        # Merge the zone-sized grid back into the world-sized Ground layer
        merge_zone_to_world(self.map_manager, zone, grid)
        elapsed = pygame.time.get_ticks() - start
        logger.info(
            f"[MapEditor] Overlay persisted for zone={zone} layer=Ground size={wz}x{hz} "
            f"painted_cells={painted} duration={elapsed}ms"
        )
        self.map_manager.view.invalidate_cache()
        # Commit command to undo stack
        if self.state.current_command is not None:
            self.state.undo_stack.append(self.state.current_command)
            self.state.redo_stack.clear()
            self.state.current_command = None
        # Pulso para el tutorial: finalizar pintado (redundante por seguridad)
        try:
            setattr(self.state, 'tutorial_paint_tiles_finalized_pulse', True)
        except Exception:
            pass

    def _perform_undo(self, camera):
        if not self.state.undo_stack:
            return
        cmd = self.state.undo_stack.pop()
        try:
            cells = cmd.undo(self.map_manager)
            if cells:
                self.map_manager.view.update_chunks(self.map_manager, camera, cells)
        finally:
            self.state.redo_stack.append(cmd)
            # Pulso para el tutorial
            try:
                setattr(self.state, 'tutorial_undo_performed_pulse', True)
            except Exception:
                pass

    def _perform_redo(self, camera):
        if not self.state.redo_stack:
            return
        cmd = self.state.redo_stack.pop()
        try:
            cells = cmd.redo(self.map_manager)
            if cells:
                self.map_manager.view.update_chunks(self.map_manager, camera, cells)
        finally:
            self.state.undo_stack.append(cmd)
            # Pulso para el tutorial
            try:
                setattr(self.state, 'tutorial_redo_performed_pulse', True)
            except Exception:
                pass

    def _handle_clear_colliders_execution(self):
        idx = self.state.execution_index
        zone = self.state.executing_zone
        if idx < self.state.execution_total:
            self.state.execution_index += 1
        else:
            try:
                self.controller.toolbar.clear_colliders.finalize(zone)
            finally:
                # Reconstruir el índice espacial mediante la API del mundo
                self.manager.game.ecs.ecs_world.rebuild_spatial_index()
                self._clear_async_state()
                # Pulso para el tutorial: finalizado
                try:
                    setattr(self.state, 'tutorial_clear_colliders_finalized_pulse', True)
                except Exception:
                    pass

    def _handle_paint_colliders_execution(self):
        idx = self.state.execution_index
        zone = self.state.executing_zone
        if idx < self.state.execution_total:
            self.state.execution_index += 1
        else:
            try:
                self.controller.toolbar.paint_colliders.finalize(zone)
            finally:
                # Reconstruir el índice espacial mediante la API del mundo
                self.manager.game.ecs.ecs_world.rebuild_spatial_index()
                self._clear_async_state()
                # Pulso para el tutorial: finalizado
                try:
                    setattr(self.state, 'tutorial_paint_colliders_finalized_pulse', True)
                except Exception:
                    pass

    def _clear_async_state(self):
        self.state.executing_tool = None
        self.state.executing_zone = None
        self.state.execution_list.clear()
        self.state.execution_index = 0
        self.state.execution_total = 0
        # Mantener tile_code para futuras operaciones (permite repetir última selección)

    # -------------------------------------------------------------
    # 2. HANDLERS DE EVENTOS DE PYGAME
    # -------------------------------------------------------------
    def _handle_zoom(self, ev, camera):
        mx, my = pygame.mouse.get_pos()
        # World point under cursor before zoom
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        # Allowed discrete zoom scales to avoid rendering artifacts
        allowed = ALLOWED_ZOOMS
        z = float(getattr(camera, 'zoom', 1.0)) or 1.0
        # Choose next/prev scale centrally
        new_z = next_allowed_zoom(z, +1, allowed) if ev.y > 0 else next_allowed_zoom(z, -1, allowed)
        # Apply only if changed
        if abs(new_z - z) > 1e-9:
            camera.zoom = new_z
            # Keep the same world point under the cursor
            camera.offset_x = wx - mx / camera.zoom
            camera.offset_y = wy - my / camera.zoom
            # Pulso tutorial
            try:
                setattr(self.state, 'tutorial_camera_zoom_changed_pulse', True)
            except Exception:
                pass

    def _start_panning(self, ev, camera):
        self.state.panning = True
        self.state.pan_start_mouse = ev.pos
        self.state.pan_start_offset = (camera.offset_x, camera.offset_y)

    def _update_panning(self, ev, camera):
        mx, my = ev.pos
        dx = (mx - self.state.pan_start_mouse[0]) / camera.zoom
        dy = (my - self.state.pan_start_mouse[1]) / camera.zoom

        # Grab-to-pan: arrastrar el mapa hacia la derecha desplaza el contenido hacia la derecha.
        # Con la convención de render (screen = (world - offset) * zoom),
        # esto implica restar el delta al offset de cámara.
        camera.offset_x = self.state.pan_start_offset[0] - dx
        camera.offset_y = self.state.pan_start_offset[1] - dy
        # Pulso tutorial
        try:
            setattr(self.state, 'tutorial_camera_panned_pulse', True)
        except Exception:
            pass

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
            try:
                setattr(self.state, 'tutorial_camera_panned_pulse', True)
            except Exception:
                pass

    def _handle_renaming_keys(self, ev) -> bool:
        if ev.key == pygame.K_RETURN:
            old_zone = self.state.renaming_zone
            new_name = self.state.rename_input.strip()
            logger.debug(f"[MapEditor] renaming (Enter) {old_zone} -> {new_name}")
            success = self.controller.rename_zone(old_zone, new_name)
            if success:
                for b in self.manager.game.buildings.buildings:
                    if getattr(b, "zone", None) == old_zone:
                        b.zone = new_name
                        logger.debug(f"[MapEditor] building {b} zone updated from {old_zone} to {new_name}")
                save_buildings_split(
                    self.manager.game.buildings.buildings,
                    z_state=self.manager.game.z_state,
                    zone_offsets=global_map_settings.zone_offsets,
                )
                logger.debug("[MapEditor] persisted buildings split files after rename")
                self.state.selected_zone = new_name
            else:
                logger.info(f"[MapEditor] rename aborted for {old_zone} -> {new_name}")
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
            logger.debug(f"[MapEditor] accept rename click {old_zone} -> {new_name}")
            success = self.controller.rename_zone(old_zone, new_name)
            if success:
                for b in self.manager.game.buildings.buildings:
                    if getattr(b, "zone", None) == old_zone:
                        b.zone = new_name
                        logger.debug(f"[MapEditor] building {b} zone updated from {old_zone} to {new_name}")
                save_buildings_split(
                    self.manager.game.buildings.buildings,
                    z_state=self.manager.game.z_state,
                    zone_offsets=global_map_settings.zone_offsets,
                )
                logger.debug("[MapEditor] persisted buildings split files after rename")
                self.state.selected_zone = new_name
            else:
                logger.info(f"[MapEditor] rename aborted for {old_zone} -> {new_name}")
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
        # Borrar zona (delegar en herramienta)
        if self.state.confirm_delete_zone:
            if self.controller.toolbar.delete_zone.events.handle_confirm_click(ev.pos):
                return True

        # Pintar tiles
        if self.state.confirm_paint_tiles:
            zone = self.state.pending_paint_tiles_zone
            if self.state.confirm_paint_yes_rect and self.state.confirm_paint_yes_rect.collidepoint(ev.pos):
                # Establecer código de overlay antes de iniciar la ejecución
                self.state.tile_code = "floor"
                tiles = self.map_manager.tiles_by_zone.get(zone, [])
                # Pulso tutorial: confirmado
                try:
                    setattr(self.state, 'tutorial_paint_tiles_confirmed_pulse', True)
                except Exception:
                    pass
                self.state.begin_async_tool("paint_tiles", zone, tiles)
                # Initialize undo/redo command for this batch
                self.state.current_command = PaintTilesCommand(zone, self.state.tile_code)
                logger.info(
                    f"[MapEditor] Paint tiles confirmed zone={zone} count={len(tiles)} overlay={self.state.tile_code}"
                )
                self.state.reset_paint_tiles_dialog()
                return True

            if self.state.confirm_paint_no_rect and self.state.confirm_paint_no_rect.collidepoint(ev.pos):
                logger.info("[MapEditor] Paint tiles canceled")
                self.state.reset_paint_tiles_dialog()
                return True

        # Vaciar colliders
        if self.state.confirm_clear_colliders:
            # Delegate to Clear Colliders tool events
            if self.controller.toolbar.clear_colliders.events.handle_confirm_click(ev.pos):
                return True

        # Pintar colliders (delegar en herramienta)
        if self.state.confirm_paint_colliders:
            if self.controller.toolbar.paint_colliders.events.handle_confirm_click(ev.pos):
                return True

        # Añadir zona
        if self.state.confirm_add_zone:
            # Delegate to Add Zone tool events (no legacy fallback)
            if self.controller.toolbar.add_zone.events.handle_confirm_click(ev.pos):
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
            if self.controller.toolbar.add_zone.handle_map_click(tx, ty):
                return True

        # Modo: Borrar zona (delegar en herramienta)
        if self.state.delete_zone_mode:
            if self.controller.toolbar.delete_zone.handle_map_click(tx, ty):
                return True

        # Modo: Pintar tiles
        if self.state.paint_tiles_mode:
            for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                if zn in ("no zone", "no-zone"):
                    continue
                w, h = global_map_settings.zone_size
                if ox <= tx < ox + w and oy <= ty < oy + h:
                    self.state.pending_paint_tiles_zone = zn
                    self.state.confirm_paint_tiles = True
                    self.state.paint_tiles_mode = False
                    logger.debug(f"DEBUG: paint_tiles_mode: pending zone {zn}, asking confirmation")
                    return True

        # Modo: Vaciar colliders
        if self.state.clear_colliders_mode:
            if self.controller.toolbar.clear_colliders.handle_map_click(tx, ty):
                return True

        # Modo: Pintar colliders (delegar en herramienta)
        if self.state.paint_colliders_mode:
            if self.controller.toolbar.paint_colliders.handle_map_click(tx, ty):
                return True

        return False

    def _handle_zone_selection(self, ev, camera) -> bool:
        world_x, world_y = self._screen_to_world(ev.pos, camera)
        tx = int(world_x) // TILE_SIZE
        ty = int(world_y) // TILE_SIZE

        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if zn in ("no zone", "no-zone"):
                continue
            w, h = global_map_settings.zone_size
            if ox <= tx < ox + w and oy <= ty < oy + h:
                now = pygame.time.get_ticks()
                if self.state.last_click_zone == zn and now - self.state.last_click_time <= 400:
                    # Doble clic: iniciar renombrado
                    self.state.renaming_zone = zn
                    self.state.rename_input = zn
                    pygame.key.set_repeat(200, 30)
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