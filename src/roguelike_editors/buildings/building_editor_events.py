import os
import pygame
import logging
from roguelike_ui.ui_blocker import is_blocked

from roguelike_editors.buildings.utils.save_buildings_to_json import (
    save_buildings_to_json,
    save_buildings_split,
)
from roguelike_engine.config.config import (
    BUILDINGS_DATA_PATH,
    BUILDINGS_TEMPLATES_PATH,
    BUILDINGS_INSTANCES_PATH,
)
from roguelike_editors.buildings.buildings_picker.building_picker_events import BuildingPickerEventHandler



logger = logging.getLogger("building_editor.events")


class BuildingEditorEventHandler:
    """
    Manejador de eventos para el Building Editor en modo MVC.
    """
    def __init__(self, state, editor_state, controller, buildings, zone_offsets: dict[str,tuple[int,int]]):
        self.state = state
        self.editor = editor_state
        self.controller = controller
        self.buildings = buildings
        self.picker_events = BuildingPickerEventHandler(editor_state, controller.picker, buildings)
        self.zone_offsets = zone_offsets
        # Pan state for camera movement with middle mouse
        self.panning = False
        self.pan_start = (0, 0)
        self.pan_offset_start = (0, 0)


    def handle(self, camera, entities, events=None):

        if events is None:
            events = pygame.event.get()
        for ev in events:
            # Early guard: iniciar drag del panel del picker con RMB dentro del panel pero fuera del grid/scrollbar
            if (
                ev.type == pygame.MOUSEBUTTONDOWN
                and getattr(ev, 'button', None) == 3
                and getattr(self.editor, 'picker_active', False)
            ):
                try:
                    panel_rect = getattr(self.editor, 'picker_panel_rect', None)
                    if panel_rect:
                        mx, my = getattr(ev, 'pos', (None, None))
                        if mx is not None and panel_rect.collidepoint(mx, my):
                            m = int(getattr(self.editor, 'picker_internal_margin', 8) or 8)
                            pad = int(getattr(self.editor, 'picker_padding', 8) or 8)
                            cw = int(getattr(self.editor, 'picker_cell_w', 64) or 64)
                            ch = int(getattr(self.editor, 'picker_cell_h', 64) or 64)
                            footer_h = int(getattr(self.editor, 'picker_footer_h', 0) or 0)
                            needs_scroll = bool(getattr(self.editor, 'picker_needs_scroll', False))
                            sb_pad = 4
                            sb_w = int(getattr(self.editor, 'picker_scrollbar_w', 10) or 10) if needs_scroll else 0
                            gx = panel_rect.left + m
                            gy = panel_rect.top + m
                            gw = max(0, panel_rect.w - 2 * m)
                            gh = max(0, panel_rect.h - 2 * m - footer_h)
                            gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))
                            track_rect = getattr(self.editor, 'picker_scroll_track_rect', None)
                            in_grid = pygame.Rect(gx, gy, gw, gh).collidepoint(mx, my)
                            in_scroll = needs_scroll and (
                                (track_rect and pygame.Rect(track_rect).collidepoint(mx, my)) or (mx >= gx + gw_effective)
                            )
                            if (not in_grid) and (not in_scroll):
                                self.editor.picker_dragging_panel = True
                                self.editor.picker_drag_offset = (mx - panel_rect.left, my - panel_rect.top)
                                if getattr(self.editor, 'picker_manual_pos', None) is None:
                                    self.editor.picker_manual_pos = (panel_rect.left, panel_rect.top)
                                # No consumir aquí; dejar que el picker procese también si está activo
                except Exception:
                    pass
            # Si el picker está activo y el mouse está sobre su panel, delegar PRIMERO al picker
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL) and getattr(self.editor, 'picker_active', False):
                try:
                    panel_rect = getattr(self.editor, 'picker_panel_rect', None)
                    if panel_rect:
                        if ev.type == pygame.MOUSEWHEEL:
                            mx, my = pygame.mouse.get_pos()
                        else:
                            mx, my = getattr(ev, 'pos', (None, None))
                        if mx is not None and panel_rect.collidepoint(mx, my):
                            # Robust guard: si es RMB down dentro del panel pero fuera del grid/scrollbar,
                            # marcar el flag de arrastre del panel inmediatamente (evita que el orden de delegación lo impida)
                            if ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 3:
                                try:
                                    m = int(getattr(self.editor, 'picker_internal_margin', 8) or 8)
                                    pad = int(getattr(self.editor, 'picker_padding', 8) or 8)
                                    cw = int(getattr(self.editor, 'picker_cell_w', 64) or 64)
                                    ch = int(getattr(self.editor, 'picker_cell_h', 64) or 64)
                                    footer_h = int(getattr(self.editor, 'picker_footer_h', 0) or 0)
                                    needs_scroll = bool(getattr(self.editor, 'picker_needs_scroll', False))
                                    sb_pad = 4
                                    sb_w = int(getattr(self.editor, 'picker_scrollbar_w', 10) or 10) if needs_scroll else 0
                                    gx = panel_rect.left + m
                                    gy = panel_rect.top + m
                                    gw = max(0, panel_rect.w - 2 * m)
                                    gh = max(0, panel_rect.h - 2 * m - footer_h)
                                    # Scrollbar track
                                    gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))
                                    track_rect = getattr(self.editor, 'picker_scroll_track_rect', None)
                                    in_grid = pygame.Rect(gx, gy, gw, gh).collidepoint(mx, my)
                                    in_scroll = needs_scroll and (
                                        (track_rect and pygame.Rect(track_rect).collidepoint(mx, my)) or (mx >= gx + gw_effective)
                                    )
                                    if (not in_grid) and (not in_scroll):
                                        self.editor.picker_dragging_panel = True
                                        self.editor.picker_drag_offset = (mx - panel_rect.left, my - panel_rect.top)
                                        if getattr(self.editor, 'picker_manual_pos', None) is None:
                                            self.editor.picker_manual_pos = (panel_rect.left, panel_rect.top)
                                except Exception:
                                    pass
                            self.picker_events.handle(ev, camera)
                            continue
                except Exception:
                    pass
            # Delegar primero a la toolbar SOLO para eventos de mouse (no teclas)
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL):
                try:
                    toolbar = getattr(self, 'buildings_toolbar_controller', None)
                    if toolbar and toolbar.handle_event(ev):
                        continue
                except Exception:
                    pass
                # Delegar al panel de Add/Remove si está activo
                try:
                    add_remove = getattr(self, 'add_remove', None)
                    if add_remove and add_remove.is_active() and add_remove.handle_event(ev, camera, entities):
                        continue
                except Exception:
                    pass
            # Delegar al panel de colisiones (si está activo). Consume el evento si corresponde.
            try:
                colliders = getattr(self, 'colliders', None)
                if colliders and colliders.is_active() and colliders.handle_event(ev, camera, self.buildings):
                    continue
            except Exception:
                pass
            # Pan camera with middle mouse
            if ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 2:
                # Start panning
                self.panning = True
                self.pan_start = ev.pos
                self.pan_offset_start = (camera.offset_x, camera.offset_y)
                logger.info(f" EDITOR] Start panning at {self.pan_start}, offset_start={self.pan_offset_start}")
                continue
            if ev.type == pygame.MOUSEBUTTONUP and getattr(ev, 'button', None) == 2 and self.panning:
                # Stop panning
                self.panning = False
                logger.info(" EDITOR] Stop panning")
                continue
            if ev.type == pygame.MOUSEMOTION and self.panning:
                # Apply panning motion (using relative motion)
                rel_x, rel_y = ev.rel
                
                camera.offset_x -= rel_x / camera.zoom
                camera.offset_y -= rel_y / camera.zoom
                
                continue
            if ev.type == pygame.QUIT:
                # Persist building changes if editor active
                if self.editor.active:
                    try:
                        save_buildings_to_json(
                            self.buildings,
                            BUILDINGS_DATA_PATH,
                            z_state=self.state.z_state,
                            zone_offsets=self.zone_offsets,
                        )
                    except Exception:
                        # avoid blocking quit on save failure
                        pass
                self.state.running = False
                return
            # --- Finaliza resize al soltar R ---
            if ev.type == pygame.KEYUP and ev.key == pygame.K_r:
                if self.editor.resizing:
                    self.editor.resizing = False
                    logger.info("✅ Resize finalizado al soltar R")
                    # Opcional: podrías llamar aquí a una función para fijar el tamaño

            # --- Si el picker está activo, delego ahí ---
            if self.editor.picker_active:
                self.picker_events.handle(ev, camera)                

            # --- Teclas cuando estoy en modo “editor” sin picker ---
            if ev.type == pygame.KEYDOWN:
                # Ctrl+P (o simplemente P) → toggle picker
                if ev.key == pygame.K_p:
                    self.controller.toggle_picker()
                    return

                # ESC → Cerrar editor completo
                if ev.key == pygame.K_ESCAPE:
                    logger.info("Escape: closing Building Editor and saving")
                    self.editor.active = False
                    self.editor.selected_building = None
                    self.editor.dragging = False
                    self.editor.resizing = False
                    self.editor.split_dragging = False
                    
                    try:
                        save_buildings_to_json(
                            entities.buildings,
                            BUILDINGS_DATA_PATH,
                            z_state=self.state.z_state,
                            zone_offsets=self.zone_offsets,
                        )
                    except Exception:
                        pass
                    return

                # D → reset (default) sobre hovered_building
                if ev.key == pygame.K_d and not getattr(self.editor, 'colliders_mode', False) and self.editor.hovered_building:
                    self.controller.default_tool.apply_reset(self.editor.hovered_building)
                    logger.info("🔄 Reset (default) aplicado con D sobre hovered_building")
                    return

                # R → iniciar resize sobre hovered_building (al presionar)
                if ev.key == pygame.K_r and not getattr(self.editor, 'colliders_mode', False) and self.editor.hovered_building:
                    mx, my = pygame.mouse.get_pos()
                    self.controller._start_resize(self.editor.hovered_building, (mx, my))
                    logger.info("🔧 Resize iniciado con R sobre hovered_building")
                    return

                # Ctrl+Z → undo eliminación de edificio
                if ev.key == pygame.K_z and (ev.mod & pygame.KMOD_CTRL):
                    self._undo_delete(entities.buildings)
                    return

                # Ctrl+S → guardar sin salir
                if ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
                    logger.info("Ctrl+S: saving buildings")

                    try:
                        save_buildings_to_json(
                            entities.buildings,
                            BUILDINGS_DATA_PATH,
                            z_state=self.state.z_state,
                            zone_offsets=self.zone_offsets,
                        )
                    except Exception:
                        pass

                    return

                # N → colocar edificio aleatorio sin picker
                if ev.key == pygame.K_n and not getattr(self.editor, 'colliders_mode', False):
                    self.controller.placer_tool.place_building_at_mouse(entities.buildings)
                    return

                # Supr → borrar edificio bajo el ratón
                if ev.key == pygame.K_DELETE and not getattr(self.editor, 'colliders_mode', False):
                    self.controller.delete_tool.delete_building_at_mouse(entities)

            # --- Mouse en modo editor (handles y split) ---
            if ev.type == pygame.MOUSEBUTTONDOWN:
                mx, my = pygame.mouse.get_pos()
                # Delegar al controlador
                self.controller.on_mouse_down((mx, my), ev.button, camera, entities.buildings)
            elif ev.type == pygame.MOUSEBUTTONUP:
                # 3) Delegar al controlador y guardar cambios de posición/tamaño
                self.controller.on_mouse_up(ev.button, camera, entities.buildings)
                # Persistir cambios de edificios (posición, tamaño, split)
                try:
                    save_buildings_to_json(
                        entities.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=self.state.z_state,
                        zone_offsets=self.zone_offsets,
                    )
                except Exception:
                    pass
                return
            elif ev.type == pygame.MOUSEMOTION:
                mx, my = ev.pos
                # If mouse is over any registered UI panel (Tiles/Buildings/Map),
                # suppress Buildings Editor hover/active states to avoid bleed-through visuals.
                try:
                    if is_blocked(mx, my):
                        self.editor.hovered_buildings = []
                        self.editor.hovered_building = None
                        self.editor.active_building = None
                        return
                except Exception:
                    pass

                # Clear active building if mouse leaves its bounds in editor mode
                if self.editor.current_tool == 'select':
                    world_x = mx / camera.zoom + camera.offset_x
                    world_y = my / camera.zoom + camera.offset_y
                    ab = getattr(self.editor, 'active_building', None)
                    if ab and not ab.rect.collidepoint(world_x, world_y):
                        self.editor.active_building = None
                # Delegate motion and update hover list
                self.controller.on_mouse_motion(ev.pos, camera, entities.buildings)
                # Focus active building for select mode
                if self.editor.current_tool == 'select' and getattr(self.editor, 'active_building', None) is None:
                    hb = getattr(self.editor, 'hovered_building', None)
                    if hb:
                        self.editor.active_building = hb
            elif ev.type == pygame.MOUSEWHEEL:
                self._handle_mouse_wheel(ev, entities.buildings)


    def _handle_mouse_wheel(self, ev, buildings):
        """Cycle hovered building when multiple under cursor."""
        hovered_list = self.editor.hovered_buildings
        if len(hovered_list) > 1:
            idx = self.editor.hovered_building_index
            idx = (idx + (-1 if ev.y < 0 else 1)) % len(hovered_list)
            self.editor.hovered_building_index = idx
            self.editor.hovered_building = hovered_list[idx]

    def _undo_delete(self, buildings):
        if hasattr(self.editor, 'undo_stack') and self.editor.undo_stack:
            building, idx = self.editor.undo_stack.pop()
            buildings.insert(idx, building)
            # Opcional: selecciona el edificio restaurado
            self.editor.hovered_building = building
            self.editor.selected_building = building