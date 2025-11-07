import pygame
from roguelike_engine.utils.benchmark import benchmark

class MeteorFallRenderSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._meteor_color = (220, 40, 40)
        self._meteor_alpha = 220
        self._ring_color = (255, 60, 60)
        self._ring_width = 3
        self._meteor_radius_px = 8

    @benchmark(lambda self: self.perf_log, 'MeteorFallRenderSystem.update')
    def update(self, world, screen: pygame.Surface, camera):
        comps = world.components.get('MeteorFallComponent', {})
        if not comps:
            return
        pos_map = world.components.get('Position', {})
        sprite_map = world.components.get('Sprite', {})
        for eid, comp in list(comps.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            # If a Sprite is present for this meteor, rely on the generic RenderSystem and skip debug drawing
            if eid in sprite_map:
                continue
            # Meteor (filled red circle)
            mr = max(1, int(self._meteor_radius_px * camera.zoom))
            diam = mr * 2
            surf = pygame.Surface((diam, diam), pygame.SRCALPHA)
            pygame.draw.circle(surf, (*self._meteor_color, self._meteor_alpha), (mr, mr), mr)
            sx, sy = camera.apply((pos.x - mr, pos.y - mr))
            screen.blit(surf, (int(sx), int(sy)))
            # Target ring (red ring at impact position)
            ir = max(1, int(float(getattr(comp, 'impact_radius', 0.0)) * camera.zoom))
            if ir > 1:
                tx, ty = camera.apply((comp.target_x, comp.target_y))
                pygame.draw.circle(screen, self._ring_color, (int(tx), int(ty)), ir, self._ring_width)
