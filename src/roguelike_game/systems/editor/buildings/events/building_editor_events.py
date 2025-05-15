# Path: src/roguelike_game/systems/editor/buildings/events/building_editor_events.py
import pygame
import logging

from roguelike_game.systems.editor.buildings.model.persistence.save_buildings_to_json import save_buildings_to_json
from roguelike_engine.config import BUILDINGS_DATA_PATH
from roguelike_game.systems.editor.buildings.controller.picker.picker_events import PickerEventHandler


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
        self.picker_events = PickerEventHandler(editor_state, controller.picker, buildings)
        self.zone_offsets = zone_offsets


    def handle(self, camera, entities):
        for ev in pygame.event.get():
            # --- Finaliza resize al soltar R ---
            if ev.type == pygame.KEYUP and ev.key == pygame.K_r:
                if self.editor.resizing:
                    self.editor.resizing = False
                    print("✅ Resize finalizado al soltar R")
                    # Opcional: podrías llamar aquí a una función para fijar el tamaño
            # --- F10: SOLO toggle editor (handles) ---
            if ev.type == pygame.KEYDOWN and ev.key == pygame.K_F10:
                # Encendemos/apagamos los handles
                self.controller.toggle_editor()
                # Si acabamos de cerrar el editor, guardamos todo
                if not self.editor.active:
                    # TODO: detectar automáticamente la zona actual de edición
                    zone = "lobby"
                    zone_off = self.zone_offsets.get(zone)
                    save_buildings_to_json(
                        entities.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=self.state.z_state,
                        zone=zone,
                        zone_offset=zone_off,
                    )
                return

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
                    
                    # TODO: detectar automáticamente la zona actual de edición
                    zone = "lobby"
                    zone_off = self.zone_offsets.get(zone)
                    save_buildings_to_json(
                        entities.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=self.state.z_state,
                        zone=zone,
                        zone_offset=zone_off,
                    )
                    return

                # D → reset (default) sobre hovered_building
                if ev.key == pygame.K_d and self.editor.hovered_building:
                    self.controller.default_tool.apply_reset(self.editor.hovered_building)
                    print("🔄 Reset (default) aplicado con D sobre hovered_building")
                    return

                # R → iniciar resize sobre hovered_building (al presionar)
                if ev.key == pygame.K_r and self.editor.hovered_building:
                    mx, my = pygame.mouse.get_pos()
                    self.controller._start_resize(self.editor.hovered_building, (mx, my))
                    print("🔧 Resize iniciado con R sobre hovered_building")
                    return

                # Ctrl+Z → undo eliminación de edificio
                if ev.key == pygame.K_z and (ev.mod & pygame.KMOD_CTRL):
                    self._undo_delete(entities.buildings)
                    return

                # Ctrl+S → guardar sin salir
                if ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
                    logger.info("Ctrl+S: saving buildings")

                    # TODO: detectar automáticamente la zona actual de guardado
                    zone = "lobby"
                    zone_off = self.zone_offsets.get(zone)
                    save_buildings_to_json(
                        entities.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=self.state.z_state,
                        zone=zone,
                        zone_offset=zone_off,
                    )

                    return

                # N → colocar edificio aleatorio sin picker
                if ev.key == pygame.K_n:
                    self.controller.placer_tool.place_building_at_mouse(entities.buildings)
                    return

                # Supr → borrar edificio bajo el ratón
                if ev.key == pygame.K_DELETE:
                    self.controller.delete_tool.delete_building_at_mouse(entities)

            # --- Mouse en modo editor (handles y split) ---
            if ev.type == pygame.MOUSEBUTTONDOWN:
                mx, my = pygame.mouse.get_pos()
                self.controller.on_mouse_down((mx, my), ev.button, camera, entities.buildings)
            elif ev.type == pygame.MOUSEBUTTONUP:
                self.controller.on_mouse_up(ev.button, camera, entities.buildings)
            elif ev.type == pygame.MOUSEMOTION:
                self.controller.on_mouse_motion(ev.pos, camera, entities.buildings)
            elif ev.type == pygame.MOUSEWHEEL:
                self._handle_mouse_wheel(ev, entities.buildings)

    def _undo_delete(self, buildings):
        if hasattr(self.editor, 'undo_stack') and self.editor.undo_stack:
            building, idx = self.editor.undo_stack.pop()
            buildings.insert(idx, building)
            # Opcional: selecciona el edificio restaurado
            self.editor.hovered_building = building
            self.editor.selected_building = building

    def _handle_mouse_wheel(self, ev, buildings):
        # Solo si hay varios edificios bajo el cursor
        hovered_list = self.editor.hovered_buildings
        if len(hovered_list) > 1:
            # Scroll up: y > 0, Scroll down: y < 0
            idx = self.editor.hovered_building_index
            idx = (idx + (-1 if ev.y < 0 else 1)) % len(hovered_list)
            self.editor.hovered_building_index = idx
            self.editor.hovered_building = hovered_list[idx]

    def _on_quit(self, ev):
        logger.info("Quit event received in Building Editor")
        self.state.running = False

    def _on_keydown(self, ev, entities):
        # ESC: salir del editor y guardar
        if ev.key == pygame.K_ESCAPE:
            logger.info("Escape: closing Building Editor and saving")
            self.editor.active = False
            self.editor.selected_building = None
            self.editor.dragging = False
            self.editor.resizing = False
            self.editor.split_dragging = False
            # TODO: detectar automáticamente la zona actual de edición
            zone = "lobby"
            zone_off = self.zone_offsets.get(zone)
            save_buildings_to_json(
                entities.buildings,
                BUILDINGS_DATA_PATH,
                z_state=self.state.z_state,
                zone=zone,
                zone_offset=zone_off,
            )

        # F10: toggle editor
        elif ev.key == pygame.K_F10:
            self.editor.active = not self.editor.active
            logger.info(f"Building Editor {'ON' if self.editor.active else 'OFF'} via F10")
            if not self.editor.active:
                # TODO: detectar automáticamente la zona actual de guardado
                zone = "lobby"
                zone_off = self.zone_offsets.get(zone)
                save_buildings_to_json(
                    entities.buildings,
                    BUILDINGS_DATA_PATH,
                    z_state=self.state.z_state,
                    zone=zone,
                    zone_offset=zone_off,
                )

        # Ctrl+S: guardar sin salir
        elif ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
            logger.info("Ctrl+S: saving buildings")
            # TODO: detectar automáticamente la zona actual de guardado
            zone = "lobby"
            zone_off = self.zone_offsets.get(zone)
            save_buildings_to_json(
                entities.buildings,
                BUILDINGS_DATA_PATH,
                z_state=self.state.z_state,
                zone=zone,
                zone_offset=zone_off,
            )

        # N: colocar edificio
        elif ev.key == pygame.K_n:
            self.controller.placer_tool.place_building_at_mouse(entities.buildings)

        # Supr: borrar edificio
        elif ev.key == pygame.K_DELETE:
            self.controller.delete_tool.delete_building_at_mouse(entities)

    def _on_mouse_down(self, ev, camera, buildings):
        mx, my = pygame.mouse.get_pos()
        self.controller.on_mouse_down((mx, my), ev.button, camera, buildings)

    def _on_mouse_up(self, ev, camera, buildings):
        self.controller.on_mouse_up(ev.button, camera, buildings)

    def _on_mouse_motion(self, ev, camera):
        self.controller.on_mouse_motion(ev.pos, camera)