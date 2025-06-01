import pygame
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

class MapEditorEventHandler:
    """
    Maneja eventos para el Map Editor.
    """
    def __init__(self, state, controller, map_manager):
        self.state = state
        self.controller = controller
        self.map_manager = map_manager

    def handle(self, camera, map_manager):
        for ev in pygame.event.get():
            if ev.type == pygame.QUIT:
                self.state.running = False
                return
            if ev.type == pygame.KEYDOWN:
                # Toggle Map Editor OFF via F11
                if ev.key == pygame.K_F11:
                    self.state.active = False
                    # reset substate
                    self.state.selected_zone = None
                    self.state.hidden_zones.clear()
                    self.state.dragging = None
                    print(" Map Editor OFF")
                    return
                # Exit game on Escape
                if ev.key == pygame.K_ESCAPE:
                    self.state.running = False
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