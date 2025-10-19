from __future__ import annotations

import math
from typing import Set, Tuple

import pygame

from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from .geometry import mask_outline_world
from .draw_utils import draw_cross, draw_pink_hit


def debug_fireballs(world, screen: pygame.Surface, camera, markers) -> None:
    comps = world.components
    positions = comps.get('Position', {})
    fireballs = comps.get('FireballComponent', {})
    multi_map = comps.get('MultiCollider', {})

    for fid, fcmp in fireballs.items():
        pos = positions.get(fid)
        if not pos:
            continue
        # projectile point
        sx, sy = camera.apply((pos.x, pos.y))
        pygame.draw.circle(screen, (255, 80, 80), (int(sx), int(sy)), 3)
        draw_cross(screen, sx, sy, (255, 255, 0))

        # entity collider highlight if colliding now
        for tid in world.get_entities_with('Position', 'MultiCollider', 'Health'):
            if tid == fid or tid == getattr(fcmp, 'caster', None):
                continue
            if tid in comps.get('DeathTimer', {}):
                continue
            tpos = positions.get(tid)
            multi = multi_map.get(tid)
            if not (tpos and multi):
                continue
            hit_drawn = False
            has_mask = any(isinstance(c, MaskCollider) for c in multi.colliders.values())
            if has_mask:
                for collider in multi.colliders.values():
                    if not isinstance(collider, MaskCollider):
                        continue
                    bx = tpos.x + collider.offset_x
                    by = tpos.y + collider.offset_y
                    lx = int(pos.x - bx)
                    ly = int(pos.y - by)
                    mw, mh = collider.mask.get_size()
                    if 0 <= lx < mw and 0 <= ly < mh and collider.mask.get_at((lx, ly)):
                        outline_pts_w = mask_outline_world(collider.mask, bx, by)
                        pts_s = [camera.apply(p) for p in outline_pts_w]
                        if len(pts_s) >= 2:
                            pygame.draw.lines(screen, (255, 255, 0), True, pts_s, 2)
                        markers.add_poly(outline_pts_w, (255, 255, 0), f"FB {fid}->{tid}")
                        draw_pink_hit(screen, camera, pos.x, pos.y)
                        markers.add_circle(
                            pos.x, pos.y,
                            max(4.0, 7.0 / max(getattr(camera, 'zoom', 1.0), 0.001)),
                            (255, 105, 180), f"FB HIT {fid}->{tid}"
                        )
                        bit_wx = bx + lx + 0.5
                        bit_wy = by + ly + 0.5
                        bsx, bsy = camera.apply((bit_wx, bit_wy))
                        pygame.draw.circle(screen, (255, 0, 255), (int(bsx), int(bsy)), 2)
                        hit_drawn = True
                        break
            else:
                for collider in multi.colliders.values():
                    if isinstance(collider, MaskCollider):
                        continue
                    rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                    if rect_w.collidepoint(pos.x, pos.y):
                        rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                        rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                        pygame.draw.rect(screen, (255, 255, 0), rect_s, 2)
                        markers.add_rect(rect_w, (255, 255, 0), f"FB {fid}->{tid}")
                        draw_pink_hit(screen, camera, pos.x, pos.y)
                        markers.add_circle(
                            pos.x, pos.y,
                            max(4.0, 7.0 / max(getattr(camera, 'zoom', 1.0), 0.001)),
                            (255, 105, 180), f"FB HIT {fid}->{tid}"
                        )
                        hit_drawn = True
                        break
            if hit_drawn:
                break
        # tiles hit (1x1 rect collides any solid)
        point = pygame.Rect(pos.x, pos.y, 1, 1)
        nearby = world.get_solid_tiles_for_rect(point)
        if nearby and point.collidelist(nearby) != -1:
            psx, psy = camera.apply((pos.x, pos.y))
            pygame.draw.rect(screen, (255, 140, 0), pygame.Rect(int(psx) - 3, int(psy) - 3, 6, 6), 2)
            markers.add_rect(pygame.Rect(pos.x - 3, pos.y - 3, 6, 6), (255, 140, 0), f"FB {fid}->TILE")


def debug_hitboxes(world, screen: pygame.Surface, camera, markers, seen_pairs: Set[Tuple[int, int]]) -> Set[Tuple[int, int]]:
    comps = world.components
    positions = comps.get('Position', {})
    multi_map = comps.get('MultiCollider', {})
    hb_store = comps.get('HitboxComponent', {})

    for eid, hb in hb_store.items():
        for tid in getattr(hb, 'hit_targets', set()):
            tpos = positions.get(tid)
            multi = multi_map.get(tid)
            if not (tpos and multi):
                continue
            pair = (eid, tid)
            is_new = pair not in seen_pairs
            if is_new:
                seen_pairs.add(pair)
            for collider in multi.colliders.values():
                if isinstance(collider, MaskCollider):
                    bx = tpos.x + collider.offset_x
                    by = tpos.y + collider.offset_y
                    outline_pts_w = mask_outline_world(collider.mask, bx, by)
                    pts_s = [camera.apply(p) for p in outline_pts_w]
                    if len(pts_s) >= 2:
                        pygame.draw.lines(screen, (255, 0, 0), True, pts_s, 2)
                    if is_new:
                        markers.add_poly(outline_pts_w, (255, 0, 0), f"HB {eid}->{tid}")
                else:
                    rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                    rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                    rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                    pygame.draw.rect(screen, (255, 0, 0), rect_s, 2)
                    if is_new:
                        markers.add_rect(rect_w, (255, 0, 0), f"HB {eid}->{tid}")
    return seen_pairs


