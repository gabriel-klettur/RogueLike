import pygame
import os
from pygame.locals import *
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config import DATA_DIR, ASSETS_DIR
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.tile.assets import get_sprite_for_tile
from roguelike_game.systems.editor.buildings.model.persistence.save_buildings_to_json import save_buildings_to_json
from roguelike_engine.map.model.overlay.overlay_manager import load_layers, save_layers

class MapEditorEventHandler:
    """
    Maneja eventos para el Map Editor.
    """
    def __init__(self, manager, state, controller, map_manager):
        self.manager = manager
        self.state = state
        self.controller = controller
        self.map_manager = map_manager

    def handle(self, camera, map_manager):
        for ev in pygame.event.get():
            #print(f"DEBUG: Event {ev.type} at {getattr(ev, 'pos', None)}")
            # Handle quit
            if ev.type == pygame.QUIT:
                # Exit entire game
                self.manager.game.state.running = False
                return
            # Zoom in/out in Map Editor (infinite, pivot on mouse)
            elif ev.type == pygame.MOUSEWHEEL:
                # store world coords under cursor before zoom
                mx, my = pygame.mouse.get_pos()
                wx = mx / camera.zoom + camera.offset_x
                wy = my / camera.zoom + camera.offset_y
                # adjust zoom with clamp to avoid zero
                zoom_step = 0.1
                if ev.y > 0:
                    new_zoom = camera.zoom + zoom_step
                else:
                    new_zoom = camera.zoom - zoom_step
                camera.zoom = max(new_zoom, 0.01)
                # recalc offset so (wx,wy) remains under cursor
                camera.offset_x = wx - mx / camera.zoom
                camera.offset_y = wy - my / camera.zoom
                return
            if ev.type == pygame.KEYDOWN:
                # Si estamos en modo renombrar, capturar solo las teclas de renombrado
                if self.state.renaming_zone:
                    if ev.key == pygame.K_RETURN:
                        old_zone = self.state.renaming_zone
                        print(f"DEBUG: Enter pressed in renaming mode. old_name={old_zone}, input_buffer='{self.state.rename_input}'")
                        new_name = self.state.rename_input.strip()
                        print(f"DEBUG: renaming to new_name={new_name}")
                        self.controller.rename_zone(old_zone, new_name)
                        # Actualizar propiedades de zona en edificios
                        for b in self.manager.game.buildings.buildings:
                            if getattr(b, 'zone', None) == old_zone:
                                b.zone = new_name
                                print(f"DEBUG: building {b} zone updated from {old_zone} to {new_name}")
                        save_buildings_to_json(self.manager.game.buildings.buildings, z_state=self.manager.game.z_state, zone_offsets=global_map_settings.zone_offsets)
                        print("DEBUG: persisted buildings_data.json")
                        print("DEBUG: rename_zone executed")
                        self.state.selected_zone = new_name
                        print(f"DEBUG: selected_zone updated to {new_name}")
                        self.state.renaming_zone = None
                        self.state.rename_input = ""
                        print("DEBUG: exited renaming mode")
                        return
                    elif ev.key == pygame.K_BACKSPACE:
                        self.state.rename_input = self.state.rename_input[:-1]
                        return
                    elif ev.unicode and ev.unicode.isprintable():
                        self.state.rename_input += ev.unicode
                        return
                # Toggle Map Editor OFF via F11
                if ev.key == pygame.K_F11:
                    self.manager.toggle()
                    return
                # Exit game on Escape
                if ev.key == pygame.K_ESCAPE:
                    # Exit entire game
                    self.manager.game.state.running = False
                    return
                # Nueva zona
                if ev.key == pygame.K_n:
                    self.controller.duplicate_zone()
                    return
                # Cargar zonas
                if ev.key == pygame.K_l:
                    self.controller.load_zones()
                    return
                # Guardar zonas
                if ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
                    self.controller.save_zones()
                    return
                # Eliminar zona
                if ev.key == pygame.K_d:
                    self.controller.delete_zone()
                    return
                # Ocultar/Mostrar zona
                if ev.key == pygame.K_h and self.state.selected_zone:
                    self.controller.toggle_hide_zone(self.state.selected_zone)
                    return
            # If renaming mode, process accept click
            if self.state.renaming_zone and ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                print(f"DEBUG: processing accept click at {ev.pos} for renaming_zone={self.state.renaming_zone}")
                # If clicked on accept button, apply rename
                if self.state.rename_accept_rect and self.state.rename_accept_rect.collidepoint(ev.pos):
                    old_zone = self.state.renaming_zone
                    print("DEBUG: accept_rect clicked, invoking rename_zone for zone", old_zone)
                    new_name = self.state.rename_input.strip()
                    self.controller.rename_zone(old_zone, new_name)
                    # Update buildings zone property
                    for b in self.manager.game.buildings.buildings:
                        if getattr(b, 'zone', None) == old_zone:
                            b.zone = new_name
                            print(f"DEBUG: building {b} zone updated from {old_zone} to {new_name}")
                    save_buildings_to_json(self.manager.game.buildings.buildings, z_state=self.manager.game.z_state, zone_offsets=global_map_settings.zone_offsets)
                    print("DEBUG: persisted buildings_data.json")
                    self.state.selected_zone = new_name
                # Exit renaming mode
                self.state.renaming_zone = None
                self.state.rename_input = ""
                self.state.rename_input_rect = None
                self.state.rename_accept_rect = None
                return
            # Selección y arrastre
            elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                # Handle toolbar button clicks, return if handled
                if self.controller.toolbar.handle_click(ev.pos):
                    return
                # Handle deletion confirmation dialog
                if self.state.confirm_delete_zone:
                    if self.state.confirm_yes_rect and self.state.confirm_yes_rect.collidepoint(ev.pos):
                        zone = self.state.pending_delete_zone
                        self.state.selected_zone = zone
                        self.controller.delete_zone()
                        self.state.confirm_delete_zone = False
                        self.state.pending_delete_zone = None
                        self.state.confirm_yes_rect = None
                        self.state.confirm_no_rect = None
                        return
                    if self.state.confirm_no_rect and self.state.confirm_no_rect.collidepoint(ev.pos):
                        self.state.confirm_delete_zone = False
                        self.state.pending_delete_zone = None
                        self.state.confirm_yes_rect = None
                        self.state.confirm_no_rect = None
                        return
                # Handle paint tiles confirmation dialog
                if self.state.confirm_paint_tiles:
                    zone = self.state.pending_paint_tiles_zone
                    # Yes: apply paint
                    if self.state.confirm_paint_yes_rect and self.state.confirm_paint_yes_rect.collidepoint(ev.pos):
                        print(f"DEBUG: confirming paint tiles for zone {zone}")
                        tile_code = "floor"
                        print(f"DEBUG: painting with tile '{tile_code}' on layer '{Layer.Ground.name}'")
                        # sprite generated per tile using overlay code
                        count = 0
                        for tile in self.map_manager.tiles_by_zone.get(zone, []):
                            original_char = tile.tile_type
                            tile.overlay_code = tile_code
                            sprite = get_sprite_for_tile(original_char, tile.overlay_code)
                            tile.sprite = sprite
                            tile.scaled_cache.clear()
                            # Also update the ground layer tile used for chunked rendering
                            tx = tile.x // TILE_SIZE
                            ty = tile.y // TILE_SIZE
                            ground = self.map_manager.tiles_by_layer.get(Layer.Ground)
                            if ground and 0 <= ty < len(ground) and 0 <= tx < len(ground[0]):
                                gt = ground[ty][tx]
                                original_char = gt.tile_type
                                gt.overlay_code = tile_code
                                sprite = get_sprite_for_tile(original_char, gt.overlay_code)
                                gt.sprite = sprite
                                gt.scaled_cache.clear()
                            count += 1
                        print(f"DEBUG: painted {count} tiles in zone {zone}")
                        # Persistir overlay de ground para zona
                        zone_offset = global_map_settings.zone_offsets.get(zone)
                        if zone_offset:
                            off_x, off_y = zone_offset
                            zone_w, zone_h = global_map_settings.zone_size
                            overlay_grid = [["" for _ in range(zone_w)] for _ in range(zone_h)]
                            for t in self.map_manager.tiles_by_zone.get(zone, []):
                                tx = t.x // TILE_SIZE
                                ty = t.y // TILE_SIZE
                                local_x = tx - off_x
                                local_y = ty - off_y
                                if 0 <= local_x < zone_w and 0 <= local_y < zone_h:
                                    overlay_grid[local_y][local_x] = t.overlay_code
                            # Merge with existing overlay layers
                            layers = load_layers(zone)
                            layers[Layer.Ground] = overlay_grid
                            save_layers(zone, layers)
                            print(f"DEBUG: persisted overlay for zone {zone}")
                        # Invalidate cached chunk surfaces to reflect updated tile sprites
                        print(f"[DEBUG][MapEditorEventHandler] invalidating chunk cache after painting")
                        self.map_manager.view.invalidate_cache()
                        # clear confirmation state
                        self.state.confirm_paint_tiles = False
                        self.state.pending_paint_tiles_zone = None
                        self.state.confirm_paint_yes_rect = None
                        self.state.confirm_paint_no_rect = None
                    # No: cancel
                    elif self.state.confirm_paint_no_rect and self.state.confirm_paint_no_rect.collidepoint(ev.pos):
                        print("DEBUG: canceled paint tiles")
                        self.state.confirm_paint_tiles = False
                        self.state.pending_paint_tiles_zone = None
                        self.state.confirm_paint_yes_rect = None
                        self.state.confirm_paint_no_rect = None
                    return
                # Handle add zone mode (click to add 50x50 zone)
                if self.state.add_zone_mode:
                    world_x = ev.pos[0] / camera.zoom + camera.offset_x
                    world_y = ev.pos[1] / camera.zoom + camera.offset_y
                    tx = int(world_x) // TILE_SIZE
                    ty = int(world_y) // TILE_SIZE
                    self.controller.add_zone(tx, ty)
                    self.state.add_zone_mode = False
                    return
                # Handle delete zone mode (click to select zone for deletion)
                if self.state.delete_zone_mode:
                    world_x = ev.pos[0] / camera.zoom + camera.offset_x
                    world_y = ev.pos[1] / camera.zoom + camera.offset_y
                    tx = int(world_x) // TILE_SIZE
                    ty = int(world_y) // TILE_SIZE
                    for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                        w, h = global_map_settings.zone_size
                        if ox <= tx < ox + w and oy <= ty < oy + h:
                            self.state.pending_delete_zone = zn
                            self.state.confirm_delete_zone = True
                            self.state.delete_zone_mode = False
                            return
                # Handle Paint Tiles Zone mode (click to select zone for painting)
                if self.state.paint_tiles_mode:
                    world_x = ev.pos[0] / camera.zoom + camera.offset_x
                    world_y = ev.pos[1] / camera.zoom + camera.offset_y
                    tx = int(world_x) // TILE_SIZE
                    ty = int(world_y) // TILE_SIZE
                    for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                        w, h = global_map_settings.zone_size
                        if ox <= tx < ox + w and oy <= ty < oy + h:
                            self.state.pending_paint_tiles_zone = zn
                            self.state.confirm_paint_tiles = True
                            self.state.paint_tiles_mode = False
                            print(f"DEBUG: paint_tiles_mode: pending zone {zn}, asking confirmation")
                            return
                # compute world tile coords
                world_x = ev.pos[0] / camera.zoom + camera.offset_x
                world_y = ev.pos[1] / camera.zoom + camera.offset_y
                tx = int(world_x) // TILE_SIZE
                ty = int(world_y) // TILE_SIZE
                #print(f"DEBUG: MouseButtonDown at screen={ev.pos}, world=({world_x:.1f},{world_y:.1f}), grid=({tx},{ty})")
                mx, my = ev.pos
                # convert to world coords
                world_x = mx / camera.zoom + camera.offset_x
                world_y = my / camera.zoom + camera.offset_y
                tx = int(world_x) // TILE_SIZE
                ty = int(world_y) // TILE_SIZE
                # Toolbar click toggle dropdown
                if self.controller.toolbar.handle_click(ev.pos):
                    return
                # Determinar zona bajo el cursor
                for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                    w, h = global_map_settings.zone_size
                    #print(f"DEBUG: checking zone {zn}: offset=({ox},{oy}), size=({w},{h}), click_grid=({tx},{ty})")
                    if ox <= tx < ox + w and oy <= ty < oy + h:
                        print(f"DEBUG: click candidate on zone {zn}")
                        # Manual double-click detection
                        now = pygame.time.get_ticks()
                        print(f"DEBUG: time since last click: {now - self.state.last_click_time}")
                        if self.state.last_click_zone == zn and now - self.state.last_click_time <= 400:
                            print(f"DEBUG: double-click detected for zone {zn}")
                            print("DEBUG: entering renaming mode")
                            self.state.renaming_zone = zn
                            self.state.rename_input = zn
                            # reset last click
                            self.state.last_click_zone = None
                            self.state.last_click_time = 0
                            return
                        # Single click: select zone and prepare for double-click
                        self.state.selected_zone = zn
                        self.state.last_click_zone = zn
                        self.state.last_click_time = now
                        return