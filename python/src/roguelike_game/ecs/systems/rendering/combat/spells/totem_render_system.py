import pygame


class TotemRenderSystem:
    """
    Dibuja el área de influencia de los totems como un círculo verde semitransparente
    y, si existe, su sprite centrado en la posición del totem.
    """
    def __init__(self, perf_log=None, area_color=(0, 255, 0, 70)):
        self.perf_log = perf_log
        # RGBA semi-transparente para el área
        self.area_color = area_color

    def update(self, world, screen, camera):
        comps = world.components.get('TotemComponent', {})
        if not comps:
            return
        pos_map = world.components.get('Position', {})
        sprite_map = world.components.get('Sprite', {})
        scale_map = world.components.get('Scale', {})
        for eid, comp in comps.items():
            pos = pos_map.get(eid)
            if pos is None:
                continue
            # Convertir a coords de pantalla
            sx, sy = camera.apply((pos.x, pos.y))
            # Dibujar área de influencia con zoom
            try:
                radius_world = float(getattr(comp, 'radius', 0.0) or 0.0)
            except Exception:
                radius_world = 0.0
            r = max(1, int(radius_world * float(getattr(camera, 'zoom', 1.0))))
            # Superficie temporal con alpha
            area_surf = pygame.Surface((r * 2, r * 2), pygame.SRCALPHA)
            pygame.draw.circle(area_surf, self.area_color, (r, r), r)
            area_rect = area_surf.get_rect(center=(int(sx), int(sy)))
            screen.blit(area_surf, area_rect.topleft)
            # Dibujar sprite si existe
            sprite = sprite_map.get(eid)
            if sprite is not None:
                # Escalado básico por zoom (y Scale si existe)
                entity_scale = getattr(scale_map.get(eid), 'scale', 1.0)
                scale_factor = float(entity_scale) * float(getattr(camera, 'zoom', 1.0))
                try:
                    image = sprite.image
                    if scale_factor != 1.0:
                        image = pygame.transform.rotozoom(image, 0, scale_factor)
                    rect = image.get_rect(center=(int(sx), int(sy)))
                    screen.blit(image, rect.topleft)
                except Exception:
                    pass