def debug_lasers(world, screen: pygame.Surface, camera, markers, laser_prev_pairs: Set[Tuple[int, int]]) -> Set[Tuple[int, int]]:
    comps = world.components
    positions = comps.get('Position', {})
    beams = comps.get('LaserBeamComponent', {})

    laser_curr_pairs: Set[Tuple[int, int]] = set()
    for caster, beam in beams.items():
        x1, y1 = beam.origin
        x2, y2 = beam.target
        dx = x2 - x1
        dy = y2 - y1
        length = math.hypot(dx, dy) or 1
        thickness_px = max(2, int(getattr(beam, 'scale', 1.0) * 20))
        color_line = (120, 200, 255, 160)
        p1 = camera.apply((x1, y1))
        p2 = camera.apply((x2, y2))
        line_surf = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        pygame.draw.line(line_surf, color_line, p1, p2, thickness_px)
        screen.blit(line_surf, (0, 0))
        half_th = thickness_px / 2

        for tid in world.get_entities_with('Position', 'Health'):
            if tid == caster:
                continue
            tpos = positions.get(tid)
            multi = comps.get('MultiCollider', {}).get(tid)
            hit_found = False
            if multi:
                steps = max(8, int(length / 16))
                nx, ny = (-dy / length, dx / length)
                for collider in multi.colliders.values():
                    if hit_found:
                        break
                    if isinstance(collider, MaskCollider):
                        bx = tpos.x + collider.offset_x
                        by = tpos.y + collider.offset_y
                        mw, mh = collider.mask.get_size()
                        for i in range(steps + 1):
                            t = i / steps
                            cx = x1 + t * dx
                            cy = y1 + t * dy
                            off = 0
                            while off <= half_th:
                                for sgn in (-1, 1):
                                    px = cx + nx * off * sgn
                                    py = cy + ny * off * sgn
                                    lx = int(px - bx)
                                    ly = int(py - by)
                                    if 0 <= lx < mw and 0 <= ly < mh and collider.mask.get_at((lx, ly)):
                                        hit_found = True
                                        outline_pts_w = mask_outline_world(collider.mask, bx, by)
                                        pts_s = [camera.apply(p) for p in outline_pts_w]
                                        if len(pts_s) >= 2:
                                            pygame.draw.lines(screen, (255, 255, 0), True, pts_s, 2)
                                        break
                                if hit_found:
                                    break
                                off += 3
                    else:
                        rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                        seg_min_x = min(x1, x2) - half_th
                        seg_max_x = max(x1, x2) + half_th
                        seg_min_y = min(y1, y2) - half_th
                        seg_max_y = max(y1, y2) + half_th
                        if not (rect_w.right < seg_min_x or rect_w.left > seg_max_x or rect_w.bottom < seg_min_y or rect_w.top > seg_max_y):
                            rx = rect_w.centerx
                            ry = rect_w.centery
                            tdx = rx - x1
                            tdy = ry - y1
                            proj = (tdx * dx + tdy * dy) / length
                            if 0 <= proj <= length:
                                pdist = abs(tdx * dy - tdy * dx) / length
                                if pdist <= half_th + max(rect_w.width, rect_w.height) * 0.5:
                                    hit_found = True
                                    rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                                    rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                                    pygame.draw.rect(screen, (255, 255, 0), rect_s, 2)
            else:
                sprite_t = comps.get('Sprite', {}).get(tid)
                if sprite_t:
                    tw, th = sprite_t.image.get_size()
                    tx = tpos.x + tw / 2
                    ty = tpos.y + th / 2
                    br = max(tw, th) / 2
                else:
                    tx = tpos.x
                    ty = tpos.y
                    br = 0
                tdx = tx - x1
                tdy = ty - y1
                proj = (tdx * dx + tdy * dy) / length
                if 0 <= proj <= length:
                    pdist = abs(tdx * dy - tdy * dx) / length
                    if pdist <= half_th + br:
                        hit_found = True

            if hit_found:
                pair = (caster, tid)
                laser_curr_pairs.add(pair)
                if pair not in laser_prev_pairs:
                    multi = comps.get('MultiCollider', {}).get(tid)
                    if multi:
                        mc = next((c for c in multi.colliders.values() if isinstance(c, MaskCollider)), None)
                        if mc is not None:
                            bx = tpos.x + mc.offset_x
                            by = tpos.y + mc.offset_y
                            outline_pts_w = mask_outline_world(mc.mask, bx, by)
                            markers.add_poly(outline_pts_w, (255, 255, 0), f"LAS {caster}->{tid}")
                        else:
                            anyc = next(iter(multi.colliders.values()))
                            rect_w = build_collider_rect(tpos.x, tpos.y, anyc)
                            markers.add_rect(rect_w, (255, 255, 0), f"LAS {caster}->{tid}")
                    else:
                        markers.add_circle(tpos.x, tpos.y, 8, (255, 255, 0), f"LAS {caster}->{tid}")
    return laser_curr_pairs


