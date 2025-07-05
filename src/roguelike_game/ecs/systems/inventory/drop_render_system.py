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
        # Cache de imágenes originales y escaladas
        self._raw_surfaces = {}       # path -> raw Surface
        self._scaled_cache = {}       # (eid, scale_factor) -> Surface
        self._last_world_positions = {}  # eid -> última posición mundial (x, y)
        

    def update(self, world, screen, camera):
        
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
            # Preparar raw image
            raw_surf = self._raw_surfaces.get(path)
            if raw_surf is None:
                raw_surf = load_image(path)
                self._raw_surfaces[path] = raw_surf
            # Escalar según scale_map y zoom de cámara
            scale_map = getattr(model, 'scale_map', 1.0)
            zoom = getattr(camera, 'zoom', 1.0)
            scale_factor = round(scale_map * zoom, 2)
            key = (eid, scale_factor)
            if key not in self._scaled_cache:
                orig = raw_surf
                # rotozoom para calidad y rotación 0
                self._scaled_cache[key] = pygame.transform.rotozoom(orig, 0, scale_factor)
            surf = self._scaled_cache[key]
            world_pos = (pos.x, pos.y)
            screen_pos = camera.apply(world_pos)
            if eid not in self._last_world_positions or self._last_world_positions[eid] != world_pos:
                print(f"[DropRenderSystem] Rendering drop eid={eid} item_id='{item_id}' world_pos={world_pos} screen_pos={screen_pos}")
                self._last_world_positions[eid] = world_pos
            screen.blit(surf, screen_pos)
