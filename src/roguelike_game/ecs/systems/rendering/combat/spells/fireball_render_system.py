import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite

class FireballRenderSystem:
    """
    Dibuja las fireballs creadas por el ECS como círculos.
    """
    def __init__(self, perf_log, radius=5, color=(255, 100, 0)):
        self.radius = radius
        self.color = color
        self.perf_log = perf_log
        # Cachés: superficies base convertidas y superficies escaladas por (img_id, escala)
        self._base_cache: dict[int, pygame.Surface] = {}
        self._scaled_cache: dict[tuple[int, float], pygame.Surface] = {}
        # Límite blando para evitar crecimiento sin control
        self._max_scaled_cache = 256
    
    def _quantize(self, s: float) -> float:
        # Reducir combinaciones de escala almacenadas en caché (tres decimales)
        try:
            return round(float(s), 3)
        except Exception:
            return 1.0

    def _get_scaled_surface(self, sprite_img: pygame.Surface, scale_factor: float) -> pygame.Surface:
        base_id = id(sprite_img)
        base = self._base_cache.get(base_id)
        if base is None:
            # Convertir a formato de pantalla para blits rápidos
            try:
                base = sprite_img.convert_alpha()
            except Exception:
                base = sprite_img
            self._base_cache[base_id] = base
        q = self._quantize(scale_factor if scale_factor is not None else 1.0)
        key = (base_id, q)
        surf = self._scaled_cache.get(key)
        if surf is not None:
            return surf
        # Evitar escalas degeneradas
        if q <= 0:
            q = 0.01
        w = max(1, int(round(base.get_width() * q)))
        h = max(1, int(round(base.get_height() * q)))
        try:
            surf = pygame.transform.smoothscale(base, (w, h)) if (w != base.get_width() or h != base.get_height()) else base
        except Exception:
            surf = base
        # Limitar tamaño del caché de escalados (política simple: borrar todos si supera el límite)
        if len(self._scaled_cache) >= self._max_scaled_cache:
            self._scaled_cache.clear()
        self._scaled_cache[key] = surf
        return surf

    def update(self, world, screen, camera):
        # Renderizar fireballs: sprite escalado cacheado o fallback círculo, con culling
        scale_map = world.components.get('Scale', {})
        sprite_map = world.components.get('Sprite', {})
        screen_rect = screen.get_rect()
        for eid, comp in world.components.get('FireballComponent', {}).items():
            pos = world.components['Position'][eid]
            x, y = camera.apply((pos.x, pos.y))
            if eid in sprite_map:
                sprite = sprite_map[eid]
                entity_scale = getattr(scale_map.get(eid, Scale()), 'scale', 1.0)
                scale_factor = float(entity_scale) * float(getattr(camera, 'zoom', 1.0))
                # Obtener superficie escalada desde caché
                img = self._get_scaled_surface(sprite.image, scale_factor)
                rect = img.get_rect(center=(int(x), int(y)))
                # Culling por pantalla
                if not rect.colliderect(screen_rect):
                    continue
                screen.blit(img, rect.topleft)
            else:
                # fallback: círculo fijo (con culling sencillo por radio)
                r = int(self.radius * float(getattr(camera, 'zoom', 1.0)))
                if r <= 0:
                    continue
                # Culling aproximado usando un rect del círculo
                if (int(x) + r) < screen_rect.left or (int(x) - r) > screen_rect.right or (int(y) + r) < screen_rect.top or (int(y) - r) > screen_rect.bottom:
                    continue
                pygame.draw.circle(screen, self.color, (int(x), int(y)), r)