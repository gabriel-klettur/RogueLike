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
            # Selección y arrastre
            elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                # Toolbar click toggle dropdown
                if self.controller.toolbar.handle_click(ev.pos):
                    return
                mx, my = ev.pos
                tx = mx // TILE_SIZE
                ty = my // TILE_SIZE
                # Determinar zona bajo el cursor
                for zn, (ox, oy) in global_map_settings.zone_offsets.items():
                    w, h = global_map_settings.zone_size
                    if ox <= tx < ox + w and oy <= ty < oy + h:
                        self.state.selected_zone = zn
                        # Preparar arrastre
                        self.state.dragging = zn
                        dx = mx - (ox * TILE_SIZE)
                        dy = my - (oy * TILE_SIZE)
                        self.state.drag_offset = (dx, dy)
                        return
            elif ev.type == pygame.MOUSEMOTION and self.state.dragging:
                mx, my = ev.pos
                dx, dy = self.state.drag_offset
                new_tx = (mx - dx) // TILE_SIZE
                new_ty = (my - dy) // TILE_SIZE
                # Mover zona
                self.controller.move_zone(self.state.dragging,
                                          new_tx - global_map_settings.zone_offsets[self.state.dragging][0],
                                          new_ty - global_map_settings.zone_offsets[self.state.dragging][1])
                return
            elif ev.type == pygame.MOUSEBUTTONUP and ev.button == 1:
                if self.state.dragging:
                    self.state.dragging = None
                    return