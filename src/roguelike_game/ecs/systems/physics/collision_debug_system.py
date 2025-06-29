"""
Module: collision_debug_system.py
Provides a debug system to visualize entity collision shapes when DEBUG mode is enabled.
"""

# Path: src/roguelike_game/ecs/systems/physics/collision_debug_system.py
import pygame
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_engine.utils.benchmark import benchmark

class CollisionDebugSystem:
    """
    Dibuja las cajas de colisión y contornos de máscara de las entidades para depuración.
    """
    def __init__(self, perf_log):
        """
        Inicializa:
          - un rectángulo reutilizable para pruebas de visibilidad (culling),
          - una lista de puntos para trazar polígonos de máscara,
          - un caché dinámico de outlines para no recalcular cada frame.
        """
        self._rect = pygame.Rect(0, 0, 0, 0)
        self._pts = []
        self._mask_outline_cache = {}
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.CollisionDebugSystem.update")
    def update(self, world, screen, camera):
        """
        Recorre todas las entidades con MultiCollider y Position. Siempre dibuja:
          1. Construye el rectángulo de colisión en coordenadas de mundo.
          2. Aplica culling para omitir shapes fuera de la pantalla.
          3. Si el collider tiene 'mask', genera (o recupera del cache) el contorno
             y lo dibuja escalado como un polígono.
          4. Si no hay máscara, dibuja el rectángulo.
        """
        # Siempre dibujar shapes de colisión en la superficie dada

        comps = world.components
        multi_map = comps.get('MultiCollider', {})
        pos_map = comps.get('Position', {})
        sprite_map = comps.get('Sprite', {})

        cam_apply = camera.apply
        screen_rect = screen.get_rect()
        draw_polygon = pygame.draw.polygon
        draw_rect = pygame.draw.rect
        mask_cache = self._mask_outline_cache

        # Iterar cada entidad que tenga múltiples colisionadores
        for eid, multi in multi_map.items():
            pos = pos_map.get(eid)
            if pos is None:
                continue  # Sin posición, no dibujar

            # Iterar cada collider dentro del MultiCollider
            for name, collider in multi.colliders.items():
                # Color morado para 'body', blanco para otros
                color = (255, 0, 255) if name == 'body' else (255, 255, 255)

                # Calcular rectángulo en mundo y pasar a coordenadas de pantalla
                rect_world = build_collider_rect(pos.x, pos.y, collider)
                screen_pos = cam_apply((rect_world.x, rect_world.y))

                # Reutilizar rect para culling
                self._rect.size = (rect_world.width, rect_world.height)
                self._rect.topleft = screen_pos
                if not screen_rect.colliderect(self._rect):
                    continue  # Fuera de pantalla

                # Si el collider tiene máscara, dibujar su contorno
                if hasattr(collider, 'mask'):
                    sprite = sprite_map.get(eid)
                    if not sprite:
                        continue  # Sin sprite, no hay máscara que extraer

                    orig_image = sprite.image
                    scale_comp = comps.get('Scale', {}).get(eid)
                    scale_val = scale_comp.scale if scale_comp else 1.0

                    # Obtener o calcular outline y cachearlo
                    key_mask = id(orig_image)
                    outline = mask_cache.get(key_mask)
                    if outline is None:
                        mask_obj = pygame.mask.from_surface(orig_image)
                        outline = mask_obj.outline()
                        mask_cache[key_mask] = outline

                    # Construir lista de puntos escalados y dibujar polígono
                    if outline:
                        self._pts.clear()
                        for ox, oy in outline:
                            dx = ox * scale_val
                            dy = oy * scale_val
                            wx = pos.x + collider.offset_x * scale_val + dx
                            wy = pos.y + collider.offset_y * scale_val + dy
                            self._pts.append(cam_apply((wx, wy)))
                        if len(self._pts) >= 2:
                            draw_polygon(screen, color, self._pts, 2)

                else:
                    # Si no hay máscara, dibujar rectángulo simple
                    draw_rect(screen, color, self._rect, 2)