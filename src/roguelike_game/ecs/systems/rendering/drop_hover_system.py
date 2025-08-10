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
from roguelike_ui.ui_helpers import draw_highlight_rect, draw_tooltip
from roguelike_ui.ui_blocker import is_blocked


class DropHoverRenderSystem:
    """
    Sistema de renderizado para mostrar información y resaltar
    ítems al hacer hover con el ratón sobre drops en el mapa.
    """
    def __init__(self, perf_log=None, items_path=None):
        self.perf_log = perf_log
        if items_path is None:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items = load_items(items_path)

    @benchmark(lambda self: self.perf_log, "DropHoverRenderSystem.update")
    def update(self, world, screen, camera):
        # Bloqueo genérico para cualquier panel UI
        mouse_x, mouse_y = pygame.mouse.get_pos()
        if is_blocked(mouse_x, mouse_y):
            return
        
        # Destacar todos los drops según flag show_all_drops en InputComponent
        show_all = False
        for inp in world.components.get('InputComponent', {}).values():
            if getattr(inp, 'show_all_drops', False):
                show_all = True
                break
        if show_all:
            comps = world.components
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
                draw_highlight_rect(screen, rect)
            return
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
        draw_highlight_rect(screen, border_rect) # highlight using UI helper
        # Mostrar tooltip con nombre y descripción usando UI helper
        phys = comps['PhysicalItemComponent'][hovered]
        model = self.items.get(phys.item_id)
        name = getattr(model, 'name', phys.item_id)
        desc = getattr(model, 'description', '')
        lines = [name, desc] if desc else [name]
        draw_tooltip(screen, mouse_x, mouse_y, lines)
        return

