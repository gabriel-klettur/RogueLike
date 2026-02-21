import pygame
import time
import random
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent


class WallRenderSystem:
    """
    Renderiza segmentos de muro como rectángulos orientados (OBB) translúcidos.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Cache para sprites escalados/rotados: (id(img), w, h, angle_0.1deg) -> Surface
        self._cache: dict[tuple[int, int, int, int], pygame.Surface] = {}

    @benchmark(lambda self: self.perf_log, 'WallRenderSystem.update')
    def update(self, world, screen: pygame.Surface, camera):
        pos_map = world.components.get('Position', {})
        walls = world.components.get('WallSegmentComponent', {})
        sprite_map = world.components.get('Sprite', {})
        scale_map = world.components.get('Scale', {})
        if not walls:
            return
        for eid, comp in list(walls.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            half_w = float(getattr(comp, 'half_w', getattr(comp, 'width', 0.0) * 0.5) or 0.0)
            half_h = float(getattr(comp, 'half_h', getattr(comp, 'height', 0.0) * 0.5) or 0.0)
            if half_w <= 0 or half_h <= 0:
                continue
            # Color azul translúcido
            color = (120, 180, 255)
            alpha = 140
            # Calcular vértices del OBB en mundo
            cos_a = float(getattr(comp, 'cos_a', 1.0))
            sin_a = float(getattr(comp, 'sin_a', 0.0))
            # Local corners
            corners = [
                (-half_w, -half_h),
                ( half_w, -half_h),
                ( half_w,  half_h),
                (-half_w,  half_h),
            ]
            world_pts = []
            for (lx, ly) in corners:
                wx = pos.x + lx * cos_a - ly * sin_a
                wy = pos.y + lx * sin_a + ly * cos_a
                sx, sy = camera.apply((wx, wy))
                world_pts.append((int(sx), int(sy)))
            # Dibujar polígono con alpha: usar surface temporal para alpha
            # Bounding rect de pantalla
            min_x = min(p[0] for p in world_pts)
            min_y = min(p[1] for p in world_pts)
            max_x = max(p[0] for p in world_pts)
            max_y = max(p[1] for p in world_pts)
            bw = max_x - min_x + 2
            bh = max_y - min_y + 2
            if bw <= 0 or bh <= 0:
                continue
            temp = pygame.Surface((bw, bh), pygame.SRCALPHA)
            shifted = [(p[0] - min_x, p[1] - min_y) for p in world_pts]
            pygame.draw.polygon(temp, (*color, alpha), shifted)
            screen.blit(temp, (min_x, min_y))
            # 2) Dibujar sprite del muro por encima del OBB si está presente
            spr = sprite_map.get(eid)
            if spr is not None:
                try:
                    sx, sy = camera.apply((pos.x, pos.y))
                    entity_scale = float(scale_map.get(eid, Scale()).scale)
                    zoom = float(getattr(camera, 'zoom', 1.0) or 1.0)
                    # Calcular ángulo de dibujado desde el eje local X del OBB
                    cos_a = float(getattr(comp, 'cos_a', 1.0))
                    sin_a = float(getattr(comp, 'sin_a', 0.0))
                    base_angle = -float(pygame.math.Vector2(cos_a, sin_a).as_polar()[1])
                    # Heurística: baseline -90° si sprite vertical
                    iw, ih = spr.image.get_size()
                    baseline_offset = -90.0 if ih > iw else 0.0
                    rot_off = float(getattr(spr, 'rotation_offset_deg', 0.0) or 0.0)
                    draw_angle = base_angle + baseline_offset + rot_off
                    # Escalado uniforme proporcional (no deformar): factor = Scale * zoom
                    scale_factor = max(0.01, entity_scale * zoom)
                    flip_y = bool(getattr(spr, 'flip_y', False))
                    # Tamaño del sprite en unidades de mundo (centered at Position)
                    spr_half_w = 0.5 * iw * entity_scale
                    spr_half_h = 0.5 * ih * entity_scale
                    # Progresión de revelado desde la base (inferior en espacio local)
                    reveal_dur = float(getattr(spr, 'reveal_duration_sec', 1.0) or 1.0)
                    if reveal_dur <= 0:
                        progress = 1.0
                    else:
                        elapsed = max(0.0, time.time() - float(getattr(comp, 'start_time', time.time())))
                        progress = max(0.0, min(1.0, elapsed / reveal_dur))
                    prog_idx = int(round(progress * 100))  # cuantizar para cache
                    cache_key = (id(spr.image), int(round(scale_factor * 1000)), int(round(draw_angle * 10)), int(flip_y), prog_idx)
                    image = self._cache.get(cache_key)
                    if image is None:
                        src0 = spr.image
                        # Flip en espacio local del sprite: primero reflejamos
                        if flip_y:
                            src0 = pygame.transform.flip(src0, False, True)
                        # Recortar en espacio local desde la base:
                        # - sin flip: base = borde inferior -> recortar desde abajo hacia arriba
                        # - con flip: base = borde superior -> recortar desde arriba hacia abajo
                        if progress < 1.0:
                            iw, ih = src0.get_size()
                            crop_h = int(round(ih * progress))
                            blank = pygame.Surface((iw, ih), pygame.SRCALPHA)
                            if crop_h > 0:
                                if not flip_y:
                                    region = pygame.Rect(0, ih - crop_h, iw, crop_h)
                                    blank.blit(src0, (0, ih - crop_h), area=region)
                                else:
                                    region = pygame.Rect(0, 0, iw, crop_h)
                                    blank.blit(src0, (0, 0), area=region)
                            src = blank
                        else:
                            src = src0
                        image = pygame.transform.rotozoom(src, draw_angle, scale_factor)
                        self._cache[cache_key] = image
                    # Emitir partículas sobre la línea de avance mientras no esté completo
                    if 0.0 < progress < 1.0:
                        fxmap = world.components.setdefault('WallRevealFX', {})
                        rec = fxmap.get(eid)
                        now = time.time()
                        if rec is None or (now - rec.get('t', 0.0)) >= 0.05:
                            fxmap[eid] = {'t': now, 'p': progress}
                            yloc = (self._frontier_y_local(spr_half_h, progress, flip_y))
                            n = max(6, int(2.0 * spr_half_w / 40.0))
                            for k in range(n):
                                if n > 1:
                                    tx = -spr_half_w + (2.0 * spr_half_w) * (k / (n - 1))
                                else:
                                    tx = 0.0
                                ty = yloc
                                wx = pos.x + tx * cos_a - ty * sin_a
                                wy = pos.y + tx * sin_a + ty * cos_a
                                if not flip_y:
                                    ndx, ndy = (sin_a, -cos_a)
                                else:
                                    ndx, ndy = (-sin_a, cos_a)
                                spd = 24.0 * random.uniform(0.7, 1.2)
                                jx = random.uniform(-4.0, 4.0)
                                jy = random.uniform(-4.0, 4.0)
                                eidp = world.create_entity()
                                world.components.setdefault('Position', {})[eidp] = Position(wx, wy)
                                world.components.setdefault('ParticleComponent', {})[eidp] = ParticleComponent(
                                    ndx * spd + jx, ndy * spd + jy,
                                    (140, 220, 255), random.randint(2, 5), 18,
                                    blend_mode='additive', drag=0.05
                                )
                    rect = image.get_rect(center=(int(sx), int(sy)))
                    screen.blit(image, rect.topleft)
                except Exception:
                    pass

    @staticmethod
    def _frontier_y_local(half_h: float, progress: float, flip_y: bool) -> float:
        if not flip_y:
            return half_h - (2.0 * half_h * progress)
        return -half_h + (2.0 * half_h * progress)
