from __future__ import annotations

import time
import pygame
from roguelike_game.ecs.utils.collider_utils import build_collider_rect, get_circle_world
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider


class WindupOutlineRenderSystem:
    """Render yellow outlines for NPC colliders while melee wind-up is active.

    Draws per-entity when a WindupOutline component exists.
    - For MaskCollider (body): polygon outline from mask.outline() if available; else AABB.
    - For CircleCollider (feet): circle outline.
    - For generic Collider: AABB outline.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, screen, camera):
        outlines = world.components.get("WindupOutline", {})
        if not outlines:
            return
        comps = world.components
        pos_map = comps.get("Position", {})
        multi_map = comps.get("MultiCollider", {})
        npc_map = comps.get("NPCState", {})

        for eid, outline in list(outlines.items()):
            pos = pos_map.get(eid)
            multi = multi_map.get(eid)
            if pos is None or multi is None:
                continue
            # Compute progress-based color (yellow->orange->red)
            try:
                fsm = npc_map.get(eid).fsm if npc_map.get(eid) is not None else None
                ctx = fsm.context if fsm is not None else {}
                start_t = float(ctx.get("attack_start", 0.0) or 0.0)
                windup_s = float(ctx.get("attack_windup_s", 1.0) or 1.0)
                now = time.time()
                prog = 0.0
                if windup_s > 1e-6 and start_t > 0.0:
                    prog = max(0.0, min(1.0, (now - start_t) / windup_s))
            except Exception:
                prog = 0.0
            # Two-stage gradient: [0..0.5] yellow->orange, [0.5..1] orange->red
            y = (255, 255, 0)
            o = (255, 165, 0)
            r = (255, 0, 0)
            if prog <= 0.5:
                t = prog / 0.5
                rc = int(y[0] + (o[0] - y[0]) * t)
                gc = int(y[1] + (o[1] - y[1]) * t)
                bc = int(y[2] + (o[2] - y[2]) * t)
            else:
                t = (prog - 0.5) / 0.5
                rc = int(o[0] + (r[0] - o[0]) * t)
                gc = int(o[1] + (r[1] - o[1]) * t)
                bc = int(o[2] + (r[2] - o[2]) * t)
            draw_color = (rc, gc, bc)
            # Width from component or default
            try:
                width = int(getattr(outline, "width", 2) or 2)
            except Exception:
                width = 2
            # Draw each sub-collider
            for key, col in list(getattr(multi, "colliders", {}).items()):
                try:
                    # Mask collider: use detailed outline when available
                    if isinstance(col, MaskCollider) and getattr(col, "mask", None) is not None:
                        pts = col.mask.outline()
                        if pts:
                            # Build poly on an overlay to support RGBA
                            try:
                                ox = getattr(col, "offset_x", 0)
                                oy = getattr(col, "offset_y", 0)
                                # Convert to world then to screen
                                spoints = []
                                for (px, py) in pts:
                                    wx = pos.x + ox + px
                                    wy = pos.y + oy + py
                                    sx, sy = camera.apply((wx, wy))
                                    spoints.append((int(sx), int(sy)))
                                # Draw with anti-aliased lines approximated by polygon edges
                                if len(spoints) >= 2:
                                    pygame.draw.lines(screen, draw_color, True, spoints, width)
                                continue
                            except Exception:
                                pass
                        # Fallback to AABB if outline failed
                        rect = build_collider_rect(pos.x, pos.y, col)
                        tlx, tly = camera.apply((rect.x, rect.y))
                        brx, bry = camera.apply((rect.x + rect.w, rect.y + rect.h))
                        pygame.draw.rect(
                            screen,
                            draw_color,
                            pygame.Rect(int(tlx), int(tly), max(1, int(brx - tlx)), max(1, int(bry - tly))),
                            width=width,
                        )
                    # Circle collider: feet
                    elif hasattr(col, "radius"):
                        cx, cy, r = get_circle_world(pos.x, pos.y, col)
                        sx, sy = camera.apply((cx, cy))
                        zoom = getattr(camera, "zoom", 1.0) or 1.0
                        sr = max(1, int(r * zoom))
                        pygame.draw.circle(screen, draw_color, (int(sx), int(sy)), sr, width=width)
                    else:
                        # Generic collider: draw AABB
                        rect = build_collider_rect(pos.x, pos.y, col)
                        tlx, tly = camera.apply((rect.x, rect.y))
                        brx, bry = camera.apply((rect.x + rect.w), (rect.y + rect.h))
                        pygame.draw.rect(
                            screen,
                            draw_color,
                            pygame.Rect(int(tlx), int(tly), max(1, int(brx - tlx)), max(1, int(bry - tly))),
                            width=width,
                        )
                except Exception:
                    # Fail per-collider without breaking the rest
                    continue
