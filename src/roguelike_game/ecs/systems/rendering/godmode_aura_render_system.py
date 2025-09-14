import pygame
import math
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent


class GodmodeAuraRenderSystem:
    """
    Dibuja un aura AMARILLA gruesa alrededor del jugador cuando Godmode está activo.

    Basado en ManaRegenAuraRenderSystem, pero:
    - Color amarillo (coherente con HUD en godmode)
    - Grosor bastante mayor
    - Activación: world.state.godmode True
    - Leve pulso para dar vida visual
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Estilo del aura amarillo (coincide con barras HUD godmode)
        self.base_color = (255, 230, 100)
        self.max_alpha = 200
        self.min_alpha = 120
        self.thickness = 8  # "mucho más gruesa"
        # Cache de superficies de contorno por (frame_id, scale_key)
        self._outline_cache: dict[tuple[int, int], pygame.Surface] = {}
        # Cache de superficies ya escaladas por zoom: (frame_id, scale_key, zoom_key)
        self._zoom_cache: dict[tuple[int, int, int], pygame.Surface] = {}

    def update(self, world, screen, camera):
        # Chequear flag global godmode: si no está activo, no hay coste
        godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False))
        if not godmode:
            return

        comps = world.components
        players = comps.get('PlayerTagComponent', {})
        if not players:
            return
        pos_map = comps.get('Position', {})
        spr_map = comps.get('Sprite', {})
        scale_map = comps.get('Scale', {})

        for eid in list(players.keys()):
            pos: Position = pos_map.get(eid)
            spr: Sprite = spr_map.get(eid)
            if not (pos and spr):
                continue
            try:
                # Pulsación de alpha para efecto de "poder"
                t = pygame.time.get_ticks() / 1000.0
                pulse = 0.5 + 0.5 * math.sin(t * 3.0)
                alpha = int(self.min_alpha + (self.max_alpha - self.min_alpha) * pulse)

                # Posición topleft del sprite en pantalla
                draw_x, draw_y = camera.apply((pos.x, pos.y))
                # Escala de entidad (sin zoom de cámara para ser coherente con otros auras)
                entity_scale = getattr(scale_map.get(eid), 'scale', 1.0) if isinstance(scale_map.get(eid), Scale) else 1.0

                # Cache key por frame y escala
                frame_id = id(spr.image)
                scale_key = max(1, int(entity_scale * 100))
                cache_key = (frame_id, scale_key)
                aura = self._outline_cache.get(cache_key)

                if aura is None:
                    base_img: pygame.Surface = spr.image
                    mw, mh = base_img.get_size()
                    if mw <= 0 or mh <= 0:
                        continue
                    # Generar contorno desde la máscara del frame actual
                    mask = pygame.mask.from_surface(base_img)
                    outline = mask.outline()
                    if not outline:
                        # Fallback a elipse gruesa bajo el sprite
                        aura = pygame.Surface((mw, mh), pygame.SRCALPHA)
                        # Glow múltiple en elipse
                        pygame.draw.ellipse(aura, (*self.base_color, 60), (0, mh * 0.50, mw, mh * 0.50), self.thickness + 20)
                        pygame.draw.ellipse(aura, (*self.base_color, 110), (0, mh * 0.52, mw, mh * 0.48), self.thickness + 12)
                        pygame.draw.ellipse(aura, (*self.base_color, 180), (0, mh * 0.54, mw, mh * 0.46), self.thickness)
                    else:
                        # Surface base del aura
                        base = pygame.Surface((mw, mh), pygame.SRCALPHA)
                        # Capas de glow (de afuera hacia adentro)
                        pygame.draw.polygon(base, (*self.base_color, 60), outline, self.thickness + 20)
                        pygame.draw.polygon(base, (*self.base_color, 110), outline, self.thickness + 12)
                        # Capa principal muy gruesa
                        pygame.draw.polygon(base, (*self.base_color, 255), outline, self.thickness)
                        # Suavizado interior leve
                        if self.thickness > 2:
                            pygame.draw.polygon(base, (*self.base_color, 170), outline, self.thickness - 2)
                        if self.thickness > 4:
                            pygame.draw.polygon(base, (*self.base_color, 110), outline, self.thickness - 4)
                        aura = base
                    # Adaptar a escala de entidad si aplica
                    if abs(entity_scale - 1.0) > 1e-3:
                        tw = max(1, int(mw * entity_scale))
                        th = max(1, int(mh * entity_scale))
                        aura = pygame.transform.smoothscale(aura, (tw, th))
                    self._outline_cache[cache_key] = aura

                # Escalar por zoom de cámara (cacheado)
                zoom_key = max(1, int(getattr(camera, 'zoom', 1.0) * 100))
                zkey = (frame_id, scale_key, zoom_key)
                if zoom_key != 100:
                    aura_zoom = self._zoom_cache.get(zkey)
                    if aura_zoom is None:
                        zw = max(1, int(aura.get_width() * camera.zoom))
                        zh = max(1, int(aura.get_height() * camera.zoom))
                        aura_zoom = pygame.transform.smoothscale(aura, (zw, zh))
                        self._zoom_cache[zkey] = aura_zoom
                else:
                    aura_zoom = aura

                # Aplicar alpha en pulso y dibujar
                aura_zoom.set_alpha(alpha)
                screen.blit(aura_zoom, (int(draw_x), int(draw_y)))
            except Exception:
                # Fallback simple: círculo grueso
                sx, sy = camera.apply((pos.x, pos.y))
                sw, sh = spr.image.get_size()
                radius = max(12, int(sw * 0.7))
                size = radius * 2 + self.thickness * 2
                t = pygame.time.get_ticks() / 1000.0
                pulse = 0.5 + 0.5 * math.sin(t * 3.0)
                alpha = int(self.min_alpha + (self.max_alpha - self.min_alpha) * pulse)
                surf = pygame.Surface((size, size), pygame.SRCALPHA)
                center = (size // 2, size // 2)
                pygame.draw.circle(surf, (*self.base_color, alpha), center, radius, self.thickness)
                screen.blit(surf, (int(sx + (sw - size) * 0.5), int(sy + (sh - size) * 0.5)))
