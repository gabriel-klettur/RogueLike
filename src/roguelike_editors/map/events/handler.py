from __future__ import annotations

import logging
import pygame

from roguelike_ui.ui_blocker import is_blocked

from . import camera as cam
from . import async_tools
from . import rename as rn
from . import confirmations as conf
from . import modes as modes_mod
from . import selection as sel

logger = logging.getLogger(__name__)


class MapEditorEventHandler:
    """
    Orquestador de eventos del Map Editor. Delegación en módulos especializados:
      - camera: zoom, panning, teclado
      - async_tools: ejecución de herramientas (tiles/colliders, undo/redo)
      - confirmations: diálogos de confirmación
      - modes: clicks según modo (add/delete/paint)
      - selection: selección/doble clic de zonas
      - rename: teclado/clicks de renombrado
    """

    def __init__(self, manager, state, controller, map_manager):
        self.manager = manager
        self.state = state
        self.controller = controller
        self.map_manager = map_manager

    def handle(self, camera, map_manager, events=None):
        # Async tools loop
        if self.state.executing_tool:
            async_tools.process_async_tool(camera, self.state, self.controller, self.manager, self.map_manager)
            return

        # Continuous keyboard pan
        cam.handle_keyboard_pan(camera, self.state)

        ev_iter = events if events is not None else pygame.event.get()
        for ev in ev_iter:
            # Toolbar widget events
            try:
                self.controller.toolbar.view.handle_event(ev)
            except Exception:
                pass

            if ev.type in (
                pygame.MOUSEBUTTONDOWN,
                pygame.MOUSEBUTTONUP,
                pygame.MOUSEMOTION,
                pygame.MOUSEWHEEL,
                pygame.KEYDOWN,
            ):
                try:
                    tutorial = getattr(self, "tutorial", None)
                    if tutorial and tutorial.is_active() and tutorial.handle_event(ev):
                        continue
                except Exception:
                    pass

            if ev.type == pygame.QUIT:
                try:
                    if self.state.active and camera is not None:
                        self.manager._save_persisted_camera(camera.offset_x, camera.offset_y, camera.zoom)
                except Exception:
                    pass
                self.manager.game.state.running = False
                continue

            if ev.type == pygame.MOUSEWHEEL:
                cam.handle_zoom(ev, camera, self.state)
                continue

            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button in (2, 3):
                mx, my = ev.pos
                if not is_blocked(mx, my):
                    cam.start_panning(ev, camera, self.state)
                    continue

            if ev.type == pygame.MOUSEBUTTONUP and ev.button in (2, 3):
                self.state.panning = False
                continue

            if ev.type == pygame.MOUSEMOTION:
                if self.state.panning:
                    cam.update_panning(ev, camera, self.state)
                    continue
                buttons = getattr(ev, "buttons", None)
                if buttons and len(buttons) >= 3 and (buttons[1] or buttons[2]):
                    mx, my = ev.pos
                    if not is_blocked(mx, my):
                        self.state.panning = True
                        self.state.pan_start_mouse = ev.pos
                        self.state.pan_start_offset = (camera.offset_x, camera.offset_y)
                        cam.update_panning(ev, camera, self.state)
                        continue

            if ev.type == pygame.KEYDOWN:
                if self.state.renaming_zone:
                    if rn.handle_renaming_keys(ev, self.state, self.controller, self.manager):
                        continue

                if (ev.key == pygame.K_z) and (ev.mod & pygame.KMOD_CTRL):
                    async_tools.perform_undo(camera, self.state, self.map_manager)
                    continue
                if (ev.key == pygame.K_y) and (ev.mod & pygame.KMOD_CTRL):
                    async_tools.perform_redo(camera, self.state, self.map_manager)
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
                    self.controller.toolbar.delete_zone.request_delete_selected()
                    continue
                if ev.key == pygame.K_h and self.state.selected_zone:
                    self.controller.toggle_hide_zone(self.state.selected_zone)
                    continue

            if self.state.renaming_zone and ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                if rn.handle_renaming_click(ev, self.state, self.controller, self.manager):
                    continue

            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                if self.controller.toolbar.handle_click(ev.pos):
                    continue

                mx, my = ev.pos
                if is_blocked(mx, my):
                    continue

                if conf.handle_confirmation_dialogs(ev, self.state, self.controller, self.manager, self.map_manager):
                    continue

                if modes_mod.handle_mode_clicks(ev, camera, self.state, self.controller, self.map_manager):
                    continue

                if sel.handle_zone_selection(ev, camera, self.state):
                    continue
