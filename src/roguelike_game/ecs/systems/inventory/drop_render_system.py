import os
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.item_models import load_items

class DropRenderSystem:
    """
    Sistema ECS que renderiza ítems dropeados en el mapa.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        items_path = os.path.join(os.getcwd(), 'data', 'items.json')
        self.items = load_items(items_path)
        self._sprites = {}  # cache de surfaces por eid

    def update(self, world, screen, camera):
        print(f"[DropRenderSystem] update called, entities_count={sum(1 for _ in world.get_entities_with('Position', 'PhysicalItemComponent'))}")
        for eid in world.get_entities_with('Position', 'PhysicalItemComponent'):
            pos = world.components['Position'][eid]
            comp = world.components['PhysicalItemComponent'][eid]
            item_id = comp.item_id
            model = self.items.get(item_id)
            if model is None:
                continue
            # seleccionar icon_small o icon
            if getattr(model, 'icon_small', None):
                path = model.icon_small
            else:
                icon = getattr(model, 'icon', None)
                if isinstance(icon, list):
                    path = icon[0]
                else:
                    path = icon
            if not path:
                continue
            if eid not in self._sprites:
                raw_surf = load_image(path)
                # Escalar sprite según propiedad scale_map
                scale = getattr(model, 'scale_map', 1.0)
                if scale != 1.0:
                    w, h = raw_surf.get_size()
                    surf = pygame.transform.smoothscale(raw_surf, (int(w * scale), int(h * scale)))
                else:
                    surf = raw_surf
                self._sprites[eid] = surf
            surf = self._sprites[eid]
            screen_pos = camera.apply((pos.x, pos.y))
            print(f"[DropRenderSystem] Rendering drop eid={eid} item_id='{item_id}' at screen_pos={screen_pos}")
            screen.blit(surf, screen_pos)
