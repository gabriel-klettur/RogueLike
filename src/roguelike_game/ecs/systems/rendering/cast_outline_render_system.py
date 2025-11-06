from __future__ import annotations

import time
import pygame
from roguelike_game.ecs.components.combat.cast_outline import CastOutline
from roguelike_game.ecs.utils.collider_utils import build_collider_rect, get_circle_world
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider


class CastOutlineRenderSystem:
    """Renderiza un wire de canalizado con gradiente color_from -> color_to.

    Dibuja cuando existe CastOutline para la entidad:
    - Si hay MultiCollider con MaskCollider: usa outline() si disponible; fallback AABB.
    - Si hay CircleCollider (pies): dibuja círculo.
    - Si no hay colliders: dibuja un círculo estimado basado en sprite size.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, screen: pygame.Surface, camera):
        outlines = world.components.get("CastOutline", {})
        if not outlines:
            return
        pos_map = world.components.get("Position", {})
        multi_map = world.components.get("MultiCollider", {})
        sprite_map = world.components.get("Sprite", {})
        scale_map = world.components.get("Scale", {})

        for eid, outline in list(outlines.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            st = float(getattr(outline, "start_time", time.time()))
            dur = max(0.0, float(getattr(outline, "duration", 0.0)))
            if dur <= 0.0:
                # Nada que dibujar
                continue
            now = time.time()
            t = max(0.0, min(1.0, (now - st) / dur))
            cf = getattr(outline, "color_from", (0, 128, 255))
            ct = getattr(outline, "color_to", (0, 255, 0))
            width = int(getattr(outline, "width", 3) or 3)
            # Interpolar color
            rc = int(cf[0] + (ct[0] - cf[0]) * t)
            gc = int(cf[1] + (ct[1] - cf[1]) * t)
            bc = int(cf[2] + (ct[2] - cf[2]) * t)
            draw_color = (rc, gc, bc)

            # Preferir dibujar sobre collider 'body' si existe
            try:
                mc = multi_map.get(eid)
                if mc is not None:
                    for key, col in list(getattr(mc, 'colliders', {}).items()):
                        # Mask collider: contorno detallado
                        if isinstance(col, MaskCollider) and getattr(col, 'mask', None) is not None:
                            pts = col.mask.outline()
                            if pts:
                                spoints = []
                                ox = getattr(col, 'offset_x', 0)
                                oy = getattr(col, 'offset_y', 0)
                                for (px, py) in pts:
                                    wx = pos.x + ox + px
                                    wy = pos.y + oy + py
                                    sx, sy = camera.apply((wx, wy))
                                    spoints.append((int(sx), int(sy)))
                                if len(spoints) >= 2:
                                    pygame.draw.lines(screen, draw_color, True, spoints, width)
                                break
                        # Circle collider
                        if hasattr(col, 'radius'):
                            cx, cy, r = get_circle_world(pos.x, pos.y, col)
                            sx, sy = camera.apply((cx, cy))
                            zoom = getattr(camera, "zoom", 1.0) or 1.0
                            sr = max(1, int(r * zoom))
                            pygame.draw.circle(screen, draw_color, (int(sx), int(sy)), sr, width=width)
                            break
                    else:
                        # Fallback AABB del primer collider
                        for key, col in list(getattr(mc, 'colliders', {}).items()):
                            rect = build_collider_rect(pos.x, pos.y, col)
                            tlx, tly = camera.apply((rect.x, rect.y))
                            brx, bry = camera.apply((rect.x + rect.w), (rect.y + rect.h))
                            pygame.draw.rect(
                                screen,
                                draw_color,
                                pygame.Rect(int(tlx), int(tly), max(1, int(brx - tlx)), max(1, int(bry - tly))),
                                width=width,
                            )
                            break
                    continue
            except Exception:
                pass

            # Si no hay colliders: estimar un radio del sprite
            spr = sprite_map.get(eid)
            scl = scale_map.get(eid)
            if spr is not None and hasattr(spr, 'image'):
                img = spr.image
                w, h = img.get_size()
                esc = float(getattr(scl, 'scale', 1.0)) if scl else 1.0
                r = 0.5 * max(w, h) * esc
                sx, sy = camera.apply((pos.x, pos.y))
                zoom = getattr(camera, "zoom", 1.0) or 1.0
                sr = max(1, int(r * zoom))
                pygame.draw.circle(screen, draw_color, (int(sx), int(sy)), sr, width=width)
