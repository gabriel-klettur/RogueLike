# Path: src/roguelike_game/systems/editor/buildings/events/building_editor_events.py
import pygame
import logging
import os
import json

from roguelike_game.systems.editor.buildings.model.persistence.save_buildings_to_json import save_buildings_to_json
from roguelike_engine.config.config import BUILDINGS_DATA_PATH, BUILDINGS_COLLISIONS_DATA_PATH
from roguelike_engine.config.config import SCREEN_WIDTH, SCREEN_HEIGHT
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.systems.editor.buildings.controller.picker.picker_events import PickerEventHandler
from roguelike_game.systems.editor.buildings.utils.zone_helpers import detect_zone_from_px


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
                    save_buildings_to_json(
                        entities.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=self.state.z_state,
                        zone_offsets=self.zone_offsets
                    )
                return

            # --- Si el picker está activo, delego ahí ---
            if self.editor.picker_active:
                self.picker_events.handle(ev, camera)                

            # --- Teclas cuando estoy en modo “editor” sin picker ---
            if ev.type == pygame.KEYDOWN:
                # Toggle collision brush picker
                if ev.key == pygame.K_c:
                    new_val = not self.editor.collision_picker_open
                    self.editor.collision_picker_open = new_val
                    self.editor.current_tool = 'collision_brush' if new_val else 'select'
                    return
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
                    
                                        
                    save_buildings_to_json(
                        entities.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=self.state.z_state,
                        zone_offsets=self.zone_offsets
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

                    save_buildings_to_json(
                        entities.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=self.state.z_state,
                        zone_offsets=self.zone_offsets
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
                # UI de collision picker
                if self.editor.collision_picker_open:
                    x0, y0 = self.editor.collision_picker_pos or (0, 0)
                    w, h = self.editor.collision_picker_panel_size
                    if x0 <= mx <= x0 + w and y0 <= my <= y0 + h:
                        if ev.button == 1:
                            for ch, rect in self.editor.collision_picker_rects.items():
                                if rect.collidepoint((mx, my)):
                                    self.editor.collision_choice = ch
                                    return
                        elif ev.button == 3:
                            self.editor.collision_picker_dragging = True
                            dx = mx - x0; dy = my - y0
                            self.editor.collision_picker_drag_offset = (dx, dy)
                            return
                # Inicia collision brush
                if ev.button == 1 and self.editor.current_tool == 'collision_brush' and self.editor.collision_choice:
                    world_x = mx / camera.zoom + camera.offset_x
                    world_y = my / camera.zoom + camera.offset_y
                    for b in reversed(self.buildings):
                        x_b, y_b = b.x, b.y
                        w_img, h_img = b.image.get_size()
                        rect = pygame.Rect(x_b, y_b, w_img, h_img)
                        if rect.collidepoint(world_x, world_y):
                            self.editor.collision_brush_dragging = True
                            col = int((world_x - x_b) // TILE_SIZE)
                            row = int((world_y - y_b) // TILE_SIZE)
                            if 0 <= row < len(b.collision_map) and 0 <= col < len(b.collision_map[0]):
                                b.collision_map[row][col] = self.editor.collision_choice
                            return
                # Delegar al controlador
                self.controller.on_mouse_down((mx, my), ev.button, camera, entities.buildings)
            elif ev.type == pygame.MOUSEBUTTONUP:
                # Fin drag collision picker
                if ev.button == 3 and self.editor.collision_picker_dragging:
                    self.editor.collision_picker_dragging = False
                    return
                # Fin collision brush y guardar
                if ev.button == 1 and self.editor.current_tool == 'collision_brush' and self.editor.collision_brush_dragging:
                    self.editor.collision_brush_dragging = False
                    # Persistir colisiones
                    data = {}
                    for b in self.buildings:
                        data[b.image_path] = {
                            'width': len(b.collision_map[0]) if b.collision_map else 0,
                            'height': len(b.collision_map),
                            'collision': b.collision_map
                        }
                    os.makedirs(os.path.dirname(BUILDINGS_COLLISIONS_DATA_PATH), exist_ok=True)
                    with open(BUILDINGS_COLLISIONS_DATA_PATH, 'w', encoding='utf-8') as cf:
                        json.dump(data, cf, indent=4)
                    print(f"✅ Colisiones guardadas en {BUILDINGS_COLLISIONS_DATA_PATH}")
                    return
                self.controller.on_mouse_up(ev.button, camera, entities.buildings)
            elif ev.type == pygame.MOUSEMOTION:
                mx, my = ev.pos
                # Mover panel picker
                if self.editor.collision_picker_dragging:
                    dx, dy = self.editor.collision_picker_drag_offset
                    self.editor.collision_picker_pos = (mx - dx, my - dy)
                    return
                # Collision brush en drag
                if self.editor.current_tool == 'collision_brush' and self.editor.collision_brush_dragging and self.editor.collision_choice:
                    world_x = mx / camera.zoom + camera.offset_x
                    world_y = my / camera.zoom + camera.offset_y
                    for b in reversed(self.buildings):
                        x_b, y_b = b.x, b.y
                        w_img, h_img = b.image.get_size()
                        rect = pygame.Rect(x_b, y_b, w_img, h_img)
                        if rect.collidepoint(world_x, world_y):
                            col = int((world_x - x_b) // TILE_SIZE)
                            row = int((world_y - y_b) // TILE_SIZE)
                            if 0 <= row < len(b.collision_map) and 0 <= col < len(b.collision_map[0]):
                                b.collision_map[row][col] = self.editor.collision_choice
                            return
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

    def _on_keydown(self, ev, entities, camera):
        # ESC: salir del editor y guardar
        if ev.key == pygame.K_ESCAPE:
            logger.info("Escape: closing Building Editor and saving")
            self.editor.active = False
            self.editor.selected_building = None
            self.editor.dragging = False
            self.editor.resizing = False
            self.editor.split_dragging = False
            
            save_buildings_to_json(
                entities.buildings,
                BUILDINGS_DATA_PATH,
                z_state=self.state.z_state,
                zone_offsets=self.zone_offsets
            )

        # F10: toggle editor
        elif ev.key == pygame.K_F10:
            self.editor.active = not self.editor.active
            logger.info(f"Building Editor {'ON' if self.editor.active else 'OFF'} via F10")
            if not self.editor.active:
                
                save_buildings_to_json(
                    entities.buildings,
                    BUILDINGS_DATA_PATH,
                    z_state=self.state.z_state,
                    zone_offsets=self.zone_offsets
                )

        # Ctrl+S: guardar sin salir
        elif ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
            logger.info("Ctrl+S: saving buildings")
            
            save_buildings_to_json(
                entities.buildings,
                BUILDINGS_DATA_PATH,
                z_state=self.state.z_state,
                zone_offsets=self.zone_offsets
            )

        # N: colocar edificio
        elif ev.key == pygame.K_n:
            self.controller.placer_tool.place_building_at_mouse(entities.buildings)

        # Supr: borrar edificio
        elif ev.key == pygame.K_DELETE:
            self.controller.delete_tool.delete_building_at_mouse(entities)

    def _current_zone_and_offset(self, camera) -> tuple[str, tuple[int,int]]:
        # centro de la pantalla en píxeles de mundo
        cx_px = camera.offset_x + (SCREEN_WIDTH  / 2) / camera.zoom
        cy_px = camera.offset_y + (SCREEN_HEIGHT / 2) / camera.zoom    
        return detect_zone_from_px(cx_px, cy_px)

    def _on_mouse_down(self, ev, camera, buildings):
        mx, my = pygame.mouse.get_pos()
        self.controller.on_mouse_down((mx, my), ev.button, camera, buildings)

    def _on_mouse_up(self, ev, camera, buildings):
        self.controller.on_mouse_up(ev.button, camera, buildings)

    def _on_mouse_motion(self, ev, camera):
        self.controller.on_mouse_motion(ev.pos, camera)