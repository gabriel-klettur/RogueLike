import pygame
from roguelike_engine.utils.benchmark import benchmark

from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent
from roguelike_game.ecs.components.transform.scale import Scale


_DEFAULT_COLORS = {
    'water': (90, 180, 255),
    'poison': (40, 200, 60),
    'acid': (170, 220, 60),
    'lava': (255, 120, 60),
    'ice': (180, 230, 255),
}


class PuddleRenderSystem:
    """
    Renderiza charcos como círculos translúcidos (o decals si existiera un renderer de sprites).
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'PuddleRenderSystem.update')
    def update(self, world, screen: pygame.Surface, camera):
        pos_map = world.components.get('Position', {})
        puddles = world.components.get('PuddleComponent', {})
        sprite_map = world.components.get('Sprite', {})
        scale_map = world.components.get('Scale', {})
        if not puddles:
            return
        for eid, comp in list(puddles.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            did_draw = False
            # Si existe Sprite: dibujar sprite escalado centrado en Position
            sprite = sprite_map.get(eid)
            if sprite is not None and hasattr(sprite, 'image'):
                entity_scale = scale_map.get(eid, Scale()).scale
                scale_factor = float(entity_scale) * float(camera.zoom)
                image = pygame.transform.rotozoom(sprite.image, 0, scale_factor) if scale_factor != 1.0 else sprite.image
                cx, cy = camera.apply((pos.x, pos.y))
                rect = image.get_rect(center=(int(cx), int(cy)))
                screen.blit(image, rect.topleft)
                did_draw = True
            # Si no hay Sprite pero hay frames de secuencia: dibujar frame actual centrado
            try:
                frames = getattr(comp, 'sequence_frames', []) or []
                if frames:
                    idx = int(getattr(comp, 'sequence_idx', 0))
                    if idx < 0 or idx >= len(frames):
                        idx = 0
                    frame = frames[idx]
                    entity_scale = scale_map.get(eid, Scale()).scale
                    scale_factor = float(entity_scale) * float(camera.zoom)
                    image = pygame.transform.rotozoom(frame, 0, scale_factor) if scale_factor != 1.0 else frame
                    cx, cy = camera.apply((pos.x, pos.y))
                    rect = image.get_rect(center=(int(cx), int(cy)))
                    screen.blit(image, rect.topleft)
                    did_draw = True
            except Exception:
                pass
            # Si ya dibujamos imagen/frame, podemos superponer un anillo de depuración por radius y saltar fallback
            if did_draw:
                try:
                    radius_px = int(getattr(comp, 'radius', 0.0) * float(camera.zoom))
                    if radius_px > 0:
                        # Dibuja un anillo naranja semi-opaco centrado
                        cx, cy = camera.apply((pos.x, pos.y))
                        ring_color = (255, 120, 0)
                        pygame.draw.circle(screen, ring_color, (int(cx), int(cy)), radius_px, 2)
                except Exception:
                    pass
                continue
            # Fallback: círculo translúcido usando radius/alpha/color
            color = comp.color or _DEFAULT_COLORS.get((comp.element or '').lower(), (120, 200, 220))
            alpha = max(0, min(255, int(comp.alpha)))
            radius_px = int(comp.radius * camera.zoom)
            if radius_px <= 0:
                continue
            diam = radius_px * 2
            surf = pygame.Surface((diam, diam), pygame.SRCALPHA)
            pygame.draw.circle(surf, (*color, alpha), (radius_px, radius_px), radius_px)
            sx, sy = camera.apply((pos.x - comp.radius, pos.y - comp.radius))
            screen.blit(surf, (int(sx), int(sy)))
