import os
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.components.item_models import load_items


class DropHoverRenderSystem:
    """
    Sistema de renderizado para mostrar información y resaltar
    ítems al hacer hover con el ratón sobre drops en el mapa.
    """
    def __init__(self, perf_log=None, items_path=None):
        self.perf_log = perf_log
        if items_path is None:
            items_path = os.path.join(os.getcwd(), 'data', 'items.json')
        self.items = load_items(items_path)

    @benchmark(lambda self: self.perf_log, "DropHoverRenderSystem.update")
    def update(self, world, screen, camera):
        # Obtener posición actual del ratón
        mouse_x, mouse_y = pygame.mouse.get_pos()
        comps = world.components
        hovered = None
        max_layer = None
        # Detectar entidad drop bajo el cursor, priorizando la capa Z más alta
        for eid in world.get_entities_in_camera(camera, 'PhysicalItemComponent', 'Sprite', 'Position', 'ZLayer'):
            pos = comps['Position'][eid]
            sprite = comps['Sprite'][eid]
            scale_comp = comps.get('Scale', {}).get(eid)
            scale = scale_comp.scale if scale_comp else 1.0
            zoom = camera.zoom
            w, h = sprite.image.get_size()
            w = int(w * scale * zoom)
            h = int(h * scale * zoom)
            sx, sy = camera.apply((pos.x, pos.y))
            rect = pygame.Rect(sx, sy, w, h)
            if rect.collidepoint(mouse_x, mouse_y):
                layer = comps['ZLayer'][eid].layer
                if hovered is None or layer >= max_layer:
                    hovered = eid
                    max_layer = layer
        if hovered is None:
            return
        # Resaltar el drop
        pos = comps['Position'][hovered]
        sprite = comps['Sprite'][hovered]
        scale_comp = comps.get('Scale', {}).get(hovered)
        scale = scale_comp.scale if scale_comp else 1.0
        w, h = sprite.image.get_size()
        w = int(w * scale * camera.zoom)
        h = int(h * scale * camera.zoom)
        sx, sy = camera.apply((pos.x, pos.y))
        border_rect = pygame.Rect(sx, sy, w, h)
        pygame.draw.rect(screen, (255, 255, 0), border_rect, width=2)
        # Mostrar tooltip con nombre y descripción
        phys = comps['PhysicalItemComponent'][hovered]
        model = self.items.get(phys.item_id)
        name = getattr(model, 'name', phys.item_id)
        desc = getattr(model, 'description', '')
        font = pygame.font.SysFont(None, 20)
        lines = [name, desc] if desc else [name]
        text_surfs = [font.render(line, True, (255, 255, 255)) for line in lines]
        padd = 4
        text_width = max(s.get_width() for s in text_surfs)
        text_height = sum(s.get_height() for s in text_surfs)
        box_w = text_width + padd * 2
        box_h = text_height + padd * 2
        box_x = mouse_x + 10
        box_y = mouse_y + 10
        screen_w, screen_h = screen.get_size()
        if box_x + box_w > screen_w:
            box_x = mouse_x - box_w - 10
        if box_y + box_h > screen_h:
            box_y = mouse_y - box_h - 10
        bg_surf = pygame.Surface((box_w, box_h), flags=pygame.SRCALPHA)
        bg_surf.fill((0, 0, 0, 200))
        screen.blit(bg_surf, (box_x, box_y))
        pygame.draw.rect(screen, (255, 255, 0), (box_x, box_y, box_w, box_h), width=1)
        y_offset = box_y + padd
        for surf in text_surfs:
            screen.blit(surf, (box_x + padd, y_offset))
            y_offset += surf.get_height()