def debug_auras(world, screen: pygame.Surface, camera) -> None:
    comps = world.components
    positions = comps.get('Position', {})
    auras = comps.get('AuraComponent', {})
    for caster, aura in auras.items():
        pos = positions.get(caster)
        if not pos:
            continue
        r = getattr(aura, 'radius', 0)
        if r <= 0:
            continue
        cx, cy = camera.apply((pos.x, pos.y))
        rr = int(r * camera.zoom)
        if rr > 0:
            pygame.draw.circle(screen, (0, 200, 255), (int(cx), int(cy)), rr, 1)


def consume_debug_events(world, screen: pygame.Surface, camera, markers) -> None:
    comps = world.components
    positions = comps.get('Position', {})

    dbg = comps.get('DebugSpellHits', {})
    queue = dbg.get('_queue', []) if isinstance(dbg, dict) else []
    if not queue:
        return

    for ev in queue:
        etype = ev.get('type')
        if etype == 'FB':
            src = ev.get('src')
            tid = ev.get('target')
            ev_pos = ev.get('pos')
            tpos = positions.get(tid)
            multi = comps.get('MultiCollider', {}).get(tid)
            if not (tpos and multi):
                continue
            mc = next((c for c in multi.colliders.values() if isinstance(c, MaskCollider)), None)
            if mc is not None:
                bx = tpos.x + mc.offset_x
                by = tpos.y + mc.offset_y
                outline_pts_w = mask_outline_world(mc.mask, bx, by)
                pts_s = [camera.apply(p) for p in outline_pts_w]
                if len(pts_s) >= 2:
                    pygame.draw.lines(screen, (255, 255, 0), True, pts_s, 2)
                markers.add_poly(outline_pts_w, (255, 255, 0), f"FB {src}->{tid}")
            else:
                anyc = next(iter(multi.colliders.values()))
                rect_w = build_collider_rect(tpos.x, tpos.y, anyc)
                rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                pygame.draw.rect(screen, (255, 255, 0), rect_s, 2)
                markers.add_rect(rect_w, (255, 255, 0), f"FB {src}->{tid}")
            if isinstance(ev_pos, (list, tuple)) and len(ev_pos) == 2:
                hx, hy = float(ev_pos[0]), float(ev_pos[1])
                draw_pink_hit(screen, camera, hx, hy)
                markers.add_circle(
                    hx, hy,
                    max(4.0, 7.0 / max(getattr(camera, 'zoom', 1.0), 0.001)),
                    (255, 105, 180), f"FB HIT {src}->{tid}"
                )
                if mc is not None:
                    bx = tpos.x + mc.offset_x
                    by = tpos.y + mc.offset_y
                    lx = int(hx - bx)
                    ly = int(hy - by)
                    mw, mh = mc.mask.get_size()
                    if 0 <= lx < mw and 0 <= ly < mh and mc.mask.get_at((lx, ly)):
                        bit_wx = bx + lx + 0.5
                        bit_wy = by + ly + 0.5
                        bsx, bsy = camera.apply((bit_wx, bit_wy))
                        pygame.draw.circle(screen, (255, 0, 255), (int(bsx), int(bsy)), 2)
        elif etype == 'HB':
            hb_eid = ev.get('hb_eid')
            tid = ev.get('target')
            ev_pos = ev.get('pos')
            tpos = positions.get(tid)
            multi = comps.get('MultiCollider', {}).get(tid)
            if not (tpos and multi):
                continue
            mc = next((c for c in multi.colliders.values() if isinstance(c, MaskCollider)), None)
            if mc is not None:
                bx = tpos.x + mc.offset_x
                by = tpos.y + mc.offset_y
                outline_pts_w = mask_outline_world(mc.mask, bx, by)
                pts_s = [camera.apply(p) for p in outline_pts_w]
                if len(pts_s) >= 2:
                    pygame.draw.lines(screen, (255, 0, 0), True, pts_s, 2)
                markers.add_poly(outline_pts_w, (255, 0, 0), f"HB {hb_eid}->{tid}")
            else:
                anyc = next(iter(multi.colliders.values()))
                rect_w = build_collider_rect(tpos.x, tpos.y, anyc)
                rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                pygame.draw.rect(screen, (255, 0, 0), rect_s, 2)
                markers.add_rect(rect_w, (255, 0, 0), f"HB {hb_eid}->{tid}")
            if isinstance(ev_pos, (list, tuple)) and len(ev_pos) == 2:
                hx, hy = float(ev_pos[0]), float(ev_pos[1])
                draw_pink_hit(screen, camera, hx, hy)
                markers.add_circle(
                    hx, hy,
                    max(4.0, 7.0 / max(getattr(camera, 'zoom', 1.0), 0.001)),
                    (255, 105, 180), f"HB HIT {hb_eid}->{tid}"
                )
    queue.clear()
