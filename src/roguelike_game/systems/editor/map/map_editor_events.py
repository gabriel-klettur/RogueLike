import pygame
from pygame.locals import *
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

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
            print(f"DEBUG: Event {ev.type} at {getattr(ev, 'pos', None)}")
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
                if self.state.renaming_zone:
                    # Handle rename input
                    if ev.key == pygame.K_RETURN:
                        self.controller.rename_zone(self.state.renaming_zone, self.state.rename_input)
                        self.state.renaming_zone = None
                        self.state.rename_input = ""
                        return
                    elif ev.key == pygame.K_BACKSPACE:
                        self.state.rename_input = self.state.rename_input[:-1]
                        return
                    elif ev.unicode and ev.unicode.isprintable():
                        self.state.rename_input += ev.unicode
                        return
            # If renaming mode, process accept click
            if self.state.renaming_zone and ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                print(f"DEBUG: processing accept click at {ev.pos} for renaming_zone={self.state.renaming_zone}")
                # If clicked on accept button, apply rename
                if self.state.rename_accept_rect and self.state.rename_accept_rect.collidepoint(ev.pos):
                    print("DEBUG: accept_rect clicked, invoking rename_zone")
                    self.controller.rename_zone(self.state.renaming_zone, self.state.rename_input)
                # Exit renaming mode
                self.state.renaming_zone = None
                self.state.rename_input = ""
                self.state.rename_input_rect = None
                self.state.rename_accept_rect = None
                return
            # Selección y arrastre
            elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                # compute world tile coords
                world_x = ev.pos[0] / camera.zoom + camera.offset_x
                world_y = ev.pos[1] / camera.zoom + camera.offset_y
                tx = int(world_x) // TILE_SIZE
                ty = int(world_y) // TILE_SIZE
                print(f"DEBUG: MouseButtonDown at screen={ev.pos}, world=({world_x:.1f},{world_y:.1f}), grid=({tx},{ty})")
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
                    print(f"DEBUG: checking zone {zn}: offset=({ox},{oy}), size=({w},{h}), click_grid=({tx},{ty})")
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