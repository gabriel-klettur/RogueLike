import os
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.managers.map.item_drop_manager import ItemDropManager


class DropDragSystem:
    """
    Sistema para arrastrar drops en el mapa con click-and-hold.
    Actualiza la posición en ECS y persiste en inventory_map.json.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        path = os.path.join(os.getcwd(), 'data', 'inventory_map.json')
        self.drop_manager = ItemDropManager(path)
        self.dragging_eid = None
        self.offset_x = 0
        self.offset_y = 0

    @benchmark(lambda self: self.perf_log, "DropDragSystem.update")
    def update(self, world, camera):
        comps = world.components
        mouse_buttons = pygame.mouse.get_pressed()
        mouse_x, mouse_y = pygame.mouse.get_pos()
        # Convertir mouse screen a world coords
        world_x = mouse_x / camera.zoom + camera.offset_x
        world_y = mouse_y / camera.zoom + camera.offset_y

        # Si no se presiona botón, finalizar drag si estaba activo
        if not mouse_buttons[0]:
            if self.dragging_eid is not None:
                # Persistir en JSON
                phys = comps['PhysicalItemComponent'][self.dragging_eid]
                # Usar posición final
                pos = comps['Position'][self.dragging_eid]
                self.drop_manager.update_drop(phys.drop_id, position=pos)
                self.dragging_eid = None
            return

        # Si botón presionado pero sin drag activo: iniciar drag si encima de drop
        if self.dragging_eid is None:
            hovered = None
            max_layer = -float('inf')
            for eid in world.get_entities_in_camera(camera, 'PhysicalItemComponent', 'Sprite', 'Position', 'ZLayer'):
                pos = comps['Position'][eid]
                sprite = comps['Sprite'][eid]
                scale_comp = comps.get('Scale', {}).get(eid)
                scale = scale_comp.scale if scale_comp else 1.0
                w, h = sprite.image.get_size()
                w = int(w * scale * camera.zoom)
                h = int(h * scale * camera.zoom)
                sx, sy = camera.apply((pos.x, pos.y))
                rect = pygame.Rect(sx, sy, w, h)
                if rect.collidepoint(mouse_x, mouse_y):
                    layer = comps['ZLayer'][eid].layer
                    if layer >= max_layer:
                        hovered = eid
                        max_layer = layer
            if hovered is not None:
                self.dragging_eid = hovered
                # Calcular offset entre pos y cursor (en world coords)
                pos = comps['Position'][hovered]
                self.offset_x = pos.x - world_x
                self.offset_y = pos.y - world_y
            return

        # Drag activo: actualizar posición componente
        pos_comp = comps['Position'][self.dragging_eid]
        pos_comp.x = world_x + self.offset_x
        pos_comp.y = world_y + self.offset_y
