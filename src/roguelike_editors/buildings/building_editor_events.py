import os
import pygame
import logging
from roguelike_ui.ui_blocker import is_blocked

from roguelike_editors.buildings.utils.save_buildings_to_json import (
    save_buildings_split,
)
from roguelike_editors.buildings.buildings_picker.building_picker_events import BuildingPickerEventHandler
from roguelike_editors.buildings.events import (
    handle_confirm_delete,
    early_rmb_drag_to_move_panel,
    handle_picker_event,
    handle_toolbar_and_panels,
    handle_colliders,
    handle_pan_state,
    handle_keydown,
    handle_keyup,
    handle_mousedown,
    handle_mouseup,
    handle_motion,
    handle_wheel,
    undo_delete,
)


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
            # 1) Modal de confirmación de borrado: consume todo
            try:
                if handle_confirm_delete(self.editor, self.controller, ev, entities):
                    continue
            except Exception:
                pass

            # 2) Tutorial: consumir primero eventos de ratón si está activo
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL):
                try:
                    tutorial = getattr(self, "tutorial", None)
                    if tutorial and tutorial.is_active() and tutorial.handle_event(ev):
                        continue
                except Exception:
                    pass

            # 3) Early guard: RMB para arrastrar el panel del picker
            try:
                early_rmb_drag_to_move_panel(self.editor, ev)
            except Exception:
                pass

            # 4) Delegar al picker si el ratón está sobre su panel
            try:
                if handle_picker_event(self.editor, self.picker_events, ev, camera):
                    continue
            except Exception:
                pass

            # 5) Delegar a toolbar y paneles (add/remove, tutorial) en eventos de ratón
            try:
                if handle_toolbar_and_panels(self, ev, camera, entities):
                    continue
            except Exception:
                pass

            # 6) Delegar al panel de colisiones (consume si corresponde)
            try:
                if handle_colliders(self, ev, camera, self.buildings):
                    continue
            except Exception:
                pass

            # 7) Paneo de cámara con MMB
            try:
                if handle_pan_state(self, ev, camera):
                    continue
            except Exception:
                pass

            # 8) QUIT: persistir y salir
            if ev.type == pygame.QUIT:
                if self.editor.active:
                    try:
                        save_buildings_split(
                            self.buildings,
                            z_state=self.state.z_state,
                            zone_offsets=self.zone_offsets,
                        )
                    except Exception:
                        pass
                self.state.running = False
                return

            # 9) Finaliza resize al soltar R
            try:
                if handle_keyup(self.editor, ev):
                    pass
            except Exception:
                pass

            # 10) Si el picker está activo, delego también (no consume aquí)
            if getattr(self.editor, "picker_active", False):
                try:
                    self.picker_events.handle(ev, camera)
                except Exception:
                    pass

            # 11) Teclas (modo editor)
            if ev.type == pygame.KEYDOWN:
                # Permitir al tutorial consumir teclas (ESC para cerrar)
                try:
                    tutorial = getattr(self, "tutorial", None)
                    if tutorial and tutorial.is_active() and tutorial.handle_event(ev):
                        continue
                except Exception:
                    pass

                # Delegar al manejador de teclas: retorna True si hay que terminar el ciclo (return)
                if handle_keydown(self, self.editor, self.controller, self.state, ev, camera, entities, save_buildings_split):
                    return

            # 12) Ratón en modo editor
            if ev.type == pygame.MOUSEBUTTONDOWN:
                handle_mousedown(self, self.editor, self.controller, ev, camera, entities)
            elif ev.type == pygame.MOUSEBUTTONUP:
                handle_mouseup(self, self.controller, ev, camera, entities, save_buildings_split, self.state)
                return
            elif ev.type == pygame.MOUSEMOTION:
                if handle_motion(self.editor, self.controller, ev, camera, entities, is_blocked):
                    return
            elif ev.type == pygame.MOUSEWHEEL:
                self._handle_mouse_wheel(ev, camera, entities.buildings)


    def _handle_mouse_wheel(self, ev, camera, buildings):
        """Recompute overlapped buildings under cursor and cycle selection."""
        # Prefer existing precomputed hovered list (e.g., set during motion or by tests)
        hovered_list = list(getattr(self.editor, 'hovered_buildings', []) or [])
        if not hovered_list:
            # Seed from current mouse position only when no list is present
            mx, my = pygame.mouse.get_pos()
            hovered_list = self.controller._buildings_under_mouse((mx, my), camera, buildings)
            self.editor.hovered_buildings = hovered_list
            if not hovered_list:
                return
        # Try to keep continuity with current hovered building if present
        cur = getattr(self.editor, 'hovered_building', None)
        try:
            base_idx = hovered_list.index(cur) if cur in hovered_list else self.editor.hovered_building_index
        except Exception:
            base_idx = 0
        delta = -1 if getattr(ev, 'y', 0) < 0 else 1
        idx = (base_idx + delta) % len(hovered_list)
        self.editor.hovered_building_index = idx
        self.editor.hovered_building = hovered_list[idx]
        # Evitar auto-selección durante el tutorial: no promover hovered -> active con la rueda
        tutorial_active = False
        try:
            t = getattr(self, 'tutorial', None)
            tutorial_active = bool(t and t.is_active())
        except Exception:
            tutorial_active = False
        # Solo auto-seleccionar si NO está activo el tutorial
        if (not tutorial_active) and getattr(self.editor, 'current_tool', 'select') == 'select' and not getattr(self.editor, 'colliders_mode', False):
            self.editor.active_building = hovered_list[idx]

    def _undo_delete(self, buildings):
        if hasattr(self.editor, 'undo_stack') and self.editor.undo_stack:
            try:
                building, idx = self.editor.undo_stack.pop()
            except Exception:
                logger.info("⚠️ Undo: pila corrupta o elemento inválido")
                return
            try:
                buildings.insert(idx, building)
            except Exception:
                buildings.append(building)
            logger.info(f"✅ Undo: edificio restaurado en índice {idx}")
            # Marcar hover para feedback, pero NO auto-seleccionar si el tutorial está activo
            self.editor.hovered_building = building
            try:
                t = getattr(self, 'tutorial', None)
                if not (t and t.is_active()):
                    self.editor.selected_building = building
            except Exception:
                # Si no hay info del tutorial, mantener el comportamiento previo
                self.editor.selected_building = building
            # Pulso para el tutorial (también cuando proviene del botón de toolbar)
            try:
                setattr(self.editor, 'tutorial_undo_delete_pulse', True)
            except Exception:
                pass
        else:
            logger.info("ℹ️ Undo: no hay operaciones de eliminación para deshacer")